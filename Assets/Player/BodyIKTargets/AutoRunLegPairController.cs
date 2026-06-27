using System;
using System.Collections.Generic;
using UnityEngine;

[DefaultExecutionOrder(175)]
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
        [Tooltip("Explicit start/root node of this leg IK chain. If empty, limbSolver.start is used.")]
        public NodeState startNode;
        public NodeState tailNode;

        [Tooltip("The actual IK fake target that the limb follows.")]
        public Transform fakeTarget;

        [Tooltip("The planted / intended target. This script moves this to the next grounded step point.")]
        public Transform realTarget;

        [Tooltip("Static knee/leg pole. This stays in front of the leg and is used for gait angle / lane measurement.")]
        public Transform staticPole;

        [Tooltip("Optional physical pole used only by the IK solver. Leave empty to auto-create one at runtime.")]
        public Transform physicalIkPole;

        [Tooltip("Legacy compatibility only. Keep false for this rig: the static/front pole is the physical IK pole too.")]
        public bool flipPhysicalIkPoleBehindStaticPole = false;

        [Tooltip("Optional override distance from the leg start to the physical IK pole. 0 uses the current static pole distance.")]
        [Min(0f)] public float physicalIkPoleDistance = 0f;

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
        public float capturedPoleSideOffset;
        public float capturedPoleForwardOffset;
        public float capturedPoleNormalOffset;
        public Transform orbitCore;
        public bool useLimbStartAsOrbitCore = true;
        public float capturedFakeSideOffset;
        public float capturedFakeForwardOffset;
        public float capturedFakeNormalOffset;
        public float capturedRealSideOffset;
        public float capturedRealForwardOffset;
        public float capturedRealNormalOffset;
        public float reach;
        public float upperLegLength;
        public float lowerLegLength;

        public bool isStepping;
        public Vector3 stepStartWorld;
        public Vector3 stepEndWorld;
        public Vector3 stepLiftAxis;
        public Vector3 plantedWorldPosition;
        public Vector3 lazyFakeTargetWorld;
        public Vector3 lazyFakeTargetVelocity;
        public float stepTimer;
        public float stepDuration;
        public float stepHeight;
        public bool lazyFakeTargetInitialized;
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

    [Header("Gait Orientation")]
    [Tooltip("When enabled, leg lanes and poles use this vector as their forward axis instead of the current movement direction.")]
    public bool useExternalGaitForward = false;

    public Vector3 externalGaitForward = Vector3.forward;

    [Tooltip("Rotates each leg's static pole around the core to match the gait forward axis. This keeps knee bending stable when moving sideways relative to the held facing direction.")]
    public bool rotateStaticPolesWithGaitForward = true;

    [Tooltip("Optional physical direction target. It is kept at its captured radius from gaitRotationCore and rotated toward externalGaitForward.")]
    public Transform gaitForwardTarget;

    public Transform gaitRotationCore;

    [Tooltip("Optional assigner that receives the current gait yaw so rotatable leg nodes actually turn toward gaitForwardTarget.")]
    public RotationAssigner gaitRotationAssigner;

    [Tooltip("Stable zero-yaw forward reference for gaitRotationAssigner. Leave empty to use world/core forward.")]
    public Transform gaitRotationAngleReference;

    public bool rotateGaitForwardTargetWithExternalForward = true;

    [Tooltip("Rotates idle real/fake foot targets around each leg core using the current gait forward, like a rotatable node.")]
    public bool rotateLegTargetsWithGaitForward = true;

    [Tooltip("Keeps idle feet slightly in front of their own leg core. This prevents left/right targets collapsing together when the gait basis changes.")]
    [Min(0f)] public float minimumForwardTargetOffsetRatio = 0.12f;

    [Tooltip("When true, leg starts/poles that have OffsetPositioningNode/RotatableNode components are not moved manually by this controller. Their offsets and the RotationAssigner are the source of truth.")]
    public bool authoritativeOffsetNodesForRotatedLegAssembly = true;

    [Tooltip("Hard-applies calculated lower-body rotatable world positions after dynamic offsets. Use this when IK/offset timing otherwise swallows visible leg start/pole rotation.")]
    public bool forceLowerBodyRotatedWorldPositions = true;

    [Header("Tail End IK Core Sync")]
    [Tooltip("Optional tail-end IK node that receives the same frame movement delta as the core.")]
    public NodeState tailEndIkNode;

    [Tooltip("Moves the tail-end IK node by the core's delta instead of setting it to a fixed position.")]
    public bool syncTailEndIkNodeWithCore = true;

    [Tooltip("1 follows the core delta. -1 mirrors the delta. Values between/above scale the additive sync.")]
    public float tailEndCoreDeltaMultiplier = 1f;

    [Tooltip("Extra IK endpoint nodes that should receive the same core delta before their solver runs. Use this for spine start/end nodes that are not parented directly to the moving core.")]
    public NodeState[] ikNodesSyncedWithCoreDelta = Array.Empty<NodeState>();

    [Tooltip("Always merge the full spine solver chain into ikNodesSyncedWithCoreDelta. This prevents partial prefab wiring from leaving torso IK nodes at world zero while only the core/tail move.")]
    public bool rebuildFullSpineSyncListAtRuntime = true;

    [Tooltip("Moves each leg IK start/hip node by the body core delta when those starts are not parented under the core. Without this, the core can walk away while legs remain at the prefab origin.")]
    public bool syncLegStartNodesWithCoreDelta = true;

    [Tooltip("Spine target setters refreshed immediately after this controller moves the core, preventing one-frame target lag.")]
    public SpineFakeTargetSetter[] spineTargetSettersToRefreshAfterCoreMove = Array.Empty<SpineFakeTargetSetter>();

    [Tooltip("Solvers applied immediately after post-core-move target refresh. Useful for the spine so meshing reads solved middle nodes this frame.")]
    public LimbSolver[] solversToSolveAfterCoreMove = Array.Empty<LimbSolver>();

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
    public float momentumBuildPerSecond = 4.5f;

    [Tooltip("Momentum decays after stopping / overshooting.")]
    [Min(0f)]
    public float momentumDecayPerSecond = 10.0f;

    [Tooltip("If true, core can continue a little because of momentum even after reaching the target.")]
    public bool allowMomentumCarry = false;

    [Range(0f, 1f)]
    public float momentumCarryThreshold = 0.08f;

    [Min(0f)]
    public float momentumCarrySpeedMultiplier = 0.15f;

    [Header("Core Second Order Response")]
    [Tooltip("Higher = core velocity reacts faster.")]
    [Min(0.01f)]
    public float coreVelocityFrequencyHz = 6.5f;

    [Tooltip("1 = stable/no overshoot. Below 1 = overshoot. Above 1 = heavy damping.")]
    [Min(0f)]
    public float coreVelocityDampingRatio = 1.35f;

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

    [Tooltip("If true, the optional gait rotation core turns toward the gait forward target. This keeps leg-side rotators aligned without forcing the movement core/body to face the run target.")]
    public bool rotateGaitRotationCoreTowardForward = true;

    [Min(0f)]
    public float gaitRotationCoreTurnSpeedDegrees = 900f;

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

    [Header("Runtime Leg Dimensions")]
    [Tooltip("When enabled, leg NodeState bone lengths are rebuilt from bodyHeightOffGround and kneeDefaultBendAngle at runtime instead of trusting authored prefab lengths.")]
    public bool deriveLegDimensionsAtRuntime = true;

    [Tooltip("Comfortable vertical distance from the body/core to grounded soles. ProceduralPlayerRig can use this as the body height target.")]
    [Min(0.01f)] public float bodyHeightOffGround = 2.0f;

    [Tooltip("Default knee bend used to choose total leg length. Higher bend means longer leg bones for the same body height.")]
    [Range(0f, 120f)] public float kneeDefaultBendAngle = 32f;

    [Tooltip("Upper-leg share of total leg length. 0.5 makes upper/lower equal.")]
    [Range(0.2f, 0.8f)] public float upperLegLengthRatio = 0.5f;

    [Tooltip("Extra multiplier for runtime IK leg length. Use this to force a visible knee bend and enough reach for larger speed-scaled strides without raising the body height.")]
    [Min(0.1f)] public float runtimeLegLengthMultiplier = 1.16f;

    [Tooltip("Applies the runtime dimensions back into the NodeState.Mylength values before solvers initialize.")]
    public bool writeRuntimeDimensionsToNodeStateLengths = true;

    public float DesiredBodyHeightOffGround => Mathf.Max(0.01f, bodyHeightOffGround);

    [Tooltip("If true, the leg with greater forward offset at Start begins as leading.")]
    public bool autoSelectInitialLeadingLeg = true;

    [Tooltip("0 = left starts leading, 1 = right starts leading. Used if autoSelectInitialLeadingLeg is false.")]
    [Range(0, 1)]
    public int initialLeadingLegIndex = 0;

    [Tooltip("Only one foot steps at a time.")]
    public bool oneStepAtATime = true;

    [Tooltip("Minimum time between step starts.")]
    [Min(0f)]
    public float minStepInterval = 0.34f;

    [Tooltip("Idle/correction-only feet must wait at least this long before nudging back home.")]
    [Min(0f)] public float idleCorrectionStepInterval = 0.85f;

    [Tooltip("Idle/correction-only feet must drift this far from home before a small correction step is allowed.")]
    [Min(0f)] public float idleCorrectionStepTriggerReachRatio = 0.26f;

    [Tooltip("Any deterministic moving step shorter than this fraction of reach is ignored. This prevents tiny shuffle steps.")]
    [Min(0f)] public float minimumVisibleStepReachRatio = 0.58f;

    [Tooltip("Leading leg must fall this far behind the core before the other leg steps.")]
    [Min(0f)]
    public float leadingBehindDistance = 0.15f;

    [Tooltip("Additional behind trigger as a fraction of pair reach.")]
    [Min(0f)]
    public float leadingBehindReachRatio = 0.04f;

    [Header("Step Size")]
    [Tooltip("Main stride ratio relative to pair reach.")]
    [Min(0f)]
    public float baseStepReachRatio = 0.78f;

    [Tooltip("Momentum adds this much extra stride ratio at full momentum.")]
    [Min(0f)]
    public float momentumStepReachRatio = 0.38f;

    [Tooltip("Movement block debt adds to requested stride.")]
    [Min(0f)]
    public float movementBlockStepInfluence = 0.80f;

    [Tooltip("Final clamp for tiny steps.")]
    [Min(0f)]
    public float minStepReachRatio = 0.62f;

    [Tooltip("Final clamp for large steps.")]
    [Min(0f)]
    public float maxStepReachRatio = 1.28f;

    [Header("Speed Adaptive Steps")]
    [Tooltip("How far ahead the foot target should account for current movement speed.")]
    [Min(0f)] public float speedLookAheadTime = 0.52f;

    [Tooltip("Minimum speed-driven forward step as a fraction of reach.")]
    [Min(0f)] public float minSpeedStepReachRatio = 0.62f;

    [Tooltip("Maximum speed-driven forward step as a fraction of reach.")]
    [Min(0f)] public float maxSpeedStepReachRatio = 1.22f;

    [Tooltip("Extra forward bias added to moving home positions so every moving step lands ahead of the body, including side/back movement lanes.")]
    [Min(0f)] public float movingStepForwardBiasReachRatio = 0.48f;

    [Tooltip("Minimum planar reach preserved for foot homes even when vertical body height would otherwise collapse the horizontal reach clamp.")]
    [Range(0f, 1f)] public float minimumPlanarReachRatioForFootTargets = 0.60f;

    [Tooltip("Foot target movement speed relative to core run speed. Higher values make feet catch up harder.")]
    [Min(0.1f)] public float footTargetSpeedMultiplier = 1.7f;

    [Tooltip("Smallest allowed step duration after speed adaptation.")]
    [Min(0.01f)] public float minAdaptiveStepDuration = 0.42f;

    [Tooltip("Largest allowed step duration after speed adaptation.")]
    [Min(0.01f)] public float maxAdaptiveStepDuration = 0.62f;

    [Header("Step Buckets")]
    [Tooltip("Quantizes steps into micro/small/medium/full. Good for clean rhythm.")]
    public bool useStepBuckets = true;

    [Min(0f)] public float microStepReachRatio = 0.62f;
    [Min(0f)] public float smallStepReachRatio = 0.78f;
    [Min(0f)] public float mediumStepReachRatio = 0.96f;
    [Min(0f)] public float fullStepReachRatio = 0.98f;

    [Header("Step Timing")]
    [Min(0.01f)]
    public float slowStepDuration = 0.52f;

    [Min(0.01f)]
    public float fastStepDuration = 0.34f;

    [Tooltip("Longer steps take slightly longer.")]
    public bool scaleDurationByStepLength = true;

    [Range(0.25f, 2f)]
    public float stepLengthDurationInfluence = 0.35f;

    [Header("Step Arc")]
    public StepLiftAxisMode stepLiftAxisMode = StepLiftAxisMode.GroundNormalAtLanding;

    [Tooltip("Base lift as a fraction of pair reach.")]
    [Min(0f)]
    public float baseStepHeightReachRatio = 0.22f;

    [Tooltip("Extra lift at full momentum as a fraction of pair reach.")]
    [Min(0f)]
    public float momentumStepHeightReachRatio = 0.035f;

    [Tooltip("Extra lift from long steps.")]
    [Min(0f)]
    public float stepLengthHeightInfluence = 0.06f;

    [Tooltip("Extra arc height added when a step approaches max dynamic stride.")]
    [Min(0f)] public float speedStepHeightReachRatio = 0.07f;

    [Tooltip("Extra lift added directly from current movement speed, so fast catch-up/side steps clear the ground.")]
    [Min(0f)] public float movementSpeedStepLiftReachRatio = 0.080f;

    [Header("Step Placement")]
    [Tooltip("Uses each leg's static pole to choose its side lane, then steps mostly forward in that lane.")]
    public bool usePoleLaneForStepSide = true;

    [Tooltip("Clean placement mode: the real foot target is placed from each leg IK start, forward along that leg's rotated pole direction, then projected down to ground.")]
    public bool placeTargetsFromLegStarts = true;

    [Tooltip("Resting target distance in front of each leg start as a fraction of leg reach.")]
    [Min(0f)] public float restingTargetForwardReachRatio = 0.58f;

    [Tooltip("Uses start->staticPole as the leg's local forward. Falls back to the shared gait forward if the pole vector is unusable.")]
    public bool useStaticPoleAsLegForward = true;

    [Tooltip("Uses deterministic per-leg home positions instead of alternating/leading-leg heuristics.")]
    public bool useDeterministicHomeStepping = true;

    [Tooltip("Foot starts a step when it is farther than this fraction of reach from its own home point.")]
    [Min(0f)] public float homeStepTriggerReachRatio = 0.42f;

    [Tooltip("If a foot is this far from home, ignore cadence/min-step gates and recover immediately.")]
    [Min(0f)] public float forcedHomeStepTriggerReachRatio = 0.68f;

    [Tooltip("Moving feet only step when the planted foot trails this far behind the body along movement direction.")]
    [Range(0.1f, 1f)] public float movementConstraintStepReachRatio = 0.76f;

    [Tooltip("If the planted foot trails this far behind the body, step immediately regardless of cadence.")]
    [Range(0.1f, 1f)] public float forcedMovementConstraintStepReachRatio = 0.90f;

    [Tooltip("At high speed, start the behind-body swing a little earlier.")]
    [Range(0f, 0.4f)] public float speedConstraintTriggerTightening = 0.08f;

    [Tooltip("If both legs are blocked/stepping, retarget the worse active step instead of waiting for cadence.")]
    public bool retargetBlockedActiveStep = true;

    [Tooltip("While a foot is already stepping, keep its landing point updated toward the current predicted home so it does not land behind a moving body.")]
    public bool retargetActiveMovingSteps = false;

    [Tooltip("How quickly an active step endpoint follows the newest predicted home.")]
    [Min(0f)] public float activeStepRetargetSpeedMultiplier = 1.15f;

    [Tooltip("Do not retarget active steps during the final part of the arc, so landings stay stable.")]
    [Range(0f, 1f)] public float activeStepRetargetMaxT = 0.55f;

    [Tooltip("While moving, the home point is pushed forward by speed * lookAhead, clamped by this fraction of reach.")]
    [Min(0f)] public float maxHomeSpeedLeadReachRatio = 1.20f;

    [Tooltip("Hard upper bound for the total moving foot-home distance from the body/hip lane, so anticipation is large but still reachable.")]
    [Range(0.25f, 1f)] public float maxDesiredMovingHomeReachRatio = 0.96f;

    [Tooltip("Samples a small area around the forward step point and chooses the lowest grounded point.")]
    public bool chooseLowestGroundAroundStep = false;

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

    [Tooltip("Minimum world-space ray height used for foot grounding after the rig has been scaled down.")]
    [Min(0f)]
    public float minFootGroundRayHeight = 2f;

    [Tooltip("Minimum world-space ray distance used for foot grounding after the rig has been scaled down.")]
    [Min(0f)]
    public float minFootGroundRayDistance = 12f;

    [Tooltip("When the physics ray misses, sample Terrain.activeTerrain so feet still land on procedural terrain without colliders.")]
    public bool fallbackFeetToTerrainHeight = true;

    [Tooltip("Last resort for flat worlds without a terrain/collider: place missed foot targets on world Y=0.")]
    public bool fallbackFeetToWorldZeroPlane = true;

    [Tooltip("Keeps planted feet/tail targets on the ground projection instead of trusting authored target height.")]
    public bool forcePlantedFeetToGround = true;

    [Header("Airborne / Landing")]
    public bool useAirbornePose = true;
    [Min(0f)] public float airbornePoseLift = 0f;
    [Min(0f)] public float airbornePoseBlendSpeed = 12f;
    public bool earlyLandFeetOnTerrain = true;
    [Range(0f, 1f)] public float earlyLandingMinStepT = 0.35f;

    [Header("Reach Safety")]
    [Tooltip("Keeps foot target slightly inside the leg's true reach.")]
    [Min(0f)]
    public float legReachSafetyPadding = 0.03f;

    [Range(0.01f, 1f)]
    public float legReachMultiplier = 0.96f;

    [Header("Emergency Reach / Anti-Trail")]
    [Tooltip("If a planted foot target drifts close to the usable IK reach, start a catch-up step immediately instead of letting the body drag the leg behind it.")]
    public bool forceStepBeforeFootExceedsReach = true;

    [Tooltip("A planted target beyond this fraction of usable reach forces a step, ignoring normal cadence. Keep this below 1 so the solver never sees a fully stretched leg.")]
    [Range(0.45f, 1f)] public float emergencyStepStartReachRatio = 0.985f;

    [Tooltip("When not moving, emergency steps wait until this hard reach fraction so stopping mid-stride can hold a stretched pose.")]
    [Range(0.45f, 1f)] public float idleEmergencyStepStartReachRatio = 0.995f;

    [Tooltip("If the smoothed fake target trails this far from its intended target, force recovery even before the raw reach limit is hit.")]
    [Range(0.05f, 1f)] public float emergencyFakeTargetLagReachRatio = 0.38f;

    [Tooltip("Do not force reach-recovery steps from fake-target lag alone. The real/planted target is the deterministic source of truth; fake targets are allowed to smooth toward it.")]
    public bool ignoreFakeLagForEmergencyStep = true;

    [Tooltip("Last-resort clamp for the real/planted target if no emergency step can start this frame.")]
    [Range(0.45f, 1f)] public float emergencyRealTargetClampReachRatio = 0.97f;

    [Tooltip("Allows the emergency catch-up to start even while the other leg is finishing a normal step.")]
    public bool allowEmergencyStepWhileOtherLegSteps = true;

    [Tooltip("Additional lift for emergency catch-up steps as a fraction of pair reach.")]
    [Min(0f)] public float emergencyStepExtraLiftReachRatio = 0.025f;

    [Tooltip("Also treats distance from the body/core as a hard reach bound, so a planted real target can never trail farther than the leg's cumulative bone length from the player.")]
    public bool enforceBodyDistanceReachForFeet = false;

    [Tooltip("Tiny tolerance for hard reach checks before a forced step is triggered.")]
    [Min(0f)] public float hardReachTriggerTolerance = 0.005f;

    [Header("Startup Walk Rhythm")]
    [Tooltip("When movement starts from standing still, start the current leading leg's first step before allowing the body to accelerate.")]
    public bool enforceStartupLeadingLegStep = false;

    [Tooltip("Caps script-driven core movement so planted feet cannot be pulled outside hard leg reach in a single frame.")]
    public bool constrainCoreMovementByFootReach = false;

    [Tooltip("Safety margin kept between planted feet and hard leg reach when limiting core movement.")]
    [Min(0f)] public float coreFootReachSafetyMargin = 0.06f;

    [Header("Moving Step Placement")]
    [Tooltip("When moving, place each foot from the body center along the actual run-target/movement direction plus that leg's side lane. This restores side/back stepping instead of only stepping along the knee pole.")]
    public bool placeMovingStepsFromBodyCenter = true;

    [Tooltip("At speed, use movement direction for foot placement while the local/static pole only controls knee bend.")]
    public bool useMovementDirectionForMovingStepForward = true;

    [Tooltip("Extra forward anticipation, in seconds of current velocity, added to moving foot homes.")]
    [Min(0f)] public float movingStepAnticipationTime = 0.72f;

    [Header("Predictive Swing Landing")]
    [Tooltip("Place swing landings against the predicted body/hip position at landing time, not the current hip. This prevents current-frame reach clamping from erasing the forward step.")]
    public bool usePredictedLandingReachForStepEnds = true;

    [Tooltip("How much of the step duration is used when predicting where the body/hip will be when the foot lands.")]
    [Range(0f, 1.5f)] public float predictedLandingTimeScale = 0.92f;

    [Tooltip("Minimum forward landing distance from the predicted body core during locomotion.")]
    [Range(0.05f, 1f)] public float minimumPredictedLandingAheadReachRatio = 0.52f;

    [Tooltip("Do not clamp active swing fake targets backward against the current hip. Endpoints are made reachable for the predicted landing hip instead.")]
    public bool allowActiveSwingTargetPastCurrentReach = true;

    [Tooltip("Cap only the vertical lift when a swing arc would exceed current reach. This preserves forward travel instead of dragging the foot backward.")]
    public bool capSwingLiftBeforeReachClamp = true;

    [Tooltip("Small reach slack for active swing arcs so lifted feet can clear the ground without the solver pulling them backward.")]
    [Range(1f, 1.35f)] public float activeSwingReachSlackRatio = 1.10f;

    [Tooltip("Strict robot-like gait: the planted leading foot stays down until it falls behind the body, then the opposite foot takes one large anticipatory step.")]
    public bool strictAlternatingPlantedGait = true;

    [Header("Final Stable Gait Override")]
    [Tooltip("Use one deterministic gait rule for normal locomotion: feet remain planted, the rear foot steps once it passes behind the body, and the landing point is forced ahead of the body. This disables home/debt/emergency systems as normal step starters.")]
    public bool useSingleRuleAnticipatoryGait = true;

    [Tooltip("A planted foot may trail this far behind the body before it is asked to swing forward.")]
    [Range(0.02f, 0.8f)] public float stableBehindTriggerReachRatio = 0.20f;

    [Tooltip("A planted foot beyond this behind distance ignores cadence and must swing forward.")]
    [Range(0.05f, 1.2f)] public float stableForcedBehindReachRatio = 0.42f;

    [Tooltip("If neither foot is clearly ahead when movement starts, the next rhythmic foot is allowed to place an initial support step.")]
    [Range(0f, 0.8f)] public float stableStartupNoAheadSupportReachRatio = 0.12f;

    [Tooltip("Minimum landing distance in front of the body for normal movement, relative to usable leg reach.")]
    [Range(0.05f, 0.95f)] public float stableLandingAheadReachRatio = 0.62f;

    [Tooltip("Extra landing distance in front of the body at full speed.")]
    [Range(0f, 0.7f)] public float stableSpeedAddedAheadReachRatio = 0.28f;

    [Tooltip("Side lane width for left/right leg placement, relative to usable leg reach.")]
    [Range(0.05f, 0.7f)] public float stableSideLaneReachRatio = 0.26f;

    [Tooltip("Cadence at slow speed. This is a minimum interval between starts, not the foot swing duration.")]
    [Min(0.05f)] public float stableSlowStepCadence = 0.54f;

    [Tooltip("Cadence at high speed. Stride length grows before cadence gets faster.")]
    [Min(0.05f)] public float stableFastStepCadence = 0.38f;

    [Tooltip("Shortest visible swing duration for a planted foot step.")]
    [Min(0.05f)] public float stableMinSwingDuration = 0.24f;

    [Tooltip("Longest visible swing duration for a planted foot step.")]
    [Min(0.05f)] public float stableMaxSwingDuration = 0.42f;

    [Tooltip("Minimum planar distance a moving foot must travel, except when a hard reach/behind trigger fires.")]
    [Range(0f, 0.8f)] public float stableMinimumStepTravelReachRatio = 0.38f;

    [Tooltip("If true, the real target is written immediately to the ahead landing while the fake target travels there through the arc.")]
    public bool commitRealTargetAtStepStart = true;

    [Tooltip("Hard apply lower-body start/pole offsets from the gait yaw without relying only on RotatableNode serialized state. This is a fallback for prefab wiring that swallows dynamic offsets.")]
    public bool hardApplyLowerBodyYawOffsets = true;

    [Tooltip("How far the current leading foot may pass behind the body before the opposite foot starts a normal step.")]
    [Range(0.02f, 0.8f)] public float plantedLeadBehindStepReachRatio = 0.18f;

    [Tooltip("Hard behind limit that can ignore cadence. This is a safety valve, not the normal rhythm.")]
    [Range(0.05f, 1f)] public float forcedLeadBehindStepReachRatio = 0.34f;

    [Tooltip("Base landing distance in front of the body for a moving step.")]
    [Range(0.05f, 1f)] public float largeStepForwardReachRatio = 0.88f;

    [Tooltip("Additional forward landing distance at full speed.")]
    [Range(0f, 1f)] public float speedAddedStepForwardReachRatio = 0.18f;

    [Tooltip("Minimum side lane used when the rotated hip/pole lane is too close to center. This gives side/back steps an actual zig-zag stance.")]
    [Range(0f, 0.8f)] public float sideStepLaneReachRatio = 0.30f;

    [Tooltip("Ignore tiny ground-projection changes on planted feet. Prevents idle feet from bobbing because raycasts return minuscule differences.")]
    [Min(0f)] public float plantedGroundSnapTolerance = 0.035f;

    [Tooltip("When clamping a grounded foot into reach, preserve its ground height and clamp only its planar distance whenever possible.")]
    public bool preserveGroundHeightWhenClampingFootTargets = true;

    [Tooltip("Do not start a cosmetic step shorter than this planar fraction of reach.")]
    [Range(0f, 0.6f)] public float minimumStepDistanceBeforeStartReachRatio = 0.50f;

    [Header("IK Safety")]
    [Tooltip("Clamps every written foot target through the LimbSolver reach bounds before the chain sees it.")]
    public bool clampTargetsWithLimbSolver = true;

    [Tooltip("Applies OffsetPositioningNode target writes immediately, before the LimbSolver runs.")]
    public bool applyTargetOffsetImmediately = true;

    [Tooltip("Runs the LimbSolver right after this script moves a foot target, so middle nodes do not lag behind.")]
    public bool solveLegAfterTargetWrite = true;

    [Header("Leg Fake Target Lazy Follow")]
    [Tooltip("Real targets may snap to the newest step/home point, but the IK target the limb follows is smoothed through this filter.")]
    public bool lazyFollowLegFakeTargets = true;

    [Tooltip("When the fake IK target is this close to its desired point, stop the spring so planted feet do not jitter or keep lifting while idle.")]
    [Min(0f)] public float legFakeTargetSettleDistance = 0.045f;

    [Tooltip("Additional settle tolerance as a fraction of leg reach while idle. This keeps fake targets from bobbing in place when they already visually sit on the real target.")]
    [Min(0f)] public float stationaryFakeTargetSettleReachRatio = 0.018f;

    [Tooltip("If an idle fake target is within this fraction of reach from the real/planted target, leave it exactly where it is instead of pulling or snapping it.")]
    [Min(0f)] public float idleFakeTargetDeadZoneReachRatio = 0.055f;

    [Tooltip("Even with smoothing, a visible fake foot target cannot lag farther than this fraction of usable reach from the current desired step/plant point.")]
    [Min(0f)] public float maxFakeTargetLagReachRatio = 0.18f;

    [Tooltip("When fake lag exceeds its cap, close the excess at this many reaches per second instead of snapping.")]
    [Min(0f)] public float fakeTargetLagCatchupReachPerSecond = 10.0f;

    [Tooltip("Small characters have tiny reaches, so reach-per-second catchup can become too slow in world units. This adds current body speed as a catchup floor without snapping.")]
    [Min(0f)] public float fakeTargetLagCatchupCoreSpeedMultiplier = 1.35f;

    [Tooltip("When true, active swing endpoints are treated as committed positions and are not reclamped backward by the current moving hip; landing reach is validated before the step starts.")]
    public bool preserveCommittedSwingEndpointDuringStep = true;

    [Tooltip("During an actual step, write the generated arc directly. Disable to keep the visible IK handle smooth while the real target steps deterministically.")]
    public bool snapFakeTargetDuringActiveSteps = true;

    [Tooltip("Higher = the fake foot target catches the real/arc target faster.")]
    [Min(0.01f)] public float legFakeTargetFrequencyHz = 12.0f;

    [Tooltip("Extra fake-target frequency added at full run speed so fast movement does not drag the legs behind.")]
    [Min(0f)] public float legFakeTargetSpeedFrequencyBoostHz = 12.0f;

    [Tooltip("1 = no bounce. Above 1 is heavier, below 1 is springier.")]
    [Min(0f)] public float legFakeTargetDampingRatio = 0.9f;

    [Tooltip("Extra damping added at speed so the faster fake target still lands cleanly.")]
    [Min(0f)] public float legFakeTargetSpeedDampingBoost = 0.45f;

    [Tooltip("Active foot steps use this multiplier on fake-target frequency. Lower values make the visible step more fluid.")]
    [Min(0.01f)] public float activeStepFakeTargetFrequencyMultiplier = 1.20f;

    [Tooltip("Active foot steps use this multiplier on fake-target speed. Lower values make step travel less snappy.")]
    [Min(0.01f)] public float activeStepFakeTargetSpeedMultiplier = 1.65f;

    [Tooltip("Active foot steps use this multiplier on fake-target acceleration. Lower values soften sudden direction changes.")]
    [Min(0.01f)] public float activeStepFakeTargetAccelerationMultiplier = 1.45f;

    [Tooltip("0 means unlimited.")]
    [Min(0f)] public float maxLegFakeTargetAcceleration = 0f;

    [Tooltip("Speed-scaled acceleration fallback used when maxLegFakeTargetAcceleration is 0.")]
    [Min(0f)] public float dynamicLegFakeTargetAccelerationMultiplier = 18f;

    [Tooltip("0 means unlimited.")]
    [Min(0f)] public float maxLegFakeTargetSpeed = 0f;

    [Tooltip("Speed-scaled fake target max speed fallback used when maxLegFakeTargetSpeed is 0.")]
    [Min(0f)] public float dynamicLegFakeTargetSpeedMultiplier = 5.5f;

    [Tooltip("Substepping keeps fake target smoothing stable on uneven frames.")]
    [Min(0.001f)] public float maxLegFakeTargetSubstepTime = 1f / 90f;

    [Header("Runtime Debug")]
    [SerializeField] private int leadingLegIndex = 0;
    [SerializeField] private int steppingLegIndex = -1;
    [SerializeField] private int nextStepLegIndex = 0;
    [SerializeField] private float momentum = 0f;
    [SerializeField] private Vector3 currentCoreVelocity = Vector3.zero;
    [SerializeField] private Vector3 desiredCoreVelocity = Vector3.zero;
    [SerializeField] private Vector3 lastMoveDirection = Vector3.forward;
    [SerializeField] private float movementBlockDebt = 0f;
    [SerializeField] private float lastSampledMovementDistance = 0f;
    [SerializeField] private bool forceAirbornePose = false;
    [SerializeField] private float currentAirbornePoseLift = 0f;
    [SerializeField] private RuntimeDebugState debugState;

    public RuntimeDebugState DebugState => debugState;
    public float PairReach => GetPairReach();

    private Vector3 coreVelocityResponseVelocity = Vector3.zero;
    private Vector3 lastSampleCorePosition = Vector3.zero;
    private Vector3 lastTailSyncCorePosition = Vector3.zero;
    private bool hasTailSyncCorePosition = false;
    private float movementSampleTimer = 0f;
    private float timeSinceLastStep = 999f;
    private bool initialized = false;
    private float capturedGaitTargetRadius = 0f;
    private float capturedGaitTargetNormalOffset = 0f;
    private Vector3 capturedGaitReferenceForward = Vector3.forward;
    private bool hasCapturedGaitReferenceForward = false;
    private float cachedFrameDeltaTime = 0f;
    private Vector3 cachedFrameNormal = Vector3.up;
    private Vector3 cachedFrameForward = Vector3.forward;
    private Vector3 cachedFrameSide = Vector3.right;
    private bool movedCoreThisFrame = false;
    private bool wasMoveCommandActive = false;
    private bool startupLeadingStepPending = false;
    private bool wantsCoreMoveThisFrame = false;

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
        UpdateGaitForwardTarget(normal, movementForward);
        UpdateStaticPolePositions(normal, movementForward, movementSide);

        cachedFrameDeltaTime = dt;
        cachedFrameNormal = normal;
        cachedFrameForward = movementForward;
        cachedFrameSide = movementSide;
        movedCoreThisFrame = true;
    }

    private void LateUpdate()
    {
        if (!initialized)
        {
            Initialize();
        }

        float dt = movedCoreThisFrame
            ? cachedFrameDeltaTime
            : Time.deltaTime;

        if (dt <= Epsilon)
        {
            return;
        }

        Vector3 normal = movedCoreThisFrame
            ? cachedFrameNormal
            : GetMovementNormal();

        Vector3 movementForward = GetMovementForward(normal);
        if (movementForward.sqrMagnitude <= Epsilon)
        {
            movementForward = cachedFrameForward.sqrMagnitude > Epsilon
                ? cachedFrameForward.normalized
                : Vector3.forward;
        }

        Vector3 movementSide = Vector3.Cross(normal, movementForward).normalized;
        if (movementSide.sqrMagnitude <= Epsilon)
        {
            movementSide = cachedFrameSide.sqrMagnitude > Epsilon
                ? cachedFrameSide.normalized
                : Vector3.right;
        }

        RefreshPostCoreMoveIk();
        UpdateLegs(dt, normal, movementForward, movementSide);
        UpdateDebugState(normal, movementForward, movementSide);
        wasMoveCommandActive = wantsCoreMoveThisFrame;
        movedCoreThisFrame = false;
    }

    [ContextMenu("Initialize Leg Pair")]
    public void Initialize()
    {
        if (authoritativeOffsetNodesForRotatedLegAssembly)
        {
            syncLegStartNodesWithCoreDelta = false;
        }

        ResolveLegReferences(leftLeg);
        ResolveLegReferences(rightLeg);

        if (gaitRotationCore == null)
        {
            gaitRotationCore = coreNode;
        }

        if (gaitForwardTarget == null)
        {
            gaitForwardTarget = forwardReference;
        }

        if (gaitRotationAssigner == null && gaitRotationCore != null)
        {
            gaitRotationAssigner = gaitRotationCore.GetComponent<RotationAssigner>();
        }

        // Snap the lower-body assembly to the body core BEFORE initializing rotatable offsets.
        // Initializing first captures the authored prefab offset and makes the leg rotators
        // spin around the wrong centre for the rest of play mode.
        SnapGaitRotationCoreToBodyCore();
        ApplyOffsetPositionNow(gaitRotationAngleReference);
        ApplyOffsetPositionNow(forwardReference);
        ForceFrontLegPoles();
        ConfigureLowerBodyRotationNodes();
        ForceFrontLegPoles();

        AutoResolvePostCoreMoveIk();

        AssignStaticPole(leftLeg);
        AssignStaticPole(rightLeg);

        Vector3 normal = GetMovementNormal();
        Vector3 forward = GetMovementForward(normal);
        Vector3 side = Vector3.Cross(normal, forward).normalized;

        ApplyRuntimeLegDimensions(leftLeg);
        ApplyRuntimeLegDimensions(rightLeg);

        leftLeg.reach = CalculateLegReach(leftLeg);
        rightLeg.reach = CalculateLegReach(rightLeg);

        CaptureLegOffsets(leftLeg, forward, side);
        CaptureLegOffsets(rightLeg, forward, side);
        CaptureLegPoleOffsets(leftLeg, normal, forward, side);
        CaptureLegPoleOffsets(rightLeg, normal, forward, side);
        CaptureLegTargetOrbitOffsets(leftLeg, normal, forward, side);
        CaptureLegTargetOrbitOffsets(rightLeg, normal, forward, side);
        CaptureGaitForwardTarget(normal);

        UpdatePhysicalIkPolePosition(leftLeg, normal, forward);
        UpdatePhysicalIkPolePosition(rightLeg, normal, forward);
        AssignStaticPole(leftLeg);
        AssignStaticPole(rightLeg);

        if (autoSelectInitialLeadingLeg)
        {
            leadingLegIndex = leftLeg.capturedForwardOffset >= rightLeg.capturedForwardOffset ? 0 : 1;
        }
        else
        {
            leadingLegIndex = Mathf.Clamp(initialLeadingLegIndex, 0, 1);
        }

        nextStepLegIndex = leadingLegIndex;
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

        InitializeFootTargets(leftLeg, normal, forward);
        InitializeFootTargets(rightLeg, normal, forward);

        initialized = true;
    }

    private void UpdateCoreMovement(
        float dt,
        Vector3 normal,
        ref Vector3 movementForward,
        ref Vector3 movementSide
    )
    {
        wantsCoreMoveThisFrame = false;

        if (coreNode == null)
        {
            return;
        }

        SnapGaitRotationCoreToBodyCore();

        bool hasExternalGaitForward = TryGetExternalGaitForward(normal, out Vector3 gaitForward);
        if (hasExternalGaitForward && rotateGaitRotationCoreTowardForward)
        {
            RotateGaitRotationCoreTowardDirection(gaitForward, normal, dt);
        }

        if (hasExternalGaitForward)
        {
            DriveGaitRotationAssigner(gaitForward, normal);
            ApplyGaitRotationOffsetNodesNow();
        }

        if (coreMovementMode == CoreMovementMode.ReadExternalCoreOnly)
        {
            currentCoreVelocity = dt > Epsilon
                ? Vector3.ProjectOnPlane(coreNode.position - lastSampleCorePosition, normal) / dt
                : Vector3.zero;

            if (hasExternalGaitForward)
            {
                movementForward = gaitForward;
                movementSide = Vector3.Cross(normal, movementForward).normalized;
            }
            else if (currentCoreVelocity.sqrMagnitude > Epsilon)
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
        wantsCoreMoveThisFrame = wantsMove;

        bool anyLegStepping =
            (leftLeg != null && leftLeg.isStepping) ||
            (rightLeg != null && rightLeg.isStepping);

        if (enforceStartupLeadingLegStep &&
            wantsMove &&
            !wasMoveCommandActive &&
            !anyLegStepping)
        {
            leadingLegIndex = Mathf.Clamp(nextStepLegIndex, 0, 1);
            startupLeadingStepPending = true;
        }

        bool holdCoreForStartupLead =
            startupLeadingStepPending &&
            !IsLeadingLegCurrentlyStepping();

        bool canMoveCore = wantsMove && !holdCoreForStartupLead;

        float targetMomentum = canMoveCore ? 1f : 0f;
        float momentumRate = targetMomentum > momentum
            ? momentumBuildPerSecond
            : momentumDecayPerSecond;

        momentum = Mathf.MoveTowards(
            momentum,
            targetMomentum,
            momentumRate * dt
        );

        Vector3 moveDirection = lastMoveDirection;

        if (canMoveCore && distanceToTarget > Epsilon)
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

            if (hasExternalGaitForward)
            {
                movementForward = gaitForward;
            }
            else
            {
                movementForward = moveDirection;
            }

            movementSide = Vector3.Cross(normal, movementForward).normalized;
        }

        float slowFactor = canMoveCore
            ? Mathf.Clamp01((distanceToTarget - stopDistance) / Mathf.Max(slowDownRadius, Epsilon))
            : 0f;

        float desiredSpeed = Mathf.Lerp(walkSpeed, runSpeed, momentum) * slowFactor;

        desiredCoreVelocity = canMoveCore
            ? moveDirection * desiredSpeed
            : Vector3.zero;

        if (!canMoveCore && allowMomentumCarry && momentum > momentumCarryThreshold)
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

        currentCoreVelocity = ConstrainCoreVelocityByFootReach(currentCoreVelocity, dt);

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

        if (hasExternalGaitForward)
        {
            movementForward = gaitForward;
            movementSide = Vector3.Cross(normal, movementForward).normalized;
        }
        else if (currentCoreVelocity.sqrMagnitude > 0.0001f)
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

    private Vector3 ConstrainCoreVelocityByFootReach(Vector3 velocity, float dt)
    {
        if (!constrainCoreMovementByFootReach ||
            coreNode == null ||
            dt <= Epsilon ||
            velocity.sqrMagnitude <= Epsilon)
        {
            return velocity;
        }

        float maxScale = 1f;
        maxScale = Mathf.Min(maxScale, GetCoreVelocityScaleAllowedByLeg(leftLeg, velocity, dt));
        maxScale = Mathf.Min(maxScale, GetCoreVelocityScaleAllowedByLeg(rightLeg, velocity, dt));

        if (maxScale >= 1f)
        {
            return velocity;
        }

        return velocity * Mathf.Clamp01(maxScale);
    }

    private float GetCoreVelocityScaleAllowedByLeg(Leg leg, Vector3 velocity, float dt)
    {
        if (leg == null || leg.isStepping || coreNode == null)
        {
            return 1f;
        }

        Vector3 planted = leg.plantedWorldPosition;
        if (planted.sqrMagnitude <= Epsilon && leg.realTarget != null)
        {
            planted = leg.realTarget.position;
        }

        Transform start = GetLegStartTransform(leg);
        if (start == null)
        {
            return 1f;
        }

        float hardReach = Mathf.Max(0.001f, GetHardLegReach(leg) - coreFootReachSafetyMargin);
        Vector3 currentOffset = planted - start.position;
        Vector3 requestedDelta = velocity * dt;

        if ((currentOffset - requestedDelta).magnitude <= hardReach)
        {
            return 1f;
        }

        float low = 0f;
        float high = 1f;
        for (int i = 0; i < 8; i++)
        {
            float mid = (low + high) * 0.5f;
            if ((currentOffset - requestedDelta * mid).magnitude <= hardReach)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return low;
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

        if (!syncTailEndIkNodeWithCore || coreDelta.sqrMagnitude <= Epsilon)
        {
            return;
        }

        MoveNodeByCoreDelta(tailEndIkNode, coreDelta);

        if (syncLegStartNodesWithCoreDelta)
        {
            MoveNodeByCoreDelta(leftLeg != null ? leftLeg.startNode : null, coreDelta);
            MoveNodeByCoreDelta(rightLeg != null ? rightLeg.startNode : null, coreDelta);
        }

        if (ikNodesSyncedWithCoreDelta == null)
        {
            return;
        }

        for (int i = 0; i < ikNodesSyncedWithCoreDelta.Length; i++)
        {
            NodeState node = ikNodesSyncedWithCoreDelta[i];

            if (node == tailEndIkNode)
            {
                continue;
            }

            MoveNodeByCoreDelta(node, coreDelta);
        }
    }

    private void MoveNodeByCoreDelta(NodeState node, Vector3 coreDelta)
    {
        if (node == null || node.transform == null || node.transform == coreNode)
        {
            return;
        }

        if (coreNode != null && node.transform.IsChildOf(coreNode))
        {
            return;
        }

        // Offset/rotatable IK nodes are authored as offsets from the core. Moving them
        // manually here fights OffsetPositioningNode and causes the lower body to detach,
        // snap back to origin, or ignore rotation. Let the offset system place them.
        OffsetPositioningNode offsetNode = node.transform.GetComponent<OffsetPositioningNode>();
        if (authoritativeOffsetNodesForRotatedLegAssembly && offsetNode != null && offsetNode.parentNode != null)
        {
            return;
        }

        node.transform.position += coreDelta * tailEndCoreDeltaMultiplier;
    }

    private void RefreshPostCoreMoveIk()
    {
        if (spineTargetSettersToRefreshAfterCoreMove != null)
        {
            for (int i = 0; i < spineTargetSettersToRefreshAfterCoreMove.Length; i++)
            {
                SpineFakeTargetSetter setter = spineTargetSettersToRefreshAfterCoreMove[i];

                if (setter != null && setter.isActiveAndEnabled)
                {
                    setter.EvaluateAndApply();
                }
            }
        }

        if (solversToSolveAfterCoreMove == null)
        {
            return;
        }

        for (int i = 0; i < solversToSolveAfterCoreMove.Length; i++)
        {
            LimbSolver solver = solversToSolveAfterCoreMove[i];

            if (solver != null && solver.isActiveAndEnabled)
            {
                solver.Apply();
            }
        }
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
        UpdateAirbornePose(dt);
        UpdateRealTargetPreviews(normal, forward);

        bool anyStepActive = false;

        if (forceAirbornePose && useAirbornePose)
        {
            CancelActiveStep(leftLeg);
            CancelActiveStep(rightLeg);
            MaintainPlantedLeg(leftLeg, normal);
            MaintainPlantedLeg(rightLeg, normal);
            return;
        }

        UpdateSingleLegStep(leftLeg, dt, normal, ref anyStepActive);
        UpdateSingleLegStep(rightLeg, dt, normal, ref anyStepActive);

        MaintainPlantedLeg(leftLeg, normal);
        MaintainPlantedLeg(rightLeg, normal);

        if (useSingleRuleAnticipatoryGait)
        {
            UpdateSingleRuleAnticipatoryGait(normal, forward, anyStepActive);
            return;
        }

        if (TryStartStartupLeadingStep(normal, forward, ref anyStepActive))
        {
            return;
        }

        if (TryForceEmergencyReachStep(normal, forward, ref anyStepActive))
        {
            return;
        }

        if (!anyStepActive && ShouldRecenterIdleFootTargets())
        {
            UpdateIdleLegTargetOrbits(normal, forward, side);
        }

        if (oneStepAtATime && anyStepActive)
        {
            return;
        }

        if (useDeterministicHomeStepping)
        {
            UpdateDeterministicHomeSteps(normal, forward);
            return;
        }

        if (timeSinceLastStep < minStepInterval)
        {
            return;
        }

        float pairReach = GetPairReach();
        float blockTriggerDistance =
            pairReach * movementDebtStepTriggerReachRatio;

        float leftForward = GetFootForwardRelativeToLegStart(leftLeg, normal, forward);
        float rightForward = GetFootForwardRelativeToLegStart(rightLeg, normal, forward);

        float behindTrigger =
            leadingBehindDistance +
            pairReach * leadingBehindReachRatio;

        bool movingEnough =
            currentCoreVelocity.magnitude > 0.05f ||
            momentum > momentumCarryThreshold ||
            movementBlockDebt > blockTriggerDistance;

        int stepLegIndex = leftForward <= rightForward ? 0 : 1;
        Leg legToStep = GetLeg(stepLegIndex);
        float mostBehindForward = Mathf.Min(leftForward, rightForward);

        bool leadingFellBehind =
            mostBehindForward < -behindTrigger;

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

    private void UpdateSingleRuleAnticipatoryGait(Vector3 normal, Vector3 fallbackForward, bool anyStepActive)
    {
        // Final locomotion path: a single, deterministic alternating gait.
        // No home correction, no movement-debt micro-steps, no active retargeting. A foot stays
        // planted; when the current lead/support relationship says the next foot must move, that
        // foot receives one committed landing in front of the body and the fake target travels
        // through a visible arc to that point.
        bool movingEnough = IsMovingEnoughForGait();
        if (!movingEnough)
        {
            movementBlockDebt = 0f;
            return;
        }

        if (oneStepAtATime && anyStepActive)
        {
            return;
        }

        float pairReach = GetPairReach();
        float speed01 = GetSpeed01();
        Vector3 movementDirection = GetMovementStepDirection(normal, fallbackForward);
        if (movementDirection.sqrMagnitude <= Epsilon)
        {
            movementDirection = Vector3.ProjectOnPlane(lastMoveDirection, normal);
        }
        if (movementDirection.sqrMagnitude <= Epsilon)
        {
            movementDirection = Vector3.ProjectOnPlane(fallbackForward, normal);
        }
        if (movementDirection.sqrMagnitude <= Epsilon)
        {
            return;
        }
        movementDirection.Normalize();

        float leftForward = GetPlantedFootForwardRelativeToCore(leftLeg, normal, movementDirection);
        float rightForward = GetPlantedFootForwardRelativeToCore(rightLeg, normal, movementDirection);
        Leg leadingLeg = GetLeg(Mathf.Clamp(leadingLegIndex, 0, 1));
        float leadingForward = GetPlantedFootForwardRelativeToCore(leadingLeg, normal, movementDirection);

        // The old working player used a leading-foot trigger: the foot currently considered the
        // front/support foot must pass behind the body before the other foot is allowed to swing.
        // This prevents every-frame correction steps and preserves a clean alternating rhythm.
        float behindTrigger = pairReach * Mathf.Lerp(
            stableBehindTriggerReachRatio,
            stableBehindTriggerReachRatio * 0.65f,
            speed01);
        float forcedBehindTrigger = pairReach * Mathf.Max(stableForcedBehindReachRatio, stableBehindTriggerReachRatio + 0.02f);
        float aheadSupport = pairReach * stableStartupNoAheadSupportReachRatio;
        float cadence = Mathf.Lerp(stableSlowStepCadence, stableFastStepCadence, speed01);
        cadence = Mathf.Max(minStepInterval, cadence);
        bool cadenceReady = timeSinceLastStep >= cadence;

        bool leadingFellBehind = leadingForward < -behindTrigger;
        bool leadingForcedBehind = leadingForward < -forcedBehindTrigger;
        bool noAheadSupport = leftForward < aheadSupport && rightForward < aheadSupport;
        bool leftHardOverReach = IsLegHardOverReach(leftLeg);
        bool rightHardOverReach = IsLegHardOverReach(rightLeg);
        bool hardOverReach = leftHardOverReach || rightHardOverReach;

        int stepLegIndex = -1;
        bool forced = false;

        if (hardOverReach)
        {
            // True reach failure is the only non-rhythmic start. Move the worst trailing foot.
            forced = true;
            if (leftHardOverReach && rightHardOverReach)
            {
                stepLegIndex = leftForward <= rightForward ? 0 : 1;
            }
            else
            {
                stepLegIndex = leftHardOverReach ? 0 : 1;
            }
        }
        else if ((leadingFellBehind && cadenceReady) || leadingForcedBehind)
        {
            // Restore the old alternating relation: when the current lead foot has fallen behind,
            // the opposite foot takes the next large step in front and becomes the new lead when
            // the step completes.
            stepLegIndex = 1 - Mathf.Clamp(leadingLegIndex, 0, 1);
            forced = leadingForcedBehind;
        }
        else if (cadenceReady && noAheadSupport && movementBlockDebt > pairReach * Mathf.Max(0.16f, movementDebtStepTriggerReachRatio))
        {
            // Startup only: if the body began moving with both feet under/behind it, put the next
            // alternating foot ahead. This is intentionally cadence-gated so it cannot become a
            // train of tiny corrective steps.
            stepLegIndex = Mathf.Clamp(nextStepLegIndex, 0, 1);
        }

        if (stepLegIndex < 0)
        {
            return;
        }

        Leg leg = GetLeg(stepLegIndex);
        if (leg == null || leg.isStepping)
        {
            return;
        }

        Vector3 landing = CalculateGuaranteedAheadStepDestination(
            leg,
            normal,
            movementDirection,
            speed01,
            out Vector3 landingNormal);

        float landingAhead = GetPointForwardRelativeToCore(landing, normal, movementDirection);
        float minimumAhead = GetUsableLegReach(leg) * Mathf.Max(0.18f, stableLandingAheadReachRatio * 0.55f);
        if (landingAhead < minimumAhead && !forced)
        {
            return;
        }

        Vector3 from = GetPlantedOrCurrentFootPosition(leg, landing);
        float stepLength = Vector3.ProjectOnPlane(landing - from, normal).magnitude;
        float minTravel = GetUsableLegReach(leg) * stableMinimumStepTravelReachRatio;
        if (!forced && stepLength < minTravel)
        {
            return;
        }

        StartLegStep(
            leg,
            stepLegIndex,
            landing,
            landingNormal,
            Mathf.Max(stepLength, minTravel),
            pairReach);

        // Do not mark this leg as leading until the swing completes. CompleteLegStep already does
        // that. Keeping the old lead during the swing avoids immediate re-trigger oscillation.
        nextStepLegIndex = stepLegIndex == 0 ? 1 : 0;
        movementBlockDebt = 0f;
        debugState.requestedStepLength = stepLength;
        debugState.selectedStepLength = Mathf.Max(stepLength, minTravel);
    }

    private bool IsLegHardOverReach(Leg leg)
    {
        if (leg == null)
        {
            return false;
        }

        Transform start = GetLegStartTransform(leg);
        if (start == null)
        {
            return false;
        }

        Vector3 planted = GetPlantedOrCurrentFootPosition(leg, start.position);
        float hardLimit = Mathf.Max(0.001f, GetUsableLegReach(leg) * 0.96f);
        return Vector3.Distance(planted, start.position) > hardLimit;
    }

    private Vector3 CalculateGuaranteedAheadStepDestination(
        Leg leg,
        Vector3 normal,
        Vector3 movementForward,
        float speed01,
        out Vector3 landingNormal)
    {
        landingNormal = normal;

        Vector3 safeNormal = normal.sqrMagnitude > Epsilon ? normal.normalized : Vector3.up;
        Vector3 forward = Vector3.ProjectOnPlane(movementForward, safeNormal);
        if (forward.sqrMagnitude <= Epsilon)
        {
            forward = Vector3.ProjectOnPlane(lastMoveDirection, safeNormal);
        }
        if (forward.sqrMagnitude <= Epsilon)
        {
            forward = Vector3.forward;
        }
        forward.Normalize();

        Vector3 side = Vector3.Cross(safeNormal, forward);
        if (side.sqrMagnitude <= Epsilon)
        {
            side = Vector3.right;
        }
        side.Normalize();

        Vector3 corePosition = coreNode != null ? coreNode.position : transform.position;
        Transform start = GetLegStartTransform(leg);
        Vector3 startPosition = start != null ? start.position : corePosition;
        float usableReach = Mathf.Max(0.001f, GetUsableLegReach(leg));

        float sideOffset = GetMovingStepSideLaneOffset(leg, safeNormal, side);
        float minSide = usableReach * stableSideLaneReachRatio;
        if (Mathf.Abs(sideOffset) < minSide)
        {
            float sign = Mathf.Abs(sideOffset) > Epsilon
                ? Mathf.Sign(sideOffset)
                : leg == leftLeg ? -1f : 1f;
            sideOffset = sign * minSide;
        }

        float forwardSpeed = Mathf.Max(0f, Vector3.Dot(currentCoreVelocity, forward));
        float requestedAhead = usableReach * (stableLandingAheadReachRatio + stableSpeedAddedAheadReachRatio * speed01);
        requestedAhead += forwardSpeed * Mathf.Max(0f, movingStepAnticipationTime) * 0.50f;
        requestedAhead = Mathf.Clamp(requestedAhead, usableReach * 0.42f, usableReach * 0.96f);

        Vector3 desired = corePosition + side * sideOffset + forward * requestedAhead;
        Vector3 grounded = raycastFeetToGround
            ? FindGroundedStepPoint(leg, desired, safeNormal, forward, side, out landingNormal)
            : desired;

        return ClampAheadLandingToCurrentReach(
            leg,
            grounded,
            startPosition,
            corePosition,
            safeNormal,
            forward,
            side,
            sideOffset,
            requestedAhead,
            usableReach,
            out landingNormal);
    }

    private Vector3 ClampAheadLandingToCurrentReach(
        Leg leg,
        Vector3 groundedDesired,
        Vector3 startPosition,
        Vector3 corePosition,
        Vector3 normal,
        Vector3 forward,
        Vector3 side,
        float requestedSideOffset,
        float requestedAhead,
        float usableReach,
        out Vector3 landingNormal)
    {
        landingNormal = normal;

        Vector3 safeNormal = normal.sqrMagnitude > Epsilon ? normal.normalized : Vector3.up;
        Vector3 safeForward = Vector3.ProjectOnPlane(forward, safeNormal);
        if (safeForward.sqrMagnitude <= Epsilon)
        {
            safeForward = Vector3.forward;
        }
        safeForward.Normalize();

        Vector3 safeSide = Vector3.ProjectOnPlane(side, safeNormal);
        if (safeSide.sqrMagnitude <= Epsilon)
        {
            safeSide = Vector3.Cross(safeNormal, safeForward);
        }
        safeSide.Normalize();

        float targetHeightFromCore = Vector3.Dot(groundedDesired - corePosition, safeNormal);
        Vector3 startPlanarFromCore = Vector3.ProjectOnPlane(startPosition - corePosition, safeNormal);
        float startSide = Vector3.Dot(startPlanarFromCore, safeSide);
        float startForward = Vector3.Dot(startPlanarFromCore, safeForward);
        float verticalFromStart = Vector3.Dot((corePosition + safeNormal * targetHeightFromCore) - startPosition, safeNormal);
        float safeReach = Mathf.Max(0.001f, usableReach * 0.985f);
        float planarBudgetSquared = safeReach * safeReach - verticalFromStart * verticalFromStart;
        float planarBudget = planarBudgetSquared > 0.0001f ? Mathf.Sqrt(planarBudgetSquared) : safeReach * 0.12f;

        float sideFromStart = requestedSideOffset - startSide;
        float maxSide = planarBudget * 0.58f;
        if (Mathf.Abs(sideFromStart) > maxSide && maxSide > Epsilon)
        {
            sideFromStart = Mathf.Sign(sideFromStart) * maxSide;
        }

        float remainingForwardSquared = planarBudget * planarBudget - sideFromStart * sideFromStart;
        float maxForwardFromStart = remainingForwardSquared > 0.0001f ? Mathf.Sqrt(remainingForwardSquared) : 0f;
        float desiredForwardFromStart = requestedAhead - startForward;
        float finalForwardFromStart = Mathf.Clamp(desiredForwardFromStart, 0f, maxForwardFromStart);
        float finalAheadFromCore = startForward + finalForwardFromStart;

        float minimumAhead = Mathf.Min(requestedAhead, Mathf.Max(0.02f, usableReach * 0.28f));
        if (finalAheadFromCore < minimumAhead && maxForwardFromStart > Epsilon)
        {
            // Sacrifice side lane first. A narrow stance is acceptable; a step behind the body is not.
            sideFromStart *= 0.25f;
            remainingForwardSquared = planarBudget * planarBudget - sideFromStart * sideFromStart;
            maxForwardFromStart = remainingForwardSquared > 0.0001f ? Mathf.Sqrt(remainingForwardSquared) : 0f;
            finalForwardFromStart = Mathf.Clamp(desiredForwardFromStart, 0f, maxForwardFromStart);
            finalAheadFromCore = startForward + finalForwardFromStart;
        }

        Vector3 candidate = startPosition + safeSide * sideFromStart + safeForward * finalForwardFromStart;
        candidate += safeNormal * verticalFromStart;

        if (raycastFeetToGround)
        {
            candidate = FindGroundedStepPoint(leg, candidate, safeNormal, safeForward, safeSide, out landingNormal);
            candidate = ProjectFootAwarePointAroundOriginIntoRadius(candidate, startPosition, safeReach, leg, safeNormal);
        }
        else
        {
            candidate = ProjectFootAwarePointAroundOriginIntoRadius(candidate, startPosition, safeReach, leg, safeNormal);
        }

        return candidate;
    }

    private bool TryStartStartupLeadingStep(Vector3 normal, Vector3 fallbackForward, ref bool anyStepActive)
    {
        if (!startupLeadingStepPending)
        {
            return false;
        }

        if (!wantsCoreMoveThisFrame)
        {
            startupLeadingStepPending = false;
            return false;
        }

        Leg leg = GetLeg(leadingLegIndex);
        if (leg == null)
        {
            startupLeadingStepPending = false;
            return false;
        }

        if (leg.isStepping)
        {
            anyStepActive = true;
            startupLeadingStepPending = false;
            return false;
        }

        if (anyStepActive && oneStepAtATime)
        {
            return false;
        }

        Vector3 destination = IsMovingEnoughForGait()
            ? CalculateLargeGaitStepDestination(leg, normal, GetMovementStepDirection(normal, fallbackForward), GetPairReach(), out _)
            : CalculateLegHomePosition(leg, normal, fallbackForward, true);
        destination = ProjectLegTargetIntoReach(leg, destination);
        if (raycastFeetToGround)
        {
            destination = ProjectFootTargetToGround(destination, normal);
            destination = ProjectLegTargetIntoReach(leg, destination);
        }

        Vector3 current = GetCurrentLegIkTargetPosition(leg, destination);
        float pairReach = GetPairReach();
        float length = Vector3.ProjectOnPlane(destination - current, normal).magnitude;

        StartLegStep(
            leg,
            leadingLegIndex,
            destination,
            normal,
            Mathf.Max(length, pairReach * minStepReachRatio),
            pairReach
        );

        startupLeadingStepPending = false;
        anyStepActive = true;
        return true;
    }

    private bool IsLeadingLegCurrentlyStepping()
    {
        Leg leadingLeg = GetLeg(leadingLegIndex);
        return leadingLeg != null && leadingLeg.isStepping;
    }

    private bool IsMovingEnoughForGait()
    {
        return currentCoreVelocity.magnitude > 0.08f ||
               momentum > momentumCarryThreshold ||
               movementBlockDebt > GetPairReach() * movementDebtStepTriggerReachRatio;
    }

    private float GetSpeed01()
    {
        float referenceSpeed = Mathf.Max(0.001f, runSpeed);
        return Mathf.Clamp01(currentCoreVelocity.magnitude / referenceSpeed);
    }

    private float GetCurrentStepCadenceInterval(bool movingEnough)
    {
        if (!movingEnough)
        {
            return Mathf.Max(minStepInterval, idleCorrectionStepInterval);
        }

        float speed01 = GetSpeed01();
        float cadence = Mathf.Lerp(slowStepDuration, fastStepDuration, speed01);
        return Mathf.Max(minStepInterval, cadence);
    }

    private bool ShouldRecenterIdleFootTargets()
    {
        // Idle feet stay planted. Walking steps are constraint-driven, not home-correction driven.
        return false;
    }

    private void UpdateDeterministicHomeSteps(Vector3 normal, Vector3 fallbackForward)
    {
        bool movingEnough = IsMovingEnoughForGait();
        if (!movingEnough)
        {
            // Idle feet are planted feet. Do not recenter, preview-slide, or cosmetic-step.
            movementBlockDebt = 0f;
            return;
        }

        if (!strictAlternatingPlantedGait)
        {
            UpdateDistanceBasedHomeSteps(normal, fallbackForward);
            return;
        }

        if (oneStepAtATime && ((leftLeg != null && leftLeg.isStepping) || (rightLeg != null && rightLeg.isStepping)))
        {
            return;
        }

        float pairReach = GetPairReach();
        float speed01 = GetSpeed01();
        float cadenceInterval = GetCurrentStepCadenceInterval(true);
        Vector3 movementDirection = GetMovementStepDirection(normal, fallbackForward);

        float leftForward = GetPlantedFootForwardRelativeToCore(leftLeg, normal, movementDirection);
        float rightForward = GetPlantedFootForwardRelativeToCore(rightLeg, normal, movementDirection);

        // The foot that is actually behind the body swings forward. This is the core gait rule:
        // plant -> body passes over -> foot becomes rear foot -> same foot takes a large step ahead.
        int stepLegIndex = leftForward <= rightForward ? 0 : 1;
        Leg legToStep = GetLeg(stepLegIndex);
        float rearForward = Mathf.Min(leftForward, rightForward);

        if (legToStep == null || legToStep.isStepping)
        {
            return;
        }

        float behindTrigger = pairReach * Mathf.Lerp(
            plantedLeadBehindStepReachRatio,
            plantedLeadBehindStepReachRatio * 0.78f,
            speed01);
        float forcedBehindTrigger = pairReach * Mathf.Max(forcedLeadBehindStepReachRatio, plantedLeadBehindStepReachRatio);
        bool rearFootIsBehindBody = rearForward < -behindTrigger;
        bool forcedRearFootIsBehindBody = rearForward < -forcedBehindTrigger;

        // Movement debt is only a startup valve. It must not create a train of little correction
        // steps while both feet are still inside a valid planted stance.
        bool startupStrideReady = movementBlockDebt > pairReach * Mathf.Max(0.75f, movementDebtStepTriggerReachRatio * 4.0f) &&
                                  timeSinceLastStep >= cadenceInterval;
        bool cadenceReady = timeSinceLastStep >= cadenceInterval;

        if (!rearFootIsBehindBody && !forcedRearFootIsBehindBody && !startupStrideReady)
        {
            return;
        }

        if (!forcedRearFootIsBehindBody && !startupStrideReady && !cadenceReady)
        {
            return;
        }

        Vector3 destination = CalculateLargeGaitStepDestination(
            legToStep,
            normal,
            movementDirection,
            pairReach,
            out Vector3 landingNormal);

        float destinationAhead = GetPointForwardRelativeToCore(destination, normal, movementDirection);
        float minimumAhead = Mathf.Max(0.05f, GetUsableLegReach(legToStep) * 0.34f);
        if (destinationAhead < minimumAhead)
        {
            // If reach clamping or ground projection ate the forward component, do not start a
            // useless step. A useless front step is the visible "mouse-step" failure mode.
            return;
        }

        Vector3 from = GetPlantedOrCurrentFootPosition(legToStep, destination);
        float selectedStepLength = Vector3.ProjectOnPlane(destination - from, normal).magnitude;
        float minimumStepLength = Mathf.Max(pairReach * minimumStepDistanceBeforeStartReachRatio, GetUsableLegReach(legToStep) * 0.36f);

        if (!forcedRearFootIsBehindBody && !startupStrideReady && selectedStepLength < minimumStepLength)
        {
            return;
        }

        StartLegStep(
            legToStep,
            stepLegIndex,
            destination,
            landingNormal,
            Mathf.Max(selectedStepLength, minimumStepLength),
            pairReach);

        leadingLegIndex = stepLegIndex;
        nextStepLegIndex = stepLegIndex == 0 ? 1 : 0;
        movementBlockDebt = Mathf.Max(0f, movementBlockDebt - Mathf.Max(selectedStepLength, pairReach * 0.55f));
        debugState.requestedStepLength = selectedStepLength;
        debugState.selectedStepLength = selectedStepLength;
    }

    private void UpdateDistanceBasedHomeSteps(Vector3 normal, Vector3 fallbackForward)
    {
        bool movingEnough = IsMovingEnoughForGait();
        if (!movingEnough)
        {
            return;
        }

        float pairReach = GetPairReach();
        float speed01 = GetSpeed01();
        float cadenceInterval = GetCurrentStepCadenceInterval(true);

        Vector3 leftHome = CalculateLegHomePosition(leftLeg, normal, fallbackForward, movingEnough);
        Vector3 rightHome = CalculateLegHomePosition(rightLeg, normal, fallbackForward, movingEnough);

        float leftHomeDistance = GetPlanarFootHomeDistance(leftLeg, leftHome, normal);
        float rightHomeDistance = GetPlanarFootHomeDistance(rightLeg, rightHome, normal);
        float leftConstraintDistance = GetMovementConstraintDistance(leftLeg, normal, fallbackForward);
        float rightConstraintDistance = GetMovementConstraintDistance(rightLeg, normal, fallbackForward);

        float triggerRatio = Mathf.Max(
            0f,
            movementConstraintStepReachRatio - speedConstraintTriggerTightening * speed01
        );
        float triggerDistance = pairReach * triggerRatio;
        float forcedTriggerDistance = pairReach * Mathf.Max(triggerRatio, forcedMovementConstraintStepReachRatio);
        bool forcedRecovery = leftConstraintDistance > forcedTriggerDistance ||
                              rightConstraintDistance > forcedTriggerDistance;

        if (!forcedRecovery && timeSinceLastStep < cadenceInterval)
        {
            return;
        }

        if (leftConstraintDistance <= triggerDistance && rightConstraintDistance <= triggerDistance)
        {
            return;
        }

        int stepLegIndex = ChooseRhythmicStepLeg(leftConstraintDistance, rightConstraintDistance, triggerDistance);
        Leg legToStep = GetLeg(stepLegIndex);
        float selectedDistance = stepLegIndex == 0 ? leftConstraintDistance : rightConstraintDistance;

        if (legToStep == null)
        {
            return;
        }

        if (legToStep.isStepping)
        {
            return;
        }

        float minVisibleDistance = pairReach * minimumVisibleStepReachRatio;
        if (!forcedRecovery && selectedDistance < minVisibleDistance)
        {
            return;
        }

        Vector3 destination = stepLegIndex == 0 ? leftHome : rightHome;
        float selectedStepLength = stepLegIndex == 0 ? leftHomeDistance : rightHomeDistance;

        if (!forcedRecovery && selectedStepLength < minVisibleDistance)
        {
            return;
        }

        StartLegStep(
            legToStep,
            stepLegIndex,
            destination,
            normal,
            selectedStepLength,
            pairReach
        );

        movementBlockDebt = Mathf.Max(0f, movementBlockDebt - selectedStepLength);
        debugState.requestedStepLength = selectedStepLength;
        debugState.selectedStepLength = selectedStepLength;
    }

    private Vector3 CalculateLargeGaitStepDestination(
        Leg leg,
        Vector3 normal,
        Vector3 movementForward,
        float pairReach,
        out Vector3 landingNormal)
    {
        return CalculateGuaranteedAheadStepDestination(
            leg,
            normal,
            movementForward,
            GetSpeed01(),
            out landingNormal);
    }


    private Vector3 ClampGaitStepAheadOfBody(
        Leg leg,
        Vector3 groundedDesired,
        Vector3 normal,
        Vector3 forward,
        Vector3 side,
        float requestedSideOffset,
        float requestedForwardOffset,
        out Vector3 landingNormal)
    {
        landingNormal = normal;

        Transform start = GetLegStartTransform(leg);
        Vector3 corePosition = coreNode != null ? coreNode.position : transform.position;
        if (start == null)
        {
            return ProjectLegTargetIntoReach(leg, groundedDesired);
        }

        Vector3 safeNormal = normal.sqrMagnitude > Epsilon ? normal.normalized : Vector3.up;
        Vector3 safeForward = Vector3.ProjectOnPlane(forward, safeNormal);
        if (safeForward.sqrMagnitude <= Epsilon)
        {
            safeForward = Vector3.forward;
        }
        safeForward.Normalize();

        Vector3 safeSide = Vector3.ProjectOnPlane(side, safeNormal);
        if (safeSide.sqrMagnitude <= Epsilon)
        {
            safeSide = Vector3.Cross(safeNormal, safeForward);
        }
        safeSide.Normalize();

        float usableReach = Mathf.Max(0.001f, GetUsableLegReach(leg));
        float safeReach = Mathf.Max(0.001f, usableReach * 0.995f);
        float groundHeightFromCore = Vector3.Dot(groundedDesired - corePosition, safeNormal);
        float verticalFromStart = Vector3.Dot((corePosition + safeNormal * groundHeightFromCore) - start.position, safeNormal);
        float planarBudgetSquared = safeReach * safeReach - verticalFromStart * verticalFromStart;
        float planarBudget = planarBudgetSquared > 0.0001f ? Mathf.Sqrt(planarBudgetSquared) : safeReach * 0.18f;

        Vector3 startFromCorePlanar = Vector3.ProjectOnPlane(start.position - corePosition, safeNormal);
        float startSide = Vector3.Dot(startFromCorePlanar, safeSide);
        float startForward = Vector3.Dot(startFromCorePlanar, safeForward);

        float targetSide = requestedSideOffset;
        float sideFromStart = targetSide - startSide;
        float maxSideFromStart = planarBudget * 0.78f;
        if (Mathf.Abs(sideFromStart) > maxSideFromStart && maxSideFromStart > Epsilon)
        {
            sideFromStart = Mathf.Sign(sideFromStart) * maxSideFromStart;
            targetSide = startSide + sideFromStart;
        }

        float remainingForwardSquared = planarBudget * planarBudget - sideFromStart * sideFromStart;
        float maxForwardFromStart = remainingForwardSquared > 0.0001f ? Mathf.Sqrt(remainingForwardSquared) : 0f;
        float maxForwardFromCore = startForward + maxForwardFromStart;
        float minimumAhead = Mathf.Min(usableReach * 0.38f, Mathf.Max(0f, maxForwardFromCore));
        float targetForward = Mathf.Clamp(requestedForwardOffset, minimumAhead, Mathf.Max(minimumAhead, maxForwardFromCore));

        Vector3 candidate = corePosition +
                            safeSide * targetSide +
                            safeForward * targetForward +
                            safeNormal * groundHeightFromCore;

        Vector3 grounded = raycastFeetToGround
            ? FindGroundedStepPoint(leg, candidate, safeNormal, safeForward, safeSide, out landingNormal)
            : candidate;

        Vector3 projected = ProjectLegTargetIntoReach(leg, grounded, 1f);

        // Do one final forward-preservation pass if the generic reach clamp rotated the target
        // sideways/backward. Reducing side width is preferable to landing behind the body.
        float ahead = GetPointForwardRelativeToCore(projected, safeNormal, safeForward);
        if (ahead < minimumAhead && maxForwardFromCore > minimumAhead + Epsilon)
        {
            targetSide = Mathf.Lerp(targetSide, startSide, 0.55f);
            sideFromStart = targetSide - startSide;
            remainingForwardSquared = planarBudget * planarBudget - sideFromStart * sideFromStart;
            maxForwardFromStart = remainingForwardSquared > 0.0001f ? Mathf.Sqrt(remainingForwardSquared) : 0f;
            maxForwardFromCore = startForward + maxForwardFromStart;
            targetForward = Mathf.Clamp(requestedForwardOffset, minimumAhead, Mathf.Max(minimumAhead, maxForwardFromCore));
            candidate = corePosition + safeSide * targetSide + safeForward * targetForward + safeNormal * groundHeightFromCore;
            grounded = raycastFeetToGround
                ? FindGroundedStepPoint(leg, candidate, safeNormal, safeForward, safeSide, out landingNormal)
                : candidate;
            projected = ProjectLegTargetIntoReach(leg, grounded, 1f);
        }

        return projected;
    }

    private float GetPointForwardRelativeToCore(Vector3 point, Vector3 normal, Vector3 forward)
    {
        if (coreNode == null || forward.sqrMagnitude <= Epsilon)
        {
            return 0f;
        }

        Vector3 planar = Vector3.ProjectOnPlane(point - coreNode.position, normal);
        return Vector3.Dot(planar, forward.normalized);
    }

    private float GetPlantedFootForwardRelativeToCore(Leg leg, Vector3 normal, Vector3 forward)
    {
        if (leg == null || coreNode == null || forward.sqrMagnitude <= Epsilon)
        {
            return 0f;
        }

        Vector3 foot = GetPlantedOrCurrentFootPosition(leg, coreNode.position);
        Vector3 fromCore = Vector3.ProjectOnPlane(foot - coreNode.position, normal);
        return Vector3.Dot(fromCore, forward.normalized);
    }

    private Vector3 GetPlantedOrCurrentFootPosition(Leg leg, Vector3 fallback)
    {
        if (leg == null)
        {
            return fallback;
        }

        if (leg.plantedWorldPosition.sqrMagnitude > Epsilon)
        {
            return leg.plantedWorldPosition;
        }

        if (leg.realTarget != null)
        {
            return leg.realTarget.position;
        }

        if (leg.fakeTarget != null)
        {
            return leg.fakeTarget.position;
        }

        if (leg.tailNode != null)
        {
            return leg.tailNode.transform.position;
        }

        return fallback;
    }

    private int ChooseRhythmicStepLeg(float leftDistance, float rightDistance, float triggerDistance)
    {
        int preferred = Mathf.Clamp(nextStepLegIndex, 0, 1);
        float preferredDistance = preferred == 0 ? leftDistance : rightDistance;
        float otherDistance = preferred == 0 ? rightDistance : leftDistance;

        if (preferredDistance > triggerDistance)
        {
            return preferred;
        }

        if (otherDistance > triggerDistance)
        {
            return preferred == 0 ? 1 : 0;
        }

        return leftDistance >= rightDistance ? 0 : 1;
    }

    private float GetMovementConstraintDistance(Leg leg, Vector3 normal, Vector3 fallbackForward)
    {
        if (leg == null)
        {
            return 0f;
        }

        Vector3 foot = leg.plantedWorldPosition;
        if (foot.sqrMagnitude <= Epsilon && leg.realTarget != null)
        {
            foot = leg.realTarget.position;
        }

        Vector3 movementDirection = GetMovementStepDirection(normal, fallbackForward);
        if (coreNode == null || movementDirection.sqrMagnitude <= Epsilon)
        {
            return 0f;
        }

        Vector3 fromBodyToFoot = Vector3.ProjectOnPlane(foot - coreNode.position, normal);
        return Mathf.Max(0f, -Vector3.Dot(fromBodyToFoot, movementDirection.normalized));
    }

    private bool TryForceEmergencyReachStep(Vector3 normal, Vector3 fallbackForward, ref bool anyStepActive)
    {
        if (!forceStepBeforeFootExceedsReach)
        {
            return false;
        }

        bool leftOver = IsLegBeyondEmergencyReach(leftLeg, normal, out float leftDistance, out float leftLimit);
        bool rightOver = IsLegBeyondEmergencyReach(rightLeg, normal, out float rightDistance, out float rightLimit);

        if (!leftOver && !rightOver)
        {
            ClampOverextendedPlantedTarget(leftLeg, normal);
            ClampOverextendedPlantedTarget(rightLeg, normal);
            return false;
        }

        int stepIndex = ChooseEmergencyStepLeg(leftOver, rightOver, leftDistance, rightDistance);

        Leg leg = GetLeg(stepIndex);
        if (leg == null)
        {
            return false;
        }

        if (leg.isStepping)
        {
            RetargetActiveEmergencyStep(leg, normal, fallbackForward);
            return true;
        }

        if (oneStepAtATime && anyStepActive && !allowEmergencyStepWhileOtherLegSteps)
        {
            ClampOverextendedPlantedTarget(leg, normal);
            return false;
        }

        Vector3 destination = IsMovingEnoughForGait()
            ? CalculateLargeGaitStepDestination(leg, normal, GetMovementStepDirection(normal, fallbackForward), GetPairReach(), out _)
            : CalculateLegHomePosition(leg, normal, fallbackForward, true);
        destination = ProjectLegTargetIntoReach(leg, destination);
        if (raycastFeetToGround)
        {
            destination = ProjectFootTargetToGround(destination, normal);
            destination = ProjectLegTargetIntoReach(leg, destination);
        }

        Vector3 current = GetCurrentLegIkTargetPosition(leg, destination);
        float length = Vector3.ProjectOnPlane(destination - current, normal).magnitude;
        float pairReach = GetPairReach();

        StartLegStep(
            leg,
            stepIndex,
            destination,
            normal,
            Mathf.Max(length, pairReach * minStepReachRatio),
            pairReach
        );

        leg.stepHeight += pairReach * emergencyStepExtraLiftReachRatio;
        anyStepActive = true;
        return true;
    }

    private int ChooseEmergencyStepLeg(bool leftOver, bool rightOver, float leftDistance, float rightDistance)
    {
        bool leftStepping = leftLeg != null && leftLeg.isStepping;
        bool rightStepping = rightLeg != null && rightLeg.isStepping;

        if (leftOver && !leftStepping && (!rightOver || rightStepping))
        {
            return 0;
        }

        if (rightOver && !rightStepping && (!leftOver || leftStepping))
        {
            return 1;
        }

        if (leftOver && rightOver)
        {
            int preferred = Mathf.Clamp(nextStepLegIndex, 0, 1);
            if (preferred == 0 && !leftStepping)
            {
                return 0;
            }

            if (preferred == 1 && !rightStepping)
            {
                return 1;
            }

            return leftDistance >= rightDistance ? 0 : 1;
        }

        return leftOver ? 0 : 1;
    }

    private bool IsLegBeyondEmergencyReach(Leg leg, Vector3 normal, out float distance, out float limit)
    {
        distance = 0f;
        limit = 0f;

        if (leg == null)
        {
            return false;
        }

        Transform start = GetLegStartTransform(leg);
        if (start == null)
        {
            return false;
        }

        // The planted/real target is the deterministic foot target. Fake-target smoothing must
        // not be allowed to force random recovery steps by itself, otherwise the foot bobs or
        // machine-guns tiny steps while visually already at the real target.
        Vector3 target = leg.plantedWorldPosition;
        if (target.sqrMagnitude <= Epsilon && leg.realTarget != null)
        {
            target = leg.realTarget.position;
            leg.plantedWorldPosition = target;
        }

        float startDistance = (target - start.position).magnitude;
        distance = startDistance;

        float usableReach = GetUsableLegReach(leg);
        float hardReach = GetHardLegReach(leg);
        bool movingEnough = IsMovingEnoughForGait();
        float speedTightening = movingEnough ? Mathf.Lerp(1f, 0.88f, GetSpeed01()) : 1f;
        float stepStartRatio = movingEnough
            ? emergencyStepStartReachRatio
            : idleEmergencyStepStartReachRatio;
        float proactiveLimit = usableReach * Mathf.Clamp01(stepStartRatio) * speedTightening;
        limit = Mathf.Max(0.001f, Mathf.Min(proactiveLimit, hardReach - hardReachTriggerTolerance));

        if (ignoreFakeLagForEmergencyStep)
        {
            return distance >= limit;
        }

        Vector3 fakeTarget = GetCurrentLegIkTargetPosition(leg, target);
        float fakeLagDistance = Vector3.ProjectOnPlane(target - fakeTarget, normal).magnitude;
        float lagLimit = usableReach * Mathf.Clamp01(emergencyFakeTargetLagReachRatio);

        return distance >= limit ||
               (IsMovingEnoughForGait() && fakeLagDistance > Mathf.Max(0.001f, lagLimit));
    }

    private void RetargetActiveEmergencyStep(Leg leg, Vector3 normal, Vector3 fallbackForward)
    {
        if (leg == null || !leg.isStepping)
        {
            return;
        }

        Vector3 destination = CalculateLegHomePosition(leg, normal, fallbackForward, true);
        destination = ProjectLegTargetIntoReach(leg, destination);
        if (raycastFeetToGround)
        {
            destination = ProjectFootTargetToGround(destination, normal);
            destination = ProjectLegTargetIntoReach(leg, destination);
        }

        leg.stepEndWorld = destination;
        leg.plantedWorldPosition = destination;
        WriteLegRealTarget(leg, destination);
    }

    private void ClampOverextendedPlantedTarget(Leg leg, Vector3 normal)
    {
        if (leg == null || leg.isStepping)
        {
            return;
        }

        Transform start = GetLegStartTransform(leg);
        if (start == null)
        {
            return;
        }

        Vector3 planted = leg.plantedWorldPosition;
        if (planted.sqrMagnitude <= Epsilon && leg.realTarget != null)
        {
            planted = leg.realTarget.position;
        }

        Vector3 clamped = ProjectLegTargetIntoReach(leg, planted, emergencyRealTargetClampReachRatio);
        if (raycastFeetToGround)
        {
            clamped = ProjectFootTargetToGround(clamped, normal);
            clamped = ProjectLegTargetIntoReach(leg, clamped, emergencyRealTargetClampReachRatio);
        }

        leg.plantedWorldPosition = clamped;
        WriteLegRealTarget(leg, clamped);

        // Do not snap the visible fake target during a last-resort real-target clamp.
        // The fake handle will smooth toward the clamped plant point through MaintainPlantedLeg.
    }

    private float GetUsableLegReach(Leg leg)
    {
        float reach = Mathf.Max(0.001f, leg != null ? leg.reach : GetPairReach());
        return Mathf.Max(0.001f, reach * legReachMultiplier - legReachSafetyPadding);
    }

    private float GetHardLegReach(Leg leg)
    {
        return Mathf.Max(0.001f, leg != null ? leg.reach : GetPairReach());
    }

    private void RetargetActiveMovingStep(Leg leg, float dt, Vector3 normal, float stepT)
    {
        if (!retargetActiveMovingSteps ||
            leg == null ||
            !leg.isStepping ||
            dt <= Epsilon ||
            stepT > activeStepRetargetMaxT ||
            !IsMovingEnoughForGait())
        {
            return;
        }

        Vector3 desired = CalculateLegHomePosition(leg, normal, cachedFrameForward, true);
        desired = ProjectLegTargetIntoReach(leg, desired);
        if (raycastFeetToGround)
        {
            desired = ProjectFootTargetToGround(desired, normal);
            desired = ProjectLegTargetIntoReach(leg, desired);
        }

        float maxMove = Mathf.Max(0.001f, GetUsableLegReach(leg)) *
                        Mathf.Max(0f, activeStepRetargetSpeedMultiplier) *
                        dt;

        leg.stepEndWorld = Vector3.MoveTowards(leg.stepEndWorld, desired, maxMove);
        leg.plantedWorldPosition = leg.stepEndWorld;
        WriteLegRealTarget(leg, leg.stepEndWorld);
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
        RetargetActiveMovingStep(leg, dt, normal, t);

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

        Vector3 liftedPosition = BuildReachSafeSwingPosition(
            leg,
            basePosition,
            leg.stepLiftAxis,
            leg.stepHeight * liftT,
            normal);

        if (TryResolveEarlyLandingPosition(leg, liftedPosition, normal, t, out Vector3 groundedPosition))
        {
            CompleteLegStep(leg, groundedPosition);
            return;
        }

        WriteLegIkTarget(leg, liftedPosition, !allowActiveSwingTargetPastCurrentReach);

        if (t >= 0.999f)
        {
            CompleteLegStep(leg, leg.stepEndWorld);
        }
    }

    private Vector3 BuildReachSafeSwingPosition(
        Leg leg,
        Vector3 basePosition,
        Vector3 liftAxis,
        float requestedLift,
        Vector3 normal)
    {
        if (leg == null || !capSwingLiftBeforeReachClamp || requestedLift <= Epsilon)
        {
            return basePosition + (liftAxis.sqrMagnitude > Epsilon ? liftAxis.normalized * requestedLift : Vector3.zero);
        }

        Transform start = GetLegStartTransform(leg);
        if (start == null)
        {
            return basePosition + (liftAxis.sqrMagnitude > Epsilon ? liftAxis.normalized * requestedLift : Vector3.zero);
        }

        Vector3 axis = liftAxis.sqrMagnitude > Epsilon
            ? liftAxis.normalized
            : (normal.sqrMagnitude > Epsilon ? normal.normalized : Vector3.up);

        float allowedReach = Mathf.Max(0.001f, GetHardLegReach(leg) * Mathf.Max(1f, activeSwingReachSlackRatio));
        Vector3 fromStartToBase = basePosition - start.position;
        float baseDistance = fromStartToBase.magnitude;

        // If the horizontal/base travel is already outside current-frame reach because the body
        // is still moving toward the predicted landing hip, do not pull it backward. The next
        // frames will bring the hip toward the foot. Only suppress extra lift in that case.
        if (baseDistance >= allowedReach)
        {
            return basePosition;
        }

        float alongAxis = Vector3.Dot(fromStartToBase, axis);
        Vector3 perpendicular = fromStartToBase - axis * alongAxis;
        float perpendicularSqr = perpendicular.sqrMagnitude;
        float remainingSqr = allowedReach * allowedReach - perpendicularSqr;

        if (remainingSqr <= Epsilon)
        {
            return basePosition;
        }

        float maxAlongAxis = Mathf.Sqrt(remainingSqr);
        float allowedLift = Mathf.Max(0f, maxAlongAxis - Mathf.Max(0f, alongAxis));
        float finalLift = Mathf.Min(requestedLift, allowedLift);

        return basePosition + axis * finalLift;
    }

    public void SetAirborneLegPose(bool enabled, float lift)
    {
        forceAirbornePose = enabled;
        airbornePoseLift = Mathf.Max(0f, lift);
    }

    public void SetExternalGaitForward(Vector3 worldForward, bool enabled = true)
    {
        Vector3 normal = GetMovementNormal();
        Vector3 planarForward = Vector3.ProjectOnPlane(worldForward, normal);

        if (planarForward.sqrMagnitude <= Epsilon)
        {
            return;
        }

        externalGaitForward = planarForward.normalized;
        useExternalGaitForward = enabled;

        SnapGaitRotationCoreToBodyCore();
        UpdateGaitForwardTarget(normal, externalGaitForward);
        DriveGaitRotationAssigner(externalGaitForward, normal);
    }

    public void ClearExternalGaitForward()
    {
        useExternalGaitForward = false;
    }

    public void ScaleRuntimeLegDimensions(float scale)
    {
        if (scale <= Epsilon)
        {
            return;
        }

        bodyHeightOffGround *= scale;
        legReachSafetyPadding *= scale;
        leadingBehindDistance *= scale;
        stopDistance *= scale;
        slowDownRadius *= scale;
        coreGroundRayHeight *= scale;
        coreGroundRayDistance *= scale;
        coreGroundOffset *= scale;
        footRayHeight *= scale;
        footRayDistance *= scale;
        footGroundOffset *= scale;

        leftLeg.manualReach *= scale;
        rightLeg.manualReach *= scale;
        leftLeg.reach *= scale;
        rightLeg.reach *= scale;
        leftLeg.upperLegLength *= scale;
        leftLeg.lowerLegLength *= scale;
        rightLeg.upperLegLength *= scale;
        rightLeg.lowerLegLength *= scale;

        // Smaller rigs have less reach, so the same world movement consumes a larger fraction
        // of leg range. Keep the authored rhythm, but make target settlement/catchup stricter
        // as scale goes down so IK handles do not visually fall behind.
        if (scale < 0.999f)
        {
            float inverseScale = 1f / Mathf.Max(0.25f, scale);
            legFakeTargetFrequencyHz = Mathf.Max(legFakeTargetFrequencyHz, 16f * Mathf.Sqrt(inverseScale));
            legFakeTargetSpeedFrequencyBoostHz = Mathf.Max(legFakeTargetSpeedFrequencyBoostHz, 14f * Mathf.Sqrt(inverseScale));
            fakeTargetLagCatchupReachPerSecond = Mathf.Max(fakeTargetLagCatchupReachPerSecond, 16f * inverseScale);
            fakeTargetLagCatchupCoreSpeedMultiplier = Mathf.Max(fakeTargetLagCatchupCoreSpeedMultiplier, 2.8f);
            dynamicLegFakeTargetSpeedMultiplier = Mathf.Max(dynamicLegFakeTargetSpeedMultiplier, 8.5f * Mathf.Sqrt(inverseScale));
            dynamicLegFakeTargetAccelerationMultiplier = Mathf.Max(dynamicLegFakeTargetAccelerationMultiplier, 28f * Mathf.Sqrt(inverseScale));
            maxFakeTargetLagReachRatio = Mathf.Min(maxFakeTargetLagReachRatio, 0.11f);
            stableFastStepCadence = Mathf.Max(0.18f, stableFastStepCadence / Mathf.Sqrt(inverseScale));
            stableSlowStepCadence = Mathf.Max(0.26f, stableSlowStepCadence / Mathf.Sqrt(inverseScale));
        }
    }

    private void UpdateAirbornePose(float dt)
    {
        float targetLift = forceAirbornePose && useAirbornePose
            ? airbornePoseLift
            : 0f;

        if (airbornePoseBlendSpeed <= 0f)
        {
            currentAirbornePoseLift = targetLift;
            return;
        }

        currentAirbornePoseLift = Mathf.MoveTowards(
            currentAirbornePoseLift,
            targetLift,
            airbornePoseBlendSpeed * dt
        );
    }

    private void CancelActiveStep(Leg leg)
    {
        if (leg == null || !leg.isStepping)
        {
            return;
        }

        leg.isStepping = false;
        leg.plantedWorldPosition = GetCurrentLegIkTargetPosition(leg, leg.plantedWorldPosition);
        steppingLegIndex = -1;
    }

    private bool TryResolveEarlyLandingPosition(
        Leg leg,
        Vector3 liftedPosition,
        Vector3 normal,
        float stepT,
        out Vector3 groundedPosition
    )
    {
        groundedPosition = liftedPosition;

        if (!earlyLandFeetOnTerrain || !raycastFeetToGround || stepT < earlyLandingMinStepT)
        {
            return false;
        }

        Vector3 safeNormal = normal.sqrMagnitude > Epsilon
            ? normal.normalized
            : Vector3.up;

        Vector3 origin = liftedPosition + safeNormal * footRayHeight;
        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            -safeNormal,
            footRayHeight + footRayDistance,
            footGroundMask,
            QueryTriggerInteraction.Ignore);

        if (hits.Length > 1)
        {
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        }

        Transform selfRoot = transform.root != null ? transform.root : transform;
        bool foundGround = false;
        RaycastHit hit = new RaycastHit();

        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].transform != null && selfRoot != null && hits[i].transform.IsChildOf(selfRoot))
            {
                continue;
            }

            hit = hits[i];
            foundGround = true;
            break;
        }

        if (!foundGround)
        {
            return false;
        }

        Vector3 hitNormal = hit.normal.sqrMagnitude > Epsilon
            ? hit.normal.normalized
            : safeNormal;
        Vector3 terrainPosition = hit.point + hitNormal * footGroundOffset;

        float signedClearance = Vector3.Dot(liftedPosition - terrainPosition, safeNormal);
        if (signedClearance > 0f)
        {
            return false;
        }

        groundedPosition = terrainPosition;
        return true;
    }

    private void CompleteLegStep(Leg leg, Vector3 finalPosition)
    {
        leg.stepEndWorld = finalPosition;
        leg.plantedWorldPosition = finalPosition;

        WriteLegRealTarget(leg, finalPosition);
        WriteLegIkTarget(leg, finalPosition, !allowActiveSwingTargetPastCurrentReach);

        leg.isStepping = false;
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

    private float EstimateStepDurationForLength(float stepLength, float pairReach, Vector3 start, Vector3 destination)
    {
        float fullStep = pairReach * Mathf.Max(fullStepReachRatio, Epsilon);
        float length01 = Mathf.Clamp01(stepLength / fullStep);
        float duration = Mathf.Lerp(slowStepDuration, fastStepDuration, momentum);

        if (scaleDurationByStepLength)
        {
            float durationScale = Mathf.Lerp(
                1f - stepLengthDurationInfluence,
                1f + stepLengthDurationInfluence,
                length01
            );

            duration *= Mathf.Max(0.05f, durationScale);
        }

        float adaptiveFootSpeed = Mathf.Max(
            0.01f,
            Mathf.Max(runSpeed, currentCoreVelocity.magnitude) * footTargetSpeedMultiplier
        );
        float adaptiveDuration = Vector3.Distance(start, destination) / adaptiveFootSpeed;

        return Mathf.Clamp(
            Mathf.Max(duration, adaptiveDuration),
            minAdaptiveStepDuration,
            maxAdaptiveStepDuration
        );
    }

    private Vector3 ProjectStepEndForPredictedLanding(
        Leg leg,
        Vector3 destination,
        float predictedStepDuration,
        Vector3 normal)
    {
        if (!usePredictedLandingReachForStepEnds)
        {
            return ProjectLegTargetIntoReach(leg, destination);
        }

        Vector3 predictedStart = GetPredictedLegStartPosition(leg, predictedStepDuration, normal);
        return ProjectLegTargetIntoReachFromOrigin(leg, destination, predictedStart, 0.995f, normal);
    }

    private Vector3 GetPredictedCorePosition(float predictedStepDuration, Vector3 normal)
    {
        Vector3 corePosition = coreNode != null ? coreNode.position : transform.position;
        Vector3 planarVelocity = Vector3.ProjectOnPlane(currentCoreVelocity, normal);
        return corePosition + planarVelocity * Mathf.Max(0f, predictedStepDuration * predictedLandingTimeScale);
    }

    private Vector3 GetPredictedLegStartPosition(Leg leg, float predictedStepDuration, Vector3 normal)
    {
        Transform start = GetLegStartTransform(leg);
        Vector3 startPosition = start != null
            ? start.position
            : coreNode != null
                ? coreNode.position
                : transform.position;

        Vector3 planarVelocity = Vector3.ProjectOnPlane(currentCoreVelocity, normal);
        return startPosition + planarVelocity * Mathf.Max(0f, predictedStepDuration * predictedLandingTimeScale);
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
        Vector3 normal = GetMovementNormal();
        Vector3 start = GetCurrentLegIkTargetPosition(leg, destination);

        // The final gait builder already preserves the forward landing while keeping the point
        // inside the usable leg radius. In this mode, do not run the older generic reach clamp
        // because it can remove the forward component and produce a lifted foot that remains
        // stretched behind the body. Legacy modes still use the defensive clamp.
        if (!useSingleRuleAnticipatoryGait)
        {
            destination = ProjectLegTargetIntoReach(leg, destination, 0.985f);
            if (raycastFeetToGround)
            {
                destination = ProjectFootTargetToGround(destination, normal);
                destination = ProjectLegTargetIntoReach(leg, destination, 0.985f);
            }
        }

        start = GetCurrentLegIkTargetPosition(leg, destination);

        leg.stepStartWorld = start;
        leg.stepEndWorld = destination;
        leg.plantedWorldPosition = destination;
        leg.stepTimer = 0f;

        float fullStep = pairReach * Mathf.Max(fullStepReachRatio, Epsilon);
        float length01 = Mathf.Clamp01(stepLength / fullStep);

        leg.stepDuration = EstimateStepDurationForLength(stepLength, pairReach, start, destination);
        if (useSingleRuleAnticipatoryGait)
        {
            float length01ForDuration = Mathf.Clamp01(stepLength / Mathf.Max(pairReach * fullStepReachRatio, Epsilon));
            float speed01ForDuration = GetSpeed01();
            float targetDuration = Mathf.Lerp(stableMaxSwingDuration, stableMinSwingDuration, speed01ForDuration);
            targetDuration = Mathf.Lerp(targetDuration * 0.92f, targetDuration * 1.12f, length01ForDuration);
            leg.stepDuration = Mathf.Clamp(targetDuration, stableMinSwingDuration, stableMaxSwingDuration);
        }

        leg.stepHeight =
            pairReach *
            (baseStepHeightReachRatio +
             momentum * momentumStepHeightReachRatio +
             length01 * speedStepHeightReachRatio +
             GetSpeed01() * movementSpeedStepLiftReachRatio);

        leg.stepHeight += stepLength * stepLengthHeightInfluence;

        leg.stepLiftAxis = GetStepLiftAxis(landingNormal);

        leg.isStepping = true;
        steppingLegIndex = legIndex;
        nextStepLegIndex = legIndex == 0 ? 1 : 0;
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
        Vector3 legForward = GetLegForwardDirection(leg, normal, forward);
        Vector3 legSide = GetLegSideDirection(leg, normal, forward);

        if (useDeterministicHomeStepping)
        {
            landingNormal = normal;
            return CalculateLegHomePosition(leg, normal, forward, true);
        }

        Vector3 desired = CalculateLegTargetInFrontOfStart(
            leg,
            normal,
            legForward,
            stepLength
        );

        Vector3 grounded = FindGroundedStepPoint(
            leg,
            desired,
            normal,
            legForward,
            legSide,
            out landingNormal
        );

        grounded = ProjectLegTargetIntoReach(leg, grounded);

        if (raycastFeetToGround)
        {
            grounded = FindGroundedStepPoint(
                leg,
                grounded,
                normal,
                legForward,
                legSide,
                out landingNormal
            );

            grounded = ProjectLegTargetIntoReach(leg, grounded);
        }

        return grounded;
    }

    private void UpdateRealTargetPreviews(Vector3 normal, Vector3 fallbackForward)
    {
        UpdateRealTargetPreview(leftLeg, normal, fallbackForward);
        UpdateRealTargetPreview(rightLeg, normal, fallbackForward);
    }

    private void UpdateRealTargetPreview(Leg leg, Vector3 normal, Vector3 fallbackForward)
    {
        if (leg == null || leg.realTarget == null || leg.isStepping)
        {
            return;
        }

        Vector3 desired = CalculateLegHomePosition(
            leg,
            normal,
            fallbackForward,
            currentCoreVelocity.magnitude > 0.05f || momentum > momentumCarryThreshold
        );

        if (useDeterministicHomeStepping)
        {
            // In deterministic stepping, the real target is the planted foot/destination marker.
            // Do not preview-slide it every frame; otherwise offset nodes and solvers can sample
            // a transient target that appears to float or snap before the actual step begins.
            return;
        }

        Vector3 grounded = FindGroundedStepPoint(
            leg,
            desired,
            normal,
            GetLegForwardDirection(leg, normal, fallbackForward),
            GetLegSideDirection(leg, normal, fallbackForward),
            out _
        );

        WriteLegRealTarget(leg, grounded);
    }

    private Vector3 CalculateLegHomePosition(
        Leg leg,
        Vector3 normal,
        Vector3 fallbackForward,
        bool includeSpeedLead
    )
    {
        float reach = leg != null ? leg.reach : GetPairReach();
        float usableReach = Mathf.Max(0.001f, GetUsableLegReach(leg));
        float speed01 = GetSpeed01();
        bool movingPlacement = includeSpeedLead && placeMovingStepsFromBodyCenter;

        Vector3 placementForward = movingPlacement && useMovementDirectionForMovingStepForward
            ? GetMovementStepDirection(normal, fallbackForward)
            : GetLegForwardDirection(leg, normal, fallbackForward);

        if (placementForward.sqrMagnitude <= Epsilon)
        {
            placementForward = GetLegForwardDirection(leg, normal, fallbackForward);
        }

        placementForward = Vector3.ProjectOnPlane(placementForward, normal);
        if (placementForward.sqrMagnitude <= Epsilon)
        {
            placementForward = Vector3.ProjectOnPlane(fallbackForward, normal);
        }
        if (placementForward.sqrMagnitude <= Epsilon)
        {
            placementForward = Vector3.forward;
        }
        placementForward.Normalize();

        // One clear deterministic home per leg: body/hip lane side offset plus an anticipatory
        // forward distance. The distance grows with speed, but is bounded inside leg reach so
        // real targets cannot be left behind outside the IK chain.
        float strideRatio = includeSpeedLead
            ? Mathf.Lerp(restingTargetForwardReachRatio, fullStepReachRatio, speed01)
            : restingTargetForwardReachRatio;

        float velocityLeadRatio = 0f;
        if (includeSpeedLead)
        {
            float forwardSpeed = Mathf.Max(0f, Vector3.Dot(currentCoreVelocity, placementForward));
            velocityLeadRatio = Mathf.Clamp(
                (forwardSpeed * Mathf.Max(speedLookAheadTime, movingStepAnticipationTime)) / usableReach,
                0f,
                maxHomeSpeedLeadReachRatio
            );
        }

        float totalForwardRatio = strideRatio +
                                  (includeSpeedLead ? movingStepForwardBiasReachRatio * Mathf.Lerp(0.35f, 1f, speed01) : 0f) +
                                  velocityLeadRatio;
        totalForwardRatio = Mathf.Clamp(totalForwardRatio, restingTargetForwardReachRatio, maxDesiredMovingHomeReachRatio);
        float forwardDistance = Mathf.Max(GetMinimumForwardTargetOffset(), usableReach * totalForwardRatio);

        Vector3 desired;

        if (movingPlacement && coreNode != null)
        {
            Vector3 side = Vector3.Cross(normal, placementForward);
            if (side.sqrMagnitude <= Epsilon)
            {
                side = GetLegSideDirection(leg, normal, fallbackForward);
            }
            side.Normalize();

            float sideOffset = GetMovingStepSideLaneOffset(leg, normal, side);
            float minimumSideLane = usableReach * 0.18f;
            if (Mathf.Abs(sideOffset) < minimumSideLane)
            {
                float sign = Mathf.Abs(sideOffset) > Epsilon
                    ? Mathf.Sign(sideOffset)
                    : leg == leftLeg ? -1f : 1f;
                sideOffset = sign * minimumSideLane;
            }

            float forwardOffset = Mathf.Max(0f, GetMovingStepForwardLaneOffset(leg, normal, placementForward));
            desired = coreNode.position + side * sideOffset + placementForward * (forwardOffset + forwardDistance);
        }
        else
        {
            desired = CalculateLegTargetInFrontOfStart(
                leg,
                normal,
                placementForward,
                forwardDistance,
                true
            );
        }

        if (raycastFeetToGround)
        {
            desired = RaycastPointToGround(
                desired,
                normal,
                footRayHeight,
                footRayDistance,
                footGroundMask,
                footGroundOffset,
                out _
            );
        }

        return ProjectLegTargetIntoReach(leg, desired, maxDesiredMovingHomeReachRatio);
    }

    private Vector3 GetMovementStepDirection(Vector3 normal, Vector3 fallbackForward)
    {
        Vector3 direction = Vector3.zero;

        if (coreNode != null && runTarget != null)
        {
            direction = Vector3.ProjectOnPlane(runTarget.position - coreNode.position, normal);
        }

        if (direction.sqrMagnitude <= Epsilon && currentCoreVelocity.sqrMagnitude > Epsilon)
        {
            direction = Vector3.ProjectOnPlane(currentCoreVelocity, normal);
        }

        if (direction.sqrMagnitude <= Epsilon)
        {
            direction = Vector3.ProjectOnPlane(fallbackForward, normal);
        }

        if (direction.sqrMagnitude <= Epsilon)
        {
            direction = Vector3.ProjectOnPlane(externalGaitForward, normal);
        }

        if (direction.sqrMagnitude <= Epsilon)
        {
            direction = Vector3.forward;
        }

        return direction.normalized;
    }

    private float GetMovingStepSideLaneOffset(Leg leg, Vector3 normal, Vector3 side)
    {
        if (leg == null || coreNode == null)
        {
            return 0f;
        }

        Transform start = GetLegStartTransform(leg);
        if (start != null)
        {
            Vector3 fromCore = Vector3.ProjectOnPlane(start.position - coreNode.position, normal);
            float dynamicSide = Vector3.Dot(fromCore, side.normalized);
            if (Mathf.Abs(dynamicSide) > Epsilon)
            {
                return dynamicSide;
            }
        }

        if (Mathf.Abs(leg.capturedSideOffset) > Epsilon)
        {
            return leg.capturedSideOffset;
        }

        return leg == leftLeg ? -GetMinimumForwardTargetOffset() : GetMinimumForwardTargetOffset();
    }

    private float GetMovingStepForwardLaneOffset(Leg leg, Vector3 normal, Vector3 forward)
    {
        if (leg == null || coreNode == null)
        {
            return 0f;
        }

        Transform start = GetLegStartTransform(leg);
        if (start != null)
        {
            Vector3 fromCore = Vector3.ProjectOnPlane(start.position - coreNode.position, normal);
            float dynamicForward = Vector3.Dot(fromCore, forward.normalized);
            if (Mathf.Abs(dynamicForward) > Epsilon)
            {
                return dynamicForward;
            }
        }

        return leg.capturedForwardOffset;
    }

    private float GetPlanarFootHomeDistance(Leg leg, Vector3 home, Vector3 normal)
    {
        if (leg == null)
        {
            return 0f;
        }

        // Trigger steps from the deterministic planted/real foot, not the smoothed fake target.
        // The fake may lag on purpose; using it here creates micro correction steps and jitter.
        Vector3 foot = leg.plantedWorldPosition;
        if (foot.sqrMagnitude <= Epsilon && leg.realTarget != null)
        {
            foot = leg.realTarget.position;
        }
        return Vector3.ProjectOnPlane(home - foot, normal).magnitude;
    }

    private Vector3 CalculateLegTargetInFrontOfStart(
        Leg leg,
        Vector3 normal,
        Vector3 fallbackForward,
        float extraForwardDistance
    )
    {
        return CalculateLegTargetInFrontOfStart(leg, normal, fallbackForward, extraForwardDistance, false);
    }

    private Vector3 CalculateLegTargetInFrontOfStart(
        Leg leg,
        Vector3 normal,
        Vector3 fallbackForward,
        float forwardDistance,
        bool forwardDistanceIsAbsolute
    )
    {
        Transform start = GetLegStartTransform(leg);

        Vector3 origin = start != null
            ? start.position
            : coreNode != null
                ? coreNode.position
                : transform.position;

        Vector3 legForward = GetLegForwardDirection(leg, normal, fallbackForward);
        float baseDistance = Mathf.Max(
            GetMinimumForwardTargetOffset(),
            (leg != null ? leg.reach : GetPairReach()) * restingTargetForwardReachRatio
        );

        float finalDistance = forwardDistanceIsAbsolute
            ? Mathf.Max(GetMinimumForwardTargetOffset(), forwardDistance)
            : baseDistance + Mathf.Max(0f, forwardDistance);

        return origin + legForward * finalDistance;
    }

    private Vector3 GetLegForwardDirection(Leg leg, Vector3 normal, Vector3 fallbackForward)
    {
        Vector3 forward = Vector3.zero;

        if (useStaticPoleAsLegForward && leg != null && leg.staticPole != null)
        {
            Transform start = GetLegStartTransform(leg);
            Vector3 origin = start != null
                ? start.position
                : coreNode != null
                    ? coreNode.position
                    : transform.position;

            forward = Vector3.ProjectOnPlane(leg.staticPole.position - origin, normal);
        }

        if (forward.sqrMagnitude <= Epsilon)
        {
            forward = Vector3.ProjectOnPlane(fallbackForward, normal);
        }

        if (forward.sqrMagnitude <= Epsilon && gaitForwardTarget != null)
        {
            Transform rotationCore = ResolveGaitRotationCore();
            Vector3 origin = rotationCore != null ? rotationCore.position : transform.position;
            forward = Vector3.ProjectOnPlane(gaitForwardTarget.position - origin, normal);
        }

        if (forward.sqrMagnitude <= Epsilon)
        {
            forward = Vector3.forward;
        }

        return forward.normalized;
    }

    private Vector3 GetLegSideDirection(Leg leg, Vector3 normal, Vector3 fallbackForward)
    {
        Vector3 forward = GetLegForwardDirection(leg, normal, fallbackForward);
        Vector3 side = Vector3.Cross(normal, forward);

        if (side.sqrMagnitude <= Epsilon)
        {
            return Vector3.right;
        }

        return side.normalized;
    }

    private float CalculateStepLength(
        float pairReach,
        out float requestedStepLength
    )
    {
        float baseLength = pairReach * baseStepReachRatio;
        float momentumLength = pairReach * momentumStepReachRatio * momentum;
        float movementDebtLength = movementBlockDebt * movementBlockStepInfluence;
        float speedLength = currentCoreVelocity.magnitude * speedLookAheadTime;

        requestedStepLength = Mathf.Max(
            baseLength + momentumLength + movementDebtLength,
            speedLength
        );

        float minLength = pairReach * minStepReachRatio;
        float speedMinLength = pairReach * minSpeedStepReachRatio * Mathf.Clamp01(momentum);
        float maxLength = pairReach * Mathf.Max(maxStepReachRatio, maxSpeedStepReachRatio);
        minLength = Mathf.Max(minLength, speedMinLength);

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
        return ProjectLegTargetIntoReach(leg, worldPoint, 1f);
    }

    private Vector3 ProjectLegTargetIntoReachFromOrigin(
        Leg leg,
        Vector3 worldPoint,
        Vector3 origin,
        float reachRatio,
        Vector3 normal)
    {
        if (!clampTargetsWithLimbSolver && !enforceBodyDistanceReachForFeet)
        {
            return worldPoint;
        }

        float usableReach = Mathf.Max(0.001f, GetUsableLegReach(leg));
        float safeReach = Mathf.Max(0.001f, usableReach * Mathf.Clamp01(reachRatio));
        return ProjectFootAwarePointAroundOriginIntoRadius(
            worldPoint,
            origin,
            safeReach,
            leg,
            normal.sqrMagnitude > Epsilon ? normal : GetMovementNormal());
    }

    private Vector3 ProjectLegTargetIntoReach(Leg leg, Vector3 worldPoint, float reachRatio)
    {
        if (!clampTargetsWithLimbSolver && !enforceBodyDistanceReachForFeet)
        {
            return worldPoint;
        }

        Transform legStart = GetLegStartTransform(leg);

        if (legStart == null && coreNode == null)
        {
            return worldPoint;
        }

        float usableReach = Mathf.Max(0.001f, GetUsableLegReach(leg));
        float safeReach = Mathf.Max(0.001f, usableReach * Mathf.Clamp01(reachRatio));
        Vector3 projected = worldPoint;
        Vector3 normal = GetMovementNormal();

        if (clampTargetsWithLimbSolver && legStart != null)
        {
            projected = ProjectFootAwarePointAroundOriginIntoRadius(
                projected,
                legStart.position,
                safeReach,
                leg,
                normal);
        }

        // Body/core distance is only a planar anti-trail hint. The real hard limit is the
        // IK chain from the leg start. A full 3D body-distance clamp consumes the body height
        // and pulls grounded feet into the air on smaller scales.
        if (enforceBodyDistanceReachForFeet && coreNode != null)
        {
            Vector3 fromCore = Vector3.ProjectOnPlane(projected - coreNode.position, normal);
            float planarDistance = fromCore.magnitude;
            if (planarDistance > safeReach && planarDistance > Epsilon)
            {
                projected = coreNode.position + fromCore.normalized * safeReach + normal * Vector3.Dot(projected - coreNode.position, normal);
            }
        }

        return projected;
    }

    private Vector3 ProjectFootAwarePointAroundOriginIntoRadius(
        Vector3 worldPoint,
        Vector3 origin,
        float radius,
        Leg leg,
        Vector3 normal)
    {
        if (!preserveGroundHeightWhenClampingFootTargets)
        {
            return ProjectPointAroundOriginIntoRadius(worldPoint, origin, radius, leg, normal);
        }

        Vector3 safeNormal = normal.sqrMagnitude > Epsilon ? normal.normalized : Vector3.up;
        Vector3 fromOrigin = worldPoint - origin;
        float normalDistance = Vector3.Dot(fromOrigin, safeNormal);
        Vector3 planar = Vector3.ProjectOnPlane(fromOrigin, safeNormal);
        float planarDistance = planar.magnitude;

        float remainingSquared = radius * radius - normalDistance * normalDistance;
        if (remainingSquared <= 0.000001f)
        {
            // Ground is vertically outside the reachable sphere; fall back to a full 3D clamp.
            return ProjectPointAroundOriginIntoRadius(worldPoint, origin, radius, leg, safeNormal);
        }

        float maxPlanarDistance = Mathf.Sqrt(remainingSquared);
        if (planarDistance <= maxPlanarDistance)
        {
            return worldPoint;
        }

        Vector3 planarDirection = planarDistance > Epsilon
            ? planar / planarDistance
            : GetLegForwardDirection(leg, safeNormal, GetMovementForward(safeNormal));
        if (planarDirection.sqrMagnitude <= Epsilon)
        {
            planarDirection = Vector3.forward;
        }

        return origin + safeNormal * normalDistance + planarDirection.normalized * maxPlanarDistance;
    }

    private Vector3 ProjectPointAroundOriginIntoRadius(
        Vector3 worldPoint,
        Vector3 origin,
        float radius,
        Leg leg,
        Vector3 normal
    )
    {
        Vector3 fromOrigin = worldPoint - origin;
        float distance = fromOrigin.magnitude;

        if (distance <= radius)
        {
            return worldPoint;
        }

        Vector3 direction;
        if (distance > Epsilon)
        {
            direction = fromOrigin / distance;
        }
        else
        {
            direction = GetLegForwardDirection(leg, normal, GetMovementForward(normal));
            if (direction.sqrMagnitude <= Epsilon)
            {
                direction = Vector3.forward;
            }
            direction.Normalize();
        }

        return origin + direction * radius;
    }

    private Vector3 ClampLegTargetToReach(Leg leg, Vector3 worldPoint)
    {
        if (!clampTargetsWithLimbSolver && !enforceBodyDistanceReachForFeet)
        {
            return worldPoint;
        }

        return ProjectLegTargetIntoReach(leg, worldPoint);
    }

    private Vector3 ProjectFootTargetToGround(Vector3 position, Vector3 normal)
    {
        if (!raycastFeetToGround)
        {
            return position;
        }

        return RaycastPointToGround(
            position,
            normal,
            footRayHeight,
            footRayDistance,
            footGroundMask,
            footGroundOffset,
            out _
        );
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

        float effectiveRayHeight = Mathf.Max(rayHeight, minFootGroundRayHeight);
        float effectiveRayDistance = Mathf.Max(rayDistance, minFootGroundRayDistance);

        Vector3 origin = point + normal * effectiveRayHeight;
        Vector3 direction = -normal;
        float maxDistance = effectiveRayHeight + effectiveRayDistance;
        RaycastHit[] hits = Physics.RaycastAll(
            origin,
            direction,
            maxDistance,
            mask,
            QueryTriggerInteraction.Ignore
        );

        if (hits.Length > 1)
        {
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        }

        Transform selfRoot = transform.root != null ? transform.root : transform;
        for (int i = 0; i < hits.Length; i++)
        {
            RaycastHit hit = hits[i];
            if (hit.transform != null && selfRoot != null && hit.transform.IsChildOf(selfRoot))
            {
                continue;
            }

            hitNormal = hit.normal.sqrMagnitude > Epsilon
                ? hit.normal.normalized
                : normal;

            return hit.point + hitNormal * groundOffset;
        }

        hitNormal = normal;

        if (fallbackFeetToTerrainHeight &&
            TrySampleTerrainGround(point, normal, groundOffset, out Vector3 terrainPoint))
        {
            return terrainPoint;
        }

        if (fallbackFeetToWorldZeroPlane &&
            TryProjectToWorldZeroPlane(point, normal, groundOffset, out Vector3 zeroPlanePoint))
        {
            return zeroPlanePoint;
        }

        return point;
    }

    private bool TrySampleTerrainGround(
        Vector3 point,
        Vector3 normal,
        float groundOffset,
        out Vector3 groundedPoint
    )
    {
        groundedPoint = point;

        Terrain terrain = Terrain.activeTerrain;
        if (terrain == null || terrain.terrainData == null)
        {
            return false;
        }

        Vector3 terrainPosition = terrain.transform.position;
        Vector3 terrainSize = terrain.terrainData.size;

        if (
            point.x < terrainPosition.x ||
            point.z < terrainPosition.z ||
            point.x > terrainPosition.x + terrainSize.x ||
            point.z > terrainPosition.z + terrainSize.z
        )
        {
            return false;
        }

        Vector3 safeNormal = normal.sqrMagnitude > Epsilon
            ? normal.normalized
            : Vector3.up;

        float y = terrain.SampleHeight(point) + terrainPosition.y;
        groundedPoint = new Vector3(point.x, y, point.z) + safeNormal * groundOffset;
        return true;
    }

    private bool TryProjectToWorldZeroPlane(
        Vector3 point,
        Vector3 normal,
        float groundOffset,
        out Vector3 groundedPoint
    )
    {
        groundedPoint = point;

        Vector3 safeNormal = normal.sqrMagnitude > Epsilon
            ? normal.normalized
            : Vector3.up;

        if (Mathf.Abs(Vector3.Dot(safeNormal, Vector3.up)) < 0.5f)
        {
            return false;
        }

        groundedPoint = new Vector3(point.x, 0f, point.z) + safeNormal * groundOffset;
        return true;
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

        if (TryGetExternalGaitForward(normal, out forward))
        {
            return forward;
        }

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

    private bool TryGetExternalGaitForward(Vector3 normal, out Vector3 forward)
    {
        forward = Vector3.zero;

        if (!useExternalGaitForward)
        {
            Transform rotationCore = ResolveGaitRotationCore();
            if (gaitForwardTarget == null || rotationCore == null)
            {
                return false;
            }

            forward = Vector3.ProjectOnPlane(
                gaitForwardTarget.position - rotationCore.position,
                normal
            );

            if (forward.sqrMagnitude <= Epsilon)
            {
                return false;
            }

            forward.Normalize();
            return true;
        }

        forward = Vector3.ProjectOnPlane(externalGaitForward, normal);

        if (forward.sqrMagnitude <= Epsilon)
        {
            return false;
        }

        forward.Normalize();
        return true;
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

    private void RotateGaitRotationCoreTowardDirection(
        Vector3 targetDirection,
        Vector3 normal,
        float dt
    )
    {
        Transform rotationCore = ResolveGaitRotationCore();
        if (
            rotationCore == null ||
            rotationCore == coreNode ||
            targetDirection.sqrMagnitude <= Epsilon
        )
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

        rotationCore.rotation = gaitRotationCoreTurnSpeedDegrees > 0f
            ? Quaternion.RotateTowards(
                rotationCore.rotation,
                targetRotation,
                gaitRotationCoreTurnSpeedDegrees * dt
            )
            : targetRotation;
    }

    private Vector3 ResolveGaitReferenceForward(Vector3 normal)
    {
        Vector3 safeNormal = normal.sqrMagnitude > Epsilon
            ? normal.normalized
            : Vector3.up;

        Transform rotationCore = ResolveGaitRotationCore();
        Vector3 referenceForward = Vector3.zero;

        if (gaitRotationAngleReference != null &&
            gaitRotationAngleReference != gaitForwardTarget &&
            rotationCore != null)
        {
            referenceForward = Vector3.ProjectOnPlane(
                gaitRotationAngleReference.position - rotationCore.position,
                safeNormal);
        }

        if (referenceForward.sqrMagnitude <= Epsilon &&
            hasCapturedGaitReferenceForward)
        {
            referenceForward = Vector3.ProjectOnPlane(capturedGaitReferenceForward, safeNormal);
        }

        if (referenceForward.sqrMagnitude <= Epsilon && forwardReference != null && rotationCore != null)
        {
            referenceForward = Vector3.ProjectOnPlane(
                forwardReference.position - rotationCore.position,
                safeNormal);
        }

        if (referenceForward.sqrMagnitude <= Epsilon && rotationCore != null)
        {
            referenceForward = Vector3.ProjectOnPlane(rotationCore.forward, safeNormal);
        }

        if (referenceForward.sqrMagnitude <= Epsilon)
        {
            referenceForward = Vector3.ProjectOnPlane(Vector3.forward, safeNormal);
        }

        return referenceForward.sqrMagnitude > Epsilon
            ? referenceForward.normalized
            : Vector3.zero;
    }

    private void DriveGaitRotationAssigner(Vector3 targetDirection, Vector3 normal)
    {
        if (gaitRotationAssigner == null || targetDirection.sqrMagnitude <= Epsilon)
        {
            return;
        }

        Vector3 safeNormal = normal.sqrMagnitude > Epsilon
            ? normal.normalized
            : Vector3.up;
        Vector3 targetForward = Vector3.ProjectOnPlane(targetDirection, safeNormal);

        if (targetForward.sqrMagnitude <= Epsilon)
        {
            return;
        }

        targetForward.Normalize();

        ApplyOffsetPositionNow(gaitRotationAngleReference);
        ApplyOffsetPositionNow(forwardReference);

        Transform rotationCore = ResolveGaitRotationCore();
        Vector3 referenceForward = ResolveGaitReferenceForward(safeNormal);

        if (referenceForward.sqrMagnitude <= Epsilon)
        {
            return;
        }

        referenceForward.Normalize();
        float yaw = Vector3.SignedAngle(referenceForward, targetForward, safeNormal);
        gaitRotationAssigner.SetInputRotationDegrees(yaw);
        gaitRotationAssigner.ApplyRotation(yaw);
        // Belt-and-braces: force the same yaw directly into the lower-body rotatable
        // nodes after the assigner has accepted it. If a disabled/duplicate pair or
        // stale prefab reference prevents normal distribution, this still writes the
        // dynamic offsets that the leg starts and local poles actually read.
        ApplyGaitRotationOffsetNodesNow(yaw);
        ApplyStaticPoleAndPhysicalPolePositions(safeNormal, targetForward);
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

    private float GetFootForwardRelativeToLegStart(Leg leg, Vector3 normal, Vector3 fallbackForward)
    {
        if (leg == null || leg.fakeTarget == null)
        {
            return 0f;
        }

        Transform start = GetLegStartTransform(leg);
        if (start == null)
        {
            return GetFootForwardRelativeToCore(leg, fallbackForward);
        }

        Vector3 legForward = GetLegForwardDirection(leg, normal, fallbackForward);
        Vector3 fromStart = Vector3.ProjectOnPlane(leg.fakeTarget.position - start.position, normal);
        return Vector3.Dot(fromStart, legForward);
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

        if (leg.startNode == null)
        {
            leg.startNode = leg.limbSolver.start;
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

        if (leg.limbSolver != null)
        {
            leg.limbSolver.tailTargetOverride = null;
            leg.limbSolver.restoreTailToSolvedEndAfterSolving = false;
            leg.limbSolver.clampTailToReachBeforeSolving = false;
        }

        EnsurePhysicalIkPole(leg);
        AssignStaticPole(leg);
    }

    private void ApplyRuntimeLegDimensions(Leg leg)
    {
        if (!deriveLegDimensionsAtRuntime || leg == null)
        {
            return;
        }

        NodeState tail = leg.tailNode != null
            ? leg.tailNode
            : leg.limbSolver != null
                ? leg.limbSolver.tail
                : null;

        NodeState start = leg.startNode != null
            ? leg.startNode
            : leg.limbSolver != null
                ? leg.limbSolver.start
                : null;

        if (tail == null || start == null || tail.next == null)
        {
            return;
        }

        NodeState knee = tail.next;
        float height = Mathf.Max(0.01f, bodyHeightOffGround);
        float halfBendRadians = Mathf.Clamp(kneeDefaultBendAngle, 0f, 120f) * Mathf.Deg2Rad * 0.5f;
        float projection = Mathf.Max(0.2f, Mathf.Cos(halfBendRadians));
        float totalLength = height / projection;
        totalLength *= Mathf.Max(0.1f, runtimeLegLengthMultiplier);

        float upperRatio = Mathf.Clamp(upperLegLengthRatio, 0.2f, 0.8f);
        leg.upperLegLength = totalLength * upperRatio;
        leg.lowerLegLength = totalLength - leg.upperLegLength;
        leg.reach = Mathf.Max(0.001f, totalLength);
        leg.manualReach = leg.reach;

        if (writeRuntimeDimensionsToNodeStateLengths)
        {
            SetNodeLengthMagnitude(knee, leg.upperLegLength, start.transform.position - knee.transform.position);
            SetNodeLengthMagnitude(tail, leg.lowerLegLength, knee.transform.position - tail.transform.position);
        }

        knee.MaxBendAngle = Mathf.Max(knee.MaxBendAngle, kneeDefaultBendAngle);
        knee.BendWeight = Mathf.Max(knee.BendWeight, 1f);

        if (leg.limbSolver != null)
        {
            leg.limbSolver.captureBoneLengthsOnInitialize = false;
            leg.limbSolver.InitializeChainData();
        }
    }

    private void SetNodeLengthMagnitude(NodeState node, float length, Vector3 fallbackDirectionToNext)
    {
        if (node == null || node.next == null)
        {
            return;
        }

        Vector3 direction = node.Mylength.sqrMagnitude > Epsilon
            ? node.Mylength.normalized
            : node.transform.position - node.next.transform.position;

        if (direction.sqrMagnitude <= Epsilon)
        {
            direction = fallbackDirectionToNext.sqrMagnitude > Epsilon
                ? -fallbackDirectionToNext.normalized
                : Vector3.down;
        }

        node.Mylength = direction.normalized * Mathf.Max(0.001f, length);
    }

    private void AutoResolvePostCoreMoveIk()
    {
        Transform searchRoot = transform.root != null ? transform.root : transform;

        if (spineTargetSettersToRefreshAfterCoreMove == null ||
            spineTargetSettersToRefreshAfterCoreMove.Length == 0)
        {
            spineTargetSettersToRefreshAfterCoreMove =
                searchRoot.GetComponentsInChildren<SpineFakeTargetSetter>(true);
        }

        if (solversToSolveAfterCoreMove == null ||
            solversToSolveAfterCoreMove.Length == 0)
        {
            SpineFakeTargetSetter[] setters = spineTargetSettersToRefreshAfterCoreMove;

            if (setters != null && setters.Length > 0)
            {
                solversToSolveAfterCoreMove = new LimbSolver[setters.Length];

                for (int i = 0; i < setters.Length; i++)
                {
                    solversToSolveAfterCoreMove[i] = setters[i] != null
                        ? setters[i].limbSolver
                        : null;
                }
            }
        }

        if (!rebuildFullSpineSyncListAtRuntime &&
            ikNodesSyncedWithCoreDelta != null &&
            ikNodesSyncedWithCoreDelta.Length > 0)
        {
            return;
        }

        List<NodeState> mergedNodes = new List<NodeState>();
        if (ikNodesSyncedWithCoreDelta != null)
        {
            for (int i = 0; i < ikNodesSyncedWithCoreDelta.Length; i++)
            {
                NodeState node = ikNodesSyncedWithCoreDelta[i];
                if (node != null && !mergedNodes.Contains(node))
                {
                    mergedNodes.Add(node);
                }
            }
        }

        if (solversToSolveAfterCoreMove != null)
        {
            for (int i = 0; i < solversToSolveAfterCoreMove.Length; i++)
            {
                NodeState[] chainNodes = BuildSolverChainNodeArray(solversToSolveAfterCoreMove[i]);
                for (int j = 0; j < chainNodes.Length; j++)
                {
                    NodeState node = chainNodes[j];
                    if (node != null && !mergedNodes.Contains(node))
                    {
                        mergedNodes.Add(node);
                    }
                }
            }
        }

        if (mergedNodes.Count == 0)
        {
            SpineFakeTargetSetter firstSetter =
                spineTargetSettersToRefreshAfterCoreMove != null &&
                spineTargetSettersToRefreshAfterCoreMove.Length > 0
                    ? spineTargetSettersToRefreshAfterCoreMove[0]
                    : null;

            LimbSolver spineSolver = firstSetter != null
                ? firstSetter.limbSolver
                : null;

            if (spineSolver == null)
            {
                return;
            }

            mergedNodes.AddRange(BuildSolverChainNodeArray(spineSolver));
        }

        ikNodesSyncedWithCoreDelta = mergedNodes.ToArray();
    }

    private NodeState[] BuildSolverChainNodeArray(LimbSolver solver)
    {
        if (solver == null)
        {
            return Array.Empty<NodeState>();
        }

        List<NodeState> nodes = new List<NodeState>();
        NodeState current = solver.tail;
        int guard = 0;

        while (current != null && guard < MaxChainNodes)
        {
            guard++;

            if (!nodes.Contains(current))
            {
                nodes.Add(current);
            }

            if (current == solver.start)
            {
                break;
            }

            current = current.next;
        }

        if (solver.start != null && !nodes.Contains(solver.start))
        {
            nodes.Add(solver.start);
        }

        return nodes.ToArray();
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

    private void CaptureLegPoleOffsets(Leg leg, Vector3 normal, Vector3 forward, Vector3 side)
    {
        if (leg == null || leg.staticPole == null || coreNode == null)
        {
            return;
        }

        Vector3 fromCore = leg.staticPole.position - coreNode.position;
        leg.capturedPoleSideOffset = Vector3.Dot(fromCore, side);
        leg.capturedPoleForwardOffset = Vector3.Dot(fromCore, forward);
        leg.capturedPoleNormalOffset = Vector3.Dot(fromCore, normal);
    }

    private void CaptureLegTargetOrbitOffsets(Leg leg, Vector3 normal, Vector3 forward, Vector3 side)
    {
        if (leg == null)
        {
            return;
        }

        Transform orbitCore = ResolveLegOrbitCore(leg);
        if (orbitCore == null)
        {
            return;
        }

        CaptureTargetOrbitOffset(
            leg.fakeTarget,
            orbitCore,
            normal,
            forward,
            side,
            out leg.capturedFakeSideOffset,
            out leg.capturedFakeForwardOffset,
            out leg.capturedFakeNormalOffset
        );

        CaptureTargetOrbitOffset(
            leg.realTarget != null ? leg.realTarget : leg.fakeTarget,
            orbitCore,
            normal,
            forward,
            side,
            out leg.capturedRealSideOffset,
            out leg.capturedRealForwardOffset,
            out leg.capturedRealNormalOffset
        );
    }

    private void CaptureTargetOrbitOffset(
        Transform target,
        Transform orbitCore,
        Vector3 normal,
        Vector3 forward,
        Vector3 side,
        out float sideOffset,
        out float forwardOffset,
        out float normalOffset)
    {
        sideOffset = 0f;
        forwardOffset = 0f;
        normalOffset = 0f;

        if (target == null || orbitCore == null)
        {
            return;
        }

        Vector3 fromCore = target.position - orbitCore.position;
        sideOffset = Vector3.Dot(fromCore, side);
        forwardOffset = Mathf.Max(
            Vector3.Dot(fromCore, forward),
            GetMinimumForwardTargetOffset()
        );
        normalOffset = Vector3.Dot(fromCore, normal);
    }

    private void CaptureGaitForwardTarget(Vector3 normal)
    {
        if (gaitForwardTarget == null)
        {
            return;
        }

        Transform rotationCore = ResolveGaitRotationCore();
        if (rotationCore == null)
        {
            return;
        }

        Vector3 fromCore = gaitForwardTarget.position - rotationCore.position;
        Vector3 planar = Vector3.ProjectOnPlane(fromCore, normal);
        capturedGaitTargetRadius = planar.magnitude;
        capturedGaitTargetNormalOffset = Vector3.Dot(fromCore, normal);

        if (planar.sqrMagnitude > Epsilon)
        {
            externalGaitForward = planar.normalized;
        }

        capturedGaitReferenceForward = ResolveGaitReferenceForward(normal);
        hasCapturedGaitReferenceForward = capturedGaitReferenceForward.sqrMagnitude > Epsilon;
    }

    private void UpdateGaitForwardTarget(Vector3 normal, Vector3 forward)
    {
        if (!rotateGaitForwardTargetWithExternalForward || gaitForwardTarget == null)
        {
            return;
        }

        Transform rotationCore = ResolveGaitRotationCore();
        if (rotationCore == null)
        {
            return;
        }

        Vector3 safeForward = Vector3.ProjectOnPlane(forward, normal);
        if (safeForward.sqrMagnitude <= Epsilon)
        {
            return;
        }

        safeForward.Normalize();

        float radius = capturedGaitTargetRadius;
        if (radius <= Epsilon)
        {
            Vector3 currentPlanar = Vector3.ProjectOnPlane(gaitForwardTarget.position - rotationCore.position, normal);
            radius = Mathf.Max(currentPlanar.magnitude, 1f);
        }

        gaitForwardTarget.position =
            rotationCore.position +
            safeForward * radius +
            normal.normalized * capturedGaitTargetNormalOffset;
    }

    private void UpdateIdleLegTargetOrbits(Vector3 normal, Vector3 forward, Vector3 side)
    {
        if (!rotateLegTargetsWithGaitForward)
        {
            return;
        }

        UpdateIdleLegTargetOrbit(leftLeg, normal, forward, side);
        UpdateIdleLegTargetOrbit(rightLeg, normal, forward, side);
    }

    private void UpdateIdleLegTargetOrbit(Leg leg, Vector3 normal, Vector3 forward, Vector3 side)
    {
        if (leg == null || leg.isStepping)
        {
            return;
        }

        Transform orbitCore = ResolveLegOrbitCore(leg);
        if (orbitCore == null)
        {
            return;
        }

        Vector3 realPosition = CalculateOrbitPosition(
            orbitCore.position,
            normal,
            forward,
            side,
            leg.capturedRealSideOffset,
            leg.capturedRealForwardOffset,
            leg.capturedRealNormalOffset
        );

        Vector3 fakePosition = CalculateOrbitPosition(
            orbitCore.position,
            normal,
            forward,
            side,
            leg.capturedFakeSideOffset,
            leg.capturedFakeForwardOffset,
            leg.capturedFakeNormalOffset
        );

        if (forcePlantedFeetToGround)
        {
            realPosition = ProjectFootTargetToGround(realPosition, normal);
            fakePosition = ProjectFootTargetToGround(fakePosition, normal);
        }

        leg.plantedWorldPosition = fakePosition;
        WriteLegRealTarget(leg, realPosition);
        WriteLegIkTarget(leg, fakePosition);
    }

    private Vector3 CalculateOrbitPosition(
        Vector3 center,
        Vector3 normal,
        Vector3 forward,
        Vector3 side,
        float sideOffset,
        float forwardOffset,
        float normalOffset)
    {
        float safeForwardOffset = Mathf.Max(forwardOffset, GetMinimumForwardTargetOffset());

        return center +
               side * sideOffset +
               forward * safeForwardOffset +
               normal.normalized * normalOffset;
    }

    private float GetMinimumForwardTargetOffset()
    {
        return GetPairReach() * minimumForwardTargetOffsetRatio;
    }

    private Transform ResolveLegOrbitCore(Leg leg)
    {
        if (leg == null)
        {
            return coreNode;
        }

        if (leg.orbitCore != null)
        {
            return leg.orbitCore;
        }

        if (leg.useLimbStartAsOrbitCore && leg.limbSolver != null && leg.limbSolver.start != null)
        {
            return leg.limbSolver.start.transform;
        }

        return coreNode;
    }


    private void SnapGaitRotationCoreToBodyCore()
    {
        Transform rotationCore = ResolveGaitRotationCore();
        if (rotationCore == null || coreNode == null || rotationCore == coreNode)
        {
            return;
        }

        // The lower-body rotation anchor must live on the body core. If the prefab starts
        // with an arbitrary serialized offset, or an OffsetPositioningNode runs late, this
        // prevents the whole leg assembly from orbiting from a stale / displaced point.
        rotationCore.position = coreNode.position;
    }

    private Transform ResolveGaitRotationCore()
    {
        return gaitRotationCore != null ? gaitRotationCore : coreNode;
    }

    public void RefreshRotatedLegAssemblyAfterOffsetApply()
    {
        if (!initialized)
        {
            Initialize();
        }

        SnapGaitRotationCoreToBodyCore();
        ApplyOffsetPositionNow(gaitRotationAngleReference);
        ApplyOffsetPositionNow(forwardReference);

        Vector3 normal = GetMovementNormal();
        Vector3 forward = GetMovementForward(normal);
        if (forward.sqrMagnitude <= Epsilon)
        {
            forward = cachedFrameForward.sqrMagnitude > Epsilon ? cachedFrameForward.normalized : Vector3.forward;
        }

        DriveGaitRotationAssigner(forward, normal);
        ApplyGaitRotationOffsetNodesNow();
        UpdatePhysicalIkPolePosition(leftLeg, normal, forward);
        UpdatePhysicalIkPolePosition(rightLeg, normal, forward);
    }

    private bool HasAuthoritativeOffsetNode(Transform target)
    {
        if (target == null)
        {
            return false;
        }

        OffsetPositioningNode offsetNode = target.GetComponent<OffsetPositioningNode>();
        if (offsetNode != null && offsetNode.parentNode != null)
        {
            return true;
        }

        return target.GetComponent<RotatableNode>() != null;
    }

    private void ApplyStaticPoleAndPhysicalPolePositions(Vector3 normal, Vector3 forward)
    {
        Vector3 safeForward = Vector3.ProjectOnPlane(forward, normal);
        if (safeForward.sqrMagnitude <= Epsilon)
        {
            safeForward = GetMovementForward(normal);
        }

        if (safeForward.sqrMagnitude <= Epsilon)
        {
            safeForward = Vector3.forward;
        }

        safeForward.Normalize();
        Vector3 safeSide = Vector3.Cross(normal, safeForward);
        if (safeSide.sqrMagnitude <= Epsilon)
        {
            safeSide = Vector3.right;
        }
        safeSide.Normalize();

        UpdateStaticPolePositions(normal, safeForward, safeSide);
    }


    private void ApplyOffsetPositionNow(Transform target)
    {
        if (target == null)
        {
            return;
        }

        OffsetPositioningNode offsetNode = target.GetComponent<OffsetPositioningNode>();
        if (offsetNode != null)
        {
            offsetNode.ApplyPosition();
        }
    }

    private void ConfigureLowerBodyRotationNodes()
    {
        if (gaitRotationAssigner == null)
        {
            return;
        }

        Transform explicitCore = coreNode != null ? coreNode : ResolveGaitRotationCore();
        if (explicitCore != null)
        {
            gaitRotationAssigner.sharedCoreNode = explicitCore;
        }

        Transform pole = gaitRotationAngleReference != null
            ? gaitRotationAngleReference
            : forwardReference;
        if (pole != null)
        {
            gaitRotationAssigner.sharedPoleVector = pole;
        }

        gaitRotationAssigner.applyEveryUpdate = false;
        DisableDirectRotationAssignersThatFightGait();

        RotatableNodePair[] pairs = gaitRotationAssigner.nodePairs;
        if (pairs == null)
        {
            return;
        }

        for (int i = 0; i < pairs.Length; i++)
        {
            RotatableNodePair pair = pairs[i];
            if (pair == null)
            {
                continue;
            }

            if (explicitCore != null)
            {
                pair.coreNode = explicitCore;
            }

            if (pole != null)
            {
                pair.poleVector = pole;
            }

            pair.pushSettingsOnAwake = false;
            pair.reinitializeNodesOnStart = false;

            if (pair.nodes == null)
            {
                continue;
            }

            for (int j = 0; j < pair.nodes.Length; j++)
            {
                ConfigureLowerBodyRotatableNode(pair.nodes[j], explicitCore, pole);
            }
        }

        EnsureMainGaitPairContainsLowerBodyNodes(explicitCore, pole);
        DisableDuplicateLowerBodyRotationPairs(explicitCore, pole);
    }

    private void EnsureMainGaitPairContainsLowerBodyNodes(Transform explicitCore, Transform pole)
    {
        if (gaitRotationAssigner == null || gaitRotationAssigner.nodePairs == null || gaitRotationAssigner.nodePairs.Length == 0)
        {
            return;
        }

        RotatableNodePair mainPair = null;
        for (int i = 0; i < gaitRotationAssigner.nodePairs.Length; i++)
        {
            if (gaitRotationAssigner.nodePairs[i] != null && gaitRotationAssigner.nodePairs[i].isActiveAndEnabled)
            {
                mainPair = gaitRotationAssigner.nodePairs[i];
                break;
            }
        }

        if (mainPair == null)
        {
            mainPair = gaitRotationAssigner.nodePairs[0];
        }

        if (mainPair == null)
        {
            return;
        }

        List<RotatableNode> merged = new List<RotatableNode>();
        if (mainPair.nodes != null)
        {
            for (int i = 0; i < mainPair.nodes.Length; i++)
            {
                if (mainPair.nodes[i] != null && !merged.Contains(mainPair.nodes[i]))
                {
                    merged.Add(mainPair.nodes[i]);
                }
            }
        }

        AddLowerBodyRotatableNode(merged, GetLegStartTransform(leftLeg));
        AddLowerBodyRotatableNode(merged, GetLegStartTransform(rightLeg));
        AddLowerBodyRotatableNode(merged, leftLeg != null ? leftLeg.staticPole : null);
        AddLowerBodyRotatableNode(merged, rightLeg != null ? rightLeg.staticPole : null);

        mainPair.nodes = merged.ToArray();
        mainPair.coreNode = explicitCore;
        mainPair.poleVector = pole;
        mainPair.pushSettingsOnAwake = false;
        mainPair.reinitializeNodesOnStart = false;

        for (int i = 0; i < mainPair.nodes.Length; i++)
        {
            ConfigureLowerBodyRotatableNode(mainPair.nodes[i], explicitCore, pole);
        }
    }

    private void AddLowerBodyRotatableNode(List<RotatableNode> nodes, Transform target)
    {
        if (nodes == null || target == null)
        {
            return;
        }

        RotatableNode node = target.GetComponent<RotatableNode>();
        if (node != null && !nodes.Contains(node))
        {
            nodes.Add(node);
        }
    }

    private void DisableDirectRotationAssignersThatFightGait()
    {
        if (gaitRotationAssigner == null)
        {
            return;
        }

        DirectTargetRotationAssigner[] directAssigners = GetComponentsInChildren<DirectTargetRotationAssigner>(true);
        for (int i = 0; i < directAssigners.Length; i++)
        {
            DirectTargetRotationAssigner direct = directAssigners[i];
            if (direct == null || !direct.enabled)
            {
                continue;
            }

            if (direct.rotationAssigner == gaitRotationAssigner ||
                direct.transform == gaitRotationAssigner.transform)
            {
                direct.enabled = false;
            }
        }
    }

    private void DisableDuplicateLowerBodyRotationPairs(Transform explicitCore, Transform pole)
    {
        RotatableNodePair[] allPairs = GetComponentsInChildren<RotatableNodePair>(true);
        if (allPairs == null)
        {
            return;
        }

        for (int i = 0; i < allPairs.Length; i++)
        {
            RotatableNodePair pair = allPairs[i];
            if (pair == null || IsGaitRotationPair(pair) || !PairTouchesLowerBody(pair))
            {
                continue;
            }

            // Duplicate lower-body pairs were overriding the same leg start/pole nodes with
            // different cores/poles. Disable them and normalize their node settings so they
            // cannot fight the LegCore rotation assigner again after prefab reserialization.
            pair.enabled = false;
            pair.pushSettingsOnAwake = false;
            pair.reinitializeNodesOnStart = false;

            if (explicitCore != null)
            {
                pair.coreNode = explicitCore;
            }

            if (pole != null)
            {
                pair.poleVector = pole;
            }

            if (pair.nodes == null)
            {
                continue;
            }

            for (int j = 0; j < pair.nodes.Length; j++)
            {
                ConfigureLowerBodyRotatableNode(pair.nodes[j], explicitCore, pole);
            }
        }
    }

    private bool IsGaitRotationPair(RotatableNodePair pair)
    {
        if (gaitRotationAssigner == null || gaitRotationAssigner.nodePairs == null)
        {
            return false;
        }

        for (int i = 0; i < gaitRotationAssigner.nodePairs.Length; i++)
        {
            if (gaitRotationAssigner.nodePairs[i] == pair)
            {
                return true;
            }
        }

        return false;
    }

    private bool PairTouchesLowerBody(RotatableNodePair pair)
    {
        if (pair == null || pair.nodes == null)
        {
            return false;
        }

        for (int i = 0; i < pair.nodes.Length; i++)
        {
            RotatableNode node = pair.nodes[i];
            if (node == null)
            {
                continue;
            }

            Transform target = node.currentNode != null
                ? node.currentNode.transform
                : node.transform;

            if (IsLowerBodyRotatableTarget(target))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsLowerBodyRotatableTarget(Transform target)
    {
        if (target == null)
        {
            return false;
        }

        return (leftLeg != null && target == leftLeg.staticPole) ||
               (rightLeg != null && target == rightLeg.staticPole) ||
               target == GetLegStartTransform(leftLeg) ||
               target == GetLegStartTransform(rightLeg);
    }

    private void ConfigureLowerBodyRotatableNode(RotatableNode node, Transform explicitCore, Transform pole)
    {
        if (node == null)
        {
            return;
        }

        if (node.currentNode == null)
        {
            node.currentNode = node.GetComponent<OffsetPositioningNode>();
        }

        if (explicitCore != null)
        {
            node.coreNode = explicitCore;
        }

        if (pole != null)
        {
            node.poleVector = pole;
        }

        // The lower-body nodes must rotate around Node (4), not around their offset parent.
        // Leg poles in the prefab are parented to leg starts, so the old "use offset parent"
        // behavior made the local poles orbit their hips instead of the body center.
        node.useOffsetParentAsRotationCore = false;
        node.applyEveryUpdate = false;
        node.applyCurrentNodeImmediately = true;
        node.initializeOnStart = false;

        // First put the target in its current static-offset pose, then capture that pose as
        // the unrotated radius from Node (4). Without this, a stale prefab/startup transform
        // can be captured and every later yaw appears to do nothing or orbit from an offset.
        if (node.currentNode != null)
        {
            node.currentNode.ApplyPosition();
        }

        node.InitializeFromCurrentPose();
    }

    private void ApplyGaitRotationOffsetNodesNow()
    {
        ApplyGaitRotationOffsetNodesNow(float.NaN);
    }

    private void ApplyGaitRotationOffsetNodesNow(float forcedYawDegrees)
    {
        float clampedYaw = 0f;
        if (gaitRotationAssigner != null)
        {
            clampedYaw = float.IsNaN(forcedYawDegrees)
                ? gaitRotationAssigner.inputRotationDegrees
                : forcedYawDegrees;
        }
        else if (!float.IsNaN(forcedYawDegrees))
        {
            clampedYaw = forcedYawDegrees;
        }
        else
        {
            return;
        }

        if (gaitRotationAssigner == null || gaitRotationAssigner.nodePairs == null)
        {
            if (hardApplyLowerBodyYawOffsets && coreNode != null)
            {
                HardApplyLowerBodyYaw(clampedYaw, GetMovementNormal());
            }
            return;
        }

        RotatableNodePair[] pairs = gaitRotationAssigner.nodePairs;
        int activePairIndex = 0;
        int activePairCount = CountActiveGaitRotationPairs(pairs);

        for (int i = 0; i < pairs.Length; i++)
        {
            RotatableNodePair pair = pairs[i];
            if (pair == null || !pair.isActiveAndEnabled || pair.nodes == null)
            {
                continue;
            }

            float pairYaw = activePairCount > 0
                ? clampedYaw * ((float)(activePairIndex + 1) / activePairCount)
                : clampedYaw;
            activePairIndex++;

            if (pair.coreNode == null && coreNode != null)
            {
                pair.coreNode = coreNode;
            }

            if (pair.poleVector == null)
            {
                pair.poleVector = gaitRotationAngleReference != null ? gaitRotationAngleReference : forwardReference;
            }

            for (int j = 0; j < pair.nodes.Length; j++)
            {
                RotatableNode node = pair.nodes[j];
                if (node == null)
                {
                    continue;
                }

                ConfigureLowerBodyRotatableNodeReferencesOnly(node);
                node.SetLocalRotationDegrees(pairYaw);
                node.ApplyRotationOffset();

                if (node.currentNode != null)
                {
                    node.currentNode.ApplyPosition();
                }

                if (forceLowerBodyRotatedWorldPositions && IsLowerBodyRotatableTarget(node.currentNode != null ? node.currentNode.transform : node.transform))
                {
                    Transform target = node.currentNode != null ? node.currentNode.transform : node.transform;
                    target.position = node.CalculateRotatedWorldPosition();
                }
            }
        }

        if (hardApplyLowerBodyYawOffsets && coreNode != null)
        {
            HardApplyLowerBodyYaw(clampedYaw, GetMovementNormal());
        }
    }

    private void HardApplyLowerBodyYaw(float yawDegrees, Vector3 normal)
    {
        if (coreNode == null)
        {
            return;
        }

        Vector3 safeNormal = normal.sqrMagnitude > Epsilon ? normal.normalized : Vector3.up;
        Quaternion yaw = Quaternion.AngleAxis(RotatableNode.NormalizeSignedDegrees(yawDegrees), safeNormal);

        Transform leftStart = GetLegStartTransform(leftLeg);
        Transform rightStart = GetLegStartTransform(rightLeg);

        HardSetRotatableYaw(leftStart, yawDegrees);
        HardSetRotatableYaw(rightStart, yawDegrees);
        HardSetRotatableYaw(leftLeg != null ? leftLeg.staticPole : null, yawDegrees);
        HardSetRotatableYaw(rightLeg != null ? rightLeg.staticPole : null, yawDegrees);

        HardApplyOffsetNodeYaw(leftStart, coreNode, yaw, 1);
        HardApplyOffsetNodeYaw(rightStart, coreNode, yaw, 1);

        // Pole offset nodes are authored relative to the leg starts. Re-read the start positions
        // after applying their yaw before evaluating pole dynamic offsets so the lower-body
        // dependency order cannot leave poles/hips one frame apart.
        HardApplyOffsetNodeYaw(leftLeg != null ? leftLeg.staticPole : null, leftStart, yaw, 1);
        HardApplyOffsetNodeYaw(rightLeg != null ? rightLeg.staticPole : null, rightStart, yaw, 1);
    }

    private void HardSetRotatableYaw(Transform target, float yawDegrees)
    {
        if (target == null)
        {
            return;
        }

        RotatableNode node = target.GetComponent<RotatableNode>();
        if (node == null)
        {
            return;
        }

        ConfigureLowerBodyRotatableNodeReferencesOnly(node);
        node.SetLocalRotationDegrees(yawDegrees);
    }

    private void HardApplyOffsetNodeYaw(Transform target, Transform parent, Quaternion yaw, int dynamicOffsetId)
    {
        if (target == null || parent == null)
        {
            return;
        }

        OffsetPositioningNode offsetNode = target.GetComponent<OffsetPositioningNode>();
        if (offsetNode == null)
        {
            return;
        }

        // The authored static offset is the unrotated local assembly position. Rotate that
        // offset around its intended parent and write the delta as the same dynamic offset ID
        // the rotatable nodes use. This bypasses prefab cases where RotatableNode state exists
        // but no visible dynamic offset is produced.
        Vector3 staticOffset = offsetNode.GetAppliedStaticOffset();
        Vector3 rotatedOffset = yaw * staticOffset;
        offsetNode.SetDynamicOffset(dynamicOffsetId, rotatedOffset - staticOffset);
        offsetNode.ApplyPosition();
        target.position = parent.position + rotatedOffset + offsetNode.GetTotalDynamicOffsetExcluding(dynamicOffsetId);
    }

    private int CountActiveGaitRotationPairs(RotatableNodePair[] pairs)
    {
        if (pairs == null)
        {
            return 0;
        }

        int count = 0;
        for (int i = 0; i < pairs.Length; i++)
        {
            if (pairs[i] != null && pairs[i].isActiveAndEnabled)
            {
                count++;
            }
        }

        return count;
    }

    private void ConfigureLowerBodyRotatableNodeReferencesOnly(RotatableNode node)
    {
        if (node == null)
        {
            return;
        }

        if (node.currentNode == null)
        {
            node.currentNode = node.GetComponent<OffsetPositioningNode>();
        }

        if (coreNode != null)
        {
            node.coreNode = coreNode;
        }

        Transform pole = gaitRotationAngleReference != null
            ? gaitRotationAngleReference
            : forwardReference;
        if (pole != null)
        {
            node.poleVector = pole;
        }

        node.useOffsetParentAsRotationCore = false;
        node.applyEveryUpdate = false;
        node.applyCurrentNodeImmediately = true;
        node.initializeOnStart = false;
    }

    private void ForceFrontLegPoles()
    {
        ForceFrontLegPole(leftLeg);
        ForceFrontLegPole(rightLeg);
    }

    private void ForceFrontLegPole(Leg leg)
    {
        if (leg == null)
        {
            return;
        }

        // Physical solver pole and gait/measurement pole are the same front pole again.
        leg.flipPhysicalIkPoleBehindStaticPole = false;
        leg.physicalIkPole = leg.staticPole;

        if (leg.staticPole == null)
        {
            return;
        }

        OffsetPositioningNode offsetNode = leg.staticPole.GetComponent<OffsetPositioningNode>();
        if (offsetNode == null)
        {
            return;
        }

        Vector3 offset = offsetNode.staticOffset;
        if (offset.z < 0f)
        {
            offset.z = Mathf.Abs(offset.z);
            offsetNode.staticOffset = offset;
            offsetNode.ApplyPosition();
        }
    }

    private void UpdateStaticPolePositions(Vector3 normal, Vector3 forward, Vector3 side)
    {
        if (coreNode == null)
        {
            return;
        }

        if (rotateStaticPolesWithGaitForward)
        {
            UpdateStaticPolePosition(leftLeg, normal, forward, side);
            UpdateStaticPolePosition(rightLeg, normal, forward, side);
        }

        UpdatePhysicalIkPolePosition(leftLeg, normal, forward);
        UpdatePhysicalIkPolePosition(rightLeg, normal, forward);
    }

    private void UpdateStaticPolePosition(Leg leg, Vector3 normal, Vector3 forward, Vector3 side)
    {
        if (leg == null || leg.staticPole == null)
        {
            return;
        }

        if (authoritativeOffsetNodesForRotatedLegAssembly && HasAuthoritativeOffsetNode(leg.staticPole))
        {
            return;
        }

        leg.staticPole.position =
            coreNode.position +
            side * leg.capturedPoleSideOffset +
            forward * leg.capturedPoleForwardOffset +
            normal * leg.capturedPoleNormalOffset;
    }

    private Transform EnsurePhysicalIkPole(Leg leg)
    {
        if (leg == null || !leg.flipPhysicalIkPoleBehindStaticPole)
        {
            return leg != null ? leg.staticPole : null;
        }

        if (leg.physicalIkPole == null && leg.staticPole != null && Application.isPlaying)
        {
            GameObject poleObject = new GameObject($"{leg.label} Physical IK Pole");
            poleObject.hideFlags = HideFlags.DontSave;
            poleObject.transform.SetParent(transform, true);
            poleObject.transform.position = leg.staticPole.position;
            leg.physicalIkPole = poleObject.transform;
        }

        return leg.physicalIkPole != null ? leg.physicalIkPole : leg.staticPole;
    }

    private Transform GetSolverPole(Leg leg)
    {
        if (leg == null)
        {
            return null;
        }

        if (leg.flipPhysicalIkPoleBehindStaticPole)
        {
            Transform physicalPole = EnsurePhysicalIkPole(leg);
            if (physicalPole != null)
            {
                return physicalPole;
            }
        }

        return leg.staticPole;
    }

    private void UpdatePhysicalIkPolePosition(Leg leg, Vector3 normal, Vector3 fallbackForward)
    {
        if (leg == null || !leg.flipPhysicalIkPoleBehindStaticPole || leg.staticPole == null)
        {
            return;
        }

        Transform physicalPole = EnsurePhysicalIkPole(leg);
        if (physicalPole == null || physicalPole == leg.staticPole)
        {
            return;
        }

        Transform start = GetLegStartTransform(leg);
        Vector3 origin = start != null
            ? start.position
            : coreNode != null
                ? coreNode.position
                : transform.position;

        Vector3 safeNormal = normal.sqrMagnitude > Epsilon
            ? normal.normalized
            : Vector3.up;

        Vector3 frontDirection = Vector3.ProjectOnPlane(leg.staticPole.position - origin, safeNormal);
        float distance = leg.physicalIkPoleDistance > 0f
            ? leg.physicalIkPoleDistance
            : frontDirection.magnitude;

        if (frontDirection.sqrMagnitude <= Epsilon)
        {
            frontDirection = Vector3.ProjectOnPlane(fallbackForward, safeNormal);
        }

        if (frontDirection.sqrMagnitude <= Epsilon)
        {
            frontDirection = GetLegForwardDirection(leg, safeNormal, fallbackForward);
        }

        if (frontDirection.sqrMagnitude <= Epsilon)
        {
            frontDirection = Vector3.forward;
        }

        float normalOffset = Vector3.Dot(leg.staticPole.position - origin, safeNormal);
        // Fourth pass: flip the physical IK pole to the opposite side from the previous build.
        // The static/front pole remains the gait measurement reference; this physical pole is
        // only what the IK chain consumes for bend direction.
        physicalPole.position = origin + frontDirection.normalized * Mathf.Max(0.05f, distance) + safeNormal * normalOffset;
    }

    private void InitializeFootTargets(Leg leg, Vector3 normal, Vector3 fallbackForward)
    {
        if (leg == null || leg.fakeTarget == null)
        {
            return;
        }

        Vector3 position = placeTargetsFromLegStarts
            ? CalculateLegTargetInFrontOfStart(leg, normal, fallbackForward, 0f)
            : leg.fakeTarget.position;

        if (forcePlantedFeetToGround)
        {
            position = ProjectFootTargetToGround(position, normal);
        }

        leg.plantedWorldPosition = position;
        WriteLegRealTarget(leg, position);
        ResetLegFakeTargetFilter(leg, position);
        WriteLegIkTarget(leg, position);

        leg.isStepping = false;
        leg.stepTimer = 0f;
    }

    private void MaintainPlantedLeg(Leg leg, Vector3 normal)
    {
        if (leg == null || leg.isStepping)
        {
            return;
        }

        Vector3 planted = leg.plantedWorldPosition;
        if (planted.sqrMagnitude <= Epsilon && leg.realTarget != null)
        {
            planted = leg.realTarget.position;
        }

        if (forcePlantedFeetToGround)
        {
            Vector3 grounded = ProjectFootTargetToGround(planted, normal);
            if (Vector3.Distance(grounded, planted) > plantedGroundSnapTolerance)
            {
                planted = grounded;
                leg.plantedWorldPosition = planted;
                WriteLegRealTarget(leg, planted);
            }
        }

        Vector3 lift = useAirbornePose
            ? normal.normalized * currentAirbornePoseLift
            : Vector3.zero;

        WriteLegIkTarget(leg, planted + lift);
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
        if (leg != null && leg.startNode != null)
        {
            return leg.startNode.transform;
        }

        if (leg != null && leg.limbSolver != null && leg.limbSolver.start != null)
        {
            return leg.limbSolver.start.transform;
        }

        return coreNode;
    }

    private void AssignStaticPole(Leg leg)
    {
        if (leg == null)
        {
            return;
        }

        Transform solverPole = GetSolverPole(leg);
        if (solverPole == null)
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

        if (leg.limbSolver != null)
        {
            leg.limbSolver.fallbackPole = solverPole;
        }

        NodeState current = tail;
        int guard = 0;

        while (current != null && guard < MaxChainNodes)
        {
            guard++;

            current.pole = solverPole;

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
        WriteLegIkTarget(leg, worldPosition, true);
    }

    private void WriteLegIkTarget(Leg leg, Vector3 worldPosition, bool clampToCurrentReach)
    {
        if (leg == null)
        {
            return;
        }

        Vector3 targetWorldPosition = clampToCurrentReach
            ? ClampLegTargetToReach(leg, worldPosition)
            : worldPosition;
        Vector3 outputWorldPosition = ResolveLazyLegFakeTargetPosition(leg, targetWorldPosition);

        // If this is an active swing and the caller explicitly preserved the planned forward
        // arc, do not let the lazy filter's final reach clamp undo that preservation.
        if (!clampToCurrentReach && allowActiveSwingTargetPastCurrentReach && preserveCommittedSwingEndpointDuringStep)
        {
            outputWorldPosition = targetWorldPosition;
            ResetLegFakeTargetFilter(leg, outputWorldPosition);
        }

        WriteLegFakeTarget(leg, outputWorldPosition);

        if (leg.tailNode != null)
        {
            leg.tailNode.transform.position = outputWorldPosition;
        }

        if (solveLegAfterTargetWrite && leg.limbSolver != null)
        {
            leg.limbSolver.Apply();
        }
    }

    private Vector3 ResolveLazyLegFakeTargetPosition(Leg leg, Vector3 desiredWorldPosition)
    {
        if (leg == null)
        {
            return desiredWorldPosition;
        }

        if (!lazyFollowLegFakeTargets)
        {
            ResetLegFakeTargetFilter(leg, desiredWorldPosition);
            return desiredWorldPosition;
        }

        if (snapFakeTargetDuringActiveSteps && leg.isStepping)
        {
            ResetLegFakeTargetFilter(leg, desiredWorldPosition);
            return desiredWorldPosition;
        }

        if (!leg.lazyFakeTargetInitialized)
        {
            ResetLegFakeTargetFilter(leg, desiredWorldPosition);
            return desiredWorldPosition;
        }

        float dt = movedCoreThisFrame && cachedFrameDeltaTime > Epsilon
            ? cachedFrameDeltaTime
            : Time.deltaTime;

        if (dt <= Epsilon)
        {
            return leg.lazyFakeTargetWorld;
        }

        bool activeStep = leg.isStepping;
        float distanceToDesired = Vector3.Distance(leg.lazyFakeTargetWorld, desiredWorldPosition);
        float usableReach = GetUsableLegReach(leg);
        float settleDistance = Mathf.Max(
            legFakeTargetSettleDistance,
            usableReach * stationaryFakeTargetSettleReachRatio,
            usableReach * idleFakeTargetDeadZoneReachRatio
        );
        if (!activeStep && distanceToDesired <= settleDistance)
        {
            // Close-enough idle feet are exactly planted. Returning the old smoothed value
            // leaves them hovering/bobbing around a target they have effectively reached.
            ResetLegFakeTargetFilter(leg, desiredWorldPosition);
            return desiredWorldPosition;
        }

        float speed01 = GetSpeed01();
        float dynamicFrequency = legFakeTargetFrequencyHz +
                                 legFakeTargetSpeedFrequencyBoostHz * speed01;
        float dynamicDamping = legFakeTargetDampingRatio +
                               legFakeTargetSpeedDampingBoost * speed01;
        float dynamicMaxSpeed = maxLegFakeTargetSpeed > 0f
            ? maxLegFakeTargetSpeed
            : Mathf.Max(runSpeed, currentCoreVelocity.magnitude, 0.01f) *
              Mathf.Max(0f, dynamicLegFakeTargetSpeedMultiplier);
        float dynamicMaxAcceleration = maxLegFakeTargetAcceleration > 0f
            ? maxLegFakeTargetAcceleration
            : Mathf.Max(runSpeed, currentCoreVelocity.magnitude, 0.01f) *
              dynamicMaxSpeed *
              Mathf.Max(0f, dynamicLegFakeTargetAccelerationMultiplier);

        if (activeStep)
        {
            dynamicFrequency *= activeStepFakeTargetFrequencyMultiplier;
            dynamicMaxSpeed *= activeStepFakeTargetSpeedMultiplier;
            dynamicMaxAcceleration *= activeStepFakeTargetAccelerationMultiplier;
        }

        leg.lazyFakeTargetWorld = StepSecondOrderPosition(
            leg.lazyFakeTargetWorld,
            desiredWorldPosition,
            ref leg.lazyFakeTargetVelocity,
            dynamicFrequency,
            dynamicDamping,
            dt,
            dynamicMaxAcceleration,
            dynamicMaxSpeed,
            maxLegFakeTargetSubstepTime
        );

        leg.lazyFakeTargetWorld = LimitFakeTargetLag(
            leg,
            leg.lazyFakeTargetWorld,
            desiredWorldPosition,
            dt
        );

        leg.lazyFakeTargetWorld = ClampLegTargetToReach(leg, leg.lazyFakeTargetWorld);
        return leg.lazyFakeTargetWorld;
    }

    private Vector3 LimitFakeTargetLag(Leg leg, Vector3 current, Vector3 desired, float dt)
    {
        if (leg == null || dt <= Epsilon || maxFakeTargetLagReachRatio <= 0f)
        {
            return current;
        }

        float maxLag = Mathf.Max(0.001f, GetUsableLegReach(leg) * maxFakeTargetLagReachRatio);
        Vector3 lag = desired - current;
        float distance = lag.magnitude;

        if (distance <= maxLag)
        {
            return current;
        }

        Vector3 cappedPosition = desired - lag.normalized * maxLag;
        float reachBasedCatchup = Mathf.Max(0.001f, GetUsableLegReach(leg)) *
                                   Mathf.Max(0f, fakeTargetLagCatchupReachPerSecond);
        float speedBasedCatchup = Mathf.Max(0f, currentCoreVelocity.magnitude) *
                                  Mathf.Max(0f, fakeTargetLagCatchupCoreSpeedMultiplier);
        float catchupDistance = Mathf.Max(reachBasedCatchup, speedBasedCatchup) * dt;

        return Vector3.MoveTowards(current, cappedPosition, catchupDistance);
    }

    private void ResetLegFakeTargetFilter(Leg leg, Vector3 worldPosition)
    {
        if (leg == null)
        {
            return;
        }

        leg.lazyFakeTargetWorld = worldPosition;
        leg.lazyFakeTargetVelocity = Vector3.zero;
        leg.lazyFakeTargetInitialized = true;
    }

    private Vector3 StepSecondOrderPosition(
        Vector3 current,
        Vector3 target,
        ref Vector3 velocity,
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

            Vector3 acceleration =
                stiffness * (target - current)
                - damping * velocity;

            if (maxAcceleration > 0f && acceleration.magnitude > maxAcceleration)
            {
                acceleration = acceleration.normalized * maxAcceleration;
            }

            velocity += acceleration * step;

            if (maxSpeed > 0f && velocity.magnitude > maxSpeed)
            {
                velocity = velocity.normalized * maxSpeed;
            }

            current += velocity * step;
        }

        return current;
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
