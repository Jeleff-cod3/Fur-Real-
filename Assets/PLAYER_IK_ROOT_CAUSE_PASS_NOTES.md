# PLAYER_NEW IK root-cause pass

Base package: latest uploaded `Assets(4).zip`.

## Focus

This pass avoids moving the body from outside the rig to hide symptoms. The fixes are aimed at the core IK/player scripts and prefab-driven systems:

- spine fake-target launch softlock;
- small-scale spine box/fake-target stability;
- lower-body/leg rotation offset dependency ordering;
- leg fake-target lag at smaller body heights/high speed;
- held item hand posing through dynamic target offsets;
- spear held scale/hand binding/windup source/collision sweep;
- final-frame mesh/collider update ordering.

## Changed files

- `Assets/Player/BodyIKTargets/SpineFakeTargetSetter.cs`
- `Assets/Player/BodyIKTargets/AutoRunLegPairController.cs`
- `Assets/Scripts/ProceduralPlayerRig.cs`
- `Assets/Scripts/PickupableWeapon.cs`

## Key changes

### Spine fake target softlock

`SpineFakeTargetSetter` now owns the startup repair where it belongs:

- recaptures static box basis during a small startup warmup so it does not permanently trust a basis captured before the rig has been scaled/placed;
- auto-repairs invalid captured bases instead of freezing the spine in a bad basis;
- snaps only the setter's own first invalid fake-target state to its own valid evaluation result, not an external body workaround;
- initializes/configures the linked spine solver after the first valid target evaluation;
- scales manual special boxes by current spine reach with minimum box scale so small players do not collapse into a side-bend/spin state;
- recaptures the spine basis after runtime visual scaling.

### Lower-body rotation / leg core nodes

The lower-body rotation path now makes the main gait pair contain all four lower-body rotatables: left/right leg starts and left/right local poles. It also sorts offset nodes parent-first in the rig scheduler, so a pole offset cannot evaluate before the leg start offset it depends on.

### Leg IK lag at small sizes

The leg fake-target lag limiter no longer uses only reach-per-second catch-up. At small sizes, reach is tiny, so absolute catch-up became too slow. It now uses the larger of reach-scaled catch-up and current core-speed catch-up, keeping small-player legs responsive without snapping.

`ScaleRuntimeLegDimensions` also increases fake-target frequency/catch-up as scale goes down while keeping the same planted gait structure.

### Held item hand pose

Two-hand item holding now computes the held item's actual bounds around `ItemHolder` and moves each hand's real target toward that item's side edge via the existing dynamic offset system. When the item is dropped, those offsets go back to zero through the carry pose.

### Spear

- Held spear local position is centered on the weapon holder/hand target.
- Held spear visual scale multiplier defaults to 3x.
- Weapon holder now uses the serialized weapon hand side; default is the opposite/left hand.
- Melee thrust distance is increased.
- Thrown spear collision uses a sorted `SphereCastAll` sweep that skips self/owner colliders before accepting a hit, making ground/enemy collision more reliable.

### Body collision / mesh lag

The prior final-frame procedural mesh/collider rebuild path is preserved. Offset nodes are now applied parent-first before solvers/mesh rebuild, reducing cases where meshes read one-frame-stale lower-body nodes.

## Unity validation checklist

1. Open `Player_NEW.prefab` and save it once after script reload so Unity serializes new fields.
2. Enter play mode at desired height 20 and height 5.
3. Verify the spine does not randomly lock bent to negative X after repeated launches.
4. Hold Shift and rotate gait direction: both leg start nodes and both local pole nodes should receive dynamic offset changes.
5. Walk/run at normal fast speed and confirm feet/leg mesh do not trail farther behind as the body gets smaller.
6. Pick up a normal item: item should stay between hands and hands should pull toward its side edges.
7. Pick up spear: spear should appear centered on the selected hand, larger, with melee range extended.
8. Throw spear: right/selected weapon hand should wind up first, then the spear should launch from that wound-up held location and collide/stick with ground/enemies.
9. Inspect procedural mesh objects in play mode: each should have a MeshCollider wired to the current mesh.
