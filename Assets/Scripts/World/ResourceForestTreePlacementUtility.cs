using System.Collections.Generic;
using UnityEngine;

public struct ResourceForestTreeAnchor
{
    public Vector3 trunkBasePosition;
    public Vector3 hidePosition;
    public float groundY;
    public float trunkHeight;
    public float canopyRadius;
    public float detectionRadius;
    public float directDropRadius;
    public bool isPine;
}

public static class ResourceForestTreePlacementUtility
{
    public static void AppendTreeAnchorsForChunk(
        WorldData worldData,
        int startX,
        int startZ,
        int chunkSize,
        int seed,
        ResourceForestTreeSettings settings,
        List<ResourceForestTreeAnchor> anchors)
    {
        if (anchors == null || worldData == null || settings == null || !settings.enabled)
        {
            return;
        }

        System.Random random = new System.Random(
            seed ^
            startX * 73856093 ^
            startZ * 19349663
        );

        for (int z = 0; z <= chunkSize; z += settings.spacing)
        {
            for (int x = 0; x <= chunkSize; x += settings.spacing)
            {
                int worldX = startX + x;
                int worldZ = startZ + z;

                if (!worldData.IsInsideMap(worldX, worldZ))
                {
                    continue;
                }

                if (worldData.GetZone(worldX, worldZ) != TerrainZone.Resource)
                {
                    continue;
                }

                float largePatch = Mathf.PerlinNoise(
                    (worldX + seed * 11.7f) / settings.largePatchScale,
                    (worldZ - seed * 8.3f) / settings.largePatchScale
                );

                float smallPatch = Mathf.PerlinNoise(
                    (worldX - seed * 4.9f) / settings.smallPatchScale,
                    (worldZ + seed * 6.2f) / settings.smallPatchScale
                );

                float patchValue = Mathf.Lerp(largePatch, smallPatch, 0.42f);
                float patchMultiplier = Mathf.Lerp(
                    0.45f,
                    1.55f + settings.patchStrength * 0.22f,
                    Mathf.Pow(patchValue, 1.18f)
                );
                float density = Mathf.Clamp01(settings.resourceDensity * patchMultiplier);

                if ((float)random.NextDouble() > density)
                {
                    continue;
                }

                float jitterRange = settings.spacing * 0.45f;
                float jitterX = Mathf.Lerp(-jitterRange, jitterRange, (float)random.NextDouble());
                float jitterZ = Mathf.Lerp(-jitterRange, jitterRange, (float)random.NextDouble());

                float finalWorldX = worldX + jitterX;
                float finalWorldZ = worldZ + jitterZ;

                int sampleX = Mathf.RoundToInt(finalWorldX);
                int sampleZ = Mathf.RoundToInt(finalWorldZ);

                if (!worldData.IsInsideMap(sampleX, sampleZ))
                {
                    continue;
                }

                if (worldData.GetZone(sampleX, sampleZ) != TerrainZone.Resource)
                {
                    continue;
                }

                if (IsTooSteep(worldData, sampleX, sampleZ, settings.maxSlopeAngle))
                {
                    continue;
                }

                bool usePine = random.NextDouble() < settings.pineChance;
                float trunkHeight;
                float trunkRadius;
                float canopyRadius;

                if (usePine)
                {
                    trunkHeight = Mathf.Lerp(
                        settings.minTrunkHeight1,
                        settings.maxTrunkHeight1,
                        (float)random.NextDouble()
                    );
                    trunkRadius = Mathf.Lerp(
                        settings.minTrunkRadius1,
                        settings.maxTrunkRadius1,
                        (float)random.NextDouble()
                    );
                    canopyRadius = Mathf.Lerp(
                        settings.minCanopyRadius1,
                        settings.maxCanopyRadius1,
                        (float)random.NextDouble()
                    );
                }
                else
                {
                    trunkHeight = Mathf.Lerp(
                        settings.minTrunkHeight2,
                        settings.maxTrunkHeight2,
                        (float)random.NextDouble()
                    );
                    trunkRadius = Mathf.Lerp(
                        settings.minTrunkRadius2,
                        settings.maxTrunkRadius2,
                        (float)random.NextDouble()
                    );
                    canopyRadius = Mathf.Lerp(
                        settings.minCanopyRadius2,
                        settings.maxCanopyRadius2,
                        (float)random.NextDouble()
                    );
                }

                float groundY = worldData.GetHeight(sampleX, sampleZ) + settings.yOffset;
                Vector3 trunkBase = new Vector3(finalWorldX, groundY, finalWorldZ);
                float hideHeight = usePine
                    ? trunkHeight * 0.92f + canopyRadius * 0.7f
                    : trunkHeight + canopyRadius * 0.72f;

                anchors.Add(new ResourceForestTreeAnchor
                {
                    trunkBasePosition = trunkBase,
                    hidePosition = trunkBase + Vector3.up * hideHeight,
                    groundY = groundY,
                    trunkHeight = trunkHeight,
                    canopyRadius = canopyRadius,
                    detectionRadius = Mathf.Max(canopyRadius * 1.9f, settings.spacing * 1.45f),
                    directDropRadius = Mathf.Max(1.1f, canopyRadius * 0.65f + trunkRadius),
                    isPine = usePine
                });
            }
        }
    }

    private static bool IsTooSteep(WorldData worldData, int x, int z, float maxSlope)
    {
        float center = worldData.GetHeight(x, z);
        float right = worldData.GetHeight(x + 1, z);
        float forward = worldData.GetHeight(x, z + 1);

        Vector3 surfaceNormal = Vector3.Cross(
            new Vector3(0f, forward - center, 1f),
            new Vector3(1f, right - center, 0f)
        ).normalized;

        return Vector3.Angle(surfaceNormal, Vector3.up) > maxSlope;
    }
}
