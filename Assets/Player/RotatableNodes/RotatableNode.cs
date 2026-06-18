using UnityEngine;

[DefaultExecutionOrder(-50)]
public class RotatableNode : MonoBehaviour
{
    private const float Epsilon = 0.000001f;

    [Header("Debug")]
    public bool debugLogging = true;

    [Header("Current Node")]
    [Tooltip("The OffsetPositioningNode on this same object. This is what receives the rotation dynamic offset.")]
    public OffsetPositioningNode currentNode;

    [Header("Rotation References")]
    public Transform coreNode;
    public Transform poleVector;

    [Tooltip("If true, the OffsetPositioningNode parent is used as the rotation center when available.")]
    public bool useOffsetParentAsRotationCore = true;

    [Tooltip("Normal of the rotation plane. Vector3.up means rotation happens on the X/Z plane.")]
    public Vector3 rotationPlaneNormal = Vector3.up;

    [Header("Rotation")]
    [Tooltip("Local degree change applied on top of the node's initial rotational offset from the pole.")]
    public float localRotationDegrees = 0f;

    [Tooltip("Maximum local rotation magnitude. 360 means effectively unclamped.")]
    public float maxLocalRotationDegrees = 360f;

    [Header("Dynamic Offset")]
    [Tooltip("Hardcoded dynamic offset ID used by this rotatable node. Default is 1.")]
    public int rotationDynamicOffsetId = 1;

    [Header("Initialization")]
    public bool initializeOnStart = true;

    [Tooltip("Normally true. Keeps the node's original height/depth outside the rotation plane.")]
    public bool preserveInitialPlaneOffset = true;

    [Tooltip("Usually false. False means each node keeps its own radius from the core. True means radius comes from core-to-pole distance.")]
    public bool usePoleDistanceAsRadius = false;

    [Header("Runtime")]
    public bool applyEveryUpdate = true;

    [Tooltip("Usually false for IK-bound offsets. False means this script only writes the rotation dynamic offset and lets OffsetPositioningNode apply after IK/solver timing.")]
    public bool applyCurrentNodeImmediately = false;

    [SerializeField]
    private bool initialized = false;

    [SerializeField]
    private float initialSignedAngleFromPole = 0f;

    [SerializeField]
    private float initialRadius = 0f;

    [SerializeField]
    private float initialPlaneOffset = 0f;

    [SerializeField]
    private Vector3 initialPlanarOffsetFromCore = Vector3.zero;

    private bool hasLoggedUpdate = false;

    private void Reset()
    {
        currentNode = GetComponent<OffsetPositioningNode>();
    }

    private void OnEnable()
    {
        Log("OnEnable", $"currentNode={(currentNode != null ? currentNode.name : "null")}, coreNode={(coreNode != null ? coreNode.name : "null")}, resolvedCore={(ResolveRotationCore() != null ? ResolveRotationCore().name : "null")}, poleVector={(poleVector != null ? poleVector.name : "null")}, initializeOnStart={initializeOnStart}, applyEveryUpdate={applyEveryUpdate}");
    }

    private void Start()
    {
        Log("Start", "Start invoked.");

        if (initializeOnStart)
        {
            Log("Start", "initializeOnStart is enabled, initializing from current pose.");
            InitializeFromCurrentPose();
            if (initialized)
            {
                Log("Start", "applying rotation immediately after initialization.");
                ApplyRotationOffset();
            }
        }
        else
        {
            Log("Start", "initializeOnStart is disabled, skipping automatic initialization.");
        }
    }

    private void Update()
    {
        if (!hasLoggedUpdate)
        {
            hasLoggedUpdate = true;
            Log("Update", $"entered Update, applyEveryUpdate={applyEveryUpdate}, initialized={initialized}");
        }

        if (applyEveryUpdate)
        {
            Log("Update", "applyEveryUpdate is enabled, applying rotation offset.");
            ApplyRotationOffset();
        }
    }

    [ContextMenu("Initialize From Current Pose")]
    public virtual void InitializeFromCurrentPose()
    {
        if (currentNode == null)
        {
            currentNode = GetComponent<OffsetPositioningNode>();
        }

        Transform rotationCore = ResolveRotationCore();

        if (currentNode == null || rotationCore == null)
        {
            initialized = false;
            LogWarning("InitializeFromCurrentPose", $"missing references: currentNode={(currentNode != null ? currentNode.name : "null")}, coreNode={(coreNode != null ? coreNode.name : "null")}, resolvedCore={(rotationCore != null ? rotationCore.name : "null")}");
            return;
        }

        Vector3 normal = GetSafePlaneNormal();
        Vector3 referenceDirection = GetReferenceDirection(normal);
        Vector3 authoredStaticOffset = currentNode.GetAppliedStaticOffset();
        Vector3 nodePlanarVector = Vector3.ProjectOnPlane(authoredStaticOffset, normal);

        if (nodePlanarVector.sqrMagnitude < Epsilon)
        {
            nodePlanarVector = referenceDirection * 0.0001f;
        }

        initialSignedAngleFromPole =
            Vector3.SignedAngle(referenceDirection, nodePlanarVector.normalized, normal);

        Vector3 polePlanarVector = poleVector != null
            ? Vector3.ProjectOnPlane(poleVector.position - rotationCore.position, normal)
            : Vector3.zero;

        initialRadius = usePoleDistanceAsRadius
            ? polePlanarVector.magnitude
            : nodePlanarVector.magnitude;

        initialPlaneOffset = Vector3.Dot(authoredStaticOffset, normal);
        initialPlanarOffsetFromCore = nodePlanarVector.normalized * initialRadius;

        initialized = true;
        Log("InitializeFromCurrentPose", $"initialized=true, rotationCore={rotationCore.name}, authoredStaticOffset={authoredStaticOffset}, initialSignedAngleFromPole={initialSignedAngleFromPole}, initialRadius={initialRadius}, initialPlanarOffsetFromCore={initialPlanarOffsetFromCore}, initialPlaneOffset={initialPlaneOffset}, normal={normal}, referenceDirection={referenceDirection}");
    }

    public void SetLocalRotationDegrees(float angleDegrees)
    {
        localRotationDegrees = angleDegrees;
        Log("SetLocalRotationDegrees", $"localRotationDegrees={localRotationDegrees}");
    }

    public void SetSharedReferences(
        Transform newCoreNode,
        Transform newPoleVector,
        Vector3 newRotationPlaneNormal,
        bool reinitialize
    )
    {
        if (newCoreNode != null)
        {
            coreNode = newCoreNode;
        }

        if (newPoleVector != null)
        {
            poleVector = newPoleVector;
        }

        rotationPlaneNormal = newRotationPlaneNormal;

        if (reinitialize)
        {
            InitializeFromCurrentPose();
            if (initialized)
            {
                ApplyRotationOffset();
            }
        }
    }

    public virtual void ApplyRotationOffset()
    {
        if (!initialized)
        {
            LogWarning("ApplyRotationOffset", "skipped because the node has not been initialized yet.");
            return;
        }

        Transform rotationCore = ResolveRotationCore();

        if (currentNode == null || rotationCore == null)
        {
            LogWarning("ApplyRotationOffset", $"skipped because required references are missing: currentNode={(currentNode != null ? currentNode.name : "null")}, coreNode={(coreNode != null ? coreNode.name : "null")}, resolvedCore={(rotationCore != null ? rotationCore.name : "null")}");
            return;
        }

        Vector3 requiredDynamicOffset = CalculateRotationDynamicOffset();
        Vector3 currentDynamicOffset = currentNode.GetDynamicOffset(rotationDynamicOffsetId);

        if ((currentDynamicOffset - requiredDynamicOffset).sqrMagnitude > Epsilon)
        {
            currentNode.SetDynamicOffset(rotationDynamicOffsetId, requiredDynamicOffset);
            if (applyCurrentNodeImmediately)
            {
                currentNode.ApplyPosition();
            }
            Log("ApplyRotationOffset", $"updated rotation dynamic offset id={rotationDynamicOffsetId}, localRotationDegrees={localRotationDegrees}, requiredDynamicOffset={requiredDynamicOffset}, finalWorldPosition={currentNode.GetFinalWorldPosition()}");
        }
    }

    public Vector3 CalculateRotationDynamicOffset()
    {
        Vector3 staticOffset = currentNode != null ? currentNode.GetAppliedStaticOffset() : Vector3.zero;
        return CalculateRotationDynamicOffsetForStaticOffset(staticOffset);
    }

    public Vector3 CalculateRotationDynamicOffsetForStaticOffset(Vector3 staticOffset)
    {
        Vector3 normal = GetSafePlaneNormal();
        Vector3 staticPlanarOffset = Vector3.ProjectOnPlane(staticOffset, normal);

        if (staticPlanarOffset.sqrMagnitude < Epsilon)
        {
            LogWarning("CalculateRotationDynamicOffsetForStaticOffset", $"static offset has no usable planar X/Z component: staticOffset={staticOffset}, normal={normal}");
            return Vector3.zero;
        }

        float clampedLocalRotation = ClampLocalRotation(localRotationDegrees);
        Quaternion rotation = Quaternion.AngleAxis(clampedLocalRotation, normal);
        Vector3 rotatedPlanarOffset = rotation * staticPlanarOffset;
        Vector3 dynamicOffset = rotatedPlanarOffset - staticPlanarOffset;

        Log("CalculateRotationDynamicOffsetForStaticOffset", $"staticOffset={staticOffset}, staticPlanarOffset={staticPlanarOffset}, clampedLocalRotation={clampedLocalRotation}, rotatedPlanarOffset={rotatedPlanarOffset}, dynamicOffset={dynamicOffset}");
        return dynamicOffset;
    }

    public Vector3 CalculateRotatedWorldPosition()
    {
        Transform rotationCore = ResolveRotationCore();
        if (rotationCore == null)
        {
            LogWarning("CalculateRotatedWorldPosition", "no rotation core available, returning current position.");
            return transform.position;
        }

        Vector3 normal = GetSafePlaneNormal();

        float clampedLocalRotation = ClampLocalRotation(localRotationDegrees);
        Quaternion rotation = Quaternion.AngleAxis(clampedLocalRotation, normal);
        Vector3 staticOffset = currentNode != null ? currentNode.GetAppliedStaticOffset() : Vector3.zero;
        Vector3 staticPlanarOffset = Vector3.ProjectOnPlane(staticOffset, normal);
        Vector3 staticPlaneOffset = staticOffset - staticPlanarOffset;
        Vector3 rotatedPlanarVector = rotation * staticPlanarOffset;

        Vector3 result = rotationCore.position + rotatedPlanarVector + staticPlaneOffset;
        Log("CalculateRotatedWorldPosition", $"rotationCore={rotationCore.name}, staticOffset={staticOffset}, clampedLocalRotation={clampedLocalRotation}, rotatedPlanarVector={rotatedPlanarVector}, result={result}, normal={normal}");
        return result;
    }

    private float ClampLocalRotation(float angleDegrees)
    {
        float signedAngle = NormalizeSignedDegrees(angleDegrees);

        if (maxLocalRotationDegrees <= 0f || maxLocalRotationDegrees >= 360f)
        {
            return signedAngle;
        }

        float limit = Mathf.Abs(maxLocalRotationDegrees);
        return Mathf.Clamp(signedAngle, -limit, limit);
    }

    private Vector3 GetSafePlaneNormal()
    {
        if (rotationPlaneNormal.sqrMagnitude < Epsilon)
        {
            return Vector3.up;
        }

        return rotationPlaneNormal.normalized;
    }

    private Vector3 GetReferenceDirection(Vector3 normal)
    {
        Transform rotationCore = ResolveRotationCore();

        if (poleVector != null && rotationCore != null)
        {
            Vector3 poleVectorFromCore = poleVector.position - rotationCore.position;
            Vector3 projectedPoleDirection = Vector3.ProjectOnPlane(poleVectorFromCore, normal);

            if (projectedPoleDirection.sqrMagnitude >= Epsilon)
            {
                return projectedPoleDirection.normalized;
            }
        }

        Vector3 fallback = Vector3.ProjectOnPlane(Vector3.forward, normal);

        if (fallback.sqrMagnitude < Epsilon)
        {
            fallback = Vector3.ProjectOnPlane(Vector3.right, normal);
        }

        return fallback.normalized;
    }

    private Transform ResolveRotationCore()
    {
        if (useOffsetParentAsRotationCore)
        {
            if (currentNode == null)
            {
                currentNode = GetComponent<OffsetPositioningNode>();
            }

            if (currentNode != null && currentNode.parentNode != null)
            {
                return currentNode.parentNode;
            }
        }

        return coreNode;
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

    private void Log(string scope, string message)
    {
        if (!debugLogging)
        {
            return;
        }

        Debug.Log($"[RotatableNode:{name}] {scope} - {message}", this);
    }

    private void LogWarning(string scope, string message)
    {
        if (!debugLogging)
        {
            return;
        }

        Debug.LogWarning($"[RotatableNode:{name}] {scope} - {message}", this);
    }
}
