// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Hemalurgic

using Il2CppTLD.AI;
using UnityEngine;

namespace DeadEye.Core;

/// <summary>
/// Target velocity for the lead prediction, expressed in GAME seconds.
/// Everything in this world moves on the scaled clock, and the projectile's flight time is
/// measured on that same clock (distance / launch speed). A velocity taken on the REAL clock
/// therefore reports `v * timeScale`: a root difference divided by unscaled seconds is 80 %
/// of the truth at an execution scale of 0.8, and far less if the player lowered it. A deer
/// absorbs that error inside a 0.6 m body; a 0.2 m rabbit does not.
/// </summary>
internal static class AnimalVelocity
{
    /// <summary>Sanity ceiling for a living animal (m/s): teleports and ragdoll spikes.</summary>
    internal const float MaxPlausibleSpeed = 30f;

    /// <summary>Speed^2 below which a reading counts as "the animal stood still".</summary>
    private const float StillSpeedSq = 0.01f; // (0.1 m/s)^2

    /// <summary>
    /// The game's own velocity for this AI. Preferred over any sampling: it is already in
    /// game seconds, it needs no observation window, and it is the number the animal's own
    /// movement used this frame. <paramref name="source"/> reports `ai` even for a genuine
    /// standstill (zero is a valid answer); it is `none` only when the read failed, which is
    /// what tells the caller to fall back to the root-difference estimate.
    /// </summary>
    internal static Vector3 FromAi(BaseAi? ai, out string source)
    {
        source = "none";
        if (ai == null || ai.gameObject == null)
            return Vector3.zero;

        try
        {
            Vector3 v = ai.GetVelocity();
            if (!IsFinite(v) || v.sqrMagnitude > MaxPlausibleSpeed * MaxPlausibleSpeed)
            {
                ModLog.Debug($"ai velocity rejected ({(IsFinite(v) ? v.magnitude : float.NaN):F1} m/s)");
                return Vector3.zero;
            }

            source = "ai";
            return v;
        }
        catch (System.Exception ex)
        {
            ModLog.Debug($"ai velocity unavailable: {ex.Message}");
            return Vector3.zero;
        }
    }

    /// <summary>
    /// Velocity for an animal that may have moved on since the sample the shot was planned
    /// with: a live AI read, confirmed against the root motion. A ZERO from the AI is not
    /// proof of a standstill (it is observed on animals whose root travels 10 m/s), so a zero
    /// or a failed read is re-checked against the root difference over the same window, and
    /// only a root that also stayed put counts as still. <paramref name="fallback"/> is the
    /// last trustworthy sample, used when neither source can answer.
    /// </summary>
    internal static Vector3 Resolve(BaseAi? ai, Vector3 fromRoot, float fromGameTime,
                                    Vector3 toRoot, Vector3 fallback, out string source)
    {
        Vector3 live = FromAi(ai, out source);
        if (source == "ai" && live.sqrMagnitude > StillSpeedSq)
            return live;

        Vector3 moved = FromRootDelta(fromRoot, toRoot, Time.time - fromGameTime);
        if (moved.sqrMagnitude > StillSpeedSq)
        {
            source = source == "ai" ? "ai-zero, root" : "root-delta";
            return moved;
        }

        return source == "ai" ? live : fallback;
    }

    /// <summary>
    /// Root-position difference over a GAME-time window. Only a fallback and a cross-check
    /// for <see cref="FromAi"/>: a two-point average cannot follow a hopping rabbit, and a
    /// window of a few frames turns animation jitter into fake speed.
    /// </summary>
    internal static Vector3 FromRootDelta(Vector3 from, Vector3 to, float gameSeconds)
    {
        if (gameSeconds < 0.001f)
            return Vector3.zero;

        Vector3 v = (to - from) / gameSeconds;
        return IsFinite(v) && v.sqrMagnitude <= MaxPlausibleSpeed * MaxPlausibleSpeed
            ? v
            : Vector3.zero;
    }

    private static bool IsFinite(Vector3 v)
        => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
             float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
}
