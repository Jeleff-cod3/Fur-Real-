# PLAYER IK FINAL RESCUE PASS

Base: latest dynamic-step package.

This pass deliberately removes the layered/competing normal gait logic and keeps one locomotion rule active:

1. Feet stay planted while reachable.
2. While moving, the current leading/support foot is allowed to pass behind the body.
3. Once that lead relationship is behind enough and cadence is ready, the opposite foot gets one committed real landing ahead of Node (4), offset into its side lane.
4. The fake target travels to that committed landing through the step arc; active step endpoints do not chase a moving home.
5. Emergency reach is only a last-resort hard safety, not a normal step starter.

Changed files:

- Assets/Player/BodyIKTargets/AutoRunLegPairController.cs
- Assets/Scripts/ProceduralPlayerRig.cs
- Assets/Player/BodyIKTargets/SpineFakeTargetSetter.cs
- Assets/Prefab_objects/Player_NEW.prefab

## Leg gait changes

- Replaced the normal locomotion path with `useSingleRuleAnticipatoryGait`.
- Restored the old working leading-foot trigger concept from Assets2: normal steps are cadence-gated and alternate; movement debt/home correction no longer starts random micro-steps.
- Step destination is explicitly `body/core + movementForward * aheadStride + sideLane` and then grounded.
- Removed the final generic reach clamp from the final gait path because it could erase the forward component after the ahead landing was calculated, producing a lifted foot that still stayed behind.
- Real targets are committed at step start; fake targets travel through the arc.
- Active swing fake target path is kept deterministic rather than retargeting/chasing moving homes.
- Step cadence, step duration, and fake target response were made faster, while stride stays large and speed-scaled.

## Leg assembly rotation changes

- Hard fallback now sets lower-body `RotatableNode.localRotationDegrees` directly on both leg starts and both local poles.
- The same yaw is also written into each lower-body `OffsetPositioningNode` dynamic offset and applied immediately.
- This means Shift/gait yaw no longer depends only on the serialized `RotatableNodePair` successfully distributing rotation. If the assigner receives an angle, the leg starts and poles should receive visible local rotation and world offset changes.

## Spine scale fix

- Added a minimum reach-relative no-clip ring for `SpineFakeTargetSetter` so scaling down the player cannot collapse the fake spine target through the core and produce left-bend/spin artifacts.
- Added a minimum world-zero guard radius after scaling.
- Runtime scaling now preserves a minimum pole distance and no-clip radius.

## Jump / body support

- Increased jump velocity from 4.8 to 6.4.
- Restored snappier ground-height springing without returning to UFO-like float.

## Item interaction

- Existing pickup/weapon/item/combat scripts were inspected. They already use `ProceduralPlayerRig.WeaponHolder` / `ItemHolder` when available and fall back to runtime child holders if missing.
- No behavior rewrite was needed; the rig holder update remains intact so pickup, holding, melee windup and spear throw still use the same anchors.

## Unity validation checklist

1. Open `PLAYER_NEW.prefab` and save it so Unity serializes the new fields.
2. In play mode, hold Shift and move the mouse. Confirm:
   - `gaitRotationAssigner.inputRotationDegrees` changes.
   - Each lower-body `RotatableNode.localRotationDegrees` changes.
   - Both leg start nodes and both leg pole nodes move around Node (4).
3. Move forward at low speed:
   - feet should stay planted until the cadence/behind trigger fires.
   - the real target should jump to a grounded point in front of Node (4).
   - the fake target should arc quickly to that point.
4. Stop moving:
   - planted feet should stay planted if within reach.
   - fake targets should settle instead of bobbing up/down.
5. Scale player smaller:
   - spine fake target should not collapse left or spin around the core.
6. Test E pickup/drop, left-click melee, and right-click spear throw.
