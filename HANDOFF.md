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

**Caught again during this handoff, and STILL OUTSTANDING:** `LargeSpraySize` was raised
2.0 → 2.5 in code, but the file still holds `2`, so the change currently has zero effect.
It could not be patched because the game was running (see rule 2 above). **Action for the
next session: with Valheim closed, set `LargeSpraySize = 2.5` in the config file.**
`CloudSprayDensity` was absent from the file, so it picks up the code default (1.0) on next
run — that one is fine.

Structural mitigation already in place: anything that *must* be right is derived in code
rather than trusted from config — the atlas grid comes from texture size ÷ cell size
(`ResolveGrid`), and the known material lists are hardcoded arrays.

---

## 3. Current state (2026-09-08)

```
Enabled            = false     <-- mod deliberately OFF
SprayVariants      = false
ReplaceDecalTexture= true
ReplaceSprayTexture= true
BloodLevel         = Normal
PoolLifetime       = 45
SprayDensity       = 1.056
HitSprayDensity    = 6
LargeSpraySize     = 2         <-- STALE, code default is 2.5, see §2
CloudSprayDensity  = 1.0       (new key, will be written on next run)
AuditMaterials     = true      <-- pending, see §7
DumpTextures       = false
```

`Enabled = false` is **not** a bug and not an abandoned state. The owner turned the mod
fully off to establish whether the "explosion of mist" seen on kills is ours or the
existing HD texture pack's. **That observation is still outstanding** — the answer decides
whether `CloudSprayDensity` needs to drop below 1.0.

Last build: 0 warnings, 0 errors. Deployed and hash-verified,
`b22d276b5c1e89ec62604c788d99d247`, 20.7 MB.

---

## 4. Source map

| File | Role |
|---|---|
| `Plugin.cs` | Entry point, all config binding, `Config.Reload()` at world load, `Update() -> TextureDump.Tick()` |
| `BloodSkin.cs` | The core. `PreSkin(ZNetScene)` walks prefabs once; `Apply(ParticleDecal)` runs from `ParticleDecal.Awake`. Texture swap, spray density, exclusions, slime-splash replacement |
| `Variants.cs` | Slices the atlas into per-cell `Texture2D` + cloned `Material`. Exists because `Custom/ParticleDecal` ignores Texture Sheet Animation |
| `Realism.cs` | `AgeDecal` (dry-and-darken over lifetime), `GrowDecal`, `JitterSize`, `Trails`, `Ballistics` |
| `Pooling.cs` | Spawns ground pools into the death effect's own decal system; raycast down; dedupes per effect root |
| `BloodLevel.cs` | `Low/Normal/High/Extreme/Custom` presets |
| `HitThreshold.cs` | Postfix on `Character.ApplyDamage`. Vanilla gates blood on `totalDamage > GetMaxHealth()/10f`; uses pre-mitigation damage so it cannot double-fire |
| `BloodProbe.cs`, `LiveProbe.cs`, `EffectGraph.cs`, `TextureDump.cs`, `MaterialAudit.cs` | Diagnostics, all off by default |

Assets shipped (`src/Assets/`, 20.8 MB total — the whole DLL is 20.7 MB):

```
blood_splat_atlas.png     7.1 MB   4096x4096, 1024px cells
blood_splat_atlas_n.png  10.7 MB   normal map  <-- largest single asset, benefit UNVERIFIED
blood_droplet.png         2.3 MB   2048x2048, 512px cells (airborne)
blood_splat_single.png    0.4 MB   fallback when variants are off
blood_splat_single_n.png  0.3 MB
```

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
  three systems (soft cloud 30, splat 50, blobs 8) at 0.5–1.5 units. Multiplying it by the
  droplet density figure produced ~265 overlapping clouds — the visual the owner
  reported. Hence `CloudSprayDensity` as a separate slider.
- **`vfx_neck_hit`'s root is `blood_cloud` at size 1.5–2**, sitting exactly on the old
  2.0 `LargeSpraySize` threshold, which sent every neck hit to the mist frames. Reason for
  the 2.5 change.
- **`Custom/ParticleDecal` ignores Texture Sheet Animation.** This is why `Variants.cs`
  exists at all. Note flame effects in this game *do* use TSA flipbooks, so it is a quirk
  of that one shader, not an engine limit.
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
3. **Square-edged mist.** With `SprayVariants = false` the material held the entire 2048
   sheet with nothing confining a particle to one cell. Fixed with `SliceFirstCell`, a
   spray fallback mirroring the ground fallback added hours earlier and never mirrored.
   (My first diagnosis — mipmaps — was wrong.)
4. **Square-edged ground decals** — fixed by premultiplying RGB (black where alpha is 0).
   Straight alpha blooms pale at the edges; this is also why HDVT's `brains` texture reads
   white-pink rather than dark red.
5. **Duplicate ground pools** (2–3 per kill) because several death effects carry multiple
   `ParticleDecal` components. Deduped on the effect root.
6. **`PoolLifetime` ignored `LifetimeMultiplier`** despite its own config description
   saying otherwise.
7. **`ResolveGrid` logged 350 times** per load. Deduped with a `_gridWarned` flag.
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

1. **Are the 43 `blood_splat2` systems ever drawn?** One is verified `maxParticles = 0`;
   the rest are inferred. If they are all inert, HDVT's best blood texture
   (`2048x2048_Blood2.png`) is installed and never seen. `MaterialAudit.cs` was written to
   settle it and `AuditMaterials = true` is already set — it needs one **menu round-trip**
   to run, then writes `carturblood_material_audit.txt`. Note `maxParticles == 0` is the
   only proof of inertness: emission being disabled still allows script `Emit()` and
   sub-emitters.
2. **Does `Custom/ParticleDecal` actually use `_NormalTex` the way we encoded it?** Never
   confirmed. This matters because the normal map is 10.7 MB of a 20.7 MB DLL — the single
   largest asset resting on an unverified assumption.
3. **Does the mist explosion still happen with `Enabled = false`?** Owner observation
   pending. Decides whether `CloudSprayDensity` goes below 1.0.

---

## 8. Pending decisions (nothing here is agreed yet)

**PNG library trim** — the owner asked whether the library needs to be this large. It does
not. Airborne droplets draw at 0.05–0.7 world units, so 16 distinct 512px silhouettes are
detail nobody can resolve; only the mist frames are drawn large (5–12 units on
Queen/Morgen/tick deaths, plus `vfx_neck_hit` at 1.5–2).

| | now | proposed | saving |
|---|---|---|---|
| Air sheet | 4×4 @ 512 (2048²) | 2×2 @ 512 — 2 droplets + 2 mist | ~4× |
| Ground large frames | 12 | 8 | ~⅓ of slices |
| Ground normal map | 4096² (10.7 MB) | 2048², or drop pending Q2 above | ~8 MB of DLL |

Runtime memory matters more than file size: 16 sliced 1024 albedo cells plus 16 normals is
roughly **170 MB uncompressed RGBA32**. Trimming to 8+8 halves that, and calling
`Texture2D.Compress(true)` on each slice would quarter what remains (~43 MB as DXT5).
`Compress(true)` is a code change worth doing regardless of the art trim.

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
| `repair.ps1` | rebuilds alpha where it was brightness-keyed |
| `tone.ps1`, `whiten.ps1` | for art whose alpha is already correct — do **not** use `repair.ps1` on these |
| `autocrop.ps1`, `subcrop.ps1` | trim to content |
| `atlas2.ps1`, `atlas3.ps1` | assemble the sheets |
| `blobcheck.ps1` | rejects giant solid blobs. Took three attempts: average density missed blob-plus-scatter, largest-solid-area rejected everything; the working test is **compactness of the largest region AND its size** |
| `shapecheck.ps1` | `aspect`, `radialCV`, `lopsided` — this is the circle detector |
| `pmpreview.ps1`, `gridpreview.ps1`, `atlaspreview.ps1`, `splatpreview.ps1` | visual checks; game art looks near-black in a viewer because the shape lives in the alpha |
| `scalemock.ps1` | renders art at its real in-game world size — the check that would have caught the invisible-variety problem in §8 much earlier |
| `dropgen.ps1`, `splatgen.ps1`, `splatgen2.ps1` | procedural generation |

QA thresholds that were actually used: circular means `aspect ≈ 1.00` with `radialCV ≈
0.11` (measured on HDVT's `brains` texture). Ground art must be premultiplied. Colour is
deliberately desaturated so the game can tint per creature — do not "fix" the art for
looking washed out in a viewer.

`docs/` holds the three probe outputs (`carturblood_probe.txt`, `carturblood_graph.txt`,
`carturblood_textures.txt`) — the full teardown of every blood effect, particle system,
size, burst count, material and texture. Regenerate per the instructions in
`Desktop\Valheim VFX Textures\README.txt`.
