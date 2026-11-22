using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace CatPet;

/// <summary>
/// 托盘图标：提醒相关的入口都放这里（宠物窗口本身的右键菜单只保留动作类）。
/// 图标在运行时从当前形象的待机帧裁出，换素材后自动跟着变。
/// </summary>
public partial class PetWindow
{
    private WinForms.NotifyIcon? _tray;
    private WinForms.ContextMenuStrip? _trayMenu;

    private void InitTray()
    {
        try
        {
            InitTrayCore();
            Console.WriteLine("[CatPet] 托盘图标已创建");
        }
        catch (Exception ex)
        {
            Console.WriteLine("[CatPet] 托盘图标创建失败：" + ex.Message);
        }
    }

    private void InitTrayCore()
    {
        _trayMenu = new WinForms.ContextMenuStrip();
        _trayMenu.Opening += (_, _) => RebuildTrayMenu();

        _tray = new WinForms.NotifyIcon
        {
            Icon = BuildTrayIcon(),
            Text = "猫咪桌宠",
            Visible = true,
            ContextMenuStrip = _trayMenu,
        };
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(TogglePetVisible);
        RebuildTrayMenu();
    }

    private void DisposeTray()
    {
        if (_tray is null)
            return;
        _tray.Visible = false;
        _tray.Dispose();
        _tray = null;
    }

    /// <summary>把猫叫出来（第二个实例启动、或托盘里点"显示猫"时用）。</summary>
    internal void ShowFromTray()
    {
        _userHidden = false;
        if (!IsVisible)
            Show();
        Topmost = true;
        Activate();
        _lastInteraction = DateTime.Now;
        RebuildTrayMenu();
    }

    /// <summary>显示/隐藏猫（只隐藏窗口，托盘还在）。</summary>
    private void TogglePetVisible()
    {
        if (Visibility == Visibility.Visible)
        {
            CloseInvite();
            if (ChaseActive) StopChase();   // 隐藏时不能把游戏挂起（会卡住姿态轮换和自动结束）
            _userHidden = true;
            _autoHidden = false;
            Hide();
            Say("（躲起来了）");
        }
        else
        {
            _userHidden = false;
            Show();
            Activate();
        }
        RebuildTrayMenu();
    }

    /// <summary>托盘菜单每次打开时按当前状态重建（二级菜单：番茄钟 / 喝水·久坐）。</summary>
    private void RebuildTrayMenu()
    {
        if (_trayMenu is null)
            return;
        _trayMenu.Items.Clear();

        var show = new WinForms.ToolStripMenuItem(Visibility == Visibility.Visible ? "隐藏猫咪" : "显示猫咪");
        show.Click += (_, _) => TogglePetVisible();
        _trayMenu.Items.Add(show);

        _trayMenu.Items.Add(new WinForms.ToolStripSeparator());

        // ---------------- 番茄钟（二级） ----------------
        var pomo = new WinForms.ToolStripMenuItem(
            _pomoState == PomoState.None ? "番茄钟" : $"番茄钟（{Fmt(_pomoRemainSec)}）");

        foreach (var m in new[] { 15, 25, 45, 60 })
        {
            var mi = new WinForms.ToolStripMenuItem($"专注 {m} 分钟");
            mi.Click += (_, _) => Dispatcher.Invoke(() => StartPomodoro(PomoState.Work, m));
            pomo.DropDownItems.Add(mi);
        }

        var workCustom = new WinForms.ToolStripMenuItem("专注 自定义…");
        workCustom.Click += (_, _) => Dispatcher.Invoke(() =>
        {
            var v = AskMinutes("专注时长", _settings.PomodoroWorkMinutes);
            if (v < 0) return;
            _settings.PomodoroWorkMinutes = v;
            _settings.Save();
            StartPomodoro(PomoState.Work, v);
        });
        pomo.DropDownItems.Add(workCustom);

        pomo.DropDownItems.Add(new WinForms.ToolStripSeparator());

        foreach (var m in new[] { 5, 10 })
        {
            var mi = new WinForms.ToolStripMenuItem($"休息 {m} 分钟");
            mi.Click += (_, _) => Dispatcher.Invoke(() => StartPomodoro(PomoState.Break, m));
            pomo.DropDownItems.Add(mi);
        }

        var brkCustom = new WinForms.ToolStripMenuItem("休息 自定义…");
        brkCustom.Click += (_, _) => Dispatcher.Invoke(() =>
        {
            var v = AskMinutes("休息时长", _settings.PomodoroBreakMinutes);
            if (v < 0) return;
            _settings.PomodoroBreakMinutes = v;
            _settings.Save();
            StartPomodoro(PomoState.Break, v);
        });
        pomo.DropDownItems.Add(brkCustom);

        if (_pomoState != PomoState.None)
        {
            pomo.DropDownItems.Add(new WinForms.ToolStripSeparator());

            var pause = new WinForms.ToolStripMenuItem(_pomoPaused ? "继续计时" : "暂停计时");
            pause.Click += (_, _) => Dispatcher.Invoke(() =>
            {
                _pomoPaused = !_pomoPaused;
                UpdateTimerBar();
            });
            pomo.DropDownItems.Add(pause);

            var stop = new WinForms.ToolStripMenuItem("停止计时");
            stop.Click += (_, _) => Dispatcher.Invoke(StopPomodoro);
            pomo.DropDownItems.Add(stop);
        }

        pomo.DropDownItems.Add(new WinForms.ToolStripSeparator());

        var autoBreak = new WinForms.ToolStripMenuItem("专注结束自动休息")
        {
            Checked = _settings.PomodoroAutoBreak,
            CheckOnClick = true,
        };
        autoBreak.Click += (_, _) => Dispatcher.Invoke(() =>
        {
            _settings.PomodoroAutoBreak = autoBreak.Checked;
            _settings.Save();
        });
        pomo.DropDownItems.Add(autoBreak);

        _trayMenu.Items.Add(pomo);

        // ---------------- 喝水 / 久坐（二级） ----------------
        var water = new WinForms.ToolStripMenuItem(
            _settings.WaterRemindEnabled ? $"喝水/久坐（{Fmt(_waterRemainSec)}）" : "喝水/久坐（已关）");

        foreach (var m in new[] { 30, 45, 60, 90 })
        {
            var mi = new WinForms.ToolStripMenuItem($"每 {m} 分钟");
            mi.Click += (_, _) => Dispatcher.Invoke(() => SetWaterInterval(m));
            water.DropDownItems.Add(mi);
        }

        var waterCustom = new WinForms.ToolStripMenuItem("自定义间隔…");
        waterCustom.Click += (_, _) => Dispatcher.Invoke(() =>
        {
            var v = AskMinutes("提醒间隔", _settings.WaterRemindMinutes);
            if (v >= 0) SetWaterInterval(v);
        });
        water.DropDownItems.Add(waterCustom);

        water.DropDownItems.Add(new WinForms.ToolStripSeparator());

        var waterOff = new WinForms.ToolStripMenuItem("关闭提醒");
        waterOff.Enabled = _settings.WaterRemindEnabled;
        waterOff.Click += (_, _) => Dispatcher.Invoke(() =>
        {
            _settings.WaterRemindEnabled = false;
            _settings.Save();
            UpdateTimerBar();
            Say("已关闭喝水提醒");
        });
        water.DropDownItems.Add(waterOff);

        _trayMenu.Items.Add(water);

        _trayMenu.Items.Add(new WinForms.ToolStripSeparator());

        var settingsItem = new WinForms.ToolStripMenuItem("设置…");
        settingsItem.Click += (_, _) => Dispatcher.Invoke(OpenSettings);
        _trayMenu.Items.Add(settingsItem);
        var openLog = new WinForms.ToolStripMenuItem("打开日志文件夹");
        openLog.Click += (_, _) =>
        {
            try { System.Diagnostics.Process.Start("explorer.exe", AppContext.BaseDirectory); }
            catch { /* ignore */ }
        };
        _trayMenu.Items.Add(openLog);

        var chaseItem = new WinForms.ToolStripMenuItem(ChaseActive ? "停止追鼠标" : "追鼠标（小游戏）");
        chaseItem.Click += (_, _) => Dispatcher.Invoke(ToggleChase);
        _trayMenu.Items.Add(chaseItem);
        _trayMenu.Items.Add(new WinForms.ToolStripSeparator());

        var aboutItem = new WinForms.ToolStripMenuItem($"关于（v{AboutWindow.AppVersion}）");
        aboutItem.Click += (_, _) => Dispatcher.Invoke(ShowAbout);
        _trayMenu.Items.Add(aboutItem);
        _trayMenu.Items.Add(new WinForms.ToolStripSeparator());

        var exit = new WinForms.ToolStripMenuItem("退出");
        exit.Click += (_, _) => Dispatcher.Invoke(Close);
        _trayMenu.Items.Add(exit);
    }
    private void SetWaterInterval(int minutes)
    {
        _settings.HintRemaining = 0;   // 已经找到这个功能了，别再提示
        _hintTimer?.Stop();
        _settings.WaterRemindMinutes = minutes;
        _settings.WaterRemindEnabled = true;
        _settings.Save();
        _waterRemainSec = minutes * 60;
        UpdateTimerBar();
        Say($"每 {minutes} 分钟提醒喝水/起身");
    }

    /// <summary>更新托盘提示：显示剩余时间（NotifyIcon.Text 上限 63 字符）。</summary>
    private void UpdateTrayText()
    {
        if (_tray is null)
            return;

        var parts = new List<string>();
        if (_pomoState != PomoState.None)
            parts.Add($"{(_pomoState == PomoState.Work ? "专注" : "休息")} {Fmt(_pomoRemainSec)}");
        if (_settings.WaterRemindEnabled)
            parts.Add($"喝水 {Fmt(_waterRemainSec)}");

        _tray.Text = parts.Count > 0 ? "猫咪桌宠 · " + string.Join(" · ", parts) : "猫咪桌宠";
    }

    /// <summary>
    /// 从当前形象的待机帧裁出头部，生成 32×32 图标（PNG 内嵌 ICO）。
    /// 找不到素材就退回系统默认图标。
    /// </summary>
    private static Icon BuildTrayIcon()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "mod", "MyCat", "pet", "cat", "idle");
            var file = Directory.GetFiles(path, "*.png").OrderBy(f => f).FirstOrDefault();
            if (file is not null)
            {
                using var src = new Bitmap(file);
                // 头部大致位置（1000×1000 画布）：x 340~700，y 130~490
                var size = Math.Min(src.Width, src.Height);
                var crop = new Rectangle(
                    (int)(size * 0.33), (int)(size * 0.12),
                    (int)(size * 0.36), (int)(size * 0.36));

                using var bmp = new Bitmap(32, 32);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.Clear(Color.Transparent);
                    g.DrawImage(src, new Rectangle(0, 0, 32, 32), crop, GraphicsUnit.Pixel);
                }

                using var ms = new MemoryStream();
                bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                var png = ms.ToArray();

                // 手写 ICO 头 + PNG 负载（Vista 以后支持）
                var ico = new MemoryStream();
                var bw = new BinaryWriter(ico);
                bw.Write((short)0);      // reserved
                bw.Write((short)1);      // type = icon
                bw.Write((short)1);      // count
                bw.Write((byte)32);      // width
                bw.Write((byte)32);      // height
                bw.Write((byte)0);       // colors
                bw.Write((byte)0);       // reserved
                bw.Write((short)1);      // planes
                bw.Write((short)32);     // bpp
                bw.Write(png.Length);    // bytes
                bw.Write(22);            // offset
                bw.Write(png);
                bw.Flush();
                ico.Position = 0;
                return new Icon(ico);
            }
        }
        catch
        {
            // 生成失败就用系统图标，不影响启动
        }
        return SystemIcons.Application;
    }
}
