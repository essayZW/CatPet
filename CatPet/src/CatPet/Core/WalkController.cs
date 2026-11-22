namespace CatPet.Core;

/// <summary>一次行走 tick 的结果。</summary>
public enum WalkTickKind
{
    /// <summary>没在走（或已经停了），什么都不用做。</summary>
    Idle,
    /// <summary>正常前进，按 <see cref="WalkStep.Dx"/> 平移窗口。</summary>
    Moved,
    /// <summary>撞到屏幕边缘：已自动停下，调用方负责吐槽/回待机。</summary>
    HitEdge,
    /// <summary>"发起行走的那个动作"已经结束了（孤儿定时器）：已自动停下。</summary>
    OrphanStopped,
}

/// <summary>一次 tick 的结果：走了多少、该把窗口放到哪。</summary>
public readonly record struct WalkStep(WalkTickKind Kind, double Dx, double NextLeft);

/// <summary>
/// 行走位移的状态机：管方向、速度、代际与边界。
/// 只算数，不碰窗口/定时器 —— 定时器在 <c>PetWindow</c> 里，只负责按
/// <see cref="Tick"/> 的结果去改 <c>Left</c>。
/// </summary>
/// <remarks>
/// 历史坑（这次抽出来的原因）：
/// 1) **孤儿定时器**：动作结束了但位移还在跑 → "猫没走，窗口却在滑"；
/// 2) **越权收尾**：旧动作的收尾把新动作的位移停掉 → "动画在播，窗口不动"。
///    解法是代际号：收尾只允许停自己那次（<see cref="StopIfCurrent"/>）。
/// </remarks>
public sealed class WalkController
{
    /// <summary>走的速度（px/s）。</summary>
    public const double WalkPxPerSecond = 48;

    /// <summary>跑的速度。</summary>
    public const double RunPxPerSecond = 150;

    /// <summary>飞奔的速度。</summary>
    public const double GallopPxPerSecond = 430;

    /// <summary>单次 tick 允许推进的最大时间：卡顿/最小化时不要一次跳很远。</summary>
    public const double MaxDeltaSeconds = 0.15;

    /// <summary>是否正在走。</summary>
    public bool IsWalking { get; private set; }

    /// <summary>方向：-1 左、+1 右。</summary>
    public int Direction { get; private set; }

    /// <summary>当前速度（px/s）。</summary>
    public double Speed { get; private set; } = WalkPxPerSecond;

    /// <summary>每次 Start 自增；收尾逻辑用它判断"自己是不是还拥有这次位移"。</summary>
    public int Generation { get; private set; }

    /// <summary>
    /// 开始走（会先停掉上一次）。返回本次的代际号，调用方保存它，
    /// 结束收尾时用 <see cref="StopIfCurrent"/> 避免越权。
    /// </summary>
    public int Start(int direction, double? pxPerSecond = null)
    {
        Stop();
        Direction = direction < 0 ? -1 : 1;      // 0 按向右处理：位移只能由 dir 决定，别留"原地走"的怪状态
        Speed = pxPerSecond is > 0 ? pxPerSecond.Value : WalkPxPerSecond;
        Generation++;
        IsWalking = true;
        return Generation;
    }

    /// <summary>
    /// 同方向只改速度（追逐时每帧都在调速，不能重新 Start，否则代际乱跳、定时器重建）。
    /// </summary>
    public void SetSpeed(double pxPerSecond)
    {
        if (!IsWalking) return;
        if (pxPerSecond > 0) Speed = pxPerSecond;
    }

    /// <summary>停下（幂等）。返回是否真的停了一次行走。</summary>
    public bool Stop()
    {
        if (!IsWalking) return false;
        IsWalking = false;
        return true;
    }

    /// <summary>只有代际仍然是自己的才停：防止旧动作收尾时把新动作的位移停掉。</summary>
    public bool StopIfCurrent(int generation)
    {
        if (!IsWalking || generation != Generation) return false;
        IsWalking = false;
        return true;
    }

    /// <summary>
    /// 推进一帧位移。
    /// </summary>
    /// <param name="dtSeconds">距上次 tick 的真实间隔（会被夹到 0~<see cref="MaxDeltaSeconds"/>）。</param>
    /// <param name="left">当前窗口左边缘。</param>
    /// <param name="width">窗口宽度。</param>
    /// <param name="screenLeft">屏幕（虚拟桌面）左边缘。</param>
    /// <param name="screenRight">屏幕右边缘。</param>
    /// <param name="ownerActive">
    /// 发起行走的动作是否还活着。传 false 会把"孤儿位移"直接停掉 ——
    /// 这就是"猫站着不动、窗口却在滑"的保险。
    /// </param>
    public WalkStep Tick(
        double dtSeconds,
        double left,
        double width,
        double screenLeft,
        double screenRight,
        bool ownerActive = true)
    {
        if (!IsWalking) return new WalkStep(WalkTickKind.Idle, 0, left);

        if (!ownerActive)
        {
            Stop();
            return new WalkStep(WalkTickKind.OrphanStopped, 0, left);
        }

        var dt = Math.Clamp(dtSeconds, 0.0, MaxDeltaSeconds);
        var dx = Speed * dt * Direction;
        var next = left + dx;

        var hitEdge = (Direction < 0 && next <= screenLeft)
                      || (Direction > 0 && next + width >= screenRight);
        if (hitEdge)
        {
            Stop();
            return new WalkStep(WalkTickKind.HitEdge, 0, left);
        }

        return new WalkStep(WalkTickKind.Moved, dx, next);
    }
}
