using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace CarturHDBlood
{
    /// Finds every renderer in the game that could draw a given material, and every particle
    /// system that could produce particles for it.
    ///
    /// Exists because the earlier texture pass had two blind spots that made a claim look
    /// stronger than it was. It enumerated only ParticleSystemRenderer, and read only
    /// sharedMaterial - so it could not see:
    ///
    ///   - trailMaterial, a completely separate slot on the same renderer
    ///   - MeshRenderer, LineRenderer, TrailRenderer, SkinnedMeshRenderer and the rest
    ///   - the second and later entries of sharedMaterials on a multi-material renderer
    ///
    /// It also recorded emission state but not maxParticles, and emission being disabled does
    /// NOT mean a system is inert: particles can arrive from a script calling Emit() or from a
    /// parent system's sub-emitter. maxParticles = 0 is the only thing that rules all of that
    /// out, so it is recorded per system here.
    ///
    /// The concrete question this was written to settle: are the 43 blood_splat2 systems truly
    /// never drawn - which would mean HD Valheim Textures' best blood texture is installed and
    /// never seen - or are they reached some other way?
    internal static class MaterialAudit
    {
        private class Use
        {
            public string Where;        // prefab :: transform path
            public string Slot;         // sharedMaterial[i] / trailMaterial
            public string RendererType;
            public bool RendererEnabled;
            public int MaxParticles = -1;   // -1 = not a particle system
            public bool EmissionEnabled;
            public int BurstTotal;
            public bool HasSubEmitters;
            public bool TargetedByDecal;
        }

        public static void Run(ZNetScene scene)
        {
            if (scene == null || !Plugin.AuditMaterials.Value)
                return;

            string[] wanted = Split(Plugin.AuditMaterialNames.Value);
            if (wanted.Length == 0)
                return;

            var uses = new Dictionary<string, List<Use>>();
            foreach (string w in wanted)
                uses[w] = new List<Use>();

            // Decal systems are collected first, so a system can be reported as "emission off but
            // a ParticleDecal emits into it" rather than being mistaken for inert.
            var decalTargets = new HashSet<int>();
            foreach (GameObject prefab in scene.m_prefabs)
            {
                if (prefab == null)
                    continue;
                foreach (ParticleDecal d in prefab.GetComponentsInChildren<ParticleDecal>(true))
                    if (d != null && d.m_decalSystem != null)
                        decalTargets.Add(d.m_decalSystem.GetInstanceID());
            }

            int prefabs = 0, renderers = 0;
            foreach (GameObject prefab in scene.m_prefabs)
            {
                if (prefab == null)
                    continue;
                prefabs++;

                // EVERY renderer type, not just particle ones.
                foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null)
                        continue;
                    renderers++;

                    Material[] mats;
                    try { mats = r.sharedMaterials; }
                    catch { mats = new Material[0]; }

                    for (int i = 0; i < mats.Length; i++)
                        Record(uses, wanted, mats[i], prefab, r, $"sharedMaterials[{i}]", decalTargets);

                    // The separate trail slot, invisible to the earlier pass.
                    var psr = r as ParticleSystemRenderer;
                    if (psr != null)
                    {
                        Material trail = null;
                        try { trail = psr.trailMaterial; }
                        catch { }
                        Record(uses, wanted, trail, prefab, r, "trailMaterial", decalTargets);
                    }
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("Cartur's HD Blood - material audit");
            sb.AppendLine("generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine($"scanned {prefabs} prefabs, {renderers} renderers of every type");
            sb.AppendLine();
            sb.AppendLine("maxParticles = 0 is the only value that proves a system cannot draw:");
            sb.AppendLine("emission being disabled still allows script Emit() and sub-emitters.");
            sb.AppendLine();

            foreach (string w in wanted)
            {
                List<Use> list = uses[w];
                sb.AppendLine("================================================================");
                sb.AppendLine($"MATERIAL \"{w}\"  -  {list.Count} renderer slot(s)");
                sb.AppendLine();

                if (list.Count == 0)
                {
                    sb.AppendLine("  no renderer of any type references this material.");
                    sb.AppendLine();
                    continue;
                }

                int canDraw = 0, provablyInert = 0, nonParticle = 0, viaTrail = 0, viaDecal = 0;

                foreach (Use u in list)
                {
                    if (u.Slot == "trailMaterial") viaTrail++;
                    if (u.MaxParticles < 0) nonParticle++;
                    else if (u.MaxParticles == 0) provablyInert++;
                    else canDraw++;
                    if (u.TargetedByDecal) viaDecal++;

                    sb.AppendLine(string.Format(
                        "  {0,-26} {1,-18} {2,-20} enabled={3,-5} maxParticles={4,-6} emission={5,-5} burst={6,-5}{7}{8}",
                        Trim(u.Where, 26), u.Slot, u.RendererType, u.RendererEnabled,
                        u.MaxParticles < 0 ? "n/a" : u.MaxParticles.ToString(),
                        u.MaxParticles < 0 ? "n/a" : u.EmissionEnabled.ToString(),
                        u.BurstTotal,
                        u.HasSubEmitters ? "  SUB-EMITTERS" : "",
                        u.TargetedByDecal ? "  DECAL-EMITS-INTO" : ""));
                }

                sb.AppendLine();
                sb.AppendLine($"  can draw (maxParticles > 0): {canDraw}");
                sb.AppendLine($"  provably inert (maxParticles == 0): {provablyInert}");
                sb.AppendLine($"  non-particle renderers (mesh/line/trail/etc): {nonParticle}");
                sb.AppendLine($"  reached via trailMaterial: {viaTrail}");
                sb.AppendLine($"  emitted into by a ParticleDecal: {viaDecal}");
                sb.AppendLine();

                if (canDraw == 0 && nonParticle == 0 && viaTrail == 0)
                    sb.AppendLine("  VERDICT: nothing can draw this material. Any texture on it is never seen.");
                else
                    sb.AppendLine("  VERDICT: this material CAN be drawn - see the rows above for how.");
                sb.AppendLine();
            }

            try
            {
                string path = Path.Combine(Plugin.ConfigDir ?? ".", "carturblood_material_audit.txt");
                File.WriteAllText(path, sb.ToString());
                Plugin.Log.LogInfo("Material audit written to " + path);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not write material audit: " + e.Message);
            }
        }

        private static void Record(Dictionary<string, List<Use>> uses, string[] wanted, Material mat,
                                   GameObject prefab, Renderer r, string slot, HashSet<int> decalTargets)
        {
            if (mat == null || mat.name == null)
                return;

            foreach (string w in wanted)
            {
                if (!mat.name.StartsWith(w, StringComparison.OrdinalIgnoreCase))
                    continue;

                var u = new Use
                {
                    Where = prefab.name + " :: " + r.gameObject.name,
                    Slot = slot,
                    RendererType = r.GetType().Name,
                    RendererEnabled = r.enabled,
                };

                var ps = r.GetComponent<ParticleSystem>();
                if (ps != null)
                {
                    try
                    {
                        u.MaxParticles = ps.main.maxParticles;
                        ParticleSystem.EmissionModule em = ps.emission;
                        u.EmissionEnabled = em.enabled;
                        for (int i = 0; i < em.burstCount; i++)
                            u.BurstTotal += (int)em.GetBurst(i).count.constantMax;
                        u.HasSubEmitters = ps.subEmitters.enabled && ps.subEmitters.subEmittersCount > 0;
                        u.TargetedByDecal = decalTargets.Contains(ps.GetInstanceID());
                    }
                    catch { }
                }

                uses[w].Add(u);
                break;
            }
        }

        private static string[] Split(string raw)
        {
            var list = new List<string>();
            foreach (string part in (raw ?? string.Empty).Split(','))
            {
                string t = part.Trim();
                if (t.Length > 0)
                    list.Add(t);
            }
            return list.ToArray();
        }

        private static string Trim(string s, int n) =>
            string.IsNullOrEmpty(s) ? "?" : (s.Length <= n ? s : s.Substring(0, n - 1) + "~");
    }
}
