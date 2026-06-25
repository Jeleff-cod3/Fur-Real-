using UnityEngine;

[DefaultExecutionOrder(-100)]
public class RotationAssigner : MonoBehaviour
{
    [Header("Debug")]
    public bool debugLogging = false;

    [Header("Pairs")]
    public RotatableNodePair[] nodePairs;

    [Header("Fake Offset Parents")]
    [Tooltip("Each fake offset parent is treated as one pair-like rotation section. Its internal offsets rotate individually.")]
    public RotatableMeshingOffsetNode[] fakeOffsetParents;

    [Header("Shared References")]
    public bool overridePairCoreAndPole = true;
    public Transform sharedCoreNode;
    public Transform sharedPoleVector;

    [Header("Shared Rotation Plane")]
    public bool overridePairPlane = true;

    public RotatableNodePair.PlaneNormalMode sharedPlaneNormalMode =
        RotatableNodePair.PlaneNormalMode.WorldVector;

    public Vector3 sharedWorldPlaneNormal = Vector3.up;
    public Transform sharedDirectionTransform;

    [Header("Rotation Input")]
    [Tooltip("Requested total rotation for the whole chain. Can be positive, negative, or 0-360 style.")]
    public float inputRotationDegrees = 0f;

    [Tooltip("Maximum total bend. 50 and 310 both represent a 50-degree side limit.")]
    public float maxBendDegrees = 50f;

    [Header("Runtime")]
    public bool applyEveryUpdate = true;

    [SerializeField]
    private float lastClampedTotalAngle = 0f;

    [SerializeField]
    private float lastAnglePerPair = 0f;

    public float LastClampedTotalAngle => lastClampedTotalAngle;
    public float LastAnglePerPair => lastAnglePerPair;

    private bool hasLoggedUpdate = false;

    private void OnEnable()
    {
        Log("OnEnable", $"pairs={(nodePairs != null ? nodePairs.Length : 0)}, fakeOffsetParents={(fakeOffsetParents != null ? fakeOffsetParents.Length : 0)}, overridePairCoreAndPole={overridePairCoreAndPole}, overridePairPlane={overridePairPlane}, inputRotationDegrees={inputRotationDegrees}, maxBendDegrees={maxBendDegrees}, applyEveryUpdate={applyEveryUpdate}");
    }

    private void Start()
    {
        Log("Start", "applying launch rotation once.");
        ApplyRotation(inputRotationDegrees);
    }

    private void Update()
    {
        if (!hasLoggedUpdate)
        {
            hasLoggedUpdate = true;
            Log("Update", $"entered Update, applyEveryUpdate={applyEveryUpdate}");
        }

        if (applyEveryUpdate)
        {
            Log("Update", "applyEveryUpdate is enabled, applying rotation.");
            ApplyRotation(inputRotationDegrees);
        }
    }

    public void SetInputRotationDegrees(float angleDegrees)
    {
        inputRotationDegrees = angleDegrees;
    }

    public void ApplyRotation(float requestedTotalAngleDegrees)
    {
        int validPairCount = CountValidPairs();
        int validFakeParentCount = CountValidFakeOffsetParents();

        if (validPairCount == 0 && validFakeParentCount == 0)
        {
            lastClampedTotalAngle = 0f;
            lastAnglePerPair = 0f;
            LogWarning("ApplyRotation", $"no valid rotation sections found. nodePairs={(nodePairs != null ? nodePairs.Length : 0)}, fakeOffsetParents={(fakeOffsetParents != null ? fakeOffsetParents.Length : 0)}");
            return;
        }

        float signedRequestedAngle = NormalizeSignedDegrees(requestedTotalAngleDegrees);
        float maxMagnitude = GetMaxMagnitudeFromDegreeValue(maxBendDegrees);

        float clampedTotalAngle = Mathf.Clamp(
            signedRequestedAngle,
            -maxMagnitude,
            maxMagnitude
        );

        float pairAngleStep = validPairCount > 0 ? clampedTotalAngle / validPairCount : 0f;
        float fakeParentAngleStep = validFakeParentCount > 0 ? clampedTotalAngle / validFakeParentCount : 0f;

        lastClampedTotalAngle = clampedTotalAngle;
        lastAnglePerPair = pairAngleStep;

        Log("ApplyRotation", $"requestedTotalAngleDegrees={requestedTotalAngleDegrees}, signedRequestedAngle={signedRequestedAngle}, maxMagnitude={maxMagnitude}, clampedTotalAngle={clampedTotalAngle}, validPairCount={validPairCount}, validFakeParentCount={validFakeParentCount}, pairAngleStep={pairAngleStep}, fakeParentAngleStep={fakeParentAngleStep}");

        int validPairIndex = 0;

        if (nodePairs != null)
        {
            for (int i = 0; i < nodePairs.Length; i++)
            {
                RotatableNodePair pair = nodePairs[i];

                if (pair == null || !pair.isActiveAndEnabled)
                {
                    continue;
                }

                ApplySharedSettingsToPair(pair);
                float cumulativeAngle = pairAngleStep * (validPairIndex + 1);
                Log("ApplyRotation", $"pairIndex={i}, validPairIndex={validPairIndex}, cumulativeAngle={cumulativeAngle}");
                pair.SetPairRotationDegrees(cumulativeAngle);
                validPairIndex++;
            }
        }

        int validFakeParentIndex = 0;

        if (fakeOffsetParents != null)
        {
            for (int i = 0; i < fakeOffsetParents.Length; i++)
            {
                RotatableMeshingOffsetNode fakeParent = fakeOffsetParents[i];

                if (fakeParent == null || !fakeParent.isActiveAndEnabled)
                {
                    continue;
                }

                if (fakeParent.GetMeshingOffsetNode() == null)
                {
                    continue;
                }

                ApplySharedSettingsToFakeOffsetParent(fakeParent);
                float cumulativeAngle = fakeParentAngleStep * (validFakeParentIndex + 1);
                Log("ApplyRotation", $"fakeOffsetParentIndex={i}, validFakeParentIndex={validFakeParentIndex}, cumulativeAngle={cumulativeAngle}");
                fakeParent.SetFakeRotationDegrees(cumulativeAngle);
                validFakeParentIndex++;
            }
        }
    }

    private void ApplySharedSettingsToPair(RotatableNodePair pair)
    {
        if (overridePairCoreAndPole)
        {
            if (sharedCoreNode != null)
            {
                pair.coreNode = sharedCoreNode;
            }

            if (sharedPoleVector != null)
            {
                pair.poleVector = sharedPoleVector;
            }
        }

        if (overridePairPlane)
        {
            pair.planeNormalMode = sharedPlaneNormalMode;
            pair.worldPlaneNormal = sharedWorldPlaneNormal;
            pair.directionTransform = sharedDirectionTransform;
        }
    }

    private void ApplySharedSettingsToFakeOffsetParent(RotatableMeshingOffsetNode fakeParent)
    {
        Transform coreNode = null;
        Transform poleVector = null;

        if (overridePairCoreAndPole)
        {
            coreNode = sharedCoreNode;
            poleVector = sharedPoleVector;
        }

        Vector3 planeNormal = overridePairPlane
            ? ResolveSharedPlaneNormal()
            : fakeParent.rotationPlaneNormal;

        fakeParent.SetFakeSharedReferences(
            coreNode,
            poleVector,
            planeNormal,
            false
        );
    }

    private Vector3 ResolveSharedPlaneNormal()
    {
        Vector3 normal;

        switch (sharedPlaneNormalMode)
        {
            case RotatableNodePair.PlaneNormalMode.DirectionTransformUp:
                normal = sharedDirectionTransform != null ? sharedDirectionTransform.up : sharedWorldPlaneNormal;
                break;

            case RotatableNodePair.PlaneNormalMode.DirectionTransformForward:
                normal = sharedDirectionTransform != null ? sharedDirectionTransform.forward : sharedWorldPlaneNormal;
                break;

            case RotatableNodePair.PlaneNormalMode.DirectionTransformRight:
                normal = sharedDirectionTransform != null ? sharedDirectionTransform.right : sharedWorldPlaneNormal;
                break;

            case RotatableNodePair.PlaneNormalMode.WorldVector:
            default:
                normal = sharedWorldPlaneNormal;
                break;
        }

        if (normal.sqrMagnitude < 0.000001f)
        {
            normal = Vector3.up;
        }

        return normal.normalized;
    }

    private int CountValidPairs()
    {
        int count = 0;

        if (nodePairs != null)
        {
            for (int i = 0; i < nodePairs.Length; i++)
            {
                if (nodePairs[i] != null && nodePairs[i].isActiveAndEnabled)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private int CountValidFakeOffsetParents()
    {
        int count = 0;

        if (fakeOffsetParents != null)
        {
            for (int i = 0; i < fakeOffsetParents.Length; i++)
            {
                if (fakeOffsetParents[i] != null &&
                    fakeOffsetParents[i].isActiveAndEnabled &&
                    fakeOffsetParents[i].GetMeshingOffsetNode() != null)
                {
                    count++;
                }
            }
        }

        return count;
    }

    public static float NormalizeSignedDegrees(float degrees)
    {
        float normalized = Mathf.Repeat(degrees + 180f, 360f) - 180f;

        if (Mathf.Approximately(normalized, -180f))
        {
            return 180f;
        }

        return normalized;
    }

    private static float GetMaxMagnitudeFromDegreeValue(float degrees)
    {
        float signed = NormalizeSignedDegrees(degrees);
        float magnitude = Mathf.Abs(signed);

        return Mathf.Clamp(magnitude, 0f, 180f);
    }

    private void Log(string scope, string message)
    {
        if (!debugLogging)
        {
            return;
        }

        Debug.Log($"[RotationAssigner:{name}] {scope} - {message}", this);
    }

    private void LogWarning(string scope, string message)
    {
        if (!debugLogging)
        {
            return;
        }

        Debug.LogWarning($"[RotationAssigner:{name}] {scope} - {message}", this);
    }
}
