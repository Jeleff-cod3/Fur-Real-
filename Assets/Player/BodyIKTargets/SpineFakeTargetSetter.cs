using UnityEngine;

[DefaultExecutionOrder(-125)]
public class SpineFakeTargetSetter : MonoBehaviour
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

    public enum MaxReachSource
    {
        Manual,
        LimbSolverCumulativeBones
    }

    public enum MinReachSource
    {
        None,
        Manual,
        InitialFakeTargetDistanceFromCore,
        LimbSolverMinimumReach
    }

    public enum TargetNoClipRadiusSource
    {
        None,
        Manual,
        InitialFakeTargetDistanceFromCore
    }

    public enum BehindTargetMode
    {
        MirrorThroughCore,
        DirectTarget,
        Neutral
    }

    public enum ActiveRuleRegion
    {
        SafeBox,
        FrontBlend,
        LeftSideBlend,
        RightSideBlend,
        OutsideSpecialBoxes,
        Behind
    }

    public enum FrontBoxTargetMode
    {
        /*
         * Front/top box:
         * stay depth-centered,
         * track left/right.
         *
         * local X = real side
         * local Z = 0
         */
        TrackSide_ZeroForward,

        /*
         * Alternative:
         * stay side-centered,
         * track depth.
         *
         * local X = 0
         * local Z = real forward
         */
        TrackForward_ZeroSide
    }

    public enum PoleReferenceMode
    {
        FakeTarget,
        RealTarget
    }

    public enum PoleDirectionMode
    {
        SameLocalDirectionAsReference,
        OppositeLocalDirectionFromReference
    }

    [Header("Main References")]
    public Transform coreNode;

    [Tooltip("STATIC forward reference. Use a separate static transform in front of the core. Do not use the moving fake pole here.")]
    public Transform forwardPoleVector;

    [Tooltip("The real object / look target.")]
    public Transform realTarget;

    [Tooltip("The fake IK target object this script moves.")]
    public Transform fakeTarget;

    [Tooltip("The fake pole object this script moves.")]
    public Transform fakePole;

    [Header("Spine IK Tail Sync")]
    public NodeState spineTailNode;
    public bool autoUseSolverTailAsSpineTailNode = true;
    public bool moveSpineTailNodeToFakeTarget = true;

    [Header("Optional Solver Link")]
    public LimbSolver limbSolver;
    public bool autoUseSolverTailAsFakeTarget = true;
    public bool autoUseSolverStartAsCore = true;
    public bool assignFakePoleToSolverNodes = true;

    [Header("External Target")]
    public bool useExternalTargetPosition = false;
    public Vector3 externalTargetWorldPosition;

    [Header("Output Through Offset Nodes")]
    public bool writeTargetThroughOffsetNode = false;
    public OffsetPositioningNode targetOffsetNode;
    public int targetDynamicOffsetId = 20;

    public bool writePoleThroughOffsetNode = true;
    public OffsetPositioningNode poleOffsetNode;
    public int poleDynamicOffsetId = 21;

    [Header("Plane / Box Basis")]
    public PlaneNormalMode planeNormalMode = PlaneNormalMode.WorldVector;

    [Tooltip("Default Vector3.up means local behavior happens in X/Z and fixed height is Y.")]
    public Vector3 worldPlaneNormal = Vector3.up;

    public Transform directionTransform;

    public bool invertSideAxis = false;
    public bool invertForwardAxis = false;

    [Tooltip("Critical: capture the forward/side/normal axes once so the boxes do not turn toward the target.")]
    public bool useCapturedStaticBasis = true;

    [Tooltip("If true, the captured basis follows the current core position but does not rotate with the core/target.")]
    public bool basisFollowsCurrentCorePosition = true;

    [SerializeField]
    private bool hasCapturedBasis = false;

    [SerializeField]
    private Vector3 capturedCoreWorldPosition;

    [SerializeField]
    private Vector3 capturedPlaneNormal = Vector3.up;

    [SerializeField]
    private Vector3 capturedForwardAxis = Vector3.forward;

    [SerializeField]
    private Vector3 capturedSideAxis = Vector3.right;

    [Header("Fixed Heights")]
    [Tooltip("Fixed fake target height along the plane normal. With Vector3.up, this is Y.")]
    public float fakeTargetPlaneHeight = 0f;

    [Tooltip("Fixed fake pole height along the plane normal. With Vector3.up, this is Y.")]
    public float fakePolePlaneHeight = 0f;

    [Header("Reach")]
    public MaxReachSource maxReachSource = MaxReachSource.LimbSolverCumulativeBones;

    [Min(0.001f)]
    public float manualMaxReach = 3f;

    [Min(0f)]
    public float maxReachMultiplier = 0.98f;

    [Min(0f)]
    public float maxReachSafetyPadding = 0.03f;

    public MinReachSource minReachSource = MinReachSource.None;

    [Min(0f)]
    public float manualMinReach = 0f;

    [Min(0f)]
    public float minReachMultiplier = 1f;

    [Tooltip("Usually false. Direct/safe reaching should not be held away from zero unless you deliberately need default bend.")]
    public bool enforceMinimumReachOnDirectReach = false;

    [Tooltip("Usually false. Special boxes are allowed to request zero, but the target no-clip radius can still protect the solver.")]
    public bool enforceMinimumReachInsideSpecialBoxes = false;

    [Tooltip("Usually false so looking behind can remove default hunch.")]
    public bool enforceMinimumReachWhenBehind = false;

    [Header("Target No-Clip Ring")]
    [Tooltip("Keeps the fake target from crossing through the core. This prevents pole/core collapse during front/back and side/side transitions.")]
    public TargetNoClipRadiusSource targetNoClipRadiusSource = TargetNoClipRadiusSource.InitialFakeTargetDistanceFromCore;

    [Min(0f)]
    public float manualTargetNoClipRadius = 0.25f;

    [Min(0f)]
    public float targetNoClipRadiusMultiplier = 1f;

    [Tooltip("Caps the no-clip radius so it cannot accidentally consume the whole IK reach.")]
    [Range(0.01f, 0.95f)]
    public float maxNoClipRadiusAsReachFraction = 0.4f;

    [Tooltip("Stable local direction used when a rule asks for exact 0,0. X = side, Y = forward.")]
    public Vector2 zeroTargetFallbackDirection = new Vector2(0f, 1f);

    [Header("Safe Box")]
    public bool autoFitSafeBoxToReach = true;

    public float autoSafeBoxForwardStart = 0f;

    [Range(0.05f, 1f)]
    public float autoSafeBoxForwardEndMultiplier = 0.65f;

    [Range(0.01f, 1f)]
    public float autoSafeBoxHalfWidthRatio = 0.3f;

    public Vector2 manualSafeBoxCenter = new Vector2(0f, 1f);

    [Min(0.0001f)]
    public float manualSafeBoxHalfWidth = 0.8f;

    [Min(0.0001f)]
    public float manualSafeBoxHalfDepth = 1f;

    [Header("Front / Top Box")]
    public FrontBoxTargetMode frontBoxTargetMode = FrontBoxTargetMode.TrackSide_ZeroForward;

    [Tooltip("Distance beyond the safe-box front edge where front behavior becomes fully active.")]
    [Min(0.0001f)]
    public float frontBoxDepth = 1.5f;

    [Tooltip("Distance after the front box where front behavior fades back to direct reach.")]
    [Min(0f)]
    public float frontExitBlendDepth = 1.5f;

    [Tooltip("Extra side width allowed for front behavior.")]
    [Min(0f)]
    public float frontBoxSidePadding = 0f;

    [Tooltip("Distance outside front width where front behavior fades away.")]
    [Min(0f)]
    public float frontSideExitBlendDistance = 0.75f;

    [Tooltip("Extra fake-target height along the plane normal while the front box is blending around the no-clip arc.")]
    [Min(0f)]
    public float frontTargetHeightGive = 0.35f;

    [Tooltip("If true, height give peaks mid-blend and returns to zero once the front rule is fully active.")]
    public bool frontTargetHeightGiveOnlyDuringBlend = true;

    [Header("Side Boxes")]
    public bool useSideBoxes = true;

    [Tooltip("Distance beyond the safe-box side edge where side behavior becomes fully active.")]
    [Min(0.0001f)]
    public float sideBoxWidth = 1.5f;

    [Tooltip("Distance after the side box where side behavior fades back to direct reach.")]
    [Min(0f)]
    public float sideExitBlendWidth = 1.5f;

    [Tooltip("Extra forward/back depth allowed for side behavior.")]
    [Min(0f)]
    public float sideBoxForwardPadding = 0f;

    [Tooltip("Distance outside side depth where side behavior fades away.")]
    [Min(0f)]
    public float sideDepthExitBlendDistance = 0.75f;

    [Header("Special Box Arbitration")]
    [Tooltip("When front and side overlap at corners, choose one dominant rule instead of blending multiple rules together.")]
    public bool useExclusiveSpecialBoxRule = true;

    public bool preferFrontBoxOnTies = true;

    [Min(0f)]
    public float boxRuleTieBreakTolerance = 0.001f;

    [Header("Debt / Border Payment")]
    [Tooltip("Not a counter. Pure multiplier on distance-from-border blend.")]
    [Min(0f)]
    public float debtPaymentFactor = 1f;

    public bool smoothDebtPayment = true;

    [Header("Optional Output Smoothing")]
    [Tooltip("Default false. The angle/radius blending already avoids core clipping.")]
    public bool smoothOutputTarget = false;

    [Min(0.0001f)]
    public float outputSmoothTime = 0.05f;

    public float outputMaxSpeed = 100f;

    [Header("Behind Behavior")]
    [Tooltip("Behind means local forward/depth is below this value.")]
    public float behindForwardThreshold = 0f;

    [Tooltip("Depth range over which behind behavior blends in. Prevents front/back clipping.")]
    [Min(0f)]
    public float behindTransitionDepth = 0.75f;

    public BehindTargetMode behindTargetMode = BehindTargetMode.MirrorThroughCore;

    [Min(0f)]
    public float behindScale = 1f;

    [Header("Pole Placement")]
    public bool placePoleFromReference = true;

    [Tooltip("Usually FakeTarget. Use RealTarget only if you want the pole based on actual target direction, not fake-target behavior.")]
    public PoleReferenceMode poleReferenceMode = PoleReferenceMode.FakeTarget;

    [Tooltip("Usually Opposite. Toggle if your IK bends the wrong way.")]
    public PoleDirectionMode poleDirectionMode = PoleDirectionMode.OppositeLocalDirectionFromReference;

    [Tooltip("Recommended false. Fixed pole distance prevents the pole from hovering near the core.")]
    public bool scalePoleDistanceWithReferenceDistance = false;

    [Min(0f)]
    public float manualPoleDistance = 5f;

    [Min(0f)]
    public float poleDistanceMultiplier = 1f;

    [Min(0f)]
    public float minimumPoleDistance = 0.5f;

    [Tooltip("Stable local pole direction used when reference is 0,0.")]
    public Vector2 zeroPoleFallbackDirection = new Vector2(0f, 1f);

    [Header("Runtime")]
    public bool updateEveryFrame = true;

    [Tooltip("When writing through OffsetPositioningNode, apply immediately so the IK solver reads the same-frame target/pole positions.")]
    public bool applyOffsetWritesImmediately = true;

    [Tooltip("Spine-specific: lets the solver use its tail as a target handle, then restores that transform to the solved spine end after solving so attachments do not follow the high target handle.")]
    public bool restoreSolverTailToSolvedEndAfterSolving = true;

    [SerializeField]
    private RuntimeDebugState debugState;

    [Header("Debug Gizmos")]
    public bool drawDebugGizmos = true;
    public Color safeBoxColor = new Color(0.2f, 0.8f, 1f, 0.8f);
    public Color frontBoxColor = new Color(0.5f, 1f, 0.6f, 0.75f);
    public Color sideBoxColor = new Color(1f, 0.65f, 0.25f, 0.75f);
    public Color realTargetColor = new Color(1f, 0.75f, 0.1f, 0.9f);
    public Color fakeTargetColor = new Color(0.5f, 1f, 0.6f, 0.9f);
    public Color poleColor = new Color(1f, 0.4f, 0.9f, 0.9f);

    private Vector3 targetVelocity;
    private float initialFakeTargetDistanceFromCore = 0f;

    public RuntimeDebugState DebugState
    {
        get { return debugState; }
    }

    [System.Serializable]
    public struct RuntimeDebugState
    {
        public ActiveRuleRegion activeRegion;

        public Vector3 coreWorld;
        public Vector3 planeNormal;
        public Vector3 forwardAxis;
        public Vector3 sideAxis;

        public Vector2 realLocal;
        public Vector2 directReachLocal;
        public Vector2 frontRuleLocal;
        public Vector2 sideRuleLocal;
        public Vector2 behindRuleLocal;
        public Vector2 desiredLocal;
        public Vector2 actualLocal;
        public Vector2 poleReferenceLocal;

        public float maxReach;
        public float minReach;
        public float noClipRadius;

        public Vector2 safeBoxCenter;
        public float safeBoxHalfWidth;
        public float safeBoxHalfDepth;

        public Vector2 frontBoxCenter;
        public float frontBoxHalfWidth;
        public float frontBoxHalfDepth;

        public Vector2 leftSideBoxCenter;
        public Vector2 rightSideBoxCenter;
        public float sideBoxHalfWidth;
        public float sideBoxHalfDepth;

        public float frontWeight;
        public float sideWeight;
        public float behindWeight;
        public float frontTargetHeightGive;
        public float targetPlaneHeight;
        public float debtPayment;

        public bool targetIsBehind;

        public Vector3 realTargetWorld;
        public Vector3 desiredTargetWorld;
        public Vector3 finalFakeTargetWorld;
        public Vector3 finalFakePoleWorld;
    }

    private void Reset()
    {
        limbSolver = GetComponent<LimbSolver>();
    }

    private void Start()
    {
        ResolveReferences();

        if (limbSolver != null)
        {
            limbSolver.InitializeChainData();
        }

        CaptureInitialReachData();
        CaptureStaticBasis();

        if (assignFakePoleToSolverNodes && limbSolver != null && fakePole != null)
        {
            limbSolver.SetPoleForAllSolvableNodes(fakePole, true);
        }
    }

    private void Update()
    {
        if (updateEveryFrame)
        {
            EvaluateAndApply();
        }
    }

    [ContextMenu("Capture Static Box Basis")]
    public void CaptureStaticBasis()
    {
        ResolveReferences();

        if (coreNode == null)
        {
            hasCapturedBasis = false;
            return;
        }

        Vector3 normal = GetLivePlaneNormal();
        Vector3 forwardAxis = GetLiveForwardAxis(normal);

        if (forwardAxis.sqrMagnitude <= Epsilon)
        {
            hasCapturedBasis = false;
            return;
        }

        Vector3 sideAxis = Vector3.Cross(normal, forwardAxis);

        if (sideAxis.sqrMagnitude <= Epsilon)
        {
            hasCapturedBasis = false;
            return;
        }

        sideAxis.Normalize();

        if (invertSideAxis)
        {
            sideAxis = -sideAxis;
        }

        if (invertForwardAxis)
        {
            forwardAxis = -forwardAxis;
        }

        capturedCoreWorldPosition = coreNode.position;
        capturedPlaneNormal = normal.normalized;
        capturedForwardAxis = forwardAxis.normalized;
        capturedSideAxis = sideAxis.normalized;
        hasCapturedBasis = true;
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

    public bool EvaluateAndApply()
    {
        RuntimeDebugState state;

        if (!TryEvaluate(out state))
        {
            return false;
        }

        Vector3 finalTargetWorld = state.desiredTargetWorld;

        if (smoothOutputTarget && fakeTarget != null)
        {
            finalTargetWorld = Vector3.SmoothDamp(
                fakeTarget.position,
                state.desiredTargetWorld,
                ref targetVelocity,
                outputSmoothTime,
                outputMaxSpeed,
                Time.deltaTime
            );

            Vector2 smoothedLocal = WorldToLocalPlane(
                finalTargetWorld,
                state.coreWorld,
                state.sideAxis,
                state.forwardAxis
            );

            /*
             * Important:
             * Even smoothing is not allowed to pass through the core.
             */
            smoothedLocal = ApplyDistanceLimits(
                smoothedLocal,
                state.maxReach,
                0f,
                state.noClipRadius,
                GetStableFallbackDirection(smoothedLocal)
            );

            finalTargetWorld = LocalPlaneToWorld(
                smoothedLocal,
                state.coreWorld,
                state.sideAxis,
                state.forwardAxis,
                state.planeNormal,
                state.targetPlaneHeight
            );
        }
        else
        {
            targetVelocity = Vector3.zero;
        }

        Vector2 actualLocal = WorldToLocalPlane(
            finalTargetWorld,
            state.coreWorld,
            state.sideAxis,
            state.forwardAxis
        );

        Vector2 poleReferenceLocal = poleReferenceMode == PoleReferenceMode.RealTarget
            ? ApplyDistanceLimits(
                state.realLocal,
                state.maxReach,
                0f,
                state.noClipRadius,
                GetStableFallbackDirection(state.realLocal)
            )
            : actualLocal;

        Vector3 finalPoleWorld = CalculatePoleWorldPosition(
            poleReferenceLocal,
            state.coreWorld,
            state.sideAxis,
            state.forwardAxis,
            state.planeNormal
        );

        WriteWorldPosition(
            fakeTarget,
            targetOffsetNode,
            writeTargetThroughOffsetNode,
            targetDynamicOffsetId,
            finalTargetWorld
        );

        if (moveSpineTailNodeToFakeTarget && spineTailNode != null)
        {
            spineTailNode.transform.position = finalTargetWorld;
        }

        WriteWorldPosition(
            fakePole,
            poleOffsetNode,
            writePoleThroughOffsetNode,
            poleDynamicOffsetId,
            finalPoleWorld
        );

        state.actualLocal = actualLocal;
        state.poleReferenceLocal = poleReferenceLocal;
        state.finalFakeTargetWorld = finalTargetWorld;
        state.finalFakePoleWorld = finalPoleWorld;

        debugState = state;

        return true;
    }

    public bool TryEvaluate(out RuntimeDebugState state)
    {
        state = new RuntimeDebugState();

        ResolveReferences();

        if (coreNode == null || fakeTarget == null)
        {
            return false;
        }

        Vector3 realTargetWorld;

        if (useExternalTargetPosition)
        {
            realTargetWorld = externalTargetWorldPosition;
        }
        else
        {
            if (realTarget == null)
            {
                return false;
            }

            realTargetWorld = realTarget.position;
        }

        Vector3 coreWorld;
        Vector3 normal;
        Vector3 forwardAxis;
        Vector3 sideAxis;

        if (!GetStaticOrLiveBasis(out coreWorld, out normal, out forwardAxis, out sideAxis))
        {
            return false;
        }

        float maxReach = GetMaxReach();
        float minReach = GetMinReach(maxReach);
        float noClipRadius = GetTargetNoClipRadius(maxReach);

        Vector2 safeBoxCenter;
        float safeBoxHalfWidth;
        float safeBoxHalfDepth;

        GetEffectiveSafeBox(
            maxReach,
            out safeBoxCenter,
            out safeBoxHalfWidth,
            out safeBoxHalfDepth
        );

        Vector2 frontBoxCenter;
        float frontBoxHalfWidth;
        float frontBoxHalfDepth;

        GetFrontBox(
            safeBoxCenter,
            safeBoxHalfWidth,
            safeBoxHalfDepth,
            out frontBoxCenter,
            out frontBoxHalfWidth,
            out frontBoxHalfDepth
        );

        Vector2 leftSideBoxCenter;
        Vector2 rightSideBoxCenter;
        float sideBoxHalfWidth;
        float sideBoxHalfDepth;

        GetSideBoxes(
            safeBoxCenter,
            safeBoxHalfWidth,
            safeBoxHalfDepth,
            out leftSideBoxCenter,
            out rightSideBoxCenter,
            out sideBoxHalfWidth,
            out sideBoxHalfDepth
        );

        Vector2 realLocal = WorldToLocalPlane(
            realTargetWorld,
            coreWorld,
            sideAxis,
            forwardAxis
        );

        Vector2 neutralLocal = GetNeutralLocal(minReach);

        Vector2 directReachLocal = ApplyDistanceLimits(
            realLocal,
            maxReach,
            enforceMinimumReachOnDirectReach ? minReach : 0f,
            noClipRadius,
            GetStableFallbackDirection(realLocal)
        );

        Vector2 frontRuleLocal = ApplyDistanceLimits(
            BuildFrontRuleLocal(realLocal),
            maxReach,
            enforceMinimumReachInsideSpecialBoxes ? minReach : 0f,
            noClipRadius,
            GetStableFallbackDirection(BuildFrontRuleLocal(realLocal))
        );

        Vector2 sideRuleLocal = ApplyDistanceLimits(
            Vector2.zero,
            maxReach,
            enforceMinimumReachInsideSpecialBoxes ? minReach : 0f,
            noClipRadius,
            GetStableFallbackDirection(Vector2.zero)
        );

        Vector2 behindRuleLocal = ApplyDistanceLimits(
            EvaluateBehindTarget(realLocal, neutralLocal),
            maxReach,
            enforceMinimumReachWhenBehind ? minReach : 0f,
            noClipRadius,
            GetStableFallbackDirection(EvaluateBehindTarget(realLocal, neutralLocal))
        );

        Vector2 desiredLocal = directReachLocal;
        ActiveRuleRegion activeRegion = ActiveRuleRegion.OutsideSpecialBoxes;

        if (IsInsideBox(realLocal, safeBoxCenter, safeBoxHalfWidth, safeBoxHalfDepth))
        {
            activeRegion = ActiveRuleRegion.SafeBox;
        }

        float frontWeight = CalculateFrontWeight(
            realLocal,
            safeBoxCenter,
            safeBoxHalfWidth,
            safeBoxHalfDepth
        );

        bool isLeftSide;

        float sideWeight = CalculateSideWeight(
            realLocal,
            safeBoxCenter,
            safeBoxHalfWidth,
            safeBoxHalfDepth,
            out isLeftSide
        );

        ApplySpecialBoxArbitration(ref frontWeight, ref sideWeight);

        if (frontWeight > 0f)
        {
            desiredLocal = BlendLocalWithoutCrossingCore(
                desiredLocal,
                frontRuleLocal,
                frontWeight,
                noClipRadius
            );

            activeRegion = ActiveRuleRegion.FrontBlend;
        }

        if (sideWeight > 0f)
        {
            desiredLocal = BlendLocalWithoutCrossingCore(
                desiredLocal,
                sideRuleLocal,
                sideWeight,
                noClipRadius
            );

            activeRegion = isLeftSide
                ? ActiveRuleRegion.LeftSideBlend
                : ActiveRuleRegion.RightSideBlend;
        }

        float behindWeight = CalculateBehindWeight(realLocal);
        bool targetIsBehind = behindWeight > 0f;

        if (behindWeight > 0f)
        {
            desiredLocal = BlendLocalWithoutCrossingCore(
                desiredLocal,
                behindRuleLocal,
                behindWeight,
                noClipRadius
            );

            activeRegion = ActiveRuleRegion.Behind;
        }

        desiredLocal = ApplyDistanceLimits(
            desiredLocal,
            maxReach,
            0f,
            noClipRadius,
            GetStableFallbackDirection(desiredLocal)
        );

        float frontHeightGive = CalculateFrontTargetHeightGive(frontWeight);
        float targetPlaneHeight = fakeTargetPlaneHeight + frontHeightGive;

        Vector3 desiredTargetWorld = LocalPlaneToWorld(
            desiredLocal,
            coreWorld,
            sideAxis,
            forwardAxis,
            normal,
            targetPlaneHeight
        );

        Vector2 poleReferenceLocal = poleReferenceMode == PoleReferenceMode.RealTarget
            ? ApplyDistanceLimits(realLocal, maxReach, 0f, noClipRadius, GetStableFallbackDirection(realLocal))
            : desiredLocal;

        Vector3 poleWorld = CalculatePoleWorldPosition(
            poleReferenceLocal,
            coreWorld,
            sideAxis,
            forwardAxis,
            normal
        );

        state.activeRegion = activeRegion;

        state.coreWorld = coreWorld;
        state.planeNormal = normal;
        state.forwardAxis = forwardAxis;
        state.sideAxis = sideAxis;

        state.realLocal = realLocal;
        state.directReachLocal = directReachLocal;
        state.frontRuleLocal = frontRuleLocal;
        state.sideRuleLocal = sideRuleLocal;
        state.behindRuleLocal = behindRuleLocal;
        state.desiredLocal = desiredLocal;
        state.actualLocal = desiredLocal;
        state.poleReferenceLocal = poleReferenceLocal;

        state.maxReach = maxReach;
        state.minReach = minReach;
        state.noClipRadius = noClipRadius;

        state.safeBoxCenter = safeBoxCenter;
        state.safeBoxHalfWidth = safeBoxHalfWidth;
        state.safeBoxHalfDepth = safeBoxHalfDepth;

        state.frontBoxCenter = frontBoxCenter;
        state.frontBoxHalfWidth = frontBoxHalfWidth;
        state.frontBoxHalfDepth = frontBoxHalfDepth;

        state.leftSideBoxCenter = leftSideBoxCenter;
        state.rightSideBoxCenter = rightSideBoxCenter;
        state.sideBoxHalfWidth = sideBoxHalfWidth;
        state.sideBoxHalfDepth = sideBoxHalfDepth;

        state.frontWeight = frontWeight;
        state.sideWeight = sideWeight;
        state.behindWeight = behindWeight;
        state.frontTargetHeightGive = frontHeightGive;
        state.targetPlaneHeight = targetPlaneHeight;
        state.debtPayment = Mathf.Max(frontWeight, Mathf.Max(sideWeight, behindWeight));

        state.targetIsBehind = targetIsBehind;

        state.realTargetWorld = realTargetWorld;
        state.desiredTargetWorld = desiredTargetWorld;
        state.finalFakeTargetWorld = desiredTargetWorld;
        state.finalFakePoleWorld = poleWorld;

        return true;
    }

    private bool GetStaticOrLiveBasis(
        out Vector3 coreWorld,
        out Vector3 normal,
        out Vector3 forwardAxis,
        out Vector3 sideAxis
    )
    {
        if (useCapturedStaticBasis && hasCapturedBasis)
        {
            coreWorld = basisFollowsCurrentCorePosition && coreNode != null
                ? coreNode.position
                : capturedCoreWorldPosition;

            normal = capturedPlaneNormal;
            forwardAxis = capturedForwardAxis;
            sideAxis = capturedSideAxis;
            return true;
        }

        if (coreNode == null)
        {
            coreWorld = Vector3.zero;
            normal = Vector3.up;
            forwardAxis = Vector3.forward;
            sideAxis = Vector3.right;
            return false;
        }

        coreWorld = coreNode.position;
        normal = GetLivePlaneNormal();
        forwardAxis = GetLiveForwardAxis(normal);

        if (forwardAxis.sqrMagnitude <= Epsilon)
        {
            sideAxis = Vector3.right;
            return false;
        }

        sideAxis = Vector3.Cross(normal, forwardAxis);

        if (sideAxis.sqrMagnitude <= Epsilon)
        {
            return false;
        }

        sideAxis.Normalize();

        if (invertSideAxis)
        {
            sideAxis = -sideAxis;
        }

        if (invertForwardAxis)
        {
            forwardAxis = -forwardAxis;
        }

        return true;
    }

    private Vector2 BuildFrontRuleLocal(Vector2 realLocal)
    {
        if (frontBoxTargetMode == FrontBoxTargetMode.TrackForward_ZeroSide)
        {
            return new Vector2(0f, Mathf.Max(realLocal.y, 0f));
        }

        /*
         * Default:
         * front/top box tracks side only and holds depth at zero.
         */
        return new Vector2(realLocal.x, 0f);
    }

    private Vector2 BlendLocalWithoutCrossingCore(
        Vector2 from,
        Vector2 to,
        float t,
        float noClipRadius
    )
    {
        t = Mathf.Clamp01(t);

        if (noClipRadius <= Epsilon)
        {
            return Vector2.Lerp(from, to, t);
        }

        Vector2 fromSafe = ApplyDistanceLimits(
            from,
            float.MaxValue,
            0f,
            noClipRadius,
            GetStableFallbackDirection(from)
        );

        Vector2 toSafe = ApplyDistanceLimits(
            to,
            float.MaxValue,
            0f,
            noClipRadius,
            GetStableFallbackDirection(to)
        );

        float fromAngle = Mathf.Atan2(fromSafe.x, fromSafe.y) * Mathf.Rad2Deg;
        float toAngle = Mathf.Atan2(toSafe.x, toSafe.y) * Mathf.Rad2Deg;

        float blendedAngle = Mathf.LerpAngle(fromAngle, toAngle, t);
        float blendedMagnitude = Mathf.Lerp(fromSafe.magnitude, toSafe.magnitude, t);

        blendedMagnitude = Mathf.Max(blendedMagnitude, noClipRadius);

        float radians = blendedAngle * Mathf.Deg2Rad;

        return new Vector2(
            Mathf.Sin(radians) * blendedMagnitude,
            Mathf.Cos(radians) * blendedMagnitude
        );
    }

    private Vector2 ApplyDistanceLimits(
        Vector2 local,
        float maxReach,
        float ikMinReach,
        float noClipRadius,
        Vector2 fallbackDirection
    )
    {
        maxReach = Mathf.Max(0.001f, maxReach);

        float minAllowed = Mathf.Max(0f, ikMinReach, noClipRadius);
        minAllowed = Mathf.Min(minAllowed, maxReach * 0.999f);

        float magnitude = local.magnitude;

        Vector2 direction;

        if (magnitude <= Epsilon)
        {
            direction = fallbackDirection.sqrMagnitude > Epsilon
                ? fallbackDirection.normalized
                : Vector2.up;
        }
        else
        {
            direction = local / magnitude;
        }

        float clampedMagnitude = Mathf.Clamp(magnitude, minAllowed, maxReach);

        return direction * clampedMagnitude;
    }

    private Vector2 GetStableFallbackDirection(Vector2 preferred)
    {
        if (preferred.sqrMagnitude > Epsilon)
        {
            return preferred.normalized;
        }

        if (zeroTargetFallbackDirection.sqrMagnitude > Epsilon)
        {
            return zeroTargetFallbackDirection.normalized;
        }

        return Vector2.up;
    }

    private void ApplySpecialBoxArbitration(ref float frontWeight, ref float sideWeight)
    {
        if (!useExclusiveSpecialBoxRule || frontWeight <= 0f || sideWeight <= 0f)
        {
            return;
        }

        float difference = frontWeight - sideWeight;

        if (Mathf.Abs(difference) <= boxRuleTieBreakTolerance)
        {
            if (preferFrontBoxOnTies)
            {
                sideWeight = 0f;
            }
            else
            {
                frontWeight = 0f;
            }

            return;
        }

        if (difference > 0f)
        {
            sideWeight = 0f;
        }
        else
        {
            frontWeight = 0f;
        }
    }

    private float CalculateFrontWeight(
        Vector2 realLocal,
        Vector2 safeCenter,
        float safeHalfWidth,
        float safeHalfDepth
    )
    {
        float safeFrontEdge = safeCenter.y + safeHalfDepth;
        float distancePastSafeFront = realLocal.y - safeFrontEdge;

        if (distancePastSafeFront <= 0f)
        {
            return 0f;
        }

        float forwardWeight = CalculateEnterThenExitWeight(
            distancePastSafeFront,
            frontBoxDepth,
            frontExitBlendDepth
        );

        float allowedHalfWidth = safeHalfWidth + frontBoxSidePadding;

        float sideOverflow = Mathf.Max(
            0f,
            Mathf.Abs(realLocal.x - safeCenter.x) - allowedHalfWidth
        );

        float sideFade = CalculateInsideThenFadeWeight(
            sideOverflow,
            frontSideExitBlendDistance
        );

        return Mathf.Clamp01(forwardWeight * sideFade);
    }

    private float CalculateFrontTargetHeightGive(float frontWeight)
    {
        if (frontTargetHeightGive <= 0f || frontWeight <= 0f)
        {
            return 0f;
        }

        float t = SmoothStep01(frontWeight);

        if (frontTargetHeightGiveOnlyDuringBlend)
        {
            return frontTargetHeightGive * Mathf.Sin(t * Mathf.PI);
        }

        return frontTargetHeightGive * t;
    }

    private float CalculateSideWeight(
        Vector2 realLocal,
        Vector2 safeCenter,
        float safeHalfWidth,
        float safeHalfDepth,
        out bool isLeftSide
    )
    {
        isLeftSide = realLocal.x < safeCenter.x;

        if (!useSideBoxes)
        {
            return 0f;
        }

        float signedSide = realLocal.x - safeCenter.x;
        float absSide = Mathf.Abs(signedSide);

        if (absSide <= safeHalfWidth)
        {
            return 0f;
        }

        isLeftSide = signedSide < 0f;

        float distancePastSafeSide = absSide - safeHalfWidth;

        float sideWeight = CalculateEnterThenExitWeight(
            distancePastSafeSide,
            sideBoxWidth,
            sideExitBlendWidth
        );

        float allowedHalfDepth = safeHalfDepth + sideBoxForwardPadding;

        float depthOverflow = Mathf.Max(
            0f,
            Mathf.Abs(realLocal.y - safeCenter.y) - allowedHalfDepth
        );

        float depthFade = CalculateInsideThenFadeWeight(
            depthOverflow,
            sideDepthExitBlendDistance
        );

        return Mathf.Clamp01(sideWeight * depthFade);
    }

    private float CalculateBehindWeight(Vector2 realLocal)
    {
        float depthBehindThreshold = behindForwardThreshold - realLocal.y;

        if (depthBehindThreshold <= 0f)
        {
            return 0f;
        }

        if (behindTransitionDepth <= Epsilon)
        {
            return 1f;
        }

        return CalculatePayment01(depthBehindThreshold, behindTransitionDepth);
    }

    private float CalculateEnterThenExitWeight(
        float distancePastInnerBorder,
        float enterDistance,
        float exitDistance
    )
    {
        if (distancePastInnerBorder <= 0f)
        {
            return 0f;
        }

        float enterWeight = CalculatePayment01(
            distancePastInnerBorder,
            enterDistance
        );

        if (distancePastInnerBorder <= enterDistance)
        {
            return enterWeight;
        }

        float distancePastFull = distancePastInnerBorder - enterDistance;

        if (exitDistance <= Epsilon)
        {
            return 0f;
        }

        float exitFade = 1f - CalculatePayment01(
            distancePastFull,
            exitDistance
        );

        return Mathf.Clamp01(exitFade);
    }

    private float CalculateInsideThenFadeWeight(
        float outsideDistance,
        float fadeDistance
    )
    {
        if (outsideDistance <= 0f)
        {
            return 1f;
        }

        if (fadeDistance <= Epsilon)
        {
            return 0f;
        }

        return Mathf.Clamp01(
            1f - CalculatePayment01(outsideDistance, fadeDistance)
        );
    }

    private float CalculatePayment01(float distance, float fullPaymentDistance)
    {
        if (debtPaymentFactor <= 0f)
        {
            return 0f;
        }

        float t = distance / Mathf.Max(fullPaymentDistance, Epsilon);
        t *= debtPaymentFactor;
        t = Mathf.Clamp01(t);

        if (!smoothDebtPayment)
        {
            return t;
        }

        return SmoothStep01(t);
    }

    private Vector2 EvaluateBehindTarget(Vector2 realLocal, Vector2 neutralLocal)
    {
        switch (behindTargetMode)
        {
            case BehindTargetMode.DirectTarget:
                return realLocal * behindScale;

            case BehindTargetMode.Neutral:
                return neutralLocal;

            case BehindTargetMode.MirrorThroughCore:
            default:
                return -realLocal * behindScale;
        }
    }

    private Vector2 GetNeutralLocal(float minReach)
    {
        if (minReach <= Epsilon)
        {
            return Vector2.zero;
        }

        return new Vector2(0f, minReach);
    }

    private Vector3 CalculatePoleWorldPosition(
        Vector2 poleReferenceLocal,
        Vector3 coreWorld,
        Vector3 sideAxis,
        Vector3 forwardAxis,
        Vector3 normal
    )
    {
        if (!placePoleFromReference)
        {
            return fakePole != null ? fakePole.position : coreWorld - forwardAxis * manualPoleDistance;
        }

        Vector2 referenceDirection;

        if (poleReferenceLocal.sqrMagnitude > Epsilon)
        {
            referenceDirection = poleReferenceLocal.normalized;
        }
        else
        {
            referenceDirection = zeroPoleFallbackDirection.sqrMagnitude > Epsilon
                ? zeroPoleFallbackDirection.normalized
                : Vector2.up;
        }

        float distance;

        if (scalePoleDistanceWithReferenceDistance)
        {
            distance = Mathf.Max(
                minimumPoleDistance,
                poleReferenceLocal.magnitude * poleDistanceMultiplier
            );
        }
        else
        {
            distance = Mathf.Max(minimumPoleDistance, manualPoleDistance);
        }

        Vector2 poleLocal;

        if (poleDirectionMode == PoleDirectionMode.OppositeLocalDirectionFromReference)
        {
            poleLocal = -referenceDirection * distance;
        }
        else
        {
            poleLocal = referenceDirection * distance;
        }

        return LocalPlaneToWorld(
            poleLocal,
            coreWorld,
            sideAxis,
            forwardAxis,
            normal,
            fakePolePlaneHeight
        );
    }

    private void ResolveReferences()
    {
        if (limbSolver == null)
        {
            limbSolver = GetComponent<LimbSolver>();
        }

        if (limbSolver != null)
        {
            limbSolver.restoreTailToSolvedEndAfterSolving = restoreSolverTailToSolvedEndAfterSolving;

            if (autoUseSolverTailAsFakeTarget && fakeTarget == null && limbSolver.tail != null)
            {
                fakeTarget = limbSolver.tail.transform;
            }

            if (autoUseSolverStartAsCore && coreNode == null && limbSolver.start != null)
            {
                coreNode = limbSolver.start.transform;
            }

            if (autoUseSolverTailAsSpineTailNode && spineTailNode == null)
            {
                spineTailNode = limbSolver.tail;
            }
        }

        if (targetOffsetNode == null && fakeTarget != null)
        {
            targetOffsetNode = fakeTarget.GetComponent<OffsetPositioningNode>();
        }

        if (poleOffsetNode == null && fakePole != null)
        {
            poleOffsetNode = fakePole.GetComponent<OffsetPositioningNode>();
        }
    }

    private void CaptureInitialReachData()
    {
        if (coreNode == null || fakeTarget == null)
        {
            initialFakeTargetDistanceFromCore = 0f;
            return;
        }

        Vector3 normal = GetLivePlaneNormal();

        Vector3 planarTargetFromCore =
            Vector3.ProjectOnPlane(fakeTarget.position - coreNode.position, normal);

        initialFakeTargetDistanceFromCore = planarTargetFromCore.magnitude;
    }

    private float GetMaxReach()
    {
        float reach = manualMaxReach;

        if (maxReachSource == MaxReachSource.LimbSolverCumulativeBones && limbSolver != null)
        {
            if (!limbSolver.IsInitialized)
            {
                limbSolver.InitializeChainData();
            }

            if (limbSolver.MaxReach > Epsilon)
            {
                reach = limbSolver.MaxReach;
            }
        }

        reach *= maxReachMultiplier;
        reach -= maxReachSafetyPadding;

        return Mathf.Max(0.001f, reach);
    }

    private float GetMinReach(float maxReach)
    {
        float reach = 0f;

        switch (minReachSource)
        {
            case MinReachSource.Manual:
                reach = manualMinReach;
                break;

            case MinReachSource.InitialFakeTargetDistanceFromCore:
                reach = initialFakeTargetDistanceFromCore;
                break;

            case MinReachSource.LimbSolverMinimumReach:
                if (limbSolver != null)
                {
                    if (!limbSolver.IsInitialized)
                    {
                        limbSolver.InitializeChainData();
                    }

                    reach = limbSolver.MinReach;
                }
                break;

            case MinReachSource.None:
            default:
                reach = 0f;
                break;
        }

        reach *= minReachMultiplier;
        reach = Mathf.Clamp(reach, 0f, Mathf.Max(0f, maxReach - Epsilon));

        return reach;
    }

    private float GetTargetNoClipRadius(float maxReach)
    {
        float radius = 0f;

        switch (targetNoClipRadiusSource)
        {
            case TargetNoClipRadiusSource.Manual:
                radius = manualTargetNoClipRadius;
                break;

            case TargetNoClipRadiusSource.InitialFakeTargetDistanceFromCore:
                radius = initialFakeTargetDistanceFromCore;
                break;

            case TargetNoClipRadiusSource.None:
            default:
                radius = 0f;
                break;
        }

        radius *= targetNoClipRadiusMultiplier;

        float maxAllowed = Mathf.Max(0f, maxReach * maxNoClipRadiusAsReachFraction);

        return Mathf.Clamp(radius, 0f, maxAllowed);
    }

    private void GetEffectiveSafeBox(
        float maxReach,
        out Vector2 center,
        out float halfWidth,
        out float halfDepth
    )
    {
        if (autoFitSafeBoxToReach)
        {
            float forwardStart = autoSafeBoxForwardStart;

            float forwardEnd = Mathf.Max(
                forwardStart + 0.001f,
                maxReach * autoSafeBoxForwardEndMultiplier
            );

            center = new Vector2(
                0f,
                (forwardStart + forwardEnd) * 0.5f
            );

            halfDepth = Mathf.Max(0.0001f, (forwardEnd - forwardStart) * 0.5f);
            halfWidth = Mathf.Max(0.0001f, maxReach * autoSafeBoxHalfWidthRatio);
        }
        else
        {
            center = manualSafeBoxCenter;
            halfWidth = Mathf.Max(0.0001f, manualSafeBoxHalfWidth);
            halfDepth = Mathf.Max(0.0001f, manualSafeBoxHalfDepth);
        }
    }

    private void GetFrontBox(
        Vector2 safeCenter,
        float safeHalfWidth,
        float safeHalfDepth,
        out Vector2 center,
        out float halfWidth,
        out float halfDepth
    )
    {
        float safeFrontEdge = safeCenter.y + safeHalfDepth;

        halfDepth = Mathf.Max(0.0001f, frontBoxDepth * 0.5f);
        halfWidth = Mathf.Max(0.0001f, safeHalfWidth + frontBoxSidePadding);

        center = new Vector2(
            safeCenter.x,
            safeFrontEdge + halfDepth
        );
    }

    private void GetSideBoxes(
        Vector2 safeCenter,
        float safeHalfWidth,
        float safeHalfDepth,
        out Vector2 leftCenter,
        out Vector2 rightCenter,
        out float halfWidth,
        out float halfDepth
    )
    {
        float safeLeftEdge = safeCenter.x - safeHalfWidth;
        float safeRightEdge = safeCenter.x + safeHalfWidth;

        halfWidth = Mathf.Max(0.0001f, sideBoxWidth * 0.5f);
        halfDepth = Mathf.Max(0.0001f, safeHalfDepth + sideBoxForwardPadding);

        leftCenter = new Vector2(
            safeLeftEdge - halfWidth,
            safeCenter.y
        );

        rightCenter = new Vector2(
            safeRightEdge + halfWidth,
            safeCenter.y
        );
    }

    private bool IsInsideBox(
        Vector2 point,
        Vector2 center,
        float halfWidth,
        float halfDepth
    )
    {
        return Mathf.Abs(point.x - center.x) <= halfWidth &&
               Mathf.Abs(point.y - center.y) <= halfDepth;
    }

    private Vector3 GetLivePlaneNormal()
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

        if (normal.sqrMagnitude <= Epsilon)
        {
            normal = Vector3.up;
        }

        return normal.normalized;
    }

    private Vector3 GetLiveForwardAxis(Vector3 planeNormal)
    {
        if (coreNode != null && forwardPoleVector != null)
        {
            Vector3 poleDirection = forwardPoleVector.position - coreNode.position;
            Vector3 projectedPoleDirection = Vector3.ProjectOnPlane(poleDirection, planeNormal);

            if (projectedPoleDirection.sqrMagnitude > Epsilon)
            {
                return projectedPoleDirection.normalized;
            }
        }

        if (coreNode != null)
        {
            Vector3 projectedCoreForward = Vector3.ProjectOnPlane(coreNode.forward, planeNormal);

            if (projectedCoreForward.sqrMagnitude > Epsilon)
            {
                return projectedCoreForward.normalized;
            }
        }

        Vector3 fallback = Vector3.ProjectOnPlane(Vector3.forward, planeNormal);

        if (fallback.sqrMagnitude <= Epsilon)
        {
            fallback = Vector3.ProjectOnPlane(Vector3.right, planeNormal);
        }

        return fallback.sqrMagnitude > Epsilon ? fallback.normalized : Vector3.forward;
    }

    private Vector2 WorldToLocalPlane(
        Vector3 worldPosition,
        Vector3 coreWorld,
        Vector3 sideAxis,
        Vector3 forwardAxis
    )
    {
        Vector3 fromCore = worldPosition - coreWorld;

        return new Vector2(
            Vector3.Dot(fromCore, sideAxis),
            Vector3.Dot(fromCore, forwardAxis)
        );
    }

    private Vector3 LocalPlaneToWorld(
        Vector2 local,
        Vector3 coreWorld,
        Vector3 sideAxis,
        Vector3 forwardAxis,
        Vector3 normal,
        float planeHeight
    )
    {
        return coreWorld
               + sideAxis * local.x
               + forwardAxis * local.y
               + normal * planeHeight;
    }

    private float SmoothStep01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    private void WriteWorldPosition(
        Transform targetTransform,
        OffsetPositioningNode offsetNode,
        bool useOffsetNode,
        int dynamicOffsetId,
        Vector3 worldPosition
    )
    {
        if (useOffsetNode && offsetNode != null)
        {
            offsetNode.SetDynamicOffsetToReachWorldPosition(
                dynamicOffsetId,
                worldPosition
            );

            if (applyOffsetWritesImmediately)
            {
                offsetNode.ApplyPosition();
            }

            return;
        }

        if (targetTransform != null)
        {
            targetTransform.position = worldPosition;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawDebugGizmos)
        {
            return;
        }

        RuntimeDebugState state;

        if (!TryEvaluate(out state))
        {
            return;
        }

        DrawBoxGizmo(
            state.safeBoxCenter,
            state.safeBoxHalfWidth,
            state.safeBoxHalfDepth,
            state,
            safeBoxColor
        );

        DrawBoxGizmo(
            state.frontBoxCenter,
            state.frontBoxHalfWidth,
            state.frontBoxHalfDepth,
            state,
            frontBoxColor
        );

        if (useSideBoxes)
        {
            DrawBoxGizmo(
                state.leftSideBoxCenter,
                state.sideBoxHalfWidth,
                state.sideBoxHalfDepth,
                state,
                sideBoxColor
            );

            DrawBoxGizmo(
                state.rightSideBoxCenter,
                state.sideBoxHalfWidth,
                state.sideBoxHalfDepth,
                state,
                sideBoxColor
            );
        }

        Gizmos.color = realTargetColor;
        Gizmos.DrawSphere(state.realTargetWorld, 0.08f);
        Gizmos.DrawLine(state.coreWorld, state.realTargetWorld);

        Gizmos.color = fakeTargetColor;
        Gizmos.DrawSphere(state.finalFakeTargetWorld, 0.08f);
        Gizmos.DrawLine(state.coreWorld, state.finalFakeTargetWorld);

        Gizmos.color = poleColor;
        Gizmos.DrawSphere(state.finalFakePoleWorld, 0.08f);
        Gizmos.DrawLine(state.coreWorld, state.finalFakePoleWorld);
    }

    private void DrawBoxGizmo(
        Vector2 center,
        float halfWidth,
        float halfDepth,
        RuntimeDebugState state,
        Color color
    )
    {
        float sideMin = center.x - halfWidth;
        float sideMax = center.x + halfWidth;
        float forwardMin = center.y - halfDepth;
        float forwardMax = center.y + halfDepth;

        Vector3 p0 = LocalPlaneToWorld(
            new Vector2(sideMin, forwardMin),
            state.coreWorld,
            state.sideAxis,
            state.forwardAxis,
            state.planeNormal,
            fakeTargetPlaneHeight
        );

        Vector3 p1 = LocalPlaneToWorld(
            new Vector2(sideMax, forwardMin),
            state.coreWorld,
            state.sideAxis,
            state.forwardAxis,
            state.planeNormal,
            fakeTargetPlaneHeight
        );

        Vector3 p2 = LocalPlaneToWorld(
            new Vector2(sideMax, forwardMax),
            state.coreWorld,
            state.sideAxis,
            state.forwardAxis,
            state.planeNormal,
            fakeTargetPlaneHeight
        );

        Vector3 p3 = LocalPlaneToWorld(
            new Vector2(sideMin, forwardMax),
            state.coreWorld,
            state.sideAxis,
            state.forwardAxis,
            state.planeNormal,
            fakeTargetPlaneHeight
        );

        Gizmos.color = color;
        Gizmos.DrawLine(p0, p1);
        Gizmos.DrawLine(p1, p2);
        Gizmos.DrawLine(p2, p3);
        Gizmos.DrawLine(p3, p0);
    }
}
