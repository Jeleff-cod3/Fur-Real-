using System.Collections;
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

    [Header("Scene Debug")]
    [SerializeField] private bool useExistingSceneMammothAsInitialSpawn = false;

    private WorldChunkRenderer worldChunkRenderer;
    private EnemyHealth currentMammoth;
    private GameObject mammothTemplate;

    private IEnumerator Start()
    {
        yield return WaitForWorldAndNavMesh();

        PrepareTemplateAndInitialMammoth();

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

    private void PrepareTemplateAndInitialMammoth()
    {
        EnemyHealth sceneMammoth = FindSceneMammoth();
        GameObject templateSource = mammothPrefab != null
            ? mammothPrefab
            : sceneMammoth != null
                ? sceneMammoth.gameObject
                : null;

        if (templateSource != null)
        {
            mammothTemplate = Instantiate(templateSource, transform);
            mammothTemplate.name = "MammothTemplate";

            MammothSpawner nestedSpawner = mammothTemplate.GetComponent<MammothSpawner>();

            if (nestedSpawner != null)
            {
                Destroy(nestedSpawner);
            }

            ResetMammothHealth(mammothTemplate, 3f);
            mammothTemplate.SetActive(false);
        }

        if (useExistingSceneMammothAsInitialSpawn && sceneMammoth != null)
        {
            PositionMammothAtSpawn(sceneMammoth, null);
            ResetMammothHealth(sceneMammoth.gameObject, 3f);
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
            if (enemy != null &&
                enemy.gameObject.name.IndexOf("Mammoth", System.StringComparison.OrdinalIgnoreCase) >= 0)
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
        if (mammoth != null)
        {
            mammoth.Died -= HandleMammothDied;
        }

        currentMammoth = null;

        Vector3 nearPosition = mammoth != null ? mammoth.transform.position : transform.position;
        StartCoroutine(RespawnMammothCoroutine(nearPosition));
    }

    private IEnumerator RespawnMammothCoroutine(Vector3 nearPosition)
    {
        if (respawnDelaySeconds > 0f)
        {
            yield return new WaitForSeconds(respawnDelaySeconds);
        }

        yield return WaitForWorldAndNavMesh();
        SpawnMammoth(nearPosition);
    }

    private void SpawnMammoth(Vector3? nearPosition)
    {
        if (mammothTemplate == null)
        {
            Debug.LogWarning("MammothSpawner: missing mammoth prefab/template.");
            return;
        }

        if (!TryResolveNavMeshSpawnPosition(nearPosition, out Vector3 spawnPosition))
        {
            Debug.LogWarning("MammothSpawner: could not find valid NavMesh spawn position.");
            return;
        }

        ResetMammothHealth(mammothTemplate, 3f);

        GameObject mammothObject = Instantiate(mammothTemplate, spawnPosition, Quaternion.identity);
        mammothObject.name = "Mammoth";
        mammothObject.SetActive(true);

        ResetMammothHealth(mammothObject, 3f);
        ConfigureSpawnedMammoth(mammothObject, spawnPosition);

        EnemyHealth mammothHealth = mammothObject.GetComponent<EnemyHealth>();
        RegisterCurrentMammoth(mammothHealth);

        if (mammothHealth != null)
        {
            Debug.Log($"MammothSpawner: spawned mammoth alive with HP {mammothHealth.CurrentHealth}/{mammothHealth.MaxHealth}");
        }
    }

    private void PositionMammothAtSpawn(EnemyHealth mammoth, Vector3? nearPosition)
    {
        if (mammoth == null)
        {
            return;
        }

        if (!TryResolveNavMeshSpawnPosition(nearPosition, out Vector3 spawnPosition))
        {
            return;
        }

        mammoth.transform.SetPositionAndRotation(spawnPosition, Quaternion.identity);
        ResetMammothHealth(mammoth.gameObject, 3f);
        ConfigureSpawnedMammoth(mammoth.gameObject, spawnPosition);
    }

    private void ConfigureSpawnedMammoth(GameObject mammothObject, Vector3 spawnPosition)
    {
        Rigidbody rb = mammothObject.GetComponent<Rigidbody>();

        if (rb != null)
        {
            rb.useGravity = false;
            rb.isKinematic = true;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        }

        NavMeshAgent agent = mammothObject.GetComponent<NavMeshAgent>();

        if (agent == null)
        {
            Debug.LogWarning("MammothSpawner: spawned mammoth has no NavMeshAgent.");
            return;
        }

        agent.enabled = true;
        agent.baseOffset = 0f;

        if (NavMesh.SamplePosition(spawnPosition, out NavMeshHit hit, navMeshSampleRadius, NavMesh.AllAreas))
        {
            mammothObject.transform.position = hit.position;
            agent.Warp(hit.position);
            Debug.Log($"MammothSpawner: mammoth spawned on NavMesh at {hit.position}");
        }
        else
        {
            Debug.LogWarning($"MammothSpawner: failed to warp mammoth to NavMesh near {spawnPosition}");
        }
    }

    private void ResetMammothHealth(GameObject mammothObject, float protectionSeconds)
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

        enemyHealth.ResetHealthToFull(protectionSeconds);
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
}