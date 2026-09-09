using System;
using System.Collections.Generic;
using UnityEngine;

namespace CarturHDBlood
{
    /// Copies the greydwarf death's blood cloud onto effects that have none.
    ///
    /// The visible splatter on a greydwarf kill is three `blood_cloud` particle systems firing
    /// together - soft cloud 30, splat 50, blobs 8; 88 particles at 0.5-1.5 world units. Nothing
    /// else in the effect accounts for it: the 200 `blood_drop` particles are 0.05 units each,
    /// a few screen pixels, and the ground decals are on the ground.
    ///
    /// It cannot be turned up on other effects, because on most of them there is nothing to turn
    /// up. Measured across the whole prefab list: 24 of 28 hit effects and 33 of 48 death effects
    /// have no cloud system at all. So the systems are copied.
    ///
    /// GRAFTED ONTO THE SPAWNED INSTANCE, NOT THE PREFAB. The first version added the copies to
    /// the prefabs at world load, and Unity refused every one of them:
    ///
    ///     Cannot instantiate objects with a parent which is persistent.
    ///     New object will be created without a parent.
    ///
    /// ZNetScene.m_prefabs holds persistent prefab assets, and Instantiate will not parent a new
    /// object to one. The copies were created parentless instead - loose in the scene at the
    /// origin, attached to nothing - so the graft reported success in the log while doing
    /// nothing at all. Working per instance is also what the rest of this mod already does, for
    /// the same underlying reason: the spawned instance is the only thing really there.
    ///
    /// Two rules keep this from becoming the mist explosion again:
    ///
    /// 1. Effects that already have a cloud are never touched. The 15 deaths that already carry
    ///    clouds include Bonemass at 250 particles and greydwarf elite at 110. Adding to those is
    ///    exactly how the explosion happened, and it is why this grafts rather than multiplies.
    /// 2. Hits are scaled down. A hit fires the graft alone; a death effect fires it against
    ///    everything else the effect already does.
    ///
    /// blood_cloud's texture is deliberately still untouched - vanilla already draws it at 1024,
    /// and it is what makes the greydwarf kill look the way it does.
    internal static class CloudGraft
    {
        /// The greydwarf death is the reference because it is the effect the look was asked for
        /// by name. Nothing here depends on it specifically - any prefab carrying blood_cloud
        /// systems would do - so if a game update renames it, the log says so and the graft
        /// no-ops rather than throwing.
        private const string SourceEffect = "vfx_greydwarf_death";

        private const string GraftPrefix = "CarturBloodCloud_";

        /// Read-only references into the source prefab. Never instantiated at load - see the
        /// class comment for what happened when they were.
        private static List<ParticleSystem> _source;

        /// The authored ground-decal size of the SOURCE creature, read from the source prefab.
        ///
        /// The copied clouds are sized for a greydwarf. Handing a chicken and a troll the same
        /// burst contradicts the rule the rest of this mod follows: vanilla authors decals per
        /// creature on purpose - 1..1.5 on a greyling, 4..6 on a troll - and BloodSkin's own
        /// header says absolute overrides "would flatten that design into one size for
        /// everything". Read rather than hardcoded, so it stays correct if the source changes.
        private static float _sourceDecalSize = 1f;

        /// Effect instances already grafted.
        ///
        /// One graft per effect, not one per decal: several effects carry more than one
        /// ParticleDecal - vfx_neck_death and vfx_troll_death have two, fx_deatsquito_death has
        /// three - and this runs from each decal's Awake. Keyed on the spawned effect's root,
        /// which the live probe confirms is the effect itself (logged as
        /// `vfx_boar_hit/bloodchunks`) and not the creature - so a second hit on the same boar is
        /// a new root and grafts again, rather than being deduped away.
        private static readonly HashSet<int> Grafted = new HashSet<int>();

        /// Finds the source systems at world load. Re-read each time, so editing the scales from
        /// the main menu takes effect on the next world load - Config.Reload() runs in the same
        /// postfix, just before this.
        public static void Cache(ZNetScene scene)
        {
            _source = null;
            Grafted.Clear();

            if (scene == null || !Plugin.ModEnabled.Value)
                return;

            float hit = Mathf.Max(0f, Plugin.HitBlood.Value);
            float death = Mathf.Max(0f, Plugin.DeathBlood.Value);
            if (hit <= 0f && death <= 0f)
            {
                Plugin.Log.LogInfo("Cloud graft off (both scales are 0).");
                return;
            }

            GameObject src = null;
            foreach (GameObject prefab in scene.m_prefabs)
            {
                if (prefab != null && prefab.name == SourceEffect)
                {
                    src = prefab;
                    break;
                }
            }

            if (src == null)
            {
                Plugin.Log.LogWarning(
                    $"Cloud graft: \"{SourceEffect}\" not found, so there is nothing to copy from. " +
                    "Nothing else is affected. If the game renamed it, point SourceEffect at any " +
                    "prefab carrying blood_cloud systems.");
                return;
            }

            // Found by material rather than by the child names ("soft cloud", "blobs", "splat"),
            // which are the sort of thing an update renames without anyone noticing.
            var systems = new List<ParticleSystem>();
            foreach (ParticleSystemRenderer r in src.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                if (r == null || !IsCloud(r.sharedMaterial))
                    continue;
                ParticleSystem ps = r.GetComponent<ParticleSystem>();
                // Emission off means the system is driven by something else, and copying it would
                // graft a burst that never fires.
                if (ps != null && ps.emission.enabled)
                    systems.Add(ps);
            }

            if (systems.Count == 0)
            {
                Plugin.Log.LogWarning(
                    $"Cloud graft: \"{SourceEffect}\" has no live blood_cloud systems. Nothing copied.");
                return;
            }

            _source = systems;

            Plugin.Log.LogInfo(
                $"Cloud graft armed: {systems.Count} system(s) from \"{SourceEffect}\", " +
                $"hits at {hit:0.##}x, deaths at {death:0.##}x. Applied per spawned effect.");
        }

        /// Grafts onto one spawned effect. Called from a decal's Awake, which is the earliest
        /// point at which the effect exists as a real scene object.
        ///
        /// Deliberately silent. This runs per hit, and the log is read by people diagnosing
        /// crashes - the arming line above says everything the log needs to say.
        public static void Apply(ParticleDecal decal)
        {
            if (_source == null || decal == null || decal.m_decalSystem == null)
                return;

            try
            {
                Transform root = decal.transform.root;
                if (root == null)
                    return;

                string name = root.name;
                bool isHit = name.IndexOf("hit", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isDeath = name.IndexOf("death", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!isHit && !isDeath)
                    return;

                float scale = Mathf.Max(0f, isHit ? Plugin.HitBlood.Value
                                                  : Plugin.DeathBlood.Value);
                if (scale <= 0f)
                    return;

                if (!Grafted.Add(root.gameObject.GetInstanceID()))
                    return;

                if (HasCloud(root))
                    return;

                // The creature's own blood colour, off the decal that brought us here. Without
                // this every graft would carry greydwarf's startColor, and greydwarf blood is
                // yellow - measured, RGBA(0.868, 0.700, 0.000) - so boars would bleed yellow.
                ParticleSystem.MinMaxGradient tint = decal.m_decalSystem.main.startColor;

                // How big this creature's blood is, relative to the creature the cloud was
                // copied from. Clamped: a chicken should still be visible and a dragon should
                // not fill the screen, and vanilla decal sizes span 0.5 to 8.
                float sizeScale = Mathf.Clamp(
                    BloodSkin.AuthoredSize(decal.m_decalSystem.main.startSize) / _sourceDecalSize,
                    0.4f, 2.5f);

                foreach (ParticleSystem src in _source)
                    Graft(src, root, scale, sizeScale, tint);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Cloud graft failed: " + e.Message);
            }
        }

        /// Mean authored decal size across a prefab's blood decals, or `fallback` if it has none.
        private static float ReadDecalSize(GameObject prefab, float fallback)
        {
            float total = 0f;
            int n = 0;
            foreach (ParticleDecal d in prefab.GetComponentsInChildren<ParticleDecal>(true))
            {
                if (d == null || d.m_decalSystem == null)
                    continue;
                var r = d.m_decalSystem.GetComponent<ParticleSystemRenderer>();
                if (r == null || r.sharedMaterial == null || !BloodSkin.IsBloodMaterial(r.sharedMaterial))
                    continue;
                total += BloodSkin.AuthoredSize(d.m_decalSystem.main.startSize);
                n++;
            }
            return n == 0 ? fallback : Mathf.Max(0.01f, total / n);
        }

        private static bool IsCloud(Material mat) =>
            mat != null && mat.name != null
                        && mat.name.StartsWith("blood_cloud", StringComparison.OrdinalIgnoreCase);

        /// Whether this effect already produces cloud of its own. Our own grafts are skipped, so
        /// the test answers "did the game author one", not "have we been here already".
        private static bool HasCloud(Transform root)
        {
            foreach (ParticleSystemRenderer r in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                if (r == null || !IsCloud(r.sharedMaterial))
                    continue;
                if (r.name.StartsWith(GraftPrefix, StringComparison.Ordinal))
                    continue;
                ParticleSystem ps = r.GetComponent<ParticleSystem>();
                if (ps != null && ps.emission.enabled)
                    return true;
            }
            return false;
        }

        /// Copies one system under a spawned effect and scales its bursts.
        ///
        /// Instantiate is called with NO parent and the parent set afterwards. Passing the parent
        /// straight to Instantiate is exactly what failed against prefabs; here the target is a
        /// scene object so either form would work, but keeping them apart makes the reason
        /// visible to whoever reads this next.
        ///
        /// Position is zeroed rather than inherited: the source sits wherever it sits inside the
        /// greydwarf effect, which means nothing under a different parent, and the effect is
        /// already spawned at the wound. Rotation and scale are kept, because scale drives
        /// particle size in Local scaling mode and dropping it would change the look.
        /// Copies one system under a spawned effect, neutralises everything that could override
        /// the creature's own blood colour, and scales its bursts.
        ///
        /// THREE things decide a particle's colour, and setting startColor only controls one:
        ///
        /// 1. `main.startColor` - what we want to be in charge.
        /// 2. `colorOverLifetime` - multiplies it. Disabled on the copy.
        /// 3. The shared material's `_Color` and `_EmissionColor` - and `_EmissionColor` is
        ///    ADDITIVE, so a yellow emission paints every particle yellow no matter what the
        ///    particle colour says.
        ///
        /// The source is the greydwarf death effect and greydwarf blood is YELLOW, so anything
        /// yellow baked into 2 or 3 travels with the copy onto every creature - which is exactly
        /// what happened: boars bled yellow. This mod has already been bitten by this once, by
        /// `slime_green` in ReplaceSlimeSplash, and the fix is the same: clone the material and
        /// neutralise it rather than touching the shared one, so the effects that legitimately
        /// use it are unaffected.
        ///
        /// Instantiate is called with NO parent and the parent set afterwards. Passing the parent
        /// straight to Instantiate is what failed against prefabs; here the target is a scene
        /// object so either form works, but keeping them apart makes the reason visible.
        ///
        /// Position is zeroed rather than inherited: the source sits wherever it sits inside the
        /// greydwarf effect, which means nothing under a different parent, and the effect is
        /// already spawned at the wound. Rotation and scale are kept, because scale drives
        /// particle size in Local scaling mode and dropping it would change the look.
        private static void Graft(ParticleSystem source, Transform parent, float scale,
                                  float sizeScale, ParticleSystem.MinMaxGradient tint)
        {
            if (source == null)
                return;

            GameObject copy = UnityEngine.Object.Instantiate(source.gameObject);
            copy.name = GraftPrefix + source.gameObject.name;

            ParticleSystem ps = copy.GetComponent<ParticleSystem>();
            if (ps == null)
            {
                UnityEngine.Object.Destroy(copy);
                return;
            }

            // Instantiating an active object runs its Awake, so a playOnAwake system starts
            // before any of the configuration below has happened. Clearing it means the burst
            // that actually reaches the screen is the one emitted after Play() at the bottom.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            // Keep the particles, drop everything else that rode along.
            //
            // Instantiate copies EVERY component, and only the particle system was ever wanted.
            // The measured instance: greydwarf's "blobs" and "splat" objects carry ParticleDecal
            // components, so each graft installed a second, greydwarf-yellow GROUND DECAL emitter
            // on the target creature - boars got yellow marks beside their own red ones:
            //
            //   ParticleDecal LIVE at vfx_boar_death/CarturBloodCloud_blobs
            //     decal startColor = RGBA(0.868, 0.700, 0.000)   <- greydwarf yellow
            //   ParticleDecal LIVE at vfx_boar_death/vfx_BloodHit 1
            //     decal startColor = RGBA(0.868, 0.006, 0.006)   <- the boar's own, correct
            //
            // A whitelist rather than a list of known stowaways, because the bug was not
            // "ParticleDecal came along", it was "components I never thought about came along" -
            // and there is no reason to believe ParticleDecal was the only one. The graft wants
            // particles and nothing else, so that is what it says.
            StripToParticlesOnly(copy);

            copy.transform.SetParent(parent, false);
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = source.transform.localRotation;
            copy.transform.localScale = source.transform.localScale;

            ParticleSystem.MainModule main = ps.main;
            main.startColor = tint;

            // Particle size follows the creature, not the creature we copied from.
            if (Math.Abs(sizeScale - 1f) > 0.001f)
                main.startSize = BloodSkin.Scale(main.startSize, sizeScale);

            // Multiplies startColor, and carries the source creature's colour with it.
            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = false;

            var r = copy.GetComponent<ParticleSystemRenderer>();
            if (r != null)
                r.sharedMaterial = NeutralCloudMaterial(r.sharedMaterial);

            if (Math.Abs(scale - 1f) > 0.001f)
            {
                ParticleSystem.EmissionModule em = ps.emission;
                for (int i = 0; i < em.burstCount; i++)
                {
                    ParticleSystem.Burst b = em.GetBurst(i);
                    // Through BloodSkin.Scale so the curve mode survives - reading .constant off
                    // a TwoConstants burst silently discards the authored range, a mistake this
                    // project has already had to fix once for decal sizes.
                    b.count = BloodSkin.Scale(b.count, scale);
                    em.SetBurst(i, b);
                }
            }

            Ballistics(ps);

            ps.Play(true);
        }

        /// Makes the grafted blood arc and fall instead of drifting in a straight line.
        ///
        /// Vanilla leaves gravityModifier at zero on these systems, so the particles travel at a
        /// constant speed in whatever direction they were emitted until their lifetime expires.
        /// That is the single most artificial thing about airborne blood: liquid thrown from a
        /// wound follows a ballistic arc and drops.
        ///
        /// Drag is applied alongside, scaled by particle size AND velocity, because that is what
        /// separates mist from chunks inside one system: air resistance barely troubles a heavy
        /// droplet but slows atomised mist quickly, so the fine stuff hangs and drifts while the
        /// big stuff carries. One setting, two behaviours, no second particle system.
        ///
        /// Only applied where the source left gravity unset. A system authored with its own
        /// gravity was tuned that way deliberately and is left alone.
        private static void Ballistics(ParticleSystem ps)
        {
            ParticleSystem.MainModule main = ps.main;

            if (Mathf.Abs(main.gravityModifier.constant) < 0.001f &&
                Mathf.Abs(main.gravityModifier.constantMax) < 0.001f)
            {
                // A range rather than one value, so particles in the same burst separate on the
                // way down instead of falling as a sheet.
                main.gravityModifier = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
            }

            ParticleSystem.LimitVelocityOverLifetimeModule lim = ps.limitVelocityOverLifetime;
            lim.enabled = true;
            lim.dampen = 0f;
            lim.drag = new ParticleSystem.MinMaxCurve(0.06f);
            lim.multiplyDragByParticleSize = true;
            lim.multiplyDragByParticleVelocity = true;
        }

        /// Everything a copied cloud system needs, and nothing else.
        ///
        /// Uses Destroy, NOT DestroyImmediate. This runs from ParticleDecal.Awake, inside the
        /// effect's instantiation, and Unity forbids immediate destruction there:
        ///
        ///     Destroying components immediately is not permitted during physics
        ///     trigger/contact, animation event callbacks, rendering callbacks or
        ///     OnValidate. You must use Destroy instead.
        ///
        /// It reports that as a logged ERROR rather than an exception, so a try/catch around it
        /// catches nothing and the components quietly survive - which is exactly what happened:
        /// the strip logged success while every ParticleDecal stayed put and kept painting
        /// greydwarf-yellow marks. A caught exception is not proof a thing worked.
        ///
        /// Destroy is deferred to the end of the frame, so each component is also DISABLED here.
        /// That is what actually stops it acting in the meantime; the destruction is only
        /// housekeeping.
        private static void StripToParticlesOnly(GameObject copy)
        {
            foreach (Transform t in copy.GetComponentsInChildren<Transform>(true))
            {
                if (t == null)
                    continue;

                foreach (Component c in t.GetComponents<Component>())
                {
                    if (c == null || c is Transform || c is ParticleSystem
                                  || c is ParticleSystemRenderer)
                        continue;

                    // Behaviour covers MonoBehaviour, so ParticleDecal lands here. Disabling is
                    // the part that takes effect immediately.
                    var behaviour = c as Behaviour;
                    if (behaviour != null)
                        behaviour.enabled = false;

                    if (_strippedTypes.Add(c.GetType().Name))
                        _strippedChanged = true;

                    UnityEngine.Object.Destroy(c);
                }
            }

            // Once ever, not per graft: the log is read by people diagnosing crashes and this
            // runs on every hit. But if a future game update puts something new on those
            // objects, this line is what makes it visible instead of silent.
            if (_strippedChanged)
            {
                _strippedChanged = false;
                Plugin.Log.LogInfo("Cloud graft disables and removes these components from its "
                                   + "copies: " + string.Join(", ", new List<string>(_strippedTypes).ToArray()));
            }
        }

        private static readonly HashSet<string> _strippedTypes = new HashSet<string>();
        private static bool _strippedChanged;

        // One clone per source material, built once. Keyed because there is no guarantee the
        // three source systems share a material, even though today they do.
        private static readonly Dictionary<int, Material> NeutralMaterials = new Dictionary<int, Material>();

        /// A copy of the cloud material with its own colour taken out, so the particle's
        /// startColor is the only thing deciding what colour the blood is.
        ///
        /// Cloned rather than modified: `blood_cloud` is shared by 52 renderer slots including
        /// the 15 effects that legitimately have their own clouds, and recolouring it would
        /// change all of them.
        private static Material NeutralCloudMaterial(Material source)
        {
            if (source == null)
                return null;

            int id = source.GetInstanceID();
            if (NeutralMaterials.TryGetValue(id, out Material cached) && cached != null)
                return cached;

            try
            {
                var clone = new Material(source) { name = "CarturBloodCloudNeutral" };
                if (clone.HasProperty("_Color"))
                    clone.SetColor("_Color", Color.white);
                // Additive, so this is the one that can paint yellow over any tint.
                if (clone.HasProperty("_EmissionColor"))
                    clone.SetColor("_EmissionColor", Color.black);

                NeutralMaterials[id] = clone;
                Plugin.Log.LogInfo(
                    $"Cloned \"{source.name}\" -> \"CarturBloodCloudNeutral\" (_Color white, " +
                    "_EmissionColor black) so grafted clouds take the creature's own blood colour. " +
                    "The original is left alone, so effects with their own clouds are unaffected.");
                return clone;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not neutralise cloud material: " + e.Message);
                return source;
            }
        }

    }
}
