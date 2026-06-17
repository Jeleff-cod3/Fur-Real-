using UnityEngine;

[System.Serializable]
public class PickupStickSpawnSettings
{
    public bool enabled = true;

    [Range(0f, 1f)]
    public float treeDropChance = 0.1f;

    [Range(0f, 1f)]
    public float oneStickWeight = 0.68f;

    [Range(0f, 1f)]
    public float twoStickWeight = 0.22f;

    [Range(0f, 1f)]
    public float threeStickWeight = 0.10f;

    public float minDistanceFromTree = 0.8f;
    public float maxDistanceFromTree = 2.2f;
    public float yOffset = 0.02f;

    [Range(0f, 45f)]
    public float maxSlopeAngle = 32f;
}
