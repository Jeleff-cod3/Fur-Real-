using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class SpearTestSpawner : MonoBehaviour
{
    private enum SpawnZone
    {
        Arena,
        Resource
    }

    [Header("Prefab")]
    [SerializeField] private PickupableWeapon spearPrefab;

    [Header("Spawn Amount")]
    [SerializeField] private int spearCount = 5;

    [Header("Spawn Position")]
    [SerializeField] private SpawnZone initialSpawnZone = SpawnZone.Arena;
    [SerializeField] private SpawnZone replacementSpawnZone = SpawnZone.Arena;
    [SerializeField] private Vector3 startOffset = new Vector3(-3f, 0f, 2f);
    [SerializeField] private float spacing = 1.5f;
    [SerializeField] private float spawnRadius = 10f;

    [Header("Ground Detection")]
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float raycastHeight = 300f;
    [SerializeField] private float spearHeightAboveGround = 0.9f;
    [SerializeField] private float navMeshSampleRadius = 12f;

    [Header("Waiting")]
    [SerializeField] private int spawnDelayFrames = 5;
    [SerializeField] private int retryDelayFrames = 5;
    [SerializeField] private float maxGroundWaitSeconds = 8f;
    [SerializeField] private float playerSearchTimeout = 8f;
    [SerializeField] private float playerSearchInterval = 0.2f;

    [Header("Respawn")]
    [SerializeField] private bool keepSpearSupplyFull = true;
    [SerializeField] private float respawnDelaySeconds = 1f;

    private readonly HashSet<PickupableWeapon> trackedSpears = new HashSet<PickupableWeapon>();

    private WorldChunkRenderer worldChunkRenderer;
    private Transform spawnAnchor;
    private bool isSpawningReplacement;

    private IEnumerator Start()
    {
        for (int i = 0; i < spawnDelayFrames; i++)
        {
            yield return null;
        }

        if (spearPrefab == null)
        {
            Debug.LogWarning("SpearTestSpawner: spearPrefab is missing.");
            yield break;
        }

        yield return FindWorldAndPlayerRoutine();

        for (int i = 0; i < spearCount; i++)
        {
            yield return SpawnSingleSpearCoroutine(i, initialSpawnZone);
        }
    }

    private IEnumerator FindWorldAndPlayerRoutine()
    {
        float deadline = Time.realtimeSinceStartup + playerSearchTimeout;

        while (Time.realtimeSinceStartup < deadline)
        {
            if (worldChunkRenderer == null)
            {
                worldChunkRenderer = FindAnyObjectByType<WorldChunkRenderer>();
            }

            if (spawnAnchor == null)
            {
                spawnAnchor = FindBestSpawnAnchor();
            }

            if (worldChunkRenderer != null && spawnAnchor != null)
            {
                yield break;
            }

            yield return new WaitForSeconds(playerSearchInterval);
        }

        if (worldChunkRenderer == null)
        {
            worldChunkRenderer = FindAnyObjectByType<WorldChunkRenderer>();
        }

        if (spawnAnchor == null)
        {
            spawnAnchor = transform;
            Debug.LogWarning("SpearTestSpawner: no player found. Using spawner transform as fallback anchor.");
        }
    }

    private IEnumerator SpawnSingleSpearCoroutine(int spearIndex, SpawnZone zone)
    {
        float deadline = Time.time + Mathf.Max(0.1f, maxGroundWaitSeconds);

        while (Time.time <= deadline)
        {
            if (TryResolveSpawnPosition(spearIndex, zone, out Vector3 spawnPosition))
            {
                SpawnSpearAt(spawnPosition);
                yield break;
            }

            for (int frame = 0; frame < Mathf.Max(1, retryDelayFrames); frame++)
            {
                yield return null;
            }
        }

        Debug.LogWarning($"SpearTestSpawner: failed to find real ground for spear {spearIndex + 1}. No spear spawned.");
    }

    private bool TryResolveSpawnPosition(int spearIndex, SpawnZone zone, out Vector3 spawnPosition)
    {
        if (TryGetWorldDataSpawnPosition(spearIndex, zone, out spawnPosition))
        {
            return true;
        }

        Vector3 basePosition = GetBaseSpawnPosition(spearIndex);

        if (TryGetRaycastGroundPosition(basePosition, out spawnPosition))
        {
            return true;
        }

        if (TryGetNavMeshGroundedPosition(basePosition, out spawnPosition))
        {
            return true;
        }

        spawnPosition = Vector3.zero;
        return false;
    }

    private bool TryGetWorldDataSpawnPosition(int spearIndex, SpawnZone zone, out Vector3 spawnPosition)
    {
        spawnPosition = Vector3.zero;

        if (worldChunkRenderer == null)
        {
            return false;
        }

        TerrainZone targetZone = zone == SpawnZone.Arena
            ? TerrainZone.Arena
            : TerrainZone.Resource;

        Vector3 nearPosition = spawnAnchor != null
            ? spawnAnchor.position + new Vector3(startOffset.x, 0f, startOffset.z) + ComputePlanarOffset(spearIndex)
            : transform.position + ComputePlanarOffset(spearIndex);

        Vector3 worldDataPosition;

        if (worldChunkRenderer.TryGetNearbySpawnPosition(
                nearPosition,
                targetZone,
                spawnRadius,
                out worldDataPosition,
                0f,
                80))
        {
            spawnPosition = worldDataPosition + Vector3.up * spearHeightAboveGround;
            return true;
        }

        if (worldChunkRenderer.TryGetRandomSpawnPosition(targetZone, out worldDataPosition, 0f, 80))
        {
            spawnPosition = worldDataPosition + Vector3.up * spearHeightAboveGround;
            return true;
        }

        return false;
    }

    private Vector3 GetBaseSpawnPosition(int spearIndex)
    {
        Transform anchor = spawnAnchor != null ? spawnAnchor : transform;
        return anchor.position + new Vector3(startOffset.x, 0f, startOffset.z) + ComputePlanarOffset(spearIndex);
    }

    private Vector3 ComputePlanarOffset(int spearIndex)
    {
        float angleDegrees = (spearIndex * 137.5f) % 360f;
        float radius = Mathf.Max(0.8f, spacing) * (1f + (spearIndex / Mathf.Max(1, spearCount)) * 0.35f);
        float angleRadians = angleDegrees * Mathf.Deg2Rad;

        return new Vector3(
            Mathf.Cos(angleRadians) * radius,
            0f,
            Mathf.Sin(angleRadians) * radius
        );
    }

    private bool TryGetRaycastGroundPosition(Vector3 basePosition, out Vector3 spawnPosition)
    {
        Vector3 rayStart = new Vector3(basePosition.x, basePosition.y + raycastHeight, basePosition.z);

        int raycastMask = ResolveGroundMask();

        RaycastHit[] hits = Physics.RaycastAll(
            rayStart,
            Vector3.down,
            raycastHeight * 2f,
            raycastMask,
            QueryTriggerInteraction.Ignore
        );

        if (hits == null || hits.Length == 0)
        {
            spawnPosition = Vector3.zero;
            return false;
        }

        System.Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

        foreach (RaycastHit hit in hits)
        {
            if (!IsGroundCandidate(hit.collider))
            {
                continue;
            }

            spawnPosition = hit.point + Vector3.up * spearHeightAboveGround;
            return true;
        }

        spawnPosition = Vector3.zero;
        return false;
    }

    private bool TryGetNavMeshGroundedPosition(Vector3 basePosition, out Vector3 spawnPosition)
    {
        if (!NavMesh.SamplePosition(basePosition, out NavMeshHit navHit, navMeshSampleRadius, NavMesh.AllAreas))
        {
            spawnPosition = Vector3.zero;
            return false;
        }

        // NavMesh gives us walkable surface, but we still validate with real collision ground.
        if (TryGetRaycastGroundPosition(navHit.position, out spawnPosition))
        {
            return true;
        }

        spawnPosition = navHit.position + Vector3.up * spearHeightAboveGround;
        return true;
    }

    private int ResolveGroundMask()
    {
        if (groundLayer.value != 0)
        {
            return groundLayer.value;
        }

        int mask = 0;

        int groundLayerIndex = LayerMask.NameToLayer("Ground");
        if (groundLayerIndex >= 0)
        {
            mask |= 1 << groundLayerIndex;
        }

        int defaultLayerIndex = LayerMask.NameToLayer("Default");
        if (defaultLayerIndex >= 0)
        {
            mask |= 1 << defaultLayerIndex;
        }

        if (mask != 0)
        {
            return mask;
        }

        return Physics.DefaultRaycastLayers;
    }

    private static bool IsGroundCandidate(Collider candidate)
    {
        if (candidate == null || candidate.isTrigger)
        {
            return false;
        }

        if (candidate.GetComponentInParent<PickupableWeapon>() != null)
        {
            return false;
        }

        if (candidate.GetComponentInParent<PlayerHealth>() != null)
        {
            return false;
        }

        if (candidate.GetComponentInParent<EnemyHealth>() != null)
        {
            return false;
        }

        if (candidate.GetComponent<TerrainCollider>() != null)
        {
            return true;
        }

        if (candidate.GetComponent<MeshCollider>() != null)
        {
            return true;
        }

        string objectName = candidate.gameObject.name;

        if (objectName.StartsWith("Chunk") || objectName.Contains("Terrain") || objectName.Contains("Ground"))
        {
            return true;
        }

        return candidate.attachedRigidbody == null;
    }

    private Transform FindBestSpawnAnchor()
    {
        GameObject taggedPlayer = GameObject.FindGameObjectWithTag("Player");

        if (taggedPlayer != null)
        {
            return taggedPlayer.transform;
        }

        PlayerHealth playerHealth = FindAnyObjectByType<PlayerHealth>();

        if (playerHealth != null)
        {
            return playerHealth.transform;
        }

        PlayerWeaponPickup weaponPickup = FindAnyObjectByType<PlayerWeaponPickup>();

        if (weaponPickup != null)
        {
            return weaponPickup.transform;
        }

        PlayerCombat playerCombat = FindAnyObjectByType<PlayerCombat>();

        if (playerCombat != null)
        {
            return playerCombat.transform;
        }

        return null;
    }

    private void SpawnSpearAt(Vector3 spawnPosition)
    {
        PickupableWeapon spear = Instantiate(
            spearPrefab,
            spawnPosition,
            Quaternion.Euler(0f, 90f, 0f)
        );

        spear.name = $"Testing Spear {trackedSpears.Count + 1}";

        spear.RemovedFromWorldSupply -= HandleSpearRemovedFromWorldSupply;
        spear.RemovedFromWorldSupply += HandleSpearRemovedFromWorldSupply;

        trackedSpears.Add(spear);

        Debug.Log($"SpearTestSpawner: spawned spear at {spawnPosition}");
    }

    private void HandleSpearRemovedFromWorldSupply(PickupableWeapon spear)
    {
        if (spear != null)
        {
            spear.RemovedFromWorldSupply -= HandleSpearRemovedFromWorldSupply;
            trackedSpears.Remove(spear);
        }

        if (keepSpearSupplyFull && !isSpawningReplacement)
        {
            StartCoroutine(RespawnReplacementSpear());
        }
    }

    private IEnumerator RespawnReplacementSpear()
    {
        isSpawningReplacement = true;

        if (respawnDelaySeconds > 0f)
        {
            yield return new WaitForSeconds(respawnDelaySeconds);
        }

        yield return SpawnSingleSpearCoroutine(trackedSpears.Count, replacementSpawnZone);

        isSpawningReplacement = false;
    }
}
