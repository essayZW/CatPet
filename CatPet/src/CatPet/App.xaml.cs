using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace CatPet;

public partial class App : Application
{
    private static Mutex? _singleInstance;
    private static EventWaitHandle? _showRequest;
    private const string ShowEventName = @"Local\CatPet.ShowRequest";

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr p);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);
    private delegate bool EnumWindowsProc(IntPtr h, IntPtr p);

    protected override void OnStartup(StartupEventArgs e)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Dump("UnhandledException", args.ExceptionObject as Exception);
        DispatcherUnhandledException += (_, args) =>
        {
            Dump("DispatcherUnhandledException", args.Exception);
            args.Handled = true;
        };

        RotateLogIfNeeded();

        // 单实例：已经有猫在跑就把那只叫出来，自己退出
        _singleInstance = new Mutex(true, @"Local\CatPet.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            // 猫被"隐藏"时窗口不可见，光 ShowWindow 没用（WPF 还认为自己是隐藏的），
            // 所以给正在运行的那只发个"出来露面"的信号
            try
            {
                using var ev = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
                ev.Set();
            }
            catch
            {
                // 事件还没建好（对方刚启动）：退回到直接找窗口
            }

            ActivateExistingPet();
            Shutdown();
            return;
        }

        // 第一个实例：建好信号并常驻监听
        try
        {
            _showRequest = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            StartShowListener();
        }
        catch
        {
            // 监听失败不影响主流程（还有 ActivateExistingPet 兜底）
        }

        // 安装器的自启标记 / 旧路径自愈
        AutoStart.SyncOnStartup();

        base.OnStartup(e);

        // WinForms 的托盘菜单在 PerMonitorV2 下会自己算错坐标（菜单跑到屏幕左上角），
        // 显式声明一次高 DPI 模式，让它和 WPF 用同一套缩放
        try
        {
            System.Windows.Forms.Application.SetHighDpiMode(System.Windows.Forms.HighDpiMode.PerMonitorV2);
        }
        catch
        {
            // 已经被初始化过就忽略
        }
    }

    /// <summary>监听"把猫叫出来"的请求（第二个实例启动时发出）。</summary>
    private void StartShowListener()
    {
        var th = new Thread(() =>
        {
            try
            {
                while (true)
                {
                    _showRequest!.WaitOne();
                    Current?.Dispatcher.BeginInvoke(() =>
                    {
                        if (Current?.MainWindow is PetWindow pet)
                            pet.ShowFromTray();
                    });
                }
            }
            catch
            {
                // 退出时忽略
            }
        })
        { IsBackground = true, Name = "CatPet.ShowListener" };
        th.Start();
    }

    /// <summary>把已经在运行的那只猫显示出来并切到前台。</summary>
    private static void ActivateExistingPet()
    {
        try
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((h, _) =>
            {
                var sb = new StringBuilder(256);
                GetWindowTextW(h, sb, 256);
                var title = sb.ToString();
                // 不要求可见：猫被藏起来时窗口是 hidden，但句柄还在
                if (title.StartsWith("CatPet - [", StringComparison.Ordinal))
                {
                    found = h;
                    return false;
                }
                return true;
            }, IntPtr.Zero);

            if (found != IntPtr.Zero)
            {
                ShowWindow(found, 9);      // SW_RESTORE
                SetForegroundWindow(found);
            }
        }
        catch
        {
            // ignore
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private const string LogFile = "catpet-error.log";
    private const int KeepLogs = 5;
    private const long MaxLogBytes = 1024 * 1024;   // 单文件超过 1 MB 就轮转

    /// <summary>日志超过上限就轮转：catpet-error.log → .1.log … 只留最近 5 份。</summary>
    private static void RotateLogIfNeeded()
    {
        try
        {
            var dir = AppContext.BaseDirectory;
            var path = Path.Combine(dir, LogFile);
            if (!File.Exists(path) || new FileInfo(path).Length <= MaxLogBytes)
                return;

            for (var i = KeepLogs - 1; i >= 1; i--)
            {
                var older = Path.Combine(dir, $"catpet-error.{i}.log");
                var newer = Path.Combine(dir, i == 1 ? LogFile : $"catpet-error.{i - 1}.log");
                if (!File.Exists(newer)) continue;
                if (File.Exists(older)) File.Delete(older);
                File.Move(newer, older);
            }
        }
        catch
        {
            // ignore
        }
    }

    private static void Dump(string tag, Exception? ex)
    {
        try
        {
            RotateLogIfNeeded();
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {tag}: {ex}\r\n";
            File.AppendAllText(Path.Combine(AppContext.BaseDirectory, LogFile), line);
        }
        catch
        {
            // ignore
        }

        MessageBox.Show(ex?.Message ?? tag, "CatPet 出错了", MessageBoxButton.OK, MessageBoxImage.Error);
    }
}