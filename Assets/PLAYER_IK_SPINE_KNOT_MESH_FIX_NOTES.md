# PLAYER IK SPINE KNOT MESH FIX

Base package: PlayerIK_SpineMeshRestorePass_Assets.zip

Scope: mesh-only spine knot / twisted middle-section fix.

Changed file:
- Assets/Player/Rotation Behaviour/MeshingOffsetLoftMeshBuilder.cs

What changed:
- Added `alignAdjacentSectionRings`, enabled by default.
- Before bridging loft sections, every section ring is phase-aligned and, if needed, winding-aligned to the previous section.
- This prevents a middle section from bridging vertex 0 to the wrong side of the next ring when the IK nodes are correct but the convex hull / projected loop chooses a different starting vertex or winding.

Why this is narrow:
- No fake target logic changed.
- No LimbSolver logic changed.
- No spine target ownership changed.
- No leg, gait, item, spear, collision, camera, or movement code changed.

Unity check:
- Open Player_NEW, enter play mode, and inspect only the spine mesh.
- If the IK nodes are in correct positions but the visible middle spine knot is gone, this was the ring-order/bridge issue.
