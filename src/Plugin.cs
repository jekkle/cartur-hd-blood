using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace CarturHDBlood
{
    [BepInPlugin(Guid, "Cartur's HD Blood", "1.1.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.jekkle.valheim.carturhdblood";

        internal static Plugin Instance;
        internal static ManualLogSource Log;
        internal static string ConfigDir;

        internal static ConfigEntry<bool> ModEnabled;
        internal static ConfigEntry<BloodAmount> BloodLevel;
        internal static ConfigEntry<float> HitEffectThreshold;

        internal static ConfigEntry<float> GroundBlood;
        internal static ConfigEntry<bool> ReplaceTexture;
        internal static ConfigEntry<bool> ReplaceSplashTexture;
        internal static ConfigEntry<string> DecalMaterials;
        internal static ConfigEntry<string> ExcludeEffects;
        internal static ConfigEntry<float> SmallDecalSize;
        internal static ConfigEntry<bool> GroundNormalMap;
        internal static ConfigEntry<float> Wetness;
        internal static ConfigEntry<bool> WetnessMap;
        internal static ConfigEntry<bool> Reflections;
        internal static ConfigEntry<bool> GroundFade;
        internal static ConfigEntry<float> FadeStart;
        internal static ConfigEntry<float> GroundOpacity;
        internal static ConfigEntry<bool> DirectionalSpray;
        internal static ConfigEntry<float> SprayAngle;
        internal static ConfigEntry<float> SprayLift;
        internal static ConfigEntry<float> SprayRadius;
        internal static ConfigEntry<float> HitBlood;
        internal static ConfigEntry<float> DeathBlood;
        internal static ConfigEntry<bool> FixGreenSplash;

        internal static ConfigEntry<float> ColorFallbackSaturation;
        internal static ConfigEntry<string> BloodColor;
        internal static ConfigEntry<float> BloodTint;
        internal static ConfigEntry<bool> DecalAging;
        internal static ConfigEntry<float> DryDarkening;
        internal static ConfigEntry<bool> DecalGrowIn;
        internal static ConfigEntry<float> SizeJitter;
        internal static ConfigEntry<bool> PoolOnDeath;
        internal static ConfigEntry<float> PoolSize;
        internal static ConfigEntry<float> PoolLifetime;
        internal static ConfigEntry<int> PoolCount;
#if DIAGNOSTICS
        internal static ConfigEntry<bool> Diagnostics;
        internal static ConfigEntry<bool> DumpTextures;
        internal static ConfigEntry<bool> DumpTextureImages;
        internal static ConfigEntry<float> DumpDelaySeconds;
        internal static ConfigEntry<bool> AuditMaterials;
        internal static ConfigEntry<string> AuditMaterialNames;
#endif

        private Harmony _harmony;


        /// Moves BloodLevel to Custom the moment a setting the preset controls is edited.
        ///
        /// Without this the settings screen lies to you. BloodLevel overrides thirteen values, so
        /// dragging GroundBlood while the level says Normal changes a number that is then thrown
        /// away - the slider moves, the blood does not, and nothing on screen explains why. That
        /// cost a real session: three sliders were tuned down to calm an effect and none of them
        /// were being read.
        ///
        /// Only the entries the preset actually overrides are watched. Editing something outside
        /// its reach - ReplaceDecalTexture, BloodColor, the exclusion lists - leaves the chosen
        /// level alone, because those genuinely still apply under a preset.
        ///
        /// Setting BloodLevel from in here raises its own SettingChanged, which is why BloodLevel
        /// is not itself in the watched list: nothing here reacts to it, so there is no loop.
        ///
        /// Config.Reload() at world load can also raise these events, for any value that differs
        /// from what is in memory. That is a file edit made outside the game, which is a manual
        /// edit by any reasonable reading, so treating it the same way is correct rather than a
        /// side effect to be suppressed.
        private void WatchForManualEdits()
        {
            ConfigEntryBase[] overridden =
            {
                GroundBlood, HitBlood, DeathBlood, Wetness, GroundOpacity,
                PoolSize, PoolCount, HitEffectThreshold,
                DebugTuning.DropletSize, DebugTuning.DropletSpread, DebugTuning.DropletCount,
                DebugTuning.CloudSize, DebugTuning.StretchParticles,
            };

            foreach (ConfigEntryBase entry in overridden)
            {
                if (entry == null)
                    continue;
                entry.ConfigFile.SettingChanged += (_, e) =>
                {
                    if (e.ChangedSetting != entry || BloodLevel.Value == BloodAmount.Custom)
                        return;
                    BloodAmount was = BloodLevel.Value;
                    BloodLevel.Value = BloodAmount.Custom;
                    Log.LogInfo($"{entry.Definition.Key} was edited, so BloodLevel moved from " +
                                $"{was} to Custom - a preset would have overridden it.");
                };
            }
        }

        /// Binds into "2 - Advanced" and marks the entry advanced, so ConfigurationManager hides
        /// it behind the Advanced tick-box.
        ///
        /// A wrapper rather than a tag on each call: there are two dozen of these and adding the
        /// attribute by hand to every one is how you end up with three that quietly do not have
        /// it. The attributes object is duck-typed by ConfigurationManager and inert without it.
        private ConfigEntry<T> Bind2<T>(string section, string key, T def, ConfigDescription desc)
        {
            var tags = new ConfigurationManagerAttributes { IsAdvanced = true };
            var d = desc == null
                ? new ConfigDescription("", null, tags)
                : new ConfigDescription(desc.Description, desc.AcceptableValues, tags);
            return Config.Bind(section, key, def, d);
        }

        private ConfigEntry<T> Bind2<T>(string section, string key, T def, string desc) =>
            Bind2(section, key, def, new ConfigDescription(desc ?? ""));

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            ConfigDir = Path.GetDirectoryName(Config.ConfigFilePath);

            // Three dials, one section, all on the same scale. Everything else lives under
            // Advanced, because it is either a knob nobody needs to touch or a lever for
            // fixing a modded creature the mod guessed wrong about.
            //
            // The sections are numbered because the config manager sorts them alphabetically,
            // and "Advanced" sorting above "Blood" is exactly backwards for a settings screen.
            ModEnabled = Config.Bind("1 - Blood", "Enabled", true,
                "Master switch. Off leaves vanilla blood completely untouched.");

            BloodLevel = Config.Bind("1 - Blood", "BloodLevel", BloodAmount.Normal,
                new ConfigDescription(
                    "How much blood, everywhere - ground marks, airborne spray, droplets, pools " +
                    "and wetness together.\n" +
                    "  Low      half of Normal; marks are occasional\n" +
                    "  Normal   DEFAULT - ground 0.3, hit 0.2, death 0.5\n" +
                    "  High     double Normal\n" +
                    "  Extreme  double again; the ground keeps most marks\n" +
                    "  Custom   ignore all of the above and use the Advanced settings instead\n" +
                    "Anything but Custom OVERRIDES the individual settings below. They are still " +
                    "there, and switching to Custom hands control straight back to them."));

            GroundBlood = Config.Bind("1 - Blood", "GroundBlood", 1.5f,
                new ConfigDescription(
                    "How much blood ends up on the GROUND.\n" +
                    "  0    none\n" +
                    "  1    exactly vanilla - only the artwork differs\n" +
                    "  1.5  default; about a third of hits mark, marks last half again as long\n" +
                    "  3    every hit marks, bigger, and they stay\n" +
                    "Drives how often a mark appears, how big it is, how long it lasts and how " +
                    "many can coexist, together. Most of the movement is in how OFTEN: vanilla " +
                    "puts most creatures' death splat at a 10% chance, so raising that floor is " +
                    "what actually produces more blood.",
                    new AcceptableValueRange<float>(0f, 3f)));

            HitBlood = Config.Bind("1 - Blood", "HitBlood", 0.25f,
                new ConfigDescription(
                    "Size of the blood burst thrown into the air on each HIT, as a fraction of " +
                    "what a greydwarf death throws.\n" +
                    "  0     none\n" +
                    "  0.25  default, about 22 particles\n" +
                    "  1     a full greydwarf death's worth, on every hit\n" +
                    "24 of the game's 28 hit effects have no such burst at all, so this is not a " +
                    "multiplier on something that already exists - the systems are copied from " +
                    "the greydwarf death effect onto each hit as it spawns.",
                    new AcceptableValueRange<float>(0f, 3f)));

            DeathBlood = Config.Bind("1 - Blood", "DeathBlood", 1.0f,
                new ConfigDescription(
                    "The same burst, on each DEATH.\n" +
                    "  0  none\n" +
                    "  1  default; what a greydwarf death already does, given to the 33 death " +
                    "effects that have none - boar, deer, draugr, goblin, lox, seeker and the rest\n" +
                    "  3  triple\n" +
                    "Deaths that ALREADY have a burst are never touched at any value. Bonemass " +
                    "throws 250 particles and greydwarf elite 110; adding to those is how you " +
                    "get an explosion of mist on an ordinary kill.",
                    new AcceptableValueRange<float>(0f, 3f)));

            HitEffectThreshold = Bind2("2 - Advanced", "HitEffectThreshold", 0.02f,
                new ConfigDescription(
                    "Minimum share of the target's max health a hit must do before it produces " +
                    "blood, as a fraction. Vanilla is 0.1 - a tenth - so a 50-damage swing on a " +
                    "600 HP troll does 8.3% and draws no blood at all, which is why non-killing " +
                    "hits on big creatures look dry. Because the bar scales with health, tougher " +
                    "creatures bleed less. 0.02 means almost every real hit bleeds. Set to 0.1 or " +
                    "above to leave vanilla behaviour untouched.",
                    new AcceptableValueRange<float>(0f, 0.1f)));

            ReplaceTexture = Bind2("2 - Advanced", "ReplaceDecalTexture", true,
                "Replace the ground blood decal texture with this mod's artwork. Off keeps the " +
                "game's own (or a texture pack's) while GroundBlood still controls the amount.");
            ReplaceSplashTexture = Bind2("2 - Advanced", "ReplaceSplashTexture", true,
                "Use the mod's splash image for the airborne splash instead of vanilla's, which is " +
                "256x256 and reads as a membrane rather than liquid. Colour is unaffected either " +
                "way - the splash still takes its colour from whatever creature it came out of.");

            SmallDecalSize = Bind2("2 - Advanced", "SmallDecalSize", 2f,
                new ConfigDescription(
                    "Ground marks with an authored size below this get the small impact texture; " +
                    "everything above it gets one of the two large ones. Vanilla ground decals " +
                    "run 0.5..1.4 on light hits up to 5..8 on the biggest deaths, so 2 splits " +
                    "roughly between grazes and real wounds.",
                    new AcceptableValueRange<float>(0.5f, 8f)));

            GroundNormalMap = Bind2("2 - Advanced", "GroundNormalMap", true,
                "Light the ground blood with its own normal map, so highlights follow the shape " +
                "of the splat and it reads as wet rather than flat. Off leaves the game's own " +
                "normal in place. Turn it off if the lighting looks wrong - the decal shader's " +
                "expected normal encoding is not documented anywhere, and the symptom of getting " +
                "it wrong is highlights on the opposite side from the light.");

            Wetness = Bind2("2 - Advanced", "Wetness", 0.75f,
                new ConfigDescription(
                    "How wet blood looks - the shader's smoothness, applied to every blood " +
                    "material rather than just the ground.\n" +
                    "Vanilla leaves these nearly matte: blood_splat 0.23, splat_decal_blend 0.14, " +
                    "and blood_drop and blood_cloud at 0, which is why blood reads as painted on " +
                    "rather than spilled. The lighting path was already switched on in all of " +
                    "them; only the smoothness was missing.\n" +
                    "  0     matte, dried\n" +
                    "  0.23  vanilla's ground blood\n" +
                    "  0.75  default; fresh and wet\n" +
                    "  1     a mirror, and it will look like it\n" +
                    "Metallic is deliberately left at 0 and not exposed. Blood is a dielectric, " +
                    "and raising metallic is the usual way this ends up looking like car paint.",
                    new AcceptableValueRange<float>(0f, 1f)));

            WetnessMap = Bind2("2 - Advanced", "WetnessMap", false,
                new ConfigDescription(
                    "Take wetness from a texture instead of one value for the whole splat, so the " +
                    "shine follows the artwork's own wet ridges and the flat areas stay dull. " +
                    "Reads congealing rather than fresh.\n" +
                    "Needs a file at BepInEx/config/carturblood_splatter_spray_1024_gloss.png, " +
                    "with metallic in the red channel and smoothness in alpha - the channel layout " +
                    "the shader expects, not a plain greyscale image. Does nothing without it.\n" +
                    "With a map assigned the shader ignores the flat Wetness value and uses it as " +
                    "a multiplier over the map instead, so Wetness still works as the overall " +
                    "level either way."));

            Reflections = Bind2("2 - Advanced", "Reflections", false,
                new ConfigDescription(
                    "Let blood mirror the sky as well as catching direct light.\n" +
                    "Every blood material ships with this on, which is why raising Wetness made " +
                    "blood look white or grey rather than wet: it was reflecting Valheim's pale " +
                    "sky across the whole splat. At vanilla's near-zero smoothness you never see " +
                    "it; at any useful Wetness it dominates.\n" +
                    "Off leaves the direct sun highlight alone, so blood shows a small bright spot " +
                    "where the light is and stays dark elsewhere - which is what wet blood does. " +
                    "On restores the vanilla behaviour if you prefer the sheen."));

            GroundFade = Bind2("2 - Advanced", "GroundFade", true,
                new ConfigDescription(
                    "Let ground blood fade out instead of being cut away and vanishing.\n" +
                    "Vanilla renders these decals in Cutout mode, where a pixel is either fully " +
                    "drawn or discarded and there is no partial transparency. Fading the colour " +
                    "under that just erodes the splat from its edges while the middle stays at " +
                    "full strength, and then the remainder drops below the cutoff in a single " +
                    "frame - a fade that ends in a pop.\n" +
                    "On switches the ground materials to the same Fade blending the game already " +
                    "uses for blood_cloud, so alpha means what it says. Off restores vanilla's " +
                    "cutout rendering."));

            FadeStart = Bind2("2 - Advanced", "FadeStart", 0f,
                new ConfigDescription(
                    "When a ground mark starts fading, as a fraction of its life. Needs " +
                    "GroundFade on to be visible at all.\n" +
                    "  0     default; fading from the moment it lands, never at full for long\n" +
                    "  0.5   sits for half its life, then fades\n" +
                    "  0.9   sits almost the whole time, then goes quickly\n" +
                    "The curve is not a straight line at any setting: it drops off quickly at " +
                    "first and flattens into a long faint tail, which is how a real mark soaks " +
                    "away, and it reaches a true zero rather than being cut off while visible.",
                    new AcceptableValueRange<float>(0f, 0.9f)));

            GroundOpacity = Bind2("2 - Advanced", "GroundOpacity", 1.4f,
                new ConfigDescription(
                    "Density of ground blood, as a multiplier on the artwork's own alpha.\n" +
                    "This exists because of what GroundFade changed. Under vanilla's Cutout " +
                    "rendering, 98% of a splat was drawn FULLY opaque no matter what the texture " +
                    "said - it read as a solid puddle because cutout has no other option. Under " +
                    "Fade the same pixels draw at their real alpha, which averages 0.78, so the " +
                    "splat became noticeably thinner. Nothing broke; the texture is simply being " +
                    "told the truth for the first time.\n" +
                    "  1    the artwork exactly as authored - thinner than you are used to\n" +
                    "  1.4  default; roughly the density Cutout used to force, soft edges kept\n" +
                    "  2+   heavier than vanilla ever was\n" +
                    "Multiplies and clamps rather than brightening evenly, so the solid middle " +
                    "reaches full while the feathered rim stays soft and can still fade out.",
                    new AcceptableValueRange<float>(1f, 3f)));

            DirectionalSpray = Bind2("2 - Advanced", "DirectionalSpray", true,
                new ConfigDescription(
                    "Throw blood AWAY FROM THE BLOW instead of outward in every direction.\n" +
                    "Vanilla emits the 200 droplets from a sphere of radius 0.32 with no " +
                    "directional randomness at all, so every droplet flies straight out from one " +
                    "point and the spray expands as a ball. A ball is what an explosion looks " +
                    "like; blood from a wound goes one way.\n" +
                    "On swaps that sphere for a cone aimed along the hit direction, which the " +
                    "game already records on every hit. Hits with no direction - falls, burns, " +
                    "poison - keep the vanilla ball, which is the right answer when nothing " +
                    "struck from a particular side.\n" +
                    "Applied only to the spawned effect, never to the prefab, so turning this off " +
                    "restores vanilla immediately with nothing to undo."));

            SprayAngle = Bind2("2 - Advanced", "SprayAngle", 35f,
                new ConfigDescription(
                    "How wide the spray cone opens, in degrees. Needs DirectionalSpray on.\n" +
                    "  15  a tight jet\n" +
                    "  35  default; a fan\n" +
                    "  90  a full hemisphere, close to vanilla's ball again",
                    new AcceptableValueRange<float>(1f, 90f)));

            SprayLift = Bind2("2 - Advanced", "SprayLift", 0.35f,
                new ConfigDescription(
                    "How much the spray is tilted upward rather than straight back along the " +
                    "blow. 0 sends it flat, which buries most of it in the floor immediately; 1 " +
                    "sends it straight up regardless of where the hit came from. Needs " +
                    "DirectionalSpray on.",
                    new AcceptableValueRange<float>(0f, 1f)));

            SprayRadius = Bind2("2 - Advanced", "SprayRadius", 0.2f,
                new ConfigDescription(
                    "Size of the area droplets are born in, in metres. Vanilla's sphere is 0.32. " +
                    "Smaller reads as a single wound, larger as a broad impact. Needs " +
                    "DirectionalSpray on.",
                    new AcceptableValueRange<float>(0.05f, 1f)));

            DecalMaterials = Bind2("2 - Advanced", "DecalMaterials",
                "splat_decal_blend,seeker_blood_splat,SeekerQueen_decals",
                "Comma-separated material names whose ground decals count as blood, matched as " +
                "prefixes. Deliberately a whitelist rather than every ParticleDecal, because the " +
                "game also uses decals for non-blood things - the 'puke' material has 9 owners " +
                "and must not be included. Add entries for modded creatures that use their own " +
                "blood material. The vanilla ones are always covered in code, so this adds to " +
                "that list rather than replacing it and a stale config cannot leave one uncovered.");

            ExcludeEffects = Bind2("2 - Advanced", "ExcludeEffects",
                "coldbreath,dragonegg,egg_splat,projectile_spit,projectile_teleport,spithit",
                "Effects that must NOT be treated as blood, matched as fragments of the effect " +
                "name. Valheim reuses the blood decal material for things that are not blood: " +
                "Moder's frost breath renders through splat_decal_blend, and the Seeker Queen's " +
                "acid spit through seeker_blood_splat. A material-based whitelist cannot tell " +
                "those apart from real blood, so they are named here and handed back the original " +
                "vanilla texture instead. Add fragments if a modded effect gets wrongly blooded.");

            FixGreenSplash = Bind2("2 - Advanced", "FixGreenSplash", true,
                "Every blood effect contains a 'wetsplsh' child using the 'slime_green' material - " +
                "a green-tinted splash that plays on every blood hit. This clones that material and " +
                "assigns the clone to those renderers only, so it becomes blood-coloured while " +
                "slimes keep the original untouched. Only applied inside effects that also carry a " +
                "blood decal, so nothing else can be caught by it.");

            ColorFallbackSaturation = Bind2("2 - Advanced", "ColorFallbackSaturation", 0.35f,
                new ConfigDescription(
                    "Decals whose authored colour is less saturated than this get repainted with " +
                    "BloodColor. Needed because the replacement texture is pure white, so an " +
                    "effect's own tint is now the only source of colour - and a few effects " +
                    "(the player's included) author something near-grey and relied on vanilla's " +
                    "reddish texture for the red. Set to 0 to disable and keep every authored " +
                    "colour exactly as the game has it, grey ones included. Raise it if other " +
                    "creatures still look washed out; lower it if a creature that should have " +
                    "odd-coloured blood is being forced red.",
                    new AcceptableValueRange<float>(0f, 1f)));

            BloodTint = Bind2("2 - Advanced", "BloodTint", 0.5f,
                new ConfigDescription(
                    "How far RED blood is pulled toward BloodColor, for a deeper, browner red.\n" +
                    "  0    the game's own colours, untouched\n" +
                    "  0.5  default\n" +
                    "  1    exactly BloodColor\n" +
                    "Vanilla authors red blood very bright and very saturated - a boar is " +
                    "RGBA(0.868, 0.006, 0.006), nearly primary red - which reads more like paint " +
                    "than blood. Only colours that are ALREADY red are affected: greydwarf blood " +
                    "is yellow and neck blood is green by the game's own design, and both are " +
                    "left exactly as they are at any value.",
                    new AcceptableValueRange<float>(0f, 1f)));

            BloodColor = Bind2("2 - Advanced", "BloodColor", "#8C0505",
                "Colour used to repaint washed-out decals. Hex. Brightness and transparency are " +
                "taken from the original, so only hue and saturation come from this.");

            DecalAging = Bind2("2 - Advanced", "DecalAging", true,
                "Ground blood darkens as it ages and fades out at the end of its life instead of " +
                "vanishing in a single frame. Vanilla sets no colour-over-lifetime at all, so " +
                "fresh blood and minute-old blood look identical and then blink away.");

            DryDarkening = Bind2("2 - Advanced", "DryDarkening", 0.5f,
                new ConfigDescription(
                    "How far ground blood darkens over its life, 0 = no change. Multiplies the " +
                    "effect's own colour, so per-creature blood colour is preserved and simply " +
                    "browns as it dries.",
                    new AcceptableValueRange<float>(0f, 1f)));

            DecalGrowIn = Bind2("2 - Advanced", "DecalGrowIn", true,
                "Ground blood grows from 82% to full size over the first few percent of its life, " +
                "so it reads as liquid spreading on contact rather than a stamp appearing.");

            SizeJitter = Bind2("2 - Advanced", "SizeJitter", 0.35f,
                new ConfigDescription(
                    "Widens each effect's random size range around its own midpoint, so marks " +
                    "from the same effect vary in size. SizeMultiplier scales everything by the " +
                    "same factor and cannot produce variation; this can. 0 disables.",
                    new AcceptableValueRange<float>(0f, 0.8f)));

            PoolOnDeath = Bind2("2 - Advanced", "PoolOnDeath", true,
                "Leave a settled pool of blood under a kill. Every other mark the mod makes is an " +
                "impact at the instant of the hit; nothing reads as blood that has run out and " +
                "spread, because vanilla has no such concept. The pool is emitted into the death " +
                "effect's own decal system, so it inherits that creature's blood colour and dries " +
                "and fades along with everything else.");

            PoolSize = Bind2("2 - Advanced", "PoolSize", 3.5f,
                new ConfigDescription(
                    "Size of a death pool in world units. Vanilla impact decals run 1 to 6, with " +
                    "a troll death at 4-6, so 3.5 reads as a substantial pool without exceeding " +
                    "the largest thing the game already draws.",
                    new AcceptableValueRange<float>(0.5f, 10f)));

            PoolLifetime = Bind2("2 - Advanced", "PoolLifetime", 45f,
                new ConfigDescription(
                    "How long a death pool lasts, in seconds, before LifetimeMultiplier applies. " +
                    "Deliberately far longer than an impact splat - a pool is the thing that " +
                    "should still be there when you walk back past. It also grows more slowly for " +
                    "free: the grow-in curve is normalised to each particle's own lifetime, so a " +
                    "long-lived pool spreads over seconds rather than instantly.",
                    new AcceptableValueRange<float>(5f, 120f)));

            PoolCount = Bind2("2 - Advanced", "PoolCount", 3,
                new ConfigDescription(
                    "Overlapping decals per pool, jittered slightly apart so they read as one " +
                    "irregular mass rather than concentric stamps.",
                    new AcceptableValueRange<int>(1, 8)));

#if DIAGNOSTICS
            Diagnostics = Config.Bind("3 - Diagnostics", "LogEffects", false,
                "Log each blood effect and decal the first time it is seen - its chance, size, " +
                "colour, material and texture. Each line is logged once per effect, so it is " +
                "bounded, but on a fresh world it is a few hundred lines. Off by default: it is " +
                "a tuning tool, and it is the first thing to turn on if blood looks wrong, " +
                "because it reports what the game actually authored rather than what anyone " +
                "assumes.");

            DumpTextures = Config.Bind("3 - Diagnostics", "DumpTextures", false,
                "Write an inventory of every particle texture in the game, ranked by texels per " +
                "world unit - fewest first, so genuinely under-resolved effects sort to the top. " +
                "Answers whether an effect is worth re-rendering, which cannot be judged from raw " +
                "resolution alone: an 8x8 texture on a 0.05-unit droplet is fine, 64x64 stretched " +
                "over an 8-unit frost-breath cloud is not. Off by default; costs one pass at " +
                "world load.");

            DumpTextureImages = Config.Bind("3 - Diagnostics", "DumpTextureImages", false,
                "Also save each of those textures as a PNG, into BepInEx/config/" +
                "carturblood_textures/. Needs DumpTextures on. Game textures are compressed and " +
                "not CPU-readable, so each is blitted through a RenderTexture and read back - " +
                "that is slow and allocates, hence a separate switch from the inventory itself.");

            DumpDelaySeconds = Config.Bind("3 - Diagnostics", "DumpDelaySeconds", 25f,
                new ConfigDescription(
                    "Seconds after world load before the texture dump runs. Texture packs apply " +
                    "their material overrides after the scene is up - HDVT's land later - so a " +
                    "dump taken at world load records vanilla textures and misses every HD " +
                    "replacement.",
                    new AcceptableValueRange<float>(1f, 120f)));

            AuditMaterials = Config.Bind("3 - Diagnostics", "AuditMaterials", false,
                "Audit which renderers can actually draw the materials named in " +
                "AuditMaterialNames. Written because the texture inventory only looked at " +
                "ParticleSystemRenderer.sharedMaterial and so could not see trail materials, " +
                "mesh or line renderers, or later material slots - and it recorded emission " +
                "state, which does not prove a system is inert because particles can still " +
                "arrive from script Emit() or a parent's sub-emitter. This records maxParticles " +
                "per system, which is the only value that rules those out. Off by default; " +
                "read-only, one pass at world load.");

            AuditMaterialNames = Config.Bind("3 - Diagnostics", "AuditMaterialNames",
                "blood_splat2,blood_splat,blood_drop,blood_cloud,splat_decal_blend",
                "Comma-separated material names to audit, matched as prefixes. Defaults to the " +
                "blood materials, with blood_splat2 first - that is the one carrying HD Valheim " +
                "Textures' 2048 blood art on 43 systems that all appear to have emission off, " +
                "and the open question is whether it is genuinely never drawn.");
#endif

            DebugTuning.Bind(Config);

            // Subscribed after every Bind above, so the binding itself cannot trip it.
            WatchForManualEdits();

            _harmony = new Harmony(Guid);
            _harmony.PatchAll();

            Log.LogInfo("Cartur's HD Blood 1.1.0 loaded.");

            // Hot reload (BepInEx ScriptEngine, F6): apply immediately if a world is already up.
            //
            // Everything this mod does is triggered from ZNetScene.Awake, which fires once per
            // world load and NOT when the assembly is swapped. Without this, a reload would
            // re-apply the Harmony patches and then appear to do nothing at all until a trip to
            // the main menu and back - which is most of what hot reload is meant to save.
            //
            // Harmless in the normal case: at real startup there is no ZNetScene yet, so this is
            // skipped and the patch does the work as usual.
            if (ZNetScene.instance != null)
            {
                Log.LogInfo("A world is already loaded - re-applying now (hot reload).");
                ApplyToScene(ZNetScene.instance);
            }
        }

        /// Everything that has to happen once per world, in one place so the hot-reload path
        /// and the ZNetScene.Awake patch cannot drift apart.
        internal static void ApplyToScene(ZNetScene scene)
        {
            // Before the diagnostics, so the first decal of the session already has our texture.
            BloodSkin.PreSkin(scene);
            // Finds the systems to copy. The copying itself happens per spawned effect - see
            // CloudGraft for why it cannot be done to the prefabs here.
            CloudGraft.Cache(scene);
            // Instance ids are unique per session; this only keeps the set from growing as
            // worlds are loaded and unloaded.
            Pooling.Clear();
            DebugTuning.Reset();
            SprayAim.Reset();
        }

#if DIAGNOSTICS
        private void Update() => TextureDump.Tick();
#endif

        private void OnDestroy() => _harmony?.UnpatchSelf();
    }

    /// The blood prefabs aren't networked, so they never pass through ZNetScene.AddInstance and
    /// can't be found that way. The scene being ready is the earliest point every prefab can be
    /// walked, which is what the diagnostic passes need.
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class Patch_ZNetScene_Awake
    {
        private static void Postfix(ZNetScene __instance)
        {
            // Re-read the config file first.
            //
            // BepInEx binds config values at plugin startup and does not watch the file, so an
            // edit made while the game is running is invisible until a full restart. Reloading
            // here means changing a setting only costs a trip to the main menu and back, which
            // matters a great deal when tuning something that can only be judged in-world.
            try
            {
                Plugin.Instance?.Config.Reload();
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Could not reload config: " + e.Message);
            }

            Plugin.ApplyToScene(__instance);

#if DIAGNOSTICS
            if (!Plugin.Diagnostics.Value)
                return;
            BloodProbe.Run(__instance);
            EffectGraph.Run(__instance);

            // The texture dump is deliberately NOT run here.
            //
            // HD Valheim Textures applies its material overrides after world load, not before, so
            // reading textures in this postfix captures vanilla and misses every HD replacement -
            // splat_decal_blend still holds the 64x64 brains at this point, and only later becomes
            // the 1024 version. The dump is scheduled for a delay instead.
            TextureDump.Schedule(__instance);
            MaterialAudit.Run(__instance);
#endif
        }
    }
}
