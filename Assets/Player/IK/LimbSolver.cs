using System;
using System.Collections.Generic;
using UnityEngine;

// INVERSE KINEMATICS IMPLEMENTATION
[DefaultExecutionOrder(200)]
public class LimbSolver : MonoBehaviour
{
    private const float Epsilon = 0.000001f;
    private const int MaxChainNodes = 256;

    [Header("Chain")]
    public NodeState start;
    public NodeState tail;

    [Header("Computed Reach")]
    public double CumulativeBones;
    public double MinimumReach;

    [Header("Pole Safety")]
    [Tooltip("Used when a NodeState has no pole assigned.")]
    public Transform fallbackPole;

    [Tooltip("If true, fallbackPole is assigned to nodes that have no pole during initialization.")]
    public bool assignFallbackPoleToNodesWithoutPole = true;

    [Header("Runtime")]
    public bool initializeOnStart = true;
    public bool autoInitializeIfNeeded = true;
    public bool solveInLateUpdate = true;

    [Tooltip("When true, a ProceduralPlayerRig frame driver invokes Apply explicitly after targets and offsets have been updated.")]
    public bool managedByProceduralRig = false;

    [Tooltip("Safety clamp. The spine target setter should already keep the target valid, but this prevents impossible targets from breaking the solver.")]
    public bool clampTailToReachBeforeSolving = true;

    [Tooltip("If true, initialization captures bone lengths from the current transform pose. Runtime-sized legs can disable this so NodeState.Mylength stays authoritative.")]
    public bool captureBoneLengthsOnInitialize = true;

    [Tooltip("Usually false for normal limbs. The spine target setter handles minimum/default bend separately.")]
    public bool enforceMinimumReachOnTail = false;

    [Min(0f)]
    public float reachPadding = 0.001f;

    [Header("Tail Target Handle")]
    [Tooltip("If true, tail is treated as a temporary IK target handle during solving, then restored to the first solved node after the handle. This is useful when other systems bind offsets/mesh to the spine tail transform.")]
    public bool restoreTailToSolvedEndAfterSolving = false;

    [Tooltip("Optional separate IK target. When assigned, the solver reads this transform as the tail target without teleporting the visible tail node before solving.")]
    public Transform tailTargetOverride;

    [Tooltip("Default false for setter-owned fake targets. The solver may clamp the target internally, but it must not write back into tailTargetOverride unless this is explicitly enabled.")]
    public bool writeClampedTailTargetBackToTransform = false;

    [Tooltip("When a separate target override is used, keep the visible tail node at the clamped override position for the mesh/attachments, while leaving the override transform itself owned by its target setter.")]
    public bool keepVisibleTailAtTargetWhenUsingOverride = false;

    [Tooltip("With a separate target override, move the visible tail after the chain solve instead of before it. This keeps the solver's intermediate-node placement stable for spine-like limbs while still giving the mesh a valid final endpoint.")]
    public bool moveVisibleTailToOverrideTargetAfterSolving = false;

    [Tooltip("If true, endpoint pre-translation is allowed even when tailTargetOverride is assigned. Keep this off for the spine: the fake target is already explicit, and pre-translation can make the intermediate nodes knot/teleport around the handle.")]
    public bool preTranslateWhenUsingTailTargetOverride = false;

    [Tooltip("When captureBoneLengthsOnInitialize is enabled, recapture impossible/stale serialized bone lengths from the current pose. This prevents old serialized 0.1 spine bones from solving a visually 1-unit-per-bone spine as a tiny knot.")]
    public bool repairStaleCapturedBoneLengths = true;

    [Range(0.05f, 0.95f)]
    public float staleBoneLengthPoseRatioThreshold = 0.35f;

    [Header("Chain Responsiveness")]
    [Tooltip("Moves intermediate IK nodes by the same-frame endpoint delta before solving. This prevents small/scaled limbs from visually lagging behind fast start/target motion if a solve frame is late or constrained.")]
    public bool preTranslateIntermediateNodesByEndpointDelta = true;

    [Range(0f, 1f)]
    [Tooltip("0 follows the start/root delta, 1 follows the target delta. 0.5 moves the chain by the average endpoint motion before the exact IK solve.")]
    public float endpointDeltaBlend = 0.5f;

    [Tooltip("Maximum endpoint pre-translation per frame as a fraction of total chain reach. 0 disables the cap.")]
    [Min(0f)] public float maxEndpointPreTranslateReachFraction = 0.75f;

    private bool hasLastEndpointPositions = false;
    private Vector3 lastSolvedStartPosition;
    private Vector3 lastSolvedTargetPosition;

    [Header("Debug")]
    public bool debugLogs = false;
    public bool drawDebugLines = false;
    public Color debugLineColor = Color.white;

    public bool IsInitialized { get; private set; }
    public List<NodeState> ChainNodes { get; private set; } = new List<NodeState>();

    public float MaxReach
    {
        get { return Mathf.Max(0f, (float)CumulativeBones); }
    }

    public float MinReach
    {
        get { return Mathf.Max(0f, (float)MinimumReach); }
    }

    public float InitialTargetDistanceFromStart { get; private set; }

    private void Start()
    {
        if (initializeOnStart)
        {
            InitializeChainData();
        }
    }

    private void LateUpdate()
    {
        if (managedByProceduralRig)
        {
            return;
        }

        if (solveInLateUpdate)
        {
            Apply();
        }
    }

    public bool InitializeChainData()
    {
        IsInitialized = false;
        ChainNodes.Clear();

        if (!TryBuildChainList(ChainNodes))
        {
            if (debugLogs)
            {
                Debug.LogError($"{name}: Could not build IK chain. Check start, tail, and NodeState.next links.", this);
            }

            return false;
        }

        for (int i = 0; i < ChainNodes.Count; i++)
        {
            ChainNodes[i].ClampBendAngles();
        }

        // Store bone vectors. Spine and body chains are fragile if an old serialized
        // Mylength survives runtime scaling/startup. When capture is enabled, always
        // validate against the current transform pose before computing the reach.
        for (int i = 0; i < ChainNodes.Count - 1; i++)
        {
            ChainNodes[i].InitializeLengthFromNext(captureBoneLengthsOnInitialize);
        }

        if (captureBoneLengthsOnInitialize && repairStaleCapturedBoneLengths)
        {
            RepairStaleBoneLengthsFromCurrentPose();
        }

        double totalLength = 0.0;
        double longestBone = 0.0;

        for (int i = 0; i < ChainNodes.Count - 1; i++)
        {
            double length = ChainNodes[i].Mylength.magnitude;
            totalLength += length;

            if (length > longestBone)
            {
                longestBone = length;
            }
        }

        CumulativeBones = totalLength;
        MinimumReach = Math.Max(0.0, longestBone - (totalLength - longestBone));

        double remainingSum = 0.0;
        double remainingLongest = 0.0;

        // Walk backwards and assign each node its remaining chain bounds.
        for (int i = ChainNodes.Count - 2; i >= 0; i--)
        {
            NodeState node = ChainNodes[i];

            node.MyChain = remainingSum;

            node.MinChain = Math.Max(
                0.0,
                remainingLongest - (remainingSum - remainingLongest)
            );

            double thisBoneLength = node.Mylength.magnitude;

            remainingSum += thisBoneLength;

            if (thisBoneLength > remainingLongest)
            {
                remainingLongest = thisBoneLength;
            }

            if (assignFallbackPoleToNodesWithoutPole && node.pole == null)
            {
                node.pole = fallbackPole;
            }
        }

        InitialTargetDistanceFromStart =
            start != null && tail != null
                ? Vector3.Distance(GetTailTargetWorldPosition(), start.transform.position)
                : 0f;

        IsInitialized = CumulativeBones > Epsilon;

        if (!IsInitialized && debugLogs)
        {
            Debug.LogError($"{name}: IK chain initialized with zero reach.", this);
        }

        return IsInitialized;
    }

    public bool Apply()
    {
        if (!IsInitialized)
        {
            if (!autoInitializeIfNeeded || !InitializeChainData())
            {
                return false;
            }
        }

        if (start == null || tail == null)
        {
            return false;
        }

        if (CumulativeBones <= Epsilon)
        {
            return false;
        }

        Vector3 targetWorldPosition = GetTailTargetWorldPosition();

        if (clampTailToReachBeforeSolving)
        {
            targetWorldPosition = ClampWorldPointToReach(
                targetWorldPosition,
                enforceMinimumReachOnTail
            );

            if (tailTargetOverride == null || writeClampedTailTargetBackToTransform)
            {
                if (tailTargetOverride != null)
                {
                    tailTargetOverride.position = targetWorldPosition;
                }
                else
                {
                    tail.transform.position = targetWorldPosition;
                }
            }
        }

        // Do not move the visible tail before the solve when an override is used. The
        // first solve step already uses targetWorldPosition as its origin; moving the
        // transform early makes the meshed spine tail participate in the pre-solve state
        // and can cause visible knots/teleports when the target is high above the core.
        PreTranslateIntermediateNodesForEndpointMotion(targetWorldPosition);

        double targetDistance =
            Vector3.Distance(targetWorldPosition, start.transform.position);

        NodeState current = tail;
        int guard = 0;

        while (current != null &&
               current.next != null &&
               current.next != start &&
               guard < MaxChainNodes)
        {
            guard++;

            Transform pole = current.pole != null ? current.pole : fallbackPole;

            Vector3 origin =
                current == tail && tailTargetOverride != null
                    ? targetWorldPosition
                    : current.transform.position;
            Vector3 rootDistance = start.transform.position - origin;

            float rootMag = rootDistance.magnitude;
            float boneMag = current.Mylength.magnitude;

            if (rootMag <= Epsilon || boneMag <= Epsilon)
            {
                if (debugLogs)
                {
                    Debug.LogError($"{name}: Invalid IK state on {current.name}. Zero-length distance or bone.", current);
                }

                return false;
            }

            Vector3 axis = rootDistance / rootMag;

            // Clamp valid distance between current bone and remaining chain.
            double currentBoneMinReach = Math.Abs(rootMag - boneMag);
            double currentBoneMaxReach = rootMag + boneMag;

            double remainingMinReach = current.MinChain;
            double remainingMaxReach = current.MyChain;

            double minWantedDistance = Math.Max(currentBoneMinReach, remainingMinReach);
            double maxWantedDistance = Math.Min(currentBoneMaxReach, remainingMaxReach);

            if (minWantedDistance > maxWantedDistance)
            {
                if (minWantedDistance > maxWantedDistance + 0.000001)
                {
                    if (debugLogs)
                    {
                        Debug.LogError($"{name}: No valid IK position exists for node {current.name}.", current);
                    }

                    return false;
                }

                double average = (minWantedDistance + maxWantedDistance) * 0.5;
                minWantedDistance = average;
                maxWantedDistance = average;
            }

            double compression =
                1.0 - ClampDouble(targetDistance / Math.Max(CumulativeBones, Epsilon), 0.0, 1.0);

            double desiredAngle =
                compression * Math.PI * Math.Max(0.0, current.BendWeight);

            double maxAngle = current.MaxBendAngle * Math.PI / 180.0;
            double minAngle = current.MinBendAngle * Math.PI / 180.0;

            desiredAngle = ClampDouble(desiredAngle, minAngle, maxAngle);

            double desiredWantedDistance = Math.Sqrt(
                rootMag * rootMag +
                boneMag * boneMag -
                2.0 * rootMag * boneMag * Math.Cos(desiredAngle)
            );

            double wantedDistance = ClampDouble(
                desiredWantedDistance,
                minWantedDistance,
                maxWantedDistance
            );

            double acosNumerator =
                rootMag * rootMag +
                boneMag * boneMag -
                wantedDistance * wantedDistance;

            double acosDenominator = 2.0 * rootMag * boneMag;

            double acosValue = ClampDouble(
                acosNumerator / Math.Max(acosDenominator, Epsilon),
                -1.0,
                1.0
            );

            float angle = (float)Math.Acos(acosValue);

            float forwardDistance = Mathf.Cos(angle) * boneMag;
            float sidewaysDistance = Mathf.Sin(angle) * boneMag;

            Vector3 circleCenter = origin + axis * forwardDistance;

            Vector3 side;

            if (pole != null)
            {
                Vector3 poleDirection = pole.position - circleCenter;
                side = Vector3.ProjectOnPlane(poleDirection, axis);

                if (side.sqrMagnitude <= Epsilon)
                {
                    side = GetFallbackSide(axis);
                }
                else
                {
                    side.Normalize();
                }
            }
            else
            {
                side = GetFallbackSide(axis);
            }

            Vector3 newPosition = circleCenter + side * sidewaysDistance;

            current.next.transform.position = newPosition;

            if (drawDebugLines)
            {
                Debug.DrawLine(
                    current.transform.position,
                    current.next.transform.position,
                    debugLineColor
                );
            }

            if (debugLogs)
            {
                Debug.Log(
                    $"{name}: Solved {current.next.name} | angle={angle * Mathf.Rad2Deg:F2} | pole={(pole != null ? pole.name : "none")}",
                    current.next
                );
            }

            current = current.next;
        }

        if (guard >= MaxChainNodes)
        {
            if (debugLogs)
            {
                Debug.LogError($"{name}: IK chain exceeded guard limit. Possible cycle in NodeState.next links.", this);
            }

            return false;
        }

        if (tailTargetOverride != null)
        {
            if (restoreTailToSolvedEndAfterSolving &&
                tail.next != null &&
                tail.next != start)
            {
                // Spine-style target overrides are handles, not necessarily visible end
                // sections. This restores the original curved-body behavior: the fake
                // target still drives the solve, but the meshed tail returns to the
                // solved chain end instead of stretching a straight section to the
                // handle and flattening the bend distribution.
                tail.transform.position = tail.next.transform.position;
            }
            else if (keepVisibleTailAtTargetWhenUsingOverride &&
                     moveVisibleTailToOverrideTargetAfterSolving &&
                     tail != null)
            {
                // For limbs that explicitly want a visible endpoint at the separate
                // handle, apply it after intermediate nodes are solved.
                tail.transform.position = targetWorldPosition;
            }
        }
        else if (restoreTailToSolvedEndAfterSolving &&
                 tail.next != null &&
                 tail.next != start)
        {
            tail.transform.position = tail.next.transform.position;
        }

        StoreEndpointPositions(targetWorldPosition);
        return true;
    }

    private void PreTranslateIntermediateNodesForEndpointMotion(Vector3 targetWorldPosition)
    {
        if (!preTranslateIntermediateNodesByEndpointDelta ||
            (tailTargetOverride != null && !preTranslateWhenUsingTailTargetOverride) ||
            start == null ||
            ChainNodes == null ||
            ChainNodes.Count <= 2)
        {
            StoreEndpointPositions(targetWorldPosition);
            return;
        }

        if (!hasLastEndpointPositions)
        {
            StoreEndpointPositions(targetWorldPosition);
            return;
        }

        Vector3 startDelta = start.transform.position - lastSolvedStartPosition;
        Vector3 targetDelta = targetWorldPosition - lastSolvedTargetPosition;
        Vector3 blendedDelta = Vector3.Lerp(startDelta, targetDelta, Mathf.Clamp01(endpointDeltaBlend));

        float maxDelta = maxEndpointPreTranslateReachFraction > 0f
            ? Mathf.Max(0.001f, MaxReach * maxEndpointPreTranslateReachFraction)
            : 0f;

        if (maxDelta > 0f && blendedDelta.magnitude > maxDelta)
        {
            blendedDelta = blendedDelta.normalized * maxDelta;
        }

        if (blendedDelta.sqrMagnitude <= Epsilon)
        {
            return;
        }

        // ChainNodes order is tail -> intermediate(s) -> start. Do not move the target handle or the root.
        for (int i = 1; i < ChainNodes.Count - 1; i++)
        {
            NodeState node = ChainNodes[i];
            if (node != null)
            {
                node.transform.position += blendedDelta;
            }
        }
    }

    private void RepairStaleBoneLengthsFromCurrentPose()
    {
        if (ChainNodes == null || ChainNodes.Count <= 1)
        {
            return;
        }

        double serializedTotal = 0.0;
        double poseTotal = 0.0;

        for (int i = 0; i < ChainNodes.Count - 1; i++)
        {
            NodeState node = ChainNodes[i];
            if (node == null || node.next == null)
            {
                continue;
            }

            serializedTotal += node.Mylength.magnitude;
            poseTotal += Vector3.Distance(node.transform.position, node.next.transform.position);
        }

        if (poseTotal <= Epsilon)
        {
            return;
        }

        float threshold = Mathf.Clamp(staleBoneLengthPoseRatioThreshold, 0.05f, 0.95f);
        bool tooShort = serializedTotal <= poseTotal * threshold;
        bool invalid = serializedTotal <= Epsilon;

        if (!tooShort && !invalid)
        {
            return;
        }

        for (int i = 0; i < ChainNodes.Count - 1; i++)
        {
            NodeState node = ChainNodes[i];
            if (node != null)
            {
                node.CaptureLengthFromCurrentPose();
            }
        }
    }

    private void StoreEndpointPositions(Vector3 targetWorldPosition)
    {
        if (start == null)
        {
            hasLastEndpointPositions = false;
            return;
        }

        lastSolvedStartPosition = start.transform.position;
        lastSolvedTargetPosition = targetWorldPosition;
        hasLastEndpointPositions = true;
    }

    public Vector3 ClampWorldPointToReach(Vector3 worldPoint, bool useMinimumReach)
    {
        if (start == null)
        {
            return worldPoint;
        }

        Vector3 fromStart = worldPoint - start.transform.position;
        float distance = fromStart.magnitude;

        Vector3 direction;

        if (distance <= Epsilon)
        {
            direction = GetFallbackTargetDirection();
        }
        else
        {
            direction = fromStart / distance;
        }

        float maxReach = Mathf.Max(0f, MaxReach - reachPadding);
        float minReach = useMinimumReach
            ? Mathf.Min(MinReach + reachPadding, Mathf.Max(0f, maxReach - reachPadding))
            : 0f;

        float clampedDistance = Mathf.Clamp(distance, minReach, maxReach);

        return start.transform.position + direction * clampedDistance;
    }

    public void SetPoleForAllSolvableNodes(Transform pole, bool includeTailNode)
    {
        if (!IsInitialized)
        {
            InitializeChainData();
        }

        fallbackPole = pole;

        for (int i = 0; i < ChainNodes.Count; i++)
        {
            NodeState node = ChainNodes[i];

            if (node == null || node == start)
            {
                continue;
            }

            if (!includeTailNode && node == tail)
            {
                continue;
            }

            node.pole = pole;
        }
    }

    public bool TryGetReachBounds(out float minReach, out float maxReach)
    {
        if (!IsInitialized)
        {
            InitializeChainData();
        }

        minReach = MinReach;
        maxReach = MaxReach;

        return IsInitialized;
    }

    private bool TryBuildChainList(List<NodeState> nodes)
    {
        nodes.Clear();

        if (start == null || tail == null)
        {
            return false;
        }

        HashSet<NodeState> visited = new HashSet<NodeState>();

        NodeState current = tail;
        int guard = 0;

        while (current != null && guard < MaxChainNodes)
        {
            guard++;

            if (visited.Contains(current))
            {
                return false;
            }

            visited.Add(current);
            nodes.Add(current);

            if (current == start)
            {
                return nodes.Count >= 2;
            }

            current = current.next;
        }

        return false;
    }

    private Vector3 GetFallbackSide(Vector3 axis)
    {
        Vector3 side = Vector3.Cross(axis, Vector3.up);

        if (side.sqrMagnitude <= Epsilon)
        {
            side = Vector3.Cross(axis, Vector3.right);
        }

        if (side.sqrMagnitude <= Epsilon)
        {
            side = Vector3.forward;
        }

        return side.normalized;
    }

    private Vector3 GetFallbackTargetDirection()
    {
        if (tail != null && start != null)
        {
            Vector3 currentDirection = GetTailTargetWorldPosition() - start.transform.position;

            if (currentDirection.sqrMagnitude > Epsilon)
            {
                return currentDirection.normalized;
            }
        }

        if (start != null)
        {
            return start.transform.forward.sqrMagnitude > Epsilon
                ? start.transform.forward.normalized
                : Vector3.forward;
        }

        return Vector3.forward;
    }

    private static double ClampDouble(double value, double min, double max)
    {
        if (value < min)
        {
            return min;
        }

        if (value > max)
        {
            return max;
        }

        return value;
    }

    private Vector3 GetTailTargetWorldPosition()
    {
        if (tailTargetOverride != null)
        {
            return tailTargetOverride.position;
        }

        return tail != null ? tail.transform.position : Vector3.zero;
    }
}
