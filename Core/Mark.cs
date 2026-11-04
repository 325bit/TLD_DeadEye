// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Hemalurgic

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
    private readonly LocalizedDamage? _headPart;
    private readonly Vector3 _headLocal;
    private readonly Collider? _headCollider;
    private float _velocityUnscaled;

    internal Vector3 Velocity { get; private set; }

    /// <summary>Root position the sample in <see cref="Velocity"/> was taken at.</summary>
    internal Vector3 VelocityRoot { get; private set; }

    /// <summary>GAME time (scaled) the sample in <see cref="Velocity"/> was taken at.</summary>
    internal float VelocityGameTime { get; private set; }

    internal Mark(BaseAi ai, Vector3 worldPoint, LocalizedDamage? headPart = null, Vector3? headCentre = null,
                  Collider? headCollider = null)
    {
        _ai = ai;
        _bodyOffset = worldPoint - ai.transform.position;
        _smallPrey = AnimalBody.IsSmallPrey(ai);
        Velocity = Vector3.zero;

        // The head is stored in the HEAD CAPSULE's own space, not as a world offset: the point
        // is only used for the hit animation, and a howling / turning wolf moves its head a
        // third of a metre while the root stays put.
        _headPart = headPart;
        _headCollider = headCollider;
        if (headPart != null && headCentre.HasValue)
        {
            try
            {
                _headLocal = headPart.transform.InverseTransformPoint(headCentre.Value);
            }
            catch (System.Exception)
            {
                _headPart = null;
            }

            // The BALLISTIC fallback of a head mark must be a torso point, not the head point
            // the click resolved: a mark placed on the head carries the head as its mark point,
            // and aiming at a capsule the game switched off is the empty shot the fallback is
            // there to prevent. Clamped into the body band and built on world up, so a turn
            // cannot sweep it sideways (same rule the steering uses, §10 #41).
            if (_headPart != null)
                _bodyOffset = Vector3.up * AnimalBody.LargeAimHeight(headCentre.Value - ai.transform.position);
        }
    }

    internal BaseAi Ai => _ai;

    /// <summary>
    /// Root-to-mark vector in WORLD space, captured when the animal was marked (used by the
    /// homing bullets).
    /// </summary>
    internal Vector3 BodyOffset => _bodyOffset;

    /// <summary>Grouse / rabbit: any hit kills, so the shot is aimed at the animal's axis.</summary>
    internal bool IsSmallPrey => _smallPrey;

    /// <summary>
    /// The head's own damage table when this mark qualified as a head mark, else null. The
    /// shot reads THIS table instead of the capsule it happened to touch, so a marked head
    /// always rolls the head row (see <see cref="RoundResolution"/>).
    /// </summary>
    internal LocalizedDamage? HeadPart => _headPart;

    /// <summary>
    /// Head centre in the head capsule's local space. The projectile carries this instead of a
    /// world point, so the animation lands on the head as it is when the round arrives.
    /// </summary>
    internal Vector3 HeadLocal => _headLocal;

    /// <summary>
    /// Whether a round can still reach the head capsule this mark was placed on. The game
    /// disables the damage capsules while a wolf howls with its head raised above them, so a
    /// head mark can outlive the thing it names — that is the state the ballistic fallback is
    /// for. Read LIVE: the capsules come back on their own a moment later.
    /// </summary>
    internal bool HeadHittable
    {
        get
        {
            if (_headPart == null || _headCollider == null)
                return false;

            try
            {
                return _headCollider.enabled
                       && !_headCollider.isTrigger
                       && _headCollider.gameObject != null
                       && _headCollider.gameObject.activeInHierarchy;
            }
            catch (System.Exception)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Where this shot is AIMED: the head, unless the head capsule cannot be hit right now —
    /// then the marked body point, which is what keeps a howling-wolf shot on the animal. The
    /// damage row is the head row either way (see <see cref="HeadPart"/>).
    /// </summary>
    internal bool AimAtHead => !_smallPrey && HeadHittable && TryGetHeadPoint(out Vector3 _);

    /// <summary>
    /// Current world position of the head centre this mark was placed on. Resolved live from
    /// the head capsule's transform, so the hit animation plays where the head is NOW rather
    /// than where it was when the trigger was pulled.
    /// </summary>
    internal bool TryGetHeadPoint(out Vector3 worldPoint)
    {
        worldPoint = default;
        if (_headPart == null || !IsAlive)
            return false;

        try
        {
            worldPoint = _headPart.transform.TransformPoint(_headLocal);
            return true;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    internal bool IsAlive => _ai != null && _ai.gameObject != null && _ai.GetAiMode() != AiMode.Dead;

    /// <summary>
    /// Where the marker X is drawn: the head centre for a head mark, the marked body point
    /// otherwise. Positional only — it says what the player aimed at, not where the round will
    /// land, so a head mark whose capsule is unusable still shows on the head.
    /// </summary>
    internal bool TryGetWorldPoint(out Vector3 worldPoint)
    {
        if (!IsAlive)
        {
            worldPoint = default;
            return false;
        }
        if (TryGetHeadPoint(out worldPoint))
            return true;
        worldPoint = _ai.transform.position + _bodyOffset;
        return true;
    }

    /// <summary>
    /// World point the shot should converge on. Small prey resolves INSIDE its body (see
    /// <see cref="AnimalBody.PreyAimPoint"/>) because a body that thin cannot absorb an aim
    /// point sitting on the surface; everything else uses the marked point itself.
    /// A head mark converges on the head while the head capsule is reachable, and drops back to
    /// the marked body point when it is not — a round aimed at a capsule that is switched off
    /// would fly clean over the animal.
    /// </summary>
    internal bool TryGetAimPoint(out Vector3 worldPoint)
    {
        if (!IsAlive)
        {
            worldPoint = default;
            return false;
        }

        if (AimAtHead && TryGetHeadPoint(out worldPoint))
            return true;

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
