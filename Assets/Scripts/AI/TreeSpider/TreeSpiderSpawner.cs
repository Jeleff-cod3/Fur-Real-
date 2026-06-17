using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class TreeSpiderSpawner : MonoBehaviour
{
    [Header("Prefab")]
    [SerializeField] private GameObject spiderPrefab;

    [Header("Population")]
    [SerializeField] private float spidersPerResourcePlayer = 1.5f;
    [SerializeField] private int minimumSpidersWhenOccupied = 1;
    [SerializeField] private int maxAliveSpiders = 10;
    [SerializeField] private float spawnCheckInterval = 2f;

    private readonly List<TreeSpiderBrain> activeSpiders = new List<TreeSpiderBrain>();
    private readonly List<Transform> playerBuffer = new List<Transform>();

    private WorldChunkRenderer worldChunkRenderer;
    private ResourceForestTreeAnchorRegistry treeRegistry;
    private GameObject fallbackTemplate;
    private float nextSpawnCheckTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (FindAnyObjectByType<TreeSpiderSpawner>() != null)
        {
            return;
        }

        GameObject bootstrap = new GameObject("TreeSpiderSpawner");
        bootstrap.AddComponent<TreeSpiderSpawner>();
    }

    private IEnumerator Start()
    {
        while (worldChunkRenderer == null)
        {
            worldChunkRenderer = FindAnyObjectByType<WorldChunkRenderer>();
            yield return null;
        }

        treeRegistry = worldChunkRenderer.GetComponent<ResourceForestTreeAnchorRegistry>();
        if (treeRegistry == null)
        {
            treeRegistry = worldChunkRenderer.gameObject.AddComponent<ResourceForestTreeAnchorRegistry>();
        }

        while (!treeRegistry.IsReady)
        {
            yield return null;
        }
    }

    private void Update()
    {
        if (worldChunkRenderer == null || treeRegistry == null || !treeRegistry.IsReady)
        {
            return;
        }

        if (Time.time < nextSpawnCheckTime)
        {
            return;
        }

        nextSpawnCheckTime = Time.time + spawnCheckInterval;
        CleanupDestroyedSpiders();

        int resourcePlayers = CountPlayersInResourceArea();
        int targetPopulation = resourcePlayers <= 0
            ? 0
            : Mathf.Clamp(
                Mathf.CeilToInt(resourcePlayers * spidersPerResourcePlayer),
                minimumSpidersWhenOccupied,
                maxAliveSpiders);

        while (activeSpiders.Count < targetPopulation)
        {
            if (!SpawnSpider())
            {
                break;
            }
        }

        if (activeSpiders.Count <= targetPopulation)
        {
            return;
        }

        int extrasToRemove = activeSpiders.Count - targetPopulation;
        for (int i = activeSpiders.Count - 1; i >= 0 && extrasToRemove > 0; i--)
        {
            TreeSpiderBrain spider = activeSpiders[i];
            if (spider == null || !spider.CanBeDespawnedSilently())
            {
                continue;
            }

            spider.ForceDespawn();
            activeSpiders.RemoveAt(i);
            extrasToRemove--;
        }
    }

    private int CountPlayersInResourceArea()
    {
        int count = 0;
        MultiplayerPrototype.GetActivePlayerTransforms(playerBuffer);

        foreach (Transform playerTransform in playerBuffer)
        {
            if (playerTransform == null)
            {
                continue;
            }

            if (!worldChunkRenderer.TryGetZoneAtWorldPosition(playerTransform.position, out TerrainZone zone))
            {
                continue;
            }

            if (zone == TerrainZone.Resource)
            {
                count++;
            }
        }

        return count;
    }

    private bool SpawnSpider()
    {
        if (!treeRegistry.TryReserveRandomAvailableAnchor(this, out int treeIndex, out ResourceForestTreeAnchor anchor))
        {
            return false;
        }

        GameObject prefabToUse = spiderPrefab != null ? spiderPrefab : GetOrCreateFallbackTemplate();
        if (prefabToUse == null)
        {
            treeRegistry.ReleaseAnchor(treeIndex, this);
            return false;
        }

        GameObject spiderObject = Instantiate(prefabToUse, anchor.hidePosition, Quaternion.identity);
        spiderObject.name = "TreeSpider";
        spiderObject.SetActive(true);

        TreeSpiderBrain brain = spiderObject.GetComponent<TreeSpiderBrain>();
        if (brain == null)
        {
            brain = spiderObject.AddComponent<TreeSpiderBrain>();
        }

        EnsureSpiderComponents(spiderObject);
        treeRegistry.ReleaseAnchor(treeIndex, this);

        if (!treeRegistry.TryReserveAnchor(treeIndex, brain, out ResourceForestTreeAnchor reservedAnchor))
        {
            Destroy(spiderObject);
            return false;
        }

        brain.InitializeInTree(treeRegistry, treeIndex, reservedAnchor);
        activeSpiders.Add(brain);
        return true;
    }

    private GameObject GetOrCreateFallbackTemplate()
    {
        if (fallbackTemplate != null)
        {
            return fallbackTemplate;
        }

        GameObject template = GameObject.CreatePrimitive(PrimitiveType.Cube);
        template.name = "TreeSpiderRuntimeTemplate";
        template.transform.localScale = new Vector3(1.25f, 0.55f, 1.25f);

        Transform mouth = new GameObject("Mouth").transform;
        mouth.SetParent(template.transform, false);
        mouth.localPosition = new Vector3(0f, 0.1f, 0.8f);

        Rigidbody body = template.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.isKinematic = true;

        NavMeshAgent agent = template.AddComponent<NavMeshAgent>();
        agent.radius = 0.45f;
        agent.height = 0.8f;
        agent.baseOffset = 0f;
        agent.angularSpeed = 720f;
        agent.acceleration = 30f;
        agent.speed = 5.8f;

        EnsureSpiderComponents(template);
        template.SetActive(false);
        fallbackTemplate = template;
        DontDestroyOnLoad(fallbackTemplate);
        return fallbackTemplate;
    }

    private static void EnsureSpiderComponents(GameObject spiderObject)
    {
        if (spiderObject.GetComponent<TreeSpiderState>() == null)
        {
            spiderObject.AddComponent<TreeSpiderState>();
        }

        if (spiderObject.GetComponent<TreeSpiderSenses>() == null)
        {
            spiderObject.AddComponent<TreeSpiderSenses>();
        }

        if (spiderObject.GetComponent<TreeSpiderMovement>() == null)
        {
            spiderObject.AddComponent<TreeSpiderMovement>();
        }

        if (spiderObject.GetComponent<TreeSpiderCombat>() == null)
        {
            spiderObject.AddComponent<TreeSpiderCombat>();
        }

        if (spiderObject.GetComponent<TreeSpiderHealth>() == null)
        {
            spiderObject.AddComponent<TreeSpiderHealth>();
        }
    }

    private void CleanupDestroyedSpiders()
    {
        activeSpiders.RemoveAll(spider => spider == null);
    }
}
