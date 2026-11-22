namespace CatPet.Core;

/// <summary>
/// 启动 / 退场运镜的时间线与选边规则。
/// 缩放和横向位移都烘在素材里，窗口不动；这里只管"用哪一段、等多久、气泡挂多久"。
/// </summary>
/// <remarks>
/// 抽出来的原因：这些数字散在 <c>PetWindow.Cinematic.cs</c> 里，只有注释说明意图
/// （"气泡一直挂到猫消失"），没有任何地方能验证它们真的对得上。
/// </remarks>
public static class CinematicTimeline
{
    /// <summary>从右边进来（朝左走）。</summary>
    public const string IntroRight = "cin_intro";

    /// <summary>从左边进来（朝右走）。</summary>
    public const string IntroLeft = "cin_intro_l";

    /// <summary>往右边走掉（背对镜头）。</summary>
    public const string ExitRight = "cin_outro";

    /// <summary>往左边走掉。</summary>
    public const string ExitLeft = "cin_outro_l";

    /// <summary>退场时的告别话（只出现在气泡里）。</summary>
    public const string ExitText = "（拜拜了喵，明天见）";

    /// <summary>开场期间让跟随让位：只覆盖片长（44 帧 × 45ms ≈ 1980ms），播完立刻待机/跟随。</summary>
    public const double IntroFollowPauseSeconds = 2.1;

    /// <summary>退场期间让跟随让位：要盖住整个退场（气泡 + 走远）。</summary>
    public const double ExitFollowPauseSeconds = 6.0;

    /// <summary>退场气泡的时长：要比"停顿 + 片长"更长，保证猫消失时气泡还在。</summary>
    public const double ExitBubbleSeconds = 3.2;

    /// <summary>说完"拜拜"后停顿多久再开始走。</summary>
    public const int ExitBubbleDelayMs = 520;

    /// <summary>没有退场素材时的兜底停顿。</summary>
    public const int ExitNoClipDelayMs = 150;

    /// <summary>运镜素材的帧时长（烘焙时定的）。</summary>
    public const int CineFrameMs = 45;

    /// <summary>运镜素材的帧数。</summary>
    public const int CineFrameCount = 44;

    /// <summary>运镜素材总时长（毫秒）。</summary>
    public static int CineClipMs => CineFrameMs * CineFrameCount;

    /// <summary>离哪边更近就用哪边那套：右侧更近（或一样近）→ true。</summary>
    public static bool PreferRightSide(double roomLeft, double roomRight) => roomRight <= roomLeft;

    /// <summary>首选的开场素材名。</summary>
    public static string PreferredIntro(bool preferRight) => preferRight ? IntroRight : IntroLeft;

    /// <summary>首选的退场素材名。</summary>
    public static string PreferredExit(bool preferRight) => preferRight ? ExitRight : ExitLeft;

    /// <summary>另一侧的那套（素材缺失时的兜底）。</summary>
    public static string Mirror(string clip) => clip switch
    {
        IntroRight => IntroLeft,
        IntroLeft => IntroRight,
        ExitRight => ExitLeft,
        ExitLeft => ExitRight,
        _ => clip,
    };

    /// <summary>首选有素材就用，否则用另一侧；两边都缺返回 null。</summary>
    public static string? ChooseClip(string preferred, Func<string, bool> hasClip)
    {
        if (hasClip(preferred)) return preferred;
        var mirror = Mirror(preferred);
        return hasClip(mirror) ? mirror : null;
    }

    /// <summary>开场素材对应的朝向：从左进来是朝右走。</summary>
    public static bool FacingRightForIntro(string clip) => clip == IntroLeft;

    /// <summary>退场素材对应的朝向：往右走掉就朝右（背对镜头）。</summary>
    public static bool FacingRightForExit(string clip) => clip != ExitLeft;

    /// <summary>退场从"说拜拜"到猫消失的总时长（毫秒）。</summary>
    public static int ExitTotalMs(int clipMs) => ExitBubbleDelayMs + clipMs;

    /// <summary>气泡是否盖得住整个退场（否则猫还在走，气泡先没了）。</summary>
    public static bool BubbleCoversExit(int clipMs) => ExitBubbleSeconds * 1000 >= ExitTotalMs(clipMs);
}
