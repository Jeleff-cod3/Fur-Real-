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
<<<<<<< HEAD
    [SerializeField] private int retryDelayFrames = 5;
    [SerializeField] private float maxGroundWaitSeconds = 5f;
    [SerializeField] private float respawnDelaySeconds = 1f;
    [SerializeField] private SpawnZone initialSpawnZone = SpawnZone.Arena;
    [SerializeField] private SpawnZone replacementSpawnZone = SpawnZone.Resource;

    private readonly HashSet<PickupableWeapon> trackedSpears = new HashSet<PickupableWeapon>();
    private WorldChunkRenderer worldChunkRenderer;
=======
    [SerializeField] private float playerSearchTimeout = 8f;
    [SerializeField] private float playerSearchInterval = 0.2f;
>>>>>>> 4e613ad (woreking mammoth and shit)

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

<<<<<<< HEAD
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
=======
        List<Transform> spawnAnchors = new List<Transform>();
        float deadline = Time.realtimeSinceStartup + playerSearchTimeout;

        while (spawnAnchors.Count == 0 && Time.realtimeSinceStartup < deadline)
        {
            spawnAnchors = FindSpawnAnchors();
            if (spawnAnchors.Count == 0)
            {
                yield return new WaitForSeconds(playerSearchInterval);
            }
        }

        if (spawnAnchors.Count == 0)
        {
            spawnAnchors.Add(transform);
            Debug.LogWarning("No player anchors found for spear spawning. Falling back to the spawner transform.");
        }

        for (int i = 0; i < spearCount; i++)
        {
            Transform anchor = spawnAnchors[i % spawnAnchors.Count];
            int ringIndex = i / spawnAnchors.Count;
            Vector3 basePosition = anchor.position + new Vector3(startOffset.x, 0f, startOffset.z) + ComputePlanarOffset(i, ringIndex);

            if (!TryGetGroundedSpawnPosition(basePosition, out Vector3 spawnPosition))
            {
                Debug.LogWarning($"No procedural ground found near player anchor for spear {i + 1}. Skipping spawn.");
                continue;
            }

            PickupableWeapon spear = Instantiate(
                spearPrefab,
                spawnPosition,
                Quaternion.Euler(0f, 90f, 0f)
            );

            spear.name = $"Testing Spear {i + 1}";
        }
    }

    private List<Transform> FindSpawnAnchors()
    {
        List<Transform> anchors = new List<Transform>();
        HashSet<Transform> seenAnchors = new HashSet<Transform>();

        LocalCubeController[] localPlayers = FindObjectsByType<LocalCubeController>(FindObjectsInactive.Exclude);
        foreach (LocalCubeController localPlayer in localPlayers)
        {
            if (localPlayer != null && seenAnchors.Add(localPlayer.transform))
            {
                anchors.Add(localPlayer.transform);
            }
        }

        RemoteCubeController[] remotePlayers = FindObjectsByType<RemoteCubeController>(FindObjectsInactive.Exclude);
        foreach (RemoteCubeController remotePlayer in remotePlayers)
        {
            if (remotePlayer != null && seenAnchors.Add(remotePlayer.transform))
            {
                anchors.Add(remotePlayer.transform);
            }
        }

        PlayerHealth[] fallbackPlayers = FindObjectsByType<PlayerHealth>(FindObjectsInactive.Exclude);
        foreach (PlayerHealth player in fallbackPlayers)
        {
            if (player != null && seenAnchors.Add(player.transform))
            {
                anchors.Add(player.transform);
            }
        }

        PlayerWeaponPickup[] weaponPlayers = FindObjectsByType<PlayerWeaponPickup>(FindObjectsInactive.Exclude);
        foreach (PlayerWeaponPickup weaponPlayer in weaponPlayers)
        {
            if (weaponPlayer != null && seenAnchors.Add(weaponPlayer.transform))
            {
                anchors.Add(weaponPlayer.transform);
            }
        }

        PlayerCombat[] combatPlayers = FindObjectsByType<PlayerCombat>(FindObjectsInactive.Exclude);
        foreach (PlayerCombat combatPlayer in combatPlayers)
        {
            if (combatPlayer != null && seenAnchors.Add(combatPlayer.transform))
            {
                anchors.Add(combatPlayer.transform);
            }
        }

        return anchors;
    }

    private Vector3 ComputePlanarOffset(int spearIndex, int ringIndex)
    {
        float angleDegrees = (spearIndex * 137.5f) % 360f;
        float radius = Mathf.Max(0.8f, spacing) * (1f + ringIndex * 0.35f);
        float angleRadians = angleDegrees * Mathf.Deg2Rad;
        return new Vector3(Mathf.Cos(angleRadians) * radius, 0f, Mathf.Sin(angleRadians) * radius);
    }

    private bool TryGetGroundedSpawnPosition(Vector3 basePosition, out Vector3 spawnPosition)
>>>>>>> 4e613ad (woreking mammoth and shit)
    {
        Vector3 rayStart = basePosition + Vector3.up * raycastHeight;
        int raycastMask = groundLayer.value != 0 ? groundLayer.value : Physics.DefaultRaycastLayers;
        RaycastHit[] hits = Physics.RaycastAll(
            rayStart,
            Vector3.down,
            raycastHeight * 2f,
<<<<<<< HEAD
            spawnGroundMask,
            QueryTriggerInteraction.Ignore))
=======
            raycastMask,
            QueryTriggerInteraction.Ignore
        );

        if (hits != null && hits.Length > 0)
>>>>>>> 4e613ad (woreking mammoth and shit)
        {
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
        }

        spawnPosition = basePosition + Vector3.up * spearHeightAboveGround;
        return worldChunkRenderer != null;
    }

<<<<<<< HEAD
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
=======
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

        if (candidate.GetComponentInParent<LocalCubeController>() != null || candidate.GetComponentInParent<RemoteCubeController>() != null)
        {
            return false;
        }

        if (candidate.GetComponent<TerrainCollider>() != null || candidate.GetComponent<MeshCollider>() != null)
        {
            return true;
        }

        return candidate.gameObject.name.StartsWith("Chunk") || candidate.attachedRigidbody == null;
>>>>>>> 4e613ad (woreking mammoth and shit)
    }
}
