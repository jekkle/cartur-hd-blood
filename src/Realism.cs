using System;
using UnityEngine;

namespace CarturHDBlood
{
    /// Behaviour changes that make the blood read as liquid rather than as stamped decals and
    /// straight-flying sprites. None of this is about texture detail - it is the part vanilla
    /// leaves entirely unset.
    internal static class Realism
    {
        /// Ground decals: dry out and fade instead of blinking away.
        ///
        /// Vanilla sets no colourOverLifetime at all, so a decal is drawn at full strength for
        /// its whole life and then vanishes in a single frame. Two problems with that: the
        /// disappearance is visible as a pop, and fresh blood and blood that has been on the
        /// ground for a minute look identical.
        ///
        /// The gradient multiplies the effect's own startColor, so per-creature blood colour is
        /// preserved - it just gets darker and slightly browner as it ages, then fades out over
        /// the last stretch of its life.
        public static void AgeDecal(ParticleSystem ps)
        {
            if (!Plugin.DecalAging.Value)
                return;

            float dry = Mathf.Clamp01(Plugin.DryDarkening.Value);

            var gradient = new Gradient();

            // Multiplying toward a darker, less saturated value reads as drying. Red falls least
            // so the result browns rather than greying out.
            var dried = new Color(
                Mathf.Lerp(1f, 0.45f, dry),
                Mathf.Lerp(1f, 0.22f, dry),
                Mathf.Lerp(1f, 0.16f, dry));

            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(Color.white, 0f),
                    new GradientColorKey(Color.Lerp(Color.white, dried, 0.5f), 0.25f),
                    new GradientColorKey(dried, 1f),
                },
                BuildAlphaKeys());

            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(gradient);
        }

        /// When the fade starts, and how it gets to nothing.
        ///
        /// FadeStart 0 gives a continuous decline from the moment the blood lands - it is never
        /// at full strength for any length of time, it is always on its way out. Anything above 0
        /// holds it at full for that fraction of its life first.
        ///
        /// Four keys rather than two, because a straight line from 1 to 0 reads as a light being
        /// dimmed rather than as something soaking away. Real marks lose their edges early and
        /// then linger faintly, so the curve drops off quickly at first and flattens into a long
        /// tail. The last key is a genuine zero - the mark reaches nothing on its own rather than
        /// being cut off while still visible.
        private static GradientAlphaKey[] BuildAlphaKeys()
        {
            float start = Mathf.Clamp01(Plugin.FadeStart.Value);

            // Gradient keys must be strictly increasing, so a start of 1 still needs room for the
            // fade itself.
            start = Mathf.Min(start, 0.9f);
            float span = 1f - start;

            return new[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, start),
                new GradientAlphaKey(0.55f, start + span * 0.35f),
                new GradientAlphaKey(0.22f, start + span * 0.70f),
                new GradientAlphaKey(0f, 1f),
            };
        }

        /// Ground decals: land rather than appear.
        ///
        /// A decal at full size on frame one reads as a stamp. Growing it over the first few
        /// percent of its life reads as liquid spreading on contact. Deliberately fast - this is
        /// meant to be felt rather than seen.
        public static void GrowDecal(ParticleSystem ps)
        {
            if (!Plugin.DecalGrowIn.Value)
                return;

            var curve = new AnimationCurve(
                new Keyframe(0f, 0.82f),
                new Keyframe(0.06f, 1f),
                new Keyframe(1f, 1f));

            ParticleSystem.SizeOverLifetimeModule sol = ps.sizeOverLifetime;
            sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, curve);
        }

        /// Widens the random size range instead of scaling it uniformly.
        ///
        /// SizeMultiplier moves every decal from a given effect by the same factor, so all its
        /// marks stay the same size relative to each other. Real spatter varies. Widening the
        /// authored min/max around its own midpoint adds that variation without changing the
        /// average, so it composes with the size multiplier rather than fighting it.
        public static ParticleSystem.MinMaxCurve JitterSize(ParticleSystem.MinMaxCurve c)
        {
            float jitter = Mathf.Clamp01(Plugin.SizeJitter.Value);
            if (jitter <= 0.001f)
                return c;

            switch (c.mode)
            {
                case ParticleSystemCurveMode.Constant:
                {
                    float v = c.constant;
                    return new ParticleSystem.MinMaxCurve(v * (1f - jitter), v * (1f + jitter));
                }
                case ParticleSystemCurveMode.TwoConstants:
                {
                    float mid = (c.constantMin + c.constantMax) * 0.5f;
                    float half = (c.constantMax - c.constantMin) * 0.5f;
                    // Widen the existing spread, and give a system authored with no spread some.
                    float widened = Mathf.Max(half * (1f + jitter), mid * jitter);
                    return new ParticleSystem.MinMaxCurve(
                        Mathf.Max(0.01f, mid - widened), mid + widened);
                }
                default:
                    return c;
            }
        }

    }
}
