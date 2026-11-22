namespace CatPet.Core;

/// <summary>追鼠标小游戏的步态。</summary>
public enum ChaseGait
{
    /// <summary>走。</summary>
    Walk,
    /// <summary>跑。</summary>
    Run,
    /// <summary>飞奔。</summary>
    Gallop,
}

/// <summary>鼠标相对猫身的位置分区。</summary>
public enum ChaseZone
{
    /// <summary>离得远：追。</summary>
    Far,
    /// <summary>横向很近但不在追：站住看鼠标。</summary>
    Near,
    /// <summary>身体贴上（椭圆内）：算抓到。</summary>
    Touching,
}

/// <summary>纵向扑击的决策。</summary>
public enum JumpDecision
{
    /// <summary>可以起跳。</summary>
    Jump,
    /// <summary>够不着，但横向还有距离：先走近再说。</summary>
    ApproachFirst,
    /// <summary>彻底够不着：站住抬头，说一句"够不着"。</summary>
    GiveUp,
}

/// <summary>
/// 追鼠标小游戏的**纯规则**：分区、抓取资格、目标速度、步态滞回、跳跃判定。
/// 不碰窗口/定时器 —— 原来这些都埋在 393 行的 <c>PetWindow.Chase.cs</c> 里，
/// 只能靠"跑起来看手感"验证。
/// </summary>
public static class ChaseRules
{
    /// <summary>横向这个范围内进入"看着鼠标"（仅在不追的时候）。</summary>
    public const double NearZone = 165;

    /// <summary>追得再急也不超过这个速度。</summary>
    public const double MaxChaseSpeed = 480;

    /// <summary>目标速度下限（别原地磨）。</summary>
    public const double MinChaseSpeed = 34;

    /// <summary>进入小游戏后至少这么久才判"抓到"（防开局误抓）。</summary>
    public const double CatchGraceMs = 800;

    /// <summary>鼠标高出这么多就考虑起跳。</summary>
    public const double JumpTriggerDy = 140;

    /// <summary>跳跃高度上限。</summary>
    public const double MaxJumpPeak = 230;

    /// <summary>横向差小于这个值就不再挪了（否则会原地来回蹭）。</summary>
    public const double StopDistance = 26;

    /// <summary>方向死区：鼠标几乎在正前/正后时保持原方向，别来回翻面。</summary>
    public const double DirectionDeadZone = 24;

    /// <summary>鼠标横向甩得快时，目标速度的附加项系数。</summary>
    public const double MouseVelocityGain = 0.45;

    /// <summary>速度平滑系数（每 40ms 一次）。</summary>
    public const double SpeedSmoothing = 0.20;

    /// <summary>连跳几次没碰到就放弃。</summary>
    public const int MaxJumpTries = 2;

    /// <summary>身体椭圆判定：鼠标进椭圆才算贴上。</summary>
    public static bool IsTouching(double dx, double dy, double halfWidth, double halfHeight)
    {
        var hw = Math.Max(20.0, halfWidth);
        var hh = Math.Max(20.0, halfHeight);
        var nx = dx / hw;
        var ny = dy / hh;
        return nx * nx + ny * ny <= 1.0;
    }

    /// <summary>身体椭圆的半宽/半高（按窗口尺寸算）。</summary>
    public static (double HalfWidth, double HalfHeight) BodyEllipse(double width, double petHeight)
        => (Math.Max(20.0, width * 0.42), Math.Max(20.0, petHeight * 0.40));

    /// <summary>
    /// 能不能判抓到：必须**正在追**（dir != 0）、开局够久、不在冷却里。
    /// "鼠标自己凑过来"（没在追）不算抓到，那是"看着鼠标"模式。
    /// </summary>
    public static bool CanCatch(
        bool touching,
        bool chasing,
        double sinceStartMs,
        bool cooldownPassed,
        double graceMs = CatchGraceMs)
        => touching && chasing && sinceStartMs > graceMs && cooldownPassed;

    /// <summary>要不要站起来看鼠标（不追、已经停稳、横向够近、纵向差不大）。</summary>
    public static bool ShouldWatchMouse(bool idle, double dist, double deltaY)
        => idle && dist <= NearZone && Math.Abs(deltaY) <= JumpTriggerDy;

    /// <summary>
    /// 鼠标在猫身下方且横向很近：猫下不去，站住低头看。
    /// 不加这条会掉进"追→贴边→换向"的循环（鼠标正在身子底下时方向每拍都在翻）。
    /// </summary>
    public static bool ShouldWatchBelow(bool idle, double dist, double deltaY)
        => idle && dist <= NearZone && deltaY > JumpTriggerDy;

    /// <summary>已经站到鼠标正下方：停住别再蹭。</summary>
    public static bool ShouldStopAtMouse(double dist, bool chasing) => chasing && dist < StopDistance;

    /// <summary>方向：死区内保持原方向，否则朝鼠标那侧。</summary>
    public static int DirectionFor(double delta, int previousDirection)
        => previousDirection != 0 && Math.Abs(delta) < DirectionDeadZone
            ? previousDirection
            : (delta > 0 ? 1 : -1);

    /// <summary>目标速度：越远越快、鼠标横向甩得快也加一点，封顶 <see cref="MaxChaseSpeed"/>。</summary>
    public static double TargetSpeed(double dist, double mouseVelocity)
        => Math.Clamp(dist * 1.1 + Math.Abs(mouseVelocity) * MouseVelocityGain, MinChaseSpeed, MaxChaseSpeed);

    /// <summary>速度平滑：加速/减速都不突变，且不低于 10。</summary>
    public static double SmoothSpeed(double current, double target, double factor = SpeedSmoothing)
    {
        var next = current + (target - current) * factor;
        return next < 10 ? 10 : next;
    }

    /// <summary>
    /// 速度 → 步态（带滞回，避免阈值附近来回切）：
    /// 进入：走 →(>140) 跑 →(>320) 飞奔；退出：飞奔 →(≤290) 跑 →(≤110) 走。
    /// 两条路径不同，形成一个 30 宽的缓冲区，速度在阈值附近抖动时不会翻面。
    /// </summary>
    public static ChaseGait GaitFor(double speed, ChaseGait? current = null)
    {
        // 滞回方向要"更难退出"：已经在跑，就得掉到 110 以下才切回走（原来是 165，
        // 反而比"进入跑"的 140 更容易，会在阈值附近每拍翻一次）
        var walkMax = current == ChaseGait.Run ? 110.0 : 140.0;
        var runMax = current == ChaseGait.Gallop ? 290.0 : 320.0;

        if (speed <= walkMax) return ChaseGait.Walk;
        if (speed <= runMax) return ChaseGait.Run;
        return ChaseGait.Gallop;
    }

    /// <summary>步态对应的素材名（朝右 _r / 朝左 _l）。</summary>
    public static string GaitClip(ChaseGait gait, int dir)
    {
        var suffix = dir > 0 ? "r" : "l";
        return gait switch
        {
            ChaseGait.Walk => $"swalk_{suffix}",
            ChaseGait.Run => $"srun_{suffix}",
            _ => $"sgal_{suffix}",
        };
    }

    /// <summary>不用跳到鼠标中心：身体椭圆有半高，跳低一点更自然（最少 90）。</summary>
    public static double JumpNeed(double deltaY) => Math.Max(90, -deltaY - 45);

    /// <summary>跳太高或上方空间不够 → 够不着。</summary>
    public static bool TooHigh(double need, double spaceAbove)
        => need > MaxJumpPeak || spaceAbove < Math.Min(need, MaxJumpPeak) + 24;

    /// <summary>
    /// 纵向扑击判定。够不着但横向还有距离时先走近（<see cref="JumpDecision.ApproachFirst"/>），
    /// 避免"走到一半停下说够不着"或者"离得老远却原地跳"。
    /// </summary>
    public static bool ShouldConsiderJump(double deltaY, double deltaX, bool jumpCooldownPassed)
        => deltaY < -JumpTriggerDy && Math.Abs(deltaX) <= 420 && jumpCooldownPassed;

    /// <summary>
    /// 决定这一拍是"跳 / 先走近 / 放弃"。
    /// </summary>
    public static JumpDecision DecideJump(
        double deltaY,
        double dist,
        bool canMoveX,
        int jumpTries,
        bool needsMove,
        double spaceAbove)
    {
        var need = JumpNeed(deltaY);
        var tooHigh = TooHigh(need, spaceAbove);
        var cantDoIt = tooHigh || jumpTries >= MaxJumpTries || needsMove;

        if (!cantDoIt) return JumpDecision.Jump;
        return cantDoIt && dist > NearZone * 0.7 && canMoveX
            ? JumpDecision.ApproachFirst
            : JumpDecision.GiveUp;
    }
}
