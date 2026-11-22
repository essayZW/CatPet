using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CatPet.Core;

namespace CatPet;

public partial class PetWindow : Window
{
    private readonly PetPlayer _player;
    private readonly PetLoader _loader;
    private readonly Image _petImage;
    private readonly DispatcherTimer _bubbleTimer;
    private readonly DispatcherTimer _sleepTimer;
    private bool _asleep;
    /// <summary>多久没人理就打盹。</summary>
    /// <summary>多久没互动就打盹。深夜犯困快、午后也容易困。</summary>
    private static TimeSpan SleepAfter
    {
        get
        {
            var h = DateTime.Now.Hour;
            if (h >= 22 || h < 5) return TimeSpan.FromSeconds(25);     // 深夜：很快犯困
            if (h >= 12 && h < 15) return TimeSpan.FromSeconds(35);    // 午后：容易犯困
            if (h >= 5 && h < 10) return TimeSpan.FromSeconds(55);     // 早上：精神好
            return TimeSpan.FromSeconds(45);
        }
    }
    /// <summary>小动作播完之前，别的轮换（换姿势/姿态切换/邀约/打盹）都不要插进来。</summary>
    private DateTime _actionBusyUntil = DateTime.MinValue;
    private bool ActionBusy => DateTime.Now < _actionBusyUntil;
    private void MarkActionBusy(double seconds) => _actionBusyUntil = DateTime.Now.AddSeconds(seconds);

    private readonly DispatcherTimer _blinkTimer;

    private readonly DispatcherTimer _idleTimer;
    private readonly DispatcherTimer _faceTimer;
    /// <summary>当前待机是否为正脸。</summary>
    private bool _facingFront;
    private readonly DispatcherTimer _hintTimer;
    /// <summary>第一次提示等 90 秒，之后每 8 分钟一次，最多 settings.HintRemaining 次。</summary>
    private static readonly TimeSpan HintFirstDelay = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan HintRepeatDelay = TimeSpan.FromMinutes(8);
    /// <summary>每隔多久在正脸/侧脸之间切换一次。</summary>
    private static readonly TimeSpan FaceSwitchEvery = TimeSpan.FromSeconds(25);

    // 文案按动作分组：叫声用拟声词，情绪/状态用括号里的描述，不用人类说话的口吻
    private static readonly string[] Meows = { "喵～", "喵呜～", "喵？", "喵嗷～" };
    private static readonly string[] TouchTexts = { "（蹭蹭）", "（呼噜呼噜）" };
    private static readonly string[] SleepTexts = { "（打呼噜）", "（睡着了）", "（蜷成一团）" };
    private static readonly string[] LickTexts = { "（尝试舔毛中~）" };
    private static readonly string[] YawnTexts = { "（伸了个懒腰）" };
    private static readonly string[] TiltTexts = { "喵？", "（歪头）" };
    // 关心 / 打气的话：不绑定动作，隔一阵子自己冒一句
    private static readonly string[] CareTexts =
    {
        "工作再忙也要记得喝水喵",
        "坐久了，起来动一动吧喵",
        "今天也辛苦了喵～",
        "别一直盯着屏幕，看看我喵",
        "你已经很努力啦喵",
        "累了就歇一会儿喵",
        "我在这儿陪着你喵",
        "保持专注，也别忘了休息喵",
        "抬头伸个懒腰吧喵",
        "忙完记得吃点东西喵",
        "深呼吸一下，放松放松喵",
        "你的努力我都看在眼里喵",
        "工作加油喵～",
        "眼睛累了就闭一会儿喵",
    };

    // 深夜 / 早上的专属几句
    private static readonly string[] CareNight =
    {
        "这么晚了，早点休息吧喵",
        "熬夜对身体不好喵",
        "夜里了，别太拼喵",
    };

    private static readonly string[] CareMorning =
    {
        "喵，早上好！今天也一起加油",
        "新的一天，慢慢来就好喵",
    };

    // 新增小动作的文案（描述性，不带喵）
    private static readonly string[] ShakeTexts = { "（抖了抖毛）", "（甩甩头）" };
    private static readonly string[] LookAroundTexts = { "（东张西望）", "（好像听到了什么）", "（四处看看）" };
    private static readonly string[] GazeTexts = { "（望着窗外发呆）", "（发呆中）" };

    // 走到屏幕边界时的吐槽
    private static readonly string[] EdgeTexts =
    {
        "走到头了喵",
        "（撞墙了）",
        "这边没路了喵",
        "再走就出屏幕啦喵",
    };
    // 功能发现提示（托盘里的番茄钟 / 喝水提醒比较隐蔽）
    private static readonly string[] HintTexts =
    {
        "右键托盘里的猫，可以定番茄钟",
        "要我提醒你喝水的话，右键托盘图标",
        "专注计时在托盘图标里喵（番茄钟）",
    };

    /// <summary>从某个动作的文案池里随机挑一句。</summary>
    private void SayFrom(string[] pool) => Say(pool[Random.Shared.Next(pool.Length)]);

    private void SayFrom(string[] pool, double seconds) => Say(pool[Random.Shared.Next(pool.Length)], seconds);


    // ------------------------------------------------------------ 调整大小
    private readonly Settings _settings;
    private bool _resizeMode;
    private FrameworkElement? _dragCorner;
    private double _zoomOnEnter, _leftOnEnter, _topOnEnter;
    private double _anchorX, _anchorY;   // 拖动时保持不动的那个角的屏幕坐标

    // ------------------------------------------------------------ 站立动作的窗口缩放
    private bool _standing;                 // 当前是不是站立姿态
    private DispatcherTimer? _scaleTimer;
    private double _scaleFrom, _scaleTo, _scaleProgress;
    private double _scaleBaseSize, _scaleAnchorX, _scaleAnchorBottom;
    private int _scaleMs = 200;
    private DispatcherTimer? _walkTimer;
    private DispatcherTimer _postureTimer = null!;
    private readonly DispatcherTimer _careTimer;
    /// <summary>关心文案的间隔区间（秒）：太频繁会烦，太少又没存在感。</summary>
    private static readonly (int Min, int Max) CareInterval = (240, 420);   // 4~7 分钟
    private bool _standAction;              // 站立「动作」正在播（区别于站立待机）
    private int _standActionGen;            // 谁拥有这个状态：播放结束/被顶掉后自动失效，避免标志卡死

    /// <summary>
    /// 站立动作是否真的还在播：既要有标志，又要当前画面还是这次动作。
    /// 只看标志的话，动作被打断（拖动/小游戏/入睡/运镜…）后会永远为 true，跟随就再也不回来了。
    /// </summary>
    private bool StandBusy => _standAction && _player.Generation == _standActionGen;

    /// <summary>窗口平滑缩放到 factor 倍（底边中点固定）。</summary>
    private void AnimateScaleTo(double factor, int ms = 200)
    {
        if (_scaleTimer is not null && _scaleTimer.IsEnabled)
            return;
        if (_scaleBaseSize <= 0)
            _scaleBaseSize = Width;

        var target = _scaleBaseSize * factor;
        // 已经是目标大小就什么都不做（原来还比 _scaleTo，而它第一次是 0，
        // 于是启动后第一次点"跑"会白跑一段 200ms 的缩放动画，
        // 这期间每个 tick 都在按锚点重设 Left/Top，把走路位移盖掉 → 看着"起步不动"）
        if (Math.Abs(Width - target) < 0.5)
        {
            _scaleTo = target;
            return;
        }

        // 以「当前」窗口的底边中点为锚点——走动过也不会弹回原位
        _scaleAnchorX = Left + Width / 2;
        _scaleAnchorBottom = Top + Height;

        _scaleFrom = Width;
        _scaleTo = target;
        _scaleProgress = 0;
        _scaleMs = ms;
        Diag($"[scale] {_scaleFrom:0} -> {target:0} (anchorX={_scaleAnchorX:0} bottom={_scaleAnchorBottom:0})");

        if (_scaleTimer is null)
        {
            _scaleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _scaleTimer.Tick += (_, _) =>
            {
                _scaleProgress = Math.Min(1, _scaleProgress + 16.0 / Math.Max(1, _scaleMs));
                var k = _scaleProgress * _scaleProgress * (3 - 2 * _scaleProgress);
                var size = _scaleFrom + (_scaleTo - _scaleFrom) * k;
        Width = size;
        Height = size + BubbleBandHeight;   // 高度多出气泡带
                Left = _scaleAnchorX - size / 2;
                Top = _scaleAnchorBottom - (size + BubbleBandHeight);   // 窗口高度含气泡带
                if (_scaleProgress >= 1)
                    _scaleTimer!.Stop();
            };
        }
        _scaleTimer.Start();
    }

    /// <summary>播放一个站立动作：先切帧再放大；走的时候同步位移；结束后回到站立待机。</summary>
    private async Task PlayStandAsync(string name, int loops, int dir, string text)
    {
        _lastInteraction = DateTime.Now;
        if (_asleep) Wake();
        if (!string.IsNullOrEmpty(text)) Say(text, 3);

        _standAction = true;
        UpdateFacingFromClip(name);
        _followPausedUntil = DateTime.Now.AddSeconds(Math.Max(2.5, loops * 0.6));   // 跟随让位给站立动作
        var play = _player.PlayStandClipAsync(name, loops);
        var gen = _player.Generation;   // 记下这次播放的代际，收尾前比对
        _standActionGen = gen;

        // 等第一帧画出来再放大，避免「窗口先变大、画面还是坐姿」
        await Task.Delay(70);
        if (_player.Generation != gen)
        {
            // 已经被更晚的动作顶掉：别改窗口状态，但要把状态复位，免得卡住
            if (_standActionGen == gen) _standAction = false;
            return;
        }
        _standing = true;
        AnimateScaleTo(Math.Clamp(_settings.StandZoom, 1.0, 2.0));

        // 按片子名区分移动速度：sgal=飞奔、srun=跑、其余=走
        var myWalkGen = _walkGen;
        if (dir != 0)
        {
            var speed = name.StartsWith("sgal", StringComparison.OrdinalIgnoreCase) ? GallopPxPerSecond
                      : name.StartsWith("srun", StringComparison.OrdinalIgnoreCase) ? RunPxPerSecond
                      : WalkPxPerSecond;
            StartWalk(dir, speed);
            myWalkGen = _walkGen;
        }

        await play;
        // 如果这段时间里又起了别的动作，这次收尾必须让位（否则会把新动作停掉、把姿态切回待机）
        var interrupted = _player.Generation != gen;
        if (interrupted)
        {
            // 只停自己那次位移；新动作已经开了新的位移，别越权停掉它（会导致"动画在播、窗口不动"）
            if (_walkGen == myWalkGen) StopWalk("动作被打断");
            return;
        }

        if (_standActionGen == gen) _standAction = false;
        StopWalk("动作结束");

        if (_standing)
            await PlayStandIdleForFacingAsync();
    }

    /// <summary>站立被摸头：播几轮闭眼待机，再回站立待机。</summary>
    private async Task PlayStandClosedAsync()
    {
        // 注意：代际要在**启动播放之后**取。RunExclusive 在返回任务时就把 _generation 自增了，
        // 先取再播的话 await 回来必然"不相等"，收尾逻辑永远不执行（闭眼后就再也不会睁眼）。
        // 2 轮 × 8 帧 × 137ms ≈ 2.19s，对齐坐姿摸头的闭眼时长（B 段 10 帧 × 110ms × 2 = 2.2s）
        var play = _player.PlayStandClipAsync(SideClip("sclose", "sclose_r"), 2);
        var gen = _player.Generation;
        await play;
        if (_player.Generation != gen) return;   // 期间被别的动作顶掉了，别抢画面
        if (_standing)
            _ = PlayStandIdleForFacingAsync();
    }

    /// <summary>按当前朝向选左右两套素材之一（没有右向素材时退回左向）。</summary>
    private string SideClip(string left, string right) =>
        _faceRight && _player.HasClip(right) ? right : left;

    /// <summary>切回坐姿：缩回原尺寸并播坐姿待机。</summary>
    private async Task ReturnSittingAsync()
    {
        if (!_standing) return;
        _standing = false;
        StopWalk("切回坐姿");
        AnimateScaleTo(1.0, 180);
        await Task.Delay(120);
        // 这 120ms 里如果又站起来了（或起了别的动作），就不要再把画面切成坐姿待机
        if (_standing || StandBusy || ChaseActive) return;
        // 不能 await：PlayIdleAsync 是无限循环，await 会让调用方永远等下去
        // （之前就是这个 bug —— 站姿下点坐姿动作要再点一次才播）
        _ = _player.PlayIdleAsync();
    }

    // ------------------------------------------------------------ 设置窗口

    /// <summary>打开设置子窗口；保存后把各项改动应用到运行时。</summary>
    internal void OpenSettings()
    {
        CloseInvite();   // 别让邀约弹窗浮在模态框上面
        var dialog = new SettingsWindow(_settings) { Owner = this };
        var ok = dialog.ShowDialog() == true;

        Topmost = _settings.Topmost;
        ApplyTextSize();

        ApplyActionFrequency();

        _waterRemainSec = Math.Max(1, _settings.WaterRemindMinutes) * 60;

        if (!_settings.HeadFollow)
        {
            _followUntil = DateTime.MinValue;
            EndHeadFollow();
        }

        RebuildTrayMenu();
        ApplyZoomFromSettings();
        if (ok) Say("设置已保存喵", 2);
    }

    /// <summary>按设置里的 Zoom 调整窗口大小（重置设置时窗口也会回到默认大小）。</summary>
    private void ApplyZoomFromSettings()
    {
        var target = Math.Clamp(500 * _settings.Zoom, 500 * Settings.MinZoom, 500 * Settings.MaxZoom);
        if (Math.Abs(Width - target) < 0.5)
            return;

        var anchorX = Left + Width / 2;
        var anchorBottom = Top + Height;

        Width = target;
        Height = target + BubbleBandHeight;
        _scaleBaseSize = target;

        Left = anchorX - Width / 2;
        Top = anchorBottom - Height;

        // 防止跑到屏幕外
        var left = SystemParameters.VirtualScreenLeft;
        var top = SystemParameters.VirtualScreenTop;
        var right = left + SystemParameters.VirtualScreenWidth;
        var bottom = top + SystemParameters.VirtualScreenHeight;
        Left = Math.Clamp(Left, left - Width * 0.5, right - Width * 0.5);
        Top = Math.Clamp(Top, top - Height * 0.5, bottom - Height * 0.5);

        SavePosition();
        UpdateBubbleOffset();
    }

    /// <summary>打开「关于」窗口（版本 + 作者 + GitHub 主页）。</summary>
    internal void ShowAbout()
    {
        CloseInvite();
        var dialog = new AboutWindow { Owner = this };
        dialog.ShowDialog();
    }

    // ------------------------------------------------------------ 气泡与字号

    private bool _applyingLayout;
    private double BubbleBandHeight => Math.Clamp(Math.Clamp(_settings.TextSize, 8, 72) * 3.2 + 12, 34, 150);

    /// <summary>
    /// 气泡字号独立于窗口缩放；窗口高度 = 宽度 + 气泡带，
    /// 所以文字永远在猫的图片上方，猫本体不会被压小也不会被遮住。
    /// </summary>
    private void ApplyTextSize()
    {
        if (_applyingLayout) return;

        var size = Math.Clamp(_settings.TextSize, 8, 72);
        BubbleText.FontSize = size;

        if (BubbleRow is not null)
            BubbleRow.Height = new GridLength(BubbleBandHeight);

        ApplyWindowLayout();
    }

    /// <summary>窗口高度 = 宽度 + 气泡带（猫本体保持 宽度×宽度 的正方形）。</summary>
    private void ApplyWindowLayout()
    {
        if (_applyingLayout) return;
        try
        {
            _applyingLayout = true;
            var target = Width + BubbleBandHeight;
            if (Math.Abs(Height - target) > 0.5)
                Height = target;

            if (Bubble is not null)
                Bubble.MaxWidth = Math.Max(80, Width - 12);
        }
        finally
        {
            _applyingLayout = false;
        }

        UpdateBubbleOffset();
    }
    /// <summary>窗口贴着屏幕左右边缘时，把气泡往里挪，保证文字完整显示。</summary>
    private void UpdateBubbleOffset()
    {
        if (Bubble is null) return;

        var vl = SystemParameters.VirtualScreenLeft;
        var vr = vl + SystemParameters.VirtualScreenWidth;
        const double margin = 4;

        // 先居中，再看左右还剩多少空间
        var halfBubble = Math.Min(Bubble.ActualWidth > 0 ? Bubble.ActualWidth : Width, Width) / 2;
        var center = Left + Width / 2;
        var shift = 0.0;

        if (center - halfBubble < vl + margin)
            shift = vl + margin - (center - halfBubble);
        else if (center + halfBubble > vr - margin)
            shift = (vr - margin) - (center + halfBubble);

        Bubble.RenderTransform = shift == 0 ? Transform.Identity : new TranslateTransform(shift, 0);
    }
    // ------------------------------------------------------------ 窗口位置记忆

    /// <summary>这个位置是否落在某个屏幕里（至少露出 60px 见方，防止拔掉副屏后跑到虚空）。</summary>
    private static bool IsOnAnyScreen(double left, double top)
    {
        var vl = SystemParameters.VirtualScreenLeft;
        var vt = SystemParameters.VirtualScreenTop;
        var vr = vl + SystemParameters.VirtualScreenWidth;
        var vb = vt + SystemParameters.VirtualScreenHeight;
        return left + 60 >= vl && left <= vr - 60 && top + 60 >= vt && top <= vb - 60;
    }

    /// <summary>把当前位置写进设置（拖拽/移动/退出时调用）。</summary>
    private void SavePosition()
    {
        if (double.IsNaN(Left) || double.IsNaN(Top)) return;
        _settings.LastLeft = Left;
        _settings.LastTop = Top;
        _settings.Save();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);

    /// <summary>窗口是否被 DWM 标记为 cloaked（比如全屏切换时系统会临时藏起所有窗口）。</summary>
    private bool IsCloaked()
    {
        try
        {
            var h = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (h == IntPtr.Zero) return false;
            const int DWMWA_CLOAKED = 14;
            return DwmGetWindowAttribute(h, DWMWA_CLOAKED, out var v, sizeof(int)) == 0 && v != 0;
        }
        catch
        {
            return false;
        }
    }

    private string _lastState = "";

    /// <summary>每秒比对一次窗口状态，有变化就记日志（查"莫名消失"用）。</summary>
    private void LogWindowState()
    {
        if (!DiagOn) return;
        var state = $"{Visibility} {Left:0},{Top:0} {Width:0}x{Height:0} topmost={Topmost} autoHidden={_autoHidden} userHidden={_userHidden} cloaked={IsCloaked()}";
        if (state == _lastState) return;
        Diag("状态变化：" + state);
        _lastState = state;
    }

    // ------------------------------------------------------------ 诊断日志（CATPET_DIAG=1 时开启）

    private static readonly bool DiagOn = Environment.GetEnvironmentVariable("CATPET_DIAG") == "1";

    /// <summary>把窗口状态写进 %LOCALAPPDATA%\CatPet\diag.log（超过 1MB 覆盖重来）。</summary>
    private static void Diag(string message)
    {
        if (!DiagOn) return;
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CatPet");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "diag.log");
            var fi = new FileInfo(path);
            if (fi.Exists && fi.Length > 1024 * 1024)
                File.Delete(path);
            File.AppendAllText(path, $"[{DateTime.Now:HH:mm:ss.fff}] {message}\r\n");
        }
        catch
        {
            // ignore
        }
    }

    // ------------------------------------------------------------ 全屏应用自动隐藏

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private readonly DispatcherTimer _fullscreenTimer;
    private bool _autoHidden;
    private bool _userHidden;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);

    /// <summary>系统壳层的窗口类名，它们会铺满屏幕但不算"全屏应用"。</summary>
    private static readonly string[] ShellClasses =
    {
        "Windows.UI.Core.CoreWindow",
        "XamlExplorerHostIslandWindow",
        "MultitaskingViewFrame",
        "ForegroundStaging",
        "Shell_TrayWnd",
        "Progman",
        "WorkerW",
        "TaskListThumbnailWnd",
    };

    /// <summary>前台窗口是不是一个占满整块屏幕的应用（视频/游戏/演示）。</summary>
    private bool IsForegroundFullscreen(out string why)
    {
        why = "";
        try
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) { why = "前台为空"; return false; }

            var own = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (hwnd == own) { why = "前台是自己"; return false; }

            var cls = new StringBuilder(256);
            GetClassNameW(hwnd, cls, 256);
            var clsName = cls.ToString();
            if (ShellClasses.Contains(clsName, StringComparer.OrdinalIgnoreCase))
            {
                why = $"壳层窗口 {clsName}";
                return false;
            }

            if (!GetWindowRect(hwnd, out var wr)) { why = "取矩形失败"; return false; }

            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            var mon = MonitorFromWindow(hwnd, 2);   // MONITOR_DEFAULTTONEAREST
            if (mon == IntPtr.Zero || !GetMonitorInfo(mon, ref mi)) { why = "取显示器失败"; return false; }

            var m = mi.rcMonitor;
            const int slack = 2;
            var covers = wr.Left <= m.Left + slack && wr.Top <= m.Top + slack
                      && wr.Right >= m.Right - slack && wr.Bottom >= m.Bottom - slack;

            var title = new StringBuilder(256);
            GetWindowTextW(hwnd, title, 256);
            why = $"class={clsName} title='{title}' rect={wr.Left},{wr.Top}-{wr.Right},{wr.Bottom} " +
                  $"monitor={m.Left},{m.Top}-{m.Right},{m.Bottom} covers={covers}";
            return covers;
        }
        catch (Exception ex)
        {
            why = "异常 " + ex.GetType().Name;
            return false;
        }
    }

    private int _fullscreenHits;

    /// <summary>有全屏应用时自动藏起来，退出了再回来（用户手动隐藏的不动）。</summary>
    private void CheckFullscreen()
    {
        if (_userHidden) return;

        var full = IsForegroundFullscreen(out var why);
        if (full)
        {
            // 连续两次命中才隐藏（4 秒），避免壳层弹窗/瞬时前台切换造成闪烁
            _fullscreenHits++;
            Diag($"全屏检测命中 #{_fullscreenHits}：{why}");
            if (_fullscreenHits >= 2 && Visibility == Visibility.Visible)
            {
                CloseInvite();
                if (ChaseActive) StopChase();
                _autoHidden = true;
                Diag("自动隐藏（全屏应用）");
                Hide();
            }
            return;
        }

        if (_fullscreenHits > 0)
        {
            Diag($"全屏检测解除：{why}");
            _fullscreenHits = 0;
        }

        if (_autoHidden)
        {
            _autoHidden = false;
            Diag("自动恢复显示");
            Show();
        }
    }
    // ------------------------------------------------------------ 头部跟随鼠标

    // 水平 25 档：-50° ~ +70°，步长 5°（右侧多 20°，因为 3/4 侧脸往右转看着幅度小）
    /// <summary>图集列数（-50°~+70°，步长 5°）。</summary>
    private const int FollowCols = 25;
    private const double FollowStepDeg = 5.0;
    private const int FollowNeutralCol = 10;   // yaw = 0 那一档（步长 5°）
    private const int FollowRows = 5;    // 抬头20 / 抬头10 / 平视 / 低头10 / 低头20

    private readonly DispatcherTimer _followTimer;
    private bool _followActive;
    private int _followRow = 2, _followCol = FollowNeutralCol;
    private DateTime _followUntil = DateTime.MinValue;
    private DateTime _followShownAt = DateTime.MinValue;

    /// <summary>在这之前暂停跟随（播动作时用，避免跟随帧把动作顶掉）。</summary>
    private DateTime _followPausedUntil = DateTime.MinValue;

    /// <summary>播站立待机：朝右时用 sidle_r（小游戏里追右边后不会突然转向）。</summary>
    private Task PlayStandIdleForFacingAsync()
    {
        if (_walkTimer is not null) StopWalk("回站立待机");   // 别让待机跟着一起滑
        return _player.PlayStandClipAsync(_faceRight && _player.HasClip("sidle_r") ? "sidle_r" : "sidle", -1);
    }

    /// <summary>按片子名更新朝向：_r 结尾 = 朝右，_l 结尾 = 朝左。</summary>
    private void UpdateFacingFromClip(string clip)
    {
        if (clip.EndsWith("_r", StringComparison.Ordinal)) _faceRight = true;
        else if (clip.EndsWith("_l", StringComparison.Ordinal)) _faceRight = false;
    }

    /// <summary>当前是否处于跟随状态：设置里常开，或者处在跟随动作区间内。</summary>
    private bool FollowWanted =>
        _settings.HeadFollow || DateTime.Now < _followUntil;

    /// <summary>当前姿态对应的素材名（坐姿 follow / 站立 sfollow）。</summary>
    private string FollowAtlas
    {
        get
        {
            if (!_standing) return "follow";
            if (_faceRight && _player.HasClip("sfollow_r")) return "sfollow_r";
            return "sfollow";
        }
    }

    /// <summary>站立时是否朝右（小游戏追右边时会切换，避免停下时突然转向）。</summary>
    private bool _faceRight;

    /// <summary>进入一段跟随动作区间（默认 9~14 秒），结束后自动回到其他动作。</summary>
    private void StartFollowBurst()
    {
        if (!_loader.Book.Has(GraphType.HeadFollow)) return;
        _followUntil = DateTime.Now + TimeSpan.FromSeconds(Random.Shared.Next(9, 15));
        System.Diagnostics.Debug.WriteLine($"[follow] 进入区间，到 {_followUntil:HH:mm:ss}");
    }

    /// <summary>
    /// 每 100ms 采样鼠标在全屏中的位置，换算成 3×21 网格，并逐格平滑移动。
    /// 以「猫头在屏幕上的位置」为原点，全屏映射到 ±50°，所以鼠标跑到屏幕哪个角落都跟随。
    /// </summary>
    private void UpdateHeadFollow()
    {
        if (_asleep)
        {
            // 睡眠中：只复位跟随状态，不要走 EndHeadFollow（它会播待机、把打盹顶掉）
            _followActive = false;
            _followCol = FollowNeutralCol;
            _followRow = 2;
            return;
        }

        // 站立动作（走/跑/飞奔/跳/摇尾巴…）进行中，跟随不许抢画面：
        // 抢了就会看到"猫停下来看你、窗口却还在滑"，跑一半也会被截断
        if (StandBusy)
            return;

        // 小游戏里"追逐"阶段不要插手：那时画面归步态，跟随只在 _chaseNear（停下来看）时才该出现。
        // 否则两边会互相抢帧、互相 Start/StopWalk，看着就是"猫不走路、窗口却在滑"。
        if (ChaseActive && !_chaseNear)
            return;

        if (!FollowWanted || _dragging || _resizeMode || Visibility != Visibility.Visible)
        {
            EndHeadFollow();
            return;
        }

        // 正在播动作（比如站立摸头）：跟随彻底让位。这里连 EndHeadFollow 都不能调，
        // 否则它会顺手播一次待机，把刚起来的动作顶掉。
        if (DateTime.Now < _followPausedUntil)
            return;
        if (!_loader.Book.Has(GraphType.HeadFollow) || !GetCursorPos(out var cursor))
            return;

        var dpi = VisualTreeHelper.GetDpi(this);

        // 以"猫头在屏幕上的位置"为原点，而不是屏幕中心：
        // 这样猫在屏幕下半部时，鼠标在它头顶就是"抬头"，而不是被算成"低头/平视"
        var headX = Left + Width / 2;
        var headY = Top + Height * 0.42;

        var left = SystemParameters.VirtualScreenLeft;
        var top = SystemParameters.VirtualScreenTop;
        var right = left + Math.Max(200.0, SystemParameters.VirtualScreenWidth);
        var bottom = top + Math.Max(200.0, SystemParameters.VirtualScreenHeight);

        var mx = cursor.X / dpi.DpiScaleX;
        var my = cursor.Y / dpi.DpiScaleY;

        // 各自按"离哪边更远"归一化，保证 -1..+1 覆盖鼠标能到达的整个范围
        // 各自按"猫头到该侧屏幕边缘"的距离归一化：鼠标贴到任何一边都能到 ±1
        var maxRight = Math.Max(120.0, right - headX);
        var maxLeft = Math.Max(120.0, headX - left);
        var maxDown = Math.Max(120.0, bottom - headY);
        var maxUp = Math.Max(120.0, headY - top);
        var dx = mx >= headX
            ? Math.Min(1.0, (mx - headX) / maxRight)
            : -Math.Min(1.0, (headX - mx) / maxLeft);
        var dy = my >= headY
            ? Math.Min(1.0, (my - headY) / maxDown)
            : -Math.Min(1.0, (headY - my) / maxUp);

        // 右侧允许转到 70°、左侧 50°，中立档固定在 col 10
        // 小游戏的"近距离看着鼠标"模式用近场映射（±NearZone → ±38°），比全屏映射更容易看出转头
        var wantYaw = _chaseNearYaw ?? (dx >= 0 ? dx * 70.0 : dx * 50.0);

        // 朝右那套素材（相机在 −X 侧）投影是镜像的：col 0 看画面右、col 24 看画面左，
        // 所以列索引要反过来算；不反的话鼠标在右边时头会朝左看。
        var useRightAtlas = _standing && _faceRight && _player.HasClip("sfollow_r");
        var targetCol = useRightAtlas
            ? (int)Math.Round((50.0 - wantYaw) / FollowStepDeg)
            : (int)Math.Round((wantYaw + 50.0) / FollowStepDeg);
        targetCol = Math.Clamp(targetCol, 0, FollowCols - 1);
        var targetRow = dy < -0.6 ? 0 : dy < -0.22 ? 1 : dy > 0.6 ? 4 : dy > 0.22 ? 3 : 2;

        // 追帧：一次最多跨 3 格，快速甩鼠标也能跟上
        var moved = false;
        if (_followCol < targetCol) { _followCol = Math.Min(targetCol, _followCol + 3); moved = true; }
        else if (_followCol > targetCol) { _followCol = Math.Max(targetCol, _followCol - 3); moved = true; }
        if (_followRow < targetRow) { _followRow = Math.Min(targetRow, _followRow + 2); moved = true; }
        else if (_followRow > targetRow) { _followRow = Math.Max(targetRow, _followRow - 2); moved = true; }

        // 没动但可能被别的动画打断了：隔一会儿重发一次
        if (!moved && _followActive && (DateTime.Now - _followShownAt).TotalMilliseconds < 800)
            return;

        _followActive = true;
        _followShownAt = DateTime.Now;
        var index = _followRow * FollowCols + _followCol;
        System.Diagnostics.Debug.WriteLine($"[follow] dx={dx:F2} dy={dy:F2} -> row={_followRow} col={_followCol} idx={index}");
        if (_walkTimer is not null) StopWalk("跟随接管");
        _ = _player.ShowStaticAsync(FollowAtlas, index);
    }
    /// <summary>
    /// 暂停跟随一小段时间，让正在播的小动作能完整播完（否则 60ms 一次的跟随帧会把它顶掉）。
    /// 只清跟随状态、不播待机；到点后 UpdateHeadFollow 会自己切回跟随帧。
    /// </summary>
    private void PauseFollowFor(double seconds)
    {
        if (!_followActive && !FollowWanted) return;
        _followPausedUntil = DateTime.Now.AddSeconds(seconds);
        _followActive = false;
    }

    /// <summary>结束跟随，回到当前姿态的待机。</summary>
    private void EndHeadFollow()
    {
        if (!_followActive) return;
        _followActive = false;
        _followCol = FollowNeutralCol;
        _followRow = 2;
        if (_asleep) return;    // 睡眠中绝不能播待机：那会取消打盹，导致 _asleep=true 但画面清醒、后续计时器全被挡住
        if (_standing)
            _ = PlayStandIdleForFacingAsync();
        else
            _ = _player.PlayIdleAsync();
    }
    // ------------------------------------------------------------ 姿态轮换（坐累/站累）

    private static readonly string[] StandUpTexts = { "（站起来活动活动）", "（站会儿）", "（伸个懒腰站起来）" };
    private static readonly string[] StrollTexts = { "（溜达一下）", "（走两步）", "（四处看看）" };
    private static readonly string[] RunTexts = { "（冲一段）", "（跑两步）", "（撒欢）" };
    private static readonly string[] GallopTexts = { "（冲啊！）", "（一溜烟）", "（撒腿就跑）" };
    private static readonly string[] SitDownTexts = { "（坐会儿）", "（站累了，坐下）", "（歇一会儿）" };

    /// <summary>坐久了站起来 / 走两步；站久了坐下来。只在真正闲置时触发。</summary>
    /// <summary>专注（番茄钟工作段）时不走动。</summary>
    private bool InFocusMode => _pomoState == PomoState.Work && !_pomoPaused;

    /// <summary>动作频率档位：决定走动/跑/飞奔这些大幅度动作出现得勤不勤。0 低 / 1 中 / 2 高。</summary>
    private int ActionLevel => Math.Clamp(_settings.ActionFrequency, 0, 2);

    /// <summary>坐久了站起来 / 走两步；站久了坐下来。只在真正闲置时触发。</summary>
    private void ChurnPosture()
    {
        // 小游戏进行中：完全不做姿态轮换（否则会"站累了坐下"打断追逐）
        if (ChaseActive) return;
        // 小动作播放中不要打断（换姿态/邀约都等它播完）
        if (ActionBusy) return;
        // 待机时小概率发起"一起玩"的邀约（专注模式/冷却中不会触发）
        if (MaybeInvite()) return;
        if (_dragging || _asleep || _resizeMode || StandBusy) return;
        if (DateTime.Now - _lastInteraction < TimeSpan.FromSeconds(18)) return;
        if (Visibility != Visibility.Visible) return;

        var level = ActionLevel;
        if (!_standing)
        {
            if (!_loader.Book.Has(GraphType.Stand)) return;
            // 溜达概率：高 0.60 / 中 0.45 / 低 0.25；专注模式一律不走
            var pStroll = InFocusMode ? 0.0 : level switch { 2 => 0.60, 0 => 0.25, _ => 0.45 };
            if (Random.Shared.NextDouble() < pStroll)
            {
                StrollOnce();
            }
            else
            {
                SayFrom(StandUpTexts);
                // 按当前朝向选 sidle / sidle_r，否则朝右时站起来会"翻个面"
                _ = PlayStandAsync(SideClip("sidle", "sidle_r"), 2, 0, "");
            }
            return;
        }

        // 站着不动的时候，偶尔先东张西望一圈
        if (!InFocusMode && Random.Shared.Next(100) < 35 && _loader.Book.Has(GraphType.Stand))
        {
            SayFrom(LookAroundTexts);
            _ = PlayStandAsync(SideClip("slook", "slook_r"), 1, 0, "");
            return;
        }

        var qStroll = InFocusMode ? 0.0 : level switch { 2 => 0.60, 0 => 0.20, _ => 0.40 };
        if (Random.Shared.NextDouble() < qStroll)
        {
            StrollOnce();
        }
        else
        {
            SayFrom(SitDownTexts);
            _ = ReturnSittingAsync();
        }
    }

    /// <summary>按档位设置轮换节奏：高 18s / 中 22s / 低 28s。</summary>
    private void ApplyActionFrequency()
    {
        if (_postureTimer is null) return;
        _postureTimer.Interval = TimeSpan.FromSeconds(ActionLevel switch { 2 => 18, 0 => 28, _ => 22 });
    }
    private void StrollOnce()
    {
        // 靠近边界时优先往空间大的方向走，避免刚起步就撞墙
        var leftEdge = SystemParameters.VirtualScreenLeft;
        var rightEdge = leftEdge + SystemParameters.VirtualScreenWidth;
        var roomLeft = Left - leftEdge;
        var roomRight = rightEdge - (Left + Width);

        bool left;
        if (roomLeft < 120 && roomRight > roomLeft) left = false;
        else if (roomRight < 120 && roomLeft > roomRight) left = true;
        else left = Random.Shared.Next(2) == 0;

        // 那个方向空间够大、又不是专注模式，才有机会把"走"升级成"跑"，再低概率升级成"飞奔"
        var room = left ? roomLeft : roomRight;
        var runClip = left ? "srun_l" : "srun_r";
        var galClip = left ? "sgal_l" : "sgal_r";
        var canRun = !InFocusMode && room > Width * 1.8 && _player.HasClip(runClip);
        // 飞奔要的空间更大（2 轮约 370px），概率压得很低，偶尔来个冲刺
        var canGallop = !InFocusMode && room > Width * 2.6 && _player.HasClip(galClip);
        var gallop = canGallop && Random.Shared.NextDouble() < 0.12;
        var run = !gallop && canRun && Random.Shared.NextDouble() < 0.45;

        SayFrom(gallop ? GallopTexts : run ? RunTexts : StrollTexts);
        _ = PlayStandAsync(gallop ? galClip : run ? runClip : (left ? "swalk_l" : "swalk_r"),
                           gallop ? 3 : run ? 3 : 5,
                           left ? -1 : 1,
                           "");
    }
    // ------------------------------------------------------------ 摸头连击

    private int _patCount;
    private DateTime _lastPatAt = DateTime.MinValue;

    /// <summary>连续摸头的反应：5 下开始呼噜，10 下就甩头躲开。</summary>
    private bool HandleCatPat()
    {
        _patCount = (DateTime.Now - _lastPatAt).TotalSeconds < 2.5 ? _patCount + 1 : 1;
        _lastPatAt = DateTime.Now;

        if (_patCount == 10)
        {
            _patCount = 0;
            _ = _player.PlayShakeAsync();
            Say("够了喵！（甩甩头）", 3);
            return true;
        }

        if (_patCount == 5)
        {
            Say("（呼噜呼噜~ 舒服）", 3);
            return true;
        }

        return false;
    }

    // ------------------------------------------------------------ 时段偏好

    /// <summary>待机随机池的掷骰：早上偏张望、午后偏哈欠发呆、深夜偏发呆。</summary>
    private static int IdleRoll()
    {
        var h = DateTime.Now.Hour;
        var r = Random.Shared.Next(11);

        if (h >= 5 && h < 10)          // 早上精神，多看看四周
            return r switch { 2 => 6, 3 => 6, 7 => 0, _ => r };

        if (h >= 12 && h < 15)         // 午后犯困，多打哈欠/发呆
            return r switch { 5 => 2, 6 => 7, 1 => 2, _ => r };

        if (h >= 22 || h < 5)          // 深夜，多发呆少折腾
            return r switch { 1 => 7, 3 => 7, 5 => 7, 8 => 7, _ => r };

        return r;
    }

    // ------------------------------------------------------------ 关心话（不绑定动作）

    /// <summary>按当前时段拼一个文案池：深夜/早上各加几句专属的。</summary>
    private static string[] CarePool()
    {
        var hour = DateTime.Now.Hour;
        if (hour >= 21 || hour < 5)
            return CareTexts.Concat(CareNight).ToArray();
        if (hour >= 5 && hour < 10)
            return CareTexts.Concat(CareMorning).ToArray();
        return CareTexts;
    }

    /// <summary>安排下一次关心话。</summary>
    private void ScheduleCare()
    {
        var sec = Random.Shared.Next(CareInterval.Min, CareInterval.Max + 1);
        _careTimer.Interval = TimeSpan.FromSeconds(sec);
        _careTimer.Stop();
        _careTimer.Start();
    }

    /// <summary>到点了：说一句关心的话（不换动画，不打断任何动作）。</summary>
    private void OnCareTick()
    {
        if (_dragging || _asleep || _resizeMode || Visibility != Visibility.Visible)
        {
            _careTimer.Interval = TimeSpan.FromSeconds(40);   // 稍后再试
            _careTimer.Start();
            return;
        }

        SayFrom(CarePool(), 4);
        ScheduleCare();
    }
    // ------------------------------------------------------------ 行走位移

    private const double WalkPxPerSecond = 48;   // 走：原 32 有点拖，提到 48（小跑 150）

    private double _walkSpeed = WalkPxPerSecond;
    private int _walkGen;                        // 每次 StartWalk 自增：旧的收尾逻辑不许停掉新动作的位移
    private DateTime _walkLastTick = DateTime.MinValue;
    private const double RunPxPerSecond = 150;   // 跑的时候窗口移动更快（和走拉开差距）
    private const double GallopPxPerSecond = 430; // 飞奔再快一档（走 32 / 跑 150）

    private void StartWalk(int dir, double? pxPerSecond = null)
    {
        _walkSpeed = pxPerSecond ?? WalkPxPerSecond;
        _walkGen++;
        _walkLastTick = DateTime.MinValue;   // 按真实经过时间算位移，定时器抖动不影响速度
        StopWalk("重新开始走");
        Diag($"[walk] start dir={dir} speed={_walkSpeed:0} L={Left:0}");
        _walkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
        _walkTimer.Tick += (_, _) =>
        {
            // 保险：动作已结束（或被打断）就别再让窗口滑行
        if (!StandBusy && !ChaseActive)
            {
                StopWalk("动作已结束");
                return;
            }

            var now = DateTime.UtcNow;
            var dt = _walkLastTick == DateTime.MinValue ? 0.0 : (now - _walkLastTick).TotalSeconds;
            _walkLastTick = now;
            dt = Math.Clamp(dt, 0.0, 0.15);      // 卡顿/最小化时不要一次跳很远
            var dx = _walkSpeed * dt * dir;
            var next = Left + dx;
            var left = SystemParameters.VirtualScreenLeft;
            var right = left + SystemParameters.VirtualScreenWidth;

            // 撞到屏幕边界：停位移、吐槽一句、结束行走
            if ((dir < 0 && next <= left) || (dir > 0 && next + Width >= right))
            {
                System.Diagnostics.Debug.WriteLine($"[walk] 撞边 L={Left:0} W={Width:0} 右={Left + Width:0}");
                StopWalk("撞边");
                if (!ChaseActive) SayFrom(EdgeTexts);   // 小游戏里贴边是常态，别刷"撞墙了"
                if (!ChaseActive) _ = PlayStandIdleForFacingAsync();
                return;
            }
            Left = next;
        };
        _walkTimer.Start();
    }

    private void StopWalk(string reason = "")
    {
        if (_walkTimer is not null)
            Diag($"[walk] stop（{reason}）L={Left:0}");
        _walkTimer?.Stop();
        _walkTimer = null;
        SavePosition();
    }
    // ------------------------------------------------------------ 番茄钟 / 喝水提醒
    private enum PomoState { None, Work, Break }

    private readonly DispatcherTimer _tickTimer;
    private PomoState _pomoState = PomoState.None;
    private int _pomoRemainSec;
    private int _pomoTotalSec;
    private bool _pomoPaused;
    private int _waterRemainSec;
    private int _waterTickCount;


    // 拖拽状态
    private bool _pressed;
    private bool _dragging;
    private Point _pressPoint;
    private POINT _lastCursor;

    private DateTime _lastInteraction = DateTime.Now;

    public PetWindow()
    {
        InitializeComponent();

        _petImage = new Image
        {
            Width = 500,
            Height = 500,
            Stretch = Stretch.Uniform,
        };
        PetHolder.Child = _petImage;

        var modRoot = Path.Combine(AppContext.BaseDirectory, "mod");
        try
        {
            _loader = PetLoader.Load(modRoot);
        }
        catch (Exception ex)
        {
            // 素材缺失/损坏：必须明确报错并退出，否则会留下「没有窗口但占着单实例锁」的僵尸进程
            MessageBox.Show("素材加载失败，程序将退出。\n\n" + ex.Message + "\n\n目录：" + modRoot,
                "CatPet 启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Application.Current?.Shutdown(1);
            throw;
        }
        _player = new PetPlayer(_loader.Book, _petImage);
        // 注意：AnimationChanged 在后台播放线程触发，改 UI 必须切回 Dispatcher
        _player.AnimationChanged += anim => Dispatcher.InvokeAsync(() => Title = $"CatPet - {anim.Info}");

        // 尺寸：500 逻辑像素 × 缩放
        // 优先级：环境变量 CATPET_ZOOM > 上次拖拽记住的大小 > 默认 0.55
        _settings = Settings.Load();
        Topmost = _settings.Topmost;   // XAML 里写死 True，这里按用户设置覆盖
        var zoom = _settings.Zoom;
        var envZoom = Environment.GetEnvironmentVariable("CATPET_ZOOM");
        if (!string.IsNullOrWhiteSpace(envZoom) &&
            double.TryParse(envZoom, NumberStyles.Float, CultureInfo.InvariantCulture, out var z) &&
            z >= Settings.MinZoom && z <= Settings.MaxZoom)
        {
            zoom = z;
        }

        Width = Height = 500 * zoom;
        _scaleBaseSize = Width;   // 站姿放大以这个尺寸为基准

        ApplyTextSize();

        // 恢复上次的位置；没有记录（或已经不在任何屏幕上）就放到右下角
        if (_settings.LastLeft is { } lastLeft && _settings.LastTop is { } lastTop && IsOnAnyScreen(lastLeft, lastTop))
        {
            Left = lastLeft;
            Top = lastTop;
        }
        else
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - Width - 60;
            Top = area.Bottom - Height + 10;
        }

        SizeChanged += (_, _) => { if (Bubble is not null && BubbleText is not null) ApplyTextSize(); };

        _bubbleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        _bubbleTimer.Tick += (_, _) => { _bubbleTimer.Stop(); Bubble.Visibility = Visibility.Collapsed; };
        // 番茄钟 / 喝水提醒共用 1 秒节拍
        _waterRemainSec = Math.Max(1, _settings.WaterRemindMinutes) * 60;
        _tickTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _tickTimer.Tick += (_, _) => { OnTick(); LogWindowState(); };
        _tickTimer.Start();

        // 空闲时随机来点小动作：转头 / 歪头 / 打哈欠 / 叫一声
        _idleTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(14) };
        _idleTimer.Tick += (_, _) =>
        {
            if (_dragging || _asleep || _standing) return;                   // 站立中不插播坐姿小动作
            if (ActionBusy) return;                                          // 上一个小动作还没播完
            if (DateTime.Now - _lastInteraction < TimeSpan.FromSeconds(6)) return;  // 刚被逗过，先别自娱自乐

            var roll = IdleRoll();

            // 头部跟随中：只放"有生活感"的小动作（舔毛/哈欠/歪头/抖毛/东张西望），
            // 转头/发呆/喵叫在看着鼠标时没什么信息量，直接跳过
            if ((_followActive || _followUntil > DateTime.Now) && !LivingAction(roll)) return;

            // 下面每个分支都会播一个 1~4 秒的小动作，先占住这个时间窗
            MarkActionBusy(4.0);
            Diag($"idle: roll={roll} follow={_followActive}");
            switch (roll)
            {
                case 0 when _loader.Book.Has(GraphType.Idel):
                    PauseFollowFor(2.6);      // 让跟随先让位，动作播完再回去盯鼠标
                    _ = _player.PlayTurnAsync();
                    return;
                case 5 when _loader.Book.Has(GraphType.Shake):
                    PauseFollowFor(2.2);
                    _ = _player.PlayShakeAsync();
                    SayFrom(ShakeTexts);
                    return;
                case 6 when _loader.Book.Has(GraphType.LookAround):
                    PauseFollowFor(4.0);
                    _ = _player.PlayLookAroundAsync();
                    SayFrom(LookAroundTexts);
                    return;
                case 7 when _loader.Book.Has(GraphType.Gaze):
                    PauseFollowFor(3.0);
                    _ = _player.PlayGazeAsync();
                    SayFrom(GazeTexts);
                    return;
                case 8 when _loader.Book.Has(GraphType.HeadFollow):
                    StartFollowBurst();   // 进入一段"盯着鼠标看"的动作区间
                    return;
                case 1 when _loader.Book.Has(GraphType.Tilt):
                    PauseFollowFor(2.0);
                    _ = _player.PlayTiltAsync();
                    SayFrom(TiltTexts);
                    return;
                case 2 when _loader.Book.Has(GraphType.Yawn):
                    PauseFollowFor(2.2);
                    _ = _player.PlayYawnAsync();
                    SayFrom(YawnTexts);
                    return;
                case 3 when _loader.Book.Has(GraphType.Lick):
                    PauseFollowFor(2.2);
                    _ = _player.PlayLickAsync();
                    SayFrom(LickTexts);
                    return;
                default:
                    PauseFollowFor(2.0);
                    _ = _player.PlaySayAsync();
                    SayFrom(Meows);
                    return;
            }
        };

        // 跟随期间允许出现的"生活化小动作"：舔毛 / 哈欠 / 歪头 / 抖毛 / 东张西望
        static bool LivingAction(int roll) => roll is 1 or 2 or 3 or 5 or 6;

        // 隔一段时间在"正脸"和"侧脸"之间换一次姿势
        _faceTimer = new DispatcherTimer { Interval = FaceSwitchEvery };
        _faceTimer.Tick += (_, _) =>
        {
            if (_dragging || _asleep || _resizeMode || _standing) return;
            if (ActionBusy || _followActive) return;      // 小动作/跟随中不换姿势
            if (!_loader.Book.Has(GraphType.Idle_Front)) return;

            _facingFront = !_facingFront;
            if (_facingFront) _ = _player.PlayFrontIdleAsync();
            else _ = _player.PlaySideIdleAsync();
        };
        // 更久没人理 → 打盹
        // 姿态轮换：坐久了站一站 / 走一走，站久了坐回去
        // 眨眼：独立节拍（6~11 秒一次），比"待机池里 1/11 概率"自然得多
        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(7) };
        _blinkTimer.Tick += (_, _) =>
        {
            _blinkTimer.Interval = TimeSpan.FromSeconds(Random.Shared.Next(6, 12));
            if (_dragging || _asleep || _standing || _followActive || ActionBusy) return;
            if (!_loader.Book.Has(GraphType.Blink)) return;
            MarkActionBusy(0.8);
            _ = _player.PlayBlinkAsync();
        };

        _fullscreenTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _fullscreenTimer.Tick += (_, _) => CheckFullscreen();

        _followTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _followTimer.Tick += (_, _) => UpdateHeadFollow();

        _careTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(180) };
        _careTimer.Tick += (_, _) => OnCareTick();

        _postureTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(22) };
        _postureTimer.Tick += (_, _) => ChurnPosture();

        _sleepTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _sleepTimer.Tick += (_, _) => CheckSleep();

        // 提醒相关入口都放在托盘图标里
        InitTray();

        // 托盘菜单比较隐蔽，隔一段时间用一句话提示一次（用过计时后就不再提）
        _hintTimer = new DispatcherTimer { Interval = HintFirstDelay };
        _hintTimer.Tick += (_, _) => ShowFeatureHint();
        _hintTimer.Start();

        Loaded += async (_, _) =>
        {
            LogLoaderState();

            // 开机问候：按时段 / 节日说一句（CATPET_NOW 可模拟日期，方便验证节日）
            var now = DateTime.Now;
            var fake = Environment.GetEnvironmentVariable("CATPET_NOW");
            if (!string.IsNullOrWhiteSpace(fake) &&
                DateTime.TryParseExact(fake.Trim(), new[] { "yyyy-MM-dd HH:mm", "yyyy-MM-dd" },
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out var fakeNow))
            {
                now = fakeNow;
            }
            var greeting = Greeting.Build(now);

            Say(greeting, 5);
            Console.WriteLine($"[CatPet] 问候 {now:MM-dd HH:mm} → {greeting}");

            _idleTimer.Start();
            _sleepTimer.Start();
            ApplyActionFrequency();
            ScheduleCare();
            _followTimer.Start();
            _fullscreenTimer.Start();
            _postureTimer.Start();
            _faceTimer.Start();
            _blinkTimer.Start();
            _hintTimer.Start();

            // 有开场运镜素材时不先播坐姿待机：否则窗口会先闪一帧坐着再开始走
            if (!_player.HasClip("cin_intro"))
                await _player.PlayIdleAsync();

            // 开场镜头：从屏幕右侧由小变大走进来，落地后站着（之后照常"站累了坐下"）
            try { await PlayEntranceAsync(); } catch { /* 运镜失败也要正常启动 */ }

            // 测试用：CATPET_CHASE=1 启动 1.5 秒后自动进入追鼠标小游戏（跳过说明卡）
            // 临时测试钩子：走 → 跑 的中断切换
            if (Environment.GetEnvironmentVariable("CATPET_RUNTEST") == "1")
            {
                _ = Task.Delay(5000).ContinueWith(_ => Dispatcher.InvokeAsync(async () =>
                {
                    Diag($"[test] 点跑 L={Left:0} gen={_player.Generation}");
                    await PlayStandAsync("srun_l", 3, -1, "");
                    Diag($"[test] 跑结束 L={Left:0}");
                }));
                _ = Task.Delay(9500).ContinueWith(_ => Dispatcher.InvokeAsync(async () =>
                {
                    Diag($"[test] 点飞奔 L={Left:0} gen={_player.Generation}");
                    await PlayStandAsync("sgal_l", 3, -1, "");
                    Diag($"[test] 飞奔结束 L={Left:0}");
                }));
            }

            if (Environment.GetEnvironmentVariable("CATPET_WALKTEST") == "1")
            {
                _ = Task.Delay(2000).ContinueWith(_ => Dispatcher.InvokeAsync(async () =>
                {
                    Diag("[test] 向左走");
                    await PlayStandAsync("swalk_l", 5, -1, "");
                }));
                _ = Task.Delay(4000).ContinueWith(_ => Dispatcher.InvokeAsync(async () =>
                {
                    Diag("[test] 向左跑（打断）");
                    await PlayStandAsync("srun_l", 5, -1, "");
                }));
                _ = Task.Delay(9500).ContinueWith(_ => Dispatcher.InvokeAsync(() => Diag($"[test] 结束 L={Left:0}")));
            }

            if (Environment.GetEnvironmentVariable("CATPET_CHASE") == "1")
            {
                _ = Task.Delay(1500).ContinueWith(_ => Dispatcher.InvokeAsync(() =>
                {
                    _settings.ChaseIntroSeen = true;
                    StartChase();
                }));
            }

            // 播放线程里的异常不会冒到 UI，这里显式检查一次，避免"窗口透明像没启动"
            await Task.Delay(1500);
            if (_player.LastError is { } err)
            {
                Console.WriteLine("[CatPet][ERR] " + err);
                Say("动画出错：" + (err.Length > 18 ? err[..18] + "…" : err));
            }
        };
    }

    private void LogLoaderState()
    {
        Console.WriteLine($"[CatPet] 形象：{_loader.Config.Name} ({_loader.Config.PetName})");
        Console.WriteLine($"[CatPet] 资源目录：{_loader.Config.RootDir}");
        Console.WriteLine($"[CatPet] 动画数量：{_loader.Book.Count}");
        foreach (var (type, names) in _loader.Book.Names)
            Console.WriteLine($"[CatPet]   {type}: {string.Join(", ", names)}");
        foreach (var err in _loader.Errors)
            Console.WriteLine("[CatPet][ERR] " + err);

        if (_loader.Errors.Count > 0)
            Say(_loader.Errors[0].Length > 24 ? _loader.Errors[0][..24] + "…" : _loader.Errors[0]);
    }

    // ---------------------------------------------------------------- 交互

    private void OnLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
            return;

        if (_cineActive) return;   // 开场/退场运镜期间不接点击，免得动画被打断

        _pressed = true;
        _dragging = false;
        _pressPoint = e.GetPosition(this);
        GetCursorPos(out _lastCursor);
        Wake();
        Root.CaptureMouse();
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        // 开场/退场运镜期间不接拖拽，免得动画被拽断
        if (_cineActive) return;

        if (!_pressed || e.LeftButton != MouseButtonState.Pressed)
            return;

        var current = e.GetPosition(this);
        if (!_dragging && (current - _pressPoint).Length < 6)
            return;

        if (!_dragging)
        {
            _dragging = true;
            _lastInteraction = DateTime.Now;

            // 被拖起来时的反应：站着的话先停走、回站立待机；坐着的话转个脸「喵？」
            if (_standing)
            {
                StopWalk("拖动打断");
                _ = PlayStandIdleForFacingAsync();
            }
            else
            {
                _ = _player.PlayFaceSwitchAsync();
            }
            Say("喵？", 2);
        }

        MoveByCursor();
    }

    private void OnLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_pressed)
            return;

        _pressed = false;
        _lastInteraction = DateTime.Now;
        Root.ReleaseMouseCapture();

        if (_dragging)
        {
            _dragging = false;
            SavePosition();      // 拖完记住位置
            return;
        }

        // 小游戏进行中：点猫本体就退出（不玩啦）
        if (ChaseActive)
        {
            StopChase(say: true);
            return;
        }

        // 邀约弹窗开着时点猫 = 关掉弹窗（算"不玩"）
        if (InviteOpen)
        {
            CloseInvite();
            return;
        }

        // 点头顶 → 蹭手；点别处 → 喵叫
        // 站立姿态下点击：先停走、回站立待机（不自动坐下，只有选了坐姿动作/拖拽才切）
        var point = e.GetPosition(Root);
        if (_standing)
        {
            StopWalk("点击打断");

            // 站立时点头顶：用站立闭眼模型做的「闭眼待机」演一段被摸的反应
            if (_loader.Config.TouchHead.Contains(point) && _player.HasClip(SideClip("sclose", "sclose_r")))
            {
                _followPausedUntil = DateTime.Now.AddSeconds(3.2);   // 别让跟随帧把闭眼动作顶掉
                Say("（呼噜呼噜）", 3);
                _ = PlayStandClosedAsync();
                return;
            }

            _ = PlayStandIdleForFacingAsync();
            Say(_loader.Config.TouchHead.Contains(point) ? "喵？" : "喵～", 2);
            return;
        }

        if (_loader.Config.TouchHead.Contains(point) && _loader.Book.Has(GraphType.Touch_Head))
        {
            if (HandleCatPat())
                return;                      // 连击到阈值时已经播了对应反应
            _ = _player.PlayTouchHeadAsync();
            SayFrom(TouchTexts);
            return;
        }

        _ = _player.PlaySayAsync();
        SayFrom(Meows);
    }

    /// <summary>记录一次交互时间；如果正在打盹，把它叫醒。</summary>
    private void Wake()
    {
        _lastInteraction = DateTime.Now;
        _facingFront = false;   // 任何动作结束后都会回到侧脸待机
        if (!_asleep)
            return;

        _asleep = false;
        _ = _player.PlayIdleAsync();
    }

    /// <summary>很久没交互 → 打盹。</summary>
    private void CheckSleep()
    {
        // 小游戏进行中不打盹（否则会"站累了坐会儿"把追逐打断）
        if (ChaseActive || InviteOpen) return;
        if (ActionBusy) return;   // 小动作播到一半别突然坐下/睡着
        if (_asleep || _dragging)
            return;
        if (DateTime.Now - _lastInteraction < SleepAfter)
            return;
        if (!_loader.Book.Has(GraphType.Sleep))
            return;

        // 站立姿态下不插播坐姿睡觉：先坐回去，之后的打盹走正常流程
        if (_standing)
        {
            Say("（站累了，坐会儿）", 3);
            _ = ReturnSittingAsync();
            _lastInteraction = DateTime.Now;
            return;
        }

        _asleep = true;
        _ = _player.PlaySleepAsync();
        SayFrom(SleepTexts);
    }



    private void OnRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        Wake();

        var menu = new ContextMenu();

        // ---------- 动作 ----------
        menu.Items.Add(new MenuItem { Header = "动作", IsEnabled = false, FontWeight = FontWeights.Bold });

        var meow = new MenuItem { Header = "喵一声" };
        meow.Click += async (_, _) => { await ReturnSittingAsync(); _ = _player.PlaySayAsync(); SayFrom(Meows); };
        menu.Items.Add(meow);

        var touch = new MenuItem { Header = "摸摸头" };
        touch.Click += async (_, _) => { _lastInteraction = DateTime.Now; await ReturnSittingAsync(); _ = _player.PlayTouchHeadAsync(); SayFrom(TouchTexts); };
        menu.Items.Add(touch);

        var lick = new MenuItem { Header = "舔舔毛" };
        lick.Click += async (_, _) => { _lastInteraction = DateTime.Now; await ReturnSittingAsync(); _ = _player.PlayLickAsync(); SayFrom(LickTexts); };
        menu.Items.Add(lick);

        var yawn = new MenuItem { Header = "打个哈欠" };
        yawn.Click += async (_, _) => { _lastInteraction = DateTime.Now; await ReturnSittingAsync(); _ = _player.PlayYawnAsync(); SayFrom(YawnTexts); };
        menu.Items.Add(yawn);


        var shakeMi = new MenuItem { Header = "抖抖毛" };
        shakeMi.Click += async (_, _) => { _lastInteraction = DateTime.Now; await ReturnSittingAsync(); _ = _player.PlayShakeAsync(); SayFrom(ShakeTexts); };
        menu.Items.Add(shakeMi);

        var look = new MenuItem { Header = "东张西望" };
        look.Click += async (_, _) => { _lastInteraction = DateTime.Now; await ReturnSittingAsync(); _ = _player.PlayLookAroundAsync(); SayFrom(LookAroundTexts); };
        menu.Items.Add(look);

        var idle = new MenuItem { Header = "重新待机" };
        idle.Click += async (_, _) => { _lastInteraction = DateTime.Now; await ReturnSittingAsync(); _ = _player.PlayIdleAsync(); };
        menu.Items.Add(idle);

        // 站立姿态动作（方案 A 重拓扑模型）：待机 / 左右走 / 摇尾巴
        // 走路会带动窗口位移，动作结束或撞到屏幕边界就停
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "站立动作", IsEnabled = false, FontWeight = FontWeights.Bold });
        foreach (var (label, name, loops, dir, text) in new (string, string, int, int, string)[]
                 {
                     ("站立待机", "sidle", 2, 0, "（站起来看看）"),
                     ("向左走", "swalk_l", 5, -1, "运动一下喵～"),
                     ("向左跑", "srun_l", 3, -1, "跑起来喵！"),
                     ("向左飞奔", "sgal_l", 3, -1, "冲啊喵！！"),
                     ("向右走", "swalk_r", 5, 1, "运动一下喵～"),
                     ("向右跑", "srun_r", 3, 1, "跑起来喵！"),
                     ("向右飞奔", "sgal_r", 3, 1, "冲啊喵！！"),
                     ("摇尾巴", "swag", 2, 0, "喵～ 主人好"),
                     ("观察四周", "slook", 1, 0, "（东张西望）"),
                 })
        {
            var mi = new MenuItem { Header = label };
            mi.Click += async (_, _) => await PlayStandAsync(
                // 摇尾巴有左右两套：按当前朝向挑，避免"停下来突然换边"
                name switch
                {
                    "swag" => SideClip("swag", "swag_r"),
                    "slook" => SideClip("slook", "slook_r"),
                    "sidle" => SideClip("sidle", "sidle_r"),
                    "sclose" => SideClip("sclose", "sclose_r"),
                    _ => name,
                },
                loops, dir, text);
            menu.Items.Add(mi);
        }
        // ---------- 小游戏 ----------
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "小游戏", IsEnabled = false, FontWeight = FontWeights.Bold });

        var chase = new MenuItem
        {
            Header = ChaseActive ? "停止追鼠标" : "追鼠标（跟着鼠标跑）",
            ToolTip = "开启后小猫会朝鼠标方向走/跑/飞奔，速度随距离变化",
        };
        chase.Click += (_, _) => ToggleChase();
        menu.Items.Add(chase);

        var invite = new MenuItem { Header = "邀请一起玩", ToolTip = "让猫咪问你要不要玩（接受后 60 秒自动结束）" };
        invite.Click += (_, _) => ShowInvite();
        menu.Items.Add(invite);

        // ---------- 设置 ----------
        menu.Items.Add(new Separator());
        menu.Items.Add(new MenuItem { Header = "设置", IsEnabled = false, FontWeight = FontWeights.Bold });

        var openSettings = new MenuItem { Header = "设置…" };
        openSettings.Click += (_, _) => OpenSettings();
        menu.Items.Add(openSettings);

        var followToggle = new MenuItem
        {
            Header = "头部跟随鼠标",
            IsCheckable = true,
            IsChecked = _settings.HeadFollow,
            ToolTip = "勾上 = 一直跟着鼠标看；不勾 = 偶尔看一阵（动作区间）",
        };
        followToggle.Click += (_, _) =>
        {
            _settings.HeadFollow = followToggle.IsChecked;
            _settings.Save();
            if (_settings.HeadFollow)
            {
                Say("好，我一直盯着你的鼠标喵", 2);
            }
            else
            {
                _followUntil = DateTime.MinValue;
                EndHeadFollow();
                Say("还是偶尔看看就好", 2);
            }
            RebuildTrayMenu();
        };
        menu.Items.Add(followToggle);

        var resize = new MenuItem { Header = "调整大小", IsCheckable = true, IsChecked = _resizeMode };
        resize.Click += (_, _) =>
        {
            // 从菜单取消勾选等同于「取消」，恢复原大小
            if (!resize.IsChecked && _resizeMode) OnCancelResize(this, new RoutedEventArgs());
            else SetResizeMode(resize.IsChecked);
        };
        menu.Items.Add(resize);

        // ---------- 关于 / 退出 ----------
        menu.Items.Add(new Separator());
        var about = new MenuItem { Header = "关于" };
        about.Click += (_, _) => ShowAbout();
        menu.Items.Add(about);

        menu.Items.Add(new Separator());
        var exit = new MenuItem { Header = "退出" };
        exit.Click += (_, _) => Close();
        menu.Items.Add(exit);

        menu.IsOpen = true;
        e.Handled = true;
    }



    // ------------------------------------------------------------ 计时逻辑

    private void OnTick()
    {
        if (_pomoState != PomoState.None && !_pomoPaused && _pomoRemainSec > 0)
        {
            _pomoRemainSec--;
            if (_pomoRemainSec == 0) OnPomodoroFinished();
        }

        if (_settings.WaterRemindEnabled)
        {
            if (_waterRemainSec > 0) _waterRemainSec--;
            if (_waterRemainSec == 0) OnWaterRemind();
        }

        UpdateTimerBar();
    }

    private void UpdateTimerBar()
    {
        var parts = new List<string>();
        if (_pomoState != PomoState.None)
        {
            var tag = _pomoState == PomoState.Work ? "专注" : "休息";
            if (_pomoPaused) tag = "已暂停";
            parts.Add($"{tag} {Fmt(_pomoRemainSec)}");
        }
        if (_settings.WaterRemindEnabled)
            parts.Add($"喝水 {Fmt(_waterRemainSec)}");

        if (parts.Count == 0)
        {
            TimerBar.Visibility = Visibility.Collapsed;
            UpdateTrayText();    // 托盘提示跟着刷新（之前这个方法是死代码）
            return;
        }
        TimerText.Text = string.Join("   ", parts);
        TimerBar.Visibility = Visibility.Visible;
    }

    private static string Fmt(int seconds)
    {
        if (seconds < 0) seconds = 0;
        return $"{seconds / 60:00}:{seconds % 60:00}";
    }

    /// <summary>提示一次「托盘里有番茄钟/喝水提醒」；用过计时功能后不再提示。</summary>
    private void ShowFeatureHint()
    {
        if (_settings.HintRemaining <= 0)
        {
            _hintTimer.Stop();
            return;
        }
        if (_dragging || _resizeMode || _standing || Visibility != Visibility.Visible)
            return;

        // 90 秒没交互时猫已经在打盹了，这里把它叫醒再说话
        if (_asleep) Wake();

        SayFrom(HintTexts);
        _ = _player.PlayTiltAsync();     // 歪个头，把注意力引过来

        _settings.HintRemaining--;
        _settings.Save();
        _hintTimer.Interval = HintRepeatDelay;
    }
    /// <summary>开始一段番茄钟（minutes 分钟）。</summary>
    private void StartPomodoro(PomoState state, int minutes)
    {
        _settings.HintRemaining = 0;   // 已经找到这个功能了，别再提示
        _hintTimer?.Stop();
        _settings.Save();
        _pomoState = state;
        _pomoTotalSec = Math.Max(1, minutes) * 60;
        _pomoRemainSec = _pomoTotalSec;
        _pomoPaused = false;
        _lastInteraction = DateTime.Now;

        // 进入专注模式：关掉可能正开着的"一起玩"邀约（专注期间也不会再发起）
        CloseInvite();
        if (ChaseActive) StopChase(say: true);

        Say(state == PomoState.Work ? $"开始专注 {minutes} 分钟" : $"休息 {minutes} 分钟");
        UpdateTimerBar();
    }

    private void StopPomodoro()
    {
        _pomoState = PomoState.None;
        _pomoPaused = false;
        _pomoRemainSec = 0;
        Say("已停止计时");
        UpdateTimerBar();
    }

    private void OnPomodoroFinished()
    {
        if (_pomoState == PomoState.Work)
        {
            Say("专注结束，休息一下～");
            _ = _player.PlaySayAsync();
            SayFrom(Meows);
            if (_settings.PomodoroAutoBreak)
                StartPomodoro(PomoState.Break, _settings.PomodoroBreakMinutes);
            else
            {
                _pomoState = PomoState.None;
                UpdateTimerBar();
            }
            return;
        }

        // 休息结束
        Say("休息结束，继续加油！");
        _ = _player.PlayYawnAsync();
        _pomoState = PomoState.None;
        _pomoRemainSec = 0;
        UpdateTimerBar();
    }

    private void OnWaterRemind()
    {
        _waterTickCount++;
        var texts = new[] { "（该喝水啦）", "（起来动一动）" };
        Say(texts[(_waterTickCount - 1) % texts.Length]);
        _ = _player.PlaySayAsync();
        _waterRemainSec = Math.Max(1, _settings.WaterRemindMinutes) * 60;
    }

    /// <summary>弹输入框问分钟数；取消返回 -1。</summary>
    private int AskMinutes(string title, int initial)
    {
        var dlg = new MinutesDialog(title, initial) { Owner = this };
        return dlg.ShowDialog() == true ? dlg.Value : -1;
    }

    // ------------------------------------------------------------ 调整大小

    /// <summary>进入/退出调整大小模式：显示四角手柄和边框。</summary>
    private void SetResizeMode(bool on)
    {
        _resizeMode = on;
        var v = on ? Visibility.Visible : Visibility.Collapsed;
        HandleTL.Visibility = v;
        HandleTR.Visibility = v;
        HandleBL.Visibility = v;
        HandleBR.Visibility = v;
        ResizeFrame.Visibility = v;
        ResizeBar.Visibility = v;

        if (on)
        {
            _zoomOnEnter = Width / 500.0;
            _leftOnEnter = Left;
            _topOnEnter = Top;
            Say("拖四角调整，完成后点「确认」");
        }
    }

    private void OnCornerDown(object sender, MouseButtonEventArgs e)
    {
        if (!_resizeMode) return;
        _dragCorner = (FrameworkElement)sender;
        // 记住对角（拖动时不动的那个角）
        switch (_dragCorner.Name)
        {
            case "HandleTL": _anchorX = Left + Width; _anchorY = Top + Height; break;
            case "HandleTR": _anchorX = Left; _anchorY = Top + Height; break;
            case "HandleBL": _anchorX = Left + Width; _anchorY = Top; break;
            default: _anchorX = Left; _anchorY = Top; break;   // BR
        }
        _dragCorner.CaptureMouse();
        e.Handled = true;
    }

    private void OnCornerMove(object sender, MouseEventArgs e)
    {
        if (!_resizeMode || _dragCorner is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        // PointToScreen 给的是物理像素，而 Left/Top/Width 是 DIP：高 DPI 下必须换算，
        // 否则拖动 100 DIP 会被算成 125/150 DIP，缩放速度和鼠标不一致
        var dpi = VisualTreeHelper.GetDpi(this);
        var raw = _dragCorner.PointToScreen(e.GetPosition(_dragCorner));
        var p = new Point(raw.X / dpi.DpiScaleX, raw.Y / dpi.DpiScaleY);

        // 垂直方向要扣掉顶部气泡带：宠物区高度才是 size
        var band = BubbleBandHeight;
        var size = _dragCorner.Name switch
        {
            "HandleTL" => Math.Max(_anchorX - p.X, _anchorY - p.Y),
            "HandleTR" => Math.Max(p.X - _anchorX, _anchorY - p.Y),
            "HandleBL" => Math.Max(_anchorX - p.X, p.Y - _anchorY - band),
            _ => Math.Max(p.X - _anchorX, p.Y - _anchorY - band),
        };

        var min = 500 * Settings.MinZoom;
        var max = 500 * Settings.MaxZoom;
        size = Math.Clamp(size, min, max);

        Width = size;
        Height = size + band;   // 高度多出气泡带
        switch (_dragCorner.Name)
        {
            case "HandleTL": Left = _anchorX - size; Top = _anchorY - size - band; break;
            case "HandleTR": Left = _anchorX; Top = _anchorY - size - band; break;
            case "HandleBL": Left = _anchorX - size; Top = _anchorY; break;
            default: Left = _anchorX; Top = _anchorY; break;
        }
        ApplyWindowLayout();
        e.Handled = true;
    }

    private void OnCornerUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragCorner is null) return;
        _dragCorner.ReleaseMouseCapture();
        _dragCorner = null;

        // 拖动期间只改窗口大小，点「确认」时才写入配置
        e.Handled = true;
    }

    /// <summary>确认：保存当前大小并退出调整模式。</summary>
    private void OnConfirmResize(object sender, RoutedEventArgs e)
    {
        _settings.Zoom = Math.Round(Width / 500.0, 3);
        _scaleBaseSize = Width;      // 站/坐切换以新尺寸为基准，否则点动作会弹回旧大小
        _settings.Save();
        SetResizeMode(false);
        Say($"大小已保存（{Math.Round(Width)}×{Math.Round(Height)}）");
        if (e.RoutedEvent is not null) e.Handled = true;
    }

    /// <summary>取消：恢复到进入调整模式之前的大小和位置。</summary>
    private void OnCancelResize(object sender, RoutedEventArgs e)
    {
        var size = 500 * _zoomOnEnter;
        Width = size;
        Height = size + BubbleBandHeight;   // 高度多出气泡带
        _scaleBaseSize = size;
        Left = _leftOnEnter;
        Top = _topOnEnter;
        SetResizeMode(false);
        Say("已取消");
        if (e.RoutedEvent is not null) e.Handled = true;
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _resizeMode)
        {
            // Esc 等同于「取消」：恢复原大小
            OnCancelResize(this, new RoutedEventArgs());
        if (e.RoutedEvent is not null) e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }

    private void Say(string text) => Say(text, 2.5);

    private void Say(string text, double seconds)
    {
        ApplyTextSize();
        BubbleText.Text = text;
        Bubble.Visibility = Visibility.Visible;
        _bubbleTimer.Stop();
        _bubbleTimer.Interval = TimeSpan.FromSeconds(seconds);
        _bubbleTimer.Start();
    }

    // ---------------------------------------------------------------- 窗口移动

    private void MoveByCursor()
    {
        if (!GetCursorPos(out var cursor))
            return;

        var dpi = VisualTreeHelper.GetDpi(this);
        var dx = (cursor.X - _lastCursor.X) / dpi.DpiScaleX;
        var dy = (cursor.Y - _lastCursor.Y) / dpi.DpiScaleY;

        Left += dx;
        Top += dy;
        _lastCursor = cursor;

        // 别让它跑出屏幕
        var left = SystemParameters.VirtualScreenLeft;
        var top = SystemParameters.VirtualScreenTop;
        var right = left + SystemParameters.VirtualScreenWidth;
        var bottom = top + SystemParameters.VirtualScreenHeight;

        Left = Math.Clamp(Left, left - Width * 0.5, right - Width * 0.5);
        Top = Math.Clamp(Top, top - Height * 0.5, bottom - Height * 0.5);
    }

    protected override void OnClosed(EventArgs e)
    {
        SavePosition();
        _player.Stop();
        _bubbleTimer.Stop();
        _idleTimer.Stop();
        _sleepTimer.Stop();
        _postureTimer?.Stop();
        _careTimer?.Stop();
        _followTimer?.Stop();
        _fullscreenTimer?.Stop();
        _faceTimer?.Stop();
        _blinkTimer?.Stop();
        _tickTimer?.Stop();
        _hintTimer?.Stop();
        _scaleTimer?.Stop();
        _walkTimer?.Stop();
        _chaseTimer?.Stop();
        _chaseJumpTimer?.Stop();
        CloseInvite();
        DisposeTray();   // 不释放 NotifyIcon 会留下"幽灵托盘图标"
        base.OnClosed(e);
        Application.Current.Shutdown();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);
}
