# Changelog

## 1.1.0

One setting instead of fifteen, and several things that were quietly wrong.

**BloodLevel** - Low / Normal / High / Extreme / Custom, driving thirteen values together.
Normal is the tuned default. Touch any slider a preset controls and it switches to Custom
on its own, so a slider can never silently do nothing.

**Blood no longer appears on a successful block.** This mod lowers the bleed threshold from
vanilla's tenth of max health to a fiftieth, and the chip damage left after a block cleared
that easily.

**Ground blood fades out instead of vanishing.** Vanilla renders these decals in Cutout,
where a pixel is drawn or discarded with nothing in between, so fading the colour just ate
the mark from its edges and then dropped the rest in a single frame. They now blend, and
the effect object is kept alive long enough for its own marks to finish fading rather than
being deleted mid-fade.

**Normal maps now actually apply.** Three of the four blood materials use `_BumpMap`, not
`_NormalTex`, so the ground normal map was being written to a property that did not exist
on them and silently dropped. Normals are also loaded as linear data rather than sRGB.

**Wetness**, on every blood material rather than the ground alone. Sky reflection is off by
default - it was mirroring a pale sky across every splat, which is what made wet blood read
white or grey.

**Directional spray** - blood is thrown away from the blow rather than outward in a ball.
Systems that mark the ground are left alone, because those particles have to reach the floor.

Also: new ground artwork, a droplet texture, and a debug section for isolating one element
at a time while judging it.

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
