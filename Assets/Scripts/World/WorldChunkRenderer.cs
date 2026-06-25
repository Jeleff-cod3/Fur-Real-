using System.Collections.Generic;
using UnityEngine;
using Unity.AI.Navigation;
using UnityEngine.AI;

public class WorldChunkRenderer : MonoBehaviour
{
    [Header("Collision Layers")]
    public string groundLayerName = "Ground";

    [Header("Navigation")]
    public NavMeshSurface navMeshSurface;
    public bool buildNavMeshAtRuntime = true;
    public bool rocksBlockNavigation = true;
    public bool treesBlockNavigation = true;
    public bool deadTreesBlockNavigation = true;

    [Header("Full Map NavMesh Baking")]
    public bool renderFullMapBeforeNavMeshBake = true;
    public bool useTerrainOnlyInitialNavMeshBake = true;
    public bool logWorldHeightRange = false;
    [Min(1)]
    public int initialNavMeshTerrainSampleStep = 4;
    public bool hideChunksOutsideViewAfterBake = true;
    public bool unloadDistantChunksAfterInitialBake = true;
    public int initialChunkBatchSize = 6;

    [Header("References")]
    public Transform player;
    public Material terrainMaterial;
    public GameObject cavePrefab;
    public PickupableWeapon craftedSpearPrefab;

    [Header("Map Settings")]
    public int mapSize = 1000;
    public int chunkSize = 50;
    public int viewDistance = 3;

    [Header("Map Border")]
    public bool createMapBorder = true;
    [Min(2f)]
    public float mapBorderInset = 6f;
    [Min(2f)]
    public float mapBorderWallHeight = 10f;
    [Min(0.5f)]
    public float mapBorderWallThickness = 4f;
    [Min(0f)]
    public float mapBorderWallBuriedDepth = 8f;
    [Min(2)]
    public int mapBorderSegmentLength = 8;
    public Material mapBorderWallMaterial;
    public bool recoverPlayersOutsideMap = true;
    public float outOfBoundsYThreshold = -8f;
    [Min(4f)]
    public float playerRecoveryInset = 18f;

    [Header("Arena Settings")]
    public int arenaRadius = 120;
    public int transitionDistance = 40;
    public int resourceRadius = 460;

    [Header("Terrain Colors")]
    public TerrainColorSettings terrainColorSettings = new TerrainColorSettings();

    [Header("Ground Grass")]
    public GroundGrassSettings groundGrassSettings = new GroundGrassSettings();
    public Material groundGrassMaterial;

    [Header("Grass")]
    public GrassSettings grassSettings = new GrassSettings();
    public Material grassMaterial;

    [Header("Trees")]
    public TreeSettings treeSettings = new TreeSettings();
    public Material treeMaterial;
    public Material treeShadowMaterial;

    [Header("Resource Forest Trees")]
    public ResourceForestTreeSettings resourceForestTreeSettings;
    public Material resourceForestTreeMaterial;
    public Material resourceForestTreeShadowMaterial;

    [Header("Rocks")]
    public RockSettings rockSettings = new RockSettings();
    public Material rockMaterial;
    public Material rockShadowMaterial;

    [Header("Pickup Resources")]
    public PickupStickSpawnSettings pickupStickSpawnSettings = new PickupStickSpawnSettings();
    public PickupRockSpawnSettings pickupRockSpawnSettings = new PickupRockSpawnSettings();

    [Header("Extra Vegetation")]
    public VegetationSettings vegetationSettings = new VegetationSettings();
    public Material vegetationMaterial;
    public Material vegetationShadowMaterial;

    [Header("Dead Trees")]
    public DeadTreeSettings deadTreeSettings = new DeadTreeSettings();
    public Material deadTreeMaterial;
    public Material deadTreeShadowMaterial;

    [Header("Terrain Settings")]
    public int seed = 12345;
    public float arenaHeightMultiplier = 2f;
    public float resourceHeightMultiplier = 16f;
    public float noiseScale = 120f;
    public int octaves = 4;

    [Range(0f, 1f)]
    public float persistence = 0.45f;

    public float lacunarity = 2f;
    public float uvScale = 30f;

    private WorldData worldData;

    public bool IsNavMeshReady { get; private set; }
    public bool IsBootstrapComplete { get; private set; }
    public string BootstrapStatus { get; private set; } = "Preparing world...";
    public float BootstrapProgress { get; private set; }
    public WorldData WorldData => worldData;
    public int WorldSeed => seed;
    public int ChunkWorldSize => chunkSize;
    public int WorldMapSize => mapSize;
    public ResourceForestTreeSettings ResourceForestSettings => resourceForestTreeSettings;

    private readonly Dictionary<Vector2Int, GameObject> activeChunks =
        new Dictionary<Vector2Int, GameObject>();

    private readonly HashSet<Transform> trackedPlayers =
        new HashSet<Transform>();

    private readonly HashSet<Vector2Int> currentTrackedPlayerChunks =
        new HashSet<Vector2Int>();

    private readonly HashSet<Vector2Int> trackedPlayerChunkBuffer =
        new HashSet<Vector2Int>();

    private GameObject caveInstance;
    private GameObject mapBorderRoot;
    private GameObject navMeshBakeRoot;
    private PickupableWeapon cachedFallbackSpearPrefab;
    private Material cachedRuntimeMapBorderMaterial;

    private readonly Dictionary<string, PickupableItem> activeResourceItems =
        new Dictionary<string, PickupableItem>();

    private readonly Dictionary<PickupableItem, string> resourceIdsByItem =
        new Dictionary<PickupableItem, string>();

    private readonly HashSet<string> removedResourceIds =
        new HashSet<string>();

    private readonly List<string> resourceIdsToCleanup =
        new List<string>();

    public PickupableWeapon CraftedSpearPrefab => ResolveCraftedSpearPrefab();

    private System.Collections.IEnumerator Start()
    {
        IsBootstrapComplete = false;
        BootstrapProgress = 0f;
        BootstrapStatus = "Preparing world renderer...";

        if (Mathf.Abs(transform.lossyScale.y) < 0.001f)
        {
            Debug.LogError("WorldChunkRenderer parent has Y scale near 0. This will flatten all chunks.");
        }

        Debug.Log($"WorldChunkRenderer scale: {transform.lossyScale}");

        if (navMeshSurface == null)
        {
            navMeshSurface = GetComponent<NavMeshSurface>();
        }

        BootstrapStatus = "Generating terrain data...";
        BootstrapProgress = 0.08f;
        yield return null;
        GenerateWorld();
        CreateMapBorder();

        BootstrapStatus = "Inspecting terrain...";
        BootstrapProgress = 0.16f;
        yield return null;
        if (logWorldHeightRange)
        {
            DebugWorldHeightRange();
        }

        BootstrapStatus = "Placing players...";
        BootstrapProgress = 0.22f;
        PlacePlayerAtArenaCenter();
        SyncTrackedPlayerChunks();
        yield return null;

        if (renderFullMapBeforeNavMeshBake)
        {
            BootstrapStatus = useTerrainOnlyInitialNavMeshBake
                ? "Building navigation terrain..."
                : "Building world chunks...";
            yield return RenderAllChunksForNavMeshBakeRoutine();
        }
        else
        {
            BootstrapStatus = "Loading nearby chunks...";
            UpdateVisibleChunks();
            BootstrapProgress = 0.62f;
            yield return null;
        }

        BootstrapStatus = "Baking navigation mesh...";
        BootstrapProgress = Mathf.Max(BootstrapProgress, 0.82f);
        yield return null;
        BuildWorldNavMesh();

        DestroyNavMeshBakeChunks();

        BootstrapStatus = "Snapping spawn positions...";
        BootstrapProgress = 0.93f;
        yield return null;
        SnapPlayerToNavMesh();

        if (renderFullMapBeforeNavMeshBake && useTerrainOnlyInitialNavMeshBake)
        {
            BootstrapStatus = "Loading nearby chunks...";
            UpdateVisibleChunks();
            yield return null;
        }
        else if (renderFullMapBeforeNavMeshBake && hideChunksOutsideViewAfterBake)
        {
            BootstrapStatus = unloadDistantChunksAfterInitialBake
                ? "Unloading distant chunks..."
                : "Finalizing visible chunks...";

            if (unloadDistantChunksAfterInitialBake)
            {
                UpdateVisibleChunks();
            }
            else
            {
                UpdateChunkVisibilityOnly();
            }

            yield return null;
        }

        BootstrapStatus = "Spawning cave...";
        SpawnCave();
        BootstrapProgress = 1f;
        BootstrapStatus = "World ready.";
        IsBootstrapComplete = true;
    }

    private void Update()
    {
        if (HaveTrackedPlayerChunksChanged())
        {
            SyncTrackedPlayerChunks();

            if (renderFullMapBeforeNavMeshBake)
            {
                if (hideChunksOutsideViewAfterBake && unloadDistantChunksAfterInitialBake)
                {
                    UpdateVisibleChunks();
                }
                else
                {
                    UpdateChunkVisibilityOnly();
                }
            }
            else
            {
                UpdateVisibleChunks();

                // In streaming mode, new blockers can appear after the first bake,
                // so we rebuild when chunks change.
                BuildWorldNavMesh();
            }
        }

        UpdateGroundGrassMaterial();
        RecoverTrackedPlayersOutsideMap();
    }

    public void SetPrimaryPlayer(Transform playerTransform)
    {
        player = playerTransform;
        RegisterTrackedPlayer(playerTransform);
    }

    public void RegisterTrackedPlayer(Transform playerTransform)
    {
        if (playerTransform == null)
        {
            return;
        }

        trackedPlayers.Add(playerTransform);
    }

    public void UnregisterTrackedPlayer(Transform playerTransform)
    {
        if (playerTransform == null)
        {
            return;
        }

        trackedPlayers.Remove(playerTransform);

        if (player == playerTransform)
        {
            player = null;
        }
    }

    private void GenerateWorld()
    {
        worldData = WorldDataGenerator.GenerateWorldData(
            mapSize,
            seed,
            arenaRadius,
            transitionDistance,
            resourceRadius,
            arenaHeightMultiplier,
            resourceHeightMultiplier,
            noiseScale,
            octaves,
            persistence,
            lacunarity
        );
    }

    private void CreateMapBorder()
    {
        if (mapBorderRoot != null)
        {
            Destroy(mapBorderRoot);
            mapBorderRoot = null;
        }

        if (!createMapBorder || worldData == null || worldData.size <= 0)
        {
            return;
        }

        mapBorderRoot = new GameObject("Map Border");
        mapBorderRoot.transform.SetParent(transform, false);
        mapBorderRoot.transform.localPosition = Vector3.zero;
        mapBorderRoot.transform.localRotation = Quaternion.identity;
        mapBorderRoot.transform.localScale = Vector3.one;

        CreateMapBorderSide(mapBorderRoot.transform, "South Border", false, mapBorderInset);
        CreateMapBorderSide(mapBorderRoot.transform, "North Border", false, worldData.size - mapBorderInset);
        CreateMapBorderSide(mapBorderRoot.transform, "West Border", true, mapBorderInset);
        CreateMapBorderSide(mapBorderRoot.transform, "East Border", true, worldData.size - mapBorderInset);
    }

    private void CreateMapBorderSide(Transform parent, string sideName, bool alongZAxis, float fixedAxisPosition)
    {
        if (parent == null || worldData == null)
        {
            return;
        }

        int segmentLength = Mathf.Max(2, mapBorderSegmentLength);
        float fixedAxis = Mathf.Clamp(fixedAxisPosition, 0f, worldData.size);
        float halfThickness = Mathf.Max(0.25f, mapBorderWallThickness * 0.5f);

        for (int start = 0; start < worldData.size; start += segmentLength)
        {
            int end = Mathf.Min(start + segmentLength, worldData.size);
            float span = Mathf.Max(0.5f, end - start);
            float centerAlong = start + span * 0.5f;

            int minX = alongZAxis
                ? Mathf.RoundToInt(Mathf.Clamp(fixedAxis - halfThickness, 0f, worldData.size))
                : start;
            int maxX = alongZAxis
                ? Mathf.RoundToInt(Mathf.Clamp(fixedAxis + halfThickness, 0f, worldData.size))
                : end;
            int minZ = alongZAxis
                ? start
                : Mathf.RoundToInt(Mathf.Clamp(fixedAxis - halfThickness, 0f, worldData.size));
            int maxZ = alongZAxis
                ? end
                : Mathf.RoundToInt(Mathf.Clamp(fixedAxis + halfThickness, 0f, worldData.size));

            GetTerrainHeightRange(minX, maxX, minZ, maxZ, out float minHeight, out float maxHeight);

            float bottom = minHeight - Mathf.Max(0f, mapBorderWallBuriedDepth);
            float top = maxHeight + Mathf.Max(2f, mapBorderWallHeight);
            Vector3 position = alongZAxis
                ? new Vector3(fixedAxis, (bottom + top) * 0.5f, centerAlong)
                : new Vector3(centerAlong, (bottom + top) * 0.5f, fixedAxis);
            Vector3 scale = alongZAxis
                ? new Vector3(mapBorderWallThickness, top - bottom, span + 0.25f)
                : new Vector3(span + 0.25f, top - bottom, mapBorderWallThickness);

            GameObject segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
            segment.name = $"{sideName} {start}-{end}";
            segment.layer = LayerMask.NameToLayer("Default");
            segment.transform.SetParent(parent, false);
            segment.transform.localPosition = position;
            segment.transform.localRotation = Quaternion.identity;
            segment.transform.localScale = scale;

            Renderer renderer = segment.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = ResolveMapBorderWallMaterial();
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.TwoSided;
                renderer.receiveShadows = true;
            }

            MarkObjectAsNotWalkable(segment);
        }
    }

    private void GetTerrainHeightRange(int minX, int maxX, int minZ, int maxZ, out float minHeight, out float maxHeight)
    {
        minHeight = float.MaxValue;
        maxHeight = float.MinValue;

        if (worldData == null)
        {
            minHeight = 0f;
            maxHeight = 0f;
            return;
        }

        int safeMinX = Mathf.Clamp(minX, 0, worldData.size);
        int safeMaxX = Mathf.Clamp(maxX, 0, worldData.size);
        int safeMinZ = Mathf.Clamp(minZ, 0, worldData.size);
        int safeMaxZ = Mathf.Clamp(maxZ, 0, worldData.size);

        for (int z = safeMinZ; z <= safeMaxZ; z++)
        {
            for (int x = safeMinX; x <= safeMaxX; x++)
            {
                float height = worldData.GetHeight(x, z);
                if (height < minHeight)
                {
                    minHeight = height;
                }

                if (height > maxHeight)
                {
                    maxHeight = height;
                }
            }
        }

        if (minHeight == float.MaxValue || maxHeight == float.MinValue)
        {
            float fallback = worldData.GetHeight(safeMinX, safeMinZ);
            minHeight = fallback;
            maxHeight = fallback;
        }
    }

    private Material ResolveMapBorderWallMaterial()
    {
        if (mapBorderWallMaterial != null)
        {
            return mapBorderWallMaterial;
        }

        if (cachedRuntimeMapBorderMaterial != null)
        {
            return cachedRuntimeMapBorderMaterial;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        if (shader == null)
        {
            shader = Shader.Find("Diffuse");
        }

        cachedRuntimeMapBorderMaterial = new Material(shader);
        cachedRuntimeMapBorderMaterial.name = "Runtime Map Border Material";

        Texture2D stripeTexture = BuildMapBorderStripeTexture();
        if (cachedRuntimeMapBorderMaterial.HasProperty("_BaseMap"))
        {
            cachedRuntimeMapBorderMaterial.SetTexture("_BaseMap", stripeTexture);
            cachedRuntimeMapBorderMaterial.SetColor("_BaseColor", Color.white);
        }

        if (cachedRuntimeMapBorderMaterial.HasProperty("_MainTex"))
        {
            cachedRuntimeMapBorderMaterial.SetTexture("_MainTex", stripeTexture);
            cachedRuntimeMapBorderMaterial.SetColor("_Color", Color.white);
        }

        if (cachedRuntimeMapBorderMaterial.HasProperty("_Smoothness"))
        {
            cachedRuntimeMapBorderMaterial.SetFloat("_Smoothness", 0.08f);
        }

        if (cachedRuntimeMapBorderMaterial.HasProperty("_Metallic"))
        {
            cachedRuntimeMapBorderMaterial.SetFloat("_Metallic", 0.02f);
        }

        cachedRuntimeMapBorderMaterial.mainTextureScale = new Vector2(4f, 1f);
        return cachedRuntimeMapBorderMaterial;
    }

    private static Texture2D BuildMapBorderStripeTexture()
    {
        Texture2D texture = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        Color dark = new Color(0.12f, 0.10f, 0.08f, 1f);
        Color bright = new Color(0.84f, 0.64f, 0.18f, 1f);

        for (int y = 0; y < texture.height; y++)
        {
            for (int x = 0; x < texture.width; x++)
            {
                bool brightStripe = ((x + y) % 6) < 3;
                texture.SetPixel(x, y, brightStripe ? bright : dark);
            }
        }

        texture.wrapMode = TextureWrapMode.Repeat;
        texture.filterMode = FilterMode.Point;
        texture.Apply();
        return texture;
    }

    private void RecoverTrackedPlayersOutsideMap()
    {
        if (!recoverPlayersOutsideMap || worldData == null)
        {
            return;
        }

        RecoverPlayerIfOutsideMap(player);

        foreach (Transform trackedPlayer in trackedPlayers)
        {
            if (trackedPlayer == null || trackedPlayer == player)
            {
                continue;
            }

            RecoverPlayerIfOutsideMap(trackedPlayer);
        }
    }

    private void RecoverPlayerIfOutsideMap(Transform playerTransform)
    {
        if (playerTransform == null)
        {
            return;
        }

        Vector3 position = playerTransform.position;
        float safeInset = Mathf.Clamp(playerRecoveryInset, 2f, Mathf.Max(2f, worldData.size * 0.5f - 1f));
        bool outsideHorizontalBounds =
            position.x < safeInset ||
            position.x > worldData.size - safeInset ||
            position.z < safeInset ||
            position.z > worldData.size - safeInset;
        bool belowWorld = position.y < outOfBoundsYThreshold;

        if (!outsideHorizontalBounds && !belowWorld)
        {
            return;
        }

        Vector3 recoveryCandidate = new Vector3(
            Mathf.Clamp(position.x, safeInset, worldData.size - safeInset),
            position.y,
            Mathf.Clamp(position.z, safeInset, worldData.size - safeInset)
        );

        Vector3 recoveryPosition = GetArenaCenterWorldPosition(1f);
        bool foundRecovery = false;

        if (TryGetGroundHeightAtWorldPosition(recoveryCandidate, out float groundHeight))
        {
            recoveryPosition = new Vector3(recoveryCandidate.x, groundHeight + 1f, recoveryCandidate.z);
            foundRecovery = true;
        }

        if (IsNavMeshReady && NavMesh.SamplePosition(recoveryPosition, out NavMeshHit hit, 24f, NavMesh.AllAreas))
        {
            recoveryPosition = hit.position + Vector3.up * 0.1f;
            foundRecovery = true;
        }

        if (!foundRecovery)
        {
            recoveryPosition = GetArenaCenterWorldPosition(1f);
        }

        Rigidbody body = playerTransform.GetComponent<Rigidbody>();
        if (body != null)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.position = recoveryPosition;
        }

        NavMeshAgent agent = playerTransform.GetComponent<NavMeshAgent>();
        if (agent != null && agent.enabled)
        {
            agent.Warp(recoveryPosition);
        }

        playerTransform.position = recoveryPosition;
    }

    private System.Collections.IEnumerator RenderAllChunksForNavMeshBakeRoutine()
    {
        int chunksPerAxis = mapSize / chunkSize;
        int totalChunks = Mathf.Max(1, chunksPerAxis * chunksPerAxis);
        int processedChunks = 0;
        int batchSize = Mathf.Max(1, initialChunkBatchSize);

        if (useTerrainOnlyInitialNavMeshBake)
        {
            if (navMeshBakeRoot != null)
            {
                Destroy(navMeshBakeRoot);
            }

            navMeshBakeRoot = new GameObject("Temporary NavMesh Bake Terrain");
            navMeshBakeRoot.transform.SetParent(transform, false);
            navMeshBakeRoot.transform.localPosition = Vector3.zero;
            navMeshBakeRoot.transform.localRotation = Quaternion.identity;
            navMeshBakeRoot.transform.localScale = Vector3.one;
        }

        for (int z = 0; z < chunksPerAxis; z++)
        {
            for (int x = 0; x < chunksPerAxis; x++)
            {
                Vector2Int chunkCoord = new Vector2Int(x, z);

                if (IsChunkInsideMap(chunkCoord))
                {
                    if (useTerrainOnlyInitialNavMeshBake)
                    {
                        CreateNavMeshBakeChunk(chunkCoord);
                    }
                    else if (!activeChunks.ContainsKey(chunkCoord))
                    {
                        CreateChunk(chunkCoord);
                    }
                }

                processedChunks++;

                if (processedChunks % batchSize == 0)
                {
                    float chunkProgress = Mathf.Clamp01(processedChunks / (float)totalChunks);
                    BootstrapProgress = Mathf.Lerp(0.28f, 0.78f, chunkProgress);
                    BootstrapStatus = useTerrainOnlyInitialNavMeshBake
                        ? $"Building navigation terrain... {processedChunks}/{totalChunks}"
                        : $"Building world chunks... {processedChunks}/{totalChunks}";
                    yield return null;
                }
            }
        }

        BootstrapProgress = 0.78f;
        BootstrapStatus = useTerrainOnlyInitialNavMeshBake
            ? $"Building navigation terrain... {totalChunks}/{totalChunks}"
            : $"Building world chunks... {totalChunks}/{totalChunks}";

        if (useTerrainOnlyInitialNavMeshBake)
        {
            Debug.Log($"Built {totalChunks} temporary terrain chunks for NavMesh bake.");
        }
        else
        {
            Debug.Log($"Rendered {activeChunks.Count} chunks before NavMesh bake.");
        }
    }

    private void DestroyNavMeshBakeChunks()
    {
        if (navMeshBakeRoot == null)
        {
            return;
        }

        Destroy(navMeshBakeRoot);
        navMeshBakeRoot = null;
    }

    private void BuildWorldNavMesh()
    {
        IsNavMeshReady = false;

        if (!buildNavMeshAtRuntime)
        {
            IsNavMeshReady = true;
            return;
        }

        if (navMeshSurface == null)
        {
            navMeshSurface = GetComponent<NavMeshSurface>();
        }

        if (navMeshSurface == null)
        {
            navMeshSurface = gameObject.AddComponent<NavMeshSurface>();
        }

        navMeshSurface.collectObjects = CollectObjects.Children;
        navMeshSurface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
        navMeshSurface.layerMask = GetNavMeshBakeLayerMask();
        navMeshSurface.defaultArea = 0;

        navMeshSurface.BuildNavMesh();

        IsNavMeshReady = true;

        Debug.Log("Runtime NavMesh built for generated world.");
    }

    private LayerMask GetNavMeshBakeLayerMask()
    {
        int mask = 0;

        int groundLayer = LayerMask.NameToLayer(groundLayerName);
        if (groundLayer >= 0)
        {
            mask |= 1 << groundLayer;
        }

        int defaultLayer = LayerMask.NameToLayer("Default");
        if (defaultLayer >= 0)
        {
            mask |= 1 << defaultLayer;
        }

        return mask != 0 ? mask : Physics.DefaultRaycastLayers;
    }

    private Vector2Int GetChunkCoord(Vector3 worldPosition)
    {
        int chunkX = Mathf.FloorToInt(worldPosition.x / chunkSize);
        int chunkZ = Mathf.FloorToInt(worldPosition.z / chunkSize);

        return new Vector2Int(chunkX, chunkZ);
    }

    private void UpdateVisibleChunks()
    {
        GetTrackedPlayerChunks(trackedPlayerChunkBuffer);

        foreach (Vector2Int playerChunk in trackedPlayerChunkBuffer)
        {
            for (int zOffset = -viewDistance; zOffset <= viewDistance; zOffset++)
            {
                for (int xOffset = -viewDistance; xOffset <= viewDistance; xOffset++)
                {
                    Vector2Int chunkCoord = new Vector2Int(
                        playerChunk.x + xOffset,
                        playerChunk.y + zOffset
                    );

                    if (IsChunkInsideMap(chunkCoord) && !activeChunks.ContainsKey(chunkCoord))
                    {
                        CreateChunk(chunkCoord);
                    }
                }
            }
        }

        List<Vector2Int> chunksToRemove = new List<Vector2Int>();

        foreach (Vector2Int coord in activeChunks.Keys)
        {
            if (!IsChunkInRangeOfTrackedPlayers(coord, trackedPlayerChunkBuffer, viewDistance + 1))
            {
                chunksToRemove.Add(coord);
            }
        }

        foreach (Vector2Int coord in chunksToRemove)
        {
            Destroy(activeChunks[coord]);
            activeChunks.Remove(coord);
        }
    }

    private void UpdateChunkVisibilityOnly()
    {
        GetTrackedPlayerChunks(trackedPlayerChunkBuffer);

        foreach (KeyValuePair<Vector2Int, GameObject> pair in activeChunks)
        {
            bool shouldBeVisible = IsChunkInRangeOfTrackedPlayers(
                pair.Key,
                trackedPlayerChunkBuffer,
                viewDistance + 1
            );
            SetChunkRenderersEnabled(pair.Value, shouldBeVisible);
        }
    }

    private bool HaveTrackedPlayerChunksChanged()
    {
        GetTrackedPlayerChunks(trackedPlayerChunkBuffer);
        return !currentTrackedPlayerChunks.SetEquals(trackedPlayerChunkBuffer);
    }

    private void SyncTrackedPlayerChunks()
    {
        GetTrackedPlayerChunks(currentTrackedPlayerChunks);
    }

    private void GetTrackedPlayerChunks(HashSet<Vector2Int> chunkCoords)
    {
        chunkCoords.Clear();
        CleanupTrackedPlayers();

        if (player != null)
        {
            chunkCoords.Add(GetChunkCoord(player.position));
        }

        foreach (Transform trackedPlayer in trackedPlayers)
        {
            if (trackedPlayer == null || trackedPlayer == player)
            {
                continue;
            }

            chunkCoords.Add(GetChunkCoord(trackedPlayer.position));
        }

        if (chunkCoords.Count == 0)
        {
            chunkCoords.Add(Vector2Int.zero);
        }
    }

    private void CleanupTrackedPlayers()
    {
        trackedPlayers.RemoveWhere(trackedPlayer => trackedPlayer == null);

        if (player == null)
        {
            return;
        }

        if (!player.gameObject.scene.IsValid())
        {
            player = null;
        }
    }

    private static bool IsChunkInRangeOfTrackedPlayers(
        Vector2Int chunkCoord,
        IEnumerable<Vector2Int> playerChunks,
        float maxDistance
    )
    {
        foreach (Vector2Int playerChunk in playerChunks)
        {
            if (Vector2Int.Distance(chunkCoord, playerChunk) <= maxDistance)
            {
                return true;
            }
        }

        return false;
    }

    private void SetChunkRenderersEnabled(GameObject chunkObject, bool enabled)
    {
        MeshRenderer[] renderers = chunkObject.GetComponentsInChildren<MeshRenderer>(true);

        foreach (MeshRenderer meshRenderer in renderers)
        {
            meshRenderer.enabled = enabled;
        }
    }

    private bool IsChunkInsideMap(Vector2Int chunkCoord)
    {
        int startX = chunkCoord.x * chunkSize;
        int startZ = chunkCoord.y * chunkSize;

        return startX >= 0 &&
               startZ >= 0 &&
               startX + chunkSize <= mapSize &&
               startZ + chunkSize <= mapSize;
    }

    private void CreateChunk(Vector2Int chunkCoord)
    {
        int startX = chunkCoord.x * chunkSize;
        int startZ = chunkCoord.y * chunkSize;

        GameObject chunkObject = new GameObject($"Chunk {chunkCoord.x}, {chunkCoord.y}");
        int terrainLayer = LayerMask.NameToLayer(groundLayerName);

        if (terrainLayer >= 0)
        {
            chunkObject.layer = terrainLayer;
        }
        else
        {
            Debug.LogWarning($"Layer '{groundLayerName}' does not exist. Create it in Project Settings > Tags and Layers.");
        }
        chunkObject.transform.SetParent(transform, false);
        chunkObject.transform.localPosition = new Vector3(startX, 0f, startZ);
        chunkObject.transform.localRotation = Quaternion.identity;
        chunkObject.transform.localScale = Vector3.one;

        MeshFilter meshFilter = chunkObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = chunkObject.AddComponent<MeshRenderer>();
        MeshCollider meshCollider = chunkObject.AddComponent<MeshCollider>();

        meshRenderer.sharedMaterial = terrainMaterial;

        Mesh mesh = MeshGenerator.GenerateChunkMesh(
            worldData,
            startX,
            startZ,
            chunkSize,
            uvScale,
            seed,
            terrainColorSettings
        );

        meshFilter.sharedMesh = mesh;
        meshCollider.sharedMesh = mesh;

        CreateGroundGrassForChunk(chunkObject, startX, startZ);
        CreateGrassForChunk(chunkObject, startX, startZ);
        CreateTreesForChunk(chunkObject, startX, startZ);
        CreateResourceForestTreesForChunk(chunkObject, startX, startZ);
        CreateVegetationForChunk(chunkObject, startX, startZ);
        CreateDeadTreesForChunk(chunkObject, startX, startZ);
        CreateRocksForChunk(chunkObject, startX, startZ);
        CreatePickupResourcesForChunk(chunkObject, chunkCoord, startX, startZ);

        activeChunks.Add(chunkCoord, chunkObject);
    }

    private void CreateNavMeshBakeChunk(Vector2Int chunkCoord)
    {
        if (navMeshBakeRoot == null)
        {
            return;
        }

        int startX = chunkCoord.x * chunkSize;
        int startZ = chunkCoord.y * chunkSize;

        GameObject chunkObject = new GameObject($"NavMesh Bake Chunk {chunkCoord.x}, {chunkCoord.y}");
        int terrainLayer = LayerMask.NameToLayer(groundLayerName);

        if (terrainLayer >= 0)
        {
            chunkObject.layer = terrainLayer;
        }

        chunkObject.transform.SetParent(navMeshBakeRoot.transform, false);
        chunkObject.transform.localPosition = new Vector3(startX, 0f, startZ);
        chunkObject.transform.localRotation = Quaternion.identity;
        chunkObject.transform.localScale = Vector3.one;

        MeshCollider meshCollider = chunkObject.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = MeshGenerator.GenerateCollisionChunkMesh(
            worldData,
            startX,
            startZ,
            chunkSize,
            initialNavMeshTerrainSampleStep
        );

        CreateNavMeshBlockersForChunk(chunkObject, startX, startZ);
    }

    private void MarkObjectAsNotWalkable(GameObject obj)
    {
        NavMeshModifier modifier = obj.AddComponent<NavMeshModifier>();

        modifier.overrideArea = true;

        int notWalkableArea = NavMesh.GetAreaFromName("Not Walkable");

        if (notWalkableArea >= 0)
        {
            modifier.area = notWalkableArea;
        }
        else
        {
            modifier.area = 1;
        }
    }

    private void CreateNavMeshBlockersForChunk(GameObject chunkObject, int startX, int startZ)
    {
        if (chunkObject == null)
        {
            return;
        }

        if (treesBlockNavigation && treeSettings != null && treeSettings.enabled)
        {
            TreeChunkMeshes treeMeshes = TreeChunkGenerator.GenerateTreeMeshes(
                worldData,
                startX,
                startZ,
                chunkSize,
                seed,
                treeSettings
            );
            CreateNavMeshBlockerObject(chunkObject.transform, "Bake Trees", treeMeshes.treeMesh);
        }

        if (treesBlockNavigation && resourceForestTreeSettings != null && resourceForestTreeSettings.enabled)
        {
            ResourceForestTreeChunkMeshes resourceMeshes = ResourceForestTreeChunkGenerator.GenerateTrees(
                worldData,
                startX,
                startZ,
                chunkSize,
                seed,
                resourceForestTreeSettings
            );
            CreateNavMeshBlockerObject(chunkObject.transform, "Bake ResourceForestTrees", resourceMeshes.treeMesh);
        }

        if (rocksBlockNavigation && rockSettings != null && rockSettings.enabled)
        {
            RockChunkMeshes rockMeshes = RockChunkGenerator.GenerateRockMeshes(
                worldData,
                startX,
                startZ,
                chunkSize,
                seed,
                rockSettings
            );
            CreateNavMeshBlockerObject(chunkObject.transform, "Bake Rocks", rockMeshes.rockMesh);
        }

        if (deadTreesBlockNavigation && deadTreeSettings != null && deadTreeSettings.enabled)
        {
            DeadTreeChunkMeshes deadTreeMeshes = DeadTreeChunkGenerator.GenerateDeadTreeMeshes(
                worldData,
                startX,
                startZ,
                chunkSize,
                seed,
                deadTreeSettings
            );
            CreateNavMeshBlockerObject(chunkObject.transform, "Bake DeadTrees", deadTreeMeshes.deadTreeMesh);
        }
    }

    private void CreateNavMeshBlockerObject(Transform parent, string name, Mesh blockerMesh)
    {
        if (parent == null || blockerMesh == null || blockerMesh.vertexCount <= 0)
        {
            return;
        }

        GameObject blockerObject = new GameObject(name);
        blockerObject.layer = LayerMask.NameToLayer("Default");
        blockerObject.transform.SetParent(parent, false);
        blockerObject.transform.localPosition = Vector3.zero;
        blockerObject.transform.localRotation = Quaternion.identity;
        blockerObject.transform.localScale = Vector3.one;

        MeshCollider blockerCollider = blockerObject.AddComponent<MeshCollider>();
        blockerCollider.sharedMesh = blockerMesh;
        MarkObjectAsNotWalkable(blockerObject);
    }

    private void UpdateGroundGrassMaterial()
    {
        if (groundGrassMaterial == null || groundGrassSettings == null)
        {
            return;
        }

        Transform grassPlayer = player;

        if (grassPlayer == null)
        {
            foreach (Transform trackedPlayer in trackedPlayers)
            {
                if (trackedPlayer != null)
                {
                    grassPlayer = trackedPlayer;
                    break;
                }
            }
        }

        if (grassPlayer == null)
        {
            return;
        }

        groundGrassMaterial.SetVector("_PlayerPosition", grassPlayer.position);

        groundGrassMaterial.SetFloat("_WindStrength", groundGrassSettings.windStrength);
        groundGrassMaterial.SetFloat("_WindSpeed", groundGrassSettings.windSpeed);
        groundGrassMaterial.SetFloat("_WindScale", groundGrassSettings.windScale);

        groundGrassMaterial.SetFloat("_PushRadius", groundGrassSettings.playerPushRadius);
        groundGrassMaterial.SetFloat("_PushStrength", groundGrassSettings.playerPushStrength);
        groundGrassMaterial.SetFloat("_FlattenStrength", groundGrassSettings.playerFlattenStrength);
    }

    private void DebugWorldHeightRange()
    {
        if (worldData == null)
        {
            return;
        }

        float min = float.MaxValue;
        float max = float.MinValue;

        for (int z = 0; z <= worldData.size; z++)
        {
            for (int x = 0; x <= worldData.size; x++)
            {
                float h = worldData.GetHeight(x, z);

                if (h < min)
                {
                    min = h;
                }

                if (h > max)
                {
                    max = h;
                }
            }
        }

        Debug.Log($"WORLD HEIGHT RANGE: min={min}, max={max}, difference={max - min}");
    }

    private void PlacePlayerAtArenaCenter()
    {
        if (player == null || worldData == null)
        {
            Debug.LogWarning("Player or worldData is missing.");
            return;
        }

        player.position = GetArenaCenterWorldPosition(1f);
        Debug.Log($"Player placed in ARENA AREA: {player.position}");
    }

    private void SnapPlayerToNavMesh()
    {
        if (player == null)
        {
            return;
        }

        if (NavMesh.SamplePosition(player.position, out NavMeshHit hit, 50f, NavMesh.AllAreas))
        {
            player.position = hit.position + Vector3.up * 0.1f;

            NavMeshAgent agent = player.GetComponent<NavMeshAgent>();

            if (agent != null)
            {
                agent.Warp(hit.position);
            }

            Debug.Log($"Player snapped to NavMesh at {hit.position}");
        }
        else
        {
            Debug.LogError("Could not find NavMesh near player. NavMesh probably did not bake near the spawn point.");
        }
    }

    private void CreateResourceForestTreesForChunk(GameObject chunkObject, int startX, int startZ)
    {
        if (resourceForestTreeSettings == null || !resourceForestTreeSettings.enabled)
        {
            return;
        }

        var meshData = ResourceForestTreeChunkGenerator.GenerateTrees(
            worldData, startX, startZ, chunkSize, seed, resourceForestTreeSettings
        );

        if (meshData.treeMesh != null)
        {
            GameObject treeObj = new GameObject("ResourceForestTrees");
            treeObj.transform.SetParent(chunkObject.transform, false);
            var mf = treeObj.AddComponent<MeshFilter>();
            var mr = treeObj.AddComponent<MeshRenderer>();
            var mc = treeObj.AddComponent<MeshCollider>();
            mf.sharedMesh = meshData.treeMesh;
            mc.sharedMesh = meshData.treeMesh;
            mr.sharedMaterial = resourceForestTreeMaterial != null
                ? resourceForestTreeMaterial
                : treeMaterial;

            if (treesBlockNavigation)
            {
                MarkObjectAsNotWalkable(treeObj);
            }
        }

        if (meshData.shadowMesh != null)
        {
            GameObject shadowObj = new GameObject("ResourceForestShadows");
            shadowObj.transform.SetParent(chunkObject.transform, false);
            var mf = shadowObj.AddComponent<MeshFilter>();
            var mr = shadowObj.AddComponent<MeshRenderer>();
            mf.sharedMesh = meshData.shadowMesh;
            mr.sharedMaterial = resourceForestTreeShadowMaterial != null
                ? resourceForestTreeShadowMaterial
                : treeShadowMaterial;
        }
    }

    private void CreateGroundGrassForChunk(GameObject chunkObject, int startX, int startZ)
    {
        if (groundGrassSettings == null || !groundGrassSettings.enabled)
        {
            return;
        }

        if (groundGrassMaterial == null)
        {
            Debug.LogWarning("Ground grass material is missing.");
            return;
        }

        Mesh groundGrassMesh = GroundGrassChunkGenerator.GenerateGroundGrassMesh(
            worldData,
            startX,
            startZ,
            chunkSize,
            seed,
            groundGrassSettings
        );

        if (groundGrassMesh == null || groundGrassMesh.vertexCount == 0)
        {
            return;
        }

        GameObject groundGrassObject = new GameObject("Ground Grass");
        groundGrassObject.transform.SetParent(chunkObject.transform, false);
        groundGrassObject.transform.localPosition = Vector3.zero;
        groundGrassObject.transform.localRotation = Quaternion.identity;
        groundGrassObject.transform.localScale = Vector3.one;

        MeshFilter meshFilter = groundGrassObject.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = groundGrassObject.AddComponent<MeshRenderer>();

        meshFilter.sharedMesh = groundGrassMesh;
        meshRenderer.sharedMaterial = groundGrassMaterial;
    }

    private void CreateGrassForChunk(GameObject chunkObject, int startX, int startZ)
    {
        if (grassSettings == null || !grassSettings.enabled)
        {
            return;
        }

        if (grassMaterial == null)
        {
            Debug.LogWarning("Grass material is missing.");
            return;
        }

        Mesh grassMesh = GrassChunkGenerator.GenerateGrassMesh(
            worldData,
            startX,
            startZ,
            chunkSize,
            seed,
            grassSettings
        );

        if (grassMesh == null || grassMesh.vertexCount == 0)
        {
            return;
        }

        GameObject grassObject = new GameObject("Grass");
        grassObject.transform.SetParent(chunkObject.transform, false);
        grassObject.transform.localPosition = Vector3.zero;
        grassObject.transform.localRotation = Quaternion.identity;
        grassObject.transform.localScale = Vector3.one;

        MeshFilter grassMeshFilter = grassObject.AddComponent<MeshFilter>();
        MeshRenderer grassMeshRenderer = grassObject.AddComponent<MeshRenderer>();

        grassMeshFilter.sharedMesh = grassMesh;
        grassMeshRenderer.sharedMaterial = grassMaterial;
    }

    private void CreateTreesForChunk(GameObject chunkObject, int startX, int startZ)
    {
        if (treeSettings == null || !treeSettings.enabled)
        {
            return;
        }

        if (treeMaterial == null)
        {
            Debug.LogWarning("Tree material is missing.");
            return;
        }

        TreeChunkMeshes meshes = TreeChunkGenerator.GenerateTreeMeshes(
            worldData,
            startX,
            startZ,
            chunkSize,
            seed,
            treeSettings
        );

        if (meshes.shadowMesh != null && meshes.shadowMesh.vertexCount > 0)
        {
            GameObject shadowObject = new GameObject("Tree Shadows");
            shadowObject.transform.SetParent(chunkObject.transform, false);
            shadowObject.transform.localPosition = Vector3.zero;
            shadowObject.transform.localRotation = Quaternion.identity;
            shadowObject.transform.localScale = Vector3.one;

            MeshFilter shadowFilter = shadowObject.AddComponent<MeshFilter>();
            MeshRenderer shadowRenderer = shadowObject.AddComponent<MeshRenderer>();

            shadowFilter.sharedMesh = meshes.shadowMesh;

            if (treeShadowMaterial != null)
            {
                shadowRenderer.sharedMaterial = treeShadowMaterial;
            }
        }

        if (meshes.treeMesh != null && meshes.treeMesh.vertexCount > 0)
        {
            GameObject treeObject = new GameObject("Trees");
            treeObject.transform.SetParent(chunkObject.transform, false);
            treeObject.transform.localPosition = Vector3.zero;
            treeObject.transform.localRotation = Quaternion.identity;
            treeObject.transform.localScale = Vector3.one;

            MeshFilter treeFilter = treeObject.AddComponent<MeshFilter>();
            MeshRenderer treeRenderer = treeObject.AddComponent<MeshRenderer>();
            MeshCollider treeCollider = treeObject.AddComponent<MeshCollider>();

            treeFilter.sharedMesh = meshes.treeMesh;
            treeRenderer.sharedMaterial = treeMaterial;
            treeCollider.sharedMesh = meshes.treeMesh;

            if (treesBlockNavigation)
            {
                MarkObjectAsNotWalkable(treeObject);
            }
        }
    }

    private void CreateRocksForChunk(GameObject chunkObject, int startX, int startZ)
    {
        if (rockSettings == null || !rockSettings.enabled)
        {
            return;
        }

        if (rockMaterial == null)
        {
            Debug.LogWarning("Rock material is missing.");
            return;
        }

        RockChunkMeshes meshes = RockChunkGenerator.GenerateRockMeshes(
            worldData,
            startX,
            startZ,
            chunkSize,
            seed,
            rockSettings
        );

        if (meshes.shadowMesh != null && meshes.shadowMesh.vertexCount > 0)
        {
            GameObject shadowObject = new GameObject("Rock Shadows");
            shadowObject.transform.SetParent(chunkObject.transform, false);
            shadowObject.transform.localPosition = Vector3.zero;
            shadowObject.transform.localRotation = Quaternion.identity;
            shadowObject.transform.localScale = Vector3.one;

            MeshFilter shadowFilter = shadowObject.AddComponent<MeshFilter>();
            MeshRenderer shadowRenderer = shadowObject.AddComponent<MeshRenderer>();

            shadowFilter.sharedMesh = meshes.shadowMesh;

            if (rockShadowMaterial != null)
            {
                shadowRenderer.sharedMaterial = rockShadowMaterial;
            }
        }

        if (meshes.rockMesh != null && meshes.rockMesh.vertexCount > 0)
        {
            GameObject rockObject = new GameObject("Rocks");
            rockObject.transform.SetParent(chunkObject.transform, false);
            rockObject.transform.localPosition = Vector3.zero;
            rockObject.transform.localRotation = Quaternion.identity;
            rockObject.transform.localScale = Vector3.one;

            MeshFilter rockFilter = rockObject.AddComponent<MeshFilter>();
            MeshRenderer rockRenderer = rockObject.AddComponent<MeshRenderer>();
            MeshCollider rockCollider = rockObject.AddComponent<MeshCollider>();

            rockFilter.sharedMesh = meshes.rockMesh;
            rockRenderer.sharedMaterial = rockMaterial;
            rockCollider.sharedMesh = meshes.rockMesh;

            if (rocksBlockNavigation)
            {
                MarkObjectAsNotWalkable(rockObject);
            }
        }
    }

    private void CreatePickupResourcesForChunk(
        GameObject chunkObject,
        Vector2Int chunkCoord,
        int startX,
        int startZ
    )
    {
        CleanupMissingResourceItems();

        if ((pickupStickSpawnSettings == null || !pickupStickSpawnSettings.enabled) &&
            (pickupRockSpawnSettings == null || !pickupRockSpawnSettings.enabled))
        {
            return;
        }

        GameObject pickupRoot = new GameObject("Pickup Resources");
        pickupRoot.transform.SetParent(chunkObject.transform, false);
        pickupRoot.transform.localPosition = Vector3.zero;
        pickupRoot.transform.localRotation = Quaternion.identity;
        pickupRoot.transform.localScale = Vector3.one;

        if (pickupStickSpawnSettings != null && pickupStickSpawnSettings.enabled)
        {
            CreateStickPickupsForChunk(pickupRoot.transform, chunkCoord, startX, startZ);
        }

        if (pickupRockSpawnSettings != null && pickupRockSpawnSettings.enabled)
        {
            CreateRockPickupsForChunk(pickupRoot.transform, chunkCoord, startX, startZ);
        }
    }

    private void CreateStickPickupsForChunk(
        Transform pickupRoot,
        Vector2Int chunkCoord,
        int startX,
        int startZ
    )
    {
        if (resourceForestTreeSettings == null || !resourceForestTreeSettings.enabled)
        {
            return;
        }

        List<ResourceForestTreeInstance> treeInstances =
            ResourceForestTreeChunkGenerator.GenerateTreeInstances(
                worldData,
                startX,
                startZ,
                chunkSize,
                seed,
                resourceForestTreeSettings
            );

        for (int treeIndex = 0; treeIndex < treeInstances.Count; treeIndex++)
        {
            ResourceForestTreeInstance treeInstance = treeInstances[treeIndex];
            System.Random random = new System.Random(
                seed ^
                chunkCoord.x * 73856093 ^
                chunkCoord.y * 19349663 ^
                treeIndex * 83492791
            );

            if ((float)random.NextDouble() > pickupStickSpawnSettings.treeDropChance)
            {
                continue;
            }

            int stickCount = GetWeightedStickCount(random);
            float treeWorldX = startX + treeInstance.localPosition.x;
            float treeWorldZ = startZ + treeInstance.localPosition.z;

            for (int stickIndex = 0; stickIndex < stickCount; stickIndex++)
            {
                string resourceId = $"stick_{chunkCoord.x}_{chunkCoord.y}_{treeIndex}_{stickIndex}";
                if (!CanSpawnResourceId(resourceId))
                {
                    continue;
                }

                for (int attempt = 0; attempt < 6; attempt++)
                {
                    float angle = (float)random.NextDouble() * Mathf.PI * 2f;
                    float distance = Mathf.Lerp(
                        pickupStickSpawnSettings.minDistanceFromTree,
                        pickupStickSpawnSettings.maxDistanceFromTree,
                        (float)random.NextDouble()
                    );

                    float worldX = treeWorldX + Mathf.Cos(angle) * distance;
                    float worldZ = treeWorldZ + Mathf.Sin(angle) * distance;

                    if (!TryGetLocalGroundPosition(
                            startX,
                            startZ,
                            worldX,
                            worldZ,
                            TerrainZone.Resource,
                            pickupStickSpawnSettings.yOffset,
                            pickupStickSpawnSettings.maxSlopeAngle,
                            out Vector3 localPosition))
                    {
                        continue;
                    }

                    Quaternion localRotation = Quaternion.Euler(
                        0f,
                        (float)random.NextDouble() * 360f,
                        Mathf.Lerp(-18f, 18f, (float)random.NextDouble())
                    );

                    PickupableItem item = PickupableItemFactory.CreateStick(
                        pickupRoot,
                        localPosition,
                        localRotation
                    );
                    RegisterResourceItem(resourceId, item);
                    break;
                }
            }
        }
    }

    private void CreateRockPickupsForChunk(
        Transform pickupRoot,
        Vector2Int chunkCoord,
        int startX,
        int startZ
    )
    {
        System.Random random = new System.Random(
            seed ^
            startX * 83492791 ^
            startZ * 297121507
        );

        for (int z = 0; z <= chunkSize; z += pickupRockSpawnSettings.spacing)
        {
            for (int x = 0; x <= chunkSize; x += pickupRockSpawnSettings.spacing)
            {
                int worldX = startX + x;
                int worldZ = startZ + z;

                if (!worldData.IsInsideMap(worldX, worldZ))
                {
                    continue;
                }

                TerrainZone zone = worldData.GetZone(worldX, worldZ);
                float density = GetPickupRockDensity(zone);
                if (density <= 0f || (float)random.NextDouble() > density)
                {
                    continue;
                }

                string resourceId = $"rock_{chunkCoord.x}_{chunkCoord.y}_{x}_{z}";
                if (!CanSpawnResourceId(resourceId))
                {
                    continue;
                }

                float jitterRange = pickupRockSpawnSettings.spacing * 0.42f;
                float finalWorldX = worldX + Mathf.Lerp(
                    -jitterRange,
                    jitterRange,
                    (float)random.NextDouble()
                );
                float finalWorldZ = worldZ + Mathf.Lerp(
                    -jitterRange,
                    jitterRange,
                    (float)random.NextDouble()
                );

                if (!TryGetLocalGroundPosition(
                        startX,
                        startZ,
                        finalWorldX,
                        finalWorldZ,
                        null,
                        pickupRockSpawnSettings.yOffset,
                        pickupRockSpawnSettings.maxSlopeAngle,
                        out Vector3 localPosition))
                {
                    continue;
                }

                float baseScale = Mathf.Lerp(
                    pickupRockSpawnSettings.minVisualScale,
                    pickupRockSpawnSettings.maxVisualScale,
                    (float)random.NextDouble()
                );
                Vector3 visualScale = new Vector3(
                    baseScale * Mathf.Lerp(0.85f, 1.18f, (float)random.NextDouble()),
                    baseScale * Mathf.Lerp(0.8f, 1.08f, (float)random.NextDouble()),
                    baseScale * Mathf.Lerp(0.85f, 1.18f, (float)random.NextDouble())
                );

                Quaternion localRotation = Quaternion.Euler(
                    (float)random.NextDouble() * 360f,
                    (float)random.NextDouble() * 360f,
                    (float)random.NextDouble() * 360f
                );

                PickupableItem item = PickupableItemFactory.CreateRock(
                    pickupRoot,
                    localPosition,
                    localRotation,
                    visualScale
                );
                RegisterResourceItem(resourceId, item);
            }
        }
    }

    private bool TryGetLocalGroundPosition(
        int startX,
        int startZ,
        float worldX,
        float worldZ,
        TerrainZone? requiredZone,
        float yOffset,
        float maxSlopeAngle,
        out Vector3 localPosition)
    {
        int sampleX = Mathf.RoundToInt(worldX);
        int sampleZ = Mathf.RoundToInt(worldZ);

        localPosition = Vector3.zero;

        if (!worldData.IsInsideMap(sampleX, sampleZ))
        {
            return false;
        }

        if (requiredZone.HasValue && worldData.GetZone(sampleX, sampleZ) != requiredZone.Value)
        {
            return false;
        }

        if (!requiredZone.HasValue && worldData.GetZone(sampleX, sampleZ) == TerrainZone.Border)
        {
            return false;
        }

        if (IsTerrainTooSteep(worldData, sampleX, sampleZ, maxSlopeAngle))
        {
            return false;
        }

        localPosition = new Vector3(
            worldX - startX,
            worldData.GetHeight(sampleX, sampleZ) + yOffset,
            worldZ - startZ
        );
        return true;
    }

    private void RegisterResourceItem(string resourceId, PickupableItem item)
    {
        if (item == null)
        {
            return;
        }

        item.RemovedFromWorldSupply -= HandleResourceRemovedFromWorldSupply;
        item.RemovedFromWorldSupply += HandleResourceRemovedFromWorldSupply;

        activeResourceItems[resourceId] = item;
        resourceIdsByItem[item] = resourceId;
    }

    private void HandleResourceRemovedFromWorldSupply(PickupableItem item)
    {
        if (item == null)
        {
            return;
        }

        item.RemovedFromWorldSupply -= HandleResourceRemovedFromWorldSupply;

        if (resourceIdsByItem.TryGetValue(item, out string resourceId))
        {
            activeResourceItems.Remove(resourceId);
            resourceIdsByItem.Remove(item);
            removedResourceIds.Add(resourceId);
        }
    }

    private void CleanupMissingResourceItems()
    {
        resourceIdsToCleanup.Clear();

        foreach (KeyValuePair<string, PickupableItem> pair in activeResourceItems)
        {
            if (pair.Value == null)
            {
                resourceIdsToCleanup.Add(pair.Key);
            }
        }

        for (int i = 0; i < resourceIdsToCleanup.Count; i++)
        {
            activeResourceItems.Remove(resourceIdsToCleanup[i]);
        }

        List<PickupableItem> itemKeys = new List<PickupableItem>(resourceIdsByItem.Keys);
        for (int i = 0; i < itemKeys.Count; i++)
        {
            if (itemKeys[i] == null)
            {
                resourceIdsByItem.Remove(itemKeys[i]);
            }
        }
    }

    private bool CanSpawnResourceId(string resourceId)
    {
        if (string.IsNullOrEmpty(resourceId))
        {
            return false;
        }

        if (removedResourceIds.Contains(resourceId))
        {
            return false;
        }

        return !activeResourceItems.TryGetValue(resourceId, out PickupableItem existingItem) || existingItem == null;
    }

    private float GetPickupRockDensity(TerrainZone zone)
    {
        switch (zone)
        {
            case TerrainZone.Arena:
                return pickupRockSpawnSettings.arenaDensity;
            case TerrainZone.Transition:
                return pickupRockSpawnSettings.transitionDensity;
            case TerrainZone.Resource:
                return pickupRockSpawnSettings.resourceDensity;
            default:
                return 0f;
        }
    }

    private int GetWeightedStickCount(System.Random random)
    {
        float roll = (float)random.NextDouble();
        float oneThreshold = pickupStickSpawnSettings.oneStickWeight;
        float twoThreshold = oneThreshold + pickupStickSpawnSettings.twoStickWeight;

        if (roll < oneThreshold)
        {
            return 1;
        }

        if (roll < twoThreshold)
        {
            return 2;
        }

        return 3;
    }

    private PickupableWeapon ResolveCraftedSpearPrefab()
    {
        if (craftedSpearPrefab != null)
        {
            return craftedSpearPrefab;
        }

        if (cachedFallbackSpearPrefab != null)
        {
            return cachedFallbackSpearPrefab;
        }

        SpearTestSpawner spearSpawner = FindAnyObjectByType<SpearTestSpawner>();
        if (spearSpawner != null && spearSpawner.SpearPrefab != null)
        {
            cachedFallbackSpearPrefab = spearSpawner.SpearPrefab;
            return cachedFallbackSpearPrefab;
        }

        cachedFallbackSpearPrefab = FindAnyObjectByType<PickupableWeapon>();
        return cachedFallbackSpearPrefab;
    }

    private static bool IsTerrainTooSteep(WorldData data, int x, int z, float maxSlopeAngle)
    {
        float center = data.GetHeight(x, z);
        float right = data.GetHeight(x + 1, z);
        float forward = data.GetHeight(x, z + 1);

        Vector3 dx = new Vector3(1f, right - center, 0f);
        Vector3 dz = new Vector3(0f, forward - center, 1f);
        Vector3 normal = Vector3.Cross(dz, dx).normalized;

        return Vector3.Angle(normal, Vector3.up) > maxSlopeAngle;
    }

    private void CreateVegetationForChunk(GameObject chunkObject, int startX, int startZ)
    {
        if (vegetationSettings == null || !vegetationSettings.enabled)
        {
            return;
        }

        if (vegetationMaterial == null)
        {
            Debug.LogWarning("Vegetation material is missing.");
            return;
        }

        VegetationChunkMeshes meshes = VegetationChunkGenerator.GenerateVegetationMeshes(
            worldData,
            startX,
            startZ,
            chunkSize,
            seed,
            vegetationSettings
        );

        if (meshes.shadowMesh != null && meshes.shadowMesh.vertexCount > 0)
        {
            GameObject shadowObject = new GameObject("Vegetation Shadows");
            shadowObject.transform.SetParent(chunkObject.transform, false);
            shadowObject.transform.localPosition = Vector3.zero;
            shadowObject.transform.localRotation = Quaternion.identity;
            shadowObject.transform.localScale = Vector3.one;

            MeshFilter shadowFilter = shadowObject.AddComponent<MeshFilter>();
            MeshRenderer shadowRenderer = shadowObject.AddComponent<MeshRenderer>();

            shadowFilter.sharedMesh = meshes.shadowMesh;

            if (vegetationShadowMaterial != null)
            {
                shadowRenderer.sharedMaterial = vegetationShadowMaterial;
            }
        }

        if (meshes.vegetationMesh != null && meshes.vegetationMesh.vertexCount > 0)
        {
            GameObject vegetationObject = new GameObject("Extra Vegetation");
            vegetationObject.transform.SetParent(chunkObject.transform, false);
            vegetationObject.transform.localPosition = Vector3.zero;
            vegetationObject.transform.localRotation = Quaternion.identity;
            vegetationObject.transform.localScale = Vector3.one;

            MeshFilter vegetationFilter = vegetationObject.AddComponent<MeshFilter>();
            MeshRenderer vegetationRenderer = vegetationObject.AddComponent<MeshRenderer>();

            vegetationFilter.sharedMesh = meshes.vegetationMesh;
            vegetationRenderer.sharedMaterial = vegetationMaterial;
        }
    }

    private void CreateDeadTreesForChunk(GameObject chunkObject, int startX, int startZ)
    {
        if (deadTreeSettings == null || !deadTreeSettings.enabled)
        {
            return;
        }

        if (deadTreeMaterial == null)
        {
            Debug.LogWarning("Dead tree material is missing.");
            return;
        }

        DeadTreeChunkMeshes meshes = DeadTreeChunkGenerator.GenerateDeadTreeMeshes(
            worldData,
            startX,
            startZ,
            chunkSize,
            seed,
            deadTreeSettings
        );

        if (meshes.shadowMesh != null && meshes.shadowMesh.vertexCount > 0)
        {
            GameObject shadowObject = new GameObject("Dead Tree Shadows");
            shadowObject.transform.SetParent(chunkObject.transform, false);
            shadowObject.transform.localPosition = Vector3.zero;
            shadowObject.transform.localRotation = Quaternion.identity;
            shadowObject.transform.localScale = Vector3.one;

            MeshFilter shadowFilter = shadowObject.AddComponent<MeshFilter>();
            MeshRenderer shadowRenderer = shadowObject.AddComponent<MeshRenderer>();

            shadowFilter.sharedMesh = meshes.shadowMesh;

            if (deadTreeShadowMaterial != null)
            {
                shadowRenderer.sharedMaterial = deadTreeShadowMaterial;
            }
        }

        if (meshes.deadTreeMesh != null && meshes.deadTreeMesh.vertexCount > 0)
        {
            GameObject deadTreeObject = new GameObject("Dead Trees");
            deadTreeObject.transform.SetParent(chunkObject.transform, false);
            deadTreeObject.transform.localPosition = Vector3.zero;
            deadTreeObject.transform.localRotation = Quaternion.identity;
            deadTreeObject.transform.localScale = Vector3.one;

            MeshFilter deadTreeFilter = deadTreeObject.AddComponent<MeshFilter>();
            MeshRenderer deadTreeRenderer = deadTreeObject.AddComponent<MeshRenderer>();
            MeshCollider deadTreeCollider = deadTreeObject.AddComponent<MeshCollider>();

            deadTreeFilter.sharedMesh = meshes.deadTreeMesh;
            deadTreeRenderer.sharedMaterial = deadTreeMaterial;
            deadTreeCollider.sharedMesh = meshes.deadTreeMesh;

            if (deadTreesBlockNavigation)
            {
                MarkObjectAsNotWalkable(deadTreeObject);
            }
        }
    }

    private void SpawnCave()
    {
        if (cavePrefab == null || worldData == null)
        {
            return;
        }

        Vector2Int cavePosition = worldData.cavePosition;
        float caveHeight = worldData.GetHeight(cavePosition.x, cavePosition.y);

        caveInstance = Instantiate(
            cavePrefab,
            new Vector3(cavePosition.x, caveHeight, cavePosition.y),
            Quaternion.identity
        );

        caveInstance.name = "Cave Entrance";
    }

    public Vector3 GetArenaCenterWorldPosition(float heightOffset = 0.1f)
    {
        if (worldData == null)
        {
            return transform.position + Vector3.up * heightOffset;
        }

        Vector2Int center = worldData.arenaCenter;
        float height = worldData.GetHeight(center.x, center.y);
        return new Vector3(center.x, height + heightOffset, center.y);
    }

    public bool TryGetRandomSpawnPosition(
        TerrainZone zone,
        out Vector3 spawnPosition,
        float heightOffset = 0.75f,
        int attempts = 60)
    {
        if (worldData == null)
        {
            spawnPosition = Vector3.zero;
            return false;
        }

        for (int i = 0; i < attempts; i++)
        {
            int x = Random.Range(0, worldData.size + 1);
            int z = Random.Range(0, worldData.size + 1);

            if (worldData.GetZone(x, z) != zone)
            {
                continue;
            }

            spawnPosition = new Vector3(x, worldData.GetHeight(x, z) + heightOffset, z);
            return true;
        }

        spawnPosition = Vector3.zero;
        return false;
    }

    public bool TryGetNearbySpawnPosition(
        Vector3 nearPosition,
        TerrainZone zone,
        float radius,
        out Vector3 spawnPosition,
        float heightOffset = 0.75f,
        int attempts = 40)
    {
        if (worldData == null)
        {
            spawnPosition = Vector3.zero;
            return false;
        }

        for (int i = 0; i < attempts; i++)
        {
            Vector2 offset = Random.insideUnitCircle * radius;
            int x = Mathf.RoundToInt(nearPosition.x + offset.x);
            int z = Mathf.RoundToInt(nearPosition.z + offset.y);

            if (!worldData.IsInsideMap(x, z) || worldData.GetZone(x, z) != zone)
            {
                continue;
            }

            spawnPosition = new Vector3(x, worldData.GetHeight(x, z) + heightOffset, z);
            return true;
        }

        return TryGetRandomSpawnPosition(zone, out spawnPosition, heightOffset, attempts);
    }

    public bool TryGetRandomNavMeshSpawnPosition(
        TerrainZone zone,
        out Vector3 spawnPosition,
        float sampleRadius = 12f,
        int attempts = 120)
    {
        spawnPosition = Vector3.zero;

        if (worldData == null)
        {
            return false;
        }

        for (int i = 0; i < attempts; i++)
        {
            int x = Random.Range(0, worldData.size + 1);
            int z = Random.Range(0, worldData.size + 1);

            if (worldData.GetZone(x, z) != zone)
            {
                continue;
            }

            Vector3 terrainPosition = new Vector3(x, worldData.GetHeight(x, z), z);

            if (NavMesh.SamplePosition(terrainPosition, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
            {
                spawnPosition = hit.position;
                return true;
            }
        }

        return false;
    }

    public bool TryGetNearbyNavMeshSpawnPosition(
        Vector3 nearPosition,
        TerrainZone zone,
        float radius,
        out Vector3 spawnPosition,
        float sampleRadius = 12f,
        int attempts = 120)
    {
        spawnPosition = Vector3.zero;

        if (worldData == null)
        {
            return false;
        }

        for (int i = 0; i < attempts; i++)
        {
            Vector2 offset = Random.insideUnitCircle * radius;
            int x = Mathf.RoundToInt(nearPosition.x + offset.x);
            int z = Mathf.RoundToInt(nearPosition.z + offset.y);

            if (!worldData.IsInsideMap(x, z))
            {
                continue;
            }

            if (worldData.GetZone(x, z) != zone)
            {
                continue;
            }

            Vector3 terrainPosition = new Vector3(x, worldData.GetHeight(x, z), z);

            if (NavMesh.SamplePosition(terrainPosition, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
            {
                spawnPosition = hit.position;
                return true;
            }
        }

        return TryGetRandomNavMeshSpawnPosition(zone, out spawnPosition, sampleRadius, attempts);
    }

    public bool TryGetZoneAtWorldPosition(Vector3 worldPosition, out TerrainZone zone)
    {
        zone = TerrainZone.Border;

        if (worldData == null)
        {
            return false;
        }

        int x = Mathf.RoundToInt(worldPosition.x);
        int z = Mathf.RoundToInt(worldPosition.z);

        if (!worldData.IsInsideMap(x, z))
        {
            return false;
        }

        zone = worldData.GetZone(x, z);
        return true;
    }

    public bool TryGetGroundHeightAtWorldPosition(Vector3 worldPosition, out float height)
    {
        height = 0f;

        if (worldData == null)
        {
            return false;
        }

        int x = Mathf.RoundToInt(worldPosition.x);
        int z = Mathf.RoundToInt(worldPosition.z);

        if (!worldData.IsInsideMap(x, z))
        {
            return false;
        }

        height = worldData.GetHeight(x, z);
        return true;
    }

}
