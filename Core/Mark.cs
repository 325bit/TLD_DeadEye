using UnityEngine;

namespace DeadEye.Core;

/// <summary>
/// A marked target: the animal plus where on its body the player aimed.
/// The offset is stored in the animal's local space so the mark follows it,
/// and re-resolves to the world point when the shot sequence runs.
/// Also tracks the animal's world velocity (position delta per game second),
/// sampled every Tick, so the shot can lead a moving target.
/// </summary>
internal sealed class Mark
{
    private readonly BaseAi _ai;
    private readonly Vector3 _localOffset;

    private Vector3 _lastWorldPos;
    private bool _hasLastPos;
    internal Vector3 Velocity { get; private set; }

    internal Mark(BaseAi ai, Vector3 worldPoint)
    {
        _ai = ai;
        _localOffset = ai.transform.InverseTransformPoint(worldPoint);
        _lastWorldPos = worldPoint;
        _hasLastPos = true;
        Velocity = Vector3.zero;
    }

    internal BaseAi Ai => _ai;

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
    /// Refresh Velocity from the position delta since the previous call. Call once per Tick
    /// with the scaled delta (Time.deltaTime): both position and time live in the same time
    /// scale, so the quotient is the animal's true game-space speed regardless of bullet time.
    /// </summary>
    internal void SampleVelocity()
    {
        if (!TryGetWorldPoint(out Vector3 pos))
            return;
        if (_hasLastPos && Time.deltaTime > 0.0001f)
        {
            Vector3 v = (pos - _lastWorldPos) / Time.deltaTime;
            // Clamp against teleport-sized spikes (animal repositioning, pathing snaps).
            if (v.sqrMagnitude <= 400f * 400f)
                Velocity = v;
        }
        _lastWorldPos = pos;
        _hasLastPos = true;
    }
}
