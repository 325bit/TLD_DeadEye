using UnityEngine;

namespace DeadEye.Core;

/// <summary>
/// A marked target: the animal plus the point on it the player aimed at. The offset is a
/// WORLD-space root-to-mark vector frozen at mark time, so the point follows the animal's
/// translation but NOT its rotation — a local-space point sweeps sideways when the animal
/// turns in place, further than a grouse is wide. Velocity is sampled twice: once while the
/// camera swing plays (<see cref="FreezeVelocity"/>) and again at the fire moment
/// (<see cref="RefreshVelocity"/>), then held, so the swing stays smooth and the shot is not.
/// </summary>
internal sealed class Mark
{
    private readonly BaseAi _ai;
    private readonly Vector3 _bodyOffset;
    private readonly bool _smallPrey;
    private float _velocityUnscaled;

    internal Vector3 Velocity { get; private set; }

    /// <summary>Root position the sample in <see cref="Velocity"/> was taken at.</summary>
    internal Vector3 VelocityRoot { get; private set; }

    /// <summary>GAME time (scaled) the sample in <see cref="Velocity"/> was taken at.</summary>
    internal float VelocityGameTime { get; private set; }

    internal Mark(BaseAi ai, Vector3 worldPoint)
    {
        _ai = ai;
        _bodyOffset = worldPoint - ai.transform.position;
        _smallPrey = AnimalBody.IsSmallPrey(ai);
        Velocity = Vector3.zero;
    }

    internal BaseAi Ai => _ai;

    /// <summary>
    /// Root-to-mark vector in WORLD space, captured when the animal was marked (used by the
    /// homing bullets).
    /// </summary>
    internal Vector3 BodyOffset => _bodyOffset;

    /// <summary>Grouse / rabbit: any hit kills, so the shot is aimed at the animal's axis.</summary>
    internal bool IsSmallPrey => _smallPrey;

    internal bool IsAlive => _ai != null && _ai.gameObject != null && _ai.GetAiMode() != AiMode.Dead;

    internal bool TryGetWorldPoint(out Vector3 worldPoint)
    {
        if (!IsAlive)
        {
            worldPoint = default;
            return false;
        }
        worldPoint = _ai.transform.position + _bodyOffset;
        return true;
    }

    /// <summary>
    /// World point the shot should converge on. Small prey resolves INSIDE its body (see
    /// <see cref="AnimalBody.PreyAimPoint"/>) because a body that thin cannot absorb an aim
    /// point sitting on the surface; everything else uses the marked point itself.
    /// </summary>
    internal bool TryGetAimPoint(out Vector3 worldPoint)
    {
        if (!IsAlive)
        {
            worldPoint = default;
            return false;
        }

        worldPoint = _smallPrey
            ? AnimalBody.PreyAimPoint(_ai.transform.position, _bodyOffset)
            : _ai.transform.position + _bodyOffset;
        return true;
    }

    /// <summary>
    /// The AI ROOT position (immune to in-place body rotation, which would sweep the marked
    /// body offset sideways and fake a lateral velocity). Used as the cross-check sample for
    /// <see cref="FreezeVelocity"/>.
    /// </summary>
    internal Vector3 RootPosition => _ai.transform.position;

    /// <summary>Which estimator produced <see cref="Velocity"/>, for the shot diagnostics.</summary>
    internal string VelocitySource { get; private set; } = "none";

    /// <summary>
    /// Freeze the animal's velocity, in GAME seconds — the same clock the projectile's flight
    /// time is measured on (see <see cref="AnimalVelocity"/>; a real-time measurement would
    /// report `v * timeScale`). Frozen on purpose: the lead is a uniform-linear prediction,
    /// and a continuously re-estimated velocity made the intercept — and the camera chasing
    /// it — jump around (test video 5).
    /// </summary>
    internal void FreezeVelocity(Vector3 velocity, string source) => Store(velocity, source);

    /// <summary>
    /// Replace the sample once more at the fire moment. The frozen one was taken 0.3-0.5 s
    /// earlier (the sample window runs inside the first glide) and a running grouse covers
    /// more than its own body in that time, so the lead and the vector handed to the in-flight
    /// steering would both describe where the animal used to be. Not per frame, and never by
    /// the camera: only the committed shot re-reads.
    /// </summary>
    internal void RefreshVelocity(Vector3 velocity, string source) => Store(velocity, source);

    private void Store(Vector3 velocity, string source)
    {
        if (velocity.sqrMagnitude > AnimalVelocity.MaxPlausibleSpeed * AnimalVelocity.MaxPlausibleSpeed)
        {
            ModLog.Debug($"velocity rejected as implausible ({velocity.magnitude:F1} m/s, source {source})");
            return;
        }

        Velocity = velocity;
        VelocitySource = source;
        VelocityRoot = _ai.transform.position;
        VelocityGameTime = Time.time;
        _velocityUnscaled = Time.unscaledTime;
    }

    /// <summary>Real seconds since <see cref="Velocity"/> was sampled (shot diagnostics).</summary>
    internal float VelocityAge => Time.unscaledTime - _velocityUnscaled;
}
