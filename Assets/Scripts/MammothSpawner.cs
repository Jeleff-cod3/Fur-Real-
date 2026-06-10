using System.Collections;
using UnityEngine;

public class MammothSpawner : MonoBehaviour
{
    [SerializeField] private GameObject mammothPrefab;
    [SerializeField] private float spawnHeightOffset = 0.75f;
    [SerializeField] private float respawnDelaySeconds = 2f;
    [SerializeField] private float nearbyRespawnRadius = 45f;
    [SerializeField] private bool useExistingSceneMammothAsInitialSpawn = true;

    private WorldChunkRenderer worldChunkRenderer;
    private EnemyHealth currentMammoth;
    private GameObject mammothTemplate;

    private IEnumerator Start()
    {
        yield return null;

        worldChunkRenderer = FindAnyObjectByType<WorldChunkRenderer>();
        PrepareTemplateAndInitialMammoth();

        if (currentMammoth == null)
        {
            SpawnMammoth(null);
        }
    }

    private void PrepareTemplateAndInitialMammoth()
    {
        EnemyHealth sceneMammoth = FindSceneMammoth();

        if (mammothPrefab != null)
        {
            mammothTemplate = mammothPrefab;
        }
        else if (sceneMammoth != null)
        {
            mammothTemplate = Instantiate(sceneMammoth.gameObject, transform);
            mammothTemplate.name = "MammothTemplate";
            MammothSpawner nestedSpawner = mammothTemplate.GetComponent<MammothSpawner>();
            if (nestedSpawner != null)
            {
                Destroy(nestedSpawner);
            }
            mammothTemplate.SetActive(false);
        }

        if (useExistingSceneMammothAsInitialSpawn && sceneMammoth != null)
        {
            PositionMammothAtSpawn(sceneMammoth, null);
            currentMammoth = sceneMammoth;
            RegisterCurrentMammoth(currentMammoth);
        }
    }

    private EnemyHealth FindSceneMammoth()
    {
        EnemyHealth[] enemies = FindObjectsByType<EnemyHealth>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

        foreach (EnemyHealth enemy in enemies)
        {
            if (enemy != null && enemy.gameObject.name.IndexOf("Mammoth", System.StringComparison.OrdinalIgnoreCase) >= 0)
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
        Vector3 nearPosition = mammoth != null ? mammoth.transform.position : Vector3.zero;
        StartCoroutine(RespawnMammothCoroutine(nearPosition));
    }

    private IEnumerator RespawnMammothCoroutine(Vector3 nearPosition)
    {
        if (respawnDelaySeconds > 0f)
        {
            yield return new WaitForSeconds(respawnDelaySeconds);
        }

        SpawnMammoth(nearPosition);
    }

    private void SpawnMammoth(Vector3? nearPosition)
    {
        if (mammothTemplate == null)
        {
            Debug.LogWarning("MammothSpawner is missing a mammoth template or prefab.");
            return;
        }

        if (!TryResolveSpawnPosition(nearPosition, out Vector3 spawnPosition))
        {
            Debug.LogWarning("MammothSpawner could not resolve a spawn position.");
            return;
        }

        Quaternion spawnRotation = Quaternion.identity;
        GameObject mammothObject = Instantiate(mammothTemplate, spawnPosition, spawnRotation);
        mammothObject.name = "Mammoth";
        mammothObject.SetActive(true);

        EnemyHealth mammothHealth = mammothObject.GetComponent<EnemyHealth>();
        RegisterCurrentMammoth(mammothHealth);
    }

    private void PositionMammothAtSpawn(EnemyHealth mammoth, Vector3? nearPosition)
    {
        if (mammoth == null)
        {
            return;
        }

        if (TryResolveSpawnPosition(nearPosition, out Vector3 spawnPosition))
        {
            mammoth.transform.SetPositionAndRotation(spawnPosition, Quaternion.identity);
        }
    }

    private bool TryResolveSpawnPosition(Vector3? nearPosition, out Vector3 spawnPosition)
    {
        if (worldChunkRenderer != null)
        {
            bool found = nearPosition.HasValue
                ? worldChunkRenderer.TryGetNearbySpawnPosition(
                    nearPosition.Value,
                    TerrainZone.Arena,
                    nearbyRespawnRadius,
                    out spawnPosition,
                    spawnHeightOffset)
                : worldChunkRenderer.TryGetRandomSpawnPosition(
                    TerrainZone.Arena,
                    out spawnPosition,
                    spawnHeightOffset);

            if (found)
            {
                return true;
            }
        }

        spawnPosition = nearPosition ?? transform.position;
        spawnPosition.y += spawnHeightOffset;
        return true;
    }
}
