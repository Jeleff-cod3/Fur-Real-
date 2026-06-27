using System;
using UnityEngine;

[DefaultExecutionOrder(-140)]
public sealed class ProceduralPlayerRig : MonoBehaviour
{
    private const float Epsilon = 0.0001f;
    private const int HeldArmOffsetId = 100;
    private const int ThrowArmOffsetId = 101;

    public enum CarryPose
    {
        None,
        TwoHandItem,
        OneHandWeapon
    }

    [Header("Runtime Targets")]
    [SerializeField] private Transform coreNode;
    [SerializeField] private Transform runTarget;
    [SerializeField] private Transform aimTarget;
    [SerializeField] private Transform weaponHolder;
    [SerializeField] private Transform itemHolder;

    [Header("Visual Scale")]
    [SerializeField] private float desiredVisualHeight = 10f;
    [SerializeField] private bool autoFitToCubeHeight = true;
    [SerializeField] private bool autoScaleAuthoredRigToCube = true;
    [SerializeField] private bool keepFeetAboveGround = true;
    [SerializeField] private float feetGroundClearance = 0.03f;
    [SerializeField] private float groundProbeHeight = 4f;
    [SerializeField] private float groundProbeDistance = 12f;
    [SerializeField] private LayerMask groundLayers = ~0;

    [Header("Held Pose")]
    [SerializeField] private float heldCenterPull = 0.5f;
    [SerializeField] private Vector3 heldLiftOffset = new Vector3(0f, -0.08f, 0.05f);
    [SerializeField] private Vector3 throwBackOffset = new Vector3(0f, 0.65f, -0.75f);
    [SerializeField] [Range(-1f, 1f)] private float weaponHandSideSign = -1f;
    [SerializeField] private float itemHandEdgePadding = 0.035f;
    [SerializeField] [Range(0f, 1f)] private float itemHoldForwardBias = 0.2f;
    [SerializeField] private float minimumHeldItemHalfWidth = 0.08f;

    [Header("Movement Tuning")]
    [SerializeField] private bool tuneLegControllerToWorldMoveSpeed = true;
    [SerializeField] private float tunedMoveSpeed = 6f;
    [SerializeField] [Range(0.05f, 1f)] private float walkSpeedRatio = 0.65f;
    [SerializeField] private float maxCoreSpeedMultiplier = 1.15f;

    [Header("Frame Scheduling / Performance")]
    [SerializeField] private bool manageChildEvaluationOrder = true;
    [SerializeField] private bool throttleRuntimeMeshRebuilds = true;
    [SerializeField] private float runtimeMeshRebuildInterval = 1f / 45f;
    [SerializeField] private int maxRuntimeRingSamples = 6;
    [SerializeField] private bool forceMeshRebuildAfterFinalSolve = true;
    [SerializeField] private bool solveAgainAfterLowerBodyOffsetRefresh = true;
    [SerializeField] private bool forceEveryMeshBuilderFinalFrame = true;

    [Header("Body Collision")]
    [SerializeField] private bool addCollisionToProceduralBodyMeshes = true;
    [SerializeField] private bool proceduralBodyMeshCollidersConvex = true;
    [SerializeField] private bool proceduralBodyMeshCollidersAreTriggers = false;

    [Header("Jump / Ground Support")]
    [SerializeField] private bool enableGroundSupport = true;
    [SerializeField] private bool liftCoreFromFootClipping = false;
    [SerializeField] [Range(0.4f, 1f)] private float desiredLegExtensionRatio = 0.86f;
    [SerializeField] private float jumpVelocity = 6.4f;
    [SerializeField] private float gravity = 12f;
    [SerializeField] private float airborneLegLift = 0.55f;
    [SerializeField] private float landingTolerance = 0.08f;
    [SerializeField] private float maxSnapDownDistance = 0.75f;
    [SerializeField] private float coreHeightFrequencyHz = 8.0f;
    [SerializeField] private float coreHeightDampingRatio = 1.55f;
    [SerializeField] private float maxCoreHeightAcceleration = 160f;
    [SerializeField] private float maxCoreHeightSpeed = 7.5f;
    [SerializeField] private float maxCoreHeightSubstepTime = 1f / 90f;

    private AutoRunLegPairController legController;
    private AutoRunMovementInput movementInput;
    private DirectTargetRotationAssigner[] rotationAssigners = Array.Empty<DirectTargetRotationAssigner>();
    private BodyRotationBoxAssigner[] bodyRotationAssigners = Array.Empty<BodyRotationBoxAssigner>();
    private SpineFakeTargetSetter[] spineTargetSetters = Array.Empty<SpineFakeTargetSetter>();
    private LazyIKTargetSetter[] armTargetSetters = Array.Empty<LazyIKTargetSetter>();
    private RotationAssigner[] rotationDrivers = Array.Empty<RotationAssigner>();
    private RotatableNode[] rotatableNodes = Array.Empty<RotatableNode>();
    private RotatableNodePair[] rotatableNodePairs = Array.Empty<RotatableNodePair>();
    private OffsetPositioningNode[] offsetNodes = Array.Empty<OffsetPositioningNode>();
    private LimbSolver[] limbSolvers = Array.Empty<LimbSolver>();
    private MeshingOffsetLoftMeshBuilder[] meshBuilders = Array.Empty<MeshingOffsetLoftMeshBuilder>();
    private ArmBinding[] arms = Array.Empty<ArmBinding>();

    private Vector3 previousCorePosition;
    private bool hasPreviousCorePosition;
    private float throwWindupUntil;
    private bool isLocalRig;
    private bool hasAppliedAuthoredScale;
    private CarryPose carryPose = CarryPose.None;
    private bool isGrounded = true;
    private float verticalVelocity;
    private float coreHeightVelocity;
    private bool jumpAirPoseRequested;
    private bool finalFrameEvaluationInProgress;

    public Transform CoreNode => coreNode != null ? coreNode : transform;
    public Transform RunTarget => runTarget;
    public Transform AimTarget => aimTarget;
    public Transform WeaponHolder => weaponHolder;
    public Transform ItemHolder => itemHolder;
    public bool HasLegController => legController != null;
    public Vector3 Velocity { get; private set; }
    public int ActionSequence { get; private set; }
    public string ActionState { get; private set; } = "idle";
    public bool IsGrounded => isGrounded;
    public CarryPose CurrentCarryPose => carryPose;
    public Vector3 LeftArmTargetWorld => arms.Length > 0 ? arms[0].CurrentTargetWorld : CoreNode.position;
    public Vector3 RightArmTargetWorld => arms.Length > 1 ? arms[1].CurrentTargetWorld : LeftArmTargetWorld;
    public Vector3 GaitForward => legController != null && legController.useExternalGaitForward
        ? legController.externalGaitForward
        : Vector3.ProjectOnPlane(CoreNode.forward, Vector3.up).normalized;

    private void Awake()
    {
        ResolveReferences();
        EnsureFrameDriver();
    }

    private void LateUpdate()
    {
        UpdateGroundSupport(Time.deltaTime);
        UpdateVelocity();
        UpdateHolders();
        RecenterRootOnCore();

        if (Time.time >= throwWindupUntil && ActionState == "throw_windup")
        {
            RefreshActionState();
        }
    }

    public void Configure(bool isLocal)
    {
        isLocalRig = isLocal;
        ResolveReferences();

        if (movementInput != null)
        {
            movementInput.enabled = false;
        }

        if (legController != null)
        {
            legController.coreMovementMode = AutoRunLegPairController.CoreMovementMode.ScriptMovesCore;
        }

        ConfigureMovementSpeed(tunedMoveSpeed);
        ConfigureJumpFeel();
        FitVisualsToCubeHeight();
    }

    private void ConfigureJumpFeel()
    {
        jumpVelocity = Mathf.Max(jumpVelocity, 7.2f);
        gravity = Mathf.Max(gravity, 16f);
        airborneLegLift = Mathf.Max(airborneLegLift, 0.65f);
        maxCoreHeightSpeed = Mathf.Max(maxCoreHeightSpeed, 9.5f);
        maxCoreHeightAcceleration = Mathf.Max(maxCoreHeightAcceleration, 190f);
    }

    public void ConfigureMovementSpeed(float moveSpeed)
    {
        tunedMoveSpeed = Mathf.Max(0.01f, moveSpeed);

        if (!tuneLegControllerToWorldMoveSpeed)
        {
            return;
        }

        ResolveReferences();

        if (legController == null)
        {
            return;
        }

        legController.runSpeed = tunedMoveSpeed;
        legController.walkSpeed = tunedMoveSpeed * walkSpeedRatio;
        legController.maxCoreSpeed = tunedMoveSpeed * Mathf.Max(1f, maxCoreSpeedMultiplier);

        // Final gait tuning: normal locomotion is one committed, alternating footstep at a
        // time. Stride distance grows with speed; cadence stays readable and does not respond
        // to tiny frame deltas.
        legController.fastStepDuration = 0.28f;
        legController.slowStepDuration = 0.46f;
        legController.minAdaptiveStepDuration = 0.18f;
        legController.maxAdaptiveStepDuration = 0.34f;
        legController.minStepInterval = 0.34f;
        legController.idleCorrectionStepInterval = 1.25f;

        legController.moveAlongCoreForwardAfterTurning = false;
        legController.rotateCoreTowardRunTarget = false;
        legController.rotateGaitRotationCoreTowardForward = true;
        legController.useAirbornePose = true;
        legController.oneStepAtATime = true;
        legController.useDeterministicHomeStepping = false;
        legController.placeTargetsFromLegStarts = true;
        legController.useStaticPoleAsLegForward = true;
        legController.chooseLowestGroundAroundStep = false;
        legController.clampTargetsWithLimbSolver = true;
        legController.allowMomentumCarry = false;
        legController.enforceStartupLeadingLegStep = false;
        legController.retargetActiveMovingSteps = false;
        legController.enforceBodyDistanceReachForFeet = false;
        legController.strictAlternatingPlantedGait = true;
        legController.syncLegStartNodesWithCoreDelta = false;
        legController.authoritativeOffsetNodesForRotatedLegAssembly = true;
        legController.rebuildFullSpineSyncListAtRuntime = true;
        legController.ignoreFakeLagForEmergencyStep = true;
        legController.maxDesiredMovingHomeReachRatio = 0.92f;
        legController.stationaryFakeTargetSettleReachRatio = 0.045f;
        legController.useSingleRuleAnticipatoryGait = true;
        legController.stableBehindTriggerReachRatio = 0.14f;
        legController.stableForcedBehindReachRatio = 0.32f;
        legController.stableStartupNoAheadSupportReachRatio = 0.18f;
        legController.stableLandingAheadReachRatio = 0.76f;
        legController.stableSpeedAddedAheadReachRatio = 0.20f;
        legController.stableSideLaneReachRatio = 0.30f;
        legController.stableSlowStepCadence = 0.52f;
        legController.stableFastStepCadence = 0.34f;
        legController.stableMinSwingDuration = 0.16f;
        legController.stableMaxSwingDuration = 0.31f;
        legController.stableMinimumStepTravelReachRatio = 0.46f;
        legController.commitRealTargetAtStepStart = true;
        legController.hardApplyLowerBodyYawOffsets = true;

        // Snappy, critically-damped core velocity. Momentum exists for stride size, not UFO drift.
        legController.momentumBuildPerSecond = Mathf.Clamp(legController.momentumBuildPerSecond, 5.5f, 9.0f);
        legController.momentumDecayPerSecond = Mathf.Max(legController.momentumDecayPerSecond, 14.0f);
        legController.coreVelocityFrequencyHz = Mathf.Clamp(legController.coreVelocityFrequencyHz, 7.0f, 10.0f);
        legController.coreVelocityDampingRatio = Mathf.Max(legController.coreVelocityDampingRatio, 1.45f);
        legController.maxCoreAcceleration = Mathf.Max(legController.maxCoreAcceleration, tunedMoveSpeed * 16f);

        // Larger stride at speed, not more frequent stride.
        legController.speedLookAheadTime = 0.56f;
        legController.movingStepAnticipationTime = 0.48f;
        legController.minSpeedStepReachRatio = Mathf.Clamp(legController.minSpeedStepReachRatio, 0.26f, 0.38f);
        legController.maxSpeedStepReachRatio = Mathf.Clamp(legController.maxSpeedStepReachRatio, 0.74f, 0.88f);
        legController.maxHomeSpeedLeadReachRatio = 0.60f;
        legController.footTargetSpeedMultiplier = 5.5f;
        legController.homeStepTriggerReachRatio = 0.90f;
        legController.idleCorrectionStepTriggerReachRatio = 0.55f;
        legController.minimumVisibleStepReachRatio = 0.60f;
        legController.movingStepForwardBiasReachRatio = Mathf.Clamp(legController.movingStepForwardBiasReachRatio, 0.34f, 0.48f);
        legController.minimumPlanarReachRatioForFootTargets = 0f;
        legController.baseStepReachRatio = 0.64f;
        legController.momentumStepReachRatio = 0.46f;
        legController.movementDebtStepTriggerReachRatio = 0.20f;
        legController.movementBlockStepInfluence = Mathf.Clamp(legController.movementBlockStepInfluence, 0.45f, 0.75f);
        legController.minStepReachRatio = 0.56f;
        legController.maxStepReachRatio = 1.10f;
        legController.microStepReachRatio = 0.62f;
        legController.smallStepReachRatio = 0.78f;
        legController.mediumStepReachRatio = 0.96f;
        legController.fullStepReachRatio = 1.14f;

        // Do not inflate the leg chain. The third pass forced 1.8x here and made the legs
        // absurdly long; keep a small natural surplus so knees bend without turning the
        // lower body into trailing stilts.
        legController.runtimeLegLengthMultiplier = Mathf.Clamp(legController.runtimeLegLengthMultiplier, 1.10f, 1.18f);
        legController.kneeDefaultBendAngle = Mathf.Clamp(legController.kneeDefaultBendAngle, 46f, 58f);
        legController.legReachMultiplier = Mathf.Clamp(legController.legReachMultiplier, 0.94f, 0.99f);
        legController.forceStepBeforeFootExceedsReach = true;
        // Emergency reach correction is only a hard safety valve now. Normal walking is the
        // strict planted-lead alternating gait above, not repeated reach-correction snaps.
        legController.emergencyStepStartReachRatio = 0.98f;
        legController.emergencyRealTargetClampReachRatio = 0.96f;
        legController.allowEmergencyStepWhileOtherLegSteps = false;
        legController.placeMovingStepsFromBodyCenter = true;
        legController.useMovementDirectionForMovingStepForward = true;

        legController.baseStepHeightReachRatio = 0.18f;
        legController.momentumStepHeightReachRatio = 0.10f;
        legController.stepLengthHeightInfluence = 0.24f;
        legController.plantedLeadBehindStepReachRatio = 0.18f;
        legController.forcedLeadBehindStepReachRatio = 0.36f;
        legController.largeStepForwardReachRatio = 0.78f;
        legController.speedAddedStepForwardReachRatio = 0.34f;
        legController.sideStepLaneReachRatio = 0.32f;
        legController.minimumStepDistanceBeforeStartReachRatio = 0.46f;
        legController.plantedGroundSnapTolerance = Mathf.Max(legController.plantedGroundSnapTolerance, 0.05f);
        legController.snapFakeTargetDuringActiveSteps = false;
        legController.legFakeTargetFrequencyHz = Mathf.Max(legController.legFakeTargetFrequencyHz, 18f);
        legController.legFakeTargetSpeedFrequencyBoostHz = Mathf.Max(legController.legFakeTargetSpeedFrequencyBoostHz, 18f);
        legController.fakeTargetLagCatchupReachPerSecond = Mathf.Max(legController.fakeTargetLagCatchupReachPerSecond, 16f);
        legController.dynamicLegFakeTargetSpeedMultiplier = Mathf.Max(legController.dynamicLegFakeTargetSpeedMultiplier, 8.0f);
        legController.dynamicLegFakeTargetAccelerationMultiplier = Mathf.Max(legController.dynamicLegFakeTargetAccelerationMultiplier, 28f);
    }

    public void PlaceCoreAt(Vector3 worldPosition)
    {
        ResolveReferences();

        Transform core = CoreNode;
        Vector3 delta = worldPosition - core.position;
        transform.position += delta;

        if (core != transform)
        {
            core.position = worldPosition;
        }

        SetRunTarget(worldPosition);
        SetAimTarget(worldPosition + transform.forward);
        previousCorePosition = core.position;
        hasPreviousCorePosition = true;
    }

    public void FitVisualsToCubeHeight()
    {
        if (!autoFitToCubeHeight || desiredVisualHeight <= Epsilon)
        {
            return;
        }

        if (autoScaleAuthoredRigToCube)
        {
            if (!hasAppliedAuthoredScale)
            {
                ScaleAuthoredRigToCubeHeight();
            }

            return;
        }

        Bounds bounds;
        if (!TryGetRendererBounds(out bounds) || bounds.size.y <= Epsilon)
        {
            return;
        }

        float scale = desiredVisualHeight / bounds.size.y;
        transform.localScale *= scale;
    }

    public void SetRunTarget(Vector3 worldPosition)
    {
        ResolveReferences();
        if (runTarget != null)
        {
            runTarget.position = worldPosition;
        }
    }

    public void SetGaitForward(Vector3 worldForward)
    {
        ResolveReferences();

        if (legController != null)
        {
            legController.SetExternalGaitForward(worldForward, true);
        }
    }

    public void SetAimTarget(Vector3 worldPosition)
    {
        ResolveReferences();

        if (aimTarget != null)
        {
            aimTarget.position = worldPosition;
        }

        for (int i = 0; i < rotationAssigners.Length; i++)
        {
            DirectTargetRotationAssigner assigner = rotationAssigners[i];
            if (assigner != null && !IsGaitRotationTargetAssigner(assigner))
            {
                assigner.SetExternalTargetWorldPosition(worldPosition);
            }
        }

        for (int i = 0; i < bodyRotationAssigners.Length; i++)
        {
            if (bodyRotationAssigners[i] != null)
            {
                bodyRotationAssigners[i].SetExternalTargetWorldPosition(worldPosition);
            }
        }

        for (int i = 0; i < spineTargetSetters.Length; i++)
        {
            if (spineTargetSetters[i] != null)
            {
                spineTargetSetters[i].SetExternalTargetWorldPosition(worldPosition);
            }
        }
    }

    public void ApplyHeldPose(bool isHolding)
    {
        ApplyCarryPose(isHolding ? CarryPose.TwoHandItem : CarryPose.None);
    }

    public void ApplyCarryPose(CarryPose nextCarryPose)
    {
        ResolveReferences();

        carryPose = nextCarryPose;
        bool isThrowing = Time.time < throwWindupUntil;
        RefreshActionState();

        float heldItemHalfWidth = carryPose == CarryPose.TwoHandItem
            ? GetHeldItemHalfWidthAlongBodyRight()
            : 0f;

        for (int i = 0; i < arms.Length; i++)
        {
            arms[i].ApplyPoseOffset(
                carryPose,
                isThrowing,
                heldCenterPull,
                heldLiftOffset,
                throwBackOffset,
                heldItemHalfWidth,
                itemHoldForwardBias,
                weaponHandSideSign,
                itemHolder != null ? itemHolder.position : CoreNode.position);
        }
    }

    public void PlayThrowWindup(float duration, Vector3 throwDirection)
    {
        throwWindupUntil = Time.time + Mathf.Max(0f, duration);
        carryPose = CarryPose.OneHandWeapon;
        ActionState = "throw_windup";
        ActionSequence++;

        if (throwDirection.sqrMagnitude > Epsilon)
        {
            SetAimTarget(CoreNode.position + throwDirection.normalized * 4f);
        }
    }

    public bool RequestJump()
    {
        ResolveReferences();

        if (!enableGroundSupport || !isGrounded)
        {
            return false;
        }

        verticalVelocity = Mathf.Max(verticalVelocity, jumpVelocity);
        coreHeightVelocity = verticalVelocity * 0.25f;
        isGrounded = false;
        jumpAirPoseRequested = true;
        ActionSequence++;

        if (legController != null)
        {
            legController.SetAirborneLegPose(true, airborneLegLift);
        }

        return true;
    }

    public void ApplyRemoteArmTargets(Vector3 leftArmTarget, Vector3 rightArmTarget)
    {
        ResolveReferences();

        if (arms.Length > 0)
        {
            arms[0].SetExternalTarget(leftArmTarget);
        }

        if (arms.Length > 1)
        {
            arms[1].SetExternalTarget(rightArmTarget);
        }
    }

    public void UseLocalArmTargets()
    {
        ResolveReferences();

        for (int i = 0; i < arms.Length; i++)
        {
            arms[i].UseTransformTarget();
        }
    }

    public void ReconcileCoreToward(Vector3 networkPosition, Quaternion networkRotation, float lerpSpeed, float snapDistance)
    {
        Transform core = CoreNode;
        float distance = Vector3.Distance(core.position, networkPosition);

        if (distance > snapDistance)
        {
            core.position = networkPosition;
        }
        else
        {
            core.position = Vector3.Lerp(core.position, networkPosition, Time.deltaTime * lerpSpeed);
        }

        core.rotation = Quaternion.Slerp(core.rotation, networkRotation, Time.deltaTime * lerpSpeed);
    }

    private void RecenterRootOnCore()
    {
        Transform core = CoreNode;
        if (core == transform)
        {
            return;
        }

        Vector3 delta = core.position - transform.position;
        if (delta.sqrMagnitude <= Epsilon * Epsilon)
        {
            return;
        }

        Transform[] children = new Transform[transform.childCount];
        Vector3[] childPositions = new Vector3[children.Length];

        for (int i = 0; i < children.Length; i++)
        {
            children[i] = transform.GetChild(i);
            childPositions[i] = children[i].position;
        }

        transform.position += delta;

        for (int i = 0; i < children.Length; i++)
        {
            if (children[i] != null)
            {
                children[i].position = childPositions[i];
            }
        }
    }

    private void ScaleAuthoredRigToCubeHeight()
    {
        Bounds bounds;
        if (!TryGetRendererBounds(out bounds) || bounds.size.y <= Epsilon)
        {
            return;
        }

        float visualScale = desiredVisualHeight / bounds.size.y;
        float existingRootScale = GetSmallestAbsRootScale();
        float authoredValueScale = visualScale * existingRootScale;

        if (Mathf.Abs(1f - visualScale) <= 0.01f &&
            Mathf.Abs(1f - authoredValueScale) <= 0.01f)
        {
            hasAppliedAuthoredScale = true;
            return;
        }

        Vector3 pivot = CoreNode.position;
        ScaleTransformTreeAroundPivot(transform, pivot, visualScale);
        ScaleAuthoredOffsetNodes(authoredValueScale);
        ScaleMeshingOffsetNodes(authoredValueScale);
        ScaleNodeStateLengths(authoredValueScale);
        ScaleLegControllerValues(authoredValueScale);
        ScaleArmAndSpineValues(authoredValueScale);
        ScaleBodyRotationValues(authoredValueScale);
        ScaleActionOffsets(authoredValueScale);
        ReinitializeScaledIkSystems();
        hasAppliedAuthoredScale = true;
    }

    private float GetSmallestAbsRootScale()
    {
        Vector3 scale = transform.localScale;
        return Mathf.Max(
            Epsilon,
            Mathf.Min(
                Mathf.Abs(scale.x),
                Mathf.Min(Mathf.Abs(scale.y), Mathf.Abs(scale.z))));
    }

    private void ScaleTransformTreeAroundPivot(Transform root, Vector3 pivot, float scale)
    {
        Transform[] nodes = root.GetComponentsInChildren<Transform>(true);
        Vector3[] originalPositions = new Vector3[nodes.Length];

        for (int i = 0; i < nodes.Length; i++)
        {
            originalPositions[i] = nodes[i].position;
        }

        root.localScale *= scale;

        for (int i = 0; i < nodes.Length; i++)
        {
            Transform node = nodes[i];
            if (node != null)
            {
                node.position = pivot + (originalPositions[i] - pivot) * scale;
            }
        }
    }

    private void ScaleAuthoredOffsetNodes(float scale)
    {
        OffsetPositioningNode[] offsetNodes = GetComponentsInChildren<OffsetPositioningNode>(true);
        for (int i = 0; i < offsetNodes.Length; i++)
        {
            if (offsetNodes[i] == null)
            {
                continue;
            }

            offsetNodes[i].staticOffset *= scale;

            for (int j = 0; j < offsetNodes[i].DynamicOffsets.Count; j++)
            {
                OffsetPositioningNode.DynamicOffsetEntry entry = offsetNodes[i].DynamicOffsets[j];
                offsetNodes[i].SetDynamicOffset(entry.id, entry.value * scale);
            }

            offsetNodes[i].debugLogging = false;
        }
    }

    public void ForceFinalRigEvaluation(float dt)
    {
        if (finalFrameEvaluationInProgress || !isActiveAndEnabled)
        {
            return;
        }

        finalFrameEvaluationInProgress = true;

        try
        {
            ResolveReferences();
            ConfigureChildEvaluationOrder();
            RecenterRootOnCore();
            EvaluateRotationTargets();
            ApplyRotationDrivers();
            ApplyOffsetNodesNow();
            if (legController != null)
            {
                legController.RefreshRotatedLegAssemblyAfterOffsetApply();
                EvaluateGaitRotationTargets();
                ApplyRotationDrivers();
                ApplyOffsetNodesNow();
            }
            EvaluateSpineTargets();
            EvaluateArmTargets(dt);
            ApplyOffsetNodesNow();
            ApplySolversNow();
            if (legController != null)
            {
                legController.RefreshRotatedLegAssemblyAfterOffsetApply();
                EvaluateGaitRotationTargets();
                ApplyRotationDrivers();
                ApplyOffsetNodesNow();
            }
            if (solveAgainAfterLowerBodyOffsetRefresh)
            {
                ApplySolversNow();
                ApplyNonIkOffsetNodesNow();
            }
            RebuildRuntimeMeshesNow(dt);
        }
        finally
        {
            finalFrameEvaluationInProgress = false;
        }
    }

    private void EvaluateRotationTargets()
    {
        for (int i = 0; i < rotationAssigners.Length; i++)
        {
            DirectTargetRotationAssigner assigner = rotationAssigners[i];
            if (assigner != null && assigner.isActiveAndEnabled)
            {
                assigner.CalculateAndAssign();
            }
        }

        for (int i = 0; i < bodyRotationAssigners.Length; i++)
        {
            if (bodyRotationAssigners[i] != null && bodyRotationAssigners[i].isActiveAndEnabled)
            {
                bodyRotationAssigners[i].CalculateAndAssign();
            }
        }
    }

    private bool IsGaitRotationTargetAssigner(DirectTargetRotationAssigner assigner)
    {
        return assigner != null &&
               legController != null &&
               legController.gaitRotationAssigner != null &&
               assigner.rotationAssigner == legController.gaitRotationAssigner;
    }

    private void EvaluateGaitRotationTargets()
    {
        for (int i = 0; i < rotationAssigners.Length; i++)
        {
            DirectTargetRotationAssigner assigner = rotationAssigners[i];
            if (assigner != null && assigner.isActiveAndEnabled && IsGaitRotationTargetAssigner(assigner))
            {
                assigner.CalculateAndAssign();
            }
        }
    }

    private void SortOffsetNodesParentFirst(OffsetPositioningNode[] nodes)
    {
        if (nodes == null || nodes.Length <= 1)
        {
            return;
        }

        Array.Sort(nodes, (a, b) => GetOffsetNodeDependencyDepth(a).CompareTo(GetOffsetNodeDependencyDepth(b)));
    }

    private int GetOffsetNodeDependencyDepth(OffsetPositioningNode node)
    {
        if (node == null)
        {
            return 0;
        }

        int depth = 0;
        Transform parent = node.parentNode;
        int guard = 0;
        while (parent != null && guard < 64)
        {
            guard++;
            depth++;
            OffsetPositioningNode parentOffset = parent.GetComponent<OffsetPositioningNode>();
            if (parentOffset != null && parentOffset.parentNode != parent)
            {
                parent = parentOffset.parentNode;
            }
            else
            {
                parent = parent.parent;
            }
        }

        return depth;
    }

    private void ApplyRotationDrivers()
    {
        for (int i = 0; i < rotationDrivers.Length; i++)
        {
            RotationAssigner driver = rotationDrivers[i];
            if (driver != null && driver.isActiveAndEnabled)
            {
                driver.ApplyRotation(driver.inputRotationDegrees);
            }
        }
    }

    private void ApplyOffsetNodesNow()
    {
        for (int i = 0; i < offsetNodes.Length; i++)
        {
            OffsetPositioningNode node = offsetNodes[i];
            if (node != null && node.isActiveAndEnabled)
            {
                node.ApplyPosition();
            }
        }
    }

    private void ApplyNonIkOffsetNodesNow()
    {
        for (int i = 0; i < offsetNodes.Length; i++)
        {
            OffsetPositioningNode node = offsetNodes[i];
            if (node == null || !node.isActiveAndEnabled || node.GetComponent<NodeState>() != null)
            {
                continue;
            }

            node.ApplyPosition();
        }
    }

    private void EvaluateSpineTargets()
    {
        for (int i = 0; i < spineTargetSetters.Length; i++)
        {
            SpineFakeTargetSetter setter = spineTargetSetters[i];
            if (setter != null && setter.isActiveAndEnabled)
            {
                setter.EvaluateAndApply();
            }
        }
    }

    private void EvaluateArmTargets(float dt)
    {
        for (int i = 0; i < armTargetSetters.Length; i++)
        {
            LazyIKTargetSetter setter = armTargetSetters[i];
            if (setter != null && setter.isActiveAndEnabled)
            {
                setter.EvaluateAndApply(dt);
            }
        }
    }

    private void ApplySolversNow()
    {
        for (int i = 0; i < limbSolvers.Length; i++)
        {
            LimbSolver solver = limbSolvers[i];
            if (solver != null && solver.isActiveAndEnabled)
            {
                solver.Apply();
            }
        }
    }

    private void RebuildRuntimeMeshesNow(float dt)
    {
        for (int i = 0; i < meshBuilders.Length; i++)
        {
            MeshingOffsetLoftMeshBuilder builder = meshBuilders[i];
            if (builder != null && builder.isActiveAndEnabled &&
                (builder.rebuildEveryFrameInPlayMode || forceEveryMeshBuilderFinalFrame))
            {
                builder.RebuildMeshForRuntime(dt, forceMeshRebuildAfterFinalSolve);
            }
        }
    }

    private void ScaleMeshingOffsetNodes(float scale)
    {
        MeshingOffsetNode[] meshingNodes = GetComponentsInChildren<MeshingOffsetNode>(true);
        for (int i = 0; i < meshingNodes.Length; i++)
        {
            MeshingOffsetNode node = meshingNodes[i];
            if (node == null || node.offsets == null)
            {
                continue;
            }

            for (int j = 0; j < node.offsets.Count; j++)
            {
                MeshingOffsetNode.MeshingOffsetEntry entry = node.offsets[j];
                if (entry == null)
                {
                    continue;
                }

                entry.baseOffset *= scale;
                entry.rotationOffset *= scale;
            }

            node.debugLogging = false;
        }

        MeshingOffsetLoftMeshBuilder[] builders = GetComponentsInChildren<MeshingOffsetLoftMeshBuilder>(true);
        for (int i = 0; i < builders.Length; i++)
        {
            MeshingOffsetLoftMeshBuilder builder = builders[i];
            if (builder == null)
            {
                continue;
            }

            builder.radialPadding *= scale;
            builder.minimumFallbackRadius *= scale;
            builder.maxOffsetDistanceFromParent *= scale;
            builder.maxBridgeDistance *= scale;

            if (builder.buildOnStart || builder.rebuildEveryFrameInPlayMode)
            {
                builder.RebuildMesh();
            }
        }
    }

    private void ScaleNodeStateLengths(float scale)
    {
        NodeState[] nodeStates = GetComponentsInChildren<NodeState>(true);
        for (int i = 0; i < nodeStates.Length; i++)
        {
            if (nodeStates[i] == null)
            {
                continue;
            }

            nodeStates[i].Mylength *= scale;
        }
    }

    private void ScaleLegControllerValues(float scale)
    {
        if (legController == null)
        {
            return;
        }

        legController.ScaleRuntimeLegDimensions(scale);
        legController.legFakeTargetSettleDistance *= scale;
        legController.plantedGroundSnapTolerance *= scale;
        legController.hardReachTriggerTolerance *= scale;
        legController.minFootGroundRayHeight = Mathf.Max(legController.minFootGroundRayHeight, 2f);
        legController.minFootGroundRayDistance = Mathf.Max(legController.minFootGroundRayDistance, 12f);
    }

    private void ScaleArmAndSpineValues(float scale)
    {
        for (int i = 0; i < armTargetSetters.Length; i++)
        {
            LazyIKTargetSetter setter = armTargetSetters[i];
            if (setter == null)
            {
                continue;
            }

            setter.manualMaxReach *= scale;
            setter.manualMinReach *= scale;
            setter.maxReachSafetyPadding *= scale;
            setter.startFollowDistance *= scale;
            setter.stopFollowDistance *= scale;
            setter.stopVelocity *= scale;
        }

        for (int i = 0; i < spineTargetSetters.Length; i++)
        {
            SpineFakeTargetSetter setter = spineTargetSetters[i];
            if (setter == null)
            {
                continue;
            }

            setter.manualMaxReach *= scale;
            setter.manualMinReach *= scale;
            setter.maxReachSafetyPadding *= scale;
            setter.manualTargetNoClipRadius *= scale;
            setter.frontTargetHeightGive *= scale;
            setter.manualSafeBoxCenter *= scale;
            setter.manualSafeBoxHalfWidth *= scale;
            setter.manualSafeBoxHalfDepth *= scale;
            setter.frontBoxDepth *= scale;
            setter.frontExitBlendDepth *= scale;
            setter.frontBoxSidePadding *= scale;
            setter.frontSideExitBlendDistance *= scale;
            setter.sideBoxWidth *= scale;
            setter.sideExitBlendWidth *= scale;
            setter.sideBoxForwardPadding *= scale;
            setter.sideDepthExitBlendDistance *= scale;
            setter.manualPoleDistance *= scale;
            setter.minimumPoleDistance = Mathf.Max(setter.minimumPoleDistance * scale, 0.05f);
            setter.fakeTargetPlaneHeight *= scale;
            setter.fakePolePlaneHeight *= scale;
            setter.bodyAnchoredTargetForwardDistance *= scale;
            setter.bodyAnchoredTargetPlaneHeight *= scale;
            setter.manualTargetNoClipRadius = Mathf.Max(setter.manualTargetNoClipRadius, setter.manualMaxReach * setter.minNoClipRadiusAsReachFraction);
            setter.worldZeroGuardRadius = Mathf.Max(setter.worldZeroGuardRadius * scale, setter.minimumWorldZeroGuardRadius);
        }
    }

    private void ScaleBodyRotationValues(float scale)
    {
        for (int i = 0; i < bodyRotationAssigners.Length; i++)
        {
            BodyRotationBoxAssigner assigner = bodyRotationAssigners[i];
            if (assigner == null)
            {
                continue;
            }

            assigner.boxCenterOffset *= scale;
            assigner.boxHalfWidth *= scale;
            assigner.boxHalfDepth *= scale;
            assigner.outsideFalloffDistance *= scale;
        }
    }

    private void ScaleActionOffsets(float scale)
    {
        heldCenterPull *= scale;
        heldLiftOffset *= scale;
        throwBackOffset *= scale;
        airborneLegLift *= scale;
        feetGroundClearance *= scale;
        landingTolerance *= scale;
        maxSnapDownDistance *= scale;
    }

    private void ReinitializeScaledIkSystems()
    {
        LimbSolver[] solvers = GetComponentsInChildren<LimbSolver>(true);
        for (int i = 0; i < solvers.Length; i++)
        {
            if (solvers[i] != null)
            {
                solvers[i].InitializeChainData();
            }
        }

        if (legController != null)
        {
            legController.Initialize();
        }

        SpineFakeTargetSetter[] setters = GetComponentsInChildren<SpineFakeTargetSetter>(true);
        for (int i = 0; i < setters.Length; i++)
        {
            if (setters[i] != null)
            {
                setters[i].RecaptureStaticBasisAfterRigScale();
            }
        }
    }

    private void UpdateGroundSupport(float dt)
    {
        if (!enableGroundSupport || legController == null || CoreNode == null)
        {
            return;
        }

        dt = Mathf.Max(dt, Time.deltaTime);

        if (!TryGetGroundHeightBelow(CoreNode.position, out float groundY))
        {
            if (legController != null)
            {
                legController.SetAirborneLegPose(false, 0f);
            }

            return;
        }

        float targetCoreY = groundY + GetDesiredCoreGroundHeight();
        float currentY = CoreNode.position.y;

        if (isGrounded && currentY - targetCoreY > maxSnapDownDistance)
        {
            isGrounded = false;
            verticalVelocity = Mathf.Min(0f, verticalVelocity);
        }

        if (isGrounded)
        {
            float nextY = StepSecondOrderFloat(
                currentY,
                targetCoreY,
                ref coreHeightVelocity,
                coreHeightFrequencyHz,
                coreHeightDampingRatio,
                dt,
                maxCoreHeightAcceleration,
                maxCoreHeightSpeed,
                maxCoreHeightSubstepTime
            );

            MoveCoreToY(nextY);
            verticalVelocity = 0f;
        }
        else
        {
            verticalVelocity -= gravity * dt;
            float nextY = currentY + verticalVelocity * dt;

            if (verticalVelocity <= 0f && nextY <= targetCoreY + landingTolerance)
            {
                nextY = Mathf.Max(nextY, targetCoreY);
                verticalVelocity = 0f;
                coreHeightVelocity = 0f;
                isGrounded = true;
                jumpAirPoseRequested = false;
            }

            MoveCoreToY(nextY);
        }

        if (liftCoreFromFootClipping)
        {
            ApplyFootClipCoreLift();
        }

        if (legController != null)
        {
            bool useJumpLegTuck = !isGrounded && jumpAirPoseRequested && airborneLegLift > 0f;
            legController.SetAirborneLegPose(useJumpLegTuck, airborneLegLift);
        }
    }

    private float GetDesiredCoreGroundHeight()
    {
        if (legController == null)
        {
            return Mathf.Max(feetGroundClearance, desiredVisualHeight * desiredLegExtensionRatio);
        }

        return Mathf.Max(feetGroundClearance, legController.DesiredBodyHeightOffGround);
    }

    private void MoveCoreToY(float y)
    {
        Transform core = CoreNode;
        Vector3 position = core.position;
        float deltaY = y - position.y;

        if (Mathf.Abs(deltaY) <= Epsilon)
        {
            return;
        }

        core.position = new Vector3(position.x, y, position.z);
    }

    private void ApplyFootClipCoreLift()
    {
        if (!keepFeetAboveGround || legController == null)
        {
            return;
        }

        Vector3 leftFoot = GetLegTailPosition(legController.leftLeg);
        Vector3 rightFoot = GetLegTailPosition(legController.rightLeg);

        float lift = 0f;
        lift = Mathf.Max(lift, GetFootGroundLift(leftFoot));
        lift = Mathf.Max(lift, GetFootGroundLift(rightFoot));

        if (lift <= 0f)
        {
            return;
        }

        CoreNode.position += Vector3.up * lift;
        coreHeightVelocity = Mathf.Max(coreHeightVelocity, lift / Mathf.Max(Time.deltaTime, Epsilon));
    }

    private float GetFootGroundLift(Vector3 footPosition)
    {
        if (!TryGetGroundHeightBelow(footPosition, out float groundY))
        {
            return 0f;
        }

        return groundY + feetGroundClearance - footPosition.y;
    }

    private Vector3 GetLegTailPosition(AutoRunLegPairController.Leg leg)
    {
        if (leg != null && leg.tailNode != null)
        {
            return leg.tailNode.transform.position;
        }

        if (leg != null && leg.fakeTarget != null)
        {
            return leg.fakeTarget.position;
        }

        return CoreNode.position;
    }

    private bool TryGetGroundHeightBelow(Vector3 nearPosition, out float groundY)
    {
        Vector3 origin = nearPosition + Vector3.up * groundProbeHeight;
        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            Vector3.down,
            groundProbeHeight + groundProbeDistance,
            groundLayers,
            QueryTriggerInteraction.Ignore);

        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.transform != null && hit.transform.IsChildOf(transform))
            {
                continue;
            }

            groundY = hit.point.y;
            return true;
        }

        Terrain terrain = Terrain.activeTerrain;
        if (terrain != null)
        {
            groundY = terrain.SampleHeight(nearPosition) + terrain.transform.position.y;
            return true;
        }

        groundY = 0f;
        return false;
    }

    private void ResolveReferences()
    {
        if (legController == null || !legController.isActiveAndEnabled)
        {
            legController = FindBestLegController();
        }

        if (movementInput == null)
        {
            movementInput = GetComponentInChildren<AutoRunMovementInput>(true);
        }

        if (coreNode == null && legController != null)
        {
            coreNode = legController.coreNode;
        }

        if (coreNode == null)
        {
            coreNode = FindChildByName(transform, "LegCore") ?? transform;
        }

        if (runTarget == null && legController != null)
        {
            runTarget = legController.runTarget;
        }

        if (runTarget == null)
        {
            runTarget = FindChildByName(transform, "run_target") ?? CreateRuntimeTarget("run_target");
        }

        if (aimTarget == null && movementInput != null)
        {
            aimTarget = movementInput.mouseTracker;
        }

        if (aimTarget == null)
        {
            aimTarget = FindChildByName(transform, "MouseTarget") ?? CreateRuntimeTarget("MouseTarget");
        }

        if (weaponHolder == null)
        {
            weaponHolder = FindChildByName(transform, "WeaponHolder") ?? CreateRuntimeTarget("WeaponHolder");
        }

        if (itemHolder == null)
        {
            itemHolder = FindChildByName(transform, "ItemHolder") ?? CreateRuntimeTarget("ItemHolder");
        }

        rotationAssigners = GetComponentsInChildren<DirectTargetRotationAssigner>(true);
        bodyRotationAssigners = GetComponentsInChildren<BodyRotationBoxAssigner>(true);
        spineTargetSetters = GetComponentsInChildren<SpineFakeTargetSetter>(true);
        rotationDrivers = GetComponentsInChildren<RotationAssigner>(true);
        rotatableNodes = GetComponentsInChildren<RotatableNode>(true);
        rotatableNodePairs = GetComponentsInChildren<RotatableNodePair>(true);
        offsetNodes = GetComponentsInChildren<OffsetPositioningNode>(true);
        SortOffsetNodesParentFirst(offsetNodes);
        limbSolvers = GetComponentsInChildren<LimbSolver>(true);
        meshBuilders = GetComponentsInChildren<MeshingOffsetLoftMeshBuilder>(true);

        if (armTargetSetters.Length == 0)
        {
            armTargetSetters = GetComponentsInChildren<LazyIKTargetSetter>(true);
            Array.Sort(armTargetSetters, CompareArms);
            arms = new ArmBinding[armTargetSetters.Length];

            for (int i = 0; i < armTargetSetters.Length; i++)
            {
                arms[i] = new ArmBinding(armTargetSetters[i], CoreNode, i == 0 ? -1f : 1f);
            }
        }

        AutoRepairCriticalRigReferences();
        ApplyScaleSafeSpineTargetDefaults();
        WireGameplayHolders();
        ConfigureChildEvaluationOrder();
    }



    private void ApplyScaleSafeSpineTargetDefaults()
    {
        for (int i = 0; i < spineTargetSetters.Length; i++)
        {
            SpineFakeTargetSetter setter = spineTargetSetters[i];
            if (setter == null)
            {
                continue;
            }

            setter.preventWorldZeroSpineTarget = true;
            setter.holdTargetAtCoreWhenEvaluationFails = true;
            setter.holdFakeTargetInsideSafeBox = true;
            setter.moveSpineTailNodeToFakeTarget = false;
            setter.applyOffsetWritesImmediately = true;
            setter.enforceSetterOwnsFakeTargetTransform = true;
            setter.useLiveBasisUntilFirstValidTarget = true;
            setter.ignoreUninitializedWorldZeroRealTargetAtStartup = true;
            setter.recaptureBasisDuringStartup = true;
            setter.startupBasisRecaptureFrames = Mathf.Max(setter.startupBasisRecaptureFrames, 8);
            setter.minNoClipRadiusAsReachFraction = Mathf.Max(setter.minNoClipRadiusAsReachFraction, 0.095f);
            setter.minimumWorldZeroGuardRadius = Mathf.Max(setter.minimumWorldZeroGuardRadius, 0.12f);
            setter.worldZeroGuardRadius = Mathf.Max(setter.worldZeroGuardRadius, setter.minimumWorldZeroGuardRadius);
            setter.minimumPoleDistance = Mathf.Max(setter.minimumPoleDistance, 0.06f);
            setter.bodyAnchoredTargetForwardDistance = Mathf.Max(setter.bodyAnchoredTargetForwardDistance, setter.manualMaxReach * 0.10f, 0.10f);
            setter.bodyAnchoredTargetPlaneHeight = Mathf.Max(setter.bodyAnchoredTargetPlaneHeight, setter.manualMaxReach * 0.12f, 0.12f);
        }
    }

    private void WireGameplayHolders()
    {
        PlayerWeaponPickup weaponPickup = GetComponent<PlayerWeaponPickup>();
        if (weaponPickup != null && weaponHolder != null)
        {
            weaponPickup.Initialize(weaponHolder);
        }

        PlayerItemPickup itemPickup = GetComponent<PlayerItemPickup>();
        if (itemPickup != null && itemHolder != null)
        {
            itemPickup.Initialize(itemHolder);
        }
    }

    private void AutoRepairCriticalRigReferences()
    {
        if (legController == null)
        {
            return;
        }

        // The leg rotation assigner must use the leg direction target as its pole/reference.
        // Some prefab revisions accidentally pointed it at the spine pole, which makes yaw
        // calculations effectively unrelated to the lower-body assembly.
        if (legController.gaitRotationAssigner != null && legController.gaitForwardTarget != null)
        {
            RotationAssigner assigner = legController.gaitRotationAssigner;
            // Rotation is driven by dynamic offsets around Node (4), while LegCore itself may
            // also rotate visually as an assembly. Keep the zero-angle reference stable; do
            // not use the moving gait target as the assigner's pole or yaw appears to cancel.
            legController.gaitRotationCore = legController.coreNode != null ? legController.coreNode : CoreNode;
            assigner.sharedCoreNode = legController.coreNode != null ? legController.coreNode : CoreNode;
            assigner.sharedPoleVector = legController.gaitRotationAngleReference != null
                ? legController.gaitRotationAngleReference
                : legController.forwardReference != null
                    ? legController.forwardReference
                    : legController.gaitForwardTarget;
            assigner.overridePairCoreAndPole = true;
            assigner.overridePairPlane = true;
            assigner.sharedPlaneNormalMode = RotatableNodePair.PlaneNormalMode.WorldVector;
            assigner.sharedWorldPlaneNormal = Vector3.up;
        }

        for (int i = 0; i < rotationAssigners.Length; i++)
        {
            DirectTargetRotationAssigner directAssigner = rotationAssigners[i];
            if (directAssigner != null && IsGaitRotationTargetAssigner(directAssigner))
            {
                directAssigner.enabled = true;
                directAssigner.updateEveryFrame = false;
                directAssigner.applyRotationAssignerImmediately = false;
            }
        }
    }

    private void ConfigureChildEvaluationOrder()
    {
        if (!Application.isPlaying || !manageChildEvaluationOrder)
        {
            return;
        }

        for (int i = 0; i < rotationAssigners.Length; i++)
        {
            DirectTargetRotationAssigner assigner = rotationAssigners[i];
            if (assigner == null)
            {
                continue;
            }

            assigner.updateEveryFrame = false;
            assigner.applyRotationAssignerImmediately = false;
        }

        for (int i = 0; i < bodyRotationAssigners.Length; i++)
        {
            BodyRotationBoxAssigner assigner = bodyRotationAssigners[i];
            if (assigner == null)
            {
                continue;
            }

            assigner.updateEveryFrame = false;
            assigner.applyRotationAssignerImmediately = false;
            assigner.smoothOutputBeforeAssigning = false;
        }

        for (int i = 0; i < spineTargetSetters.Length; i++)
        {
            SpineFakeTargetSetter setter = spineTargetSetters[i];
            if (setter != null)
            {
                setter.updateEveryFrame = false;
                setter.applyOffsetWritesImmediately = true;
                setter.smoothOutputTarget = false;
                setter.holdFakeTargetInsideSafeBox = true;
                setter.moveSpineTailNodeToFakeTarget = false;
                setter.enforceSetterOwnsFakeTargetTransform = true;
                setter.useLiveBasisUntilFirstValidTarget = true;
                setter.ignoreUninitializedWorldZeroRealTargetAtStartup = true;
            }
        }

        for (int i = 0; i < armTargetSetters.Length; i++)
        {
            LazyIKTargetSetter setter = armTargetSetters[i];
            if (setter != null)
            {
                setter.updateEveryFrame = false;
                setter.applyOffsetWritesImmediately = true;
            }
        }

        for (int i = 0; i < rotationDrivers.Length; i++)
        {
            RotationAssigner driver = rotationDrivers[i];
            if (driver != null)
            {
                driver.applyEveryUpdate = false;
                driver.debugLogging = false;
            }
        }

        for (int i = 0; i < rotatableNodes.Length; i++)
        {
            RotatableNode node = rotatableNodes[i];
            if (node != null)
            {
                node.applyEveryUpdate = false;
                node.debugLogging = false;
            }
        }

        for (int i = 0; i < rotatableNodePairs.Length; i++)
        {
            RotatableNodePair pair = rotatableNodePairs[i];
            if (pair != null)
            {
                pair.debugLogging = false;
            }
        }

        for (int i = 0; i < offsetNodes.Length; i++)
        {
            OffsetPositioningNode node = offsetNodes[i];
            if (node != null)
            {
                node.managedByProceduralRig = true;
                node.debugLogging = false;
            }
        }

        for (int i = 0; i < limbSolvers.Length; i++)
        {
            LimbSolver solver = limbSolvers[i];
            if (solver != null)
            {
                solver.managedByProceduralRig = true;
                solver.solveInLateUpdate = false;
                solver.preTranslateIntermediateNodesByEndpointDelta = true;
                solver.endpointDeltaBlend = 0.5f;
                solver.maxEndpointPreTranslateReachFraction = Mathf.Max(solver.maxEndpointPreTranslateReachFraction, 0.75f);
                solver.repairStaleCapturedBoneLengths = true;

                if (solver.tailTargetOverride != null)
                {
                    // Separate target handles are used by the spine fake target. Let the
                    // setter own that handle, and do not pre-translate the visible chain
                    // around it. The solver will read the handle, solve the chain, then put
                    // the visible tail at the clamped endpoint after solving.
                    solver.writeClampedTailTargetBackToTransform = false;
                    solver.keepVisibleTailAtTargetWhenUsingOverride = true;
                    solver.moveVisibleTailToOverrideTargetAfterSolving = true;
                    solver.preTranslateWhenUsingTailTargetOverride = false;
                }
            }
        }

        for (int i = 0; i < meshBuilders.Length; i++)
        {
            MeshingOffsetLoftMeshBuilder builder = meshBuilders[i];
            if (builder == null)
            {
                continue;
            }

            builder.managedByProceduralRig = true;
            builder.debugLogging = false;
            builder.drawGizmos = false;
            EnsureBodyMeshCollider(builder);

            if (throttleRuntimeMeshRebuilds)
            {
                builder.minimumRuntimeRebuildInterval = 0f;
                builder.onlyRebuildWhenInputMoved = false;
            }

            if (maxRuntimeRingSamples >= 3)
            {
                builder.ringSamples = Mathf.Min(builder.ringSamples, maxRuntimeRingSamples);
            }
        }
    }



    private void EnsureBodyMeshCollider(MeshingOffsetLoftMeshBuilder builder)
    {
        if (!addCollisionToProceduralBodyMeshes || builder == null)
        {
            return;
        }

        MeshCollider collider = builder.optionalMeshCollider;
        if (collider == null)
        {
            collider = builder.GetComponent<MeshCollider>();
        }

        if (collider == null)
        {
            collider = builder.gameObject.AddComponent<MeshCollider>();
        }

        collider.convex = proceduralBodyMeshCollidersConvex;
        collider.isTrigger = proceduralBodyMeshCollidersAreTriggers;
        collider.cookingOptions = MeshColliderCookingOptions.EnableMeshCleaning |
                                  MeshColliderCookingOptions.WeldColocatedVertices |
                                  MeshColliderCookingOptions.CookForFasterSimulation;
        builder.optionalMeshCollider = collider;
    }

    private void EnsureFrameDriver()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        if (GetComponent<ProceduralPlayerRigFrameDriver>() == null)
        {
            gameObject.AddComponent<ProceduralPlayerRigFrameDriver>();
        }
    }

    private AutoRunLegPairController FindBestLegController()
    {
        AutoRunLegPairController[] controllers = GetComponentsInChildren<AutoRunLegPairController>(true);
        AutoRunLegPairController first = null;
        AutoRunLegPairController firstEnabled = null;
        AutoRunLegPairController firstEnabledWithGaitTarget = null;

        for (int i = 0; i < controllers.Length; i++)
        {
            AutoRunLegPairController controller = controllers[i];
            if (controller == null)
            {
                continue;
            }

            first ??= controller;

            if (!controller.isActiveAndEnabled)
            {
                continue;
            }

            firstEnabled ??= controller;

            if (controller.gaitForwardTarget != null)
            {
                firstEnabledWithGaitTarget ??= controller;
            }

            if (controller.gameObject.name == "LegCore" && controller.gaitForwardTarget != null)
            {
                return controller;
            }
        }

        return firstEnabledWithGaitTarget ?? firstEnabled ?? first;
    }

    private void UpdateVelocity()
    {
        Transform core = CoreNode;

        if (!hasPreviousCorePosition)
        {
            previousCorePosition = core.position;
            hasPreviousCorePosition = true;
            Velocity = Vector3.zero;
            return;
        }

        float dt = Time.deltaTime;
        Velocity = dt > Epsilon ? (core.position - previousCorePosition) / dt : Vector3.zero;
        previousCorePosition = core.position;
    }

    private void UpdateHolders()
    {
        Vector3 rightHand = RightArmTargetWorld;
        Vector3 leftHand = LeftArmTargetWorld;
        Vector3 midpoint = (rightHand + leftHand) * 0.5f;

        Vector3 weaponAnchor = carryPose == CarryPose.OneHandWeapon
            ? GetWeaponHandAnchor(leftHand, rightHand)
            : midpoint;

        Vector3 aimDirection = aimTarget != null ? aimTarget.position - weaponAnchor : CoreNode.forward;
        aimDirection.y = 0f;

        if (aimDirection.sqrMagnitude <= Epsilon)
        {
            aimDirection = CoreNode.forward;
        }

        Quaternion holderRotation = Quaternion.LookRotation(aimDirection.normalized, Vector3.up);

        if (weaponHolder != null)
        {
            weaponHolder.position = weaponAnchor;
            weaponHolder.rotation = holderRotation;
        }

        if (itemHolder != null)
        {
            itemHolder.position = midpoint;
            itemHolder.rotation = holderRotation;
        }

        if (carryPose != CarryPose.None || Time.time < throwWindupUntil)
        {
            ApplyCarryPose(carryPose);
        }
    }

    private Vector3 GetWeaponHandAnchor(Vector3 leftHand, Vector3 rightHand)
    {
        return weaponHandSideSign < 0f ? leftHand : rightHand;
    }

    private bool TryGetHeldItemWorldBounds(out Bounds bounds)
    {
        bounds = new Bounds(itemHolder != null ? itemHolder.position : CoreNode.position, Vector3.zero);
        if (itemHolder == null)
        {
            return false;
        }

        Renderer[] renderers = itemHolder.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        Collider[] colliders = itemHolder.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || !collider.enabled)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        return hasBounds;
    }

    private float GetHeldItemHalfWidthAlongBodyRight()
    {
        if (!TryGetHeldItemWorldBounds(out Bounds bounds))
        {
            return minimumHeldItemHalfWidth;
        }

        Vector3 right = CoreNode != null ? CoreNode.right : Vector3.right;
        right.y = 0f;
        if (right.sqrMagnitude <= Epsilon)
        {
            right = Vector3.right;
        }
        right.Normalize();

        Vector3 extents = bounds.extents;
        float projectedHalf = Mathf.Abs(right.x) * extents.x +
                              Mathf.Abs(right.y) * extents.y +
                              Mathf.Abs(right.z) * extents.z;

        return Mathf.Max(minimumHeldItemHalfWidth, projectedHalf + itemHandEdgePadding);
    }

    private void RefreshActionState()
    {
        if (Time.time < throwWindupUntil)
        {
            ActionState = "throw_windup";
            return;
        }

        switch (carryPose)
        {
            case CarryPose.OneHandWeapon:
                ActionState = "holding_weapon";
                break;

            case CarryPose.TwoHandItem:
                ActionState = "holding_item";
                break;

            case CarryPose.None:
            default:
                ActionState = isGrounded ? "idle" : "jump";
                break;
        }
    }

    private float StepSecondOrderFloat(
        float current,
        float target,
        ref float derivative,
        float frequencyHz,
        float dampingRatio,
        float deltaTime,
        float maxAcceleration,
        float maxSpeed,
        float maxSubstep
    )
    {
        if (deltaTime <= Epsilon)
        {
            return current;
        }

        float remaining = deltaTime;
        int guard = 0;

        while (remaining > Epsilon && guard < 64)
        {
            guard++;

            float step = Mathf.Min(remaining, Mathf.Max(maxSubstep, 0.001f));
            remaining -= step;

            float omega = 2f * Mathf.PI * Mathf.Max(0.01f, frequencyHz);
            float stiffness = omega * omega;
            float damping = 2f * Mathf.Max(0f, dampingRatio) * omega;

            float acceleration =
                stiffness * (target - current)
                - damping * derivative;

            if (maxAcceleration > 0f && Mathf.Abs(acceleration) > maxAcceleration)
            {
                acceleration = Mathf.Sign(acceleration) * maxAcceleration;
            }

            derivative += acceleration * step;

            if (maxSpeed > 0f && Mathf.Abs(derivative) > maxSpeed)
            {
                derivative = Mathf.Sign(derivative) * maxSpeed;
            }

            current += derivative * step;
        }

        return current;
    }

    private bool TryGetRendererBounds(out Bounds bounds)
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        bounds = new Bounds(transform.position, Vector3.zero);

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds;
    }

    private Transform CreateRuntimeTarget(string targetName)
    {
        GameObject target = new GameObject(targetName);
        target.transform.SetParent(transform, true);
        target.transform.position = CoreNode.position;
        target.transform.rotation = Quaternion.identity;
        target.transform.localScale = Vector3.one;
        return target.transform;
    }

    private static Transform FindChildByName(Transform parent, string childName)
    {
        if (parent == null)
        {
            return null;
        }

        if (string.Equals(parent.name, childName, StringComparison.OrdinalIgnoreCase))
        {
            return parent;
        }

        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindChildByName(parent.GetChild(i), childName);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private static int CompareArms(LazyIKTargetSetter a, LazyIKTargetSetter b)
    {
        float ax = a != null && a.transform != null ? a.transform.position.x : 0f;
        float bx = b != null && b.transform != null ? b.transform.position.x : 0f;
        return ax.CompareTo(bx);
    }

    private struct ArmBinding
    {
        private readonly LazyIKTargetSetter setter;
        private readonly Transform realTarget;
        private readonly OffsetPositioningNode offsetNode;
        private readonly Transform core;
        private readonly float sideSign;

        public ArmBinding(LazyIKTargetSetter setter, Transform core, float fallbackSideSign)
        {
            this.setter = setter;
            this.core = core;
            realTarget = setter != null ? setter.realTarget : null;
            offsetNode = realTarget != null ? realTarget.GetComponent<OffsetPositioningNode>() : null;

            if (realTarget != null && core != null)
            {
                float side = Vector3.Dot(realTarget.position - core.position, core.right);
                sideSign = Mathf.Abs(side) > Epsilon ? Mathf.Sign(side) : fallbackSideSign;
            }
            else
            {
                sideSign = fallbackSideSign;
            }
        }

        public Vector3 CurrentTargetWorld
        {
            get
            {
                if (realTarget != null)
                {
                    return realTarget.position;
                }

                return setter != null ? setter.transform.position : Vector3.zero;
            }
        }

        public void ApplyPoseOffset(
            CarryPose carryPose,
            bool isThrowing,
            float centerPull,
            Vector3 heldLiftOffset,
            Vector3 throwBackOffset,
            float heldItemHalfWidth,
            float itemForwardBias,
            float weaponHandSideSign,
            Vector3 heldItemCenterWorld)
        {
            if (offsetNode == null)
            {
                return;
            }

            bool shouldHoldItem = carryPose == CarryPose.TwoHandItem;
            bool isWeaponHand = Mathf.Sign(sideSign) == Mathf.Sign(weaponHandSideSign == 0f ? 1f : weaponHandSideSign);

            Vector3 heldOffset = Vector3.zero;
            if (shouldHoldItem)
            {
                Vector3 desiredHandWorld = GetTwoHandItemGripWorld(heldItemHalfWidth, itemForwardBias, heldLiftOffset, heldItemCenterWorld);
                heldOffset = offsetNode.CalculateDynamicOffsetForDesiredWorldPosition(HeldArmOffsetId, desiredHandWorld);
            }

            Vector3 windupOffset = isThrowing && isWeaponHand
                ? ToWorldOffset(throwBackOffset)
                : Vector3.zero;

            offsetNode.SetDynamicOffset(HeldArmOffsetId, heldOffset);
            offsetNode.SetDynamicOffset(ThrowArmOffsetId, windupOffset);
            offsetNode.ApplyPosition();
        }

        private Vector3 GetTwoHandItemGripWorld(float heldItemHalfWidth, float itemForwardBias, Vector3 liftOffset, Vector3 heldItemCenterWorld)
        {
            if (core == null)
            {
                return CurrentTargetWorld;
            }

            float halfWidth = Mathf.Max(0f, heldItemHalfWidth);
            float sideDistance = Mathf.Max(0f, halfWidth) * Mathf.Sign(sideSign);
            float forwardDistance = Mathf.Max(0f, halfWidth) * Mathf.Clamp01(itemForwardBias);

            return heldItemCenterWorld +
                   core.right * sideDistance +
                   core.forward * forwardDistance +
                   core.up * liftOffset.y;
        }

        public void SetExternalTarget(Vector3 worldPosition)
        {
            if (setter != null)
            {
                setter.SetExternalTargetWorldPosition(worldPosition);
            }
        }

        public void UseTransformTarget()
        {
            if (setter != null)
            {
                setter.UseTransformTarget();
            }
        }

        private Vector3 ToWorldOffset(Vector3 localOffset)
        {
            if (core == null)
            {
                return localOffset;
            }

            return core.right * localOffset.x
                   + core.up * localOffset.y
                   + core.forward * localOffset.z;
        }
    }
}

[DefaultExecutionOrder(10000)]
public sealed class ProceduralPlayerRigFrameDriver : MonoBehaviour
{
    private ProceduralPlayerRig rig;

    private void Awake()
    {
        rig = GetComponent<ProceduralPlayerRig>();
    }

    private void LateUpdate()
    {
        if (rig == null)
        {
            rig = GetComponent<ProceduralPlayerRig>();
        }

        if (rig != null)
        {
            rig.ForceFinalRigEvaluation(Time.deltaTime);
        }
    }
}
