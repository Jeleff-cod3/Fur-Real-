using UnityEngine;

[DefaultExecutionOrder(-150)]
public class BodyRotationBoxAssigner : MonoBehaviour
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

    public enum BoxDistanceMode
    {
        MaxAxisOverflow,
        EuclideanOverflow
    }

    public enum BoxAngleProjectionMode
    {
        /*
         * This matches the presentation rule:
         *
         * fullYaw = atan2(realSide, realForward)
         * boxYaw  = atan2(clampedSide, realForward)
         *
         * The box protects side-angle at the target's current depth.
         */
        ClampSideKeepTargetForward,

        /*
         * This uses the nearest point inside the finite box:
         *
         * fullYaw = atan2(realSide, realForward)
         * boxYaw  = atan2(clampedSide, clampedForward)
         *
         * Useful later if you want the box to behave more like a literal finite rectangle.
         */
        NearestPointInsideBox
    }

    [Header("Output")]
    [Tooltip("The rotation distributor / assigner from the previous step.")]
    public RotationAssigner rotationAssigner;

    [Tooltip("If true, this script calls ApplyRotation immediately after setting the input value.")]
    public bool applyRotationAssignerImmediately = true;

    [Header("Body References")]
    public Transform coreNode;

    [Tooltip("Defines the body's forward direction from the core. Usually a transform placed in front of the torso/root.")]
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

    [Header("Comfort Box")]
    [Tooltip("Box center offset in body-local side/forward space. X = side, Y = forward.")]
    public Vector2 boxCenterOffset = new Vector2(0f, 3f);

    [Min(0f)]
    public float boxHalfWidth = 1.5f;

    [Min(0f)]
    public float boxHalfDepth = 3f;

    [Tooltip("If true, forward overflow also contributes to debt payment. This makes far-front targets gradually pull the torso too.")]
    public bool includeForwardOverflowInDebt = true;

    public BoxDistanceMode boxDistanceMode = BoxDistanceMode.EuclideanOverflow;

    public BoxAngleProjectionMode boxAngleProjectionMode =
        BoxAngleProjectionMode.ClampSideKeepTargetForward;

    [Header("Debt Payment")]
    [Tooltip("How far outside the box the target must go before debt is fully paid, before applying debtPaymentRatio.")]
    [Min(0.0001f)]
    public float outsideFalloffDistance = 3f;

    [Tooltip("Higher means debt gets paid faster. 1 = normal. 2 = twice as aggressive. 0 = never pay debt.")]
    [Min(0f)]
    public float debtPaymentRatio = 1f;

    [Tooltip("Uses t*t*(3-2*t) after clamping t to 0..1.")]
    public bool useSmoothStepDebtPayment = true;

    [Header("Output Smoothing")]
    [Tooltip("Formula result is desired yaw. This optionally smooths the value before sending it to the RotationAssigner.")]
    public bool smoothOutputBeforeAssigning = true;

    [Min(0.0001f)]
    public float smoothTime = 0.12f;

    public float maxSmoothingSpeed = 720f;

    [Tooltip("Extra multiplier on the final yaw. Usually keep this at 1.")]
    public float outputYawMultiplier = 1f;

    [Header("Runtime")]
    public bool updateEveryFrame = true;

    [SerializeField]
    private float currentSmoothedYaw = 0f;

    [SerializeField]
    private float yawSmoothVelocity = 0f;

    [SerializeField]
    private RuntimeDebugState debugState;

    [Header("Debug Gizmos")]
    public bool drawDebugGizmos = true;
    public Color boxColor = new Color(0.2f, 0.8f, 1f, 0.8f);
    public Color targetColor = new Color(1f, 0.75f, 0.1f, 0.9f);
    public Color protectedPointColor = new Color(0.5f, 1f, 0.6f, 0.9f);

    public RuntimeDebugState DebugState => debugState;

    [System.Serializable]
    public struct RuntimeDebugState
    {
        public Vector3 targetWorldPosition;

        public Vector3 coreWorldPosition;
        public Vector3 forwardAxis;
        public Vector3 sideAxis;
        public Vector3 planeNormal;

        public float localSide;
        public float localForward;

        public float clampedSide;
        public float clampedForward;

        public float sideOverflow;
        public float forwardOverflow;
        public float outsideDistance;
        public float debtPayment;

        public float fullYawDegrees;
        public float boxYawDegrees;
        public float minimumTurnDegrees;
        public float ignoredDebtDegrees;
        public float desiredYawDegrees;
        public float outputYawDegrees;

        public Vector3 nearestBoxPointWorld;
        public Vector3 protectedYawPointWorld;
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

        debugState = result;

        float outputYaw = result.desiredYawDegrees * outputYawMultiplier;

        if (invertOutputYaw)
        {
            outputYaw = -outputYaw;
        }

        if (smoothOutputBeforeAssigning)
        {
            currentSmoothedYaw = Mathf.SmoothDampAngle(
                currentSmoothedYaw,
                outputYaw,
                ref yawSmoothVelocity,
                smoothTime,
                maxSmoothingSpeed,
                Time.deltaTime
            );

            outputYaw = currentSmoothedYaw;
        }
        else
        {
            currentSmoothedYaw = outputYaw;
            yawSmoothVelocity = 0f;
        }

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

        Vector3 corePosition = coreNode.position;
        Vector3 targetFromCore = targetWorldPosition - corePosition;

        float side = Vector3.Dot(targetFromCore, sideAxis);
        float forward = Vector3.Dot(targetFromCore, forwardAxis);

        float sideMin = boxCenterOffset.x - Mathf.Abs(boxHalfWidth);
        float sideMax = boxCenterOffset.x + Mathf.Abs(boxHalfWidth);

        float forwardMin = boxCenterOffset.y - Mathf.Abs(boxHalfDepth);
        float forwardMax = boxCenterOffset.y + Mathf.Abs(boxHalfDepth);

        float clampedSide = Mathf.Clamp(side, sideMin, sideMax);
        float clampedForward = Mathf.Clamp(forward, forwardMin, forwardMax);

        float sideOverflow = CalculateAxisOverflow(side, sideMin, sideMax);

        float forwardOverflow = includeForwardOverflowInDebt
            ? CalculateAxisOverflow(forward, forwardMin, forwardMax)
            : 0f;

        float outsideDistance = CalculateOutsideDistance(sideOverflow, forwardOverflow);

        float debtPayment = CalculateDebtPayment(outsideDistance);

        float fullYawDegrees = Mathf.Atan2(side, forward) * Mathf.Rad2Deg;

        float boxAngleSide = clampedSide;
        float boxAngleForward;

        if (boxAngleProjectionMode == BoxAngleProjectionMode.NearestPointInsideBox)
        {
            boxAngleForward = clampedForward;
        }
        else
        {
            boxAngleForward = forward;
        }

        float boxYawDegrees = Mathf.Atan2(boxAngleSide, boxAngleForward) * Mathf.Rad2Deg;

        float minimumTurnDegrees = NormalizeSignedDegrees(fullYawDegrees - boxYawDegrees);
        float ignoredDebtDegrees = boxYawDegrees;

        /*
         * Presentation formula:
         *
         * minimumTurn = fullYaw - boxYaw
         * debt        = boxYaw
         *
         * desiredYaw = minimumTurn + debtPayment * debt
         *
         * Same as:
         *
         * desiredYaw = fullYaw - (1 - debtPayment) * boxYaw
         */
        float desiredYawDegrees =
            minimumTurnDegrees + debtPayment * ignoredDebtDegrees;

        desiredYawDegrees = NormalizeSignedDegrees(desiredYawDegrees);

        Vector3 nearestBoxPointWorld =
            corePosition
            + sideAxis * clampedSide
            + forwardAxis * clampedForward;

        Vector3 protectedYawPointWorld =
            corePosition
            + sideAxis * boxAngleSide
            + forwardAxis * boxAngleForward;

        result.targetWorldPosition = targetWorldPosition;

        result.coreWorldPosition = corePosition;
        result.forwardAxis = forwardAxis;
        result.sideAxis = sideAxis;
        result.planeNormal = normal;

        result.localSide = side;
        result.localForward = forward;

        result.clampedSide = clampedSide;
        result.clampedForward = clampedForward;

        result.sideOverflow = sideOverflow;
        result.forwardOverflow = forwardOverflow;
        result.outsideDistance = outsideDistance;
        result.debtPayment = debtPayment;

        result.fullYawDegrees = fullYawDegrees;
        result.boxYawDegrees = boxYawDegrees;
        result.minimumTurnDegrees = minimumTurnDegrees;
        result.ignoredDebtDegrees = ignoredDebtDegrees;
        result.desiredYawDegrees = desiredYawDegrees;
        result.outputYawDegrees = desiredYawDegrees;

        result.nearestBoxPointWorld = nearestBoxPointWorld;
        result.protectedYawPointWorld = protectedYawPointWorld;

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

    private float CalculateAxisOverflow(float value, float min, float max)
    {
        if (value < min)
        {
            return min - value;
        }

        if (value > max)
        {
            return value - max;
        }

        return 0f;
    }

    private float CalculateOutsideDistance(float sideOverflow, float forwardOverflow)
    {
        if (boxDistanceMode == BoxDistanceMode.MaxAxisOverflow)
        {
            return Mathf.Max(sideOverflow, forwardOverflow);
        }

        return Mathf.Sqrt(
            sideOverflow * sideOverflow
            + forwardOverflow * forwardOverflow
        );
    }

    private float CalculateDebtPayment(float outsideDistance)
    {
        if (debtPaymentRatio <= 0f)
        {
            return 0f;
        }

        float t = outsideDistance / Mathf.Max(outsideFalloffDistance, Epsilon);
        t *= debtPaymentRatio;
        t = Mathf.Clamp01(t);

        if (!useSmoothStepDebtPayment)
        {
            return t;
        }

        return SmoothStep01(t);
    }

    private float SmoothStep01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
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

        DrawBoxGizmo(result);
        DrawTargetGizmo(result);
    }

    private void DrawBoxGizmo(RuntimeDebugState result)
    {
        float sideMin = boxCenterOffset.x - Mathf.Abs(boxHalfWidth);
        float sideMax = boxCenterOffset.x + Mathf.Abs(boxHalfWidth);

        float forwardMin = boxCenterOffset.y - Mathf.Abs(boxHalfDepth);
        float forwardMax = boxCenterOffset.y + Mathf.Abs(boxHalfDepth);

        Vector3 p0 =
            result.coreWorldPosition
            + result.sideAxis * sideMin
            + result.forwardAxis * forwardMin;

        Vector3 p1 =
            result.coreWorldPosition
            + result.sideAxis * sideMax
            + result.forwardAxis * forwardMin;

        Vector3 p2 =
            result.coreWorldPosition
            + result.sideAxis * sideMax
            + result.forwardAxis * forwardMax;

        Vector3 p3 =
            result.coreWorldPosition
            + result.sideAxis * sideMin
            + result.forwardAxis * forwardMax;

        Gizmos.color = boxColor;
        Gizmos.DrawLine(p0, p1);
        Gizmos.DrawLine(p1, p2);
        Gizmos.DrawLine(p2, p3);
        Gizmos.DrawLine(p3, p0);

        Gizmos.DrawLine(result.coreWorldPosition, result.coreWorldPosition + result.forwardAxis);
        Gizmos.DrawLine(result.coreWorldPosition, result.coreWorldPosition + result.sideAxis);
    }

    private void DrawTargetGizmo(RuntimeDebugState result)
    {
        Gizmos.color = targetColor;
        Gizmos.DrawLine(result.coreWorldPosition, result.targetWorldPosition);
        Gizmos.DrawSphere(result.targetWorldPosition, 0.08f);

        Gizmos.color = protectedPointColor;
        Gizmos.DrawLine(result.coreWorldPosition, result.protectedYawPointWorld);
        Gizmos.DrawSphere(result.protectedYawPointWorld, 0.07f);

        Gizmos.color = Color.white;
        Gizmos.DrawSphere(result.nearestBoxPointWorld, 0.05f);
    }
}