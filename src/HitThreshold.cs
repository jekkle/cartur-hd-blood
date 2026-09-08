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
        private static void Postfix(Character __instance, HitData hit, bool triggerEffects)
        {
            if (__instance == null || hit == null)
                return;
            if (!Plugin.ModEnabled.Value || !triggerEffects)
                return;

            float fraction = Plugin.HitEffectThreshold.Value;
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
}
