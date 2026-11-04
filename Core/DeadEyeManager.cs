// SPDX-License-Identifier: GPL-3.0-only
// Copyright (c) 2026 Hemalurgic

using System.Collections.Generic;
using UnityEngine;

namespace DeadEye.Core;

/// <summary>
/// Central state machine.
/// Inactive --toggle--> Marking (bullet time, marks accumulate, cap = clip size) --toggle
/// with marks--> Firing (bullet time KEPT: filter + slowdown persist to the end while the
/// player is compensated back to real time) --> Exiting (time lerps back to 1) --> Inactive.
/// Toggle with NO marks exits immediately. The sequence runs at the vanilla real rate;
/// every projectile is redirected onto the committed aim point (docs/DEVELOPMENT.md §3/§5/§7).
/// </summary>
internal static class DeadEyeManager
{
    internal static DeadEyeState State { get; private set; } = DeadEyeState.Inactive;

    private static readonly List<Mark> _marks = new();
    internal static IReadOnlyList<Mark> Marks => _marks;

    internal static int MarkCap { get; private set; }
    internal static int RemainingMarks => Mathf.Max(0, MarkCap - _marks.Count);

    private static GearItem? _boundGun;
    private static int _fireIndex;
    private static float _targetDeadline;
    private static float _sequenceDeadline;
    private static bool _holdForRounds;           // execution held until the last bullet is consumed
    private static float _holdDeadline;

    private const float TargetWaitSeconds = 8f;   // unscaled: max wait per target (generous: the player's state machine runs on scaled time)
    private const float SequenceWatchdog = 30f;   // unscaled: whole sequence abort
    private const float HoldGraceSeconds = 1f;    // unscaled: margin past the rounds' own expiry
    private const float FocusedMarkingScale = 0.01f;  // marking speed once the revolver is revealed (not user-tunable)
    private const float RevealTimeScale = 0.1f;       // draw reveal speed for the first RevealSeconds (not user-tunable)
    private const float RevealSeconds = 0.7f;         // unscaled: how long the reveal speed lasts after activation
    private const float FirstGlideSeconds = 0.55f; // opening swing duration (game-native tween)
    private const float MaxSwingDegPerSec = 400f; // hard ceiling: a segment never whips faster than this
    private const float MinSegmentSeconds = 0.03f; // one frame-ish floor for a swing segment
    private const float MaxSegmentSeconds = 2f;   // ceiling so a stuck fire gate cannot crawl forever
    private const float FallbackTimeToGo = 0.5f;  // time budget when the fire gate reports nothing useful
    private const float ArrivedDeg = 0.5f;        // no new segment is issued this close to the target
    private const float TrackReissueDriftDegrees = 5f; // re-issue the pursuit glide when the endpoint drifted this far off its captured value
    private const float TrackReissueMinSeconds = 0.12f; // and at most this often (storm guard; re-issues are dead-frame-free via the pre-advanced start)
    private const float VelocitySampleSeconds = 0.1f; // real-time gap between the two root-position samples; only a cross-check for the AI velocity, and it finishes inside glide #1
    private const float MinVelocityGameSeconds = 0.02f; // the root difference is meaningless on a shorter GAME-time window
    private const float MaxFallbackSampleSeconds = 0.5f; // ceiling for extending that window when the AI velocity is unreadable
    private const float AimPointTtl = 0.5f;       // unscaled: how long a committed aim point stays valid
    private const float MaxAnimCompensation = 20f; // Animator.speed ceiling: must cover 1/0.05, i.e. the whole slider range

    private static float _nextLookAtIssue;        // unscaled: when the running glide expires
    private static bool _firstGlideDone;          // false until glide #1 of a sequence has played out
    private static Quaternion _segEndpointRot;    // orientation the active pursuit glide heads to
    private static float _segEndUnscaled;         // unscaled: when the active pursuit glide expires
    private static float _segIssueUnscaled;       // unscaled: when the active pursuit glide was issued
    private static float _nextShotUnscaled;       // unscaled: when the next shot of the sequence may fire
    private static float _nextGateLog;            // unscaled: throttle for the shot-gated diagnostic line
    private static float _nextBlockedLog;         // unscaled: throttle for the "activation ignored" line

    // Velocity measurement for the CURRENT mark. The AI's own velocity is taken as the
    // primary source (it already lives in game seconds); a root-position pair is kept purely
    // as a cross-check and a fallback, and its denominator is GAME time — measuring the
    // distance over real seconds silently scaled the speed by the time scale, which is
    // harmless on a deer and fatal on a rabbit.
    private static bool _velFirstTaken;
    private static Vector3 _velFirstPos;
    private static float _velFirstUnscaled;
    private static float _velFirstGame;
    private static bool _interceptReady;          // true once the current mark's velocity is frozen
    private static bool _lastCameraPhase;         // camera phase currently in force (false = mark body, true = intercept)
    private static bool _awaitingDraw;            // marking still runs at the draw speed until the revolver is up
    private static float _drawDeadline;           // unscaled: fallback deadline for the draw phase

    /// <summary>Index of the mark currently being fired at (-1 when not firing).</summary>
    internal static int CurrentFireIndex => State == DeadEyeState.Firing ? _fireIndex : -1;

    // The committed aim point is kept with a short TTL instead of being cleared at sequence
    // end: the LAST bullet's Start() runs a frame AFTER the state machine has already gone
    // back to Inactive, and it must still be redirected.
    private static Vector3? _lastAimPoint;
    private static float _aimPointExpiry;

    /// <summary>
    /// World point the LAST committed shot was aimed at (valid for AimPointTtl after the
    /// shot). The redirect patches use it to aim the fresh bullet onto the mark, because by
    /// the time the bullet spawns/starts the sequencer may already have advanced.
    /// </summary>
    internal static Vector3? LastShotAimPoint
        => Time.unscaledTime <= _aimPointExpiry ? _lastAimPoint : null;

    /// <summary>
    /// The animal the shot about to leave the barrel should chase. Consumed synchronously
    /// by the AdaptiveArsenal Fire prefix (registration), then cleared — null when the
    /// homing setting is off, which leaves the projectile on its ballistic path.
    /// </summary>
    internal static GameObject? PendingHomingTarget;

    /// <summary>
    /// Body point offset (WORLD space, from the animal's root) that goes with
    /// <see cref="PendingHomingTarget"/>.
    /// </summary>
    internal static Vector3 PendingHomingBodyOffset;

    /// <summary>Grouse / rabbit: the projectile converges on the body centre, not the mark.</summary>
    internal static bool PendingHomingSmallPrey;

    /// <summary>
    /// Head damage table of a HEAD mark (null otherwise), travelling with
    /// <see cref="PendingHomingTarget"/> so the round reads the head row instead of the row of
    /// the capsule it happens to touch. <see cref="PendingHomingHeadLocal"/> is the head centre
    /// in that capsule's local space, which is where the hit animation is played.
    /// </summary>
    internal static LocalizedDamage? PendingHomingHeadPart;
    internal static Vector3 PendingHomingHeadLocal;

    /// <summary>
    /// True when this shot is AIMED at the head: the mark qualifies AND its head capsule is
    /// reachable right now. False means the round is aimed at the body point instead — the
    /// head row still applies, it just has to be hit on a capsule that is switched on.
    /// </summary>
    internal static bool PendingHomingAimHead;

    /// <summary>
    /// The target's velocity (GAME seconds) as measured at the fire moment, which goes with
    /// <see cref="PendingHomingTarget"/> as the steering FALLBACK. In-flight steering re-reads
    /// the live velocity every frame and only falls back to this when the animal is unreadable.
    /// </summary>
    internal static Vector3 PendingHomingVelocity;
    /// <summary>World speed this shot's round must fly at, slow-motion compensation included.</summary>
    internal static float PendingLaunchSpeed;

    /// <summary>Aim point for the camera driver while the firing sequence runs (null otherwise).</summary>
    internal static Vector3? CurrentAimPoint
    {
        get
        {
            if (State != DeadEyeState.Firing || _fireIndex >= _marks.Count)
                return null;
            return _marks[_fireIndex].TryGetAimPoint(out var p) ? p : null;
        }
    }

    // ---------------------------------------------------------------- Tick

    internal static void Tick()
    {
        // Advance the owned time scale on unscaled time, then push it onto both clocks.
        // The authoritative re-assertion runs from the InterfaceManager.Update POSTFIX
        // (Patches.TimeScaleGuardPatch), i.e. after TimeAccelerator's prefix stomps it.
        TimeScaler.Advance();

        // Steer any bullet that is still chasing its mark. Runs in every state: a shot can
        // still be in the air after the sequence has ended.
        HomingSteering.Tick(DeadEyeMod.Settings?.HomingStrength ?? Settings.DefaultHomingStrength);

        // Warm the audio subsystem up during normal gameplay, long before the first
        // activation — the first winmm sound a process issues would otherwise be swallowed.
        if (State == DeadEyeState.Inactive && IsGameplayActive())
            ModAudio.Tick();

        switch (State)
        {
            case DeadEyeState.Inactive:
                // Fool-proof: we should never own a modified time scale while inactive.
                if (TimeScaler.Owned)
                    TimeScaler.ForceRestore();
                if (KeyPressed(DeadEyeMod.Settings?.ToggleKey))
                {
                    // The toggle key is read from raw Unity input, so it also fires while the
                    // pause menu, the inventory or any other panel owns the screen. The guard
                    // is the ONLY thing that keeps the mode out of those screens.
                    string? blocked = BlockingReason();
                    if (blocked == null)
                        TryEnter();
                    else
                        LogIgnoredActivation(blocked);
                }
                break;

            case DeadEyeState.Marking:
                TickMarking();
                break;

            case DeadEyeState.Exiting:
                TickExiting();
                break;

            case DeadEyeState.Firing:
                TickFiring();
                break;
        }
    }

    private static void TickMarking()
    {
        if (BlockingReason() is string blocked)
        {
            ForceExit($"gameplay lost ({blocked})");
            return;
        }
        if (_boundGun == null || !IsBoundGunStillCarried())
        {
            ForceExit("weapon stowed");
            return;
        }
        if (FatigueEmpty())
        {
            ForceExit($"fatigue empty (norm={GameManager.GetFatigueComponent()?.GetNormalizedFatigue():F3})");
            return;
        }

        var settings = DeadEyeMod.Settings!;

        // Reveal phase: the draw speed only runs until the muzzle clears the screen; after
        // that the marking speed takes over and the rest of the raise crawls through the
        // whole marking phase (the slow-draw close-up). Time-driven because the game
        // exposes no "weapon raised" flag — the animation is what the player is watching.
        if (_awaitingDraw && Time.unscaledTime >= _drawDeadline)
        {
            _awaitingDraw = false;
            TimeScaler.LerpTo(FocusedMarkingScale, settings.EnterLerpSpeed);
            ModLog.Info($"revolver revealed, marking speed -> {FocusedMarkingScale:F3}");
        }

        // Explicit cancel (default Q): discard every mark and leave the mode without
        // firing, even when marks are already placed — the toggle key would have started
        // the execution instead. Checked before the toggle so a cancel is never eaten.
        if (KeyPressed(settings.CancelKey))
        {
            BeginExit("cancel key");
            return;
        }

        if (KeyPressed(settings.ToggleKey))
        {
            // With marks: end the marking phase and dive straight into the firing phase —
            // the yellow filter and the slowdown carry over to the end of the sequence, and
            // only the player is compensated back to real time. With no marks: plain exit.
            if (_marks.Count > 0)
                StartFiring();
            else
                BeginExit("toggle with no marks");
            return;
        }

        if (KeyPressed(settings.MarkKey))
            TryMark();
    }

    private static void TickExiting()
    {
        // Restore tail shared by the no-marks exit and the end of the firing sequence.
        // Advance() lerps the owned value up every frame; complete once it is back at ~1.
        if (TimeScaler.Value >= 0.98f)
        {
            TimeScaler.ForceRestore();
            ModAudio.Stop(); // the activation sound ends with the mode
            State = DeadEyeState.Inactive;
            // 0.7.39: anchors the state timeline — the ghost-shot window after a cancel-then-
            // fire can only be attributed to a stuck state if the log shows where Inactive began.
            ModLog.Info("Dead Eye inactive (exit complete)");
        }
    }

    private static void TickFiring()
    {
        if (Time.unscaledTime > _sequenceDeadline)
        {
            ForceExit("sequence watchdog");
            return;
        }
        if (BlockingReason() is string blocked)
        {
            ForceExit($"gameplay lost ({blocked})");
            return;
        }

        // Drive the first-person Animator at real speed: the world runs at the execution
        // scale, the player must not. Unity's Animator.speed multiplies the scaled
        // deltaTime, so 1/timeScale restores real-time animation while everything else
        // stays in bullet time. (The scale itself is guarded by TimeScaler + the
        // InterfaceManager.Update postfix.)
        ApplyPlayerTimeCompensation(true);

        if (_fireIndex >= _marks.Count)
        {
            FinishFiring();
            return;
        }

        var mark = _marks[_fireIndex];
        if (!mark.TryGetAimPoint(out Vector3 aimPoint))
        {
            ModLog.Debug($"target {_fireIndex} is dead, skipping");
            AdvanceTarget("target dead");
            return;
        }

        // Per-target window, checked BEFORE the fire gates: if the gates stay closed
        // (stuck animation, stowed weapon) the target is still skipped on time instead of
        // stalling the sequence until the watchdog.
        if (Time.unscaledTime > _targetDeadline)
        {
            AdvanceTarget("target window timeout");
            return;
        }

        // Velocity for this mark, taken while glide #1 plays so the shot is never delayed
        // behind it. Normally only the frozen value is read (see Mark.Velocity) — an input
        // that changes every frame made the camera chase a jittering endpoint.
        if (!_velFirstTaken)
        {
            _velFirstPos = mark.RootPosition;
            _velFirstUnscaled = Time.unscaledTime;
            _velFirstGame = Time.time;
            _velFirstTaken = true;
        }
        else if (!_interceptReady && Time.unscaledTime - _velFirstUnscaled >= VelocitySampleSeconds)
        {
            float realElapsed = Time.unscaledTime - _velFirstUnscaled;
            float gameSeconds = Time.time - _velFirstGame;
            Vector3 aiVelocity = AnimalVelocity.FromAi(mark.Ai, out string source);
            bool aiOk = source == "ai";

            // The pair is the only fallback, so a failed AI read keeps the window open until
            // it holds enough GAME time to mean something. Capped, because a permanently
            // failed read would otherwise stall every shot behind it.
            if (aiOk || gameSeconds >= MinVelocityGameSeconds || realElapsed >= MaxFallbackSampleSeconds)
            {
                Vector3 pairedVelocity = gameSeconds >= MinVelocityGameSeconds
                    ? AnimalVelocity.FromRootDelta(_velFirstPos, mark.RootPosition, gameSeconds)
                    : Vector3.zero;

                // A failed AI read ("none") falls back to the pair. A ZERO AI velocity is a real
                // standstill and must not be replaced by pair noise, so only the source decides.
                mark.FreezeVelocity(aiOk ? aiVelocity : pairedVelocity, aiOk ? "ai" : "root-delta");
                _interceptReady = true;

                // Both numbers plus the angle between them: a matching angle with a different
                // magnitude means the pair averaged a hop-and-freeze cycle, while a mismatched
                // angle means one of the two sources is not the animal's velocity at all.
                float disagreement = aiVelocity.magnitude > 0.5f && pairedVelocity.magnitude > 0.5f
                    ? Vector3.Angle(aiVelocity, pairedVelocity)
                    : -1f;
                ModLog.Info($"velocity frozen (target {_fireIndex}, {SafeName(mark)}): " +
                            $"ai {aiVelocity.magnitude:F1} m/s [{source}] vs root-delta {pairedVelocity.magnitude:F1} m/s " +
                            $"over {gameSeconds:F3} game s ({realElapsed:F2} real s), " +
                            $"angle {(disagreement < 0f ? "n/a" : $"{disagreement:F0} deg")} " +
                            $"-> using {mark.Velocity.magnitude:F1} m/s from {mark.VelocitySource}");
            }
        }

        // Rhythm: the FIRST shot waits for the first look-at glide to play out, so the
        // crosshair visibly swings onto the first target before its bullet leaves (a fast
        // swing is fine there — the crosshair can be far from target 1). Every later
        // camera motion is a time-to-go pursuit glide (see UpdateTrackingGlide): a LINEAR
        // look-at issued per segment whose duration equals the time left until the weapon
        // may fire again, so the view arrives at the next mark when the shot is due.
        if (!_firstGlideDone)
        {
            if (_nextLookAtIssue == 0f)
            {
                IssueLookAt(aimPoint); // glide #1 of the sequence
                return;
            }
            if (Time.unscaledTime < _nextLookAtIssue)
                return; // glide #1 in flight
            _firstGlideDone = true; // glide #1 finished -> release the first shot
        }

        // Two-phase camera target per mark, per the uniform-linear design:
        // BEFORE the velocity pair is frozen the crosshair swings onto the mark itself
        // (angular speed #1); once the pair is frozen the crosshair tracks the predicted
        // point instead (angular speed #2). Both phases share the SAME time budget (until
        // the next shot may fire), so each derives its own angular speed from its own
        // remaining angle — a large phase-2 jump just swings faster instead of snapping.
        Vector3 cameraTarget = _interceptReady
            ? ComputeIntercept(mark, aimPoint) // frozen velocity + live position + fresh flight time
            : aimPoint;                        // prediction not ready yet — chase the mark itself

        // The phase switch is a deliberate retarget: end the running segment at once so
        // angular speed #2 takes over immediately instead of finishing a swing toward the
        // stale body point.
        if (_interceptReady != _lastCameraPhase)
        {
            _lastCameraPhase = _interceptReady;
            _segEndUnscaled = 0f;
        }

        float remainingDeg = UpdateTrackingGlide(cameraTarget);

        // The shot does NOT wait for the camera (full vanilla fire rate wins; hits are
        // resolved by the projectile redirect, which never depended on the view).
        TryFireCurrentTarget(mark, aimPoint, remainingDeg);
    }

    /// <summary>
    /// Fire-gates for the current mark; when they all pass, commits the shot (snapshot the
    /// intercept, Fire(), advance). Returns false while any gate is still closed. The view
    /// is deliberately NOT a gate: the camera swings on its own time-to-go schedule and the
    /// shot keeps the vanilla rate (the projectile redirect owns the hit).
    /// </summary>
    private static bool TryFireCurrentTarget(Mark mark, Vector3 aimPoint, float remainingDeg)
    {
        var shooter = GameManager.GetVpFPSCamera()?.CurrentShooter;
        if (shooter == null || shooter.m_Weapon == null || shooter.m_Weapon.m_GunItem == null)
            return false; // still raising the weapon

        // Safety: only chain-fire the bound revolver.
        if (shooter.m_Weapon.m_GunItem.name == null ||
            !shooter.m_Weapon.m_GunItem.name.Contains(GunResolver.BoundGearName))
        {
            ModLog.Debug("equipped weapon is not the bound revolver, aborting sequence");
            ForceExit("weapon changed");
            return true;
        }

        // Fire-rate gate: the game's own gate lives in scaled seconds (m_NextAllowedFireTime
        // vs Time.time), so under the firing-phase slowdown it would stretch the real
        // interval by 1/scale. Pace the sequence on UNSCALED time instead, compressed by
        // the owned scale (vanilla real rate) and divided by the rapid-fire multiplier.
        // The vanilla post-shot cooldown (IsReloadCooldownActive, ~0.5 s) is the pacing
        // member that actually binds under rapid fire — the measured rate-4 session saw a
        // 0.52 s median gap with the other members compressed to 0.12 s — so at rate > 1
        // it is overridden; rate 1 keeps it and restores the vanilla pace exactly.
        float rate = Mathf.Max(1f, DeadEyeMod.Settings?.FireRateMultiplier ?? Settings.DefaultFireRateMultiplier);
        bool pacingOpen = Time.unscaledTime >= _nextShotUnscaled &&
                          (rate > 1f || !shooter.IsReloadCooldownActive);

        // The shot needs the frozen velocity: never fire before the two-sample window has
        // completed for this mark (it always completes long before the pacing opens).
        if (!_interceptReady)
            return false;

        var animation = GameManager.GetPlayerAnimationComponent();
        bool animOk = animation != null && !animation.IsReloading() &&
                      animation.IsAllowedToFire(shooter.m_Weapon.m_GunItem.m_AllowHipFire);

        if (pacingOpen && !animOk && Time.unscaledTime >= _nextGateLog)
        {
            // Diagnostic: our pacing is open but the animation still refuses to fire — dump
            // every gate so a repeat of the "second shot never fires" failure is triageable.
            _nextGateLog = Time.unscaledTime + 1f;
            ModLog.Info($"shot gated: remaining={remainingDeg:F1}deg " +
                        $"reloadCooldown={shooter.IsReloadCooldownActive} " +
                        $"reloading={animation != null && animation.IsReloading()} " +
                        $"allowedToFire={animation != null && animation.IsAllowedToFire(shooter.m_Weapon.m_GunItem.m_AllowHipFire)} " +
                        $"scaledGateLeft={shooter.m_NextAllowedFireTime - Time.time:F2}s " +
                        $"ts={Time.timeScale:F2} gts={GameManager.m_GlobalTimeScale:F2} own={TimeScaler.Value:F2} " +
                        $"animSpeed={(animation != null && animation.m_Animator != null ? animation.m_Animator.speed : -1f):F2}");
        }

        if (!pacingOpen || !animOk)
            return false;

        // The lead is about to be built on a sample taken 0.3-0.5 s ago (the sampling window
        // runs inside glide #1), and a running grouse covers 4-7 m in that time — more than
        // its whole body. Re-read here, once per committed shot: the fresh vector feeds both
        // the intercept and the in-flight steering, so neither points at where the animal was.
        Vector3 frozenVelocity = mark.Velocity;
        string frozenSource = mark.VelocitySource;
        float staleAge = mark.VelocityAge; // must be read BEFORE the refresh, which resets it
        Vector3 fireVelocity = AnimalVelocity.Resolve(mark.Ai, mark.VelocityRoot, mark.VelocityGameTime,
                                                      mark.RootPosition, frozenVelocity, out string fireSource);
        mark.RefreshVelocity(fireVelocity, fireSource);

        // Diagnostic: a LARGE angle means the animal turned or accelerated while the crosshair
        // was on its way (so no uniform-linear lead could have been right), a SMALL angle with
        // misses means the model is fine and the residual has another cause.
        float velocityDrift = frozenVelocity.magnitude > 0.5f && fireVelocity.magnitude > 0.5f
            ? Vector3.Angle(frozenVelocity, fireVelocity)
            : -1f;
        ModLog.Info($"v at fire (target {_fireIndex}, {SafeName(mark)}): frozen {frozenVelocity.magnitude:F1} m/s [{frozenSource}] " +
                    $"-> now {fireVelocity.magnitude:F1} m/s [{fireSource}], " +
                    $"angle {(velocityDrift < 0f ? "n/a" : $"{velocityDrift:F0} deg")}, " +
                    $"{staleAge:F2} real s stale");

        // Recompute the intercept at the fire moment and snapshot it: the redirect patches
        // aim the fresh projectile exactly there, independent of where the camera is.
        Vector3 intercept = ComputeIntercept(mark, aimPoint, out float flightTime);
        _lastAimPoint = intercept;
        _aimPointExpiry = Time.unscaledTime + AimPointTtl;

        // Fire() consumes the round and spawns the projectile synchronously: when it
        // returns, the shot is committed. The scaled gate is neutralized first (our
        // unscaled pacing above is authoritative under the slowdown).
        shooter.m_NextAllowedFireTime = Time.time;
        // Tell the projectile patch what to chase: the animal itself, so the bullet corrects
        // in flight instead of relying on the lead prediction being perfect.
        PendingHomingTarget = DeadEyeMod.Settings?.HomingBullets == true ? mark.Ai.gameObject : null;
        PendingHomingBodyOffset = mark.BodyOffset;
        PendingHomingSmallPrey = mark.IsSmallPrey;
        PendingHomingHeadPart = mark.HeadPart;
        PendingHomingHeadLocal = mark.HeadLocal;
        PendingHomingAimHead = mark.AimAtHead;
        PendingHomingVelocity = mark.Velocity;
        shooter.Fire();
        PendingHomingTarget = null; // consumed synchronously by the Fire prefix
        PendingHomingHeadPart = null;
        PendingHomingAimHead = false;
        // The interval Fire() pushes onto the gate is the vanilla fire rate expressed in
        // SCALED seconds (vanilla always runs at scale 1, so the number IS the real
        // interval). Left alone under the execution slowdown the game would advance only
        // `scale` seconds per real second, stretching every shot by 1/scale — the "fire
        // rate got slower during the execution" report. Compress the gate by the owned
        // scale so the REAL wait equals the vanilla interval again, then divide by the
        // rapid-fire multiplier: gate, unscaled wait and first-person animator
        // (see PlayerAnimSpeedPatch) must shrink together or the slowest one wins.
        float intervalReal = Mathf.Max(0f, shooter.m_NextAllowedFireTime - Time.time);
        float ts = Mathf.Max(0.001f, TimeScaler.Value);
        shooter.m_NextAllowedFireTime = Time.time + intervalReal * ts / rate;
        _nextShotUnscaled = Time.unscaledTime + Mathf.Max(0.05f, intervalReal / rate);
        // Diagnostics: the velocity and its source, the flight time it produced, and the two
        // distances that decide a small-prey hit — the lead the shot commits to, and how far
        // the aim point sits from the part the player actually marked. A miss is then one of
        // three things and the numbers say which: velocity wrong, aim point wrong, or the
        // animal accelerated after the freeze. The aim fraction is printed because it is the
        // one input the player can change live.
        Vector3 markedPoint = mark.TryGetWorldPoint(out Vector3 mp) ? mp : aimPoint;
        ModLog.Info($"shot {_fireIndex + 1}/{_marks.Count} fired ({(mark.IsSmallPrey ? "small prey" : "large animal")}, " +
                    $"v {mark.Velocity.magnitude:F1} m/s [{mark.VelocitySource}], bullet {PendingLaunchSpeed:F0} m/s, " +
                    $"flight {flightTime * 1000f:F0} ms, " +
                    $"lead {(intercept - aimPoint).magnitude:F2} m, aim {(aimPoint - markedPoint).magnitude:F2} m from the mark" +
                    $"{(mark.IsSmallPrey ? $", fraction {AnimalBody.PreyAimFraction:F2}" : string.Empty)}, " +
                    $"next in {intervalReal / rate:F2}s real{(rate > 1f ? $" (x{rate:F0})" : string.Empty)})");
        AdvanceTarget("shot fired");
        return true;
    }

    /// <summary>Predicted world point the shot should be aimed at (lead + gravity drop).</summary>
    private static Vector3 ComputeIntercept(Mark mark, Vector3 aimPoint)
        => ComputeIntercept(mark, aimPoint, out _);

    private static Vector3 ComputeIntercept(Mark mark, Vector3 aimPoint, out float flightTime)
    {
        var camera = GameManager.GetMainCamera();
        Vector3 muzzlePos = camera != null ? camera.transform.position : GameManager.GetPlayerTransform().position;
        float launchSpeed = CompensatedLaunchSpeed(
            GameManager.GetVpFPSCamera()?.CurrentShooter?.m_Weapon?.m_GunItem?.name);
        // Published for the round: HomingSteering forces the projectile to this speed on its
        // first live frame. Computed here because this is the last call before Fire().
        PendingLaunchSpeed = launchSpeed;
        return Ballistics.PredictIntercept(muzzlePos, aimPoint, mark.Velocity, launchSpeed, out flightTime);
    }

    /// <summary>
    /// The launch speed a Dead Eye round actually flies at. The world runs at the OWNED scale
    /// (<see cref="TimeScaler.Value"/>, never the readback a prefix writer can stomp — §10 #1),
    /// so a bullet at the table speed looks 1/scale times slower than a normal shot; the speed
    /// is raised by exactly that factor, capped at <see cref="HomingSteering.MaxCoveredSpeed"/>,
    /// because beyond it the swept hit test cannot sample the whole segment and would miss.
    /// The lead prediction MUST use this value too or every shot misses by the ratio.
    /// </summary>
    private static float CompensatedLaunchSpeed(string? gunName)
    {
        float baseSpeed = Ballistics.MuzzleVelocityFor(gunName);
        if (baseSpeed < 1f)
            return baseSpeed;

        float scale = Mathf.Clamp(TimeScaler.Value, 0.05f, 1f);
        float wanted = 1f / scale;
        float ceiling = Mathf.Max(1f, HomingSteering.MaxCoveredSpeed / baseSpeed);
        float applied = Mathf.Max(1f, Mathf.Min(wanted, ceiling));

        if (wanted > ceiling + 0.01f)
            ModLog.Debug($"launch speed capped at x{ceiling:F2} ({baseSpeed * ceiling:F0} m/s): " +
                         $"x{wanted:F2} would outrun the swept hit test");

        return baseSpeed * applied;
    }

    // ---------------------------------------------------------------- Enter / exit

    /// <summary>
    /// Drive the first-person Animator at real speed while the world is slowed. Unity's
    /// Animator.speed multiplies the scaled deltaTime, so speed = 1/timeScale makes the
    /// player animate exactly as at normal time; off resets to 1. Applied only during the
    /// firing phase — marking keeps the whole world (player included) in bullet time.
    /// </summary>
    private static void ApplyPlayerTimeCompensation(bool on)
    {
        var anim = GameManager.GetPlayerAnimationComponent();
        if (anim == null || anim.m_Animator == null)
            return;
        float s = TimeScaler.Value; // owned value, not the (externally stomped) engine readback
        // Clamped: at the focused marking scale an unclamped 1/s would be 100x, and
        // the ramp into the execution would visibly fast-forward every animation.
        anim.m_Animator.speed = on && s > 0.001f ? Mathf.Clamp(1f / s, 1f, MaxAnimCompensation) : 1f;
    }

    /// <summary>
    /// Convert a REAL-time duration to the scaled seconds the game's look-at struct expects:
    /// the struct's m_TimeRemaining is decremented by SCALED deltaTime, so a glide that must
    /// last D real seconds is issued with D * timeScale. Uses the OWNED scale rather than
    /// Time.timeScale: a stomped readback would shrink every glide and read as a teleport.
    /// </summary>
    private static float RealToScaled(float realSeconds)
        => realSeconds * Mathf.Max(0.001f, TimeScaler.Value);

    private static void TryEnter()
    {
        var settings = DeadEyeMod.Settings!;

        // Progression gate: the mode stays locked until the vanilla revolver skill reaches the
        // configured level. Checked BEFORE the gear checks so a low-level player is told about
        // the level, not about a missing revolver they may well be carrying.
        if (!RevolverSkillUnlocked(out int skillLevel, out int requiredLevel))
        {
            Overlay.SetMessage(skillLevel <= 0
                ? "读不到左轮手枪技能等级，暂时无法进入死神之眼"
                : $"左轮手枪技能达到 {requiredLevel} 级后才能使用死神之眼（当前 {skillLevel} 级）", 3f);
            ModLog.Info($"activation refused: revolver skill {skillLevel} < required {requiredLevel}");
            return;
        }

        var fatigue = GameManager.GetFatigueComponent();
        if (fatigue != null && fatigue.IsExhausted())
        {
            Overlay.SetMessage("太疲惫了，无法进入死神之眼", 2.5f);
            return;
        }

        _boundGun = GunResolver.FindCarried();
        if (_boundGun == null)
        {
            Overlay.SetMessage("身上没有携带绑定的左轮手枪", 2.5f);
            return;
        }

        int rounds = GunResolver.RoundsAvailable(_boundGun);
        if (rounds <= 0)
        {
            Overlay.SetMessage("左轮里没有子弹，无法标记目标", 2.5f);
            return;
        }

        MarkCap = rounds;
        _marks.Clear();
        // One full collider dump per animal per session: the entry point is the session boundary.
        MarkForensics.BeginSession();
        _lastAimPoint = null;
        _aimPointExpiry = 0f;

        var fatigueNow = GameManager.GetFatigueComponent();

        // Draw the revolver AT ACTIVATION when it is still stowed. RevealTimeScale only
        // covers the REVEAL — the moment the muzzle clears the bottom of the screen
        // (RevealSeconds); the scale then drops to FocusedMarkingScale so the REST of the
        // raise plays out far slower and spans the whole marking phase, which is the
        // slow-draw close-up. A player already holding the gun skips the reveal.
        _awaitingDraw = !GunResolver.IsInHands(_boundGun);
        if (_awaitingDraw)
        {
            GameManager.GetPlayerManagerComponent().EquipItem(_boundGun, false);
            _drawDeadline = Time.unscaledTime + RevealSeconds;
        }

        TimeScaler.LerpTo(_awaitingDraw ? RevealTimeScale : FocusedMarkingScale,
                          settings.EnterLerpSpeed);
        Overlay.SetOverlay(settings.YellowOverlay && settings.OverlayOpacity > 0f);
        State = DeadEyeState.Marking;
        ScratchFx.Trigger(); // RDR2-style white scratch wipe across the screen
        ModLog.Info($"Dead Eye entered: cap={MarkCap}, bound={GunResolver.BoundGearName}, draw={_awaitingDraw}, fatigue norm={fatigueNow?.GetNormalizedFatigue():F3}");
        Overlay.SetMessage($"死神之眼 · 标记目标（0/{MarkCap}）", 2.5f);
        ModAudio.PlayEnter();
    }

    /// <summary>
    /// Leave the marking phase without firing (toggle with no marks, or the cancel key).
    /// The reason only feeds the log; the on-screen line differs so a cancelled session
    /// with marks on animals is distinguishable from a plain empty exit.
    /// </summary>
    private static void BeginExit(string reason)
    {
        bool cancelled = reason == "cancel key";
        int discarded = _marks.Count;
        ModAudio.StopLoop(); // marking is over: the loop stops even before the restore tail
        State = DeadEyeState.Exiting;
        Overlay.SetOverlay(false);
        _marks.Clear();
        MarkCap = 0;
        _awaitingDraw = false;
        TimeScaler.LerpTo(1f, DeadEyeMod.Settings!.ExitLerpSpeed);
        ModLog.Info($"Dead Eye exiting ({reason}, {discarded} mark(s) discarded)");
        Overlay.SetMessage(cancelled ? "死神之眼已取消" : "死神之眼结束", 1.5f);
    }

    private static void StartFiring()
    {
        // Bullet time is KEPT through the firing phase, at its own (milder) pace: the
        // yellow filter persists and the time scale lerps from the marking value to
        // ExecutionTimeScale (0.2). Only the player is compensated back toward
        // real time (first-person Animator + unscaled fire pacing, see TickFiring).
        var settings = DeadEyeMod.Settings!;
        ModAudio.StopLoop(); // the loop belongs to marking; the execution keeps only the sting
        _awaitingDraw = false; // the draw phase never survives into the execution
        _holdForRounds = false; // a fresh sequence never starts inside the previous hold
        // Snap, do not ramp: the Animator compensation is 1/scale, so a ramp would leave
        // the player firing behind a closed animation gate for its whole duration.
        TimeScaler.SetImmediate(settings.ExecutionTimeScale);

        // Safety: the revolver is normally drawn at activation already, but if the player
        // stowed it mid-marking this keeps the sequence self-contained. When it is already
        // in hand nothing happens (no animation, no delay).
        if (_boundGun != null && !GunResolver.IsInHands(_boundGun))
            GameManager.GetPlayerManagerComponent().EquipItem(_boundGun, false);

        // Strip any equip-related fire cooldown so the first shot is not artificially
        // delayed; per-shot pacing afterwards is ours (unscaled) in TickFiring.
        var shooter = GameManager.GetVpFPSCamera()?.CurrentShooter;
        if (shooter != null)
            shooter.m_NextAllowedFireTime = Mathf.Min(shooter.m_NextAllowedFireTime, Time.time);

        _fireIndex = 0;
        _sequenceDeadline = Time.unscaledTime + SequenceWatchdog;
        _targetDeadline = Time.unscaledTime + TargetWaitSeconds;
        _nextLookAtIssue = 0f; // glide toward the first mark on the next Tick
        _nextShotUnscaled = 0f;
        _nextGateLog = 0f;
        ResetVelocitySampling();
        _firstGlideDone = false;
        _segEndpointRot = Quaternion.identity;
        _segEndUnscaled = 0f;  // first pursuit glide issues immediately after shot #1
        _segIssueUnscaled = 0f;
        State = DeadEyeState.Firing;
        ModLog.Info($"fire sequence started with {_marks.Count} mark(s), timeScale {Time.timeScale:F2}");
        Overlay.SetMessage("处决 · 开火", 1.2f);
    }

    /// <summary>Restart the velocity measurement window for the newly active mark.</summary>
    private static void ResetVelocitySampling()
    {
        _velFirstTaken = false;
        _interceptReady = false;
        _lastCameraPhase = false;
        _velFirstPos = default;
        _velFirstUnscaled = 0f;
        _velFirstGame = 0f;
    }

    /// <summary>Prefab name for the diagnostics, without risking a null chain in a log line.</summary>
    private static string SafeName(Mark mark)
    {
        try
        {
            var ai = mark.Ai;
            return ai != null && ai.gameObject != null ? ai.gameObject.name : "?";
        }
        catch (System.Exception)
        {
            return "?";
        }
    }

    private static void AdvanceTarget(string reason)
    {
        ModLog.Debug($"target {_fireIndex} done ({reason})");
        _fireIndex++;
        _targetDeadline = Time.unscaledTime + TargetWaitSeconds;
        ResetVelocitySampling(); // fresh two-sample window for the next mark
        // _nextLookAtIssue only gates glide #1; tracking glides re-issue every Tick and
        // need no scheduling, so the swing to the next mark starts on the next Tick.
        if (_fireIndex >= _marks.Count)
            FinishFiring();
    }

    private static void FinishFiring()
    {
        // Hold the execution while the last rounds are still in flight: the slow-mo and
        // tint end when the LAST bullet is consumed — by the marked animal, by scenery,
        // or by its own expiry (HomingSteering removes the entry in every case, including
        // the 8 s no-contact expiry). TickFiring re-enters here every Tick while holding;
        // without live rounds (vanilla bullet path, homing off, or all rounds gone) the
        // sequence ends immediately, exactly as it did before the hold existed.
        if (!_holdForRounds && HomingSteering.LiveRounds > 0)
        {
            _holdForRounds = true;
            _holdDeadline = Time.unscaledTime + HomingSteering.LifetimeSeconds + HoldGraceSeconds;
            ModLog.Info($"fire sequence fired — holding the execution for {HomingSteering.LiveRounds} live round(s)");
            Overlay.SetMessage("死神之眼：最后一发", 1f);
            return; // stay in Firing: the state tick keeps the scale, tint and steering alive
        }

        if (_holdForRounds && HomingSteering.LiveRounds > 0 && Time.unscaledTime < _holdDeadline)
            return; // still holding

        bool wasHolding = _holdForRounds;
        _holdForRounds = false;
        _marks.Clear();
        MarkCap = 0;
        // NOTE: _lastAimPoint is intentionally left to expire via its TTL — the last
        // bullet's Start() runs after this and still needs the redirect.
        ApplyPlayerTimeCompensation(false);
        Overlay.SetOverlay(false); // the yellow filter ends with the sequence
        State = DeadEyeState.Exiting; // shared restore tail lerps time back to 1
        TimeScaler.LerpTo(1f, DeadEyeMod.Settings!.ExitLerpSpeed);
        ModLog.Info(wasHolding ? "fire sequence finished (last round consumed)" : "fire sequence finished");
        Overlay.SetMessage("死神之眼结束", 1.5f);
    }

    /// <summary>Hard exit from any state: restore everything, no firing.</summary>
    internal static void ForceExit(string reason)
    {
        if (State == DeadEyeState.Inactive && !TimeScaler.Owned)
            return;
        ModLog.Info($"forced exit: {reason}");
        ApplyPlayerTimeCompensation(false);
        TimeScaler.ForceRestore();
        Overlay.SetOverlay(false);
        ModAudio.Stop(); // mid-mode cancel: cut the activation sound immediately
        _marks.Clear();
        MarkCap = 0;
        _holdForRounds = false; // the execution hold dies with the sequence
        _awaitingDraw = false;
        _lastAimPoint = null;
        _aimPointExpiry = 0f;
        State = DeadEyeState.Inactive;
    }

    /// <summary>Scene change / menu hard reset.</summary>
    internal static void HardReset()
    {
        HomingSteering.Clear(); // in-flight bullets do not survive a scene load
        if (TimeScaler.Owned || State != DeadEyeState.Inactive)
            ForceExit("hard reset");
    }

    // ---------------------------------------------------------------- Marking

    private static void TryMark()
    {
        if (_marks.Count >= MarkCap)
        {
            Overlay.SetMessage($"标记已达上限（{MarkCap} = 左轮剩余子弹）", 2f);
            return;
        }

        var camera = GameManager.GetMainCamera();
        if (camera == null)
            return;

        var ray = camera.ScreenPointToRay(new Vector3(Screen.width / 2f, Screen.height / 2f, 0f));
        var rawHits = Physics.RaycastAll(ray, AnimalBody.MarkRange);
        // Copy into a managed array so System.Array.Sort(comparison) is usable.
        var hits = new RaycastHit[rawHits.Length];
        for (int i = 0; i < rawHits.Length; i++)
            hits[i] = rawHits[i];
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        // A head mark changes what EVERY round of the execution reads, so the qualification is
        // decided once, here, from the same ray the gate used — never re-decided at fire time,
        // when the animal may have turned its head away. `surfaceHit` is the collider the click
        // actually landed on, or null when it landed on nothing: a surface names the part by
        // identity, a miss can only be judged by distance to the head box.
        void CommitAs(BaseAi target, Vector3 point, float gateMiss, string src, Collider? surfaceHit = null)
        {
            LocalizedDamage? headPart = null;
            Vector3 headCentre = default;
            Collider? headCollider = null;
            if (target != null && HeadPart.TryFind(target, ray, surfaceHit, out LocalizedDamage? found,
                                                   out Vector3 centre, out Collider? cap))
            {
                headPart = found;
                headCentre = centre;
                headCollider = cap;
            }

            CommitMark(target, point, gateMiss, src, headPart, headCentre, headCollider);
        }

        // Sorted raw hits: the FIRST collider hit that resolves to a live animal decides the
        // click — it either becomes a mark or refuses with the crosshair-gate message.
        foreach (var hit in hits)
        {
            var ai = hit.collider.gameObject.layer == 16
                ? hit.collider.GetComponent<BaseAi>()
                : hit.collider.gameObject.layer == 27
                    ? hit.collider.GetComponentInParent<BaseAi>()
                    : null;

            if (ai == null || ai.GetAiMode() == AiMode.Dead)
                continue;

            // Restrict the mark to the animal's DAMAGEABLE colliders. The raw hit point can be
            // anywhere on a collision capsule five to seven times taller than the animal, and
            // an aim point derived from it sits above the body — the whole shot then misses.
            // A resolution failure must never lock marking out, so it degrades to the raw hit
            // point (the old behaviour) with a warning instead of dropping the click.
            if (!AnimalBody.TryResolveMarkPoint(ai, hit.collider, ray, out Vector3 markPoint,
                                                out float miss, out float tolerance, out string source))
            {
                // Second failure shape: the ray DID hit the animal but no damageable collider
                // resolved. Log the full collider inventory (0.7.38) before degrading — absent
                // vs disabled capsules are different games, and vanilla bullets care too.
                MarkForensics.LogNoDamageable(ai, hit.collider, hit);
                ModLog.Warn($"mark: nothing damageable resolved on {hit.collider.gameObject.name} " +
                            $"({HomingSteering.DescribeHitbox(hit.collider)}) — using the raw hit point");
                CommitAs(ai, hit.point, 0f, $"raw hit on {hit.collider.gameObject.name}", hit.collider);
                return;
            }
            // …and the crosshair has to be ON that body, not merely inside the oversized
            // capsule: refusing here is what stops a mark being planted in empty air.
            if (miss > tolerance)
            {
                Overlay.SetMessage($"准星偏离活物的可伤害部位（偏 {miss:F2} m，容差 {tolerance:F2} m）", 1.5f);
                return;
            }

            CommitAs(ai, markPoint, miss, source, hit.collider);
            return;
        }

        // No raw hit resolved to a live animal: on a visible animal this means the fur is wider
        // than the colliders (a howling wolf's raised head clears the body capsule). Instead of
        // dropping the click, take the live animal whose body box lies nearest to the ray —
        // within the fallback radius only, which is what keeps a neighbouring animal in a herd
        // from being marked.
        if (MarkFallback.TryFind(ray, hits, out BaseAi? nearAi, out Collider? nearProxy,
                                 out Vector3 bodyPoint, out float linearMiss, out float nearSpan) &&
            nearAi != null && nearProxy != null)
        {
            string source;
            Vector3 markPoint;
            float miss;
            if (AnimalBody.TryResolveMarkPoint(nearAi, nearProxy, ray, out Vector3 resolvedPoint,
                                               out float resolvedMiss, out float _, out string resolvedSource))
            {
                markPoint = resolvedPoint;
                miss = resolvedMiss;
                source = $"linear-fallback {linearMiss:F2} m ({resolvedSource})";
            }
            else
            {
                // Same degradation as the hit path, but there is no hit.point here: the box
                // point nearest the ray stands in for it.
                markPoint = bodyPoint;
                miss = linearMiss;
                source = $"linear-fallback {linearMiss:F2} m (body box point)";
            }

            // The body-scaled crosshair gate is deliberately NOT re-applied: the fallback radius
            // already decided this click is on that animal, and the gate (0.4x the body's largest
            // dimension = 0.60 m on a wolf capsule) is exactly what the measured 0.90 / 1.14 m
            // near misses exceed. See docs/DEVELOPMENT.md §10 #38.
            ModLog.Warn($"mark: no raw hit resolved to an animal — linear fallback chose {nearAi.gameObject.name} " +
                        $"at {nearSpan:F1} m, {linearMiss:F2} m off the ray (radius {MarkFallback.Radius:F2} m), " +
                        $"source {source}");
            CommitAs(nearAi, markPoint, miss, source);
            return;
        }

        // Log-only forensics at Warning: the failure shape (zero raw hits vs skipped aux
        // volumes vs a visible animal off-ray) decides the fix, and without evidence every
        // report looks the same. See Core/MarkForensics.cs.
        MarkForensics.LogMiss(ray, hits);
        Overlay.SetMessage("准星处没有活物（射线没碰到动物）", 1.2f);
    }

    /// <summary>
    /// Shared tail of every successful mark, whichever gate produced it: store it, tell the
    /// player, play the confirm chime, log the triage line and auto-fire when the cap is reached.
    /// <paramref name="headPart"/> is the animal's head damage table when this click counted as
    /// a head mark (see <see cref="HeadPart"/>); the mark carries it into the execution, where
    /// it overrides the table of whatever capsule a round happens to touch.
    /// </summary>
    private static void CommitMark(BaseAi ai, Vector3 markPoint, float miss, string source,
                                   LocalizedDamage? headPart = null, Vector3 headCentre = default,
                                   Collider? headCollider = null)
    {
        var mark = new Mark(ai, markPoint, headPart, headCentre, headCollider);
        _marks.Add(mark);
        Overlay.SetMessage($"已标记 {_marks.Count}/{MarkCap}", 1.2f);
        ModAudio.PlayMark();
        // Triage line: the runtime species decides the aim model (the interop wrapper
        // only ever reports BaseAi), and the distance between the shot point and the
        // point the player actually aimed at says whether the model moved the round off
        // the animal. (A prey body is ~0.2 m wide.)
        mark.TryGetAimPoint(out Vector3 markAim);
        ModLog.Debug($"marked {ai.gameObject.name} [{AnimalBody.SpeciesName(ai)}] " +
                     $"{(mark.IsSmallPrey ? "small prey" : "large animal")}: {source}, " +
                     $"{(markPoint - ai.transform.position).magnitude:F2} m from root, " +
                     $"crosshair miss {miss:F2} m, aim {(markAim - markPoint).magnitude:F2} m off it " +
                     $"(fraction {AnimalBody.PreyAimFraction:F2}), " + AnimalBody.DescribeBody(ai));
        if (headPart != null)
        {
            // Which of the two the shot follows is decided at FIRE time (the capsules can be
            // switched off between the click and the trigger), so this line reports the state
            // the mark was born in and the rule it will obey.
            ModLog.Info($"head mark: '{ai.gameObject.name}' carries the HEAD damage row " +
                        $"(centre {headCentre.y:F2} m high, {(headCentre - ai.transform.position).magnitude:F2} m from root) — " +
                        $"rounds aim {(mark.AimAtHead ? "at the head" : "at the body (head capsule is not hittable now)")} " +
                        "and resolve as head hits");
            // Only the homing resolver knows about the mark; with homing off the shot is
            // resolved by the game's own contact callback and the row override cannot apply.
            if (DeadEyeMod.Settings == null || !DeadEyeMod.Settings.HomingBullets)
                ModLog.Warn("head mark: homing bullets are OFF, so this mark's head row cannot be applied " +
                            "(the round is resolved outside Dead Eye)");
        }
        // Auto-fire: the mark that reaches the cap (= rounds left in the cylinder) is
        // also the last one the sequence can shoot, so the execution starts by itself
        // instead of waiting for another toggle press (setting-gated).
        if (_marks.Count >= MarkCap && DeadEyeMod.Settings!.AutoFireAtCap)
        {
            ModLog.Info("mark cap reached, starting the execution automatically");
            StartFiring();
        }
    }

    // ---------------------------------------------------------------- Helpers

    /// <summary>
    /// One-shot glide onto <paramref name="targetPoint"/> through the game's scripted
    /// look-at system (disassembly of PlayerManager.LookAtTargetForTime.Update, §6):
    /// Slerp(Restore, CameraTarget, t) with t = Tween(1 - (TR - BlendIn) / (Frame - BlendIn)),
    /// so t runs 0 -> 1 — Restore is the START, CameraTarget is the END and the park slot.
    /// BlendInTime must stay 0 or the ease phase is empty (instant pin).
    /// </summary>
    private static void IssueLookAt(Vector3 targetPoint)
    {
        var playerManager = GameManager.GetPlayerManagerComponent();
        var fpsCamera = GameManager.GetVpFPSCamera();
        if (playerManager == null || fpsCamera == null)
            return;

        Transform cam = fpsCamera.transform;
        var look = new PlayerManager.LookAtTargetForTime
        {
            m_RestoreCameraOrientation = cam.rotation,                // glide START (t = 0)
            m_CameraTargetOrientation = Quaternion.LookRotation((targetPoint - cam.position).normalized, Vector3.up), // glide END + park slot (t = 1)
            m_ShouldRestoreCameraOrientation = false,                 // park on the target after expiry
            m_FrameTimeSeconds = RealToScaled(FirstGlideSeconds),     // lasts FirstGlideSeconds REAL seconds under the slowdown
            m_TimeRemaining = RealToScaled(FirstGlideSeconds),
            m_BlendInTime = 0f,          // MUST be 0, or the ease phase is empty (instant pin)
            m_BlendOutTime = 0f,
            m_TtweenInMethod = UITweener.Method.EaseOut, // quick whip that decelerates onto mark 1
            m_SteeperCurvesIn = false,
            m_TweenOutMethod = UITweener.Method.Linear,
            m_SteeperCurvesOut = false,
        };
        playerManager.RequestLookAtTarget(look);
        _nextLookAtIssue = Time.unscaledTime + FirstGlideSeconds; // real seconds; the struct itself gets scaled time
        ModLog.Debug("look-at glide issued toward the current mark");
    }

    /// <summary>
    /// Pursuit swing onto <paramref name="targetPoint"/> with a per-phase ANGULAR SPEED
    /// derived from the fire rhythm (§6): the segment lasts exactly the time left until the
    /// weapon may fire again, so even the swiftest swing lands when the next shot is due —
    /// the vanilla fire rate is preserved. <see cref="MaxSwingDegPerSec"/> caps the whip.
    /// Phase 1 (mark) and phase 2 (intercept) derive their own speed from their own
    /// remaining angle; each segment is issued on expiry or endpoint drift.
    /// </summary>
    private static float UpdateTrackingGlide(Vector3 targetPoint)
    {
        var playerManager = GameManager.GetPlayerManagerComponent();
        var fpsCamera = GameManager.GetVpFPSCamera();
        if (playerManager == null || fpsCamera == null)
            return 0f;

        Vector3 toTarget = targetPoint - fpsCamera.transform.position;
        if (toTarget.sqrMagnitude < 0.0001f)
            return 0f;
        toTarget.Normalize();
        Quaternion markRot = Quaternion.LookRotation(toTarget, Vector3.up);
        float remainingDeg = Quaternion.Angle(fpsCamera.transform.rotation, markRot);

        bool expired = Time.unscaledTime >= _segEndUnscaled;
        // Always a real angle: a MaxValue sentinel for "expired" used to leak into the log
        // as 3.4e38deg on every re-issue, which is the normal path, so it was unreadable.
        float drift = Quaternion.Angle(_segEndpointRot, markRot);
        bool drifted = drift > TrackReissueDriftDegrees &&
                       Time.unscaledTime - _segIssueUnscaled >= TrackReissueMinSeconds;
        if (!expired && !drifted)
            return remainingDeg; // active glide still healthy — let it run

        if (remainingDeg <= ArrivedDeg * Mathf.Max(1f, DeadEyeMod.Settings?.FireRateMultiplier ?? Settings.DefaultFireRateMultiplier))
        {
            // Parked on the target already: issuing a 0-degree glide would only burn a
            // dead frame. Just re-arm the endpoint bookkeeping so drift stays meaningful.
            // Under rapid fire the tolerance widens with the rate (x4 -> 2 deg): the shot
            // is committed from the intercept, not the view, so a wider park only stops
            // the 0.03 s micro-glide churn without affecting where the round goes.
            _segEndpointRot = markRot;
            _segEndUnscaled = Time.unscaledTime + TrackReissueMinSeconds;
            _segIssueUnscaled = Time.unscaledTime;
            return remainingDeg;
        }

        float duration = SwingDuration(remainingDeg);

        // Pre-advance the start slot by one frame of motion: the first game Update after a
        // re-issue applies t = 0 (the start slot) and would otherwise burn a dead frame.
        // Starting one step along the glide makes that first application land where the
        // next frame should be. Capped at half the way for very short segments.
        float preAdvance = Mathf.Min(Time.unscaledDeltaTime / duration, 0.5f);

        Transform cam = fpsCamera.transform;
        var look = new PlayerManager.LookAtTargetForTime
        {
            // Verified slot semantics (see IssueLookAt): START = m_RestoreCameraOrientation,
            // END = m_CameraTargetOrientation, t runs 0 -> 1. Swapping these makes the first
            // frame of every segment land on the endpoint — that is the camera teleport.
            m_RestoreCameraOrientation = Quaternion.Slerp(cam.rotation, markRot, preAdvance),
            m_CameraTargetOrientation = markRot,
            m_ShouldRestoreCameraOrientation = false,
            m_FrameTimeSeconds = RealToScaled(duration),
            m_TimeRemaining = RealToScaled(duration),
            m_BlendInTime = 0f, // MUST be 0, or the ease phase is empty (instant pin)
            m_BlendOutTime = 0f,
            m_TtweenInMethod = UITweener.Method.Linear, // constant angular speed
            m_SteeperCurvesIn = false,
            m_TweenOutMethod = UITweener.Method.Linear,
            m_SteeperCurvesOut = false,
        };
        playerManager.RequestLookAtTarget(look);
        _segEndpointRot = markRot;
        _segEndUnscaled = Time.unscaledTime + duration; // REAL seconds
        _segIssueUnscaled = Time.unscaledTime;
        string trigger = expired ? "expired" : $"drift {drift:F1}deg";
        ModLog.Debug($"pursuit glide issued ({remainingDeg:F1}deg over {duration:F2}s real = " +
                     $"{remainingDeg / duration:F0}deg/s, {trigger})");
        return remainingDeg;
    }

    /// <summary>
    /// Segment duration = the time left until the weapon may fire again, floored by the
    /// hard angular-speed ceiling. A <=0 budget (gate already open) means "get there now",
    /// still bounded by <see cref="MaxSwingDegPerSec"/> — never an instant snap.
    /// </summary>
    private static float SwingDuration(float remainingDeg)
    {
        float floor = Mathf.Max(MinSegmentSeconds, remainingDeg / MaxSwingDegPerSec);
        float duration = Mathf.Max(NextShotInRealSeconds(), floor);
        return Mathf.Clamp(duration, MinSegmentSeconds, MaxSegmentSeconds);
    }

    /// <summary>
    /// Real seconds until the next shot may fire: our own unscaled pacing plus the game's
    /// scaled gate (converted back to real time with the owned scale). When neither is
    /// meaningful (weapon raising, reloading) fall back to <see cref="FallbackTimeToGo"/>.
    /// </summary>
    private static float NextShotInRealSeconds()
    {
        float t = _nextShotUnscaled - Time.unscaledTime;
        var shooter = GameManager.GetVpFPSCamera()?.CurrentShooter;
        if (shooter != null)
        {
            float ts = Mathf.Max(0.001f, TimeScaler.Value);
            float gate = (shooter.m_NextAllowedFireTime - Time.time) / ts; // scaled -> real
            if (gate > t)
                t = gate;
        }
        if (t <= 0f)
            return 0f;
        return Mathf.Min(t, FallbackTimeToGo);
    }

    private static bool IsBoundGunStillCarried()
        => _boundGun != null && _boundGun.gameObject != null &&
           (GunResolver.IsInHands(_boundGun) || GunResolver.FindCarried() == _boundGun);

    private static bool FatigueEmpty()
    {
        var fatigue = GameManager.GetFatigueComponent();
        // m_CurrentFatigue is TIREDNESS (higher = more exhausted; the HUD rest bar is the
        // inverse), so the mode must end when the normalized value reaches 1, not 0.
        return fatigue == null || fatigue.GetNormalizedFatigue() >= 0.995f;
    }

    private static bool KeyPressed(KeyCode? key) => key.HasValue && Input.GetKeyDown(key.Value);

    // ---------------------------------------------------------------- skill gate

    /// <summary>Vanilla skills have five levels: `Skill.m_TierPoints` holds five thresholds.</summary>
    internal const int VanillaMaxSkillLevel = 5;

    /// <summary>
    /// The player's revolver skill level as the skills page numbers it (1-5), or 0 when it
    /// cannot be read (no world yet, no skills manager). `Skill.GetCurrentTierNumber()`
    /// returns the 0-based tier INDEX — verified on the 2.55 binary: 0 points -> 0, points at
    /// or above the last threshold -> 4 — so the number the player sees is that index + 1.
    /// </summary>
    internal static int RevolverSkillLevel()
    {
        try
        {
            var skill = GameManager.GetSkillsManager()?.GetSkill(SkillType.Revolver);
            return skill == null ? 0 : skill.GetCurrentTierNumber() + 1;
        }
        catch (System.Exception ex)
        {
            ModLog.Warn($"revolver skill unreadable: {ex.GetType().Name}: {ex.Message}");
            return 0;
        }
    }

    /// <summary>
    /// Unlock gate: the revolver skill must be at or above the configured level.
    /// <paramref name="level"/> is the current level (0 = unreadable), <paramref name="required"/>
    /// the setting clamped into the vanilla 1-5 range. An unreadable level only passes when the
    /// requirement is 1 — i.e. when the player asked for no gate at all. As soon as a level IS
    /// required, missing data must not open the gate; but a read failure must also not break the
    /// default, ungated experience.
    /// </summary>
    private static bool RevolverSkillUnlocked(out int level, out int required)
    {
        required = Mathf.Clamp(DeadEyeMod.Settings?.RequiredRevolverLevel ?? Settings.DefaultRequiredRevolverLevel,
                               1, VanillaMaxSkillLevel);
        level = RevolverSkillLevel();
        return level <= 0 ? required <= 1 : level >= required;
    }

    /// <summary>
    /// Names the guard that refuses to run the mode, or null while gameplay is live. The first
    /// failing gate wins, because it is the one worth reporting.
    /// </summary>
    internal static string? BlockingReason()
    {
        try
        {
            if (GameManager.GetPlayerManagerComponent() == null)
                return "no player manager";
            // The gameplay HUD: not a panel test, just the "a world is running" baseline.
            if (!InterfaceManager.TryGetPanel<Panel_HUD>(out var hud) || hud == null || !hud.IsEnabled())
                return "HUD not up";
            // The one that matters: a panel took the screen away from gameplay — the pause
            // menu (ESC), the inventory, the container view, crafting, the options page. The
            // game's own overlay flag answers it, so no panel class has to be listed: it is
            // "some cached panel is enabled and calls itself an overlay", and `Panel_Base`
            // makes that the DEFAULT (returns true) while the panels that share the screen
            // with gameplay override it to false (HUD, subtitles, save icon, camera, actions,
            // tutorial popup, loading). A new Hinterland menu is therefore covered for free.
            if (InterfaceManager.IsOverlayActiveImmediate())
                return "interface panel open";
            if (uConsole.m_On)
                return "debug console open";
            if (GameManager.m_IsPaused)
                return "game paused";
            return null;
        }
        catch (System.Exception ex)
        {
            // A half-loaded interface must never let the mode start by accident.
            return $"guard failed ({ex.GetType().Name})";
        }
    }

    /// <summary>Gameplay guard: a world is running and no interface panel owns the screen.</summary>
    internal static bool IsGameplayActive() => BlockingReason() == null;

    /// <summary>
    /// The toggle key was pressed outside gameplay (pause menu, inventory, console, pause).
    /// Logged at Info, throttled: a session log that shows this line plus the gate that refused
    /// is what turns "Dead Eye opened inside my inventory" into a settled question.
    /// </summary>
    private static void LogIgnoredActivation(string blocked)
    {
        if (Time.unscaledTime < _nextBlockedLog)
            return;
        _nextBlockedLog = Time.unscaledTime + 2f;
        ModLog.Info($"activation ignored: {blocked}");
    }
}
