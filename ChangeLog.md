# ChangeLog

## 0.7.57

- **New setting 解锁所需的左轮技能等级 (`RequiredRevolverLevel`, weapon-binding section, 1-5, default 1).** Dead Eye is now a progression reward: the toggle does nothing until the vanilla revolver skill reaches the configured level, and the entry is refused with a log line (`activation refused: revolver skill 2 < required 3`) instead of silently working. 1 = the starting level, i.e. the old behaviour (nothing to unlock); 5 = only a maxed revolver skill unlocks the mode.
- **The number is the one the skills page shows.** `Skill.GetCurrentTierNumber()` returns a 0-based tier INDEX — verified in the 2.55 disassembly (0 points -> 0, points at the last threshold -> 4) — so the mod adds 1 before comparing. Levels are the vanilla five, and the setting is clamped into 1-5 whatever the JSON says.
- **Checked on activation only, before the gear and fatigue refusals.** Skill levels never go down, so a running sequence cannot fall below the threshold; and a low-level player pressing the key is told about the level rather than about a revolver they may well be carrying. A level that cannot be read (no skills manager yet) passes ONLY when the requirement is 1 — a missing read cannot satisfy a real requirement, and it cannot break the default ungated game either.
- Fixed on `Core/DeadEyeManager.cs` (`TryEnter` + `RevolverSkillUnlocked` / `RevolverSkillLevel`), `Settings.cs`; documented in `docs/DEVELOPMENT.md` §2/§8/§10 #43. `README` badge and `BuildInfo` bumped together (version check in §16).

## 0.7.56

- **Fixed: the toggle key started Dead Eye inside the ESC menu and the inventory.** The gameplay guard only asked `Panel_HUD.IsEnabled()`, and the HUD stays ENABLED behind the pause menu, the inventory, the container view and every other screen-taking panel — so all of them passed. The toggle key is read from raw Unity input (deliberately: it is the mode's own key, not the game's), so nothing else stopped it either: the mode entered marking, the world dropped into bullet time and the yellow filter painted over a menu.
- **The guard now asks whose screen it is.** `InterfaceManager.IsOverlayActiveImmediate()` is added next to the HUD check, plus `uConsole.m_On` and `GameManager.m_IsPaused` (see `docs/DEVELOPMENT.md` §10 #42). The overlay flag is "some cached panel is enabled and calls itself an overlay", and `Panel_Base.IsOverlayPanel()` returns **true** by default while the panels that share the screen with gameplay override it to **false** (`Panel_HUD`, `Panel_Subtitles`, `Panel_SaveIcon`, `Panel_Camera`, `Panel_Actions`, `Panel_TutorialPopup`, `Panel_Loading`, `PopupBase`, both debug panels — verified in the 2.55 disassembly). `Panel_Inventory`, `Panel_PauseMenu`, `Panel_OptionsMenu`, `Panel_Crafting` and the container/harvest views inherit `true`, so they are rejected without a single panel class being listed, and a menu Hinterland adds later is covered for free.
- **A menu opened mid-mode now ends the mode instead of sitting under it.** Marking and the firing sequence share the same guard, so opening the inventory or the pause menu force-exits: the time scale, the yellow filter and every mark are restored, instead of leaving the world slowed while the player browses. The firing watchdog still runs on its own (its reason is now logged separately from "gameplay lost").
- **Diagnostics: `activation ignored: <gate>`**, at Info and throttled to one line per 2 s, names the gate that refused (`interface panel open`, `game paused`, `HUD not up`, …). A report of "Dead Eye did nothing" or "Dead Eye opened in my menu" is now answered by the session log without a repro.
- `README` badge and `BuildInfo` bumped together (version check in §16); the player-facing behaviour is unchanged inside normal gameplay.

## 0.7.55

- **New setting 屏幕文字提示 (`ShowHudText`, 视觉 section, default OFF).** It switches EVERY text the mode prints along the bottom of the screen: entering / leaving, the mark counter, the cap banner, the execution banners, "nothing alive at the crosshair", and the per-mark refusal reasons. Off = the marker Xs and the yellow filter, nothing else — which is now what a fresh install gets.
- **This replaces the 0.7.53 debug-build-only split** (that build shipped the line in DEBUG and dropped it in release). The split answered the wrong question: a release player who wanted the counter could not get it, and a debug player got it whether they wanted it or not. One checkbox covers both.
- **The gate is per frame in `Overlay.Draw`**, not at queue time, so unticking the box hides the line that is already on screen and re-ticking shows the next message without a restart. Messages are still stored while hidden (`Overlay.SetMessage` is unconditional), which is what makes the toggle instant.
- Refusal messages that happen BEFORE the mode starts (exhausted, no bound revolver, empty cylinder) are covered by the same switch. With the text off the toggle key stays silent — that is the intended trade, the yellow filter and the Xs are the mode's output and the text is optional commentary.
- `Overlay.SetNotice` (added in 0.7.53) is removed; all thirteen call sites are back on `Overlay.SetMessage`. Verified by decompiling the release dll: `Draw` reads `Settings.ShowHudText`, and the settings page lists twelve entries (twenty-three in a debug build). `docs/DEVELOPMENT.md` §1/§8/§10 #39 and both README blocks updated.

## 0.7.54

- **The mark now sits where you aimed.** A head mark's marker (and the camera's glide target) is the head centre, not the point the old body-box fallback picked — on a howling wolf that fallback put the X on the chest while the crosshair was on the head. Positional only: it says what was aimed at, not where the round will land.
- **Rounds are aimed at the head as well.** The launch direction always pointed at the head, but in-flight steering chased `root + up * AimHeight`, and `LargeAimHeight` clamps to 1.20 m — a wolf's head sits 1.23–1.57 m above its root, so the trajectory was pulled back onto the chest within a few frames (measured: marks on `capsule_head` resolving on `capsule_chest`, 0.66–0.73 m off the mark). A head mark now chases the head capsule's live centre instead, which is also what keeps it correct when the animal turns.
- **If the head capsule cannot be hit, the shot falls back to the body and still reads the head row.** The game disables the damage capsules while a howling wolf holds its head above them (measured: three wolves, all with a single enabled `1.00x1.50x1.00` box and no capsule reachable). A round aimed at a switched-off capsule flies clean over the animal, so the aim point drops back to a torso point (the marked height clamped into the 0.40-1.20 m body band) at the fire moment; damage scale, bleed-out and the critical roll stay the head row, and the hit reaction still plays at the head.
- **Chest marks stay chest marks.** A click that lands on a capsule is named by that collider's identity: the head row applies only when the click hit the head capsule. The 0.35 m radius survives for the one case that has no surface to identify — a click that hit no collider at all (the howling wolf). Before this, an upper-chest aim was promoted to a head mark, because the head box and the chest capsule overlap in height (measured: `capsule_chest` at 1.48 m from the root, head centre at 1.57 m).
- **Which row is fixed at mark time, where to aim is decided at the trigger.** The row comes from the marking ray and is never re-decided, so a wolf that turns its head between the first mark and the sixth shot cannot silently fall back to the body row; the aim point is re-evaluated per shot because the capsules can be switched off in between.
- **License changed from MIT to GPL-3.0-only.** `LICENSE` now holds the GNU General Public License v3, all 23 source files carry an `SPDX-License-Identifier: GPL-3.0-only` header, and the README badge plus both licence sections were updated. From this release on, anyone distributing a modified Dead Eye must do so under GPL-3.0 with source available. Releases published before the change (0.7.53 and earlier) were released under MIT and remain under MIT — that grant is irrevocable and is explicitly not withdrawn, so no old version is retroactively relicensed.

## 0.7.53

- **Head marks now resolve as head hits.** A mark that qualifies as a head mark (crosshair within 0.35 m of the head capsule, found by `LocalizedDamage.m_BodyPart` rather than by object name) makes EVERY round of that execution read the head row of the damage table — damage scale, bleed-out minutes and the critical-hit roll — and play its hit reaction at the head, whichever capsule the round actually reached. The reason this is needed: shots resolve on the capsule the round passes closest to (in practice the chest), so a mark placed dead centre on a howling wolf's head used to roll the chest row no matter how well it was aimed.
- **Geometry is not relaxed.** The round still has to resolve on a damageable capsule of that animal; a mark aimed beside it does not become a headshot, and the non-damageable root capsule (five to seven times the model's size) is still not a hit. Small prey is excluded — any hit already kills a grouse or rabbit, so the head row would add nothing.
- **No new setting.** The 0.35 m qualification radius is compiled into both builds and listed in the README's tuning table. Homing bullets must be on: the override lives in Dead Eye's own hit resolution, and a head mark made with homing off logs a warning instead of failing silently.
- The head centre travels with the mark in the head capsule's local space, so the hit reaction plays where the head is when the round arrives, not where it was when the trigger was pulled.
- **The bottom-centre status line is now a debug-build feature.** (SUPERSEDED in 0.7.55 by the `ShowHudText` setting — the debug/release split was replaced because it decided who can see the text by build kind instead of by preference.) Release builds draw only the mark Xs: the `死神之眼 · 标记目标（0/6）` banner, the `已标记 n/m` counter, the per-mark refusals with their measured miss and tolerance, and the phase banners are gone from the shipped dll. They were development telemetry — the yellow filter, the scratch wipe and the Xs already tell the player everything they need — and the numeric half of it (`偏 0.42 m，容差 0.55 m`) only means something while tuning.
- **One exception kept on purpose: the pre-entry refusals.** `太疲惫了，无法进入死神之眼`, `身上没有携带绑定的左轮手枪` and `左轮里没有子弹，无法标记目标` still show in both builds — without them the toggle key looks broken, and they are the only feedback the mode has before it starts. Everything after `State = Marking` is debug-only.
- The split lives in one place (`Overlay.SetNotice` vs `Overlay.SetMessage`) instead of a preprocessor pair at each of the thirteen call sites, so a message added later cannot leak into release by omission. Verified by decompiling both configurations: release `SetMessage` is an empty method, debug still calls `Store`. `docs/DEVELOPMENT.md` §1/§8/§10 #39 updated, both README blocks reworded (they promised the banner).

## 0.7.52

- **Fixed: 0.7.51's cover test rejected perfectly aimed marks — a TRIGGER was read as solid cover.** The first live session (2026-11-04 02:42) had two clicks on a wolf at 62.7 m with the crosshair 0.00 m and 0.22 m off its body, both refused with `准星处没有活物`: the fallback saw `TRIGGER_FishingHolePlacementSalt_cube` (layer 25, `trigger=True`, an invisible fishing-hole placement volume) 13 m in front of the wolf and treated it as a wall. Triggers neither render nor stop anything, so `MarkFallback.Blocked` now skips them and only real geometry can block a fallback candidate. The refusal line also names the blocking collider and its distance (`is behind 'X' at N m`) — the old wording "behind a raw hit" is exactly what made this take a log dig to find.
- **The same session confirmed the rest of the fallback works** (so this is a one-line-scale fix, not a redesign): 4 marks out of 6 clicks (2 raw-hit degradations, 2 fallbacks at 24.9 m / 0.00 m and 18.9 m / 0.37 m), 4 rounds fired, 4 hits, 2 critical. Every mark resolved with all `capsule_*` colliders `enabled=False` (the 0.7.40 howl/idle state gate still holds), and both fallback marks hit `capsule_stomache` at fire time.
- **Forensics no longer floods the log.** The second live session produced 226 warnings, because the `no-damageable` dump printed both collider inventories (149 + 14 colliders) on every click while the `Wildlife` holder with its twelve `BaseAi` children kept defeating the root rule. The header, the collect-path line and a one-line summary (`N collider(s), M damageable of which K enabled, T trigger(s)`) still print on every failure; the full inventory now prints once per animal per marking session (cleared when the mode is entered) and the raw-hit list is capped at 25 lines.
- Known, deliberately unchanged: the fallback radius is a pure linear distance, so it is far more permissive in angle at close range (1.2 m is 3.6 deg at 19 m but 13.7 deg at 5 m). No session has reported marking an animal beside the crosshair, so the threshold is left as tuned.

## 0.7.51

- **Fixed: a mark was refused on an animal the crosshair was visibly inside.** The first marking gate demands an exact ray∩collider intersection, and a howling wolf raises its head above the 1.00x1.50x1.00 m body capsule — the fur the player sees is simply outside every collider, so the click reported `准星处没有活物` with the crosshair on the animal. The 0.7.40/0.7.41 sessions measured it twice: the ray touched NOTHING on its whole 150 m path while the wolf stood 28.8 m away with a 0.90 m linear miss (and 12.5 m / 1.14 m in the earlier case).
- **New: linear closest-approach fallback for that gate.** When no raw hit resolves to a live `BaseAi`, the mod now scans the live animals once and picks the one whose BODY BOUNDS lie nearest to the crosshair ray, ties broken by the animal the player is actually facing. That animal then goes through the normal resolution (damageable colliders first, the existing raw-point degradation when the capsules are disabled), so the fallback only chooses WHICH animal — it never invents a mark point. Triggered by the failed click alone, so there is no per-frame cost.
- **The fallback radius replaces the crosshair gate on that path only.** The body-scaled tolerance is 0.60 m on a wolf capsule, i.e. below both measured near misses, so re-applying it would refuse exactly the clicks the fallback exists for. A ray that DOES hit an animal keeps the old gate untouched, and a neighbouring animal in a herd is still not marked (the nearest body within the radius wins).
- **Two false targets rejected by the scan:** the far "ghost" twin of the same prefab reports a 0.00x0.00x0.00 body box 200 m away (zero-extent candidates are skipped), and an animal that a raw hit puts clearly in front of it is behind cover, not the target (`Blocked`, with the body's half-diagonal as slack so the animal's own oversized root capsule/aux triggers never count as cover).
- **New setting 标记回退半径 (`MarkFallbackRadius`, 0 = off, release value 1.2 m)** in the 弹道 section, alongside the marking tolerance knobs; read live per click, so a debug build retunes it without restarting. The mark log distinguishes the path: `mark: no raw hit resolved to an animal — linear fallback chose X at N m, M m off the ray (radius R m)` at Warning, and the commit line's source reads `linear-fallback M m (… surface)` or `… (body box point)`. `docs/DEVELOPMENT.md` §1/§2/§8/§9/§10 #38/§14 updated; both README blocks gained the tuning row and a FAQ entry.

## 0.7.50

- **Fixed: three disagreeing sources for the near-miss radius default.** The field initializers said 0.6 (both builds), the `DefaultNearMissRadius` fallback const said 0.5, and the slider description claimed "0.5 = the tuned value" — so a frame with `Settings == null` silently ran a 0.1 smaller near-miss radius than every other frame. All three now derive from one place: the field initializers reference `DefaultNearMissRadius` (compile-time const, usable in initializers) and the const is 0.6; the description text updated to match.

## 0.7.49

- **Removed the entire revolver-bug probe matrix; root cause attributed outside this mod.** The user found the stable reproduction and isolated the trigger: firing at the tail of the ACCELERATED reload animation (Skill-Adjustment's reload-animation-speed-up) — with the boost on, a left-click during the arms' return-to-idle enters the bug (silent shot, then a dead reload); with the reload speed back at 1, the same click correctly does nothing until the weapon is fully returned. All probes introduced across 0.7.39-0.7.48 are removed: the input-block swallow logging, the reload-input heartbeat/observation, the input-Process and context PUSH/POP ledgers, the HasContext refusal logging, and the entire fire-stage matrix (shooter.Fire / Trigger_Generic_Fire / GunItem.Fired / FX / muzzle flash / complete event / PlayFireAudio / misfire roll / jam probes) plus the CanReload/Reload/shooter.Reload chain. The input-block prefixes are back to their silent pre-0.7.39 form, and AdaptiveProjectileRedirectPatch back to the once-only "engaged" line. KEPT: the silent context-stack mirror and the WorldViewContextRestore (formerly the HasContext probe) — a safety net that re-pushes the gameplay context when its membership refusal persists over 1 s while Dead Eye is inactive, the HUD is up and no panel owns input (the exact state the Skill-Adjustment bug leaves behind; at most once per episode). The full evidence handoff for the Skill-Adjustment session lives in the workspace analysis notes, and the probe-era source is archived as TLD_DeadEye_Patches_probes_backup_0.7.48.cs.

## 0.7.48

- **Reload-decision chain probes.** The 0.7.47 session proved the input context can be HEALTHY while R still fails — three presses reached the game (heartbeat "reached", suppress no) yet no reload trigger ever fired, and the refusal sits between the input reader and the reload animation. The vanilla chain is vp_FPSPlayer.CanReload() → vp_FPSPlayer.Reload() → vp_FPSShooter.Reload(ammoCount), guarded by m_NextAllowedReloadTime which every Fire — INCLUDING dry fires on an empty cylinder — may refresh. Probes: CanReload postfix (throttled false log), Reload prefix, shooter.Reload postfix (ammo + bool result). Also clarified: the pause menu freezes Time.time (timeScale 0), so a +0.06 s suppression stamp set at pause-open legitimately persists until unpause — the "(time-gated)" refusals during menus are vanilla behaviour, not a defect.

## 0.7.47

- **Fixed (rewritten): the dead reload — restore the lost gameplay input context.** Retracted the 0.7.46 clamp (static analysis proved it could never fire: the only writer of m_InputDisableTime sets Time.time + 0.06, far below its 1 s threshold). The evidence chain: a binary pattern scan found exactly ONE writer of m_InputDisableTime — PopContext itself (Time.time + 0.06, the constant read from .rdata); the two +0xA0 writes inside InputManager.ProcessInput belong to a PlayerInConstrainedCamera instance (the earlier "ProcessInput re-writes it every frame" claim was a misattribution and is retracted); the caller lists show every Panel_*.Enable calls PopContext (the gameplay context WorldView LEAVES the static m_InputContexts list while a panel owns input) and the panel's close path is supposed to PushContext it back. The logs showed HasContext('WorldView') = FALSE continuously while no panel was open and the push/pop ledger was balanced: the WorldView instance was missing from the static list, and since that list is static it survives scene loads, later sessions start with the gameplay context absent (reload, aim and every gated reader refuse; fire is ungated and keeps working). The fix: the HasContext probe caches the WorldView instance from the game's own continuous polling, and when the refusal (membership, time gate clear) persists over 1 s while Dead Eye is fully inactive, the HUD is up and no panel owns input, it issues PushContext(currentInstance) — the exact invariant the game's own panel close path maintains — once per episode, logged loudly. Legitimate 0.06 s pop windows and open panels never trigger it. Performance: per-call dictionary lookup + two float compares, no allocation after the first call per instance; far below 0.01 ms/frame.

## 0.7.46

- **Attempted clamp — RETRACTED in 0.7.47.** A postfix on InputManager.Process clamping m_InputDisableTime back to now when it sat more than 1 s ahead. Static analysis the same day proved the only writer sets Time.time + 0.06, so the clamp could never fire; removed.

## 0.7.45

- **Heartbeat upgrade: the suppression timestamp made visible.** The reload-input heartbeat now prints the raw m_InputDisableTime vs Time.time values (the static field the HasContext disassembly gates on), and logs the moment it is set into the future ("input suppressed … N s ahead"). First real values: m_InputDisableTime=89.51 vs Time.time=107.45 (ok) in a healthy window — the read path works.

## 0.7.44

- **Forensics round 7 — the complete one-shot probe matrix for the revolver bug.** Everything left unknown is now instrumented at once, all log-only. Input side: the context-stack ledger keeps a managed mirror and prints depth + full contents on EVERY `PushContext`/`PopContext` (a pop against an empty mirror is logged loudly as the leak; an out-of-order pop is flagged), `HasContext` logs a FALSE result with the refused context's name (resolved via TargetMethod against stub overloads), and `InputManager.Process` beats a 5 s heartbeat so "input processing died entirely" separates from "a gate refused one reader". Fire side: `fire stage 0` anchors `vp_FPSShooter.Fire` (the entry the mode itself calls), stage 2.6 logs `ShowMuzzleFlash(hasMisfired)` — the game's own misfire verdict per shot — stage 2.7 logs the fire-complete animation event (a missing complete event leaves the weapon busy and would refuse reload with no input trace), and `PlayDryFireAudio` logs the misfire/dry-click branch. With this matrix one reproduction session yields: the full push/pop ledger, the gate verdicts, the poll heartbeats, and every stage of both fire chains (0 entry, 1 trigger, 2 keyframe, 2.5 FX, 2.6 flash, 2.7 complete, 3 legacy audio probe, plus the misfire roll) — enough to write the fix without another round.

## 0.7.43

- **Reload-input heartbeat: closing a blind spot in the 0.7.41 probe.** The observation postfix only logged a TRUE result, so a dead-reload window cannot distinguish "polled and refused by the context gate" from "not polled at all" — the game's weapon/hud state machine may stop asking for reload input one level ABOVE the input manager, which is a different mechanism and a different fix. The postfix now keeps a poll counter and flushes a heartbeat line every 5 s while inactive (`N poll(s) in 5 s, last result X`); during a dead-reload window the ABSENCE of heartbeats is itself the evidence that the game stopped polling. The press-edge line is unchanged. Also verified this round: the Dead Eye source never calls PushContext/PopContext/HasContext — the mod cannot be the one mutating the input-context stack.

## 0.7.42

- **Forensics round 6: the input CONTEXT STACK and the FX keyframe.** The 0.7.41 session produced two hard facts: (1) `fire stage 3 (audio)` logged ZERO times across 84 stage-1 hits — `GunItem.PlayFireAudio` is simply not the 2.55 revolver's shot-audio path, so the probe targeted a dead method; the real FX stage is `PlayerAnimation.OnAnimationEvent_Generic_Fire_FX` (private, animation-event driven: muzzle flash + shot audio). (2) The dead reload is now located: after the six-shot burst, R produced no `reload input reached` line for 16 s — `GetReloadPressed` never returned true — and its disassembly shows the gate BEFORE the key read: `InputManager.HasContext(context, 0)`. The game keeps a static context stack (`m_InputContexts` / `m_CurrentContext`, managed by `PushContext` / `PopContext`); one unbalanced push — from any mod reacting to anything — makes every gated reader return false with no trace while fire (a different gate path) still works. New probes, all log-only: `input context PUSH/POP '<name>'` (every stack mutation with the Dead Eye state at that moment — the next repro yields a complete who-pushed-what ledger), `fire stage 2.5 (FX keyframe)` on the FX event handler, and `misfire roll -> ok/MISFIRE` on `MisfireTable.DoesMisfire` (the per-shot roll inside `vp_FPSShooter.Fire`, whose misfire branch is `PlayDryFireAudio` + HUD message). Jam/misfire probes from 0.7.40/0.7.41 stayed silent — no jams occurred.

## 0.7.41

- **Forensics round 5, two probes aimed at the last blind spots.** The 0.7.40 session nailed the marking bug with the cleanest evidence yet: the ray hit ZERO colliders on its whole 150 m path while the cone scan put the howling wolf at 28.8 m / 1.80 deg = 0.90 m linear miss — right at the top edge of the 1.00x1.50x1.00 m capsule. Second independent case (12.5 m / 5.25 deg / 1.14 m earlier), both covered by a ~1.2 m linear closest-approach fallback. New datum: the FAR twin instances report body 0.00x0.00x0.00 — no colliders at all — so the fallback must ignore zero-bounds instances. On the revolver side the six post-exit shots were ALL mechanically complete (trigger x2, keyframe x2 with the AA-normal projectile=null, projectile in flight, ammo 6->0) yet the player heard nothing, and R afterwards produced NO Reload trigger — but the input block only logs when it swallows, so whether R even reached the game was unknown. Two probes: `fire stage 3 (audio)` on GunItem.PlayFireAudio (separates "audio event never ran" from "Wwise ate a posted event"), and an observation postfix on GetReloadPressed that logs a true result outside the mode (throttled to the press edge, 1 s) — repeated lines mean the input arrived and the refusal lives in the weapon logic; silence means it never got there.

## 0.7.40

- **Forensics round 4: the vanilla fire pipeline gets stage probes.** The 0.7.39 session settled the marking question — during the howl/idle state the game itself DISABLES every damage capsule (`capsule_head`/`capsule_chest`… all `damage=True enabled=False` in the inventory dump; they re-enable once the wolf goes alert, which is why later marks resolved `capsule_head` and the execution bullets hit) — and caught the revolver bug in three shapes: normal shots, AA launches under `Exiting`/`Inactive` (all with correct ammo), and the reported one — a silent 3→0 ammo drain with NO `HipFire` trigger, NO AA launch, NO projectile, then a dead reload until re-equip. That shape points at a break between the fire entry and the fire KEYFRAME: `Trigger_Generic_Fire` (entry, consumes the round) and `GunItem.Fired` (keyframe: spawns the projectile, plays the audio) are separate — a break between them spends the round with no sound and no bullet, and leaves the weapon busy. Four new log-only probes (Info, one per trigger pull / jam change): stage 1 `Trigger_Generic_Fire` (roundsInClip, willJam), stage 2 `GunItem.Fired` (projectile name), plus `SetJammed` (Warning) and `MaybeHandleMisfire` — a jam/misfire path would explain the dead reload without any state bug. Next repro decides which stage dropped.

## 0.7.39

- **Forensics round 3: the revolver bug after a cancel-then-fire is now instrumented.** The 0.7.38 session reproduced it with a full paper trail: a failed mark (ray over the howling wolf's collider top), V-exit, one NORMAL shot that hit the wolf (`capsule_chest`, layer 27, `LocalizedDamage` yes — the animal was hittable; the raised howling head simply sits outside the collider union), then a second enter/exit cycle, and then three GHOST rounds: `ProjectileItem` projectiles in flight (destroyed on terrain at ~295 m/s) while the weapon animator showed only movement blends — no `HipFire` trigger, no ammo write (`RoundsLoaded` stuck at 5), no reload trigger, no sound. The vanilla fire pipeline never ran, yet AdaptiveArsenal launched — the two pipelines diverged. Suspect #1 is the input lockout (it guards the InputManager readers AdaptiveArsenal does not use), suspect #2 a state that never reached Inactive. Three new evidence sources, all log-only: (1) every actually-swallowed input logs at Warning with the reader, the key, the state and the time scale, throttled to one line per reader per 0.5 s; (2) the exit path logs `Dead Eye inactive (exit complete)` so a stuck state cannot hide; (3) `AA Fire` logs one Info line PER launch with the state, scale and whether the aim redirect applied — replacing the old once-per-session "prefix engaged" line, which could not attribute the ghost rounds. Also from this session: the twin-instance finding is confirmed with instance ids (`WILDLIFE_Wolf_grey_0` at 12.5 m AND 150.8 m), and the marking failure is settled as pure geometry — the fix direction is a linear closest-approach fallback (~1.2 m at the measured 5.25 deg / 1.14 m / 12.5 m case), not tolerance tuning.

## 0.7.38

- **Forensics round 2: the no-damageable degradation now dumps the full collider inventory.** The 0.7.37 session answered the first question — on every failed click the ray hit ONLY the warehouse scenery 52-59 m behind the wolf while the cone scan put the howling wolf at 25.6 m / 1.5-1.7 deg off-ray (≈0.7 m linear miss, right at the top of the wolf's 1.00x1.50x1.00 m capsule): the raised howling head is a genuine hitbox blind spot. But it opened a second one: marks made seconds later degraded with `nothing damageable resolved` (the ray had hit the wolf's layer-16 root capsule, which has `BaseAi` but NO `LocalizedDamage`), and then at 20:14:21 the same wolves resolved `capsule_head` normally — so the damage capsules' availability is state- or instance-dependent. The static trail is exhausted: `BaseAi$$ChangeCollisionCapsulesLayer` has exactly one caller (`BaseAi$$EnterDead`, corpse re-layering — not it), `LocalizedDamage` is a bare marker component, `DamageTrigger` is environmental. The discriminator is runtime: on every `nothing damageable resolved` the mod now logs (Warning) the hit distance, the collect path (transform root vs BaseAi object, with the `BaseAi` count in the root — a shared holder defeats the root rule), and EVERY collider under both candidate sets including disabled ones and triggers (`goActive/enabled/trigger/damage` per line, capped at 40). Absent capsules → hierarchy mismatch; present-but-disabled → a game-side state gate that would also make the animal unhittable for vanilla bullets while it lasts. The cone scan lines gained the instance id and `active` flag — the 0.7.37 log showed `WILDLIFE_Wolf_grey_1` twice (25.6 m AND 222.9 m), so same-named near/far twin instances exist and must be told apart. Implementation: `MarkForensics.LogNoDamageable` + `DumpInventory`; `AnimalBody.CollectFor` widened to internal.

## 0.7.37

- **New: a forensic dump when marking fails at the first gate (log-only, no behaviour change).** A 2026-11-03 session showed a howling wolf whose visible head the crosshair sat inside, yet marking reported `准星处没有活物（射线没碰到动物）`. The first gate demands an exact ray∩collider intersection, so this failure has three possible shapes that all look identical from the overlay: the ray truly touched nothing (hitbox coverage thinner than the visible mesh), a hit WAS recorded but skipped by the layer-16/27 `BaseAi` resolution (an aux volume such as the `Audio_Wildlife` trigger on layer 22), or the animal is genuinely off-ray. A failed mark now logs at **Warning (not behind Verbose — a diagnostic nobody can see is what made the 0.7.8 marking regression invisible)**: every raw `RaycastAll` hit with name, layer, collider type, trigger/enabled, `LocalizedDamage` and `BaseAi` direct/parent resolution, followed by a cone scan of the three nearest LIVE animals by angle to the ray with their body bounds. If the smallest reported angle is a few degrees at tens of metres, the crosshair was on the animal and the ray still found no collider — the direct evidence for a cone / sentinel-volume fallback (the next step) instead of tolerance tuning. Implementation: `Core/MarkForensics.cs`; `AnimalBody.TryGetBodyBounds` widened to internal for the scan. `docs/DEVELOPMENT.md` §9 updated.

## 0.7.36

- **Fixed: ironsight aiming still worked during the mode.** The 0.7.25 lockout blocked the fire, reload and generic key paths, but RMB is wired as the game's "alt fire" action: `vp_FPSPlayer.InputZoom` — the weapon-aim state machine and the only caller of `vp_FPSWeapon.CanStartAiming` (found via `find_callers.py` on both `CanStartAiming` variants plus disassembly of the hit) — reads it through `InputManager.GetAltFirePressed / GetAltFire / GetAltFireReleased`, none of which were patched. All three are now blocked while marking or firing, covering every phase of the gesture (press, hold, release) so no partial aim state can start or stick.
- A practical note from the same investigation: RMB was never read through `GetKeyDown(Mouse1)` or `GetMouseButtonDown(1)` during aiming — the earlier prefixes were correct but simply aimed at paths the aim state machine never walks.

## 0.7.35

- **New settings: 刮屏白带宽度 (0.15-0.60, default 0.30) and 刮屏扫过时长 (0.3-1.5 s, default 0.6)** under a new 刮屏特效 section — the sweeping band's width and speed are now tunable in-game for live testing instead of being compiled constants.
- **The horizontal scanlines are gone.** Cause: both the veil and the blob textures modulated alpha PER ROW (a "feather" design inherited from the tail era) — each of the 128 texture rows stretches to ~8 px of screen and spans the full width, so adjacent rows differing by up to 5x in strength rendered as full-width horizontal stripes; inside the bright band the same modulation drew brighter lines. Both textures are now smooth (per-pixel grain only, the veil keeps its gentle lower-middle emphasis), leaving just the wide, slightly tilted white band.

## 0.7.34

- **The scratch is now a static veil + a sweeping glow, by design.** Pixel measurement of the 0.7.32 capture proved the remaining "straight line" was not a seam but the tail haze's VISIBILITY ONSET: the tail alpha ramps from zero, white-over-bright-scene becomes perceptible at alpha ≈ 0.045, and the point where the ramp crosses that threshold travels with the strip as a coherent, leaning line — a perceptual edge no envelope curve can remove, because removing the ramp's steepness just moves the line.
- Solution, mirroring how the tint already works: the white haze is now a full-screen fibrous VEIL that appears the moment the mode starts (no spatial alpha ramp — the onset line cannot exist by construction) and fades out globally over the second half of the sweep (a whole-screen fade reads as fog lifting, never as a boundary). The only moving element left is the bright head GLOW, a soft blob that fades both ways from its centre, sweeping across the veil with the random tilt. Row wisps widened to 0.2-1.0 with 20% tears so both layers read as discrete fibres.
- Trade-off: the haze no longer appears to be dragged behind the scratch — the look is "fog rolls in, one light sweeps across it, fog lifts".

## 0.7.33

- **The tint no longer rides with the scratch — full yellow from the first frame again.** The split-tint experiment (0.7.27-0.7.30: drawing the tint in OnGUI split at the scratch head, canvas suppressed until the sweep ends) produced every hard-line artifact we chased: the unclipped-rect revert region, the rect-end clip line, and finally a 1 px float seam between the solid and gradient rects that showed as a thin coloured line over the scene (0.7.30 capture). The seam class cannot be fully trusted away while two translucent rects must tile edge-to-edge every frame, so the whole path is removed: the canvas overlay goes full yellow the moment the mode starts (as before 0.7.27) and the white scratch — which has no hard edges since 0.7.31 — sweeps over it. Trade-off by choice: the tone boundary no longer follows the head, but no tint seam can exist at all.
- `Overlay.SetTintSuppressed` and the gradient texture are gone; the sweep is a single `DrawTexture` call.

## 0.7.32

- **The streak is thicker and slightly tilted.** The bright head cloud's Gaussian widened (σ 0.07 → 0.12 of the strip) together with the vertical mask (0.26 → 0.30), so the white column reads as a broad burn across the frame instead of a narrow pillar. The whole strip is now drawn rotated by a small random lean (3.5-8°, random sign, set per activation) around the head point, like the reference's scratch — the strip is oversized vertically (1.35×) so it still spans the full screen height after the rotation.
- Housekeeping: the previous build's rect geometry edits had partially not landed in the source (the deployed 0.7.31 paired the new blob placement with the old rect placement); this build pins the blob centre to the head line explicitly in `DrawStreak` and was verified warning-free.

## 0.7.31

- **Fixed: the streak's leading edge was a hard vertical line.** The head cloud's Gaussian was centred on the texture's edge, so the brightest point of the streak had full alpha on one side and NOTHING on the other — wherever the head line was, the white glow simply stopped (the 0.7.30 capture shows it at ~97% screen width). The strip now reserves its last 25% for the forward glow: the blob centre sits at 75% and decays past the head line in the travel direction, so the head radiates forward like a light bloom and fades out with no cut. Rect placement updated for both directions (head centre stays pinned to headX).

## 0.7.30

- **Fixed: the hard tint edge after the scratch head left the screen** (both user reports from the 17-40-33 capture). Root cause found by pixel-measuring the recording: the solid tint rect was `[headX, headX+W]` unclamped — once headX went negative (right-to-left sweeps, last third of the motion) its right end fell INSIDE the screen, exposing an untinted region behind a perfectly straight edge that swept leftward ("a part of the screen reverts to the real look, then the full yellow arrives"), while the gradient rode off-screen with the head, unable to soften it. The rect is now intersected with the screen bounds, so the tint always covers everything the head has crossed, all the way to the sweep's end — the canvas handoff is seamless.
- **The front boundary fade is now perceptible.** Pixel measurement showed the gradient WAS rendering (B channel 199→167 across it) but a linear alpha ramp's low half is invisible over bright snow, so it read as a line. The gradient curve is now sqrt (rises fast near the head) and the soft zone widened to 18% of the screen width.

## 0.7.29

- **Scratch wipe polish from the second live capture** (刮屏特效测试2.mkv): the geometry fix held — the tint boundary rides the head in both directions and the streak is visible for ~0.33 s in near-linear motion. Three look refinements against the reference:
  - The tint boundary is softer: the gradient straddling the head widened from 8% to 14% of the screen width, so the sepia-to-normal transition reads as a wash instead of a straight seam.
  - The tail is sparser (tail envelope 0.6 → 0.45, vertical-mask floor 0.20 → 0.15), letting the world show through the wisps instead of a full-screen haze.
  - The whole streak fades to 60% alpha by the end of the sweep, so the wisps behind the head dissipate like the reference instead of hanging until the tail exits.

## 0.7.28

- **Scratch wipe fixes from the first live capture** (刮屏特效测试1.mkv, R2L direction):
  - **Bug: the tint region was mirrored wrong.** `DrawTintBehindHead` consumed the travelled distance instead of the screen-space head position, so on right-to-left sweeps the screen was tinted from the wrong edge at the start and a stray yellow band floated ahead of the head. Both directions now derive a screen-space headX and tint strictly behind it (L2R `[0, headX]`, R2L `[headX, W]`), with the soft gradient straddling the head line on the correct side. The streak rect had the same class of bug on R2L and is fixed too.
  - **The tail now reads as horizontal wisps.** Streak variation moved from per-column (rendered as vertical blinds after the full-height stretch) to per-row.
  - **Visible crossing slowed to reference pace**: 0.45 s with strong ease-out showed the bright head for only ~0.12 s; now 0.6 s with near-linear motion (~0.2 s of bright head, matching the clip's ~0.27 s), and the vertical mask floor lowered so the streak reads less like a light pillar.

## 0.7.27

- **The activation scratch wipe was reworked to match the reference frame-by-frame** (10.65-10.92 s of the clip). Two structural changes from the first take:
  - **The tint boundary now rides with the scratch head.** The canvas tint no longer pops on globally at activation: while the sweep runs, the tint is drawn in OnGUI only behind the head (solid up to the head line, a soft gradient straddling it), so the world the scratch has crossed is already tinted and the world ahead is still normal — the canvas fades in when the sweep completes.
  - **The streak is a comet, not a band.** The texture alpha is now a vertical Gaussian hugging the lower-middle of the frame times a hot round head cloud plus a feathery tail (column streaks, torn gaps, grain, wavy head line), replacing the uniform full-height strip. Sweep tightened to ~0.45 s.

## 0.7.26

- **New visual: the activation scratch wipe.** Entering Dead Eye now sweeps a bright, feathery white streak horizontally across the whole screen in ~0.5 s (unscaled), starting from a random side — modelled on the RDR2 activation (reference clip 10-11 s: hot head, torn streaky tail, warm tone settling behind it). It runs while the draw reveal plays and finishes even if the mode is cancelled immediately.
- Implementation is one procedurally generated RGBA strip (256x128, regenerated per activation: column streaks, torn gaps, grain and a wavy head line over a power-falloff envelope) drawn in the existing OnGUI layer and mirrored at draw time for the right-to-left direction. No shaders, cameras or post-processing — IL2CPP strips the usual post stack, so the HUD layer is the only reliable canvas.

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
