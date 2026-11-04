// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Hemalurgic

using UnityEngine;

namespace DeadEye.Core;

/// <summary>
/// Makes every revolver draw use the full "draw" animation instead of the short one.
/// Two things pick the variant, both inside PlayerAnimation.Trigger_Generic_Equip: the
/// `immediate` argument swaps its trigger for `Equip_Immediate` (a quick raise from the bottom
/// of the screen), and the ammo count decides the animator bool `IsLoaded`. The prefix turns
/// `immediate` off, the postfix forces `IsLoaded` on for an empty cylinder, and the draw's own
/// completion event hands the real value back. Cosmetic only; see docs/DEVELOPMENT.md §3.
/// </summary>
internal static class LoadedDraw
{
    /// <summary>Every revolver prefab starts with this: GEAR_Revolver, GEAR_RevolverFancy,
    /// GEAR_RevolverGreen, GEAR_RevolverStubNosed.</summary>
    private const string RevolverGearPrefix = "GEAR_Revolver";

    /// <summary>Unscaled fallback in case the completion event never arrives (draw interrupted).</summary>
    private const float SafetyRestoreSeconds = 6f;

    /// <summary>Animator state hash of the revolver reload-close state, measured in this build of
    /// the game (the DrawProbe tape prints it whenever the reload finishes, so it can be
    /// re-checked after a game update). An unknown hash makes `Play` a no-op, not a crash.</summary>
    private const int ReloadCloseStateHash = -2113718774;

    /// <summary>Where in that state the "pull the gun down and swing it back up" part starts.</summary>
    private const float ReloadSwingStart = 0.5f;

    /// <summary>Fixed cross-fade into that state, in seconds — long enough not to snap, short
    /// enough that the swing still reads as one motion.</summary>
    private const float ReloadSwingFadeSeconds = 0.12f;

    /// <summary>Length of `FPH_Revolver_Reload_Close`, so the manual equip events can be timed
    /// against the part of it that is actually played.</summary>
    private const float ReloadClipSeconds = 1.63f;

    /// <summary>Delay (real seconds) before the weapon is shown.</summary>
    private const float ShowItemDelaySeconds = 0.06f;

    /// <summary>Real seconds the swing occupies from <see cref="ReloadSwingStart"/>.</summary>
    private const float SwingSeconds = (1f - ReloadSwingStart) * ReloadClipSeconds;

    /// <summary>Grace before the state machine is consulted, so the cross-fade cannot look like
    /// "already left the swing".</summary>
    private const float SwingGraceSeconds = 0.12f;

    /// <summary>Same trigger arriving twice within this window is one draw, not two.</summary>
    private const float DuplicateTriggerSeconds = 0.25f;

    private static bool _forced;
    private static float _forcedAt;

    private static PlayerAnimation? _swingAnimation;
    private static bool _swingPending;
    private static float _swingStartedUnscaled;
    private static float _showItemAt;
    private static float _completeAt;
    private static bool _sawSwingState;

    /// <summary>The setting, read live: turning it off stops new overrides and the next tick
    /// restores the truth, but a draw already in progress finishes as it started.</summary>
    private static bool Enabled => DeadEyeMod.Settings?.ReplaceDrawAnimation ?? true;

    /// <summary>
    /// Runs BEFORE the game picks its trigger. `immediate` selects `Trigger_Generic_Equip_Immediate`
    /// instead of the normal equip trigger, which is the short draw; forcing it off gives the
    /// full draw on every path, including the ones that normally skip it.
    /// </summary>
    internal static void BeforeEquip(ref bool immediate, PlayerAnimation? animation, GearItem? gearItem)
    {
        if (animation == null || gearItem == null || !Enabled)
            return;

        try
        {
            if (!TryReadRevolver(gearItem, out string name, out int rounds))
                return;

            DrawProbe.Begin(animation, $"equip{(immediate ? "-immediate" : string.Empty)} {name} {rounds}r");

            if (!immediate)
            {
                ModLog.Debug($"draw: {name} ({rounds} rounds) taking the normal equip trigger");
                return;
            }

            immediate = false;
            ModLog.Info($"draw: {name} ({rounds} rounds) switched from the quick equip to the full draw");
        }
        catch (System.Exception ex)
        {
            ModLog.Error($"preparing the draw failed: {ex}");
        }
    }

    /// <summary>
    /// Runs AFTER the game wrote its animator values and fired the equip trigger, but before the
    /// Animator evaluates the transition. That ordering is what makes a real replacement
    /// possible: the trigger is cancelled again and the swing is cross-faded in its place, so
    /// the vanilla raise is never seen.
    /// </summary>
    internal static void AfterEquip(PlayerAnimation? animation, GearItem? gearItem)
    {
        if (animation == null || gearItem == null || !Enabled)
            return;

        try
        {
            if (!TryReadRevolver(gearItem, out string name, out int rounds))
                return;

            Animator? animator = animation.m_Animator;
            if (animator == null)
                return;

            if (rounds == 0)
            {
                // The hash is the game's own, taken from the animation component, so the
                // parameter name never has to be guessed (a wrong string does nothing).
                animator.SetBool(animation.m_AnimParameter_IsLoaded, true);
                _forced = true;
                _forcedAt = Time.unscaledTime;
            }

            // The game calls this twice per draw, and the second call arms the equip trigger
            // again — so it is cancelled on EVERY call, while the cross-fade runs only once.
            animator.ResetTrigger(animation.m_AnimParameter_Trigger_Generic_Equip);
            animator.ResetTrigger(animation.m_AnimParameter_Trigger_Generic_Equip_Immediate);

            if (_swingPending && Time.unscaledTime - _swingStartedUnscaled < DuplicateTriggerSeconds)
                return;

            StartSwing(animation, animator, name);
        }
        catch (System.Exception ex)
        {
            ModLog.Error($"starting the draw swing failed: {ex}");
        }
    }

    /// <summary>
    /// Replaces the whole draw with the motion the reload is known for: the gun is pulled down
    /// out of frame and swung back up. That motion is the second half of
    /// `FPH_Revolver_Reload_Close`, so the state starts past its cylinder-closing part.
    /// The equip trigger is reset because the game set it a few instructions earlier; its
    /// animation events (`ShowItem`, `Complete`) are then replayed by <see cref="Tick"/> at the
    /// offsets the swing reaches them, so the equip flow still settles normally.
    /// </summary>
    private static void StartSwing(PlayerAnimation animation, Animator animator, string name)
    {
        float now = Time.unscaledTime;

        animator.CrossFadeInFixedTime(ReloadCloseStateHash, ReloadSwingFadeSeconds, 0, ReloadSwingStart);

        _swingAnimation = animation;
        _swingPending = true;
        _sawSwingState = false;
        _swingStartedUnscaled = now;
        _showItemAt = now + ShowItemDelaySeconds;
        _completeAt = now + SwingSeconds;

        ModLog.Info($"draw: {name} replaced by the reload swing " +
                    $"(state {ReloadCloseStateHash}, from {ReloadSwingStart:F2} normalized)");
    }

    /// <summary>The draw finished — either the game's own event or the replay in
    /// <see cref="ReplayEquipEvents"/>: hand the animator the real `IsLoaded` back.</summary>
    internal static void OnEquipComplete(PlayerAnimation? animation) => Restore(animation);

    /// <summary>
    /// The other draw path: bringing a stowed weapon back. Nothing is overridden here — it is
    /// only sampled, because that animation is what the swap is trying to reproduce and the log
    /// needs both sides of the comparison when a session says the swap did nothing.
    /// </summary>
    internal static void OnUnstow(PlayerAnimation? animation)
    {
        if (animation == null || DeadEyeMod.Settings?.Verbose != true)
            return;

        ModLog.Info("draw probe: unstow trigger");
        DrawProbe.Begin(animation, "unstow");
    }

    internal static void Tick()
    {
        ReplayEquipEvents();
        RestoreFallback();
    }

    /// <summary>
    /// The equip clip that normally raises `ShowItem` and `Complete` never runs, so both are
    /// replayed here. Timed on the REAL clock, not the scaled one: Dead Eye equips inside its own
    /// slow motion (0.1x, then 0.01x), where a scaled 0.8 s would be a minute and a half of
    /// wall-clock time — the weapon would not reach the hands until the execution started. If the
    /// Animator itself is slowed, the state check below ends the draw early instead.
    /// </summary>
    private static void ReplayEquipEvents()
    {
        if (!_swingPending)
            return;

        PlayerAnimation? animation = _swingAnimation;
        Animator? animator = animation != null ? animation.m_Animator : null;
        if (animation == null || animator == null)
        {
            _swingPending = false;
            return;
        }

        try
        {
            float now = Time.unscaledTime;
            if (_showItemAt > 0f && now >= _showItemAt)
            {
                _showItemAt = 0f;
                animation.OnAnimationEvent_Generic_Equip_ShowItem();
            }

            bool swingOver = false;
            if (now - _swingStartedUnscaled > SwingGraceSeconds)
            {
                AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
                bool inSwing = state.fullPathHash == ReloadCloseStateHash;
                if (inSwing)
                    _sawSwingState = true;
                else if (_sawSwingState)
                    swingOver = true; // the machine already left the swing (slower bullet time)
            }

            if (now < _completeAt && !swingOver)
                return;

            _swingPending = false;
            _swingAnimation = null;
            _sawSwingState = false;
            animation.OnAnimationEvent_Generic_Equip_Complete();
        }
        catch (System.Exception ex)
        {
            _swingPending = false;
            _swingAnimation = null;
            ModLog.Error($"replaying the equip events failed: {ex}");
        }
    }

    /// <summary>Safety net for a draw that never reached its completion event, and for the draw
    /// in progress when the setting is switched off.</summary>
    private static void RestoreFallback()
    {
        if (!_forced)
            return;
        if (Enabled && Time.unscaledTime - _forcedAt < SafetyRestoreSeconds)
            return;

        PlayerAnimation? animation = null;
        try
        {
            animation = GameManager.GetPlayerAnimationComponent();
        }
        catch (System.Exception ex)
        {
            ModLog.Error($"reading the player animation component failed: {ex.Message}");
        }

        Restore(animation);
    }

    private static bool TryReadRevolver(GearItem? gearItem, out string name, out int rounds)
    {
        name = string.Empty;
        rounds = 0;

        if (gearItem == null)
            return false;

        string? gearName = gearItem.name;
        if (gearName == null || !gearName.Contains(RevolverGearPrefix))
            return false;

        GunItem? gun = gearItem.GetComponent<GunItem>();
        if (gun == null)
            return false;

        name = gearName;
        rounds = gun.NumRoundsInClip();
        return true;
    }

    private static void Restore(PlayerAnimation? animation)
    {
        if (!_forced)
            return;
        _forced = false;

        Animator? animator = animation != null ? animation.m_Animator : null;
        if (animator == null)
            return;

        int rounds = 0;
        try
        {
            GearItem? inHands = GameManager.GetPlayerManagerComponent()?.m_ItemInHands;
            GunItem? gun = inHands != null ? inHands.GetComponent<GunItem>() : null;
            if (gun != null)
                rounds = gun.NumRoundsInClip();
        }
        catch (System.Exception ex)
        {
            ModLog.Warn($"round count unreadable while restoring IsLoaded, assuming empty: {ex.Message}");
        }

        animator.SetBool(animation!.m_AnimParameter_IsLoaded, rounds > 0);
    }
}
