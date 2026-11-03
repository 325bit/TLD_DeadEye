using UnityEngine;

namespace DeadEye.Core;

/// <summary>
/// Makes every revolver draw use the loaded-cylinder animation, whatever the cylinder holds.
/// PlayerAnimation.Trigger_Generic_Equip picks the variant: it computes IsLoaded =
/// NumRoundsInClip() &gt; 0 and writes that animator bool right before firing the equip
/// trigger, so an empty cylinder plays a shorter raise. A postfix overwrites that single value
/// after the game wrote it and before the Animator evaluates the transition; the real value is
/// returned when the draw finishes. Cosmetic only; see docs/DEVELOPMENT.md §3 / §10 #37.
/// </summary>
internal static class LoadedDraw
{
    /// <summary>Every revolver prefab starts with this: GEAR_Revolver, GEAR_RevolverFancy,
    /// GEAR_RevolverGreen, GEAR_RevolverStubNosed.</summary>
    private const string RevolverGearPrefix = "GEAR_Revolver";

    /// <summary>Unscaled fallback in case the completion event never arrives (draw interrupted).</summary>
    private const float SafetyRestoreSeconds = 6f;

    private static bool _forced;
    private static float _forcedAt;

    /// <summary>The setting, read live: turning it off stops new overrides and the next draw
    /// restores the truth, but a draw already in progress finishes as it started.</summary>
    private static bool Enabled => DeadEyeMod.Settings?.ReplaceDrawAnimation ?? true;

    internal static void OnEquipTriggered(PlayerAnimation? animation, GearItem? gearItem)
    {
        if (animation == null || gearItem == null)
            return;

        try
        {
            if (!Enabled)
                return;

            string? name = gearItem.name;
            if (name == null || !name.Contains(RevolverGearPrefix))
                return;

            GunItem? gun = gearItem.GetComponent<GunItem>();
            if (gun == null)
                return;

            int rounds = gun.NumRoundsInClip();
            if (rounds > 0)
                return; // already loaded: the game picks the wanted draw by itself

            Animator? animator = animation.m_Animator;
            if (animator == null)
                return;

            // The hash is the game's own, taken from the animation component, so the parameter
            // name never has to be guessed (a wrong string silently does nothing).
            animator.SetBool(animation.m_AnimParameter_IsLoaded, true);
            _forced = true;
            _forcedAt = Time.unscaledTime;
            ModLog.Info($"empty revolver draw switched to the loaded animation ({name}, 0 rounds)");
        }
        catch (System.Exception ex)
        {
            ModLog.Error($"forcing the loaded draw failed: {ex}");
        }
    }

    /// <summary>The draw finished: hand the animator its real value back.</summary>
    internal static void OnEquipComplete(PlayerAnimation? animation) => Restore(animation);

    /// <summary>Safety net for a draw that never reached its completion event, and for the draw
    /// in progress when the setting is switched off.</summary>
    internal static void Tick()
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
