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
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    // Held at full until late, so raising LifetimeMultiplier lengthens the time
                    // blood sits there rather than the time it spends fading.
                    new GradientAlphaKey(1f, 0.72f),
                    new GradientAlphaKey(0f, 1f),
                });

            ParticleSystem.ColorOverLifetimeModule col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(gradient);
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
