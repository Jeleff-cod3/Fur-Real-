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

    [Header("Rotation")]
    [SerializeField] private float movementTurnSpeed = 6.5f;
    [SerializeField] private float forcedTurnSpeed = 9f;
    [SerializeField] private float forcedFacingDuration = 0.3f;

    [Header("NavMesh Recovery")]
    [SerializeField] private float navMeshRecoveryRadius = 80f;
    [SerializeField] private float destinationSampleRadius = 10f;

    [Header("Path Safety")]
    [SerializeField] private float maxPathLengthMultiplier = 1.45f;
    [SerializeField] private int maxChargePathCorners = 3;
    [SerializeField] [Range(-1f, 1f)] private float minimumChargeForwardDot = 0.45f;

    private NavMeshAgent agent;
    private MammothState state;
    private WorldChunkRenderer worldChunkRenderer;
    private Vector3 spawnPosition;
    private bool lookAroundAtDestination;
    private Transform forcedFacingTarget;
    private float forcedFacingUntilTime;

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

        if (agent != null)
        {
            agent.updateRotation = false;
        }
    }

    private void Start()
    {
        TryPlaceOnNavMesh();
    }

    private void Update()
    {
        if (!lookAroundAtDestination || state == null || state.currentAction != MammothActionType.Investigate)
        {
            UpdateRotation();
            return;
        }

        if (!HasReachedDestination)
        {
            UpdateRotation();
            return;
        }

        if (agent != null && agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
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
        forcedFacingTarget = null;
        forcedFacingUntilTime = 0f;
        agent.isStopped = true;
        agent.ResetPath();
        agent.velocity = Vector3.zero;
    }

    public bool Chase(Transform target)
    {
        if (target == null || !IsAgentReady())
        {
            return false;
        }

        lookAroundAtDestination = false;
        forcedFacingTarget = target;

        if (!TryResolveTerritoryDestination(target.position, true, out Vector3 targetPosition))
        {
            return false;
        }

        return TrySetDestination(targetPosition, chaseSpeed, false);
    }

    public bool RunAwayFrom(Transform threat)
    {
        if (threat == null || !IsAgentReady())
        {
            return false;
        }

        Vector3 awayDirection = transform.position - threat.position;
        awayDirection.y = 0f;

        if (awayDirection.sqrMagnitude < 0.001f)
        {
            awayDirection = -transform.forward;
        }

        Vector3 desiredPosition = transform.position + awayDirection.normalized * runAwayDistance;
        lookAroundAtDestination = false;
        forcedFacingTarget = threat;

        if (TryResolveTerritoryDestination(desiredPosition, true, out Vector3 navPosition))
        {
            return TrySetDestination(navPosition, runAwaySpeed, false);
        }

        return false;
    }

    public bool Roam()
    {
        if (!IsAgentReady())
        {
            return false;
        }

        lookAroundAtDestination = false;
        forcedFacingTarget = null;

        if (TryFindRoamDestination(out Vector3 navPosition))
        {
            return TrySetDestination(navPosition, roamSpeed, false);
        }

        return false;
    }

    public bool Investigate(Vector3 lastKnownPosition)
    {
        if (!IsAgentReady())
        {
            return false;
        }

        if (!TryResolveTerritoryDestination(lastKnownPosition, true, out Vector3 navPosition))
        {
            return false;
        }

        lookAroundAtDestination = true;
        forcedFacingTarget = null;
        return TrySetDestination(navPosition, investigateSpeed, false);
    }

    public bool ChargeToward(Transform target)
    {
        if (target == null || !IsAgentReady())
        {
            return false;
        }

        Vector3 direction = target.position - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
        {
            direction = transform.forward;
        }

        Vector3 chargeTarget = transform.position + direction.normalized * chargeDistance;
        lookAroundAtDestination = false;
        forcedFacingTarget = target;

        if (TryResolveTerritoryDestination(chargeTarget, true, out Vector3 navPosition))
        {
            return TrySetDestination(navPosition, chargeSpeed, true);
        }

        return false;
    }

    public bool CanReachTarget(Transform target)
    {
        return target != null &&
            TryResolveTerritoryDestination(target.position, true, out Vector3 navPosition) &&
            IsPathSafeTo(navPosition, false);
    }

    public bool HasSafeChargePath(Transform target)
    {
        if (target == null)
        {
            return false;
        }

        Vector3 direction = target.position - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.001f)
        {
            return false;
        }

        Vector3 chargeTarget = transform.position + direction.normalized * chargeDistance;
        return TryResolveTerritoryDestination(chargeTarget, true, out Vector3 navPosition) &&
            IsPathSafeTo(navPosition, true);
    }

    public void FaceTarget(Transform target)
    {
        if (target == null)
        {
            return;
        }

        forcedFacingTarget = target;
        forcedFacingUntilTime = Time.time + forcedFacingDuration;

        Vector3 direction = target.position - transform.position;
        direction.y = 0f;

        if (direction.sqrMagnitude < 0.001f)
        {
            return;
        }

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            Quaternion.LookRotation(direction.normalized),
            Time.deltaTime * forcedTurnSpeed
        );
    }

    private void UpdateRotation()
    {
        if (lookAroundAtDestination)
        {
            return;
        }

        if (forcedFacingTarget != null &&
            forcedFacingTarget.gameObject.activeInHierarchy &&
            Time.time <= forcedFacingUntilTime)
        {
            RotateTowards(forcedFacingTarget.position - transform.position, forcedTurnSpeed);
            return;
        }

        if (agent == null || !agent.enabled || !agent.isOnNavMesh || agent.isStopped)
        {
            return;
        }

        Vector3 desiredDirection = agent.desiredVelocity;
        desiredDirection.y = 0f;

        if (desiredDirection.sqrMagnitude <= 0.01f)
        {
            desiredDirection = agent.velocity;
            desiredDirection.y = 0f;
        }

        if (desiredDirection.sqrMagnitude <= 0.01f)
        {
            return;
        }

        RotateTowards(desiredDirection, movementTurnSpeed);
    }

    private void RotateTowards(Vector3 direction, float speed)
    {
        direction.y = 0f;

        if (direction.sqrMagnitude <= 0.001f)
        {
            return;
        }

        Quaternion desiredRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            desiredRotation,
            Time.deltaTime * speed
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

    private bool TrySetDestination(Vector3 destination, float speed, bool requireChargePath)
    {
        if (!TryBuildSafePath(destination, requireChargePath, out NavMeshPath safePath))
        {
            return false;
        }

        agent.speed = speed;
        agent.isStopped = false;
        return agent.SetPath(safePath);
    }

    private bool IsPathSafeTo(Vector3 destination, bool requireChargePath)
    {
        return TryBuildSafePath(destination, requireChargePath, out _);
    }

    private bool TryBuildSafePath(Vector3 destination, bool requireChargePath, out NavMeshPath path)
    {
        path = new NavMeshPath();

        if (!IsAgentReady())
        {
            return false;
        }

        if (!agent.CalculatePath(destination, path))
        {
            return false;
        }

        if (path.status != NavMeshPathStatus.PathComplete || path.corners == null || path.corners.Length == 0)
        {
            return false;
        }

        float directDistance = Vector3.Distance(transform.position, destination);
        float pathLength = GetPathLength(path);
        float minimumReasonableDistance = Mathf.Max(1f, directDistance);

        if (pathLength > minimumReasonableDistance * maxPathLengthMultiplier)
        {
            return false;
        }

        if (!requireChargePath)
        {
            return true;
        }

        if (path.corners.Length > maxChargePathCorners)
        {
            return false;
        }

        Vector3 overallDirection = destination - transform.position;
        overallDirection.y = 0f;

        if (overallDirection.sqrMagnitude <= 0.001f)
        {
            return false;
        }

        Vector3 firstSegment = path.corners.Length > 1
            ? path.corners[1] - path.corners[0]
            : overallDirection;
        firstSegment.y = 0f;

        if (firstSegment.sqrMagnitude <= 0.001f)
        {
            return false;
        }

        return Vector3.Dot(firstSegment.normalized, overallDirection.normalized) >= minimumChargeForwardDot;
    }

    private static float GetPathLength(NavMeshPath path)
    {
        if (path == null || path.corners == null || path.corners.Length < 2)
        {
            return 0f;
        }

        float total = 0f;

        for (int i = 1; i < path.corners.Length; i++)
        {
            total += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        }

        return total;
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
