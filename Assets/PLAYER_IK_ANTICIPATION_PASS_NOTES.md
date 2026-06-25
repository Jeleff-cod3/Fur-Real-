# PLAYER_NEW IK Anticipation Pass

This package is based on the latest FluidPass archive and focuses specifically on the walking/leg gait complaint: feet were technically constrained, but they were not walking. They were still behaving like correction handles.

## Main intent

The moving gait is now explicit:

1. Feet stay planted while the body moves.
2. The foot that becomes the rear foot behind the body is the foot that swings.
3. That foot lands in front of the body along the actual movement/run-target direction.
4. The landing point includes a side lane for the leg, so side/back movement produces a visible zig-zag stance instead of a centerline shuffle.
5. The real target is kept reachable; the fake target follows a deliberate arc and settles exactly when planted.

## Changed files

- `Assets/Player/BodyIKTargets/AutoRunLegPairController.cs`
- `Assets/Prefab_objects/Player_NEW.prefab`

## Important behavior changes

- Replaced the old strict gait branch that stepped the opposite of the stored `leadingLegIndex`. The new branch looks at actual planted foot positions in the movement direction and swings the most-rear foot forward.
- Movement debt no longer creates normal gait steps except as a high-threshold startup valve. This removes the micro-step train.
- Added reach-aware forward destination projection. It computes side lane and forward landing from body center, then clamps within leg reach while preserving as much forward-ahead distance as possible.
- Raised the required ahead distance. If reach/ground projection would make a destination not actually land in front of the body, the step is rejected instead of generating a useless little shuffle.
- Disabled core movement foot-reach constraining by default so the body does not stagger to hide broken gait. The legs must step; the body should not twitch/wait for them.
- Tightened emergency reach behavior so it is a last-resort safety valve, not a normal walking driver.
- Active steps write the generated arc directly, so feet lift visibly and do not lazily drag behind the moving body.
- Tuned `PLAYER_NEW.prefab` toward fewer, bigger, higher steps: larger forward stride, larger side lane, stronger lift, lower fake-target lag, stricter minimum step size.

## First Unity validation

1. In Play Mode, show left/right real targets and fake targets.
2. Move forward: the rear foot should remain planted until it is behind the body, then swing to a target in front of `Node (4)`.
3. Stop moving: both fake targets should settle onto their real targets and stop bobbing.
4. Side-step/back-step: the swing target should still be ahead along the actual run-target/movement direction, with the leg lane offset sideways.
5. Hold Shift and rotate the gait direction: leg starts and front poles should receive non-zero lower-body rotatable offsets from the existing gait rotation assigner.

## Known limitation

This was patched without Unity play-mode execution in this environment. The code is structured to compile, but the exact visual feel still needs in-editor tuning of the prefab ratios if the rig scale or body height differs from the uploaded prefab.
