using System.Collections.Generic;
using UnityEngine;

namespace DeadEye.Core;

/// <summary>
/// Central state machine.
/// Inactive --toggle--> Marking (bullet time, marks accumulate, cap = clip size) --toggle-->
/// Exiting (time lerps back to 1) --> Firing (glide onto each mark, then fire) --> Inactive.
/// The first shot waits for the first look-at glide (crosshair visibly swings onto target
/// 1), then the sequence runs at full vanilla rate with glides as pure visual tracking;
/// every projectile is redirected onto the committed aim point (docs/DEVELOPMENT.md §3/§5).
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
    private const float GlideSeconds = 0.55f;     // one look-at glide duration (game-native tween)
    private const float AimPointTtl = 0.5f;       // unscaled: how long a committed aim point stays valid

    private static float _nextLookAtIssue;        // unscaled: when the running glide expires
    private static bool _firstGlideDone;          // false until glide #1 of a sequence has played out

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
        // Warm the audio subsystem up during normal gameplay, long before the first
        // activation — the first winmm sound a process issues would otherwise be swallowed.
        if (State == DeadEyeState.Inactive && IsGameplayActive())
            ModAudio.Tick();

        // Keep target velocities fresh for the lead prediction while marks exist.
        if (_marks.Count > 0)
        {
            foreach (var m in _marks)
                m.SampleVelocity();
        }

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
            BeginExit(fireAfter: _marks.Count > 0);
            return;
        }

        if (KeyPressed(settings.MarkKey))
            TryMark();
    }

    private static void TickExiting()
    {
        // Snap fully to 1 once the lerp is done, then decide what comes next.
        if (Time.timeScale >= 0.98f || !TimeScaler.Owned)
        {
            TimeScaler.ForceRestore();
            if (_marks.Count > 0 && IsGameplayActive())
                StartFiring();
            else
            {
                ModAudio.Stop(); // no firing round: the activation sound ends with the mode
                State = DeadEyeState.Inactive;
            }
        }
    }

    private static void TickFiring()
    {
        if (!IsGameplayActive() || Time.unscaledTime > _sequenceDeadline)
        {
            ForceExit("gameplay lost or sequence watchdog");
            return;
        }

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

        // Rhythm: the FIRST shot waits for the first look-at glide to play out, so the
        // crosshair visibly swings onto the first target before its bullet leaves.
        // Every later shot fires the moment the weapon's own fire gate allows — the full
        // vanilla rate — while the glides keep chaining as pure visual tracking. Hit
        // accuracy never depends on the camera: each projectile is redirected onto the
        // committed intercept point when it spawns.
        if (!_firstGlideDone)
        {
            if (_nextLookAtIssue == 0f)
            {
                IssueLookAt(ComputeIntercept(mark, aimPoint)); // glide #1 of the sequence
                return;
            }
            if (Time.unscaledTime < _nextLookAtIssue)
                return; // glide #1 in flight
            _firstGlideDone = true; // glide #1 finished -> release the first shot
        }

        TryFireCurrentTarget(mark, aimPoint);

        // Visual tracking: chain a glide toward the current intercept whenever none runs.
        if (State == DeadEyeState.Firing && Time.unscaledTime >= _nextLookAtIssue)
            IssueLookAt(ComputeIntercept(mark, aimPoint));
    }

    /// <summary>
    /// Fire-gates for the current mark; when they all pass, commits the shot (snapshot the
    /// intercept, Fire(), advance). Returns false while any gate is still closed.
    /// </summary>
    private static bool TryFireCurrentTarget(Mark mark, Vector3 aimPoint)
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

        // Fire-rate gate: Fire() itself sets m_NextAllowedFireTime, so the pace of the
        // sequence is exactly the weapon's own fire rate (no hand-rolled timing).
        if (Time.time < shooter.m_NextAllowedFireTime || shooter.IsReloadCooldownActive)
            return false;

        var animation = GameManager.GetPlayerAnimationComponent();
        if (animation.IsReloading() ||
            !animation.IsAllowedToFire(shooter.m_Weapon.m_GunItem.m_AllowHipFire))
            return false; // raise animation / blocked by game state

        // Recompute the intercept at the fire moment and snapshot it: the redirect patches
        // aim the fresh projectile exactly there, independent of where the camera is.
        Vector3 intercept = ComputeIntercept(mark, aimPoint);
        _lastAimPoint = intercept;
        _aimPointExpiry = Time.unscaledTime + AimPointTtl;

        // Fire() consumes the round and spawns the projectile synchronously: when it
        // returns, the shot is committed.
        shooter.Fire();
        ModLog.Info($"shot {_fireIndex + 1}/{_marks.Count} fired (lead {mark.Velocity.magnitude:F1} m/s, intercept {(intercept - aimPoint).magnitude:F2} m off current pos)");
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

    private static void BeginExit(bool fireAfter)
    {
        State = DeadEyeState.Exiting;
        Overlay.SetOverlay(false);
        if (!fireAfter)
        {
            _marks.Clear();
            MarkCap = 0;
        }
        else if (_boundGun != null && !GunResolver.IsInHands(_boundGun))
        {
            // Draw at the exit press so the raise animation overlaps the time-restore
            // lerp and the first shot is not delayed by an idle equip afterwards.
            GameManager.GetPlayerManagerComponent().EquipItem(_boundGun, false);
            ModLog.Info("drawing bound revolver for the firing sequence");
        }
        TimeScaler.LerpTo(1f, DeadEyeMod.Settings!.ExitLerpSpeed);
        ModLog.Info($"Dead Eye exiting (fireAfter={fireAfter}, marks={_marks.Count})");
    }

    private static void StartFiring()
    {
        if (TimeScaler.Owned)
            TimeScaler.ForceRestore();

        // Fallback draw (normally already started in BeginExit).
        if (_boundGun != null && !GunResolver.IsInHands(_boundGun))
            GameManager.GetPlayerManagerComponent().EquipItem(_boundGun, false);

        // Strip any equip-related fire cooldown so the first shot is not artificially
        // delayed; per-shot pacing afterwards stays on the weapon's own fire rate
        // (Fire() pushes m_NextAllowedFireTime forward on every call).
        var shooter = GameManager.GetVpFPSCamera()?.CurrentShooter;
        if (shooter != null)
            shooter.m_NextAllowedFireTime = Mathf.Min(shooter.m_NextAllowedFireTime, Time.time);

        _fireIndex = 0;
        _sequenceDeadline = Time.unscaledTime + SequenceWatchdog;
        _targetDeadline = Time.unscaledTime + TargetWaitSeconds;
        _nextLookAtIssue = 0f; // glide toward the first mark on the next Tick
        _firstGlideDone = false;
        State = DeadEyeState.Firing;
        ModLog.Info($"fire sequence started with {_marks.Count} mark(s)");
        Overlay.SetMessage("处决 · 开火", 1.2f);
    }

    private static void AdvanceTarget(string reason)
    {
        ModLog.Debug($"target {_fireIndex} done ({reason})");
        _fireIndex++;
        _targetDeadline = Time.unscaledTime + TargetWaitSeconds;
        _nextLookAtIssue = 0f; // glide toward the next mark immediately
        if (_fireIndex >= _marks.Count)
            FinishFiring();
    }

    private static void FinishFiring()
    {
        _marks.Clear();
        MarkCap = 0;
        // NOTE: _lastAimPoint is intentionally left to expire via its TTL — the last
        // bullet's Start() runs after this and still needs the redirect.
        State = DeadEyeState.Inactive;
        ModAudio.Stop(); // the activation sound must not outlive the mode
        Overlay.SetMessage("死神之眼结束", 1.5f);
        ModLog.Info("fire sequence finished");
    }

    /// <summary>Hard exit from any state: restore everything, no firing.</summary>
    internal static void ForceExit(string reason)
    {
        if (State == DeadEyeState.Inactive && !TimeScaler.Owned)
            return;
        ModLog.Info($"forced exit: {reason}");
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
    /// look-at system (story-mode cutscenes use it). Verified by disassembly: the struct
    /// is stored wholesale and its Update() slerps from m_RestoreCameraOrientation (our
    /// captured "from") to m_CameraTargetOrientation over m_BlendInTime, then pins the
    /// view at the target until m_TimeRemaining expires; with the restore flag false
    /// there is no blend-out — normal mouse look resumes where the glide ended.
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
            m_RestoreCameraOrientation = cam.rotation,                 // slerp FROM
            m_CameraTargetOrientation = Quaternion.LookRotation((targetPoint - cam.position).normalized, Vector3.up), // slerp TO
            m_ShouldRestoreCameraOrientation = false,                  // pin at target, no blend-out
            m_FrameTimeSeconds = GlideSeconds,
            m_TimeRemaining = GlideSeconds,
            m_BlendInTime = GlideSeconds,
            m_BlendOutTime = 0.1f,
            m_TtweenInMethod = UITweener.Method.EaseInOut,
            m_SteeperCurvesIn = false,
            m_TweenOutMethod = UITweener.Method.Linear,
            m_SteeperCurvesOut = false,
        };
        playerManager.RequestLookAtTarget(look);
        _nextLookAtIssue = Time.unscaledTime + GlideSeconds;
        ModLog.Debug("look-at glide issued toward the current mark");
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
