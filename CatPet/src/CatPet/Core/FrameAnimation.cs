using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CatPet.Core;

/// <summary>
/// PNG 序列帧动画 —— 复刻 VPet 的核心做法：
///   1) 一个文件夹里的 <c>*.png</c> 就是一段动画；
///   2) 文件名最后一段数字 = 该帧停留毫秒数（<c>idle_003_120.png</c> → 120ms）；
///   3) 加载时把所有帧横向拼成一张"精灵图"缓存到磁盘，播放时只做裁剪。
/// </summary>
public sealed class FrameAnimation
{
    /// <summary>精灵图里单帧的边长：窗口默认 228px，380 已经够清晰（原来 500 白占内存）。</summary>
    private const int SheetFrameWidth = 380;

    /// <summary>单张精灵图的最大像素宽度，超过就压缩（和 VPet 的 60000 上限同理）。</summary>
    private const int MaxSheetWidth = 30000;

    public GraphInfo Info { get; }
    public bool IsLoop { get; }
    public string Dir { get; }

    private readonly string[] _files;
    private readonly int[] _durations;
    private readonly List<Int32Rect> _rects = new();

    private readonly object _lock = new();
    private BitmapSource? _sheet;
    private BitmapSource? _lastFrame;
    private int _lastIndex = -1;
    private int _frameW;
    private int _frameH;

    public int FrameCount => _files.Length;

    public int Duration(int index) => _durations[index];

    public int TotalDuration => _durations.Sum();

    private FrameAnimation(string dir, GraphInfo info, bool loop, string[] files, int[] durations)
    {
        Dir = dir;
        Info = info;
        IsLoop = loop;
        _files = files;
        _durations = durations;
    }

    public static FrameAnimation Create(string dir, GraphInfo info, bool isLoop)
    {
        if (!Directory.Exists(dir))
            throw new DirectoryNotFoundException($"动画目录不存在：{dir}");

        // 顺序 = 文件名序（所以序号要补零），与 VPet 的 GetFiles 行为一致
        var files = Directory.GetFiles(dir, "*.png")
                             .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
                             .ToArray();

        if (files.Length == 0)
            throw new InvalidDataException($"目录里没有 png：{dir}");

        var durations = files.Select(ParseDuration).ToArray();
        return new FrameAnimation(dir, info, isLoop, files, durations);
    }

    /// <summary>静态图：包成"只有一帧、停留 length 毫秒"的动画（对应 VPet 的 Picture）。</summary>
    public static FrameAnimation CreatePicture(string file, GraphInfo info, bool isLoop, int lengthMs = 1000)
    {
        if (!File.Exists(file))
            throw new FileNotFoundException($"图片不存在：{file}");
        return new FrameAnimation(Path.GetDirectoryName(file)!, info, isLoop,
            new[] { file }, new[] { Math.Max(1, lengthMs) });
    }

    /// <summary>从文件名最后一段解析帧时长；解析失败按 100ms 处理。</summary>
    private static int ParseDuration(string file)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var idx = name.LastIndexOf('_');
        if (idx >= 0 && int.TryParse(name[(idx + 1)..], out var ms) && ms > 0)
            return ms;
        return 100;
    }

    /// <summary>释放内存里的精灵图（下次用到会自动重建）。用于 LRU 回收。</summary>
    public void ReleaseSheet()
    {
        lock (_lock)
        {
            _sheet = null;
            _lastFrame = null;
            _lastIndex = -1;
            _rects.Clear();
        }
    }

    /// <summary>该动画当前是否已把精灵图载入内存。</summary>
    public bool IsLoaded => _sheet is not null;

    /// <summary>取第 index 帧（内部保证精灵图已就绪）。</summary>
    public BitmapSource GetFrame(int index)
    {
        EnsureSheet();
        lock (_lock)
        {
            if (index == _lastIndex && _lastFrame is not null)
                return _lastFrame;

            var frame = new CroppedBitmap(_sheet!, _rects[index]);
            frame.Freeze();
            _lastFrame = frame;
            _lastIndex = index;
            return frame;
        }
    }

    // ---------------------------------------------------------------- 精灵图

    private static bool _cacheCleaned;

    /// <summary>清掉旧尺寸的精灵图缓存（改过 SheetFrameWidth 之后免得白占磁盘）。</summary>
    private static void EnsureCacheClean(string cacheDir)
    {
        if (_cacheCleaned) return;
        _cacheCleaned = true;
        try
        {
            var keep = $"{SheetFrameWidth}_";
            var cutoff = DateTime.UtcNow.AddDays(-14);
            foreach (var f in Directory.GetFiles(cacheDir, "*.png"))
            {
                var name = Path.GetFileName(f);
                var wrongSize = !name.StartsWith(keep, StringComparison.Ordinal);
                var tooOld = File.GetLastWriteTimeUtc(f) < cutoff;
                if (wrongSize || tooOld)
                {
                    try { File.Delete(f); } catch { /* ignore */ }
                }
            }
        }
        catch
        {
            // ignore
        }
    }

    private void EnsureSheet()
    {
        if (_sheet is not null)
            return;

        lock (_lock)
        {
            if (_sheet is not null)
                return;

            // 缓存放 LocalAppData：装到 Program Files 时程序目录不可写，
            // 否则 BuildSheet 每次保存都抛异常，整只猫会没有画面
            var cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CatPet", "cache");
            Directory.CreateDirectory(cacheDir);
            EnsureCacheClean(cacheDir);

            var cacheFile = Path.Combine(cacheDir, $"{SheetFrameWidth}_{StableHash(Dir)}_{AssetStamp()}.png");

            if (File.Exists(cacheFile))
            {
                try
                {
                    _sheet = LoadFrozen(cacheFile);
                    BuildRects(_sheet.PixelWidth / _files.Length, _sheet.PixelHeight);
                    return;
                }
                catch
                {
                    // 缓存损坏就当没有，下面重建
                }
            }

            _sheet = BuildSheet(cacheFile);
        }
    }

    /// <summary>
    /// 素材指纹：帧数 + 最新修改时间 + 总字节数。
    /// 只要素材文件变了（换素材、重新烘焙），缓存文件名就不同，避免升级后仍读到旧精灵图。
    /// </summary>
    private string AssetStamp()
    {
        long newest = 0;
        long total = 0;
        foreach (var f in _files)
        {
            try
            {
                var fi = new FileInfo(f);
                if (!fi.Exists) continue;
                if (fi.LastWriteTimeUtc.Ticks > newest) newest = fi.LastWriteTimeUtc.Ticks;
                total += fi.Length;
            }
            catch
            {
                // ignore
            }
        }
        return $"{_files.Length}_{newest}_{total}";
    }

    private void BuildRects(int frameW, int frameH)
    {
        _frameW = frameW;
        _frameH = frameH;
        _rects.Clear();
        for (var i = 0; i < _files.Length; i++)
            _rects.Add(new Int32Rect(frameW * i, 0, frameW, frameH));
    }

    private BitmapSource BuildSheet(string cacheFile)
    {
        // 以第一帧的宽高比决定单帧尺寸
        var first = LoadFrozen(_files[0]);
        var frameW = Math.Min(first.PixelWidth, SheetFrameWidth);
        var frameH = (int)(first.PixelHeight * (frameW / (double)first.PixelWidth));

        if (_files.Length * frameW > MaxSheetWidth)
        {
            frameW = MaxSheetWidth / _files.Length;
            frameH = (int)(first.PixelHeight * (frameW / (double)first.PixelWidth));
        }
        frameW = Math.Max(1, frameW);
        frameH = Math.Max(1, frameH);

        // 用 GDI+ 拼图（和 VPin 用 SkiaSharp 拼图同理：都在"像素层"做，不依赖 WPF 渲染管线），
        // 拼好后落盘，再由 WPF 一次性读入并裁剪。
        var tmpFallback = Path.Combine(Path.GetTempPath(), $"catpet_sheet_{Guid.NewGuid():N}.png");
        var savedPath = cacheFile;
        using (var sheet = new System.Drawing.Bitmap(frameW * _files.Length, frameH,
                   System.Drawing.Imaging.PixelFormat.Format32bppArgb))
        using (var g = System.Drawing.Graphics.FromImage(sheet))
        {
            g.Clear(System.Drawing.Color.Transparent);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            for (var i = 0; i < _files.Length; i++)
            {
                using var img = System.Drawing.Image.FromFile(_files[i]);
                g.DrawImage(img, new System.Drawing.Rectangle(frameW * i, 0, frameW, frameH));
            }

            try
            {
                sheet.Save(cacheFile, System.Drawing.Imaging.ImageFormat.Png);
            }
            catch
            {
                // 程序目录不可写（比如装在 Program Files）：改存临时文件，保证这一帧能用
                sheet.Save(tmpFallback, System.Drawing.Imaging.ImageFormat.Png);
                savedPath = tmpFallback;
            }
        }

        try
        {
            var built = LoadFrozen(savedPath);
            BuildRects(built.PixelWidth / _files.Length, built.PixelHeight);
            return built;
        }
        finally
        {
            if (savedPath == tmpFallback)
            {
                try { File.Delete(tmpFallback); } catch { /* ignore */ }
            }
        }
    }

    private static BitmapSource LoadFrozen(string path, int decodeWidth = 0)
    {
        var bmp = new BitmapImage();
        bmp.BeginInit();
        bmp.CacheOption = BitmapCacheOption.OnLoad;
        // 注意：用 StreamSource 时不能再设 CreateOptions = IgnoreImageCache
        // （会进 FinalizeCreation → ImagingCache.RemoveFromCache(null) 抛 ArgumentNullException）
        if (decodeWidth > 0)
            bmp.DecodePixelWidth = decodeWidth;   // 让解码器直接按目标尺寸出图
        // 用文件流而不是 UriSource：安装路径里可能有 # 或 %，拼成 Uri 会解析成 fragment 或非法转义
        using (var fs = File.OpenRead(path))
        {
            bmp.StreamSource = fs;
            bmp.EndInit();
        }
        bmp.Freeze();
        return bmp;
    }

    /// <summary>string.GetHashCode 在 .NET Core 里每进程随机，这里自己算一个稳定的。</summary>
    private static string StableHash(string text)
    {
        unchecked
        {
            var hash = 2166136261u;
            foreach (var c in text.ToLowerInvariant())
            {
                hash ^= c;
                hash *= 16777619u;
            }
            return hash.ToString("x8");
        }
    }
}
