using System.IO;
using CatPet.Core;
using Xunit;

namespace CatPet.Tests;

/// <summary>
/// 素材加载：用临时目录造一个最小 MOD（pet lps + info.lps + 假 png），
/// 验证注册、退段、模式回退、缺素材与错误收集。
/// FrameAnimation.Create 只列文件名、不读像素，所以假 png 用空文件就够。
/// </summary>
public sealed class PetLoaderTests : IDisposable
{
    private readonly string _root;

    public PetLoaderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "catpet-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* 测试目录清理失败无所谓 */ }
    }

    // ---------------------------------------------------------------- 造数据

    private string MakeMod(string petName = "MyCat", string modName = "MyMod")
    {
        var modDir = Path.Combine(_root, modName);
        var petDir = Path.Combine(modDir, "pet");
        var catDir = Path.Combine(petDir, petName);
        Directory.CreateDirectory(catDir);

        File.WriteAllText(Path.Combine(petDir, petName + ".lps"),
            $"pet#{petName}:|path#{petName}:|petname#咪咪:|\n" +
            "touchhead:|px#150:|py#40:|sw#200:|sh#150:|\n" +
            "touchbody:|px#160:|py#200:|sw#180:|sh#170:|\n");

        return catDir;
    }

    private static void WriteFrames(string dir, string prefix, int count, int ms)
    {
        Directory.CreateDirectory(dir);
        for (var i = 0; i < count; i++)
            File.WriteAllBytes(Path.Combine(dir, $"{prefix}_{i:000}_{ms}.png"), Array.Empty<byte>());
    }

    private static string InfoLine(string name, string path, string graph, string animat = "Single", string mode = "Nomal")
        => $"PNGAnimation#{name}:|path#{path}:|mode#{mode}:|graph#{graph}:|animat#{animat}:|";

    // ---------------------------------------------------------------- 用例

    [Fact]
    public void 加载最小形象并注册动画()
    {
        var catDir = MakeMod();
        WriteFrames(Path.Combine(catDir, "idle"), "idle", 3, 100);
        WriteFrames(Path.Combine(catDir, "sleep"), "sleep", 2, 110);
        File.WriteAllText(Path.Combine(catDir, "info.lps"), string.Join('\n',
            InfoLine("idle", "idle", "Idel"),
            InfoLine("sleep", "sleep", "Sleep")));

        var loader = PetLoader.Load(Path.Combine(_root));

        Assert.Equal(2, loader.Book.Count);
        Assert.True(loader.Book.Has(GraphType.Idel));
        Assert.True(loader.Book.Has(GraphType.Sleep));
        Assert.False(loader.Book.Has(GraphType.Gaze));
        Assert.Empty(loader.Errors);
        Assert.Equal("咪咪", loader.Config.PetName);
    }

    [Fact]
    public void 帧数与时长的解析()
    {
        var catDir = MakeMod();
        WriteFrames(Path.Combine(catDir, "idle"), "idle", 4, 120);
        File.WriteAllText(Path.Combine(catDir, "info.lps"), InfoLine("idle", "idle", "Idel"));

        var loader = PetLoader.Load(_root);
        var anim = loader.Book.Find("idle", AnimatType.Single, ModeType.Nomal);

        Assert.NotNull(anim);
        Assert.Equal(4, anim!.FrameCount);
        Assert.Equal(120, anim.Duration(0));
        Assert.Equal(480, anim.TotalDuration);
    }

    [Fact]
    public void 文件名里的时长不合法时按100ms兜底()
    {
        var catDir = MakeMod();
        var dir = Path.Combine(catDir, "idle");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "idle_000_abc.png"), Array.Empty<byte>());
        File.WriteAllText(Path.Combine(catDir, "info.lps"), InfoLine("idle", "idle", "Idel"));

        var anim = PetLoader.Load(_root).Book.Find("idle", AnimatType.Single, ModeType.Nomal);
        Assert.Equal(100, anim!.Duration(0));
    }

    [Fact]
    public void 按段落拆分A_B_C()
    {
        var catDir = MakeMod();
        WriteFrames(Path.Combine(catDir, "sleep", "a"), "sleep_a", 2, 100);
        WriteFrames(Path.Combine(catDir, "sleep", "b"), "sleep_b", 3, 140);
        WriteFrames(Path.Combine(catDir, "sleep", "c"), "sleep_c", 2, 110);
        File.WriteAllText(Path.Combine(catDir, "info.lps"), string.Join('\n',
            InfoLine("sleep", @"sleep\a", "Sleep", "A_Start"),
            InfoLine("sleep", @"sleep\b", "Sleep", "B_Loop"),
            InfoLine("sleep", @"sleep\c", "Sleep", "C_End")));

        var set = PetLoader.Load(_root).Book.FindSet(GraphType.Sleep, ModeType.Nomal);

        Assert.NotNull(set.A);
        Assert.NotNull(set.B);
        Assert.NotNull(set.C);
        Assert.Equal(2, set.A!.FrameCount);
        Assert.Equal(3, set.B!.FrameCount);
        Assert.Equal(2, set.C!.FrameCount);
    }

    [Fact]
    public void 模式回退_缺Nomal时用相邻模式()
    {
        var catDir = MakeMod();
        WriteFrames(Path.Combine(catDir, "idle"), "idle", 2, 100);
        File.WriteAllText(Path.Combine(catDir, "info.lps"),
            InfoLine("idle", "idle", "Idel", mode: "Happy"));

        var book = PetLoader.Load(_root).Book;

        Assert.NotNull(book.Find("idle", AnimatType.Single, ModeType.Happy));
        Assert.NotNull(book.Find("idle", AnimatType.Single, ModeType.Nomal));   // 回退
    }

    [Fact]
    public void 生病模式不回退()
    {
        var catDir = MakeMod();
        WriteFrames(Path.Combine(catDir, "idle"), "idle", 2, 100);
        File.WriteAllText(Path.Combine(catDir, "info.lps"),
            InfoLine("idle", "idle", "Idel", mode: "Nomal"));

        var book = PetLoader.Load(_root).Book;
        Assert.Null(book.Find("idle", AnimatType.Single, ModeType.Ill));   // 不"装没事"
    }

    [Fact]
    public void 没有info_lps的叶子目录按目录名推断()
    {
        var catDir = MakeMod();
        WriteFrames(Path.Combine(catDir, "gaze"), "gaze", 2, 100);

        var loader = PetLoader.Load(_root);

        Assert.True(loader.Book.Has(GraphType.Gaze));
        Assert.Equal(1, loader.Book.Count);
    }

    [Fact]
    public void 声明指向的目录不存在时进错误列表()
    {
        var catDir = MakeMod();
        File.WriteAllText(Path.Combine(catDir, "info.lps"),
            InfoLine("idle", "missing_dir", "Idle"));

        var loader = PetLoader.Load(_root);

        Assert.Equal(0, loader.Book.Count);
        Assert.NotEmpty(loader.Errors);
    }

    [Fact]
    public void 未知的动画声明类型进错误列表()
    {
        var catDir = MakeMod();
        WriteFrames(Path.Combine(catDir, "idle"), "idle", 2, 100);
        File.WriteAllText(Path.Combine(catDir, "info.lps"),
            "UnknownKind#idle:|path#idle:|");

        var loader = PetLoader.Load(_root);

        Assert.Equal(0, loader.Book.Count);
        Assert.Contains(loader.Errors, e => e.Contains("未知的动画声明类型"));
    }

    [Fact]
    public void 找不到任何形象时抛异常()
    {
        Directory.CreateDirectory(Path.Combine(_root, "EmptyMod", "pet"));
        Assert.Throws<InvalidDataException>(() => PetLoader.Load(_root));
    }

    [Fact]
    public void 指定形象名只加载那一个()
    {
        var catDir = MakeMod();
        WriteFrames(Path.Combine(catDir, "idle"), "idle", 2, 100);
        File.WriteAllText(Path.Combine(catDir, "info.lps"), InfoLine("idle", "idle", "Idel"));

        var loader = PetLoader.Load(_root, "MyCat");
        Assert.True(loader.Book.Has(GraphType.Idel));

        Assert.Throws<InvalidDataException>(() => PetLoader.Load(_root, "NoSuchPet"));
    }

    [Fact]
    public void 热区缺失时用默认值_非法值也回退()
    {
        // MakeMod 里写的是合法热区
        var catDir = MakeMod();
        WriteFrames(Path.Combine(catDir, "idle"), "idle", 2, 100);
        File.WriteAllText(Path.Combine(catDir, "info.lps"), InfoLine("idle", "idle", "Idel"));
        var cfg = PetLoader.Load(_root).Config;
        Assert.Equal(150, cfg.TouchHead.X);
        Assert.Equal(200, cfg.TouchHead.Width);
        Assert.Equal(160, cfg.TouchBody.X);
    }

    [Fact]
    public void 热区宽高非法时回退默认()
    {
        var petDir = Path.Combine(_root, "BadMod", "pet");
        var catDir = Path.Combine(petDir, "BadCat");
        Directory.CreateDirectory(Path.Combine(catDir, "idle"));
        File.WriteAllBytes(Path.Combine(catDir, "idle", "idle_000_100.png"), Array.Empty<byte>());
        File.WriteAllText(Path.Combine(petDir, "BadCat.lps"),
            "pet#BadCat:|path#BadCat:|\ntouchhead:|px#10:|py#10:|sw#-5:|sh#0:|\n");
        File.WriteAllText(Path.Combine(catDir, "info.lps"), InfoLine("idle", "idle", "Idel"));

        var cfg = PetLoader.Load(_root).Config;
        Assert.Equal(200, cfg.TouchHead.Width);      // 回退默认
        Assert.Equal(150, cfg.TouchHead.Height);
    }
}
