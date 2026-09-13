using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using BepInEx.Configuration;
using UnityEngine;

namespace CarturHDBlood
{
    /// Live per-element switches and sliders for fine-tuning in game.
    ///
    /// Everything here runs from the spawned effect instance, at the decal's Awake, which is why
    /// it takes effect on the NEXT hit rather than needing a restart. That is the whole reason
    /// this section exists separately from "1 - Blood": those settings are the shipped look, and
    /// several of them (textures, material skinning) are applied once to shared materials at
    /// world load and genuinely cannot change live. These can.
    ///
    /// Off by default. With DebugMenu false nothing in this file touches a single particle, so a
    /// player who never opens it gets exactly the shipped behaviour.
    ///
    /// The three elements it exposes are the three things actually drawn when you hit something,
    /// measured off the greydwarf death effect rather than assumed:
    ///
    ///   blood_drop    200 particles, 0.05 units each, NO TEXTURE - flat shaded quads. At melee
    ///                 range (1.5-3 m) that is 14-28 screen pixels across at 1080p, which is
    ///                 plenty to show a texture; it only falls to 3 pixels across a field.
    ///   blood_cloud    88 particles, 0.5-1.5 units, texture "brains" at 1024. That texture is a
    ///                 field of 4660 separate specks, so every particle stamps all 4660 at once -
    ///                 roughly 6 pixels of real resolution per speck. That is why the burst reads
    ///                 as grain rather than as spray.
    ///   slime_splash  the flat splash quad, tinted per creature by startColor.
    internal static class DebugTuning
    {
        internal static ConfigEntry<bool> DebugMenu;

        internal static ConfigEntry<bool> ShowDroplets;
        internal static ConfigEntry<bool> DropletTexture;
        internal static ConfigEntry<float> DropletSize;
        internal static ConfigEntry<float> DropletSpread;
        internal static ConfigEntry<float> DropletCount;
        internal static ConfigEntry<bool> BigDropsSlowFaster;

        internal static ConfigEntry<bool> ShowCloud;
        internal static ConfigEntry<float> CloudSize;

        internal static ConfigEntry<bool> ShowSplash;
        internal static ConfigEntry<float> SplashSize;

        internal static ConfigEntry<bool> ShowGroundDecals;
        internal static ConfigEntry<float> GroundDecalSize;

        internal static ConfigEntry<bool> ShowDeathPool;
        internal static ConfigEntry<float> DeathPoolSize;
        internal static ConfigEntry<float> DeathPoolCount;

        internal static ConfigEntry<bool> LogShaderProperties;

        internal static ConfigEntry<bool> StretchParticles;
        internal static ConfigEntry<float> StretchAmount;
        internal static ConfigEntry<float> Gravity;
        internal static ConfigEntry<float> Drag;
        internal static ConfigEntry<float> Speed;

        private const string Section = "4 - Debug";

        /// Binds a debug entry and marks it advanced, so the settings screen shows BloodLevel
        /// and little else until Advanced is ticked. Duck-typed by ConfigurationManager; inert
        /// when it is not installed.
        private static ConfigEntry<T> BindDebug<T>(ConfigFile cfg, string section, string key,
                                                   T def, ConfigDescription desc)
        {
            var tags = new ConfigurationManagerAttributes { IsAdvanced = true };
            var d = desc == null
                ? new ConfigDescription("", null, tags)
                : new ConfigDescription(desc.Description, desc.AcceptableValues, tags);
            return cfg.Bind(section, key, def, d);
        }

        public static void Bind(ConfigFile cfg)
        {
            DebugMenu = BindDebug(cfg, Section, "DebugMenu", false,
                new ConfigDescription(
                    "Master switch for everything below. Off means this whole section is ignored " +
                    "and the mod behaves exactly as shipped.\n" +
                    "These settings apply to the NEXT hit or death - no restart needed - because " +
                    "they are applied to each effect as it spawns. Settings in the other sections " +
                    "mostly are not: those change shared materials once at world load."));

            // ---- the 200 droplets ----

            ShowDroplets = BindDebug(cfg, Section, "ShowDroplets", true,
                new ConfigDescription(
                    "The 200 small blood_drop particles. Turn off to see what the rest of the " +
                    "effect contributes on its own.\n" +
                    "Note these carry NO texture in vanilla - they are flat shaded quads."));

            DropletTexture = BindDebug(cfg, Section, "DropletTexture", true,
                new ConfigDescription(
                    "Gives the droplets a texture. Vanilla has none on them at all - they are " +
                    "flat shaded quads.\n" +
                    "Needs a file at BepInEx/config/carturblood_droplet_256_rgba.png. With no " +
                    "file present this does nothing and says so once in the log.\n" +
                    "Visible at vanilla size in melee - a droplet is 28 screen pixels across at " +
                    "1.5 m on a 1080p screen. It is only across a field that it shrinks to a dot."));

            DropletSize = BindDebug(cfg, Section, "DropletSize", 0.15f,
                new ConfigDescription(
                    "Multiplies droplet size. Vanilla is 0.05 world units, and since a world unit " +
                    "is a metre that is a 50 mm droplet - a golf ball. Real spatter thrown off a " +
                    "blade is 2-4 mm, so the default here is well below vanilla.\n" +
                    "Screen sizes are quoted at 1.5 m on a 1080p screen, which is where you stand " +
                    "when swinging something. Halve them at 3 m, multiply by 1.33 at 1440p.\n" +
                    "  0.08   4 mm,  2 px  faint dot; near the floor of visible\n" +
                    "  0.15   8 mm,  4 px  DEFAULT. a clear dot - the texture does not read here\n" +
                    "  0.25  13 mm,  7 px  the droplet shape starts to read\n" +
                    "  0.40  20 mm, 11 px  texture clearly readable, still well under vanilla\n" +
                    "  1.00  50 mm, 28 px  vanilla\n" +
                    "Below about 0.1 a droplet is under two pixels and stops contributing. Above " +
                    "about 0.5 the count has to come down or the screen fills with red.",
                    new AcceptableValueRange<float>(0.02f, 10f)));

            DropletSpread = BindDebug(cfg, Section, "DropletSpread", 0.6f,
                new ConfigDescription(
                    "Spread of droplet sizes within one burst, as a fraction either side of " +
                    "DropletSize. 0.6 means 0.4x to 1.6x.\n" +
                    "Vanilla authors startSize as a flat 0.05 - every one of the 200 droplets is " +
                    "exactly the same size, which is the most artificial thing about them. Real " +
                    "spatter is a distribution: a few heavy drops among many fine ones.\n" +
                    "The useful ceiling moves with DropletSize, because the bottom of the range " +
                    "has to stay above one screen pixel or those particles are spent on nothing. " +
                    "At the 0.15 default the smallest droplet hits one pixel at a spread of about " +
                    "0.75, so:\n" +
                    "  DropletSize 0.15  ->  spread up to 0.75\n" +
                    "  DropletSize 0.25  ->  spread up to 0.85\n" +
                    "  DropletSize 0.40  ->  the full 0.9 is usable\n" +
                    "Going past it is not harmful, it just buys nothing - the small half of the " +
                    "burst becomes invisible.\n" +
                    "Uniformly distributed, so it is an even mix rather than the long tail real " +
                    "spatter has. An even mix is already a large improvement on none.",
                    new AcceptableValueRange<float>(0f, 0.9f)));

            DropletCount = BindDebug(cfg, Section, "DropletCount", 1f,
                new ConfigDescription(
                    "Multiplies how many droplets are emitted. 1 is vanilla's 200, 5 is 1000.\n" +
                    "Count and size pull against each other, and small-and-many is the pairing " +
                    "that reads as spatter: real blood throws hundreds of small marks, not a " +
                    "handful of large ones. Turning size DOWN is what buys room to turn this UP.\n" +
                    "Ground decals are held steady automatically. The airborne droplets are also " +
                    "the ground-mark emitter, so raising this would otherwise multiply the mess on " +
                    "the floor by the same amount; the per-collision chance is divided to match.",
                    new AcceptableValueRange<float>(0f, 8f)));

            // ---- the 88 cloud particles ----

            ShowCloud = BindDebug(cfg, Section, "ShowCloud", true,
                new ConfigDescription(
                    "The blood_cloud burst - both the systems this mod grafts on and any the " +
                    "effect already had. Turn off to judge the droplets and splash alone."));

            CloudSize = BindDebug(cfg, Section, "CloudSize", 1f,
                new ConfigDescription(
                    "Multiplies cloud particle size. Vanilla is 0.5-1.5 world units.\n" +
                    "Smaller reads as spray, larger reads as mist.",
                    new AcceptableValueRange<float>(0.1f, 3f)));

            // ---- the splash quad ----

            ShowSplash = BindDebug(cfg, Section, "ShowSplash", true,
                new ConfigDescription(
                    "The flat splash quad (slime_splash material, tinted per creature)."));

            SplashSize = BindDebug(cfg, Section, "SplashSize", 1f,
                new ConfigDescription(
                    "Multiplies splash size.",
                    new AcceptableValueRange<float>(0.1f, 4f)));

            // ---- ground marks ----

            ShowGroundDecals = BindDebug(cfg, Section, "ShowGroundDecals", true,
                new ConfigDescription(
                    "The blood left on the floor. Turn off to judge the airborne blood on its own " +
                    "- the ground marks are what dominate a screenshot a second after the kill, " +
                    "and they make it hard to see what the spray itself is doing."));

            GroundDecalSize = BindDebug(cfg, Section, "GroundDecalSize", 1f,
                new ConfigDescription(
                    "Multiplies ground mark size, on top of GroundBlood in section 1. This one is " +
                    "live on the next hit; GroundBlood is applied to shared materials at world " +
                    "load and needs a reload to change.",
                    new AcceptableValueRange<float>(0.1f, 4f)));

            // ---- the death pool ----

            ShowDeathPool = BindDebug(cfg, Section, "ShowDeathPool", true,
                new ConfigDescription(
                    "The pool that spreads under a corpse. Off skips it entirely, leaving the " +
                    "ordinary death decals."));

            DeathPoolSize = BindDebug(cfg, Section, "DeathPoolSize", 1f,
                new ConfigDescription(
                    "Multiplies pool size, on top of PoolSize in section 2.",
                    new AcceptableValueRange<float>(0.1f, 4f)));

            DeathPoolCount = BindDebug(cfg, Section, "DeathPoolCount", 1f,
                new ConfigDescription(
                    "Multiplies how many overlapping pools are laid down, on top of PoolCount in " +
                    "section 2. The result is still clamped to 1-8 by the pool code itself.",
                    new AcceptableValueRange<float>(0.1f, 4f)));

            LogShaderProperties = BindDebug(cfg, Section, "LogShaderProperties", false,
                new ConfigDescription(
                    "Writes every shader property on each blood material to the BepInEx log, once " +
                    "per material, on the next hit. Then switch it back off.\n" +
                    "This is how you find out what a material can actually be told to do - which " +
                    "gloss, smoothness or specular property exists on Custom/ParticleDecal, and " +
                    "what it is currently set to. Guessing a property name costs nothing at " +
                    "compile time and silently does nothing at runtime, which is the expensive " +
                    "way to find out you were wrong."));

            // ---- spray shape ----

            StretchParticles = BindDebug(cfg, Section, "StretchParticles", false,
                new ConfigDescription(
                    "Draws droplets and cloud particles STRETCHED along their own direction of " +
                    "travel instead of as camera-facing squares.\n" +
                    "This is the single biggest difference between grain floating in the air and " +
                    "blood spray: a fast particle becomes a streak, a slow one stays a blob, so " +
                    "the burst shows its own motion. Vanilla draws every particle as the same " +
                    "square whether it is flying or hanging still."));

            StretchAmount = BindDebug(cfg, Section, "StretchAmount", 0.012f,
                new ConfigDescription(
                    "How far a particle stretches, in metres per metre-per-second of its speed. " +
                    "Only used when StretchParticles is on.\n" +
                    "This is not a taste dial - it has a correct order of magnitude. Droplets " +
                    "travel 1-6 m/s and are about 17 mm across at the default size, so a streak " +
                    "two or three times a droplet's own length needs roughly 0.012. The old " +
                    "default of 2 drew TWELVE METRE streaks and turned a kill into a firework.\n" +
                    "  0.004  barely elongated\n" +
                    "  0.012  default; reads as a droplet in flight\n" +
                    "  0.05   long tracer streaks\n" +
                    "Cloud particles are far bigger than droplets, so no single value flatters " +
                    "both. This one is scaled for the droplets.",
                    new AcceptableValueRange<float>(0f, 0.2f)));

            Gravity = BindDebug(cfg, Section, "Gravity", 0.85f,
                new ConfigDescription(
                    "Downward pull on airborne blood. 0 makes particles fly in a straight line " +
                    "until they expire, which is the vanilla behaviour and the most artificial " +
                    "thing about it. The mod already applies 0.6-1.1; this is that value, exposed.",
                    new AcceptableValueRange<float>(0f, 4f)));

            Drag = BindDebug(cfg, Section, "Drag", 0.06f,
                new ConfigDescription(
                    "Air resistance, scaled by particle size AND speed - so fine mist slows and " +
                    "hangs while heavy droplets carry. Higher values stop the burst sooner.",
                    new AcceptableValueRange<float>(0f, 0.5f)));

            BigDropsSlowFaster = BindDebug(cfg, Section, "BigDropsSlowFaster", false,
                new ConfigDescription(
                    "Scales Drag by particle size. Only has a visible effect once DropletSpread " +
                    "is above 0, because until then every droplet is the same size.\n" +
                    "READ THIS BEFORE TURNING IT ON. Unity applies MORE drag to LARGER particles, " +
                    "so this makes heavy drops slow down first and fine mist carry furthest. That " +
                    "is backwards from real air resistance, where a heavy droplet punches through " +
                    "and atomised mist is stopped almost immediately.\n" +
                    "Left off, all droplets get the same drag, which is neither right nor " +
                    "backwards. Unity has no option for the physically correct direction in this " +
                    "module, so off is the honest default. On is available because backwards can " +
                    "still look good - long fine streaks with heavy drops falling short is a " +
                    "recognisable stylised look, it is just not what blood does."));

            Speed = BindDebug(cfg, Section, "Speed", 1f,
                new ConfigDescription(
                    "Multiplies how fast particles leave the wound. Higher throws blood further " +
                    "and, with StretchParticles on, makes longer streaks.",
                    new AcceptableValueRange<float>(0.1f, 5f)));
        }

        /// Effect instances already tuned.
        ///
        /// Keyed on the effect root rather than on the decal, because several effects carry more
        /// than one ParticleDecal and this walks the whole effect - the same reason CloudGraft
        /// dedupes the way it does. Without it, an effect with three decals would scale its own
        /// particle sizes three times over.
        private static readonly HashSet<int> Tuned = new HashSet<int>();

        public static void Reset() => Tuned.Clear();

        /// Applied to each spawned effect once, from BloodSkin.Apply.
        public static void Apply(ParticleDecal decal)
        {
            if (decal == null || DebugMenu == null)
                return;

            Transform root = decal.transform.root;
            if (root == null || !Tuned.Add(root.GetInstanceID()))
                return;

            // Two sets of settings run through this loop and they are gated differently.
            //
            // The PRESET values - droplet size, spread, count, cloud size, stretch - describe how
            // much blood there is, so they always apply. They used to sit behind DebugMenu, which
            // meant the droplets a player actually saw were vanilla's until they opened a menu
            // named "Debug", and BloodLevel could not reach them at all.
            //
            // The DEBUG values - the Show toggles, gravity, drag, speed, the shader dump - exist
            // to isolate one element while judging it. Those stay behind DebugMenu, because
            // switching three of five elements off is a diagnostic state, not a configuration.
            bool debug = DebugMenu.Value;
            BloodPreset preset = BloodPreset.Current();

            foreach (ParticleSystemRenderer r in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                if (r == null)
                    continue;

                ParticleSystem ps = r.GetComponent<ParticleSystem>();
                if (ps == null)
                    continue;

                Kind kind = Classify(r.sharedMaterial);
                if (kind == Kind.Other)
                    continue;

                if (debug && LogShaderProperties.Value)
                    DumpProperties(r.sharedMaterial, kind);

                // Re-applied per hit so the Wetness slider is live. The same call runs at world
                // load from SkinMaterial and the material clones, which is what covers players
                // who never open this menu.
                BloodSkin.ApplyWetness(r.sharedMaterial);

                bool show;
                float size;
                switch (kind)
                {
                    case Kind.Droplet:
                        show = !debug || ShowDroplets.Value;
                        size = preset.DropletSize;
                        break;
                    case Kind.Cloud:
                        show = !debug || ShowCloud.Value;
                        size = preset.CloudSize;
                        break;
                    case Kind.Ground:
                        show = !debug || ShowGroundDecals.Value;
                        // Ground decal size is driven by GroundBlood through GroundPreset, which
                        // has already scaled it by the time this runs. The debug slider is a
                        // multiplier on top for judging one kill, not a second source of truth.
                        size = debug ? GroundDecalSize.Value : 1f;
                        break;
                    default:
                        show = !debug || ShowSplash.Value;
                        size = debug ? SplashSize.Value : 1f;
                        break;
                }

                if (!show)
                {
                    // Stopped and cleared rather than the GameObject disabled: the effect's own
                    // scripts may re-enable the object, and a stopped system that is still
                    // enabled stays off.
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    continue;
                }

                ParticleSystem.MainModule main = ps.main;

                if (Math.Abs(size - 1f) > 0.001f)
                    main.startSize = BloodSkin.Scale(main.startSize, size);

                // After the scale, so the spread is a fraction of the size actually being used
                // rather than of vanilla's.
                if (kind == Kind.Droplet && preset.DropletSpread > 0.001f)
                    Spread(ref main, preset.DropletSpread);

                if (debug && Math.Abs(Speed.Value - 1f) > 0.001f)
                    main.startSpeed = BloodSkin.Scale(main.startSpeed, Speed.Value);

                // Airborne blood only. The splash is a flat camera-facing quad with no meaningful
                // velocity, and the ground marks are lying on the floor - giving either of them
                // gravity, drag or velocity stretching smears something that is not moving.
                if (kind == Kind.Droplet || kind == Kind.Cloud)
                {
                    // Gravity and drag are only exposed as debug sliders, so outside the menu the
                    // vanilla-derived values in CloudGraft.Ballistics stand.
                    if (debug)
                        Ballistics(ps);
                    // With the debug menu open its toggle is authoritative in BOTH directions.
                    // ORing the two meant a preset asking for stretch could not be switched off
                    // from the menu - the one place you would go to turn it off.
                    SetStretch(r, debug ? StretchParticles.Value : preset.Stretch);
                }

                if (kind == Kind.Droplet)
                {
                    if (Math.Abs(preset.DropletCount - 1f) > 0.001f)
                    {
                        // Passing the decal only when THIS system is the one it marks the ground
                        // from - see ScaleEmission for why that matters.
                        ScaleEmission(ps, preset.DropletCount,
                                      ps == decal.m_decalSystem ? decal : null);
                    }

                    if (DropletTexture.Value)
                        BloodSkin.ApplyDropletTexture(r);
                }
            }
        }

        /// Same shape as CloudGraft.Ballistics, but driven by the sliders and applied to whatever
        /// the effect already had rather than only to grafted copies.
        ///
        /// Unconditional here, unlike the graft: the point of a debug slider is to see the value
        /// you set, and silently skipping systems that were authored with their own gravity would
        /// make the slider look broken on exactly the effects worth testing.
        private static void Ballistics(ParticleSystem ps)
        {
            ParticleSystem.MainModule main = ps.main;
            // A range rather than one value, so particles in the same burst separate on the way
            // down instead of falling as a sheet.
            main.gravityModifier =
                new ParticleSystem.MinMaxCurve(Gravity.Value * 0.7f, Gravity.Value * 1.3f);

            ParticleSystem.LimitVelocityOverLifetimeModule lim = ps.limitVelocityOverLifetime;
            if (Drag.Value <= 0f)
            {
                lim.enabled = false;
                return;
            }

            lim.enabled = true;
            lim.dampen = 0f;
            lim.drag = new ParticleSystem.MinMaxCurve(Drag.Value);
            // See the BigDropsSlowFaster description: Unity scales drag UP with particle size,
            // which is the opposite of how air resistance treats a heavy droplet. Off by default
            // for that reason, rather than because uniform drag is correct.
            lim.multiplyDragByParticleSize = BigDropsSlowFaster.Value;
            lim.multiplyDragByParticleVelocity = true;
        }

        /// Turns a single authored size into a range, so one burst carries fine mist and heavy
        /// drops instead of 200 identical particles.
        ///
        /// Reads the authored value through BloodSkin.AuthoredSize, which handles every curve
        /// mode - a system authored as a range keeps its midpoint and widens around it rather
        /// than having the range thrown away, which is the mistake this project has already made
        /// once by reading .constant off a TwoConstants curve.
        private static void Spread(ref ParticleSystem.MainModule main, float spread)
        {
            float mid = BloodSkin.AuthoredSize(main.startSize);
            if (mid <= 0f)
                return;

            main.startSize = new ParticleSystem.MinMaxCurve(
                mid * Mathf.Max(0.01f, 1f - spread),
                mid * (1f + spread));
        }

        /// Billboard particles are camera-facing squares - the same shape whether the particle is
        /// hanging still or flying at speed. Stretch mode scales each particle along its own
        /// velocity vector instead, which is what makes liquid in flight read as spray.
        private static void SetStretch(ParticleSystemRenderer r, bool stretch)
        {
            if (stretch)
            {
                r.renderMode = ParticleSystemRenderMode.Stretch;
                r.velocityScale = StretchAmount.Value;
                // Left at 1: lengthScale multiplies the particle's own size on top of the
                // velocity term, and turning both up stretches stationary particles too, which is
                // the opposite of what this is for.
                r.lengthScale = 1f;
            }
            else
            {
                r.renderMode = ParticleSystemRenderMode.Billboard;
            }
        }

        /// Through BloodSkin.Scale so the curve mode survives - reading .constant off a
        /// TwoConstants burst silently discards the authored range.
        ///
        /// Two things have to move with the burst count or turning it up does nothing useful.
        ///
        /// maxParticles is a hard cap, not a hint: a system authored for 200 that is asked to
        /// emit 1000 emits 200 and reports no error. Raising the burst without raising this is
        /// the quiet way to spend an evening wondering why the slider stopped working.
        ///
        /// ParticleDecal.m_chance is the other one, and it is the bug this mod has already had
        /// once. The airborne particles ARE the ground-mark emitter - ParticleDecal emits one
        /// decal per collision of its own system, and `blood_drop` is in the DecalMaterials list -
        /// so five times the droplets is five times the ground decals unless the chance comes
        /// down to match. Dividing keeps the ground looking the same while the air fills up,
        /// which is what someone reaching for this slider is actually asking for.
        ///
        /// Only applied when the system being scaled is the decal's OWN emitter. Effects where
        /// the droplets and the decal system are separate objects need no compensation, and
        /// applying it there would thin the ground marks for no reason.
        private static void ScaleEmission(ParticleSystem ps, float mul, ParticleDecal groundSource)
        {
            ParticleSystem.EmissionModule em = ps.emission;

            for (int i = 0; i < em.burstCount; i++)
            {
                ParticleSystem.Burst b = em.GetBurst(i);
                b.count = BloodSkin.Scale(b.count, mul);
                em.SetBurst(i, b);
            }

            em.rateOverTime = BloodSkin.Scale(em.rateOverTime, mul);

            ParticleSystem.MainModule main = ps.main;
            if (mul > 1f)
            {
                int want = Mathf.CeilToInt(main.maxParticles * mul);
                if (main.maxParticles < want)
                    main.maxParticles = want;
            }

            if (groundSource != null && mul > 0.01f)
                groundSource.m_chance = Mathf.Clamp(groundSource.m_chance / mul, 0f, 100f);
        }

        /// Materials already reported, so one hit does not write the same block forty times.
        private static readonly HashSet<int> Dumped = new HashSet<int>();

        /// Lists every property the material's shader declares, with its current value.
        ///
        /// Reflection rather than a direct call, for the same reason Texture2D.LoadImage is
        /// reflected in BloodSkin: Shader.GetPropertyCount and friends arrived in Unity 2019.3
        /// and referencing them directly ties the build to a Unity version this project does not
        /// control. If they are missing the dump says so and nothing else breaks.
        private static void DumpProperties(Material mat, Kind kind)
        {
            if (mat == null || mat.shader == null || !Dumped.Add(mat.GetInstanceID()))
                return;

            try
            {
                Type st = typeof(Shader);
                MethodInfo count = st.GetMethod("GetPropertyCount", BindingFlags.Public | BindingFlags.Static);
                MethodInfo name = st.GetMethod("GetPropertyName", BindingFlags.Public | BindingFlags.Static);
                MethodInfo type = st.GetMethod("GetPropertyType", BindingFlags.Public | BindingFlags.Static);

                if (count == null || name == null || type == null)
                {
                    Plugin.Log.LogWarning(
                        "Shader.GetPropertyCount is not available in this Unity version, so the " +
                        "property list cannot be read. Nothing else is affected.");
                    return;
                }

                var sb = new StringBuilder();
                sb.AppendLine($"--- {kind} material \"{mat.name}\"  shader \"{mat.shader.name}\" ---");

                int n = (int)count.Invoke(null, new object[] { mat.shader });
                for (int i = 0; i < n; i++)
                {
                    string pn = (string)name.Invoke(null, new object[] { mat.shader, i });
                    string pt = type.Invoke(null, new object[] { mat.shader, i }).ToString();

                    string value;
                    switch (pt)
                    {
                        case "Color":   value = mat.GetColor(pn).ToString(); break;
                        case "Vector":  value = mat.GetVector(pn).ToString(); break;
                        case "Float":
                        case "Range":   value = mat.GetFloat(pn).ToString("0.###"); break;
                        case "Texture":
                            Texture t = mat.GetTexture(pn);
                            value = t == null ? "(none)" : $"{t.name} {t.width}x{t.height}";
                            break;
                        default:        value = "?"; break;
                    }
                    sb.AppendLine($"    {pn,-24} {pt,-8} = {value}");
                }

                sb.AppendLine($"    keywords: {string.Join(" ", mat.shaderKeywords)}");
                Plugin.Log.LogInfo(sb.ToString());
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not list shader properties: " + e.Message);
            }
        }

        private enum Kind { Other, Droplet, Cloud, Splash, Ground }

        /// Prefix match, not equality: Unity appends " (Instance)" to a material the moment
        /// anything touches it, and this mod instances several of them by design.
        ///
        /// The mod's own clone names are matched alongside the vanilla ones because this runs
        /// LAST in BloodSkin.Apply, after the ground and splash materials have already been
        /// swapped for clones. Matching only the vanilla names was a real bug: ShowSplash looked
        /// for "slime" on a renderer that by then said "CarturBloodSplash", so the toggle did
        /// nothing on exactly the effects the mod had touched.
        private static Kind Classify(Material mat)
        {
            if (mat == null || mat.name == null)
                return Kind.Other;

            string n = mat.name;

            if (Is(n, "blood_drop") || Is(n, "CarturBloodDroplet")) return Kind.Droplet;
            if (Is(n, "blood_cloud")) return Kind.Cloud;
            if (Is(n, "slime") || Is(n, "CarturBloodSplash")) return Kind.Splash;
            if (Is(n, "blood_splat") || Is(n, "splat_decal_blend") || Is(n, "CarturBloodGround"))
                return Kind.Ground;

            return Kind.Other;
        }

        private static bool Is(string name, string prefix) =>
            name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);

        // ---- read by BloodSkin and Pooling, so the pool obeys the menu too ----

        /// True unless the menu is on AND the pool has been switched off.
        internal static bool DeathPoolEnabled() =>
            DebugMenu == null || !DebugMenu.Value || ShowDeathPool.Value;

        internal static float PoolSizeMultiplier() =>
            DebugMenu != null && DebugMenu.Value ? DeathPoolSize.Value : 1f;

        internal static float PoolCountMultiplier() =>
            DebugMenu != null && DebugMenu.Value ? DeathPoolCount.Value : 1f;
    }
}
