using HarmonyLib;
using UnityEngine;

namespace DeadEye.Patches;

/// <summary>
/// Keeps the first-person Animator at real speed while the execution runs slowed down.
/// `PlayerAnimation.Update` writes the Animator speed itself every frame, so the value set
/// from the manager tick is overwritten before the Animator is evaluated (live log: at an
/// execution scale of 0.1 the fire gate stayed closed for 4 s per target and 3 marks
/// produced 1 shot). A postfix re-applies `1/scale` right after the game's own write.
/// Clamped at 20x, which covers the whole execution-scale range (0.05-1).
/// </summary>
[HarmonyPatch(typeof(PlayerAnimation), "Update")]
internal static class PlayerAnimSpeedPatch
{
    private static void Postfix(PlayerAnimation __instance)
    {
        try
        {
            if (DeadEye.Core.DeadEyeManager.State != DeadEye.Core.DeadEyeState.Firing)
                return;
            if (__instance == null || __instance.m_Animator == null)
                return;

            float scale = DeadEye.Core.TimeScaler.Value;
            if (scale >= 0.99f)
                return;

            __instance.m_Animator.speed = Mathf.Clamp(1f / scale, 1f, 20f);
        }
        catch (System.Exception ex)
        {
            ModLog.Error($"animator speed patch failed: {ex}");
        }
    }
}

/// <summary>
/// Re-asserts the owned bullet-time scale after every InterfaceManager.Update. Other mods
/// write Time.timeScale from a PREFIX on this very method (TimeAccelerator sets
/// `Time.timeScale = 1` on every frame while its key is not held), which stamped our
/// slowdown back to normal between ticks — the "time stop stopped working" report. A
/// postfix here is guaranteed to run after any prefix writer on the same method, and it is
/// a cheap no-op while we own nothing.
/// </summary>
[HarmonyPatch(typeof(InterfaceManager), "Update")]
internal static class TimeScaleGuardPatch
{
    private static void Postfix()
    {
        try
        {
            DeadEye.Core.TimeScaler.Reassert();
        }
        catch (System.Exception ex)
        {
            ModLog.Error($"time scale guard failed: {ex}");
        }
    }
}

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

            // Homing: hand this projectile to the steering list with the animal it should
            // chase, so it keeps correcting after launch (lead prediction is only the
            // starting guess once steering is on). Small prey converge on the body centre.
            var target = DeadEye.Core.DeadEyeManager.PendingHomingTarget;
            if (target != null)
            {
                var body = component.GetComponent<Rigidbody>();
                if (body != null)
                    DeadEye.Core.HomingSteering.Register(
                        body, target, DeadEye.Core.DeadEyeManager.PendingHomingOffset,
                        DeadEye.Core.DeadEyeManager.PendingHomingSmallPrey);
            }

            ModLog.Debug("AdaptiveArsenal projectile aimed onto the mark");
        }
        catch (System.Exception ex)
        {
            ModLog.Error($"adaptive projectile redirect failed: {ex}");
        }
    }
}
