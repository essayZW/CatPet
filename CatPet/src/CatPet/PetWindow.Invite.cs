using System;
using System.Windows;

namespace CatPet;

/// <summary>
/// 「一起玩」邀约：待机时低概率弹出，接受后进入追鼠标小游戏（60 秒自动结束）。
/// "不" 有 15 秒倒计时，超时视为拒绝。专注模式（番茄钟工作）下不会发起邀约。
/// </summary>
public partial class PetWindow
{
    private ChaseInviteWindow? _inviteWindow;
    private DateTime _inviteCooldownUntil = DateTime.MinValue;
    private const int InviteAutoStopSeconds = 60;   // 邀约开局的时长
    private const int InviteCooldownSeconds = 180;  // 被拒/玩完后这么久内不再邀请

    private static readonly string[] InviteTexts =
    {
        "（陪我玩一会儿喵？）",
        "（一起追鼠标好不好？）",
        "（来玩嘛，就一小会儿）",
    };

    private static readonly string[] InviteDeclineTexts = { "（那我自己待着）", "（好吧…）", "（哼，不玩就不玩）" };

    internal bool InviteOpen => _inviteWindow is not null;

    /// <summary>现在能不能发起邀约。</summary>
    private bool CanInvite =>
        !InFocusMode && !ChaseActive && !InviteOpen && !_asleep
        && !_dragging && !_resizeMode && Visibility == Visibility.Visible
        && DateTime.Now > _inviteCooldownUntil
        && DateTime.Now - _lastInteraction > TimeSpan.FromSeconds(20);

    /// <summary>待机轮换时调用：小概率发起"一起玩"的邀约。</summary>
    private bool MaybeInvite()
    {
        if (!CanInvite) return false;
        // 概率压低：轮换一次（18~28 秒）只有 18% 会邀请
        if (Random.Shared.NextDouble() > 0.18) return false;
        ShowInvite();
        return true;
    }

    private void ShowInvite()
    {
        if (InviteOpen) return;

        // 邀约时先站起来（游戏是站立姿态）
        if (!_standing)
        {
            _standing = true;
            AnimateScaleTo(Math.Clamp(_settings.StandZoom, 1.0, 2.0));
            _ = PlayStandIdleForFacingAsync();
        }

        var win = new ChaseInviteWindow(InviteTexts[Random.Shared.Next(InviteTexts.Length)])
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.Manual,
        };

        // 和普通气泡同一个高度：贴在窗口顶部（气泡带那一条），水平居中
        win.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var cardW = win.DesiredSize.Width > 0 ? win.DesiredSize.Width : win.Width;
        var cardH = win.DesiredSize.Height > 0 ? win.DesiredSize.Height : 84;

        var vl = SystemParameters.VirtualScreenLeft;
        var vt = SystemParameters.VirtualScreenTop;
        var vr = vl + SystemParameters.VirtualScreenWidth;
        var vb = vt + SystemParameters.VirtualScreenHeight;

        win.Left = Math.Clamp(Left + (Width - cardW) / 2, vl + 4, Math.Max(vl + 4, vr - cardW - 4));
        win.Top = Math.Clamp(Top + 4, vt + 4, Math.Max(vt + 4, vb - cardH - 4));

        win.Closed += (_, _) => OnInviteClosed(win);
        _inviteWindow = win;
        win.Show();
        Diag("invite: shown");
    }

    private void OnInviteClosed(ChaseInviteWindow win)
    {
        _inviteWindow = null;
        var accepted = win.Accepted;
        _inviteCooldownUntil = DateTime.Now.AddSeconds(InviteCooldownSeconds);

        if (accepted)
        {
            Diag("invite: accepted");
            Say("（我来了喵！）", 2);
            _ = BeginChaseAsync(InviteAutoStopSeconds);
        }
        else
        {
            Diag("invite: declined");
            SayFrom(InviteDeclineTexts, 2);
        }
    }

    /// <summary>关掉正在显示的邀约（比如用户点了猫咪）。</summary>
    private void CloseInvite()
    {
        var w = _inviteWindow;
        _inviteWindow = null;
        if (w is not null)
        {
            try { w.Close(); } catch { /* ignore */ }
        }
    }
}
