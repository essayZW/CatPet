namespace CatPet.Core;

/// <summary>
/// 一处状态快照，喂给 <see cref="PetGates"/> 做"这个动作现在能不能做"的判断。
/// 全部是值，不引用窗口/播放器，方便直接构造来测。
/// 注意用对象初始化器构造（`new PetActivity { Asleep = true }`）：
/// 结构体的主构造函数默认值在 `new PetActivity()` 时不会生效，容易踩坑。
/// </summary>
public sealed class PetActivity
{
    public bool Asleep { get; init; }
    public bool Standing { get; init; }
    public bool Dragging { get; init; }
    public bool Resizing { get; init; }
    public bool Busy { get; init; }
    public bool Visible { get; init; } = true;
    public bool Chase { get; init; }
    public bool ChaseNear { get; init; }
    public bool InviteOpen { get; init; }
    public bool Focus { get; init; }
    public bool HasSleepClip { get; init; } = true;
    public bool StandBusy { get; init; }
    public bool FollowWanted { get; init; } = true;
    public bool FollowPaused { get; init; }
    /// <summary>最近几秒鼠标在动（人在电脑前），不该打盹。</summary>
    public bool MouseActive { get; init; }
    public double IdleSeconds { get; init; }
    public double SleepAfterSeconds { get; init; } = 45;
}

/// <summary>跟随此刻该怎么处理。</summary>
public enum FollowGate
{
    /// <summary>可以跟随。</summary>
    Allowed,
    /// <summary>不是跟随的问题（睡/站立动作/追逐中/动作暂停），原样返回，别去改画面。</summary>
    Blocked,
    /// <summary>不该跟随了（关掉设置/拖动中/缩放中/隐藏），要结束跟随并回到待机。</summary>
    EndFollow,
    /// <summary>睡眠中：只复位跟随状态，绝不能播待机（会顶掉打盹）。</summary>
    ResetOnly,
}

/// <summary>
/// 编排层的**门禁表**：哪些动作现在允许做。
/// 这些条件以前散落在 <c>PetWindow</c> 的四个定时器回调里，改一处很容易漏另一处
/// （"打盹中跟随被挡死""拖拽中还在自娱自乐"都是这么来的）。
/// 纯函数，无副作用，可直接单测。
/// </summary>
public static class PetGates
{
    /// <summary>坐姿小动作：空闲 6 秒以上才演，站立/拖动/睡觉/正忙时都不演。</summary>
    public const double IdleActionAfterSeconds = 6;

    /// <summary>姿态轮换（站起/坐下/溜达）：空闲 18 秒以上。</summary>
    public const double ChurnAfterSeconds = 18;

    /// <summary>坐姿小动作的定时器周期。</summary>
    public const double IdleTickSeconds = 14;

    /// <summary>姿态轮换的定时器周期（中档）。</summary>
    public const double ChurnTickSeconds = 22;

    /// <summary>跟随采样周期。</summary>
    public const double FollowTickSeconds = 0.06;

    /// <summary>贴着边时的朝向纠正概率。</summary>
    public const double TurnAwayChance = 0.8;

    /// <summary>离边缘远时回边的概率。</summary>
    public const double GoBackChance = 0.7;

    /// <summary>坐姿小动作。</summary>
    public static bool CanIdleAction(PetActivity a)
        => !a.Asleep && !a.Standing && !a.Dragging && !a.Busy && a.IdleSeconds >= IdleActionAfterSeconds;

    /// <summary>姿态轮换（站起/坐下/溜达）。</summary>
    public static bool CanChurn(PetActivity a)
        => !a.Asleep && !a.Dragging && !a.Resizing && !a.StandBusy && !a.Chase
           && a.Visible && a.IdleSeconds >= ChurnAfterSeconds;

    /// <summary>打盹：注意**站立时不在这里拦**——站立要先坐下来，那条分支在调用方。</summary>
    public static bool CanSleep(PetActivity a)
        => !a.Asleep && !a.Dragging && !a.Busy && !a.Chase && !a.InviteOpen
           && !a.MouseActive                 // 鼠标在动 = 人在电脑前，别打盹
           && a.HasSleepClip && a.IdleSeconds >= a.SleepAfterSeconds;

    /// <summary>小游戏 tick 能不能动（拖拽/缩放/睡眠/隐藏时冻结）。</summary>
    public static bool CanChaseTick(PetActivity a)
        => !a.Dragging && !a.Resizing && !a.Asleep && a.Visible;

    /// <summary>
    /// 头部跟随此刻该怎么办。优先级与历史实现一致：
    /// 睡眠 &gt; 站立动作 &gt; 追逐阶段 &gt; 该结束（隐藏/关设置/拖动/缩放）&gt; 动作暂停 &gt; 允许。
    /// </summary>
    public static FollowGate EvaluateFollowGate(PetActivity a)
    {
        if (a.Asleep) return FollowGate.ResetOnly;
        if (a.StandBusy) return FollowGate.Blocked;
        if (a.Chase && !a.ChaseNear) return FollowGate.Blocked;
        if (!a.Visible || !a.FollowWanted || a.Dragging || a.Resizing) return FollowGate.EndFollow;
        if (a.FollowPaused) return FollowGate.Blocked;
        return FollowGate.Allowed;
    }

    /// <summary>跟随期间允许出现的"生活化小动作"档位（发呆/转头/盯鼠标不算）。</summary>
    public static bool AllowedDuringFollow(int roll) => PetLogic.IsLivingAction(roll);
}
