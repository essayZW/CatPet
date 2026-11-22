using System;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows;

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
    private const string CineIntroRight = "cin_intro";
    private const string CineIntroLeft = "cin_intro_l";
    private const string CineExitRight = "cin_outro";
    private const string CineExitLeft = "cin_outro_l";
    private const string CineExitText = "（拜拜了喵，明天见）";

    private bool _exitAnimRunning;
    private bool _cineActive;                                  // 运镜进行中：屏蔽点击/拖拽
    private bool _exitAnimDone;

    private double RightEdge => SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth;
    private double LeftEdge => SystemParameters.VirtualScreenLeft;

    /// <summary>离右边更近 → 用右侧那套（从右边进来 / 往右边走掉）。</summary>
    private bool PreferRightSide =>
        RightEdge - (Left + Width) <= Left - LeftEdge;

    private string IntroClip => PreferRightSide ? CineIntroRight : CineIntroLeft;
    private string ExitClip => PreferRightSide ? CineExitRight : CineExitLeft;

    /// <summary>启动镜头：由远及近走进来，结束后进入站立待机。</summary>
    internal async Task PlayEntranceAsync()
    {
        var clip = IntroClip;
        if (!_player.HasClip(clip))
            clip = clip == CineIntroRight ? CineIntroLeft : CineIntroRight;
        if (!_player.HasClip(clip))
            return;

        _cineActive = true;
        _standAction = true;                                  // 挡住姿态轮换/小动作
        _followPausedUntil = DateTime.Now.AddSeconds(2.1);    // 只覆盖片长，播完立刻接待机/跟随，不留空档
        _lastInteraction = DateTime.Now;
        _standing = true;
        // 从右边进来是朝左走，从左边进来是朝右走
        _faceRight = clip == CineIntroLeft;

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
            _followPausedUntil = DateTime.Now.AddSeconds(6);   // 别让跟随帧把退场片顶掉
            _lastInteraction = DateTime.Now;

            var clip = ExitClip;
            if (!_player.HasClip(clip))
                clip = clip == CineExitRight ? CineExitLeft : CineExitRight;

            if (_player.HasClip(clip))
            {
                // 气泡一直挂到猫消失（520ms 停顿 + 约 2.1s 走远 ≈ 2.6s）
                Say(CineExitText, 3.2);
                await Task.Delay(520);

                _faceRight = clip == CineExitLeft ? false : true;
                try { await _player.PlayStandClipAsync(clip, 1); }
                catch { /* 忽略 */ }
            }
            else
            {
                await Task.Delay(150);
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
