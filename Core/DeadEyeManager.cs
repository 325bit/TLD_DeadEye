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

    private const float TargetWaitSeconds = 4f;   // unscaled: max wait per target
    private const float SequenceWatchdog = 20f;   // unscaled: whole sequence abort
    private const float FirstGlideSeconds = 0.55f; // opening swing duration (game-native tween)
    private const float MaxSwingDegPerSec = 400f; // hard ceiling: a segment never whips faster than this
    private const float MinSegmentSeconds = 0.03f; // one frame-ish floor for a swing segment
    private const float MaxSegmentSeconds = 2f;   // ceiling so a stuck fire gate cannot crawl forever
    private const float FallbackTimeToGo = 0.5f;  // time budget when the fire gate reports nothing useful
    private const float ArrivedDeg = 0.5f;        // no new segment is issued this close to the target
    private const float TrackReissueDriftDegrees = 5f; // re-issue the pursuit glide when the endpoint drifted this far off its captured value
    private const float TrackReissueMinSeconds = 0.12f; // and at most this often (storm guard; re-issues are dead-frame-free via the pre-advanced start)
    private const float VelocitySampleSeconds = 0.25f; // real-time gap between the two root-position samples that freeze a target's velocity
    private const float AimPointTtl = 0.5f;       // unscaled: how long a committed aim point stays valid

    private static float _nextLookAtIssue;        // unscaled: when the running glide expires
    private static bool _firstGlideDone;          // false until glide #1 of a sequence has played out
    private static Quaternion _segEndpointRot;    // orientation the active pursuit glide heads to
    private static float _segEndUnscaled;         // unscaled: when the active pursuit glide expires
    private static float _segIssueUnscaled;       // unscaled: when the active pursuit glide was issued
    private static float _nextShotUnscaled;       // unscaled: when the next shot of the sequence may fire
    private static float _nextGateLog;            // unscaled: throttle for the shot-gated diagnostic line

    // Two-sample velocity measurement for the CURRENT mark (uniform-linear assumption):
    // sample the AI root once when the target becomes active, again after the interval,
    // freeze the vector — the intercept is then stable and the camera never chases a
    // re-estimated direction (the source of the teleport-like spikes).
    private static bool _velFirstTaken;
    private static Vector3 _velFirstPos;
    private static float _velFirstUnscaled;
    private static bool _interceptReady;          // true once the current mark's velocity is frozen
    private static bool _lastCameraPhase;         // camera phase currently in force (false = mark body, true = intercept)

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

    /// <summary>Aim point for the camera driver while the firing sequence runs (null otherwise).</summary>
    internal static Vector3? CurrentAimPoint
    {
        get
        {
            if (State != DeadEyeState.Firing || _fireIndex >= _marks.Count)
                return null;
            return _marks[_fireIndex].TryGetWorldPoint(out var p) ? p : null;
        }
    }

    // ---------------------------------------------------------------- Tick

    internal static void Tick()
    {
        // Advance the owned time scale on unscaled time, then push it onto both clocks.
        // The authoritative re-assertion runs from the InterfaceManager.Update POSTFIX
        // (Patches.TimeScaleGuardPatch), i.e. after TimeAccelerator's prefix stomps it.
        TimeScaler.Advance();

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
                if (IsGameplayActive() && KeyPressed(DeadEyeMod.Settings?.ToggleKey))
                    TryEnter();
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
        if (!IsGameplayActive())
        {
            ForceExit("gameplay lost");
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

        if (KeyPressed(settings.ToggleKey))
        {
            // With marks: end the marking phase and dive straight into the firing phase —
            // the yellow filter and the slowdown carry over to the end of the sequence, and
            // only the player is compensated back to real time. With no marks: plain exit.
            if (_marks.Count > 0)
                StartFiring();
            else
                BeginExit();
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
        }
    }

    private static void TickFiring()
    {
        if (!IsGameplayActive() || Time.unscaledTime > _sequenceDeadline)
        {
            ForceExit("gameplay lost or sequence watchdog");
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
        if (!mark.TryGetWorldPoint(out Vector3 aimPoint))
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

        // Two-sample velocity window for this mark (uniform-linear assumption), started
        // immediately so it runs DURING glide #1 / the pursuit swing instead of delaying
        // the shot behind them.
        if (!_velFirstTaken)
        {
            _velFirstPos = mark.RootPosition;
            _velFirstUnscaled = Time.unscaledTime;
            _velFirstTaken = true;
        }
        else if (!_interceptReady && Time.unscaledTime - _velFirstUnscaled >= VelocitySampleSeconds)
        {
            mark.SetVelocity(mark.RootPosition - _velFirstPos, Time.unscaledTime - _velFirstUnscaled);
            _interceptReady = true;
            ModLog.Debug($"velocity frozen for target {_fireIndex} ({mark.Velocity.magnitude:F1} m/s)");
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
        // interval by 1/scale. Pace the sequence on UNSCALED time instead: the interval
        // Fire() pushes onto the gate IS the vanilla real rate (vanilla runs at scale 1),
        // and the scaled gate is neutralized right before firing. Result: vanilla real
        // fire rate while the world stays slowed.
        bool pacingOpen = Time.unscaledTime >= _nextShotUnscaled && !shooter.IsReloadCooldownActive;

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

        // Recompute the intercept at the fire moment and snapshot it: the redirect patches
        // aim the fresh projectile exactly there, independent of where the camera is.
        Vector3 intercept = ComputeIntercept(mark, aimPoint);
        _lastAimPoint = intercept;
        _aimPointExpiry = Time.unscaledTime + AimPointTtl;

        // Fire() consumes the round and spawns the projectile synchronously: when it
        // returns, the shot is committed. The scaled gate is neutralized first (our
        // unscaled pacing above is authoritative under the slowdown).
        shooter.m_NextAllowedFireTime = Time.time;
        shooter.Fire();
        // The interval Fire() pushes onto the gate is the vanilla fire rate expressed in
        // scaled-time units — vanilla always runs at scale 1, so the number IS the real
        // interval. Use it as-is (no rescaling): the player keeps the vanilla real rate.
        float intervalReal = Mathf.Max(0f, shooter.m_NextAllowedFireTime - Time.time);
        _nextShotUnscaled = Time.unscaledTime + Mathf.Max(0.05f, intervalReal);
        ModLog.Info($"shot {_fireIndex + 1}/{_marks.Count} fired (lead {mark.Velocity.magnitude:F1} m/s, intercept {(intercept - aimPoint).magnitude:F2} m off current pos, next in {intervalReal:F2}s real)");
        AdvanceTarget("shot fired");
        return true;
    }

    /// <summary>Predicted world point the shot should be aimed at (lead + gravity drop).</summary>
    private static Vector3 ComputeIntercept(Mark mark, Vector3 aimPoint)
    {
        var camera = GameManager.GetMainCamera();
        Vector3 muzzlePos = camera != null ? camera.transform.position : GameManager.GetPlayerTransform().position;
        float launchSpeed = Ballistics.MuzzleVelocityFor(
            GameManager.GetVpFPSCamera()?.CurrentShooter?.m_Weapon?.m_GunItem?.name);
        return Ballistics.PredictIntercept(muzzlePos, aimPoint, mark.Velocity, launchSpeed);
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
        anim.m_Animator.speed = on && s > 0.01f ? 1f / s : 1f;
    }

    /// <summary>
    /// Convert a REAL-time duration to the scaled seconds the game's look-at struct expects:
    /// the struct's m_TimeRemaining is decremented by SCALED deltaTime, so a glide that must
    /// last D real seconds is issued with D * timeScale. Uses the OWNED scale rather than
    /// Time.timeScale: a stomped readback would shrink every glide and read as a teleport.
    /// </summary>
    private static float RealToScaled(float realSeconds)
        => realSeconds * Mathf.Max(0.05f, TimeScaler.Value);

    private static void TryEnter()
    {
        var settings = DeadEyeMod.Settings!;
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
        _lastAimPoint = null;
        _aimPointExpiry = 0f;

        var fatigueNow = GameManager.GetFatigueComponent();

        // NOTE: no auto-draw here. The revolver stays wherever it is (hands or backpack)
        // during bullet time; drawing happens only when the firing sequence starts.
        TimeScaler.LerpTo(settings.TargetTimeScale, settings.EnterLerpSpeed);
        Overlay.SetOverlay(settings.YellowOverlay && settings.OverlayOpacity > 0f);
        State = DeadEyeState.Marking;
        ModLog.Info($"Dead Eye entered: cap={MarkCap}, bound={GunResolver.BoundGearName}, fatigue norm={fatigueNow?.GetNormalizedFatigue():F3}");
        Overlay.SetMessage($"死神之眼 · 标记目标（0/{MarkCap}）", 2.5f);
        ModAudio.PlayEnter();
    }

    private static void BeginExit()
    {
        State = DeadEyeState.Exiting;
        Overlay.SetOverlay(false);
        _marks.Clear();
        MarkCap = 0;
        TimeScaler.LerpTo(1f, DeadEyeMod.Settings!.ExitLerpSpeed);
        ModLog.Info("Dead Eye exiting (no marks)");
    }

    private static void StartFiring()
    {
        // Bullet time is KEPT through the firing phase, at its own (milder) pace: the
        // yellow filter persists and the time scale lerps from the marking value to
        // ExecutionTimeScale (default 0.8). Only the player is compensated back toward
        // real time (first-person Animator + unscaled fire pacing, see TickFiring).
        var settings = DeadEyeMod.Settings!;
        TimeScaler.LerpTo(settings.ExecutionTimeScale, settings.EnterLerpSpeed);

        // Draw the bound revolver now so the raise animation (compensated to real speed)
        // overlaps the first glide.
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

    /// <summary>Restart the two-sample velocity window for the newly active mark.</summary>
    private static void ResetVelocitySampling()
    {
        _velFirstTaken = false;
        _interceptReady = false;
        _lastCameraPhase = false;
        _velFirstPos = default;
        _velFirstUnscaled = 0f;
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
        _marks.Clear();
        MarkCap = 0;
        // NOTE: _lastAimPoint is intentionally left to expire via its TTL — the last
        // bullet's Start() runs after this and still needs the redirect.
        ApplyPlayerTimeCompensation(false);
        Overlay.SetOverlay(false); // the yellow filter ends with the sequence
        State = DeadEyeState.Exiting; // shared restore tail lerps time back to 1
        TimeScaler.LerpTo(1f, DeadEyeMod.Settings!.ExitLerpSpeed);
        ModLog.Info("fire sequence finished");
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
        _lastAimPoint = null;
        _aimPointExpiry = 0f;
        State = DeadEyeState.Inactive;
    }

    /// <summary>Scene change / menu hard reset.</summary>
    internal static void HardReset()
    {
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
        var rawHits = Physics.RaycastAll(ray, 150f);
        // Copy into a managed array so System.Array.Sort(comparison) is usable.
        var hits = new RaycastHit[rawHits.Length];
        for (int i = 0; i < rawHits.Length; i++)
            hits[i] = rawHits[i];
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            var ai = hit.collider.gameObject.layer == 16
                ? hit.collider.GetComponent<BaseAi>()
                : hit.collider.gameObject.layer == 27
                    ? hit.collider.GetComponentInParent<BaseAi>()
                    : null;

            if (ai == null || ai.GetAiMode() == AiMode.Dead)
                continue;

            _marks.Add(new Mark(ai, hit.point));
            Overlay.SetMessage($"已标记 {_marks.Count}/{MarkCap}", 1.2f);
            ModAudio.PlayMark();
            ModLog.Debug($"marked {ai.gameObject.name} at {hit.point} ({_marks.Count}/{MarkCap})");
            return;
        }

        Overlay.SetMessage("准星处没有活物", 1.2f);
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
        float drift = expired
            ? float.MaxValue
            : Quaternion.Angle(_segEndpointRot, markRot);
        bool drifted = drift > TrackReissueDriftDegrees &&
                       Time.unscaledTime - _segIssueUnscaled >= TrackReissueMinSeconds;
        if (!expired && !drifted)
            return remainingDeg; // active glide still healthy — let it run

        if (remainingDeg <= ArrivedDeg)
        {
            // Parked on the target already: issuing a 0-degree glide would only burn a
            // dead frame. Just re-arm the endpoint bookkeeping so drift stays meaningful.
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
        ModLog.Debug($"pursuit glide issued ({remainingDeg:F1}deg over {duration:F2}s real = {remainingDeg / duration:F0}deg/s, drift {drift:F1}deg)");
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
            float ts = Mathf.Max(0.05f, TimeScaler.Value);
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

    /// <summary>Gameplay guard: player manager exists and the gameplay HUD is up.</summary>
    internal static bool IsGameplayActive()
    {
        if (GameManager.GetPlayerManagerComponent() == null)
            return false;
        if (!InterfaceManager.TryGetPanel<Panel_HUD>(out var hud) || hud == null || !hud.IsEnabled())
            return false;
        return true;
    }
}
