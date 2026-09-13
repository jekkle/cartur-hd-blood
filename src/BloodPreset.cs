using UnityEngine;

namespace CarturHDBlood
{
    /// How much blood, as one choice instead of fifteen.
    ///
    /// Custom is deliberately last and deliberately exists: every other value here overrides the
    /// individual settings, so without it the Advanced section would be decoration. Picking
    /// Custom hands control back to those settings exactly as they were before presets.
    internal enum BloodAmount
    {
        Low,
        Normal,
        High,
        Extreme,
        Custom,
    }

    /// Every dial the four presets drive, resolved once per read.
    ///
    /// This exists because the settings had grown to the point where changing "how much blood"
    /// meant reasoning about fifteen interacting multipliers - which is not a thing anyone can
    /// hold in their head, including the person who wrote them. The presets are the answer to
    /// "more blood" and the Advanced section is the answer to "this one specific thing".
    ///
    /// Normal is the mod's shipped, tuned configuration - the values that were defaults before
    /// this file existed, not a snapshot of whatever the config happened to hold.
    ///
    /// The numbers are not a smooth curve. Ground blood is the part people actually notice, so
    /// it climbs hardest; wetness barely moves, because it is a look rather than an amount and
    /// pushing it high is what made blood read as wet plastic rather than blood.
    ///
    /// Droplets are sized to 0.02 - one millimetre, comfortably sub-pixel - rather than removed.
    /// They look like nothing, which is the intent, but they are still emitted because they are
    /// also the ground-mark emitter: ParticleDecal spawns one decal per collision of its own
    /// particle system, and on several effects that system IS blood_drop. Setting the count to
    /// zero would have taken the ground blood with it, which is a bug this project has already
    /// shipped once.
    ///
    /// NORMAL IS THE ANCHOR, and it is the owner's own tuned configuration rather than anything
    /// derived: GroundBlood 0.3, HitBlood 0.2, DeathBlood 0.5, read off the settings screen once
    /// those three had been dialled in by eye. Every other level is that scale halved or doubled.
    ///
    /// It sits well BELOW vanilla on the ground - 1.0 is vanilla and 0.3 is under a third of it -
    /// which is not a mistake. The artwork carries far more coverage per mark than vanilla's, so
    /// matching vanilla's frequency buries the ground; fewer, better marks is the look that was
    /// actually wanted. Earlier scales here were anchored on what a full greydwarf-death burst
    /// looked like, which is how Normal ended up at 1.5 and reading as a smoke grenade.
    ///
    /// The old note about HIGH being the anchor is superseded. The scale was originally built with Normal at a full
    /// greydwarf-death burst, which on a boar dying at your feet filled the screen with fog. That
    /// look is what High means now, and the rest of the scale was moved down under it.
    ///
    /// Hit and Death are equal at every level. The airborne burst is the whole look now, and a
    /// hit that produces a quarter of one reads as a different effect rather than a smaller one.
    /// This does mean every landed blow costs a full death-sized burst.
    ///
    /// Stretch is off at every level. It was on, and it was wrong: Unity's velocityScale is a
    /// multiplier on SPEED, so a droplet travelling 6 m/s with the old default of 2 drew as a
    /// TWELVE METRE streak. Three hundred of those turned a kill into a fountain of red lines
    /// reaching into the sky. It stays available in the debug section, at a sane scale, as a
    /// thing to opt into rather than something shipped untested.
    internal struct BloodPreset
    {
        public float Ground;          // GroundBlood
        public float Hit;             // HitBlood
        public float Death;           // DeathBlood
        public float Wet;             // Wetness
        public float Opacity;         // GroundOpacity
        public float PoolSize;
        public int PoolCount;
        public float DropletSize;
        public float DropletSpread;
        public float DropletCount;
        public float CloudSize;
        public bool Stretch;          // StretchParticles
        public float HitThreshold;    // HitEffectThreshold

        public static BloodPreset For(BloodAmount level)
        {
            switch (level)
            {
                case BloodAmount.Low:
                    return new BloodPreset
                    {
                        // Vanilla's own ground amount - at 1 only the artwork differs.
                        Ground = 0.15f, Hit = 0.1f, Death = 0.25f,
                        Wet = 0.25f, Opacity = 1.2f,
                        PoolSize = 2.5f, PoolCount = 1,
                        DropletSize = 0.02f, DropletSpread = 0.4f, DropletCount = 0.75f,
                        CloudSize = 0.85f, Stretch = false,
                        // Vanilla's own bar: only real wounds bleed.
                        HitThreshold = 0.1f,
                    };

                case BloodAmount.High:
                    return new BloodPreset
                    {
                        Ground = 0.6f, Hit = 0.4f, Death = 1f,
                        Wet = 0.4f, Opacity = 1.6f,
                        PoolSize = 4.5f, PoolCount = 3,
                        DropletSize = 0.02f, DropletSpread = 0.7f, DropletCount = 2f,
                        CloudSize = 1f, Stretch = false,
                        HitThreshold = 0.01f,
                    };

                case BloodAmount.Extreme:
                    return new BloodPreset
                    {
                        // 3 is the top of the ground scale: every hit marks, and marks stay.
                        Ground = 1.2f, Hit = 0.8f, Death = 2f,
                        Wet = 0.45f, Opacity = 2f,
                        PoolSize = 6f, PoolCount = 6,
                        DropletSize = 0.02f, DropletSpread = 0.85f, DropletCount = 4f,
                        CloudSize = 1.25f, Stretch = false,
                        // Everything bleeds, including glancing blows.
                        HitThreshold = 0f,
                    };

                default: // Normal - the mod's shipped defaults
                    return new BloodPreset
                    {
                        Ground = 0.3f, Hit = 0.2f, Death = 0.5f,
                        Wet = 0.35f, Opacity = 1.4f,
                        PoolSize = 3.5f, PoolCount = 3,
                        DropletSize = 0.02f, DropletSpread = 0.6f, DropletCount = 1.5f,
                        CloudSize = 0.92f, Stretch = false,
                        HitThreshold = 0.02f,
                    };
            }
        }

        /// The preset in force, or the individual settings when Custom is chosen.
        ///
        /// Read fresh every time rather than cached, so changing the level in the settings screen
        /// takes effect on the next hit like everything else in this mod.
        public static BloodPreset Current()
        {
            BloodAmount level = Plugin.BloodLevel.Value;
            if (level != BloodAmount.Custom)
                return For(level);

            return new BloodPreset
            {
                Ground = Plugin.GroundBlood.Value,
                Hit = Plugin.HitBlood.Value,
                Death = Plugin.DeathBlood.Value,
                Wet = Plugin.Wetness.Value,
                Opacity = Plugin.GroundOpacity.Value,
                PoolSize = Plugin.PoolSize.Value,
                PoolCount = Plugin.PoolCount.Value,
                DropletSize = DebugTuning.DropletSize.Value,
                DropletSpread = DebugTuning.DropletSpread.Value,
                DropletCount = DebugTuning.DropletCount.Value,
                CloudSize = DebugTuning.CloudSize.Value,
                Stretch = DebugTuning.StretchParticles.Value,
                HitThreshold = Plugin.HitEffectThreshold.Value,
            };
        }
    }
}
