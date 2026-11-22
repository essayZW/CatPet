using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace CatPet.Core;

/// <summary>
/// 动画播放器 / 状态机。
/// 播放模型和 VPet 一致：一个动画在后台线程按"每帧毫秒数"推进，通过 Dispatcher 换图。
/// 区别是这里用 async/await + CancellationToken 取代了 VPet 的 TaskControl 轮询。
/// </summary>
public sealed class PetPlayer
{
    private readonly GraphBook _book;
    private readonly Image _target;

    private CancellationTokenSource _cts = new();

    private int _generation;

    /// <summary>
    /// 每次"独占播放"都会自增。调用方在 await 之后比对它，
    /// 就能知道自己的收尾逻辑是否已经过期（被更新的播放顶掉了）。
    /// </summary>
    public int Generation => _generation;
    private Task _current = Task.CompletedTask;
    private TaskCompletionSource _loopBreak = NewTcs();

    public ModeType Mode { get; set; } = ModeType.Nomal;
    public FrameAnimation? Current { get; private set; }
    public string? LastError { get; private set; }

    /// <summary>当前动画切换时触发（用于 UI 显示"现在在干嘛"）。</summary>
    public event Action<FrameAnimation>? AnimationChanged;

    public PetPlayer(GraphBook book, Image target)
    {
        _book = book;
        _target = target;
    }

    private static TaskCompletionSource NewTcs()
        => new(TaskCreationOptions.RunContinuationsAsynchronously);

    // ------------------------------------------------------------ 对外接口

    /// <summary>待机：Default 单段动画无限循环（猫的呼吸/眨眼）。</summary>
    public Task PlayIdleAsync()
        => RunExclusive(async ct => await PlayIdleInner(ct));

    /// <summary>转到正脸并保持待机（B 段无限循环）。</summary>
    public Task PlayFrontIdleAsync() => RunExclusive(async ct =>
    {
        var set = _book.FindSet(GraphType.Idle_Front, Mode);
        if (set.A is not null) await PlayAsync(set.A, 1, ct);
        if (set.B is not null) await PlayAsync(set.B, -1, ct);
        else await PlayIdleInner(ct);
    });

    /// <summary>从正脸转回侧脸并继续待机。</summary>
    public Task PlaySideIdleAsync() => RunExclusive(async ct =>
    {
        var set = _book.FindSet(GraphType.Idle_Front, Mode);
        if (set.C is not null) await PlayAsync(set.C, 1, ct);
        await PlayIdleInner(ct);
    });
    /// <summary>只播这一段，播完就结束（不自动回到待机），由调用方决定下一步。</summary>
    /// <summary>是否声明了某段单帧动画（按名字查）。</summary>
    public bool HasClip(string name) => _book.Find(name, AnimatType.Single, Mode) is not null;

    public Task PlayStandClipAsync(string name, int cycles) => RunExclusive(async ct =>
    {
        var anim = _book.Find(name, AnimatType.Single, Mode);
        if (anim is not null)
            await PlayAsync(anim, cycles, ct);
    });
    /// <summary>被拖起来时的反应：转到正脸再转回去，最后回待机。</summary>
    public Task PlayFaceSwitchAsync() => RunExclusive(async ct =>
    {
        var set = _book.FindSet(GraphType.Idle_Front, Mode);
        if (set.A is not null) await PlayAsync(set.A, 1, ct);   // 侧脸 → 正脸
        await Task.Delay(800, ct);                              // 在正脸停久一点，让"转正脸"看得清
        if (set.C is not null) await PlayAsync(set.C, 1, ct);   // 正脸 → 侧脸
        await PlayIdleInner(ct);                                // 回到默认待机
    });
    /// <summary>眨一下眼（播完回待机）。</summary>
    public Task PlayBlinkAsync() => PlayChainAsync(GraphType.Blink, 1);

    /// <summary>甩毛：整只抖一抖。</summary>
    public Task PlayShakeAsync() => PlayChainAsync(GraphType.Shake, 1);

    /// <summary>观察四周：东张西望 + 轻嗅。</summary>
    public Task PlayLookAroundAsync() => PlayChainAsync(GraphType.LookAround, 1);

    /// <summary>呆望：望着远处走神。</summary>
    public Task PlayGazeAsync() => PlayChainAsync(GraphType.Gaze, 1);

    /// <summary>站立时的观察四周。</summary>
    public Task PlayStandLookAsync() => RunExclusive(async ct =>
    {
        var anim = _book.Find("slook", AnimatType.Single, Mode);
        if (anim is not null) await PlayAsync(anim, 1, ct);
    });
    /// <summary>显示某段动画的指定帧并停住（用于头部跟随鼠标）。</summary>
    public Task ShowStaticAsync(string name, int frameIndex) => RunExclusive(async ct =>
    {
        var anim = _book.Find(name, AnimatType.Single, Mode);
        if (anim is null || anim.FrameCount == 0)
            return;

        Current = anim;
        TouchSheetCache(anim);
        AnimationChanged?.Invoke(anim);

        BitmapSource frame;
        try
        {
            frame = anim.GetFrame(Math.Clamp(frameIndex, 0, anim.FrameCount - 1));
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            return;
        }

        await _target.Dispatcher.InvokeAsync(() =>
        {
            if (!ct.IsCancellationRequested) _target.Source = frame;
        });
        await Task.Delay(-1, ct);      // 停住不动，直到被打断
    });
    /// <summary>站立姿态的待机（循环播放 sidle）。</summary>
    public Task PlayStandIdleAsync() => RunExclusive(async ct =>
    {
        var anim = _book.Find("sidle", AnimatType.Single, Mode);
        if (anim is not null) await PlayAsync(anim, -1, ct);
        else await PlayIdleInner(ct);
    });
    /// <summary>喵叫：Say 的 A → B → C。</summary>
    public Task PlaySayAsync() => PlayChainAsync(GraphType.Say, 2);

    /// <summary>转头张望：Idel 的 A → B → C。</summary>
    public Task PlayTurnAsync() => PlayChainAsync(GraphType.Idel, 2);

    /// <summary>被摸头：Touch_Head 的 A → B → C（蹭手）。</summary>
    public Task PlayTouchHeadAsync() => PlayChainAsync(GraphType.Touch_Head, 2);

    /// <summary>
    /// 打盹：Sleep 的 A → B(若干轮) → C，然后**停在睡姿循环**（不回待机）。
    /// 不能回待机：`_asleep` 要等用户点击才清，而回待机会让画面看起来只是普通待机，
    /// 于是"打盹中但不像睡着"+ 跟随/小动作全被 `_asleep` 挡住 = 看起来像卡死。
    /// </summary>
    public Task PlaySleepAsync() => PlayChainAsync(GraphType.Sleep, 6, endWithIdle: false);

    /// <summary>打哈欠：一次性动画。</summary>
    public Task PlayYawnAsync()
        => RunExclusive(async ct =>
        {
            var anim = _book.Find(GraphType.Yawn, AnimatType.Single, Mode) ?? _book.Find("yawn", AnimatType.Single, Mode);
            if (anim is not null)
                await PlayAsync(anim, 1, ct);
            await PlayIdleInner(ct);
        });

    /// <summary>舔毛：低头舔胸口，下颌快速张合。</summary>
    public Task PlayLickAsync() => PlayChainAsync(GraphType.Lick, 2);

    /// <summary>歪头（好奇）：一次性动画。</summary>
    public Task PlayTiltAsync()
        => RunExclusive(async ct =>
        {
            var anim = _book.Find(GraphType.Tilt, AnimatType.Single, Mode) ?? _book.Find("tilt", AnimatType.Single, Mode);
            if (anim is not null)
                await PlayAsync(anim, 1, ct);
            await PlayIdleInner(ct);
        });

    /// <summary>播放任意名字的动画（调试/扩展用）。</summary>
    public Task PlayAsync(string name, AnimatType animat, int cycles = 1)
        => RunExclusive(async ct =>
        {
            var anim = _book.Find(name, animat, Mode);
            if (anim is not null)
                await PlayAsync(anim, cycles, ct);
            await PlayIdleInner(ct);
        });

    public void Stop() => _cts.Cancel();

    // ------------------------------------------------------------ 内部实现

    private Task PlayChainAsync(GraphType type, int loopCycles, bool endWithIdle = true)
        => RunExclusive(async ct =>
        {
            var set = _book.FindSet(type, Mode);

            if (set.Single is not null && set.A is null && set.B is null && set.C is null)
            {
                // endWithIdle=false：播完停在最后一段循环（打盹用，保持睡姿）
                await PlayAsync(set.Single, endWithIdle ? 1 : -1, ct);
                if (endWithIdle) await PlayIdleInner(ct);
                return;
            }

            if (set.A is not null) await PlayAsync(set.A, 1, ct);
            if (set.B is not null) await PlayAsync(set.B, loopCycles, ct);
            if (set.C is not null) await PlayAsync(set.C, endWithIdle ? 1 : -1, ct);

            if (endWithIdle) await PlayIdleInner(ct);
        });

    private async Task PlayIdleInner(CancellationToken ct)
    {
        var idle = _book.Find(GraphType.Default, AnimatType.Single, Mode)
                   ?? _book.Find(GraphType.Idel, AnimatType.B_Loop, Mode)
                   ?? _book.Find(GraphType.Idel, AnimatType.Single, Mode);

        if (idle is null)
            return;

        await PlayAsync(idle, -1, ct);
    }

    /// <summary>独占播放：取消上一个动画，重新开一条播放链。</summary>
    private Task RunExclusive(Func<CancellationToken, Task> body)
    {
        var previous = _cts;
        _cts = new CancellationTokenSource();
        previous.Cancel();
        _generation++;          // 代际自增：旧的 await 返回后可以据此判断"我已经被顶掉了"

        _loopBreak = NewTcs();
        var ct = _cts.Token;

        _current = Task.Run(async () =>
        {
            try
            {
                await body(ct);
            }
            catch (OperationCanceledException)
            {
                // 被新动画打断，正常
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
            }
        }, CancellationToken.None);

        return _current;
    }

    /// <summary>
    /// 逐帧播放。cycles &lt; 0 表示无限循环；breakSignal 完成则提前结束（用于"松手"）。
    /// </summary>
    // ------------------------------------------------------------ 精灵图 LRU

    /// <summary>同时在内存里保留的精灵图上限。500px 帧的精灵图一张约 20~40 MB。</summary>
    private const int SheetCacheLimit = 12;

    private readonly List<FrameAnimation> _sheetOrder = new();
    private static DateTime _lastCollect = DateTime.MinValue;

    /// <summary>记录一次使用；超过上限就把最久没用的精灵图放掉，并适时回收一次。</summary>
    private void TouchSheetCache(FrameAnimation anim)
    {
        bool evicted = false;
        lock (_sheetOrder)
        {
            _sheetOrder.Remove(anim);
            _sheetOrder.Add(anim);

            while (_sheetOrder.Count > SheetCacheLimit)
            {
                var oldest = _sheetOrder[0];
                _sheetOrder.RemoveAt(0);
                if (!ReferenceEquals(oldest, anim))
                {
                    oldest.ReleaseSheet();
                    evicted = true;
                }
            }
        }

        // 放掉的是 WIC 位图（非托管内存），必须强制 GC 才会真的还给系统；
        // 每次回收都很贵，所以最多一分钟一次，而且丢到后台线程做
        if (evicted && (DateTime.Now - _lastCollect).TotalSeconds > 60)
        {
            _lastCollect = DateTime.Now;
            _ = Task.Run(() =>
            {
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                GC.Collect(2, GCCollectionMode.Forced, true, false);
            });
        }
    }
    private async Task PlayAsync(FrameAnimation anim, int cycles, CancellationToken ct, Task? breakSignal = null)
    {
        if (anim.FrameCount == 0)
            return;

        Current = anim;
        AnimationChanged?.Invoke(anim);

        for (var cycle = 0; cycles < 0 || cycle < cycles; cycle++)
        {
            for (var i = 0; i < anim.FrameCount; i++)
            {
                if (ct.IsCancellationRequested)
                    return;
                if (breakSignal is { IsCompleted: true })
                    return;

                BitmapSource frame;
                try
                {
                    frame = anim.GetFrame(i); // 首次会构建精灵图（较慢，其余帧很快）
                }
                catch (Exception ex)
                {
                    LastError = $"GetFrame 失败：{ex.Message}";
                    System.Diagnostics.Debug.WriteLine(LastError);
                    Console.WriteLine("[CatPet][ERR] " + LastError);
                    return;
                }

                await _target.Dispatcher.InvokeAsync(() =>
                {
                    if (!ct.IsCancellationRequested)
                        _target.Source = frame;
                });

                await Task.Delay(anim.Duration(i), ct);
            }
        }
    }
}
