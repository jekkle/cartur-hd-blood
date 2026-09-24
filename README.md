# Cartur's HD Blood

BepInEx/Harmony mod for Valheim. Replaces the game's 64×64 ground blood decal
with 1024/512 textures and normal maps, and gives most creatures an airborne
blood burst they do not have by default.

Thunderstore package name `Carturs_HD_Blood`, plugin GUID
`com.jekkle.valheim.carturhdblood`. Nexus mod 3679.

The player-facing description is `package/README.md`; this file is about the
code.

## How it works

Nothing here edits a prefab asset. Everything is applied per instance, as the
instance wakes, so the game's own effect definitions are left alone and a mod
loading after this one still sees vanilla.

| file | what it does |
| --- | --- |
| `BloodSkin.cs` | Re-skins and re-tunes the blood effects, applied per `ParticleDecal` from its `Awake` |
| `CloudGraft.cs` | Copies the greydwarf death's three `blood_cloud` particle systems onto effects that have none |
| `SprayAim.cs` | Sprays droplets away from the blow instead of expanding as a ball |
| `HitThreshold.cs` | Makes ordinary hits bleed — vanilla gates the hit effect on damage above a tenth of max health |
| `CreatureSize.cs` | Sizes the death pool to the creature rather than to the effect's authored decal size |
| `Pooling.cs` | A settled pool under a kill, as opposed to the instant of impact |
| `Realism.cs` | The behavioural part — making blood read as liquid rather than stamped decals |
| `BloodPreset.cs`, `BloodLevel.cs` | The preset/level system behind the config |

### Why the graft exists

Most of the game has no airborne blood at all: **24 of 28 hit effects and 33 of
48 death effects** carry no burst. The greydwarf death is one of the few that
does, so its cloud is copied onto the ones that don't, scaled down for a hit and
sized to the creature. Per-creature colour survives the copy — neck blood stays
green, greydwarf yellow, boar and deer red.

### Harmony patches

Shipped in every build:

```
ParticleDecal.Awake        LiveProbe     (the entry point - fires for every decal instance)
ZNetScene.Awake            Plugin        (config reload and the pre-skin pass)
Character.ApplyDamage      HitThreshold, SprayAim
Humanoid.BlockAttack       HitThreshold
Character.OnDeath          CreatureSize
Player.OnDeath             CreatureSize  (Player overrides the slot and never calls base)
```

Debug builds only, behind `#if DIAGNOSTICS`:

```
EffectList.Create          LiveProbe
Character.OnDeath          LiveProbe
```

## Diagnostics

`BloodProbe.cs`, `EffectGraph.cs`, `MaterialAudit.cs` and `TextureDump.cs` exist
to answer "why does this effect look wrong" — dumping effect graphs, material
properties and textures from a live game. They are development tools, not
features, and the csproj drops all four from a Release build.

`LiveProbe.cs` and `DebugTuning.cs` ship in every build: the first carries the
mod's `ParticleDecal.Awake` entry point and the second binds the look settings
`BloodPreset` reads. Only their `#if DIAGNOSTICS` parts are development-only.

`tools/` holds the texture pipeline that produced the shipped atlases:
generation, cropping, tone and splat previews, plus `releasecheck.ps1`.

## Build

Requires .NET 8 SDK and a Valheim install with BepInEx.

```
cd src
dotnet build
```

Managed DLLs for compiling come from the Steam install (`VALHEIM_INSTALL`); the
built plugin deploys to the r2modman profile (`R2MODMAN_PROFILE`). Override
either rather than editing the csproj:

```
dotnet build -p:VALHEIM_INSTALL="D:\SteamLibrary\steamapps\common\Valheim"
```

Fully quit Valheim through r2modman and relaunch — BepInEx only scans plugins
on startup.

## Packaging and publishing

```
powershell -ExecutionPolicy Bypass -File tools\pack.ps1
powershell -ExecutionPolicy Bypass -File tools\publish.ps1 -WhatIf
powershell -ExecutionPolicy Bypass -File tools\publish.ps1         # Thunderstore
powershell -ExecutionPolicy Bypass -File tools\publish-nexus.ps1   # Nexus
```

`pack.ps1` builds Release and writes both zips to `dist\` — the Thunderstore one
with `manifest.json`, `icon.png`, `README.md`, `CHANGELOG.md` and
`plugins\CarturHDBlood.dll`, and a Nexus one holding only
`BepInEx/plugins/CarturHDBlood.dll`, because Nexus unpacks into the game folder
rather than reading a manifest. It refuses to pack if `manifest.json` and
`PluginVersion` in `src\Plugin.cs` disagree.

Both publishers upload the zip `pack.ps1` already made rather than building
their own, take `-WhatIf` to print the plan without sending anything, and refuse
a zip older than the last source edit. Credentials come from `TCLI_AUTH_TOKEN`
and `NEXUS_API_KEY`; neither is stored in the repo.

## Config

`BepInEx/config/com.jekkle.valheim.carturhdblood.cfg`, in two sections:

- **1 - Blood** — `Enabled`, `BloodLevel` (a preset), and `GroundBlood`,
  `HitBlood`, `DeathBlood` multipliers.
- **2 - Advanced** — `SmallDecalSize`, `GroundNormalMap`, `Wetness`,
  `Reflections`, `FadeStart`, `GroundOpacity`, `GroundLighting`, `BloodTint`
  and the rest of the per-knob tuning.

## Compatibility

Client-side and visual only. The server does not need it and other players do
not need it.

Anything else that reskins blood effects will conflict, since both would be
writing the same particle systems.
