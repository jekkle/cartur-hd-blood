using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace CarturHDBlood
{
    /// Makes blood spray away from the blow instead of expanding as a ball.
    ///
    /// Vanilla emits the 200 droplets from a Sphere of radius 0.32 with radiusThickness 1 and
    /// randomDirectionAmount 0 - read out of the game's bundle, not guessed. That means every
    /// droplet is born somewhere inside a 32 cm ball and travels straight outward from its
    /// centre, in every direction equally. An expanding ball is what an explosion looks like.
    /// Blood from a wound goes one way: away from whatever hit it.
    ///
    /// Changing the shape to a Cone is only half of it, because a cone still has to be pointed
    /// somewhere and the effect is spawned with Quaternion.identity - it carries no aim at all.
    /// HitData.m_dir does: it is the direction the blow travelled. The prefix below records it
    /// for the hit currently being processed, and the graft rotates each spray to match.
    ///
    /// REVERSIBLE BY DESIGN. Everything happens on the spawned instance, never on the prefab,
    /// and the whole file is gated behind DirectionalSpray. Turn it off and the next effect
    /// spawns exactly as vanilla authored it - there is nothing to undo, because nothing
    /// persistent was changed.
    internal static class SprayAim
    {
        /// The direction of the blow being processed right now.
        ///
        /// Same reasoning as the block flag in HitThreshold: Character.RPC_Damage runs the whole
        /// sequence synchronously and the decal's Awake fires inside it, so a single field is
        /// enough and nothing can interleave.
        ///
        /// Deliberately NOT cleared after use. Death effects are spawned from OnDeath rather than
        /// from ApplyDamage, so at that point this still holds the killing blow's direction -
        /// which is exactly what a death spray should follow. The one case it gets wrong is a
        /// creature that dies to poison or burning some time after its last real hit, where the
        /// spray follows a blow that is no longer relevant. A wrong direction there is a far
        /// smaller error than no direction at all, which would put the ball back.
        private static Vector3 _hitDir;

        internal static void RecordHit(HitData hit)
        {
            _hitDir = hit == null ? Vector3.zero : hit.m_dir;
        }

        /// Effects already aimed, keyed on the spawned effect root - the same dedupe CloudGraft
        /// uses, and for the same reason: one effect can carry several ParticleDecals.
        private static readonly HashSet<int> Aimed = new HashSet<int>();

        public static void Reset() => Aimed.Clear();

        public static void Apply(ParticleDecal decal)
        {
            if (decal == null || !Plugin.DirectionalSpray.Value)
                return;

            Transform root = decal.transform.root;
            if (root == null || !Aimed.Add(root.GetInstanceID()))
                return;

            // No usable direction - a fall, a burn, an environmental hit. Left as vanilla's ball,
            // which is the right answer when nothing struck from a particular side.
            Vector3 dir = _hitDir;
            if (dir.sqrMagnitude < 0.0001f)
                return;

            // HitData.m_dir points from attacker to target. Verified rather than assumed: the
            // game feeds this same vector to Character.ApplyPushback, which shoves the target
            // away from whoever hit it, so its sign is not in doubt.
            //
            // Only the HORIZONTAL part is used, and the earlier version's mistake was not using
            // it. That version reflected the whole vector about world up, which flips the
            // vertical component - so a downward sword chop, whose direction points down and
            // forward, came out pointing UP and forward. The result was a fountain of blood
            // firing into the sky on every kill, which then rained back down on the player. One
            // mistake, both symptoms.
            //
            // Taking the flat direction and tilting it upward by SprayLift means the swing
            // decides WHERE the blood goes and the setting decides HOW HIGH, independently. A
            // straight-down blow has no horizontal component to follow, so it falls back to up -
            // which is the only sensible answer for a blow with no sideways travel.
            Vector3 flat = new Vector3(dir.x, 0f, dir.z);
            Vector3 spray = flat.sqrMagnitude < 0.0001f ? Vector3.up : flat.normalized;
            spray = Vector3.Slerp(spray, Vector3.up, Mathf.Clamp01(Plugin.SprayLift.Value)).normalized;

            float angle = Mathf.Clamp(Plugin.SprayAngle.Value, 1f, 90f);

            foreach (ParticleSystemRenderer r in root.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                if (r == null || !IsAirborneBlood(r.sharedMaterial))
                    continue;

                ParticleSystem ps = r.GetComponent<ParticleSystem>();
                if (ps == null)
                    continue;

                // NEVER re-aim a system that reports its collisions.
                //
                // Ground decals are not painted on - ParticleDecal spawns one per particle
                // collision, so the particles have to physically reach the floor. Vanilla emits
                // them from a sphere, which sends a good share straight down; a cone aimed along
                // the swing and tilted upward sends almost none. Re-aiming these emptied the
                // ground of blood on every HIT, while deaths kept theirs because Pooling raycasts
                // and emits its pool directly rather than waiting for a collision. That is
                // exactly the pattern it produced: marks on kills, nothing while fighting.
                //
                // sendCollisionMessages is the honest test for "this one feeds the decals" - it
                // is the flag that makes Unity call OnParticleCollision at all. The systems
                // without it are free to be aimed, and they are the ones that carry the look.
                if (ps.collision.enabled && ps.collision.sendCollisionMessages)
                    continue;

                try
                {
                    ParticleSystem.ShapeModule shape = ps.shape;
                    shape.enabled = true;
                    shape.shapeType = ParticleSystemShapeType.Cone;
                    shape.angle = angle;
                    // Kept near vanilla's sphere radius so droplets still leave a wound-sized
                    // area rather than a single point.
                    shape.radius = Mathf.Max(0.05f, Plugin.SprayRadius.Value);
                    shape.radiusThickness = 1f;

                    // A cone emits along its own local +Z, so the emitter is turned to face the
                    // spray direction rather than the particles being redirected individually.
                    ps.transform.rotation = Quaternion.LookRotation(spray);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Directional spray failed: " + e.Message);
                }
            }
        }

        /// Droplets AND clouds, not droplets alone.
        ///
        /// Aiming only the droplets made this feature invisible the moment the presets shrank
        /// them to a millimetre: the thing you actually see thrown out of a wound is the cloud
        /// burst, so leaving it spherical meant the directional spray cost ground decals and
        /// bought nothing on screen. The clouds carry the look, so the clouds get aimed.
        private static bool IsAirborneBlood(Material mat)
        {
            if (mat == null || mat.name == null)
                return false;

            string n = mat.name;
            return n.StartsWith("blood_drop", StringComparison.OrdinalIgnoreCase)
                || n.StartsWith("CarturBloodDroplet", StringComparison.OrdinalIgnoreCase)
                || n.StartsWith("blood_cloud", StringComparison.OrdinalIgnoreCase)
                || n.StartsWith("CarturBloodCloud", StringComparison.OrdinalIgnoreCase);
        }
    }

    [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
    internal static class Patch_Character_ApplyDamage_Direction
    {
        private static void Prefix(HitData hit) => SprayAim.RecordHit(hit);
    }
}
