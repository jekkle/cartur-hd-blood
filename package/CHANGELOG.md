# Changelog

## 1.4.0

**Fire spun out.** All fire VFX work now lives in **Cartur Enhanced**
(`com.jekkle.valheim.carturenhanced`). This mod is blood-only again. Install
Cartur Enhanced alongside if you want the HD flames.

## 1.3.1

**Character-select fire.** HD fire mats also apply on the main menu / character
select screen (`FejdStartup.Awake` + `ShowCharacterSelection`), which loads
before `ZNetScene`.

## 1.3.0

**Missing fire mats.** Also skins:

- `flame_small` / `grill_burnt_smoke` (leaf_low fuel FX)
- `flame_large` / `portal_flame` / `shaman_purpleflame` (wildfire06)
- `aoe_flame` / `aspect_aoe_flame` (wildfire01)
- `flames_large_flipbook` (16×8 sheet)
- `FlameRound*` (10×5 sheet)
- Extra sparks: `embers`, `blue_cinder`, `firestaff_sparc`, `faderflame_sparcs`, …
- `magetable_smoke_cinder` (cinder tex)

Vanilla sizes. Same toggle: `EnableCampfireTextures`.

## 1.2.9

**Candles + combat.** Fire texture pass walks every prefab for flame / fireball /
candleflame / ashrain_cinder mats. Vanilla sizes.

## 1.2.8

**Rest of the fire items.** Smelter, blast furnace, charcoal kiln, forge, blackforge,
eitr refinery, frost foundry/kiln, upgrade station, portal, demisters, plus their
fuel/produce VFX. Also `flame2` (oven slots). Vanilla sizes.

## 1.2.7

Removed player-fire size scaling. Vanilla authored particle sizes win (torch already
smaller than campfire than bonfire). Textures only.

## 1.2.6

Per-type particle size experiment (torch/brazier haircut). Reverted in 1.2.7.

## 1.2.5

**More fire objects.** Extended the player-fire list to dungeon/NPC kits, pyres,
cooking fires, hot tub, jack-o-turnip, goblin/Morkhalla torches, HouseFire, and
fuel-add bursts. Still skips smelters/forges/kilns and combat AOEs. Same toggle.

## 1.2.4

**Player fires.** Same sharper flame art now on hearth, bonfire, braziers and torches
(plus campfire variants `fire_pit_haldor` / `fire_pit_hildir`). Still textures only,
cloned mats — smelters/AOEs untouched. Same toggle: `EnableCampfireTextures`.

## 1.2.3

**Campfire textures.** `fire_pit` and `fire_pit_iron` get the same sharper soft flame,
8×8 flipbook and ember art (cloned mats only). Textures only — no rate retune.
Toggle: `EnableCampfireTextures`.

## 1.2.2

Dialed creature burn back a notch (still above vanilla, below 1.2.1):

- Milder flame size / rate / lifetime and burn light
- Quieter smoke (near bonfire rate, smaller plume)
- Ember boost closer to the original light bump

## 1.2.1

More punch on creature burn (campfires still vanilla):

- Hotter flame / flipbook / ember art
- Particle retune — denser flames, taller tongues, brighter burn light
- Thin sooty smoke grafted from bonfire smoke onto spawned `vfx_Burning*`
- Advanced: `EnableBurnRetune`, `EnableBurnSmoke`

## 1.2.0

**Creature-on-fire VFX.** `vfx_Burning` (and blue/green/undead variants) get sharper flame
and ember textures. Materials are cloned on those prefabs only — campfires, hearths,
bonfires and smelters stay vanilla.

- Soft flame replaces the 8×8 `leaf_low` blob on the body-cling systems
- Enhanced 8×8 flipbook on the animated fireball layer
- Ember spark texture + light spark-rate bump
- Advanced toggles: `EnableBurnTextures`, `EnableBurnEmberBoost`
- Override art without rebuild: drop `carturblood_flame_soft_512.png` (etc.) in BepInEx/config

No smoke graft yet (keeps EffectList.Create out of Release).

## 1.1.2

- Page only - the mod itself is unchanged from 1.1.1. Adds a link to Cartur's Flooring,
  so every mod of mine points at the others.

## 1.1.1

Listing only - no code changed. The only difference in the DLL is the version number stamped
into it, so there is no reason to update for this unless you want the new icon.

- New package icon, matching the screenshot used on Nexus.
- The 1.1.0 notes below were written before the last round of ground-blood work and did not
  describe it. They now do. Nothing in that list is new in 1.1.1 - it all shipped in 1.1.0.

## 1.1.0

One setting instead of fifteen, and a lot of things that were quietly wrong.

**BloodLevel** - Low / Normal / High / Extreme / Custom, driving thirteen values together.
Touch any slider a preset controls and it switches to Custom on its own, so a slider can
never silently do nothing.

**Ground blood was drawing at half strength.** The material Valheim marks the ground with
ships its colour at (0.502, 0.502, 0.502). Vanilla's own reddish texture carried enough
colour to survive that; this mod's artwork relies entirely on the creature's blood colour,
so half of it was being thrown away. Worth roughly 50% more saturation and brightness.

**Look settings could only be changed by restarting the game.** Wetness, reflections and
the rest were applied in a code path that was skipped once a material had already been
set up, so a world reload never reached them while the settings screen implied it had.

**Blood no longer appears on a successful block.** This mod lowers the bleed threshold
from vanilla's tenth of max health to a fiftieth, and the chip damage left after a block
cleared that easily.

**Ground blood fades out instead of vanishing.** Vanilla renders these decals in Cutout,
where a pixel is drawn or discarded with nothing in between, so fading the colour just ate
the mark from its edges and dropped the rest in one frame. They now blend, and the effect
is kept alive long enough for its own marks to finish fading rather than being deleted
mid-fade.

**Normal maps now actually apply.** Three of the four blood materials use `_BumpMap`, not
`_NormalTex`, so the ground normal map was being written to a property those materials do
not have and was silently dropped.

**GroundOpacity is the main density control**, and it now goes down to 0.2. These decals
blend, so overlapping marks stack toward solid - at high opacity two marks are already
opaque and no colour underneath can be seen. Lower values let blood soak into ground
instead of sitting on top of it.

**GroundLighting**, for installs running a shading overhaul or a sky replacer. Valheim's
outdoor ambient is blue and is added to a surface rather than multiplied by it, so on some
setups blood takes the sky's colour and nothing on the material side can win. Off draws
each mark in its own colour with nothing added.

**Wetness** on every blood material rather than the ground alone, and sky reflection is off
by default - it was mirroring a pale sky across every splat, which is what made wet blood
read white or grey.

**Directional spray** - blood is thrown away from the blow rather than outward in a ball.
Systems that mark the ground are left alone, because those particles have to reach the
floor to make a mark.

The settings in force are written to the log at every world load, so a value that is not
doing what you expect can be seen rather than guessed at.

New ground artwork, a droplet texture, and a debug section for isolating one element at a
time.

**Ground blood now scales with the creature, not with the effect.** Death pool size follows
the dead creature's own collider height, so a boar leaves less than a greydwarf and a troll
leaves a great deal more. Two earlier attempts scaled by the effect's authored decal size
instead; that inverts, because decal systems are shared child prefabs - a hen and a lox carry
the same one.

**Marks that piled on the corpse now scatter.** Greydwarf, greydwarf elite, neck, bat,
deathsquito and tentaroot all carry a death emitter authored to live 0.18 seconds with no
gravity, so everything it landed fell inside a 1.8 metre circle. Those now arc and travel.
At the other end, the three effects that threw blood past 20 metres are pulled back in, so
it lands where the kill happened.

**Ground marks are no longer shorter-lived than vanilla.** Turning blood down used to cut
every mark's lifetime by the same factor, so at a low setting a greydwarf hit mark lasted
1.5 seconds - it landed correctly and then vanished before the fight ended. Less blood now
means fewer marks, not marks that blink out. PoolLifetime is absolute seconds and ignores
the blood level entirely: 45 means 45.

**Hit marks are levelled up to the common size.** Six creatures emitted 3 ground-seeking
particles where player, boar and wolf emit 5, and six drew a smaller hit mark than everyone
else, both at the same 100% chance. Which texture a mark uses is unchanged - that is decided
by the size the game authored, not by the size it ends up drawn at.

### Upgrading from 1.0.x

Existing config values always win over new defaults, so a few settings keep their old
numbers rather than the retuned ones. If ground blood looks too bright or pink, set:

```
BloodColor    = #470303
BloodTint     = 0.6
GroundOpacity = 0.47
Wetness       = 0.35
```

Those are the defaults a fresh install now gets.

## 1.0.1

- The large ground mark no longer ever renders as bare spray on its own - it reads as
  a thin scatter with no solid body alone, so it's now always paired with the mist or
  impact art instead.
- Added screenshots and links to my other mods.

## 1.0.0

First release.

- 1024 and 512 ground blood textures with normal maps, replacing the vanilla
  64x64 decal, with separate marks for a graze and for a kill.
- Airborne blood on hits and deaths for the creatures that ship without any.
- Per-creature blood colour preserved.
- GroundBlood / HitBlood / DeathBlood dials on a 0-none, 1-vanilla, 3-a lot scale.
