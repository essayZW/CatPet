using System;
using System.Windows;
using System.Windows.Threading;

namespace CatPet;

/// <summary>
/// 「一起玩？」邀约弹窗：两个选项（一起玩 / 不），"不" 上有 15 秒倒计时，
/// 时间到了视为拒绝。窗口自己不做决定后的动作，交给 PetWindow 处理。
/// </summary>
public partial class ChaseInviteWindow : Window
{
    private readonly DispatcherTimer _timer;
    private int _remain = 15;

    /// <summary>用户点了「一起玩」。</summary>
    public bool Accepted { get; private set; }

    /// <summary>倒计时结束或点了「不」。</summary>
    public bool Declined => !Accepted;

    public ChaseInviteWindow(string message)
    {
        InitializeComponent();
        if (!string.IsNullOrWhiteSpace(message)) MessageText.Text = message;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) =>
        {
            _remain--;
            if (_remain <= 0)
            {
                Close();
                return;
            }
            NoButton.Content = $"不（{_remain}s）";
        };
        _timer.Start();
        UpdateNoButton();
    }

    private void UpdateNoButton() => NoButton.Content = $"不（{_remain}s）";

    private void OnYes(object sender, RoutedEventArgs e)
    {
        Accepted = true;
        Close();
    }

    private void OnNo(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }
}
