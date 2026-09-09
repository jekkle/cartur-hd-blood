using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace CarturHDBlood
{
    /// Re-skins and re-tunes the ground blood decals.
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
    internal static class BloodSkin
    {
        // The decal material is shared by every blood effect in the game (verified: one instance
        // id across player, greydwarf and troll effects), so the texture swap needs to happen
        // once per material, not once per spawned decal.
        private static readonly HashSet<int> SkinnedMaterials = new HashSet<int>();

        private static readonly HashSet<int> SkinnedSprayMaterials = new HashSet<int>();

        private static Texture2D _atlas;
        private static Texture2D _atlasNormal;
        // Single-cell fallback, for when the shader turns out not to honour the sheet animation.
        private static Texture2D _single;
        private static Texture2D _singleNormal;
        private static Texture2D _droplet;
        private static Texture2D _mist;
        private static Texture2D _spraySplat;
        private static Texture2D _sprayDrop;
        private static bool _loadAttempted;

        // Parsed once, like the decal list.
        private static string[] _sprayPrefixes;

        /// Blood materials that are always covered, whatever the config says.
        ///
        /// The config file persists across updates and silently wins over new code defaults -
        /// the same trap that had a 12-frame sheet being sliced as 8 for hours. So the materials
        /// the game actually uses for blood are listed here in code and always included; the
        /// config setting adds to this rather than replacing it. That way finding a missed
        /// material - blood_cloud, with 37 owners, was missed - fixes every install rather than
        /// only fresh ones.
        private static readonly string[] KnownDecalMaterials =
            { "splat_decal_blend", "seeker_blood_splat", "SeekerQueen_decals" };

        private static readonly string[] KnownSprayMaterials =
            { "blood_splat", "blood_drop", "blood_cloud" };

        // Parsed once from config, then reused - this is checked on every decal spawn.
        private static string[] _materialPrefixes;

        /// Grid size for a sheet, derived from the texture rather than taken from config.
        ///
        /// BepInEx keeps whatever is already in the .cfg and ignores new code defaults, so every
        /// time the sheet layout changed the config on an existing install kept the OLD numbers.
        /// The result is silent and confusing: a 12-frame sheet sliced as 8, wrong sub-rects,
        /// bottom rows never drawn, and nothing in the log to say so.
        ///
        /// Cell size is the fixed thing here - 512 for the ground sheet, 256 for the spray - so
        /// the grid is just the texture divided by it. Config still wins when it matches, and is
        /// corrected with a log line when it doesn't.
        private static readonly HashSet<string> _gridWarned = new HashSet<string>();

        internal static void ResolveGrid(Texture2D tex, int cellSize, ref int cols, ref int rows, string what)
        {
            if (tex == null || cellSize <= 0)
                return;

            int c = Mathf.Max(1, tex.width / cellSize);
            int r = Mathf.Max(1, tex.height / cellSize);

            if (c == cols && r == rows)
                return;

            // Logged once per distinct correction. This runs per spray system - 350 of them -
            // and the first version buried every other line in the log under one repeated
            // warning.
            string key = what + c + "x" + r;
            if (_gridWarned.Add(key))
                Plugin.Log.LogWarning(
                $"{what} grid in config is {cols}x{rows} but the texture is {tex.width}x{tex.height} " +
                $"({cellSize}px cells = {c}x{r}). Using {c}x{r}. Config values persist across " +
                "updates, so an older layout would otherwise be used against newer art.");

            cols = c;
            rows = r;
        }

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
            // the ground texture off also silently disabled the spray texture, the green-splash
            // fix, spray density, gravity, drag and trails, all of which have their own settings.
            // Three unrelated features hidden behind one flag.
            if (!Plugin.ReplaceTexture.Value && !Plugin.ReplaceSprayTexture.Value
                                             && !Plugin.FixGreenSplash.Value)
                return;

            int decals = 0;
            int sprays = 0;
            int splashes = 0;

            // Decals and sprays in one pass over the prefabs. GetComponentsInChildren isn't
            // cheap across 3500 prefabs, so it's worth not doing it twice.
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

                if (!Plugin.ReplaceSprayTexture.Value && !Plugin.FixGreenSplash.Value)
                    continue;

                // Whether this prefab is a blood effect at all, decided structurally: it carries
                // a ParticleDecal that renders with a blood material. That test is what makes the
                // green-splash fix below safe - no name matching, no guessing.
                bool isBloodEffect = decalsHere > 0;

                // Hit effects and death effects get separate density budgets. Naming is
                // consistent across the game's per-creature effects - vfx_neck_hit,
                // vfx_troll_death, fx_bat_hit - which is what makes this reliable here even
                // though name matching was the wrong tool for finding the blood materials.
                bool isHitEffect = prefab.name.IndexOf("hit", StringComparison.OrdinalIgnoreCase) >= 0;

                foreach (ParticleSystemRenderer r in prefab.GetComponentsInChildren<ParticleSystemRenderer>(true))
                {
                    if (r == null)
                        continue;
                    Material mat = r.sharedMaterial;
                    if (mat == null)
                        continue;

                    if (IsSprayMaterial(mat))
                    {
                        if (!Plugin.ReplaceSprayTexture.Value)
                            continue;
                        try
                        {
                            if (!SkinnedSprayMaterials.Contains(mat.GetInstanceID()))
                            {
                                SkinSprayMaterial(mat);
                                sprays++;
                            }
                            EnableSprayVariants(r, isHitEffect);
                        }
                        catch (Exception e)
                        {
                            Plugin.Log.LogWarning("Spray skin failed on " + mat.name + ": " + e.Message);
                        }
                        continue;
                    }

                    if (isBloodEffect && Plugin.FixGreenSplash.Value && IsSlimeSplashMaterial(mat))
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

            Plugin.Log.LogInfo($"Pre-skinned {decals} decal material(s), {sprays} spray material(s) " +
                               $"and re-coloured {splashes} green splash renderer(s) at world load.");
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

                TuneDecalSystem(decalSystem);
                TuneChance(decal);

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
        private static bool IsBloodMaterial(Material mat)
        {
            if (mat.name == null)
                return false;

            if (_materialPrefixes == null)
            {
                string raw = Plugin.DecalMaterials.Value ?? string.Empty;
                var parsed = new List<string>();
                foreach (string part in raw.Split(','))
                {
                    string trimmed = part.Trim();
                    if (trimmed.Length > 0)
                        parsed.Add(trimmed);
                }
                _materialPrefixes = parsed.ToArray();
            }

            foreach (string prefix in _materialPrefixes)
            {
                // Prefix, not equality: Unity appends " (Instance)" when a material is instanced.
                if (mat.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            foreach (string prefix in KnownDecalMaterials)
            {
                if (mat.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// The airborne spray, as distinct from the ground decal.
        ///
        /// Two materials carry it: "blood_splat", whose texture is `leaf_low` at 8x8, and
        /// "blood_drop", which has no _MainTex at all - meaning those particles draw as flat
        /// untextured quads, which is why airborne blood looks blocky.
        ///
        /// "slime_green" is the third material inside the blood effects and is deliberately
        /// excluded: Blobs almost certainly share it, and replacing it would recolour every
        /// slime in the game. Its owners were only visible within blood-named prefabs, so the
        /// sharing report couldn't rule that out - and an unprovable risk isn't worth taking
        /// for a splash that lasts half a second.
        private static bool IsSprayMaterial(Material mat)
        {
            if (mat.name == null)
                return false;

            if (_sprayPrefixes == null)
            {
                string raw = Plugin.SprayMaterials.Value ?? string.Empty;
                var parsed = new List<string>();
                foreach (string part in raw.Split(','))
                {
                    string trimmed = part.Trim();
                    if (trimmed.Length > 0)
                        parsed.Add(trimmed);
                }
                _sprayPrefixes = parsed.ToArray();
            }

            foreach (string prefix in _sprayPrefixes)
            {
                if (mat.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            foreach (string prefix in KnownSprayMaterials)
            {
                if (mat.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static void SkinSprayMaterial(Material mat)
        {
            if (!SkinnedSprayMaterials.Add(mat.GetInstanceID()))
                return;

            LoadTextures();

            if (!mat.HasProperty("_MainTex"))
                return;

            Texture before = mat.GetTexture("_MainTex");

            // One fixed texture per material, chosen by name.
            //
            // The old path handed every spray material the same sheet and let particle size
            // decide which frame to sample. That was wrong in principle and measurably wrong in
            // practice: "is this mist" is a property of blood_cloud, not of how large the
            // particle happens to be, and blood_cloud draws at 0.5 units on an ordinary death -
            // well under any sensible size threshold - so the material that IS mist was being
            // handed crisp droplet frames. With MistBias at 0 the mist frames were unreachable
            // for ordinary kills entirely.
            //
            // Selecting by material removes the guess. It is also exactly what vanilla does:
            // leaf_low on blood_splat, brains on blood_cloud, nothing at all on blood_drop.
            Texture2D sprayTex = SprayTextureFor(mat.name);
            if (sprayTex == null)
                return;

            mat.SetTexture("_MainTex", sprayTex);

            Plugin.Log.LogInfo($"Skinned spray \"{mat.name}\" id={mat.GetInstanceID()} " +
                               $"shader=\"{(mat.shader == null ? "?" : mat.shader.name)}\": " +
                               $"_MainTex \"{(before == null ? "NONE (untextured quads)" : before.name)}\" " +
                               $"-> \"{sprayTex.name}\" {sprayTex.width}x{sprayTex.height}");
        }

        /// Picks the texture for a spray material by name.
        ///
        /// A recognised material whose texture failed to load returns null, leaving vanilla's
        /// texture in place. It must never be handed a different material's art: when the three
        /// spray PNGs were missing from the csproj, the old fallback substituted a cell of the
        /// legacy sheet - a 512px glossy sphere - onto all three materials, so every airborne
        /// particle including blood_drop's 200-per-death drew a large shiny ball. The mod looked
        /// broken rather than absent, which is the worse failure of the two.
        private static Texture2D SprayTextureFor(string matName)
        {
            if (matName == null)
                return null;

            if (matName.StartsWith("blood_cloud", StringComparison.OrdinalIgnoreCase))
                return _mist;

            if (matName.StartsWith("blood_drop", StringComparison.OrdinalIgnoreCase))
                return _sprayDrop;

            if (matName.StartsWith("blood_splat", StringComparison.OrdinalIgnoreCase))
                return _spraySplat;

            // Only a material we do not recognise at all reaches the generic image, and only
            // when it actually loaded.
            return _spraySplat;
        }

        /// Gives the airborne spray the same random-variant treatment the ground decals get.
        ///
        /// Without it every particle in every spray is the same droplet silhouette, which is
        /// what made the air look flatter than the ground once the ground had eight shapes.
        private static void EnableSprayVariants(ParticleSystemRenderer r, bool isHitEffect)
        {
            ParticleSystem ps = r.GetComponent<ParticleSystem>();
            if (ps == null)
                return;

            // A dedicated figure for hits, because that is the case that reads as too dry: a
            // death already throws a lot of particles, while an ordinary hit emits a burst of
            // five chunks and is over. Zero means "no separate value, use the general one".
            float mul = Plugin.SprayDensity.Value;
            if (isHitEffect && Plugin.HitSprayDensity.Value > 0f)
                mul = Plugin.HitSprayDensity.Value;

            // Cloud systems get their own multiplier, because they are not droplets.
            //
            // A greydwarf or neck death fires 88 blood_cloud particles across three systems
            // (soft cloud 30, splat 50, blobs 8) at 0.5-1.5 units each. Multiplying those by the
            // droplet figure produced ~265 overlapping clouds and read as an explosion of mist on
            // ordinary kills. Droplets want density; clouds do not.
            Material mat = r.sharedMaterial;
            if (mat != null && mat.name != null
                            && mat.name.StartsWith("blood_cloud", StringComparison.OrdinalIgnoreCase))
                mul = Plugin.CloudSprayDensity.Value;

            DensifySpray(ps, mul);
            Realism.Trails(r, ps, TrailMaterial(r.sharedMaterial));
            RandomiseSprayRotation(ps);
            Realism.Ballistics(ps);

            if (!Plugin.SprayVariants.Value)
                return;

            int cols = Mathf.Max(1, Plugin.SprayAtlasColumns.Value);
            int rows = Mathf.Max(1, Plugin.SprayAtlasRows.Value);
            ResolveGrid(_droplet, Plugin.SprayCellSize.Value, ref cols, ref rows, "Spray atlas");

            ParticleSystem.TextureSheetAnimationModule tsa = ps.textureSheetAnimation;
            tsa.enabled = true;
            tsa.numTilesX = cols;
            tsa.numTilesY = rows;
            tsa.animation = ParticleSystemAnimationType.WholeSheet;
            tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f);

            // Big airborne particles must draw mist, not droplets.
            //
            // blood_cloud covers both "soft cloud" at 0.5 units and "Big Splat" at 5-12, so a
            // free choice from the whole sheet would stretch a crisp round droplet across twelve
            // metres - a smooth red ball. Anything large gets the mist row, where a soft diffuse
            // cloud is exactly what enlarging should produce.
            float authored = AuthoredSize(ps.main.startSize);
            int mistRow = Mathf.Clamp(Plugin.MistFrameRow.Value, 0, rows - 1);

            bool large = Plugin.SizeAwareSpray.Value && rows > 1 && authored >= Plugin.LargeSpraySize.Value;

            // Small systems can also be pushed toward mist.
            //
            // Left to a free choice the mist row is 4 frames of 16, so only a quarter of small
            // particles are hazy. MistBias raises that. It is decided PER SYSTEM rather than per
            // particle - Unity's startFrame takes one range, not a weighted distribution - but a
            // single hit fires several systems at once (chunks, drops, splash), so a burst still
            // comes out mixed rather than uniformly one or the other.
            bool biasToMist = !large && rows > 1 && Plugin.MistBias.Value > 0f
                              && UnityEngine.Random.value < Plugin.MistBias.Value;

            if (large || biasToMist)
            {
                tsa.startFrame = new ParticleSystem.MinMaxCurve(
                    mistRow * cols, (mistRow + 1) * cols - 0.001f);
            }
            else
            {
                tsa.startFrame = new ParticleSystem.MinMaxCurve(0f, cols * rows - 0.001f);
            }
        }

        /// Random start rotation for spray particles.
        ///
        /// Needed as soon as the spray sheet contains anything elongated - a slash arc, a
        /// stretched droplet. These particles are view-aligned billboards, so without a random
        /// rotation every one of them points the same way on screen and a burst reads as a set
        /// of parallel streaks rather than blood thrown in all directions.
        ///
        /// Harmless for the round droplets, which look identical at any rotation.
        private static void RandomiseSprayRotation(ParticleSystem ps)
        {
            if (!Plugin.SprayVariants.Value)
                return;

            ParticleSystem.MainModule main = ps.main;
            if (main.startRotation3D)
                return;   // authored per-axis; leave that alone rather than flatten it

            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        }

        /// More particles in the air.
        ///
        /// Vanilla's spray counts are modest - a hit emits a burst of 5 chunks, 30 splash and 50
        /// droplets, and a death 200 - so the air clears almost immediately while the ground mark
        /// lingers for ten seconds. Scaling the burst counts is what closes that gap.
        ///
        /// maxParticles is raised alongside, because it caps the system regardless of what the
        /// bursts ask for: multiplying a burst of 50 by four does nothing if the cap is still 50.
        /// The emission rate is scaled too, for the few systems that stream instead of bursting.
        private static void DensifySpray(ParticleSystem ps, float mul)
        {
            // Lifetime and size first - these apply regardless of the density multiplier.
            //
            // Density alone does not make the air feel bloodier. Vanilla spray lives 0.4 to 1
            // second, so however many particles are emitted they are gone almost at once and
            // never accumulate into anything. Making them last longer and read larger does more
            // for "misting" than emitting more of them.
            ParticleSystem.MainModule m = ps.main;

            float lifeMul = Plugin.SprayLifetimeMultiplier.Value;
            if (Math.Abs(lifeMul - 1f) > 0.001f)
                m.startLifetime = Scale(m.startLifetime, lifeMul);

            float sizeMul = Plugin.SpraySizeMultiplier.Value;
            if (Math.Abs(sizeMul - 1f) > 0.001f)
                m.startSize = Scale(m.startSize, sizeMul);

            if (Math.Abs(mul - 1f) <= 0.001f)
                return;

            ParticleSystem.EmissionModule em = ps.emission;

            for (int i = 0; i < em.burstCount; i++)
            {
                ParticleSystem.Burst b = em.GetBurst(i);
                b.count = Scale(b.count, mul);
                em.SetBurst(i, b);
            }

            if (em.rateOverTime.constant > 0f || em.rateOverTime.constantMax > 0f)
                em.rateOverTime = Scale(em.rateOverTime, mul);

            ParticleSystem.MainModule main = ps.main;
            int wanted = Mathf.CeilToInt(main.maxParticles * mul);
            if (wanted > main.maxParticles)
                main.maxParticles = Mathf.Min(wanted, 10000);
        }

        /// Material for spray trails: the spray material with a single droplet cell.
        ///
        /// A ribbon sampling the full atlas would show the 4x4 grid stretched along its length,
        /// so it gets one cell rather than the sheet.
        private static Material TrailMaterial(Material template)
        {
            if (_trailMaterial != null || template == null)
                return _trailMaterial;

            try
            {
                LoadTextures();
                _trailMaterial = new Material(template) { name = "CarturBloodTrail" };
                // The plain droplet, not a sheet cell. The legacy sheet's first cell is a large
                // glossy sphere with a specular highlight - stretched along a trail it reads as a
                // shiny tube.
                Texture2D cell = _sprayDrop ?? SliceFirstCell(_droplet);
                if (cell != null && _trailMaterial.HasProperty("_MainTex"))
                    _trailMaterial.SetTexture("_MainTex", cell);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not build trail material: " + e.Message);
            }
            return _trailMaterial;
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

        // The textures the decal material had before we touched it, kept so effects that borrow
        // the blood material for something that is not blood can be handed them back.
        private static Texture _originalMainTex;
        private static Texture _originalNormalTex;
        private static Material _vanillaDecalMaterial;
        private static Material _trailMaterial;
        private static string[] _excludePrefixes;

        /// Top-left cell of the spray sheet, as a standalone texture.
        private static Texture2D SliceFirstCell(Texture2D sheet)
        {
            if (sheet == null)
                return null;
            try
            {
                int cols = Mathf.Max(1, Plugin.SprayAtlasColumns.Value);
                int rows = Mathf.Max(1, Plugin.SprayAtlasRows.Value);
                ResolveGrid(sheet, Plugin.SprayCellSize.Value, ref cols, ref rows, "Spray atlas");
                int cw = sheet.width / cols;
                int ch = sheet.height / rows;

                // Cell 0 sits at the TOP-left, and Unity's texture origin is bottom-left, so the
                // read starts at the top row rather than at y = 0.
                Color[] px = sheet.GetPixels(0, sheet.height - ch, cw, ch);
                var tex = new Texture2D(cw, ch, TextureFormat.RGBA32, true)
                {
                    name = "CarturBloodSplashCell",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                tex.SetPixels(px);
                tex.Apply(true);
                return tex;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not slice splash cell: " + e.Message);
                return null;
            }
        }

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

                LoadTextures();

                // A single image, not a sheet. This material's renderers don't get sheet
                // animation enabled - they aren't in the spray whitelist, they're reached by the
                // clone path - so handing them the atlas made every splash particle draw all four
                // droplets squashed together. One image is what a plain shader expects.
                Texture2D splashTex = _spraySplat ?? SliceFirstCell(_droplet) ?? _droplet;
                if (splashTex != null && _bloodSplashMaterial.HasProperty("_MainTex"))
                    _bloodSplashMaterial.SetTexture("_MainTex", splashTex);

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

            // With the atlas off, a single-splat texture has to be used - not the atlas. Leaving
            // the atlas assigned while the sheet animation is disabled maps all eight cells onto
            // one quad, which reads as a square blob. That was a real gap: turning the setting
            // off to escape a square would have kept the square.
            bool useAtlas = Plugin.UseVariantAtlas.Value;
            Texture2D albedo = useAtlas ? _atlas : _single;
            Texture2D normal = useAtlas ? _atlasNormal : _singleNormal;

            if (albedo == null)
            {
                albedo = _atlas ?? _single;
                normal = _atlas != null ? _atlasNormal : _singleNormal;
                if (albedo == null)
                    return;
            }

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
            {
                _originalMainTex = before;
                if (mat.HasProperty("_NormalTex"))
                    _originalNormalTex = mat.GetTexture("_NormalTex");
            }

            mat.SetTexture("_MainTex", albedo);

            if (Plugin.ReplaceNormalMap.Value && normal != null && mat.HasProperty("_NormalTex"))
                mat.SetTexture("_NormalTex", normal);

            Plugin.Log.LogInfo($"Skinned \"{mat.name}\" id={mat.GetInstanceID()}: " +
                               $"_MainTex \"{(before == null ? "none" : before.name)}\" -> \"{albedo.name}\" " +
                               $"{albedo.width}x{albedo.height} (atlas={useAtlas})");

            // Built from the freshly skinned material, so the variants inherit its shader,
            // normal map and colour - everything except which cell of the atlas they show.
            if (useAtlas && _atlas != null)
                Variants.Build(mat, _atlas, Plugin.ReplaceNormalMap.Value ? _atlasNormal : null);

            Texture after = mat.HasProperty("_MainTex") ? mat.GetTexture("_MainTex") : null;
            if (after != albedo)
                Plugin.Log.LogWarning($"\"{mat.name}\" still holds \"{(after == null ? "none" : after.name)}\" " +
                                      "immediately after skinning - something else is writing to it.");
        }

        private static void TuneDecalSystem(ParticleSystem ps)
        {
            BloodPreset preset = BloodPreset.Current();
            ParticleSystem.MainModule main = ps.main;

            // Captured before scaling: the frame range is chosen from the decal's authored size,
            // and a size multiplier shouldn't change which artwork it gets.
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
            Realism.AgeDecal(ps);
            Realism.GrowDecal(ps);

            if (!Plugin.ReplaceTexture.Value || !Plugin.UseVariantAtlas.Value)
                return;

            bool small = authoredSize < Plugin.SmallDecalSize.Value;

            // Material variants first: the shader ignores sheet animation, so that route made
            // every puddle identical. A per-decal material is something it can't ignore.
            if (Variants.Assign(ps, small))
                return;

            EnableVariants(ps, authoredSize);
        }

        private static float AuthoredSize(ParticleSystem.MinMaxCurve c)
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
        /// Replacing the texture with pure white made the effect's own startColor the only source
        /// of colour. Most creatures author a saturated red and now look better than vanilla, but
        /// some - the player's hit effect among them - author something near-grey and relied on
        /// vanilla's reddish 'brains' texture to supply the red. Those came out grey.
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

        /// Turns one shared material into four random splat shapes.
        ///
        /// Texture Sheet Animation is off in vanilla (confirmed on every blood system). Enabling
        /// it with a 2x2 grid, frameOverTime pinned to 0 and startFrame randomised gives each
        /// particle a random cell that then holds still - the standard Unity way to get sprite
        /// variants out of a single material. Without this, every splat in the game is the same
        /// shape rotated, which is the main thing that makes vanilla blood read as repetitive.
        private static void EnableVariants(ParticleSystem ps, float authoredSize)
        {
            int cols = Mathf.Max(1, Plugin.AtlasColumns.Value);
            int rows = Mathf.Max(1, Plugin.AtlasRows.Value);

            ParticleSystem.TextureSheetAnimationModule tsa = ps.textureSheetAnimation;
            tsa.enabled = true;
            tsa.numTilesX = cols;
            tsa.numTilesY = rows;
            tsa.animation = ParticleSystemAnimationType.WholeSheet;
            // Pinned so the cell chosen at birth is the cell drawn for the decal's whole life.
            tsa.frameOverTime = new ParticleSystem.MinMaxCurve(0f);

            // Pick the frame range by how big this decal actually is.
            //
            // Otherwise a random cell means a size-1 hit can draw a full death burst while a
            // troll's size-6 splat draws two droplets - the texture is random but the quad size
            // is authored per creature, and nothing tied them together. The atlas is laid out
            // with big shapes on the top row and small marks below, so the row is the choice.
            //
            // Frame indices run in reading order, so row r spans [r*cols, (r+1)*cols). Just
            // short of the upper bound because the value is floored to an index.
            int firstRow = 0;
            int lastRow = rows - 1;
            if (rows > 1 && Plugin.SizeAwareVariants.Value)
            {
                bool small = authoredSize < Plugin.SmallDecalSize.Value;
                firstRow = small ? rows - 1 : 0;
                lastRow = small ? rows - 1 : Mathf.Max(0, rows - 2);
            }

            tsa.startFrame = new ParticleSystem.MinMaxCurve(
                firstRow * cols,
                (lastRow + 1) * cols - 0.001f);
        }

        private static void TuneChance(ParticleDecal decal)
        {
            BloodPreset preset = BloodPreset.Current();

            float chance = decal.m_chance * preset.ChanceMultiplier;
            if (chance < preset.MinChance)
                chance = preset.MinChance;

            // Decouple ground blood from airborne density.
            //
            // The particles thrown into the air ARE the ones that mark the ground:
            // ParticleDecal.OnParticleCollision emits one decal per collision event of its own
            // emitter. So multiplying the spray by six also multiplies ground decals by roughly
            // six, no matter what the chance says - a thick spray would carpet the terrain.
            //
            // Dividing the final chance by the same multiplier keeps the number of marks at the
            // vanilla-equivalent rate while the air gets denser. Applied last, after the floor,
            // because the goal is a decal count and anything applied afterwards would undo it.
            if (Plugin.DecoupleGroundFromSpray.Value)
            {
                float sprayMul = SprayMultiplierFor(decal);
                if (sprayMul > 1f)
                    chance /= sprayMul;
            }

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
            {
                string raw = Plugin.ExcludeEffects.Value ?? string.Empty;
                var parsed = new List<string>();
                foreach (string part in raw.Split(','))
                {
                    string t = part.Trim();
                    if (t.Length > 0)
                        parsed.Add(t);
                }
                _excludePrefixes = parsed.ToArray();
            }

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

        /// Gives one decal renderer a material carrying the pre-swap textures, so an excluded
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
                    if (_originalNormalTex != null && _vanillaDecalMaterial.HasProperty("_NormalTex"))
                        _vanillaDecalMaterial.SetTexture("_NormalTex", _originalNormalTex);
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

        /// Which spray multiplier this decal's emitter received.
        ///
        /// Read from the spawned instance's root name rather than remembered from the prefab
        /// pass, because prefab instance ids don't survive instantiation. The game's per-creature
        /// effects are named consistently - vfx_neck_hit, vfx_troll_death - and Unity only
        /// appends "(Clone)", so the classification carries over intact.
        private static float SprayMultiplierFor(ParticleDecal decal)
        {
            try
            {
                Transform root = decal.transform.root;
                string name = root == null ? string.Empty : root.name;
                bool isHit = name.IndexOf("hit", StringComparison.OrdinalIgnoreCase) >= 0;

                if (isHit && Plugin.HitSprayDensity.Value > 0f)
                    return Plugin.HitSprayDensity.Value;
                return Plugin.SprayDensity.Value;
            }
            catch
            {
                return 1f;
            }
        }

        /// Scales a MinMaxCurve while preserving whichever mode it was authored in - the decal
        /// systems use both Constant and TwoConstants, and writing the wrong mode back would
        /// silently discard the authored range.
        private static ParticleSystem.MinMaxCurve Scale(ParticleSystem.MinMaxCurve c, float mul)
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

        private static void LoadTextures()
        {
            if (_loadAttempted)
                return;
            _loadAttempted = true;

            // The ground atlas keeps mipmaps: it is sliced into one texture per cell, so a mip
            // level can only ever average within a single splat.
            _atlas = Load("blood_splat_atlas.png", "CarturBloodAtlas", mipmap: true);
            _atlasNormal = Load("blood_splat_atlas_n.png", "CarturBloodAtlasNormal", mipmap: true);

            // One texture per spray material, each sized to what that material actually draws
            // at, which is how vanilla assigns particle textures in the first place - it never
            // samples a sheet. Because these are single images rather than atlas cells, mipmaps
            // are safe and wanted: there are no cell boundaries for a mip level to average
            // across, which was the whole reason the old sheet had to ship without them.
            //
            // The sizes come from the effect graph, not from taste:
            //   blood_cloud  0.5 units typical, up to 12 on big creatures  -> 1024
            //   blood_splat  <= 0.6                                        ->  256
            //   blood_drop   0.05, 200 per death; vanilla ships 8x8 here   ->  128
            _mist = Load("blood_mist.png", "CarturBloodMist", mipmap: true);
            _spraySplat = Load("blood_splat.png", "CarturBloodSpraySplat", mipmap: true);
            _sprayDrop = Load("blood_drops.png", "CarturBloodDrop", mipmap: true);

            // The legacy 16-frame sheet. Still loaded because SprayVariants, the trail material
            // and the green-splash clone all slice cells out of it, and because a file dropped in
            // BepInEx/config can still override it. Nothing reads it on the default path.
            _droplet = Load("blood_droplet.png", "CarturBloodDroplet", mipmap: false);

            _single = Load("blood_splat_single.png", "CarturBloodSingle", mipmap: true);
            _singleNormal = Load("blood_splat_single_n.png", "CarturBloodSingleNormal", mipmap: true);
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
                // Clamp matters for an atlas: repeat would let a cell sample its neighbour at
                // the seam. The cells also carry transparent margins, which keeps mip levels
                // from bleeding one splat into the next.
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
