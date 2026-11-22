using System.IO;
using Microsoft.Win32;

namespace CatPet;

/// <summary>开机自启动：写 HKCU\...\Run，不需要管理员权限。</summary>
internal static class AutoStart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CatPet";

    /// <summary>当前是否已经设置开机自启。</summary>
    public static bool IsEnabled
    {
        get
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(RunKey);
                return key?.GetValue(ValueName) is string s && s.Length > 0;
            }
            catch
            {
                return false;
            }
        }
    }

    /// <summary>打开/关闭开机自启。</summary>
    public static bool Set(bool on)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key is null) return false;

            if (on)
                key.SetValue(ValueName, $"\"{ExePath}\"");
            else
                key.DeleteValue(ValueName, throwOnMissingValue: false);

            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 启动时对齐一次：
    /// 1) 安装器留的 autostart.on 标记 → 用「当前用户」写入自启（避免安装时用管理员账户写错 hive）
    /// 2) 自启项指向旧路径（换过安装目录/绿色版挪位置）→ 改成当前 exe
    /// </summary>
    public static void SyncOnStartup()
    {
        try
        {
            var flag = Path.Combine(AppContext.BaseDirectory, "autostart.on");
            if (File.Exists(flag))
            {
                Set(true);
                File.Delete(flag);
                return;
            }

            if (!IsEnabled) return;

            var current = $"\"{ExePath}\"";
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key?.GetValue(ValueName) is string existing && existing != current)
                key.SetValue(ValueName, current);
        }
        catch
        {
            // ignore
        }
    }

    /// <summary>可执行文件路径（开发时 dotnet CatPet.dll 也能拿到正确的 exe）。</summary>
    private static string ExePath
    {
        get
        {
            var local = Path.Combine(AppContext.BaseDirectory, "CatPet.exe");
            return File.Exists(local) ? local : Environment.ProcessPath ?? local;
        }
    }
}
