using UnityEngine;

[System.Serializable]
public class PickupRockSpawnSettings
{
    public bool enabled = true;

    [Min(1)]
    public int spacing = 18;

    [Range(0f, 1f)]
    public float arenaDensity = 0.02f;

    [Range(0f, 1f)]
    public float transitionDensity = 0.035f;

    [Range(0f, 1f)]
    public float resourceDensity = 0.05f;

    public float yOffset = 0.03f;

    [Range(0f, 45f)]
    public float maxSlopeAngle = 34f;

    public float minVisualScale = 0.28f;
    public float maxVisualScale = 0.52f;
}
