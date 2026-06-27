# PLAYER IK SPINE MESH RESTORE PASS

Base package: `PlayerIK_SpineMeshEmergencyFix_Assets.zip`.

Scope: spine IK/mesh regression only. No gait rewrite, no item/spear changes, no body movement changes.

## Root cause targeted

The emergency pass made the visible spine tail follow the fake spine target, but the generic `LimbSolver` endpoint pre-translation was also enabled for every solver. That pre-translation is useful for fast legs, but unsafe for the spine when it uses a separate `tailTargetOverride` fake target. The solver was allowed to move intermediate spine nodes around a separate high target handle before the chain solve rebuilt the chain, which could create knots, clipping, and apparent teleporting even while static.

The spine also has old serialized short bone lengths in the prefab (`0.1`-style values) while the authored spine node spacing is about one unit per section. If a runtime init ever trusts stale serialized values, the spine solves as a tiny chain and the visible mesh collapses into a knot.

## Changes

### `Assets/Player/IK/LimbSolver.cs`

- Added `preTranslateWhenUsingTailTargetOverride` and defaulted it to false.
- `PreTranslateIntermediateNodesForEndpointMotion()` now skips pre-translation when a separate target override is assigned, unless explicitly opted in.
- The visible tail is no longer moved to the override target before solving.
- The visible tail is moved to the clamped override target after the chain has placed intermediate nodes.
- Added stale bone-length repair when `captureBoneLengthsOnInitialize` is enabled.
  - If serialized chain lengths are implausibly smaller than the current authored pose, the solver recaptures lengths from the current transform pose.
  - This protects the spine from solving a visually 4-unit chain as a 0.4-unit chain.

### `Assets/Player/BodyIKTargets/SpineFakeTargetSetter.cs`

- When wiring the spine solver, explicitly keeps the fake target setter as the fake target owner.
- Forces tail-override solvers to keep visible-tail placement after solving and to disable endpoint pre-translation around the fake target.
- Enables stale bone-length repair for the spine solver.

### `Assets/Scripts/ProceduralPlayerRig.cs`

- Rig scheduling still manages child solvers, but solvers with `tailTargetOverride` now keep fake target ownership, place the visible tail after solving, and disable override pre-translation.

## What should be preserved

- Fake spine target behavior remains inside `SpineFakeTargetSetter`.
- The solver does not write/clamp back into the setter-owned fake target.
- Walking/gait, pickup, spear, collision, and body movement code were not reworked in this pass.

## Unity validation

1. Import package and let it compile.
2. Open `Player_NEW.prefab` and save once so new `LimbSolver` fields serialize.
3. In play mode, inspect the spine solver:
   - `tailTargetOverride` should point at `TargetSpine`.
   - `writeClampedTailTargetBackToTransform` should be false.
   - `keepVisibleTailAtTargetWhenUsingOverride` should be true.
   - `moveVisibleTailToOverrideTargetAfterSolving` should be true.
   - `preTranslateWhenUsingTailTargetOverride` should be false.
4. Watch spine nodes while static and while aiming. Intermediate nodes should remain chained, not orbit/knot/teleport.
