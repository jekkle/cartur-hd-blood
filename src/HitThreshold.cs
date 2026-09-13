using System;
using HarmonyLib;
using UnityEngine;

namespace CarturHDBlood
{
    /// Makes ordinary hits bleed.
    ///
    /// Vanilla gates the hit effect on damage exceeding a tenth of the target's maximum health,
    /// in Character.ApplyDamage:
    ///
    ///     if (triggerEffects &amp;&amp; totalDamage2 &gt; GetMaxHealth() / 10f)
    ///     {
    ///         DoDamageCameraShake(hit);
    ///         if (hit.m_damage.GetTotalPhysicalDamage() &gt; 0f)
    ///             m_hitEffects.Create(hit.m_point, Quaternion.identity, base.transform);
    ///     }
    ///
    /// So a 50-damage swing on a 600 HP troll is 8.3% and produces no blood whatsoever - which
    /// is why non-killing hits on anything large look completely dry. The threshold scales with
    /// the target's health, so the bigger and tougher the creature, the less it bleeds.
    ///
    /// Rather than rewrite that arithmetic with a transpiler, this adds the missing effect from a
    /// postfix: if the hit did real physical damage but fell under vanilla's bar, play the hit
    /// effect ourselves.
    ///
    /// Double-firing is avoided by construction. The comparison uses the damage BEFORE
    /// resistances, and vanilla compares the value after them - which can only be smaller. So
    /// whenever our pre-mitigation figure is under the bar, vanilla's post-mitigation figure was
    /// under it too, and vanilla definitely did not fire.
    [HarmonyPatch(typeof(Character), nameof(Character.ApplyDamage))]
    internal static class Patch_Character_ApplyDamage
    {
        /// The hit that was most recently blocked successfully.
        ///
        /// Character.RPC_Damage calls BlockAttack and then ApplyDamage on the same HitData, in
        /// that order, synchronously - so by the time the postfix below runs, this either holds
        /// the very object it was handed or something older. Compared by reference, never by
        /// value, so an older entry cannot match a new hit; HitData instances are not pooled.
        ///
        /// One field rather than a set, because the two calls are adjacent in one method and
        /// nothing can interleave between them.
        private static HitData _lastBlocked;

        internal static void MarkBlocked(HitData hit) => _lastBlocked = hit;

        private static void Postfix(Character __instance, HitData hit, bool triggerEffects)
        {
            if (__instance == null || hit == null)
                return;
            if (!Plugin.ModEnabled.Value || !triggerEffects)
                return;

            // A hit the target blocked should not draw blood. This mod lowered the bleed
            // threshold from vanilla's tenth of max health down to a fiftieth, which is what
            // made blocked hits start bleeding: the chip damage left after a block clears the
            // new bar easily and never came close to the old one.
            if (ReferenceEquals(hit, _lastBlocked))
                return;

            float fraction = BloodPreset.Current().HitThreshold;
            if (fraction >= 0.1f)
                return;   // at or above vanilla's tenth there is nothing to add

            try
            {
                if (hit.m_damage.GetTotalPhysicalDamage() <= 0f)
                    return;   // same condition vanilla applies - no physical damage, no blood

                float maxHealth = __instance.GetMaxHealth();
                if (maxHealth <= 0f)
                    return;

                float damage = hit.GetTotalDamage();

                // Above vanilla's bar: it already played the effect, so adding one would double it.
                if (damage > maxHealth * 0.1f)
                    return;

                // Below our own bar: too glancing to bleed at all.
                if (damage <= maxHealth * fraction)
                    return;

                __instance.m_hitEffects.Create(hit.m_point, Quaternion.identity, __instance.transform);
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Hit-threshold effect failed: " + e.Message);
            }
        }
    }

    /// Records a successful block so the effect patch above can skip it.
    ///
    /// Humanoid rather than Character: BlockAttack is virtual, Harmony patches the exact method
    /// it is given, and the base implementation is not the one that runs for anything that can
    /// actually block. Every creature that blocks - the player included - is a Humanoid.
    ///
    /// Only the return value matters. BlockAttack already subtracts the blocked damage from the
    /// hit through HitData.BlockDamage, so a fully absorbed hit was never going to bleed anyway;
    /// this is about the chip damage left over when a block reduces a hit without erasing it.
    /// Named by string with explicit parameter types, because BlockAttack is protected - nameof
    /// cannot reach it from outside the class, and the argument list pins the right overload
    /// rather than leaving Harmony to guess if the game ever adds one.
    [HarmonyPatch(typeof(Humanoid), "BlockAttack", new[] { typeof(HitData), typeof(Character) })]
    internal static class Patch_Humanoid_BlockAttack
    {
        private static void Postfix(HitData hit, bool __result)
        {
            if (__result && hit != null)
                Patch_Character_ApplyDamage.MarkBlocked(hit);
        }
    }
}
