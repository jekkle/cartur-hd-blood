using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CarturHDBlood
{
    /// How big the thing that just died actually is.
    ///
    /// The death pool used to be scaled by the effect's authored ground-decal size, on the
    /// assumption that a bigger creature carries a bigger decal. It does not. Effects share child
    /// prefabs, so 47 systems are authored at exactly 2.0 and 17 at 3.5 - a hen and a lox carry the
    /// same decal, and a boar's is larger than a greydwarf's despite the greydwarf being the taller
    /// creature. Scaling anything by it produced exactly that inversion on screen.
    ///
    /// Character.GetHeight() is the real measurement: it reads the creature's own CapsuleCollider,
    /// so it is whatever the model actually is, it costs nothing, and it is right for modded
    /// creatures the mod has never heard of without a lookup table to maintain.
    internal static class CreatureSize
    {
        /// Height of the creature whose death effect is being processed right now.
        ///
        /// Same single-field approach as HitThreshold's block flag and SprayAim's direction, and
        /// safe for the same reason: Character.OnDeath spawns its effects synchronously, so the
        /// decal's Awake fires inside the call that set this and nothing can interleave.
        private static float _height;

        /// The creature this is calibrated against, in metres of collider height.
        ///
        /// A greydwarf, which is the size the owner signed off on. Anything shorter gets a smaller
        /// pool and anything taller a larger one, so PoolSize means "the pool for a greydwarf-sized
        /// kill" rather than one flat number for a hen and a troll alike.
        internal const float ReferenceHeight = 1.8f;

        /// Clamped rather than open-ended. A bat should still leave a visible mark, and neither end
        /// is worth trusting to a collider on a creature some other mod authored.
        ///
        /// The ceiling is 4.0 because the real heights, read off all 164 Character prefabs, run
        /// from a 0.8m bat to a 12m Hive. At 2.5 the cap bit from StoneGolem (4.5m) upward and
        /// flattened 31 creatures - troll, dragon, Bonemass and the Hive all got one pool size,
        /// which is the same failure the authored-decal rule had, just moved up the scale. At 4.0
        /// only seven clamp, and everything through Troll and Bonemass still separates.
        internal const float MinScale = 0.35f;
        internal const float MaxScale = 4f;

        /// Logged once per creature so the reference can be checked against real numbers rather
        /// than argued about. Cleared on world load.
        private static readonly HashSet<string> Logged = new HashSet<string>();

        public static void Clear() => Logged.Clear();

        internal static void Record(Character character)
        {
            if (character == null)
            {
                _height = 0f;
                return;
            }

            try
            {
                _height = character.GetHeight();

                string name = character.name ?? "?";
                if (Logged.Add(name))
                    Plugin.Log.LogInfo($"Creature size: {name} height {_height:0.##}m " +
                                       $"-> pool scale {Scale():0.##}");
            }
            catch
            {
                _height = 0f;
            }
        }

        /// 1.0 for a greydwarf-sized creature. Falls back to 1.0 when nothing was recorded, which
        /// is the old flat behaviour rather than a pool of zero.
        internal static float Scale() => ScaleFor(_height);

        /// The same maths for any height, so the probe's table and the live pool cannot drift
        /// apart the way two copies of a formula always eventually do.
        internal static float ScaleFor(float height)
        {
            if (height <= 0.01f)
                return 1f;
            return Mathf.Clamp(height / ReferenceHeight, MinScale, MaxScale);
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
    internal static class Patch_Character_OnDeath_Size
    {
        /// Prefix, not postfix: the death effects are spawned inside OnDeath, so by the time a
        /// postfix ran the pool would already have been laid down at the wrong size.
        private static void Prefix(Character __instance) => CreatureSize.Record(__instance);
    }
}
