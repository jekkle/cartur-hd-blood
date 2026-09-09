using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace CarturHDBlood
{
    /// Re-skins and re-tunes the blood effects.
    ///
    /// Applied per ParticleDecal instance from its Awake, deliberately, rather than by editing
    /// the vfx_BloodHit / vfx_BloodDeath prefabs. The live probe showed why: creatures don't use
    /// those prefabs at all. Blood comes from per-creature effects - vfx_greydwarf_death,
    /// vfx_troll_death, vfx_player_hit - each carrying its own nested ParticleDecal with its own
    /// settings (troll death is size 4..6 at 10% chance; greydwarf death is 3..3 at 100%).
    /// Patching every instance as it spawns catches all of them without needing to know the
    /// full list, and picks up modded creatures for free.
    ///
    /// The dials are multipliers, not absolute values, precisely because those per-creature
    /// numbers differ on purpose - a troll should mark more ground than a greyling. Absolute
    /// overrides would flatten that design into one size for everything.
    ///
    /// Texture assignment follows vanilla's own scheme: one fixed image per material, sized to
    /// the world size that material actually draws at. There is no atlas and no Texture Sheet
    /// Animation. An earlier version sliced a 4096 sheet into twelve materials to get shape
    /// variety; it cost about 170 MB of uncompressed RGBA at runtime, needed a grid to be kept
    /// in sync with the art, and Custom/ParticleDecal ignores sheet animation anyway.
    internal static class BloodSkin
    {
        // The decal material is shared by every blood effect in the game (verified: one instance
        // id across player, greydwarf and troll effects), so the texture swap needs to happen
        // once per material, not once per spawned decal.
        private static readonly HashSet<int> SkinnedMaterials = new HashSet<int>();

        // Ground marks. Two large, chosen per decal, and one small.
        private static Texture2D _groundLarge;
        private static Texture2D _groundLargeAlt;
        private static Texture2D _groundSmall;
        // One normal map per mark. Custom/ParticleDecal has a live _NormalTex - vanilla keeps
        // brains_n 64x64 there - so these decals are lit. Leaving vanilla's normal under our
        // albedo meant highlights followed the shape of a texture that is no longer drawn,
        // which is what made the blood read flat instead of wet.
        private static Texture2D _groundLargeNormal;
        private static Texture2D _groundLargeAltNormal;
        private static Texture2D _groundSmallNormal;
        private static bool _loadAttempted;

        /// Blood materials that are always covered, whatever the config says.
        ///
        /// The config file persists across updates and silently wins over new code defaults, so
        /// the materials the game actually uses for blood are listed here in code and always
        /// included; the config setting adds to this rather than replacing it. That way finding
        /// a missed material fixes every install rather than only fresh ones.
        private static readonly string[] KnownDecalMaterials =
            { "splat_decal_blend", "seeker_blood_splat", "SeekerQueen_decals" };

        // Parsed once from config, then reused - this is checked on every decal spawn.
        private static string[] _materialPrefixes;

        /// Skins the decal materials up front, at world load.
        ///
        /// Without this the first decal of a session renders with the old texture: the material
        /// was only re-skinned from a decal's Awake, and the decal that triggers it has already
        /// been handed the material by then. Walking the prefabs here gets every decal material
        /// in the game skinned before anything can spawn.
        ///
        /// Deliberately at ZNetScene.Awake and not earlier - it must land after a texture pack's
        /// own load-time replacement, or the pack would overwrite us instead.
        public static void PreSkin(ZNetScene scene)
        {
            if (scene == null || !Plugin.ModEnabled.Value)
                return;

            // Each part checks its own switch below.
            //
            // This used to bail on ReplaceDecalTexture, which is the GROUND flag - so turning
            // the ground texture off also silently disabled the green-splash fix, which has its
            // own setting. Unrelated features hidden behind one flag.
            if (!Plugin.ReplaceTexture.Value && !Plugin.FixGreenSplash.Value)
                return;

            int decals = 0;
            int splashes = 0;

            // Decals and the green splash in one pass over the prefabs. GetComponentsInChildren
            // isn't cheap across 3500 prefabs, so it's worth not doing it twice.
            foreach (GameObject prefab in scene.m_prefabs)
            {
                if (prefab == null)
                    continue;

                // Counted whether or not the material still needs skinning, because this is also
                // the test for "is this prefab a blood effect", used further down.
                int decalsHere = 0;

                foreach (ParticleDecal decal in prefab.GetComponentsInChildren<ParticleDecal>(true))
                {
                    if (decal == null || decal.m_decalSystem == null)
                        continue;
                    Material mat = MaterialOf(decal.m_decalSystem);
                    if (mat == null || !IsBloodMaterial(mat))
                        continue;

                    decalsHere++;

                    // Counted for the blood-effect test above regardless, but only skinned when
                    // the ground flag is actually on.
                    if (!Plugin.ReplaceTexture.Value)
                        continue;

                    try
                    {
                        SkinMaterial(mat);
                        decals++;
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogWarning("Pre-skin failed on " + mat.name + ": " + e.Message);
                    }
                }

                if (!Plugin.FixGreenSplash.Value)
                    continue;

                // Whether this prefab is a blood effect at all, decided structurally: it carries
                // a ParticleDecal that renders with a blood material. That test is what makes the
                // green-splash fix below safe - no name matching, no guessing.
                bool isBloodEffect = decalsHere > 0;

                foreach (ParticleSystemRenderer r in prefab.GetComponentsInChildren<ParticleSystemRenderer>(true))
                {
                    if (r == null)
                        continue;
                    Material mat = r.sharedMaterial;
                    if (mat == null)
                        continue;

                    if (isBloodEffect && IsSlimeSplashMaterial(mat))
                    {
                        try
                        {
                            if (ReplaceSlimeSplash(r))
                                splashes++;
                        }
                        catch (Exception e)
                        {
                            Plugin.Log.LogWarning("Splash fix failed on " + mat.name + ": " + e.Message);
                        }
                    }
                }
            }

            Plugin.Log.LogInfo($"Pre-skinned {decals} decal material(s) and re-coloured " +
                               $"{splashes} green splash renderer(s) at world load.");
        }

        public static void Apply(ParticleDecal decal)
        {
            if (decal == null || !Plugin.ModEnabled.Value)
                return;

            ParticleSystem decalSystem = decal.m_decalSystem;
            if (decalSystem == null)
                return;

            Material mat = MaterialOf(decalSystem);
            if (mat == null || !IsBloodMaterial(mat))
                return;

            // Not everything using the blood material is blood. Frost breath, acid spit and egg
            // goo all borrow it, and a material-based whitelist cannot tell them apart - so these
            // are excluded by effect name and handed back the original texture.
            if (IsExcludedEffect(decal))
            {
                RestoreVanilla(decalSystem, mat);
                return;
            }

            try
            {
                if (Plugin.ReplaceTexture.Value)
                    SkinMaterial(mat);

                TuneDecalSystem(decalSystem, mat);
                TuneChance(decal);

                // Both of these key off the spawned effect root and dedupe themselves, so an
                // effect carrying two or three ParticleDecals still gets one of each.
                CloudGraft.Apply(decal);

                if (IsDeathEffect(decal))
                    Pooling.SpawnPool(decal);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Failed applying blood skin: " + e.Message);
            }
        }

        /// Matching on the decal's material, not on the effect's name, is what keeps this off
        /// non-blood decals - the game reuses ParticleDecal for other things. The "puke"
        /// material is the reason this list is a whitelist rather than "any ParticleDecal":
        /// it has 9 owners and would otherwise get blood-splattered.
        ///
        /// Whitelisting by material also means the count of creatures is irrelevant. One entry
        /// covers 88 per-creature effects; the seeker entries cover another 17.
        internal static bool IsBloodMaterial(Material mat)
        {
            if (mat.name == null)
                return false;

            if (_materialPrefixes == null)
                _materialPrefixes = ParseList(Plugin.DecalMaterials.Value);

            return MatchesAny(mat.name, _materialPrefixes) || MatchesAny(mat.name, KnownDecalMaterials);
        }

        /// The green splash hiding inside every blood effect.
        ///
        /// Each of these effects has a "wetsplsh" child whose material is "slime_green" -
        /// slime_splash texture, tinted RGBA(0.557, 0.702, 0.384). Green. It plays on every
        /// blood hit in the game.
        ///
        /// It can't simply be re-skinned like the other materials, because Blobs almost
        /// certainly share it and that would recolour every slime in the game. The sharing
        /// report couldn't rule that out, since it only saw material owners inside blood-named
        /// prefabs.
        ///
        /// So instead of touching the shared material, this clones it once and assigns the clone
        /// to only these renderers. Slimes keep the original object untouched, and no proof about
        /// who else uses it is needed - which is the point.
        private static bool IsSlimeSplashMaterial(Material mat) =>
            mat.name != null && mat.name.StartsWith("slime", StringComparison.OrdinalIgnoreCase);

        private static Material _bloodSplashMaterial;

        // The texture the decal material had before we touched it, kept so effects that borrow
        // the blood material for something that is not blood can be handed it back.
        private static Texture _originalMainTex;
        private static Material _vanillaDecalMaterial;
        private static string[] _excludePrefixes;

        private static bool ReplaceSlimeSplash(ParticleSystemRenderer r)
        {
            Material original = r.sharedMaterial;
            if (original == null || original == _bloodSplashMaterial)
                return false;

            if (_bloodSplashMaterial == null)
            {
                _bloodSplashMaterial = new Material(original) { name = "CarturBloodSplash" };

                // Neutral tint, so the per-creature startColor decides the colour here too
                // rather than the green being baked into the material.
                if (_bloodSplashMaterial.HasProperty("_Color"))
                    _bloodSplashMaterial.SetColor("_Color", Color.white);
                if (_bloodSplashMaterial.HasProperty("_EmissionColor"))
                    _bloodSplashMaterial.SetColor("_EmissionColor", Color.black);

                // The texture is left as vanilla's slime_splash. Only the green comes off.
                // This used to be handed the mod's own spray art, but the mod no longer ships
                // any - the airborne blood is vanilla again - and a splash carrying the wrong
                // silhouette was never the complaint. The tint was.
                Plugin.Log.LogInfo($"Cloned \"{original.name}\" -> \"CarturBloodSplash\" " +
                                   "(green splash inside blood effects; the original is left alone " +
                                   "so slimes are unaffected).");
            }

            r.sharedMaterial = _bloodSplashMaterial;
            return true;
        }

        private static void SkinMaterial(Material mat)
        {
            LoadTextures();

            // The shared material carries the first of the two large marks. It is the default
            // every decal gets; the small mark and the second large one reach individual decals
            // through AssignGroundVariant, which a shared material cannot do on its own.
            Texture2D albedo = _groundLarge ?? _groundLargeAlt ?? _groundSmall;
            if (albedo == null)
                return;

            Texture before = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;

            // Verify against the material's ACTUAL texture, not against a record of having
            // skinned it before.
            //
            // A texture pack applies its overrides after world load, so it overwrites whatever
            // was set during the pre-skin pass. Tracking "already skinned" by id meant this
            // returned early and left the pack's texture in place permanently - the pre-skin
            // fix for the first-decal case silently disabled the re-apply that made the mod
            // work at all. Comparing textures is self-correcting: whoever wrote last, we put
            // ours back on the next decal.
            bool alreadyOurs = before == albedo;
            if (alreadyOurs && SkinnedMaterials.Contains(mat.GetInstanceID()))
                return;

            SkinnedMaterials.Add(mat.GetInstanceID());

            // Captured once, before the first swap. Valheim reuses the blood decal material for
            // things that are not blood - Moder's frost breath and the Seeker Queen's spit both
            // render through it - so those effects need the original texture put back.
            if (_originalMainTex == null && before != null)
                _originalMainTex = before;

            mat.SetTexture("_MainTex", albedo);
            SetNormal(mat, _groundLargeNormal);

            Plugin.Log.LogInfo($"Skinned \"{mat.name}\" id={mat.GetInstanceID()}: " +
                               $"_MainTex \"{(before == null ? "none" : before.name)}\" -> \"{albedo.name}\" " +
                               $"{albedo.width}x{albedo.height}");

            Texture after = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
            if (after != albedo)
                Plugin.Log.LogWarning($"\"{mat.name}\" still holds \"{(after == null ? "none" : after.name)}\" " +
                                      "immediately after skinning - something else is writing to it.");
        }

        // One cloned material per source material per variant. Keyed by the source's instance id
        // because there are three decal materials in the game, not one - cloning splat_decal_blend
        // and handing the clone to a Seeker Queen decal would change more than its texture.
        private static readonly Dictionary<int, Material> SmallGroundMaterials = new Dictionary<int, Material>();
        private static readonly Dictionary<int, Material> AltGroundMaterials = new Dictionary<int, Material>();

        /// Gives this decal system one of the three ground marks.
        ///
        /// The shared material already holds the first large mark, so only the other two need a
        /// clone, built once each and reused. Cloning is also what keeps a texture pack from
        /// reaching them: the pack overwrites the shared material at about 25 seconds after
        /// world load, and these are separate objects it has never heard of.
        ///
        /// Chosen per ParticleDecal INSTANCE, not per particle, so one effect's burst of three
        /// decals all draw the same mark. Per-particle would need Texture Sheet Animation, and
        /// Custom/ParticleDecal ignores that - verified, and the reason the old atlas existed.
        private static void AssignGroundVariant(ParticleSystem ps, Material shared, float authoredSize)
        {
            ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();
            if (r == null)
                return;

            if (authoredSize < Plugin.SmallDecalSize.Value)
            {
                Material small = GroundClone(SmallGroundMaterials, shared, _groundSmall,
                                             _groundSmallNormal, "CarturBloodGroundSmall");
                if (small != null)
                    r.sharedMaterial = small;
                return;
            }

            // Coin flip between the two large marks. The shared material is already the first.
            if (UnityEngine.Random.value < 0.5f)
                return;

            Material alt = GroundClone(AltGroundMaterials, shared, _groundLargeAlt,
                                       _groundLargeAltNormal, "CarturBloodGroundAlt");
            if (alt != null)
                r.sharedMaterial = alt;
        }

        private static Material GroundClone(Dictionary<int, Material> cache, Material shared,
                                            Texture2D tex, Texture2D normal, string name)
        {
            if (shared == null || tex == null)
                return null;

            int id = shared.GetInstanceID();
            if (cache.TryGetValue(id, out Material cached) && cached != null)
                return cached;

            try
            {
                var clone = new Material(shared) { name = name };
                if (!clone.HasProperty("_MainTex"))
                    return null;
                clone.SetTexture("_MainTex", tex);
                // Its own normal, not the shared material's: the small mark lit by the large
                // mark's bumps is exactly the mismatch this whole change is fixing.
                SetNormal(clone, normal);
                cache[id] = clone;
                Plugin.Log.LogInfo($"Built \"{name}\" from \"{shared.name}\" with \"{tex.name}\" " +
                                   $"{tex.width}x{tex.height}.");
                return clone;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not build " + name + ": " + e.Message);
                return null;
            }
        }

        /// Assigns a normal map if the shader has the slot and we have the texture.
        ///
        /// Silent when either is missing, deliberately: the property is not documented anywhere,
        /// and a modded or updated shader without it should cost the shine, not the blood.
        private static void SetNormal(Material mat, Texture2D normal)
        {
            if (normal == null || mat == null || !mat.HasProperty("_NormalTex"))
                return;
            mat.SetTexture("_NormalTex", normal);
        }

        private static void TuneDecalSystem(ParticleSystem ps, Material shared)
        {
            GroundPreset preset = GroundPreset.Current();
            ParticleSystem.MainModule main = ps.main;

            // Captured before scaling: which mark a decal gets is chosen from its authored size,
            // and a size multiplier shouldn't change the artwork.
            float authoredSize = AuthoredSize(main.startSize);

            main.startSize = Realism.JitterSize(main.startSize);

            if (Math.Abs(preset.SizeMultiplier - 1f) > 0.001f)
                main.startSize = Scale(main.startSize, preset.SizeMultiplier);

            if (Math.Abs(preset.LifetimeMultiplier - 1f) > 0.001f)
                main.startLifetime = Scale(main.startLifetime, preset.LifetimeMultiplier);

            // maxParticles caps how many decals can coexist. Raising chance without raising this
            // just means new decals evict old ones, so the ground never actually accumulates.
            if (preset.MaxDecals > 0 && main.maxParticles < preset.MaxDecals)
                main.maxParticles = preset.MaxDecals;

            FixWashedOutColor(ref main);
            DeepenRed(ref main);
            Realism.AgeDecal(ps);
            Realism.GrowDecal(ps);

            if (Plugin.ReplaceTexture.Value)
                AssignGroundVariant(ps, shared, authoredSize);
        }

        internal static float AuthoredSize(ParticleSystem.MinMaxCurve c)
        {
            switch (c.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return c.constant;
                case ParticleSystemCurveMode.TwoConstants:
                    return (c.constantMin + c.constantMax) * 0.5f;
                default:
                    return c.curveMultiplier;
            }
        }

        /// Repaints only the decals whose authored colour is too washed out to read as blood.
        ///
        /// Replacing the texture with a white one made the effect's own startColor the only
        /// source of colour. Most creatures author a saturated red and now look better than
        /// vanilla, but some - the player's hit effect among them - author something near-grey
        /// and relied on vanilla's reddish 'brains' texture to supply the red. Those came out
        /// grey.
        ///
        /// The threshold is why this isn't a blanket recolour: forcing one colour everywhere
        /// would throw away the per-creature variety that the white texture just unlocked. Only
        /// the ones that can't carry a tint get overwritten.
        private static void FixWashedOutColor(ref ParticleSystem.MainModule main)
        {
            float threshold = Plugin.ColorFallbackSaturation.Value;
            if (threshold <= 0f)
                return;

            float sat = LiveProbe.Saturation(main.startColor);
            if (sat < 0f || sat >= threshold)
                return;   // negative means a gradient we shouldn't second-guess

            Color blood = ParseColor(Plugin.BloodColor.Value);

            // Brightness and alpha are kept from the original, so a dark effect stays dark and
            // a faint one stays faint - only the hue and saturation are supplied.
            ParticleSystem.MinMaxGradient g = main.startColor;
            switch (g.mode)
            {
                case ParticleSystemGradientMode.Color:
                    main.startColor = new ParticleSystem.MinMaxGradient(Recolor(g.color, blood));
                    break;
                case ParticleSystemGradientMode.TwoColors:
                    main.startColor = new ParticleSystem.MinMaxGradient(
                        Recolor(g.colorMin, blood), Recolor(g.colorMax, blood));
                    break;
            }
        }

        /// Pulls red blood toward a deeper, browner red.
        ///
        /// Vanilla authors its red blood very bright and very saturated - a boar is
        /// RGBA(0.868, 0.006, 0.006), which is nearly primary red. Real blood is darker and
        /// browner, and BloodColor already holds a better value; it was just never applied to
        /// anything except the washed-out effects.
        ///
        /// ONLY colours that are already red-dominant are touched. Greydwarf blood is yellow and
        /// neck blood is green - measured, and the game's own design - so the test requires green
        /// and blue to both be well under red before anything happens. Greydwarf's orange
        /// RGBA(0.838, 0.468, 0.000) fails it and is left alone, which is the point.
        private static void DeepenRed(ref ParticleSystem.MainModule main)
        {
            float tint = Mathf.Clamp01(Plugin.BloodTint.Value);
            if (tint <= 0.001f)
                return;

            Color blood = ParseColor(Plugin.BloodColor.Value);
            ParticleSystem.MinMaxGradient g = main.startColor;

            switch (g.mode)
            {
                case ParticleSystemGradientMode.Color:
                    if (IsRedBlood(g.color))
                        main.startColor = new ParticleSystem.MinMaxGradient(Deepen(g.color, blood, tint));
                    break;
                case ParticleSystemGradientMode.TwoColors:
                    if (IsRedBlood(g.colorMin) && IsRedBlood(g.colorMax))
                    {
                        main.startColor = new ParticleSystem.MinMaxGradient(
                            Deepen(g.colorMin, blood, tint), Deepen(g.colorMax, blood, tint));
                    }
                    break;
            }
        }

        /// Red-dominant: green and blue both well under red. Deliberately strict, so anything
        /// the game authored as a non-red blood keeps its own colour.
        private static bool IsRedBlood(Color c) =>
            c.r > 0.15f && c.g < c.r * 0.4f && c.b < c.r * 0.4f;

        private static Color Deepen(Color original, Color blood, float tint)
        {
            Color mixed = Color.Lerp(original, blood, tint);
            mixed.a = original.a;   // transparency is the effect's business, not ours
            return mixed;
        }

        private static Color Recolor(Color original, Color blood)
        {
            float brightness = Mathf.Max(original.r, Mathf.Max(original.g, original.b));
            if (brightness <= 0f)
                brightness = 1f;
            return new Color(blood.r * brightness, blood.g * brightness, blood.b * brightness, original.a);
        }

        private static Color ParseColor(string hex)
        {
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex.Trim(), out Color c))
                return c;
            return new Color(0.55f, 0.02f, 0.02f, 1f);   // fallback if the config value is malformed
        }

        private static void TuneChance(ParticleDecal decal)
        {
            GroundPreset preset = GroundPreset.Current();

            float chance = decal.m_chance * preset.ChanceMultiplier;
            if (chance < preset.MinChance)
                chance = preset.MinChance;

            // No spray multiplier to divide back out any more.
            //
            // While the mod tuned airborne density this had to compensate: the particles thrown
            // into the air ARE the ones that mark the ground - ParticleDecal.OnParticleCollision
            // emits one decal per collision of its own emitter - so multiplying the spray by six
            // multiplied ground decals by roughly six whatever the chance said. The airborne
            // systems are vanilla now, so the chance is the whole story again.
            decal.m_chance = Mathf.Clamp(chance, 0f, 100f);
        }

        private static bool IsDeathEffect(ParticleDecal decal)
        {
            try
            {
                Transform root = decal.transform.root;
                string name = root == null ? string.Empty : root.name;
                return name.IndexOf("death", StringComparison.OrdinalIgnoreCase) >= 0;
            }
            catch { return false; }
        }

        private static bool IsExcludedEffect(ParticleDecal decal)
        {
            if (_excludePrefixes == null)
                _excludePrefixes = ParseList(Plugin.ExcludeEffects.Value);

            if (_excludePrefixes.Length == 0)
                return false;

            try
            {
                Transform root = decal.transform.root;
                string name = root == null ? string.Empty : root.name;
                foreach (string frag in _excludePrefixes)
                {
                    if (name.IndexOf(frag, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            catch { }
            return false;
        }

        /// Gives one decal renderer a material carrying the pre-swap texture, so an excluded
        /// effect looks exactly as it did in vanilla. Assigned per renderer, so the shared
        /// material - and therefore real blood - is unaffected.
        private static void RestoreVanilla(ParticleSystem decalSystem, Material template)
        {
            if (_originalMainTex == null)
                return;

            try
            {
                if (_vanillaDecalMaterial == null)
                {
                    _vanillaDecalMaterial = new Material(template) { name = "CarturVanillaDecal" };
                    _vanillaDecalMaterial.SetTexture("_MainTex", _originalMainTex);
                    Plugin.Log.LogInfo(
                        $"Built \"CarturVanillaDecal\" from \"{_originalMainTex.name}\" for effects that " +
                        "borrow the blood material for non-blood (frost breath, acid spit, egg goo).");
                }

                var r = decalSystem.GetComponent<ParticleSystemRenderer>();
                if (r != null)
                    r.sharedMaterial = _vanillaDecalMaterial;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not restore vanilla decal: " + e.Message);
            }
        }

        /// Scales a MinMaxCurve while preserving whichever mode it was authored in - the decal
        /// systems use both Constant and TwoConstants, and writing the wrong mode back would
        /// silently discard the authored range.
        internal static ParticleSystem.MinMaxCurve Scale(ParticleSystem.MinMaxCurve c, float mul)
        {
            switch (c.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return new ParticleSystem.MinMaxCurve(c.constant * mul);
                case ParticleSystemCurveMode.TwoConstants:
                    return new ParticleSystem.MinMaxCurve(c.constantMin * mul, c.constantMax * mul);
                default:
                    c.curveMultiplier *= mul;
                    return c;
            }
        }

        private static Material MaterialOf(ParticleSystem ps)
        {
            var r = ps.GetComponent<ParticleSystemRenderer>();
            return r == null ? null : r.sharedMaterial;
        }

        private static string[] ParseList(string raw)
        {
            var parsed = new List<string>();
            foreach (string part in (raw ?? string.Empty).Split(','))
            {
                string trimmed = part.Trim();
                if (trimmed.Length > 0)
                    parsed.Add(trimmed);
            }
            return parsed.ToArray();
        }

        // Prefix, not equality: Unity appends " (Instance)" when a material is instanced.
        private static bool MatchesAny(string name, string[] prefixes)
        {
            foreach (string prefix in prefixes)
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// One texture per material, each sized to what that material actually draws at, which is
        /// how vanilla assigns particle textures in the first place - it never samples a sheet.
        /// Because these are single images rather than atlas cells, mipmaps are safe and wanted:
        /// there are no cell boundaries for a mip level to average across.
        ///
        /// The sizes come from the effect graph, not from taste:
        ///   ground decal   0.5-8 world units, largest on troll and bjorn deaths  -> 1024 / 512
        private static void LoadTextures()
        {
            if (_loadAttempted)
                return;
            _loadAttempted = true;

            _groundLarge = Load("splatter_spray_1024_rgba.png", "CarturBloodGroundLarge", mipmap: true);
            _groundLargeAlt = Load("splatter_mist_1024_rgba.png", "CarturBloodGroundLargeAlt", mipmap: true);
            _groundSmall = Load("splatter_impact_512_rgba.png", "CarturBloodGroundSmall", mipmap: true);

            if (Plugin.GroundNormalMap.Value)
            {
                _groundLargeNormal = Load("splatter_spray_1024_normal.png", "CarturBloodGroundLargeN", mipmap: true);
                _groundLargeAltNormal = Load("splatter_mist_1024_normal.png", "CarturBloodGroundLargeAltN", mipmap: true);
                _groundSmallNormal = Load("splatter_impact_512_normal.png", "CarturBloodGroundSmallN", mipmap: true);
            }
        }

        /// A file in BepInEx/config wins over the embedded asset, so the art can be swapped
        /// without a rebuild.
        private static Texture2D Load(string fileName, string texName, bool mipmap)
        {
            byte[] data = null;

            try
            {
                string overridePath = Path.Combine(Plugin.ConfigDir ?? ".", "carturblood_" + fileName);
                if (File.Exists(overridePath))
                {
                    data = File.ReadAllBytes(overridePath);
                    Plugin.Log.LogInfo("Using override texture " + overridePath);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not read override texture: " + e.Message);
            }

            if (data == null)
            {
                try
                {
                    Assembly asm = Assembly.GetExecutingAssembly();
                    using (Stream s = asm.GetManifestResourceStream("CarturHDBlood." + fileName))
                    {
                        if (s == null)
                        {
                            Plugin.Log.LogError("Embedded texture missing: " + fileName);
                            return null;
                        }
                        data = new byte[s.Length];
                        s.Read(data, 0, data.Length);
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError("Could not read embedded texture " + fileName + ": " + e.Message);
                    return null;
                }
            }

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipmap)
            {
                name = texName,
                // Clamp, so a particle quad cannot sample the opposite edge of the image and
                // draw a seam.
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };

            if (!LoadImageViaReflection(tex, data))
            {
                UnityEngine.Object.Destroy(tex);
                return null;
            }

            return tex;
        }

        /// Texture2D.LoadImage is called through reflection on purpose. Referencing it directly
        /// drags in the ReadOnlySpan<byte> overload set, which fails to compile as CS0518 under
        /// net472 against this game's netstandard.dll. Reflection resolves the byte[] overload
        /// at runtime and sidesteps the whole problem.
        private static bool LoadImageViaReflection(Texture2D tex, byte[] data)
        {
            try
            {
                Type conv = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")
                            ?? Type.GetType("UnityEngine.ImageConversion, UnityEngine");

                if (conv != null)
                {
                    MethodInfo mi = conv.GetMethod("LoadImage",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new[] { typeof(Texture2D), typeof(byte[]), typeof(bool) },
                        null);
                    if (mi != null)
                        return (bool)mi.Invoke(null, new object[] { tex, data, false });
                }

                MethodInfo legacy = typeof(Texture2D).GetMethod("LoadImage",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(byte[]) },
                    null);
                if (legacy != null)
                    return (bool)legacy.Invoke(tex, new object[] { data });

                Plugin.Log.LogError("No usable LoadImage overload found.");
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("LoadImage failed: " + e.Message);
                return false;
            }
        }
    }
}
