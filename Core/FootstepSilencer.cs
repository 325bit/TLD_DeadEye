// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Hemalurgic

using UnityEngine;

namespace DeadEye.Core;

/// <summary>
/// Silences the player's footsteps while the marking phase is up. The game already owns a flag
/// for exactly this — `FootStepSounds.m_SupressFoostepAudio` is tested at the top of its
/// footstep-sound method, and its own Audio_SuppressFootsteps action sets it — so the mod flips
/// that instead of touching audio sources or Wwise state. The value found on the first frame of
/// suppression is put back when marking ends, so a cinematic that had set it is not cleared.
/// </summary>
internal static class FootstepSilencer
{
    private static bool? _original;

    internal static void Tick()
    {
        try
        {
            FootStepSounds? footsteps = GameManager.GetFootStepSoundsComponent();
            if (footsteps == null)
                return;

            if (DeadEyeManager.State == DeadEyeState.Marking)
            {
                _original ??= footsteps.m_SupressFoostepAudio;
                footsteps.m_SupressFoostepAudio = true;
                return;
            }

            if (_original == null)
                return;

            footsteps.m_SupressFoostepAudio = _original.Value;
            _original = null;
        }
        catch (System.Exception ex)
        {
            ModLog.Error($"footstep silence failed: {ex}");
        }
    }
}
