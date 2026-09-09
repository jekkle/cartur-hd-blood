using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace CarturHDBlood
{
    /// Watches what actually spawns when something gets hit or dies.
    ///
    /// Why this is needed on top of the prefab probe: the prefab probe found vfx_BloodHit and
    /// vfx_BloodDeath by NAME only. Not one of the 104 Character prefabs references them from
    /// m_hitEffects / m_critHitEffects / m_backstabHitEffects / m_deathEffects. So the values
    /// read off those prefabs - m_chance, decal size, decal lifetime - are not yet known to be
    /// the values that fire in play. If creatures instead spawn a nested copy inside some other
    /// effect, that copy is a separate component instance carrying its own settings, and editing
    /// the standalone prefab would change nothing.
    ///
    /// Two hooks settle it:
    ///   EffectList.Create   - names every effect actually instantiated, so we learn the real
    ///                         chain from a creature to its blood.
    ///   ParticleDecal.Awake - reports the LIVE decal component: its chance, and the material
    ///                         plus texture its decal system is really rendering with.
    ///
    /// Log-once-per-key throughout: a fight would otherwise produce thousands of identical lines.
    internal static class LiveProbe
    {
        private static readonly HashSet<string> SeenEffect = new HashSet<string>();
        private static readonly HashSet<string> SeenDecal = new HashSet<string>();

        public static void NoteEffect(GameObject go)
        {
            if (go == null || !Plugin.Diagnostics.Value)
                return;

            string name = go.name.Replace("(Clone)", "");
            bool bloody = name.IndexOf("blood", StringComparison.OrdinalIgnoreCase) >= 0;

            // Anything containing a ParticleDecal is relevant even when its name says nothing
            // about blood - that component is the thing that marks the ground.
            ParticleDecal[] decals = go.GetComponentsInChildren<ParticleDecal>(true);
            if (!bloody && decals.Length == 0)
                return;

            if (!SeenEffect.Add(name))
                return;

            var sb = new StringBuilder();
            sb.Append("SPAWNED effect \"").Append(name).Append("\"");
            if (decals.Length > 0)
                sb.Append("  (").Append(decals.Length).Append(" ParticleDecal in hierarchy)");
            Plugin.Log.LogInfo("[live] " + sb);

            foreach (ParticleDecal d in decals)
            {
                // Skip the cloud graft's own copies.
                //
                // This scan runs from EffectList.Create's postfix, mid-frame. The graft disables
                // its stowaway ParticleDecals on the spot but destroys them with Destroy, which
                // is deferred to the end of the frame - so they are still present here, already
                // inert, and reporting them made it look as though the strip had failed. It cost
                // one round of chasing a bug that was already fixed.
                if (d == null || d.gameObject.name.StartsWith("CarturBloodCloud_", StringComparison.Ordinal))
                    continue;
                Describe(d, "    via " + name);
            }
        }

        public static void NoteDecal(ParticleDecal decal) => Describe(decal, "direct Awake");

        private static void Describe(ParticleDecal decal, string how)
        {
            if (decal == null || !Plugin.Diagnostics.Value)
                return;

            string path = PathOf(decal.transform).Replace("(Clone)", "");
            if (!SeenDecal.Add(path))
                return;

            Plugin.Log.LogInfo($"[live] ParticleDecal LIVE at {path}   ({how})");
            Plugin.Log.LogInfo($"[live]   m_chance = {decal.m_chance}");

            ParticleSystem ds = decal.m_decalSystem;
            if (ds == null)
            {
                Plugin.Log.LogInfo("[live]   m_decalSystem = NULL");
                return;
            }

            try
            {
                ParticleSystem.MainModule main = ds.main;
                Plugin.Log.LogInfo($"[live]   decal startSize = {main.startSize.constantMin}..{main.startSize.constantMax}" +
                                   $"  startLifetime = {main.startLifetime.constantMin}..{main.startLifetime.constantMax}" +
                                   $"  maxParticles = {main.maxParticles}");
                // Colour is the whole reason player blood came out grey while neck blood looked
                // right: the texture is now pure white, so whatever tint the effect authored is
                // the tint you see. Vanilla's reddish 'brains' texture used to mask a weak one.
                Plugin.Log.LogInfo($"[live]   decal startColor = {FmtGradient(main.startColor)}" +
                                   $"  saturation = {Saturation(main.startColor):0.###}");
            }
            catch (Exception e) { Plugin.Log.LogInfo("[live]   main read failed: " + e.Message); }

            try
            {
                var r = ds.GetComponent<ParticleSystemRenderer>();
                Material m = r == null ? null : r.sharedMaterial;
                if (m == null)
                {
                    Plugin.Log.LogInfo("[live]   decal material = NULL");
                    return;
                }
                Texture tex = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                // The instance ID is the point of this line: if it matches the one the prefab
                // probe recorded for splat_decal_blend, then it is literally the same material
                // object and a single swap reaches this decal.
                Plugin.Log.LogInfo($"[live]   decal material = \"{m.name}\" id={m.GetInstanceID()} " +
                                   $"shader=\"{(m.shader == null ? "?" : m.shader.name)}\" " +
                                   $"_MainTex=\"{(tex == null ? "none" : tex.name)}\" " +
                                   $"{(tex == null ? "" : tex.width + "x" + tex.height)}");
            }
            catch (Exception e) { Plugin.Log.LogInfo("[live]   material read failed: " + e.Message); }
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

        internal static string FmtGradient(ParticleSystem.MinMaxGradient g)
        {
            try
            {
                switch (g.mode)
                {
                    case ParticleSystemGradientMode.Color:
                        return g.color.ToString();
                    case ParticleSystemGradientMode.TwoColors:
                        return g.colorMin + ".." + g.colorMax;
                    default:
                        return g.mode.ToString();
                }
            }
            catch { return "?"; }
        }

        /// HSV saturation of the authored colour - the number that separates "this effect has a
        /// real blood tint" from "this effect is grey and relied on the texture for its colour".
        internal static float Saturation(ParticleSystem.MinMaxGradient g)
        {
            try
            {
                Color c;
                switch (g.mode)
                {
                    case ParticleSystemGradientMode.Color:
                        c = g.color;
                        break;
                    case ParticleSystemGradientMode.TwoColors:
                        c = g.colorMin;
                        break;
                    default:
                        return -1f;   // gradient modes: not a single colour, don't pretend
                }

                float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
                float min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
                return max <= 0f ? 0f : (max - min) / max;
            }
            catch { return -1f; }
        }
    }

    /// Every effect in the game is instantiated through here, so this one hook names the whole
    /// chain from a creature's hit or death to whatever draws its blood.
    [HarmonyPatch(typeof(EffectList), nameof(EffectList.Create))]
    internal static class Patch_EffectList_Create
    {
        private static void Postfix(GameObject[] __result)
        {
            if (__result == null)
                return;
            foreach (GameObject go in __result)
                LiveProbe.NoteEffect(go);
        }
    }

    /// The mod's actual entry point, and the reason it works at all: this fires for every
    /// ParticleDecal instance no matter how it was spawned - standalone effect prefab or a copy
    /// nested inside a per-creature effect. Editing prefabs would have missed the nested ones,
    /// which turned out to be the only ones creatures actually use.
    [HarmonyPatch(typeof(ParticleDecal), "Awake")]
    internal static class Patch_ParticleDecal_Awake
    {
        private static void Postfix(ParticleDecal __instance)
        {
            // Logged before the skin is applied, so the diagnostics record vanilla's values
            // rather than our own edits reflected back at us.
            LiveProbe.NoteDecal(__instance);
            BloodSkin.Apply(__instance);
        }
    }

    /// Marks the moment in the log, so the lines above and below a kill can be told apart.
    [HarmonyPatch(typeof(Character), "OnDeath")]
    internal static class Patch_Character_OnDeath
    {
        private static void Prefix(Character __instance)
        {
            Plugin.Log.LogInfo($"[live] ---- OnDeath: {__instance?.m_name} ----");
        }
    }
}
