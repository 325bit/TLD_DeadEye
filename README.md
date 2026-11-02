# Dead Eye (死神之眼)

[English](#english) · [中文](#中文)

![version](https://img.shields.io/badge/version-0.7.10-informational)

![game](https://img.shields.io/badge/The%20Long%20Dark-2.55-success)

![loader](https://img.shields.io/badge/MelonLoader-0.7.3-blueviolet)

![license](https://img.shields.io/badge/license-MIT-blue)

---

## English

A The Long Dark mod that slows the world down, lets you mark animals like RDR2's Dead Eye, and then chain-fires every mark with your revolver.

### Features

**Bullet time**

- Press the side mouse button while carrying a revolver — the revolver is drawn if it was stowed (the raise plays out in slow motion), time slows to a tenth speed for 0.7 s (the draw reveal), the screen tints yellow and the signature activation sound plays, like Dead Eye in Red Dead Redemption 2. When the reveal ends the world drops to the fixed deeper marking speed (0.01x), so the rest of the raise crawls through the whole marking phase.
- Press the middle mouse button while looking at a live animal — a red mark appears over it and stays attached to the animal even while it moves, with a short sound confirming the mark.
- You can mark the same animal several times or several animals in any order — the number of marks never exceeds the rounds left in the revolver's cylinder.

**The execution**

- Press the side mouse button again — time returns to normal, the mod draws your revolver if it was not in hand, and the view swings onto the first mark through the game's own cinematic look-at system. Once the crosshair has swung onto it the sequence opens fire at the weapon's own full rate, one shot per mark, while the view keeps sweeping smoothly from target to target — the swing is paced to the weapon's fire rate, arriving at each mark just as its shot is due.
- Each projectile is redirected the instant it spawns onto a lead-predicted intercept point — where the animal will be when the shot arrives, computed from its tracked movement and the revolver's launch speed (gravity included) — so head marks hit heads and body marks hit bodies even on running targets. The game's own damage, bleed-out, critical-hit and skill-progress rules all apply unchanged. With AdaptiveArsenal installed the bullet also keeps steering onto the animal during flight — toward where it will BE, not where it was — and cancels its own muzzle error on the first frame, so a slightly-off lead still lands. Grouse and rabbits are the exception: whatever part you mark, the mark is taken on the animal's damageable body (its body capsule, which is where the shot converges) — a body that small dies to any hit, and an aim point sitting on the visible surface puts half of any error outside the animal. Because those collision capsules are several times larger than the models, a mark is refused when the crosshair is not really on the body — a tolerance scaled to the animal's size, not a fixed distance — instead of being planted a body-length above the bird. How deep inside is the 小体型瞄准点 slider. And the slow motion does not end at the last trigger pull: the execution holds until the LAST bullet is consumed — by a hit, by scenery, or by its own expiry — then time restores.
- Fatigue drains a configurable percentage of the full bar per real-time second (default 1%, ~100 s from full), and returns to normal the moment it ends — if you run out of stamina mid-mode, it shuts off by itself, and a fully exhausted survivor cannot enter the mode at all.

**Bound weapon**

- Only one revolver triggers the mode — you pick which one (左轮手枪 / 猎人的左轮手枪 / 护林人的左轮手枪 / 典狱长的左轮手枪, matching the in-game item names) in the settings; carrying any other gun does nothing.
- With no revolver in your pack, or an empty cylinder, pressing the key does nothing except show a short on-screen reason.

### Requirements

- The Long Dark **2.55** (built against 2.55)
- [MelonLoader](https://melonwiki.xyz/) **0.7.3** or newer (tested on 0.7.3)
- **ModSettings** (required; the settings page depends on it)

### Installation

1. Make sure `MelonLoader/` exists in the game folder (install it and run the game once if not).
2. Put `TLD_DeadEye.dll` into `TheLongDark/Mods`, and the `Asset` folder (the two `.wav` sound files) into `TheLongDark/Mods/DeadEye/Asset`.
3. Start the game.

### Usage

1. Carry the revolver selected in the settings (it can stay in your pack).
2. Press the side mouse button — time slows, the revolver is drawn if it was stowed, the screen turns yellow, and "死神之眼 · 标记目标" appears. The draw speed (0.1x) only lasts the first 0.7 s; the world then drops to the fixed marking speed (0.01x) so the rest of the raise crawls through the whole marking phase.
3. Aim at an animal and press the middle mouse button — a red mark appears; repeat up to the mark cap.
4. Press the side mouse button again — the marking phase ends and every mark is shot in order. The yellow tint and the slowdown carry through the whole execution, while your own actions (draw, fire rate) run at normal speed. If nothing was marked, the same press ends the mode immediately.

Settings live under **Options → Mod Settings → Dead Eye (死神之眼)**.

### Settings

| Setting | Effect | Default |
|---|---|---|
| 绑定的左轮手枪 | Picks which revolver triggers the mode; the other three are ignored by it | 左轮手枪 |
| 子弹追踪 | The execution's bullets keep steering toward the marked animal while in flight (needs AdaptiveArsenal; the vanilla projectile resolves its hit at spawn). Grouse and rabbits are aimed inside the body (depth set by 小体型瞄准点), so the marked part does not matter | On |
| 追踪强度 | How hard the tracking pulls: 1 is standard; lower flies straighter (0.05 barely curves at all), higher turns harder and holds fast crossing targets | 1 |
| 小体型瞄准点 | Where the shot goes on grouse and rabbits: 0 = the animal's root (its feet), 1 = the centre of its body capsule, in between = part of the way up. The mark already sits on the body centre, so 1.0 aims dead centre and lower values pull the round toward the feet. Applies immediately, no re-marking | 0.6 |
| 近失补偿半径 | A shot that passes within this many metres of the marked animal's damage colliders resolves as a hit (0 = exact contact only). Compensates the real bullet diameter and aim tolerance — the round only needs to pass close, not dead-centre. The honest range is 0.1-0.2; above ~0.3 it starts hitting air, and the slider goes to 0.5 only for testing extremes. Applies immediately | 0.1 |
| 进入 / 退出死神之眼 | Key that starts and ends the mode; click the box and press any key or mouse button to rebind | 鼠标后侧键 |
| 标记目标 | Key that marks what is under the crosshair; rebindable the same way | 鼠标中键 |
| 处决时间流速 | World speed during the firing phase (the yellow tint persists, your own fire rate stays normal). Very low values also stretch the game's own weapon state machine, so keep it at 0.4 or above; 1 = no slowdown | 0.8 |
| 处决射速倍率 | How much faster the execution fires compared to normal shooting: 1 = normal pace (~0.5 s per shot), 2 = ~0.25 s per shot — the closer to RDR2-style Dead Eye rapid fire. Affects the execution sequence only, never normal shooting or reloading | 2 |
| 进入渐变速度 | How fast time slows when entering; raise for a snappier start | 1 |
| 退出渐变速度 | How fast time returns to normal when leaving; raise for a snappier exit | 3 |
| 疲劳每秒消耗 | Fatigue drain while marking, as a percentage of the full bar per real-time second; raise it to pay more | 1 |
| 黄色滤镜 | Fullscreen yellow tint while marking. **Off** leaves the normal colours | On |
| 滤镜不透明度 | Strength of the yellow tint; 0 equals no tint | 0.22 |
| 音效音量 | Volume of the activation and mark sounds; 0 is silent | 0.8 |
| 输出详细日志 | Writes mode transitions, marks and shots to `MelonLoader/Latest.log` | Off |

### Tuning

The defaults aim for an RDR2 feel: a quick 0.1x draw reveal, then a near-frozen 0.01x marking phase. The remaining knobs are a handful — changes apply on the next activation:

- **Fires too early / too late after exiting**: the sequence waits for the revolver's own firing rate; the "退出渐变速度" only affects the time restore, not the shots.

### Compatibility

- Responds only while a save is loaded and the gameplay HUD is up; in menus the keys have no effect and no time-scale change is ever left behind.
- Does not modify item definitions, weapon stats or save files. While the mode is on, Dead Eye owns the time-scale value: other mods that also drive the game speed (for example TimeAccelerator, which resets the scale every frame from an interface-update prefix) take over again the moment Dead Eye ends, because this mod's re-assertion runs after theirs.
- The sounds are two plain `.wav` files under `Mods/DeadEye/Asset`, played through the Windows audio interface (`winmm!waveOut`) — each sound owns its own device handle, so the mark chime and the activation sound can overlap freely. No game audio banks (Wwise) are touched and no Unity audio components are created.
- **AdaptiveArsenal compatible**: detected automatically (with or without it, shots land on the marked point).

### FAQ

**Nothing happens when I press the key?**  
First check that the bound revolver is in your pack and its cylinder is not empty, then check that the mode is not disabled by another mod overriding the game speed.

**The marks do not all fire?**  
A mark on an animal that dies before the sequence reaches it is skipped; shots land at the marked body point, so marks placed on a moving animal's old position follow the animal itself.

### Uninstall

Delete `TheLongDark/Mods/TLD_DeadEye.dll` (and the `TheLongDark/Mods/DeadEye` folder if you no longer need the sound files).

The save keeps no fields from this mod. If you delete it mid-mode, the next scene load or game restart returns the time scale to normal.

See [ChangeLog.md](ChangeLog.md) for version history.

### License

MIT — see [LICENSE](LICENSE).

---

## 中文

一个把世界放慢、让你像《荒野大镖客2》死神之眼那样标记猎物、再用左轮逐个处决的《漫漫长夜》模组。

### 功能

**子弹时间**

- 携带左轮时按下鼠标后侧键——左轮若还在背包里会当场拔出，世界以十分之一速播 0.7 秒（拔枪亮相），画面染黄，标志性启动音效响起，就是大镖客里死神之眼的味道；0.7 秒后世界固定降到 0.01 倍——剩下的抬枪动作会在极慢的流速里一路贯穿整个标记过程。
- 准星对着活物按下鼠标中键——它身上出现一个红色标记并跟着动物移动，同时一声短音效确认标记成功。
- 同一只动物可以标多次，也可以标多只——标记总数永远不会超过左轮弹巢里剩下的子弹数。

**处决**

- 再按一次鼠标后侧键——**结束标记环节，直接进入处决**：黄色滤镜与时间放慢一直持续到处决结束（若一个都没标，同一按键则立刻结束死神之眼）。左轮在进入标记时就已经拔出。视角通过游戏自带的过场演出视角系统先甩向第一个标记；准星到位后立刻以武器原生满射速逐个开火，视角在目标之间如「甩枪」般连续平滑掠过——甩动节奏与射速匹配。**放慢只作用于世界：人物自身的拔枪、开火动作与射速都以正常速率进行。**
- 每发弹丸在出膛瞬间被重定向到**提前量拦截点**——根据动物当前移动速度与左轮出膛速度（含重力下坠）计算「子弹到达时它会在哪」——所以奔跑中的目标也能标头打头、标身打身。游戏自己的伤害、失血、要害判定和技能经验全部原样生效；装了自适应兵工厂时，子弹还会在飞行途中持续修正方向追着它走——追的是「它将要到的地方」而不是「它刚才在的地方」，并且出膛第一帧就抹掉枪口误差——提前量稍有偏差也能命中。松鸡和兔子是例外：无论你标的是哪个部位，标记都取在它的**可伤害身体**上（子弹朝那个胶囊收束）——这么小的目标挨一发就死，而瞄准点如果贴在它可见的表面上，任何偏差都有一半落在身体之外。由于这些碰撞胶囊比模型大好几倍，准星没有真正落在身体上时会**拒绝标记**（容差按动物体型缩放，不是固定距离），而不是把标记点飘到鸟的上方半米处。收进多少由「小体型瞄准点」滑条决定。此外，子弹时间不会在最后一枪扣下时就结束——处决效果会一直维持到最后一发子弹消失（命中目标、打在环境上、或自行飞完超时），之后时间才恢复正常。
- 标记期间疲劳按真实时间每秒消耗总量的一定百分比（默认 1%，满疲劳约 100 秒耗空），结束立刻恢复——中途力竭会自动收招，完全力竭时根本进不了模式。

**绑定武器**

- 只有你指定的那一把左轮能触发模式——在设置里四选一（左轮手枪、猎人的左轮手枪、护林人的左轮手枪、典狱长的左轮手枪）；带别的枪按了没反应。
- 背包里没有这把左轮、或弹巢是空的，按键只会给出一行屏幕提示，不会进入模式。

### 前置条件

- 《The Long Dark》**2.55**（本 mod 针对 2.55 编译）
- [MelonLoader](https://melonwiki.xyz/) **0.7.3** 或更高（实测环境 0.7.3）
- **ModSettings**（必需，设置页依赖它）

### 安装

1. 确认游戏目录下已有 `MelonLoader/` 文件夹（没有就先装 MelonLoader 并启动一次游戏）
2. 把 `TLD_DeadEye.dll` 复制到 `TheLongDark/Mods/`，并把 `Asset` 文件夹（两个 `.wav` 音效）放到 `TheLongDark/Mods/DeadEye/Asset`
3. 重启游戏

### 使用

1. 携带设置里选定的那把左轮（放背包里也行）。
2. 按鼠标后侧键——时间放慢；左轮若还在背包里会当场拔出来；画面变黄，屏幕出现「死神之眼 · 标记目标」。前 0.7 秒是 0.1 倍，之后世界固定降到 0.01 倍，剩下的抬枪动作在极慢的流速里贯穿整个标记过程。
3. 准星对准动物按鼠标中键——出现红色标记，可一直标到上限。
4. 再按一次鼠标后侧键——结束标记、进入处决；黄滤镜和慢动作持续到处决结束，人物动作保持正常速率。若没有标记，则立刻结束死神之眼。

设置入口：**选项 → Mod Settings → Dead Eye (死神之眼)**。

### 设置

| 项 | 说明 | 默认 |
|---|---|---|
| 绑定的左轮手枪 | 选择哪把左轮能触发模式；其余三把不会被它响应 | 左轮手枪 |
| 子弹追踪 | 处决射出的子弹会一路修正方向追着标记目标飞（需要 AdaptiveArsenal；原版弹道在出膛瞬间结算）。松鸡和兔子会被瞄准身体内部（深度由「小体型瞄准点」决定），标哪个部位都一样 | 开 |
| 追踪强度 | 追踪力度：1 为标准；越低弹道越直（0.05 几乎不打弯），越高转得越急、越能咬住高速横移的目标 | 1 |
| 小体型瞄准点 | 松鸡、兔子这类小目标朝哪里打：0 = 动物根部（脚下），1 = 它身体胶囊的中心，中间值 = 两者之间。标记本身已经落在身体中心上，所以 1.0 = 正中心，调小则更贴脚下；立刻生效，不用重新标记 | 0.6 |
| 近失补偿半径 | 弹道从标记猎物的伤害碰撞体该距离（米）以内擦过时，按命中结算（0 = 仅精确接触才算）。补偿真实弹头直径与瞄准容错——只要求贴近，不要求正中。推荐区间 0.1-0.2；0.3 以上明显在打空气，滑条放到 0.5 仅供测试极端值。立刻生效 | 0.1 |
| 进入 / 退出死神之眼 | 开启和结束模式的键；点输入框再按任意键或鼠标键可重绑 | 鼠标后侧键 |
| 标记目标 | 把准星下的目标标记下来的键；重绑方式相同 | 鼠标中键 |
| 处决时间流速 | 处决期间的世界流速（黄滤镜延续，人物开火保持正常速率）。过低的值会连带拖慢游戏自身的持枪状态机，建议 ≥0.4；1 = 不放慢 | 0.8 |
| 处决射速倍率 | 处决序列相对平时射击的加速倍率：1 = 平时节奏（约 0.5 秒一发），2 = 约 0.25 秒一发——越接近《荒野大镖客2》的速射感。只影响处决序列，不影响平时射击与换弹 | 2 |
| 进入渐变速度 | 进入时时间放慢的快慢；调大更干脆 | 1 |
| 退出渐变速度 | 退出时时间恢复的快慢；调大更干脆 | 3 |
| 疲劳每秒消耗 | 标记期间每秒（真实时间）消耗疲劳上限的百分比；调大付出更多 | 1 |
| 黄色滤镜 | 标记期间全屏黄色滤镜；关掉则保持原色 | 开 |
| 滤镜不透明度 | 黄色滤镜浓度；0 等于没有滤镜 | 0.22 |
| 音效音量 | 进入音效与标记音效的音量；0 = 静音 | 0.8 |
| 输出详细日志 | 把模式切换、标记、开火记录写进 `MelonLoader/Latest.log` | 关 |

### 按症状调参

默认按大镖客观感取：0.1 倍速的拔枪亮相，随后是近乎凝固的 0.01 倍标记阶段。可调的只剩下面这些，下次进入模式生效：

- **处决节奏不对**：射击间隔由左轮自身的射速决定（慢动作下人物以正常速率开枪，射速不变）；「退出渐变速度」只影响处决结束后的时间恢复。

### 兼容性

- 只在读档后、游戏 HUD 可用时响应；在菜单里按键无效果，也不会留下任何时间流速残留。
- 不修改物品定义、武器数值与存档文件。模式开启期间由它独占时间流速：其它同样接管游戏速度的 mod（例如每帧重置流速的 TimeAccelerator）会在死神之眼结束后自动接管回来，因为本 mod 的重申发生在它们之后。
- 音效是 `Mods/DeadEye/Asset` 下的两个普通 `.wav` 文件，通过 Windows 音频接口（`winmm!waveOut`）播放——每个音效独占一个播放通道，标记音与启动音可以同时响、互不打断；不触碰游戏音频库（Wwise），也不创建任何 Unity 音频组件。
- **与 AdaptiveArsenal 兼容**：自动探测有无该 mod，两种弹道下射击都落在标记点上。

### 常见问题

**按了没反应？**  
先确认绑定的左轮在身上、弹巢里有子弹，再确认没有别的 mod 正在接管游戏速度。

**标记没有被全部射出？**  
轮到它之前动物已经死亡的标记会被跳过；子弹落在标记的身体点上，标在动物身上的标记会跟着动物走。

### 卸载

删掉 `TheLongDark/Mods/TLD_DeadEye.dll` 即可（不再需要音效的话可一并删除 `TheLongDark/Mods/DeadEye` 文件夹）。

存档里不会留下本 mod 的任何字段。如果在模式进行中删除，下次读档或重启游戏后时间流速自动恢复正常。

更新记录见 [ChangeLog.md](ChangeLog.md)。

### 许可证

MIT —— 见 [LICENSE](LICENSE)。
