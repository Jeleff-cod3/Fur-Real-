# Player IK Spine / Leg Target Ownership Fix

Base package: `PlayerIK_RootCausePass_Assets.zip`.

## Scope
This pass deliberately avoids changing the working gait rules, item pickup, body movement speed, rotation wiring, or held-object behavior. It targets only the two issues requested:

1. spine fake target / spine IK startup softlock;
2. leg IK chain / fake target lag at small scale and high movement speed.

## Changed files
- `Assets/Player/BodyIKTargets/SpineFakeTargetSetter.cs`
- `Assets/Player/IK/LimbSolver.cs`
- `Assets/Player/BodyIKTargets/AutoRunLegPairController.cs`
- `Assets/Scripts/ProceduralPlayerRig.cs`

## Spine fake target ownership
The important root-cause change is in `LimbSolver`.

Before this pass, `SpineFakeTargetSetter` wrote the fake spine target, but `LimbSolver` could immediately clamp `tailTargetOverride.position`. Because the spine fake target is used as `tailTargetOverride`, that meant the solver was also writing the fake spine target. This broke the rule that the fake target setter is the only owner of that target.

Now the solver still clamps internally for solving, but by default it does **not** write the clamped position back into `tailTargetOverride`. `SpineFakeTargetSetter` explicitly enforces this when it assigns its fake target to the solver.

## Startup determinism for spine boxes
`SpineFakeTargetSetter` now:

- evaluates the first valid target from the live core/pole basis instead of trusting any stale serialized/captured basis from startup;
- recaptures the static basis after the first valid live evaluation;
- ignores a world-zero mouse/look target during the startup warmup when the body is already elsewhere, using the safe box center for that frame instead;
- keeps all of this logic inside `SpineFakeTargetSetter`, not in an outside body unstuck script.

This is meant to address the random launch case where the spine permanently starts bent toward negative X.

## Leg IK chain / small scale lag
`LimbSolver` now pre-translates intermediate IK nodes by the same-frame endpoint delta before the exact solve. This is not a walking-rule change; it only prevents intermediate nodes from visually lagging behind if the start/target endpoints move quickly between frames, which is especially visible on small scaled limbs.

`AutoRunLegPairController.ScaleRuntimeLegDimensions()` now gives small players stronger fake-target follow responsiveness and tighter lag limits:

- higher small-scale fake-target frequency;
- higher speed-based frequency boost;
- stronger reach-per-second and body-speed catchup;
- stronger dynamic max speed / acceleration;
- lower maximum visible fake-target lag.

## Validation checklist
1. Launch play mode repeatedly at normal size and small size. The spine should not randomly lock bent toward negative X.
2. In play mode, inspect the spine solver: `tailTargetOverride` may be assigned to the fake target, but `writeClampedTailTargetBackToTransform` should be false.
3. The spine fake target should move only from `SpineFakeTargetSetter` / its optional offset node writes.
4. At player height ~5, run and jump: leg intermediate nodes should follow the endpoints more tightly and the mesh should tear less.
5. Confirm item pickup, spear behavior, rotation behavior, and existing gait still behave as in the previous working package.
