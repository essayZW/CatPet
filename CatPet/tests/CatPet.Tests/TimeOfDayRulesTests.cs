using CatPet.Core;
using Xunit;

namespace CatPet.Tests;

/// <summary>
/// 时段相关规则：待机掷骰映射、打盹阈值、发呆口气。
/// 都是"按小时查表"的纯函数，最适合单测——以前只能改系统时间才能验证。
/// </summary>
public class TimeOfDayRulesTests
{
    [Theory]
    [InlineData(8)]    // 早上
    [InlineData(13)]   // 午后
    [InlineData(20)]   // 白天
    [InlineData(2)]    // 深夜
    public void 掷骰结果永远在合法档位内(int hour)
    {
        for (var r = 0; r < 11; r++)
        {
            var mapped = PetLogic.IdleRoll(hour, r);
            Assert.InRange(mapped, 0, 10);
        }
    }

    [Fact]
    public void 早上把发呆档改掉()
    {
        Assert.Equal(6, PetLogic.IdleRoll(8, 2));
        Assert.Equal(6, PetLogic.IdleRoll(8, 3));
        Assert.Equal(0, PetLogic.IdleRoll(8, 7));
    }

    [Fact]
    public void 午后更容易发呆()
    {
        Assert.Equal(7, PetLogic.IdleRoll(13, 6));
        Assert.Equal(2, PetLogic.IdleRoll(13, 5));
        Assert.Equal(2, PetLogic.IdleRoll(13, 1));
    }

    [Fact]
    public void 深夜更多发呆()
    {
        foreach (var r in new[] { 1, 3, 5, 8 })
            Assert.Equal(7, PetLogic.IdleRoll(23, r));
        Assert.Equal(7, PetLogic.IdleRoll(2, 7));
    }

    [Fact]
    public void 白天不改映射()
    {
        for (var r = 0; r < 11; r++)
            Assert.Equal(r, PetLogic.IdleRoll(16, r));
    }

    [Theory]
    [InlineData(23, 25.0)]
    [InlineData(2, 25.0)]
    [InlineData(13, 35.0)]
    [InlineData(7, 55.0)]
    [InlineData(16, 45.0)]
    public void 打盹阈值按时段(int hour, double expected)
        => Assert.Equal(expected, PetLogic.SleepAfterSeconds(hour));

    [Theory]
    [InlineData(23, GazeMood.Night)]
    [InlineData(3, GazeMood.Night)]
    [InlineData(13, GazeMood.Drowsy)]
    [InlineData(9, GazeMood.Normal)]
    [InlineData(18, GazeMood.Normal)]
    public void 发呆口气按时段(int hour, GazeMood expected)
        => Assert.Equal(expected, PetLogic.GazeMoodForHour(hour));

    [Fact]
    public void 跟随期间只放生活化小动作_发呆不在内()
    {
        Assert.False(PetLogic.IsLivingAction(7));      // 发呆
        Assert.False(PetLogic.IsLivingAction(0));      // 转头
        Assert.False(PetLogic.IsLivingAction(8));      // 盯着鼠标
        foreach (var ok in new[] { 1, 2, 3, 5, 6 })
            Assert.True(PetLogic.IsLivingAction(ok));
    }

    [Fact]
    public void 走路步频与速度匹配()
    {
        // 48px/s、步幅 34.6px/圈、30 帧/圈 → 约 24ms/帧（现在的 swalk 就是这个值）
        var ms = PetLogic.FrameMsForSpeed(48);
        Assert.InRange(ms, 23.5, 24.5);
    }
}
