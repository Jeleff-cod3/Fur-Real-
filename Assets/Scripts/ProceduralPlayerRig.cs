using System;
using UnityEngine;

[DefaultExecutionOrder(-140)]
public sealed class ProceduralPlayerRig : MonoBehaviour
{
    private const float Epsilon = 0.0001f;
    private const int HeldArmOffsetId = 100;
    private const int ThrowArmOffsetId = 101;

    [Header("Runtime Targets")]
    [SerializeField] private Transform coreNode;
    [SerializeField] private Transform runTarget;
    [SerializeField] private Transform aimTarget;
    [SerializeField] private Transform weaponHolder;
    [SerializeField] private Transform itemHolder;

    [Header("Visual Scale")]
    [SerializeField] private float desiredVisualHeight = 1f;
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

    private AutoRunLegPairController legController;
    private AutoRunMovementInput movementInput;
    private DirectTargetRotationAssigner[] rotationAssigners = Array.Empty<DirectTargetRotationAssigner>();
    private SpineFakeTargetSetter[] spineTargetSetters = Array.Empty<SpineFakeTargetSetter>();
    private LazyIKTargetSetter[] armTargetSetters = Array.Empty<LazyIKTargetSetter>();
    private ArmBinding[] arms = Array.Empty<ArmBinding>();

    private Vector3 previousCorePosition;
    private bool hasPreviousCorePosition;
    private float throwWindupUntil;
    private bool isLocalRig;
    private bool hasAppliedAuthoredScale;

    public Transform CoreNode => coreNode != null ? coreNode : transform;
    public Transform RunTarget => runTarget;
    public Transform AimTarget => aimTarget;
    public Transform WeaponHolder => weaponHolder;
    public Transform ItemHolder => itemHolder;
    public bool HasLegController => legController != null;
    public Vector3 Velocity { get; private set; }
    public int ActionSequence { get; private set; }
    public string ActionState { get; private set; } = "idle";
    public Vector3 LeftArmTargetWorld => arms.Length > 0 ? arms[0].CurrentTargetWorld : CoreNode.position;
    public Vector3 RightArmTargetWorld => arms.Length > 1 ? arms[1].CurrentTargetWorld : LeftArmTargetWorld;

    private void Awake()
    {
        ResolveReferences();
    }

    private void LateUpdate()
    {
        UpdateVelocity();
        UpdateHolders();
        LateUpdateFootGrounding();
        RecenterRootOnCore();

        if (Time.time >= throwWindupUntil && ActionState == "throw_windup")
        {
            ActionState = "idle";
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

        FitVisualsToCubeHeight();
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

        if (autoScaleAuthoredRigToCube && !hasAppliedAuthoredScale)
        {
            ScaleAuthoredRigToCubeHeight();
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

    public void SetAimTarget(Vector3 worldPosition)
    {
        ResolveReferences();

        if (aimTarget != null)
        {
            aimTarget.position = worldPosition;
        }

        for (int i = 0; i < rotationAssigners.Length; i++)
        {
            if (rotationAssigners[i] != null)
            {
                rotationAssigners[i].SetExternalTargetWorldPosition(worldPosition);
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
        ResolveReferences();

        bool isThrowing = Time.time < throwWindupUntil;
        ActionState = isThrowing ? "throw_windup" : (isHolding ? "holding" : "idle");

        for (int i = 0; i < arms.Length; i++)
        {
            arms[i].ApplyPoseOffset(isHolding, isThrowing, heldCenterPull, heldLiftOffset, throwBackOffset);
        }
    }

    public void PlayThrowWindup(float duration, Vector3 throwDirection)
    {
        throwWindupUntil = Time.time + Mathf.Max(0f, duration);
        ActionState = "throw_windup";
        ActionSequence++;

        if (throwDirection.sqrMagnitude > Epsilon)
        {
            SetAimTarget(CoreNode.position + throwDirection.normalized * 4f);
        }
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
        ScaleLegControllerValues(authoredValueScale);
        ScaleArmAndSpineValues(authoredValueScale);
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
            offsetNodes[i].debugLogging = false;
        }
    }

    private void ScaleLegControllerValues(float scale)
    {
        if (legController == null)
        {
            return;
        }

        legController.stopDistance *= scale;
        legController.slowDownRadius *= scale;
        legController.coreGroundRayHeight *= scale;
        legController.coreGroundRayDistance *= scale;
        legController.coreGroundOffset *= scale;
        legController.footRayHeight *= scale;
        legController.footRayDistance *= scale;
        legController.footGroundOffset *= scale;
        legController.legReachSafetyPadding *= scale;

        legController.leftLeg.manualReach *= scale;
        legController.rightLeg.manualReach *= scale;
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
            setter.minimumPoleDistance *= scale;
        }
    }

    private void LateUpdateFootGrounding()
    {
        if (!keepFeetAboveGround || legController == null)
        {
            return;
        }

        Vector3 leftFoot = GetLegTailPosition(legController.leftLeg);
        Vector3 rightFoot = GetLegTailPosition(legController.rightLeg);

        if (!TryGetGroundHeightBelow(CoreNode.position, out float groundY))
        {
            return;
        }

        float lowestFootY = Mathf.Min(leftFoot.y, rightFoot.y);
        float minFootY = groundY + feetGroundClearance;
        float lift = minFootY - lowestFootY;

        if (lift <= 0f)
        {
            return;
        }

        CoreNode.position += Vector3.up * lift;
        if (runTarget != null)
        {
            runTarget.position += Vector3.up * lift;
        }
        if (aimTarget != null)
        {
            aimTarget.position += Vector3.up * lift;
        }
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
        if (legController == null)
        {
            legController = GetComponentInChildren<AutoRunLegPairController>(true);
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
        spineTargetSetters = GetComponentsInChildren<SpineFakeTargetSetter>(true);

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
        Vector3 aimDirection = aimTarget != null ? aimTarget.position - midpoint : CoreNode.forward;
        aimDirection.y = 0f;

        if (aimDirection.sqrMagnitude <= Epsilon)
        {
            aimDirection = CoreNode.forward;
        }

        Quaternion holderRotation = Quaternion.LookRotation(aimDirection.normalized, Vector3.up);

        if (weaponHolder != null)
        {
            weaponHolder.position = midpoint;
            weaponHolder.rotation = holderRotation;
        }

        if (itemHolder != null)
        {
            itemHolder.position = midpoint;
            itemHolder.rotation = holderRotation;
        }
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
        private readonly float sideSign;

        public ArmBinding(LazyIKTargetSetter setter, Transform core, float fallbackSideSign)
        {
            this.setter = setter;
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
            bool isHolding,
            bool isThrowing,
            float centerPull,
            Vector3 heldLiftOffset,
            Vector3 throwBackOffset)
        {
            if (offsetNode == null)
            {
                return;
            }

            Vector3 heldOffset = isHolding
                ? new Vector3(-sideSign * centerPull, heldLiftOffset.y, heldLiftOffset.z)
                : Vector3.zero;

            Vector3 windupOffset = isThrowing ? throwBackOffset : Vector3.zero;
            offsetNode.SetDynamicOffset(HeldArmOffsetId, heldOffset);
            offsetNode.SetDynamicOffset(ThrowArmOffsetId, windupOffset);
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
    }
}
