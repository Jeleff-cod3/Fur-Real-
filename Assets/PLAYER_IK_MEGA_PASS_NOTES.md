# PLAYER_NEW IK mega pass

This pass was applied to the fresh `Assets(1).zip` upload.

## Main fixes

### 1. Torso / spine IK left behind at world zero
- `AutoRunLegPairController.AutoResolvePostCoreMoveIk()` no longer trusts a partially wired prefab `ikNodesSyncedWithCoreDelta` array.
- It now merges every node from the refreshed spine solver chain into the core-delta sync list.
- This prevents the common failure where `Node (4)` and the tail move but intermediate torso IK nodes remain at their authored/world-zero positions.

### 2. Leg assembly rotation not propagating
- Disabled the `DirectTargetRotationAssigner` on `LegCore` in `Player_NEW.prefab`; it was still able to overwrite the gait `RotationAssigner`.
- Runtime also disables any direct assigner targeting the gait rotation assigner.
- Lower-body rotatable nodes are initialized only after `LegCore` is snapped onto `Node (4)`.
- Before capturing each lower-body rotatable node's initial radius, its `OffsetPositioningNode` is applied first, so the captured unrotated pose is the actual Node-4-relative offset, not a stale prefab transform.
- `DriveGaitRotationAssigner()` now immediately applies the gait rotation offsets after writing the yaw.

### 3. Front pole vectors restored
- Static leg poles remain in front and are also the physical IK poles again.
- `flipPhysicalIkPoleBehindStaticPole` is forced off for both legs.
- The pole offset Z is kept positive when initialization runs.

### 4. Deterministic reachable real targets
- Emergency reach checks now use the planted/real target as the source of truth, not the smoothed fake target.
- Fake-target lag no longer causes recovery steps by itself.
- Real/planted targets are clamped through actual usable leg reach; the old planar minimum reach override was removed because it could intentionally place the foot farther than the real 3D IK chain could reach.
- If a planted target drifts past the allowed reach, it is clamped and the real target is kept in sync.

### 5. Bigger anticipatory stepping without pixel-step spam
- Home points are now computed as: body/core lane + per-leg side lane + movement/run-target forward anticipation.
- Step triggering uses planted/real target distance to the deterministic home, not fake-target distance.
- Stride size grows with speed, while cadence remains around half-second high-speed stepping and slower correction/idle stepping.
- Side lanes have a minimum width so sideways movement produces visible zig-zag stepping instead of both feet collapsing into the same line.

### 6. Idle fake target bobbing
- Fake foot targets now settle to the desired point when within a tolerance based on world distance or a small fraction of leg reach.
- While idle, this prevents the fake target from repeatedly springing around a real target it has already reached.

## Changed files
- `Assets/Player/BodyIKTargets/AutoRunLegPairController.cs`
- `Assets/Scripts/ProceduralPlayerRig.cs`
- `Assets/Prefab_objects/Player_NEW.prefab`

## Unity validation checklist
1. Open `Player_NEW.prefab` and confirm `LegCore`'s `DirectTargetRotationAssigner` is disabled.
2. Enter play mode and hold Shift/move mouse. Check that `RotationAssigner.inputRotationDegrees` changes and the four lower-body `RotatableNode.localRotationDegrees` values change.
3. Watch the left/right leg start nodes and static poles. They should orbit `Node (4)` instead of staying at their prefab/world positions.
4. Move forward, backward, and sideways. Real targets should land ahead of the body in the movement/run-target direction, offset per leg, and should not remain outside usable leg reach.
5. Stand still. Fake targets should settle instead of bobbing up and down.
6. Watch the spine solver intermediate nodes while moving. They should receive core delta and should not sit at world zero.

## Not play-mode verified here
This environment cannot run the Unity editor, so the package is a structural/code/prefab patch. Compile/play-mode testing still has to happen inside Unity.
