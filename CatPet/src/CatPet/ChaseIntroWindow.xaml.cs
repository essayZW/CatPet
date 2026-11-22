using System;
using System.Windows;

namespace CatPet;

/// <summary>
/// 第一次进追鼠标小游戏时的玩法说明卡片。点「知道了，开始」返回 true。
/// </summary>
public partial class ChaseIntroWindow : Window
{
    public bool Started { get; private set; }

    public ChaseIntroWindow()
    {
        InitializeComponent();
    }

    private void OnStart(object sender, RoutedEventArgs e)
    {
        Started = true;
        Close();
    }
}
