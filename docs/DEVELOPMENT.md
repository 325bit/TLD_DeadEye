# Dead Eye (TLD_DeadEye) — Development Notes

> Language: English only — code comments, docs/ and CHANGELOG.

RDR2-style Dead Eye for The Long Dark 2.55: bullet-time marking mode plus a chain-fire sequence over the vanilla firing pipeline, with lead-predicted bullet redirection. The camera is never touched.

## 1. File map

| File | Responsibility |
|---|---|
| `DeadEyeMod.cs` | MelonMod entry: settings registration, `PatchAll`, per-frame `Tick`, scene-change hard reset, `OnGUI` overlay |
| `Settings.cs` | `JsonModSettings` panel (weapon binding, keybinds, bullet time, fatigue, visuals, verbose log) |
| `BuildInfo.cs` / `ModLog.cs` | Metadata; static logger wrapper (avoids the `Log.` CS0117 pitfall) |
| `Core/DeadEyeState.cs` | State enum: `Inactive → Marking → Exiting → Firing` |
| `Core/DeadEyeManager.cs` | Central state machine, mark store, firing sequencer, fool-proof restoration |
| `Core/Mark.cs` | One mark: `BaseAi` + local-space offset so the mark follows the animal; two-sample frozen velocity for lead prediction |
| `Core/GunResolver.cs` | Finds the bound revolver (hands first, then backpack) and reads its clip |
| `Core/TimeScaler.cs` | Owns the bullet-time scale (`Value`, never read back) and re-asserts it on both clocks; driven from the tick + an `InterfaceManager.Update` postfix |
| `Core/Ballistics.cs` | Muzzle-velocity table (AdaptiveArsenal values) + fixed-point intercept prediction for moving targets |
| `Core/ModAudio.cs` | Loads the two WAV sound effects from `Mods/DeadEye/Asset` (hand-parsed RIFF/PCM16) and plays them through `winmm!waveOut` — one device handle per sound, so sounds overlap |
| `Ui/Overlay.cs` | Yellow Canvas filter + OnGUI markers and message line |
| `Core/HomingSteering.cs` | Steers in-flight AdaptiveArsenal projectiles toward their own mark (homing bullets); registered from the Fire prefix, ticked from the manager |
| `Patches/Patches.cs` | `BulletStartPatch` / `AdaptiveProjectileRedirectPatch` (redirect the spawn onto the intercept, zero spread, register homing), `FatiguePatch`, `TimeScaleGuardPatch`, `PlayerAnimSpeedPatch` |

Visual glide: no dedicated component — `DeadEyeManager.IssueLookAt` issues one-shot requests through the game's own `PlayerManager.RequestLookAtTarget` (see §6).

## 2. Per-frame pipeline

```
MelonMod.OnUpdate
  └─ DeadEyeManager.Tick          (state machine, input, guards)
       ├─ TimeScaler.Advance      (unscaled lerp of the OWNED scale; the authoritative push happens in the InterfaceManager.Update postfix)
       ├─ velocity: per mark, TWO root-position samples taken once (0.25 s apart, real time), vector frozen — uniform-linear lead
       ├─ Marking:  draw stage (auto-equip + TargetTimeScale) → FocusedTimeScale once the draw window ends; toggle WITH marks → StartFiring (bullet time kept); toggle with no marks → BeginExit (lerp back to 1) → Exiting → Inactive
       ├─ Exiting:  restore tail shared by the no-marks exit and the end of the firing sequence → lerp back to 1 → Inactive
       └─ Firing:   BULLET TIME KEPT — the yellow filter and the slowdown persist to the end; the first-person Animator is driven at speed = 1/owned scale (real-time player) and the fire pacing runs on UNSCALED time (the scaled m_NextAllowedFireTime gate is neutralized; the interval Fire() pushes is used as-is) so the player fires at the vanilla real rate
                    → one-shot glide #1 (0.55 s EaseOut), then per-segment pursuit glides whose duration = time until the next shot (per phase: mark body → predicted intercept), capped at 400 deg/s; re-issued on expiry or >5 deg drift, start slot pre-advanced one frame
                    → per-target gates (cooldown / anim) — the camera is NOT a gate → predict intercept
                    → snapshot LastShotAimPoint → vp_FPSShooter.Fire() → next mark

Patches
  ├─ InterfaceManager.Update postfix: re-assert the owned time scale (beats prefix writers such as TimeAccelerator)
  ├─ vp_Bullet.Start prefix:   redirect the spawned bullet onto the predicted intercept, zero Accuracy
  └─ Fatigue.Update postfix:   extra drain while Marking
```

## 3. Injection points (2.55)

| Target | Kind | Why |
|---|---|---|
| `Fatigue.Update` | Postfix | Flat fatigue cost while marking: `FatiguePerSecond` percent of the bar per UNSCALED second, ADDED to `m_CurrentFatigue` (tiredness) scaled by `m_MaxFatigue` |
| `vp_Bullet.Start` | Prefix (private, patched by name) | Vanilla path only (AdaptiveArsenal absent): vanilla resolves the hit inside the bullet's `Start()` raycast; the prefix redirects the freshly spawned bullet straight onto the lead-predicted intercept point and zeroes its `Accuracy` spread. Aim point comes from `DeadEyeManager.LastShotAimPoint` (snapshotted at `Fire()` time, TTL 0.5 s, since the sequencer already advanced and the LAST bullet's `Start()` even runs after the state machine is back to `Inactive`) |
| `AdaptiveArsenal.Components.ProjectileItem.Fire` | Prefix, bound dynamically via `AccessTools.TypeByName` at startup | AdaptiveArsenal path: its `vp_FPSShooter.Awake` prefix swaps `ProjectilePrefab` for its own physics projectile (Rigidbody, gravity, drag, random spread) and its `Fire` prefix spawns it via `SpawnAndFire` — the vanilla `vp_Bullet` is NEVER spawned, which made the patch above dead code (verified by live logs: 0 redirect lines across every 0.1.4/0.1.5 test shot). Must be a **prefix** (0.2.1 lesson: the launch is `AddForce`, which only lands on the next physics step, so a postfix reads a zero velocity): the prefix aims the projectile transform before the body runs, so AdaptiveArsenal's own launch force flies exactly onto the mark and its random spread is bypassed |

Deliberately **not** patched: `vp_FPSShooter.Fire` (called directly — it already performs the ammo check, dry-fire audio, jam roll and `PlayerAnimation.IsAllowedToFire` gate, and consumes a round via `GunItem.RemoveNextFromClip`). The animation-event path (`OnAnimationEvent_Generic_Fire_FX`) is deliberately not patched either: full `Fire()` disassembly shows the shot direction is computed and consumed **inline inside `Fire()` itself** (sight projection → `MaybeAdjustShotPositionForNearShot` → projectile spawn → `GunItem.Fired`), so when `Fire()` returns the shot is already committed — no separate signal is needed.

## 4. Data and fields (verified against the 2.55 dump)

| Member | Role in this mod |
|---|---|
| `GunItem.NumRoundsInClip()` | Mark cap = cylinder contents at entry |
| `GunItem.m_Clip` (`List<int>`) | What `NumRoundsInClip` counts; `Fire` pops one per shot |
| `PlayerManager.EquipItem(GearItem, bool)` | Draw the bound revolver at the exit press (2nd param is `fromDeserialize`, not an immediate flag — verified by dump) |
| `PlayerManager.m_ItemInHands` | Detect what the player holds |
| `Inventory.m_Items` (`List<GearItemObject>`) | Backpack scan; iterating the struct list avoids il2cpp generic-delegate marshalling of `GetGearItems(Predicate, List)` |
| `GameManager.GetVpFPSCamera().CurrentShooter` | The active `vp_FPSShooter` to call `Fire()` on |
| `vp_FPSShooter.m_NextAllowedFireTime` | Per-shot cooldown gate (set by the game from `m_FiringRateSeconds`) |
| `PlayerAnimation.IsAllowedToFire(bool)` | Param is `allowHipFire` (verified by dump): `IsAllowedToFire(false)` ⇔ the player is in the aim pose (AdaptiveArsenal's convention); used as the fire gate with the weapon's own `m_GunItem.m_AllowHipFire` |
| `PlayerAnimation.IsReloading()` | Gate the sequence during reloads |
| `FirstPersonWeapon.m_FrontSight / m_RearSight / m_BulletEmissionPoint` | Sight-projection math (mirrors AdaptiveArsenal `ProjectileUtilities`) |
| `vp_FPSCamera.Angle` / `SetAngle(yaw, pitch)` | The game's authoritative look state (`m_Yaw` 0x144 / `m_Pitch` 0x140, verified by `set_Angle` stores). `Angle` = (yaw, pitch). `LookAtTargetForTime.Update` ends in `set_Angle`, so this is the sanctioned write path |
| `vp_Bullet` (`Range/Damage/Accuracy/...`) | Vanilla projectile component; the hit is resolved inside its `Start()` raycast. `Accuracy` is residual spread (zeroed for sequence shots). Only spawns when AdaptiveArsenal is absent |
| `Fatigue.m_CurrentFatigue` / `m_MaxFatigue` / `GetNormalizedFatigue()` | m_CurrentFatigue is TIREDNESS: higher = more exhausted (`Fatigue.Update` ADDS the burn via `AddFatigue`; `IsExhausted()` is current > threshold; the HUD rest bar is the inverse). Scale follows `m_MaxFatigue`; `GetNormalizedFatigue()` = current / max |
| `BaseAi.GetAiMode() != AiMode.Dead` | Alive check for marks; animal hitboxes live on layers 16 (direct) / 27 (parent) |
| Muzzle velocities (AdaptiveArsenal `ProjectileItem` table) | Revolver 400 / Fancy 600 / Green 400 / StubNosed 300 m/s nominal; AdaptiveArsenal launches at x 0.5 (its `ScaleMultiplier`) under gravity, so with it present the lead prediction uses the halved speeds and adds a gravity-drop offset to the aim point |
| `PlayerManager.RequestLookAtTarget(LookAtTargetForTime)` | Game-native look-at glide (story-mode cutscenes use it). Verified by disassembly: the struct is stored wholesale and its `Update()` slerps from `m_RestoreCameraOrientation` (caller-provided start) to `m_CameraTargetOrientation` over `m_BlendInTime` with the chosen `UITweener.Method`, ending in euler → negated → `vp_FPSCamera.set_Angle`. With `m_ShouldRestoreCameraOrientation=false` there is no blend-out — the view pins at the target until `m_TimeRemaining` expires. `PlayerManager.Update` invokes it with **no weapon-state gating** (only the `m_IsLookAtTargetForTimeActive` flag at 0x540), so glides work with a gun raised |
| `UITweener.Method` | Linear 0 / EaseIn 1 / EaseOut 2 / EaseInOut 3 (the glide uses EaseInOut) |

## 5. Firing sequence design

1. **Marking runs in two stages**: the revolver is drawn AT ACTIVATION (when stowed). `RevealTimeScale` (fixed 0.1) only covers the REVEAL — `RevealSeconds` (fixed 0.7 s, until the muzzle clears the bottom of the screen in the reference footage) — then the scale lerps to `FocusedMarkingScale` (fixed 0.01) for the rest of the marking phase, so the remaining raise crawls through the whole marking phase and reads as a slow-draw close-up instead of finishing during the approach. A revolver already in hand skips the reveal. Time-driven because the game exposes no "weapon raised" flag (`PlayerAnimation` has none, and `vp_FPSShooter.m_Weapon` is set before the animation finishes) — the reveal moment is a visual event, so its duration is the only handle.
2. Entering the execution keeps bullet time but at `ExecutionTimeScale` (default 0.8); the revolver is already in hand.
3. `StartFiring` clamps `m_NextAllowedFireTime` down to now, stripping any equip-related cooldown; per-shot pacing afterwards is the weapon's own fire rate (`Fire()` pushes it forward on every call).
4. **Rhythm (0.2.1)**: a look-at glide toward the first mark's predicted intercept is issued unconditionally at sequence start, and the FIRST shot waits for it to play out (0.55 s) so the crosshair visibly swings onto the first target. Every later shot fires the moment the weapon's own fire gate allows — the full vanilla rate — while the camera keeps swinging via per-segment LINEAR pursuit glides (see §6). Hits never depend on the camera: each projectile is redirected onto its committed intercept point at spawn. (0.2.0's per-shot glide wait throttled the sequence to ~1.1 s/shot, and its fire-attempt-first order let shot 1 leave ~20 ms after exit with the gun already in hand — both fixed.)
5. Fire gates: `Time.time >= m_NextAllowedFireTime` AND not reload-cooling AND not reloading AND `IsAllowedToFire(m_GunItem.m_AllowHipFire)`. No camera-based gate. The per-target window (4 s) is checked BEFORE the gates so a stuck gate cannot stall the sequence.
6. **Lead-predicted projectile redirect**: when the gates pass, the intercept is recomputed at the fire moment — flight time `t = distance / effective launch speed` (AdaptiveArsenal table, halved + gravity-drop offset when AdaptiveArsenal is present; fixed-point iterated 3x), intercept = current mark position + animal velocity x t (velocity frozen from a two-sample root-position pair taken 0.25 s apart in real time, clamped to 30 m/s — never re-estimated per Tick, see 0.5.2). The intercept is snapshotted into `LastShotAimPoint` (TTL 0.5 s), `Fire()` is called, and whichever projectile spawned — vanilla `vp_Bullet` (Start prefix) or AdaptiveArsenal's `ProjectileItem` (Fire prefix) — is aimed exactly at that point with zero spread. Running targets are hit where they WILL be, regardless of camera pose or spread.
7. Watchdogs: 8 s per target (skips it), 30 s for the whole sequence (hard exit). Dead targets are skipped.
8. **Homing bullets** (`HomingBullets` setting, default on): right before `Fire()` the manager publishes the active mark (its `GameObject` + body offset); the AdaptiveArsenal `Fire` prefix aims the spawn AND registers the projectile's `Rigidbody` in `HomingSteering` with that mark. Every tick the bullet's velocity direction is rotated toward the target ROOT plus a height (`Vector3.RotateTowards`, `BaseTurnDegPerSec` 400 times the strength setting, speed magnitude preserved), correcting at most 40 % of the remaining angle per frame. Two details matter: the aim point must NOT be `TransformPoint(localOffset)` — that point sweeps sideways when the animal turns on the spot and the bullet visibly chases the jump — and splitting the correction over frames keeps the path a smooth curve rather than a line with a kink. The control law is deliberately pure pursuit so the strength setting is the single knob being tuned for hit rate versus look: a lead term would confound those measurements. The `homing engaged: initial error ...` debug line separates the two failure modes — a large initial error means the SPAWN direction is off (fix the lead prediction), a growing error along the flight means the turn rate is too low. Each entry keeps its OWN target, so an in-flight bullet never retargets when the sequence advances to the next mark. Steering runs on SCALED delta time on purpose — the bullet lives in the slowed world, so its turn rate must slow down with it. The vanilla `vp_Bullet` resolves its hit inside `Start()` and has no physics body, so homing applies to the AdaptiveArsenal path only; without it the ballistic behavior is unchanged.

## 6. Camera control (game-native look-at glides)

- **Verified `LookAtTargetForTime` semantics (2.55 disassembly of `Update`, re-read in 0.5.4 — the 0.4.6 reading was INVERTED on both halves)**: with `m_TimeRemaining` counting DOWN from `m_FrameTimeSeconds`, the factor is `raw = 1 - (m_TimeRemaining - m_BlendInTime) / (m_FrameTimeSeconds - m_BlendInTime)` and the update calls `Slerp(m_RestoreCameraOrientation, m_CameraTargetOrientation, Tween(raw))`. **t therefore runs 0 -> 1: `m_RestoreCameraOrientation` is the START and `m_CameraTargetOrientation` is the END** — which is exactly what the field names say. When `m_TimeRemaining` reaches zero the update early-returns and the view stays where the last application left it, i.e. parked on the t=1 end (`m_ShouldRestoreCameraOrientation = false` skips the blend-out pass entirely). `m_BlendInTime` must be 0: the ease phase lasts `FrameTime - BlendIn` seconds, so `FrameTime == BlendIn` (everything up to 0.4.5) emptied it and every request collapsed into an instant pin. The 0.4.6 correction ("swapped slots, t 1 -> 0") got the direction AND the slots wrong, which made the first Update of every segment apply the endpoint — the 0.5.3 "camera teleports" report. The final angle path is euler -> negate -> `vp_FPSCamera.set_Angle` (authoritative internal yaw/pitch state).
- **Two phases, each with its own angular speed (0.5.4)**: while the two-sample velocity window is still running, the swing heads for the MARK BODY (`aimPoint`) at angular speed #1; once the velocity is frozen it heads for the predicted INTERCEPT at speed #2. Both phases share the same time budget — the real seconds left until the weapon may fire again — so each derives its own speed from its own remaining angle (`remaining / time-to-go`), with `MaxSwingDegPerSec` (400) as a hard ceiling so a huge inter-target angle whips fast instead of snapping. **The shot does NOT wait for the camera**: the vanilla fire rate is preserved and the projectile redirect owns the hit. A budget of zero or less means "get there now" at the ceiling rate, never an instant snap. Segments already within 0.5 deg of the target are not re-issued at all (0.5.3 re-issued 0.05 s stubs in a loop while the animation gate was closed).
- **Issue policy**: the opening swing is a single EaseOut `IssueLookAt` over `FirstGlideSeconds` (0.55 s — fast start, decelerating onto mark 1). Afterwards the pursuit glide is re-issued per SEGMENT, when the active glide expires or the mark drifted more than 5 deg off the glide's captured endpoint (at most every 0.12 s). **The start slot is pre-advanced by one frame of motion** (`Slerp(current, mark, dt/duration)`, capped at 50%): the first game Update after a re-issue otherwise burns a dead frame. All yaw/pitch conventions and blending stay the game's own (disassembly-confirmed: `PlayerManager.Update` runs the look-at with no weapon-state gating).
- **History (do not repeat)**: 0.1.0 re-issued the request every frame with EASE-IN blending — restarting the tween at its zero-speed start each frame, so the view never progressed (frozen). 0.1.2 (transform writes) and 0.1.3 (`vp_FPSCamera.SetAngle` state feeding) fought the game's own look pipeline and shook the view violently in-game. 0.1.5 fired each shot ~2 ms after issuing the glide, so the view had no time to swing. 0.4.3 chained full 0.55 s EaseInOut glides for tracking, which read as stop-and-go with hard velocity jumps at every shot. 0.4.4's fixed short 0.12 s pursuit reached the target in a burst then sat still. 0.4.5 re-issued a Linear pursuit every Tick — but because its struct still had `FrameTime == BlendIn`, every re-issue degenerated into an instant pin on the (per-tick recomputed) endpoint: measured in test video 3 as alternating move/freeze frames at ~30 Hz plus exponential-looking swings filtered through the game's camera smoothing. The struct fix (BlendIn = 0 + swapped slots) and the per-segment re-issue policy both landed in 0.4.6. 0.4.7 added the pre-advanced start slot (re-issues are dead-frame-free) and the angular-speed cap on the pursuit duration (0.05 s slams right after a shot, live-log confirmed). 0.4.8 moved the glide endpoint from the lead intercept to the target body (a spiked velocity sample flung the intercept and the camera chased it — swing-away after every shot, test video 5). 0.5.3 reintroduced a hard constant speed (150 deg/s) and made the shot wait for the view, which only traded the whip for a slower wait — and the inverted slot semantics from 0.4.6 were still in place, so every segment's first frame landed on the endpoint. 0.5.4 fixes the semantics (t 0 -> 1, Restore = start), derives the angular speed per phase from the fire rhythm, and drops the camera gate.

## 7. Time-scale ownership

`Time.timeScale` is a global that several mods write. The concrete conflict found in 0.5.4: **TimeAccelerator patches `InterfaceManager.Update` with a PREFIX that assigns `Time.timeScale = 1` on every frame** while its acceleration key is not held. Our per-Tick write therefore got stamped back to normal between ticks — the "bullet time stopped working" report — and a lerp that starts from the (stomped) engine value can never converge, because each frame restarts one delta-time step away from 1.

The fix is threefold and all three parts are needed:

1. **The scale is owned, not read back.** `TimeScaler` keeps `_value` (the authoritative scale) and `_target`; `Value` is what everything else uses — `RealToScaled`, the Animator compensation, `TickExiting`'s completion test. Reading `Time.timeScale` back is forbidden: another mod may have overwritten it mid-frame.
2. **Advance on unscaled time** from the manager Tick (`MoveTowards`), never a coroutine (MelonCoroutines stopped resuming in the field, see 0.5.3).
3. **Re-assert from a POSTFIX on `InterfaceManager.Update`** (`Patches.TimeScaleGuardPatch`). A postfix on that method is guaranteed to run after any prefix writer on the same method, so the owned value is the one left standing at the end of the frame. It writes `Time.timeScale` first and `GameManager.m_GlobalTimeScale` last (the game copies the global onto `Time.timeScale` in its own update) and is a no-op while nothing is owned.

The marking phase owns `TargetTimeScale`; the firing phase owns its own milder `ExecutionTimeScale` (default 0.8, lerped at the Marking→Firing transition); the sequence end lerps back to 1. `Owned` tracks whether this mod holds a non-normal scale; scene changes trigger a hard reset. Without the ownership flag we would stomp other mods' slow-motion.

The firing phase paces shots on UNSCALED time (`_nextShotUnscaled`) and drives the first-person `PlayerAnimation.m_Animator` at `speed = 1/value`. **The interval `Fire()` pushes onto `m_NextAllowedFireTime` is expressed in SCALED seconds**, so it must be compressed right after the shot (`gate = Time.time + interval x value`, where the interval is the vanilla real rate read back from the fresh gate) — otherwise every shot waits `interval / value` real seconds and the fire rate silently drops by the execution scale (the 0.5.5 report: 25 % slower at 0.8). The scaled gate is also neutralized (`= Time.time`) immediately before each `Fire()`, because our unscaled pacing is the authority. If a shot is still gated, a throttled diagnostic line (`shot gated: ...`) dumps every gate value plus all three scales (`ts`, `gts`, our `own`).

**The Animator compensation has to be re-applied AFTER the game's own write.** `PlayerAnimation.Update` assigns the first-person `m_Animator.speed` every frame, so a value set earlier in the tick is gone before the Animator is evaluated; `Patches.PlayerAnimSpeedPatch` (postfix on `PlayerAnimation.Update`) re-applies `1/scale` immediately after. The ceiling must cover the whole execution-scale range (`1/0.05 = 20`) — clamping lower silently slows the player. This mattered because `PlayerAnimation.IsAllowedToFire` is a pure "current Animator state hash == fire-ready state" test: with the animations ten times too slow at scale 0.1 the gate never reopened, so 3 marks produced 1 shot (0.5.6 live log: `animSpeed=4.00`, `allowedToFire=False` for the whole target window). The execution also SNAPS the scale (`TimeScaler.SetImmediate`) instead of lerping, so the compensation is right from the first frame. Residual limit: any game-side timer that uses scaled delta time directly is still slowed — the per-target window is 8 s for that reason.

## 8. Settings mapping

Every entry in `Settings.cs` maps 1:1 to the README settings table (both language blocks). Fixed scales: reveal 0.1 for 0.7 s, marking 0.01 (neither is user-tunable). Remaining defaults: execution time scale 0.8, enter lerp 1, exit lerp 3, fatigue 1 %/s, tint 0.22, sound volume 0.8. Choice labels for the four revolvers mirror the official in-game Chinese item names (the strings live in Settings.cs, which is allowed to be Chinese as in-game copy). Label order is fixed to the GearNames table — never reorder the Choice attributes without reordering GearNames. Keybind defaults `Mouse3` (side) / `Mouse2` (middle) via the ModSettings key-capture widget; rebindable to any key or mouse button.

## 9. Logging

All output goes through `ModLog` with the `[DEBUG]` prefix gated by the Verbose setting. Entry/exit, each mark, each shot (with aim error) and every forced exit are logged; the message doubles as the first triage step in a `DEBUG-*` write-up if needed.

## 10. Design constraints

| # | Constraint | If violated | Notes |
|---|---|---|---|
| 1 | Own the scale inside `TimeScaler` (`Value`), never read `Time.timeScale` back for decisions, and re-assert it from a POSTFIX on `InterfaceManager.Update` | A PREFIX writer on that very method (TimeAccelerator sets `Time.timeScale = 1` every frame) stamps the slowdown back to normal and a lerp starting from the stomped value never converges — the 0.5.3 "bullet time stopped working" report | TT-PauseOnRadial's core lesson + §7 |
| 2 | Only hard-restore the time scale while this mod owns it | Another mod's slowdown gets stomped | `TimeScaler.Owned` |
| 3 | Resolve accuracy inside the projectile: snapshot the lead-predicted intercept at `Fire()` time (TTL 0.5 s) and redirect the spawned projectile onto it — `vp_Bullet.Start` prefix (vanilla) or AdaptiveArsenal `ProjectileItem.Fire` PREFIX (detected at startup; a postfix sees velocity 0 because `AddForce` only lands on the next physics step) | Camera pose drift and spread bend the shot; moving targets are missed; with AdaptiveArsenal the vanilla `vp_Bullet` never spawns and the redirect is dead code | Velocity is frozen from a two-sample root-position pair; halved speeds + gravity drop when AdaptiveArsenal is present |
| 4 | Never write camera transforms or `SetAngle` directly; a look-at request must set `m_BlendInTime = 0` (else the ease phase is empty = instant pin), put the START rotation in `m_RestoreCameraOrientation` and the END rotation in `m_CameraTargetOrientation` (**t runs 0 -> 1; do NOT swap the slots**), be re-issued per SEGMENT and never per frame, pre-advance the start slot by one frame, cap the angular speed, and derive the segment duration from the fire rhythm per phase (mark body before the velocity is frozen, intercept after) | Direct writes/state writes fight the game's look pipeline (violent shaking); `FrameTime == BlendIn` degenerates every request into an instant pin (0.4.5 per-frame pin = ~30 Hz stutter); the swapped-slot reading from 0.4.6 made every segment's first frame land on the endpoint (0.5.3 camera teleports); ease-in per-frame restarts never progress (0.1.0); fixed-short pursuit bursts then dead-stops (0.4.4); a camera gate on firing throttled the whole sequence (0.5.3) | Verified semantics in §6 |
| 4 | Mark cap strictly equals `NumRoundsInClip()` at entry | Fire() dry-fires mid-sequence | Cap never recomputed mid-mode |
| 5 | Chain-fire only the bound revolver | A swapped weapon gets force-fired | `TickFiring` aborts on weapon-name mismatch |
| 6 | Every `Tick` ends in a state that can restore time, tint and marks | Residual slow-mo / tint after death, menu, scene change | Fool-proof branch + `HardReset` on scene load |
| 7 | Fatigue semantics: m_CurrentFatigue is tiredness (high = exhausted); the mode ADDS cost and ends at normalized 1 | Treating it as rest (drain = subtract, empty = 0) made the mode exit instantly on a rested player | Postfix adds `(FatiguePerSecond / 100) x m_MaxFatigue x Time.unscaledDeltaTime` clamped to max; spent check is `GetNormalizedFatigue() >= 0.995`; entry gate is the game's `IsExhausted()` |
| 8 | No managed-type fields across scene loads besides the driver | Stale `BaseAi`/`GearItem` references after a load | Marks cleared on every exit path |
| 11 | Register homing only for shots the sequence fires, keep ONE target per bullet, and rotate the velocity on scaled time | A shared target makes bullets still in the air curve into the next mark; unscaled steering makes the turn 10x too fast under the execution slowdown | `PendingHomingTarget` is consumed synchronously by the Fire prefix |
| 9 | Never treat `m_NextAllowedFireTime` as a real-time value under a slowdown — it lives on the SCALED clock, so re-compress it by the owned scale immediately after `Fire()` | Every shot arrives `interval / scale` real seconds late; the fire rate silently scales with the execution speed (0.8 -> 25 % slower) | Read the vanilla interval back from the fresh gate, then rewrite `gate = now + interval x value` |
| 10 | The Animator compensation `1/scale` must be re-applied from a POSTFIX on `PlayerAnimation.Update` (the game overwrites `Animator.speed` every frame), never clamped below the execution-scale range, and the scale must snap on entering the execution | The fire gate (`IsAllowedToFire` compares the current Animator state hash) stops reopening, targets time out and only the first mark gets shot (0.5.6: `animSpeed=4.00` at scale 0.1, 3 marks -> 1 shot) | Ceiling 20x covers 0.05-1; our own TickFiring compensation is only a backstop |

## 11. Comment migration map

| Original location | Original content | Now lives in |
|---|---|---|
| (initial implementation) | API verification notes from the 2.55 dump and AdaptiveArsenal cross-checks | §4 above |
| (initial implementation) | Fire()-only-consumes-ammo / direction-locks-at-FX-event finding | §3, §5 (0.1.1 correction: direction is consumed inline inside `Fire()`; the FX event carries no shot logic) |
| (0.1.1) | RequestLookAtTarget-based camera steering failed in-game (look controller overwrote the camera each frame; no shot ever fired) | §6 |
| (0.1.2-0.1.3) | Camera glide via transform writes and `SetAngle` state feeding (with sign auto-calibration) — all fought the game's look pipeline and shook the view violently in-game | §6 (removed on purpose) |
| (0.1.3 driver) | `vp_FPSCamera.SetAngle(yaw, pitch)` semantics: authoritative look state, `Angle` = (yaw, pitch), `LookAtTargetForTime.Update` → `set_Angle` | §4, §6 |
| (0.1.5) | `LookAtTargetForTime.Update` full semantics: slerp from caller-provided `m_RestoreCameraOrientation`, tween-in over `m_BlendInTime`, pin until `m_TimeRemaining`, no blend-out when restore flag is false | §4, §6 (0.4.6 CORRECTION: the slerp t runs 1 -> 0, the ease lasts `FrameTime - BlendIn`, the quaternion slots are swapped versus their names, and `FrameTime == BlendIn` means instant pin — see §6) |
| (0.2.0) | AdaptiveArsenal replaces the vanilla projectile pipeline (`Awake` prefix swaps `ProjectilePrefab`, `Fire` prefix spawns its own physics projectile) — the `vp_Bullet.Start` redirect never ran; glide issued ~2 ms before shot 1 so the view never swung onto the first target | §3, §5, §6, ChangeLog 0.2.0 |
| (0.2.0) | `PlayerManager.Update` gates the look-at only on `m_IsLookAtTargetForTimeActive` (0x540), no weapon state — glides valid with a gun raised; `LookAtTargetForTime.Update` ends euler → negate → `set_Angle` | §4, §6 |
| (0.2.1) | Postfix-on-AA-Fire read a zero velocity (`AddForce` lands on the next physics step) and silently skipped every shot; shot 1 fired ~20 ms after exit because the fire attempt preceded the first glide; per-shot glide wait throttled the rate to ~1.1 s/shot | §3, §5, ChangeLog 0.2.1 |

## 12. Compatibility

- Touches: `Time.timeScale` + `GameManager.m_GlobalTimeScale` (only while `Owned`), `Fatigue.m_CurrentFatigue` (flat drain while marking), freshly spawned projectile transforms/velocity (only for shots committed by this mod, TTL 0.5 s), the player look state via the game's own look-at system (only while firing).
- **AdaptiveArsenal**: detected at startup (reflection on `AdaptiveArsenal.Components.ProjectileItem`, no hard dependency). When present, Dead Eye binds a postfix onto its private `ProjectileItem.Fire` and switches ballistics to its effective speeds (table x 0.5 + gravity). When absent, the vanilla `vp_Bullet.Start` redirect is used.
- Conflicts possible with mods that also drive the global time scale (slow-motion, radial-menu slow-down) or redirect projectiles while the mode is active; nothing is written outside the mode.
- No item definitions, prefabs, save data or Wwise banks are modified.

## 13. Dependencies

- MelonLoader 0.7.3 (live install), HarmonyX via `HarmonyInstance.PatchAll` (explicit patch count logged).
- ModSettings 2.2.5 (settings page; hard dependency).
- STBlade.Modding.TLD.Il2CppAssemblies.Windows 2.55.0 (compile-time only).

## 14. Build and deploy

```
cd TLD_DeadEye
dotnet build -c Release
```

The csproj post-build copies `TLD_DeadEye.dll` to the live install's `Mods/` folder (path defined once in `TLD_DeadEye.csproj`) and to `Release/`, and copies `Asset/*.wav` to `Mods/DeadEye/Asset` / `Release/DeadEye/Asset` (see §15). Verify with the last lines of `MelonLoader/Latest.log`: the mod prints its version and the number of patched methods at startup.

## 15. Sound effects

- Two PCM16 WAV files ship in `DeadEye/Asset/` (`dead-eye-rdr2.wav` = activation, ~12.6 s; `dead-eye-mark.wav` = mark confirm, ~0.75 s). The `.mp3` files next to them are the editable sources — re-run ffmpeg (`-acodec pcm_s16le`) after editing an mp3.
- **Playback goes through `winmm!waveOut`, NOT Unity audio and NOT `PlaySound`.** Both Unity routes are dead in this IL2CPP build: `AudioClip.Create` + `SetData` returns an empty clip (length/frequency/channels all zero) and the `DownloadHandlerAudioClip` constructor is stripped (Method not found) — both proven in TLD_LanternBelt. `PlaySound` was the first workaround, but it has ONE channel per process, so the mark confirm cut the 12.6 s activation sound short. Every sound now opens its own `waveOutOpen` handle (WAVE_MAPPER), writes its whole PCM buffer through a heap-allocated `WAVEHDR` (the header must live in unmanaged memory — the driver keeps the pointer), and a short watcher thread polls `WHDR_DONE` before unpreparing/closing. Handles are independent, so sounds overlap; `ModAudio.Stop` only resets the activation handle it owns, so other mods' sounds are no longer purged either.
- Volume (the `SoundVolume` setting, default 0.8) is applied by scaling the samples **in memory** while copying (waveOut has no per-buffer gain). The old cached `*_v.wav` siblings are no longer written — existing ones in `Mods/DeadEye/Asset` are inert leftovers.
- Triggers: `PlayEnter` in `DeadEyeManager.TryEnter`, `PlayMark` in `TryMark` (only when a mark is actually added). `ModAudio.Stop` is called on every transition back to `Inactive` — forced exit, exit without marks, sequence finished — so the activation sound never outlives the mode. Files are located next to the dll under `DeadEye/Asset`, validated once (PCM16 RIFF only, decoded into memory once per file); a missing/invalid file logs one error and silences the sounds.
- The csproj post-build copies `Asset/*.wav` to `Mods/DeadEye/Asset` and `Release/DeadEye/Asset`; without that folder the mod logs `sound effects unavailable` and continues silently.

## 16. Maintenance rules

- Version numbers live in exactly four places: `BuildInfo.cs`, the README badge, the CHANGELOG first section, and the header line above. Never in code comments.
- Any constraint decided during debugging gets a row in §10, and the source comment keeps only one line plus a "see §10" pointer.
- Any injection-point change updates §3 in the same commit; any settings change updates §8 and both README language blocks.
- Run `check_readme.py` and `check_docs.py` after documentation work; both must report 0 errors before a release.
- Never commit without an in-game smoke test of enter / mark / exit / fire — the maintainer runs the game, not the CI.
