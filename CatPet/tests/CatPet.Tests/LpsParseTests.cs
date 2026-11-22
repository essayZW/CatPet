using CatPet.Core;
using Xunit;

namespace CatPet.Tests;

/// <summary>
/// LPS 解析器：VPet 的 <c>name#info:sub#value:</c> 格式。
/// 真实素材里注释、续行、BOM 都有，解析错一个字符就会整段动画消失。
/// </summary>
public class LpsParseTests
{
    [Fact]
    public void 解析单行与子字段()
    {
        var lines = Lps.Parse("PNGAnimation#idle:|path#idle:|mode#Nomal:|graph#Idle:|animat#Single:|");

        var line = Assert.Single(lines);
        Assert.Equal("PNGAnimation", line.Name);
        Assert.Equal("idle", line.Info);
        Assert.Equal("idle", line.Get("path"));
        Assert.Equal("Nomal", line.Get("mode"));
        Assert.Equal("Idle", line.Get("graph"));
        Assert.Equal("Single", line.Get("animat"));
    }

    [Fact]
    public void 忽略注释行()
    {
        var lines = Lps.Parse("// 这是注释\nidle#idle:|path#idle:|");
        Assert.Single(lines);
        Assert.Equal("idle", lines[0].Name);
    }

    [Fact]
    public void 处理续行()
    {
        var text = "idle#idle:|path#idle:|\n|mode#Nomal:|graph#Idle:|";
        var line = Assert.Single(Lps.Parse(text));
        Assert.Equal("idle", line.Get("path"));
        Assert.Equal("Nomal", line.Get("mode"));
        Assert.Equal("Idle", line.Get("graph"));
    }

    [Fact]
    public void 处理UTF8_BOM()
    {
        var line = Assert.Single(Lps.Parse("\uFEFFidle#idle:|path#idle:|"));
        Assert.Equal("idle", line.Name);
    }

    [Fact]
    public void 空行与行尾冒号不影响解析()
    {
        var lines = Lps.Parse("\n\nidle#idle:|path#idle:|\n\n");
        var line = Assert.Single(lines);
        Assert.Equal("idle", line.Get("path"));
    }

    [Fact]
    public void 子字段大小写不敏感()
    {
        var line = Assert.Single(Lps.Parse("x#y:|Path#idle:|MODE#Ill:|"));
        Assert.Equal("idle", line.Get("path"));
        Assert.Equal("Ill", line.Get("mode"));
        Assert.True(line.Has("PATH"));
    }

    [Fact]
    public void 类型化读取会兜底()
    {
        var line = Assert.Single(Lps.Parse("x#y:|n#42:|d#1.5:|b#true:|bad#abc:"));
        Assert.Equal(42, line.GetInt("n"));
        Assert.Equal(1.5, line.GetDouble("d"), 3);
        Assert.True(line.GetBool("b"));
        Assert.Equal(7, line.GetInt("bad", 7));         // 解析失败用默认值
        Assert.Equal(3.5, line.GetDouble("missing", 3.5), 3);
        Assert.False(line.GetBool("missing"));
    }

    [Fact]
    public void 无子字段的行也能解析()
    {
        var line = Assert.Single(Lps.Parse("pet#MyCat:|"));
        Assert.Equal("pet", line.Name);
        Assert.Equal("MyCat", line.Info);
    }

    [Fact]
    public void 没有井号的行把整行当名字()
    {
        var line = Assert.Single(Lps.Parse("onlyname|"));
        Assert.Equal("onlyname", line.Name);
        Assert.Equal("", line.Info);
    }
}
