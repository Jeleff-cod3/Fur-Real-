# Quick compile / scale / collision pass

Base: latest Assets(3).zip.

Applied fixes:

- Added `WorldChunkRenderer.RebuildWorldWithSeed(int)` plus cleanup/rebuild helpers so `MultiplayerPrototype` can compile against the world renderer again.
- Replaced `Assets/Materials/M_GroundGrass.mat` with a clean material YAML that keeps the same custom grass shader GUID and removes any bad/merged serialized material payload.
- Procedural player rig now auto-attaches MeshCollider components to procedural body mesh builders and wires them to the builder `optionalMeshCollider`. Colliders are convex by default.
- Runtime body meshes are forced to rebuild after the final IK/offset solve every frame rather than being throttled/dirty-skipped, reducing visible leg mesh lag.
- Spine fake target scale safety is applied every rig reference resolve and every evaluation, not only during the one-time authored scaling pass. This keeps no-clip radius, world-zero guard, pole distance, and body-anchored fallback distances from collapsing when the player is scaled down.
- Leg fake target smoothing is made faster but not hard-snapped during active steps.
- Item/weapon holder wiring is preserved and re-run through `ProceduralPlayerRig.WireGameplayHolders()`.

Unity validation checklist:

1. Reimport the project and confirm compile errors are gone.
2. Confirm `M_GroundGrass.mat` imports without merge-conflict warnings.
3. Enter play mode, inspect Player_NEW body mesh child objects, and confirm MeshColliders appear on procedural mesh sections.
4. Scale the player down and check that spine fake target stays body-relative instead of collapsing/rotating around world zero.
5. Pick up/drop items and spear with E.
- Player item and weapon pickup now explicitly update `ProceduralPlayerRig.ApplyCarryPose(...)` on pickup, drop, and throw, so arm/holder pose state is not left stale.
