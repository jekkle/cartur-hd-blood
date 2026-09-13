using UnityEngine;

namespace CarturHDBlood
{
    /// Ground decals: how often the terrain gets marked, how big, how long it lasts.
    ///
    /// One number drives all five, because five interacting multipliers is not a setting a
    /// person can reason about. The scale is anchored so that **1 is exactly vanilla** - at 1
    /// only the artwork differs from an unmodded game - and 0 is no ground blood at all.
    ///
    /// The mapping is not linear on chance, and that is deliberate. Vanilla puts 23 of its 116
    /// decal owners at only 10% chance, including most creatures' death splat, while 69 are
    /// already at 100. So the multiplier does nothing for the majority and the FLOOR is what
    /// actually produces more blood - which is why the floor is what climbs fastest above 1.
    /// Raising chance without also raising the particle cap just makes new decals evict old
    /// ones, so the cap comes up alongside it.
    internal struct GroundPreset
    {
        public float MinChance;
        public float ChanceMultiplier;
        public float SizeMultiplier;
        public float LifetimeMultiplier;
        public int MaxDecals;

        /// <param name="g">0 = none, 1 = vanilla, 3 = every hit marks and marks last.</param>
        public static GroundPreset From(float g)
        {
            g = Mathf.Clamp(g, 0f, 3f);

            if (g <= 0f)
            {
                return new GroundPreset
                {
                    MinChance = 0f,
                    ChanceMultiplier = 0f,
                    SizeMultiplier = 1f,
                    LifetimeMultiplier = 1f,
                    MaxDecals = 0,
                };
            }

            return new GroundPreset
            {
                // Below 1 the floor falls away with the scale; above it, it climbs toward
                // certainty. 1 -> 10 (vanilla), 1.5 -> 32, 2 -> 55, 3 -> 100.
                MinChance = g <= 1f ? 10f * g : Mathf.Lerp(10f, 100f, (g - 1f) * 0.5f),

                ChanceMultiplier = g,

                // Size moves far less than the rest. Decal size is authored per creature on
                // purpose - a troll marks more ground than a greyling - and this scale is about
                // how much blood there is, not about flattening that design.
                SizeMultiplier = Mathf.Max(0.25f, 1f + (g - 1f) * 0.3f),

                // Never SHORTER than vanilla, only longer.
                //
                // This used to be the raw scale, so GroundBlood 0.3 cut every mark to 30% of its
                // authored life - a greydwarf hit mark went from 5 seconds to 1.5 and was gone
                // before the fight ended, which read as "hit blood doesn't reach the ground" when
                // it was landing perfectly well and then disappearing.
                //
                // Turning the blood down should mean FEWER marks, not marks that blink out: the
                // chance multiplier above already delivers "less blood", and doing it twice was
                // never the intent. Above 1 it still stretches, so the high presets keep blood
                // around longer than vanilla.
                LifetimeMultiplier = Mathf.Max(1f, g),

                // 0 leaves vanilla's own caps alone (they run 10 to 1000); the caller only ever
                // raises a cap, never lowers one. 2 -> 300, 3 -> 600.
                MaxDecals = g <= 1f ? 0 : Mathf.RoundToInt(Mathf.Lerp(0f, 600f, (g - 1f) * 0.5f)),
            };
        }

        /// Reads the blood preset rather than GroundBlood directly, so the one BloodLevel choice
        /// reaches the ground decals too. On Custom the preset simply returns GroundBlood.
        public static GroundPreset Current() => From(BloodPreset.Current().Ground);
    }
}
