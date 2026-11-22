using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace CatPet;

/// <summary>关于：版本号取自程序集（csproj 的 &lt;Version&gt;），作者与主页写死在这里。</summary>
public partial class AboutWindow : Window
{
    public const string RepoUrl = "https://github.com/essayZW";
    public const string Author = "essay";

    public AboutWindow()
    {
        InitializeComponent();
        VersionText.Text = $"版本 {AppVersion}";
    }

    /// <summary>当前版本（如 1.1.0），来自 csproj 的 &lt;Version&gt;。</summary>
    public static string AppVersion
    {
        get
        {
            try
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                if (v is null) return "1.0.0";
                return v.Revision > 0 ? $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}"
                                      : $"{v.Major}.{v.Minor}.{v.Build}";
            }
            catch
            {
                return "1.0.0";
            }
        }
    }

    private void OnOpenRepo(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(RepoUrl) { UseShellExecute = true });
        }
        catch
        {
            // ignore
        }
        e.Handled = true;
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
