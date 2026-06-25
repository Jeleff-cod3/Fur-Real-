using UnityEngine;

[DefaultExecutionOrder(-150)]
public class DirectTargetRotationAssigner : MonoBehaviour
{
    private const float Epsilon = 0.000001f;

    public enum PlaneNormalMode
    {
        WorldVector,
        CoreUp,
        CoreForward,
        CoreRight,
        DirectionTransformUp,
        DirectionTransformForward,
        DirectionTransformRight
    }

    [Header("Output")]
    [Tooltip("The rotation distributor / assigner on this object. If empty, this script finds one on the same GameObject.")]
    public RotationAssigner rotationAssigner;

    [Tooltip("If true, this script calls ApplyRotation immediately after setting the input value.")]
    public bool applyRotationAssignerImmediately = true;

    [Header("Body References")]
    public Transform coreNode;

    [Tooltip("Defines the zero-angle forward direction from the core.")]
    public Transform poleVector;

    [Header("Target")]
    public Transform targetTransform;

    [Tooltip("Enable this if another script wants to feed an abstract/world-space target position instead of using targetTransform.")]
    public bool useExternalTargetPosition = false;

    public Vector3 externalTargetWorldPosition;

    [Header("Rotation Plane")]
    public PlaneNormalMode planeNormalMode = PlaneNormalMode.WorldVector;

    [Tooltip("Default Vector3.up means yaw on the X/Z plane.")]
    public Vector3 worldPlaneNormal = Vector3.up;

    [Tooltip("Used by the DirectionTransform plane modes.")]
    public Transform directionTransform;

    [Tooltip("Flips left/right if the resulting yaw sign feels backwards.")]
    public bool invertSideAxis = false;

    [Tooltip("Flips the final yaw output if the downstream rotation direction is reversed.")]
    public bool invertOutputYaw = false;

    [Tooltip("Extra multiplier on the final yaw. Usually keep this at 1.")]
    public float outputYawMultiplier = 1f;

    [Header("Runtime")]
    public bool updateEveryFrame = true;

    [SerializeField]
    private RuntimeDebugState debugState;

    [Header("Debug Gizmos")]
    public bool drawDebugGizmos = false;
    public Color targetColor = new Color(1f, 0.75f, 0.1f, 0.9f);
    public Color forwardColor = new Color(0.2f, 0.8f, 1f, 0.9f);

    public RuntimeDebugState DebugState
    {
        get { return debugState; }
    }

    [System.Serializable]
    public struct RuntimeDebugState
    {
        public Vector3 targetWorldPosition;
        public Vector3 coreWorldPosition;
        public Vector3 forwardAxis;
        public Vector3 sideAxis;
        public Vector3 planeNormal;
        public Vector3 projectedTargetDirection;
        public float directYawDegrees;
        public float outputYawDegrees;
    }

    private void Reset()
    {
        rotationAssigner = GetComponent<RotationAssigner>();
    }

    private void Awake()
    {
        if (rotationAssigner == null)
        {
            rotationAssigner = GetComponent<RotationAssigner>();
        }
    }

    private void Update()
    {
        if (updateEveryFrame)
        {
            CalculateAndAssign();
        }
    }

    public void SetExternalTargetWorldPosition(Vector3 worldPosition)
    {
        externalTargetWorldPosition = worldPosition;
        useExternalTargetPosition = true;
    }

    public void UseTransformTarget()
    {
        useExternalTargetPosition = false;
    }

    public void CalculateAndAssign()
    {
        RuntimeDebugState result;

        if (!TryEvaluate(out result))
        {
            return;
        }

        float outputYaw = result.directYawDegrees * outputYawMultiplier;

        if (invertOutputYaw)
        {
            outputYaw = -outputYaw;
        }

        outputYaw = NormalizeSignedDegrees(outputYaw);
        result.outputYawDegrees = outputYaw;
        debugState = result;

        if (rotationAssigner == null)
        {
            return;
        }

        rotationAssigner.SetInputRotationDegrees(outputYaw);

        if (applyRotationAssignerImmediately)
        {
            rotationAssigner.ApplyRotation(outputYaw);
        }
    }

    public bool TryEvaluate(out RuntimeDebugState result)
    {
        result = new RuntimeDebugState();

        if (coreNode == null || poleVector == null)
        {
            return false;
        }

        Vector3 targetWorldPosition;

        if (useExternalTargetPosition)
        {
            targetWorldPosition = externalTargetWorldPosition;
        }
        else
        {
            if (targetTransform == null)
            {
                return false;
            }

            targetWorldPosition = targetTransform.position;
        }

        Vector3 normal = GetPlaneNormal();
        Vector3 forwardAxis = GetForwardAxis(normal);

        if (forwardAxis.sqrMagnitude < Epsilon)
        {
            return false;
        }

        Vector3 sideAxis = Vector3.Cross(normal, forwardAxis);

        if (sideAxis.sqrMagnitude < Epsilon)
        {
            return false;
        }

        sideAxis.Normalize();

        if (invertSideAxis)
        {
            sideAxis = -sideAxis;
        }

        Vector3 targetDirection = Vector3.ProjectOnPlane(
            targetWorldPosition - coreNode.position,
            normal
        );

        if (targetDirection.sqrMagnitude < Epsilon)
        {
            return false;
        }

        targetDirection.Normalize();

        float side = Vector3.Dot(targetDirection, sideAxis);
        float forward = Vector3.Dot(targetDirection, forwardAxis);
        float directYawDegrees = NormalizeSignedDegrees(Mathf.Atan2(side, forward) * Mathf.Rad2Deg);

        result.targetWorldPosition = targetWorldPosition;
        result.coreWorldPosition = coreNode.position;
        result.forwardAxis = forwardAxis;
        result.sideAxis = sideAxis;
        result.planeNormal = normal;
        result.projectedTargetDirection = targetDirection;
        result.directYawDegrees = directYawDegrees;
        result.outputYawDegrees = directYawDegrees;

        return true;
    }

    private Vector3 GetPlaneNormal()
    {
        Vector3 normal;

        switch (planeNormalMode)
        {
            case PlaneNormalMode.CoreUp:
                normal = coreNode != null ? coreNode.up : worldPlaneNormal;
                break;

            case PlaneNormalMode.CoreForward:
                normal = coreNode != null ? coreNode.forward : worldPlaneNormal;
                break;

            case PlaneNormalMode.CoreRight:
                normal = coreNode != null ? coreNode.right : worldPlaneNormal;
                break;

            case PlaneNormalMode.DirectionTransformUp:
                normal = directionTransform != null ? directionTransform.up : worldPlaneNormal;
                break;

            case PlaneNormalMode.DirectionTransformForward:
                normal = directionTransform != null ? directionTransform.forward : worldPlaneNormal;
                break;

            case PlaneNormalMode.DirectionTransformRight:
                normal = directionTransform != null ? directionTransform.right : worldPlaneNormal;
                break;

            case PlaneNormalMode.WorldVector:
            default:
                normal = worldPlaneNormal;
                break;
        }

        if (normal.sqrMagnitude < Epsilon)
        {
            normal = Vector3.up;
        }

        return normal.normalized;
    }

    private Vector3 GetForwardAxis(Vector3 planeNormal)
    {
        Vector3 poleDirection = poleVector.position - coreNode.position;
        Vector3 projectedPoleDirection = Vector3.ProjectOnPlane(poleDirection, planeNormal);

        if (projectedPoleDirection.sqrMagnitude >= Epsilon)
        {
            return projectedPoleDirection.normalized;
        }

        Vector3 projectedCoreForward = Vector3.ProjectOnPlane(coreNode.forward, planeNormal);

        if (projectedCoreForward.sqrMagnitude >= Epsilon)
        {
            return projectedCoreForward.normalized;
        }

        Vector3 fallbackForward = Vector3.ProjectOnPlane(Vector3.forward, planeNormal);

        if (fallbackForward.sqrMagnitude >= Epsilon)
        {
            return fallbackForward.normalized;
        }

        Vector3 fallbackRight = Vector3.ProjectOnPlane(Vector3.right, planeNormal);

        if (fallbackRight.sqrMagnitude >= Epsilon)
        {
            return fallbackRight.normalized;
        }

        return Vector3.zero;
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

    private void OnDrawGizmosSelected()
    {
        if (!drawDebugGizmos)
        {
            return;
        }

        RuntimeDebugState result;

        if (!TryEvaluate(out result))
        {
            return;
        }

        Gizmos.color = forwardColor;
        Gizmos.DrawLine(result.coreWorldPosition, result.coreWorldPosition + result.forwardAxis);
        Gizmos.DrawLine(result.coreWorldPosition, result.coreWorldPosition + result.sideAxis);

        Gizmos.color = targetColor;
        Gizmos.DrawLine(result.coreWorldPosition, result.targetWorldPosition);
        Gizmos.DrawSphere(result.targetWorldPosition, 0.08f);
    }
}
