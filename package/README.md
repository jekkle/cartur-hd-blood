# Cartur's HD Blood

Higher resolution ground blood, and a real burst of blood on every hit and kill.

Fire VFX (campfires, burn status, candles, combat flames) live in a separate mod:
**Cartur Enhanced**.

![Ground blood mark](https://raw.githubusercontent.com/jekkle/cartur-hd-blood/master/docs/images/ground-blood-overhead.png)

![Blood burst on a kill](https://raw.githubusercontent.com/jekkle/cartur-hd-blood/master/docs/images/death-burst.png)

**Ground blood.** Vanilla draws its ground blood from a 64×64 texture. Three new
1024 and 512 textures replace it — a different mark for a graze than for a kill,
and two large marks alternating so the same shape doesn't repeat. Each has its own
normal map, so light catches the strands and the blood reads as wet rather than
as a flat stain.

**Blood in the air.** Most creatures throw nothing into the air when you hit them —
24 of 28 hit effects and 33 of 48 death effects have no burst at all. This gives
them one, scaled down for a hit and sized to the creature: a chicken bleeds less
than a troll.

**Per-creature colour is kept.** Neck blood green, greydwarf yellow, boar and deer
red. That's the game's own design.

## Settings

`BepInEx/config/com.jekkle.valheim.carturhdblood.cfg`. Applies on a world reload —
a trip to the main menu and back.

| Setting | Default | What it does |
| --- | --- | --- |
| `Enabled` | true | Master switch. Off leaves vanilla blood untouched. |
| `BloodLevel` | Normal | How much blood, everywhere. Low / Normal / High / Extreme / Custom. |
| `GroundBlood` | 1.5 | How much ends up on the ground. **Custom only.** |
| `HitBlood` | 0.25 | Blood thrown into the air per hit. **Custom only.** |
| `DeathBlood` | 1.0 | Blood thrown into the air per death. **Custom only.** |

**`BloodLevel` is the dial to turn.** It drives everything, and anything but
`Custom` overrides the three below it — set `BloodLevel` to `Custom` first if you
want to work those by hand. On that scale 0 is none, 1 is vanilla, 3 is a lot.

Everything else lives under **Advanced** and you shouldn't need it: droplet size,
spread and count, cloud size, pool size and lifetime, wetness, opacity and the
material names.

## Compatibility

Built and tested against **Valheim 1.0.7**, BepInEx pack 5.4.2350.

Six methods are hooked: `ZNetScene.Awake`, `ParticleDecal.Awake`,
`Character.ApplyDamage`, `Humanoid.BlockAttack`, `Character.OnDeath` and
`Player.OnDeath`. The last four are the ones to know about if you run other combat
mods. All of them are Harmony prefixes and postfixes that only read — was the hit
blocked, which way did it come from, how tall was the thing that died — and then
spawn a visual effect. Nothing here changes damage, blocking or death, so a mod
that replaces one of those methods outright will stop the blood rather than the
other way round.

Blood is identified by *material* rather than by prefab name, so it should
survive a game update. If blood stops appearing after a patch, the material names
are under Advanced and can be corrected there without a new build.

Works alongside texture packs, including HD Valheim Textures. Modded creatures are
picked up automatically if they use the game's own blood materials; if one doesn't,
add its material name under `Advanced / DecalMaterials`.

Client-side and visual. Nothing is networked, no save data is touched.

## Install

Use a mod manager (r2modman / Thunderstore / Gale) and it pulls in BepInEx for you.
Manually: drop `CarturHDBlood.dll` into `BepInEx/plugins`.

Artwork and design by Cartur.

---

*Free, and always will be. If it improved your game you can [tip me on Patreon](https://www.patreon.com/c/cartur).*

**[Discord](https://discord.gg/nd5RqpwNkz)** — bug reports, install help, and mod requests.
Bug reports get their own thread so nothing is lost in a chat scroll, and requests are voted on.

**More from Cartur:**
[Map Pins](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Map_Pins/) ·
[Compass and Clock](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Compass_and_Clock/) ·
[Safe Stamina](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Safe_Stamina/) ·
[Follow Command](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Follow_Command/) ·
[Flooring](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Flooring/) ·
[UI HUD](https://thunderstore.io/c/valheim/p/Cartur/Carturs_UI_HUD/) ·
[Waste Management](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Waste_Management/) ·
[Feeding Trough](https://thunderstore.io/c/valheim/p/Cartur/Carturs_Feeding_Trough/)
