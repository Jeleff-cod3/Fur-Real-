using System;
using UnityEngine;

[DefaultExecutionOrder(-115)]
public class AutoRunLegPairController : MonoBehaviour
{
    private const float Epsilon = 0.000001f;
    private const int MaxChainNodes = 256;

    public enum ReachSource
    {
        Manual,
        NodeStateChain,
        LimbSolverCumulativeBones
    }

    public enum CoreMovementMode
    {
        ScriptMovesCore,
        ReadExternalCoreOnly
    }

    public enum StepLiftAxisMode
    {
        MovementPlaneNormal,
        GroundNormalAtLanding,
        WorldUp
    }

    [Serializable]
    public class Leg
    {
        [Header("Identity")]
        public string label = "Leg";

        [Header("IK References")]
        public LimbSolver limbSolver;
        public NodeState tailNode;

        [Tooltip("The actual IK fake target that the limb follows.")]
        public Transform fakeTarget;

        [Tooltip("The planted / intended target. This script moves this to the next grounded step point.")]
        public Transform realTarget;

        [Tooltip("Static knee/leg pole. This script assigns it but does not animate it.")]
        public Transform staticPole;

        [Header("Optional Offset Output")]
        public bool writeFakeTargetThroughOffsetNode = false;
        public OffsetPositioningNode fakeTargetOffsetNode;
        public int fakeTargetDynamicOffsetId = 60;

        public bool writeRealTargetThroughOffsetNode = false;
        public OffsetPositioningNode realTargetOffsetNode;
        public int realTargetDynamicOffsetId = 61;

        [Header("Manual Reach Fallback")]
        [Min(0.001f)]
        public float manualReach = 2f;

        [Header("Runtime - Read Only")]
        public float capturedSideOffset;
        public float capturedForwardOffset;
        public float reach;

        public bool isStepping;
        public Vector3 stepStartWorld;
        public Vector3 stepEndWorld;
        public Vector3 stepLiftAxis;
        public Vector3 plantedWorldPosition;
        public float stepTimer;
        public float stepDuration;
        public float stepHeight;
    }

    [Header("Core / Run Target")]
    public CoreMovementMode coreMovementMode = CoreMovementMode.ScriptMovesCore;

    [Tooltip("The leg-pair/root/core node that moves through the world.")]
    public Transform coreNode;

    [Tooltip("The target object the leg pair should run toward.")]
    public Transform runTarget;

    [Tooltip("Fallback forward reference. Used when the run direction is too small.")]
    public Transform forwardReference;

    [Tooltip("The movement plane normal. Default Vector3.up means movement happens on X/Z.")]
    public Vector3 movementPlaneNormal = Vector3.up;

    [Header("Tail End IK Core Sync")]
    [Tooltip("Optional tail-end IK node that receives the same frame movement delta as the core.")]
    public NodeState tailEndIkNode;

    [Tooltip("Moves the tail-end IK node by the core's delta instead of setting it to a fixed position.")]
    public bool syncTailEndIkNodeWithCore = true;

    [Tooltip("1 follows the core delta. -1 mirrors the delta. Values between/above scale the additive sync.")]
    public float tailEndCoreDeltaMultiplier = 1f;

    [Header("Core Movement")]
    [Min(0f)]
    public float stopDistance = 0.35f;

    [Tooltip("Within this distance, the core slows down toward the target.")]
    [Min(0.001f)]
    public float slowDownRadius = 2f;

    [Tooltip("Speed when momentum is low.")]
    [Min(0f)]
    public float walkSpeed = 1.2f;

    [Tooltip("Speed when momentum is high.")]
    [Min(0f)]
    public float runSpeed = 5f;

    [Tooltip("Momentum builds while we keep moving toward the target.")]
    [Min(0f)]
    public float momentumBuildPerSecond = 0.85f;

    [Tooltip("Momentum decays after stopping / overshooting.")]
    [Min(0f)]
    public float momentumDecayPerSecond = 1.2f;

    [Tooltip("If true, core can continue a little because of momentum even after reaching the target.")]
    public bool allowMomentumCarry = true;

    [Range(0f, 1f)]
    public float momentumCarryThreshold = 0.08f;

    [Min(0f)]
    public float momentumCarrySpeedMultiplier = 0.45f;

    [Header("Core Second Order Response")]
    [Tooltip("Higher = core velocity reacts faster.")]
    [Min(0.01f)]
    public float coreVelocityFrequencyHz = 2.2f;

    [Tooltip("1 = stable/no overshoot. Below 1 = overshoot. Above 1 = heavy damping.")]
    [Min(0f)]
    public float coreVelocityDampingRatio = 0.75f;

    [Tooltip("0 means unlimited.")]
    [Min(0f)]
    public float maxCoreAcceleration = 0f;

    [Tooltip("0 means unlimited.")]
    [Min(0f)]
    public float maxCoreSpeed = 0f;

    [Tooltip("Substepping keeps the second-order motion stable at uneven frame rates.")]
    [Min(0.001f)]
    public float maxCoreSubstepTime = 1f / 90f;

    [Header("Core Facing")]
    [Tooltip("If true, the whole core assembly turns toward the run target before moving.")]
    public bool rotateCoreTowardRunTarget = true;

    [Min(0f)]
    public float coreTurnSpeedDegrees = 720f;

    [Tooltip("If true, movement uses the core's planar forward after turning, keeping one forward walking shape.")]
    public bool moveAlongCoreForwardAfterTurning = true;

    [Header("Core Grounding")]
    public bool raycastCoreToGround = false;
    public LayerMask coreGroundMask = ~0;
    [Min(0f)] public float coreGroundRayHeight = 3f;
    [Min(0f)] public float coreGroundRayDistance = 8f;
    public float coreGroundOffset = 0.8f;

    [Header("Movement Sampling / Blocks")]
    [Tooltip("How often we measure core movement as a block, instead of reacting to tiny frame deltas.")]
    [Min(0.01f)]
    public float movementSampleInterval = 0.18f;

    [Tooltip("How much sampled movement contributes to step size / step need.")]
    [Min(0f)]
    public float movementBlockAccumulationMultiplier = 1f;

    [Tooltip("If movement block debt exceeds this fraction of pair reach, we can force a step.")]
    [Min(0f)]
    public float movementDebtStepTriggerReachRatio = 0.12f;

    [Header("Legs")]
    public Leg leftLeg = new Leg { label = "Left Leg" };
    public Leg rightLeg = new Leg { label = "Right Leg" };

    [Tooltip("If true, the leg with greater forward offset at Start begins as leading.")]
    public bool autoSelectInitialLeadingLeg = true;

    [Tooltip("0 = left starts leading, 1 = right starts leading. Used if autoSelectInitialLeadingLeg is false.")]
    [Range(0, 1)]
    public int initialLeadingLegIndex = 0;

    [Tooltip("Only one foot steps at a time.")]
    public bool oneStepAtATime = true;

    [Tooltip("Minimum time between step starts.")]
    [Min(0f)]
    public float minStepInterval = 0.08f;

    [Tooltip("Leading leg must fall this far behind the core before the other leg steps.")]
    [Min(0f)]
    public float leadingBehindDistance = 0.15f;

    [Tooltip("Additional behind trigger as a fraction of pair reach.")]
    [Min(0f)]
    public float leadingBehindReachRatio = 0.04f;

    [Header("Step Size")]
    [Tooltip("Main stride ratio relative to pair reach.")]
    [Min(0f)]
    public float baseStepReachRatio = 0.22f;

    [Tooltip("Momentum adds this much extra stride ratio at full momentum.")]
    [Min(0f)]
    public float momentumStepReachRatio = 0.18f;

    [Tooltip("Movement block debt adds to requested stride.")]
    [Min(0f)]
    public float movementBlockStepInfluence = 0.65f;

    [Tooltip("Final clamp for tiny steps.")]
    [Min(0f)]
    public float minStepReachRatio = 0.07f;

    [Tooltip("Final clamp for large steps.")]
    [Min(0f)]
    public float maxStepReachRatio = 0.48f;

    [Header("Step Buckets")]
    [Tooltip("Quantizes steps into micro/small/medium/full. Good for clean rhythm.")]
    public bool useStepBuckets = true;

    [Min(0f)] public float microStepReachRatio = 0.08f;
    [Min(0f)] public float smallStepReachRatio = 0.16f;
    [Min(0f)] public float mediumStepReachRatio = 0.28f;
    [Min(0f)] public float fullStepReachRatio = 0.42f;

    [Header("Step Timing")]
    [Min(0.01f)]
    public float slowStepDuration = 0.38f;

    [Min(0.01f)]
    public float fastStepDuration = 0.18f;

    [Tooltip("Longer steps take slightly longer.")]
    public bool scaleDurationByStepLength = true;

    [Range(0.25f, 2f)]
    public float stepLengthDurationInfluence = 0.35f;

    [Header("Step Arc")]
    public StepLiftAxisMode stepLiftAxisMode = StepLiftAxisMode.GroundNormalAtLanding;

    [Tooltip("Base lift as a fraction of pair reach.")]
    [Min(0f)]
    public float baseStepHeightReachRatio = 0.06f;

    [Tooltip("Extra lift at full momentum as a fraction of pair reach.")]
    [Min(0f)]
    public float momentumStepHeightReachRatio = 0.045f;

    [Tooltip("Extra lift from long steps.")]
    [Min(0f)]
    public float stepLengthHeightInfluence = 0.08f;

    [Header("Step Placement")]
    [Tooltip("Uses each leg's static pole to choose its side lane, then steps mostly forward in that lane.")]
    public bool usePoleLaneForStepSide = true;

    [Tooltip("Samples a small area around the forward step point and chooses the lowest grounded point.")]
    public bool chooseLowestGroundAroundStep = true;

    [Min(0f)]
    public float lowestGroundSearchRadiusReachRatio = 0.08f;

    [Tooltip("Controls horizontal movement from start to end.")]
    public AnimationCurve stepTravelCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    [Tooltip("Controls vertical lift. Default lifts early, then carries forward and lands.")]
    public AnimationCurve stepLiftCurve = new AnimationCurve(
        new Keyframe(0f, 0f),
        new Keyframe(0.18f, 1f),
        new Keyframe(0.65f, 0.65f),
        new Keyframe(1f, 0f)
    );

    [Header("Ground Raycast For Feet")]
    public bool raycastFeetToGround = true;
    public LayerMask footGroundMask = ~0;

    [Tooltip("Ray starts this far above the desired foot point along movementPlaneNormal.")]
    [Min(0f)]
    public float footRayHeight = 3f;

    [Tooltip("Ray travels this far down along movementPlaneNormal.")]
    [Min(0f)]
    public float footRayDistance = 8f;

    public float footGroundOffset = 0.02f;

    [Header("Reach Safety")]
    [Tooltip("Keeps foot target slightly inside the leg's true reach.")]
    [Min(0f)]
    public float legReachSafetyPadding = 0.03f;

    [Range(0.01f, 1f)]
    public float legReachMultiplier = 0.96f;

    [Header("IK Safety")]
    [Tooltip("Clamps every written foot target through the LimbSolver reach bounds before the chain sees it.")]
    public bool clampTargetsWithLimbSolver = true;

    [Tooltip("Applies OffsetPositioningNode target writes immediately, before the LimbSolver runs.")]
    public bool applyTargetOffsetImmediately = true;

    [Tooltip("Runs the LimbSolver right after this script moves a foot target, so middle nodes do not lag behind.")]
    public bool solveLegAfterTargetWrite = true;

    [Header("Runtime Debug")]
    [SerializeField] private int leadingLegIndex = 0;
    [SerializeField] private int steppingLegIndex = -1;
    [SerializeField] private float momentum = 0f;
    [SerializeField] private Vector3 currentCoreVelocity = Vector3.zero;
    [SerializeField] private Vector3 desiredCoreVelocity = Vector3.zero;
    [SerializeField] private Vector3 lastMoveDirection = Vector3.forward;
    [SerializeField] private float movementBlockDebt = 0f;
    [SerializeField] private float lastSampledMovementDistance = 0f;
    [SerializeField] private RuntimeDebugState debugState;

    public RuntimeDebugState DebugState => debugState;

    private Vector3 coreVelocityResponseVelocity = Vector3.zero;
    private Vector3 lastSampleCorePosition = Vector3.zero;
    private Vector3 lastTailSyncCorePosition = Vector3.zero;
    private bool hasTailSyncCorePosition = false;
    private float movementSampleTimer = 0f;
    private float timeSinceLastStep = 999f;
    private bool initialized = false;

    [Serializable]
    public struct RuntimeDebugState
    {
        public Vector3 corePosition;
        public Vector3 runTargetPosition;
        public Vector3 movementForward;
        public Vector3 movementSide;
        public Vector3 movementNormal;

        public float distanceToRunTarget;
        public float momentum;
        public float movementBlockDebt;
        public float pairReach;

        public int leadingLegIndex;
        public int steppingLegIndex;

        public float leftForward;
        public float rightForward;
        public float requestedStepLength;
        public float selectedStepLength;
    }

    private void Start()
    {
        Initialize();
    }

    private void Update()
    {
        if (!initialized)
        {
            Initialize();
        }

        float dt = Time.deltaTime;

        if (dt <= Epsilon)
        {
            return;
        }

        Vector3 normal = GetMovementNormal();

        Vector3 movementForward = GetMovementForward(normal);
        Vector3 movementSide = Vector3.Cross(normal, movementForward).normalized;

        UpdateCoreMovement(dt, normal, ref movementForward, ref movementSide);
        SyncTailEndIkNodeToCoreDelta();
        UpdateMovementBlockSampling(dt, normal);
        UpdateLegs(dt, normal, movementForward, movementSide);
        UpdateDebugState(normal, movementForward, movementSide);
    }

    [ContextMenu("Initialize Leg Pair")]
    public void Initialize()
    {
        ResolveLegReferences(leftLeg);
        ResolveLegReferences(rightLeg);

        AssignStaticPole(leftLeg);
        AssignStaticPole(rightLeg);

        Vector3 normal = GetMovementNormal();
        Vector3 forward = GetMovementForward(normal);
        Vector3 side = Vector3.Cross(normal, forward).normalized;

        leftLeg.reach = CalculateLegReach(leftLeg);
        rightLeg.reach = CalculateLegReach(rightLeg);

        CaptureLegOffsets(leftLeg, forward, side);
        CaptureLegOffsets(rightLeg, forward, side);

        if (autoSelectInitialLeadingLeg)
        {
            leadingLegIndex = leftLeg.capturedForwardOffset >= rightLeg.capturedForwardOffset ? 0 : 1;
        }
        else
        {
            leadingLegIndex = Mathf.Clamp(initialLeadingLegIndex, 0, 1);
        }

        steppingLegIndex = -1;

        if (coreNode != null)
        {
            lastSampleCorePosition = coreNode.position;
            lastTailSyncCorePosition = coreNode.position;
            hasTailSyncCorePosition = true;
        }
        else
        {
            hasTailSyncCorePosition = false;
        }

        lastMoveDirection = forward;
        currentCoreVelocity = Vector3.zero;
        coreVelocityResponseVelocity = Vector3.zero;
        desiredCoreVelocity = Vector3.zero;

        InitializeFootTargets(leftLeg, normal);
        InitializeFootTargets(rightLeg, normal);

        initialized = true;
    }

    private void UpdateCoreMovement(
        float dt,
        Vector3 normal,
        ref Vector3 movementForward,
        ref Vector3 movementSide
    )
    {
        if (coreNode == null)
        {
            return;
        }

        if (coreMovementMode == CoreMovementMode.ReadExternalCoreOnly)
        {
            currentCoreVelocity = dt > Epsilon
                ? Vector3.ProjectOnPlane(coreNode.position - lastSampleCorePosition, normal) / dt
                : Vector3.zero;

            if (currentCoreVelocity.sqrMagnitude > Epsilon)
            {
                lastMoveDirection = currentCoreVelocity.normalized;
                movementForward = lastMoveDirection;
                movementSide = Vector3.Cross(normal, movementForward).normalized;
            }

            return;
        }

        Vector3 toTarget = GetPlanarRunTargetDelta(normal);
        float distanceToTarget = toTarget.magnitude;

        bool wantsMove = runTarget != null && distanceToTarget > stopDistance;

        float targetMomentum = wantsMove ? 1f : 0f;
        float momentumRate = targetMomentum > momentum
            ? momentumBuildPerSecond
            : momentumDecayPerSecond;

        momentum = Mathf.MoveTowards(
            momentum,
            targetMomentum,
            momentumRate * dt
        );

        Vector3 moveDirection = lastMoveDirection;

        if (wantsMove && distanceToTarget > Epsilon)
        {
            Vector3 targetDirection = toTarget / distanceToTarget;

            if (rotateCoreTowardRunTarget)
            {
                RotateCoreTowardDirection(targetDirection, normal, dt);
            }

            Vector3 facingForward = GetCorePlanarForward(normal);
            moveDirection =
                moveAlongCoreForwardAfterTurning &&
                facingForward.sqrMagnitude > Epsilon &&
                Vector3.Dot(facingForward, targetDirection) > 0f
                    ? facingForward
                    : targetDirection;

            lastMoveDirection = moveDirection;
            movementForward = moveDirection;
            movementSide = Vector3.Cross(normal, movementForward).normalized;
        }

        float slowFactor = wantsMove
            ? Mathf.Clamp01((distanceToTarget - stopDistance) / Mathf.Max(slowDownRadius, Epsilon))
            : 0f;

        float desiredSpeed = Mathf.Lerp(walkSpeed, runSpeed, momentum) * slowFactor;

        desiredCoreVelocity = wantsMove
            ? moveDirection * desiredSpeed
            : Vector3.zero;

        if (!wantsMove && allowMomentumCarry && momentum > momentumCarryThreshold)
        {
            desiredCoreVelocity =
                lastMoveDirection *
                runSpeed *
                momentum *
                momentumCarrySpeedMultiplier;
        }

        currentCoreVelocity = StepSecondOrderVector(
            currentCoreVelocity,
            desiredCoreVelocity,
            ref coreVelocityResponseVelocity,
            coreVelocityFrequencyHz,
            coreVelocityDampingRatio,
            dt,
            maxCoreAcceleration,
            maxCoreSpeed > 0f ? maxCoreSpeed : runSpeed * 2f,
            maxCoreSubstepTime
        );

        Vector3 newCorePosition = coreNode.position + currentCoreVelocity * dt;

        if (raycastCoreToGround)
        {
            newCorePosition = RaycastPointToGround(
                newCorePosition,
                normal,
                coreGroundRayHeight,
                coreGroundRayDistance,
                coreGroundMask,
                coreGroundOffset,
                out _
            );
        }

        coreNode.position = newCorePosition;

        if (currentCoreVelocity.sqrMagnitude > 0.0001f)
        {
            Vector3 facingForward = GetCorePlanarForward(normal);
            movementForward =
                moveAlongCoreForwardAfterTurning &&
                facingForward.sqrMagnitude > Epsilon
                    ? facingForward
                    : currentCoreVelocity.normalized;

            movementSide = Vector3.Cross(normal, movementForward).normalized;
        }
    }

    private void SyncTailEndIkNodeToCoreDelta()
    {
        if (coreNode == null)
        {
            hasTailSyncCorePosition = false;
            return;
        }

        if (!hasTailSyncCorePosition)
        {
            lastTailSyncCorePosition = coreNode.position;
            hasTailSyncCorePosition = true;
            return;
        }

        Vector3 coreDelta = coreNode.position - lastTailSyncCorePosition;
        lastTailSyncCorePosition = coreNode.position;

        if (
            !syncTailEndIkNodeWithCore ||
            tailEndIkNode == null ||
            tailEndIkNode.transform == coreNode ||
            coreDelta.sqrMagnitude <= Epsilon
        )
        {
            return;
        }

        tailEndIkNode.transform.position += coreDelta * tailEndCoreDeltaMultiplier;
    }

    private void UpdateMovementBlockSampling(float dt, Vector3 normal)
    {
        if (coreNode == null)
        {
            return;
        }

        movementSampleTimer += dt;

        if (movementSampleTimer < movementSampleInterval)
        {
            return;
        }

        Vector3 delta = Vector3.ProjectOnPlane(
            coreNode.position - lastSampleCorePosition,
            normal
        );

        lastSampledMovementDistance = delta.magnitude;

        movementBlockDebt +=
            lastSampledMovementDistance *
            movementBlockAccumulationMultiplier;

        lastSampleCorePosition = coreNode.position;
        movementSampleTimer = 0f;
    }

    private void UpdateLegs(
        float dt,
        Vector3 normal,
        Vector3 forward,
        Vector3 side
    )
    {
        timeSinceLastStep += dt;

        bool anyStepActive = false;

        UpdateSingleLegStep(leftLeg, dt, normal, ref anyStepActive);
        UpdateSingleLegStep(rightLeg, dt, normal, ref anyStepActive);
        MaintainPlantedLeg(leftLeg);
        MaintainPlantedLeg(rightLeg);

        if (oneStepAtATime && anyStepActive)
        {
            return;
        }

        if (timeSinceLastStep < minStepInterval)
        {
            return;
        }

        float pairReach = GetPairReach();
        float blockTriggerDistance =
            pairReach * movementDebtStepTriggerReachRatio;

        Leg leadingLeg = GetLeg(leadingLegIndex);

        float leadingForward = GetFootForwardRelativeToCore(
            leadingLeg,
            forward
        );

        float behindTrigger =
            leadingBehindDistance +
            pairReach * leadingBehindReachRatio;

        bool movingEnough =
            currentCoreVelocity.magnitude > 0.05f ||
            momentum > momentumCarryThreshold ||
            movementBlockDebt > blockTriggerDistance;

        bool leadingFellBehind =
            leadingForward < -behindTrigger;

        bool movementDebtNeedsStep =
            movementBlockDebt > blockTriggerDistance &&
            movingEnough;

        if (!movingEnough)
        {
            return;
        }

        if (!leadingFellBehind && !movementDebtNeedsStep)
        {
            return;
        }

        int stepLegIndex = 1 - leadingLegIndex;
        Leg legToStep = GetLeg(stepLegIndex);

        if (legToStep.isStepping)
        {
            return;
        }

        float requestedStepLength;
        float selectedStepLength = CalculateStepLength(
            pairReach,
            out requestedStepLength
        );

        Vector3 stepDestination = CalculateStepDestination(
            legToStep,
            selectedStepLength,
            normal,
            forward,
            side,
            out Vector3 landingNormal
        );

        StartLegStep(
            legToStep,
            stepLegIndex,
            stepDestination,
            landingNormal,
            selectedStepLength,
            pairReach
        );

        movementBlockDebt = Mathf.Max(
            0f,
            movementBlockDebt - selectedStepLength
        );

        debugState.requestedStepLength = requestedStepLength;
        debugState.selectedStepLength = selectedStepLength;
    }

    private void UpdateSingleLegStep(
        Leg leg,
        float dt,
        Vector3 normal,
        ref bool anyStepActive
    )
    {
        if (leg == null || !leg.isStepping)
        {
            return;
        }

        anyStepActive = true;

        leg.stepTimer += dt;

        float t = Mathf.Clamp01(leg.stepTimer / Mathf.Max(leg.stepDuration, Epsilon));

        float travelT = stepTravelCurve != null
            ? Mathf.Clamp01(stepTravelCurve.Evaluate(t))
            : SmoothStep01(t);

        float liftT = stepLiftCurve != null
            ? Mathf.Max(0f, stepLiftCurve.Evaluate(t))
            : 4f * t * (1f - t);

        Vector3 basePosition = Vector3.Lerp(
            leg.stepStartWorld,
            leg.stepEndWorld,
            travelT
        );

        Vector3 liftedPosition =
            basePosition +
            leg.stepLiftAxis.normalized * (leg.stepHeight * liftT);

        WriteLegIkTarget(leg, liftedPosition);

        if (t >= 0.999f)
        {
            leg.isStepping = false;
            leg.plantedWorldPosition = leg.stepEndWorld;

            WriteLegIkTarget(leg, leg.stepEndWorld);

            steppingLegIndex = -1;

            if (leg == leftLeg)
            {
                leadingLegIndex = 0;
            }
            else
            {
                leadingLegIndex = 1;
            }
        }
    }

    private void StartLegStep(
        Leg leg,
        int legIndex,
        Vector3 destination,
        Vector3 landingNormal,
        float stepLength,
        float pairReach
    )
    {
        destination = ClampLegTargetToReach(leg, destination);

        Vector3 start = GetCurrentLegIkTargetPosition(leg, destination);

        leg.stepStartWorld = start;
        leg.stepEndWorld = destination;
        leg.plantedWorldPosition = destination;
        leg.stepTimer = 0f;

        float fullStep = pairReach * Mathf.Max(fullStepReachRatio, Epsilon);
        float length01 = Mathf.Clamp01(stepLength / fullStep);

        leg.stepDuration = Mathf.Lerp(
            slowStepDuration,
            fastStepDuration,
            momentum
        );

        if (scaleDurationByStepLength)
        {
            float durationScale = Mathf.Lerp(
                1f - stepLengthDurationInfluence,
                1f + stepLengthDurationInfluence,
                length01
            );

            leg.stepDuration *= Mathf.Max(0.05f, durationScale);
        }

        leg.stepHeight =
            pairReach *
            (baseStepHeightReachRatio + momentum * momentumStepHeightReachRatio);

        leg.stepHeight += stepLength * stepLengthHeightInfluence;

        leg.stepLiftAxis = GetStepLiftAxis(landingNormal);

        leg.isStepping = true;
        steppingLegIndex = legIndex;
        timeSinceLastStep = 0f;

        WriteLegRealTarget(leg, destination);
    }

    private Vector3 CalculateStepDestination(
        Leg leg,
        float stepLength,
        Vector3 normal,
        Vector3 forward,
        Vector3 side,
        out Vector3 landingNormal
    )
    {
        landingNormal = normal;

        Vector3 corePosition = coreNode != null ? coreNode.position : transform.position;
        float laneSideOffset = GetLegStepSideOffset(leg, normal, side);
        float laneForwardOffset = Mathf.Max(0f, leg.capturedForwardOffset);

        Vector3 desired =
            corePosition +
            side * laneSideOffset +
            forward * (laneForwardOffset + stepLength);

        Vector3 grounded = FindGroundedStepPoint(
            leg,
            desired,
            normal,
            forward,
            side,
            out landingNormal
        );

        grounded = ProjectLegTargetIntoReach(leg, grounded);

        if (raycastFeetToGround)
        {
            grounded = FindGroundedStepPoint(
                leg,
                grounded,
                normal,
                forward,
                side,
                out landingNormal
            );

            grounded = ProjectLegTargetIntoReach(leg, grounded);
        }

        return grounded;
    }

    private float CalculateStepLength(
        float pairReach,
        out float requestedStepLength
    )
    {
        float baseLength = pairReach * baseStepReachRatio;
        float momentumLength = pairReach * momentumStepReachRatio * momentum;
        float movementDebtLength = movementBlockDebt * movementBlockStepInfluence;

        requestedStepLength = baseLength + momentumLength + movementDebtLength;

        float minLength = pairReach * minStepReachRatio;
        float maxLength = pairReach * maxStepReachRatio;

        requestedStepLength = Mathf.Clamp(
            requestedStepLength,
            minLength,
            maxLength
        );

        if (!useStepBuckets)
        {
            return requestedStepLength;
        }

        float micro = pairReach * microStepReachRatio;
        float small = pairReach * smallStepReachRatio;
        float medium = pairReach * mediumStepReachRatio;
        float full = pairReach * fullStepReachRatio;

        micro = Mathf.Clamp(micro, minLength, maxLength);
        small = Mathf.Clamp(small, minLength, maxLength);
        medium = Mathf.Clamp(medium, minLength, maxLength);
        full = Mathf.Clamp(full, minLength, maxLength);

        if (requestedStepLength <= micro)
        {
            return micro;
        }

        if (requestedStepLength <= small)
        {
            return small;
        }

        if (requestedStepLength <= medium)
        {
            return medium;
        }

        return full;
    }

    private Vector3 ProjectLegTargetIntoReach(Leg leg, Vector3 worldPoint)
    {
        Transform legStart = GetLegStartTransform(leg);

        if (legStart == null)
        {
            return worldPoint;
        }

        float reach = Mathf.Max(0.001f, leg.reach);
        float safeReach = Mathf.Max(
            0.001f,
            reach * legReachMultiplier - legReachSafetyPadding
        );

        Vector3 fromStart = worldPoint - legStart.position;
        float distance = fromStart.magnitude;

        if (distance <= safeReach)
        {
            return worldPoint;
        }

        Vector3 direction = distance > Epsilon
            ? fromStart / distance
            : Vector3.forward;

        return legStart.position + direction * safeReach;
    }

    private Vector3 ClampLegTargetToReach(Leg leg, Vector3 worldPoint)
    {
        if (!clampTargetsWithLimbSolver || leg == null || leg.limbSolver == null)
        {
            return ProjectLegTargetIntoReach(leg, worldPoint);
        }

        if (!leg.limbSolver.IsInitialized)
        {
            leg.limbSolver.InitializeChainData();
        }

        if (!leg.limbSolver.IsInitialized)
        {
            return ProjectLegTargetIntoReach(leg, worldPoint);
        }

        Vector3 clamped = leg.limbSolver.ClampWorldPointToReach(
            worldPoint,
            leg.limbSolver.enforceMinimumReachOnTail
        );

        return ProjectLegTargetIntoReach(leg, clamped);
    }

    private float GetLegStepSideOffset(Leg leg, Vector3 normal, Vector3 side)
    {
        if (!usePoleLaneForStepSide || leg == null || leg.staticPole == null || coreNode == null)
        {
            return leg != null ? leg.capturedSideOffset : 0f;
        }

        Vector3 toPole = Vector3.ProjectOnPlane(
            leg.staticPole.position - coreNode.position,
            normal
        );

        if (toPole.sqrMagnitude <= Epsilon)
        {
            return leg.capturedSideOffset;
        }

        return Vector3.Dot(toPole, side.normalized);
    }

    private Vector3 FindGroundedStepPoint(
        Leg leg,
        Vector3 desired,
        Vector3 normal,
        Vector3 forward,
        Vector3 side,
        out Vector3 landingNormal
    )
    {
        landingNormal = normal;

        if (!raycastFeetToGround)
        {
            return desired;
        }

        if (!chooseLowestGroundAroundStep)
        {
            return RaycastPointToGround(
                desired,
                normal,
                footRayHeight,
                footRayDistance,
                footGroundMask,
                footGroundOffset,
                out landingNormal
            );
        }

        float searchRadius =
            Mathf.Max(0f, leg != null ? leg.reach : GetPairReach()) *
            lowestGroundSearchRadiusReachRatio;

        Vector3 bestPoint = RaycastPointToGround(
            desired,
            normal,
            footRayHeight,
            footRayDistance,
            footGroundMask,
            footGroundOffset,
            out landingNormal
        );

        float bestHeight = Vector3.Dot(bestPoint, normal);

        if (searchRadius <= Epsilon)
        {
            return bestPoint;
        }

        Vector3 safeForward = forward.sqrMagnitude > Epsilon
            ? forward.normalized
            : GetMovementForward(normal);
        Vector3 safeSide = side.sqrMagnitude > Epsilon
            ? side.normalized
            : Vector3.Cross(normal, safeForward).normalized;

        for (int sideIndex = -1; sideIndex <= 1; sideIndex++)
        {
            for (int forwardIndex = -1; forwardIndex <= 1; forwardIndex++)
            {
                if (sideIndex == 0 && forwardIndex == 0)
                {
                    continue;
                }

                Vector3 candidate =
                    desired +
                    safeSide * (sideIndex * searchRadius) +
                    safeForward * (forwardIndex * searchRadius * 0.75f);

                Vector3 candidateNormal;
                Vector3 grounded = RaycastPointToGround(
                    candidate,
                    normal,
                    footRayHeight,
                    footRayDistance,
                    footGroundMask,
                    footGroundOffset,
                    out candidateNormal
                );

                float candidateHeight = Vector3.Dot(grounded, normal);

                if (candidateHeight < bestHeight)
                {
                    bestHeight = candidateHeight;
                    bestPoint = grounded;
                    landingNormal = candidateNormal;
                }
            }
        }

        return bestPoint;
    }

    private Vector3 RaycastPointToGround(
        Vector3 point,
        Vector3 normal,
        float rayHeight,
        float rayDistance,
        LayerMask mask,
        float groundOffset,
        out Vector3 hitNormal
    )
    {
        normal = normal.sqrMagnitude > Epsilon
            ? normal.normalized
            : Vector3.up;

        Vector3 origin = point + normal * rayHeight;
        Vector3 direction = -normal;

        if (Physics.Raycast(
                origin,
                direction,
                out RaycastHit hit,
                rayHeight + rayDistance,
                mask,
                QueryTriggerInteraction.Ignore
            ))
        {
            hitNormal = hit.normal.sqrMagnitude > Epsilon
                ? hit.normal.normalized
                : normal;

            return hit.point + hitNormal * groundOffset;
        }

        hitNormal = normal;
        return point;
    }

    private Vector3 GetStepLiftAxis(Vector3 landingNormal)
    {
        switch (stepLiftAxisMode)
        {
            case StepLiftAxisMode.GroundNormalAtLanding:
                return landingNormal.sqrMagnitude > Epsilon
                    ? landingNormal.normalized
                    : GetMovementNormal();

            case StepLiftAxisMode.WorldUp:
                return Vector3.up;

            case StepLiftAxisMode.MovementPlaneNormal:
            default:
                return GetMovementNormal();
        }
    }

    private Vector3 GetMovementNormal()
    {
        if (movementPlaneNormal.sqrMagnitude <= Epsilon)
        {
            return Vector3.up;
        }

        return movementPlaneNormal.normalized;
    }

    private Vector3 GetMovementForward(Vector3 normal)
    {
        Vector3 forward = Vector3.zero;

        if (rotateCoreTowardRunTarget && coreNode != null)
        {
            forward = GetCorePlanarForward(normal);

            if (forward.sqrMagnitude > Epsilon)
            {
                return forward.normalized;
            }
        }

        if (runTarget != null && coreNode != null)
        {
            forward = Vector3.ProjectOnPlane(
                runTarget.position - coreNode.position,
                normal
            );
        }

        if (forward.sqrMagnitude > Epsilon)
        {
            return forward.normalized;
        }

        if (currentCoreVelocity.sqrMagnitude > Epsilon)
        {
            forward = Vector3.ProjectOnPlane(currentCoreVelocity, normal);

            if (forward.sqrMagnitude > Epsilon)
            {
                return forward.normalized;
            }
        }

        if (forwardReference != null && coreNode != null)
        {
            forward = Vector3.ProjectOnPlane(
                forwardReference.position - coreNode.position,
                normal
            );

            if (forward.sqrMagnitude > Epsilon)
            {
                return forward.normalized;
            }
        }

        if (coreNode != null)
        {
            forward = Vector3.ProjectOnPlane(coreNode.forward, normal);

            if (forward.sqrMagnitude > Epsilon)
            {
                return forward.normalized;
            }
        }

        return Vector3.forward;
    }

    private Vector3 GetCorePlanarForward(Vector3 normal)
    {
        if (coreNode == null)
        {
            return Vector3.zero;
        }

        Vector3 forward = Vector3.ProjectOnPlane(coreNode.forward, normal);

        return forward.sqrMagnitude > Epsilon
            ? forward.normalized
            : Vector3.zero;
    }

    private void RotateCoreTowardDirection(
        Vector3 targetDirection,
        Vector3 normal,
        float dt
    )
    {
        if (coreNode == null || targetDirection.sqrMagnitude <= Epsilon)
        {
            return;
        }

        Vector3 safeNormal = normal.sqrMagnitude > Epsilon
            ? normal.normalized
            : Vector3.up;
        Vector3 safeDirection = Vector3.ProjectOnPlane(
            targetDirection,
            safeNormal
        );

        if (safeDirection.sqrMagnitude <= Epsilon)
        {
            return;
        }

        Quaternion targetRotation = Quaternion.LookRotation(
            safeDirection.normalized,
            safeNormal
        );

        coreNode.rotation = coreTurnSpeedDegrees > 0f
            ? Quaternion.RotateTowards(
                coreNode.rotation,
                targetRotation,
                coreTurnSpeedDegrees * dt
            )
            : targetRotation;
    }

    private Vector3 GetPlanarRunTargetDelta(Vector3 normal)
    {
        if (coreNode == null || runTarget == null)
        {
            return Vector3.zero;
        }

        return Vector3.ProjectOnPlane(
            runTarget.position - coreNode.position,
            normal
        );
    }

    private float GetFootForwardRelativeToCore(Leg leg, Vector3 forward)
    {
        if (leg == null || leg.fakeTarget == null || coreNode == null)
        {
            return 0f;
        }

        Vector3 fromCore = leg.fakeTarget.position - coreNode.position;
        return Vector3.Dot(fromCore, forward.normalized);
    }

    private float GetPairReach()
    {
        float leftReach = Mathf.Max(0.001f, leftLeg.reach);
        float rightReach = Mathf.Max(0.001f, rightLeg.reach);

        return Mathf.Min(leftReach, rightReach);
    }

    private Leg GetLeg(int index)
    {
        return index == 0 ? leftLeg : rightLeg;
    }

    private void ResolveLegReferences(Leg leg)
    {
        if (leg == null)
        {
            return;
        }

        if (leg.limbSolver == null)
        {
            return;
        }

        if (leg.tailNode == null)
        {
            leg.tailNode = leg.limbSolver.tail;
        }

        if (leg.fakeTarget == null && leg.tailNode != null)
        {
            leg.fakeTarget = leg.tailNode.transform;
        }

        if (leg.fakeTargetOffsetNode == null && leg.fakeTarget != null)
        {
            leg.fakeTargetOffsetNode =
                leg.fakeTarget.GetComponent<OffsetPositioningNode>();
        }

        if (leg.realTargetOffsetNode == null && leg.realTarget != null)
        {
            leg.realTargetOffsetNode =
                leg.realTarget.GetComponent<OffsetPositioningNode>();
        }

        if (leg.limbSolver != null && !leg.limbSolver.IsInitialized)
        {
            leg.limbSolver.InitializeChainData();
        }
    }

    private void CaptureLegOffsets(Leg leg, Vector3 forward, Vector3 side)
    {
        if (leg == null || leg.fakeTarget == null || coreNode == null)
        {
            return;
        }

        Vector3 fromCore = leg.fakeTarget.position - coreNode.position;

        leg.capturedSideOffset = Vector3.Dot(fromCore, side);
        leg.capturedForwardOffset = Vector3.Dot(fromCore, forward);
    }

    private void InitializeFootTargets(Leg leg, Vector3 normal)
    {
        if (leg == null || leg.fakeTarget == null)
        {
            return;
        }

        Vector3 position = leg.fakeTarget.position;

        leg.plantedWorldPosition = position;
        WriteLegRealTarget(leg, position);
        WriteLegIkTarget(leg, position);

        leg.isStepping = false;
        leg.stepTimer = 0f;
    }

    private void MaintainPlantedLeg(Leg leg)
    {
        if (leg == null || leg.isStepping)
        {
            return;
        }

        WriteLegIkTarget(leg, leg.plantedWorldPosition);
    }

    private float CalculateLegReach(Leg leg)
    {
        if (leg == null)
        {
            return 1f;
        }

        if (leg.limbSolver != null && leg.limbSolver.CumulativeBones > Epsilon)
        {
            return Mathf.Max(0.001f, (float)leg.limbSolver.CumulativeBones);
        }

        float chainReach = CalculateNodeStateChainReach(leg);

        if (chainReach > Epsilon)
        {
            return chainReach;
        }

        return Mathf.Max(0.001f, leg.manualReach);
    }

    private float CalculateNodeStateChainReach(Leg leg)
    {
        if (leg == null)
        {
            return 0f;
        }

        NodeState tail = leg.tailNode;

        if (tail == null && leg.limbSolver != null)
        {
            tail = leg.limbSolver.tail;
        }

        NodeState start = leg.limbSolver != null
            ? leg.limbSolver.start
            : null;

        if (tail == null)
        {
            return 0f;
        }

        float total = 0f;
        NodeState current = tail;
        int guard = 0;

        while (current != null && current.next != null && guard < MaxChainNodes)
        {
            guard++;

            float boneLength = current.Mylength.magnitude;

            if (boneLength <= Epsilon)
            {
                boneLength = Vector3.Distance(
                    current.transform.position,
                    current.next.transform.position
                );
            }

            total += boneLength;

            if (start != null && current.next == start)
            {
                break;
            }

            current = current.next;
        }

        return total;
    }

    private Transform GetLegStartTransform(Leg leg)
    {
        if (leg != null && leg.limbSolver != null && leg.limbSolver.start != null)
        {
            return leg.limbSolver.start.transform;
        }

        return coreNode;
    }

    private void AssignStaticPole(Leg leg)
    {
        if (leg == null || leg.staticPole == null)
        {
            return;
        }

        NodeState tail = leg.tailNode;

        if (tail == null && leg.limbSolver != null)
        {
            tail = leg.limbSolver.tail;
        }

        NodeState start = leg.limbSolver != null
            ? leg.limbSolver.start
            : null;

        NodeState current = tail;
        int guard = 0;

        while (current != null && guard < MaxChainNodes)
        {
            guard++;

            current.pole = leg.staticPole;

            if (start != null && current == start)
            {
                break;
            }

            current = current.next;
        }
    }

    private void WriteLegFakeTarget(Leg leg, Vector3 worldPosition)
    {
        if (leg == null)
        {
            return;
        }

        if (leg.writeFakeTargetThroughOffsetNode && leg.fakeTargetOffsetNode != null)
        {
            leg.fakeTargetOffsetNode.SetDynamicOffsetToReachWorldPosition(
                leg.fakeTargetDynamicOffsetId,
                worldPosition
            );

            if (applyTargetOffsetImmediately)
            {
                leg.fakeTargetOffsetNode.ApplyPosition();
            }

            return;
        }

        if (leg.fakeTarget != null)
        {
            leg.fakeTarget.position = worldPosition;
        }
    }

    private void WriteLegIkTarget(Leg leg, Vector3 worldPosition)
    {
        if (leg == null)
        {
            return;
        }

        Vector3 clampedWorldPosition = ClampLegTargetToReach(leg, worldPosition);

        WriteLegFakeTarget(leg, clampedWorldPosition);

        if (leg.tailNode != null)
        {
            leg.tailNode.transform.position = clampedWorldPosition;
        }

        if (solveLegAfterTargetWrite && leg.limbSolver != null)
        {
            leg.limbSolver.Apply();
        }
    }

    private Vector3 GetCurrentLegIkTargetPosition(Leg leg, Vector3 fallback)
    {
        if (leg == null)
        {
            return fallback;
        }

        if (leg.tailNode != null)
        {
            return leg.tailNode.transform.position;
        }

        if (leg.fakeTarget != null)
        {
            return leg.fakeTarget.position;
        }

        return fallback;
    }

    private void WriteLegRealTarget(Leg leg, Vector3 worldPosition)
    {
        if (leg == null)
        {
            return;
        }

        if (leg.writeRealTargetThroughOffsetNode && leg.realTargetOffsetNode != null)
        {
            leg.realTargetOffsetNode.SetDynamicOffsetToReachWorldPosition(
                leg.realTargetDynamicOffsetId,
                worldPosition
            );

            if (applyTargetOffsetImmediately)
            {
                leg.realTargetOffsetNode.ApplyPosition();
            }

            return;
        }

        if (leg.realTarget != null)
        {
            leg.realTarget.position = worldPosition;
        }
    }

    private Vector3 StepSecondOrderVector(
        Vector3 current,
        Vector3 target,
        ref Vector3 derivative,
        float frequencyHz,
        float dampingRatio,
        float deltaTime,
        float maxAcceleration,
        float maxOutputMagnitude,
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

            Vector3 acceleration =
                stiffness * (target - current)
                - damping * derivative;

            if (maxAcceleration > 0f &&
                acceleration.magnitude > maxAcceleration)
            {
                acceleration = acceleration.normalized * maxAcceleration;
            }

            derivative += acceleration * step;
            current += derivative * step;

            if (maxOutputMagnitude > 0f &&
                current.magnitude > maxOutputMagnitude)
            {
                current = current.normalized * maxOutputMagnitude;
            }
        }

        return current;
    }

    private float SmoothStep01(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    private void UpdateDebugState(
        Vector3 normal,
        Vector3 forward,
        Vector3 side
    )
    {
        if (coreNode == null)
        {
            return;
        }

        debugState.corePosition = coreNode.position;
        debugState.runTargetPosition = runTarget != null ? runTarget.position : coreNode.position;
        debugState.movementForward = forward;
        debugState.movementSide = side;
        debugState.movementNormal = normal;

        debugState.distanceToRunTarget =
            runTarget != null
                ? Vector3.ProjectOnPlane(runTarget.position - coreNode.position, normal).magnitude
                : 0f;

        debugState.momentum = momentum;
        debugState.movementBlockDebt = movementBlockDebt;
        debugState.pairReach = GetPairReach();

        debugState.leadingLegIndex = leadingLegIndex;
        debugState.steppingLegIndex = steppingLegIndex;

        debugState.leftForward = GetFootForwardRelativeToCore(leftLeg, forward);
        debugState.rightForward = GetFootForwardRelativeToCore(rightLeg, forward);
    }

    private void OnDrawGizmosSelected()
    {
        if (coreNode == null)
        {
            return;
        }

        Vector3 normal = movementPlaneNormal.sqrMagnitude > Epsilon
            ? movementPlaneNormal.normalized
            : Vector3.up;

        Vector3 forward = Application.isPlaying
            ? debugState.movementForward
            : Vector3.ProjectOnPlane(coreNode.forward, normal).normalized;

        if (forward.sqrMagnitude <= Epsilon)
        {
            forward = Vector3.forward;
        }

        Vector3 side = Vector3.Cross(normal, forward).normalized;

        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(coreNode.position, coreNode.position + forward);
        Gizmos.DrawLine(coreNode.position, coreNode.position + side);

        DrawLegGizmos(leftLeg, Color.green);
        DrawLegGizmos(rightLeg, Color.yellow);

        if (runTarget != null)
        {
            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(coreNode.position, runTarget.position);
            Gizmos.DrawWireSphere(runTarget.position, 0.12f);
        }
    }

    private void DrawLegGizmos(Leg leg, Color color)
    {
        if (leg == null)
        {
            return;
        }

        Gizmos.color = color;

        if (leg.fakeTarget != null)
        {
            Gizmos.DrawSphere(leg.fakeTarget.position, 0.07f);
        }

        if (leg.realTarget != null)
        {
            Gizmos.DrawWireSphere(leg.realTarget.position, 0.09f);
        }

        if (leg.fakeTarget != null && leg.realTarget != null)
        {
            Gizmos.DrawLine(leg.fakeTarget.position, leg.realTarget.position);
        }

        Transform start = GetLegStartTransform(leg);

        if (start != null)
        {
            Gizmos.DrawWireSphere(start.position, 0.05f);
            Gizmos.DrawWireSphere(start.position, Mathf.Max(0.001f, leg.reach * legReachMultiplier));
        }

        if (leg.isStepping)
        {
            Gizmos.color = Color.white;
            Gizmos.DrawLine(leg.stepStartWorld, leg.stepEndWorld);
            Gizmos.DrawWireSphere(leg.stepEndWorld, 0.12f);
        }
    }
}
