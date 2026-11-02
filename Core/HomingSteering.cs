using System.Collections.Generic;
using UnityEngine;

namespace DeadEye.Core;

/// <summary>
/// Steering for bullets that chase their mark after leaving the barrel (AdaptiveArsenal
/// physics projectile only; the vanilla vp_Bullet resolves its hit inside Start()).
/// Each keeps its OWN target, so an in-flight round never retargets on sequence advance.
/// Aiming never uses the mark's local body point: it sweeps sideways when the animal
/// turns and the bullet chases a jump. Both species use root + a mark-derived height, with
/// per-species clamps; corrections split over frames so the path reads as a curve.
/// </summary>
internal static class HomingSteering
{
    private const float BaseTurnDegPerSec = 400f; // x the strength setting (0.5-2.5 -> 200-1000 deg/s)
    private const float CorrectionGain = 0.4f;    // fraction of the remaining angle corrected per frame
    private const float LifetimeSeconds = 8f;     // unscaled: a long shot under the slowdown can take seconds
    private const float MinSpeed = 1f;            // below this the launch force has not landed yet

    private sealed class Entry
    {
        internal Rigidbody Body = null!;
        internal GameObject Target = null!;
        internal Vector3 LocalOffset;
        internal float AimHeight;
        internal bool SmallPrey;
        internal float ExpiresAt;
        internal bool Logged;
    }

    private static readonly List<Entry> _entries = new();

    internal static void Register(Rigidbody body, GameObject target, Vector3 localOffset, bool smallPrey)
    {
        _entries.Add(new Entry
        {
            Body = body,
            Target = target,
            LocalOffset = localOffset,
            AimHeight = AnimalBody.LargeAimHeight(localOffset),
            SmallPrey = smallPrey,
            ExpiresAt = Time.unscaledTime + LifetimeSeconds,
        });
        ModLog.Debug($"homing projectile registered ({(smallPrey ? "small prey: inside the body" : "large animal: root + height")})");
    }

    /// <summary>
    /// Same aim point the shot was fired at, so the round does not bend toward a second
    /// opinion on the way: inside the body for prey, root + height for large animals.
    /// </summary>
    private static Vector3 AimPointOf(Entry entry)
        => entry.SmallPrey
            ? AnimalBody.PreyAimPoint(entry.Target.transform, entry.LocalOffset)
            : entry.Target.transform.position + Vector3.up * entry.AimHeight;

    /// <summary>
    /// Called every Tick, in any state: bullets fired by the sequence are still in the air
    /// after it ends. Steering runs on SCALED delta time on purpose — the bullet lives in
    /// the slowed world, so its turn rate must slow down with it.
    /// </summary>
    internal static void Tick(float strength)
    {
        if (_entries.Count == 0)
            return;

        float turnRate = BaseTurnDegPerSec * Mathf.Clamp(strength, 0.2f, 3f) * Mathf.Deg2Rad;
        float nowReal = Time.unscaledTime;

        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            Entry entry = _entries[i];
            try
            {
                if (nowReal > entry.ExpiresAt || entry.Body == null || entry.Target == null)
                {
                    _entries.RemoveAt(i);
                    continue;
                }

                Vector3 velocity = entry.Body.velocity;
                float speed = velocity.magnitude;
                if (speed < MinSpeed)
                    continue; // still waiting for AdaptiveArsenal's launch force to land

                Vector3 toTarget = AimPointOf(entry) - entry.Body.transform.position;
                if (toTarget.sqrMagnitude < 0.0001f)
                    continue;

                Vector3 dir = velocity / speed;
                Vector3 aim = toTarget.normalized;
                float errorDeg = Vector3.Angle(dir, aim);
                float errorRad = errorDeg * Mathf.Deg2Rad;

                if (!entry.Logged)
                {
                    // Triage line: how far off the spawn direction already was. A large
                    // angle means the visible curve is the initial aim, not the steering.
                    entry.Logged = true;
                    ModLog.Debug($"homing engaged: initial error {errorDeg:F1}deg, speed {speed:F0} m/s, strength {strength:F2}");
                }

                // Many small steps instead of one snap: correct a fraction of the remaining
                // angle per frame, capped by the strength-derived turn rate. Small errors
                // therefore barely bend the flight at all.
                float step = Mathf.Min(turnRate * Time.deltaTime, errorRad * CorrectionGain);
                step = Mathf.Min(step, errorRad);

                Vector3 steered = Vector3.RotateTowards(dir, aim, step, 0f);
                entry.Body.velocity = steered * speed;
                if (steered.sqrMagnitude > 0.0001f)
                    entry.Body.transform.rotation = Quaternion.LookRotation(steered);
            }
            catch (System.Exception ex)
            {
                ModLog.Error($"homing steering failed: {ex.Message}");
                _entries.RemoveAt(i);
            }
        }
    }

    internal static void Clear() => _entries.Clear();
}
