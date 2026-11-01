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
| `Core/Mark.cs` | One mark: `BaseAi` + local-space offset so the mark follows the animal; per-tick velocity sampling for lead prediction |
| `Core/GunResolver.cs` | Finds the bound revolver (hands first, then backpack) and reads its clip |
| `Core/TimeScaler.cs` | Coroutine lerp of `Time.timeScale` + `GameManager.m_GlobalTimeScale` (TT-PauseOnRadial pattern) |
| `Core/Ballistics.cs` | Muzzle-velocity table (AdaptiveArsenal values) + fixed-point intercept prediction for moving targets |
| `Core/ModAudio.cs` | Loads the two WAV sound effects from `Mods/DeadEye/Asset` (hand-parsed RIFF/PCM16) and plays them through `winmm!PlaySound` |
| `Ui/Overlay.cs` | Yellow Canvas filter + OnGUI markers and message line |
| `Patches/Patches.cs` | `BulletStartPatch` (redirect spawned bullet onto the predicted intercept point, zero spread) and `FatiguePatch` (drain multiplier) |

Visual glide: no dedicated component — `DeadEyeManager.IssueLookAt` issues one-shot requests through the game's own `PlayerManager.RequestLookAtTarget` (see §6).

## 2. Per-frame pipeline

```
MelonMod.OnUpdate
  └─ DeadEyeManager.Tick          (state machine, input, guards)
       ├─ velocity sampling: every mark's animal velocity refreshed (Time.deltaTime based)
       ├─ Marking:  input → TryMark (raycast → BaseAi → Mark)
       ├─ Exiting:  draw revolver here (overlaps the time lerp) → lerp back to 1 → StartFiring
       └─ Firing:   chained one-shot look-at glides (game-native tween, 0.55 s each)
                    → per-target gates (cooldown / anim) → predict intercept
                    → snapshot LastShotAimPoint → vp_FPSShooter.Fire() → next mark

Patches
  ├─ vp_Bullet.Start prefix:   redirect the spawned bullet onto the predicted intercept, zero Accuracy
  └─ Fatigue.Update postfix:   extra drain while Marking
```

## 3. Injection points (2.55)

| Target | Kind | Why |
|---|---|---|
| `Fatigue.Update` | Postfix | Extra fatigue burn while marking; uses the game's own per-hour rate and scaled delta |
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
| `Fatigue.m_CurrentFatigue` / `m_CurrentFatigueBurnPerHour` | 0-100 (higher = more rested); current per-hour burn applied by the game each tick |
| `BaseAi.GetAiMode() != AiMode.Dead` | Alive check for marks; animal hitboxes live on layers 16 (direct) / 27 (parent) |
| Muzzle velocities (AdaptiveArsenal `ProjectileItem` table) | Revolver 400 / Fancy 600 / Green 400 / StubNosed 300 m/s nominal; AdaptiveArsenal launches at x 0.5 (its `ScaleMultiplier`) under gravity, so with it present the lead prediction uses the halved speeds and adds a gravity-drop offset to the aim point |
| `PlayerManager.RequestLookAtTarget(LookAtTargetForTime)` | Game-native look-at glide (story-mode cutscenes use it). Verified by disassembly: the struct is stored wholesale and its `Update()` slerps from `m_RestoreCameraOrientation` (caller-provided start) to `m_CameraTargetOrientation` over `m_BlendInTime` with the chosen `UITweener.Method`, ending in euler → negated → `vp_FPSCamera.set_Angle`. With `m_ShouldRestoreCameraOrientation=false` there is no blend-out — the view pins at the target until `m_TimeRemaining` expires. `PlayerManager.Update` invokes it with **no weapon-state gating** (only the `m_IsLookAtTargetForTimeActive` flag at 0x540), so glides work with a gun raised |
| `UITweener.Method` | Linear 0 / EaseIn 1 / EaseOut 2 / EaseInOut 3 (the glide uses EaseInOut) |

## 5. Firing sequence design

1. Exit slow-mo first (`Exiting` snaps time to 1), then run the sequence at normal speed. The revolver is drawn at the exit press (only if marks exist) so the raise animation overlaps the restore lerp.
2. `StartFiring` clamps `m_NextAllowedFireTime` down to now, stripping any equip-related cooldown; per-shot pacing afterwards is the weapon's own fire rate (`Fire()` pushes it forward on every call).
3. **Rhythm (0.2.1)**: a look-at glide toward the first mark's predicted intercept is issued unconditionally at sequence start, and the FIRST shot waits for it to play out (0.55 s) so the crosshair visibly swings onto the first target. Every later shot fires the moment the weapon's own fire gate allows — the full vanilla rate — while glides keep chaining as pure visual tracking (issued whenever none is running, aimed at the recomputed intercept). Hits never depend on the camera: each projectile is redirected onto its committed intercept point at spawn. (0.2.0's per-shot glide wait throttled the sequence to ~1.1 s/shot, and its fire-attempt-first order let shot 1 leave ~20 ms after exit with the gun already in hand — both fixed.)
4. Fire gates: `Time.time >= m_NextAllowedFireTime` AND not reload-cooling AND not reloading AND `IsAllowedToFire(m_GunItem.m_AllowHipFire)`. No camera-based gate. The per-target window (4 s) is checked BEFORE the gates so a stuck gate cannot stall the sequence.
5. **Lead-predicted projectile redirect**: when the gates pass, the intercept is recomputed at the fire moment — flight time `t = distance / effective launch speed` (AdaptiveArsenal table, halved + gravity-drop offset when AdaptiveArsenal is present; fixed-point iterated 3x), intercept = current mark position + animal velocity x t (velocity sampled every Tick from the animal's position delta over scaled `Time.deltaTime`). The intercept is snapshotted into `LastShotAimPoint` (TTL 0.5 s), `Fire()` is called, and whichever projectile spawned — vanilla `vp_Bullet` (Start prefix) or AdaptiveArsenal's `ProjectileItem` (Fire prefix) — is aimed exactly at that point with zero spread. Running targets are hit where they WILL be, regardless of camera pose or spread.
6. Watchdogs: 4 s per target (skips it), 20 s for the whole sequence (hard exit). Dead targets are skipped.

## 6. Camera control (game-native look-at glides)

- **Visual glide**: during the firing sequence `DeadEyeManager.IssueLookAt` requests a look-at toward the current mark's predicted intercept; the first glide is issued the moment the sequence starts, and while fire gates stay closed requests re-issue every `GlideSeconds` (0.55 s) so a moving target stays tracked. Each request is issued **once and plays out** — the game's own tween slerps from the captured start orientation to the target (EaseInOut over the full duration, no blend-out) and pins the view there; when the request expires the next one captures the current orientation as its new start, so the motion is continuous. All yaw/pitch conventions and blending are the game's own — nothing is fought, nothing shakes. After the sequence the last request expires within 0.55 s and normal mouse look resumes from wherever the view ended. (0.2.0 confirmed by disassembly: `PlayerManager.Update` runs the look-at with no weapon-state gating, so glides work with the revolver raised.)
- **History (do not repeat)**: 0.1.0 re-issued the request every frame — that resets the caller-provided slerp start (`m_RestoreCameraOrientation`) every frame, so the view never progressed. 0.1.2 (transform writes) and 0.1.3 (`vp_FPSCamera.SetAngle` state feeding with sign auto-calibration) fought the game's own look pipeline and shook the view violently in-game. 0.1.5 fired each shot ~2 ms after issuing the glide, so the view had no time to swing and the first shot always left along the exit orientation (live log + capture video evidence). The one-shot captured-start request plus glide-then-fire pacing is the variant that works with the system's grain.

## 7. Time-scale ownership

`TimeScaler.LerpTo` sets both `Time.timeScale` and `GameManager.m_GlobalTimeScale` in one coroutine (unscaled, so it survives the slowdown). `Owned` tracks whether this mod holds a non-normal scale; `Tick` force-restores it while `Inactive`, and scene changes trigger a hard reset. Without the ownership flag we would stomp other mods' slow-motion.

## 8. Settings mapping

Every entry in `Settings.cs` maps 1:1 to the README settings table (both language blocks). Defaults: time scale 0.1, enter lerp 1, exit lerp 3, fatigue x3, tint 0.22, sound volume 0.8. Keybind defaults `Mouse3` (side) / `Mouse2` (middle) via the ModSettings key-capture widget; rebindable to any key or mouse button.

## 9. Logging

All output goes through `ModLog` with the `[DEBUG]` prefix gated by the Verbose setting. Entry/exit, each mark, each shot (with aim error) and every forced exit are logged; the message doubles as the first triage step in a `DEBUG-*` write-up if needed.

## 10. Design constraints

| # | Constraint | If violated | Notes |
|---|---|---|---|
| 1 | Always set `Time.timeScale` and `GameManager.m_GlobalTimeScale` together | Parts of the game keep running at full speed or time stays slowed after exit | TT-PauseOnRadial's core lesson |
| 2 | Only hard-restore the time scale while this mod owns it | Another mod's slowdown gets stomped | `TimeScaler.Owned` |
| 3 | Resolve accuracy inside the projectile: snapshot the lead-predicted intercept at `Fire()` time (TTL 0.5 s) and redirect the spawned projectile onto it — `vp_Bullet.Start` prefix (vanilla) or AdaptiveArsenal `ProjectileItem.Fire` postfix (detected at startup) | Camera pose drift and spread bend the shot; moving targets are missed; with AdaptiveArsenal the vanilla `vp_Bullet` never spawns and the redirect is dead code | Velocity is sampled per Tick; halved speeds + gravity drop when AdaptiveArsenal is present |
| 4 | Issue look-at glides one-shot and let them play; never re-issue every frame and never write camera transforms or `SetAngle` directly | Re-issuing resets the slerp start each frame (view frozen, 0.1.0); direct writes/state writes fight the game's look pipeline (violent shaking, 0.1.2-0.1.3) | Slerp start is the caller-provided `m_RestoreCameraOrientation` |
| 4 | Mark cap strictly equals `NumRoundsInClip()` at entry | Fire() dry-fires mid-sequence | Cap never recomputed mid-mode |
| 5 | Chain-fire only the bound revolver | A swapped weapon gets force-fired | `TickFiring` aborts on weapon-name mismatch |
| 6 | Every `Tick` ends in a state that can restore time, tint and marks | Residual slow-mo / tint after death, menu, scene change | Fool-proof branch + `HardReset` on scene load |
| 7 | Fatigue drain uses the game's own `m_CurrentFatigueBurnPerHour` and scaled delta | Drain desyncs from the HUD's expectation | Postfix adds (multiplier - 1) x vanilla rate |
| 8 | No managed-type fields across scene loads besides the driver | Stale `BaseAi`/`GearItem` references after a load | Marks cleared on every exit path |

## 11. Comment migration map

| Original location | Original content | Now lives in |
|---|---|---|
| (initial implementation) | API verification notes from the 2.55 dump and AdaptiveArsenal cross-checks | §4 above |
| (initial implementation) | Fire()-only-consumes-ammo / direction-locks-at-FX-event finding | §3, §5 (0.1.1 correction: direction is consumed inline inside `Fire()`; the FX event carries no shot logic) |
| (0.1.1) | RequestLookAtTarget-based camera steering failed in-game (look controller overwrote the camera each frame; no shot ever fired) | §6 |
| (0.1.2-0.1.3) | Camera glide via transform writes and `SetAngle` state feeding (with sign auto-calibration) — all fought the game's look pipeline and shook the view violently in-game | §6 (removed on purpose) |
| (0.1.3 driver) | `vp_FPSCamera.SetAngle(yaw, pitch)` semantics: authoritative look state, `Angle` = (yaw, pitch), `LookAtTargetForTime.Update` → `set_Angle` | §4, §6 |
| (0.1.5) | `LookAtTargetForTime.Update` full semantics: slerp from caller-provided `m_RestoreCameraOrientation`, tween-in over `m_BlendInTime`, pin until `m_TimeRemaining`, no blend-out when restore flag is false | §4, §6 |
| (0.2.0) | AdaptiveArsenal replaces the vanilla projectile pipeline (`Awake` prefix swaps `ProjectilePrefab`, `Fire` prefix spawns its own physics projectile) — the `vp_Bullet.Start` redirect never ran; glide issued ~2 ms before shot 1 so the view never swung onto the first target | §3, §5, §6, ChangeLog 0.2.0 |
| (0.2.0) | `PlayerManager.Update` gates the look-at only on `m_IsLookAtTargetForTimeActive` (0x540), no weapon state — glides valid with a gun raised; `LookAtTargetForTime.Update` ends euler → negate → `set_Angle` | §4, §6 |
| (0.2.1) | Postfix-on-AA-Fire read a zero velocity (`AddForce` lands on the next physics step) and silently skipped every shot; shot 1 fired ~20 ms after exit because the fire attempt preceded the first glide; per-shot glide wait throttled the rate to ~1.1 s/shot | §3, §5, ChangeLog 0.2.1 |

## 12. Compatibility

- Touches: `Time.timeScale` + `GameManager.m_GlobalTimeScale` (only while `Owned`), `Fatigue.m_CurrentFatigue` (extra drain while marking), freshly spawned projectile transforms/velocity (only for shots committed by this mod, TTL 0.5 s), the player look state via the game's own look-at system (only while firing).
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
- **Playback goes through `winmm!PlaySound`, NOT Unity audio.** Both Unity routes are dead in this IL2CPP build: `AudioClip.Create` + `SetData` returns an empty clip (length/frequency/channels all zero) and the `DownloadHandlerAudioClip` constructor is stripped (Method not found) — both proven in TLD_LanternBelt, which this module follows. Windows decodes the WAV itself, independent of the game's Wwise mix; the one PlaySound channel is process-wide, so a new sound interrupts the previous one (a mark confirm cuts the activation sound).
- Volume (the `SoundVolume` setting, default 0.8) is applied by pre-scaling the samples into a cached `*_v.wav` sibling per source file, regenerated only when the volume changes — `PlaySound` has no volume argument.
- Triggers: `PlayEnter` in `DeadEyeManager.TryEnter`, `PlayMark` in `TryMark` (only when a mark is actually added). Files are located next to the dll under `DeadEye/Asset`, validated once (PCM16 RIFF only); a missing/invalid file logs one error and silences the sounds.
- The csproj post-build copies `Asset/*.wav` to `Mods/DeadEye/Asset` and `Release/DeadEye/Asset`; without that folder the mod logs `sound effects unavailable` and continues silently.

## 16. Maintenance rules

- Version numbers live in exactly four places: `BuildInfo.cs`, the README badge, the CHANGELOG first section, and the header line above. Never in code comments.
- Any constraint decided during debugging gets a row in §10, and the source comment keeps only one line plus a "see §10" pointer.
- Any injection-point change updates §3 in the same commit; any settings change updates §8 and both README language blocks.
- Run `check_readme.py` and `check_docs.py` after documentation work; both must report 0 errors before a release.
- Never commit without an in-game smoke test of enter / mark / exit / fire — the maintainer runs the game, not the CI.
