// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Hemalurgic

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
    internal const float LifetimeSeconds = 8f;     // unscaled: a long shot under the slowdown can take seconds
    private const float MinSpeed = 1f;            // below this the launch force has not landed yet
    private const float AlignWindowSeconds = 0.1f; // real time after spawn in which the muzzle error may be cancelled outright
    private const float ResidualDegrees = 0.5f;    // per frame: heading drift away from the commanded velocity
    private const float ResidualMinSpeed = 30f;    // below this the round is dying and the frame timing dominates
    private const float SampleSpacing = 0.25f;    // tube-test interval: consecutive r=0.1 spheres overlap without gaps
    private const int MaxPathSamples = 128;       // cap on tube-test samples: 0.25 m x 127 = 32 m of swept segment
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
        internal Vector3 SpawnPosition;  // muzzle position at Register: the run the damage falloff is measured on
        internal float AimHeight;
        internal bool SmallPrey;
        internal LocalizedDamage? HeadPart; // set for a HEAD mark: the row every hit reads
        internal Vector3 HeadLocal;         // head centre in that capsule's local space
        internal bool AimAtHead;            // this round chases the head, not the body point
        internal float LaunchSpeed;      // world speed this round must fly at (slow-motion compensated)
        internal bool Resolved;          // the hit was applied by this mod; the round is torn down
        internal string ResolutionReason = "resolved on hit"; // report label: hit vs scenery
        internal float CreatedAt;
        internal float ExpiresAt;
        internal bool Aligned;
        internal bool SpeedForced;       // the launch speed planned at the fire moment has been applied

        // Command-residual bookkeeping, see CheckCommandResidual.
        internal Vector3 Commanded;
        internal bool HasCommanded;
        internal int ResidualFrames;
        internal float ResidualMax;
        internal float ResidualSeconds;
        internal bool ResidualLogged;

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
        internal bool HitboxDamageable;
        internal float HitboxSeconds;
        internal int HitboxFrame;
        internal string HitboxDesc = "?";
        internal readonly List<float> Trace = new();
        internal bool TraceTruncated;
        internal bool Reported;
    }

    private static readonly List<Entry> _entries = new();

    internal static void Register(Rigidbody body, GameObject target, Vector3 bodyOffset, bool smallPrey,
                                  Vector3 targetVelocity, float launchSpeed,
                                  LocalizedDamage? headPart = null, Vector3 headLocal = default,
                                  bool aimAtHead = false)
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
            SpawnPosition = body.transform.position,
            AimHeight = AnimalBody.LargeAimHeight(bodyOffset),
            SmallPrey = smallPrey,
            HeadPart = headPart,
            HeadLocal = headLocal,
            AimAtHead = aimAtHead && headPart != null,
            LaunchSpeed = launchSpeed,
            CreatedAt = now,
            ExpiresAt = now + LifetimeSeconds,
        };
        _entries.Add(entry);

        string mode = DescribeCollision(body);
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

    private static bool IsDead(BaseAi? ai)
    {
        if (ai == null)
            return false;
        try
        {
            return ai.GetAiMode() == AiMode.Dead;
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Fastest round the tube test can still cover. The swept segment between two observed
    /// positions is one physics step of world travel, so the budget is expressed in metres and
    /// divided by the step — with anything faster the test would sample only the front of the
    /// segment and miss the animals further along it.
    /// </summary>
    internal static float MaxCoveredSpeed
        => SampleSpacing * (MaxPathSamples - 1) * 0.8f / Mathf.Max(0.001f, Time.fixedDeltaTime);

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
    /// What the projectile ships with, for the log. The asset is created kinematic and its
    /// `Fire` only releases it afterwards, so this value describes the pre-launch state — the
    /// live mode is reported with each pass instead.
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
    private static bool TrackClosest(Entry entry, Vector3 bulletPos, Vector3 bodyPoint, float speed,
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
            return false;
        }

        // Geometric pass test, not "the distance grew a little": the round's transform only
        // updates once per physics step while the body point wobbles with the animal's own
        // motion, so a growth threshold trips on the very first frame of every flight and
        // freezes the diagnostic metres short of the target.
        if (entry.Samples > 2 && Vector3.Dot(bodyPoint - bulletPos, bulletVelocity) < 0f)
        {
            ReportClosest(entry, "passed");
            return true;
        }
        return false;
    }

    /// <summary>
    /// Tests the swept segment against the animal's cached colliders as a TUBE, not a line:
    /// the segment is walked at fixed intervals and every sample is distance-tested against
    /// each collider (`Collider.ClosestPoint`), so a round that passes within the near-miss
    /// radius of a body resolves as a hit — a thin raycast turns those into misses. Samples
    /// inside a collider count as exact crossings; the first damageable proximity in path
    /// order resolves the hit (see <see cref="RoundResolution.ApplyDamage"/>).
    /// </summary>
    private static void TestHitbox(Entry entry, Vector3 from, Vector3 sweep)
    {
        if (entry.Resolved || entry.Colliders.Length == 0 || sweep.sqrMagnitude < 0.0001f)
            return;

        float radius = DeadEyeMod.Settings != null
            ? DeadEyeMod.Settings.NearMissRadius
            : Settings.DefaultNearMissRadius;
        float reach = sweep.magnitude;
        int samples = Mathf.Clamp(Mathf.CeilToInt(reach / SampleSpacing), 1, MaxPathSamples);

        try
        {
            for (int s = 1; s <= samples; s++)
            {
                Vector3 sample = from + sweep * (s / (float)samples);

                for (int c = 0; c < entry.Colliders.Length; c++)
                {
                    Collider collider = entry.Colliders[c];
                    if (collider == null || !collider.enabled)
                        continue;

                    // Cheap reject first: the unexpanded AABB vs the r-expanded one.
                    Bounds bounds = collider.bounds;
                    Bounds expanded = bounds;
                    expanded.Expand(radius);
                    if (!expanded.Contains(sample))
                        continue;

                    Vector3 closest = collider.ClosestPoint(sample);
                    float distance = Vector3.Distance(sample, closest);
                    bool inside = bounds.Contains(sample);
                    if (!inside && distance > radius)
                        continue;

                    if (!entry.HitboxCrossed || (!entry.HitboxDamageable && RoundResolution.IsDamageable(collider)))
                    {
                        entry.HitboxCrossed = true;
                        entry.HitboxDamageable = RoundResolution.IsDamageable(collider);
                        entry.HitboxSeconds = Time.unscaledTime - entry.FirstTrackAt;
                        entry.HitboxFrame = entry.Samples;
                        entry.HitboxDesc = DescribeHitbox(collider);
                    }

                    if (!RoundResolution.IsDamageable(collider))
                        continue;

                    entry.Resolved = true;
                    WeaponSource weapon = RoundResolution.WeaponSourceOf(entry.Body);
                    bool damaged = RoundResolution.ApplyDamage(collider, closest, entry.SpawnPosition,
                                                               weapon, out float damage, entry.HeadPart,
                                                               HeadPointOf(entry, closest));
                    RoundResolution.SpawnImpactEffects(collider, closest, sweep.normalized);
                    entry.ResolutionReason = damaged ? "resolved on hit" : "ended on undamageable body";
                    if (entry.Body != null)
                        UnityEngine.Object.Destroy(entry.Body.gameObject);
                    ModLog.Info($"{(damaged ? "homing hit: path test resolved on" : "homing round ended on")} " +
                                $"{collider.gameObject.name} {DescribeHitbox(collider)}, " +
                                $"{Vector3.Distance(entry.SpawnPosition, closest):F1} m from the muzzle, " +
                                $"{(damaged ? $"damage {(float.IsPositiveInfinity(damage) ? "critical" : $"{damage:F0}")} applied"
                                            : "no damage (collider not damageable)")}" +
                                $"{(inside ? "" : $", near-miss r={distance:F2}")}");
                    return;
                }
            }
        }
        catch (System.Exception ex)
        {
            ModLog.Debug($"homing: hitbox test failed ({ex.Message})");
            entry.Colliders = System.Array.Empty<Collider>();
        }
    }

    /// <summary>
    /// What the round flew through. AdaptiveArsenal's damage path reads the collider owner's
    /// layer — 16 (BaseAi on the object) or 27 (BaseAi on a parent), anything else is ignored —
    /// and then requires a LocalizedDamage there, so a crossing is only a tunnel if this
    /// description names a damageable collider. Layers and triggers also matter: a trigger
    /// volume produces no collision at all.
    /// </summary>
    internal static string DescribeHitbox(Collider collider)
    {
        try
        {
            GameObject go = collider.gameObject;
            int layer = go.layer;
            BaseAi? ai = layer == 16
                ? go.GetComponent<BaseAi>()
                : go.transform.GetComponentInParent<BaseAi>();
            bool localised = go.GetComponent<Il2Cpp.LocalizedDamage>() != null;
            Vector3 size = collider.bounds.size;
            return $"{go.name} layer {layer}, trigger {(collider.isTrigger ? "yes" : "no")}, " +
                   $"BaseAi {(ai != null ? "yes" : "no")}, LocalizedDamage {(localised ? "yes" : "no")}, " +
                   $"size {size.x:F2}x{size.y:F2}x{size.z:F2}";
        }
        catch (System.Exception ex)
        {
            return $"unreadable ({ex.Message})";
        }
    }

    /// <summary>
    /// The round is gone, so the last frame's segment — the one that probably crossed the
    /// animal — was never sampled. Extend it along the last velocity and take the closest
    /// approach on THAT line; without it every hit reports the distance it had one whole
    /// physics step earlier, which at 300 m/s is ten metres. One physics step is the natural
    /// reach: the transform only advances that often, so the impact lies within it.
    /// </summary>
    private static void ExtrapolateFinal(Entry entry)
    {
        if (!entry.HasLastPos || entry.Samples == 0 || entry.LastVelocity.sqrMagnitude < 0.01f)
            return;

        float speed = entry.LastVelocity.magnitude;
        Vector3 dir = entry.LastVelocity / speed;
        float step = Mathf.Max(Time.fixedDeltaTime, Time.unscaledDeltaTime);
        float reach = speed * step * 2f;
        Vector3 sweep = dir * reach;
        TestHitbox(entry, entry.LastPos, sweep);

        float along = Mathf.Clamp(Vector3.Dot(entry.LastBodyPoint - entry.LastPos, dir), 0f, reach);
        Vector3 reached = entry.LastPos + dir * along;
        Vector3 miss = reached - entry.LastBodyPoint;

        if (miss.magnitude >= entry.ClosestDistance)
            return;

        entry.ClosestDistance = miss.magnitude;
        entry.ClosestMiss = miss;
        entry.ClosestVelocity = entry.LastTargetVelocity;
        entry.ClosestRoundSpeed = speed;
        entry.ClosestRoundPos = reached;
        entry.ClosestBodyPoint = entry.LastBodyPoint;
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
            ? $"hitbox crossed at {entry.HitboxSeconds * 1000f:F0} ms real (frame {entry.HitboxFrame}): {entry.HitboxDesc}"
            : "hitbox never crossed";
        string body = BodyState(entry.Body);

        ModLog.Info($"homing pass ({SafeName(entry)}, {(entry.SmallPrey ? "small prey" : "large animal")}, " +
                    $"{reason}): closest {entry.ClosestDistance:F2} m at {entry.ClosestSeconds * 1000f:F0} ms real, " +
                    $"miss ahead {Signed(Vector3.Dot(miss, forward))} right {Signed(Vector3.Dot(miss, right))} " +
                    $"up {Signed(miss.y)}, aspect {aspect:F0} deg, {entry.Samples} frames, {hitbox}, " +
                    $"v {entry.SpawnSpeed:F0} -> {entry.ClosestRoundSpeed:F0} m/s, {body}, " +
                    $"target {velocity.magnitude:F1} m/s, " + ResidualText(entry) + ", " +
                    $"round {Vec(entry.ClosestRoundPos)} body {Vec(entry.ClosestBodyPoint)}, trace {TraceText(entry)}");
    }

    /// <summary>
    /// Sets the round's world speed to the one planned at the fire moment, so the execution's
    /// slow motion does not also slow the bullet on screen. Applied on the first frame the
    /// round is seen moving rather than in the Fire patch: AdaptiveArsenal commits its launch
    /// with AddForce (which the projectile only picks up a step later), so this is the first
    /// place the mod can see — and overwrite — the actual velocity. Direction is kept.
    /// </summary>
    private static float ForceLaunchSpeed(Entry entry, Vector3 velocity, float speed)
    {
        entry.SpeedForced = true;

        float target = entry.LaunchSpeed;
        if (target < 1f || speed < 0.001f)
            return speed;

        entry.Body.velocity = velocity / speed * target;
        ModLog.Debug($"homing launch speed {speed:F0} -> {target:F0} m/s");
        return target;
    }

    /// <summary>
    /// Compares the velocity the game reports with the one this mod wrote last frame, plus the
    /// gravity it must have picked up. Gravity is a KNOWN quantity, and it only turns a 299 m/s
    /// round at ~1.9 deg/s — two orders below the threshold — so subtracting it lets the test
    /// sit at half a degree per frame without ever flagging a falling round. What is left says
    /// something OTHER than steering moved the round: a bounce, a teleport, or a jumping aim point.
    /// </summary>
    private static void CheckCommandResidual(Entry entry, Vector3 measured, float speed)
    {
        if (!entry.HasCommanded || speed < ResidualMinSpeed)
            return;

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 expected = entry.Commanded;
        try
        {
            if (entry.Body != null && entry.Body.useGravity)
                expected += Physics.gravity * dt;
        }
        catch (System.Exception) { }

        float residual = Vector3.Angle(measured, expected);
        if (residual <= ResidualDegrees)
            return;

        entry.ResidualFrames++;
        if (residual > entry.ResidualMax)
        {
            entry.ResidualMax = residual;
            entry.ResidualSeconds = Time.unscaledTime - entry.FirstTrackAt;
        }

        if (entry.ResidualLogged)
            return;

        entry.ResidualLogged = true;
        ModLog.Info($"homing steer residual: {residual:F2} deg in one frame ({residual / dt:F0} deg/s) at " +
                    $"{entry.Samples} frames, {speed:F0} m/s — the round moved further than steering " +
                    $"commanded (expected {Vec(expected)}, got {Vec(measured)})");
    }

    /// <summary>
    /// The round's own physics state at report time — the value read at <c>Register</c> is the
    /// PRE-fire one (the projectile is still kinematic until <c>ProjectileItem.Fire</c> runs),
    /// and continuous collision detection is silently ignored on a kinematic body.
    /// </summary>
    private static string BodyState(Rigidbody? body)
    {
        if (body == null)
            return "round destroyed";
        try
        {
            return $"{body.collisionDetectionMode}, kinematic {body.isKinematic}";
        }
        catch (System.Exception)
        {
            return "round state unreadable";
        }
    }

    private static string ResidualText(Entry entry)
        => entry.ResidualFrames > 0
            ? $"steer residual {entry.ResidualMax:F2} deg in {entry.ResidualFrames} frame(s)"
            : "steer residual none";

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
    /// The body point the shot is being described by, resolved from the animal's CURRENT
    /// root — one place, so the flight steering and the impact bookkeeping cannot disagree.
    /// A round aimed at a HEAD mark chases the head centre itself instead of the height band:
    /// the head is a specific capsule, not a slice of the body cylinder, and the head point
    /// lives in that capsule's local space, so it stays correct when the animal turns.
    /// </summary>
    private static Vector3 BodyPointOf(Entry entry)
    {
        Vector3 root = entry.Target.transform.position;
        if (entry.SmallPrey)
            return AnimalBody.PreyAimPoint(root, entry.BodyOffset);

        Vector3 bodyPoint = root + Vector3.up * entry.AimHeight;
        return entry.AimAtHead ? HeadPointOf(entry, bodyPoint) : bodyPoint;
    }

    /// <summary>
    /// Where the head reaction is played for a HEAD mark: the head centre as it is NOW, resolved
    /// from the capsule the mark was placed on. Falls back to the contact point when that
    /// component has been torn down between the shot and the impact.
    /// </summary>
    private static Vector3 HeadPointOf(Entry entry, Vector3 fallback)
    {
        if (entry.HeadPart == null)
            return fallback;

        try
        {
            return entry.HeadPart.transform.TransformPoint(entry.HeadLocal);
        }
        catch (System.Exception)
        {
            return fallback;
        }
    }

    private static bool BelongsToTarget(Entry entry, Collider collider)
    {
        try
        {
            if (collider == null || entry.Target == null)
                return false;
            Transform probe = collider.transform;
            Transform root = entry.Target.transform;
            return probe == root || probe.IsChildOf(root);
        }
        catch (System.Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// A contact is the round's true final position: the segment that reached the animal was
    /// never sampled, because the physics step that ends the flight is the one that destroys
    /// the round. Recording it here is what stops the pass report from claiming a contact hit
    /// passed 8-17 m wide (one physics step of travel) and "never crossed" the hitbox it hit.
    /// Only a collider belonging to the tracked animal counts as a hitbox crossing, so a round
    /// that hits the scenery or another animal still reports the distance it truly ended at.
    /// </summary>
    private static void RecordImpact(Entry entry, Collider collider, Vector3 point)
    {
        try
        {
            float speed = entry.SpawnSpeed;
            try
            {
                if (entry.Body != null && entry.Body.velocity.sqrMagnitude > 0.01f)
                    speed = entry.Body.velocity.magnitude;
            }
            catch (System.Exception) { }

            Vector3 bodyPoint = BodyPointOf(entry);
            Vector3 miss = point - bodyPoint;

            if (entry.Samples == 0)
            {
                entry.Samples = 1;
                entry.SpawnSpeed = speed;
                entry.FirstTrackAt = Time.unscaledTime;
            }

            entry.LastPos = point;
            entry.LastBodyPoint = bodyPoint;

            if (miss.magnitude < entry.ClosestDistance)
            {
                entry.ClosestDistance = miss.magnitude;
                entry.ClosestMiss = miss;
                entry.ClosestVelocity = entry.LastTargetVelocity;
                entry.ClosestRoundSpeed = speed > 0f ? speed : entry.SpawnSpeed;
                entry.ClosestRoundPos = point;
                entry.ClosestBodyPoint = bodyPoint;
                entry.ClosestSeconds = Time.unscaledTime - entry.FirstTrackAt;
            }

            if (!BelongsToTarget(entry, collider))
                return;

            entry.HitboxCrossed = true;
            entry.HitboxDamageable = RoundResolution.IsDamageable(collider);
            entry.HitboxSeconds = Time.unscaledTime - entry.FirstTrackAt;
            entry.HitboxFrame = entry.Samples;
            entry.HitboxDesc = DescribeHitbox(collider);
        }
        catch (System.Exception ex)
        {
            ModLog.Debug($"homing: impact not recorded ({ex.Message})");
        }
    }

    /// <summary>
    /// Called from the collision prefix when a round physically contacts something. Returns
    /// true when the round belongs to Dead Eye: the contact is resolved HERE — damage when
    /// the collider is damageable, effects, teardown — and the caller skips AdaptiveArsenal's
    /// own handler (which would apply damage a second time on every contact the stationary
    /// round picks up until its deferred Destroy lands). Returns false for manual rounds,
    /// which keep AdaptiveArsenal's behaviour unchanged.
    /// </summary>
    internal static bool ResolveContact(Component round, Collider collider, Vector3 point)
    {
        Rigidbody? body = round.GetComponent<Rigidbody>();
        if (body == null)
            return false;

        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            Entry entry = _entries[i];
            if (entry.Body != body)
                continue;

            if (!entry.Resolved)
            {
                entry.Resolved = true;
                RecordImpact(entry, collider, point);
                Vector3 forward = Vector3.forward;
                try
                {
                    if (entry.Body.velocity.sqrMagnitude > 0.01f)
                        forward = entry.Body.velocity.normalized;
                }
                catch (System.Exception) { }

                bool damaged = RoundResolution.ApplyDamage(collider, point, entry.SpawnPosition,
                                                           RoundResolution.WeaponSourceOf(entry.Body),
                                                           out float damage, entry.HeadPart,
                                                           HeadPointOf(entry, point));
                RoundResolution.SpawnImpactEffects(collider, point, forward);
                entry.ResolutionReason = damaged ? "resolved on hit" : "ended on scenery";
                UnityEngine.Object.Destroy(entry.Body.gameObject);
                ModLog.Info($"{(damaged ? "homing hit: contact resolved on" : "homing round ended on scenery:")} " +
                            $"{DescribeHitbox(collider)}, " +
                            $"{Vector3.Distance(entry.SpawnPosition, point):F1} m from the muzzle, " +
                            $"{(damaged ? $"damage {(float.IsPositiveInfinity(damage) ? "critical" : $"{damage:F0}")} applied"
                                        : "no damage (collider not damageable)")}");
            }
            return true;
        }

        return false;
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
                // Resolved rounds are reported and dropped first: their Body is already
                // destroyed, which would otherwise read as the "round gone" miss reason.
                if (entry.Resolved)
                {
                    ReportClosest(entry, entry.ResolutionReason);
                    _entries.RemoveAt(i);
                    continue;
                }

                // A round whose animal is already dead can still be steered but can no longer
                // achieve anything, and holding the execution for it means holding it for a
                // corpse: one measured session held bullet time 5.6 s after the killing shot
                // while three rounds flew on into a bird that was already down.
                if (IsDead(entry.Ai))
                {
                    ExtrapolateFinal(entry);
                    ReportClosest(entry, "target dead");
                    _entries.RemoveAt(i);
                    continue;
                }

                if (nowReal > entry.ExpiresAt || entry.Body == null || entry.Target == null)
                {
                    // Reasons are named apart on purpose: a round that vanishes just after a
                    // hit is the projectile being destroyed on impact, an animal that vanishes
                    // is a despawn, and an expiry is neither.
                    bool roundGone = entry.Body == null;
                    ExtrapolateFinal(entry);
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
                    ExtrapolateFinal(entry);
                    ReportClosest(entry, "round parked");
                    _entries.RemoveAt(i);
                    continue;
                }

                Vector3 velocity = entry.Body.velocity;
                float speed = velocity.magnitude;
                if (speed < MinSpeed)
                    continue; // still waiting for AdaptiveArsenal's launch force to land

                if (!entry.SpeedForced)
                {
                    speed = ForceLaunchSpeed(entry, velocity, speed);
                    velocity = entry.Body.velocity;
                }

                CheckCommandResidual(entry, velocity, speed);

                Vector3 bodyPos = entry.Body.transform.position;
                Vector3 bodyPoint = BodyPointOf(entry);
                Vector3 targetVelocity = VelocityOf(entry, entry.Target.transform.position);

                bool passed = TrackClosest(entry, bodyPos, bodyPoint, speed, velocity, targetVelocity);

                // A round that has flown past its animal cannot be turned around into it in
                // any useful time, so it stops being steered AND stops counting toward the
                // execution hold. Without this the hold ran a full 8 s lifetime on one wasted
                // round (measured 5.6 s of bullet time after the last hit).
                if (passed)
                {
                    _entries.RemoveAt(i);
                    continue;
                }

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
                        entry.Commanded = aim * speed;
                        entry.HasCommanded = true;
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
                entry.Commanded = steered * speed;
                entry.HasCommanded = true;
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

    /// <summary>Live homing rounds currently tracked — a round leaves this count when it
    /// is consumed (contact, path test) or expires, which is what the execution hold waits
    /// on.</summary>
    internal static int LiveRounds => _entries.Count;

    internal static void Clear() => _entries.Clear();
}
