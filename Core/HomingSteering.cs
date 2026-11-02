using System.Collections.Generic;
using UnityEngine;

namespace DeadEye.Core;

/// <summary>
/// Steering for bullets that keep chasing their mark after leaving the barrel. Only the
/// AdaptiveArsenal projectile is a real physics body (Rigidbody + AddForce), so this is the
/// path that can be steered; the vanilla vp_Bullet resolves its hit inside Start() and has
/// nothing to steer. Entries are registered from the AdaptiveArsenal Fire prefix, i.e. only
/// for shots the execution sequence actually fires, and each entry keeps its OWN target so
/// an in-flight bullet never retargets when the sequence advances to the next mark.
/// </summary>
internal static class HomingSteering
{
    private const float MaxTurnDegPerSec = 220f; // plenty for a 1-8 m/s animal against a 150+ m/s bullet
    private const float LifetimeSeconds = 3f;    // unscaled: give up on a bullet that never arrives
    private const float MinSpeed = 1f;           // below this the launch force has not landed yet

    private sealed class Entry
    {
        internal Rigidbody Body = null!;
        internal GameObject Target = null!;
        internal Vector3 LocalOffset;
        internal float ExpiresAt;
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
    internal static void Tick()
    {
        if (_entries.Count == 0)
            return;

        float now = Time.unscaledTime;
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            Entry entry = _entries[i];
            try
            {
                if (now > entry.ExpiresAt || entry.Body == null || entry.Target == null)
                {
                    _entries.RemoveAt(i);
                    continue;
                }

                Vector3 velocity = entry.Body.velocity;
                float speed = velocity.magnitude;
                if (speed < MinSpeed)
                    continue; // still waiting for AdaptiveArsenal's launch force to land

                Vector3 goal = entry.Target.transform.TransformPoint(entry.LocalOffset);
                Vector3 toGoal = goal - entry.Body.transform.position;
                if (toGoal.sqrMagnitude < 0.0001f)
                    continue;

                Vector3 steered = Vector3.RotateTowards(
                    velocity / speed,
                    toGoal.normalized,
                    MaxTurnDegPerSec * Mathf.Deg2Rad * Time.deltaTime,
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

    internal static void Clear() => _entries.Clear();
}
