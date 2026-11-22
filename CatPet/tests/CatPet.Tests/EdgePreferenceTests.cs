using CatPet.Core;
using Xunit;

namespace CatPet.Tests;

/// <summary>
/// 贴边偏好：离边缘远了要回去、贴边朝外要转身、被拖回边上就不再触发。
/// 这些规则以前埋在 1800 行的窗口类里，只能靠肉眼验证。
/// </summary>
public class EdgePreferenceTests
{
    private const double W = 226;               // 默认窗口宽度
    private const double ScreenWidth = 1920;    // 两边剩余空间按这个算

    /// <summary>按窗口左边缘算两侧剩余空间（Left, Right）。</summary>
    private static (double Left, double Right) Room(double left) => (left, ScreenWidth - left - W);

    [Fact]
    public void 在屏幕中间判定为离边缘远()
    {
        var (l, r) = Room(847);
        Assert.True(PetLogic.IsAwayFromEdges(l, r));
        Assert.False(PetLogic.IsNearLeftEdge(l));
        Assert.False(PetLogic.IsNearRightEdge(r));
    }

    [Fact]
    public void 贴右边缘判定为近右()
    {
        var (l, r) = Room(ScreenWidth - W - 8);
        Assert.True(PetLogic.IsNearRightEdge(r));
        Assert.False(PetLogic.IsAwayFromEdges(l, r));
    }

    [Fact]
    public void 在中间要往回走且距离远时用跑()
    {
        var (l, r) = Room(847);   // 两侧都 847
        var edge = PetLogic.EvaluateEdge(l, r, standing: true, faceRight: false,
                                         focusMode: false, rollTurn: 1.0, rollGoBack: 0.0);
        Assert.Equal(EdgeDecision.GoBack, edge.Decision);
        Assert.True(edge.Fast);          // 847 > 320 → 跑
        Assert.False(edge.GoLeft);       // 两边一样远时取右
    }

    [Fact]
    public void 往更近的那条边走()
    {
        var (l, r) = Room(1000);   // 离左 1000、离右 694 → 应该往右
        var edge = PetLogic.EvaluateEdge(l, r, true, false, false, 1.0, 0.0);
        Assert.Equal(EdgeDecision.GoBack, edge.Decision);
        Assert.False(edge.GoLeft);
        Assert.True(edge.Fast);
    }

    [Fact]
    public void 距离不远时走过去而不是跑()
    {
        var (l, r) = Room(300);    // 离左 300（>260 算远），距离 300 < 320 → 走
        var edge = PetLogic.EvaluateEdge(l, r, true, false, false, 1.0, 0.0);
        Assert.Equal(EdgeDecision.GoBack, edge.Decision);
        Assert.True(edge.GoLeft);
        Assert.False(edge.Fast);
    }

    [Fact]
    public void 回边概率未命中时不动()
    {
        var (l, r) = Room(847);
        var edge = PetLogic.EvaluateEdge(l, r, true, false, false, 1.0, 0.99);
        Assert.Equal(EdgeDecision.None, edge.Decision);
    }

    [Fact]
    public void 贴右边朝右要转身()
    {
        var (l, r) = Room(ScreenWidth - W - 8);
        var edge = PetLogic.EvaluateEdge(l, r, standing: true, faceRight: true,
                                         focusMode: false, rollTurn: 0.0, rollGoBack: 1.0);
        Assert.Equal(EdgeDecision.TurnAway, edge.Decision);
    }

    [Fact]
    public void 贴右边朝左是正确姿势不纠正()
    {
        var (l, r) = Room(ScreenWidth - W - 8);
        var edge = PetLogic.EvaluateEdge(l, r, standing: true, faceRight: false,
                                         focusMode: false, rollTurn: 0.0, rollGoBack: 0.0);
        Assert.Equal(EdgeDecision.None, edge.Decision);
    }

    [Fact]
    public void 贴左边朝左要转身且换成朝右素材()
    {
        var (l, r) = Room(8);
        Assert.True(PetLogic.IsNearLeftEdge(l));
        var edge = PetLogic.EvaluateEdge(l, r, standing: true, faceRight: false,
                                         focusMode: false, rollTurn: 0.0, rollGoBack: 1.0);
        Assert.Equal(EdgeDecision.TurnAway, edge.Decision);
        Assert.Equal("sidle_r", PetLogic.InwardClip("sidle", nearLeftEdge: true));
        Assert.Equal("sidle", PetLogic.InwardClip("sidle", nearLeftEdge: false));
    }

    [Fact]
    public void 坐姿不转身()
    {
        var (l, r) = Room(ScreenWidth - W - 8);
        var edge = PetLogic.EvaluateEdge(l, r, standing: false, faceRight: true,
                                         focusMode: false, rollTurn: 0.0, rollGoBack: 1.0);
        Assert.Equal(EdgeDecision.None, edge.Decision);
    }

    [Fact]
    public void 专注模式下不纠正()
    {
        var (l, r) = Room(847);
        Assert.Equal(EdgeDecision.None,
            PetLogic.EvaluateEdge(l, r, true, true, focusMode: true, rollTurn: 0.0, rollGoBack: 0.0).Decision);
    }

    [Fact]
    public void 被手动拖回边上之后不再要求回边()
    {
        // 用户把猫从中间拖到右下角 → 已经贴边，下一次轮换不该再"回去"
        var (l, r) = Room(ScreenWidth - W - 8);
        Assert.Equal(EdgeDecision.None,
            PetLogic.EvaluateEdge(l, r, true, false, false, 0.0, 0.0).Decision);
    }


    // ---------------------------------------------------------------- 概率来自常量

    [Fact]
    public void 转身的默认概率就是TurnAwayChance()
    {
        var (l, r) = Room(ScreenWidth - W - 8);          // 贴右边缘、朝右 → 该转身
        var atChance = PetLogic.TurnAwayChance;

        // 刚好小于概率 → 转身；刚好大于概率 → 不转身
        Assert.Equal(EdgeDecision.TurnAway,
            PetLogic.EvaluateEdge(l, r, true, true, false, atChance - 0.01, 1.0).Decision);
        Assert.Equal(EdgeDecision.None,
            PetLogic.EvaluateEdge(l, r, true, true, false, atChance + 0.01, 1.0).Decision);
    }

    [Fact]
    public void 回边的默认概率就是GoBackChance()
    {
        var (l, r) = Room(847);                          // 两侧都远 → 该回边
        var atChance = PetLogic.GoBackChance;

        Assert.Equal(EdgeDecision.GoBack,
            PetLogic.EvaluateEdge(l, r, false, false, false, 1.0, atChance - 0.01).Decision);
        Assert.Equal(EdgeDecision.None,
            PetLogic.EvaluateEdge(l, r, false, false, false, 1.0, atChance + 0.01).Decision);
    }

    [Fact]
    public void 概率常量与历史值一致()
    {
        // 这两个数原来是硬编码在窗口里的 0.8 / 0.7，改动要同步这里的用例
        Assert.Equal(0.8, PetLogic.TurnAwayChance);
        Assert.Equal(0.7, PetLogic.GoBackChance);
    }
}
