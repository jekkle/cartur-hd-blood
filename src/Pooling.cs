using System;
using UnityEngine;

namespace CarturHDBlood
{
    /// A settled pool of blood under a kill.
    ///
    /// Every mark the mod produces so far is an impact - the instant of the hit. Nothing reads as
    /// blood that has run out of a body and spread, because vanilla has no such concept: decals
    /// are emitted per particle collision, at the point of collision, at impact size.
    ///
    /// This adds one on death by emitting extra particles directly into the death effect's own
    /// decal system, with a large size and a long lifetime. Reusing that system rather than
    /// building a new one matters: it inherits the shader, the material, the per-creature blood
    /// colour and the ageing gradient, so a pool dries and fades exactly like the rest.
    ///
    /// The growth comes for free from the size-over-lifetime curve, because Unity normalises that
    /// curve to each particle's own lifetime. The same curve that makes a 10-second splat land in
    /// half a second makes a 40-second pool spread over two and a half.
    internal static class Pooling
    {
        // Cached because a mask built from names is not free and this runs per death.
        private static int _groundMask;
        private static bool _maskResolved;

        /// Effect instances that have already produced a pool.
        ///
        /// One pool per death, not one per decal. Several death effects carry more than one
        /// ParticleDecal - vfx_neck_death and vfx_troll_death have two, fx_deatsquito_death has
        /// three - and this runs from each decal's Awake, so without this a single kill would lay
        /// down two or three overlapping pools of PoolCount decals each.
        private static readonly System.Collections.Generic.HashSet<int> Pooled =
            new System.Collections.Generic.HashSet<int>();

        /// Cleared on world load. Instance ids are unique within a session, so this only exists
        /// to stop the set growing across worlds.
        public static void Clear() => Pooled.Clear();

        /// Emits the pool. Called from a decal's Awake when it belongs to a death effect.
        public static void SpawnPool(ParticleDecal decal)
        {
            if (!Plugin.PoolOnDeath.Value)
                return;

            ParticleSystem system = decal.m_decalSystem;
            if (system == null)
                return;

            try
            {
                // One pool per effect instance, keyed on the spawned effect's root.
                Transform effectRoot = decal.transform.root;
                if (effectRoot == null || !Pooled.Add(effectRoot.gameObject.GetInstanceID()))
                    return;

                Vector3 origin = decal.transform.position;

                // Find the ground beneath the kill. A pool has to lie on the surface, and the
                // effect spawns at the creature's origin - which for a large creature can be well
                // above it.
                if (!TryFindGround(origin, out Vector3 point, out Vector3 normal))
                    return;

                // The debug multipliers are 1 unless the debug menu is switched on, so this reads
                // exactly as before for anyone who never opens it. Both are still clamped to the
                // same 1-8 and the same units as the section 2 settings they multiply.
                BloodPreset preset = BloodPreset.Current();
                int count = Mathf.Clamp(
                    Mathf.RoundToInt(preset.PoolCount * DebugTuning.PoolCountMultiplier()), 1, 8);
                // Pool size follows the dead creature's actual collider height, so PoolSize means
                // "the pool for a greydwarf-sized kill".
                //
                // This was first written to scale by the effect's authored ground-decal size, which
                // looked principled and was wrong: those decals are shared child prefabs, so a boar
                // reads as LARGER than a greydwarf and got a 5-unit pool against the greydwarf's
                // 3.2 - visibly backwards, since the greydwarf is the taller creature. See
                // CreatureSize for the measurement that replaced it.
                float scale = CreatureSize.Scale();

                float size = preset.PoolSize * DebugTuning.PoolSizeMultiplier() * scale;
                // Absolute seconds, NOT scaled by the preset's lifetime multiplier.
                //
                // It used to be scaled, on the grounds that the config description promised it.
                // That was the wrong call: every other lifetime in the mod multiplies a value the
                // GAME authored, where a multiplier is the only way to express "a bit longer than
                // vanilla". PoolLifetime is a number the owner types in seconds, and a setting that
                // reads 45 has to mean 45. At GroundBlood 0.3 the multiplier was 0.3, so a pool
                // asked to last 45 seconds was vanishing in 13.5 - and the setting gave no hint
                // why. The config description is corrected to match.
                float life = Plugin.PoolLifetime.Value;

                for (int i = 0; i < count; i++)
                {
                    // Same orientation maths ParticleDecal itself uses, so a pool sits flush to
                    // the surface exactly as an impact decal does, with a random spin.
                    Quaternion look = Quaternion.LookRotation(normal);
                    Vector3 euler = look.eulerAngles;
                    euler.x = 0f - euler.x + 180f;
                    euler.y = 0f - euler.y;
                    euler.z = UnityEngine.Random.Range(0, 360);

                    // Spread slightly, so several pools read as one irregular mass rather than
                    // concentric stamps.
                    Vector3 jitter = Vector3.zero;
                    if (i > 0)
                    {
                        float a = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                        float d = UnityEngine.Random.Range(0.15f, 0.5f) * size;
                        jitter = new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                    }

                    var p = new ParticleSystem.EmitParams
                    {
                        position = point + normal * 0.002f + jitter,
                        rotation3D = euler,
                        velocity = -normal * 0.001f,
                        startSize = size * UnityEngine.Random.Range(0.8f, 1.25f),
                        startLifetime = life * UnityEngine.Random.Range(0.85f, 1.15f),
                    };

                    system.Emit(p, 1);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Could not spawn blood pool: " + e.Message);
            }
        }

        /// Ground point and normal below a position.
        private static bool TryFindGround(Vector3 origin, out Vector3 point, out Vector3 normal)
        {
            point = origin;
            normal = Vector3.up;

            if (!_maskResolved)
            {
                _maskResolved = true;
                // Valheim's own solid layers. Falling back to the default mask rather than to
                // everything, so a rename upstream degrades to "probably right" instead of
                // hitting characters and projectiles.
                _groundMask = LayerMask.GetMask("terrain", "static_solid", "piece", "Default", "vehicle");
                if (_groundMask == 0)
                    _groundMask = Physics.DefaultRaycastLayers;
            }

            // Start above the origin: a death effect can spawn slightly inside the ground, and a
            // ray beginning below the surface finds nothing.
            Vector3 from = origin + Vector3.up * 1.5f;
            if (Physics.Raycast(from, Vector3.down, out RaycastHit hit, 6f, _groundMask,
                                QueryTriggerInteraction.Ignore))
            {
                point = hit.point;
                normal = hit.normal;
                return true;
            }

            return false;
        }
    }
}
