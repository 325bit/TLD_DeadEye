using ModSettings;
using UnityEngine;

namespace DeadEye;

/// <summary>
/// Settings panel (Options > Mod Settings > Dead Eye). Persisted to Mods\TLD_DeadEye.json.
/// Name/Description strings are in-game copy (Chinese by design).
/// The tuning knobs (tracking, near-miss radius, small-prey aim depth, execution pace, lerp
/// speeds) are DEBUG-build only: the RELEASE build declares them internal, so ModSettings
/// never lists them and the values below are compiled in as constants. ModSettings reflects
/// over PUBLIC instance fields only, which is what makes the split work (see DEVELOPMENT §8).
/// </summary>
internal class Settings : JsonModSettings
{
    [Section("武器绑定")]

    [Name("绑定的左轮手枪")]
    [Description("只有这把左轮能触发死神之眼。进入标记模式前会检查背包与手上是否携带它，标记次数上限等于它的剩余装弹数。")]
    [Choice("左轮手枪", "猎人的左轮手枪", "护林人的左轮手枪", "典狱长的左轮手枪")]
    public int BoundRevolver = 0;

    [Name("拔枪动作替换")]
    [Description("开启后左轮的每一次拔枪都用「装弹后」那套幅度更大的动作（从右下方横扫入画），空弹巢也一样；原版空枪是从屏幕下方小幅抬起。纯外观：弹巢里的真实子弹数、射击与装弹逻辑都不受影响。")]
    public bool ReplaceDrawAnimation = true;

#if DEBUG
    [Section("弹道")]

    [Name("子弹追踪")]
    [Description("处决时射出的子弹会一路修正方向、追着标记目标飞（需要安装 AdaptiveArsenal；原版弹道在出膛瞬间结算，无法追踪）。关掉则退回「出膛即锁定提前量」的旧行为。")]
    public bool HomingBullets = true;
    [Name("追踪强度")]
    [Description("子弹飞行中转向追踪标记的力度。0.5 = 当前调校值；0.2 几乎不打弯，3 = 转得最急、最能咬住高速横移的目标。")]
    [Slider(0.2f, 3f)]
    public float HomingStrength = 0.5f;

    [Name("小体型瞄准点")]
    [Description("松鸡、兔子这类小目标朝哪里打：0 = 动物根部（脚下），1 = 它身体胶囊的中心，中间值 = 两者之间。标记已经落在身体中心上了，所以 1.0 = 正中心，调小则更贴脚下（用来抵消飞行中的下坠感）。调整后立刻生效，不用重新标记。")]
    [Slider(0f, 1f)]
    public float PreyAimFraction = 0.7f;

    [Name("近失补偿半径")]
    [Description("弹道从标记猎物的伤害碰撞体该距离（米）以内擦过时，按命中结算（0 = 仅精确接触才算）。补偿真实弹头直径与瞄准容错——只要求贴近，不要求正中。0.1-0.2 偏保守，0.5 = 当前调校值；越大越容易把擦身而过算成命中。立刻生效。")]
    [Slider(0f, 0.5f)]
    public float NearMissRadius = 0.5f;
#else
    internal bool HomingBullets = true;
    internal float HomingStrength = 0.5f;
    internal float PreyAimFraction = 0.7f;
    internal float NearMissRadius = 0.5f;
#endif

    internal const float DefaultNearMissRadius = 0.5f;

    [Section("按键")]

    [Name("进入 / 退出死神之眼")]
    [Description("默认鼠标后侧键。点一下输入框再按想要的键即可重新绑定。")]
    public KeyCode ToggleKey = KeyCode.Mouse3;

    [Name("标记目标")]
    [Description("默认鼠标中键。在子弹时间里瞄准一只活物并按下，即可把它标记为射击目标，可重复标记同一目标或标记多个目标。")]
    public KeyCode MarkKey = KeyCode.Mouse2;

    [Name("取消标记模式")]
    [Description("默认 Q 键。标记模式下按下会立刻放弃所有已打的标记并退出死神之眼，不会开火（切换键在有标记时是进入处决，这个键是直接取消）。")]
    public KeyCode CancelKey = KeyCode.Q;

    [Section("处决")]

    [Name("标记满自动开火")]
    [Description("标记个数到达上限（等于左轮剩余子弹数）的瞬间自动进入处决开火，不用再按一次切换键。关掉则保持原行为：上限后仍需手动按切换键开始射击。")]
    public bool AutoFireAtCap = true;

#if DEBUG
    [Section("子弹时间")]

    [Name("处决时间流速")]
    [Description("按切换键进入处决环节后的时间流速，独立于标记阶段的流速。0.2 = 当前调校值（世界很慢、黄滤镜延续，人物开火保持正常速率）；1 = 不放慢。")]
    [Slider(0.1f, 1f)]
    public float ExecutionTimeScale = 0.2f;

    [Name("处决射速倍率")]
    [Description("处决序列相对平时射击的加速倍率：1 = 平时节奏（约 0.5 秒一发），1.5 = 当前调校值，4 = 约 0.12 秒一发——越接近《荒野大镖客2》的速射感。只影响处决序列，不影响平时射击与换弹。")]
    [Slider(1f, 4f)]
    public float FireRateMultiplier = 1.5f;

    [Name("进入渐变速度")]
    [Description("进入标记模式时时间流速渐变到目标值的快慢，越大越快。1.5 = 当前调校值。")]
    [Slider(0.5f, 5f)]
    public float EnterLerpSpeed = 1.5f;

    [Name("退出渐变速度")]
    [Description("退出标记模式时时间流速恢复正常的快慢，越大越快。8 = 当前调校值。")]
    [Slider(0.5f, 8f)]
    public float ExitLerpSpeed = 8f;
#else
    internal float ExecutionTimeScale = 0.2f;
    internal float FireRateMultiplier = 1.5f;
    internal float EnterLerpSpeed = 1.5f;
    internal float ExitLerpSpeed = 8f;
#endif

    [Section("体力消耗")]

    [Name("疲劳每秒消耗")]
    [Description("标记模式期间每秒消耗疲劳上限的百分比（按真实时间计），1 = 每秒烧掉总量的 1%，满疲劳约 100 秒耗空。疲劳耗尽时模式自动退出；完全力竭时无法进入死神之眼。")]
    [Slider(0.0f, 10f)]
    public float FatiguePerSecond = 1f;

    [Section("视觉")]

    [Name("黄色滤镜")]
    [Description("标记模式期间画面叠加一层黄色滤镜，模拟《荒野大镖客2》的死神之眼观感。")]
    public bool YellowOverlay = true;

    [Name("滤镜不透明度")]
    [Description("黄色滤镜的浓度，0 = 全透明（等于关闭滤镜）。")]
    [Slider(0f, 0.6f)]
    public float OverlayOpacity = 0.22f;

    [Section("音效")]

    [Name("音效音量")]
    [Description("进入音效与标记音效的音量，0 = 静音。")]
    [Slider(0f, 1f)]
    public float SoundVolume = 0.8f;

    [Section("调试")]

    [Name("输出详细日志")]
    [Description("把进入 / 标记 / 开火序列等状态写进 MelonLoader 日志，排查问题时打开。")]
    public bool Verbose = false;
}
