using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace CarturHDBlood
{
    /// Reports what the vanilla blood effects actually are, so the replacement work isn't built
    /// on guesses. Reads only - nothing here modifies the game.
    ///
    /// Three questions it exists to answer:
    ///
    ///  1. Is Texture Sheet Animation enabled? If it is, a single splat image breaks the
    ///     animation and we need a flipbook grid of matching cells instead. Everything about
    ///     what art to prepare hangs on this.
    ///  2. Which material carries the ground decal? ParticleDecal.m_decalSystem is a SEPARATE
    ///     ParticleSystem from the spray that feeds it, so there are two materials per effect
    ///     and only one of them is the flat mark left on the ground.
    ///  3. Are those materials shared between prefabs? A shared material means one swap changes
    ///     every effect using it - convenient, but it also means we can't give the hit spray and
    ///     the death spray different art without instancing first.
    ///
    /// It also records the dials worth tuning: m_chance, decal start size, decal lifetime.
    internal static class BloodProbe
    {
        private static bool _ran;

        // instance ID -> every prefab path that renders with this material, for the sharing check
        private static readonly Dictionary<int, List<string>> MaterialUsers = new Dictionary<int, List<string>>();

        public static void Run(ZNetScene scene)
        {
            if (_ran || scene == null)
                return;
            _ran = true;

            var sb = new StringBuilder();
            try
            {
                Collect(scene, sb);
            }
            catch (Exception e)
            {
                sb.AppendLine("PROBE FAILED: " + e);
                Plugin.Log.LogError("Blood probe failed: " + e);
            }

            string path = Path.Combine(Plugin.ConfigDir ?? ".", "carturblood_probe.txt");
            try
            {
                File.WriteAllText(path, sb.ToString());
                Plugin.Log.LogInfo("Blood probe written to " + path);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not write probe file: " + e.Message);
                // Still get the findings out somehow.
                foreach (string line in sb.ToString().Split('\n'))
                    Plugin.Log.LogInfo(line.TrimEnd());
            }
        }

        private static void Collect(ZNetScene scene, StringBuilder sb)
        {
            sb.AppendLine("Cartur's HD Blood - vanilla blood probe");
            sb.AppendLine("generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();

            // Gather the distinct blood effect prefabs. Two routes, because neither alone is
            // guaranteed to be complete: the EffectLists are authoritative for what actually
            // plays on a hit or death, while the name scan catches anything registered directly.
            var found = new Dictionary<int, GameObject>();
            var reachedFrom = new Dictionary<int, List<string>>();
            var sizes = new Dictionary<string, float>();

            int characters = 0;
            foreach (GameObject prefab in scene.m_prefabs)
            {
                if (prefab == null)
                    continue;

                if (prefab.name.IndexOf("blood", StringComparison.OrdinalIgnoreCase) >= 0)
                    Note(found, reachedFrom, prefab, "ZNetScene.m_prefabs (by name)");

                Character character = prefab.GetComponent<Character>();
                if (character == null)
                    continue;
                characters++;

                // Character.GetHeight() is Mathf.Max(m_collider.height, m_collider.radius * 2) and
                // applies no scale, so reading the collider straight off the prefab gives exactly
                // the number the live call would - without having to kill one of everything first.
                // m_collider itself is only assigned in Awake, which has not run on a prefab.
                CapsuleCollider capsule = prefab.GetComponent<CapsuleCollider>();
                if (capsule != null)
                    sizes[prefab.name] = Mathf.Max(capsule.height, capsule.radius * 2f);

                Sweep(character.m_hitEffects, prefab.name + ".m_hitEffects", found, reachedFrom);
                Sweep(character.m_critHitEffects, prefab.name + ".m_critHitEffects", found, reachedFrom);
                Sweep(character.m_backstabHitEffects, prefab.name + ".m_backstabHitEffects", found, reachedFrom);
                Sweep(character.m_deathEffects, prefab.name + ".m_deathEffects", found, reachedFrom);
            }

            sb.AppendLine($"scanned {scene.m_prefabs.Count} prefabs, {characters} with a Character component");
            sb.AppendLine($"distinct blood effect prefabs: {found.Count}");
            sb.AppendLine();

            // Every creature's height and the death-pool size it will get, without needing to have
            // killed one. This is the table PoolSize is calibrated against.
            sb.AppendLine("================================================================");
            sb.AppendLine("CREATURE HEIGHTS AND DEATH POOL SIZE");
            sb.AppendLine($"  reference height {CreatureSize.ReferenceHeight:0.##}m = scale 1.00, " +
                          $"clamped {CreatureSize.MinScale:0.##}..{CreatureSize.MaxScale:0.##}");
            sb.AppendLine($"  PoolSize is currently {Plugin.PoolSize.Value:0.###}");
            sb.AppendLine();
            sb.AppendLine(string.Format("  {0,-34}{1,9}{2,8}{3,11}", "creature", "height", "scale", "pool"));
            foreach (var kv in sizes.OrderBy(k => k.Value))
            {
                float scale = CreatureSize.ScaleFor(kv.Value);
                sb.AppendLine(string.Format("  {0,-34}{1,8:0.##}m{2,8:0.00}{3,11:0.00}",
                                            kv.Key, kv.Value, scale, Plugin.PoolSize.Value * scale));
            }
            sb.AppendLine();

            foreach (var kv in found)
            {
                GameObject prefab = kv.Value;
                sb.AppendLine("================================================================");
                sb.AppendLine("PREFAB: " + prefab.name);
                if (reachedFrom.TryGetValue(kv.Key, out List<string> refs))
                {
                    // Capped: a common effect is referenced by dozens of creatures and the full
                    // list would bury the part that matters.
                    int show = Math.Min(refs.Count, 8);
                    sb.AppendLine($"  referenced by {refs.Count} source(s), first {show}:");
                    for (int i = 0; i < show; i++)
                        sb.AppendLine("    " + refs[i]);
                }
                sb.AppendLine();
                DumpTree(prefab.transform, prefab.name, sb, 1);
                sb.AppendLine();
            }

            sb.AppendLine("================================================================");
            sb.AppendLine("MATERIAL SHARING");
            sb.AppendLine("  (a material listed against more than one path is shared - swapping it");
            sb.AppendLine("   changes every one of them unless the material is instanced first)");
            sb.AppendLine();
            foreach (var kv in MaterialUsers)
            {
                if (kv.Value.Count < 2)
                    continue;
                sb.AppendLine($"  SHARED by {kv.Value.Count}:");
                foreach (string user in kv.Value)
                    sb.AppendLine("    " + user);
            }
            sb.AppendLine();
            sb.AppendLine("  materials seen in total: " + MaterialUsers.Count);
        }

        private static void Sweep(EffectList list, string source, Dictionary<int, GameObject> found,
            Dictionary<int, List<string>> reachedFrom)
        {
            if (list?.m_effectPrefabs == null)
                return;

            foreach (EffectList.EffectData data in list.m_effectPrefabs)
            {
                GameObject prefab = data?.m_prefab;
                if (prefab == null)
                    continue;
                if (prefab.name.IndexOf("blood", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                Note(found, reachedFrom, prefab, source);
            }
        }

        private static void Note(Dictionary<int, GameObject> found, Dictionary<int, List<string>> reachedFrom,
            GameObject prefab, string source)
        {
            int id = prefab.GetInstanceID();
            if (!found.ContainsKey(id))
                found[id] = prefab;
            if (!reachedFrom.TryGetValue(id, out List<string> list))
            {
                list = new List<string>();
                reachedFrom[id] = list;
            }
            list.Add(source);
        }

        private static void DumpTree(Transform t, string path, StringBuilder sb, int depth)
        {
            string pad = new string(' ', depth * 2);
            sb.AppendLine($"{pad}[{t.name}]  localScale={Fmt(t.localScale)}");

            ParticleSystem ps = t.GetComponent<ParticleSystem>();
            if (ps != null)
                DumpParticleSystem(ps, path, sb, depth + 1);

            ParticleDecal decal = t.GetComponent<ParticleDecal>();
            if (decal != null)
            {
                sb.AppendLine($"{pad}  ParticleDecal:");
                sb.AppendLine($"{pad}    m_chance = {decal.m_chance}   <-- decals per collision, 0-100");
                sb.AppendLine($"{pad}    m_decalSystem = " +
                              (decal.m_decalSystem == null ? "NULL" : PathOf(decal.m_decalSystem.transform)) +
                              "   <-- THIS system's material is the ground mark");
            }

            // Anything else attached is worth knowing about; some of these effects carry lights,
            // audio, or a Projector-style decal instead of a particle decal.
            foreach (Component c in t.GetComponents<Component>())
            {
                if (c == null)
                    continue;
                string n = c.GetType().Name;
                if (n == "Transform" || n == "ParticleSystem" || n == "ParticleSystemRenderer" || n == "ParticleDecal")
                    continue;
                sb.AppendLine($"{pad}  + {n}");
            }

            for (int i = 0; i < t.childCount; i++)
                DumpTree(t.GetChild(i), path, sb, depth + 1);
        }

        private static void DumpParticleSystem(ParticleSystem ps, string prefabPath, StringBuilder sb, int depth)
        {
            string pad = new string(' ', depth * 2);
            sb.AppendLine($"{pad}ParticleSystem:");

            try
            {
                ParticleSystem.MainModule main = ps.main;
                sb.AppendLine($"{pad}  duration={main.duration} looping={main.loop} maxParticles={main.maxParticles}");
                sb.AppendLine($"{pad}  startSize     = {Fmt(main.startSize)}      <-- decal size, if this is the decal system");
                sb.AppendLine($"{pad}  startLifetime = {Fmt(main.startLifetime)}  <-- how long a decal lingers");
                sb.AppendLine($"{pad}  startSpeed    = {Fmt(main.startSpeed)}");
                sb.AppendLine($"{pad}  startColor    = {FmtGradient(main.startColor)}");
                sb.AppendLine($"{pad}  simulationSpace={main.simulationSpace} scalingMode={main.scalingMode}");
            }
            catch (Exception e) { sb.AppendLine($"{pad}  main: FAILED {e.Message}"); }

            try
            {
                ParticleSystem.EmissionModule em = ps.emission;
                sb.AppendLine($"{pad}  emission: enabled={em.enabled} rateOverTime={Fmt(em.rateOverTime)} bursts={em.burstCount}");
                for (int i = 0; i < em.burstCount; i++)
                {
                    ParticleSystem.Burst b = em.GetBurst(i);
                    sb.AppendLine($"{pad}    burst[{i}] count={Fmt(b.count)} time={b.time} cycles={b.cycleCount} prob={b.probability}");
                }
            }
            catch (Exception e) { sb.AppendLine($"{pad}  emission: FAILED {e.Message}"); }

            // The headline question.
            try
            {
                ParticleSystem.TextureSheetAnimationModule tsa = ps.textureSheetAnimation;
                sb.AppendLine($"{pad}  textureSheetAnimation: ENABLED={tsa.enabled}   <-- ANSWERS single-image vs flipbook");
                if (tsa.enabled)
                {
                    sb.AppendLine($"{pad}    numTilesX={tsa.numTilesX} numTilesY={tsa.numTilesY} animation={tsa.animation}");
                    sb.AppendLine($"{pad}    cycleCount={tsa.cycleCount} frameOverTime={Fmt(tsa.frameOverTime)} startFrame={Fmt(tsa.startFrame)}");
                    sb.AppendLine($"{pad}    => art must be a {tsa.numTilesX}x{tsa.numTilesY} grid of equal cells");
                }
            }
            catch (Exception e) { sb.AppendLine($"{pad}  textureSheetAnimation: FAILED {e.Message}"); }

            try
            {
                ParticleSystem.CollisionModule col = ps.collision;
                // sendCollisionMessages is what makes ParticleDecal.OnParticleCollision fire at all.
                sb.AppendLine($"{pad}  collision: enabled={col.enabled} type={col.type} mode={col.mode} " +
                              $"sendCollisionMessages={col.sendCollisionMessages} lifetimeLoss={col.lifetimeLoss}");
            }
            catch (Exception e) { sb.AppendLine($"{pad}  collision: FAILED {e.Message}"); }

            ParticleSystemRenderer r = ps.GetComponent<ParticleSystemRenderer>();
            if (r == null)
            {
                sb.AppendLine($"{pad}  (no ParticleSystemRenderer)");
                return;
            }

            try
            {
                sb.AppendLine($"{pad}  renderer: mode={r.renderMode} alignment={r.alignment} sortMode={r.sortMode}");
            }
            catch (Exception e) { sb.AppendLine($"{pad}  renderer: FAILED {e.Message}"); }

            Material[] mats;
            try { mats = r.sharedMaterials; }
            catch { mats = new Material[0]; }

            for (int i = 0; i < mats.Length; i++)
                DumpMaterial(mats[i], prefabPath + "/" + PathOf(ps.transform) + $" [mat{i}]", sb, depth + 1);
        }

        private static void DumpMaterial(Material m, string user, StringBuilder sb, int depth)
        {
            string pad = new string(' ', depth * 2);
            if (m == null)
            {
                sb.AppendLine($"{pad}material: NULL");
                return;
            }

            int id = m.GetInstanceID();
            if (!MaterialUsers.TryGetValue(id, out List<string> users))
            {
                users = new List<string>();
                MaterialUsers[id] = users;
            }
            users.Add(user);

            // The id is what the live probe compares against, to prove the material a real
            // in-game decal renders with is the same object as the one on the prefab.
            sb.AppendLine($"{pad}material: \"{m.name}\" id={id}  shader=\"{(m.shader == null ? "NULL" : m.shader.name)}\"");
            sb.AppendLine($"{pad}  renderQueue={m.renderQueue}");

            // Enumerate the shader's texture slots properly where the API allows it, so we learn
            // the real property names rather than testing a guessed list.
            bool enumerated = false;
            try
            {
                if (m.shader != null)
                {
                    int count = m.shader.GetPropertyCount();
                    for (int i = 0; i < count; i++)
                    {
                        if (m.shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture)
                            continue;
                        enumerated = true;
                        string name = m.shader.GetPropertyName(i);
                        DumpTexture(m, name, sb, depth + 1);
                    }
                }
            }
            catch (Exception e)
            {
                sb.AppendLine($"{pad}  (shader property enumeration unavailable: {e.Message})");
            }

            if (!enumerated)
            {
                // Fallback for when the reflection-free enumeration isn't there.
                foreach (string name in new[] { "_MainTex", "_MaskTex", "_BumpMap", "_EmissionMap", "_Splat", "_Texture" })
                {
                    if (m.HasProperty(name))
                        DumpTexture(m, name, sb, depth + 1);
                }
            }

            // Colour/tint properties decide whether we can ship greyscale art and tint in engine.
            foreach (string name in new[] { "_Color", "_TintColor", "_EmissionColor" })
            {
                if (m.HasProperty(name))
                    sb.AppendLine($"{pad}  {name} = {m.GetColor(name)}");
            }
        }

        private static void DumpTexture(Material m, string prop, StringBuilder sb, int depth)
        {
            string pad = new string(' ', depth * 2);
            Texture tex = null;
            try { tex = m.GetTexture(prop); }
            catch { }

            if (tex == null)
            {
                sb.AppendLine($"{pad}{prop} = (none)");
                return;
            }

            var t2d = tex as Texture2D;
            string extra = t2d == null ? tex.GetType().Name : $"Texture2D format={t2d.format} mips={t2d.mipmapCount}";
            sb.AppendLine($"{pad}{prop} = \"{tex.name}\" {tex.width}x{tex.height} {extra}");
        }

        private static string PathOf(Transform t)
        {
            var parts = new List<string>();
            while (t != null)
            {
                parts.Insert(0, t.name);
                t = t.parent;
            }
            return string.Join("/", parts.ToArray());
        }

        private static string Fmt(Vector3 v) =>
            string.Format(CultureInfo.InvariantCulture, "({0:0.###}, {1:0.###}, {2:0.###})", v.x, v.y, v.z);

        private static string Fmt(ParticleSystem.MinMaxCurve c)
        {
            switch (c.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return c.constant.ToString("0.###", CultureInfo.InvariantCulture);
                case ParticleSystemCurveMode.TwoConstants:
                    return string.Format(CultureInfo.InvariantCulture, "{0:0.###}..{1:0.###}", c.constantMin, c.constantMax);
                default:
                    return string.Format(CultureInfo.InvariantCulture, "{0} (curve, multiplier {1:0.###})", c.mode, c.curveMultiplier);
            }
        }

        private static string FmtGradient(ParticleSystem.MinMaxGradient g)
        {
            try
            {
                if (g.mode == ParticleSystemGradientMode.Color)
                    return g.color.ToString();
                if (g.mode == ParticleSystemGradientMode.TwoColors)
                    return g.colorMin + ".." + g.colorMax;
                return g.mode.ToString();
            }
            catch { return "?"; }
        }
    }
}
