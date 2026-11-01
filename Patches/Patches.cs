using HarmonyLib;
using UnityEngine;

namespace DeadEye.Patches;

/// <summary>
/// Vanilla path (AdaptiveArsenal absent): the hit is resolved inside vp_Bullet.Start().
/// The prefix redirects the freshly spawned bullet onto the committed aim point
/// (LastShotAimPoint, TTL 0.5 s) and zeroes its residual Accuracy spread. See §3.
/// </summary>
[HarmonyPatch(typeof(vp_Bullet), "Start")]
internal static class BulletStartPatch
{
    private static void Prefix(vp_Bullet __instance)
    {
        try
        {
            // No State check here on purpose: the LAST bullet's Start() runs a frame after
            // the state machine has returned to Inactive. LastShotAimPoint carries a short
            // TTL instead, which keeps stale manual shots (fired later than that) safe.
            Vector3? aim = DeadEye.Core.DeadEyeManager.LastShotAimPoint;
            if (!aim.HasValue)
                return;

            Transform t = __instance.transform;
            Vector3 toMark = aim.Value - t.position;
            if (toMark.sqrMagnitude < 0.0001f)
                return;

            t.forward = toMark.normalized;
            __instance.Accuracy = 0f;
            ModLog.Debug("bullet redirected onto the mark");
        }
        catch (System.Exception ex)
        {
            ModLog.Error($"bullet start patch failed: {ex}");
        }
    }
}

/// <summary>
/// Fatigue drain multiplier while Dead Eye is active (default 3x).
/// The vanilla Update already applied its own burn this frame; we add (multiplier - 1)
/// extra burn using the same per-hour rate and the same scaled delta time the game used,
/// so the drain scales naturally with the bullet-time time scale.
/// </summary>
[HarmonyPatch(typeof(Fatigue), nameof(Fatigue.Update))]
internal static class FatiguePatch
{
    private static void Postfix(Fatigue __instance)
    {
        try
        {
            if (DeadEye.Core.DeadEyeManager.State != DeadEye.Core.DeadEyeState.Marking)
                return;

            float multiplier = DeadEyeMod.Settings?.FatigueMultiplier ?? 3f;
            if (multiplier <= 1f)
                return;

            float extraBurn = __instance.m_CurrentFatigueBurnPerHour
                              * (Time.deltaTime / 3600f)
                              * (multiplier - 1f);
            __instance.m_CurrentFatigue = Mathf.Max(0f, __instance.m_CurrentFatigue - extraBurn);
        }
        catch (System.Exception ex)
        {
            ModLog.Error($"fatigue patch failed: {ex}");
        }
    }
}

/// <summary>
/// AdaptiveArsenal path: its ProjectileItem commits the launch inside its private Fire()
/// via AddForce(transform.forward x speed) — which only lands on the NEXT physics step, so
/// a postfix reading velocity sees zero and silently skips. Bound dynamically at startup
/// (TypeByName, no hard dependency). Prefix: aim the transform at the committed aim point
/// BEFORE the body runs, so AdaptiveArsenal's own launch force flies exactly there and its
/// random hip-fire spread is bypassed. See §3.
/// </summary>
internal static class AdaptiveProjectileRedirectPatch
{
    private static bool _engagedLogged;

    internal static void Prefix(object __instance)
    {
        try
        {
            if (!_engagedLogged)
            {
                ModLog.Info("AdaptiveArsenal ProjectileItem.Fire prefix engaged");
                _engagedLogged = true;
            }

            Vector3? aim = DeadEye.Core.DeadEyeManager.LastShotAimPoint;
            if (!aim.HasValue)
                return;

            var component = __instance as UnityEngine.Component;
            if (component == null)
                return;

            Vector3 toMark = aim.Value - component.transform.position;
            if (toMark.sqrMagnitude < 0.0001f)
                return;

            component.transform.rotation = Quaternion.LookRotation(toMark.normalized);
            ModLog.Debug("AdaptiveArsenal projectile aimed onto the mark");
        }
        catch (System.Exception ex)
        {
            ModLog.Error($"adaptive projectile redirect failed: {ex}");
        }
    }
}
