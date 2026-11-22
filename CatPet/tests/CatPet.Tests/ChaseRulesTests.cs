using CatPet.Core;
using Xunit;

namespace CatPet.Tests;

/// <summary>
/// 追鼠标小游戏的纯规则：分区、抓取资格、目标速度/平滑、步态滞回、跳跃判定。
/// </summary>
public class ChaseRulesTests
{
    // ---------------------------------------------------------------- 接触与抓取

    [Fact]
    public void 身体椭圆内算贴上()
    {
        // 226 宽、283 高（气泡带 40）→ 半宽 94.9、半高 97.2
        var (hw, hh) = ChaseRules.BodyEllipse(226, 243);
        Assert.True(ChaseRules.IsTouching(0, 0, hw, hh));       // 正中
        Assert.True(ChaseRules.IsTouching(hw * 0.9, 0, hw, hh));
        Assert.False(ChaseRules.IsTouching(hw * 1.05, 0, hw, hh));
        Assert.False(ChaseRules.IsTouching(0, hh * 1.05, hw, hh));
    }

    [Fact]
    public void 椭圆的对角线比单轴更严格()
    {
        var (hw, hh) = ChaseRules.BodyEllipse(226, 243);
        // 0.7² + 0.7² = 0.98 ≤ 1 → 还在里面
        Assert.True(ChaseRules.IsTouching(hw * 0.7, hh * 0.7, hw, hh));
        // 0.75² + 0.75² = 1.125 > 1 → 出去了
        Assert.False(ChaseRules.IsTouching(hw * 0.75, hh * 0.75, hw, hh));
    }

    [Fact]
    public void 椭圆有最小尺寸下限()
    {
        var (hw, hh) = ChaseRules.BodyEllipse(10, 10);
        Assert.Equal(20, hw);
        Assert.Equal(20, hh);
    }

    [Fact]
    public void 没在追时鼠标凑过来不算抓到()
        => Assert.False(ChaseRules.CanCatch(touching: true, chasing: false, sinceStartMs: 5000, cooldownPassed: true));

    [Fact]
    public void 开局宽限期内不算抓到()
        => Assert.False(ChaseRules.CanCatch(true, true, sinceStartMs: 500, cooldownPassed: true));

    [Fact]
    public void 冷却中不算抓到()
        => Assert.False(ChaseRules.CanCatch(true, true, sinceStartMs: 5000, cooldownPassed: false));

    [Fact]
    public void 追着且贴上且过了宽限期才算抓到()
        => Assert.True(ChaseRules.CanCatch(true, true, sinceStartMs: 900, cooldownPassed: true));

    // ---------------------------------------------------------------- 分区

    [Fact]
    public void 很近且停稳时站着看鼠标()
        => Assert.True(ChaseRules.ShouldWatchMouse(idle: true, dist: 100, deltaY: 0));

    [Fact]
    public void 还在移动时不进看鼠标模式()
        => Assert.False(ChaseRules.ShouldWatchMouse(idle: false, dist: 100, deltaY: 0));

    [Fact]
    public void 纵向差太大不算近()
        => Assert.False(ChaseRules.ShouldWatchMouse(idle: true, dist: 100, deltaY: 200));

    [Fact]
    public void 到了鼠标正下方就停()
    {
        Assert.True(ChaseRules.ShouldStopAtMouse(dist: 20, chasing: true));
        Assert.False(ChaseRules.ShouldStopAtMouse(dist: 40, chasing: true));
        Assert.False(ChaseRules.ShouldStopAtMouse(dist: 20, chasing: false));
    }

    // ---------------------------------------------------------------- 方向

    [Fact]
    public void 方向死区保持原方向()
    {
        Assert.Equal(1, ChaseRules.DirectionFor(delta: 10, previousDirection: 1));
        Assert.Equal(-1, ChaseRules.DirectionFor(delta: -10, previousDirection: -1));
        Assert.Equal(1, ChaseRules.DirectionFor(delta: 100, previousDirection: -1));   // 超出死区就转向
        Assert.Equal(-1, ChaseRules.DirectionFor(delta: -100, previousDirection: 1));
    }

    [Fact]
    public void 原本停着时按鼠标方向()
    {
        Assert.Equal(1, ChaseRules.DirectionFor(5, 0));
        Assert.Equal(-1, ChaseRules.DirectionFor(-5, 0));
    }

    // ---------------------------------------------------------------- 速度与步态

    [Fact]
    public void 目标速度随距离增长且封顶()
    {
        Assert.Equal(ChaseRules.MinChaseSpeed, ChaseRules.TargetSpeed(dist: 0, mouseVelocity: 0));
        Assert.Equal(110, ChaseRules.TargetSpeed(dist: 100, mouseVelocity: 0), 3);
        Assert.Equal(ChaseRules.MaxChaseSpeed, ChaseRules.TargetSpeed(dist: 5000, mouseVelocity: 0));
    }

    [Fact]
    public void 鼠标甩得快会加速()
        => Assert.True(ChaseRules.TargetSpeed(100, 400) > ChaseRules.TargetSpeed(100, 0));

    [Fact]
    public void 速度平滑不会突变且不低于下限()
    {
        var s = ChaseRules.SmoothSpeed(0, 400);
        Assert.Equal(80, s, 3);                    // 0 + 400*0.2
        Assert.True(ChaseRules.SmoothSpeed(0, 0) >= 10);
    }

    [Fact]
    public void 步态阈值()
    {
        Assert.Equal(ChaseGait.Walk, ChaseRules.GaitFor(100));
        Assert.Equal(ChaseGait.Run, ChaseRules.GaitFor(200));
        Assert.Equal(ChaseGait.Gallop, ChaseRules.GaitFor(400));
    }

    [Fact]
    public void 步态有滞回_小跑不会因为掉速立刻切回走()
    {
        // 进入跑要 >140；已经在跑则要掉到 ≤110 才切回走（滞回缓冲 30）
        Assert.Equal(ChaseGait.Run, ChaseRules.GaitFor(150, ChaseGait.Run));
        Assert.Equal(ChaseGait.Run, ChaseRules.GaitFor(150));
        Assert.Equal(ChaseGait.Walk, ChaseRules.GaitFor(100, ChaseGait.Run));

        // 进入飞奔要 >320；已经在飞奔则要掉到 ≤290 才切回跑
        Assert.Equal(ChaseGait.Gallop, ChaseRules.GaitFor(330, ChaseGait.Gallop));
        Assert.Equal(ChaseGait.Gallop, ChaseRules.GaitFor(330));
        Assert.Equal(ChaseGait.Run, ChaseRules.GaitFor(280, ChaseGait.Gallop));
    }

    [Fact]
    public void 步态素材按朝向选()
    {
        Assert.Equal("swalk_r", ChaseRules.GaitClip(ChaseGait.Walk, 1));
        Assert.Equal("srun_l", ChaseRules.GaitClip(ChaseGait.Run, -1));
        Assert.Equal("sgal_r", ChaseRules.GaitClip(ChaseGait.Gallop, 1));
    }

    // ---------------------------------------------------------------- 跳跃

    [Fact]
    public void 跳跃高度按纵向差算_最少90()
    {
        Assert.Equal(90, ChaseRules.JumpNeed(-100));      // -100-45 = 145? 不，-200 才 155
        Assert.Equal(155, ChaseRules.JumpNeed(-200));
    }

    [Fact]
    public void 不需要跳到鼠标中心()
        => Assert.True(ChaseRules.JumpNeed(-300) < 300);

    [Fact]
    public void 超过跳跃上限算够不着()
        => Assert.True(ChaseRules.TooHigh(need: 400, spaceAbove: 2000));

    [Fact]
    public void 上方空间不够算够不着()
        => Assert.True(ChaseRules.TooHigh(need: 150, spaceAbove: 100));

    [Fact]
    public void 空间充足且不太高就能跳()
        => Assert.False(ChaseRules.TooHigh(need: 150, spaceAbove: 1000));

    [Fact]
    public void 纵向够高才考虑跳()
    {
        Assert.False(ChaseRules.ShouldConsiderJump(deltaY: -100, deltaX: 50, jumpCooldownPassed: true));
        Assert.True(ChaseRules.ShouldConsiderJump(deltaY: -200, deltaX: 50, jumpCooldownPassed: true));
        Assert.False(ChaseRules.ShouldConsiderJump(deltaY: -200, deltaX: 50, jumpCooldownPassed: false));
        Assert.False(ChaseRules.ShouldConsiderJump(deltaY: -200, deltaX: 500, jumpCooldownPassed: true));
    }

    [Fact]
    public void 够得着就跳()
        => Assert.Equal(JumpDecision.Jump,
            ChaseRules.DecideJump(deltaY: -250, dist: 50, canMoveX: true, jumpTries: 0, needsMove: false, spaceAbove: 1000));

    [Fact]
    public void 够不着但还远就先走近()
        => Assert.Equal(JumpDecision.ApproachFirst,
            ChaseRules.DecideJump(deltaY: -250, dist: 300, canMoveX: true, jumpTries: 0, needsMove: true, spaceAbove: 1000));

    [Fact]
    public void 够不着又已经走近就放弃()
        => Assert.Equal(JumpDecision.GiveUp,
            ChaseRules.DecideJump(deltaY: -250, dist: 50, canMoveX: true, jumpTries: 3, needsMove: false, spaceAbove: 1000));

    [Fact]
    public void 跳太多次就放弃()
        => Assert.Equal(JumpDecision.GiveUp,
            ChaseRules.DecideJump(deltaY: -250, dist: 50, canMoveX: false, jumpTries: 2, needsMove: false, spaceAbove: 1000));
}
