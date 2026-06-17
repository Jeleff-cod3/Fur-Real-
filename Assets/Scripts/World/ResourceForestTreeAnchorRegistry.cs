using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ResourceForestTreeAnchorRegistry : MonoBehaviour
{
    private readonly List<ResourceForestTreeAnchor> anchors = new List<ResourceForestTreeAnchor>();
    private readonly Dictionary<int, Object> reservations = new Dictionary<int, Object>();

    private WorldChunkRenderer worldChunkRenderer;
    private bool buildAttempted;

    public bool IsReady { get; private set; }
    public int AnchorCount => anchors.Count;

    private void Awake()
    {
        worldChunkRenderer = GetComponent<WorldChunkRenderer>();
        if (worldChunkRenderer == null)
        {
            worldChunkRenderer = FindAnyObjectByType<WorldChunkRenderer>();
        }
    }

    private void Start()
    {
        StartCoroutine(BuildWhenWorldReady());
    }

    public bool TryGetAnchor(int index, out ResourceForestTreeAnchor anchor)
    {
        if (index >= 0 && index < anchors.Count)
        {
            anchor = anchors[index];
            return true;
        }

        anchor = default;
        return false;
    }

    public bool TryReserveRandomAvailableAnchor(Object owner, out int anchorIndex, out ResourceForestTreeAnchor anchor)
    {
        anchorIndex = -1;
        anchor = default;

        if (!IsReady || owner == null)
        {
            return false;
        }

        List<int> candidates = ListPool<int>.Get();

        try
        {
            for (int i = 0; i < anchors.Count; i++)
            {
                if (IsAnchorAvailable(i))
                {
                    candidates.Add(i);
                }
            }

            if (candidates.Count == 0)
            {
                return false;
            }

            int chosenIndex = candidates[Random.Range(0, candidates.Count)];
            reservations[chosenIndex] = owner;
            anchorIndex = chosenIndex;
            anchor = anchors[chosenIndex];
            return true;
        }
        finally
        {
            ListPool<int>.Release(candidates);
        }
    }

    public bool TryReserveNearestAvailableAnchor(
        Vector3 nearPosition,
        Object owner,
        out int anchorIndex,
        out ResourceForestTreeAnchor anchor)
    {
        anchorIndex = -1;
        anchor = default;

        if (!IsReady || owner == null)
        {
            return false;
        }

        float closestDistanceSqr = float.PositiveInfinity;
        int closestIndex = -1;

        for (int i = 0; i < anchors.Count; i++)
        {
            if (!IsAnchorAvailable(i))
            {
                continue;
            }

            float distanceSqr = (anchors[i].trunkBasePosition - nearPosition).sqrMagnitude;
            if (distanceSqr >= closestDistanceSqr)
            {
                continue;
            }

            closestDistanceSqr = distanceSqr;
            closestIndex = i;
        }

        if (closestIndex < 0)
        {
            return false;
        }

        reservations[closestIndex] = owner;
        anchorIndex = closestIndex;
        anchor = anchors[closestIndex];
        return true;
    }

    public bool TryReserveAnchor(int anchorIndex, Object owner, out ResourceForestTreeAnchor anchor)
    {
        anchor = default;

        if (!IsReady || owner == null)
        {
            return false;
        }

        if (anchorIndex < 0 || anchorIndex >= anchors.Count || !IsAnchorAvailable(anchorIndex))
        {
            return false;
        }

        reservations[anchorIndex] = owner;
        anchor = anchors[anchorIndex];
        return true;
    }

    public void ReleaseAnchor(int anchorIndex, Object owner)
    {
        if (anchorIndex < 0)
        {
            return;
        }

        if (!reservations.TryGetValue(anchorIndex, out Object reservedBy))
        {
            return;
        }

        if (owner != null && reservedBy != owner)
        {
            return;
        }

        reservations.Remove(anchorIndex);
    }

    private IEnumerator BuildWhenWorldReady()
    {
        if (buildAttempted)
        {
            yield break;
        }

        buildAttempted = true;

        while (worldChunkRenderer == null)
        {
            worldChunkRenderer = FindAnyObjectByType<WorldChunkRenderer>();
            yield return null;
        }

        while (worldChunkRenderer.WorldData == null)
        {
            yield return null;
        }

        BuildAnchors();
    }

    private void BuildAnchors()
    {
        anchors.Clear();
        reservations.Clear();
        IsReady = false;

        WorldData worldData = worldChunkRenderer.WorldData;
        ResourceForestTreeSettings settings = worldChunkRenderer.ResourceForestSettings;

        if (worldData == null || settings == null || !settings.enabled)
        {
            return;
        }

        int mapSize = worldChunkRenderer.WorldMapSize;
        int chunkSize = worldChunkRenderer.ChunkWorldSize;
        int seed = worldChunkRenderer.WorldSeed;

        for (int startZ = 0; startZ < mapSize; startZ += chunkSize)
        {
            for (int startX = 0; startX < mapSize; startX += chunkSize)
            {
                ResourceForestTreePlacementUtility.AppendTreeAnchorsForChunk(
                    worldData,
                    startX,
                    startZ,
                    chunkSize,
                    seed,
                    settings,
                    anchors
                );
            }
        }

        IsReady = anchors.Count > 0;
        Debug.Log($"ResourceForestTreeAnchorRegistry built {anchors.Count} tree anchors.");
    }

    private bool IsAnchorAvailable(int index)
    {
        if (!reservations.TryGetValue(index, out Object owner))
        {
            return true;
        }

        if (owner != null)
        {
            return false;
        }

        reservations.Remove(index);
        return true;
    }

    private static class ListPool<T>
    {
        private static readonly Stack<List<T>> Pool = new Stack<List<T>>();

        public static List<T> Get()
        {
            return Pool.Count > 0 ? Pool.Pop() : new List<T>();
        }

        public static void Release(List<T> list)
        {
            if (list == null)
            {
                return;
            }

            list.Clear();
            Pool.Push(list);
        }
    }
}
