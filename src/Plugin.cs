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
        internal static ConfigEntry<float> HitEffectThreshold;

        internal static ConfigEntry<float> GroundBlood;
        internal static ConfigEntry<bool> ReplaceTexture;
        internal static ConfigEntry<string> DecalMaterials;
        internal static ConfigEntry<string> ExcludeEffects;
        internal static ConfigEntry<float> SmallDecalSize;
        internal static ConfigEntry<bool> GroundNormalMap;
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

            // Three dials, one section, all on the same scale. Everything else lives under
            // Advanced, because it is either a knob nobody needs to touch or a lever for
            // fixing a modded creature the mod guessed wrong about.
            //
            // The sections are numbered because the config manager sorts them alphabetically,
            // and "Advanced" sorting above "Blood" is exactly backwards for a settings screen.
            ModEnabled = Config.Bind("1 - Blood", "Enabled", true,
                "Master switch. Off leaves vanilla blood completely untouched.");

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

            HitEffectThreshold = Config.Bind("2 - Advanced", "HitEffectThreshold", 0.02f,
                new ConfigDescription(
                    "Minimum share of the target's max health a hit must do before it produces " +
                    "blood, as a fraction. Vanilla is 0.1 - a tenth - so a 50-damage swing on a " +
                    "600 HP troll does 8.3% and draws no blood at all, which is why non-killing " +
                    "hits on big creatures look dry. Because the bar scales with health, tougher " +
                    "creatures bleed less. 0.02 means almost every real hit bleeds. Set to 0.1 or " +
                    "above to leave vanilla behaviour untouched.",
                    new AcceptableValueRange<float>(0f, 0.1f)));

            ReplaceTexture = Config.Bind("2 - Advanced", "ReplaceDecalTexture", true,
                "Replace the ground blood decal texture with this mod's artwork. Off keeps the " +
                "game's own (or a texture pack's) while GroundBlood still controls the amount.");

            SmallDecalSize = Config.Bind("2 - Advanced", "SmallDecalSize", 2f,
                new ConfigDescription(
                    "Ground marks with an authored size below this get the small impact texture; " +
                    "everything above it gets one of the two large ones. Vanilla ground decals " +
                    "run 0.5..1.4 on light hits up to 5..8 on the biggest deaths, so 2 splits " +
                    "roughly between grazes and real wounds.",
                    new AcceptableValueRange<float>(0.5f, 8f)));

            GroundNormalMap = Config.Bind("2 - Advanced", "GroundNormalMap", true,
                "Light the ground blood with its own normal map, so highlights follow the shape " +
                "of the splat and it reads as wet rather than flat. Off leaves the game's own " +
                "normal in place. Turn it off if the lighting looks wrong - the decal shader's " +
                "expected normal encoding is not documented anywhere, and the symptom of getting " +
                "it wrong is highlights on the opposite side from the light.");

            DecalMaterials = Config.Bind("2 - Advanced", "DecalMaterials",
                "splat_decal_blend,seeker_blood_splat,SeekerQueen_decals",
                "Comma-separated material names whose ground decals count as blood, matched as " +
                "prefixes. Deliberately a whitelist rather than every ParticleDecal, because the " +
                "game also uses decals for non-blood things - the 'puke' material has 9 owners " +
                "and must not be included. Add entries for modded creatures that use their own " +
                "blood material. The vanilla ones are always covered in code, so this adds to " +
                "that list rather than replacing it and a stale config cannot leave one uncovered.");

            ExcludeEffects = Config.Bind("2 - Advanced", "ExcludeEffects",
                "coldbreath,dragonegg,egg_splat,projectile_spit,projectile_teleport,spithit",
                "Effects that must NOT be treated as blood, matched as fragments of the effect " +
                "name. Valheim reuses the blood decal material for things that are not blood: " +
                "Moder's frost breath renders through splat_decal_blend, and the Seeker Queen's " +
                "acid spit through seeker_blood_splat. A material-based whitelist cannot tell " +
                "those apart from real blood, so they are named here and handed back the original " +
                "vanilla texture instead. Add fragments if a modded effect gets wrongly blooded.");

            FixGreenSplash = Config.Bind("2 - Advanced", "FixGreenSplash", true,
                "Every blood effect contains a 'wetsplsh' child using the 'slime_green' material - " +
                "a green-tinted splash that plays on every blood hit. This clones that material and " +
                "assigns the clone to those renderers only, so it becomes blood-coloured while " +
                "slimes keep the original untouched. Only applied inside effects that also carry a " +
                "blood decal, so nothing else can be caught by it.");

            ColorFallbackSaturation = Config.Bind("2 - Advanced", "ColorFallbackSaturation", 0.35f,
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

            BloodTint = Config.Bind("2 - Advanced", "BloodTint", 0.5f,
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

            BloodColor = Config.Bind("2 - Advanced", "BloodColor", "#8C0505",
                "Colour used to repaint washed-out decals. Hex. Brightness and transparency are " +
                "taken from the original, so only hue and saturation come from this.");

            DecalAging = Config.Bind("2 - Advanced", "DecalAging", true,
                "Ground blood darkens as it ages and fades out at the end of its life instead of " +
                "vanishing in a single frame. Vanilla sets no colour-over-lifetime at all, so " +
                "fresh blood and minute-old blood look identical and then blink away.");

            DryDarkening = Config.Bind("2 - Advanced", "DryDarkening", 0.5f,
                new ConfigDescription(
                    "How far ground blood darkens over its life, 0 = no change. Multiplies the " +
                    "effect's own colour, so per-creature blood colour is preserved and simply " +
                    "browns as it dries.",
                    new AcceptableValueRange<float>(0f, 1f)));

            DecalGrowIn = Config.Bind("2 - Advanced", "DecalGrowIn", true,
                "Ground blood grows from 82% to full size over the first few percent of its life, " +
                "so it reads as liquid spreading on contact rather than a stamp appearing.");

            SizeJitter = Config.Bind("2 - Advanced", "SizeJitter", 0.35f,
                new ConfigDescription(
                    "Widens each effect's random size range around its own midpoint, so marks " +
                    "from the same effect vary in size. SizeMultiplier scales everything by the " +
                    "same factor and cannot produce variation; this can. 0 disables.",
                    new AcceptableValueRange<float>(0f, 0.8f)));

            PoolOnDeath = Config.Bind("2 - Advanced", "PoolOnDeath", true,
                "Leave a settled pool of blood under a kill. Every other mark the mod makes is an " +
                "impact at the instant of the hit; nothing reads as blood that has run out and " +
                "spread, because vanilla has no such concept. The pool is emitted into the death " +
                "effect's own decal system, so it inherits that creature's blood colour and dries " +
                "and fades along with everything else.");

            PoolSize = Config.Bind("2 - Advanced", "PoolSize", 3.5f,
                new ConfigDescription(
                    "Size of a death pool in world units. Vanilla impact decals run 1 to 6, with " +
                    "a troll death at 4-6, so 3.5 reads as a substantial pool without exceeding " +
                    "the largest thing the game already draws.",
                    new AcceptableValueRange<float>(0.5f, 10f)));

            PoolLifetime = Config.Bind("2 - Advanced", "PoolLifetime", 45f,
                new ConfigDescription(
                    "How long a death pool lasts, in seconds, before LifetimeMultiplier applies. " +
                    "Deliberately far longer than an impact splat - a pool is the thing that " +
                    "should still be there when you walk back past. It also grows more slowly for " +
                    "free: the grow-in curve is normalised to each particle's own lifetime, so a " +
                    "long-lived pool spreads over seconds rather than instantly.",
                    new AcceptableValueRange<float>(5f, 120f)));

            PoolCount = Config.Bind("2 - Advanced", "PoolCount", 3,
                new ConfigDescription(
                    "Overlapping decals per pool, jittered slightly apart so they read as one " +
                    "irregular mass rather than concentric stamps.",
                    new AcceptableValueRange<int>(1, 8)));

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

            _harmony = new Harmony(Guid);
            _harmony.PatchAll();

            Log.LogInfo("Cartur's HD Blood 1.0.0 loaded.");

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

            Plugin.ApplyToScene(__instance);

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
