using ModSettings;
using UnityEngine;

namespace DeadEye;

/// <summary>
/// Settings panel (Options > Mod Settings > Dead Eye). Persisted to Mods\TLD_DeadEye.json.
/// Name/Description strings are in-game copy (Chinese by design).
/// </summary>
internal class Settings : JsonModSettings
{
    [Section("武器绑定")]

    [Name("绑定的左轮手枪")]
    [Description("只有这把左轮能触发死神之眼。进入标记模式前会检查背包与手上是否携带它，标记次数上限等于它的剩余装弹数。")]
    [Choice("左轮手枪", "猎人的左轮手枪", "护林人的左轮手枪", "典狱长的左轮手枪")]
    public int BoundRevolver = 0;

    [Section("按键")]

    [Name("进入 / 退出死神之眼")]
    [Description("默认鼠标后侧键。点一下输入框再按想要的键即可重新绑定。")]
    public KeyCode ToggleKey = KeyCode.Mouse3;

    [Name("标记目标")]
    [Description("默认鼠标中键。在子弹时间里瞄准一只活物并按下，即可把它标记为射击目标，可重复标记同一目标或标记多个目标。")]
    public KeyCode MarkKey = KeyCode.Mouse2;

    [Section("子弹时间")]

    [Name("时间流速")]
    [Description("进入标记模式后世界的时间流速。0.1 = 十分之一速，越小越慢。")]
    [Slider(0.05f, 0.5f)]
    public float TargetTimeScale = 0.1f;

    [Name("处决时间流速")]
    [Description("按切换键进入处决环节后的时间流速，独立于标记阶段的流速。默认 0.8：世界轻微放慢（黄滤镜延续），人物开火保持正常速率。1 = 不放慢。")]
    [Slider(0.1f, 1f)]
    public float ExecutionTimeScale = 0.8f;

    [Name("进入渐变速度")]
    [Description("进入标记模式时时间流速渐变到目标值的快慢，越大越快。")]
    [Slider(0.5f, 5f)]
    public float EnterLerpSpeed = 1f;

    [Name("退出渐变速度")]
    [Description("退出标记模式时时间流速恢复正常的快慢，越大越快。")]
    [Slider(0.5f, 8f)]
    public float ExitLerpSpeed = 3f;

    [Section("体力消耗")]

    [Name("疲劳每秒消耗")]
    [Description("标记模式期间每秒消耗疲劳上限的百分比（按真实时间计），1 = 每秒烧掉总量的 1%，满疲劳约 100 秒耗空。疲劳耗尽时模式自动退出；完全力竭时无法进入死神之眼。")]
    [Slider(0.5f, 10f)]
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
