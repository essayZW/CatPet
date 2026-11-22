using System.IO;
using System.Linq;
using System.Windows;

namespace CatPet.Core;

/// <summary>形象配置（对应 VPet 的 <c>pet/{名字}.lps</c>）。</summary>
public sealed class PetConfig
{
    public string Name { get; set; } = "pet";
    public string Intro { get; set; } = "";
    public string PetName { get; set; } = "咪咪";
    /// <summary>动画资源根目录（mod/{MOD}/pet/{形象}/）。</summary>
    public string RootDir { get; set; } = "";

    /// <summary>摸头热区（500×500 逻辑坐标系）。</summary>
    public Rect TouchHead { get; set; } = new(150, 40, 200, 150);
    /// <summary>摸身体热区。</summary>
    public Rect TouchBody { get; set; } = new(160, 200, 180, 170);

    /// <summary>从 <c>pet/{形象}.lps</c> 解析；<c>path</c> 相对于 pet 目录（与 VPet 一致）。</summary>
    public static PetConfig Parse(string lpsPath)
    {
        var lines = Lps.ParseFile(lpsPath);
        var config = new PetConfig();

        var petLine = lines.FirstOrDefault(l => l.Name.Equals("pet", StringComparison.OrdinalIgnoreCase))
                      ?? throw new InvalidDataException("pet lps 里缺少 pet 行");

        config.Name = petLine.Info;
        config.Intro = petLine.Get("intor");
        config.PetName = petLine.Get("petname", config.Name);

        var relative = petLine.Get("path");
        var petDir = Path.GetDirectoryName(Path.GetFullPath(lpsPath))!;
        config.RootDir = Path.GetFullPath(Path.Combine(petDir, relative));

        // 热区数据不合法时回退默认值：宽高为负会抛 ArgumentException、NaN 会让 Contains 永远 false
        config.TouchHead = ReadRect(lines, "touchhead", config.TouchHead);
        config.TouchBody = ReadRect(lines, "touchbody", config.TouchBody);


        return config;
    }

    /// <summary>读一个热区矩形；缺字段/负数/NaN 时用兜底值。</summary>
    private static Rect ReadRect(IEnumerable<LpsLine> lines, string name, Rect fallback)
    {
        var line = lines.FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (line is null) return fallback;

        var x = line.GetDouble("px");
        var y = line.GetDouble("py");
        var w = line.GetDouble("sw");
        var h = line.GetDouble("sh");
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(w) || !double.IsFinite(h))
            return fallback;
        if (w <= 1 || h <= 1) return fallback;

        return new Rect(x, y, w, h);
    }
}

/// <summary>
/// 资源加载器：扫描 <c>mod/{MOD}/</c>，读取形象描述并把所有动画注册进 <see cref="GraphBook"/>。
/// 递归规则与 VPet 的 <c>PetLoader.LoadGraph</c> 一致。
/// </summary>
public sealed class PetLoader
{
    public PetConfig Config { get; private set; } = new();
    public GraphBook Book { get; } = new();
    public List<string> Errors { get; } = new();
    public string ModDir { get; private set; } = "";

    public static PetLoader Load(string modRoot, string? petName = null)
    {
        var loader = new PetLoader();
        loader.LoadInternal(modRoot, petName);
        return loader;
    }

    private void LoadInternal(string modRoot, string? petName)
    {
        if (!Directory.Exists(modRoot))
            throw new DirectoryNotFoundException($"MOD 目录不存在：{modRoot}");

        // 遍历 mod 下每个 MOD 的 pet 目录
        foreach (var modDir in Directory.GetDirectories(modRoot))
        {
            var petDir = Path.Combine(modDir, "pet");
            if (!Directory.Exists(petDir))
                continue;

            foreach (var lps in Directory.GetFiles(petDir, "*.lps"))
            {
                var name = Path.GetFileNameWithoutExtension(lps);
                if (petName is not null && !name.Equals(petName, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    Config = PetConfig.Parse(lps);
                    ModDir = modDir;
                }
                catch (Exception ex)
                {
                    Errors.Add($"读取形象 {lps} 失败：{ex.Message}");
                    continue;
                }

                if (!Directory.Exists(Config.RootDir))
                {
                    Errors.Add($"形象资源目录不存在：{Config.RootDir}");
                    continue;
                }

                LoadGraph(Config.RootDir, Config.RootDir);
                return; // 一个形象足够（多形象可自行改成列表）
            }
        }

        throw new InvalidDataException($"在 {modRoot} 下没有找到任何 pet/*.lps");
    }

    /// <summary>递归加载：有 info.lps 按其声明；叶子目录按 png 数量自动判定；否则继续下钻。</summary>
    private void LoadGraph(string dir, string root)
    {
        var infoPath = Path.Combine(dir, "info.lps");
        if (File.Exists(infoPath))
        {
            foreach (var line in Lps.ParseFile(infoPath))
            {
                try
                {
                    Register(line, dir, root);
                }
                catch (Exception ex)
                {
                    Errors.Add($"{dir} / {line.Name}：{ex.Message}");
                }
            }
            return;
        }

        var subDirs = Directory.GetDirectories(dir);
        if (subDirs.Length == 0)
        {
            var pngs = Directory.GetFiles(dir, "*.png");
            if (pngs.Length == 0)
                return;

            var relative = Path.GetRelativePath(root, dir);
            var graphInfo = GraphInfo.Infer(relative, dir);
            try
            {
                Book.Add(FrameAnimation.Create(dir, graphInfo, isLoop: true));
            }
            catch (Exception ex)
            {
                Errors.Add($"{dir}：{ex.Message}");
            }
            return;
        }

        foreach (var sub in subDirs)
            LoadGraph(sub, root);
    }

    private void Register(LpsLine line, string dir, string root)
    {
        var kind = line.Name.ToLowerInvariant();
        var relative = line.Get("path");
        var target = string.IsNullOrEmpty(relative)
            ? dir
            : Path.GetFullPath(Path.Combine(dir, relative));

        var isLoop = line.GetBool("loop");
        var fallbackName = Path.GetFileName(dir);

        switch (kind)
        {
            case "pnganimation":
            case "apnganimation": // 本 demo 用 PNG 序列代替 APNG
            {
                var info = GraphInfo.FromLine(line, dir, fallbackName);
                var anim = FrameAnimation.Create(target, info, isLoop);
                Book.Add(anim);
                break;
            }
            case "picture":
            {
                var info = GraphInfo.FromLine(line, dir, fallbackName);
                var length = line.GetInt("length", 1000);
                var anim = FrameAnimation.CreatePicture(target, info, isLoop, length);
                Book.Add(anim);
                break;
            }
            default:
                Errors.Add($"{dir}：未知的动画声明类型 {line.Name}");
                break;
        }
    }
}
