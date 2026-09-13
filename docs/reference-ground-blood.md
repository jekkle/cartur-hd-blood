# Ground blood — the settled look, 13 September 2026

Signed off by the owner: *"i feel like the blood looks good."* This is the target. A change that
moves these numbers is a regression unless the owner asked for it.

Supersedes `reference-boar-death.md`, which captured an intermediate state that has since been
deliberately moved away from — boar was the reference creature there, and is not any more.

## The rule that came out of the day

**Size and amount vary by creature. Textures do not.**

The owner reverted two texture changes explicitly: *"I didn't want to change the textures just the
size"*, then *"i want all the textures like before just their size and amount needed to change based
on creature"*. Which artwork a mark draws with is vanilla's business, decided by
`SmallDecalSize` against the authored decal size, and nothing added this session touches it.

## Settings

```
BloodLevel      Custom
GroundBlood     0.3      -> SizeMultiplier 0.79   LifetimeMultiplier 1.00   MinChance 3%
SmallDecalSize  2        (vanilla split; marks under 2.0 use splatter_impact)
FadeStart       0.5
SizeJitter      0.35
PoolOnDeath     true
PoolSize        3.2
PoolCount       4
PoolLifetime    45
```

## The two creatures that were tuned against each other

```
boar death        height 1.40m   pool scale 0.78
   marks      burst 200 x 3%  = ~6.0
   mark size  0.51..2.65 units
   mark life  0..10s
   pool       4 stamps of 1.99..3.11 units, 38..52s

greydwarf death   height 1.80m   pool scale 1.00
   marks      burst 8 x 30%   = ~2.4
   mark size  0.64..1.33 units
   mark life  5..10s
   pool       4 stamps of 2.56..4.00 units, 38..52s
```

Boar sits at **78% of greydwarf**, which is what the owner asked for: *"the boar should be on
similar level of blood as the greydwarf if not a bit smaller."*

## Why pool size follows collider height

`CreatureSize` reads `Character.GetHeight()` — `Mathf.Max(m_collider.height, m_collider.radius * 2)`,
no scale applied — in a prefix on `Character.OnDeath`. Greydwarf is exactly **1.80m** and is the
reference at scale 1.00, clamped 0.35..4.0.

Two earlier attempts used the effect's **authored ground-decal size** as the size signal. Both were
wrong, and for the same reason: decal systems are shared child prefabs, so 47 of them are authored
at exactly 2.0 and 17 at 3.5. A hen and a lox carry the same decal, and a boar's is *larger* than a
greydwarf's despite the greydwarf being taller. Scaling anything by it inverts on screen.

`MaxScale` is 4.0 rather than 2.5 because the real heights, read off all 164 Character prefabs, run
0.8m (bat) to 12m (Hive). At 2.5 the clamp bit from 4.5m upward and flattened 31 creatures to one
pool size — troll, dragon, Bonemass and the Hive identical. At 4.0 only seven clamp.

The full table is regenerated into `carturblood_probe.txt` on every world load, so it never has to
be guessed at again.

## Spread, clamped at both ends

`BloodSkin.NormaliseSpread`, measured across all 32 death decal hosts:

```
floor    lifetime < 0.25s  ->  0.8s, gravity 0.5      6 hosts
ceiling  reach > 20m       ->  lifetime shortened     3 hosts
neither  the 6-16m band                              untouched
```

The floor exists because greydwarf, greydwarf elite, neck, bat, deathsquito and tentaroot all carry
a `splat` host authored `speed 10, lifetime 0.18s, gravity 0`. At 10 m/s over 0.18s that is 1.8
metres with no arc, so every mark it landed — and these are its *largest* — fell in a pile on the
corpse. Nothing else in the game was under 6m.

There is deliberately **no rule scaling spread by creature size**, for the shared-decal reason above.

## Lifetime

`LifetimeMultiplier` is floored at 1.0, so ground marks are never shorter-lived than vanilla
authored them. It used to be the raw `GroundBlood` scale, which at 0.3 cut a greydwarf hit mark from
5 seconds to 1.5 — it landed correctly and then vanished before the fight ended, which read as "hit
blood doesn't reach the ground". Turning blood down means fewer marks, not marks that blink out.

`PoolLifetime` is absolute seconds and ignores the multiplier entirely: a setting that reads 45 has
to mean 45.

## Hit density

`LevelHitDensity` floors a decal host's burst at 5, the count 14 of 108 hosts already use, including
player, boar and wolf. Greydwarf, greydwarf nest, neck, bat and deathsquito were authored at 3 with
the same `m_chance` of 100 — 40% fewer chances to mark the ground. All hit effects now land on the
same ~1.5 expected marks.

## Hit marks are boar-sized everywhere

`BloodSkin.LevelHitMark` raises any hit decal authored below boar's to boar's exact `1..3`. Six
qualified — greydwarf, greydwarf nest, neck, bat, deathsquito and the SeekerQueen spit, all at
`1..2`. Twenty-four others were already `1..3`, so boar's value was the norm rather than a choice.

```
hit effect        size was   size now         drawn      texture
boar                  1..3       1..3    0.51..2.65        large
greydwarf             1..2       1..3    0.51..2.65       impact
neck / bat / etc.     1..2       1..3    0.51..2.65       impact
```

Four hit decals authored **larger** than boar's — seeker, babyseeker, serpent, hjall spit at `3..4`
— are left alone. It only ever raises.

**Texture selection is pinned to the pre-resize size.** `Apply` reads `AuthoredSize` before
`LevelHitMark` runs and hands that value to `TuneDecalSystem`, which passes it to
`AssignGroundVariant`. Without this, resizing a mark past `SmallDecalSize` would also swap its
artwork, which breaks the rule at the top of this file. The size multiplier was already excluded
from texture choice for exactly the same reason.

The consequence, accepted knowingly: a greydwarf hit mark is boar-*sized* but still drawn with
`splatter_impact`, so it does not look like a boar's. Making it look the same means giving it boar's
artwork, which the owner ruled out. Asked and declined on 13 Sept — *"blood looks fine the way it
is now."*

## Known and accepted

Greydwarf-family splats draw with `splatter_impact` at 13.8% ink, against `splatter_spray`'s 38.5%.
Lowering `SmallDecalSize` fixes the visibility but changes which artwork is used, and the owner
ruled that out. **If it is revisited, the fix is denser artwork for that texture, not a different
texture.**
