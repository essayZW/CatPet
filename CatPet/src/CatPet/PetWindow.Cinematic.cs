using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;
using CatPet.Core;

namespace CatPet;

/// <summary>
/// 启动 / 退场的"运镜"。
///
/// 缩放和横向位移都**烘进素材**，窗口本身不动 —— 否则窗口一变大小，
/// 气泡文字跟着缩放，看着会抖；顺带好处是动画全程不改 Left/Top，
/// 不会污染"下次打开的位置记忆"。
///
/// 走哪一侧由"猫离哪边屏幕边缘更近"决定：
///   靠右 → cin_intro / cin_outro（从右边进来，往右边走掉）
///   靠左 → cin_intro_l / cin_outro_l（从左边进来，往左边走掉）
/// 每段都是"行走 + 落定过渡"，不会从迈步姿势硬切到站立待机。
/// </summary>
public partial class PetWindow
{
    // 素材名与时长都在 Core/CinematicTimeline.cs（有单测），这里只做别名
    private const string CineIntroRight = CinematicTimeline.IntroRight;
    private const string CineIntroLeft = CinematicTimeline.IntroLeft;
    private const string CineExitRight = CinematicTimeline.ExitRight;
    private const string CineExitLeft = CinematicTimeline.ExitLeft;

    private bool _exitAnimRunning;
    private bool _cineActive;                                  // 运镜进行中：屏蔽点击/拖拽
    private bool _exitAnimDone;

    private double RightEdge => SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth;
    private double LeftEdge => SystemParameters.VirtualScreenLeft;

    /// <summary>离右边更近 → 用右侧那套（从右边进来 / 往右边走掉）。比较规则见 CinematicTimeline。</summary>
    private bool PreferRightSide =>
        CinematicTimeline.PreferRightSide(Left - LeftEdge, RightEdge - (Left + Width));

    private string IntroClip => PreferRightSide ? CineIntroRight : CineIntroLeft;
    private string ExitClip => PreferRightSide ? CineExitRight : CineExitLeft;

    /// <summary>启动镜头：由远及近走进来，结束后进入站立待机。</summary>
    internal async Task PlayEntranceAsync()
    {
        var clip = CinematicTimeline.ChooseClip(IntroClip, _player.HasClip);
        if (clip is null)
            return;

        _cineActive = true;
        _standAction = true;                                  // 挡住姿态轮换/小动作
        _followPausedUntil = DateTime.Now.AddSeconds(CinematicTimeline.IntroFollowPauseSeconds);   // 只覆盖片长，播完立刻接待机/跟随
        _lastInteraction = DateTime.Now;
        _standing = true;
        // 从右边进来是朝左走，从左边进来是朝右走
        _faceRight = CinematicTimeline.FacingRightForIntro(clip);

        try
        {
            await _player.PlayStandClipAsync(clip, 1);
        }
        catch
        {
            // 运镜失败不影响正常启动
        }
        finally
        {
            _cineActive = false;
            _standAction = false;
            _lastInteraction = DateTime.Now;
            _ = PlayStandIdleForFacingAsync();
        }
    }

    /// <summary>退场镜头：说拜拜 → 由近及远走掉 → 关窗（气泡跟着一起消失）。</summary>
    internal async Task PlayExitAsync()
    {
        if (_exitAnimRunning) return;
        _exitAnimRunning = true;

        try
        {
            if (ChaseActive) StopChase();
            CloseInvite();
            _cineActive = true;
            _standAction = true;
            _followPausedUntil = DateTime.Now.AddSeconds(CinematicTimeline.ExitFollowPauseSeconds);   // 别让跟随帧把退场片顶掉
            _lastInteraction = DateTime.Now;

            var clip = CinematicTimeline.ChooseClip(ExitClip, _player.HasClip);

            if (clip is not null)
            {
                // 气泡要挂到猫消失（停顿 + 片长 ≈ 2.5s，气泡 3.2s 覆盖得住，见 CinematicTimeline）
                Say(CinematicTimeline.ExitText, CinematicTimeline.ExitBubbleSeconds);
                await Task.Delay(CinematicTimeline.ExitBubbleDelayMs);

                _faceRight = CinematicTimeline.FacingRightForExit(clip);
                try { await _player.PlayStandClipAsync(clip, 1); }
                catch { /* 忽略 */ }
            }
            else
            {
                await Task.Delay(CinematicTimeline.ExitNoClipDelayMs);
            }
        }
        finally
        {
            _cineActive = false;
            _exitAnimDone = true;
            _exitAnimRunning = false;
            Close();
        }
    }

    /// <summary>关窗时先放退场镜头（只放一次）。</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_exitAnimDone && IsVisible && !_exitAnimRunning)
        {
            e.Cancel = true;
            _ = PlayExitAsync();
            return;
        }

        base.OnClosing(e);
    }
}
