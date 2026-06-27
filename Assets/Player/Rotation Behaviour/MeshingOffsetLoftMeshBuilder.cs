using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Extremely light mesh builder for MeshingOffsetNode.
///
/// What it does:
/// - Each MeshingOffsetNode becomes one 2D boundary loop made from its offsets.
/// - The loop is sorted/hulled locally, then optionally resampled to a tiny fixed vertex count.
/// - Adjacent loops are bridged with quads.
///
/// What it does NOT do:
/// - No SDF.
/// - No marching cubes/tetrahedra.
/// - No editor-time Update.
/// - No all-to-all node connections.
/// - No invented rim geometry except an optional tiny fallback ring when a section has only 1-2 points.
///
/// Safe by default:
/// - Adding this component does not build anything.
/// - It only builds from the context menu, from Start if buildOnStart is enabled, or in play mode if
///   rebuildEveryFrameInPlayMode is enabled.
/// </summary>
[DisallowMultipleComponent]
[DefaultExecutionOrder(250)]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class MeshingOffsetLoftMeshBuilder : MonoBehaviour
{
    private const float Epsilon = 0.000001f;

    public enum OffsetNodeSourceMode
    {
        ExplicitArray,
        Children,
        ExplicitArrayThenChildren
    }

    public enum SectionOrderMode
    {
        AsFound,
        AlongWorldX,
        AlongWorldY,
        AlongWorldZ,
        AlongCustomWorldAxis
    }

    public enum LoopPlaneMode
    {
        /// <summary>Uses offset.x / offset.z. This matches the default MeshingOffsetNode file.</summary>
        OffsetXZ,

        /// <summary>Uses offset.x / offset.y.</summary>
        OffsetXY,

        /// <summary>Uses offset.z / offset.y.</summary>
        OffsetZY,

        /// <summary>Builds the loop in the plane perpendicular to the local section-chain direction.</summary>
        PerpendicularToSectionChain
    }

    [Header("Input")]
    public OffsetNodeSourceMode sourceMode = OffsetNodeSourceMode.Children;
    public MeshingOffsetNode[] explicitOffsetNodes;
    public bool includeInactiveChildren = false;
    public SectionOrderMode sectionOrderMode = SectionOrderMode.AsFound;
    public Vector3 customWorldOrderAxis = Vector3.up;

    [Header("Build Timing")]
    [Tooltip("Safe default: false. Adding the component will not build anything in edit mode or play mode unless this is enabled.")]
    public bool buildOnStart = false;

    [Tooltip("Safe default: false. Enable only after the mesh looks correct. This only runs in play mode, never in edit mode.")]
    public bool rebuildEveryFrameInPlayMode = false;

    [Tooltip("When true, a ProceduralPlayerRig frame driver rebuilds this mesh explicitly after IK and offsets finish.")]
    public bool managedByProceduralRig = false;

    [Tooltip("In play mode, keep the previous valid mesh if one frame resolves no usable sections/triangles. This prevents renderer flicker when IK targets are between states.")]
    public bool keepLastValidRuntimeMeshOnBuildFailure = true;

    [Tooltip("Minimum seconds between runtime mesh rebuilds. Use 0 for every frame. This is ignored when a rebuild is forced.")]
    [Min(0f)] public float minimumRuntimeRebuildInterval = 1f / 45f;

    [Tooltip("Skips runtime rebuilds when the input nodes have not moved enough since the previous build.")]
    public bool onlyRebuildWhenInputMoved = true;

    [Tooltip("World-space movement needed before a runtime rebuild is considered dirty.")]
    [Min(0f)] public float inputMoveEpsilon = 0.0025f;

    [Header("Loop Shape")]
    public LoopPlaneMode loopPlaneMode = LoopPlaneMode.OffsetXZ;

    [Tooltip("If enabled, inner offsets are ignored and the local section becomes the tight outer wrap of the offset points.")]
    public bool useConvexHull = true;

    [Tooltip("Fixed number of vertices in every section loop. 4-12 is usually enough. This is the main cost knob.")]
    [Range(3, 64)] public int ringSamples = 8;

    [Tooltip("Adds a tiny amount outside the found local wrap. 0 is the tightest wrap.")]
    [Min(0f)] public float radialPadding = 0f;

    [Tooltip("Used only when a section has 1 or 2 usable offsets. Prevents zero-area triangles while staying tiny.")]
    [Min(0.0001f)] public float minimumFallbackRadius = 0.025f;

    [Tooltip("Optional smoothing after the loop is built. Keep 0 for the tightest polygon. 1-2 can soften pointy shapes.")]
    [Range(0, 4)] public int smoothingIterations = 0;

    [Range(0f, 0.75f)] public float smoothingStrength = 0.25f;

    [Tooltip("Aligns each section ring to the previous ring before bridging. This prevents a spine/limb mesh knot when a convex hull or projected loop chooses a different first vertex/winding on one middle section while the IK nodes themselves are correct.")]
    public bool alignAdjacentSectionRings = true;

    [Header("Safety Gates")]
    [Tooltip("Offsets farther than this from their parent/core are ignored. <= 0 disables the limit.")]
    [Min(0f)] public float maxOffsetDistanceFromParent = 5f;

    [Tooltip("Adjacent sections farther apart than this are not bridged. <= 0 disables the limit.")]
    [Min(0f)] public float maxBridgeDistance = 5f;

    [Tooltip("Sections with fewer than this many valid offsets are skipped unless fallback rings are allowed.")]
    [Range(1, 3)] public int minimumOffsetsForNormalSection = 3;

    public bool allowFallbackSectionsForOneOrTwoOffsets = true;

    [Header("Mesh Output")]
    public MeshFilter meshFilter;
    public MeshCollider optionalMeshCollider;
    public bool capFirstAndLastOpenEnds = true;
    [Tooltip("If there is only one parent section, build its local 2D shape by linking its offsets together instead of requiring a second parent to bridge to.")]
    public bool capSingleSection = true;
    public bool flipWinding = false;
    public bool doubleSided = false;
    public bool recalculateNormals = true;
    public bool recalculateBounds = true;
    public bool generateUVs = true;

    [Header("Debug")]
    public bool debugLogging = false;
    public bool drawGizmos = false;
    public Color sectionGizmoColor = new Color(0.1f, 0.8f, 1f, 0.9f);
    public Color bridgeGizmoColor = new Color(1f, 0.75f, 0.1f, 0.9f);

    private readonly List<Section> sections = new List<Section>();
    private readonly List<Vector3> meshVertices = new List<Vector3>();
    private readonly List<int> meshTriangles = new List<int>();
    private readonly List<Vector2> meshUvs = new List<Vector2>();
    private readonly List<Vector3> tempWorldLoop = new List<Vector3>();
    private readonly List<ProjectedPoint> tempProjected = new List<ProjectedPoint>();
    private readonly List<ProjectedPoint> tempHull = new List<ProjectedPoint>();
    private readonly List<MeshingOffsetNode> tempNodes = new List<MeshingOffsetNode>();

    private Mesh generatedMesh;
    private Mesh runtimeStagingMesh;
    private readonly List<Vector3> lastInputNodePositions = new List<Vector3>();
    private readonly List<Vector3> currentInputNodePositions = new List<Vector3>();
    private bool hasLastInputNodePositions = false;
    private float runtimeRebuildTimer = 999f;
    private bool hasBuiltValidMesh = false;

    private class Section
    {
        public MeshingOffsetNode source;
        public Vector3 center;
        public Vector3 right;
        public Vector3 up;
        public Vector3[] ring;
        public int[] vertexIndices;
        public bool isPointSection;
        public Vector3 pointWorld;
        public int pointVertexIndex;
        public bool usedByBridge;
    }

    private struct ProjectedPoint
    {
        public Vector2 p2;
        public Vector3 p3;

        public ProjectedPoint(Vector2 p2, Vector3 p3)
        {
            this.p2 = p2;
            this.p3 = p3;
        }
    }

    private void Reset()
    {
        meshFilter = GetComponent<MeshFilter>();
    }

    private void OnValidate()
    {
        ringSamples = Mathf.Clamp(ringSamples, 3, 64);
        maxOffsetDistanceFromParent = Mathf.Max(0f, maxOffsetDistanceFromParent);
        maxBridgeDistance = Mathf.Max(0f, maxBridgeDistance);
        minimumFallbackRadius = Mathf.Max(0.0001f, minimumFallbackRadius);
    }

    private void Start()
    {
        if (Application.isPlaying && buildOnStart)
        {
            RebuildMesh();
        }
    }

    private void LateUpdate()
    {
        if (managedByProceduralRig)
        {
            return;
        }

        if (Application.isPlaying && rebuildEveryFrameInPlayMode)
        {
            RebuildMeshForRuntime(Time.deltaTime, false);
        }
    }

    [ContextMenu("Rebuild Meshing Offset Loft")]
    public void RebuildMesh()
    {
        EnsureReferences();
        ResolveOffsetNodes(tempNodes);
        if (RebuildMeshFromResolvedNodes(tempNodes, false))
        {
            CaptureInputNodePositions(tempNodes);
        }
    }

    public bool RebuildMeshForRuntime(float deltaTime, bool force)
    {
        EnsureReferences();
        ResolveOffsetNodes(tempNodes);

        runtimeRebuildTimer += Mathf.Max(0f, deltaTime);

        if (!force)
        {
            if (minimumRuntimeRebuildInterval > 0f && runtimeRebuildTimer < minimumRuntimeRebuildInterval)
            {
                return false;
            }

            if (onlyRebuildWhenInputMoved && !HaveInputNodesMoved(tempNodes))
            {
                return false;
            }
        }

        bool built = RebuildMeshFromResolvedNodes(tempNodes, true);
        if (!built)
        {
            runtimeRebuildTimer = 0f;
            return false;
        }

        CaptureInputNodePositions(tempNodes);
        runtimeRebuildTimer = 0f;
        return true;
    }

    private bool RebuildMeshFromResolvedNodes(List<MeshingOffsetNode> nodes, bool runtimeBuild)
    {
        SortOffsetNodes(nodes);
        BuildSections(nodes);
        return BuildMeshFromSections(runtimeBuild);
    }

    private bool HaveInputNodesMoved(List<MeshingOffsetNode> nodes)
    {
        CollectInputPointPositions(nodes, currentInputNodePositions);

        if (!hasLastInputNodePositions || lastInputNodePositions.Count != currentInputNodePositions.Count)
        {
            return true;
        }

        float epsilonSqr = inputMoveEpsilon * inputMoveEpsilon;
        for (int i = 0; i < currentInputNodePositions.Count; i++)
        {
            if ((currentInputNodePositions[i] - lastInputNodePositions[i]).sqrMagnitude > epsilonSqr)
            {
                return true;
            }
        }

        return false;
    }

    private void CaptureInputNodePositions(List<MeshingOffsetNode> nodes)
    {
        CollectInputPointPositions(nodes, currentInputNodePositions);
        lastInputNodePositions.Clear();

        for (int i = 0; i < currentInputNodePositions.Count; i++)
        {
            lastInputNodePositions.Add(currentInputNodePositions[i]);
        }

        hasLastInputNodePositions = true;
    }

    private static void CollectInputPointPositions(List<MeshingOffsetNode> nodes, List<Vector3> output)
    {
        output.Clear();

        for (int i = 0; i < nodes.Count; i++)
        {
            MeshingOffsetNode node = nodes[i];
            if (node == null || node.Count <= 0)
            {
                output.Add(Vector3.zero);
                continue;
            }

            for (int j = 0; j < node.Count; j++)
            {
                output.Add(node.GetWorldPosition(j));
            }
        }
    }

    [ContextMenu("Clear Generated Mesh")]
    public void ClearMesh()
    {
        EnsureReferences();

        if (meshFilter != null)
        {
            meshFilter.sharedMesh = null;
        }

        if (optionalMeshCollider != null)
        {
            optionalMeshCollider.sharedMesh = null;
        }

        if (generatedMesh != null)
        {
            generatedMesh.Clear();
        }
    }

    private void EnsureReferences()
    {
        if (meshFilter == null)
        {
            meshFilter = GetComponent<MeshFilter>();
        }

        if (!hasBuiltValidMesh && meshFilter != null && meshFilter.sharedMesh != null)
        {
            hasBuiltValidMesh = true;
        }
    }

    private void ResolveOffsetNodes(List<MeshingOffsetNode> output)
    {
        output.Clear();
        HashSet<MeshingOffsetNode> seen = new HashSet<MeshingOffsetNode>();

        if (sourceMode == OffsetNodeSourceMode.ExplicitArray || sourceMode == OffsetNodeSourceMode.ExplicitArrayThenChildren)
        {
            if (explicitOffsetNodes != null)
            {
                for (int i = 0; i < explicitOffsetNodes.Length; i++)
                {
                    AddNodeIfValid(explicitOffsetNodes[i], output, seen);
                }
            }
        }

        if (sourceMode == OffsetNodeSourceMode.Children || sourceMode == OffsetNodeSourceMode.ExplicitArrayThenChildren)
        {
            MeshingOffsetNode[] found = GetComponentsInChildren<MeshingOffsetNode>(includeInactiveChildren);
            for (int i = 0; i < found.Length; i++)
            {
                AddNodeIfValid(found[i], output, seen);
            }
        }

        Log("Resolved sections: " + output.Count);
    }

    private void AddNodeIfValid(MeshingOffsetNode node, List<MeshingOffsetNode> output, HashSet<MeshingOffsetNode> seen)
    {
        if (node == null || seen.Contains(node))
        {
            return;
        }

        seen.Add(node);
        output.Add(node);
    }

    private void SortOffsetNodes(List<MeshingOffsetNode> nodes)
    {
        if (sectionOrderMode == SectionOrderMode.AsFound || nodes.Count < 2)
        {
            return;
        }

        Vector3 axis = Vector3.up;

        if (sectionOrderMode == SectionOrderMode.AlongWorldX)
        {
            axis = Vector3.right;
        }
        else if (sectionOrderMode == SectionOrderMode.AlongWorldY)
        {
            axis = Vector3.up;
        }
        else if (sectionOrderMode == SectionOrderMode.AlongWorldZ)
        {
            axis = Vector3.forward;
        }
        else if (sectionOrderMode == SectionOrderMode.AlongCustomWorldAxis)
        {
            axis = customWorldOrderAxis.sqrMagnitude > Epsilon ? customWorldOrderAxis.normalized : Vector3.up;
        }

        nodes.Sort(delegate (MeshingOffsetNode a, MeshingOffsetNode b)
        {
            float da = Vector3.Dot(a.GetParentWorldPosition(), axis);
            float db = Vector3.Dot(b.GetParentWorldPosition(), axis);
            return da.CompareTo(db);
        });
    }

    private void BuildSections(List<MeshingOffsetNode> nodes)
    {
        sections.Clear();

        for (int i = 0; i < nodes.Count; i++)
        {
            MeshingOffsetNode node = nodes[i];
            if (node == null)
            {
                continue;
            }

            Vector3 previousCenter = i > 0 && nodes[i - 1] != null ? nodes[i - 1].GetParentWorldPosition() : node.GetParentWorldPosition();
            Vector3 nextCenter = i < nodes.Count - 1 && nodes[i + 1] != null ? nodes[i + 1].GetParentWorldPosition() : node.GetParentWorldPosition();

            Section section;
            if (TryBuildSection(node, previousCenter, nextCenter, out section))
            {
                sections.Add(section);
            }
        }

        Log("Built usable sections: " + sections.Count);
    }

    private bool TryBuildSection(MeshingOffsetNode node, Vector3 previousCenter, Vector3 nextCenter, out Section section)
    {
        section = null;

        Vector3 center = node.GetParentWorldPosition();
        Vector3 right;
        Vector3 up;
        ResolveLoopBasis(center, previousCenter, nextCenter, out right, out up);

        tempProjected.Clear();

        int count = node.Count;
        for (int i = 0; i < count; i++)
        {
            Vector3 offset = node.GetCurrentOffset(i);
            float distance = offset.magnitude;

            if (distance < Epsilon)
            {
                continue;
            }

            if (maxOffsetDistanceFromParent > 0f && distance > maxOffsetDistanceFromParent)
            {
                continue;
            }

            Vector3 world = center + offset;
            Vector2 projected = new Vector2(Vector3.Dot(offset, right), Vector3.Dot(offset, up));

            if (projected.sqrMagnitude < Epsilon)
            {
                // The point is along the plane normal, so it cannot help define this 2D loop.
                continue;
            }

            AddProjectedUnique(tempProjected, new ProjectedPoint(projected, world), 0.0001f);
        }

        if (tempProjected.Count < minimumOffsetsForNormalSection && tempProjected.Count != 1 && !allowFallbackSectionsForOneOrTwoOffsets)
        {
            return false;
        }

        tempWorldLoop.Clear();
        bool isPointSection = false;
        Vector3 pointWorld = Vector3.zero;

        if (tempProjected.Count >= 3)
        {
            if (useConvexHull)
            {
                BuildConvexHull(tempProjected, tempHull);
            }
            else
            {
                SortByAngle(tempProjected, tempHull);
            }

            for (int i = 0; i < tempHull.Count; i++)
            {
                Vector3 p = tempHull[i].p3;

                if (radialPadding > 0f)
                {
                    Vector2 dir2 = tempHull[i].p2.sqrMagnitude > Epsilon ? tempHull[i].p2.normalized : Vector2.zero;
                    p += (right * dir2.x + up * dir2.y) * radialPadding;
                }

                tempWorldLoop.Add(p);
            }
        }
        else if (tempProjected.Count == 2)
        {
            BuildTwoPointFallbackLoop(center, tempProjected[0].p3, tempProjected[1].p3, right, up, tempWorldLoop);
        }
        else if (tempProjected.Count == 1)
        {
            isPointSection = true;
            pointWorld = tempProjected[0].p3;
            BuildOnePointFallbackLoop(pointWorld, right, up, tempWorldLoop);
        }
        else
        {
            return false;
        }

        if (tempWorldLoop.Count < 3)
        {
            return false;
        }

        Section built = new Section();
        built.source = node;
        built.center = center;
        built.right = right;
        built.up = up;
        built.ring = ResampleClosedLoop(tempWorldLoop, ringSamples);
        built.vertexIndices = new int[ringSamples];
        built.isPointSection = isPointSection;
        built.pointWorld = pointWorld;
        built.pointVertexIndex = -1;
        built.usedByBridge = false;

        SmoothRingInPlace(built.ring, smoothingIterations, smoothingStrength);

        section = built;
        return true;
    }

    private void ResolveLoopBasis(Vector3 center, Vector3 previousCenter, Vector3 nextCenter, out Vector3 right, out Vector3 up)
    {
        if (loopPlaneMode == LoopPlaneMode.OffsetXZ)
        {
            right = Vector3.right;
            up = Vector3.forward;
            return;
        }

        if (loopPlaneMode == LoopPlaneMode.OffsetXY)
        {
            right = Vector3.right;
            up = Vector3.up;
            return;
        }

        if (loopPlaneMode == LoopPlaneMode.OffsetZY)
        {
            right = Vector3.forward;
            up = Vector3.up;
            return;
        }

        Vector3 axis = nextCenter - previousCenter;

        if (axis.sqrMagnitude < Epsilon)
        {
            axis = nextCenter - center;
        }

        if (axis.sqrMagnitude < Epsilon)
        {
            axis = center - previousCenter;
        }

        if (axis.sqrMagnitude < Epsilon)
        {
            axis = Vector3.up;
        }

        BuildPerpendicularBasis(axis.normalized, out right, out up);
    }

    private void BuildPerpendicularBasis(Vector3 axis, out Vector3 right, out Vector3 up)
    {
        Vector3 seed = Mathf.Abs(Vector3.Dot(axis, Vector3.up)) > 0.92f ? Vector3.right : Vector3.up;
        right = Vector3.Cross(seed, axis).normalized;

        if (right.sqrMagnitude < Epsilon)
        {
            right = Vector3.right;
        }

        up = Vector3.Cross(axis, right).normalized;

        if (up.sqrMagnitude < Epsilon)
        {
            up = Vector3.forward;
        }
    }

    private void AddProjectedUnique(List<ProjectedPoint> points, ProjectedPoint point, float tolerance)
    {
        float sqrTolerance = tolerance * tolerance;

        for (int i = 0; i < points.Count; i++)
        {
            if ((points[i].p2 - point.p2).sqrMagnitude <= sqrTolerance)
            {
                // Keep the farther 3D point if two offsets collapse to the same 2D projection.
                if (point.p3.sqrMagnitude > points[i].p3.sqrMagnitude)
                {
                    points[i] = point;
                }
                return;
            }
        }

        points.Add(point);
    }

    private void SortByAngle(List<ProjectedPoint> input, List<ProjectedPoint> output)
    {
        output.Clear();
        for (int i = 0; i < input.Count; i++)
        {
            output.Add(input[i]);
        }

        output.Sort(delegate (ProjectedPoint a, ProjectedPoint b)
        {
            float aa = Mathf.Atan2(a.p2.y, a.p2.x);
            float ab = Mathf.Atan2(b.p2.y, b.p2.x);
            return aa.CompareTo(ab);
        });
    }

    private void BuildConvexHull(List<ProjectedPoint> input, List<ProjectedPoint> output)
    {
        output.Clear();

        if (input.Count <= 3)
        {
            SortByAngle(input, output);
            return;
        }

        List<ProjectedPoint> sorted = new List<ProjectedPoint>(input);
        sorted.Sort(delegate (ProjectedPoint a, ProjectedPoint b)
        {
            int cx = a.p2.x.CompareTo(b.p2.x);
            if (cx != 0)
            {
                return cx;
            }
            return a.p2.y.CompareTo(b.p2.y);
        });

        List<ProjectedPoint> lower = new List<ProjectedPoint>();
        for (int i = 0; i < sorted.Count; i++)
        {
            while (lower.Count >= 2 && Cross(lower[lower.Count - 2].p2, lower[lower.Count - 1].p2, sorted[i].p2) <= 0f)
            {
                lower.RemoveAt(lower.Count - 1);
            }
            lower.Add(sorted[i]);
        }

        List<ProjectedPoint> upper = new List<ProjectedPoint>();
        for (int i = sorted.Count - 1; i >= 0; i--)
        {
            while (upper.Count >= 2 && Cross(upper[upper.Count - 2].p2, upper[upper.Count - 1].p2, sorted[i].p2) <= 0f)
            {
                upper.RemoveAt(upper.Count - 1);
            }
            upper.Add(sorted[i]);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);

        for (int i = 0; i < lower.Count; i++)
        {
            output.Add(lower[i]);
        }

        for (int i = 0; i < upper.Count; i++)
        {
            output.Add(upper[i]);
        }

        if (output.Count < 3)
        {
            SortByAngle(input, output);
        }
    }

    private float Cross(Vector2 origin, Vector2 a, Vector2 b)
    {
        Vector2 oa = a - origin;
        Vector2 ob = b - origin;
        return oa.x * ob.y - oa.y * ob.x;
    }

    private void BuildOnePointFallbackLoop(Vector3 point, Vector3 right, Vector3 up, List<Vector3> output)
    {
        output.Clear();
        int count = Mathf.Max(3, ringSamples);

        for (int i = 0; i < count; i++)
        {
            float angle = ((float)i / count) * Mathf.PI * 2f;
            Vector3 dir = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
            output.Add(point + dir * minimumFallbackRadius);
        }
    }

    private void BuildTwoPointFallbackLoop(Vector3 center, Vector3 a, Vector3 b, Vector3 right, Vector3 up, List<Vector3> output)
    {
        output.Clear();

        Vector3 line = b - a;
        Vector3 mid = (a + b) * 0.5f;
        Vector3 side = Vector3.Cross(line, (mid - center));

        if (side.sqrMagnitude < Epsilon)
        {
            side = right;
        }

        side = Vector3.ProjectOnPlane(side, line.sqrMagnitude > Epsilon ? line.normalized : up);

        if (side.sqrMagnitude < Epsilon)
        {
            side = up;
        }

        side.Normalize();
        float r = minimumFallbackRadius;

        output.Add(a + side * r);
        output.Add(b + side * r);
        output.Add(b - side * r);
        output.Add(a - side * r);
    }

    private Vector3[] ResampleClosedLoop(List<Vector3> source, int sampleCount)
    {
        sampleCount = Mathf.Max(3, sampleCount);
        Vector3[] result = new Vector3[sampleCount];

        if (source.Count == sampleCount)
        {
            for (int i = 0; i < sampleCount; i++)
            {
                result[i] = source[i];
            }
            return result;
        }

        float perimeter = 0f;
        for (int i = 0; i < source.Count; i++)
        {
            Vector3 a = source[i];
            Vector3 b = source[(i + 1) % source.Count];
            perimeter += Vector3.Distance(a, b);
        }

        if (perimeter < Epsilon)
        {
            for (int i = 0; i < sampleCount; i++)
            {
                result[i] = source[0];
            }
            return result;
        }

        for (int sample = 0; sample < sampleCount; sample++)
        {
            float target = perimeter * ((float)sample / sampleCount);
            float travelled = 0f;

            for (int edge = 0; edge < source.Count; edge++)
            {
                Vector3 a = source[edge];
                Vector3 b = source[(edge + 1) % source.Count];
                float length = Vector3.Distance(a, b);

                if (travelled + length >= target || edge == source.Count - 1)
                {
                    float t = length > Epsilon ? (target - travelled) / length : 0f;
                    result[sample] = Vector3.Lerp(a, b, Mathf.Clamp01(t));
                    break;
                }

                travelled += length;
            }
        }

        return result;
    }

    private void SmoothRingInPlace(Vector3[] ring, int iterations, float strength)
    {
        if (ring == null || ring.Length < 3 || iterations <= 0 || strength <= 0f)
        {
            return;
        }

        Vector3[] temp = new Vector3[ring.Length];

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            for (int i = 0; i < ring.Length; i++)
            {
                Vector3 previous = ring[(i - 1 + ring.Length) % ring.Length];
                Vector3 next = ring[(i + 1) % ring.Length];
                Vector3 average = (previous + next) * 0.5f;
                temp[i] = Vector3.Lerp(ring[i], average, strength);
            }

            for (int i = 0; i < ring.Length; i++)
            {
                ring[i] = temp[i];
            }
        }
    }


    private void AlignAdjacentSectionRings()
    {
        for (int s = 1; s < sections.Count; s++)
        {
            Section previous = sections[s - 1];
            Section current = sections[s];

            if (previous == null || current == null || previous.ring == null || current.ring == null)
            {
                continue;
            }

            if (previous.ring.Length < 3 || current.ring.Length != previous.ring.Length)
            {
                continue;
            }

            AlignRingToReference(current.ring, previous.ring);
        }
    }

    private void AlignRingToReference(Vector3[] ring, Vector3[] reference)
    {
        int count = Mathf.Min(ring != null ? ring.Length : 0, reference != null ? reference.Length : 0);
        if (count < 3)
        {
            return;
        }

        int bestShift = 0;
        bool bestReverse = false;
        float bestCost = float.PositiveInfinity;

        for (int reverseFlag = 0; reverseFlag < 2; reverseFlag++)
        {
            bool reverse = reverseFlag == 1;

            for (int shift = 0; shift < count; shift++)
            {
                float cost = 0f;

                for (int i = 0; i < count; i++)
                {
                    int sourceIndex = reverse
                        ? PositiveModulo(shift - i, count)
                        : (shift + i) % count;

                    cost += (ring[sourceIndex] - reference[i]).sqrMagnitude;

                    if (cost >= bestCost)
                    {
                        break;
                    }
                }

                if (cost < bestCost)
                {
                    bestCost = cost;
                    bestShift = shift;
                    bestReverse = reverse;
                }
            }
        }

        if (!bestReverse && bestShift == 0)
        {
            return;
        }

        Vector3[] aligned = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            int sourceIndex = bestReverse
                ? PositiveModulo(bestShift - i, count)
                : (bestShift + i) % count;

            aligned[i] = ring[sourceIndex];
        }

        for (int i = 0; i < count; i++)
        {
            ring[i] = aligned[i];
        }
    }

    private int PositiveModulo(int value, int modulo)
    {
        int result = value % modulo;
        return result < 0 ? result + modulo : result;
    }

    private bool BuildMeshFromSections(bool runtimeBuild)
    {
        meshVertices.Clear();
        meshTriangles.Clear();
        meshUvs.Clear();

        if (sections.Count == 0)
        {
            if (!ShouldKeepPreviousMeshOnRuntimeFailure(runtimeBuild))
            {
                ClearMesh();
            }
            return false;
        }

        if (alignAdjacentSectionRings && sections.Count > 1)
        {
            AlignAdjacentSectionRings();
        }

        for (int s = 0; s < sections.Count; s++)
        {
            Section section = sections[s];
            section.usedByBridge = false;
            section.vertexIndices = new int[ringSamples];

            for (int r = 0; r < ringSamples; r++)
            {
                section.vertexIndices[r] = meshVertices.Count;
                meshVertices.Add(transform.InverseTransformPoint(section.ring[r]));

                if (generateUVs)
                {
                    meshUvs.Add(new Vector2((float)r / ringSamples, sections.Count > 1 ? (float)s / (sections.Count - 1) : 0f));
                }
            }

            if (section.isPointSection)
            {
                section.pointVertexIndex = meshVertices.Count;
                meshVertices.Add(transform.InverseTransformPoint(section.pointWorld));

                if (generateUVs)
                {
                    meshUvs.Add(new Vector2(0.5f, sections.Count > 1 ? (float)s / (sections.Count - 1) : 0f));
                }
            }
        }

        List<int> connectedSectionIndices = new List<int>();

        for (int s = 0; s < sections.Count - 1; s++)
        {
            Section a = sections[s];
            Section b = sections[s + 1];
            float distance = Vector3.Distance(a.center, b.center);

            if (maxBridgeDistance > 0f && distance > maxBridgeDistance)
            {
                Log("Skipped bridge " + s + " -> " + (s + 1) + " because distance " + distance.ToString("0.###") + " > " + maxBridgeDistance.ToString("0.###"));
                continue;
            }

            a.usedByBridge = true;
            b.usedByBridge = true;
            AddUniqueInt(connectedSectionIndices, s);
            AddUniqueInt(connectedSectionIndices, s + 1);
            AddBridge(a, b);
        }

        if (sections.Count == 1 && capSingleSection)
        {
            AddCap(sections[0], true);
            sections[0].usedByBridge = true;
        }
        else if (capFirstAndLastOpenEnds && connectedSectionIndices.Count > 0)
        {
            int first = connectedSectionIndices[0];
            int last = connectedSectionIndices[connectedSectionIndices.Count - 1];

            AddCap(sections[first], true);

            if (last != first)
            {
                AddCap(sections[last], false);
            }
        }

        if (meshTriangles.Count == 0)
        {
            if (!ShouldKeepPreviousMeshOnRuntimeFailure(runtimeBuild))
            {
                ClearMesh();
            }
            return false;
        }

        if (flipWinding)
        {
            FlipTriangleWinding(meshTriangles);
        }

        if (doubleSided)
        {
            AddBackFaces(meshTriangles);
        }

        Mesh mesh = GetWritableMesh(runtimeBuild);
        mesh.Clear();
        mesh.name = "Meshing Offset Loft Mesh";
        mesh.indexFormat = meshVertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;

        if (rebuildEveryFrameInPlayMode)
        {
            mesh.MarkDynamic();
        }

        mesh.SetVertices(meshVertices);

        if (generateUVs)
        {
            mesh.SetUVs(0, meshUvs);
        }

        mesh.SetTriangles(meshTriangles, 0);

        if (recalculateNormals)
        {
            mesh.RecalculateNormals();
        }

        if (recalculateBounds)
        {
            mesh.RecalculateBounds();
        }

        CommitBuiltMesh(mesh, runtimeBuild);

        if (optionalMeshCollider != null)
        {
            optionalMeshCollider.cookingOptions = MeshColliderCookingOptions.EnableMeshCleaning |
                                             MeshColliderCookingOptions.WeldColocatedVertices |
                                             MeshColliderCookingOptions.CookForFasterSimulation;
            optionalMeshCollider.sharedMesh = null;
            optionalMeshCollider.sharedMesh = mesh;
        }

        hasBuiltValidMesh = true;
        Log("Built mesh: vertices=" + meshVertices.Count + ", triangles=" + (meshTriangles.Count / 3));
        return true;
    }

    private Mesh GetWritableMesh(bool runtimeBuild)
    {
        if (!runtimeBuild || !keepLastValidRuntimeMeshOnBuildFailure || meshFilter == null || meshFilter.sharedMesh == null)
        {
            return GetOrCreateMesh();
        }

        if (runtimeStagingMesh == null)
        {
            runtimeStagingMesh = new Mesh();
            runtimeStagingMesh.name = "Meshing Offset Loft Mesh";
        }

        return runtimeStagingMesh;
    }

    private void CommitBuiltMesh(Mesh mesh, bool runtimeBuild)
    {
        if (meshFilter == null || mesh == null)
        {
            return;
        }

        Mesh previouslyVisible = meshFilter.sharedMesh;
        meshFilter.sharedMesh = mesh;

        if (runtimeBuild && keepLastValidRuntimeMeshOnBuildFailure && previouslyVisible != null && previouslyVisible != mesh)
        {
            runtimeStagingMesh = previouslyVisible;
        }

        generatedMesh = mesh;
    }

    private bool ShouldKeepPreviousMeshOnRuntimeFailure(bool runtimeBuild)
    {
        return runtimeBuild && keepLastValidRuntimeMeshOnBuildFailure && hasBuiltValidMesh && meshFilter != null && meshFilter.sharedMesh != null;
    }

    private void AddBridge(Section a, Section b)
    {
        if (a.isPointSection && b.isPointSection)
        {
            AddPointToPointBridge(a, b);
            return;
        }

        if (a.isPointSection)
        {
            AddPointToRingBridge(a, b, true);
            return;
        }

        if (b.isPointSection)
        {
            AddPointToRingBridge(b, a, false);
            return;
        }

        for (int r = 0; r < ringSamples; r++)
        {
            int next = (r + 1) % ringSamples;

            int a0 = a.vertexIndices[r];
            int a1 = a.vertexIndices[next];
            int b0 = b.vertexIndices[r];
            int b1 = b.vertexIndices[next];

            meshTriangles.Add(a0);
            meshTriangles.Add(a1);
            meshTriangles.Add(b1);

            meshTriangles.Add(a0);
            meshTriangles.Add(b1);
            meshTriangles.Add(b0);
        }
    }

    private void AddPointToRingBridge(Section pointSection, Section ringSection, bool pointIsStart)
    {
        int pointIndex = pointSection.pointVertexIndex;

        if (pointIndex < 0)
        {
            return;
        }

        for (int r = 0; r < ringSamples; r++)
        {
            int next = (r + 1) % ringSamples;
            int a = ringSection.vertexIndices[r];
            int b = ringSection.vertexIndices[next];

            if (pointIsStart)
            {
                meshTriangles.Add(pointIndex);
                meshTriangles.Add(a);
                meshTriangles.Add(b);
            }
            else
            {
                meshTriangles.Add(pointIndex);
                meshTriangles.Add(b);
                meshTriangles.Add(a);
            }
        }
    }

    private void AddPointToPointBridge(Section a, Section b)
    {
        if (a.pointVertexIndex < 0 || b.pointVertexIndex < 0)
        {
            return;
        }

        Vector3 axis = b.pointWorld - a.pointWorld;
        if (axis.sqrMagnitude < Epsilon)
        {
            return;
        }

        Vector3 right;
        Vector3 up;
        BuildPerpendicularBasis(axis.normalized, out right, out up);

        int a0 = meshVertices.Count;
        meshVertices.Add(transform.InverseTransformPoint(a.pointWorld + right * minimumFallbackRadius));
        int a1 = meshVertices.Count;
        meshVertices.Add(transform.InverseTransformPoint(a.pointWorld - right * minimumFallbackRadius));
        int b0 = meshVertices.Count;
        meshVertices.Add(transform.InverseTransformPoint(b.pointWorld + right * minimumFallbackRadius));
        int b1 = meshVertices.Count;
        meshVertices.Add(transform.InverseTransformPoint(b.pointWorld - right * minimumFallbackRadius));

        if (generateUVs)
        {
            meshUvs.Add(new Vector2(0f, 0f));
            meshUvs.Add(new Vector2(1f, 0f));
            meshUvs.Add(new Vector2(0f, 1f));
            meshUvs.Add(new Vector2(1f, 1f));
        }

        meshTriangles.Add(a0);
        meshTriangles.Add(b0);
        meshTriangles.Add(b1);

        meshTriangles.Add(a0);
        meshTriangles.Add(b1);
        meshTriangles.Add(a1);
    }

    private void AddCap(Section section, bool isStartCap)
    {
        int centerIndex = meshVertices.Count;
        meshVertices.Add(transform.InverseTransformPoint(section.center));

        if (generateUVs)
        {
            meshUvs.Add(new Vector2(0.5f, 0.5f));
        }

        for (int r = 0; r < ringSamples; r++)
        {
            int next = (r + 1) % ringSamples;
            int a = section.vertexIndices[r];
            int b = section.vertexIndices[next];

            if (isStartCap)
            {
                meshTriangles.Add(centerIndex);
                meshTriangles.Add(b);
                meshTriangles.Add(a);
            }
            else
            {
                meshTriangles.Add(centerIndex);
                meshTriangles.Add(a);
                meshTriangles.Add(b);
            }
        }
    }

    private void AddUniqueInt(List<int> values, int value)
    {
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] == value)
            {
                return;
            }
        }

        values.Add(value);
    }

    private void FlipTriangleWinding(List<int> triangles)
    {
        for (int i = 0; i < triangles.Count; i += 3)
        {
            int temp = triangles[i + 1];
            triangles[i + 1] = triangles[i + 2];
            triangles[i + 2] = temp;
        }
    }

    private void AddBackFaces(List<int> triangles)
    {
        int originalCount = triangles.Count;

        for (int i = 0; i < originalCount; i += 3)
        {
            triangles.Add(triangles[i]);
            triangles.Add(triangles[i + 2]);
            triangles.Add(triangles[i + 1]);
        }
    }

    private Mesh GetOrCreateMesh()
    {
        if (generatedMesh != null)
        {
            return generatedMesh;
        }

        if (meshFilter != null && meshFilter.sharedMesh != null && meshFilter.sharedMesh.name == "Meshing Offset Loft Mesh")
        {
            generatedMesh = meshFilter.sharedMesh;
            return generatedMesh;
        }

        generatedMesh = new Mesh();
        generatedMesh.name = "Meshing Offset Loft Mesh";
        return generatedMesh;
    }

    private void Log(string message)
    {
        if (debugLogging)
        {
            Debug.Log("[MeshingOffsetLoftMeshBuilder:" + name + "] " + message, this);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawGizmos)
        {
            return;
        }

        Gizmos.color = sectionGizmoColor;
        for (int s = 0; s < sections.Count; s++)
        {
            Section section = sections[s];
            if (section == null || section.ring == null)
            {
                continue;
            }

            Gizmos.DrawWireSphere(section.center, 0.025f);

            for (int r = 0; r < section.ring.Length; r++)
            {
                Vector3 a = section.ring[r];
                Vector3 b = section.ring[(r + 1) % section.ring.Length];
                Gizmos.DrawLine(a, b);
            }
        }

        Gizmos.color = bridgeGizmoColor;
        for (int s = 0; s < sections.Count - 1; s++)
        {
            if (!sections[s].usedByBridge || !sections[s + 1].usedByBridge)
            {
                continue;
            }

            Gizmos.DrawLine(sections[s].center, sections[s + 1].center);
        }
    }
}
