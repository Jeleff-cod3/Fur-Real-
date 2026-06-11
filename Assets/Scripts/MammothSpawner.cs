using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class MammothSpawner : MonoBehaviour
{
    [Header("Prefab")]
    [SerializeField] private GameObject mammothPrefab;

    [Header("Respawn")]
    [SerializeField] private float respawnDelaySeconds = 2f;
    [SerializeField] private float nearbyRespawnRadius = 45f;

    [Header("Runtime World / NavMesh")]
    [SerializeField] private TerrainZone spawnZone = TerrainZone.Arena;
    [SerializeField] private float navMeshSampleRadius = 40f;
    [SerializeField] private float worldReadyTimeout = 12f;
    [SerializeField] private int spawnAttempts = 100;

    [Header("Placement")]
    [SerializeField] private float spawnProtectionSeconds = 3f;
    [SerializeField] private float groundProbeHeight = 40f;
    [SerializeField] private float groundProbeDistance = 120f;

    [Header("Scene Debug")]
    [SerializeField] private bool useExistingSceneMammothAsInitialSpawn = false;

    private WorldChunkRenderer worldChunkRenderer;
    private EnemyHealth currentMammoth;
    private GameObject mammothSpawnSource;
    private Coroutine respawnCoroutine;

    private IEnumerator Start()
    {
        yield return WaitForWorldAndNavMesh();

        PrepareSpawnSourceAndInitialMammoth();

        if (currentMammoth == null)
        {
            SpawnMammoth(null);
        }
    }

    private IEnumerator WaitForWorldAndNavMesh()
    {
        float deadline = Time.time + worldReadyTimeout;

        while (worldChunkRenderer == null && Time.time <= deadline)
        {
            worldChunkRenderer = FindAnyObjectByType<WorldChunkRenderer>();
            yield return null;
        }

        if (worldChunkRenderer == null)
        {
            Debug.LogWarning("MammothSpawner: WorldChunkRenderer was not found.");
            yield break;
        }

        while (Time.time <= deadline)
        {
            if (TryFindAnyValidNavMeshPoint(out _))
            {
                yield break;
            }

            yield return null;
        }

        Debug.LogWarning("MammothSpawner: runtime NavMesh was not ready before timeout.");
    }

    private void PrepareSpawnSourceAndInitialMammoth()
    {
        EnemyHealth sceneMammoth = FindSceneMammoth();

        if (mammothPrefab != null)
        {
            mammothSpawnSource = mammothPrefab;
        }
        else if (sceneMammoth != null)
        {
            mammothSpawnSource = Instantiate(sceneMammoth.gameObject, transform);
            mammothSpawnSource.name = "MammothTemplate";

            MammothSpawner nestedSpawner = mammothSpawnSource.GetComponent<MammothSpawner>();
            if (nestedSpawner != null)
            {
                Destroy(nestedSpawner);
            }

            ConfigureSpawnSource(mammothSpawnSource);
            ResetMammothHealth(mammothSpawnSource);
            mammothSpawnSource.SetActive(false);
        }

        if (useExistingSceneMammothAsInitialSpawn && sceneMammoth != null)
        {
            PositionMammothAtSpawn(sceneMammoth, null);
            ResetMammothHealth(sceneMammoth.gameObject);
            RegisterCurrentMammoth(sceneMammoth);
        }
        else if (sceneMammoth != null)
        {
            sceneMammoth.gameObject.SetActive(false);
        }
    }

    private EnemyHealth FindSceneMammoth()
    {
        EnemyHealth[] enemies = FindObjectsByType<EnemyHealth>(
            FindObjectsInactive.Exclude,
            FindObjectsSortMode.None
        );

        foreach (EnemyHealth enemy in enemies)
        {
            if (enemy == null || enemy.transform.IsChildOf(transform) || HasEnemyHealthAncestor(enemy.transform))
            {
                continue;
            }

            string enemyName = enemy.gameObject.name;
            if (enemyName.IndexOf("Mammoth", StringComparison.OrdinalIgnoreCase) >= 0 ||
                enemyName.IndexOf("Mamoth", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return enemy;
            }
        }

        return null;
    }

    private void RegisterCurrentMammoth(EnemyHealth mammoth)
    {
        if (mammoth == null)
        {
            Debug.LogWarning("MammothSpawner: spawned mammoth has no EnemyHealth.");
            return;
        }

        currentMammoth = mammoth;
        currentMammoth.Died -= HandleMammothDied;
        currentMammoth.Died += HandleMammothDied;
    }

    private void HandleMammothDied(EnemyHealth mammoth)
    {
        if (mammoth == null)
        {
            return;
        }

        if (currentMammoth != null && mammoth != currentMammoth)
        {
            return;
        }

        mammoth.Died -= HandleMammothDied;
        currentMammoth = null;

        Vector3 nearPosition = mammoth.transform.position;

        if (respawnCoroutine != null)
        {
            StopCoroutine(respawnCoroutine);
        }

        respawnCoroutine = StartCoroutine(RespawnMammothCoroutine(nearPosition));
    }

    private IEnumerator RespawnMammothCoroutine(Vector3 nearPosition)
    {
        if (respawnDelaySeconds > 0f)
        {
            yield return new WaitForSeconds(respawnDelaySeconds);
        }

        yield return WaitForWorldAndNavMesh();

        if (currentMammoth == null)
        {
            SpawnMammoth(nearPosition);
        }

        respawnCoroutine = null;
    }

    private void SpawnMammoth(Vector3? nearPosition)
    {
        if (mammothSpawnSource == null)
        {
            Debug.LogWarning("MammothSpawner: missing mammoth spawn source.");
            return;
        }

        if (!TryResolveNavMeshSpawnPosition(nearPosition, out Vector3 navMeshSpawnPosition))
        {
            Debug.LogWarning("MammothSpawner: could not find valid NavMesh spawn position.");
            return;
        }

        GameObject mammothObject = Instantiate(mammothSpawnSource, navMeshSpawnPosition, Quaternion.identity);
        mammothObject.name = "Mammoth";
        mammothObject.SetActive(true);

        ConfigureSpawnSource(mammothObject);
        PositionObjectOnGroundAndNavMesh(mammothObject, navMeshSpawnPosition);
        ResetMammothHealth(mammothObject);

        EnemyHealth mammothHealth = mammothObject.GetComponent<EnemyHealth>();
        InitializeSpawnedMammoth(mammothObject);
        RegisterCurrentMammoth(mammothHealth);
        MultiplayerPrototype.NotifyMammothRespawned(mammothHealth);
    }

    private void PositionMammothAtSpawn(EnemyHealth mammoth, Vector3? nearPosition)
    {
        if (mammoth == null)
        {
            return;
        }

        if (!TryResolveNavMeshSpawnPosition(nearPosition, out Vector3 navMeshSpawnPosition))
        {
            return;
        }

        ConfigureSpawnSource(mammoth.gameObject);
        PositionObjectOnGroundAndNavMesh(mammoth.gameObject, navMeshSpawnPosition);
        InitializeSpawnedMammoth(mammoth.gameObject);
        MultiplayerPrototype.NotifyMammothRespawned(mammoth);
    }

    private void ConfigureSpawnSource(GameObject mammothObject)
    {
        if (mammothObject == null)
        {
            return;
        }

        MammothCollisionSetup collisionSetup = mammothObject.GetComponent<MammothCollisionSetup>();
        if (collisionSetup != null)
        {
            collisionSetup.ApplySetup();
        }

        Rigidbody rb = mammothObject.GetComponent<Rigidbody>();
        if (rb != null)
        {
            bool wasKinematic = rb.isKinematic;
            if (!wasKinematic)
            {
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }

            rb.useGravity = false;
            rb.isKinematic = true;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }

        NavMeshAgent agent = mammothObject.GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            agent.baseOffset = 0f;
        }
    }

    private void PositionObjectOnGroundAndNavMesh(GameObject mammothObject, Vector3 navMeshSpawnPosition)
    {
        if (mammothObject == null)
        {
            return;
        }

        NavMeshAgent agent = mammothObject.GetComponent<NavMeshAgent>();
        if (agent != null)
        {
            agent.enabled = false;
        }

        mammothObject.transform.SetPositionAndRotation(navMeshSpawnPosition, Quaternion.identity);
        Physics.SyncTransforms();

        Collider rootCollider = mammothObject.GetComponent<Collider>();
        if (TryFindGroundBelow(navMeshSpawnPosition, mammothObject.transform, out RaycastHit groundHit))
        {
            if (rootCollider != null)
            {
                float delta = groundHit.point.y - rootCollider.bounds.min.y;
                mammothObject.transform.position += Vector3.up * delta;
            }
            else
            {
                mammothObject.transform.position = groundHit.point;
            }
        }

        Physics.SyncTransforms();

        if (agent != null)
        {
            agent.enabled = true;

            if (NavMesh.SamplePosition(mammothObject.transform.position, out NavMeshHit hit, navMeshSampleRadius, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
                mammothObject.transform.position = hit.position;
                Physics.SyncTransforms();

                if (rootCollider != null && TryFindGroundBelow(hit.position, mammothObject.transform, out RaycastHit alignedGroundHit))
                {
                    float delta = alignedGroundHit.point.y - rootCollider.bounds.min.y;
                    mammothObject.transform.position += Vector3.up * delta;
                    agent.Warp(mammothObject.transform.position);
                    Physics.SyncTransforms();
                }
            }
        }
    }

    private bool TryFindGroundBelow(Vector3 nearPosition, Transform ignoreRoot, out RaycastHit groundHit)
    {
        Vector3 origin = nearPosition + Vector3.up * groundProbeHeight;
        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            Vector3.down,
            groundProbeDistance,
            Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore
        );

        Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

        foreach (RaycastHit hit in hits)
        {
            if (ignoreRoot != null && hit.transform != null && hit.transform.IsChildOf(ignoreRoot))
            {
                continue;
            }

            groundHit = hit;
            return true;
        }

        groundHit = default;
        return false;
    }

    private void ResetMammothHealth(GameObject mammothObject)
    {
        if (mammothObject == null)
        {
            return;
        }

        EnemyHealth enemyHealth = mammothObject.GetComponent<EnemyHealth>();
        if (enemyHealth == null)
        {
            return;
        }

        enemyHealth.ResetHealthToFull(spawnProtectionSeconds);
    }

    private void InitializeSpawnedMammoth(GameObject mammothObject)
    {
        if (mammothObject == null)
        {
            return;
        }

        MammothState state = mammothObject.GetComponent<MammothState>();
        if (state != null)
        {
            state.currentTarget = null;
            state.lastKnownTargetPosition = Vector3.zero;
            state.lastTargetSeenTime = 0f;
            state.lastTargetLostTime = 0f;
        }

        MammothSenses senses = mammothObject.GetComponent<MammothSenses>();
        if (senses != null)
        {
            senses.SetTarget(null);
        }

        MammothPersonality personality = mammothObject.GetComponent<MammothPersonality>();
        if (personality != null)
        {
            personality.RandomizePersonality();
        }

        StripNestedMammothRuntimeComponents(mammothObject);
    }

    private bool TryResolveNavMeshSpawnPosition(Vector3? nearPosition, out Vector3 spawnPosition)
    {
        spawnPosition = Vector3.zero;

        if (worldChunkRenderer == null)
        {
            worldChunkRenderer = FindAnyObjectByType<WorldChunkRenderer>();
        }

        if (worldChunkRenderer == null)
        {
            return false;
        }

        for (int i = 0; i < spawnAttempts; i++)
        {
            Vector3 basePosition;

            bool gotBasePosition = nearPosition.HasValue
                ? worldChunkRenderer.TryGetNearbySpawnPosition(
                    nearPosition.Value,
                    spawnZone,
                    nearbyRespawnRadius,
                    out basePosition,
                    0f
                )
                : worldChunkRenderer.TryGetRandomSpawnPosition(
                    spawnZone,
                    out basePosition,
                    0f
                );

            if (!gotBasePosition)
            {
                continue;
            }

            if (NavMesh.SamplePosition(basePosition, out NavMeshHit hit, navMeshSampleRadius, NavMesh.AllAreas))
            {
                spawnPosition = hit.position;
                return true;
            }
        }

        return false;
    }

    private bool TryFindAnyValidNavMeshPoint(out Vector3 point)
    {
        point = Vector3.zero;

        if (worldChunkRenderer == null)
        {
            return false;
        }

        for (int i = 0; i < 20; i++)
        {
            if (!worldChunkRenderer.TryGetRandomSpawnPosition(spawnZone, out Vector3 basePosition, 0f))
            {
                continue;
            }

            if (NavMesh.SamplePosition(basePosition, out NavMeshHit hit, navMeshSampleRadius, NavMesh.AllAreas))
            {
                point = hit.position;
                return true;
            }
        }

        return false;
    }

    private static bool HasEnemyHealthAncestor(Transform transform)
    {
        if (transform == null)
        {
            return false;
        }

        Transform current = transform.parent;
        while (current != null)
        {
            if (current.GetComponent<EnemyHealth>() != null)
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }

    private static void StripNestedMammothRuntimeComponents(GameObject mammothObject)
    {
        if (mammothObject == null)
        {
            return;
        }

        List<Component> nestedComponentsToDestroy = new List<Component>();
        Component[] allComponents = mammothObject.GetComponentsInChildren<Component>(true);

        foreach (Component component in allComponents)
        {
            if (component == null || component.gameObject == mammothObject)
            {
                continue;
            }

            if (component is EnemyHealth ||
                component is MammothBrain ||
                component is MammothActionController ||
                component is MammothCombat ||
                component is MammothMovement ||
                component is MammothSenses ||
                component is MammothState ||
                component is NavMeshAgent ||
                component is Rigidbody)
            {
                nestedComponentsToDestroy.Add(component);
            }
        }

        foreach (Component component in nestedComponentsToDestroy)
        {
            UnityEngine.Object.Destroy(component);
        }
    }
}
