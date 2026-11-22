using CatPet.Core;
using Xunit;

namespace CatPet.Tests;

/// <summary>
/// 编排层门禁表：哪些动作此刻允许做。
/// 这些条件以前散在四个定时器回调里，出过"打盹中跟随被挡死""拖拽中还在自娱自乐"这类 bug。
/// </summary>
public class PetGatesTests
{
    // ---------------------------------------------------------------- 跟随

    [Fact]
    public void 一切正常时允许跟随()
        => Assert.Equal(FollowGate.Allowed, PetGates.EvaluateFollowGate(new PetActivity()));

    [Fact]
    public void 睡眠中只复位跟随状态_不能播待机()
    {
        // 回归：打盹时 sleep 链播完会回待机，_asleep 还是 true，
        // 跟随与小动作全被挡 → 表现为"看着像正常待机但点一下才恢复"
        var gate = PetGates.EvaluateFollowGate(new PetActivity { Asleep = true });
        Assert.Equal(FollowGate.ResetOnly, gate);
    }

    [Fact]
    public void 站立动作进行中不抢画面()
        => Assert.Equal(FollowGate.Blocked,
            PetGates.EvaluateFollowGate(new PetActivity { StandBusy = true }));

    [Fact]
    public void 小游戏追逐阶段不插手()
        => Assert.Equal(FollowGate.Blocked,
            PetGates.EvaluateFollowGate(new PetActivity { Chase = true, ChaseNear = false }));

    [Fact]
    public void 小游戏近场停下来看时允许跟随()
        => Assert.Equal(FollowGate.Allowed,
            PetGates.EvaluateFollowGate(new PetActivity { Chase = true, ChaseNear = true }));

    [Fact]
    public void 关掉设置要结束跟随()
        => Assert.Equal(FollowGate.EndFollow,
            PetGates.EvaluateFollowGate(new PetActivity { FollowWanted = false }));

    [Fact]
    public void 隐藏要结束跟随()
        => Assert.Equal(FollowGate.EndFollow,
            PetGates.EvaluateFollowGate(new PetActivity { Visible = false }));

    [Fact]
    public void 拖动中要结束跟随()
        => Assert.Equal(FollowGate.EndFollow,
            PetGates.EvaluateFollowGate(new PetActivity { Dragging = true }));

    [Fact]
    public void 缩放中要结束跟随()
        => Assert.Equal(FollowGate.EndFollow,
            PetGates.EvaluateFollowGate(new PetActivity { Resizing = true }));

    [Fact]
    public void 动作进行中只是暂停_不结束跟随()
        => Assert.Equal(FollowGate.Blocked,
            PetGates.EvaluateFollowGate(new PetActivity { FollowPaused = true }));

    [Fact]
    public void 优先级_睡眠高于一切()
        => Assert.Equal(FollowGate.ResetOnly, PetGates.EvaluateFollowGate(new PetActivity
        {
            Asleep = true,
            Dragging = true,
            FollowWanted = false,
            Chase = true,
        }));

    [Fact]
    public void 优先级_站立动作高于追逐()
        => Assert.Equal(FollowGate.Blocked, PetGates.EvaluateFollowGate(new PetActivity
        {
            StandBusy = true,
            Chase = true,
            ChaseNear = false,
        }));

    // ---------------------------------------------------------------- 待机小动作

    [Fact]
    public void 空闲够久才演小动作()
    {
        Assert.False(PetGates.CanIdleAction(new PetActivity { IdleSeconds = 5.9 }));
        Assert.True(PetGates.CanIdleAction(new PetActivity { IdleSeconds = 6 }));
    }

    [Fact]
    public void 睡觉时不许演小动作()
        => Assert.False(PetGates.CanIdleAction(new PetActivity { Asleep = true, IdleSeconds = 60 }));

    [Fact]
    public void 站立时不许演坐姿小动作()
        => Assert.False(PetGates.CanIdleAction(new PetActivity { Standing = true, IdleSeconds = 60 }));

    [Fact]
    public void 拖动时不许演小动作()
        => Assert.False(PetGates.CanIdleAction(new PetActivity { Dragging = true, IdleSeconds = 60 }));

    [Fact]
    public void 上个小动作没播完不许插播()
        => Assert.False(PetGates.CanIdleAction(new PetActivity { Busy = true, IdleSeconds = 60 }));

    // ---------------------------------------------------------------- 姿态轮换

    [Fact]
    public void 姿态轮换要空闲十八秒()
    {
        Assert.False(PetGates.CanChurn(new PetActivity { IdleSeconds = 17.9 }));
        Assert.True(PetGates.CanChurn(new PetActivity { IdleSeconds = 18 }));
    }

    [Fact]
    public void 睡觉不轮换()
        => Assert.False(PetGates.CanChurn(new PetActivity { Asleep = true, IdleSeconds = 60 }));

    [Fact]
    public void 拖动不轮换()
        => Assert.False(PetGates.CanChurn(new PetActivity { Dragging = true, IdleSeconds = 60 }));

    [Fact]
    public void 缩放不轮换()
        => Assert.False(PetGates.CanChurn(new PetActivity { Resizing = true, IdleSeconds = 60 }));

    [Fact]
    public void 站立动作进行中不轮换()
        => Assert.False(PetGates.CanChurn(new PetActivity { StandBusy = true, IdleSeconds = 60 }));

    [Fact]
    public void 小游戏不轮换()
        => Assert.False(PetGates.CanChurn(new PetActivity { Chase = true, IdleSeconds = 60 }));

    [Fact]
    public void 隐藏时不轮换()
        => Assert.False(PetGates.CanChurn(new PetActivity { Visible = false, IdleSeconds = 60 }));

    // ---------------------------------------------------------------- 打盹

    [Fact]
    public void 到达打盹阈值才睡()
    {
        Assert.False(PetGates.CanSleep(new PetActivity { IdleSeconds = 24, SleepAfterSeconds = 25 }));
        Assert.True(PetGates.CanSleep(new PetActivity { IdleSeconds = 25, SleepAfterSeconds = 25 }));
    }

    [Fact]
    public void 已睡不重复睡()
        => Assert.False(PetGates.CanSleep(new PetActivity { Asleep = true, IdleSeconds = 999, SleepAfterSeconds = 25 }));

    [Fact]
    public void 小游戏时不睡()
        => Assert.False(PetGates.CanSleep(new PetActivity { Chase = true, IdleSeconds = 999, SleepAfterSeconds = 25 }));

    [Fact]
    public void 邀约开着不睡()
        => Assert.False(PetGates.CanSleep(new PetActivity { InviteOpen = true, IdleSeconds = 999, SleepAfterSeconds = 25 }));

    [Fact]
    public void 鼠标在动时不睡()
        => Assert.False(PetGates.CanSleep(new PetActivity
        {
            MouseActive = true,
            IdleSeconds = 999,
            SleepAfterSeconds = 25,
        }));

    [Fact]
    public void 正忙时不睡()
        => Assert.False(PetGates.CanSleep(new PetActivity { Busy = true, IdleSeconds = 999, SleepAfterSeconds = 25 }));

    [Fact]
    public void 没素材不睡()
        => Assert.False(PetGates.CanSleep(new PetActivity
        {
            HasSleepClip = false,
            IdleSeconds = 999,
            SleepAfterSeconds = 25,
        }));

    // ---------------------------------------------------------------- 小游戏 tick

    [Fact]
    public void 小游戏tick在正常情况下能动()
        => Assert.True(PetGates.CanChaseTick(new PetActivity()));

    [Fact]
    public void 小游戏tick在拖动缩放睡眠隐藏时冻结()
    {
        Assert.False(PetGates.CanChaseTick(new PetActivity { Dragging = true }));
        Assert.False(PetGates.CanChaseTick(new PetActivity { Resizing = true }));
        Assert.False(PetGates.CanChaseTick(new PetActivity { Asleep = true }));
        Assert.False(PetGates.CanChaseTick(new PetActivity { Visible = false }));
    }
}
