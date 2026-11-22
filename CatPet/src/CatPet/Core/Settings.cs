using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CatPet.Core;

/// <summary>
/// 用户设置（目前只有窗口缩放），存在 %LOCALAPPDATA%\CatPet\settings.json。
/// 读失败一律回退默认值，不让配置文件影响启动。
/// </summary>
public sealed class Settings
{
    /// <summary>窗口缩放：500 逻辑像素 × Zoom = 窗口边长。</summary>
    public double Zoom { get; set; } = 0.452;

    // ---------------- 番茄钟 ----------------
    /// <summary>专注时长（分钟）。</summary>
    public int PomodoroWorkMinutes { get; set; } = 25;
    /// <summary>休息时长（分钟）。</summary>
    public int PomodoroBreakMinutes { get; set; } = 5;
    /// <summary>专注结束后自动开始休息。</summary>
    public bool PomodoroAutoBreak { get; set; } = true;

    // ---------------- 喝水 / 久坐提醒 ----------------
    /// <summary>提醒间隔（分钟）。</summary>
    public int WaterRemindMinutes { get; set; } = 45;
    /// <summary>是否开启。</summary>
    public bool WaterRemindEnabled { get; set; } = false;

    /// <summary>头部跟随鼠标：true = 常开（强制跟随），false = 只在动作区间内跟随（默认）。</summary>
    /// <summary>气泡字号（像素，不随窗口缩放）。</summary>
    public double TextSize { get; set; } = 14;

    /// <summary>上次关闭时的窗口位置（null = 没记录过）。</summary>
    public double? LastLeft { get; set; }
    public double? LastTop { get; set; }

    public bool HeadFollow { get; set; } = false;

    /// <summary>窗口是否总在最前。</summary>
    public bool Topmost { get; set; } = true;

    /// <summary>动作频率：决定走动/跑/飞奔这些大幅度动作的频率。0=低，1=中（默认），2=高。</summary>
    /// <remarks>JSON 里仍叫 WalkFrequency，改名前存下的配置能继续用。</remarks>
    [JsonPropertyName("WalkFrequency")]
    public int ActionFrequency { get; set; } = 1;

    /// <summary>播放站立动作时的窗口放大倍数（站立体态偏瘦小，放大一点才和坐姿接近）。</summary>
    public double StandZoom { get; set; } = 1.0;

    /// <summary>是否已经看过「追鼠标」小游戏的玩法说明（只提示一次）。</summary>
    public bool ChaseIntroSeen { get; set; }

    /// <summary>还剩几次「托盘里有提醒功能」的提示（用过计时后归零）。</summary>
    public int HintRemaining { get; set; } = 3;

    /// <summary>默认窗口缩放（重置设置时也回到这个值）。</summary>
    public const double DefaultZoom = 0.452;

    public const double MinZoom = 0.25;
    public const double MaxZoom = 2.0;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CatPet", "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var s = JsonSerializer.Deserialize<Settings>(json);
                if (s is not null)
                {
                    s.Zoom = Math.Clamp(s.Zoom, MinZoom, MaxZoom);
                    return s;
                }
            }
        }
        catch
        {
            // 配置损坏就当没有，用默认值
        }
        return new Settings();
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(FilePath)!;
            Directory.CreateDirectory(dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // 存不下来也不影响使用
        }
    }
}
