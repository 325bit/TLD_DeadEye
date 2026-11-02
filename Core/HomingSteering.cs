using System.Collections.Generic;
using UnityEngine;

namespace DeadEye.Core;

/// <summary>
/// Steering for bullets that chase their mark after leaving the barrel. Only the
/// AdaptiveArsenal projectile is a real physics body (Rigidbody + AddForce); the vanilla
/// vp_Bullet resolves its hit inside Start() and cannot be steered. Each entry keeps its
/// OWN target, so an in-flight round never retargets when the sequence advances. Aiming
/// is LEAD pursuit (where the animal will be), because pure pursuit makes a fast bullet
/// overshoot and spiral on crossing targets.
/// </summary>
internal static class HomingSteering
{
    private const float BaseTurnDegPerSec = 520f; // before the strength setting; pure pursuit spiralled at 220
    private const float LifetimeSeconds = 8f;     // unscaled: a long shot under the slowdown can take seconds
    private const float MinSpeed = 1f;            // below this the launch force has not landed yet
    private const float LeadFactor = 0.9f;        // 1 = full lead (bullet flies straight), <1 leaves room to correct
    private const float SampleInterval = 0.05f;   // SCALED seconds between target-velocity samples
    private const float MaxTargetSpeed = 40f;     // per scaled second; rejects teleports and scene moves

    private sealed class Entry
    {
        internal Rigidbody Body = null!;
        internal GameObject Target = null!;
        internal Vector3 LocalOffset;
        internal float ExpiresAt;

        internal Vector3 LastPoint;
        internal float LastSampleTime;   // scaled
        internal Vector3 VelocityScaled; // metres per SCALED second
        internal bool Sampled;
    }

    private static readonly List<Entry> _entries = new();

    internal static void Register(Rigidbody body, GameObject target, Vector3 localOffset)
    {
        _entries.Add(new Entry
        {
            Body = body,
            Target = target,
            LocalOffset = localOffset,
            ExpiresAt = Time.unscaledTime + LifetimeSeconds,
        });
        ModLog.Debug("homing projectile registered");
    }

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
        float nowScaled = Time.time;
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

                Vector3 point = entry.Target.transform.TransformPoint(entry.LocalOffset);
                SampleTargetVelocity(entry, point, nowScaled);

                Vector3 toPoint = point - entry.Body.transform.position;
                if (toPoint.sqrMagnitude < 0.0001f)
                    continue;

                // Lead pursuit: aim where the animal will be when the bullet gets there.
                // VelocityScaled is already in metres per scaled second, so the remaining
                // flight time (distance / speed, also scaled) multiplies it directly.
                float flightTime = toPoint.magnitude / speed;
                Vector3 aim = point + entry.VelocityScaled * (flightTime * LeadFactor);
                Vector3 toAim = aim - entry.Body.transform.position;
                if (toAim.sqrMagnitude < 0.0001f)
                    toAim = toPoint;

                Vector3 steered = Vector3.RotateTowards(
                    velocity / speed,
                    toAim.normalized,
                    turnRate * Time.deltaTime,
                    0f);

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

    /// <summary>
    /// Samples the target's velocity in SCALED world units (metres per scaled second), so it
    /// can be multiplied by a scaled flight time directly. Smoothed, and clamped so a single
    /// teleport-sized sample cannot fling the aim point.
    /// </summary>
    private static void SampleTargetVelocity(Entry entry, Vector3 point, float nowScaled)
    {
        if (!entry.Sampled)
        {
            entry.LastPoint = point;
            entry.LastSampleTime = nowScaled;
            entry.Sampled = true;
            return;
        }

        float dt = nowScaled - entry.LastSampleTime;
        if (dt < SampleInterval)
            return;

        Vector3 measured = (point - entry.LastPoint) / dt;
        entry.LastPoint = point;
        entry.LastSampleTime = nowScaled;

        if (measured.sqrMagnitude > MaxTargetSpeed * MaxTargetSpeed)
            return; // teleport / scene change — keep the previous estimate

        entry.VelocityScaled = entry.VelocityScaled.sqrMagnitude < 0.0001f
            ? measured
            : Vector3.Lerp(entry.VelocityScaled, measured, 0.5f);
    }

    internal static void Clear() => _entries.Clear();
}
