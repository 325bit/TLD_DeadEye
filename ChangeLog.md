# ChangeLog

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
