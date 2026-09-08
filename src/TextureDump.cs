using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace CarturHDBlood
{
    /// Saves every particle texture in the game to PNG, with a ranking of which are actually
    /// under-resolved for the size they draw at.
    ///
    /// Purely diagnostic and off by default. It exists because the question "should we re-render
    /// the boss ability effects" can't be answered from the outside: the textures live in Unity's
    /// serialized asset files and HDVT's own pack is a custom container, so nothing on disk
    /// exposes them as images.
    ///
    /// The ranking is the point rather than the images. Resolution only matters in proportion to
    /// how large a particle draws - an 8x8 texture on a 0.05-unit droplet is perfectly adequate,
    /// while 64x64 stretched over an 8-unit cloud is not. Dividing texture width by the largest
    /// authored particle size that uses it gives texels-per-world-unit, which sorts the genuinely
    /// weak cases to the top instead of us guessing from two examples.
    internal static class TextureDump
    {
        private class Entry
        {
            public Texture Tex;
            public string MaterialName;
            public float MaxParticleSize;
            public readonly List<string> Users = new List<string>();
        }

        private static ZNetScene _pending;
        private static float _dueAt;

        /// Queues the dump for a while after world load.
        ///
        /// Texture packs finish their work after the scene is up - HDVT's overrides land later -
        /// so an inventory taken at world load records vanilla textures and misses every HD
        /// replacement. Waiting means the dump reports what is actually on screen.
        public static void Schedule(ZNetScene scene)
        {
            if (scene == null || !Plugin.DumpTextures.Value)
                return;

            _pending = scene;
            _dueAt = Time.realtimeSinceStartup + Mathf.Max(1f, Plugin.DumpDelaySeconds.Value);
            Plugin.Log.LogInfo($"Texture dump scheduled in {Plugin.DumpDelaySeconds.Value:0}s " +
                               "(after texture packs have applied their overrides).");
        }

        /// Absolute deadline against realtimeSinceStartup, not a per-frame subtraction - a
        /// countdown decremented by deltaTime inside a throttled tick is how an earlier probe in
        /// another mod ended up firing minutes late.
        public static void Tick()
        {
            if (_pending == null || Time.realtimeSinceStartup < _dueAt)
                return;

            ZNetScene scene = _pending;
            _pending = null;
            Run(scene);
        }

        public static void Run(ZNetScene scene)
        {
            if (scene == null || !Plugin.DumpTextures.Value)
                return;

            string dir = Path.Combine(Plugin.ConfigDir ?? ".", "carturblood_textures");
            var byTexture = new Dictionary<int, Entry>();

            foreach (GameObject prefab in scene.m_prefabs)
            {
                if (prefab == null)
                    continue;

                foreach (ParticleSystemRenderer r in prefab.GetComponentsInChildren<ParticleSystemRenderer>(true))
                {
                    if (r == null || r.sharedMaterial == null)
                        continue;

                    Material mat = r.sharedMaterial;
                    if (!mat.HasProperty("_MainTex"))
                        continue;

                    Texture tex = mat.GetTexture("_MainTex");
                    if (tex == null)
                        continue;

                    int id = tex.GetInstanceID();
                    if (!byTexture.TryGetValue(id, out Entry e))
                    {
                        e = new Entry { Tex = tex, MaterialName = mat.name };
                        byTexture[id] = e;
                    }

                    // Largest authored size across every system that uses this texture - that is
                    // the case the resolution has to serve.
                    float size = 0f;
                    try
                    {
                        ParticleSystem ps = r.GetComponent<ParticleSystem>();
                        if (ps != null)
                        {
                            ParticleSystem.MinMaxCurve c = ps.main.startSize;
                            size = c.mode == ParticleSystemCurveMode.TwoConstants
                                ? c.constantMax
                                : c.constant;
                        }
                    }
                    catch { }

                    if (size > e.MaxParticleSize)
                        e.MaxParticleSize = size;

                    if (e.Users.Count < 6)
                        e.Users.Add(prefab.name);
                }
            }

            var rows = new List<Entry>(byTexture.Values);
            // Fewest texels per world unit first: the weakest cases at the top.
            rows.Sort((a, b) => TexelsPerUnit(a).CompareTo(TexelsPerUnit(b)));

            var sb = new StringBuilder();
            sb.AppendLine("Cartur's HD Blood - particle texture inventory");
            sb.AppendLine("generated " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine();
            sb.AppendLine("Sorted by texels per world unit, weakest first. That ratio - not raw");
            sb.AppendLine("resolution - is what decides whether a texture is under-resolved: an 8x8");
            sb.AppendLine("texture on a 0.05-unit droplet is fine, 64x64 on an 8-unit cloud is not.");
            sb.AppendLine();
            sb.AppendLine($"{"texels/unit",12}  {"texture",-34} {"size",10}  {"maxParticle",11}  material / users");
            sb.AppendLine(new string('-', 130));

            int saved = 0;
            foreach (Entry e in rows)
            {
                float tpu = TexelsPerUnit(e);
                sb.AppendLine(string.Format("{0,12:0.#}  {1,-34} {2,10}  {3,11:0.##}  {4} [{5}]",
                    tpu,
                    Trim(e.Tex.name, 34),
                    e.Tex.width + "x" + e.Tex.height,
                    e.MaxParticleSize,
                    Trim(e.MaterialName, 24),
                    string.Join(", ", e.Users.ToArray())));

                if (Plugin.DumpTextureImages.Value && SaveTexture(e.Tex, dir))
                    saved++;
            }

            sb.AppendLine();
            sb.AppendLine($"{rows.Count} distinct particle textures; {saved} written as PNG.");

            try
            {
                string path = Path.Combine(Plugin.ConfigDir ?? ".", "carturblood_textures.txt");
                File.WriteAllText(path, sb.ToString());
                Plugin.Log.LogInfo($"Texture inventory written to {path} ({rows.Count} textures, {saved} PNGs).");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not write texture inventory: " + e.Message);
            }
        }

        private static float TexelsPerUnit(Entry e)
        {
            if (e.MaxParticleSize <= 0.001f)
                return float.MaxValue;   // no size known: not a candidate, sort last
            return e.Tex.width / e.MaxParticleSize;
        }

        private static string Trim(string s, int n) =>
            string.IsNullOrEmpty(s) ? "(unnamed)" : (s.Length <= n ? s : s.Substring(0, n - 1) + "~");

        /// Copies a texture into a readable one and writes it out.
        ///
        /// Game textures are compressed and not CPU-readable, so GetPixels fails on them
        /// directly. Blitting through a RenderTexture and reading back is the standard way round
        /// that - the GPU does the decompression.
        private static bool SaveTexture(Texture tex, string dir)
        {
            RenderTexture rt = null;
            RenderTexture prev = null;
            Texture2D readable = null;

            try
            {
                Directory.CreateDirectory(dir);

                rt = RenderTexture.GetTemporary(tex.width, tex.height, 0,
                        RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
                Graphics.Blit(tex, rt);

                prev = RenderTexture.active;
                RenderTexture.active = rt;

                readable = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
                readable.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
                readable.Apply();

                byte[] png = EncodeViaReflection(readable);
                if (png == null)
                    return false;

                string safe = string.IsNullOrEmpty(tex.name) ? "unnamed_" + tex.GetInstanceID() : tex.name;
                foreach (char c in Path.GetInvalidFileNameChars())
                    safe = safe.Replace(c, '_');

                File.WriteAllBytes(Path.Combine(dir, $"{tex.width}x{tex.height}_{safe}.png"), png);
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning($"Could not save texture '{tex.name}': {e.Message}");
                return false;
            }
            finally
            {
                RenderTexture.active = prev;
                if (rt != null)
                    RenderTexture.ReleaseTemporary(rt);
                if (readable != null)
                    UnityEngine.Object.Destroy(readable);
            }
        }

        /// Same reasoning as the LoadImage call elsewhere: referencing ImageConversion directly
        /// pulls in an overload set that will not compile under net472 against this game's
        /// netstandard.dll.
        private static byte[] EncodeViaReflection(Texture2D tex)
        {
            try
            {
                Type conv = Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")
                            ?? Type.GetType("UnityEngine.ImageConversion, UnityEngine");
                if (conv != null)
                {
                    MethodInfo mi = conv.GetMethod("EncodeToPNG",
                        BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Texture2D) }, null);
                    if (mi != null)
                        return (byte[])mi.Invoke(null, new object[] { tex });
                }

                MethodInfo legacy = typeof(Texture2D).GetMethod("EncodeToPNG",
                    BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
                if (legacy != null)
                    return (byte[])legacy.Invoke(tex, null);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("EncodeToPNG failed: " + e.Message);
            }
            return null;
        }
    }
}
