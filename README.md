# Dead Eye (死神之眼)

[English](#english) · [中文](#中文)

![version](https://img.shields.io/badge/version-0.7.53-informational)

![game](https://img.shields.io/badge/The%20Long%20Dark-2.55-success)

![loader](https://img.shields.io/badge/MelonLoader-0.7.3-blueviolet)

![license](https://img.shields.io/badge/license-GPL--3.0-blue)

---

## English

A The Long Dark mod that slows the world down, lets you mark animals like RDR2's Dead Eye, and then chain-fires every mark with your revolver.

### Features

**Bullet time**

- Press the side mouse button while carrying a revolver — the revolver is drawn if it was stowed (the raise plays out in slow motion), time slows to a tenth speed for 0.7 s (the draw reveal), the screen tints yellow and the signature activation sound plays, like Dead Eye in Red Dead Redemption 2. When the reveal ends the world drops to the fixed deeper marking speed (0.01x), so the rest of the raise crawls through the whole marking phase.
- Entering the mode also plays the RDR2-style white scratch wipe: a bright, cloud-headed streak with a feathery tail sweeps across the screen in about half a second from a random side, hugging the lower-middle of the frame — and the warm tint rides with it, so the world behind the scratch head is already tinted while the world ahead is still normal. Purely cosmetic (procedural textures drawn in the HUD layer), and it finishes sweeping even if the mode is cancelled immediately.
- While marking, a short background loop keeps playing once the activation sting finishes, and stops the moment marking ends — the execution starting, the cancel key, running out of fatigue, any exit.
- Press the middle mouse button while looking at a live animal — a red mark appears over it and stays attached to the animal even while it moves, with a short sound confirming the mark.
- You can mark the same animal several times or several animals in any order — the number of marks never exceeds the rounds left in the revolver's cylinder.

**The execution**

- Press the side mouse button again — time returns to normal, the mod draws your revolver if it was not in hand, and the view swings onto the first mark through the game's own cinematic look-at system. Once the crosshair has swung onto it the sequence opens fire at the weapon's own full rate, one shot per mark, while the view keeps sweeping smoothly from target to target — the swing is paced to the weapon's fire rate, arriving at each mark just as its shot is due.
- Each projectile is redirected the instant it spawns onto a lead-predicted intercept point — where the animal will be when the shot arrives, computed from its tracked movement and the revolver's launch speed (gravity included) — so head marks hit heads and body marks hit bodies even on running targets. The game's own damage, bleed-out, critical-hit and skill-progress rules all apply unchanged. With AdaptiveArsenal installed the bullet also keeps steering onto the animal during flight — toward where it will BE, not where it was — and cancels its own muzzle error on the first frame, so a slightly-off lead still lands. Grouse and rabbits are the exception: whatever part you mark, the mark is taken on the animal's damageable body (its body capsule, which is where the shot converges) — a body that small dies to any hit, and an aim point sitting on the visible surface puts half of any error outside the animal. Because those collision capsules are several times larger than the models, a mark is refused when the crosshair is not really on the body — a tolerance scaled to the animal's size, not a fixed distance — instead of being planted a body-length above the bird. How deep inside is the compiled aim fraction (a debug build exposes it as the 小体型瞄准点 slider). And the slow motion does not end at the last trigger pull: the execution holds until the LAST bullet is consumed — by a hit, by scenery, or by its own expiry — then time restores. The rounds themselves are sped up by the same factor the world is slowed by, so a shot crosses the gap at a normal-looking pace instead of crawling; the boost is capped at what the hit detection can still follow.
- **A mark placed on the head makes every shot of the execution a head hit.** Once a mark qualifies as a head mark, every round that touches the animal reads the head's row of the damage table — damage scale, bleed-out time and the critical-hit roll — and plays its hit reaction at the head, whichever capsule the round actually reached. The round still has to land on a damageable part of that animal: a mark aimed beside it does not become a headshot. Grouse and rabbits are excluded (any hit already kills them).
- While the mode is up — marking or the execution — the left and right mouse buttons and the reload key (R) do nothing: the mod owns the weapon for the duration, and firing, aiming or reloading mid-sequence would fight its own look-at glides and pacing. Movement, the camera, and every other key are unaffected, and the mode's own keys keep working.
- Fatigue drains a configurable percentage of the full bar per real-time second (default 1%, ~100 s from full), and returns to normal the moment it ends — if you run out of stamina mid-mode, it shuts off by itself, and a fully exhausted survivor cannot enter the mode at all.
- Footsteps go quiet for as long as you are marking: entering the mode mid-stride no longer leaves the step sounds running under the slow motion. Normal sound returns the moment the mode ends; footprints and splashes are untouched.

**Bound weapon**

- Only one revolver triggers the mode — you pick which one (左轮手枪 / 猎人的左轮手枪 / 护林人的左轮手枪 / 典狱长的左轮手枪, matching the in-game item names) in the settings; carrying any other gun does nothing.
- With no revolver in your pack, or an empty cylinder, pressing the key does nothing except show a short on-screen reason.

**Draw animation**

- Every revolver draw plays the wide "loaded" animation — the one that sweeps in from the lower right — even when the cylinder is empty (vanilla draws an empty revolver with a small raise from the bottom). Applies to all four revolvers and to any way of drawing one, including the automatic draw when the mode starts. Cosmetic only: the cylinder's real contents, firing and reloading are untouched, and the animator is handed the true value back when the draw ends. Switchable in the settings.

### Requirements

- The Long Dark **2.55** (built against 2.55)
- [MelonLoader](https://melonwiki.xyz/) **0.7.3** or newer (tested on 0.7.3)
- **ModSettings** (required; the settings page depends on it)

### Installation

1. Make sure `MelonLoader/` exists in the game folder (install it and run the game once if not).
2. Put `TLD_DeadEye.dll` into `TheLongDark/Mods`, and the `Asset` folder (the three `.wav` sound files) into `TheLongDark/Mods/DeadEye/Asset`.
3. Start the game.

### Usage

1. Carry the revolver selected in the settings (it can stay in your pack).
2. Press the side mouse button — time slows, the revolver is drawn if it was stowed, and the screen turns yellow. The draw speed (0.1x) only lasts the first 0.7 s; the world then drops to the fixed marking speed (0.01x) so the rest of the raise crawls through the whole marking phase.
3. Aim at an animal and press the middle mouse button — a red mark appears; repeat up to the mark cap. Reaching the cap starts the execution by itself (switchable off in the settings).
4. Press the side mouse button again — the marking phase ends and every mark is shot in order. The yellow tint and the slowdown carry through the whole execution, while your own actions (draw, fire rate) run at normal speed. If nothing was marked, the same press ends the mode immediately. Press the cancel key (Q) instead to abort: all marks are discarded and nothing is fired.

Settings live under **Options → Mod Settings → Dead Eye (死神之眼)**.

### Settings

| Setting | Effect | Default |
|---|---|---|
| 绑定的左轮手枪 | Picks which revolver triggers the mode; the other three are ignored by it | 左轮手枪 |
| 拔枪动作替换 | Every revolver draw uses the wide "loaded" animation, even on an empty cylinder. Cosmetic — the cylinder contents, firing and reloading are untouched | On |
| 子弹追踪 | The execution's bullets keep steering toward the marked animal while in flight (needs AdaptiveArsenal; the vanilla projectile resolves its hit at spawn). Grouse and rabbits are aimed inside the body (depth baked into the release build), so the marked part does not matter | On |
| 进入 / 退出死神之眼 | Key that starts and ends the mode; click the box and press any key or mouse button to rebind | 鼠标后侧键 |
| 标记目标 | Key that marks what is under the crosshair; rebindable the same way | 鼠标中键 |
| 取消标记模式 | Cancels the mode outright while marking: every mark is discarded and nothing is fired (the toggle key with marks starts the execution instead) | Q |
| 标记满自动开火 | Reaching the mark cap (= rounds left in the cylinder) starts the execution by itself, no extra press needed. Off = the old behaviour: the toggle key still has to be pressed | On |
| 疲劳每秒消耗 | Fatigue drain while marking, as a percentage of the full bar per real-time second; raise it to pay more | 1 |
| 黄色滤镜 | Fullscreen yellow tint while marking. **Off** leaves the normal colours | On |
| 滤镜不透明度 | Strength of the yellow tint; 0 equals no tint | 0.22 |
| 刮屏白带宽度 | Width of the white band that sweeps across on activation, as a fraction of screen width | 0.30 |
| 刮屏扫过时长 | How long the band takes to cross the screen, in seconds; lower is faster | 0.6 |
| 音效音量 | Volume of the activation and mark sounds; 0 is silent | 0.8 |
| 输出详细日志 | Writes mode transitions, marks and shots to `MelonLoader/Latest.log` | Off |

### Tuning (debug builds only)

Nine tuning values are **compiled into the release build** and deliberately absent from the settings page, so a shipped dll always plays the way it was tuned:

| Value | Release build | What it does |
|---|---|---|
| Tracking strength | 0.5 | How hard an in-flight round turns toward its mark |
| Small-prey aim point | 0.7 | How deep inside a grouse / rabbit the shot converges (0 = feet, 1 = body centre) |
| Near-miss radius | 0.6 m | A round passing this close to a damage collider counts as a hit |
| Head-mark radius | 0.35 m | How close the crosshair must pass to a large animal's head for the mark to count as a head mark |
| Mark fallback radius | 1.2 m | How far the crosshair ray may miss an animal's body and still mark it — this is what catches a howling wolf's head, which sticks out above its collision capsule; 0 turns the fallback off |
| Execution time scale | 0.2 | World speed during the execution |
| Execution fire rate | 1.5x | How much faster than normal shooting the sequence fires |
| Enter lerp speed | 5 | How fast time slows down on activation |
| Exit lerp speed | 8 | How fast time returns to normal afterwards |

A `DEBUG` build of the same version exposes them all as sliders under 弹道 / 子弹时间, which is the only way to re-tune them in game. The build kind is printed at startup — `Dead Eye (死神之眼) 0.7.12 loaded (release build)` — so a deployed dll can be told apart at a glance. Changes made in a debug build are saved to `Mods/TLD_DeadEye.json` but are ignored by the release build.

A debug build also prints the bottom-centre status line (mark counter, per-mark rejections); the release build draws only the mark Xs themselves. The one exception in both builds is a refusal that happens before the mode starts — exhausted, no bound revolver, empty cylinder — which always shows.

### Compatibility

- Responds only while a save is loaded and the gameplay HUD is up; in menus the keys have no effect and no time-scale change is ever left behind.
- Does not modify item definitions, weapon stats or save files. While the mode is on, Dead Eye owns the time-scale value: other mods that also drive the game speed (for example TimeAccelerator, which resets the scale every frame from an interface-update prefix) take over again the moment Dead Eye ends, because this mod's re-assertion runs after theirs.
- The sounds are three plain `.wav` files under `Mods/DeadEye/Asset`, played through the Windows audio interface (`winmm!waveOut`) — each sound owns its own device handle, so the mark chime, the activation sting and the marking loop can overlap freely. The loop repeats device-side (`WHDR_LOOP`), so there is no gap between repeats. No game audio banks (Wwise) are touched and no Unity audio components are created.
- **AdaptiveArsenal compatible**: detected automatically (with or without it, shots land on the marked point).

### FAQ

**Nothing happens when I press the key?**  
First check that the bound revolver is in your pack and its cylinder is not empty, then check that the mode is not disabled by another mod overriding the game speed.

**The marks do not all fire?**  
A mark on an animal that dies before the sequence reaches it is skipped; shots land at the marked body point, so marks placed on a moving animal's old position follow the animal itself.

**The crosshair sits on the animal but no mark appears?**  
Some poses put visible fur outside the collision capsule — a howling wolf raises its head above the body box. The mod then marks the nearest live animal whose body lies within 1.2 m of the crosshair ray instead of dropping the click; only when nothing is that close does the click fail.

### Uninstall

Delete `TheLongDark/Mods/TLD_DeadEye.dll` (and the `TheLongDark/Mods/DeadEye` folder if you no longer need the sound files).

The save keeps no fields from this mod. If you delete it mid-mode, the next scene load or game restart returns the time scale to normal.

See [ChangeLog.md](ChangeLog.md) for version history.

### License

GPL-3.0 — see [LICENSE](LICENSE).

You may use, modify and redistribute Dead Eye, including shipping the unmodified dll as part of a modpack. A modified version you distribute must itself be licensed under GPL-3.0 with its source available. Releases published before the license change (0.7.53 and earlier) were released under MIT and remain under MIT — that grant is not withdrawn.

---

## 中文

一个把世界放慢、让你像《荒野大镖客2》死神之眼那样标记猎物、再用左轮逐个处决的《漫漫长夜》模组。

### 功能

**子弹时间**

- 携带左轮时按下鼠标后侧键——左轮若还在背包里会当场拔出，世界以十分之一速播 0.7 秒（拔枪亮相），画面染黄，标志性启动音效响起，就是大镖客里死神之眼的味道；0.7 秒后世界固定降到 0.01 倍——剩下的抬枪动作会在极慢的流速里一路贯穿整个标记过程。
- 进入模式的瞬间还有大镖客同款的白色刮屏：一道宽幅、云状亮头、拖着羽状长尾的白色划痕从随机一侧带一点随机倾斜横扫屏幕（约半秒），此时黄滤镜已整体铺满画面。纯外观（HUD 层程序生成贴图），就算刚进模式就取消，划痕也会自然刮完。
- 标记期间，启动音效播完后会接上一段循环背景音，标记环节一结束就停——进入处决、按取消键、疲劳耗尽，任何退出方式都算。
- 准星对着活物按下鼠标中键——它身上出现一个红色标记并跟着动物移动，同时一声短音效确认标记成功。
- 同一只动物可以标多次，也可以标多只——标记总数永远不会超过左轮弹巢里剩下的子弹数。

**处决**

- 再按一次鼠标后侧键——**结束标记环节，直接进入处决**：黄色滤镜与时间放慢一直持续到处决结束（若一个都没标，同一按键则立刻结束死神之眼）。左轮在进入标记时就已经拔出。视角通过游戏自带的过场演出视角系统先甩向第一个标记；准星到位后立刻以武器原生满射速逐个开火，视角在目标之间如「甩枪」般连续平滑掠过——甩动节奏与射速匹配。**放慢只作用于世界：人物自身的拔枪、开火动作与射速都以正常速率进行。**
- 每发弹丸在出膛瞬间被重定向到**提前量拦截点**——根据动物当前移动速度与左轮出膛速度（含重力下坠）计算「子弹到达时它会在哪」——所以奔跑中的目标也能标头打头、标身打身。游戏自己的伤害、失血、要害判定和技能经验全部原样生效；装了自适应兵工厂时，子弹还会在飞行途中持续修正方向追着它走——追的是「它将要到的地方」而不是「它刚才在的地方」，并且出膛第一帧就抹掉枪口误差——提前量稍有偏差也能命中。松鸡和兔子是例外：无论你标的是哪个部位，标记都取在它的**可伤害身体**上（子弹朝那个胶囊收束）——这么小的目标挨一发就死，而瞄准点如果贴在它可见的表面上，任何偏差都有一半落在身体之外。由于这些碰撞胶囊比模型大好几倍，准星没有真正落在身体上时会**拒绝标记**（容差按动物体型缩放，不是固定距离），而不是把标记点飘到鸟的上方半米处。收进多少由内置的瞄准深度决定（调试版里对应「小体型瞄准点」滑条）。此外，子弹时间不会在最后一枪扣下时就结束——处决效果会一直维持到最后一发子弹消失（命中目标、打在环境上、或自行飞完超时），之后时间才恢复正常。子弹本身也会按世界放慢的倍数加速，所以它划过那段距离的观感接近正常射击，而不是慢吞吞地爬；加速有上限，取命中判定还能跟得上的速度。
- **标在头上的标记，会让这次处决的每一发都按头部结算。** 只要一个标记被判定为头部标记，之后任何一发只要打中这只动物，伤害倍率、失血时间与爆头暴击掷骰都取头部那一行，受击反应也播在头上——不管子弹实际碰到的是哪个胶囊。子弹仍然必须真正打中一个可伤害部位：准星偏在动物旁边的标记不会变成爆头。松鸡、兔子不适用（挨一发就死）。
- 模式开启期间——标记或处决——鼠标左键、右键与换弹键（R）全部无效：这把枪整个期间由 mod 接管，中途手动开火、瞄准或换弹都会和它自己的甩枪视角与射击节奏打架。移动、视角和其余按键不受影响，模式自己的几个键也照常可用。
- 标记期间疲劳按真实时间每秒消耗总量的一定百分比（默认 1%，满疲劳约 100 秒耗空），结束立刻恢复——中途力竭会自动收招，完全力竭时根本进不了模式。
- 标记期间脚步声静音：走过去随手开启死神之眼，不会再听到慢动作里还在响的脚步。模式一结束声音立刻恢复，脚印与水花不受影响。

**绑定武器**

- 只有你指定的那一把左轮能触发模式——在设置里四选一（左轮手枪、猎人的左轮手枪、护林人的左轮手枪、典狱长的左轮手枪）；带别的枪按了没反应。
- 背包里没有这把左轮、或弹巢是空的，按键只会给出一行屏幕提示，不会进入模式。

**拔枪动作**

- 左轮的每一次拔枪都播幅度大的那个「装弹后」动作（从右下方横扫入画），空弹巢也一样——原版空枪是从屏幕下方小幅抬起。四把左轮全部生效，任何拔枪途径都算（包括进入模式时的自动拔枪）。纯外观：弹巢里装了什么、能不能打响、怎么装弹全都不变，拔枪动作播完就把真实值还回动画参数。设置里可以关掉。

### 前置条件

- 《The Long Dark》**2.55**（本 mod 针对 2.55 编译）
- [MelonLoader](https://melonwiki.xyz/) **0.7.3** 或更高（实测环境 0.7.3）
- **ModSettings**（必需，设置页依赖它）

### 安装

1. 确认游戏目录下已有 `MelonLoader/` 文件夹（没有就先装 MelonLoader 并启动一次游戏）
2. 把 `TLD_DeadEye.dll` 复制到 `TheLongDark/Mods/`，并把 `Asset` 文件夹（三个 `.wav` 音效）放到 `TheLongDark/Mods/DeadEye/Asset`
3. 重启游戏

### 使用

1. 携带设置里选定的那把左轮（放背包里也行）。
2. 按鼠标后侧键——时间放慢；左轮若还在背包里会当场拔出来；画面变黄。前 0.7 秒是 0.1 倍，之后世界固定降到 0.01 倍，剩下的抬枪动作在极慢的流速里贯穿整个标记过程。
3. 准星对准动物按鼠标中键——出现红色标记，可一直标到上限。标满上限的瞬间自动进入处决（设置里可关）。
4. 再按一次鼠标后侧键——结束标记、进入处决；黄滤镜和慢动作持续到处决结束，人物动作保持正常速率。若没有标记，则立刻结束死神之眼。改按取消键（Q）则直接放弃：所有标记作废，不开火退出。

设置入口：**选项 → Mod Settings → Dead Eye (死神之眼)**。

### 设置

| 项 | 说明 | 默认 |
|---|---|---|
| 绑定的左轮手枪 | 选择哪把左轮能触发模式；其余三把不会被它响应 | 左轮手枪 |
| 拔枪动作替换 | 左轮的每一次拔枪都用幅度大的「装弹后」动作，空弹巢也一样。纯外观，弹巢内容、射击与装弹逻辑都不受影响 | 是 |
| 子弹追踪 | 处决射出的子弹会一路修正方向追着标记目标飞（需要 AdaptiveArsenal；原版弹道在出膛瞬间结算）。松鸡和兔子会被瞄准身体内部（深度已内置在发布版里），标哪个部位都一样 | 开 |
| 进入 / 退出死神之眼 | 开启和结束模式的键；点输入框再按任意键或鼠标键可重绑 | 鼠标后侧键 |
| 标记目标 | 把准星下的目标标记下来的键；重绑方式相同 | 鼠标中键 |
| 取消标记模式 | 标记模式下按下直接取消：放弃全部已打的标记、不开火退出（有标记时按切换键是进入处决，按这个键是放弃） | Q |
| 标记满自动开火 | 标记个数到达上限（等于左轮剩余子弹数）的瞬间自动进入处决开火，不用再按切换键。关掉则保持原行为：仍需手动按切换键开始射击 | 开 |
| 疲劳每秒消耗 | 标记期间每秒（真实时间）消耗疲劳上限的百分比；调大付出更多 | 1 |
| 黄色滤镜 | 标记期间全屏黄色滤镜；关掉则保持原色 | 开 |
| 滤镜不透明度 | 黄色滤镜浓度；0 等于没有滤镜 | 0.22 |
| 刮屏白带宽度 | 启动死神之眼时扫过的白色光斑宽度（占屏幕宽度的比例） | 0.30 |
| 刮屏扫过时长 | 白色光斑扫过全屏所花的时间（秒），越小越快 | 0.6 |
| 音效音量 | 进入音效与标记音效的音量；0 = 静音 | 0.8 |
| 输出详细日志 | 把模式切换、标记、开火记录写进 `MelonLoader/Latest.log` | 关 |

### 调参（仅调试版）

下面 9 个调参值是**直接编译进发布版的**，故意不放进设置页——发布出去的 dll 永远按这份调校在跑：

| 项 | 发布版取值 | 作用 |
|---|---|---|
| 追踪强度 | 0.5 | 飞行中的子弹咬住标记的力度 |
| 小体型瞄准点 | 0.7 | 打到松鸡 / 兔子体内多深（0 = 脚下，1 = 身体胶囊中心） |
| 近失补偿半径 | 0.6 米 | 弹道擦到伤害碰撞体这个距离内即算命中 |
| 头部标记判定半径 | 0.35 米 | 准星离大型动物头部多近，这次标记才算头部标记 |
| 标记回退半径 | 1.2 米 | 准星射线擦过动物身体多远之内仍算「瞄着它」——专门用来救仰头嚎叫的狼（头部伸出碰撞胶囊之外）这类情况；0 = 关闭回退 |
| 处决时间流速 | 0.2 | 处决期间的世界流速 |
| 处决射速倍率 | 1.5 倍 | 处决序列比平时射击快多少 |
| 进入渐变速度 | 5 | 进入时时间放慢的快慢 |
| 退出渐变速度 | 8 | 退出时时间恢复的快慢 |

同版本的 **DEBUG** 构建会把它们全部作为滑条放进「弹道 / 子弹时间」分区，这是唯一能在游戏里重新调这些值的方式。启动日志会写明构建类型——`Dead Eye (死神之眼) 0.7.12 loaded (release build)`——一眼就能分辨部署的是哪个构建。调试版的调整会存进 `Mods/TLD_DeadEye.json`，但发布版会忽略它。

调试版还会在屏幕下方打一行状态文字（标记计数、每次标记被拒的原因）；发布版只画标记本身的红色叉。两个构建都保留的唯一例外是**进入之前**的拒绝提示——太疲惫、没带绑定的左轮、弹巢没子弹——这类提示照常显示。

### 兼容性

- 只在读档后、游戏 HUD 可用时响应；在菜单里按键无效果，也不会留下任何时间流速残留。
- 不修改物品定义、武器数值与存档文件。模式开启期间由它独占时间流速：其它同样接管游戏速度的 mod（例如每帧重置流速的 TimeAccelerator）会在死神之眼结束后自动接管回来，因为本 mod 的重申发生在它们之后。
- 音效是 `Mods/DeadEye/Asset` 下的三个普通 `.wav` 文件，通过 Windows 音频接口（`winmm!waveOut`）播放——每个音效独占一个播放通道，标记音、启动音与循环背景音可以同时响、互不打断；循环在设备侧（`WHDR_LOOP`）自动重复，两遍之间没有空隙。不触碰游戏音频库（Wwise），也不创建任何 Unity 音频组件。
- **与 AdaptiveArsenal 兼容**：自动探测有无该 mod，两种弹道下射击都落在标记点上。

### 常见问题

**按了没反应？**  
先确认绑定的左轮在身上、弹巢里有子弹，再确认没有别的 mod 正在接管游戏速度。

**标记没有被全部射出？**  
轮到它之前动物已经死亡的标记会被跳过；子弹落在标记的身体点上，标在动物身上的标记会跟着动物走。

**准星明明在动物身上却没标记上？**  
有些姿势会让可见的毛皮伸出碰撞胶囊之外——比如狼仰头嚎叫时，头部整块在身体包围盒上方。这种情况会改为标记「准星射线 1.2 米内最近的活物」，不再直接放弃这次点击；只有附近什么都没有时才会失败。

### 卸载

删掉 `TheLongDark/Mods/TLD_DeadEye.dll` 即可（不再需要音效的话可一并删除 `TheLongDark/Mods/DeadEye` 文件夹）。

存档里不会留下本 mod 的任何字段。如果在模式进行中删除，下次读档或重启游戏后时间流速自动恢复正常。

更新记录见 [ChangeLog.md](ChangeLog.md)。

### 许可证

GPL-3.0 —— 见 [LICENSE](LICENSE)。

可以自由使用、修改和再分发 Dead Eye，包括把未修改的 dll 原样打包进整合包。但如果分发的是修改版，该版本必须以 GPL-3.0 授权并提供源码。许可证变更之前发布的版本（0.7.53 及更早）是 MIT，那份授权不会被收回。
