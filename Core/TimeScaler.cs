// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Hemalurgic

using UnityEngine;

namespace DeadEye.Core;

/// <summary>
/// Bullet-time scale owner. The scale is OWNED here (_value) and never read back from the
/// engine: TimeAccelerator assigns `Time.timeScale = 1` from an InterfaceManager.Update
/// PREFIX every frame, which stamps the slowdown back to normal and leaves a readback-based
/// lerp unable to converge. Advancing here plus re-asserting from the InterfaceManager.Update
/// POSTFIX (Patches.TimeScaleGuardPatch) runs after that writer, so the owned value wins.
/// </summary>
internal static class TimeScaler
{
    private static float _value = 1f;      // authoritative owned scale (independent of readback)
    private static float _target = 1f;
    private static float _lerpSpeed = 1f;
    private static bool _owned;

    /// <summary>True while this mod owns a non-normal scale (until ForceRestore).</summary>
    internal static bool Owned => _owned;

    /// <summary>The scale this mod intends right now (use this instead of Time.timeScale).</summary>
    internal static float Value => _value;

    internal static void LerpTo(float value, float speed)
    {
        _target = value;
        _lerpSpeed = Mathf.Max(0.05f, speed);
        _owned = true;
    }

    /// <summary>
    /// Jump straight to <paramref name="value"/>. Used when entering the execution: the
    /// player's animation speed is derived from the scale (1/scale), so ramping it would
    /// leave the Animator compensation wrong for the whole ramp — the fire gate then stays
    /// closed and the targets time out one after another.
    /// </summary>
    internal static void SetImmediate(float value)
    {
        _target = value;
        _value = value;
        _owned = true;
        Reassert();
    }

    /// <summary>Advance the lerp on unscaled time (call once per manager Tick).</summary>
    internal static void Advance()
    {
        if (!Mathf.Approximately(_value, _target))
            _value = Mathf.MoveTowards(_value, _target, Time.unscaledDeltaTime * _lerpSpeed);
        Reassert();
    }

    /// <summary>
    /// Push the owned value onto both clocks. Cheap no-op while nothing is owned, so it
    /// can be called from a per-frame postfix. Writes Time.timeScale FIRST and the global
    /// last: the game copies the global onto Time.timeScale in its own Update.
    /// </summary>
    internal static void Reassert()
    {
        if (!_owned)
            return;
        if (!Mathf.Approximately(Time.timeScale, _value))
            Time.timeScale = _value;
        GameManager.m_GlobalTimeScale = _value;
    }

    /// <summary>Hard reset. Only call when we know we own the scale.</summary>
    internal static void ForceRestore()
    {
        _target = 1f;
        _value = 1f;
        _owned = false;
        Time.timeScale = 1f;
        GameManager.m_GlobalTimeScale = 1f;
    }
}
