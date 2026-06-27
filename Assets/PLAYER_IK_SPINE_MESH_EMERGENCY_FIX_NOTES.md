# PLAYER IK Spine/Mesh Emergency Fix

This package fixes the regression where the spine/body mesh disappeared or rendered under the map after the previous spine target ownership pass.

## Root cause fixed

The previous pass made the spine solver read `SpineFakeTargetSetter.fakeTarget` through `tailTargetOverride` without moving the visible spine tail node to the clamped solve endpoint. The fake target was protected, but the mesh still depends on the visible IK tail/node transforms. That left the visible spine chain using stale startup positions or collapsed positions, which could make the mesh disappear or appear under the map.

## Changes

- `Assets/Player/IK/LimbSolver.cs`
  - Added `keepVisibleTailAtTargetWhenUsingOverride`.
  - The solver still does **not** write/clamp the setter-owned fake target.
  - When `tailTargetOverride` is used, the visible `tail` node is moved to the internally clamped solve endpoint so meshing has a valid endpoint every frame.
  - Tail restore/collapse is skipped when an override is active.

- `Assets/Player/BodyIKTargets/SpineFakeTargetSetter.cs`
  - Spine setter now forces the linked solver into the safe mode:
    - fake target override is read-only to the solver;
    - visible tail follows the clamped endpoint;
    - no restore-to-next-node collapse.
  - Added `anchorForwardPoleOffsetToCore` so the spine behavior boxes use a core-anchored static forward pole. This fixes the circular startup issue where the forward box pole was offset from the solved spine tail before the spine had solved, which could lock the spine into a bad side bend.

- `Assets/Prefab_objects/Player_NEW.prefab`
  - Disabled the spine solver's tail restore flag.
  - Serialized the safe override/visible-tail fields on the spine solver.
  - Re-anchored `PolevectorBody` offset parent from the solved spine top node to `Node (4)`, so box placement is stable at startup.

## What this intentionally does not touch

- Walking/gait rules.
- Item pickup/spear logic.
- Body movement speed.
- External body unstuck rules.

The fake spine target remains owned by `SpineFakeTargetSetter`; the solver only moves the visible IK tail node needed by the mesh.
