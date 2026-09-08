namespace CarturHDBlood
{
    /// One dial for "how much blood", instead of making the player reason about four
    /// interacting multipliers.
    ///
    /// The numbers aren't arbitrary - they come from what the probe found in the game. Vanilla
    /// puts 23 of its 116 decal owners at only 10% chance, including most creatures' death
    /// splat, so raising the floor is what actually produces more blood; the multiplier alone
    /// does nothing to the 69 owners already sitting at 100. And raising chance without also
    /// raising the particle cap just makes new decals evict old ones, so the ground never fills.
    public enum BloodLevel
    {
        Low,
        Normal,
        High,
        Extreme,
        Custom,
    }

    internal struct BloodPreset
    {
        public float MinChance;
        public float ChanceMultiplier;
        public float SizeMultiplier;
        public float LifetimeMultiplier;
        public int MaxDecals;

        /// Normal reproduces vanilla's own amounts exactly, so it's a true baseline rather than
        /// a mild version of the mod - at Normal only the artwork differs from vanilla.
        public static BloodPreset For(BloodLevel level)
        {
            switch (level)
            {
                case BloodLevel.Low:
                    return new BloodPreset
                    {
                        MinChance = 0f,
                        ChanceMultiplier = 0.5f,
                        SizeMultiplier = 0.8f,
                        LifetimeMultiplier = 0.6f,
                        MaxDecals = 0,
                    };

                case BloodLevel.High:
                    return new BloodPreset
                    {
                        MinChance = 50f,
                        ChanceMultiplier = 1.5f,
                        SizeMultiplier = 1.25f,
                        LifetimeMultiplier = 2f,
                        MaxDecals = 200,
                    };

                case BloodLevel.Extreme:
                    return new BloodPreset
                    {
                        MinChance = 100f,
                        ChanceMultiplier = 3f,
                        SizeMultiplier = 1.6f,
                        LifetimeMultiplier = 4f,
                        MaxDecals = 600,
                    };

                case BloodLevel.Normal:
                default:
                    return new BloodPreset
                    {
                        MinChance = 10f,
                        ChanceMultiplier = 1f,
                        SizeMultiplier = 1f,
                        LifetimeMultiplier = 1f,
                        MaxDecals = 0,
                    };
            }
        }

        /// Custom hands control back to the individual config entries.
        public static BloodPreset Current()
        {
            BloodLevel level = Plugin.Level.Value;
            if (level != BloodLevel.Custom)
                return For(level);

            return new BloodPreset
            {
                MinChance = Plugin.MinChance.Value,
                ChanceMultiplier = Plugin.ChanceMultiplier.Value,
                SizeMultiplier = Plugin.SizeMultiplier.Value,
                LifetimeMultiplier = Plugin.LifetimeMultiplier.Value,
                MaxDecals = Plugin.MaxDecals.Value,
            };
        }
    }
}
