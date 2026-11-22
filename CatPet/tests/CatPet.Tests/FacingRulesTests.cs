using CatPet.Core;
using Xunit;

namespace CatPet.Tests;

/// <summary>
/// 朝向规则：哪批素材算"朝左"。
/// 回归用例来自真实 bug：开着头跟随时"贴边转身"被立刻撤销。
/// </summary>
public class FacingRulesTests
{
    [Theory]
    [InlineData("sidle")]
    [InlineData("slook")]
    [InlineData("swag")]
    [InlineData("sclose")]
    [InlineData("sjump")]
    [InlineData("sjump_diag")]
    [InlineData("sfollow")]
    public void 不带后缀的站立素材都是朝左(string clip)
        => Assert.False(PetLogic.FacingAfterClip(faceRight: true, clip));

    [Theory]
    [InlineData("sidle_r")]
    [InlineData("slook_r")]
    [InlineData("sfollow_r")]
    [InlineData("swalk_r")]
    [InlineData("srun_r")]
    public void _r_结尾朝右(string clip)
        => Assert.True(PetLogic.FacingAfterClip(faceRight: false, clip));

    [Theory]
    [InlineData("swalk_l")]
    [InlineData("srun_l")]
    [InlineData("sgal_l")]
    [InlineData("cin_intro_l")]
    public void _l_结尾朝左(string clip)
        => Assert.False(PetLogic.FacingAfterClip(faceRight: true, clip));

    [Theory]
    [InlineData("idle")]
    [InlineData("say")]
    [InlineData("follow")]
    [InlineData("sleep")]
    public void 无关素材保持原朝向(string clip)
    {
        Assert.True(PetLogic.FacingAfterClip(faceRight: true, clip));
        Assert.False(PetLogic.FacingAfterClip(faceRight: false, clip));
    }

    [Fact]
    public void 转身后跟随图集应该选到朝左的那套()
    {
        // 贴右边、朝右 → 播 sidle 转身
        var faceRight = PetLogic.FacingAfterClip(true, "sidle");
        Assert.False(faceRight);

        // 跟随接管时按朝向选图集：应当选 sfollow（朝左），不是 sfollow_r
        var atlas = PetLogic.SideClip(faceRight, "sfollow", "sfollow_r", _ => true);
        Assert.Equal("sfollow", atlas);
    }

    [Fact]
    public void 朝右但缺右侧素材时退回左侧()
        => Assert.Equal("sfollow", PetLogic.SideClip(true, "sfollow", "sfollow_r", _ => false));
}
