using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class MammothMovement : MonoBehaviour
{
    [Header("Movement Speeds")]
    [SerializeField] private float roamSpeed = 2f;
    [SerializeField] private float chaseSpeed = 4f;
    [SerializeField] private float runAwaySpeed = 5f;
    [SerializeField] private float chargeSpeed = 8f;

    [Header("Roaming")]
    [SerializeField] private float roamRadius = 12f;
    [SerializeField] private float roamPointSearchRadius = 8f;
    [SerializeField] [Range(0f, 1f)] private float transitionRoamChance = 0.12f;

    [Header("Run Away")]
    [SerializeField] private float runAwayDistance = 14f;

    [Header("Charge")]
    [SerializeField] private float chargeDistance = 12f;

    [Header("Search Behaviour")]
    [SerializeField] private float investigateSpeed = 3.2f;
    [SerializeField] private float lookAroundTurnSpeed = 4.5f;
    [SerializeField] private float lookAroundSweepAngle = 70f;
    [SerializeField] private float lookAroundSweepSpeed = 1.8f;

    [Header("NavMesh Recovery")]
    [SerializeField] private float navMeshRecoveryRadius = 80f;
    [SerializeField] private float destinationSampleRadius = 10f;

    private NavMeshAgent agent;
    private MammothState state;
    private WorldChunkRenderer worldChunkRenderer;
    private Vector3 spawnPosition;
    private bool lookAroundAtDestination;

    public bool HasReachedDestination =>
        agent != null &&
        agent.enabled &&
        agent.isOnNavMesh &&
        !agent.pathPending &&
        agent.remainingDistance <= agent.stoppingDistance + 0.2f;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        state = GetComponent<MammothState>();
        worldChunkRenderer = FindAnyObjectByType<WorldChunkRenderer>();
        spawnPosition = transform.position;
    }

    private void Start()
    {
        TryPlaceOnNavMesh();
    }

    private void Update()
    {
        if (!lookAroundAtDestination || state == null || state.currentAction != MammothActionType.Investigate)
        {
            return;
        }

        if (!HasReachedDestination)
        {
            return;
        }

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.ResetPath();
            agent.velocity = Vector3.zero;
        }

        Vector3 baseDirection = state.lastKnownTargetPosition - transform.position;
        baseDirection.y = 0f;

        if (baseDirection.sqrMagnitude < 0.001f)
        {
            baseDirection = transform.forward;
        }

        float sweep = Mathf.Sin(Time.time * lookAroundSweepSpeed) * lookAroundSweepAngle;
        Quaternion desiredRotation =
            Quaternion.AngleAxis(sweep, Vector3.up) *
            Quaternion.LookRotation(baseDirection.normalized, Vector3.up);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            desiredRotation,
            Time.deltaTime * lookAroundTurnSpeed
        );
    }

    public void Stop()
    {
        if (!IsAgentReady())
        {
            return;
        }

        lookAroundAtDestination = false;
        agent.ResetPath();
        agent.velocity = Vector3.zero;
    }

    public void Chase(Transform target)
    {
        if (target == null || !IsAgentReady())
        {
            return;
        }

        lookAroundAtDestination = false;

        if (!TryResolveTerritoryDestination(target.position, true, out Vector3 targetPosition))
        {
            return;
        }

        agent.speed = chaseSpeed;
        agent.isStopped = false;
        agent.SetDestination(targetPosition);
    }

    public void RunAwayFrom(Transform threat)
    {
        if (threat == null || !IsAgentReady())
        {
            return;
        }

        Vector3 awayDirection = transform.position - threat.position;
        awayDirection.y = 0f;

        if (awayDirection.sqrMagnitude < 0.001f)
        {
            awayDirection = -transform.forward;
        }

        Vector3 desiredPosition = transform.position + awayDirection.normalized * runAwayDistance;
        lookAroundAtDestination = false;

        if (TryResolveTerritoryDestination(desiredPosition, true, out Vector3 navPosition))
        {
            agent.speed = runAwaySpeed;
            agent.isStopped = false;
            agent.SetDestination(navPosition);
        }
    }

    public void Roam()
    {
        if (!IsAgentReady())
        {
            return;
        }

        lookAroundAtDestination = false;

        if (TryFindRoamDestination(out Vector3 navPosition))
        {
            agent.speed = roamSpeed;
            agent.isStopped = false;
            agent.SetDestination(navPosition);
        }
    }

    public void Investigate(Vector3 lastKnownPosition)
    {
        if (!IsAgentReady())
        {
            return;
        }

        if (!TryResolveTerritoryDestination(lastKnownPosition, true, out Vector3 navPosition))
        {
            return;
        }

        lookAroundAtDestination = true;
        agent.speed = investigateSpeed;
        agent.isStopped = false;
        agent.SetDestination(navPosition);
    }

    public void ChargeToward(Transform target)
    {
        if (target == null || !IsAgentReady())
        {
            return;
        }

        Vector3 direction = target.position - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
        {
            direction = transform.forward;
        }

        Vector3 chargeTarget = transform.position + direction.normalized * chargeDistance;
        lookAroundAtDestination = false;

        if (TryResolveTerritoryDestination(chargeTarget, true, out Vector3 navPosition))
        {
            agent.speed = chargeSpeed;
            agent.isStopped = false;
            agent.SetDestination(navPosition);
        }
    }

    public void FaceTarget(Transform target)
    {
        if (target == null)
        {
            return;
        }

        Vector3 direction = target.position - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
        {
            return;
        }

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            Quaternion.LookRotation(direction.normalized),
            Time.deltaTime * 8f
        );
    }

    private bool IsAgentReady()
    {
        if (agent == null)
        {
            return false;
        }

        if (!agent.enabled)
        {
            return false;
        }

        if (agent.isOnNavMesh)
        {
            return true;
        }

        return TryPlaceOnNavMesh();
    }

    private bool TryPlaceOnNavMesh()
    {
        if (agent == null || !agent.enabled)
        {
            return false;
        }

        if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, navMeshRecoveryRadius, NavMesh.AllAreas))
        {
            transform.position = hit.position;
            agent.Warp(hit.position);
            spawnPosition = hit.position;
            return true;
        }

        Debug.LogWarning($"{gameObject.name}: could not find NavMesh near {transform.position}.");
        return false;
    }

    private bool TryFindRoamDestination(out Vector3 navPosition)
    {
        if (worldChunkRenderer == null)
        {
            worldChunkRenderer = FindAnyObjectByType<WorldChunkRenderer>();
        }

        if (worldChunkRenderer != null)
        {
            TerrainZone zone = Random.value < transitionRoamChance ? TerrainZone.Transition : TerrainZone.Arena;
            float nearbyRadius = Mathf.Max(roamRadius * 1.6f, roamPointSearchRadius * 2f);

            if (worldChunkRenderer.TryGetNearbyNavMeshSpawnPosition(
                transform.position,
                zone,
                nearbyRadius,
                out navPosition,
                destinationSampleRadius,
                80))
            {
                return true;
            }

            if (zone != TerrainZone.Arena &&
                worldChunkRenderer.TryGetNearbyNavMeshSpawnPosition(
                    transform.position,
                    TerrainZone.Arena,
                    nearbyRadius,
                    out navPosition,
                    destinationSampleRadius,
                    80))
            {
                return true;
            }

            if (worldChunkRenderer.TryGetRandomNavMeshSpawnPosition(
                TerrainZone.Arena,
                out navPosition,
                destinationSampleRadius,
                80))
            {
                return true;
            }
        }

        Vector2 randomCircle = Random.insideUnitCircle * roamRadius;
        Vector3 randomPosition = spawnPosition + new Vector3(randomCircle.x, 0f, randomCircle.y);
        return TryFindNavMeshPosition(randomPosition, roamPointSearchRadius, out navPosition);
    }

    private bool TryResolveTerritoryDestination(Vector3 desiredPosition, bool allowTransition, out Vector3 navPosition)
    {
        if (worldChunkRenderer == null)
        {
            worldChunkRenderer = FindAnyObjectByType<WorldChunkRenderer>();
        }

        if (worldChunkRenderer == null)
        {
            return TryFindNavMeshPosition(desiredPosition, destinationSampleRadius, out navPosition);
        }

        if (worldChunkRenderer.TryGetZoneAtWorldPosition(desiredPosition, out TerrainZone desiredZone))
        {
            if (desiredZone == TerrainZone.Arena &&
                TryFindNavMeshPosition(desiredPosition, destinationSampleRadius, out navPosition))
            {
                return true;
            }

            if (allowTransition &&
                desiredZone == TerrainZone.Transition &&
                TryFindNavMeshPosition(desiredPosition, destinationSampleRadius, out navPosition))
            {
                return true;
            }
        }

        float searchRadius = Mathf.Max(roamRadius * 2f, runAwayDistance, chargeDistance, roamPointSearchRadius * 2f);

        if (allowTransition &&
            worldChunkRenderer.TryGetNearbyNavMeshSpawnPosition(
                desiredPosition,
                TerrainZone.Transition,
                searchRadius,
                out navPosition,
                destinationSampleRadius,
                80))
        {
            return true;
        }

        if (worldChunkRenderer.TryGetNearbyNavMeshSpawnPosition(
            desiredPosition,
            TerrainZone.Arena,
            searchRadius,
            out navPosition,
            destinationSampleRadius,
            80))
        {
            return true;
        }

        if (worldChunkRenderer.TryGetNearbyNavMeshSpawnPosition(
            transform.position,
            TerrainZone.Arena,
            searchRadius,
            out navPosition,
            destinationSampleRadius,
            80))
        {
            return true;
        }

        return worldChunkRenderer.TryGetRandomNavMeshSpawnPosition(
            TerrainZone.Arena,
            out navPosition,
            destinationSampleRadius,
            80);
    }

    private bool TryFindNavMeshPosition(Vector3 position, float radius, out Vector3 navPosition)
    {
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, radius, NavMesh.AllAreas))
        {
            navPosition = hit.position;
            return true;
        }

        navPosition = transform.position;
        return false;
    }
}
