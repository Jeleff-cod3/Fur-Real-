# PLAYER IK SPINE CURVATURE STYLE RESTORE

Focused-only patch after the spine knot mesh fix.

Changed:
- `Assets/Player/IK/LimbSolver.cs`
- `Assets/Prefab_objects/Player_NEW.prefab`

Intent:
- Keep the now-working `SpineFakeTargetSetter` target behavior.
- Keep the spine mesh ring/knot fix.
- Restore the old visual spine curvature style by treating the spine fake target as an IK handle, not as a visible straight mesh endpoint.

Details:
- `LimbSolver` now lets `restoreTailToSolvedEndAfterSolving` work even when `tailTargetOverride` is assigned.
- For the spine solver on `SpineRotatable`, `restoreTailToSolvedEndAfterSolving` is set back to `true`.
- For that spine solver, visible-tail-at-target override behavior is disabled so the fake target still drives solving but the visible spine tail returns to the solved chain end, matching the previous bend distribution better.

Not touched:
- walking/gait
- item pickup/spear
- collision
- camera
- fake target box rules
- mesh section ring alignment
