using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
public class TreeSpiderMovement : MonoBehaviour
{
    [Header("Speeds")]
    [SerializeField] private float chaseSpeed = 5.8f;
    [SerializeField] private float wanderSpeed = 3.6f;
    [SerializeField] private float returnSpeed = 4.5f;

    [Header("Wander")]
    [SerializeField] private float wanderPointRadius = 12f;
    [SerializeField] private float navMeshSampleRadius = 10f;
    [SerializeField] private float navMeshRecoveryRadius = 70f;

    private NavMeshAgent agent;
    private WorldChunkRenderer worldChunkRenderer;

    public bool HasReachedDestination =>
        agent != null &&
        agent.enabled &&
        agent.isOnNavMesh &&
        !agent.pathPending &&
        agent.remainingDistance <= agent.stoppingDistance + 0.25f;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        worldChunkRenderer = FindAnyObjectByType<WorldChunkRenderer>();
    }

    public void Stop()
    {
        if (!IsAgentReady())
        {
            return;
        }

        agent.ResetPath();
        agent.velocity = Vector3.zero;
        agent.isStopped = true;
    }

    public void SetAgentEnabled(bool isEnabled)
    {
        if (agent == null)
        {
            return;
        }

        if (agent.enabled == isEnabled)
        {
            return;
        }

        agent.enabled = isEnabled;

        if (isEnabled)
        {
            TryPlaceOnNavMesh();
        }
    }

    public void Chase(Transform target)
    {
        if (target == null || !IsAgentReady())
        {
            return;
        }

        if (!TryFindResourceNavMeshPointNear(target.position, 4.5f, out Vector3 destination))
        {
            destination = target.position;
        }

        agent.speed = chaseSpeed;
        agent.isStopped = false;
        agent.SetDestination(destination);
    }

    public void WanderAround(Vector3 center)
    {
        if (!IsAgentReady())
        {
            return;
        }

        if (!TryFindResourceNavMeshPointNear(center, wanderPointRadius, out Vector3 destination))
        {
            return;
        }

        agent.speed = wanderSpeed;
        agent.isStopped = false;
        agent.SetDestination(destination);
    }

    public void ReturnToTree(Vector3 treeBasePosition)
    {
        if (!IsAgentReady())
        {
            return;
        }

        if (!TryFindResourceNavMeshPointNear(treeBasePosition, 3f, out Vector3 destination))
        {
            destination = treeBasePosition;
        }

        agent.speed = returnSpeed;
        agent.isStopped = false;
        agent.SetDestination(destination);
    }

    public void WarpTo(Vector3 worldPosition)
    {
        if (!IsAgentReady())
        {
            transform.position = worldPosition;
            return;
        }

        if (NavMesh.SamplePosition(worldPosition, out NavMeshHit hit, navMeshRecoveryRadius, NavMesh.AllAreas))
        {
            agent.Warp(hit.position);
            transform.position = hit.position;
        }
        else
        {
            transform.position = worldPosition;
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

        if (direction.sqrMagnitude <= 0.001f)
        {
            return;
        }

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            Quaternion.LookRotation(direction.normalized, Vector3.up),
            Time.deltaTime * 10f
        );
    }

    public bool TryGetGroundedPositionNear(Vector3 worldPosition, float sampleRadius, out Vector3 groundedPosition)
    {
        if (NavMesh.SamplePosition(worldPosition, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
        {
            groundedPosition = hit.position;
            return true;
        }

        groundedPosition = worldPosition;
        return false;
    }

    private bool TryFindResourceNavMeshPointNear(Vector3 worldPosition, float radius, out Vector3 navPosition)
    {
        if (worldChunkRenderer == null)
        {
            worldChunkRenderer = FindAnyObjectByType<WorldChunkRenderer>();
        }

        if (worldChunkRenderer != null &&
            worldChunkRenderer.TryGetNearbyNavMeshSpawnPosition(
                worldPosition,
                TerrainZone.Resource,
                Mathf.Max(radius, 1f),
                out navPosition,
                navMeshSampleRadius,
                80))
        {
            return true;
        }

        if (NavMesh.SamplePosition(worldPosition, out NavMeshHit hit, navMeshSampleRadius, NavMesh.AllAreas))
        {
            navPosition = hit.position;
            return true;
        }

        navPosition = transform.position;
        return false;
    }

    private bool IsAgentReady()
    {
        if (agent == null || !agent.enabled)
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

        if (!NavMesh.SamplePosition(transform.position, out NavMeshHit hit, navMeshRecoveryRadius, NavMesh.AllAreas))
        {
            return false;
        }

        transform.position = hit.position;
        agent.Warp(hit.position);
        return true;
    }
}
