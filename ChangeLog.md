# ChangeLog

## 0.6.3

- **The visible "hard turn" was largely a jumping aim point, not the turn rate.** The bullet aimed at the mark's local-space body point, which sweeps sideways whenever the animal turns on the spot — the bullet then chased that jump. It now aims at the target ROOT plus a height (taken from the mark), so the point only translates when the animal actually moves.
- Corrections are split over many frames: each tick turns only 40 % of the remaining angle (still capped by the strength-derived turn rate), so the path reads as a smooth curve instead of a straight line with a kink.
- 追踪强度 range widened to **0.05-10** (was 0.5-2.5): at 0.05 the turn rate is ~20 deg/s, i.e. a 300 m/s bullet curves over a radius of hundreds of metres and the shot looks straight; 1 is the previous standard (400 deg/s).
- New verbose line `homing engaged: initial error X deg, speed Y m/s, strength Z` — if the initial error is large, the curve you see is the SPAWN direction being off, not the steering, and the fix belongs in the lead prediction.

## 0.6.2

- Back to PURE pursuit (aim at the animal's current body point) so the 追踪强度 setting alone decides hit rate versus look — no lead term confounding the comparison. The strength setting is kept and now scales the turn ceiling around a 400 deg/s base (0.5 -> 200 deg/s, 2.5 -> 1000 deg/s).
- Kept from 0.6.1: the 8 s bullet lifetime (a long shot under the execution slowdown no longer stops steering mid-flight) and the overall turn ceiling raise from the original 220 deg/s.

## 0.6.1

- Stronger, smarter tracking. The bullet used to aim straight at the animal's CURRENT body point, which makes a 300 m/s projectile overshoot and spiral on fast crossing targets — the remaining misses. It now uses LEAD pursuit: the mark's sampled velocity (per scaled second, smoothed, teleport-clamped) times the remaining flight time, so the round flies almost straight at where the animal will be and spends its turn budget on real corrections. Turn ceiling raised 220 -> 520 deg/s.
- New setting 追踪强度 (HomingStrength, default 1, range 0.5-2.5) scales the turn ceiling: higher pulls harder on fast sideways movement (more visible curve), lower flies straighter.
- Bullet lifetime 3 -> 8 real seconds: a long shot under the execution slowdown could otherwise stop steering mid-flight.

## 0.6.0

- **Homing bullets.** With AdaptiveArsenal installed, every projectile fired by the execution sequence now keeps steering toward its marked animal while in flight (new setting 子弹追踪, default on). The Fire prefix registers the projectile's Rigidbody together with that mark's GameObject and body offset; each tick the velocity direction is rotated toward the mark's CURRENT position (up to 220 deg/s, speed preserved), so the lead prediction is only the starting guess and a slightly-off lead still lands. Each bullet keeps its own target — an in-flight round never retargets when the sequence advances to the next mark. Turning runs on scaled time so the bullet behaves correctly under the execution slowdown.
- The vanilla `vp_Bullet` resolves its hit inside `Start()` and has no physics body to steer, so homing applies to the AdaptiveArsenal path only; without it the previous ballistic behavior is unchanged.

## 0.5.9

- **The mark chime no longer cuts the activation sound.** Both sounds used `winmm!PlaySound`, which has a single channel per process, so every mark stopped the 12.6 s activation sound. They now go through `winmm!waveOut` with one device handle per sound: sounds overlap freely, and `Stop()` only resets the activation handle it owns — other mods' sounds (e.g. TLD_LanternBelt's ignite click) are no longer purged either.
- Volume is now applied by scaling the decoded samples in memory, so the mod no longer writes `*_v.wav` copies next to the assets (existing ones can be deleted).

## 0.5.8

- The draw-reveal timing is no longer tunable: 拔枪秒流速 and 拔枪特写时长 are gone, fixed at **0.1x for 0.7 s** (constants `RevealTimeScale` / `RevealSeconds`). After the reveal the marking phase runs at the fixed 0.01x.
- Settings now only expose what is worth tuning per player: the bound revolver, two keys, execution speed, the two transition speeds, fatigue cost, tint, volume and the verbose flag.

## 0.5.7

- The fixed marking speed after the reveal is now **0.01** (was 0.025), so the remaining raise really does crawl through the whole marking phase.
- Internal: every place that divides by or converts through the owned scale used a `0.05` floor, which at 0.01 would have silently substituted 0.05 — stretching every glide and fire-gate conversion. The floor is now `0.001`, so the conversions stay correct at any marking speed. The execution phase is unaffected (it runs at its own scale, 0.1-1).

## 0.5.6

- **Fixed "3 marks, only 1 shot" during the execution.** Live log: `allowedToFire=False` for the full target window with `ts=0.10` and `animSpeed=4.00`. Two things were wrong. (1) `PlayerAnimation.Update` writes the Animator speed itself EVERY frame, so the compensation set from our tick was overwritten before the Animator was evaluated — the raise/fire animations really ran at `scale x 1`, i.e. ten times too slow at 0.1, and the fire gate (`IsAllowedToFire` is a pure "current animation state == fire-ready state" test) never reopened. (2) Even the value we did set was clamped to 4x. Both fixed: a postfix on `PlayerAnimation.Update` re-applies `1/scale` right after the game's own write, and the ceiling is now 20x so the whole 0.05-1 range is honoured exactly.
- Entering the execution now SNAPS the scale (`TimeScaler.SetImmediate`) instead of lerping: the compensation is `1/scale`, so a ramp leaves it wrong for the whole ramp duration.
- Target window widened 4 s -> 8 s and the sequence watchdog 20 s -> 30 s: the game's own weapon state machine advances on scaled time, so a very low execution scale can still need more real time than before even with the animations compensated.
- Settings simplified: 拔枪后流速 is gone — the marking speed after the reveal is fixed at **0.025**. The only scale the player tunes is 拔枪秒流速 (the first-second reveal), still 0.05-0.5.

## 0.5.5

- **Two-stage marking slowdown with a drawn-out reveal.** Entering the mode used to leave the revolver stowed until the execution; now it is drawn at activation. The marking speed (时间流速, 0.1x) only covers the REVEAL — one second, i.e. until the muzzle clears the bottom of the screen in the reference footage — and then drops to the new 拔枪后流速 (default 0.025, a quarter of 0.1), so the rest of the raise crawls through the whole marking phase and the slow draw stays visible instead of finishing early. 拔枪特写时长 (default 1 s) tunes the reveal. A player who is already holding the revolver skips the reveal and gets the deeper speed immediately.
- **Fixed the fire rate slowing down during the execution.** `Fire()` pushes the vanilla interval onto `m_NextAllowedFireTime`, which lives on the SCALED clock. Under a 0.8 execution scale the same number therefore stretched the real wait by 1/0.8 = 25 %, so every shot came 25 % late even though our own pacing ran on unscaled time. The gate is now compressed by the owned scale right after `Fire()` (`gate = now + interval x scale`), so the real interval equals the vanilla one at any execution speed. The first-person Animator compensation (`speed = 1/scale`) keeps the draw and fire animations at real speed.

## 0.5.4

- **Fixed the camera teleports and the dead bullet time — two separate root causes, both confirmed by disassembly / live logs.**
- Camera: the look-at slot semantics documented in 0.4.6 were inverted on BOTH halves. Re-disassembling `PlayerManager.LookAtTargetForTime.Update` shows the factor is `1 - (TimeRemaining - BlendIn) / (FrameTime - BlendIn)` and the update calls `Slerp(m_RestoreCameraOrientation, m_CameraTargetOrientation, t)`, so **t runs 0 -> 1 with `m_RestoreCameraOrientation` as the START and `m_CameraTargetOrientation` as the END** — the field names were right all along. Every release since 0.4.6 used the swapped slots, which made the first Update of every glide segment apply the ENDPOINT: the view snapped onto the target and then slid back. Requests are now written with the current rotation in the Restore slot and the target in the CameraTarget slot, and the opening swing uses EaseOut (a fast whip that decelerates) instead of EaseIn.
- Time scale: TimeAccelerator patches `InterfaceManager.Update` with a PREFIX that assigns `Time.timeScale = 1` on every frame while its key is not held, so the slowdown was stamped back to normal between ticks (live log: `fire sequence started ... timeScale 0.05`, then `ts=1.00 gts=1.00` half a second later). `TimeScaler` now owns the value (`Value`, never read back from the engine), advances the lerp on unscaled time, and re-asserts it from a **postfix on `InterfaceManager.Update`** — guaranteed to run after any prefix writer on that method. Everything that needs the scale (`RealToScaled`, the Animator compensation, the exit test) uses the owned value.
- Swing pacing: the fixed 150 deg/s sweep plus "the shot waits for the camera" is gone. Each camera phase now derives its OWN angular speed from the fire rhythm: segment duration = real seconds left until the weapon may fire again, floored by a 400 deg/s ceiling. Phase 1 (before the velocity pair is frozen) heads for the mark body; phase 2 (after) heads for the predicted intercept — different remaining angles, same time budget, so a big inter-target angle swings fast instead of snapping, and **the vanilla fire rate is preserved** because the camera is no longer a firing gate.
- Segments already within 0.5 deg of their target are no longer re-issued (0.5.3 issued 0.05 s stubs in a loop while the animation gate was closed, which read as a stutter).

## 0.5.3

- Fixed the between-shot camera whip (test video 6 + live log): the marks were spread up to 131 deg apart, and the time-to-go law compressed each inter-target swing into the 0.5 s fire interval — a 300 deg/s whip after EVERY shot (log: "pursuit glide issued (duration 0.44s real, drift 131.3 deg)"), which read as a teleport. The tracking law is now a CONSTANT swing speed (150 deg/s, clamped 0.05-2 s): the camera sweeps the remaining angle at one comfortable rate, and the SHOT WAITS FOR ARRIVAL (fires once the view is within 8 deg of its target point, on top of the vanilla weapon-rate pacing). Smoothness beats raw fire rate; hits never depended on the camera anyway.
- Fixed the execution/marking slow-mo silently dead (both sessions ran at ts=1.00 gts=1.00): the MelonCoroutines-based lerp stopped resuming in the field, so the scale never moved while every guard believed it was active. TimeScaler is now driven entirely from the Tick (MoveTowards lerp + per-frame re-assertion of both clocks, no coroutines).

## 0.5.2

- Reworked the lead prediction into a uniform-linear two-sample design (spike fix round 2): velocity is NO LONGER re-estimated every Tick. When a mark becomes the active target, the mod takes exactly TWO samples of the AI ROOT position 0.25 s apart (real time, started immediately so the window overlaps the swing), freezes the vector, and treats the animal as uniform linear motion from then on. Root position instead of the marked body offset, so an in-place body rotation cannot fake a lateral velocity.
- Camera retarget phases per mark: BEFORE the velocity pair is frozen the crosshair swings onto the mark itself; AFTER it is frozen the crosshair tracks the predicted intercept (frozen velocity + live position + fresh flight time, so it still moves smoothly). The switch happens exactly once per mark — the camera never chases a per-Tick re-estimated direction, which is what produced the teleport-like spikes.
- Shots wait for the frozen velocity (`_interceptReady`); the 0.25 s window always completes during the approach, so the pacing is unaffected.

## 0.5.1

- Fixed "the player is still slowed and the second shot never fires" (0.5.0 live log): at sequence start `Time.timeScale` had been restored to 1.00 by an external writer (the game or another mod) while our `GameManager.m_GlobalTimeScale` stayed at the marking value — the mismatch alone slowed the player's fire path (the player reads the global scale), so shots 2/3 timed out after 4 s each while shot pacing was already open. Both `Marking` and `Firing` now re-assert the owned scale EVERY frame (`TimeScaler.EnsureScale`), defeating any external stomp.
- New setting 处决时间流速 (ExecutionTimeScale, default 0.8): the firing phase owns a SEPARATE, milder time scale — entering the execution lerps from the marking value to it, the yellow tint persists, and the sequence end restores to 1. With both clocks equal the Animator compensation (`1/timeScale`) is correct regardless of which scale the player's rig consumes.
- Added a throttled `shot gated: ...` diagnostic that dumps every fire-gate value (reload cooldown, reloading, allowed-to-fire, scaled gate, both time scales, animator speed) whenever our pacing is open but the game refuses to fire — a repeat of this failure is now triageable from one log line.

## 0.5.0

- Reworked the toggle's end logic. With NO marks: pressing the toggle exits the mode immediately (unchanged). With marks: pressing the toggle now ends the marking phase and dives STRAIGHT into the firing phase — the yellow filter and the slowdown are no longer removed for the exit lerp; they persist until the whole sequence ends, then restore together.
- The slowdown now only affects the WORLD during firing; the player acts at real speed: the first-person `PlayerAnimation.m_Animator` is driven at `speed = 1/timeScale` (Unity's Animator.speed multiplies the scaled deltaTime, so this restores real-time animation exactly), and the fire pacing moved to UNSCALED time — the interval `Fire()` pushes onto `m_NextAllowedFireTime` is the vanilla real rate (vanilla always runs at scale 1), so it is used as-is, while the scaled gate itself is neutralized before each shot. The look-at glides now take REAL durations (struct fields get `duration x timeScale`), keeping the time-to-go pacing coherent under the slowdown.
- State flow simplified: `Marking --toggle+marks--> Firing --> Exiting (restore tail) --> Inactive`; `Exiting` is now the shared restore tail of both the no-marks exit and the end of a firing sequence.
- README fatigue default corrected (the 0.4.3 change to 1 %/s had not reached the bilingual usage text).

## 0.4.8

- Fixed the post-shot swing-away (test video 5: right after each shot the camera swung 40-70 degrees AWAY from the next target, then swung back and arrived just as the next shot fired — read as teleport / pull-back / re-swing).
- Root cause: the pursuit glide's endpoint was the LEAD INTERCEPT (`ComputeIntercept`), not the target. The intercept moves with the per-Tick velocity sample, and one teleport-sized sample (AI repositioning / root-rotation burst sweeping the marked body offset; the clamp allowed up to 400 m/s) flung the intercept dozens of meters off — live logs show the first pursuit glide of a sequence needing `duration 0.67s`, i.e. a remaining angle of ~161 degrees while the crosshair sat ON the target, and drift re-issues growing 14 -> 54 -> 116 -> 143 degrees within a single sequence. The camera was chasing a phantom.
- Fix: the camera now aims at the TARGET BODY point only; the lead intercept is exclusively the projectile redirect's business (hits never depended on the camera). The velocity clamp tightened from 400 to 30 m/s so one bad sample can no longer fling the shot's own intercept either. The re-issue pre-advance is additionally capped at 25% of the glide so a short duration can never pre-jump visibly.

## 0.4.7

- Killed the residual between-shot stutter (test video 4: one fully frozen frame roughly every 0.06–0.12 s during the firing swing, plus short slam-then-crawl bursts right after each shot).
- Live-log evidence: each shot triggered THREE pursuit re-issues — two with `duration 0.05s` (the fire gate is still near zero right after a shot, so time-to-go clamped to the floor) and then the main segment. Each re-issue burns one dead frame (the first game Update applies t=1 = the start slot = the current view, so nothing moves that frame).
- Two fixes in the pursuit glide: (1) the start slot is PRE-ADVANCED by one frame of motion (`Slerp(current, mark, dt/duration)`), so the first application after a re-issue lands exactly where the next frame should be — re-issues no longer cost a dead frame; (2) the duration is never below remaining-angle / 240 deg/s, so a near-zero fire gate can no longer slam the remaining angle into 0.05 s (burst-then-stall) — the view instead creeps onto the mark at a capped constant speed. Arriving late is safe: hits never depend on the camera (projectiles are redirected onto the committed intercept).

## 0.4.6

- Fixed the between-shot camera stutter (test video 3: alternating move/freeze frames at ~30 Hz, instant snaps, and swings that sped up and died instead of holding a rate).
- Root cause found by disassembling `PlayerManager.LookAtTargetForTime.Update` + `TweenValue`: the tween factor runs 1 -> 0 and the ease phase lasts exactly `FrameTime - BlendIn`. Every request so far used `FrameTime == BlendIn`, which empties that phase — each request degenerated into an instant pin on its endpoint. The per-Tick re-issue from 0.4.5 therefore pinned the camera onto the recomputed endpoint every single frame (filtered through the game's camera smoothing = the stutter), and the first 0.55 s "ease" glide was always an instant snap.
- The look-at requests now set `BlendInTime = 0` so the tween actually runs, with the two quaternion slots deliberately swapped (mark -> `m_RestoreCameraOrientation`, current rotation -> `m_CameraTargetOrientation`) to match the 1 -> 0 direction; expiry parks the view on the mark.
- The first swing is a true 0.55 s EaseIn glide (fast start, decelerates onto target 1 — the "quick whip" stays).
- The pursuit glide is now issued per segment instead of per Tick: a re-issue happens only when the active glide expires or the mark drifts more than 5 deg off its captured endpoint (at most every 0.12 s, since a re-issue costs one idle frame). Duration stays time-to-go paced, so the view holds a constant rate and arrives at each mark exactly when its shot is due.

## 0.4.5

- Fixed the remaining camera jolt during the firing sequence. The 0.4.4 pursuit used a fixed 0.12 s glide, so the view burst onto each mark within a few frames and then sat still for the rest of the fire interval — still stop-and-go, just faster.
- The tracking glide is now time-to-go paced: every tick re-issues a LINEAR glide whose duration equals the time left until the weapon may fire again (clamped 0.05–0.6 s). A per-frame restart advances dt/duration of the remaining angle, so the swing speed self-regulates to a constant and the view arrives at each mark exactly when its shot is due — one continuous swing paced to the fire rate.

## 0.4.4

- Smoothed the camera swing during the firing sequence. Before, the view played a full 0.55 s ease glide, sat still until the shot, then snapped toward the next mark — the velocity profile read as stop-and-go with hard jumps at every shot.
- Now only the FIRST swing is the one-shot 0.55 s ease glide; every later camera motion is a per-frame chained LINEAR pursuit glide (0.12 s) re-issued from the camera's current orientation toward the recomputed intercept. Linear has full speed at the tween start, so per-frame re-issuing never stalls (ease-in per-frame restarts are what froze the view back in 0.1.0) — the swing reads as one continuous "gun-swing" motion that gently slows onto each target.

## 0.4.3

- Renamed the four revolver choices in the mod settings to the official in-game Chinese item names: 左轮手枪 / 猎人的左轮手枪 / 护林人的左轮手枪 / 典狱长的左轮手枪 (order unchanged, saved selections stay valid).
- The default fatigue cost is now 1% of the bar per second (was 3%); the slider range is unchanged. Existing save files of the settings keep their previous value — set the slider to 1 manually if you want the new default.

## 0.4.2

- Fixed: the mode still exited instantly on entry (0.4.1). Root cause: `Fatigue.m_CurrentFatigue` is **tiredness** — higher means more exhausted (the game's own `Update` adds the per-hour burn via `AddFatigue`, and `IsExhausted()` is current > threshold); the HUD rest bar is its inverse. The previous fix subtracted cost and treated normalized 0 as "empty", which read a fully rested player as spent.
- The cost now adds `(FatiguePerSecond / 100) x m_MaxFatigue` per unscaled second (clamped to max), the mode ends when `GetNormalizedFatigue() >= 0.995`, and entering stays blocked while the game's `IsExhausted()` is true. Verified against disassembly of `Fatigue.Update` / `AddFatigue` / `GetNormalizedFatigue` / `IsExhausted`.

## 0.4.1

### Fixed

- Entering Dead Eye force-exited within a frame. Root cause: the internal `Fatigue.m_CurrentFatigue` scale follows `m_MaxFatigue` (the game's own `GetNormalizedFatigue()` divides by it) — it is NOT a fixed 0-100 range. The 0.4.0 drain subtracted absolute points (3/s), which on the normalized scale emptied the bar several times over per second, tripping the empty check instantly. The drain is now expressed relative to `m_MaxFatigue` (3% of the bar per second), and the empty check uses `GetNormalizedFatigue() <= 0.01` instead of an absolute value. Exit reasons in `TickMarking` are split into distinct log lines (gameplay lost / weapon stowed / fatigue empty with the normalized value) so the next misdiagnosis is a one-line grep.

## 0.4.0

### Fixed

- The FIRST activation after starting the game played no activation sound: the very first winmm sound a process issues initializes the audio device and that request gets swallowed. The mod now warms the audio up during normal gameplay (validates the files and plays a generated 0.1 s silent WAV once), so the first real activation is audible.

### Changed

- Fatigue cost reworked: instead of a multiple (3x) of the vanilla drain, Dead Eye now burns a configurable percentage of the full bar per real-time second (setting 疲劳每秒消耗, default 3%, ~33 s from full). The vanilla drain still applies underneath. A fully exhausted survivor cannot enter the mode (as before), and the mode still force-exits when the bar hits zero.

## 0.3.2

### Fixed

- The 12.6 s activation sound kept playing to the end after a quick exit from the mode. The mode now stops it the moment it ends — on a cancel (forced exit), on an exit with no marks, and when the firing sequence finishes.

## 0.3.1

### Fixed

- **Sounds were silent in-game**: the Unity audio route does not work in this IL2CPP build — `AudioClip.Create` + `SetData` produces an empty clip, and the `DownloadHandlerAudioClip` constructor is stripped (both proven in TLD_LanternBelt). Sounds now play through the Windows system audio interface (`winmm!PlaySound`), same as the lantern mod: Windows decodes the WAV, independent of the game's Wwise mix. Volume is applied by pre-scaling samples into a cached copy (PlaySound has no volume argument). Note: a new sound interrupts the previous one, so a mark confirm cuts off the activation sound.

### Changed

- Sound files now live in `TheLongDark/Mods/DeadEye/Asset` instead of `TheLongDark/Mods/Asset` (the old folder can be deleted).

## 0.3.0

### Added

- **Sound effects**: entering the mode plays the RDR2-style activation sound, and every successful mark plays a short confirmation sound. Two PCM16 WAV files ship in `Mods/Asset` and are loaded by a hand-written RIFF parser (Unity cannot decode MP3 from disk at runtime; the repo mp3s are the editable sources). Playback runs on Unity's DSP clock, so sounds play at normal speed even while bullet time is applied.
- New setting 音效音量 (sound volume, default 0.8, 0 = silent). The build now deploys `Asset/*.wav` next to the dll; a missing Asset folder only logs an error and disables sounds.

## 0.2.1

### Fixed

- Most sequences still fired shot 1 in place (only sometimes the crosshair swung onto the first target): the fire attempt ran BEFORE the first glide could be issued, so with the revolver already in hand the shot left ~20 ms after exit. The first shot now always waits for the first look-at glide to play out; the glide is issued unconditionally at sequence start.
- The AdaptiveArsenal aim redirect silently never engaged (zero log lines with the postfix bound): its `Fire()` launches via `AddForce`, which only lands on the NEXT physics step, so the postfix read a zero velocity and skipped. Replaced with a **prefix** that aims the projectile's transform before the body runs — AdaptiveArsenal's own launch force then flies exactly onto the mark and its random spread is bypassed. A one-time "prefix engaged" log line verifies the hook next session.
- Fire rate: shots were additionally throttled by the 0.55 s glide pacing (~1.1 s per shot). Only the FIRST shot waits for a glide now; every later shot fires at the weapon's own full vanilla rate while the look-at glides keep chaining as pure visual tracking (hits never depend on the camera — each projectile is redirected onto its committed intercept point).
- A per-target timeout that was checked after the fire gates could never trigger while the gates stayed closed, letting a stuck sequence limp until the watchdog; the window is now checked first.

## 0.2.0

### Fixed

- The first shot always missed and the crosshair appeared not to move: the shot was committed ~2 ms after the look-at request, before the glide had swung the view onto the target. The sequence is now **glide-then-fire**: each look-at glide (aimed at the predicted intercept point, not the current position) must play out (0.55 s) before its shot is committed, so the crosshair visibly swings onto every target — including the first — and the bullet leaves only when the view is already there. While the weapon is still being raised, fresh glides re-issue every 0.55 s to keep tracking a moving target.
- Shots did not reliably hit with AdaptiveArsenal installed. Root cause (from its source + live logs): AdaptiveArsenal swaps the shooter's `ProjectilePrefab` for its own physics projectile in `vp_FPSShooter.Awake` and spawns it from a `vp_FPSShooter.Fire` prefix — the vanilla `vp_Bullet` is never spawned, so the `vp_Bullet.Start` redirect (0.1.3/0.1.4) was dead code and never ran once (zero `bullet redirected` log lines across every test shot). Dead Eye now detects AdaptiveArsenal at startup (reflection, no hard dependency) and binds a redirect postfix onto its private `ProjectileItem.Fire`, which rebuilds the projectile's velocity vector exactly onto the committed aim point — removing its random hip-fire spread for Dead Eye shots. Without AdaptiveArsenal the vanilla `vp_Bullet.Start` redirect stays active.
- The last bullet of a sequence could not be redirected: its `Start()` runs a frame after the state machine has already returned to Inactive. The committed aim point now carries a short TTL (0.5 s) instead of being cleared at sequence end, and the redirect patches key off that TTL rather than the state machine.

### Changed

- Ballistics match AdaptiveArsenal when it is present: its projectiles fly at the table speed x 0.5 (its `ScaleMultiplier`, e.g. Fancy Revolver 300 m/s effective) under gravity, so the intercept prediction uses those effective speeds and adds a gravity-drop offset to the aim point.
- The last-shot log line no longer prints the muzzle speed; it prints the lead and the intercept offset.

## 0.1.5

### Added

- The view now glides onto each mark during the execution sequence through the game's own scripted look-at system (`PlayerManager.RequestLookAtTarget`): each request plays out once (slerp from the captured orientation to the target over 0.55 s, EaseInOut, no blend-out), and requests are chained back to back so a moving target stays tracked. The game handles all blending and yaw/pitch conventions itself — no transform writes, no state fighting, no shaking.
- Why earlier attempts failed (documented from disassembly): the look-at slerp starts from `m_RestoreCameraOrientation`, a struct field the caller provides. Re-issuing the request every frame (0.1.0) reset that start every frame, freezing the view; hand-driving `vp_FPSCamera.SetAngle` every frame (0.1.3) fought the game's own look pipeline and shook the view. One-shot requests with a captured start fix both.

### Unchanged

- Accuracy still comes entirely from the lead-predicted bullet redirect (0.1.4); the glide is purely visual.

## 0.1.4

### Fixed

- The view shook violently during the firing sequence: every camera-steering approach (transform writes, `SetAngle` state feeding with sign auto-calibration) fights the game's own look pipeline frame by frame. The camera is no longer touched at all — the view stays exactly where the player points it.
- Moving targets were hard to hit: shots now fire immediately (no convergence wait) and each bullet is redirected onto a **lead-predicted intercept point** — where the animal will be when the bullet arrives, computed from its tracked velocity and AdaptiveArsenal's muzzle-velocity table (Revolver 400 / Fancy 600 / Green 400 / StubNosed 300 m/s) with a fixed-point flight-time iteration, the same way AdaptiveArsenal reasons about ballistics.

### Removed

- `DeadEyeDriver` (the camera-glide MonoBehaviour) entirely; aiming is fully resolved inside the bullet, not the camera.

## 0.1.3

### Fixed

- The crosshair never moved to the marks by itself: the camera glide wrote the transform directly, which the game's own look pipeline overwrites every frame from its internal angle state. The driver now feeds that state itself via `vp_FPSCamera.SetAngle` (the same write path the game's scripted look-at uses), with a per-axis sign auto-calibration, so the view glides onto each mark without manual aiming.
- Marked shots could miss: vanilla applies a random accuracy spread to every spawned bullet. During the firing sequence the freshly spawned `vp_Bullet` is redirected straight onto the committed aim point and its residual spread is zeroed, so a marked head shot is always a head shot.

### Changed

- Each shot waits at most 1.2 s for the glide to converge, then fires regardless — the sequence can no longer stall if the glide is disturbed.

## 0.1.2

### Changed

- The camera now glides smoothly onto each mark (exponential slerp in the driver) instead of hard-snapping; the sequence fires as soon as the glide converges, and the view moves on to the next mark after every shot.
- The revolver is drawn the moment the exit key is pressed, so the raise animation overlaps the time-restore lerp instead of delaying the first shot.
- Any equip-related fire cooldown is stripped when the sequence starts; per-shot pacing stays on the weapon's own fire rate.

## 0.1.1

### Fixed

- The execution sequence never fired after leaving Dead Eye: the aim-lock relied on a cross-frame camera override (`RequestLookAtTarget`) that the game's own look controller overwrote every frame, so the aim error never converged and every mark timed out. The corrected camera rotation is now written and `Fire()` is called within the same frame — vanilla resolves the shot direction inline at call time (verified by full `Fire()` disassembly), so the bullet always follows the mark.
- The bound revolver was drawn when entering Dead Eye; it is now drawn only when the firing sequence starts after exiting (and only when marks exist).

### Removed

- The fire-FX animation-event harmony patch: `Fire()` consumes the round and commits the shot synchronously, so the sequence advances on the weapon's own fire-rate cooldown instead of waiting for an animation signal.

## 0.1.0

Initial release.

### Added

- Bullet-time marking mode: mouse side button toggles it while the bound revolver is carried; world time lerps to a configurable fraction and a yellow tint fades in.
- Marking: middle mouse button raycasts through the crosshair; each hit on a live animal drops a red mark that follows the animal. The mark cap equals the rounds left in the bound revolver's cylinder; repeated marks on the same animal are allowed.
- Fatigue cost: while marking, fatigue drains at a configurable multiple (default 3x) of the vanilla per-hour rate, scaling with the slowed time; the mode shuts itself off when fatigue runs out.
- Execution sequence: toggling out restores time, auto-draws the bound revolver if needed, then chain-fires one shot per mark in order. Each shot pre-corrects the camera so the vanilla bullet passes through the marked body point; vanilla damage, bleed-out, critical hits and skill progression apply unchanged.
- Bound-weapon selection in the settings: Revolver / Fancy Revolver / Green Revolver / Stub-Nosed Revolver.
- Rebindable keys for toggle and mark (any key or mouse button) via the ModSettings key-capture widget.
- Robust restoration: time scale, tint and marks are always cleaned up on exit, death, menus and scene changes.
- Verbose logging mode for mode transitions, marks and shots.
