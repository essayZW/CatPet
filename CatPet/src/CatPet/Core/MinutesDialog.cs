using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CatPet.Core;

/// <summary>
/// 极简输入框：问一个分钟数。WPF 没有内置 InputBox，这里手写一个，
/// 免得为了一个数字再引第三方库。
/// </summary>
public sealed class MinutesDialog : Window
{
    private readonly TextBox _box;
    public int Value { get; private set; }

    public MinutesDialog(string title, int initial, int min = 1, int max = 240)
    {
        Title = title;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        Background = new SolidColorBrush(Color.FromRgb(0xF6, 0xF6, 0xF8));

        var panel = new StackPanel { Margin = new Thickness(16) };

        panel.Children.Add(new TextBlock
        {
            Text = $"{title}（{min} ~ {max} 分钟）",
            Margin = new Thickness(0, 0, 0, 8),
        });

        _box = new TextBox
        {
            Text = initial.ToString(CultureInfo.InvariantCulture),
            Width = 120,
            Padding = new Thickness(4),
            FontSize = 15,
        };
        _box.SelectAll();
        panel.Children.Add(_box);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
        };

        var ok = new Button { Content = "确定", Width = 70, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        ok.Click += (_, _) => Accept(min, max);

        var cancel = new Button { Content = "取消", Width = 70, IsCancel = true };

        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        Content = panel;
        Loaded += (_, _) => _box.Focus();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) Accept(min, max);
        };
    }

    private void Accept(int min, int max)
    {
        if (!int.TryParse(_box.Text.Trim(), out var v) || v < min || v > max)
        {
            MessageBox.Show(this, $"请输入 {min} ~ {max} 之间的整数", "输入无效",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        Value = v;
        DialogResult = true;
    }
}
