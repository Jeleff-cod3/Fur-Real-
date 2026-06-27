# Player IK Torso Reference Merge

Base package: `PlayerIK_SpineCurvatureStyleRestore_Assets.zip` (modern walking, item interaction, spear, collision, mesh fixes preserved).
Reference torso package: uploaded `Assets(5).zip`.

Scope: torso/spine behavior only.

Changed files:
- `Assets/Player/BodyIKTargets/SpineFakeTargetSetter.cs`
- `Assets/Player/IK/LimbSolver.cs`
- `Assets/Prefab_objects/Player_NEW.prefab`

What was restored from the reference torso:
- The spine fake target is treated as an IK handle, not the visible mesh endpoint.
- The solver restores the visible spine tail to the solved chain end after solving, preserving the previous bend/curvature distribution.
- The visible tail is no longer forced to the fake target endpoint, which was flattening the spine and making the torso look stiff/goofy.
- `PolevectorBody` prefab offset wiring was restored to the reference-style authored parent/capture setup instead of being rewritten to the body core by the fake target setter.

What was preserved from the modern package:
- Walking/gait scripts and prefab wiring.
- Item pickup/holding fixes.
- Spear holding/throwing fixes.
- Mesh/collider rebuild fixes.
- Spine target startup and scale guards that do not affect the visible-tail/curvature behavior.
- Mesh ring/knot alignment fix.

Unity validation:
1. Import and let scripts compile.
2. Open/save `Assets/Prefab_objects/Player_NEW.prefab`.
3. Test normal height and small height.
4. Confirm the spine fake target still follows the behavior boxes.
5. Confirm the visible torso uses the reference curved bend distribution.
6. Confirm walking, item pickup, spear attack/throw, and collision still behave like the modern package.
