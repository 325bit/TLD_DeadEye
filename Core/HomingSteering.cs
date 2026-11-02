using System.Collections.Generic;
using Il2CppTLD.AI;
using UnityEngine;

namespace DeadEye.Core;

/// <summary>
/// Steering for bullets that chase their mark after leaving the barrel (AdaptiveArsenal
/// physics projectile only; the vanilla vp_Bullet resolves its hit inside Start()). Each
/// keeps its OWN target, so an in-flight round never retargets on sequence advance. Aiming
/// uses a body-centred point led by the LIVE target velocity — never one that rotates with
/// the animal, nor its current position, which the launch outran. Rounds also report their
/// closest pass and whether it entered the animal's colliders; diagnostics only.
/// </summary>
internal static class HomingSteering
{
    private const float BaseTurnDegPerSec = 400f; // x the strength setting (0.5-2.5 -> 200-1000 deg/s)
    private const float CorrectionGain = 0.4f;    // fraction of the remaining angle corrected per frame
    private const float LifetimeSeconds = 8f;     // unscaled: a long shot under the slowdown can take seconds
    private const float MinSpeed = 1f;            // below this the launch force has not landed yet
    private const float AlignWindowSeconds = 0.1f; // real time after spawn in which the muzzle error may be cancelled outright
    private const int TraceLimit = 8;             // distances kept per round (first 7, then the latest)

    private sealed class Entry
    {
        internal Rigidbody Body = null!;
        internal GameObject Target = null!;
        internal BaseAi? Ai;
        internal Collider[] Colliders = System.Array.Empty<Collider>();
        internal Vector3 BodyOffset;
        internal Vector3 TargetVelocity; // sampled at fire: the fallback when the live read fails
        internal Vector3 VelocityRoot;   // root position that sample was taken at
        internal float VelocityGameTime;
        internal float AimHeight;
        internal bool SmallPrey;
        internal float CreatedAt;
        internal float ExpiresAt;
        internal bool Aligned;

        // Closest-approach bookkeeping, see TrackClosest / ReportClosest.
        internal Vector3 LastPos;
        internal Vector3 LastVelocity;
        internal Vector3 LastBodyPoint;
        internal Vector3 LastTargetVelocity;
        internal bool HasLastPos;
        internal int Samples;
        internal float SpawnSpeed;
        internal float FirstTrackAt;
        internal float ClosestDistance = float.MaxValue;
        internal float ClosestRoundSpeed;
        internal Vector3 ClosestMiss;
        internal Vector3 ClosestVelocity;
        internal Vector3 ClosestRoundPos;
        internal Vector3 ClosestBodyPoint;
        internal float ClosestSeconds;
        internal bool HitboxCrossed;
        internal float HitboxSeconds;
        internal int HitboxFrame;
        internal readonly List<float> Trace = new();
        internal bool TraceTruncated;
        internal bool Reported;
    }

    private static readonly List<Entry> _entries = new();

    internal static void Register(Rigidbody body, GameObject target, Vector3 bodyOffset, bool smallPrey,
                                  Vector3 targetVelocity)
    {
        float now = Time.unscaledTime;
        var entry = new Entry
        {
            Body = body,
            Target = target,
            Ai = ResolveAi(target),
            Colliders = ResolveColliders(target),
            BodyOffset = bodyOffset,
            TargetVelocity = targetVelocity,
            VelocityRoot = target.transform.position,
            VelocityGameTime = Time.time,
            AimHeight = AnimalBody.LargeAimHeight(bodyOffset),
            SmallPrey = smallPrey,
            CreatedAt = now,
            ExpiresAt = now + LifetimeSeconds,
        };
        _entries.Add(entry);

        // Switched to continuous dynamic when the asset ships Discrete — never on a
        // kinematic body, and the mode found is what the registered line reports.
        string mode = DescribeCollision(body);
        if (body.collisionDetectionMode == CollisionDetectionMode.Discrete && !body.isKinematic)
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        ModLog.Debug($"homing projectile registered ({(smallPrey ? "small prey" : "large animal")}, " +
                     $"target v {targetVelocity.magnitude:F1} m/s, {entry.Colliders.Length} colliders, " +
                     $"{mode})");
    }

    private static BaseAi? ResolveAi(GameObject target)
    {
        try
        {
            return target.GetComponent<BaseAi>();
        }
        catch (System.Exception ex)
        {
            ModLog.Debug($"homing: target has no readable BaseAi ({ex.Message})");
            return null;
        }
    }

    /// <summary>
    /// Colliders belonging to the animal, cached once: the pass test raycasts against these
    /// rather than against the whole scene, so a hit means the round entered the hitbox.
    /// </summary>
    private static Collider[] ResolveColliders(GameObject target)
    {
        try
        {
            return target.GetComponentsInChildren<Collider>(true);
        }
        catch (System.Exception ex)
        {
            ModLog.Debug($"homing: colliders unreadable ({ex.Message})");
            return System.Array.Empty<Collider>();
        }
    }

    /// <summary>
    /// What the projectile ships with, for the log.
    /// </summary>
    private static string DescribeCollision(Rigidbody body)
    {
        try
        {
            return $"{body.collisionDetectionMode}, kinematic {body.isKinematic}";
        }
        catch (System.Exception ex)
        {
            ModLog.Debug($"homing: collision mode unreadable ({ex.Message})");
            return "collision mode unknown";
        }
    }

    /// <summary>
    /// Velocity to lead by THIS frame. A flight lasts 60-320 ms, and prey that changes
    /// direction inside it makes a fire-time vector point the wrong way for the whole
    /// remaining flight; the live read costs one call and follows the turn. Falls back to the
    /// fire-time sample only when the animal cannot answer at all (see
    /// <see cref="AnimalVelocity.Resolve"/> — a zero read is cross-checked against the root).
    /// </summary>
    private static Vector3 VelocityOf(Entry entry, Vector3 root)
    {
        Vector3 velocity = AnimalVelocity.Resolve(entry.Ai, entry.VelocityRoot, entry.VelocityGameTime,
                                                  root, entry.TargetVelocity, out _);
        entry.VelocityRoot = root;
        entry.VelocityGameTime = Time.time;
        return velocity;
    }

    /// <summary>
    /// The point to steer at: the body point the shot was described by, led by the target's
    /// velocity over the round's remaining flight time. Without the lead the steering target
    /// is where the animal WAS while the fire direction already carried the lead, so every
    /// frame spends part of its small correction budget undoing the launch aim.
    /// </summary>
    private static Vector3 AimPointOf(Vector3 bodyPoint, Vector3 from, float speed, Vector3 targetVelocity)
    {
        if (targetVelocity.sqrMagnitude < 0.0001f)
            return bodyPoint;

        // Two iterations: the flight time depends on the point that is being led.
        Vector3 lead = bodyPoint + targetVelocity *
                       (Vector3.Distance(from, bodyPoint) / Mathf.Max(MinSpeed, speed));
        lead = bodyPoint + targetVelocity * (Vector3.Distance(from, lead) / Mathf.Max(MinSpeed, speed));
        return lead;
    }

    /// <summary>
    /// Records how close the round came to the animal's ACTUAL body point (the one without
    /// the lead), measured against the SEGMENT swept since the previous frame: at 300 m/s a
    /// physics frame covers several animal lengths, so endpoint sampling would report even a
    /// dead-centre shot as metres wide. Also raycasts that same segment against the animal's
    /// own colliders, which is the only test that says whether the round entered the hitbox.
    /// </summary>
    private static void TrackClosest(Entry entry, Vector3 bulletPos, Vector3 bodyPoint, float speed,
                                     Vector3 bulletVelocity, Vector3 targetVelocity)
    {
        if (entry.SpawnSpeed <= 0f)
        {
            entry.SpawnSpeed = speed;
            entry.FirstTrackAt = Time.unscaledTime;
        }

        Vector3 from = entry.HasLastPos ? entry.LastPos : bulletPos;
        Vector3 sweep = bulletPos - from;
        float along = sweep.sqrMagnitude > 0.0001f
            ? Mathf.Clamp01(Vector3.Dot(bodyPoint - from, sweep) / sweep.sqrMagnitude)
            : 0f;

        Vector3 reached = from + sweep * along;
        Vector3 miss = reached - bodyPoint;
        float distance = miss.magnitude;

        entry.Samples++;
        entry.LastPos = bulletPos;
        entry.LastVelocity = bulletVelocity;
        entry.LastBodyPoint = bodyPoint;
        entry.LastTargetVelocity = targetVelocity;
        entry.HasLastPos = true;
        if (entry.Trace.Count < TraceLimit)
            entry.Trace.Add(distance);
        else
        {
            entry.Trace[TraceLimit - 1] = distance;
            entry.TraceTruncated = true;
        }

        TestHitbox(entry, from, sweep);

        if (distance < entry.ClosestDistance)
        {
            entry.ClosestDistance = distance;
            entry.ClosestMiss = miss;
            entry.ClosestVelocity = targetVelocity;
            entry.ClosestRoundSpeed = speed;
            entry.ClosestRoundPos = reached;
            entry.ClosestBodyPoint = bodyPoint;
            entry.ClosestSeconds = Time.unscaledTime - entry.FirstTrackAt;
            return;
        }

        // Geometric pass test, not "the distance grew a little": the round's transform only
        // updates once per physics step while the body point wobbles with the animal's own
        // motion, so a growth threshold trips on the very first frame of every flight and
        // freezes the diagnostic metres short of the target.
        if (entry.Samples > 2 && Vector3.Dot(bodyPoint - bulletPos, bulletVelocity) < 0f)
            ReportClosest(entry, "passed");
    }

    /// <summary>
    /// Raycasts the swept segment against the animal's cached colliders. A crossing with no
    /// kill therefore means the round went THROUGH the hitbox — a real defect — rather than
    /// that the aim point stood outside the animal, which is what every distance number here
    /// has been unable to separate.
    /// </summary>
    private static void TestHitbox(Entry entry, Vector3 from, Vector3 sweep)
    {
        if (entry.HitboxCrossed || entry.Colliders.Length == 0 || sweep.sqrMagnitude < 0.0001f)
            return;

        try
        {
            var ray = new Ray(from, sweep.normalized);
            float reach = sweep.magnitude;
            for (int i = 0; i < entry.Colliders.Length; i++)
            {
                Collider collider = entry.Colliders[i];
                if (collider == null || !collider.enabled)
                    continue;
                if (!collider.Raycast(ray, out RaycastHit _, reach))
                    continue;

                entry.HitboxCrossed = true;
                entry.HitboxSeconds = Time.unscaledTime - entry.FirstTrackAt;
                entry.HitboxFrame = entry.Samples;
                return;
            }
        }
        catch (System.Exception ex)
        {
            ModLog.Debug($"homing: hitbox test failed ({ex.Message})");
            entry.Colliders = System.Array.Empty<Collider>();
        }
    }

    /// <summary>
    /// One line per round describing where the closest pass happened, split into the animal's
    /// own frame: <c>ahead</c> (negative = behind its heading, i.e. the lead was short),
    /// <c>right</c>, <c>up</c>, plus the aspect angle between its heading and the line of
    /// sight (0 = running straight away, 90 = broadside, 180 = head on). The raw positions,
    /// the per-frame distances and whether the hitbox was ever entered are printed too, because
    /// they separate a real miss from a tunnelled or recycled projectile. Diagnostics only.
    /// </summary>
    private static void ReportClosest(Entry entry, string reason)
    {
        if (entry.Reported || entry.Samples == 0)
            return;

        entry.Reported = true;

        Vector3 velocity = entry.ClosestVelocity;
        Vector3 miss = entry.ClosestMiss;
        bool moving = velocity.sqrMagnitude > 0.01f;
        Vector3 forward = moving ? velocity.normalized : Vector3.forward;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        float aspect = moving && miss.sqrMagnitude > 0.0001f ? Vector3.Angle(velocity, -miss) : 0f;

        string hitbox = entry.HitboxCrossed
            ? $"hitbox crossed at {entry.HitboxSeconds * 1000f:F0} ms (frame {entry.HitboxFrame})"
            : "hitbox never crossed";

        ModLog.Info($"homing pass ({SafeName(entry)}, {(entry.SmallPrey ? "small prey" : "large animal")}, " +
                    $"{reason}): closest {entry.ClosestDistance:F2} m at {entry.ClosestSeconds * 1000f:F0} ms, " +
                    $"miss ahead {Signed(Vector3.Dot(miss, forward))} right {Signed(Vector3.Dot(miss, right))} " +
                    $"up {Signed(miss.y)}, aspect {aspect:F0} deg, {entry.Samples} frames, {hitbox}, " +
                    $"v {entry.SpawnSpeed:F0} -> {entry.ClosestRoundSpeed:F0} m/s, " +
                    $"target {velocity.magnitude:F1} m/s, " +
                    $"round {Vec(entry.ClosestRoundPos)} body {Vec(entry.ClosestBodyPoint)}, trace {TraceText(entry)}");
    }

    private static string Signed(float value) => value >= 0f ? $"+{value:F2}" : $"{value:F2}";

    private static string Vec(Vector3 v) => $"({v.x:F1},{v.y:F1},{v.z:F1})";

    private static string TraceText(Entry entry)
    {
        var parts = new List<string>(entry.Trace.Count + 1);
        for (int i = 0; i < entry.Trace.Count; i++)
        {
            if (entry.TraceTruncated && i == TraceLimit - 1)
                parts.Add("...");
            parts.Add(entry.Trace[i].ToString("F1"));
        }
        return parts.Count == 0 ? "-" : string.Join(" ", parts);
    }

    private static string SafeName(Entry entry)
    {
        try
        {
            return entry.Target != null ? entry.Target.name : "?";
        }
        catch (System.Exception)
        {
            return "?";
        }
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
        float nowReal = Time.unscaledTime;

        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            Entry entry = _entries[i];
            try
            {
                if (nowReal > entry.ExpiresAt || entry.Body == null || entry.Target == null)
                {
                    // Reasons are named apart on purpose: a round that vanishes just after a
                    // hit is the projectile being destroyed on impact, an animal that vanishes
                    // is a despawn, and an expiry is neither.
                    bool roundGone = entry.Body == null;
                    ReportClosest(entry, roundGone ? "round gone" : "target gone");
                    _entries.RemoveAt(i);
                    continue;
                }

                // A projectile parked back in its pool is not destroyed, so every nil check
                // above passes while its transform stops tracking the flight. Only checked
                // after the round has been seen moving, so a not-yet-activated spawn is not
                // mistaken for one.
                if (entry.Samples > 0 && !entry.Body.gameObject.activeInHierarchy)
                {
                    ReportClosest(entry, "round parked");
                    _entries.RemoveAt(i);
                    continue;
                }

                Vector3 velocity = entry.Body.velocity;
                float speed = velocity.magnitude;
                if (speed < MinSpeed)
                    continue; // still waiting for AdaptiveArsenal's launch force to land

                Vector3 bodyPos = entry.Body.transform.position;
                Vector3 root = entry.Target.transform.position;
                Vector3 bodyPoint = entry.SmallPrey
                    ? AnimalBody.PreyAimPoint(root, entry.BodyOffset)
                    : root + Vector3.up * entry.AimHeight;
                Vector3 targetVelocity = VelocityOf(entry, root);

                TrackClosest(entry, bodyPos, bodyPoint, speed, velocity, targetVelocity);

                // Aim point recomputed NOW rather than replayed from the fire moment: a
                // snapshot goes stale the instant the animal moves and the round covers
                // 300 m/s. Doing so also lets the first frame with a real velocity cancel
                // the muzzle error outright — the aim was set from the PRE-spawn transform
                // and AdaptiveArsenal then moves the round to the muzzle, which is worth a
                // few tenths of a degree at 20 m, more than a grouse is wide. Re-aiming
                // beside the barrel costs no visible curve and leaves every later frame a
                // clean starting error instead of a budget spent undoing this one.
                Vector3 toTarget = AimPointOf(bodyPoint, bodyPos, speed, targetVelocity) - bodyPos;
                if (toTarget.sqrMagnitude < 0.0001f)
                    continue;

                Vector3 dir = velocity / speed;
                Vector3 aim = toTarget.normalized;

                if (!entry.Aligned)
                {
                    entry.Aligned = true;
                    if (nowReal - entry.CreatedAt <= AlignWindowSeconds)
                    {
                        float muzzleError = Vector3.Angle(dir, aim);
                        entry.Body.velocity = aim * speed;
                        entry.Body.transform.rotation = Quaternion.LookRotation(aim);
                        ModLog.Info($"homing aligned: muzzle error {muzzleError:F2}deg cancelled at " +
                                    $"{speed:F0} m/s ({(entry.SmallPrey ? "small prey" : "large animal")})");
                        continue;
                    }
                }

                float errorRad = Vector3.Angle(dir, aim) * Mathf.Deg2Rad;

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
                ReportClosest(entry, "error");
                _entries.RemoveAt(i);
            }
        }
    }

    internal static void Clear() => _entries.Clear();
}
