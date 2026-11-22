using CatPet.Core;
using Xunit;

namespace CatPet.Tests;

/// <summary>
/// 运镜时间线与选边规则。
/// 回归背景：气泡要挂到猫消失、开场不能有空档、素材缺失时用另一侧兜底。
/// </summary>
public class CinematicTimelineTests
{
    [Fact]
    public void 离哪边近用哪边()
    {
        Assert.True(CinematicTimeline.PreferRightSide(roomLeft: 800, roomRight: 200));
        Assert.False(CinematicTimeline.PreferRightSide(roomLeft: 200, roomRight: 800));
        Assert.True(CinematicTimeline.PreferRightSide(500, 500));      // 一样近取右侧
    }

    [Fact]
    public void 首选素材按侧边选()
    {
        Assert.Equal(CinematicTimeline.IntroRight, CinematicTimeline.PreferredIntro(true));
        Assert.Equal(CinematicTimeline.IntroLeft, CinematicTimeline.PreferredIntro(false));
        Assert.Equal(CinematicTimeline.ExitRight, CinematicTimeline.PreferredExit(true));
        Assert.Equal(CinematicTimeline.ExitLeft, CinematicTimeline.PreferredExit(false));
    }

    [Fact]
    public void 缺一侧素材就用另一侧()
    {
        // 只有左侧素材
        Assert.Equal(CinematicTimeline.IntroLeft,
            CinematicTimeline.ChooseClip(CinematicTimeline.IntroRight, c => c == CinematicTimeline.IntroLeft));

        // 首选就有
        Assert.Equal(CinematicTimeline.ExitRight,
            CinematicTimeline.ChooseClip(CinematicTimeline.ExitRight, _ => true));

        // 两边都没有
        Assert.Null(CinematicTimeline.ChooseClip(CinematicTimeline.ExitRight, _ => false));
    }

    [Fact]
    public void 镜像映射是双向的()
    {
        foreach (var (a, b) in new[]
        {
            (CinematicTimeline.IntroRight, CinematicTimeline.IntroLeft),
            (CinematicTimeline.ExitRight, CinematicTimeline.ExitLeft),
        })
        {
            Assert.Equal(b, CinematicTimeline.Mirror(a));
            Assert.Equal(a, CinematicTimeline.Mirror(b));
        }
    }

    [Fact]
    public void 朝向按素材决定()
    {
        // 从左进来 → 朝右走
        Assert.True(CinematicTimeline.FacingRightForIntro(CinematicTimeline.IntroLeft));
        Assert.False(CinematicTimeline.FacingRightForIntro(CinematicTimeline.IntroRight));

        // 往右走掉 → 朝右（背对镜头）
        Assert.True(CinematicTimeline.FacingRightForExit(CinematicTimeline.ExitRight));
        Assert.False(CinematicTimeline.FacingRightForExit(CinematicTimeline.ExitLeft));
    }

    [Fact]
    public void 运镜素材长度是44帧45ms()
    {
        Assert.Equal(45, CinematicTimeline.CineFrameMs);
        Assert.Equal(44, CinematicTimeline.CineFrameCount);
        Assert.Equal(1980, CinematicTimeline.CineClipMs);
    }

    [Fact]
    public void 退场气泡要盖住整个退场()
    {
        // 520ms 停顿 + 1980ms 片长 = 2500ms < 3.2s 气泡 ✓
        Assert.Equal(2500, CinematicTimeline.ExitTotalMs(CinematicTimeline.CineClipMs));
        Assert.True(CinematicTimeline.BubbleCoversExit(CinematicTimeline.CineClipMs));
    }

    [Fact]
    public void 开场跟随让位要盖住片长()
        => Assert.True(CinematicTimeline.IntroFollowPauseSeconds * 1000 >= CinematicTimeline.CineClipMs);

    [Fact]
    public void 退场跟随让位要盖住气泡()
        => Assert.True(CinematicTimeline.ExitFollowPauseSeconds >= CinematicTimeline.ExitBubbleSeconds);

    [Fact]
    public void 没有退场素材时也要有个兜底停顿()
        => Assert.InRange(CinematicTimeline.ExitNoClipDelayMs, 50, 1000);

    [Fact]
    public void 告别话不带喵以外的废话()
    {
        Assert.Contains("拜拜", CinematicTimeline.ExitText);
        Assert.StartsWith("（", CinematicTimeline.ExitText);
        Assert.EndsWith("）", CinematicTimeline.ExitText);
    }
}
