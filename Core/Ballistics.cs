// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Hemalurgic

using UnityEngine;

namespace DeadEye.Core;

/// <summary>
/// Lead-prediction math for the firing pipeline.
/// AdaptiveArsenal (if installed) launches its physics projectile at the table speed x 0.5
/// under gravity, so this module halves the speeds and adds a gravity-drop offset when it
/// is present; the vanilla vp_Bullet is a straight raycast and needs no compensation.
/// See docs/DEVELOPMENT.md §3/§5.
/// </summary>
internal static class Ballistics
{
    /// <summary>Set at startup: AdaptiveArsenal's ProjectileItem type was found.</summary>
    internal static bool AdaptiveBallistics { get; private set; }

    internal static void SetAdaptiveBallistics(bool present) => AdaptiveBallistics = present;

    /// <summary>
    /// Nominal muzzle velocities (m/s), same table as AdaptiveArsenal's ProjectileItem.
    /// Only meaningful as a flight-time estimate; the actual projectile direction is
    /// enforced by the redirect patches, not by this table.
    /// </summary>
    private static readonly (string Key, float Speed)[] MuzzleVelocities =
    {
        ("GEAR_RevolverFancy", 600f),
        ("GEAR_RevolverGreen", 400f),
        ("GEAR_RevolverStubNosed", 300f),
        ("GEAR_Revolver", 400f),
    };

    private const float Gravity = 9.81f;
    private const float AdaptiveScaleMultiplier = 0.5f; // ProjectileItem.ScaleMultiplier

    /// <summary>Effective launch speed for the bound revolver (halved when AdaptiveArsenal is active).</summary>
    internal static float MuzzleVelocityFor(string? gunName)
    {
        float speed = 400f;
        if (!string.IsNullOrEmpty(gunName))
        {
            foreach (var (key, s) in MuzzleVelocities)
            {
                if (gunName.Contains(key))
                {
                    speed = s;
                    break;
                }
            }
        }
        return AdaptiveBallistics ? speed * AdaptiveScaleMultiplier : speed;
    }

    /// <summary>
    /// Intercept point for a moving target: where the animal WILL be when the projectile
    /// arrives, plus a gravity-drop offset so the redirected shot lands on the body.
    /// Fixed-point iteration on the flight time (t = distance / launch speed, then lead the
    /// target by velocity * t), 3 iterations — ample for revolver speeds.
    /// <paramref name="flightTime"/> is in GAME seconds and therefore directly comparable to
    /// everything else that is scaled; it is reported for the shot diagnostics.
    /// </summary>
    internal static Vector3 PredictIntercept(Vector3 muzzlePos, Vector3 targetPos, Vector3 targetVelocity, float launchSpeed)
        => PredictIntercept(muzzlePos, targetPos, targetVelocity, launchSpeed, out _);

    internal static Vector3 PredictIntercept(Vector3 muzzlePos, Vector3 targetPos, Vector3 targetVelocity, float launchSpeed, out float flightTime)
    {
        Vector3 predicted = targetPos;
        flightTime = 0f;
        for (int i = 0; i < 3; i++)
        {
            flightTime = Vector3.Distance(muzzlePos, predicted) / Mathf.Max(1f, launchSpeed);
            predicted = targetPos + targetVelocity * flightTime;
        }
        if (AdaptiveBallistics)
            predicted += Vector3.up * (0.5f * Gravity * flightTime * flightTime);
        return predicted;
    }
}
