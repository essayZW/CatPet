namespace CatPet.Core;

/// <summary>贴边偏好的判定结果。</summary>
public enum EdgeDecision
{
    /// <summary>不需要纠正。</summary>
    None,
    /// <summary>贴着边却朝屏幕外，应该转身（背对边缘）。</summary>
    TurnAway,
    /// <summary>离两侧边缘都远，应该往回走。</summary>
    GoBack,
}

/// <summary>贴边判定 + 该往哪走。</summary>
public readonly record struct EdgeResult(EdgeDecision Decision, bool GoLeft, bool Fast);

/// <summary>发呆气泡的口气（按时段）。</summary>
public enum GazeMood
{
    Normal,
    Drowsy,
    Night,
}

/// <summary>
/// 桌宠的**纯逻辑**：不碰 WPF、不读时钟、不掷骰子（随机数与时间都由调用方传入）。
/// 这样这些规则可以脱离窗口直接单测，历史 bug 也能固化成回归测试。
/// 行为规则见 AGENTS.md 的 §5.2（状态机）与 §5.4（贴边偏好）。
/// </summary>
public static class PetLogic
{
    /// <summary>距屏幕边缘 ≤ 这个距离视为"贴着边"。</summary>
    public const double EdgeComfort = 96;

    /// <summary>两侧边缘都远于这个距离视为"待在中间"。</summary>
    public const double EdgeFar = 260;

    /// <summary>往回走时，超过这个距离才值得跑。</summary>
    public const double RunBackIfFartherThan = 320;

    /// <summary>朝左的站立素材名（不带 _l 后缀的这批默认就是朝左）。</summary>
    public static readonly IReadOnlySet<string> LeftFacingClips = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "sidle", "slook", "swag", "sclose", "sjump", "sjump_diag", "sfollow",
    };

    /// <summary>
    /// 播完某个素材之后的朝向：`_r` 结尾 = 朝右，`_l` 结尾或"朝左的那批" = 朝左，
    /// 其余（idle / say / follow …）不改朝向。
    /// </summary>
    /// <remarks>
    /// 历史 bug：只认 `_r` / `_l` 后缀时，播 `sidle`（其实是朝左素材）不会把朝向改回去，
    /// 于是头部跟随一接管就切回 `sfollow_r`，看起来像"转身被撤销"。
    /// </remarks>
    public static bool FacingAfterClip(bool faceRight, string clip)
    {
        if (string.IsNullOrEmpty(clip)) return faceRight;
        if (clip.EndsWith("_r", StringComparison.Ordinal)) return true;
        if (clip.EndsWith("_l", StringComparison.Ordinal)) return false;
        return LeftFacingClips.Contains(clip) ? false : faceRight;
    }

    public static bool IsNearRightEdge(double roomRight) => roomRight <= EdgeComfort;

    public static bool IsNearLeftEdge(double roomLeft) => roomLeft <= EdgeComfort;

    /// <summary>离两侧边缘都远，应该往回挪。</summary>
    public static bool IsAwayFromEdges(double roomLeft, double roomRight)
        => Math.Min(roomLeft, roomRight) > EdgeFar;

    /// <summary>贴着边却朝屏幕外侧（应该转过去）。</summary>
    public static bool IsFacingOutward(double roomLeft, double roomRight, bool faceRight)
        => (IsNearRightEdge(roomRight) && faceRight) || (IsNearLeftEdge(roomLeft) && !faceRight);

    /// <summary>
    /// 一次性算出该不该纠正位置/朝向。转身优先于回边。
    /// </summary>
    /// <param name="standing">坐姿不做转身（坐姿没有左右两套待机）。</param>
    /// <param name="focusMode">专注（番茄钟工作段）时不走动、不摆动作。</param>
    /// <param name="rollTurn">0~1，用于决定这次是否执行"转身"。</param>
    /// <param name="rollGoBack">0~1，用于决定这次是否执行"回边"。</param>
    /// <param name="turnChance">转身概率（默认 0.8）。</param>
    /// <param name="goBackChance">回边概率（默认 0.7）。</param>
    public static EdgeResult EvaluateEdge(
        double roomLeft,
        double roomRight,
        bool standing,
        bool faceRight,
        bool focusMode,
        double rollTurn,
        double rollGoBack,
        double turnChance = 0.8,
        double goBackChance = 0.7)
    {
        if (focusMode)
            return new EdgeResult(EdgeDecision.None, false, false);

        if (standing && IsFacingOutward(roomLeft, roomRight, faceRight) && rollTurn < turnChance)
            return new EdgeResult(EdgeDecision.TurnAway, false, false);

        if (IsAwayFromEdges(roomLeft, roomRight) && rollGoBack < goBackChance)
        {
            var goLeft = roomLeft < roomRight;
            var distance = goLeft ? roomLeft : roomRight;
            return new EdgeResult(EdgeDecision.GoBack, goLeft, distance > RunBackIfFartherThan);
        }

        return new EdgeResult(EdgeDecision.None, false, false);
    }

    /// <summary>
    /// 把动作名换成"朝屏幕内侧"的那一套：靠左边的边 → 朝右（`*_r`），靠右边的边 → 朝左（不带后缀）。
    /// </summary>
    public static string InwardClip(string baseName, bool nearLeftEdge) => baseName switch
    {
        "sidle" => nearLeftEdge ? "sidle_r" : "sidle",
        "slook" => nearLeftEdge ? "slook_r" : "slook",
        "swag" => nearLeftEdge ? "swag_r" : "swag",
        _ => baseName,
    };

    /// <summary>按朝向挑左右两套素材之一（朝右且素材存在时用 right）。</summary>
    public static string SideClip(bool faceRight, string left, string right, Func<string, bool> has) =>
        faceRight && has(right) ? right : left;

    /// <summary>
    /// 待机随机池的时段重映射：早上偏张望、午后偏哈欠/发呆、深夜偏发呆。
    /// roll 取值 0~10（调用方掷骰），8 档里 7 = 发呆、1 = 歪头、2 = 哈欠、3 = 舔毛、5 = 抖毛、6 = 东张西望。
    /// </summary>
    public static int IdleRoll(int hour, int roll) => hour switch
    {
        >= 5 and < 10 => roll switch { 2 => 6, 3 => 6, 7 => 0, _ => roll },
        >= 12 and < 15 => roll switch { 5 => 2, 6 => 7, 1 => 2, _ => roll },
        >= 22 or < 5 => roll switch { 1 => 7, 3 => 7, 5 => 7, 8 => 7, _ => roll },
        _ => roll,
    };

    /// <summary>发呆也算"生活化小动作"的档位集合（跟随期间只放这些）。</summary>
    public static bool IsLivingAction(int roll) => roll is 1 or 2 or 3 or 5 or 6;

    /// <summary>多久没互动就打盹：深夜 25s、午后 35s、早上 55s、其余 45s。</summary>
    public static double SleepAfterSeconds(int hour) => hour switch
    {
        >= 22 or < 5 => 25,
        >= 12 and < 15 => 35,
        >= 5 and < 10 => 55,
        _ => 45,
    };

    /// <summary>发呆气泡的口气：深夜"好困…"、午后"困了…"、其余中性。</summary>
    public static GazeMood GazeMoodForHour(int hour) =>
        hour is >= 22 or < 5 ? GazeMood.Night
        : hour is >= 12 and < 15 ? GazeMood.Drowsy
        : GazeMood.Normal;

    /// <summary>走路速度与步频是否匹配（观感检查，脚本/测试共用）。</summary>
    /// <remarks>步幅约 34.6px/圈，一圈 30 帧 → 每帧毫秒 = 34.6 / 速度 × 1000 / 30。</remarks>
    public static double FrameMsForSpeed(double pxPerSecond, double stridePxPerCycle = 34.6, int framesPerCycle = 30)
        => pxPerSecond <= 0 ? double.NaN : stridePxPerCycle / pxPerSecond * 1000.0 / framesPerCycle;
}
