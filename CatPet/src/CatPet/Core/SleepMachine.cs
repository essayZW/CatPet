namespace CatPet.Core;

/// <summary>睡眠阶段。</summary>
public enum SleepPhase
{
    Awake,
    Asleep,
}

/// <summary>一次检查/请求的结果。</summary>
public enum SleepEvent
{
    /// <summary>没有变化。</summary>
    None,
    /// <summary>刚刚睡着（调用方该播打盹动画）。</summary>
    FellAsleep,
    /// <summary>刚刚醒来（调用方该播"抬头睁眼"再回待机）。</summary>
    Woke,
}

/// <summary>
/// 打盹状态机：**只有** <see cref="RequestWake"/> 能让它醒，<see cref="CheckFallAsleep"/>
/// 负责在满足条件时让它睡。两个方法都是幂等的。
/// </summary>
/// <remarks>
/// 抽出来的原因：历史上这里出过"假死"bug —— 打盹动画播完回待机，但标志位还是"睡着"，
/// 于是跟随、小动作、姿态轮换全被挡，表现成"看着像正常待机，点一下才恢复"。
/// 现在"什么时候能睡""谁能叫醒"都是这一处的显式转移，可以脱离窗口单测。
/// </remarks>
public sealed class SleepMachine
{
    /// <summary>当前阶段。</summary>
    public SleepPhase Phase { get; private set; } = SleepPhase.Awake;

    /// <summary>是否睡着。睡着时跟随/小动作/姿态轮换都不该发生。</summary>
    public bool Asleep => Phase == SleepPhase.Asleep;

    /// <summary>
    /// 长时间没互动时调一次：够久且允许就进入睡眠。
    /// </summary>
    /// <param name="idleSeconds">距上次互动的秒数。</param>
    /// <param name="sleepAfterSeconds">该时段的打盹阈值。</param>
    /// <param name="canSleep">其它前置条件（无追逐/无邀约/有素材…），见 <see cref="PetGates.CanSleep"/>。</param>
    /// <returns>刚睡着返回 <see cref="SleepEvent.FellAsleep"/>，否则 <see cref="SleepEvent.None"/>。</returns>
    public SleepEvent CheckFallAsleep(double idleSeconds, double sleepAfterSeconds, bool canSleep = true)
    {
        if (Asleep) return SleepEvent.None;                 // 幂等：睡着就不再"睡一次"
        if (!canSleep) return SleepEvent.None;
        if (idleSeconds < sleepAfterSeconds) return SleepEvent.None;

        Phase = SleepPhase.Asleep;
        return SleepEvent.FellAsleep;
    }

    /// <summary>
    /// 有交互（点击/拖动/托盘显示…）时调：睡着就醒来。
    /// </summary>
    /// <returns>刚醒来返回 <see cref="SleepEvent.Woke"/>，本来醒着返回 <see cref="SleepEvent.None"/>。</returns>
    public SleepEvent RequestWake()
    {
        if (!Asleep) return SleepEvent.None;                // 幂等：没睡就没有"醒"
        Phase = SleepPhase.Awake;
        return SleepEvent.Woke;
    }

    /// <summary>强制复位（切换姿态/重建状态时用，正常流程走 <see cref="RequestWake"/>）。</summary>
    public void Reset() => Phase = SleepPhase.Awake;
}
