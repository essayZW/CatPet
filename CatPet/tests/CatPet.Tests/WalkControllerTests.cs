using CatPet.Core;
using Xunit;

namespace CatPet.Tests;

/// <summary>
/// 行走位移状态机。
/// 回归背景：①孤儿位移（动作结束了窗口还在滑） ②越权收尾（旧动作把新动作的位移停掉）。
/// </summary>
public class WalkControllerTests
{
    private const double ScreenLeft = 0;
    private const double ScreenRight = 1920;
    private const double Width = 226;

    private static WalkStep Tick(WalkController w, double dt, double left, bool ownerActive = true)
        => w.Tick(dt, left, Width, ScreenLeft, ScreenRight, ownerActive);

    [Fact]
    public void 没走时tick什么都不做()
    {
        var w = new WalkController();
        var step = Tick(w, 0.03, 500);
        Assert.Equal(WalkTickKind.Idle, step.Kind);
        Assert.Equal(0, step.Dx);
        Assert.Equal(500, step.NextLeft);
    }

    [Fact]
    public void 按速度和方向前进()
    {
        var w = new WalkController();
        w.Start(-1);                                   // 往左，默认 48px/s
        var step = Tick(w, 0.1, 900);                  // 0.1s → 4.8px
        Assert.Equal(WalkTickKind.Moved, step.Kind);
        Assert.Equal(-4.8, step.Dx, 3);
        Assert.Equal(895.2, step.NextLeft, 3);
    }

    [Fact]
    public void 速度可覆盖()
    {
        var w = new WalkController();
        w.Start(1, WalkController.RunPxPerSecond);
        var step = Tick(w, 0.1, 500);
        Assert.Equal(15.0, step.Dx, 3);
    }

    [Fact]
    public void 单次tick最多走015秒()
    {
        var w = new WalkController();
        w.Start(1, WalkController.GallopPxPerSecond);
        var step = Tick(w, 5.0, 500);                 // 卡顿 5 秒
        Assert.Equal(430 * 0.15, step.Dx, 3);          // 只推进 0.15s
    }

    [Fact]
    public void 撞左边自动停下且不移动()
    {
        var w = new WalkController();
        w.Start(-1);
        var step = Tick(w, 0.1, 2);                    // 走一步就出界
        Assert.Equal(WalkTickKind.HitEdge, step.Kind);
        Assert.Equal(0, step.Dx);
        Assert.Equal(2, step.NextLeft);                // 位置不动
        Assert.False(w.IsWalking);
    }

    [Fact]
    public void 撞右边自动停下且不移动()
    {
        var w = new WalkController();
        w.Start(1);
        var step = w.Tick(0.1, ScreenRight - Width - 2, Width, ScreenLeft, ScreenRight);
        Assert.Equal(WalkTickKind.HitEdge, step.Kind);
        Assert.Equal(ScreenRight - Width - 2, step.NextLeft);
        Assert.False(w.IsWalking);
    }

    [Fact]
    public void 发起动作结束了就停_不留孤儿位移()
    {
        var w = new WalkController();
        w.Start(1);
        Tick(w, 0.03, 500);                            // 正常走了一帧
        var step = Tick(w, 0.03, 500, ownerActive: false);
        Assert.Equal(WalkTickKind.OrphanStopped, step.Kind);
        Assert.False(w.IsWalking);
    }

    [Fact]
    public void 重启走会先停上一次并换新代际()
    {
        var w = new WalkController();
        var g1 = w.Start(1);
        var g2 = w.Start(-1, WalkController.RunPxPerSecond);
        Assert.NotEqual(g1, g2);
        Assert.True(w.IsWalking);
        Assert.Equal(-1, w.Direction);
        Assert.Equal(WalkController.RunPxPerSecond, w.Speed);
    }

    [Fact]
    public void 旧代际收尾不许停掉新位移()
    {
        var w = new WalkController();
        var oldGen = w.Start(1);
        var newGen = w.Start(-1);                      // 新动作接管

        Assert.False(w.StopIfCurrent(oldGen));          // 旧动作收尾：越权，拒绝
        Assert.True(w.IsWalking);                       // 新位移还在

        Assert.True(w.StopIfCurrent(newGen));           // 自己的代际才能停
        Assert.False(w.IsWalking);
    }

    [Fact]
    public void 停两次只有第一次算数()
    {
        var w = new WalkController();
        w.Start(1);
        Assert.True(w.Stop());
        Assert.False(w.Stop());
    }

    [Fact]
    public void 已经停了之后旧代际收尾也不报错()
    {
        var w = new WalkController();
        var gen = w.Start(1);
        w.Stop();
        Assert.False(w.StopIfCurrent(gen));
    }

    [Fact]
    public void 走一整屏会撞边()
    {
        var w = new WalkController();
        w.Start(-1, 1000);
        var left = 900.0;
        var guard = 0;
        while (w.IsWalking && guard++ < 100)
        {
            var step = Tick(w, 0.05, left);
            left = step.NextLeft;
            if (step.Kind == WalkTickKind.HitEdge) break;
        }
        Assert.False(w.IsWalking);
        Assert.True(guard < 100);
    }

    [Fact]
    public void 同方向只改速度不换代际()
    {
        var w = new WalkController();
        var gen = w.Start(1);
        w.SetSpeed(400);
        Assert.Equal(gen, w.Generation);      // 代际不变：追逐时每帧调速不会误判"被接管"
        Assert.Equal(400, w.Speed);
        Assert.True(w.IsWalking);

        w.SetSpeed(-5);                        // 非法速度忽略
        Assert.Equal(400, w.Speed);
    }

    [Fact]
    public void 没在走时改速度无效()
    {
        var w = new WalkController();
        w.SetSpeed(999);
        Assert.Equal(WalkController.WalkPxPerSecond, w.Speed);
    }

    [Fact]
    public void 默认速度就是48()
        => Assert.Equal(48, WalkController.WalkPxPerSecond);
}
