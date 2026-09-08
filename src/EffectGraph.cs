using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace CarturHDBlood
{
    /// Answers, without needing to play: who actually spawns the blood effects?
    ///
    /// The first probe only checked four EffectList fields on Character and found nothing, which
    /// proved only that Character isn't the referrer. Blood can be reached from many other
    /// places - Ragdoll, a projectile's hit effect, a weapon's m_shared.m_hitEffect, a
    /// SpawnOnHit - and an EffectList is a plain serializable class, so some of those live
    /// nested inside other objects rather than directly on a component.
    ///
    /// So this walks every prefab, every component, and recurses a bounded depth through plain
    /// serializable fields looking for EffectList anywhere, then reports every reference to a
    /// blood prefab.
    ///
    /// It also lists every prefab with a ParticleDecal anywhere in its hierarchy. That settles
    /// the question the dials depend on: if the only owners are the three standalone blood
    /// prefabs, then editing those prefabs is enough. If other prefabs carry their own nested
    /// copies, each copy is a separate component instance and would need editing too.
    internal static class EffectGraph
    {
        private const int MaxDepth = 4;

        // Reflection over thousands of prefabs is only affordable with the field lists cached.
        private static readonly Dictionary<Type, FieldInfo[]> FieldCache = new Dictionary<Type, FieldInfo[]>();

        private static readonly List<string> Refs = new List<string>();
        private static readonly Dictionary<string, List<string>> DecalOwners = new Dictionary<string, List<string>>();

        public static void Run(ZNetScene scene)
        {
            if (scene == null)
                return;

            var sb = new StringBuilder();
            sb.AppendLine("Cartur's HD Blood - effect reference graph");
            sb.AppendLine("generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();

            int scanned = 0;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            foreach (GameObject prefab in scene.m_prefabs)
            {
                if (prefab == null)
                    continue;
                scanned++;

                // Who owns a ParticleDecal?
                ParticleDecal[] decals = prefab.GetComponentsInChildren<ParticleDecal>(true);
                if (decals.Length > 0)
                {
                    foreach (ParticleDecal d in decals)
                    {
                        string mat = MaterialOf(d);
                        if (!DecalOwners.TryGetValue(mat, out List<string> list))
                        {
                            list = new List<string>();
                            DecalOwners[mat] = list;
                        }
                        list.Add($"{prefab.name} :: {PathOf(d.transform)} (m_chance={d.m_chance})");
                    }
                }

                // Who references a blood prefab through any EffectList, however nested?
                foreach (Component c in prefab.GetComponentsInChildren<Component>(true))
                {
                    if (c == null)
                        continue;
                    var visited = new HashSet<object>(ReferenceComparer.Instance);
                    Walk(c, c.GetType().Name, prefab.name, visited, 0);
                }
            }

            sw.Stop();
            sb.AppendLine($"scanned {scanned} prefabs in {sw.ElapsedMilliseconds} ms");
            sb.AppendLine();

            sb.AppendLine("================================================================");
            sb.AppendLine("REFERENCES TO BLOOD EFFECT PREFABS");
            sb.AppendLine("  (owner prefab :: component.field -> effect prefab)");
            sb.AppendLine();
            if (Refs.Count == 0)
            {
                sb.AppendLine("  NONE. Nothing in any prefab references a blood-named effect through");
                sb.AppendLine("  an EffectList - so the blood must be spawned from code, or as an");
                sb.AppendLine("  already-parented child object rather than an instantiated effect.");
            }
            else
            {
                Refs.Sort(StringComparer.OrdinalIgnoreCase);
                foreach (string r in Refs)
                    sb.AppendLine("  " + r);
            }
            sb.AppendLine();

            sb.AppendLine("================================================================");
            sb.AppendLine("EVERY PREFAB CARRYING A ParticleDecal, GROUPED BY DECAL MATERIAL");
            sb.AppendLine("  (if a material's only owners are the standalone blood prefabs, then");
            sb.AppendLine("   editing those prefabs reaches every decal that uses it)");
            sb.AppendLine();
            foreach (var kv in DecalOwners)
            {
                sb.AppendLine($"  material {kv.Key}  -  {kv.Value.Count} owner(s):");
                foreach (string owner in kv.Value)
                    sb.AppendLine("      " + owner);
                sb.AppendLine();
            }

            sb.AppendLine("================================================================");
            sb.AppendLine("PARTICLE INVENTORY OF EVERY BLOOD EFFECT");
            sb.AppendLine("  Answers whether a per-creature hit effect has any airborne spray at");
            sb.AppendLine("  all, or only a ground decal. The earlier probes only ever dumped the");
            sb.AppendLine("  standalone vfx_BloodHit / vfx_BloodDeath prefabs, which turned out not");
            sb.AppendLine("  to be what creatures actually use - so this was never established.");
            sb.AppendLine();
            DumpBloodEffectParticles(scene, sb);

            string path = Path.Combine(Plugin.ConfigDir ?? ".", "carturblood_graph.txt");
            try
            {
                File.WriteAllText(path, sb.ToString());
                Plugin.Log.LogInfo("Effect graph written to " + path);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not write graph file: " + e.Message);
            }
        }

        /// For every prefab carrying a blood decal, list its particle systems: which are the decal
        /// systems and which are visible spray, with the burst count and size that decide whether
        /// anything is actually seen in the air.
        private static void DumpBloodEffectParticles(ZNetScene scene, StringBuilder sb)
        {
            foreach (GameObject prefab in scene.m_prefabs)
            {
                if (prefab == null)
                    continue;

                ParticleDecal[] decals = prefab.GetComponentsInChildren<ParticleDecal>(true);
                if (decals.Length == 0)
                    continue;

                // Only blood ones; puke and friends aren't interesting here.
                bool blood = false;
                var decalSystems = new HashSet<int>();
                foreach (ParticleDecal d in decals)
                {
                    if (d?.m_decalSystem == null)
                        continue;
                    decalSystems.Add(d.m_decalSystem.GetInstanceID());
                    string mn = MaterialOf(d);
                    if (mn.IndexOf("splat_decal_blend", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        mn.IndexOf("seeker_blood", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        mn.IndexOf("SeekerQueen", StringComparison.OrdinalIgnoreCase) >= 0)
                        blood = true;
                }
                if (!blood)
                    continue;

                ParticleSystem[] systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
                int sprayCount = 0;

                var lines = new List<string>();
                foreach (ParticleSystem ps in systems)
                {
                    if (ps == null)
                        continue;

                    bool isDecal = decalSystems.Contains(ps.GetInstanceID());
                    string role = isDecal ? "DECAL" : "spray";
                    if (!isDecal)
                        sprayCount++;

                    string mat = "(none)";
                    var r = ps.GetComponent<ParticleSystemRenderer>();
                    if (r != null && r.sharedMaterial != null)
                        mat = r.sharedMaterial.name;

                    int burst = 0;
                    try
                    {
                        ParticleSystem.EmissionModule em = ps.emission;
                        for (int i = 0; i < em.burstCount; i++)
                            burst += (int)em.GetBurst(i).count.constantMax;
                        if (!em.enabled)
                            role += "(emission OFF)";
                    }
                    catch { }

                    string size = "?";
                    try
                    {
                        ParticleSystem.MainModule main = ps.main;
                        size = $"{main.startSize.constantMin:0.##}..{main.startSize.constantMax:0.##}";
                    }
                    catch { }

                    lines.Add($"      {role,-22} {ps.name,-26} mat={mat,-22} burst={burst,-5} size={size}");
                }

                sb.AppendLine($"  {prefab.name}  ({sprayCount} spray system(s), {decalSystems.Count} decal system(s))");
                if (sprayCount == 0)
                    sb.AppendLine("      >>> NO SPRAY AT ALL - this effect only marks the ground <<<");
                foreach (string l in lines)
                    sb.AppendLine(l);
                sb.AppendLine();
            }
        }

        private static void Walk(object obj, string trail, string ownerPrefab, HashSet<object> visited, int depth)
        {
            if (obj == null || depth > MaxDepth)
                return;

            Type type = obj.GetType();
            foreach (FieldInfo f in FieldsOf(type))
            {
                object value;
                try { value = f.GetValue(obj); }
                catch { continue; }
                if (value == null)
                    continue;

                if (value is EffectList list)
                {
                    Check(list, trail + "." + f.Name, ownerPrefab);
                    continue;
                }

                if (value is EffectList[] arr)
                {
                    for (int i = 0; i < arr.Length; i++)
                        if (arr[i] != null)
                            Check(arr[i], $"{trail}.{f.Name}[{i}]", ownerPrefab);
                    continue;
                }

                // Recurse only into plain serializable game classes. Following a
                // UnityEngine.Object reference would wander off into unrelated prefabs, and
                // following primitives or strings is pointless.
                if (!Recursable(value))
                    continue;
                if (!visited.Add(value))
                    continue;

                if (value is System.Collections.IEnumerable seq && !(value is string))
                {
                    int i = 0;
                    foreach (object item in seq)
                    {
                        if (item == null || !Recursable(item) || !visited.Add(item))
                            continue;
                        Walk(item, $"{trail}.{f.Name}[{i}]", ownerPrefab, visited, depth + 1);
                        i++;
                        if (i > 64)
                            break;   // long arrays of data objects aren't worth exhausting
                    }
                    continue;
                }

                Walk(value, trail + "." + f.Name, ownerPrefab, visited, depth + 1);
            }
        }

        private static bool Recursable(object value)
        {
            if (value is UnityEngine.Object)
                return false;
            Type t = value.GetType();
            if (t.IsPrimitive || t.IsEnum || value is string)
                return false;
            // Unity structs (Vector3, Color, ...) hold nothing we're looking for.
            if (t.Namespace != null && t.Namespace.StartsWith("UnityEngine", StringComparison.Ordinal))
                return false;
            if (t.Namespace != null && t.Namespace.StartsWith("System", StringComparison.Ordinal)
                                    && !(value is System.Collections.IEnumerable))
                return false;
            return true;
        }

        private static void Check(EffectList list, string trail, string ownerPrefab)
        {
            if (list?.m_effectPrefabs == null)
                return;
            foreach (EffectList.EffectData d in list.m_effectPrefabs)
            {
                GameObject p = d?.m_prefab;
                if (p == null)
                    continue;
                if (p.name.IndexOf("blood", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                Refs.Add($"{ownerPrefab} :: {trail} -> {p.name}  (enabled={d.m_enabled})");
            }
        }

        private static FieldInfo[] FieldsOf(Type t)
        {
            if (FieldCache.TryGetValue(t, out FieldInfo[] cached))
                return cached;
            FieldInfo[] fields;
            try
            {
                fields = t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            }
            catch { fields = new FieldInfo[0]; }
            FieldCache[t] = fields;
            return fields;
        }

        private static string MaterialOf(ParticleDecal d)
        {
            try
            {
                ParticleSystem ds = d.m_decalSystem;
                if (ds == null)
                    return "(null decal system)";
                var r = ds.GetComponent<ParticleSystemRenderer>();
                Material m = r?.sharedMaterial;
                if (m == null)
                    return "(null material)";
                Texture tex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                return $"\"{m.name}\" id={m.GetInstanceID()} _MainTex=\"{(tex == null ? "none" : tex.name)}\"" +
                       (tex == null ? "" : $" {tex.width}x{tex.height}");
            }
            catch { return "(unreadable)"; }
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

        /// Reference identity, so the visited set never calls a game class's own Equals - some
        /// override it, and a value-equal-but-distinct object would then be skipped wrongly.
        private sealed class ReferenceComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceComparer Instance = new ReferenceComparer();
            public new bool Equals(object a, object b) => ReferenceEquals(a, b);
            public int GetHashCode(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
        }
    }
}
