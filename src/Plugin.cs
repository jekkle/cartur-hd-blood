using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace CarturHDBlood
{
    [BepInPlugin(Guid, "Cartur's HD Blood", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.jekkle.valheim.carturhdblood";

        internal static Plugin Instance;
        internal static ManualLogSource Log;
        internal static string ConfigDir;

        internal static ConfigEntry<bool> ModEnabled;
        internal static ConfigEntry<BloodLevel> Level;
        internal static ConfigEntry<string> DecalMaterials;
        internal static ConfigEntry<bool> ReplaceTexture;
        internal static ConfigEntry<bool> UseVariantAtlas;
        internal static ConfigEntry<int> AtlasCellSize;
        internal static ConfigEntry<int> SprayCellSize;
        internal static ConfigEntry<int> AtlasColumns;
        internal static ConfigEntry<int> AtlasRows;
        internal static ConfigEntry<bool> SizeAwareVariants;
        internal static ConfigEntry<float> SmallDecalSize;
        internal static ConfigEntry<bool> ReplaceSprayTexture;
        internal static ConfigEntry<string> SprayMaterials;
        internal static ConfigEntry<bool> SprayVariants;
        internal static ConfigEntry<float> SprayDensity;
        internal static ConfigEntry<float> HitSprayDensity;
        internal static ConfigEntry<float> CloudSprayDensity;
        internal static ConfigEntry<float> SprayLifetimeMultiplier;
        internal static ConfigEntry<float> SpraySizeMultiplier;
        internal static ConfigEntry<float> MistBias;
        internal static ConfigEntry<bool> DecoupleGroundFromSpray;
        internal static ConfigEntry<string> ExcludeEffects;
        internal static ConfigEntry<float> HitEffectThreshold;
        internal static ConfigEntry<int> SprayAtlasColumns;
        internal static ConfigEntry<int> SprayAtlasRows;
        internal static ConfigEntry<bool> FixGreenSplash;
        internal static ConfigEntry<bool> ReplaceNormalMap;
        internal static ConfigEntry<float> SizeMultiplier;
        internal static ConfigEntry<float> LifetimeMultiplier;
        internal static ConfigEntry<float> ChanceMultiplier;
        internal static ConfigEntry<float> MinChance;
        internal static ConfigEntry<float> ColorFallbackSaturation;
        internal static ConfigEntry<string> BloodColor;
        internal static ConfigEntry<int> MaxDecals;
        internal static ConfigEntry<bool> DecalAging;
        internal static ConfigEntry<float> DryDarkening;
        internal static ConfigEntry<bool> DecalGrowIn;
        internal static ConfigEntry<float> SizeJitter;
        internal static ConfigEntry<float> SprayGravity;
        internal static ConfigEntry<float> SprayDrag;
        internal static ConfigEntry<bool> PoolOnDeath;
        internal static ConfigEntry<float> PoolSize;
        internal static ConfigEntry<float> PoolLifetime;
        internal static ConfigEntry<int> PoolCount;
        internal static ConfigEntry<bool> SizeAwareSpray;
        internal static ConfigEntry<float> LargeSpraySize;
        internal static ConfigEntry<int> MistFrameRow;
        internal static ConfigEntry<bool> SprayTrails;
        internal static ConfigEntry<float> TrailRatio;
        internal static ConfigEntry<float> TrailLifetime;
        internal static ConfigEntry<float> TrailWidth;
        internal static ConfigEntry<bool> Diagnostics;
        internal static ConfigEntry<bool> DumpTextures;
        internal static ConfigEntry<bool> DumpTextureImages;
        internal static ConfigEntry<float> DumpDelaySeconds;
        internal static ConfigEntry<bool> AuditMaterials;
        internal static ConfigEntry<string> AuditMaterialNames;

        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            ConfigDir = Path.GetDirectoryName(Config.ConfigFilePath);

            ModEnabled = Config.Bind("General", "Enabled", true,
                "Master switch. Off leaves vanilla blood completely untouched.");

            Level = Config.Bind("General", "BloodLevel", BloodLevel.Normal,
                "How much blood. Sets chance, size, lifetime and the decal cap together, so the " +
                "four Amount settings below don't have to be reasoned about individually.\n" +
                "  Low     - less and shorter-lived than vanilla\n" +
                "  Normal  - vanilla's own amounts, so only the artwork differs\n" +
                "  High    - every kill marks the ground, decals last about twice as long\n" +
                "  Extreme - guaranteed marks, large, long-lasting, high cap\n" +
                "  Custom  - use the individual values in the Amount section instead\n" +
                "The presets exist because vanilla puts most creatures' death splat at only 10% " +
                "chance, so raising that floor is what actually produces more blood - a plain " +
                "multiplier does nothing to the majority already at 100%.");

            DecalMaterials = Config.Bind("Texture", "DecalMaterials",
                "splat_decal_blend,seeker_blood_splat,SeekerQueen_decals",
                "Comma-separated material names whose decals get re-skinned, matched as prefixes. " +
                "These are the game's blood decal materials: splat_decal_blend covers 88 " +
                "per-creature effects, the seeker ones cover another 17. Deliberately a whitelist " +
                "rather than every ParticleDecal, because the game also uses decals for non-blood " +
                "things - the 'puke' material has 9 owners and must not be included. Add entries " +
                "here for modded creatures that use their own blood material. The vanilla ones " +
                "are always covered in code, so this setting adds to that list rather than " +
                "replacing it and a stale config cannot leave one uncovered.");

            ExcludeEffects = Config.Bind("Texture", "ExcludeEffects",
                "coldbreath,dragonegg,egg_splat,projectile_spit,projectile_teleport,spithit",
                "Effects that must NOT be treated as blood, matched as fragments of the effect " +
                "name. Valheim reuses the blood decal material for things that are not blood: " +
                "Moder's frost breath renders through splat_decal_blend, and the Seeker Queen's " +
                "acid spit through seeker_blood_splat. A material-based whitelist cannot tell " +
                "those apart from real blood, so they are named here and handed back the original " +
                "vanilla texture instead. Add fragments if a modded effect gets wrongly blooded.");

            ReplaceTexture = Config.Bind("Texture", "ReplaceDecalTexture", true,
                "Replace the ground blood decal texture. Note this overrides any texture pack's " +
                "version of it, because it is applied later - when a decal spawns, not at load.");

            UseVariantAtlas = Config.Bind("Texture", "UseVariantAtlas", true,
                "Treat the texture as a grid (see AtlasColumns/AtlasRows) and pick a random cell " +
                "per splat, so kills get twelve different large shapes instead of one shape rotated. " +
                "Turn off if the shader mishandles the sub-rect and splats appear cropped or " +
                "tiled - then only the top-left shape is used.");

            AtlasCellSize = Config.Bind("Texture", "AtlasCellSize", 1024,
                new ConfigDescription(
                    "Pixel size of one cell in the splat atlas. Used to work out the grid from " +
                    "the texture, and it is also the detail ceiling for a ground mark: a decal " +
                    "several metres across, viewed from standing height, can occupy a few hundred " +
                    "screen pixels, so 512 is adequate and 1024 is visibly sharper up close. Each " +
                    "cell becomes its own texture at load, so 16 cells at 1024 costs roughly four " +
                    "times the memory of 512. Drop to 512 if that matters.",
                    new AcceptableValueList<int>(256, 512, 1024)));

            SprayCellSize = Config.Bind("Texture", "SprayCellSize", 512,
                new ConfigDescription(
                    "Pixel size of one cell in the spray atlas. Droplets render at 0.05-0.7 " +
                    "world units and need very little, but the blood_cloud systems run 5-12 " +
                    "units and draw from the mist row, so the sheet is sized for those: a 256px " +
                    "cell stretched across twelve metres is mush. 512 is the compromise, since " +
                    "the sheet is shared by both.",
                    new AcceptableValueList<int>(128, 256, 512)));

            AtlasColumns = Config.Bind("Texture", "AtlasColumns", 3,
                new ConfigDescription(
                    "Columns in the splat atlas. Must match the shipped texture, or the wrong " +
                    "sub-rects get sampled and splats appear cropped.",
                    new AcceptableValueRange<int>(1, 8)));

            AtlasRows = Config.Bind("Texture", "AtlasRows", 3,
                new ConfigDescription(
                    "Rows in the splat atlas. Shipped texture is 3x3 - nine splats at 1024px in a " +
                    "3072x3072 sheet: six large marks across rows 0-1 and three small marks on " +
                    "row 2. Every row above the last is the 'large' set and the last row is the " +
                    "'small' set, so a grid with no spare slots is required - an empty cell would " +
                    "still get a material built for it and would draw an invisible decal. Both " +
                    "values are auto-corrected from the texture size if they disagree with it.",
                    new AcceptableValueRange<int>(1, 8)));

            SizeAwareVariants = Config.Bind("Texture", "SizeAwareVariants", true,
                "Choose the atlas row by how big the decal is: large splats from the top row, " +
                "small droplet marks from the bottom. Without this a random cell means a size-1 " +
                "graze can draw a full death burst while a troll's size-6 splat draws two " +
                "droplets - the artwork is random but quad size is authored per creature, and " +
                "nothing otherwise connects them. Turn off to pick freely from the whole sheet.");

            SmallDecalSize = Config.Bind("Texture", "SmallDecalSize", 1.75f,
                new ConfigDescription(
                    "Decals with an authored size below this count as small and use the bottom " +
                    "atlas row. Vanilla sizes run from 1..1.5 on light hits up to 4..6 on a " +
                    "troll death, so 1.75 splits roughly between grazes and real wounds.",
                    new AcceptableValueRange<float>(0.5f, 6f)));

            ReplaceSprayTexture = Config.Bind("Texture", "ReplaceSprayTexture", true,
                "Also replace the airborne spray - the particles that fly off on a hit, as " +
                "opposed to the mark left on the ground. Vanilla uses an 8x8 texture for one of " +
                "these materials and no texture at all for the other, so those particles draw as " +
                "flat quads.");

            SprayMaterials = Config.Bind("Texture", "SprayMaterials", "blood_splat,blood_drop",
                "Comma-separated material names for the airborne spray, matched as prefixes. " +
                "'slime_green' is the third material inside the blood effects and is left out on " +
                "purpose - Blobs very likely share it, and replacing it would recolour every " +
                "slime in the game. Note the game's own blood materials - blood_splat, blood_drop " +
                "and blood_cloud - are always covered in code, so this setting ADDS to that list " +
                "rather than replacing it; an out-of-date config cannot leave one uncovered.");

            SprayVariants = Config.Bind("Texture", "SprayVariants", true,
                "Treat the spray texture as a 2x2 grid and pick a random cell per particle, so " +
                "airborne blood gets four droplet shapes instead of one repeated. Same trick as " +
                "the ground decals.");

            HitEffectThreshold = Config.Bind("Amount", "HitEffectThreshold", 0.02f,
                new ConfigDescription(
                    "Minimum share of the target's max health a hit must do before it produces " +
                    "blood, as a fraction. Vanilla is 0.1 - a tenth - so a 50-damage swing on a " +
                    "600 HP troll does 8.3% and draws no blood at all, which is why non-killing " +
                    "hits on big creatures look dry. Because the bar scales with health, tougher " +
                    "creatures bleed less. 0.02 means almost every real hit bleeds. Set to 0.1 or " +
                    "above to leave vanilla behaviour untouched.",
                    new AcceptableValueRange<float>(0f, 0.1f)));

            HitSprayDensity = Config.Bind("Amount", "HitSprayDensity", 6.0f,
                new ConfigDescription(
                    "Airborne particles emitted by a HIT, as a multiplier on vanilla. Separate " +
                    "from SprayDensity because hits are the dry case: vanilla bursts about five " +
                    "chunks and thirty splash on a hit and two hundred on a death, so one shared " +
                    "figure either leaves hits thin or makes deaths absurd. Particle caps are " +
                    "raised to match, since a cap of 50 would otherwise swallow a multiplied " +
                    "burst. Set to 0 to use SprayDensity for hits as well.",
                    new AcceptableValueRange<float>(0f, 12f)));

            CloudSprayDensity = Config.Bind("Amount", "CloudSprayDensity", 1.0f,
                new ConfigDescription(
                    "Density multiplier for the blood_cloud systems specifically, which are mist " +
                    "rather than droplets. A greydwarf or neck death already fires 88 cloud " +
                    "particles across three systems at 0.5-1.5 units each; putting the droplet " +
                    "multiplier on top produced roughly 265 overlapping clouds and read as an " +
                    "explosion of mist on ordinary kills. Left at 1.0 the clouds stay vanilla " +
                    "while droplets can be as dense as you like.",
                    new AcceptableValueRange<float>(0.25f, 6f)));

            SprayLifetimeMultiplier = Config.Bind("Amount", "SprayLifetimeMultiplier", 2.2f,
                new ConfigDescription(
                    "How much longer airborne blood lasts. Vanilla spray lives 0.4 to 1 second, " +
                    "so it is gone almost as soon as it appears and never builds into anything - " +
                    "this does more for a misty, bloody feel than raising the particle count. " +
                    "Combines with gravity and drag, so longer-lived mist has time to slow, hang " +
                    "and drift rather than vanishing mid-arc.",
                    new AcceptableValueRange<float>(0.5f, 6f)));

            SpraySizeMultiplier = Config.Bind("Amount", "SpraySizeMultiplier", 1.4f,
                new ConfigDescription(
                    "Scales airborne particle size. Droplets are authored at 0.05-0.7 world " +
                    "units, which is a few screen pixels each, so a modest increase reads as " +
                    "considerably more blood in the air without emitting anything extra. Note it " +
                    "can push a particle past LargeSpraySize and into the mist frames, which is " +
                    "usually what you want.",
                    new AcceptableValueRange<float>(0.5f, 4f)));

            MistBias = Config.Bind("Amount", "MistBias", 0.35f,
                new ConfigDescription(
                    "Chance that a small spray system draws from the mist row instead of the " +
                    "whole sheet. Left alone the mist is 4 frames of 16, so only a quarter of " +
                    "small particles are hazy; this raises that share. Decided per system rather " +
                    "than per particle - Unity's startFrame takes a range, not a weighted " +
                    "distribution - but a hit fires several systems at once so a burst still " +
                    "comes out mixed. 0 disables.",
                    new AcceptableValueRange<float>(0f, 1f)));

            DecoupleGroundFromSpray = Config.Bind("Amount", "DecoupleGroundFromSpray", true,
                "Keep ground blood at its normal rate while the air is thick. The particles that " +
                "fly through the air are the same ones that mark the ground - a decal is emitted " +
                "per particle collision - so raising spray density would otherwise multiply " +
                "ground decals by the same factor and carpet the terrain. This divides the decal " +
                "chance by the spray multiplier so the number of marks stays vanilla-like no " +
                "matter how dense the spray. Turn off if you want more air to mean more ground.");

            SprayAtlasColumns = Config.Bind("Texture", "SprayAtlasColumns", 4,
                new ConfigDescription(
                    "Columns in the spray atlas. Shipped sheet is 4x4 - sixteen frames: six mid-air " +
                    "droplets, four aerosol mists, four thrown streaks, a spray fan and a slash. " +
                    "Airborne blood mixes droplets, haze and fast streaks instead of one repeated " +
                    "shape.",
                    new AcceptableValueRange<int>(1, 8)));

            SprayAtlasRows = Config.Bind("Texture", "SprayAtlasRows", 4,
                new ConfigDescription(
                    "Rows in the spray atlas. Change together with SprayAtlasColumns if you " +
                    "supply your own sheet.",
                    new AcceptableValueRange<int>(1, 8)));

            SprayDensity = Config.Bind("Amount", "SprayDensity", 1.0f,
                new ConfigDescription(
                    "Multiplies how many particles the airborne spray emits. Vanilla is modest - " +
                    "a hit bursts 5 chunks, 30 splash and 50 droplets, a death 200 - so the air " +
                    "clears almost at once while the ground mark lingers ten seconds. Raising this " +
                    "closes that gap. This is the general figure - hits use HitSprayDensity " +
                    "instead when that is above zero. Applies at every BloodLevel, including " +
                    "Normal, because it is about the air rather than the ground.",
                    new AcceptableValueRange<float>(0.25f, 8f)));

            FixGreenSplash = Config.Bind("Texture", "FixGreenSplash", true,
                "Every blood effect contains a 'wetsplsh' child using the 'slime_green' material - " +
                "a green-tinted splash that plays on every blood hit. This clones that material and " +
                "assigns the clone to those renderers only, so it becomes blood-coloured while " +
                "slimes keep the original untouched. Only applied inside effects that also carry a " +
                "blood decal, so nothing else can be caught by it.");

            ReplaceNormalMap = Config.Bind("Texture", "ReplaceNormalMap", true,
                "Also replace the decal's normal map, so lighting follows the new splat shape. " +
                "Turn off if the blood looks oddly shaded - the custom shader's expected normal " +
                "encoding is not documented.");

            SizeMultiplier = Config.Bind("Amount", "SizeMultiplier", 1.0f,
                new ConfigDescription(
                    "Only used when BloodLevel is Custom. Scales decal size. A multiplier rather " +
                    "than a fixed size, so per-creature " +
                    "design is preserved - a troll marks more ground than a greyling.",
                    new AcceptableValueRange<float>(0.25f, 4f)));

            LifetimeMultiplier = Config.Bind("Amount", "LifetimeMultiplier", 1.0f,
                new ConfigDescription(
                    "Only used when BloodLevel is Custom. Scales how long decals linger; vanilla " +
                    "ranges from about 5 to 10 seconds.",
                    new AcceptableValueRange<float>(0.25f, 10f)));

            ChanceMultiplier = Config.Bind("Amount", "ChanceMultiplier", 1.0f,
                new ConfigDescription(
                    "Only used when BloodLevel is Custom. Scales the chance a collision leaves a " +
                    "mark. Many effects are already at " +
                    "100, where this has no effect - MinChance is what lifts the low ones.",
                    new AcceptableValueRange<float>(0.1f, 10f)));

            MinChance = Config.Bind("Amount", "MinChance", 10f,
                new ConfigDescription(
                    "Only used when BloodLevel is Custom. Floor for the decal chance, in percent. " +
                    "Vanilla sets some of the biggest " +
                    "kills low - troll death is 10 - so raising this is the single most effective " +
                    "dial for 'more blood on the ground'. 10 is the vanilla value, changing nothing.",
                    new AcceptableValueRange<float>(0f, 100f)));

            ColorFallbackSaturation = Config.Bind("Colour", "ColorFallbackSaturation", 0.35f,
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

            BloodColor = Config.Bind("Colour", "BloodColor", "#8C0505",
                "Colour used to repaint washed-out decals. Hex. Brightness and transparency are " +
                "taken from the original, so only hue and saturation come from this.");

            MaxDecals = Config.Bind("Amount", "MaxDecals", 0,
                new ConfigDescription(
                    "Only used when BloodLevel is Custom. Raise each decal system to at least this " +
                    "particle cap. Needed if you raise " +
                    "chance a lot, or new decals just evict old ones and the ground never fills up. " +
                    "0 leaves vanilla caps alone (they range from 10 to 1000).",
                    new AcceptableValueRange<int>(0, 2000)));

            DecalAging = Config.Bind("Realism", "DecalAging", true,
                "Ground blood darkens as it ages and fades out at the end of its life instead of " +
                "vanishing in a single frame. Vanilla sets no colour-over-lifetime at all, so " +
                "fresh blood and minute-old blood look identical and then blink away.");

            DryDarkening = Config.Bind("Realism", "DryDarkening", 0.5f,
                new ConfigDescription(
                    "How far ground blood darkens over its life, 0 = no change. Multiplies the " +
                    "effect's own colour, so per-creature blood colour is preserved and simply " +
                    "browns as it dries.",
                    new AcceptableValueRange<float>(0f, 1f)));

            DecalGrowIn = Config.Bind("Realism", "DecalGrowIn", true,
                "Ground blood grows from 82% to full size over the first few percent of its life, " +
                "so it reads as liquid spreading on contact rather than a stamp appearing.");

            SizeJitter = Config.Bind("Realism", "SizeJitter", 0.35f,
                new ConfigDescription(
                    "Widens each effect's random size range around its own midpoint, so marks " +
                    "from the same effect vary in size. SizeMultiplier scales everything by the " +
                    "same factor and cannot produce variation; this can. 0 disables.",
                    new AcceptableValueRange<float>(0f, 0.8f)));

            SprayGravity = Config.Bind("Realism", "SprayGravity", 0.85f,
                new ConfigDescription(
                    "Gravity applied to airborne blood. Vanilla leaves this at zero, so spray " +
                    "flies in a straight line at constant speed until it expires - the most " +
                    "artificial thing about the airborne blood. Around 0.85 gives a believable " +
                    "arc. Only applied where the effect had no authored gravity of its own.",
                    new AcceptableValueRange<float>(0f, 3f)));

            SprayDrag = Config.Bind("Realism", "SprayDrag", 0.06f,
                new ConfigDescription(
                    "Air resistance on airborne blood, scaled by particle size. This is what " +
                    "separates mist from droplets while they share one particle system: fine mist " +
                    "slows and hangs, heavy droplets carry on. 0 disables.",
                    new AcceptableValueRange<float>(0f, 0.5f)));

            PoolOnDeath = Config.Bind("Realism", "PoolOnDeath", true,
                "Leave a settled pool of blood under a kill. Every other mark the mod makes is an " +
                "impact at the instant of the hit; nothing reads as blood that has run out and " +
                "spread, because vanilla has no such concept. The pool is emitted into the death " +
                "effect's own decal system, so it inherits that creature's blood colour and dries " +
                "and fades along with everything else.");

            PoolSize = Config.Bind("Realism", "PoolSize", 3.5f,
                new ConfigDescription(
                    "Size of a death pool in world units. Vanilla impact decals run 1 to 6, with " +
                    "a troll death at 4-6, so 3.5 reads as a substantial pool without exceeding " +
                    "the largest thing the game already draws.",
                    new AcceptableValueRange<float>(0.5f, 10f)));

            PoolLifetime = Config.Bind("Realism", "PoolLifetime", 45f,
                new ConfigDescription(
                    "How long a death pool lasts, in seconds, before LifetimeMultiplier applies. " +
                    "Deliberately far longer than an impact splat - a pool is the thing that " +
                    "should still be there when you walk back past. It also grows more slowly for " +
                    "free: the grow-in curve is normalised to each particle's own lifetime, so a " +
                    "long-lived pool spreads over seconds rather than instantly.",
                    new AcceptableValueRange<float>(5f, 120f)));

            PoolCount = Config.Bind("Realism", "PoolCount", 3,
                new ConfigDescription(
                    "Overlapping decals per pool, jittered slightly apart so they read as one " +
                    "irregular mass rather than concentric stamps.",
                    new AcceptableValueRange<int>(1, 8)));

            SizeAwareSpray = Config.Bind("Texture", "SizeAwareSpray", true,
                "Large airborne particles draw from the mist row instead of the whole sheet. The " +
                "blood_cloud material covers both a 0.5-unit soft cloud and a 5-12 unit 'Big " +
                "Splat', so a free choice would stretch a crisp droplet across twelve metres and " +
                "produce a smooth red ball. A soft diffuse cloud is what enlarging should give.");

            LargeSpraySize = Config.Bind("Texture", "LargeSpraySize", 2.5f,
                new ConfigDescription(
                    "Airborne particles with an authored size at or above this count as large and " +
                    "use the mist row. Vanilla droplets run 0.05 to 0.7 and the big cloud systems " +
                    "5 to 12, but vfx_neck_hit's root sits at 1.5..2 - exactly on a 2.0 threshold, " +
                    "which sent every neck hit to the mist frames. 2.5 clears it: a 2-unit " +
                    "particle is not a cloud.",
                    new AcceptableValueRange<float>(0.5f, 12f)));

            MistFrameRow = Config.Bind("Texture", "MistFrameRow", 2,
                new ConfigDescription(
                    "Which row of the spray sheet holds the mist frames, counting from 0 at the " +
                    "top. The shipped 4x4 sheet has droplets on rows 0-1, mist on row 2 and " +
                    "streaks on row 3.",
                    new AcceptableValueRange<int>(0, 7)));

            SprayTrails = Config.Bind("Realism", "SprayTrails", true,
                "Draw ribbon trails behind airborne droplets, so blood streaks through the air " +
                "instead of appearing as separate dots each frame.");

            TrailRatio = Config.Bind("Realism", "TrailRatio", 0.3f,
                new ConfigDescription(
                    "Fraction of spray particles that get a trail. Deliberately well under 1: a " +
                    "trail on every particle turns a dense spray into a solid red mass. At 0.3 it " +
                    "reads as the heavier droplets tearing through the air while the fine mist " +
                    "does not.",
                    new AcceptableValueRange<float>(0f, 1f)));

            TrailLifetime = Config.Bind("Realism", "TrailLifetime", 0.22f,
                new ConfigDescription(
                    "How long a trail persists behind its droplet. Short - this is motion blur, " +
                    "not a streamer.",
                    new AcceptableValueRange<float>(0.02f, 1f)));

            TrailWidth = Config.Bind("Realism", "TrailWidth", 0.35f,
                new ConfigDescription(
                    "Trail width relative to its particle. Scaled by particle size, so mist " +
                    "trails stay finer than droplet trails.",
                    new AcceptableValueRange<float>(0.05f, 2f)));

            Diagnostics = Config.Bind("Diagnostics", "LogEffects", true,
                "Log each blood effect and decal the first time it is seen. Useful while tuning; " +
                "harmless to leave on, as each line is logged once.");

            DumpTextures = Config.Bind("Diagnostics", "DumpTextures", false,
                "Write an inventory of every particle texture in the game, ranked by texels per " +
                "world unit - fewest first, so genuinely under-resolved effects sort to the top. " +
                "Answers whether an effect is worth re-rendering, which cannot be judged from raw " +
                "resolution alone: an 8x8 texture on a 0.05-unit droplet is fine, 64x64 stretched " +
                "over an 8-unit frost-breath cloud is not. Off by default; costs one pass at " +
                "world load.");

            DumpTextureImages = Config.Bind("Diagnostics", "DumpTextureImages", false,
                "Also save each of those textures as a PNG, into BepInEx/config/" +
                "carturblood_textures/. Needs DumpTextures on. Game textures are compressed and " +
                "not CPU-readable, so each is blitted through a RenderTexture and read back - " +
                "that is slow and allocates, hence a separate switch from the inventory itself.");

            DumpDelaySeconds = Config.Bind("Diagnostics", "DumpDelaySeconds", 25f,
                new ConfigDescription(
                    "Seconds after world load before the texture dump runs. Texture packs apply " +
                    "their material overrides after the scene is up - HDVT's land later - so a " +
                    "dump taken at world load records vanilla textures and misses every HD " +
                    "replacement.",
                    new AcceptableValueRange<float>(1f, 120f)));

            AuditMaterials = Config.Bind("Diagnostics", "AuditMaterials", false,
                "Audit which renderers can actually draw the materials named in " +
                "AuditMaterialNames. Written because the texture inventory only looked at " +
                "ParticleSystemRenderer.sharedMaterial and so could not see trail materials, " +
                "mesh or line renderers, or later material slots - and it recorded emission " +
                "state, which does not prove a system is inert because particles can still " +
                "arrive from script Emit() or a parent's sub-emitter. This records maxParticles " +
                "per system, which is the only value that rules those out. Off by default; " +
                "read-only, one pass at world load.");

            AuditMaterialNames = Config.Bind("Diagnostics", "AuditMaterialNames",
                "blood_splat2,blood_splat,blood_drop,blood_cloud,splat_decal_blend",
                "Comma-separated material names to audit, matched as prefixes. Defaults to the " +
                "blood materials, with blood_splat2 first - that is the one carrying HD Valheim " +
                "Textures' 2048 blood art on 43 systems that all appear to have emission off, " +
                "and the open question is whether it is genuinely never drawn.");

            _harmony = new Harmony(Guid);
            _harmony.PatchAll();

            Log.LogInfo("Cartur's HD Blood 1.0.0 loaded.");
        }

        private void Update() => TextureDump.Tick();

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

            // Before the diagnostics, so the first decal of the session already has our texture.
            BloodSkin.PreSkin(__instance);
            // Instance ids are unique per session; this only keeps the set from growing as
            // worlds are loaded and unloaded.
            Pooling.Clear();

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
        }
    }
}
