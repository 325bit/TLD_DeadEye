using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace DeadEye.Patches;

/// <summary>
/// Input lockout while Dead Eye owns the weapon (marking or execution state): the game
/// must not react to LMB (fire), RMB (aim) or R (reload) while the mode drives the gun.
/// Blocked at the concrete gameplay readers of the Hinterland input wrapper
/// (<c>InputManager.GetKeyDown</c> / <c>GetMouseButtonDown</c> / <c>GetReloadPressed</c>,
/// plus the fire entry <c>ProcessFireAction</c> as a second fence) — NOT at raw Unity
/// input, which is exactly what this mod's own toggle/mark/cancel keys read, so the mode
/// stays controllable. Camera, movement, scroll and every other key are untouched, and
/// every prefix is a no-op (lets the original run) while the mode is off.
/// </summary>
internal static class DeadEyeInputBlock
{
    internal static bool Active =>
        DeadEye.Core.DeadEyeManager.State is DeadEye.Core.DeadEyeState.Marking
            or DeadEye.Core.DeadEyeState.Firing;
}

/// <summary>Keyboard path: R (reload) and the mouse buttons some paths read as keys.</summary>
[HarmonyPatch(typeof(InputManager), nameof(InputManager.GetKeyDown))]
internal static class BlockGetKeyDownPatch
{
    private static bool Prefix(KeyCode key) =>
        !(DeadEyeInputBlock.Active &&
          (key == KeyCode.Mouse0 || key == KeyCode.Mouse1 || key == KeyCode.R));
}

/// <summary>Mouse path: LMB (fire) and RMB (aim).</summary>
[HarmonyPatch(typeof(InputManager), nameof(InputManager.GetMouseButtonDown))]
internal static class BlockGetMouseButtonDownPatch
{
    private static bool Prefix(int button) =>
        !(DeadEyeInputBlock.Active && (button == 0 || button == 1));
}

/// <summary>Reload wrapper (R on desktop bindings, its own gamepad action otherwise).</summary>
[HarmonyPatch(typeof(InputManager), nameof(InputManager.GetReloadPressed))]
internal static class BlockReloadPatch
{
    private static bool Prefix() => !DeadEyeInputBlock.Active;
}

/// <summary>
/// The fire entry itself, called from <c>InputManager.ProcessInput</c> every HUD frame:
/// even if a fire press slipped through a reader above, the weapon cannot be made to
/// fire. Private in the game, patched by name like the other private game methods here.
/// </summary>
[HarmonyPatch(typeof(InputManager), "ProcessFireAction")]
internal static class BlockFireActionPatch
{
    private static bool Prefix() => !DeadEyeInputBlock.Active;
}

/// <summary>
/// The AIM path — the one the 0.7.25 lockout missed. RMB is wired as the game's "alt fire"
/// action, and <c>vp_FPSPlayer.InputZoom</c> (the weapon-aim state machine, confirmed by
/// disassembly: it is the only caller of <c>vp_FPSWeapon.CanStartAiming</c>) reads it
/// through these three wrappers — press edge, hold, release edge. None of the fire or key
/// paths above touch them, which is why ironsight aiming survived the lockout. Blocking
/// all three keeps every phase of the aim gesture (press, hold, release) from reaching
/// InputZoom, so no partial aim state can start or stick.
/// </summary>
[HarmonyPatch(typeof(InputManager), nameof(InputManager.GetAltFirePressed))]
internal static class BlockAltFirePressedPatch
{
    private static bool Prefix() => !DeadEyeInputBlock.Active;
}

[HarmonyPatch(typeof(InputManager), nameof(InputManager.GetAltFire))]
internal static class BlockAltFireHeldPatch
{
    private static bool Prefix() => !DeadEyeInputBlock.Active;
}

[HarmonyPatch(typeof(InputManager), nameof(InputManager.GetAltFireReleased))]
internal static class BlockAltFireReleasedPatch
{
    private static bool Prefix() => !DeadEyeInputBlock.Active;
}

/// <summary>
/// Keeps the first-person Animator at real speed while the execution runs slowed down.
/// `PlayerAnimation.Update` writes the Animator speed itself every frame, so the value set
/// from the manager tick is overwritten before the Animator is evaluated (live log: at an
/// execution scale of 0.1 the fire gate stayed closed for 4 s per target and 3 marks
/// produced 1 shot). A postfix re-applies `1/scale x FireRateMultiplier` right after the
/// game's own write — the multiplier is the RDR2-style rapid fire. Clamped at 20x.
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
            float rate = Mathf.Max(1f, DeadEyeMod.Settings?.FireRateMultiplier ?? 1.5f);
            if (scale >= 0.99f && rate <= 1f)
                return; // nothing to compensate and no rapid fire: leave the game's own value

            // The fire animation must play at (vanilla pace x multiplier) regardless of the
            // owned world slowdown: 1/scale restores the vanilla pace, the multiplier speeds
            // it past that. At scale 0.1 x rate 4 the wanted 40 is capped at 20, trading part
            // of the multiplier for the slowdown compensation.
            __instance.m_Animator.speed = Mathf.Clamp(1f / scale * rate, 1f, 20f);
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
/// Names the collider that ate the round, and takes the hit resolution over for Dead Eye
/// rounds. AdaptiveArsenal damages only from a physics callback and destroys on ANY solid
/// contact, so "round gone, hitbox never crossed" proved a hit but not on what. This prefix
/// logs the identity, the material tag, the player distance and impact speed; for Dead Eye
/// rounds it then resolves the contact itself (<see cref="Core.HomingSteering.ResolveContact"/>) and returns
/// false to skip AdaptiveArsenal's handler. Manual rounds return true and keep its behaviour.
/// </summary>
internal static class AdaptiveCollisionProbe
{
    internal static bool Prefix(object __instance, Collision collision)
    {
        try
        {
            Collider? collider = collision?.collider;
            if (collider == null || collider.gameObject == null)
                return true; // unattributable contact: leave AdaptiveArsenal's handler in charge

            Component? round = __instance as Component;
            Vector3 impact = round != null ? round.transform.position : collider.transform.position;

            float distance = -1f;
            float speed = -1f;
            try
            {
                distance = Vector3.Distance(GameManager.GetPlayerTransform().position, impact);
            }
            catch (System.Exception) { }

            var body = round != null ? round.GetComponent<Rigidbody>() : null;
            if (body != null)
                speed = body.velocity.magnitude;

            ModLog.Info($"projectile probe: round destroyed, {Core.HomingSteering.DescribeHitbox(collider)}, " +
                         $"material {Utils.GetMaterialTagForObjectAtPosition(collider.gameObject, impact)}, " +
                         $"{(distance >= 0f ? $"{distance:F1} m from the player" : "player distance unreadable")}, " +
                         $"{(speed >= 0f ? $"v {speed:F0} m/s" : "speed unreadable")}");
        }
        catch (System.Exception ex)
        {
            ModLog.Error($"projectile probe failed: {ex.Message}");
        }

        // Takeover: Dead Eye rounds are resolved here and AdaptiveArsenal's handler is
        // skipped — its pipeline depends on physics contacts the animals' animated colliders
        // do not produce, and its deferred Destroy let one stationary round re-damage on
        // every contact (§10 #25). Manual rounds keep the original behaviour.
        try
        {
            if (__instance is Component round && collision != null)
            {
                Collider? collider = collision.collider;
                Vector3 point = round.transform.position;
                if (collider != null)
                {
                    var contacts = collision.contacts;
                    if (contacts != null && contacts.Length > 0)
                        point = contacts[0].point;
                    if (Core.HomingSteering.ResolveContact(round, collider, point))
                        return false;
                }
            }
        }
        catch (System.Exception ex)
        {
            ModLog.Error($"contact takeover failed: {ex.Message}");
        }

        return true;
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
            // starting guess once steering is on). The target's frozen velocity rides along
            // so the steered point is led too, and the intercept rides along so the first
            // frame can cancel the muzzle error from the real spawn position.
            var target = DeadEye.Core.DeadEyeManager.PendingHomingTarget;
            if (target != null)
            {
                var body = component.GetComponent<Rigidbody>();
                if (body != null)
                    DeadEye.Core.HomingSteering.Register(
                        body, target, DeadEye.Core.DeadEyeManager.PendingHomingBodyOffset,
                        DeadEye.Core.DeadEyeManager.PendingHomingSmallPrey,
                        DeadEye.Core.DeadEyeManager.PendingHomingVelocity,
                        DeadEye.Core.DeadEyeManager.PendingLaunchSpeed);
            }

            ModLog.Debug("AdaptiveArsenal projectile aimed onto the mark");
        }
        catch (System.Exception ex)
        {
            ModLog.Error($"adaptive projectile redirect failed: {ex}");
        }
    }
}

/// <summary>
/// Draw-animation swap (see <see cref="Core.LoadedDraw"/>). The prefix turns the game's
/// `immediate` quick-draw flag off (it swaps the trigger for `Equip_Immediate`, the short raise
/// from the bottom of the screen); the postfix then overrides the `IsLoaded` animator bool the
/// game derives from the cylinder's round count. Neither the trigger itself nor the ammo count
/// is touched, so the equip flow and the weapon state are unaffected.
/// </summary>
[HarmonyPatch(typeof(PlayerAnimation), nameof(PlayerAnimation.Trigger_Generic_Equip))]
internal static class EquipDrawPatch
{
    private static void Prefix(PlayerAnimation __instance, GearItem gearItem, ref bool immediate)
        => Core.LoadedDraw.BeforeEquip(ref immediate, __instance, gearItem);

    private static void Postfix(PlayerAnimation __instance, GearItem gearItem)
        => Core.LoadedDraw.AfterEquip(__instance, gearItem);
}

/// <summary>The draw's own completion event — where the animator gets the real value back.
/// Private in the game, public in the Il2CppInterop stub.</summary>
[HarmonyPatch(typeof(PlayerAnimation), "OnAnimationEvent_Generic_Equip_Complete")]
internal static class EquipDrawCompletePatch
{
    private static void Postfix(PlayerAnimation __instance) => Core.LoadedDraw.OnEquipComplete(__instance);
}

/// <summary>Diagnostics only: samples the animation the stowed-weapon draw plays, so a session
/// can compare it with the equip draw the swap replaces.</summary>
[HarmonyPatch(typeof(PlayerAnimation), nameof(PlayerAnimation.Trigger_Generic_Unstow))]
internal static class UnstowProbePatch
{
    private static void Postfix(PlayerAnimation __instance) => Core.LoadedDraw.OnUnstow(__instance);
}

/// <summary>
/// Diagnostics only: names every trigger the game fires. `__args` is used instead of named
/// parameters on purpose — the interop stub's parameter names are generated, and a mismatch
/// would make Harmony reject the patch (taking the whole PatchAll down with it).
/// </summary>
[HarmonyPatch(typeof(PlayerAnimation), "SetTrigger")]
internal static class AnimTriggerWritePatch
{
    private static void Prefix(PlayerAnimation __instance, object[] __args)
        => Core.DrawProbe.OnWrite(__instance, "trigger", __args.Length > 0 ? (int)__args[0] : 0, string.Empty);
}

/// <summary>Diagnostics only: weapon-related bools (IsLoaded, IsEquipped, IsWeapon, …).</summary>
[HarmonyPatch(typeof(PlayerAnimation), "SetBool")]
internal static class AnimBoolWritePatch
{
    private static void Prefix(PlayerAnimation __instance, object[] __args)
    {
        if (__args.Length < 2)
            return;
        Core.DrawProbe.OnWrite(__instance, "bool", (int)__args[0], (bool)__args[1] ? "true" : "false");
    }
}

/// <summary>Diagnostics only: weapon-related ints (RoundsLoaded, ReloadCount, WeaponId, …).</summary>
[HarmonyPatch(typeof(PlayerAnimation), "SetInt")]
internal static class AnimIntWritePatch
{
    private static void Prefix(PlayerAnimation __instance, object[] __args)
    {
        if (__args.Length < 2)
            return;
        Core.DrawProbe.OnWrite(__instance, "int", (int)__args[0], ((int)__args[1]).ToString());
    }
}

/// <summary>
/// Silent mirror of the game's input-context stack: every panel's Enable pops the gameplay
/// context ('WorldView') and the panel's close path is supposed to push it back. The mirror
/// records that ownership without logging anything; <see cref="WorldViewContextRestore"/>
/// uses it to tell "a panel owns input" from "the stack is empty and input is still refused".
/// </summary>
internal static class ContextStackMirror
{
    private static readonly List<string> _stack = new();

    internal static bool IsEmpty => _stack.Count == 0;

    internal static void Push(MonoBehaviour context)
    {
        try { _stack.Add(context != null ? context.gameObject.name : "null"); }
        catch { }
    }

    internal static void Pop(MonoBehaviour context)
    {
        try { if (_stack.Count > 0) _stack.RemoveAt(_stack.Count - 1); }
        catch { }
    }
}

[HarmonyPatch(typeof(InputManager), nameof(InputManager.PushContext))]
internal static class PushContextMirrorPatch
{
    private static void Prefix(MonoBehaviour __0) => ContextStackMirror.Push(__0);
}

[HarmonyPatch(typeof(InputManager), nameof(InputManager.PopContext))]
internal static class PopContextMirrorPatch
{
    private static void Prefix(MonoBehaviour __0) => ContextStackMirror.Pop(__0);
}

/// <summary>
/// Restores a lost gameplay input context. The game's input readers are gated by
/// HasContext, which refuses while (a) the static m_InputDisableTime is ahead of Time.time —
/// a 0.06 s window InputManager.PopContext sets on every panel close — or (b) the caller's
/// context is absent from the static m_InputContexts list. Every Panel_*.Enable pops the
/// gameplay context ('WorldView') and the panel's close path is supposed to push it back;
/// that close path can be interrupted while Dead Eye owns the time scale, and because the
/// list is STATIC it survives scene loads, so later sessions can start with the gameplay
/// context missing: reload, aim and every other gated reader refuse silently while fire
/// (ungated) keeps working. This postfix watches the game's own continuous
/// HasContext('WorldView') polling; when the refusal — membership, with the 0.06 s time gate
/// clear — persists over 1 s while Dead Eye is fully inactive, the HUD is up and no panel
/// owns input, the CURRENT WorldView instance is re-pushed with the game's own API, once
/// per episode.
/// </summary>
[HarmonyPatch]
internal static class WorldViewContextRestore
{
    private static readonly Dictionary<int, string> _names = new();
    private static float _firstFalseReal = -1f;
    private static float _nextLog;
    private static float _nextRestore;
    private static int RestoreCount;

    private static System.Reflection.MethodBase TargetMethod()
    {
        foreach (var m in AccessTools.GetDeclaredMethods(typeof(InputManager)))
            if (m.Name == "HasContext" && m.GetParameters().Length == 1)
                return m;
        foreach (var m in AccessTools.GetDeclaredMethods(typeof(InputManager)))
            if (m.Name == "HasContext")
                return m;
        return AccessTools.Method(typeof(InputManager), nameof(InputManager.Process));
    }

    private static void Postfix(MonoBehaviour __0, bool __result)
    {
        try
        {
            if (__result)
            {
                _firstFalseReal = -1f;
                return;
            }

            int id = 0;
            try { id = __0 != null ? __0.GetInstanceID() : 0; } catch { }
            if (id == 0)
                return;
            if (!_names.TryGetValue(id, out string? name))
            {
                try { name = __0.gameObject.name ?? "null"; }
                catch { name = "destroyed"; }
                _names[id] = name;
            }
            name ??= "null";

            if (name != "WorldView")
                return;

            float disableTime;
            try { disableTime = InputManager.m_InputDisableTime; }
            catch { return; }
            if (disableTime > Time.time)
            {
                _firstFalseReal = -1f; // the 0.06 s pop window: legitimate, self-clearing
                return;
            }

            float now = Time.unscaledTime;
            if (_firstFalseReal < 0f)
                _firstFalseReal = now;

            if (DeadEye.Core.DeadEyeManager.State == DeadEye.Core.DeadEyeState.Inactive &&
                DeadEye.Core.DeadEyeManager.IsGameplayActive() &&
                ContextStackMirror.IsEmpty &&
                now - _firstFalseReal > 1f &&
                now >= _nextRestore)
            {
                _nextRestore = now + 10f;
                RestoreCount++;
                InputManager.PushContext(__0);
                ModLog.Warn($"input context restored: WorldView was missing from the static list — " +
                            $"PushContext issued (restore #{RestoreCount}); reload and every gated " +
                            $"reader should recover immediately");
            }
        }
        catch
        {
            // a destroyed context argument must not break the game's input path
        }
    }
}
