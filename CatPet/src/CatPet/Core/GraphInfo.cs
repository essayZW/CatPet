using System.IO;
using System.Linq;

namespace CatPet.Core;

/// <summary>动画的大类（对应 VPet 的 GraphType，这里只保留桌宠 MVP 需要的子集）。</summary>
public enum GraphType
{
    Common,
    Default,
    Touch_Head,
    Touch_Body,
    Idel,
    Sleep,
    Say,
    StartUP,
    Shutdown,
    Work,
    Move,
    Raised_Static,
    Raised_Dynamic,
    SideHide_Left_Main,
    SideHide_Right_Main,
    // 本桌宠扩展的两类一次性小动作
    Yawn,
    Tilt,
    Lick,
    /// <summary>正脸待机（整体绕 Z 转向镜头）。</summary>
    Idle_Front,
    /// <summary>站立姿态的附加动作（来自方案 A 的重拓扑模型）。</summary>
    Stand,
    /// <summary>坐姿小动作：甩尾巴 / 抖毛 / 观察四周 / 呆望。</summary>
    Shake,
    LookAround,
    Gaze,
    /// <summary>眨眼：睁 → 半闭 → 闭 → 半闭 → 睁。</summary>
    Blink,

    /// <summary>头部跟随鼠标：一组静态角度帧。</summary>
    HeadFollow,
}

/// <summary>动画的段落（ABC 三段式）。</summary>
public enum AnimatType
{
    Single,
    A_Start,
    B_Loop,
    C_End,
}

/// <summary>宠物状态，决定播放哪一套变体。</summary>
public enum ModeType
{
    Happy,
    Nomal,
    PoorCondition,
    Ill,
}

/// <summary>
/// 一段动画的元数据：类型 + 段落 + 状态 + 名字。
/// 对应 VPet 的 <c>GraphInfo</c>，并复刻了"从目录路径推断"的规则。
/// </summary>
public sealed class GraphInfo
{
    public string Name { get; set; } = "";
    public GraphType Type { get; set; } = GraphType.Common;
    public AnimatType Animat { get; set; } = AnimatType.Single;
    public ModeType Mode { get; set; } = ModeType.Nomal;
    public string SourceDir { get; set; } = "";

    public override string ToString() => $"[{Name}] {Type}/{Animat}/{Mode}";

    /// <summary>
    /// 由 info.lps 的显式声明构造。
    /// 例：<c>PNGAnimation#idle:|path#idle:|mode#Nomal:|graph#Default:|animat#Single:|loop#true:|</c>
    /// </summary>
    public static GraphInfo FromLine(LpsLine line, string sourceDir, string fallbackName)
    {
        var info = new GraphInfo { SourceDir = sourceDir };

        info.Mode = Enum.TryParse<ModeType>(line.Get("mode"), true, out var m) ? m : ModeType.Nomal;
        info.Type = Enum.TryParse<GraphType>(line.Get("graph"), true, out var g) ? g : GraphType.Common;
        info.Animat = Enum.TryParse<AnimatType>(line.Get("animat"), true, out var a) ? a : AnimatType.Single;

        info.Name = string.IsNullOrWhiteSpace(line.Info) ? fallbackName : line.Info.Trim();
        if (string.IsNullOrWhiteSpace(info.Name))
            info.Name = info.Type.ToString().ToLowerInvariant();

        return info;
    }

    /// <summary>
    /// 没有 info.lps 时，按"相对目录名"推断（VPet 的隐式规则）：
    /// 目录 <c>Touch_Head/A_ill</c> → 类型 Touch_Head、段落 A_Start、状态 Ill。
    /// </summary>
    public static GraphInfo Infer(string relativeDir, string sourceDir)
    {
        var info = new GraphInfo { SourceDir = sourceDir };

        var parts = relativeDir
            .ToLowerInvariant()
            .Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries)
            .SelectMany(s => s.Split('_'))
            .Where(s => s.Length > 0)
            .ToList();

        // ① 状态
        if (Remove(parts, "happy")) info.Mode = ModeType.Happy;
        else if (Remove(parts, "nomal")) info.Mode = ModeType.Nomal;
        else if (Remove(parts, "poorcondition")) info.Mode = ModeType.PoorCondition;
        else if (Remove(parts, "ill")) info.Mode = ModeType.Ill;
        else info.Mode = ModeType.Nomal;

        // ② 动画类型：按枚举名的下划线分段做匹配
        info.Type = GraphType.Common;
        foreach (GraphType candidate in Enum.GetValues<GraphType>())
        {
            var tokens = candidate.ToString().ToLowerInvariant().Split('_');
            var index = parts.IndexOf(tokens[0]);
            if (index < 0)
                continue;

            var match = true;
            for (var i = 1; i < tokens.Length && index + i < parts.Count; i++)
            {
                if (parts[index + i] != tokens[i]) { match = false; break; }
            }
            if (!match)
                continue;

            info.Type = candidate;
            parts.RemoveRange(index, Math.Min(tokens.Length, parts.Count - index));
            break;
        }

        // ③ 动作段落
        if (Remove(parts, "a") || Remove(parts, "start")) info.Animat = AnimatType.A_Start;
        else if (Remove(parts, "b") || Remove(parts, "loop")) info.Animat = AnimatType.B_Loop;
        else if (Remove(parts, "c") || Remove(parts, "end")) info.Animat = AnimatType.C_End;
        else info.Animat = AnimatType.Single;

        // ④ 名字：去掉尾部纯数字/~ 标记后取最后一段
        while (parts.Count > 0 && (double.TryParse(parts[^1], out _) || parts[^1].StartsWith('~')))
            parts.RemoveAt(parts.Count - 1);

        info.Name = parts.Count > 0
            ? parts[^1]
            : info.Type.ToString().ToLowerInvariant();

        return info;
    }

    private static bool Remove(List<string> parts, string token) => parts.Remove(token);
}
