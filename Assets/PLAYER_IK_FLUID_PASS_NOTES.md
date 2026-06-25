# PLAYER_NEW Fluid Gait / Rotation Pass

Fresh pass made from `Assets(2).zip`.

## Main intent

Keep the current new player rig intact, but stop the legs from behaving like small reach-correction widgets. The walking controller is now biased toward an explicit planted-foot gait:

1. A foot stays planted while it is reachable.
2. The current lead foot is allowed to pass behind the body.
3. Only then does the opposite foot take one larger anticipated step in the movement/run-target direction.
4. The landing point is clamped by the actual IK start-to-foot reach, not by body-core height.
5. The fake target follows the step arc, but idle fake targets settle exactly once they are already close enough.

## Changed files

- `Assets/Player/BodyIKTargets/AutoRunLegPairController.cs`
- `Assets/Player/RotatableNodes/RotationAssigner.cs`
- `Assets/Player/RotatableNodes/RotatableNodePair.cs`
- `Assets/Scripts/ProceduralPlayerRig.cs`
- `Assets/Prefab_objects/Player_NEW.prefab`

## Leg/gait changes

- Added strict alternating planted gait mode.
- Disabled startup cosmetic lead-step by default.
- Disabled active moving step retargeting by default so active steps do not chase a moving home point every frame.
- Raised normal walking cadence to about one larger step beat instead of many tiny corrections.
- Made emergency reach stepping a last-resort safety valve instead of the main gait driver.
- Real/planted targets are clamped using the leg start/IK hip reach, with ground height preserved where possible.
- Body/core distance is no longer treated as a full 3D hard foot reach because body height was consuming reach and pulling feet upward.
- Step destination now uses body core + per-leg side lane + forward movement anticipation, so sidesteps/backsteps still take directionally useful steps.
- Idle fake targets now stop filtering/bobbing when they are within a tolerance of the real/planted target.
- Step lift and step length scaling were increased so a real step has visible knee bend and height.

## Rotation changes

- `gaitRotationCore` on `PLAYER_NEW.prefab` now points at `Node (4)` instead of the LegCore object.
- `RotationAssigner` ignores disabled node pairs/fake offset parents when distributing rotation.
- `AutoRunLegPairController` now forces the accepted gait yaw directly into the lower-body rotatable nodes after the assigner accepts the angle.
- Lower-body rotatable nodes have their references refreshed without reinitializing their radius every frame.
- This specifically targets the case where `RotationAssigner.inputRotationDegrees` changes but the leg start/local pole `RotatableNode.localRotationDegrees` values do not visibly update.

## Scaling / spine changes

- Spine fake target plane heights, body-anchored distances, and world-zero guard radius are scaled with authored value scale.
- This targets the small-scale case where the spine target boxes appear to bend the spine sideways or behave differently from the larger scale.

## Jump / dynamics

- Jump velocity is raised to 4.8 in script and prefab.
- Core velocity dynamics are kept snappy/critically damped in `ProceduralPlayerRig.ConfigureMovementSpeed`.
- Movement momentum affects stride size, not player-body UFO drift.

## Unity validation checklist

1. Open `Player_NEW.prefab` and verify `AutoRunLegPairController.gaitRotationCore` is `Node (4)`.
2. Hold Shift and move the mouse. Check `RotationAssigner.inputRotationDegrees` and each lower-body `RotatableNode.localRotationDegrees` value.
3. Verify the left/right leg starts and local poles move around `Node (4)`, not around their own offset parents.
4. Walk forward: the lead foot should stay planted until it trails behind, then the other foot should take one large forward step.
5. Strafe/backstep: targets should land in the movement/run-target direction plus side lane, not only along world Z.
6. Stop moving: real and fake targets should remain planted if they are already within reach/tolerance.
7. Test small player scale: spine fake target should remain centered inside safe boxes instead of bending sideways.
8. Test jump/landing and item hold/throw paths to ensure no unrelated systems were broken.

## Limitation

This package was edited structurally outside the Unity editor. It has not been play-mode verified in this environment.
