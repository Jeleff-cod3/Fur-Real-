using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpearTestSpawner : MonoBehaviour
{
    private enum SpawnZone
    {
        Arena,
        Resource
    }

    [SerializeField] private PickupableWeapon spearPrefab;
    [SerializeField] private int spearCount = 5;
    [SerializeField] private LayerMask groundLayer;
    [SerializeField] private float raycastHeight = 300f;
    [SerializeField] private float spearHeightAboveGround = 0.75f;
    [SerializeField] private int spawnDelayFrames = 3;
    [SerializeField] private int retryDelayFrames = 5;
    [SerializeField] private float maxGroundWaitSeconds = 5f;
    [SerializeField] private float respawnDelaySeconds = 1f;
    [SerializeField] private SpawnZone initialSpawnZone = SpawnZone.Arena;
    [SerializeField] private SpawnZone replacementSpawnZone = SpawnZone.Resource;

    private readonly HashSet<PickupableWeapon> trackedSpears = new HashSet<PickupableWeapon>();
    private WorldChunkRenderer worldChunkRenderer;

    private IEnumerator Start()
    {
        for (int i = 0; i < spawnDelayFrames; i++)
        {
            yield return null;
        }

        if (spearPrefab == null)
        {
            Debug.LogWarning("Spear prefab is missing.");
            yield break;
        }

        worldChunkRenderer = FindAnyObjectByType<WorldChunkRenderer>();

        for (int i = 0; i < spearCount; i++)
        {
            yield return SpawnSingleSpearCoroutine(initialSpawnZone);
        }
    }

    private IEnumerator SpawnSingleSpearCoroutine(SpawnZone zone)
    {
        LayerMask spawnGroundMask = ResolveGroundMask();
        float deadline = Time.time + Mathf.Max(0.1f, maxGroundWaitSeconds);

        while (Time.time <= deadline)
        {
            if (TryResolveSpawnBasePosition(zone, out Vector3 basePosition) &&
                TryGetGroundedSpawnPosition(basePosition, spawnGroundMask, out Vector3 spawnPosition))
            {
                PickupableWeapon spear = Instantiate(
                    spearPrefab,
                    spawnPosition,
                    Quaternion.Euler(0f, 90f, 0f)
                );

                spear.name = $"Testing Spear {trackedSpears.Count + 1}";
                spear.RemovedFromWorldSupply += HandleSpearRemovedFromWorldSupply;
                trackedSpears.Add(spear);
                yield break;
            }

            for (int frame = 0; frame < Mathf.Max(1, retryDelayFrames); frame++)
            {
                yield return null;
            }
        }

        Debug.LogWarning($"Failed to spawn a spear in {zone} zone within {maxGroundWaitSeconds:0.##} seconds.");
    }

    private void HandleSpearRemovedFromWorldSupply(PickupableWeapon spear)
    {
        if (spear != null)
        {
            spear.RemovedFromWorldSupply -= HandleSpearRemovedFromWorldSupply;
            trackedSpears.Remove(spear);
        }

        StartCoroutine(RespawnReplacementSpear());
    }

    private IEnumerator RespawnReplacementSpear()
    {
        if (respawnDelaySeconds > 0f)
        {
            yield return new WaitForSeconds(respawnDelaySeconds);
        }

        yield return SpawnSingleSpearCoroutine(replacementSpawnZone);
    }

    private bool TryResolveSpawnBasePosition(SpawnZone zone, out Vector3 basePosition)
    {
        if (worldChunkRenderer != null)
        {
            TerrainZone targetZone = zone == SpawnZone.Arena ? TerrainZone.Arena : TerrainZone.Resource;
            if (worldChunkRenderer.TryGetRandomSpawnPosition(targetZone, out basePosition, 0f))
            {
                return true;
            }
        }

        basePosition = transform.position;
        return true;
    }

    private bool TryGetGroundedSpawnPosition(
        Vector3 basePosition,
        LayerMask spawnGroundMask,
        out Vector3 spawnPosition)
    {
        Vector3 rayStart = basePosition + Vector3.up * raycastHeight;

        if (Physics.Raycast(
            rayStart,
            Vector3.down,
            out RaycastHit hit,
            raycastHeight * 2f,
            spawnGroundMask,
            QueryTriggerInteraction.Ignore))
        {
            spawnPosition = hit.point + Vector3.up * spearHeightAboveGround;
            return true;
        }

        spawnPosition = basePosition + Vector3.up * spearHeightAboveGround;
        return worldChunkRenderer != null;
    }

    private LayerMask ResolveGroundMask()
    {
        if (groundLayer.value != 0)
        {
            return groundLayer;
        }

        int fallbackMask = 0;
        int groundLayerIndex = LayerMask.NameToLayer("Ground");
        int defaultLayerIndex = LayerMask.NameToLayer("Default");

        if (groundLayerIndex >= 0)
        {
            fallbackMask |= 1 << groundLayerIndex;
        }

        if (defaultLayerIndex >= 0)
        {
            fallbackMask |= 1 << defaultLayerIndex;
        }

        if (fallbackMask != 0)
        {
            Debug.LogWarning("SpearTestSpawner groundLayer is empty. Falling back to Ground and Default layers.");
            return fallbackMask;
        }

        Debug.LogWarning("SpearTestSpawner groundLayer is empty. Falling back to DefaultRaycastLayers.");
        return Physics.DefaultRaycastLayers;
    }
}
