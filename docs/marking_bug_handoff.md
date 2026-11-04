# 移交提示词：TLD_DeadEye 标记 bug 修复（近失回退）

> 用途：整段复制到新会话作为任务上下文。  
> 来源：TLD_DeadEye 会话 2026-11-03 的完整取证（全部证据链见工作区  
> `memory/2026-11-03.md` 与 TLD_DeadEye `ChangeLog.md` 0.7.39–0.7.49 条目）。  
> 项目：`D:\code\Csharp\The_Long_dark\TLD_DeadEye\`（当前版本 0.7.50）。

---

## 任务

实现"标记 bug"的修复：准星落在可见动物身上（甚至头里）却无法标记。机制已全部取证完毕、修复方案已与用户商定，只差实现与实机验证。**用户已确认的方向：线性最近距离回退，阈值约 1.2 m。**

## 标记机制现状（0.7.50，未做过任何标记侧改动）

代码位置：

- `Core/DeadEyeManager.cs` → `TryMark()`：两段判定的入口。
- `Core/AnimalBody.cs` → `TryResolveMarkPoint()`（可伤害部位定点）、`CollectFor()`（internal，收集动物碰撞体，优先 transform root 但要求根内恰好 1 个 BaseAi，否则退回 BaseAi 自身子树）、`TryGetBodyBounds()`（internal，身体包围盒）、`IsSmallPrey()`。
- `Core/RoundResolution.cs` → `IsDamageable(Collider)`：层 16/27 + `LocalizedDamage`。
- 关键常量：`AnimalBody.MarkRange = 150f`（射线距离）；准星门容差 = `max(0.30 m, 0.4 × 碰撞体最大边)`。

两段判定（设计原则，不可破坏——**入口放松、定点仍严**）：

1. **第一段（入口）**：屏幕中心 `Physics.RaycastAll(150 m)`，要求射线与动物碰撞体几何精确相交，再解析 `BaseAi`（层 16 直取 / 层 27 父级）。**零角度容差——这就是 bug 所在。**
2. **第二段（准星门）**：只对"射线已碰到该动物"的情况生效——收集可伤害碰撞体，射中取命中点、未中取 `ClosestPoint` 算 `miss`，要求 `miss ≤ 容差`，否则拒绝并提示 `准星偏离活物的可伤害部位（偏 X m，容差 Y m）`。
3. 解析失败（无可伤害碰撞体）时**降级到 raw hit point** 并打 Warning——0.7.9 引入的兜底，必须保留。

## 已实锤的机制（全部有二进制/探针证据，不必重查）

1. **嚎叫姿势的头部是碰撞体盲区（主因）**：狼嚎叫时头颈甩到身体胶囊（约 1.00×1.50×1.00 m，中心高于根部 0.8 m）上方的空档里，可见毛皮网格比碰撞体大。两个独立实测案例：
   - 21:49 会话：射线 150 m **零命中**，锥形扫描显示狼在 28.8 m、仅偏 1.80° = **0.90 m** 线性最近距离——擦着胶囊顶（1.55 m）上方掠过；
   - 21:30 会话：狼 12.5 m、偏 5.25° = **1.14 m**。
   - 对照：原版左轮（AdaptiveArsenal 弹丸）同姿势射击**能命中** `capsule_chest`（12.3 m，`LocalizedDamage` 正常）——伤害胶囊系统本身有效，只有上仰头部区域在胶囊之外。
2. **嚎叫/待机状态下游戏自己禁用全部伤害胶囊**：`capsule_head/chest/waist…` 全部 `damage=True` 但 `enabled=False`（层 27）；狼进入警觉后恢复。因此 `TryResolveMarkPoint` 会报 "nothing damageable resolved"——现有 raw-hit 降级已把这种情况的标记救活（用户看到的是"标记成功"）。
3. **同名远近双实例**：`WILDLIFE_Wolf_grey_N` 在场景中可同时存在两个实例；远景替身身体包围盒为 **0.00×0.00×0.00（无任何碰撞体）**——任何回退搜索必须忽略它们，否则会标记到 200 m 外的幽灵。
4. 玩家感知：准星"明明在头里"却标记失败——因为可感知的嚎叫头部轮廓延伸到约 1.9 m 高，而射线从胶囊顶（1.55 m）上方通过；屏幕上两者只差几个像素。

## 已商定的修复方案（实现这个）

**第一段加"线性最近距离回退"**：当 `RaycastAll` 没有任何命中解析出 `BaseAi`（即现在报 `准星处没有活物（射线没碰到动物）` 的路径）时：

1. 遍历场景内活体 `BaseAi`（`FindObjectsOfType<BaseAi>()` 一次性、或维护缓存；只在标记失败的点击上触发，每帧零开销）。
2. 对每只活体计算**射线到其身体包围盒的线性最近距离**（用 `AnimalBody.TryGetBodyBounds`，注意它只统计 enabled 非 trigger 碰撞体——嚎叫时伤害胶囊被禁用会得到只剩根胶囊的包围盒，这没问题，回退要的只是"玩家在看哪只"）。
3. **忽略身体包围盒为 0.00×0.00×0.00 的远景替身**。
4. 最近距离 ≤ 阈值（建议 1.2 m，两个实测案例 0.90/1.14 m 都覆盖；做成可调参数——参照 `Settings.cs` 里 `NearMissRadius` 的写法：DEBUG 滑条 + `#else` 内部字段 + 单一真值 const，注意 0.7.50 刚统一过这个模式）即选为候选。
5. 候选定案后**走现有的 `TryResolveMarkPoint(ai, hitCollider, ray, …)`** 精确定点 + 现有准星门——需要传一个命中碰撞体，可用该动物根胶囊或 `CollectFor` 结果里任意碰撞体充当 `hitCollider`（它只用于确定收集根；定点逻辑自己会找可伤害部位）。若解析仍失败，沿用现有 raw-hit 降级。
6. 多候选排序：按线性最近距离升序，平手按实际距离。
7. 修复后同样要报现有日志行（`marked …` 格式），并在日志里注明本次来自回退（例如 source 加 `linear-fallback`）。

**不做的事**：

- 不放宽第二段准星门（0.30/0.4×maxdim 容差是 0.7.8–0.7.9 修小猎物时建立的精度资产）；
- 不改伤害胶囊被禁用时的降级行为；
- 不碰 `Patches.cs`（0.7.49 已清空全部探针；`WorldViewContextRestore` 是输入上下文恢复器，与标记无关，别动）。

## 验证标准

1. 嚎叫狼、准星放头部（原失败姿势）→ 能标记（走回退 + raw-hit 或胶囊定点）；
2. 瞄躯干 → 行为与现在完全一致（不走回退，射线正常命中）；
3. 远景替身（200 m+）不会被误标；
4. 鸟群/狼群中不会标记到准星旁的另一只（线性距离阈值内的最近者才有效）；
5. 性能：标记失败的点击才触发遍历，帧率无可感知变化。

## 项目约定（务必遵守）

- 作者署名 **Hemalurgic**（`MelonInfo`/LICENSE/README）；**不要由 AI 执行 git commit**（用户自己提交；写好标题正文即可）；编译 + 部署 dll 到 `F:\GameAbouts\The Long Dark\TheLongDark_2.55` 可以做（csproj PostBuild 自动复制）。
- 文档分层：根 `README.md` 只面向玩家（双语）；字段映射与设计放 `docs/DEVELOPMENT.md`（本次改动需同步 §2 管线描述与 §10 约束表）；版本演进进 `ChangeLog.md`（每版本一条，中文）；源码注释全英文、只写"做什么/为什么/违反会怎样"。
- 反编译/取证统一走 skill `~/.workbuddy/skills/tld-decompilation`（`tld_dump`/`tld_disasm`/`find_callers` 模式扫描）。
- 若需要旧探针参考：`D:\code\WorkBuddy_WorkSpace\The_Long_Dark\analysis\TLD_DeadEye_Patches_probes_backup_0.7.48.cs`（0.7.48 全套探针源码）。

