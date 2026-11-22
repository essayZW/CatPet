using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using CatPet.Core;

namespace CatPet;

/// <summary>设置子窗口：数值都可直接输入，保存后交给 PetWindow 应用。</summary>
public partial class SettingsWindow : Window
{
    private readonly Settings _settings;
    private bool _resetDefaults;

    public SettingsWindow(Settings settings)
    {
        InitializeComponent();
        _settings = settings;

        TextSizeBox.Text = settings.TextSize.ToString("0.#", CultureInfo.InvariantCulture);
        PomoWorkBox.Text = settings.PomodoroWorkMinutes.ToString();
        PomoBreakBox.Text = settings.PomodoroBreakMinutes.ToString();
        WaterBox.Text = settings.WaterRemindMinutes.ToString();
        ActionBox.SelectedIndex = Math.Clamp(settings.ActionFrequency, 0, 2);
        TopmostBox.IsChecked = settings.Topmost;
        HeadFollowBox.IsChecked = settings.HeadFollow;
        AutoBreakBox.IsChecked = settings.PomodoroAutoBreak;
        WaterEnabledBox.IsChecked = settings.WaterRemindEnabled;
        AutoStartBox.IsChecked = AutoStart.IsEnabled;

        Loaded += (_, _) => TextSizeBox.Focus();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!TryRead(TextSizeBox, 8, 72, "气泡字号", out var textSize)) return;
        if (!TryRead(PomoWorkBox, 1, 180, "专注时长", out var work)) return;
        if (!TryRead(PomoBreakBox, 1, 120, "休息时长", out var brk)) return;
        if (!TryRead(WaterBox, 1, 480, "提醒间隔", out var water)) return;

        _settings.TextSize = textSize;
        _settings.PomodoroWorkMinutes = (int)work;
        _settings.PomodoroBreakMinutes = (int)brk;
        _settings.WaterRemindMinutes = (int)water;
        _settings.ActionFrequency = Math.Clamp(ActionBox.SelectedIndex, 0, 2);
        _settings.Topmost = TopmostBox.IsChecked == true;
        _settings.HeadFollow = HeadFollowBox.IsChecked == true;
        _settings.PomodoroAutoBreak = AutoBreakBox.IsChecked == true;
        _settings.WaterRemindEnabled = WaterEnabledBox.IsChecked == true;
        if (_resetDefaults)
            _settings.Zoom = Settings.DefaultZoom;   // 窗口大小也回默认

        _settings.Save();
        AutoStart.Set(AutoStartBox.IsChecked == true);

        DialogResult = true;
    }

    /// <summary>把界面上所有项恢复成出厂默认（窗口位置/大小不在这个窗口里，不受影响）。</summary>
    private void OnResetDefaults(object sender, RoutedEventArgs e)
    {
        var d = new Settings();
        TextSizeBox.Text = d.TextSize.ToString("0.#", CultureInfo.InvariantCulture);
        PomoWorkBox.Text = d.PomodoroWorkMinutes.ToString();
        PomoBreakBox.Text = d.PomodoroBreakMinutes.ToString();
        WaterBox.Text = d.WaterRemindMinutes.ToString();
        ActionBox.SelectedIndex = Math.Clamp(d.ActionFrequency, 0, 2);
        TopmostBox.IsChecked = d.Topmost;
        HeadFollowBox.IsChecked = d.HeadFollow;
        AutoBreakBox.IsChecked = d.PomodoroAutoBreak;
        WaterEnabledBox.IsChecked = d.WaterRemindEnabled;

        _resetDefaults = true;
        ShowHint("已填入默认值（含窗口大小），点「保存」生效；开机自启不在此列");
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    /// <summary>读取输入框里的数字，并做范围校验；不合法时在窗口里提示。</summary>
    private bool TryRead(TextBox box, double min, double max, string name, out double value)
    {
        var raw = box.Text.Trim();
        if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && !double.TryParse(raw, out value))
        {
            ShowHint($"{name}只能填数字");
            return false;
        }

        if (value < min || value > max)
        {
            ShowHint($"{name}要在 {min:0.#} ~ {max:0.#} 之间");
            return false;
        }

        HintText.Visibility = Visibility.Collapsed;
        return true;
    }

    private void ShowHint(string message)
    {
        HintText.Text = message;
        HintText.Visibility = Visibility.Visible;
    }
}
