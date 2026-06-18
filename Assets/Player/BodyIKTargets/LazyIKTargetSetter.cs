using UnityEngine;

[DefaultExecutionOrder(-120)]
public class LazyIKTargetSetter : MonoBehaviour
{
    private const float Epsilon = 0.000001f;
    private const int MaxChainNodes = 256;

    public enum MaxReachSource
    {
        Manual,
        NodeStateChain,
        LimbSolverCumulativeBones
    }

    public enum MinReachSource
    {
        None,
        Manual,
        InitialFakeTargetDistanceFromCore
    }

    public enum FollowMode
    {
        LazyTriggered,
        ContinuousSecondOrder,
        DirectSnap
    }

    [Header("Main References")]
    [Tooltip("Usually the shoulder / limb start / IK start node.")]
    public Transform coreNode;

    [Tooltip("The clean desired target. This can be body-attached with your offset system.")]
    public Transform realTarget;

    [Tooltip("The actual fake IK target object that the IK limb follows.")]
    public Transform fakeTarget;

    [Tooltip("Optional IK tail node. If assigned, it is forced to the fake target position.")]
    public NodeState tailNode;

    [Tooltip("Optional solver. Used for auto references and reach reading.")]
    public LimbSolver limbSolver;

    [Header("Static Pole")]
    [Tooltip("Static pole for the limb. This script does not move it.")]
    public Transform staticPole;

    [Tooltip("If true, writes staticPole into the NodeState chain on Start.")]
    public bool assignStaticPoleToChainOnStart = true;

    [Header("Auto References")]
    public bool autoUseSolverStartAsCore = true;
    public bool autoUseSolverTailAsFakeTarget = true;
    public bool autoUseSolverTailAsTailNode = true;

    [Header("External Target")]
    public bool useExternalTargetPosition = false;
    public Vector3 externalTargetWorldPosition;

    [Header("Optional Offset Node Output")]
    [Tooltip("False = set fakeTarget.position directly. True = write dynamic offset into OffsetPositioningNode.")]
    public bool writeTargetThroughOffsetNode = false;

    public OffsetPositioningNode targetOffsetNode;
    public int targetDynamicOffsetId = 30;

    [Header("Reach")]
    public MaxReachSource maxReachSource = MaxReachSource.NodeStateChain;

    [Min(0.001f)]
    public float manualMaxReach = 3f;

    [Tooltip("Useful to keep the target slightly inside the true reach boundary.")]
    [Min(0f)]
    public float maxReachMultiplier = 0.98f;

    [Min(0f)]
    public float maxReachSafetyPadding = 0.02f;

    public MinReachSource minReachSource = MinReachSource.None;

    [Min(0f)]
    public float manualMinReach = 0f;

    [Min(0f)]
    public float minReachMultiplier = 1f;

    [Tooltip("Usually false for arms. True keeps the fake target at least min reach from the core.")]
    public bool enforceMinimumReach = false;

    [Header("Follow Trigger")]
    public FollowMode followMode = FollowMode.LazyTriggered;

    [Tooltip("Fake target starts moving only when it is farther than this from the projected real target.")]
    [Min(0f)]
    public float startFollowDistance = 0.08f;

    [Tooltip("Fake target stops following when it gets this close to the projected real target.")]
    [Min(0f)]
    public float stopFollowDistance = 0.025f;

    [Tooltip("Fake target also needs to slow below this speed before stopping.")]
    [Min(0f)]
    public float stopVelocity = 0.02f;

    [Tooltip("If true, the destination keeps updating while following. If false, it moves from point A to the target position captured when follow started.")]
    public bool updateDestinationWhileFollowing = true;

    [Header("Second Order Motion")]
    [Tooltip("Higher = tighter/faster response.")]
    [Min(0.01f)]
    public float frequencyHz = 3.0f;

    [Tooltip("1 = no overshoot, below 1 = overshoot/bounce, above 1 = heavy/damped.")]
    [Min(0f)]
    public float dampingRatio = 0.65f;

    [Tooltip("Optional predictive lead. Keep 0 for lazy trailing. Small values like 0.03 can make it anticipate moving targets.")]
    [Min(0f)]
    public float targetVelocityLeadTime = 0f;

    [Tooltip("0 means unlimited.")]
    [Min(0f)]
    public float maxAcceleration = 0f;

    [Tooltip("0 means unlimited.")]
    [Min(0f)]
    public float maxSpeed = 0f;

    [Tooltip("Substepping improves spring stability at low frame rates.")]
    [Min(0.001f)]
    public float maxSubstepTime = 1f / 90f;

    [Header("Initialization")]
    [Tooltip("If true, fake target starts exactly at the projected real target.")]
    public bool initializeFakeTargetAtRealTarget = false;

    [Tooltip("If true, the current fake target is clamped to reach on Start.")]
    public bool clampFakeTargetOnStart = true;

    [Header("Runtime")]
    public bool updateEveryFrame = true;

    [SerializeField]
    private bool isFollowing = false;

    [SerializeField]
    private Vector3 currentWorldPosition;

    [SerializeField]
    private Vector3 currentVelocity;

    [SerializeField]
    private Vector3 activeDestinationWorld;

    [SerializeField]
    private Vector3 lastProjectedRealTargetWorld;

    [SerializeField]
    private Vector3 lastRawRealTargetWorld;

    [SerializeField]
    private RuntimeDebugState debugState;

    public bool IsFollowing => isFollowing;
    public Vector3 CurrentVelocity => currentVelocity;
    public RuntimeDebugState DebugState => debugState;

    [System.Serializable]
    public struct RuntimeDebugState
    {
        public Vector3 rawRealTargetWorld;
        public Vector3 projectedRealTargetWorld;
        public Vector3 activeDestinationWorld;
        public Vector3 fakeTargetWorld;
        public Vector3 currentVelocity;

        public float distanceToProjectedTarget;
        public float maxReach;
        public float minReach;

        public bool isFollowing;
        public bool targetWasReachClamped;
    }

    private void Reset()
    {
        limbSolver = GetComponent<LimbSolver>();
    }

    private void Start()
    {
        ResolveReferences();

        if (assignStaticPoleToChainOnStart && staticPole != null)
        {
            AssignStaticPoleToChain();
        }

        InitializeFollowerState();
    }

    private void Update()
    {
        if (updateEveryFrame)
        {
            EvaluateAndApply(Time.deltaTime);
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

    [ContextMenu("Reset Fake Target To Real Target")]
    public void ResetFakeTargetToRealTarget()
    {
        ResolveReferences();

        if (coreNode == null || fakeTarget == null)
        {
            return;
        }

        Vector3 rawTarget = GetRawTargetWorldPosition();
        Vector3 projectedTarget = ProjectWorldPointIntoReach(rawTarget, out _);

        currentWorldPosition = projectedTarget;
        currentVelocity = Vector3.zero;
        activeDestinationWorld = projectedTarget;
        lastProjectedRealTargetWorld = projectedTarget;
        isFollowing = false;

        WriteFakeTarget(projectedTarget);

        if (tailNode != null)
        {
            tailNode.transform.position = projectedTarget;
        }
    }

    public bool EvaluateAndApply(float deltaTime)
    {
        ResolveReferences();

        if (coreNode == null || fakeTarget == null)
        {
            return false;
        }

        if (deltaTime <= 0f)
        {
            deltaTime = Time.deltaTime;
        }

        Vector3 rawTarget = GetRawTargetWorldPosition();

        bool wasReachClamped;
        Vector3 projectedTarget = ProjectWorldPointIntoReach(rawTarget, out wasReachClamped);

        Vector3 targetVelocity = deltaTime > Epsilon
            ? (projectedTarget - lastProjectedRealTargetWorld) / deltaTime
            : Vector3.zero;

        Vector3 targetForSpring =
            projectedTarget + targetVelocity * targetVelocityLeadTime;

        targetForSpring = ProjectWorldPointIntoReach(targetForSpring, out _);

        float distanceToProjectedTarget =
            Vector3.Distance(currentWorldPosition, projectedTarget);

        UpdateFollowState(
            distanceToProjectedTarget,
            projectedTarget,
            targetForSpring
        );

        if (followMode == FollowMode.DirectSnap)
        {
            currentWorldPosition = projectedTarget;
            currentVelocity = Vector3.zero;
            isFollowing = false;
        }
        else if (isFollowing || followMode == FollowMode.ContinuousSecondOrder)
        {
            Vector3 destination = updateDestinationWhileFollowing
                ? targetForSpring
                : activeDestinationWorld;

            currentWorldPosition = StepSecondOrder(
                currentWorldPosition,
                destination,
                deltaTime
            );

            currentWorldPosition = ProjectWorldPointIntoReach(
                currentWorldPosition,
                out _
            );

            if (followMode == FollowMode.LazyTriggered)
            {
                float remainingDistance =
                    Vector3.Distance(currentWorldPosition, projectedTarget);

                if (remainingDistance <= stopFollowDistance &&
                    currentVelocity.magnitude <= stopVelocity)
                {
                    isFollowing = false;
                    currentVelocity = Vector3.zero;
                    activeDestinationWorld = currentWorldPosition;
                }
            }
        }
        else
        {
            /*
             * Lazy idle:
             * keep fake target exactly where it currently is.
             * Do not creep around for tiny target changes.
             */
            currentVelocity = Vector3.zero;
        }

        WriteFakeTarget(currentWorldPosition);

        if (tailNode != null)
        {
            tailNode.transform.position = currentWorldPosition;
        }

        lastRawRealTargetWorld = rawTarget;
        lastProjectedRealTargetWorld = projectedTarget;

        debugState.rawRealTargetWorld = rawTarget;
        debugState.projectedRealTargetWorld = projectedTarget;
        debugState.activeDestinationWorld = activeDestinationWorld;
        debugState.fakeTargetWorld = currentWorldPosition;
        debugState.currentVelocity = currentVelocity;
        debugState.distanceToProjectedTarget = distanceToProjectedTarget;
        debugState.maxReach = GetMaxReach();
        debugState.minReach = GetMinReach(GetMaxReach());
        debugState.isFollowing = isFollowing;
        debugState.targetWasReachClamped = wasReachClamped;

        return true;
    }

    private void UpdateFollowState(
        float distanceToProjectedTarget,
        Vector3 projectedTarget,
        Vector3 targetForSpring
    )
    {
        if (followMode == FollowMode.ContinuousSecondOrder)
        {
            isFollowing = true;
            activeDestinationWorld = targetForSpring;
            return;
        }

        if (followMode == FollowMode.DirectSnap)
        {
            isFollowing = false;
            activeDestinationWorld = projectedTarget;
            return;
        }

        float startDistance = Mathf.Max(startFollowDistance, stopFollowDistance);

        if (!isFollowing && distanceToProjectedTarget > startDistance)
        {
            isFollowing = true;
            activeDestinationWorld = updateDestinationWhileFollowing
                ? targetForSpring
                : projectedTarget;
        }

        if (isFollowing && updateDestinationWhileFollowing)
        {
            activeDestinationWorld = targetForSpring;
        }
    }

    private Vector3 StepSecondOrder(
        Vector3 currentPosition,
        Vector3 destination,
        float deltaTime
    )
    {
        if (deltaTime <= Epsilon)
        {
            return currentPosition;
        }

        float remaining = deltaTime;
        Vector3 position = currentPosition;

        int guard = 0;

        while (remaining > Epsilon && guard < 32)
        {
            guard++;

            float step = Mathf.Min(remaining, maxSubstepTime);
            remaining -= step;

            float omega = 2f * Mathf.PI * Mathf.Max(0.01f, frequencyHz);
            float stiffness = omega * omega;
            float damping = 2f * Mathf.Max(0f, dampingRatio) * omega;

            Vector3 acceleration =
                stiffness * (destination - position)
                - damping * currentVelocity;

            if (maxAcceleration > 0f && acceleration.magnitude > maxAcceleration)
            {
                acceleration = acceleration.normalized * maxAcceleration;
            }

            currentVelocity += acceleration * step;

            if (maxSpeed > 0f && currentVelocity.magnitude > maxSpeed)
            {
                currentVelocity = currentVelocity.normalized * maxSpeed;
            }

            position += currentVelocity * step;
        }

        return position;
    }

    private void InitializeFollowerState()
    {
        ResolveReferences();

        if (fakeTarget == null)
        {
            return;
        }

        Vector3 startPosition = fakeTarget.position;

        if (coreNode != null && initializeFakeTargetAtRealTarget)
        {
            Vector3 rawTarget = GetRawTargetWorldPosition();
            startPosition = ProjectWorldPointIntoReach(rawTarget, out _);
        }
        else if (coreNode != null && clampFakeTargetOnStart)
        {
            startPosition = ProjectWorldPointIntoReach(startPosition, out _);
        }

        currentWorldPosition = startPosition;
        currentVelocity = Vector3.zero;
        activeDestinationWorld = startPosition;
        lastProjectedRealTargetWorld = startPosition;
        lastRawRealTargetWorld = startPosition;
        isFollowing = false;

        WriteFakeTarget(startPosition);

        if (tailNode != null)
        {
            tailNode.transform.position = startPosition;
        }
    }

    private Vector3 GetRawTargetWorldPosition()
    {
        if (useExternalTargetPosition)
        {
            return externalTargetWorldPosition;
        }

        if (realTarget != null)
        {
            return realTarget.position;
        }

        return fakeTarget != null ? fakeTarget.position : transform.position;
    }

    private Vector3 ProjectWorldPointIntoReach(
        Vector3 worldPoint,
        out bool wasClamped
    )
    {
        wasClamped = false;

        if (coreNode == null)
        {
            return worldPoint;
        }

        Vector3 corePosition = coreNode.position;
        Vector3 fromCore = worldPoint - corePosition;

        float distance = fromCore.magnitude;

        Vector3 direction;

        if (distance <= Epsilon)
        {
            direction = GetFallbackDirection();
        }
        else
        {
            direction = fromCore / distance;
        }

        float maxReach = GetMaxReach();
        float minReach = enforceMinimumReach
            ? GetMinReach(maxReach)
            : 0f;

        float clampedDistance = Mathf.Clamp(distance, minReach, maxReach);

        if (Mathf.Abs(clampedDistance - distance) > 0.0001f)
        {
            wasClamped = true;
        }

        return corePosition + direction * clampedDistance;
    }

    private float GetMaxReach()
    {
        float reach = manualMaxReach;

        if (maxReachSource == MaxReachSource.LimbSolverCumulativeBones &&
            limbSolver != null &&
            limbSolver.CumulativeBones > Epsilon)
        {
            reach = (float)limbSolver.CumulativeBones;
        }
        else if (maxReachSource == MaxReachSource.NodeStateChain)
        {
            float chainReach = CalculateNodeStateChainReach();

            if (chainReach > Epsilon)
            {
                reach = chainReach;
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
                if (coreNode != null && fakeTarget != null)
                {
                    reach = Vector3.Distance(coreNode.position, fakeTarget.position);
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

    private float CalculateNodeStateChainReach()
    {
        NodeState tail = tailNode;

        if (tail == null && limbSolver != null)
        {
            tail = limbSolver.tail;
        }

        NodeState start = null;

        if (limbSolver != null)
        {
            start = limbSolver.start;
        }

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

            if (current.next == start)
            {
                break;
            }

            current = current.next;
        }

        return total;
    }

    private void AssignStaticPoleToChain()
    {
        NodeState tail = tailNode;

        if (tail == null && limbSolver != null)
        {
            tail = limbSolver.tail;
        }

        NodeState start = null;

        if (limbSolver != null)
        {
            start = limbSolver.start;
        }

        NodeState current = tail;
        int guard = 0;

        while (current != null && guard < MaxChainNodes)
        {
            guard++;

            current.pole = staticPole;

            if (current == start)
            {
                break;
            }

            current = current.next;
        }
    }

    private Vector3 GetFallbackDirection()
    {
        if (coreNode != null)
        {
            if (realTarget != null)
            {
                Vector3 toReal = realTarget.position - coreNode.position;

                if (toReal.sqrMagnitude > Epsilon)
                {
                    return toReal.normalized;
                }
            }

            if (coreNode.forward.sqrMagnitude > Epsilon)
            {
                return coreNode.forward.normalized;
            }
        }

        return Vector3.forward;
    }

    private void ResolveReferences()
    {
        if (limbSolver == null)
        {
            limbSolver = GetComponent<LimbSolver>();
        }

        if (limbSolver != null)
        {
            if (autoUseSolverStartAsCore && coreNode == null && limbSolver.start != null)
            {
                coreNode = limbSolver.start.transform;
            }

            if (autoUseSolverTailAsFakeTarget && fakeTarget == null && limbSolver.tail != null)
            {
                fakeTarget = limbSolver.tail.transform;
            }

            if (autoUseSolverTailAsTailNode && tailNode == null)
            {
                tailNode = limbSolver.tail;
            }
        }

        if (targetOffsetNode == null && fakeTarget != null)
        {
            targetOffsetNode = fakeTarget.GetComponent<OffsetPositioningNode>();
        }
    }

    private void WriteFakeTarget(Vector3 worldPosition)
    {
        if (writeTargetThroughOffsetNode && targetOffsetNode != null)
        {
            targetOffsetNode.SetDynamicOffsetToReachWorldPosition(
                targetDynamicOffsetId,
                worldPosition
            );

            return;
        }

        if (fakeTarget != null)
        {
            fakeTarget.position = worldPosition;
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (coreNode == null)
        {
            return;
        }

        float maxReach = Application.isPlaying
            ? debugState.maxReach
            : manualMaxReach;

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(coreNode.position, maxReach);

        Gizmos.color = Color.yellow;
        Gizmos.DrawSphere(lastProjectedRealTargetWorld, 0.055f);
        Gizmos.DrawLine(coreNode.position, lastProjectedRealTargetWorld);

        Gizmos.color = isFollowing ? Color.cyan : Color.gray;
        Gizmos.DrawSphere(currentWorldPosition, 0.07f);
        Gizmos.DrawLine(currentWorldPosition, activeDestinationWorld);

        Gizmos.color = Color.magenta;
        Gizmos.DrawSphere(activeDestinationWorld, 0.05f);
    }
}