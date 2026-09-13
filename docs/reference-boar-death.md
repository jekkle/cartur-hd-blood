# Reference kill — boar death, 13 September 2026

**This is the look to preserve.** The owner called this one right: *"what I really liked about it
was how big the puddles were on the ground and the way it landed."* When tuning ground blood, this
is the target, and a change that moves boar death away from these proportions is a regression even
if it improves something else.

Captured from the BepInEx log (a Debug build, so the `[live]` probes were compiled in) plus the
asset bundle. Nothing here is estimated.

## The probe, verbatim

```
[live] ---- OnDeath: $enemy_boar ----
[live] ParticleDecal LIVE at vfx_boar_death/vfx_BloodHit 1   (direct Awake)
[live]   m_chance = 10
[live]   decal startSize = 1..3  startLifetime = 0..10  maxParticles = 100
[live]   decal startColor = RGBA(0.868, 0.006, 0.006, 1.000)..RGBA(0.934, 0.034, 0.034, 1.000)
[live]                      saturation = 0.993
[live]   decal material = "splat_decal_blend" id=141888 shader="Custom/ParticleDecal"
[live]                    _MainTex="CarturBloodGroundLarge" 1024x1024
[live] SPAWNED effect "vfx_boar_death"  (3 ParticleDecal in hierarchy)
Built "CarturBloodGroundPool" from "splat_decal_blend" with "CarturBloodGroundLarge" 1024x1024
```

The last line is the pool system being cloned on that kill — the separate-pool-texture change
working, first time in game.

## Settings in force

```
BloodLevel      Custom
GroundBlood     0.3      -> SizeMultiplier 0.79   LifetimeMultiplier 0.30   MinChance 3%
SmallDecalSize  2
SizeJitter      0.35
PoolOnDeath     true
PoolSize        5.076
PoolCount       4
PoolLifetime    45
FadeStart       0
DecalAging      true
DryDarkening    0.5
```

## Every system in vfx_boar_death

```
node                            material            burst   authored     drawn
vfx_BloodHit 1                  blood_drop            200   0.05         unchanged    airborne
vfx_BloodHit 1/blob_splat (1)   splat_decal_blend       3   1..3         0.51..2.65   GROUND MARK
skinflakes                      blood_splat            50   0.4..0.6     unchanged    airborne
wetsplsh                        slime_green            30   0.3..0.7     unchanged    airborne
vfx_boar_death (root)           blood_splat2           50   1..1         never drawn (maxParticles 0)
```

## The ground mark

```
authored      1..3        midpoint 2.00  ->  large branch, splatter_spray
after jitter  0.65..3.35  (SizeJitter 0.35)
after x0.79   0.51..2.65 units          <-- what actually landed
lifetime      0..10  ->  0..3.0s        (x LifetimeMultiplier 0.30)
chance        10  ->  max(10 x 0.3, 3) = 3%
marks         burst 200 x 3% = ~6
maxParticles  100
```

## The pool

```
count     4
size      5.076 x scale 1.00 (boar) x random 0.80..1.25  =  4.06..6.34 units
lifetime   45 x 0.30 x random 0.85..1.15  =  11.5..15.5s    <-- AT THE TIME
texture   splatter_spray, always
```

## The proportion that matters

**About six splats of 0.5–2.6 units, sitting over four spray puddles of 4–6.3 units.** The puddles
run roughly **2.4x the largest splat**. That ratio is the thing to hold on to — it is what makes the
kill read as blood that pooled rather than as one big stamp.

Boar is also the reference for pool scaling: `PoolSize` means "the pool for a creature whose ground
decal is authored 2.0", which is boar, player, wolf and deer. Everything else is scaled by
`authored / 2.0`, clamped to 1.0, so this kill is by definition unscaled.

## Changed immediately after this capture

The owner asked for two things off the back of it:

1. **Puddles too big on greydwarf.** Pool size now scales by the creature's own authored decal size.
   Boar is unaffected (ratio exactly 1.0); greydwarf drops 38% to about 3.17 units.
2. **Blood should last 45 seconds and fade, never pop.** `PoolLifetime` is now **absolute seconds**
   and no longer multiplied by the blood level's lifetime multiplier. At `GroundBlood 0.3` a pool
   set to 45 was vanishing in 13.5 seconds. It now lasts the 45 it says.

The fade itself was already correct — `Realism.BuildAlphaKeys` runs alpha `1 -> 0.55 -> 0.22 -> 0`,
so a decal dissolves and never pops out. With `FadeStart = 0` that fade begins the instant the blood
lands and spans the whole lifetime. Raising `FadeStart` toward 0.5 holds full opacity for the first
half and fades over the rest, which is closer to "sits, then dries away".

## Still true at the time of writing

`SmallDecalSize` is still **2** in the live config, so greydwarf's 1.25 and 1.5 decals are drawn with
`splatter_impact` at 13.8% ink. Boar is unaffected — 2.0 is not below 2.0. The setting is hidden
behind the config manager's **Advanced settings** tick-box because `Bind2` marks everything in
`2 - Advanced` with `IsAdvanced = true`.
