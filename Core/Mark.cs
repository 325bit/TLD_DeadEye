using UnityEngine;

namespace DeadEye.Core;

/// <summary>
/// A marked target: the animal plus where on its body the player aimed.
/// The offset is stored in the animal's local space so the mark follows it,
/// and re-resolves to the world point when the shot sequence runs.
/// Velocity is NOT tracked continuously: right before the shot the manager takes
/// exactly two root-position samples and hands the resulting vector to
/// <see cref="SetVelocity"/> — the animal is then treated as uniform linear motion.
/// </summary>
internal sealed class Mark
{
    private readonly BaseAi _ai;
    private readonly Vector3 _localOffset;

    internal Vector3 Velocity { get; private set; }

    internal Mark(BaseAi ai, Vector3 worldPoint)
    {
        _ai = ai;
        _localOffset = ai.transform.InverseTransformPoint(worldPoint);
        Velocity = Vector3.zero;
    }

    internal BaseAi Ai => _ai;

    /// <summary>Body point offset in the animal's local space (used by the homing bullets).</summary>
    internal Vector3 LocalOffset => _localOffset;

    internal bool IsAlive => _ai != null && _ai.gameObject != null && _ai.GetAiMode() != AiMode.Dead;

    internal bool TryGetWorldPoint(out Vector3 worldPoint)
    {
        if (!IsAlive)
        {
            worldPoint = default;
            return false;
        }
        worldPoint = _ai.transform.TransformPoint(_localOffset);
        return true;
    }

    /// <summary>
    /// First sample of the velocity pair: the AI ROOT position (immune to in-place body
    /// rotation, which would sweep the marked body offset sideways and fake a lateral
    /// velocity). Call again after VelocitySampleSeconds and feed both points to
    /// <see cref="SetVelocity"/>.
    /// </summary>
    internal Vector3 RootPosition => _ai.transform.position;

    /// <summary>
    /// Freeze the animal's velocity from a two-point measurement. Clamped against
    /// teleport-sized spikes (a real animal stays well under 30 m/s); a rejected vector
    /// keeps the previous value. Frozen on purpose: the lead is a uniform-linear
    /// prediction, and a continuously re-estimated velocity made the intercept — and the
    /// camera chasing it — jump around (test video 5).
    /// </summary>
    internal void SetVelocity(Vector3 measured, float seconds)
    {
        Vector3 v = measured / Mathf.Max(0.05f, seconds);
        if (v.sqrMagnitude <= 30f * 30f)
            Velocity = v;
    }
}
