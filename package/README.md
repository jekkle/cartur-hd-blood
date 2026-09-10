# Cartur's HD Blood

Higher resolution ground blood, and a real burst of blood on every hit and kill.

Vanilla draws its ground blood from a **64×64** texture, and most creatures throw nothing into
the air at all when you hit them. This replaces the first and fixes the second.

## What it does

**Ground blood.** Three new 1024 and 512 textures replace the game's 64×64 decal, with a
different mark for a graze than for a kill, and two large marks alternating so the same shape
doesn't repeat. Each one ships with its own normal map, so light catches the strands and
droplets and the blood reads as wet rather than as a flat stain.

**Blood in the air, on every hit.** A greydwarf death throws a burst of blood that most of the
game simply doesn't have — 24 of 28 hit effects and 33 of 48 death effects have no such burst
at all. This gives it to them, scaled down for a hit, and sized to the creature: a chicken
bleeds less than a troll.

**Per-creature colour is preserved.** Neck blood is green, greydwarf yellow, boar and deer red.
That's the game's own design, and this mod keeps it rather than painting everything red.

## Settings

Three dials, all on the same scale — **0 = none, 1 = vanilla, 3 = a lot.**

| | default | |
|---|---|---|
| `GroundBlood` | 1.5 | how much blood ends up on the ground |
| `HitBlood` | 0.25 | blood thrown into the air per hit |
| `DeathBlood` | 1.0 | blood thrown into the air per death |

`GroundBlood` drives how often a mark appears, how big it is, how long it lasts and how many
can coexist, together. Most of the movement is in how *often*: vanilla puts most creatures'
death splat at a 10% chance, so raising that floor is what actually produces more blood.

Everything else lives under **Advanced** and you shouldn't need it. Settings apply on a world
reload — a trip to the main menu and back — not just on restart.

## Compatibility

Built and tested against **Valheim 1.0.7**, BepInEx pack 5.4.2350.

It hooks only three methods — `ZNetScene.Awake`, `ParticleDecal.Awake` and
`Character.ApplyDamage` — and identifies blood by *material* rather than by prefab name, so it
should survive a game update. What an update is most likely to break is the material names it
looks for, and those are listed under Advanced so they can be corrected without a new build. If
blood stops appearing after a patch, that's the first place to look.

Works alongside texture packs, including HD Valheim Textures: those apply their overrides
about 25 seconds after world load, and this re-applies on the next decal rather than losing to
them.

Blood is client-side and visual. Nothing is networked, no save data is touched, and other
players don't need it.

Harmony patches are limited to `ZNetScene.Awake`, `ParticleDecal.Awake` and
`Character.ApplyDamage`. Modded creatures are picked up automatically if they use the game's
own blood materials; if one doesn't, add its material name under `Advanced / DecalMaterials`.

## Known limitations

- The airborne burst is copied from the greydwarf death effect and uses the game's own cloud
  texture, not custom art.
- If ground blood ever looks lit from the wrong side, turn off `Advanced / GroundNormalMap`.
  The decal shader's expected normal encoding isn't documented, so that switch is there as an
  escape hatch.

## Credits

Artwork and design by Cartur.
