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

    [Header("Run Away")]
    [SerializeField] private float runAwayDistance = 14f;

    [Header("Charge")]
    [SerializeField] private float chargeDistance = 12f;

    [Header("NavMesh Recovery")]
    [SerializeField] private float navMeshRecoveryRadius = 80f;
    [SerializeField] private float destinationSampleRadius = 10f;

    private NavMeshAgent agent;
    private Vector3 spawnPosition;

    public bool HasReachedDestination =>
        agent != null &&
        agent.enabled &&
        agent.isOnNavMesh &&
        !agent.pathPending &&
        agent.remainingDistance <= agent.stoppingDistance + 0.2f;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        spawnPosition = transform.position;
    }

    private void Start()
    {
        TryPlaceOnNavMesh();
    }

    public void Stop()
    {
        if (!IsAgentReady())
        {
            return;
        }

        agent.ResetPath();
        agent.velocity = Vector3.zero;
    }

    public void Chase(Transform target)
    {
        if (target == null || !IsAgentReady())
        {
            return;
        }

        if (!TryFindNavMeshPosition(target.position, destinationSampleRadius, out Vector3 targetPosition))
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

        if (TryFindNavMeshPosition(desiredPosition, roamPointSearchRadius, out Vector3 navPosition))
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

        Vector2 randomCircle = Random.insideUnitCircle * roamRadius;
        Vector3 randomPosition = spawnPosition + new Vector3(randomCircle.x, 0f, randomCircle.y);

        if (TryFindNavMeshPosition(randomPosition, roamPointSearchRadius, out Vector3 navPosition))
        {
            agent.speed = roamSpeed;
            agent.isStopped = false;
            agent.SetDestination(navPosition);
        }
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

        if (TryFindNavMeshPosition(chargeTarget, roamPointSearchRadius, out Vector3 navPosition))
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