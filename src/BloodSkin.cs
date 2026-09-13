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

        /// Every material this mod owns or has written to, held as references rather than ids so
        /// the look settings can be re-applied to all of them at world load.
        ///
        /// SkinnedMaterials holds instance ids, and an id is not a handle. That distinction is
        /// why changing Wetness or Reflections appeared to do nothing for an entire session:
        /// SkinMaterial returns early once the texture is already ours, so on a world reload it
        /// never reached the lines that write those properties, and only a full relaunch applied
        /// them. The menu implied they were live and they were not.
        private static readonly List<Material> Owned = new List<Material>();

        private static void Own(Material mat)
        {
            if (mat != null && !Owned.Contains(mat))
                Owned.Add(mat);
        }

        /// Re-applies the settings that live on a material rather than inside a texture.
        ///
        /// Called once per world load, so changing them in the menu takes effect on the next
        /// world load instead of the next launch.
        ///
        /// The split is worth knowing, because not everything can refresh this way. Wetness,
        /// Reflections and the Cutout-to-Fade switch are material properties, so they update
        /// here. GroundOpacity is baked into the texture's pixels when it loads, and turning
        /// GroundNormalMap on when it was off at startup means the normal map was never loaded
        /// at all - both of those still need a relaunch, and saying so is better than pretending
        /// otherwise.
        internal static void RefreshLookSettings()
        {
            int n = 0;
            foreach (Material mat in Owned)
            {
                if (mat == null)
                    continue;
                ApplyWetness(mat);
                ApplyGroundFade(mat);
                n++;
            }
            if (n > 0)
                Plugin.Log.LogInfo($"Re-applied look settings to {n} blood material(s).");
        }

        // Ground marks. Two large, chosen per decal, and one small.
        private static Texture2D _groundLarge;
        private static Texture2D _groundLargeAlt;
        private static Texture2D _groundSmall;
        // The airborne splash, for the cloned blood material only.
        private static Texture2D _splash;
        // Smoothness map. Linear, not sRGB: this is data the shader reads as a number, not a
        // picture, and letting Unity gamma-correct it would skew every smoothness value.
        private static Texture2D _gloss;
        // The airborne droplet. Vanilla's blood_drop material has no _MainTex at all - the 200
        // droplets are flat shaded quads - so this is an addition rather than a replacement.
        // 256 is sized to what a droplet actually covers: 0.05 world units is 28 screen pixels
        // at melee range on a 1080p screen and 113 at DropletSize 4, so 256 leaves about 2x for
        // mipmapping and no more.
        private static Texture2D _droplet;
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

                if (IsDeathEffect(decal) && DebugTuning.DeathPoolEnabled())
                    Pooling.SpawnPool(decal);

                // After the decal's lifetime has been scaled, so the timer covers the value
                // actually in use rather than the authored one.
                ExtendEffectLifetime(decal);

                SprayAim.Apply(decal);

                // Last, so it can tune the systems CloudGraft just added as well as the ones the
                // effect shipped with. No-op unless the debug menu is switched on.
                DebugTuning.Apply(decal);
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

        private static Material _bloodDropMaterial;

        /// Gives the 200 airborne droplets a texture, which vanilla never does.
        ///
        /// Cloned rather than assigned to the shared material, for the same reason the splash is:
        /// `blood_drop` is one material shared by 128 renderer slots, and this mod has already
        /// been caught once assuming a material named after blood is only used by blood. Cloning
        /// means no proof about the other owners is needed - whoever they are, they keep the
        /// original object untouched.
        ///
        /// _Color is left alone here, unlike the splash clone: the droplets are not green, and
        /// their per-creature startColor already works.
        internal static bool ApplyDropletTexture(ParticleSystemRenderer r)
        {
            LoadTextures();

            if (_droplet == null || r == null)
                return false;

            Material original = r.sharedMaterial;
            if (original == null || original == _bloodDropMaterial)
                return false;

            if (_bloodDropMaterial == null)
            {
                _bloodDropMaterial = new Material(original) { name = "CarturBloodDroplet" };

                if (!_bloodDropMaterial.HasProperty("_MainTex"))
                {
                    Plugin.Log.LogWarning(
                        $"\"{original.name}\" has no _MainTex, so a droplet texture cannot be " +
                        "assigned to it. Droplets stay untextured; nothing else is affected.");
                    _bloodDropMaterial = null;
                    return false;
                }

                _bloodDropMaterial.SetTexture("_MainTex", _droplet);
                ApplyWetness(_bloodDropMaterial);
                Plugin.Log.LogInfo($"Cloned \"{original.name}\" -> \"CarturBloodDroplet\" and gave " +
                                   "it a droplet texture (vanilla leaves these untextured).");
            }

            Own(_bloodDropMaterial);
            r.sharedMaterial = _bloodDropMaterial;
            return true;
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

                // Vanilla's slime_splash is 256x256 and a membrane rather than a splash, so the
                // clone gets our own 1024 instead. Only the picture changes: _Color stays white
                // above, so each creature's startColor still decides the colour exactly as before.
                //
                // An earlier version put the mod's spray art here and it read wrong - that art was
                // a spray, not a splash. This one is the right silhouette for the job.
                if (_splash != null && Plugin.ReplaceSplashTexture.Value &&
                    _bloodSplashMaterial.HasProperty("_MainTex"))
                {
                    _bloodSplashMaterial.SetTexture("_MainTex", _splash);
                }
                ApplyWetness(_bloodSplashMaterial);
                Plugin.Log.LogInfo($"Cloned \"{original.name}\" -> \"CarturBloodSplash\" " +
                                   "(green splash inside blood effects; the original is left alone " +
                                   "so slimes are unaffected).");
            }

            Own(_bloodSplashMaterial);
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
            {
                // The texture is already ours and does not need rewriting, but the look settings
                // may have been changed in the menu since. Applying them here is what makes
                // Wetness and Reflections take on a world reload rather than only on a restart -
                // without it this early return skipped every line below, and an entire session
                // was spent changing settings that could not take effect.
                Own(mat);
                ApplyWetness(mat);
                ApplyGroundFade(mat);
                // Clears _BumpMap and the _NORMALMAP keyword off materials an earlier build wrote
                // them to, so the fix lands on a world reload rather than needing a clean install.
                ClearForeignNormalSlots(mat);
                SetNormal(mat, _groundLargeNormal);
                return;
            }

            SkinnedMaterials.Add(mat.GetInstanceID());
            Own(mat);

            // Captured once, before the first swap. Valheim reuses the blood decal material for
            // things that are not blood - Moder's frost breath and the Seeker Queen's spit both
            // render through it - so those effects need the original texture put back.
            if (_originalMainTex == null && before != null)
                _originalMainTex = before;

            mat.SetTexture("_MainTex", albedo);
            SetNormal(mat, _groundLargeNormal);
            ApplyWetness(mat);
            ApplyGroundFade(mat);

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
                Own(clone);
                clone.SetTexture("_MainTex", tex);
                ApplyWetness(clone);
                ApplyGroundFade(clone);
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

        /// Assigns a normal map to whichever slot this material's shader actually has.
        ///
        /// TWO names, because the blood materials do not share a shader. Read out of the game's
        /// own bundle rather than guessed:
        ///
        ///     blood_splat         Particles/Standard Surface2   _BumpMap     (was empty)
        ///     blood_splat2        (external shader)             _BumpMap     (was empty)
        ///     blood_drop          Particles/Standard Surface2   _BumpMap     (was empty)
        ///     splat_decal_blend   Custom/ParticleDecal          _NormalTex
        ///
        /// Only splat_decal_blend has _NormalTex, so writing that name alone meant the normal map
        /// was silently dropped on three of the four materials - including blood_splat, which is
        /// the main ground mark. The HasProperty guard turned a wrong property name into no error
        /// and no effect, which is exactly the failure it was written to prevent.
        ///
        /// Still silent when a material has neither, for the original reason: a modded or updated
        /// shader without the slot should cost the shine, not the blood.
        /// Undoes what an earlier build wrote to the wrong normal slot.
        ///
        /// Material property values persist for the life of the loaded material, so a material
        /// that had _BumpMap and _NORMALMAP set by a previous session keeps them until something
        /// clears them. Only done where _NormalTex exists - that is the marker for "this shader
        /// had its own slot and should never have been given the other one".
        private static void ClearForeignNormalSlots(Material mat)
        {
            if (mat == null || !mat.HasProperty("_NormalTex") || !mat.HasProperty("_BumpMap"))
                return;
            mat.SetTexture("_BumpMap", null);
            mat.DisableKeyword("_NORMALMAP");
        }

        private static void SetNormal(Material mat, Texture2D normal)
        {
            if (normal == null || mat == null)
                return;

            // ONE slot, not both, and _NormalTex wins where it exists.
            //
            // A material that declares _NormalTex has told us where its normal goes. Writing
            // _BumpMap as well, and switching on _NORMALMAP, is writing to names whose meaning in
            // THAT shader is unknown - and splat_decal_blend is Custom/ParticleDecal, a custom
            // shader whose source is not readable from the bundle. Those two lines were added for
            // Particles/Standard Surface2, which genuinely needs them, and applied to every blood
            // material without checking whether the others wanted them.
            //
            // Ground blood then began rendering as a blue-grey network tracing the splat's own
            // shape, with the decal's start colour measured live at full yellow saturation - the
            // tint was correct and something after it was overriding. A tangent-space normal map
            // is blue-lavender, which is what that looked like. Not proven, because the shader
            // cannot be read, but it is the one change that could plausibly cause it and this
            // restores the behaviour that was working before.
            if (mat.HasProperty("_NormalTex"))
            {
                mat.SetTexture("_NormalTex", normal);
                return;
            }

            if (mat.HasProperty("_BumpMap"))
            {
                mat.SetTexture("_BumpMap", normal);
                // Particles/Standard Surface2 compiles the normal path out unless the keyword is
                // on. Assigning the texture without it binds a map nothing ever samples.
                mat.EnableKeyword("_NORMALMAP");
            }
        }

        /// Makes blood look wet rather than painted on.
        ///
        /// Every blood material's shader is a PBR particle shader with the usual smoothness
        /// controls, which the mod had never touched. Their authored values, from the bundle:
        ///
        ///     blood_splat        _Glossiness 0.23   _Metallic 0   _MetallicGlossMap empty
        ///     splat_decal_blend  _Glossiness 0.14   _Metallic 0   _NormalTex bound
        ///     blood_drop         _Glossiness 0      _Metallic 0   _MetallicGlossMap empty
        ///     blood_cloud        _Glossiness 0      _Metallic 0
        ///
        /// _SpecularHighlights, _GlossyReflections and _LightingEnabled are already 1 on all of
        /// them, so the lighting path is live and only the smoothness was missing.
        ///
        /// Metallic is deliberately left at 0. Blood is a dielectric; raising metallic makes it
        /// reflect like wet paint on a car, which is the usual way this goes wrong.
        ///
        /// Two paths, because they do different things and the shader only honours one at a time:
        /// with no gloss map assigned the shader uses _Glossiness, a single value over the whole
        /// splat - fresh, evenly wet. With a map assigned _Glossiness is IGNORED and _GlossMapScale
        /// multiplies the map instead, so the shine follows the artwork's own ridges and the flat
        /// areas stay dull - congealing, wet only where it pooled. Both are written so the setting
        /// works whichever way the map toggle is left.
        /// Switches ground blood from alpha-testing to alpha-blending so it can actually fade.
        ///
        /// Vanilla ships these materials at _Mode 1 (Cutout) with _Cutoff 0.32, _SrcBlend One,
        /// _DstBlend Zero and _ZWrite on. Under alpha test there is no partial transparency at
        /// all: a pixel is either fully drawn or discarded. So the colourOverLifetime fade in
        /// Realism.AgeDecal could not do what it was written to do - lowering alpha ate the splat
        /// away from its edges inward, held the middle at full strength, and then dropped the
        /// remainder below the threshold in one frame. A fade that ends in a pop.
        ///
        /// The target values are not invented. blood_cloud in the same bundle already ships at
        /// _Mode 2 with _SrcBlend 5 (SrcAlpha), _DstBlend 10 (OneMinusSrcAlpha) and _ZWrite 0,
        /// which is Unity's own Fade preset; this copies that configuration onto the ground
        /// materials.
        ///
        /// Keywords and render queue move with the blend state. Unity's Standard family branches
        /// on _ALPHATEST_ON / _ALPHABLEND_ON, and a material left in the opaque queue would be
        /// drawn before the things it has to blend against.
        ///
        /// ZWrite goes off, which is what a blended surface requires, and is safe here for the
        /// reason it is safe for every other decal: these lie flat on terrain rather than
        /// intersecting each other in depth.
        internal static void ApplyGroundFade(Material mat)
        {
            if (mat == null || !Plugin.GroundFade.Value || !mat.HasProperty("_Mode"))
                return;

            if (Mathf.Abs(mat.GetFloat("_Mode") - 2f) < 0.01f)
                return;   // already in Fade mode

            mat.SetFloat("_Mode", 2f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite"))   mat.SetFloat("_ZWrite", 0f);

            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            Plugin.Log.LogInfo($"\"{mat.name}\": Cutout -> Fade, so ground blood can fade out " +
                               "instead of being cut away and popping.");
        }

        internal static void ApplyWetness(Material mat)
        {
            if (mat == null)
                return;

            float wet = Mathf.Clamp01(BloodPreset.Current().Wet);

            if (mat.HasProperty("_Glossiness"))
                mat.SetFloat("_Glossiness", wet);

            if (mat.HasProperty("_GlossMapScale"))
                mat.SetFloat("_GlossMapScale", wet);

            // Environment reflection, and the reason wet blood came out looking white or grey.
            //
            // All five blood materials ship with _GlossyReflections = 1. That makes the surface
            // mirror the skybox, and Valheim's sky is grey-white, so every splat picks up a broad
            // pale sheen across its whole area. At the vanilla smoothness of 0.14-0.23 it is
            // invisible; at 0.75 it is the first thing you see.
            //
            // It is not a bug in the shader - blood is a dielectric, so its specular really is
            // the colour of the light rather than of the blood. But a whole-surface reflection of
            // the sky reads as wet plastic. Real wet blood shows a small bright highlight where
            // the sun is and stays dark everywhere else, which is _SpecularHighlights alone.
            //
            // So reflections go off by default and the direct highlight stays on. The float and
            // the keyword both have to be set: Unity's Standard family branches on the keyword,
            // and setting the float alone changes the inspector value while the compiled shader
            // carries on reflecting.
            // Full brightness on the ground materials. Verified from the game bundle:
            //
            //     splat_decal_blend    _Color (0.502, 0.502, 0.502)
            //     seeker_blood_splat   _Color (0.557, 0.557, 0.557)
            //     SeekerQueen_decals   _Color (0.708, 0.708, 0.708)
            //
            // A plain halving, applied before texture, tint or lighting has a say. Vanilla's
            // reddish brains texture carried enough colour of its own to survive it; a white
            // texture that relies entirely on the creature's startColor does not.
            //
            // This also matters MORE the more another mod adds to the lighting. Ambient is a
            // fixed ADDITION and the creature's colour is a MULTIPLICATION, so the only lever
            // against an ambient this mod does not control is making the multiplied term bigger.
            // Measured on greydwarf yellow over grass: saturation 0.50 -> 0.63, brightness
            // 0.47 -> 0.71. Doubling the colour halves how much the blue matters.
            //
            // Only ever raised, and _TintColor is deliberately left alone - its 0.5 alpha may be
            // a legacy-neutral value that doubles internally, and that cannot be read from the
            // bundle.
            if (mat.HasProperty("_Color"))
            {
                Color c = mat.GetColor("_Color");
                if (c.r < 0.999f || c.g < 0.999f || c.b < 0.999f)
                    mat.SetColor("_Color", new Color(1f, 1f, 1f, c.a));
            }

            // Unlit is the escape hatch from every other mod's lighting.
            //
            // Valheim's outdoor ambient is blue and ADDITIVE, so it survives whatever the albedo
            // and the tint say - and a shading overhaul or a sky replacer changes how much of it
            // lands on a surface. On an install running both, ground blood rendered as a blue-grey
            // network whose colour followed the weather, while the decal's own start colour
            // measured fully saturated yellow throughout. Nothing in this mod could win that
            // argument, because the light is added after everything this mod controls.
            //
            // _LightingEnabled = 0 takes the decal out of that path entirely: albedo times the
            // creature's colour and nothing else. Less physically interesting, and the only way to
            // guarantee blood is the colour of blood on an install like that.
            if (mat.HasProperty("_LightingEnabled"))
                mat.SetFloat("_LightingEnabled", Plugin.GroundLighting.Value ? 1f : 0f);

            if (mat.HasProperty("_GlossyReflections"))
            {
                bool on = Plugin.Reflections.Value;
                mat.SetFloat("_GlossyReflections", on ? 1f : 0f);
                if (on) mat.DisableKeyword("_GLOSSYREFLECTIONS_OFF");
                else    mat.EnableKeyword("_GLOSSYREFLECTIONS_OFF");
            }

            if (Plugin.WetnessMap.Value && _gloss != null && mat.HasProperty("_MetallicGlossMap"))
            {
                mat.SetTexture("_MetallicGlossMap", _gloss);
                // Same as the normal map: the sampler is compiled out without the keyword.
                mat.EnableKeyword("_METALLICGLOSSMAP");
            }
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

            Texture2D spray = Load("splatter_spray_1024_rgba.png", "CarturBloodGroundLargeSpray", mipmap: true);
            _groundLargeAlt = Load("splatter_mist_1024_rgba.png", "CarturBloodGroundLargeAlt", mipmap: true);
            _groundSmall = Load("splatter_impact_512_rgba.png", "CarturBloodGroundSmall", mipmap: true);

            // Ground art only. The splash and droplet are drawn against the sky rather than
            // layered on terrain, and they were never rendered through the cutout path, so they
            // have nothing to compensate for.
            Solidify(spray);
            Solidify(_groundLargeAlt);
            Solidify(_groundSmall);

            // Spray on its own is almost all thin streak and bare canvas - no solid body to read
            // as a mark from a few steps away. Baking it together with the other two rather than
            // leaving it as a standalone option: the existing coin flip in AssignGroundVariant
            // still picks which large mark a given hit gets, but now BOTH outcomes carry spray
            // plus a companion instead of one outcome being spray alone.
            _groundLarge = Composite(spray, _groundSmall, "CarturBloodGroundLarge");
            _groundLargeAlt = Composite(spray, _groundLargeAlt, "CarturBloodGroundLargeAlt");
            _splash = Load("splash_1024_rgba.png", "CarturBloodSplashTex", mipmap: true);
            // Optional: there is no embedded copy yet, so this stays null until a file is dropped
            // in BepInEx/config. Absence is not an error.
            _droplet = Load("droplet_256_rgba.png", "CarturBloodDropletTex", mipmap: true, optional: true);
            _gloss = Load("splatter_spray_1024_gloss.png", "CarturBloodGlossTex", mipmap: true,
                          optional: true, linear: true);

            if (Plugin.GroundNormalMap.Value)
            {
                // linear: normal maps are direction data, not a picture. Loaded as sRGB, Unity
                // gamma-decodes every channel and the decoded values are no longer unit vectors.
                _groundLargeNormal = Load("splatter_spray_1024_normal.png", "CarturBloodGroundLargeN", mipmap: true, linear: true);
                _groundLargeAltNormal = Load("splatter_mist_1024_normal.png", "CarturBloodGroundLargeAltN", mipmap: true, linear: true);
                _groundSmallNormal = Load("splatter_impact_512_normal.png", "CarturBloodGroundSmallN", mipmap: true, linear: true);
            }
        }

        /// Puts the ground art onto the alpha convention the shader actually blends with, and
        /// applies the density multiplier while it is there.
        ///
        /// THE COLOUR FIX. Measured on mid-alpha pixels, where the two conventions differ:
        ///
        ///     vanilla brains 64  (splat_decal_blend's own texture)   ink/alpha 2.19
        ///     splatter_spray_1024                                    ink/alpha 1.00
        ///     splatter_mist_1024                                     ink/alpha 0.99
        ///     splatter_impact_512                                    ink/alpha 1.00
        ///
        /// Vanilla authors these STRAIGHT: RGB is white, the shape lives entirely in alpha, and
        /// ink/alpha lands near 1/alpha. This mod's art is premultiplied - RGB equal to alpha -
        /// and splat_decal_blend blends _SrcBlend 5 / _DstBlend 10, which is straight alpha. So
        /// the colour was being multiplied by alpha TWICE:
        ///
        ///     result = (texRGB * startColor) * alpha + background * (1 - alpha)
        ///     texRGB = alpha   ->   startColor * alpha^2
        ///
        /// Greydwarf yellow (0.82, 0.80, 0.10) on grass at alpha 0.5 came out at brightness 0.29
        /// against vanilla's 0.39 - 36% dimmer - which is the "it used to be vibrant and show on
        /// grass, now it's dull" this was reported as. GroundOpacity made it worse rather than
        /// better: raising alpha while leaving RGB behind drops ink/alpha to 0.71, so the mark got
        /// denser and duller at the same time.
        ///
        /// The v1 rule that ground art must be premultiplied is not wrong, it is out of scope. It
        /// was derived when these decals were believed to render through a cutout path, where
        /// alpha is 0 or 1 and the two conventions are identical. Under Fade they are not.
        ///
        /// Nothing is lost by whitening. The art is greyscale - its RGB carries no information the
        /// alpha does not already hold - so this discards a duplicate, not a detail. Alpha is
        /// untouched apart from the density multiplier, so silhouette, coverage and the fade-out
        /// are all exactly as before.
        private static void Solidify(Texture2D tex)
        {
            float mul = BloodPreset.Current().Opacity;
            // Skips only when the multiplier is exactly 1, not when it is at or below 1. The
            // earlier "<= 1.001f" silently ignored every value under 1, which would have made
            // the setting's lower half do nothing at all once the range was widened to allow it.
            if (tex == null || Mathf.Abs(mul - 1f) < 0.001f)
                return;

            try
            {
                // REVERTED to premultiplied. Whitening the albedo is correct in isolation -
                // vanilla's brains measures a flat 255 at solid pixels - but in game it made
                // ground blood render as a pale blue-white network tracing the normal map's
                // ridges instead of a coloured mark.
                //
                // Same mechanism as the earlier magenta: outdoor ambient light in this game is
                // blue and is ADDED rather than multiplied by albedo, so doubling the albedo
                // doubled how much of that blue survives. Vanilla gets away with flat white
                // because brains is 64x64 and soft, with no 1024 normal map underneath it for
                // the ambient to catch. Our art has one on every tendril.
                //
                // The alpha multiplier below is all this does now, exactly as before.
                Color[] px = tex.GetPixels();
                for (int i = 0; i < px.Length; i++)
                    px[i].a = Mathf.Clamp01(px[i].a * mul);
                tex.SetPixels(px);
                tex.Apply(updateMipmaps: true);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not rewrite \"{tex.name}\" to straight alpha: {e.Message}");
            }
        }

        /// Lays `overlay` under `basePattern` into one new texture. Sampled by UV rather than by
        /// pixel index, so `overlay` can be a different resolution than `basePattern` - impact is
        /// 512, spray and mist are both 1024 - with no separate resize step.
        ///
        /// Alpha screen-combines (coverage only grows, never cancels) and colour takes whichever
        /// side is more opaque at that pixel, rather than averaging the two into a flat wash.
        /// Cheap, and the blend already goes through the same ground-fade material as a plain
        /// mark, so a true alpha composite would not read any differently once decaled.
        private static Texture2D Composite(Texture2D basePattern, Texture2D overlay, string texName)
        {
            if (basePattern == null || overlay == null)
                return basePattern;

            try
            {
                int w = basePattern.width, h = basePattern.height;
                Color[] basePixels = basePattern.GetPixels();
                Color[] outPixels = new Color[basePixels.Length];

                for (int y = 0; y < h; y++)
                {
                    float v = (y + 0.5f) / h;
                    for (int x = 0; x < w; x++)
                    {
                        Color a = basePixels[y * w + x];
                        Color b = overlay.GetPixelBilinear((x + 0.5f) / w, v);
                        float outA = 1f - (1f - a.a) * (1f - b.a);
                        Color rgb = a.a >= b.a ? a : b;
                        outPixels[y * w + x] = new Color(rgb.r, rgb.g, rgb.b, outA);
                    }
                }

                var result = new Texture2D(w, h, TextureFormat.RGBA32, mipChain: true)
                {
                    name = texName,
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                result.SetPixels(outPixels);
                result.Apply(updateMipmaps: true);
                return result;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not composite \"{texName}\": {e.Message}. Using " +
                                      $"\"{basePattern.name}\" alone.");
                return basePattern;
            }
        }

        /// Keeps the effect object alive long enough for its own decals to finish fading.
        ///
        /// Valheim destroys each spawned effect with TimedDestruction.m_timeout, authored to suit
        /// vanilla's particle lifetimes. This mod lengthens decal lifetime through GroundBlood
        /// without touching that timer, so on a long-lived mark the parent object is destroyed
        /// while the decal is still visible - and everything it holds vanishes in a single frame.
        /// That is the pop at the end of a fade: not the gradient, the object being deleted
        /// underneath it.
        ///
        /// Only ever extended, never shortened, so an effect the game already gives plenty of
        /// time keeps exactly what its author set.
        private static void ExtendEffectLifetime(ParticleDecal decal)
        {
            try
            {
                Transform root = decal.transform.root;
                if (root == null)
                    return;

                var timer = root.GetComponentInChildren<TimedDestruction>(true);
                if (timer == null)
                    return;

                float needed = 0f;
                foreach (ParticleSystem ps in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    if (ps == null) continue;
                    ParticleSystem.MainModule m = ps.main;
                    float life = m.startLifetime.mode == ParticleSystemCurveMode.TwoConstants
                        ? m.startLifetime.constantMax
                        : m.startLifetime.constant;
                    needed = Mathf.Max(needed, m.duration + life);
                }

                // A second of headroom, because the timer starts before the last particle is born.
                needed += 1f;

                if (timer.m_timeout < needed)
                    timer.m_timeout = needed;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not extend effect lifetime: " + e.Message);
            }
        }

        /// A file in BepInEx/config wins over the embedded asset, so the art can be swapped
        /// without a rebuild.
        private static Texture2D Load(string fileName, string texName, bool mipmap,
                                      bool optional = false, bool linear = false)
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
                            if (optional)
                            {
                                Plugin.Log.LogInfo(
                                    "No " + fileName + " yet - drop one in BepInEx/config as " +
                                    "carturblood_" + fileName + " to use it.");
                            }
                            else
                            {
                                Plugin.Log.LogError("Embedded texture missing: " + fileName);
                            }
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

            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, mipmap, linear)
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
