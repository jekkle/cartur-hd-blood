# Cartur's HD Blood — handoff

Written 2026-09-08. Everything below is either verified from files/commands or explicitly
marked unverified. Goal of the mod, in the owner's words: *"make the blood and blood
effects hyper realistic detailed."*

---

## 1. Where things are

| | |
|---|---|
| Repo | `D:\Ai\Modding\Valheim\cartur-hd-blood` |
| Build | `cd src && dotnet build` — auto-deploys the DLL to the r2modman profile |
| Profile | `%APPDATA%\r2modmanPlus-local\Valheim\profiles\Default` |
| Config | `<profile>\BepInEx\config\com.jekkle.valheim.carturhdblood.cfg` |
| Game refs | `C:\Program Files (x86)\Steam\steamapps\common\Valheim\valheim_Data\Managed` |
| Diagnostic output | `<profile>\BepInEx\config\carturblood_*.txt` and `carturblood_textures\` |
| Extracted textures | `Desktop\Valheim VFX Textures\` (476 MB, has its own READMEs) |
| Art deliverable | `Desktop\Neck Blood - Cartur HD Blood\` |

Sibling mods: `compass-mod`, `stamina-mod`, `cartur-map-pins`, `qss-randy-fix` (all under
`D:\Ai\Modding\Valheim`). BepInEx only scans plugins at startup — a rebuild needs a full
game restart, not just a world reload.

---

## 2. Read this before touching config: the BepInEx trap

Three separate hours were lost to this, in three different disguises.

1. **An existing on-disk value always beats a new code default.** Changing a
   `ConfigEntry` default in `Plugin.cs` does *nothing* on an install that already has that
   key written. `AtlasRows = 2` sat in the file against a 12-frame sheet for hours this way.
2. **BepInEx writes in-memory values back over the file.** Editing the config while the
   game is running gets the edit clobbered. Edit with the game **closed**, or in-game via
   the config manager.
3. `Config.Reload()` is called at world load so a file edit made while sitting at the main
   menu applies on the next world load without a restart.

Structural mitigation in place: anything that *must* be right is derived in code rather
than trusted from config — the known material lists are hardcoded arrays, and
`NeverSprayMaterials` is checked *before* the config whitelist so a stale config cannot
reach `blood_cloud` or the slimes.

**The rewrite below used the second mitigation: new section names.** BepInEx keys are a
(section, key) pair, so moving a setting to a new section gives it a fresh default and the
old value becomes an inert orphan. Every setting whose *meaning* changed was moved rather
than edited in place, because a stale `HitSprayDensity = 6` reinterpreted as a trim on top
of a preset would have been brutal and silent.

---

## 3. Current state (2026-09-08, after the vanilla-behaviour rewrite and the cloud graft)

Two changes, in order. First the texture scheme was rewritten to work the way vanilla does:
**one fixed image per material, sized to the world size that material draws at.** No atlas,
no Texture Sheet Animation, no runtime slicing. Then the airborne system was removed
entirely and replaced with a graft (§4a).

Shipped art — three files, and nothing else:

| asset | size | goes on | world size |
|---|---|---|---|
| `splatter_spray_1024_rgba.png` + `_normal` | 1024 | ground, large (shared material) | 2–8 |
| `splatter_mist_1024_rgba.png` + `_normal` | 1024 | ground, large (cloned material, coin flip) | 2–8 |
| `splatter_impact_512_rgba.png` + `_normal` | 512 | ground, small (cloned material) | 0.5–2 |

**`Custom/ParticleDecal` has a live `_NormalTex`** — vanilla keeps `brains_n` 64×64 BC7 in it,
so these decals are lit. That reopens what §7 had closed "by deletion": with our albedo and
vanilla's normal, highlights followed the shape of a texture no longer being drawn, which is
what made the blood read flat rather than wet. Each mark now carries its own normal, including
the two cloned materials — the small mark lit by the large mark's bumps would be the same
mismatch in miniature.

BC7 stores full RGB, which is the evidence that the shader wants plain tangent-space rather
than DXT5nm swizzling; textures are loaded through `LoadImage` into RGBA32 without Unity's
normal-map flag, so the shader gets raw RGB either way. **Still unproven in game.** The failure
mode is highlights on the wrong side; the escape hatch is `Advanced / GroundNormalMap`, and the
real fix if it happens is flipping the green channel in the assets, not a runtime option.

Also measured and NOT yet acted on: `splat_decal_blend`'s `_Color` is `RGBA(0.502, 0.502,
0.502, 1)` — every ground decal renders at half intensity before anything else applies. One
line to change, shared by 88 effects, untested.

**The mod no longer touches airborne blood at all.** `blood_splat`, `blood_drop` and
`blood_cloud` are vanilla. What used to be there — texture swaps on the first two, density,
lifetime, size, trails, gravity and drag — is gone, along with the `[Spray]` config section,
`SprayPreset`, and `Realism.Trails` / `Realism.Ballistics`. The two assets that fed it
(`splatter_specks_64_rgba.png`, `8x8_leaf_low.png`) are deleted.

Config, rewritten for legibility on the owner's instruction — three dials, one scale:

```
[1 - Blood]        Enabled
                   GroundBlood  0-3, default 1.5   blood on the ground
                   HitBlood     0-3, default 0.25  blood in the air, per hit
                   DeathBlood   0-3, default 1.0   blood in the air, per death
[2 - Advanced]     HitEffectThreshold, ReplaceDecalTexture, SmallDecalSize,
                   DecalMaterials, ExcludeEffects, FixGreenSplash,
                   ColorFallbackSaturation, BloodColor, DecalAging, DryDarkening,
                   DecalGrowIn, SizeJitter, PoolOnDeath, PoolSize, PoolLifetime, PoolCount
[3 - Diagnostics]  unchanged, all off
```

**0 = none, 1 = vanilla, 3 = a lot**, on all three. Sections are numbered because the config
manager sorts alphabetically and "Advanced" above "Blood" is backwards for a settings screen.

`GroundBlood` replaced a `Low/Normal/High/Extreme` enum; `GroundPreset.From(float)` derives
chance floor, chance multiplier, size, lifetime and decal cap from the one number. Most of
the movement is in the CHANCE FLOOR, deliberately: vanilla puts 23 of its 116 decal owners at
10% and 69 already at 100, so a plain multiplier does nothing for the majority. It defaults
to 1.5 rather than 1 because 1 is vanilla and vanilla is what the owner could not see.

Renaming the sections orphaned every old key. That was deliberate — the §2 trap used on
purpose, so a stale `HitSprayDensity = 6` or `HitCloudScale` could not be silently
reinterpreted under new semantics. Verified first that nothing would be lost: every value the
owner had was already at its default except `GroundLevel`, which no longer exists.

**Watch this class of change.** The earlier `[General] BloodLevel` → `[Ground] GroundLevel`
rename reset the owner from `High` to `Normal` and the visible result was "I don't see my
ground textures" — High sets `MinChance = 50` against Normal's 10, and most creatures' death
splat is authored at 10% chance. A *new* section is as dangerous as a stale key when the old
value was deliberate. Check what a rename resets before doing it.

Ground art is built from the owner's originals with `repair.ps1 -Floor 0.02 -White` and a
per-file gamma, chosen by measurement rather than taste:

| asset | gamma | mean alpha @128px | peak | >=128 |
|---|---|---|---|---|
| `splatter_spray_1024` | 0.20 | 33.7 | 255 | 9.9% |
| `splatter_mist_1024` | 0.60 | 40.4 | 234 | 9.2% |
| `splatter_impact_512` | 0.20 | 29.3 | 255 | 10.7% |
| *HDVT `brains`, the benchmark* | — | 35.9 | 255 | 8.8% |
| *vanilla 64×64 `brains`* | — | 19.8 | 252 | 6.1% |
| *the owner's untouched originals* | 1.00 | 14.6 | 176 | 0.6% |

**The benchmark is HDVT's `brains`**, because it is a texture known to read on terrain in this
install. Aim new ground art at roughly `mean 30-40, >=128 around 9%`. The two large marks are
coin-flipped against each other, so they must land close or half the marks look heavy and half
look faint — which is why mist gets 0.60 while the other two get 0.20. Matching on `>=128`
matters more than matching on mean: the solid core is what reads, the mid-alpha haze is not.

Measure with `scratchpad/survive.ps1`-style downsampling, not a raw alpha histogram. A
histogram says the untouched originals had roughly as much coverage as vanilla `brains`; on
screen they were less than half as strong and their peak collapsed from 255 to 176. Those are
different questions and only the second one predicts what you see.

**The actual cause of "the ground isn't showing" was `GroundLevel = Normal` both times.**
Normal is exactly vanilla, and vanilla puts most creatures' death splat at a 10% chance with
a 5-10 second life and a cap that evicts old marks - so nine kills in ten leave nothing. The
first time it was a section rename that reset the owner from High; the second time it was
still sitting at the default. **Check the amount setting before touching the art.** Two
sessions were spent on artwork for what was a config value on both occasions.

Last build: 0 warnings, 0 errors, **1.05 MB** (was 20.7 MB). Deployed, and the three
embedded resource names verified against what `LoadTextures` asks for.

**Note for the next session:** the profile also holds `CarturHDBlood.dll.disabled` (11 MB,
an r2modman toggle-off of a much older build). The current DLL deployed alongside it.

### 3a. Released 1.0.0 — and what in it has never been run

Published to Thunderstore on 2026-09-08 as `CarturHDBlood-1.0.0.zip` (3.13 MB), built against
**Valheim 0.221.12**, BepInEx pack 5.4.2333. Package source lives in `package/`
(`manifest.json`, `icon.png`, `README.md`); the zip is assembled at the repo root.

Shipped ahead of the Valheim 1.0 patch, deliberately and with the owner's call, which means
**three features in it were never run in game**:

1. **Ground normal maps** — the shine. Failure mode is highlights on the wrong side; escape
   hatch is `Advanced / GroundNormalMap`; the real fix if it happens is flipping the green
   channel in the three `*_normal.png` assets, not a runtime option.
2. **Per-creature graft scale** — cloud size scaled by `target decal size / source decal size`,
   clamped 0.4–2.5. Check a chicken or hare against a troll or lox.
3. **`BloodTint`** — default 0.5, pulls red blood toward `BloodColor` `#8C0505`. Only
   red-dominant colours are touched (`g` and `b` both under 40% of `r`). **The check that
   matters is that greydwarf blood is still YELLOW** — if it has gone red, the strict test is
   not working and it is repainting everything.

First test session should cover those three plus the two older unknowns in §7 (the coin flip,
and `SmallDecalSize = 2`). Nothing in the build can trap a user: every feature reaches 0 or off
from the config menu.

Also still unshipped and worth trying: `splat_decal_blend`'s `_Color` is
`RGBA(0.502, 0.502, 0.502, 1)`, halving every ground decal before anything else applies.

---

## 4. Source map

| File | Role |
|---|---|
| `Plugin.cs` | Entry point, all config binding, `Config.Reload()` at world load, `Update() -> TextureDump.Tick()` |
| `BloodSkin.cs` | The core. `PreSkin(ZNetScene)` walks prefabs once; `Apply(ParticleDecal)` runs from `ParticleDecal.Awake`. Texture assignment, ground variant clones, spray tuning, exclusions, slime-splash replacement |
| `Realism.cs` | `AgeDecal` (dry-and-darken over lifetime), `GrowDecal`, `JitterSize` |
| `Pooling.cs` | Spawns ground pools into the death effect's own decal system; raycast down; dedupes per effect root |
| `BloodLevel.cs` | `GroundPreset.From(float)` — the one number behind GroundBlood |
| `CloudGraft.cs` | Copies the greydwarf death's cloud systems onto effects that have none. See §4a |
| `HitThreshold.cs` | Postfix on `Character.ApplyDamage`. Vanilla gates blood on `totalDamage > GetMaxHealth()/10f`; uses pre-mitigation damage so it cannot double-fire |
| `BloodProbe.cs`, `LiveProbe.cs`, `EffectGraph.cs`, `TextureDump.cs`, `MaterialAudit.cs` | Diagnostics, all off by default |

`LiveProbe.cs` also carries `Patch_ParticleDecal_Awake`, which is the mod's real entry
point — it fires for every decal instance however it was spawned, including the nested ones
inside per-creature effects, which are the only ones creatures actually use.

**How three ground textures reach one shared material.** `splat_decal_blend` is shared by
88 effects, so it can only hold one image; that is the first large mark. The small mark and
the second large one are given to individual decals through cloned materials
(`AssignGroundVariant` / `GroundClone`), cached per source material id — there are three
decal materials in the game, not one, and cloning `splat_decal_blend` onto a Seeker Queen
decal would change more than its texture. Cloning has a second benefit: a texture pack
overwrites the *shared* material about 25 s after world load and has never heard of the
clones.

The choice is per ParticleDecal **instance**, not per particle, so one effect's burst of
three decals all draw the same mark. Per-particle would need Texture Sheet Animation and
`Custom/ParticleDecal` ignores that — see §5.

Assets shipped (`src/Assets/`, 2.25 MB total — the whole DLL is 2.34 MB). See the table in
§3 for what goes where.

### 4a. The cloud graft

The visible splatter on a greydwarf kill — confirmed with the owner as the thing they were
pointing at — is three `blood_cloud` particle systems firing together: soft cloud 30, splat
50, blobs 8; **88 particles** at 0.5–1.5 world units. The 200 `blood_drop` particles in the
same effect are 0.05 units each and account for none of it.

It could not be done with a multiplier, because on most effects there is nothing to
multiply. Measured over the whole prefab list:

| | total | no cloud system | graft reaches |
|---|---|---|---|
| hit effects | 28 | 24 | 24 |
| death effects | 48 | 33 | 33 |

Only Bonemass (60), blob and skeleton-mace (10 each) and neck (2) have any cloud on hit. So
`CloudGraft` copies the three systems off `vfx_greydwarf_death`, scaled by `HitBlood`
(0.25, about 22 particles) or `DeathBlood` (1.0).

**Grafted onto the spawned instance, never the prefab.** The first version did it to the
prefabs at world load and Unity refused every single one:

```
Cannot instantiate objects with a parent which is persistent.
New object will be created without a parent.
```

`ZNetScene.m_prefabs` holds persistent prefab assets, and `Instantiate` will not parent a new
object to one — it creates it parentless instead. So 168 loose particle systems were made at
the world origin, attached to nothing, while the log cheerfully reported
`Grafted 3 cloud system(s) onto 24 hit effect(s)`. **A success line in the log is not
evidence the thing worked.** This is the second time in this project that a causal story was
told before the instrumentation was read; here the instrumentation was mine and it was
lying, which is worse.

The fix is `Instantiate` with no parent, then `SetParent` onto the effect's spawned root from
`ParticleDecal.Awake` — the same per-instance approach `BloodSkin` and `Pooling` already use,
and for the same underlying reason: the instance is the only thing that is really there.
That also deleted the strip-and-rebuild machinery the prefab version needed, because
instances are fresh every time.

Four things that are load-bearing:

1. **Effects that already have a cloud are never touched.** Adding to Bonemass's 250 or
   greydwarf elite's 110 is precisely how the mist explosion happened. Our own grafts are
   excluded from that test by name prefix, so it answers "did the game author one".
2. **The tint is read off the decal that triggered it.** Greydwarf blood is *yellow* —
   measured live as `RGBA(0.868, 0.700, 0.000)`. Copying its systems without overwriting
   `startColor` makes boars bleed yellow.
3. **Deduped on the effect root**, because several effects carry two or three
   `ParticleDecal`s. The live probe confirms the root is the effect (`vfx_boar_hit/
   bloodchunks`) and not the creature — which matters, or a second hit on the same boar
   would be deduped away and produce nothing.
4. **Three things decide the colour, not one.** `main.startColor` is only the first; a
   `colorOverLifetime` gradient multiplies it, and the shared material's `_EmissionColor` is
   ADDITIVE and overrides everything. Greydwarf blood is yellow, so anything yellow baked into
   the source travelled with the copy and **boars bled yellow**. The graft now disables
   `colorOverLifetime` on the copy and assigns a cloned material with `_Color` white and
   `_EmissionColor` black. This is the second time this project has hit colour baked into a
   shared material — `slime_green` in `ReplaceSlimeSplash` was the first, and the fix is
   deliberately the same shape. **Cloning, not modifying:** `blood_cloud` is shared by 52
   renderer slots including the 15 effects that legitimately have clouds.
5. **`Instantiate` copies EVERY component, not just the one you wanted.** Greydwarf's
   `blobs` and `splat` objects carry `ParticleDecal` components alongside their cloud systems,
   so each graft installed a second, greydwarf-yellow GROUND DECAL emitter on the target.
   Boars got yellow marks beside their own red ones. Caught from the live log, which had been
   printing the evidence for three exchanges while the theorising went on elsewhere:

   ```
   ParticleDecal LIVE at vfx_boar_death/CarturBloodCloud_blobs
     decal startColor = RGBA(0.868, 0.700, 0.000)   <- greydwarf yellow
   ParticleDecal LIVE at vfx_boar_death/vfx_BloodHit 1
     decal startColor = RGBA(0.868, 0.006, 0.006)   <- the boar's own, correct
   ```

   Grafted copies are now stripped to a whitelist — `Transform`, `ParticleSystem`,
   `ParticleSystemRenderer` — rather than having known stowaways removed one at a time. The
   bug was not "ParticleDecal came along", it was "components nobody thought about came along".

   **Use `Destroy`, never `DestroyImmediate`, anywhere in this path.** The graft runs from
   `ParticleDecal.Awake`, inside effect instantiation, where Unity forbids immediate
   destruction:

   ```
   Destroying components immediately is not permitted during physics trigger/contact,
   animation event callbacks, rendering callbacks or OnValidate. You must use Destroy instead.
   ```

   It reports this as a logged **error, not an exception**, so the `try/catch` around it caught
   nothing, the strip logged success, and every component quietly survived. Two builds shipped
   believing the problem was fixed. **A caught exception is not proof a thing worked** — and
   neither is a log line you wrote yourself saying it did, which is the same lesson the
   prefab-graft failure taught earlier in §4a.

   Because `Destroy` is deferred to end of frame, each component is also **disabled** on the
   spot. The disable is what actually stops it acting; the destruction is housekeeping.

6. **The copy must be stopped and cleared immediately after Instantiate.** Instantiating an
   active object runs its `Awake`, so a `playOnAwake` system starts before any configuration
   happens and can emit a burst carrying the source's colour.
7. **`ps.Play(true)` is required.** The copy comes from a prefab whose systems are not
   running, so it does not start on its own whatever `playOnAwake` says.

Burst counts go through `BloodSkin.Scale` so the `MinMaxCurve` mode survives; reading
`.constant` off a `TwoConstants` burst discards the authored range, which is §6 bug 6
repeating itself somewhere new.

---

## 5. Established by measurement — not guesses

Read out of the running game via the probes, not inferred:

- **Blood colour is per-creature and vanilla-driven.** Neck blood is *green*, greydwarf
  *yellow*, boar/deer *red*. This resolved a long "why is my blood grey" thread: the
  answer was HDVT's pale texture plus genuinely non-red creature blood, not a bug.
- **Weapons contribute nothing** to blood effects. Checked directly.
- **Hit effects do have spray systems** — an earlier assumption that only death effects
  sprayed was wrong.
- **`blood_drop` carries most airborne blood**: ~200 particles per death, and it has **no
  texture assigned at all** (vanilla's airborne blood texture is `8x8_leaf_low.png` —
  eight pixels square, which is fine, because those particles are 0.05–0.7 world units,
  a few screen pixels each).
- **`blood_cloud` is the mist explosion**: ~88 particles per greydwarf/neck death across
  three systems (soft cloud 30, splat 50, blobs 8) at 0.5–1.5 units, and up to 5–12 on
  Queen/Morgen/tick deaths. Multiplying it by the droplet density figure produced ~265
  overlapping clouds — the visual the owner reported. The mod now leaves this material
  entirely alone, which is a stronger fix than any slider was.
- **`vfx_neck_hit`'s root is `blood_cloud` at size 1.5–2.** It sat exactly on the old 2.0
  size threshold, which sent every neck hit to the mist frames. Moot now that `blood_cloud`
  is untouched, but keep it in mind if anyone ever re-enables that material: 1.5–2 is a
  boundary value and thresholds land on it.
- **`Custom/ParticleDecal` ignores Texture Sheet Animation.** Verified. This is why per-
  particle shape variety is impossible on ground decals without one material per shape, and
  why the atlas was abandoned in favour of cloned materials. Note flame effects in this game
  *do* use TSA flipbooks, so it is a quirk of that one shader, not an engine limit.
- **HD Valheim Textures (HDVT) applies its overrides *after* world load**, ~25 s in. Two
  consequences: a dump at load time captures vanilla and a delayed dump captures HD (this
  is why both texture folders on the Desktop exist), and our skin must survive being
  overwritten later.
- **`soft_cloud` was the only VFX worth re-rendering** across the whole game — 256×256
  stretched over 60 world units, 4.3 texels/unit, *and* real noise structure. HDVT already
  replaced it with a 2048 version. So that avenue is closed, which is worth knowing before
  anyone spends a day on it.
- **Moder's frost breath is not worth re-rendering** — `water_mist` at 128×128, 16
  texels/unit, a soft diffuse blob with no structure to lose.
- **The texels-per-world-unit metric cannot see content.** Its worst-scoring entries are
  smooth radial gradients where resolution costs nothing. Always read the number next to
  the image.
- **`1024x1024_blood.png` is not blood** — it is material `ship_water`, boat splash.
  `2048x2048_Blood.png` is `puke`. `32x32_blood_drop.png` is `tar_drop`. Filter blood by
  *material*, never by filename; this cost one wrong answer already.

---

## 6. Bugs found and fixed — do not reintroduce

Each of these was a real regression with a mechanism, listed so the next session doesn't
recreate it:

1. **`if (!SkinnedMaterials.Add(id)) return;`** — a "run once ever" guard that became wrong
   the moment `PreSkin` was added as a second caller. HDVT's later overwrite then stuck
   permanently. Now the skin compares the material's **actual current texture**:
   ```csharp
   bool alreadyOurs = before == albedo;
   if (alreadyOurs && SkinnedMaterials.Contains(mat.GetInstanceID())) return;
   ```
2. **`PreSkin` early-returned on the ground-texture flag**, silently disabling spray
   texture, green-splash fix, density, gravity, drag and trails. Every feature now checks
   its own flag.
3. **Square-edged mist.** A sheet assigned to a material with nothing confining a particle
   to one cell draws every cell on one quad. The class of bug that killed the atlas: any
   "is the sheet mode on" flag has to be checked everywhere the sheet is assigned, and it
   never was. Gone now — no sheets ship. (My first diagnosis — mipmaps — was wrong.)
4. **Square-edged ground decals** — fixed by premultiplying RGB (black where alpha is 0).
   Straight alpha blooms pale at the edges; this is also why HDVT's `brains` texture reads
   white-pink rather than dark red. **Still a live requirement for any new art**: run
   `tools/verifypm.ps1` before embedding. The five current assets pass with zero leaking
   pixels.
5. **Duplicate ground pools** (2–3 per kill) because several death effects carry multiple
   `ParticleDecal` components. Deduped on the effect root.
6. **`PoolLifetime` ignored `LifetimeMultiplier`** despite its own config description
   saying otherwise.
7. **`ResolveGrid` logged 350 times** per load, once per spray system. Deleted with the
   atlas, but the shape of the mistake survives it: anything that logs from inside the
   prefab walk runs a few hundred times.
8. Build-level: `Path` name collision (renamed `PathOf`), missing
   `UnityEngine.PhysicsModule` reference, unqualified `Instance`/`Log` inside the patch
   class.
9. **`Texture2D.LoadImage` / `EncodeToPNG` must be called by reflection.** Referencing
   `ImageConversion` directly pulls in a `ReadOnlySpan<byte>` overload that will not
   compile (CS0518) under net472 against this game's `netstandard.dll`.

**Process lessons worth as much as the code ones:**

- Twice I told a causal story before checking the log I had myself added
  (a "write-fight" that never happened — our code hadn't run at all because
  `ReplaceDecalTexture = false`; and a grey-blood theory when `startColor` logging was
  already sitting there unread). Read the instrumentation before narrating.
- My own art prompt caused the defect I then spent hours removing: I asked Grok for
  "tendrils radiating outward in all directions", which is a specification for circles.
  The owner's complaint — *"everything is mostly circles"* — was my fault, not the
  generator's.

---

## 7. Open questions

1. ~~Are the `blood_splat2` systems ever drawn?~~ **Answered: no. Never, anywhere.**
   `MaterialAudit` ran on 2026-09-08 over 3505 prefabs and 20381 renderers of every type:

   ```
   blood_splat2        49 slots   maxParticles=0 on 49   live: 0
   blood_splat        119 slots   maxParticles=0 on 27   live: 92
   blood_drop         128 slots   maxParticles=0 on 10   live: 118
   blood_cloud         52 slots   maxParticles=0 on  0   live: 52
   splat_decal_blend   88 slots   maxParticles=0 on  0   live: 88
   ```

   `maxParticles = 0` on all 49, which is the only value that proves it — emission being
   disabled still allows script `Emit()` and sub-emitters, which is why the audit was written
   rather than the question being answered off the effect graph. Consequence: HDVT's best
   blood texture, `2048x2048_Blood2.png`, is installed and has never been drawn on this
   install. Anyone tempted to re-render it should not bother.

   `AuditMaterials` can go back to `false` now; the answer will not change without a game
   update. Full output in `<profile>/BepInEx/config/carturblood_material_audit.txt`.

2. ~~Does `Custom/ParticleDecal` use `_NormalTex` as we encoded it?~~ **Closed by
   deletion.** No normal map ships any more, and no code writes `_NormalTex`. The question
   only ever mattered because 10.7 MB of a 20.7 MB DLL rested on the assumption.
3. ~~Does the mist explosion still happen with the mod off?~~ **Closed by design.**
   `blood_cloud` is now in `NeverSprayMaterials` and is not touched at all, so whatever the
   answer was, the mod cannot be contributing to it. If mist still explodes on kills, it is
   HDVT's or vanilla's and the next step is a texture-pack A/B, not a config dial.

New, from this rewrite and **unverified in-game**:

4. **Does the coin flip read as variety, or as noise?** Two large ground marks alternating
   per decal system was chosen on the owner's instruction, not measured. Worth a look at
   whether `splatter_mist_1024` — which is a fine speckle rather than a splatter — actually
   reads as a ground mark at 2–8 world units, or whether it wants to be small-only.
5. **Is `SmallDecalSize = 2` the right split?** Measured decal midpoints cluster at 0.95,
   1.25, 1.5 then jump to 2, 2.5, 3. A threshold of 2 puts the first three on the small
   impact texture. Plausible, not confirmed by eye.
6. ~~Does the grafted cloud actually fire?~~ **Answered: no, and then fixed.** The prefab
   version never attached anything (§4a). The per-instance version is built and deployed but
   **still not confirmed in-world.** Check a boar hit and a deer death: both are grafted,
   neither had any cloud before, so anything you see there is the graft.
7. **Is 0.25 the right hit scale?** Picked from the owner's "quarter of a death", not
   measured. It is a live config value, so tune it from the main menu — `Config.Reload()`
   plus the strip-and-rebuild in §4a means a world reload applies it without a restart.


---

## 8. Pending decisions (nothing here is agreed yet)

~~**PNG library trim**~~ — **done, and then some.** The library went from eight assets and
20.8 MB to five and 2.25 MB, and the runtime cost went from roughly **170 MB** of
uncompressed RGBA32 (16 sliced 1024 albedo cells plus 16 normals) to five plain textures
totalling about **11 MB**. Slicing is gone, so is the normal map.

`Texture2D.Compress(true)` on each texture would still roughly quarter what remains (~3 MB
as DXT5). Not done — 11 MB no longer justifies the quality loss, and it should only be
revisited if the art grows again.

**Valheim 1.0 update plan** (launches ~2026-09-08). Realistic exposure:

- Config, art and all texture work are version-independent — zero risk.
- Harmony patch targets are the risk surface, and this mod's are small and stable:
  `ZNetScene.Awake`, `ParticleDecal.Awake`, `Character.ApplyDamage`.
- The material and prefab *names* we key on (`splat_decal_blend`, `blood_cloud`,
  `blood_drop`, `blood_splat`) are the more likely breakage — new content may add blood
  materials we don't recognise.
- Recovery path: rebuild against the new `assembly_valheim.dll`, run the probes
  (`ProbeEffects`, `DumpEffectGraph`), diff the material list against the hardcoded arrays
  in `BloodSkin.cs`. That is what the diagnostics were built for and why they ship in the
  DLL rather than living in a scratch branch.

---

## 9. The art toolchain (`tools/`)

23 PowerShell scripts, recovered out of a temp directory during this handoff — they would
have been lost on cleanup. They are the reason art decisions were measured rather than
eyeballed.

**Diagnosis first — the polarity rule.** Every batch of generated art arrived with alpha
keyed to brightness (`corr(alpha, luminance)` 0.75–1.00, coverage 0–5%). Getting the
direction of the fix backwards produces flat red silhouettes, so always measure before
repairing:

| Script | Use |
|---|---|
| `imgprobe.ps1`, `imgprobe2.ps1` | coverage, aspect, basic stats |
| `polarity.ps1` | `corr(alpha, luminance)` — decides repair vs tone-map |
| `alphahist.ps1` | alpha distribution |
| `verifypm.ps1` | confirms premultiplication actually applied |
| `ink.ps1` | **ink/alpha ratio** — whether the art's RGB is white or carries its own grey. The one check the other three cannot make |
| `repair.ps1` | rebuilds alpha where it was brightness-keyed |
| `tone.ps1`, `whiten.ps1` | for art whose alpha is already correct — do **not** use `repair.ps1` on these |
| `autocrop.ps1`, `subcrop.ps1` | trim to content |
| `atlas2.ps1`, `atlas3.ps1` | assemble the sheets |
| `blobcheck.ps1` | rejects giant solid blobs. Took three attempts: average density missed blob-plus-scatter, largest-solid-area rejected everything; the working test is **compactness of the largest region AND its size** |
| `shapecheck.ps1` | `aspect`, `radialCV`, `lopsided` — this is the circle detector |
| `pmpreview.ps1`, `gridpreview.ps1`, `atlaspreview.ps1`, `splatpreview.ps1` | visual checks; game art looks near-black in a viewer because the shape lives in the alpha |
| `scalemock.ps1` | renders art at its real in-game world size — the check that would have caught the invisible-variety problem in §8 much earlier |
| `dropgen.ps1`, `splatgen.ps1`, `splatgen2.ps1` | procedural generation |

**The check that was missing, and cost a round: `ink.ps1`.** A ground texture arrived clean
on every existing test - black background, correct polarity, `leaking 0 worstRGB 0 OK
premultiplied` - and rendered with visibly wrong colour. Its ink was dark grey:

```
                     solid pixels        ink/alpha
splatter_impact_512  R254 G254 B254        0.99
splatter_mist_1024   R254 G254 B254        0.99
the new arrival      R 79  G 79  B 79      0.24    <- renders as a dark smear
```

`verifypm.ps1` cannot catch this: it tests `RGB <= alpha`, which dark ink passes. But the
game tints these per creature by multiplying, so ink at 24% brightness means no tint can ever
produce the colour the effect asks for - which is what `repair.ps1`'s own comment warns about
and what nobody was measuring. **Run `ink.ps1` on every new asset.** Anything below about
0.9 needs the RGB flattening.

The fix preserves alpha exactly rather than re-repairing:
`repair.ps1 -Gamma 1.0 -Floor 0 -White`. Gamma 1 and floor 0 pass alpha through untouched
(`max(alpha, luminance)` is alpha when the ink is darker than the coverage), while `-White`
rewrites RGB to premultiplied white. Verified: alpha histogram byte-identical before and
after, ink 0.24 -> 1.00.

**The current assets arrived already correct** and needed no repair pass — measured,
not assumed: `polarity.ps1` reported black background on all four new ones, and
`verifypm.ps1` reported `leaking 0  worstRGB 0  OK premultiplied` on every one. That is the
first batch this has been true of. If the next batch is not, the order is polarity check,
then repair, then verify — never repair first.

Note the atlas scripts (`atlas2`, `atlas3`, `atlasrebuild`, `gridpreview`, `atlaspreview`)
have no consumer any more, since no sheet ships. Kept rather than deleted because they are
the record of how the sheets were built, and rebuilding one is a plausible future.

QA thresholds that were actually used: circular means `aspect ≈ 1.00` with `radialCV ≈
0.11` (measured on HDVT's `brains` texture). Ground art must be premultiplied. Colour is
deliberately desaturated so the game can tint per creature — do not "fix" the art for
looking washed out in a viewer.

`docs/` holds the three probe outputs (`carturblood_probe.txt`, `carturblood_graph.txt`,
`carturblood_textures.txt`) — the full teardown of every blood effect, particle system,
size, burst count, material and texture. Regenerate per the instructions in
`Desktop\Valheim VFX Textures\README.txt`.
