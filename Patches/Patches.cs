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
/// Fatigue cost while Dead Eye is active: a percentage of the full bar per real-time
/// second (default 3, i.e. the bar lasts ~33 s). IMPORTANT: m_CurrentFatigue is
/// TIREDNESS — higher means more exhausted (the game's own Update ADDS the per-hour burn
/// via AddFatigue, and IsExhausted is current &gt; threshold); the HUD rest bar is its
/// inverse. Paying fatigue therefore ADDS to m_CurrentFatigue, relative to m_MaxFatigue.
/// UNSCALED delta time keeps the cost predictable regardless of the bullet-time scale.
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

            float perSecond = DeadEyeMod.Settings?.FatiguePerSecond ?? 1f;
            if (perSecond <= 0f)
                return;

            float cost = (perSecond / 100f) * __instance.m_MaxFatigue * Time.unscaledDeltaTime;
            __instance.m_CurrentFatigue = Mathf.Min(
                __instance.m_MaxFatigue, __instance.m_CurrentFatigue + cost);
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
