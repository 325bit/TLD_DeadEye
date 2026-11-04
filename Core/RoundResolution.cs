// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Hemalurgic

using Il2CppTLD.AI;
using Il2CppTLD.Stats;
using UnityEngine;

namespace DeadEye.Core;

/// <summary>
/// Dead Eye's own hit resolution. AdaptiveArsenal damages only from a physics callback, and
/// the live log proved the flying round never receives one from an animal's animated
/// colliders (a 0.02 m pass with zero contacts), so every hit would be luck. This replicates
/// AdaptiveArsenal's TryInflictDamage verbatim — distance falloff, LocalizedDamage scale and
/// bleed-out, the critical-hit roll, the stat and skill point — plus its impact effects.
/// Cosmetic steps degrade silently; the damage call must not depend on them.
/// </summary>
internal static class RoundResolution
{
    private const float Damage = 400f;
    private const float MinDamage = 200f;
    private const float MaxRange = 5000f;

    /// <summary>Layer 16 carries BaseAi on the object, 27 on a parent; anything else is
    /// ignored, and LocalizedDamage must be present — the filter AdaptiveArsenal applies.</summary>
    internal static bool IsDamageable(Collider collider)
    {
        try
        {
            GameObject go = collider.gameObject;
            int layer = go.layer;
            BaseAi? ai = layer == 16
                ? go.GetComponent<BaseAi>()
                : layer == 27 ? go.transform.GetComponentInParent<BaseAi>() : null;
            return ai != null && go.GetComponent<LocalizedDamage>() != null;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    /// <summary>The weapon table the damage rolls against, read from the round's own ammo.</summary>
    internal static WeaponSource WeaponSourceOf(Component? round)
    {
        try
        {
            AmmoItem? ammo = round != null ? round.GetComponent<AmmoItem>() : null;
            return ammo != null && ammo.m_AmmoForGunType == GunType.Rifle
                ? WeaponSource.Rifle
                : WeaponSource.Revolver;
        }
        catch (System.Exception)
        {
            return WeaponSource.Revolver;
        }
    }

    /// <summary>
    /// Applies the damage pipeline to the collider the round was resolved on. Returns true
    /// when damage was applied (the collider is a damageable body part of a live animal);
    /// false means the round hit scenery or a harmless volume.
    /// <paramref name="headPart"/> is set for a HEAD mark: the geometry still has to land on a
    /// damageable capsule, but the numbers (damage scale, bleed-out, critical chance) and the
    /// hit animation are taken from the head instead of the capsule the round happened to
    /// touch. Without it the row of that capsule is used, exactly as before.
    /// </summary>
    internal static bool ApplyDamage(Collider collider, Vector3 hitPoint, Vector3 spawnPosition,
                                     WeaponSource weapon, out float damage,
                                     LocalizedDamage? headPart = null, Vector3 headPoint = default)
    {
        damage = 0f;
        try
        {
            GameObject go = collider.gameObject;
            int layer = go.layer;
            BaseAi? ai = layer == 16
                ? go.GetComponent<BaseAi>()
                : layer == 27 ? go.transform.GetComponentInParent<BaseAi>() : null;
            LocalizedDamage? localized = go.GetComponent<LocalizedDamage>();
            if (ai == null || localized == null)
                return false;

            // A head mark reads the HEAD row whatever the round touched; the animation follows
            // the same component, so a chest contact still plays the head reaction rather than
            // showing a body-part hit the player did not make.
            LocalizedDamage data = headPart ?? localized;
            Vector3 animPoint = headPart != null ? headPoint : hitPoint;

            float bleedOutMinutes = data.GetBleedOutMinutes(weapon);
            float scale = data.GetDamageScale(weapon);
            float distance = Vector3.Distance(spawnPosition, hitPoint);
            damage = Mathf.Lerp(Damage, MinDamage, Mathf.Clamp01(distance / MaxRange)) * scale;

            // AdaptiveArsenal rolls the RIFLE critical-hit table regardless of the gun;
            // kept verbatim so a Dead Eye hit costs the same chance as the same AA shot.
            if (!ai.m_IgnoreCriticalHits && data.RollChanceToKill(WeaponSource.Rifle))
                damage = float.PositiveInfinity;

            if (ai.GetAiMode() != AiMode.Dead)
            {
                bool rifle = weapon == WeaponSource.Rifle;
                StatsManager.IncrementValue(rifle ? StatID.RifleShot : StatID.RevolverShot);
                GameManager.GetSkillsManager().IncrementPointsAndNotify(
                    rifle ? SkillType.Rifle : SkillType.Revolver, 1,
                    SkillsManager.PointAssignmentMode.AssignOnlyInSandbox);
            }

            ai.SetupDamageForAnim(animPoint, GameManager.GetPlayerTransform().position, data);
            ai.ApplyDamage(damage, bleedOutMinutes, DamageSource.Player, go.name);
            return true;
        }
        catch (System.Exception ex)
        {
            ModLog.Error($"hit resolution failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// The impact puff, decal and audio AdaptiveArsenal plays on contact, replayed at the
    /// resolved hit point. Every step is optional: a missing API degrades to no effect
    /// rather than to a lost hit.
    /// </summary>
    internal static void SpawnImpactEffects(Collider collider, Vector3 position, Vector3 forward)
    {
        try
        {
            GameObject go = collider.gameObject;
            string tag = Utils.GetMaterialTagForObjectAtPosition(go, position);
            BulletImpactEffectType effectType = vp_Bullet.GetImpactEffectTypeBasedOnMaterial(tag);
            if (effectType == BulletImpactEffectType.BulletImpactEffect_Untagged)
                effectType = BulletImpactEffectType.BulletImpactEffect_Stone;

            GameManager.GetEffectPoolManager().GetBulletImpactEffectPool()
                .SpawnUntilParticlesDone(effectType, position, Quaternion.LookRotation(forward));
            GameManager.GetDynamicDecalsManager().AddImpactDecal(
                ProjectileType.Bullet,
                ImpactDecals.MapBulletImpactEffectTypeToMaterialEffectType(effectType),
                position, forward);

            GameAudioManager.SetMaterialSwitch(tag, go);
            GameObject emitter = GameAudioManager.GetSoundEmitterFromGameObject(go);
            AkSoundEngine.PostEvent("Play_BulletImpacts", emitter);
            GameAudioManager.SetAudioSourceTransform(go, go.transform);
        }
        catch (System.Exception)
        {
            // cosmetic only
        }
    }
}
