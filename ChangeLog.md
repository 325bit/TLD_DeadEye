# ChangeLog

## 0.7.25

- **Input lockout while the mode is up.** During marking and the execution the game no longer reacts to the left mouse button (fire), the right mouse button (aim) or the reload key R — firing or reloading mid-sequence fought the mod's own look-at glides and fire pacing. Blocked at the Hinterland input wrapper's concrete readers (`InputManager.GetKeyDown`, `GetMouseButtonDown`, `GetReloadPressed`, plus the `ProcessFireAction` entry as a second fence), so raw Unity input is untouched: the mode's own keys keep working, and movement, camera and every other binding are unaffected. All four prefixes are no-ops while the mode is off.
- 0.7.24 note: the first 0.7.24 build shipped with the loop fix uncompiled (version string still read 0.7.23 in the log — a build-order slip, caught and verified by string-grepping the dll). The loop itself needed `WHDR_BEGINLOOP | WHDR_ENDLOOP` set together; with only the begin flag the device ignored the loop count and played the buffer once. Both are in this build.

## 0.7.24

- **New sound: the marking loop.** Once the activation sting finishes during the marking phase, a short background loop (`dead-eye-rdr2-cycle.wav`) starts on its own waveOut handle and keeps playing until marking ends. It stops the moment the execution begins (including the auto-fire path), or the mode exits any other way — toggle with no marks, the cancel key, fatigue, weapon stowed, scene change. The activation sting itself still rings out into the execution as before.
- The repeat is device-side (`WHDR_LOOP` + `waveOutReset` to stop), so there is no gap between repeats and no extra watcher thread.
- The new wav is OPTIONAL: an updated dll without it keeps the sting and the mark chime, and one warning line notes the loop is disabled. Installation now ships three `.wav` files in `Mods/DeadEye/Asset`.

## 0.7.23

- **New setting: 标记满自动开火 (on by default).** The mark that reaches the cap — which equals the rounds left in the revolver's cylinder — starts the execution the moment it lands, so the last press of the marking flow is the last mark itself instead of one more toggle press. Switchable off in the settings page (new 处决 section); with it off the old behaviour is kept exactly.
- The auto-start goes through the same `StartFiring` path as the toggle key (same look-at glide, same pacing), and the log line distinguishes it (`mark cap reached, starting the execution automatically`). The cap-reached hint overlay stays as the fallback when the setting is off.

## 0.7.22

- **New key: 取消标记模式 (default Q).** While the marking phase is up, pressing it discards every mark placed so far and leaves Dead Eye immediately — no shots are fired. It is checked before the toggle key, so a cancel is never swallowed even on the same frame: the toggle with marks starts the execution, the cancel key aborts it outright. Rebindable in the settings page like the other two keys.
- The empty exit path (toggle with no marks) keeps its previous behaviour and on-screen line; a cancelled session shows 「死神之眼已取消」 and logs how many marks were discarded.

## 0.7.21

- **Footsteps are silenced while marking.** Entering the mode in mid-stride used to keep the step sounds going under the slow motion. The game has a flag for exactly this (`FootStepSounds.m_SupressFoostepAudio`, tested at the top of its footstep method and used by its own Audio_SuppressFootsteps action), so the mod flips it while the marking state is up and puts back the value it found when the phase ends — a cinematic that set the flag itself is not cleared.
- Silent by design, no setting: the mode is a slow-motion stare, and a walking player is the one thing that should not be heard during it. Footprints and water splashes are untouched — only the audio is suppressed.

## 0.7.20

- **Fixed: activating Dead Eye empty-handed left the revolver in the pack until the execution began.** The replayed equip events were timed on the scaled clock, and Dead Eye equips inside its own slow motion — 0.82 scaled seconds is 8 s at the 0.1x reveal speed and over a minute at the 0.01x marking speed, so the completion never arrived during marking. They now run on the real clock (0.06 s / 0.82 s wall time) and end early if the Animator has already left the swing state.
- Ordinary draws are unchanged in feel; only the timing source moved.
- This round is also the first **DEBUG** build of the mod, so the tuning sliders (弹道 / 子弹时间) are back in the settings page.

## 0.7.19

- **The draw is now replaced, not appended.** 0.7.18 played the vanilla raise first and the swing after it. The equip trigger the game fires is now cancelled again in the same frame (before the Animator evaluates it), and the reload-close swing is cross-faded in — the vanilla raise never appears.
- **The equip flow still completes.** The equip clip is also what raises the game's `ShowItem` and `Complete` animation events, so both are replayed by the mod at the offsets the swing reaches them (0.06 s and 0.82 s of animator time), on the SCALED clock so they keep pace under bullet time. `IsEquipped`, the callbacks and the item in hands therefore settle exactly as before.
- Only revolvers, only with 拔枪动作替换 on. `.25 s` duplicate-trigger guard, because the game calls the equip trigger twice per draw.
- If a future game update changes the state hash the cross-fade becomes a no-op: the vanilla raise then plays and the replayed events arrive early, which is survivable but visible — the tape prints the real hash on every reload.

## 0.7.18

- **The wanted motion is identified, and the swap is now built on it.** It is not a draw: the "pull the gun down out of frame, then swing it back up" flourish is the second half of **`FPH_Revolver_Reload_Close`** (state hash `-2113718774`, 1.63 s), recorded off a reload finish and confirmed frame by frame in the capture. The revolver has exactly one equip clip (`FPH_Revolver_Equip`), so nothing else could have been it.
- **How it is applied:** when the game signals the end of an equip (`OnAnimationEvent_Generic_Equip_Complete`), the mod cross-fades into that state at 0.5 normalized — the point where the gun has been pulled down — so the draw ends with the swing instead of settling into the idle pose. The cross-fade is 0.12 s; the equip flow itself is untouched, because the game's completion callback has already run.
- Setting-gated (拔枪动作替换, default on). Revolvers only. The `IsLoaded`/`immediate` overrides stay as harmless no-ops.
- If the game update ever changes the state hash, the cross-fade becomes a no-op and the log says so; the tape prints the real hash on every reload finish.

## 0.7.17

- **Diagnostics only.** The tape now runs continuously while 输出详细日志 is on, not only inside a draw session: the animation being hunted fires no equip trigger, so a recorder that only listens where it expects something will keep missing it. One line per state/clip change, with the draw session's tag and offset when one is active.
- The full reload sequence is now on record: `Reload_Open` (0.53 s) → `Reload_Loop` → `Reload_Close` (1.63 s) → `Idle`, with `RoundsLoaded` climbing while `IsLoaded` flips true on the first round. No `Unequip_Reload` and no second equip clip appear anywhere in it.
- Everything else (controller dump, trigger naming) unchanged.

## 0.7.16

- **Diagnostics only.** The controller inventory arrived: the first-person controller holds 517 clips, and the revolver set is `AimLoop, AimToIdle, Equip, Fire, HipFire, Idle, IdleToAim, Reload_Open, Reload_Loop, Reload_Close, Unequip, Unequip_Reload` plus movement/walk/runs. **There is exactly one revolver draw clip (`FPH_Revolver_Equip`) — the `Equip_Loaded` / `Equip_Unloaded` pair exists only for the flare gun.** So the two draws are not two clips.
- **The normal draw is cut short.** The equip timeline shows `FPH_Revolver_Equip` (2.70 s) entered at +270 ms and left again at normalized time **0.46** for `FPH_Revolver_Idle` — only the first half is ever shown, which is the "small raise".
- **Every trigger now opens its own timeline** (`Trigger_Generic_Reload*` gets 4.5 s): the raise that reportedly follows a reload fires no equip trigger, so it has to be captured from whatever trigger it does use.
- Hash naming, the per-draw state/clip timeline and the dump stay as in 0.7.15, all behind 输出详细日志.

## 0.7.15

- **Diagnostics only — the draw swap itself is unchanged and still does not work.** 0.7.14 proved that neither `immediate` nor `IsLoaded` selects the animation, so the next step is measuring instead of guessing.
- **The first-person controller is now dumped once per session**: every clip name it holds is printed (sorted, six per line). The controller is a binary asset, so this is the only way to see whether a second revolver draw clip exists at all.
- **Every animator write is named.** `PlayerAnimation.SetTrigger/SetBool/SetInt` are hooked, and the hash is translated through the game's own `m_AnimParameter_*` fields, so the log reads `trigger Trigger_Generic_Unstow` or `int RoundsLoaded = 6` instead of an integer. Triggers always log; bools/ints log only for the weapon parameters and at most every 200 ms.
- **Each equip/unstow now records a timeline** (every 80 ms for 2.4 s, one line per state/clip *change*): state hash, normalized time, clip names with weights, `IsLoaded`/`RoundsLoaded`/`IsEquipped`. This is what maps an action to the clip it plays — the question both failed attempts guessed at.
- All of it is behind 输出详细日志; with the setting off the probes cost one cached boolean test per write.

## 0.7.14

- **The draw swap was patched at the wrong half of the mechanism.** 0.7.13 only forced the `IsLoaded` animator bool, and a test session changed nothing: two different *triggers*, not two different bools, produce the two draws. `Trigger_Generic_Equip` ends with `immediate ? SetTrigger(Equip_Immediate) : SetTrigger(Equip)` — the `immediate` argument selects `Equip_Immediate`, the short raise from the bottom of the screen — and the ammo-derived `IsLoaded` only follows it. A prefix now forces `immediate = false` for revolvers, so every equip path takes the full draw; the `IsLoaded` force stays for an empty cylinder.
- **Both halves are one setting** (拔枪动作替换, on by default): the prefix for the trigger, the postfix for the bool, the completion event for the restore.
- **New diagnostic: `draw probe`.** With 输出详细日志 on, every equip and unstow trigger is sampled at +50/250/600/1200 ms and logs the Animator state hash, the playing clip names with their weights and the `IsLoaded`/`RoundsLoaded` values — the controller is a binary asset, so a wrong assumption has to surface in the log rather than as "it did nothing". The unstow trigger (the wide draw after a stow) is sampled too, as the reference the swap imitates.

## 0.7.13

- **New setting: 拔枪动作替换 (on by default).** Every revolver draw now plays the wide "loaded cylinder" animation — the one that sweeps in from the lower right — even when the cylinder is empty; vanilla plays a shorter raise from the bottom for an empty revolver. It covers all four revolvers and every way of drawing one, including the automatic draw when the mode starts, and it can be switched off in the settings page.
- **How it works.** The choice is not a C# branch between clips: `PlayerAnimation.Trigger_Generic_Equip` computes `IsLoaded = GunItem.NumRoundsInClip() > 0` and writes that Animator bool a few instructions before it fires the equip trigger, and the controller picks the clip from it. The mod overwrites that single value from a postfix (after the game's write, before the Animator evaluates the transition) and returns the real value at the draw's own completion event, with a 6 s fallback for an interrupted draw. `GunItem.Update` does not rewrite the parameter, so one write holds for the whole draw.
- Cosmetic by construction: the round count, firing, jamming and reloading are never touched; an empty revolver still clicks and still has to be reloaded. Only an empty cylinder is overridden — a loaded one already gets the wanted animation.
- The merge replaces the standalone `TLD_RevolverDraw.dll`; installing both is harmless but the two would each write the same value, so the setting would no longer be authoritative.

## 0.7.12

- **The tuning knobs are now a debug-build feature and no longer appear in a release build's settings page.** 追踪强度, 小体型瞄准点, 近失补偿半径, 处决时间流速, 处决射速倍率, 进入渐变速度 and 退出渐变速度 are declared `public` with `[Name]`/`[Slider]` inside `#if DEBUG` and plain `internal` otherwise. ModSettings enumerates public instance fields only, so a release dll simply has no control for them — it always plays the tuned values, and a `TLD_DeadEye.json` written by a debug build cannot override them (internal fields are not serialised either). The startup line now prints the configuration: `Dead Eye (死神之眼) 0.7.12 loaded (release build)`.
- **New tuned defaults, shipped in both builds.** Tracking strength 0.5, small-prey aim point 0.7, near-miss radius 0.5 m, execution time scale 0.2, execution fire-rate multiplier 1.5, enter lerp 5, exit lerp 8. Together they make the execution much slower on screen than 0.7.11 while firing faster than the vanilla pace.
- **The tracking-strength slider now spans what the code accepts.** It was 0.05-10 while `HomingSteering` clamped the value to 0.2-3, so roughly two thirds of the slider did nothing; the range is now 0.2-3 (the clamp is unchanged).
- **The launch-speed compensation reads the OWNED time scale.** `CompensatedLaunchSpeed` used `Time.timeScale`, which a prefix writer (TimeAccelerator) can stamp back to 1 mid-frame; that would have cancelled the compensation for that shot and sent the round off at the table speed. It now uses `TimeScaler.Value` like every other consumer (constraint #1). This matters far more at the new 0.2 execution scale, where the compensation is 5x.
- The seven settings are documented in the README under "Tuning (debug builds only)" with their shipped values, and `docs/DEVELOPMENT.md` records the split (§8, §10 #36, §14).

## 0.7.11

- **The bullets no longer crawl when the execution is slowed right down.** At 处决时间流速 0.1 the world runs ten times slower, so a 300 m/s round covers just 30 m/s of screen distance: a 60 m shot took **2.0 s** of real time to land (the 120 m shot measured 4.05 s). The launch speed is now multiplied by `1 / Time.timeScale`, so a round crosses the gap at a normal-looking pace.
- **Capped at what the hit detection can still follow.** The swept hit test samples the segment between two observed positions, and that segment is one physics step of world travel — so the speed is clamped at `SampleSpacing x (MaxPathSamples - 1) x 0.8 / fixedDeltaTime`. `MaxPathSamples` went from 48 to 128 (0.25 m x 127 = 32 m of coverage) which moves that ceiling to ~760 m/s, i.e. **2.5x** at the revolver's 300 m/s — a 60 m shot now takes 0.79 s instead of 2.0 s. Raising it further needs the sampling replaced by a single sphere sweep, or hits start being missed.
- **The lead prediction uses the same number.** `ComputeIntercept` now computes the compensated speed and stores it on the round, so the intercept, the gravity drop, the reported `flight` time and the actual projectile all agree — using the table speed while the bullet flew faster would have shortened every lead by the same ratio.
- The speed is applied on the round's FIRST live frame rather than inside `ProjectileItem.Fire`: AdaptiveArsenal commits its launch with `AddForce`, which the projectile only picks up a physics step later, so a Fire postfix reads zero velocity. Direction is preserved; the shot log now prints `bullet N m/s`.
- Requires 子弹追踪 to be on — the adjustment rides the homing entry, so with homing disabled the round keeps the table speed. The cap and the applied speed are logged (`launch speed capped at x2.54`).

## 0.7.10

- **New detector: a round that turns further than the steering commanded.** Each frame the velocity the game reports is compared with the vector this mod wrote one frame earlier, plus the gravity it must have picked up in between. Gravity is a known quantity and only turns a 299 m/s round at ~1.9 deg/s, so subtracting it lets the test sit at **0.5 deg per frame** — 15x above what gravity can do in a 17 ms frame, and 10-40x below a real steering correction — without ever flagging a falling round. Anything left over means something OTHER than steering moved the round: a bounce, a teleport, or a jumping aim point.
- The pass report gains a field: `steer residual 1.24 deg in 3 frame(s)` or `steer residual none`, and the first offending frame per round logs a line at Info (not behind Verbose — a diagnostic nobody can see is what made the marking regression invisible):
  `homing steer residual: 1.24 deg in one frame (73 deg/s) at 41 frames, 297 m/s — the round moved further than steering commanded (expected ..., got ...)`
- This is the first of the four sharp-turn detectors discussed; the other three (per-frame heading delta, gravity-subtracted path/chord ratio, curvature radius) are not implemented yet.
- Session results this was designed from (0.7.9, 17 shots): 15 hits (88 %), 14 of 16 passes within 0.42 m, one hit at **120.4 m** with a 0.16 m closest pass; marking worked (11 resolved on the body — 5 body-capsule centres, 6 head surfaces — plus 6 Ptarmigan marks that fell back to the raw hit point because no damageable collider existed on them at mark time).

## 0.7.9

- **Marking works again: 0.7.8 could not mark a single animal.** That build resolved the mark only on a "damageable, enabled, non-trigger" collider and dropped the click otherwise, so when the ray's hit resolved to the oversized proxy and the real capsules were not reachable that way, every candidate was silently skipped and the only thing the player saw was `准星处没有活物`. Measured session: 11 mode entries, 11 `exiting (no marks)`, 0 marks. The resolution failure is now **logged as a warning** and the mark **degrades to the raw hit point** (the pre-0.7.8 behaviour) instead of dropping the click — a marking defect can no longer lock the mode out.
- **The collider set is taken from the hit collider's transform ROOT** (only when that root holds exactly one animal, so a shared holder cannot mix a flock), falling back to the `BaseAi` object. A prefab that hangs its damage capsules outside the object carrying `BaseAi` was the most likely reason the new resolution found nothing.
- **The crosshair gate is body-scaled, not a bare metre.** The fixed 0.15 m equals **0.095 degrees at 88 m**, so it would have refused nearly every long shot on a target the player can see perfectly — the tolerance is now `max(0.30 m, 0.4 x the body's largest dimension)`, and the refusal message prints both numbers.
- The three failure messages are now distinct: the ray hit nothing (`准星处没有活物（射线没碰到动物）`), the crosshair is off the damageable body (distance + tolerance), and the resolution fell back to the raw hit point (log warning, mark still created).

## 0.7.8

- **A mark is now taken on the animal's damageable body, not wherever the physics ray happened to hit.** An animal's collision geometry is a species-sized capsule several times taller than the model — measured `bodyBounds` 1.00 x 1.50 x 1.00 for a grouse and 1.50 x 2.26 x 1.50 for a rabbit, against a 0.25-0.30 m body — so the raw hit point could sit a body-length above the bird. A screenshot showed the red X hanging roughly 0.35 m over a ptarmigan, and because the small-prey aim point is derived from that mark, the whole shot inherited the error. The mark is now resolved against the colliders that can take damage (`LocalizedDamage` on layer 16/27), so it lands on the body however big the invisible proxy is.
- **Grouse and rabbits mark the CENTRE of their body capsule.** Inside a 0.3 m animal there are no parts worth distinguishing, and the centre is what the depth slider was always approximating. The slider keeps its meaning but its upper reference is now that centre: 1.0 aims dead centre, lower values pull the round toward the feet. The mark log prints `aim N m off it`, which is the number to tune against.
- **A mark is refused when the crosshair is not actually on the body** — if the ray misses every damageable collider by more than 0.15 m, the click does nothing but show `准星不在活物的可伤害部位上（偏 N m）`. The oversized capsule used to accept a mark from anywhere inside it, including empty air beside or above the animal.
- Large animals keep the marked surface point (a head mark still hits the head), but now taken from a damageable collider, so the height clamp and the intercept work off a point that is really on the animal.
- Mark log format changed to report the resolution source and the real geometry:
  `marked WILDLIFE_Ptarmigan_2 [AiPtarmigan] small prey: capsule_body centre, 0.42 m from root, crosshair miss 0.00 m, aim 0.11 m off it (fraction 0.60), bodyBounds=...`

## 0.7.7

- **A round killed by a physical contact now reports where it actually died.** The pass report reads its last recorded sample, but the physics step that ends a flight is also the step that destroys the round — so the segment that reached the animal was never sampled, and every contact hit reported the distance it had one whole step earlier. In the last session that meant `closest 8.44-17.51 m` and `hitbox never crossed` on all 7 contact hits, while the same rounds logged `contact resolved on capsule_body ... 31.0 m from the muzzle, damage 598 applied`: the numbers contradicted each other and made a real hit indistinguishable from a 15 m miss.
- The contact point is now written into the round's bookkeeping before teardown (position, body point, miss vector, distance, speed, time), and a contact with a collider belonging to the tracked animal is recorded as the hitbox crossing it is. A contact with scenery or with a *different* animal still reports the true distance the round ended at, and still does not claim a crossing.
- Only the path-test resolutions were trustworthy before this (`closest 0.10 / 0.26 m` in that session); after it both resolution paths describe the same thing.

## 0.7.6

- **The execution hold ends when the last round can no longer achieve anything, not when its lifetime runs out.** The 0.7.5 session held bullet time for **5.6 s after the final hit**: one round of a five-round volley flew past the ptarmigan, reported `passed`, and then kept being steered at it until its own 8 s no-contact expiry — the hold waited on the whole 8 s. A round that has already flown past its animal can never be turned around into it in a useful time, so it now leaves the count (and steering) the moment it passes.
- **Rounds aimed at an animal that is already dead stop counting too.** In the same session the killing shot landed at 21.420 and three rounds still in the air continued into the corpse for another 1.3 s of bullet time, each applying 595 damage to something that was already down. A round whose target is dead is dropped from the count on the next tick.
- **"Resolved on hit" no longer covers scenery.** 3 of the 16 "hits" in that session were `contact resolved on MountainTown_main_terrain ... no damage (collider not damageable)` — rounds that missed the animal and hit the ground — and they were counted and labelled as hits in the pass report. Contacts with a non-damageable collider now log `homing round ended on scenery` and report `ended on scenery`; the path test's equivalent case reports `ended on undamageable body`.
- The hitbox description now prefers the **damageable** collider when a round crosses several: the first one hit is usually the animal's `Audio_Wildlife*` trigger (layer 22, no `LocalizedDamage`), which made every crossing read as a harmless volume.
- Times in the pass report are labelled `ms real` — they are real seconds, which under a 0.2 execution scale is several times the game-time flight the shot line prints.
- Session results (00:38-00:48, 19 sequences, 55 shots): with homing **on** 16 of 17 rounds resolved as hits and 13 of those applied damage (76 %); with homing **off** 38 shots produced 14 animal contacts (37 %). The hold fired in all 7 homing sequences and lasted 0.67-1.91 s except the 6.88 s case above.

## 0.7.5

- **The execution's slow motion now holds until the LAST bullet is consumed.** Previously the time scale started restoring the moment the last shot fired — the round was still in the air while the world snapped back. The firing state (execution scale, yellow tint, in-flight steering) is now held after the sequence fires until HomingSteering reports no live rounds: a round leaves the count when it is consumed by the marked animal, by scenery, or by its own 8 s no-contact expiry, and each of those ends the hold. Without live rounds (vanilla bullet path, homing off) the sequence ends immediately as before; the hold is capped one second past the rounds' own expiry, is reset by every exit path (force exit, scene change, new sequence), and the on-screen message shows 死神之眼：最后一发 while waiting.

## 0.7.4

- **Rapid fire now actually fires fast: the vanilla post-shot cooldown is overridden while the multiplier is above 1.** The 0.7.3 session measured a 0.52 s median gap at rate 4 even though the other three pacing members were compressed to 0.12 s — the binder was the fourth member nobody had counted: `vp_FPSShooter.IsReloadCooldownActive`, the game's own ~0.5 s post-shot cooldown, which held `pacingOpen` shut for the whole inter-shot window (the "shot gated" diagnostic never fired because pacing itself never opened). At rate > 1 our own unscaled pacing is authoritative and the cooldown is skipped; rate 1 keeps it, restoring the vanilla pace exactly. The reload-in-progress safety is separate (`animOk`) and unchanged.
- The camera park tolerance widens with the multiplier (`ArrivedDeg` x rate): under rapid fire the camera stops issuing 0.03 s micro-glide corrections out to 2 deg instead of 0.5 deg. The shot is committed from the intercept, not the view — this only quiets the tracking churn.
- 近失补偿半径 slider range widened to 0-0.5 for testing extreme values; the description now says where the honest range ends (~0.3).

## 0.7.3

- **RDR2-style rapid fire: new setting 处决射速倍率 (1-4, default 2).** The multiplier is applied to every place the shot pacing lives, and all three must shrink together or whichever is left slowest becomes the actual pace: the vanilla fire-gate compression (`m_NextAllowedFireTime`), the first-person animator compensation (the fire animation itself, via `PlayerAnimSpeedPatch` — at execution scale 0.1 x rate 4 the wanted 40 is capped at the 20x ceiling, trading part of the multiplier for the slowdown compensation), and the manager's own unscaled per-shot wait. 1 = the previous real-time pace; 2 = roughly 0.25 s per shot. Affects the execution sequence only — normal shooting and reloading are untouched.

## 0.7.2

- **The path test sweeps a tube, not a line.** The 0.7.1 session's misses included rounds that grazed an animal by centimetres and died on the ground behind it (one rabbit passed through the audio volume and landed 0.62 m from the body point). The swept segment is now walked at 0.25 m intervals and every sample is distance-tested against each cached collider (`Collider.ClosestPoint`) — a pass within the new `NearMissRadius` setting (0-0.15 m, default 0.1) resolves as a hit through the same replicated damage pipeline, with the hit point on the collider surface feeding the distance falloff. A cheap bounds pre-filter keeps the per-frame cost negligible, and samples inside a collider still count as exact crossings for the report. The contact path and everything else are unchanged.
- New setting 近失补偿半径 (NearMissRadius, default 0.1): how close a pass counts as a hit. 0 restores exact-contact-only.

## 0.7.1

- **The round's collision-detection mode is left exactly as the asset ships it.** 0.6.14 upgraded flying rounds to `ContinuousSpeculative`, and the 0.7.0 hardware session caught the consequence on camera: all six rounds died 9.5-9.8 m from the muzzle at full speed (v 298, 1-2 frames), in six different directions — speculative detection inflates the collider by velocity x step (~6 m per step at 299 m/s), so a shot downhill from a height generates phantom contacts with the sloping ground below the flight path and the solver slams the 0.02 kg round there. The same vantage on the asset's shipped `Continuous` (true linear sweep, no inflation) flew past that line and reached the animals. The upgrade also never delivered its intended benefit — the thin animated capsules it was meant to catch are exactly the pairs speculative misses (0.02 m passes with zero contacts). Hits are resolved by the path test instead (0.7.0), which does not depend on collision detection at all, so the CCD mode now only affects where the round physically dies — and the shipped mode is the one measured to let it fly.

## 0.7.0

- **Dead Eye rounds resolve their own hits — physics contacts are no longer trusted.** The probe session settled it: a round measured 0.02 m from a ptarmigan's neck by the segment raycast produced ZERO physics contacts across its whole flight (an animal's animated colliders do not feed `OnCollisionEnter`), while the kills that did happen came from the round parking in the flock after a terrain hit and re-firing AdaptiveArsenal's `TryInflictDamage` on every contact the flapping birds made — 19 x 400 damage from one stationary round. Both defects are fixed by the same takeover: when the path test crosses a damageable collider (layer 16/27 + `LocalizedDamage`) or the collision probe receives a physical contact, the damage is applied by Dead Eye itself — AdaptiveArsenal's numbers verbatim (400→200 over 5 km, `LocalizedDamage` scale and bleed-out, the critical-hit roll, shot stat and skill point, `SetupDamageForAnim` + `ApplyDamage`), its impact puff / decal / audio replayed at the resolved point, and the round torn down immediately. AdaptiveArsenal's handler is skipped for Dead Eye rounds only; manual shots keep its behaviour unchanged. A hit now logs `homing hit: path test resolved on ...` or `homing hit: contact resolved on ...` with the damage applied, and the pass report closes with `resolved on hit` instead of an anonymous miss reason.
- The probe's material tag now comes from the game's own `Utils.GetMaterialTagForObjectAtPosition`. The previous reflection guessed an AdaptiveArsenal namespace that does not exist ("Could not find type named AdaptiveArsenal.Utilities.Utils") and fell back to `unknown` on every line.

## 0.6.15

- **Every destroyed round now names the collider that destroyed it** (`projectile probe:` line). The 2026-11-02 session showed the dominant miss mode is not aiming at all: 25 of 27 rounds ended as `round gone, hitbox never crossed` 1-11 frames after launch (10-55 m travelled, 3-90 m short of the mark), and AdaptiveArsenal destroys its projectile on ANY solid contact while damaging only layer 16/27 + `LocalizedDamage` — so something solid between muzzle and target was eating rounds invisibly. All previous diagnostics raycast only the marked target's own colliders, which cannot see terrain, player-placed objects, or the animals' aux colliders (rabbits carry a solid, non-trigger collider union of 1.50x2.27x1.50 centred 1.21 m above the root — nothing like the rabbit's body — and the one round that reached its rabbit died 0.64 m out right after crossing the animal's audio volume). The probe logs the collider identity (same description as the hitbox report), the impact material tag via AdaptiveArsenal's own helper, the distance from the player and the impact speed, for manual shots as well, so the Dead Eye rounds have a baseline.

## 0.6.14

- **The pass report now names the collider the round flew through**, with its GameObject, layer, trigger flag, whether it carries `BaseAi` and `LocalizedDamage`, and its size. AdaptiveArsenal only damages a victim whose GameObject is on layer 16 (with `BaseAi` on it) or 27 (with `BaseAi` on a parent) *and* that carries `LocalizedDamage` — every other collider on the animal is a harmless volume. So "the round crossed the hitbox and nothing died" had two candidate explanations and only this identifies which one applies.
- **Rounds are upgraded to `ContinuousSpeculative` once they are actually flying.** The asset ships `Continuous`, which sweeps against STATIC colliders only, while an animal rides a kinematic actor — so the round crossed it inside one ~10 m physics step and produced no contact. `Register` cannot do it: it runs before `ProjectileItem.Fire` calls `Utils.SetIsKinematic(rb, false)`, and continuous detection is silently ignored on a kinematic body.
- **A destroyed round's last segment is now extended by a full physics step**, not 1.5 render frames: the transform only advances once per physics step, so the impact lies up to ~10 m past the last observed position, and 18 of 25 kills previously reported `hitbox never crossed` at a median 5.2 m simply because the segment that hit was never tested.
- The report also prints the round's live `collisionDetectionMode` and kinematic flag, so a mode that failed to apply is visible instead of assumed.
- Results from the 0.6.13 session: 25/57 hits (44 %, rabbit 68 %, ptarmigan 29 %), 30 rounds crossed the animal's collider and only 7 of those killed. The most repeated pattern was a ptarmigan with six consecutive `closest 0.03-0.31 m` passes and no kill.

## 0.6.13

- **Shots that pass through the animal are no longer a matter of guesswork.** Each round now raycasts the segment it swept since the previous frame against the animal's OWN colliders (cached once when the round is registered) and says so in its report: `hitbox crossed at 167 ms (frame 12)` or `hitbox never crossed`. Every earlier distance number was measured against a point this mod invents — the aim point — so "0.2 m away" could not be told apart from "outside the bird"; a crossing with no kill is a tunnel, a non-crossing is an aim error, and the two need opposite fixes.
- **The pass report no longer fires on the first frame of a flight.** "The round has passed" was decided by "the distance grew by more than 5 cm", but the round's transform only advances once per physics step while the target's body point wobbles with its own motion — so half the flights were declared over while the round was still 24-78 m short, freezing `closest` at a number that describes nothing. It is now the geometric test `Dot(body point - round position, round velocity) < 0`.
- **Fast rounds get continuous collision detection.** One physics step covers ~10 m at 299 m/s and a grouse hitbox is about one metre across, so a `Discrete` projectile crosses the entire animal between two collision checks. Every registered round is switched to `ContinuousDynamic` when the asset ships `Discrete` (never on a kinematic body); the mode found is printed in the `homing projectile registered` debug line.
- What the previous session's data already showed, and what these three changes exist to finish off: 6 consecutive rounds at one ptarmigan logged `closest 0.07-0.29 m` with the round and the body point within 0.1 m of each other, and none of them registered a hit. The bird's collider is 1.00 x 1.51 x 1.00 m, so a 0.29 m pass is inside it — the rounds were going through. Overall hit rate that session was 17/48 (35 %), unchanged from 34 %.

## 0.6.12

- **The closest-pass line now reports a usable number.** 0.6.11's value was the distance the round had one whole frame BEFORE it disappeared — at 300 m/s that is 5-15 m, so confirmed hits reported 3-17 m "misses" and the metric could not separate a hit from a near miss. When a round vanishes, its last segment is now extended along the last velocity by a frame of travel and the closest approach is taken on that line, which is the segment that actually crossed the animal.
- **The line also prints the raw geometry**: `round (x,y,z)` and `body (x,y,z)` at the closest pass, plus the per-frame distance trace (`trace 84.2 74.1 ... 6.9`). Together they tell a real miss from a misread: a projectile that is recycled or teleported jumps the trace in one step and lands somewhere unrelated to its flight path.
- **A vanished animal is no longer filed under "round gone"**: `target gone` is reported separately, so a despawn cannot be mistaken for an impact. A round that is still alive but has been parked back in a projectile pool (its transform no longer follows the flight) is dropped and reported as `round parked` — that state passes every nil check and is the leading explanation for the 12 long-range rounds whose distance started growing after 3-5 frames.
- Findings from the 0.6.11 session this replaces: round counts are right (48 rounds for 47 shots), every round is steered (`homing aligned` 47/47, muzzle error ≤ 0.22 deg), and the hit rate is unchanged at 34 % (rabbit 32 %, ptarmigan 38 %) on 47 shots. Two things did NOT work as intended: hits reported their pre-impact distance as their "closest pass", and every long-range round stopped being sampled after 3-5 frames with its distance already growing — the trace added here exists to say whether that is the projectile being recycled, the animal being despawned, or a genuine divergence.

## 0.6.11

- **New diagnostic line: where the round actually passed the animal.**
  `homing pass (WILDLIFE_Ptarmigan(Clone), small prey, passed): closest 0.34 m at 96 ms, miss ahead +0.31 right -0.05 up +0.18, aspect 12 deg, 7 frames, v 299 -> 296 m/s, target 10.4 m/s`
  The distance is measured to the animal's body point WITHOUT the lead, against the whole segment the round swept since the previous frame — at 300 m/s one frame covers several animal lengths, so sampling only the frame endpoints would call every shot metres wide. The miss is split into the animal's own frame (`ahead` negative = the lead was short), and the aspect angle says how wide its silhouette was (0 = running straight away, 90 = broadside, 180 = head on). One round of testing then separates the remaining causes: a pass INSIDE the 0.2 m body that is still not a hit means the round tunnelled through; a systematically short `ahead` means the launch speed is below the table value; a stray `up` means height or gravity. Diagnostics only — no aim or steering reads it.
- **Fixed the camera drift value that filled the log with `3.4e38deg`.** "This glide segment expired" was encoded as `float.MaxValue` in the very number the log printed, and expiring is the NORMAL reason for a re-issue — so the field was unreadable and the real drift between endpoint and target was never shown. The angle is now always computed and the line says which of the two triggered it (`expired` / `drift 5.3deg`). No behaviour change: an expired segment was always re-issued, and a healthy one is still only re-issued past 5 deg.

## 0.6.10

- **The lead is now built from a velocity read at the fire moment, not 0.3-0.5 s earlier.** The speed was sampled once, while the camera was still swinging onto the animal — but the shot itself happens 0.37-0.50 s later (median 0.45 s), and a running grouse covers 3.7-6.8 m in that time, more than its whole body. Every shot was therefore aimed at a point the animal had already left, and the error grew with speed: 68 % hits at 0-2 m/s, 27 % at 2-6, 23 % at 6-10, 19 % above. The sample is now re-read once per committed shot and feeds both the intercept and the in-flight steering. The camera keeps the older sample on purpose, so the swing into the target does not chase a value that changes every frame.
- **In-flight steering re-reads the target's velocity every frame.** Flight lasts 60-320 ms, and prey that changes direction inside it made the fire-time vector point the wrong way for the whole remaining flight. The fire-time sample is kept only as a fallback.
- **A zero velocity read is no longer taken at face value.** `BaseAi.GetVelocity()` is observed returning 0 for animals whose root is visibly running at 10 m/s; a zero (or a failed read) is now cross-checked against the root motion over the same window, and only a root that also stayed put counts as a standstill.
- New diagnostic line so the next log answers the open question directly:
  `v at fire (target 2, WILDLIFE_Ptarmigan(Clone)): frozen 12.4 m/s [ai] -> now 9.1 m/s [ai], angle 41 deg, 0.45 real s stale`
  A large angle means the animal turned while the crosshair was on its way (no straight-line lead could have been right); a small angle with misses means the model is fine and the residual is elsewhere.

## 0.6.9

- **A turning bird can no longer drag the aim point off itself.** The mark was stored in the animal's LOCAL space, so every turn in place swept the marked point across the body: on a grouse a quarter turn moves a flank mark by more than the animal is wide, and the shot, the camera and the in-flight steering all followed that sweep. The offset is now a world-space root-to-mark vector frozen at mark time — the point follows the animal's translation, not its rotation. This is what the logged misses were made of: on shots that hit, the aim point drifted +0.003 m between marking and firing; on shots that missed it drifted +0.065 m, up to 0.63 m.
- **The lower-only height clamp replaces the 0.28 m ceiling.** The vertical part of the aim point was clamped into a 0.06-0.28 m band above the root, but a grouse root pivot sits a good half-metre BELOW its body, so the ceiling pushed the point under the animal — the shot flew below a mark the player had lined up correctly. Only the floor is kept (a mark on the feet must not aim at the ground); the marked point is itself on the body, so 0 to 1 of the way up can never overshoot it.
- The 小体型瞄准点 slider keeps its meaning (0 = root, 1 = the marked surface) and is still read live, so both changes apply to an existing mark and to rounds already in the air.

## 0.6.8

- **In-flight steering no longer pulls the round back in time.** The fire direction carried the lead (animal velocity x flight time), but the steering target was the animal's CURRENT body point — so the instant the round left the barrel it started being turned away from the intercept and back toward where the animal had been. Pure pursuit does converge, but a running grouse allows only 4-7 frames, leaving 5-13 % of the error, pointing the wrong way. Steering now leads its own target identically, so it only corrects what is genuinely left.
- **The muzzle error is cancelled on the first frame instead of being chased.** The aim was computed from the PRE-spawn transform; AdaptiveArsenal then moves the round to the muzzle and launches it, which is worth 0.3-0.4 deg at 20 m — the full width of a ptarmigan. The first frame that sees a real velocity now re-aims from the ACTUAL spawn position, so that offset never enters the flight. Logged as `homing aligned: muzzle error 0.37deg cancelled at 300 m/s`, which is also the number that used to be lost.
- **Small-prey detection now actually runs.** `GetComponent<BaseAi>()` hands back an interop wrapper of exactly the requested type, so `is AiRabbit` was false for every animal and that branch was dead code — the log said `[BaseAi]` and identification only worked because the prefab names happen to contain `Rabbit` / `Ptarmigan`. The test uses `TryCast`, which asks the runtime class, and the mark log prints the real species name.
- Note: the two remaining issues from the same log analysis — the cougar `MaybeSetThreatLevel` null-reference aborting the whole sequence, and the `float.MaxValue` camera drift on the first glide segment — are NOT in this build.

## 0.6.7

- **Fixed the lead prediction being almost absent on moving targets.** The animal's velocity was measured as a root-position difference divided by REAL seconds, while the position change itself happens on the SCALED clock — so every measured speed was multiplied by the current time scale (20 % low at the default execution scale of 0.8, far worse if you lowered it). That error is absorbed by a deer's 0.6 m body, which is why deer hit 100 % while running grouse and rabbits were missed by two body widths. The prediction now uses the game's own `BaseAi.GetVelocity()`, which is already in game seconds and needs no observation window at all.
- The root-position pair is kept, but as a cross-check and a fallback only, and its denominator is game time now. The sample window also shrank from 0.25 s to 0.1 s real, so it finishes while the first camera swing is still playing — the fire rhythm is unchanged.
- Two new diagnostic log lines make the remaining causes visible instead of guessed:
  `velocity frozen (target 0, WILDLIFE_Rabbit(Clone)): ai 6.2 m/s [ai] vs root-delta 5.8 m/s over 0.080 game s (0.10 real s), angle 4 deg -> using 6.2 m/s from ai`
  `shot 1/3 fired (small prey, v 6.2 m/s [ai], flight 67 ms, lead 0.41 m, aim 0.12 m from the mark, fraction 0.80, next in 0.20s real)`
  The angle between the two velocity estimates is the tell: matching direction with a different magnitude means the pair averaged a hop-and-freeze cycle, a mismatched angle means one source is not the animal's velocity at all.
- A static mark still reports velocity 0 — a zero AI read is treated as a real standstill, not as a failed read, so standing targets keep the un-leaded point.

## 0.6.6

- **New setting 小体型瞄准点 (small-prey aim point, slider 0-1, default 0.6).** It is the aim depth that used to be the hard-coded 0.6: 0 puts the round at the animal's root (its feet), 1 puts it exactly on what you marked (the body surface), anything between lands inside the body. Move it while testing — it is read live, so the next mark, the shot already in the air and the camera all pick it up without restarting or re-marking.
- The mark log now also prints the fraction in use (`small prey: 0.60 from root`), so a log line is enough to know which value produced which result.

## 0.6.5

- **Reworked the small-prey aim point again — the collider centre was worse than no special case at all.** A grouse or rabbit body is ~0.2 m wide, and the union of its colliders is much wider than the body, so the "centre" moved the aim point off the animal: the shot looked more misaligned than before, exactly as reported. The aim point is now **60 % of the way from the animal's root toward the marked surface**, i.e. inside the body — no collider bounds involved, so nothing can drag it outside.
- The point is used identically for the camera, the lead intercept and the in-flight steering, so the round no longer starts at one point and bends toward a second opinion.
- Mark log now prints the AI type, the distance between the shot point and the crosshair, and the body's collider bounds (`bodyBounds=... bodyCentreFromRoot=...`) — diagnostics only, no aiming uses them. On a grouse the bounds numbers show why the previous centre was unusable.

## 0.6.4

- **Grouse and rabbits now get their own aim point.** They were the losers of the 0.6.3 change: steering to the root plus a height used a 0.4 m floor, which sits above a ptarmigan's back, so a mark that looked dead-on still flew over it. Small prey is now detected (AI class `AiRabbit` / `AiPtarmigan`, then prefab name, then measured body height) and aimed at the **centre of its colliders** — the part you marked no longer matters, because a body that small dies to any hit.
- The centre is used everywhere the shot is described: the lead intercept, the camera glide onto the mark, and the in-flight steering, so the round leaves the muzzle already aligned instead of turning after it.
- The mark log now prints the AI type and which aim model was chosen (`small prey: body centre` / `large: root + height`), so a species that slips past detection is visible instead of showing up as a silent miss.

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
