using CatPet.Core;
using Xunit;

namespace CatPet.Tests;

/// <summary>
/// 打盹状态机。
/// 回归背景：以前"睡着"是个裸 bool，打盹动画播完回待机但标志位没清，
/// 跟随/小动作/轮换全被挡 —— 表现为"看着像正常待机，点一下才恢复"。
/// </summary>
public class SleepMachineTests
{
    private const double NightThreshold = 25;

    [Fact]
    public void 初始是醒着()
    {
        var m = new SleepMachine();
        Assert.False(m.Asleep);
        Assert.Equal(SleepPhase.Awake, m.Phase);
    }

    [Fact]
    public void 空闲到阈值就睡着_并且只触发一次()
    {
        var m = new SleepMachine();
        Assert.Equal(SleepEvent.None, m.CheckFallAsleep(24.9, NightThreshold));
        Assert.Equal(SleepEvent.FellAsleep, m.CheckFallAsleep(25, NightThreshold));
        Assert.True(m.Asleep);

        // 睡着之后不管空闲多久都不再"重新睡"
        Assert.Equal(SleepEvent.None, m.CheckFallAsleep(9999, NightThreshold));
        Assert.True(m.Asleep);
    }

    [Fact]
    public void 门禁不允许时不睡()
    {
        var m = new SleepMachine();
        Assert.Equal(SleepEvent.None, m.CheckFallAsleep(999, NightThreshold, canSleep: false));
        Assert.False(m.Asleep);
    }

    [Fact]
    public void 有人交互才醒_并且只触发一次()
    {
        var m = new SleepMachine();
        m.CheckFallAsleep(25, NightThreshold);

        Assert.Equal(SleepEvent.Woke, m.RequestWake());
        Assert.False(m.Asleep);
        Assert.Equal(SleepEvent.None, m.RequestWake());   // 醒了再点，不该再播"抬头"
    }

    [Fact]
    public void 没睡时点击不触发唤醒()
    {
        var m = new SleepMachine();
        Assert.Equal(SleepEvent.None, m.RequestWake());
    }

    [Fact]
    public void 睡过醒来还能再睡()
    {
        var m = new SleepMachine();
        Assert.Equal(SleepEvent.FellAsleep, m.CheckFallAsleep(25, NightThreshold));
        m.RequestWake();
        Assert.Equal(SleepEvent.FellAsleep, m.CheckFallAsleep(25, NightThreshold));
    }

    [Fact]
    public void 别处的门禁决定不了睡眠_只影响这一次检查()
    {
        var m = new SleepMachine();
        m.CheckFallAsleep(999, NightThreshold, canSleep: false);
        Assert.False(m.Asleep);

        // 门禁恢复了就能睡
        Assert.Equal(SleepEvent.FellAsleep, m.CheckFallAsleep(999, NightThreshold, canSleep: true));
    }

    [Fact]
    public void 复位回到醒着()
    {
        var m = new SleepMachine();
        m.CheckFallAsleep(999, NightThreshold);
        m.Reset();
        Assert.False(m.Asleep);
    }


    [Fact]
    public void 站立时门禁允许也先不睡_由调用方安排坐下()
    {
        // CheckSleep 的实现约定：站立且"可以睡"时先坐下来（Say + ReturnSittingAsync），
        // 真正进入睡眠要等坐姿下的下一次检查。这里固化"门禁只看睡眠条件，不看站立"这一点，
        // 避免以后把 _standing 塞进 PetGates.CanSleep 导致站姿永远睡不着。
        var standing = new PetActivity { Standing = true, IdleSeconds = 999, SleepAfterSeconds = 25 };
        Assert.True(PetGates.CanSleep(standing));

        var m = new SleepMachine();
        // 调用方在站立分支里直接 return，不会调用 CheckFallAsleep
        Assert.Equal(SleepEvent.None, m.RequestWake());
        Assert.False(m.Asleep);
    }
    [Fact]
    public void 睡着期间跟随与小动作都被门禁挡住()
    {
        var m = new SleepMachine();
        m.CheckFallAsleep(25, NightThreshold);
        var a = new PetActivity { Asleep = m.Asleep, IdleSeconds = 999 };

        Assert.Equal(FollowGate.ResetOnly, PetGates.EvaluateFollowGate(a));
        Assert.False(PetGates.CanIdleAction(a));
        Assert.False(PetGates.CanChurn(a));
        Assert.False(PetGates.CanChaseTick(a));
    }
}
