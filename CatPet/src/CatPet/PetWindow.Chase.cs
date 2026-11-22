using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace CatPet;

/// <summary>
/// 「追鼠标」小游戏：进入后小猫横向跟着鼠标跑，按距离和鼠标速度在
/// 走 / 跑 / 飞奔 之间切换，速度连续平滑，避免忽快忽慢。
///
/// 三个区间：
///   很近（≤ CatchZone）  → 抓到：播 swag 摇尾巴，然后退出小游戏
///   近  （≤ NearZone）   → 不追，站住"看"鼠标（头部跟随）
///   远                   → 追，越远越快（有上限），飞奔段允许再快一档
/// </summary>
public partial class PetWindow
{
    // ---------------- 区间与上限
    private const double NearZone = 165;      // 横向这个范围内进入「看着鼠标」模式（仅在不追的时候）
    private const double MaxChaseSpeed = 480; // 追得再急也不超过这个速度
    private const int CatchGraceMs = 800;     // 进入小游戏后至少这么久才会判定"抓到"
    private const int CelebrateMs = 1500;     // 抓到后摇尾巴的时间（这期间不跑）
    private const int CatchCooldownMs = 2600; // 抓到后的冷却，避免贴着鼠标反复触发

    // ---------------- 小游戏状态
    private DispatcherTimer? _chaseTimer;
    private string _chaseClip = "";                 // 当前在播的片子（sidle / swalk_l / srun_r ...）
    private DateTime _chaseClipAt = DateTime.MinValue;
    private double _chaseSpeed;                     // 平滑后的移动速度 px/s
    private int _chaseDir;                          // 0=站住，-1 / +1
    private double _chaseLastMouseX;
    private DateTime _chaseLastMouseAt = DateTime.MinValue;
    private double _chaseMouseVel;                  // 鼠标横向速度（平滑）px/s
    private DateTime _chaseStartedAt = DateTime.MinValue;
    private bool _chaseNear;                        // 当前是"近距离看着鼠标"模式
    private double? _chaseNearYaw;                  // 近距离模式下的头部偏航角（度）
    private DateTime _chaseCelebrateUntil = DateTime.MinValue;  // 抓到后摇尾巴期间
    private DateTime _chaseAutoStopAt = DateTime.MinValue;      // 邀约开局时 60 秒后自动结束
    private DateTime _chaseHoldUntil = DateTime.MinValue;       // 开局"看你一眼"的短暂停顿
    private DateTime _chaseCatchCooldownUntil = DateTime.MinValue;
    private int _chaseTicks;

    // ---------------- 纵向扑击（跳跃）
    private bool _chaseJumping;                              // 正在空中
    private bool _chaseJumpCatchPending;                     // 空中碰到鼠标，落地后结算抓到
    private int _chaseJumpTries;                              // 连续跳了几次没碰到，够不着就放弃
    private bool _chaseJumpNeedsMove;                         // 刚抓到过：鼠标不动就不再扑
    private double _chaseJumpAnchorX, _chaseJumpAnchorY;      // 抓到时鼠标的位置
    private DispatcherTimer? _chaseJumpTimer;                // 16ms，只在跳跃期间存在
    private double _chaseJumpGroundTop;                      // 起跳前的地面 Top
    private double _chaseJumpPeak;                           // 本次跳跃高度
    private bool _chaseJumpDiagonal;                         // 这次是"斜向扑"还是"原地跳"
    private double _chaseJumpDelayMs;                         // 蓄力阶段时长
    private DateTime _chaseJumpStart;
    private DateTime _chaseJumpCooldownUntil = DateTime.MinValue;
    private DateTime _chaseJumpMissUntil = DateTime.MinValue;   // "差一点"文案的节流
    private bool _chaseCantReachTold;                        // 这次"够不着"已经说过没有
    private double _chaseCantReachAtX, _chaseCantReachAtY;   // 说的时候鼠标在哪
    private const double JumpTriggerDy = 140;                // 鼠标高出这么多就起跳
    private const double MaxJumpPeak = 230;                  // 跳跃高度上限
    private const int JumpFlightMs = 700;                    // 空中时间（放慢，看着不那么急）
    private const int JumpCrouchMs = 240;                    // 原地跳的蓄力时间（蹲下再起跳，约两帧）
    private const int JumpCrouchMsDiag = 100;                // 斜向跳的简短蓄力

    private DateTime _followUntilOnChase;           // 进小游戏前的跟随区间，退出时还原

    // 猫追鼠标：文案要站在猫的视角（不是"来追我"）
    private static readonly string[] ChaseStartTexts = { "（我来抓你咯）", "（你要跑快点了喵）", "（等等我！）" };
    private static readonly string[] ChaseCatchTexts = { "（抓住啦！）", "（逮到你了）", "（嘿嘿，抓到）" };
    private static readonly string[] JumpMissTexts = { "（差一点了喵）", "（就差一点点…）", "（再来一次喵！）" };

    /// <summary>小游戏是否在跑。</summary>
    internal bool ChaseActive => _chaseTimer is not null;

    internal void ToggleChase()
    {
        if (ChaseActive) StopChase(say: true);
        else _ = BeginChaseAsync();
    }

    /// <summary>
    /// 进入小游戏的统一入口：第一次先弹玩法说明，确认后倒数 3-2-1 再真正开始。
    /// </summary>
    private async Task BeginChaseAsync(int autoStopSeconds = 0)
    {
        if (ChaseActive) return;
        if (_settings.ChaseIntroSeen)
        {
            StartChase(autoStopSeconds);
            return;
        }
        await ShowChaseIntroAsync(autoStopSeconds);
    }


    private async Task ShowChaseIntroAsync(int autoStopSeconds)
    {
        if (ChaseActive) return;

        // 站起来再讲规则（游戏是站立姿态）
        if (!_standing)
        {
            if (_asleep) Wake();
            _standing = true;
            AnimateScaleTo(Math.Clamp(_settings.StandZoom, 1.0, 2.0));
            _ = PlayStandIdleForFacingAsync();
        }

        var card = new ChaseIntroWindow { Owner = this };
        card.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var cardW = card.DesiredSize.Width > 0 ? card.DesiredSize.Width : card.Width;
        var cardH = card.DesiredSize.Height > 0 ? card.DesiredSize.Height : 150;

        var vl = SystemParameters.VirtualScreenLeft;
        var vt = SystemParameters.VirtualScreenTop;
        var vr = vl + SystemParameters.VirtualScreenWidth;
        var vb = vt + SystemParameters.VirtualScreenHeight;
        card.Left = Math.Clamp(Left + (Width - cardW) / 2, vl + 4, Math.Max(vl + 4, vr - cardW - 4));
        card.Top = Math.Clamp(Top + 4, vt + 4, Math.Max(vt + 4, vb - cardH - 4));

        card.ShowDialog();
        if (!card.Started) return;

        _settings.ChaseIntroSeen = true;
        _settings.Save();

        // 确认后倒数 3-2-1（约 2.1 秒），给用户时间把鼠标准备好
        for (var n = 3; n >= 1; n--)
        {
            Say($"（{n}）", 1);
            await Task.Delay(700);
        }

        StartChase(autoStopSeconds);
    }

    private void StartChase(int autoStopSeconds = 0)
    {
        if (ChaseActive) return;

        if (_asleep) Wake();

        // 站起来（不播动作，直接从待机开始）
        if (!_standing)
        {
            _standing = true;
            AnimateScaleTo(Math.Clamp(_settings.StandZoom, 1.0, 2.0));
        }

        // 小游戏期间不要自己溜达
        _postureTimer.Stop();
        _followUntilOnChase = _followUntil;
        _followPausedUntil = DateTime.MaxValue;
        StopWalk("进入小游戏");

        _chaseClip = "";
        _chaseSpeed = 0;
        _chaseDir = 0;
        _chaseNear = false;
        _chaseLastMouseAt = DateTime.MinValue;
        _chaseMouseVel = 0;
        _chaseLastMouseX = 0;
        _chaseStartedAt = DateTime.Now;
        _chaseAutoStopAt = autoStopSeconds > 0
            ? DateTime.Now.AddSeconds(autoStopSeconds)
            : DateTime.MinValue;

        // 开局先"看你一眼"：不立刻按菜单点击位置定朝向（那时鼠标多半停在菜单上，
        // 会先朝左再朝右闪一下）。这 260ms 里只站着，之后由第一次移动方向决定身位。
        _chaseHoldUntil = DateTime.Now.AddMilliseconds(260);

        SayFrom(ChaseStartTexts, 2);
        // 不在这里按朝向播待机：交给第一次 ChaseTick 决定（避免身位来回闪）
        _ = PlayStandIdleForFacingAsync();   // 保持当前朝向，别在开局闪一下

        _chaseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(40) };
        _chaseTimer.Tick += (_, _) => ChaseTick();
        _chaseTimer.Start();

        Diag("chase-start");
        RebuildTrayMenu();
    }

    private void StopChase(bool say = false, bool playIdle = true, [System.Runtime.CompilerServices.CallerMemberName] string caller = "")
    {
        if (!ChaseActive) return;
        Diag($"chase-stop 原因: {caller}");
        _chaseTimer?.Stop();
        _chaseTimer = null;

        // 空中退出：停掉跳跃并把窗口放回地面，否则猫会悬在半空
        if (_chaseJumping)
        {
            _chaseJumpTimer?.Stop();
            _chaseJumpTimer = null;
            _chaseJumping = false;
            Top = _chaseJumpGroundTop;
            PetTilt(0);
        }

        StopWalk("退出小游戏");
        _chasePaused();

        _chaseClip = "";
        _chaseSpeed = 0;
        _chaseDir = 0;
        _chaseNear = false;
        _chaseNearYaw = null;
        _chaseAutoStopAt = DateTime.MinValue;
        _chaseJumpTries = 0;
        _chaseJumpNeedsMove = true;
        if (GetCursorPos(out var cc))
        {
            var dd = VisualTreeHelper.GetDpi(this);
            _chaseJumpAnchorX = cc.X / dd.DpiScaleX;
            _chaseJumpAnchorY = cc.Y / dd.DpiScaleY;
        }
        _chaseJumpCatchPending = false;
        _chaseJumpCooldownUntil = DateTime.MinValue;

        if (_standing && playIdle)
        {
            _ = PlayStandIdleForFacingAsync();
        }

        // 恢复待机轮换节奏（Stop 之后必须重新 Start，否则退出小游戏后再也不会自己溜达）
        ApplyActionFrequency();
        _postureTimer.Start();

        if (say) Say("不追啦喵～", 2);
        RebuildTrayMenu();
    }

    /// <summary>退出/暂停时把跟随状态还原（恢复进入前的跟随区间）。</summary>
    private void _chasePaused()
    {
        _followPausedUntil = DateTime.MinValue;
        _followUntil = _followUntilOnChase;
    }

    /// <summary>40ms 一次：读鼠标 → 判定区间 → 选步态/速度 → 驱动窗口位移。</summary>
    private void ChaseTick()
    {
        if (!ChaseActive) return;
        if (_dragging || _resizeMode || _asleep || Visibility != Visibility.Visible) return;
        if (!GetCursorPos(out var cursor)) return;

        var dpi = VisualTreeHelper.GetDpi(this);
        var mouseX = cursor.X / dpi.DpiScaleX;
        var mouseY = cursor.Y / dpi.DpiScaleY;

        var now = DateTime.Now;

        // 邀约开的局：到点自动收工
        if (_chaseAutoStopAt != DateTime.MinValue && now >= _chaseAutoStopAt)
        {
            _chaseAutoStopAt = DateTime.MinValue;
            Say("（玩够啦，歇会儿）", 3);
            StopChase();
            return;
        }

        // 抓到后摇尾巴的这段时间里不跑、不判定
        if (now < _chaseCelebrateUntil)
        {
            if (_walkTimer is not null) StopWalk("庆祝中");   // 抓到后摇尾巴时不要残留位移
            return;
        }

        if (_chaseLastMouseAt != DateTime.MinValue)
        {
            var dt = Math.Max(0.001, (now - _chaseLastMouseAt).TotalSeconds);
            var v = (mouseX - _chaseLastMouseX) / dt;
            _chaseMouseVel = _chaseMouseVel * 0.72 + v * 0.28;   // 平滑，滤掉抖动
        }
        _chaseLastMouseX = mouseX;
        _chaseLastMouseAt = now;
        _lastInteraction = now;      // 玩小游戏期间不算"很久没互动"

        var centerX = Left + Width / 2;
        var delta = mouseX - centerX;
        var dist = Math.Abs(delta);

        // 纵向也纳入判定：猫身（气泡带下方那块）的中心
        var bandHeight = BubbleBandHeight;
        var petHeight = Math.Max(1.0, Height - bandHeight);
        var centerY = Top + bandHeight + petHeight / 2;
        var deltaY = mouseY - centerY;

        // "抓到"按身体接触算：把猫身看成一个椭圆（半宽略小于窗口宽，半高略小于宠物体高），
        // 鼠标进入椭圆才算贴上，避免还差一截就判定抓到
        var halfW = Math.Max(20.0, Width * 0.42);
        var halfH = Math.Max(20.0, petHeight * 0.40);
        var nx = delta / halfW;
        var ny = deltaY / halfH;
        var touching = nx * nx + ny * ny <= 1.0;

        if (++_chaseTicks % 10 == 0)
            Diag($"chase tick: mouse=({mouseX:0},{mouseY:0}) left={Left:0} dist={dist:0} dy={deltaY:0} touch={touching} speed={_chaseSpeed:0} clip={_chaseClip} dir={_chaseDir} near={_chaseNear} face={_faceRight} stand={_standing}");

        // 开局 260ms 内只站着看你一眼，不跑也不抓；顺带让身位切换发生在"开跑"那一刻
        if (now < _chaseHoldUntil)
        {
            SetChaseClip(_faceRight && _player.HasClip("sidle_r") ? "sidle_r" : "sidle", allowSwitch: true);
            return;
        }

        // 抓过之后要先让鼠标动起来才会再扑（否则原地一直跳）
        // 鼠标动了 → "够不着"可以再提示一次
        if (_chaseCantReachTold
            && (Math.Abs(mouseX - _chaseCantReachAtX) > 40 || Math.Abs(mouseY - _chaseCantReachAtY) > 40))
            _chaseCantReachTold = false;

        if (_chaseJumpNeedsMove
            && (Math.Abs(mouseX - _chaseJumpAnchorX) > 40 || Math.Abs(mouseY - _chaseJumpAnchorY) > 40))
            _chaseJumpNeedsMove = false;

        // ---------- 0) 空中：纵向由 JumpTick 驱动，这里只管横向和抓取
        if (_chaseJumping)
        {
            if (touching && now > _chaseCatchCooldownUntil)
                _chaseJumpCatchPending = true;   // 落地后再结算，别在半空摇尾巴
            return;
        }

        // ---------- 1) 抓到了：正在追 + 身体贴上
        // 鼠标自己凑过来（没在追）不算抓到，那是"看着鼠标"模式
        if (touching
            && _chaseDir != 0
            && (now - _chaseStartedAt).TotalMilliseconds > CatchGraceMs
            && now > _chaseCatchCooldownUntil)
        {
            Catch();
            return;
        }

        if (deltaY > -JumpTriggerDy) _chaseJumpTries = 0;   // 鼠标下来了，重新给机会

        // ---------- 2) 很近但没在追：站着看鼠标（只转头、不换身位）
        // 正在追的时候不进这个分支：要让猫一路贴近到身体接触，否则永远抓不到
        // 纵向差太大也不算"近"：鼠标在头顶老高时要走跳跃分支
        // 只有已经停下（没有位移）才进入"盯着你看"；移动中一律优先继续走到鼠标下方
        if (_chaseDir == 0 && _walkTimer is null && dist <= NearZone && Math.Abs(deltaY) <= JumpTriggerDy)
        {
            EnterNearMode(delta);
            return;
        }

        // ---------- 2.4) 鼠标在下方且横向很近：猫下不去，站住低头看
        // （不加这条会掉进"追→贴边→换向"的循环：鼠标正在身子底下时 dir 每拍都在翻）
        if (_chaseDir == 0 && _walkTimer is null && deltaY > JumpTriggerDy && dist <= NearZone)
        {
            EnterNearMode(delta);
            return;
        }

        // ---------- 2.5) 鼠标在上方够远：扑一次（够不着就站住抬头）

        if (deltaY < -JumpTriggerDy && Math.Abs(delta) <= 420
            && now > _chaseJumpCooldownUntil && now > _chaseCelebrateUntil)
        {
            var dirToMouse = delta > 0 ? 1 : -1;
            var edgeL = SystemParameters.VirtualScreenLeft;
            var edgeR = edgeL + SystemParameters.VirtualScreenWidth;
            var canMoveX = (dirToMouse > 0 && Left + Width < edgeR - 8) || (dirToMouse < 0 && Left > edgeL + 8);

            // 不用跳到鼠标中心：身体椭圆有半高，跳低一点更自然
            var need = Math.Max(90, -deltaY - 45);
            var spaceAbove = Top - SystemParameters.VirtualScreenTop;
            var tooHigh = need > MaxJumpPeak || spaceAbove < Math.Min(need, MaxJumpPeak) + 24;
            var cantDoIt = tooHigh || _chaseJumpTries >= 2 || _chaseJumpNeedsMove;

            // 够不着、但横向还有距离：先走近再试，别站在老远就放弃
            // （不然会出现"走到一半停下说够不着"，或者"离得远却原地跳"）
            if (cantDoIt && dist > NearZone * 0.7 && canMoveX)
            {
                // 落到下面的追逐逻辑
            }
            else if (cantDoIt)
            {
                if (!_chaseCantReachTold)
                {
                    _chaseCantReachTold = true;
                    _chaseCantReachAtX = mouseX;
                    _chaseCantReachAtY = mouseY;
                    Say("（够不着喵）", 2);
                    Diag($"chase: jump give-up dy={deltaY:0} need={need:0} tooHigh={tooHigh} tries={_chaseJumpTries} needsMove={_chaseJumpNeedsMove} dist={dist:0}");
                }
                // 还没走到鼠标下方就继续走（停下盯人只在"已经到位"时才做），
                // 否则会出现"移动中突然站着看你、窗口还在滑"
                if (_chaseDir == 0 && _walkTimer is null)
                {
                    EnterNearMode(delta);
                    return;
                }
            }
            else
            {
                Diag($"chase: jump fire need={need:0} dy={deltaY:0} dist={dist:0} tries={_chaseJumpTries}");
                // 横向还有距离就"扑过去"，只有鼠标几乎在正上方才原地跳
                // 起跳前把朝向对准目标：贴着屏幕边追不过去时 dir 是 0，
                // 单看 _faceRight 可能还停在上一次的方向，跳出来就是反的
                if (Math.Abs(delta) > 24) _faceRight = delta > 0;
                var diagonal = canMoveX && Math.Abs(delta) > 60;
                ExitNearMode();   // 起跳前收掉"看着鼠标"状态，别让跟随帧跟步态抢画面
                StartJump(need, diagonal, dirToMouse);
                return;
            }
        }

        // ---------- 3) 追
        // 已经站到鼠标正下方（横向差很小）：先停下，别在原地来回蹭
        if (dist < 26 && _chaseDir != 0)
        {
            StopWalk("到位");
            _chaseDir = 0;
            _chaseSpeed = 0;
            SetChaseClip(_faceRight && _player.HasClip("sidle_r") ? "sidle_r" : "sidle", allowSwitch: true);
            return;
        }
        ExitNearMode();
        // 方向死区：鼠标几乎在正前方/正后方时别来回翻面
        var dir = _chaseDir != 0 && Math.Abs(delta) < 24
            ? _chaseDir
            : (delta > 0 ? 1 : -1);

        // 已经贴在鼠标那一侧的屏幕边缘：站住等它往回走
        var leftEdge = SystemParameters.VirtualScreenLeft;
        var rightEdge = leftEdge + SystemParameters.VirtualScreenWidth;
        var step = Math.Max(3.0, _chaseSpeed * 0.06);
        // 判定要和 StartWalk 的撞边条件一致，否则 StartWalk 已经停了、这里还以为没到边，
        // 会每 40ms 重新 StartWalk → 撞边 → 吐槽 + 写配置，形成死循环
        var atEdge = (dir < 0 && Left <= leftEdge + step) || (dir > 0 && Left + Width >= rightEdge - step);
        if (atEdge)
        {
            if (_chaseDir != 0)
            {
                StopWalk("到边了");
                _chaseDir = 0;
                _chaseSpeed = 0;
            }
            SetChaseClip(_faceRight && _player.HasClip("sidle_r") ? "sidle_r" : "sidle", allowSwitch: true);
            return;
        }

        // 目标速度：距离越远越快；鼠标横向甩得快也加一点；
        // 超过 430（飞奔档）之后还有一小段额外加速，但封顶 MaxChaseSpeed
        var raw = dist * 1.1 + Math.Abs(_chaseMouseVel) * 0.45;
        var target = Math.Clamp(raw, 34.0, MaxChaseSpeed);
        // 速度平滑：加速/减速都不突变
        _chaseSpeed += (target - _chaseSpeed) * 0.20;
        if (_chaseSpeed < 10) _chaseSpeed = 10;

        if (dir != _chaseDir)
        {
            _chaseDir = dir;
            StartWalk(dir, _chaseSpeed);
        }
        else if (_walkTimer is null)
        {
            StartWalk(dir, _chaseSpeed);
        }
        else
        {
            _walk.SetSpeed(_chaseSpeed);   // 同方向只改速度，位移定时器按真实时间积分
        }

        var clip = GaitClip(_chaseSpeed, dir);
        UpdateFacingFromClip(clip);
        SetChaseClip(clip, allowSwitch: true);
    }

    // ------------------------------------------------------------ 跳跃（纵向扑击）

    /// <summary>起跳：窗口 Top 走抛物线，素材播一次 sjump。</summary>
    private void StartJump(double peak, bool diagonal, int dir)
    {
        _chaseJumping = true;
        _chaseJumpGroundTop = Top;
        _chaseJumpTries++;
        _chaseJumpPeak = Math.Clamp(peak, 80, MaxJumpPeak);
        _chaseJumpStart = DateTime.Now;
        _chaseJumpDiagonal = diagonal;
        // 静止跳：先蹲下蓄力一小段时间再离地；斜向跳：跑动中起跳，不额外停顿
        _chaseJumpDelayMs = diagonal ? JumpCrouchMsDiag : JumpCrouchMs;

        // 斜向扑要保证窗口真的在横向移动，否则就成了垂直跳
        if (diagonal && dir != 0 && _walkTimer is null)
            StartWalk(dir, Math.Max(120, _chaseSpeed));

        // 跳跃期间别让头部跟随（60ms 一次）把跳跃片顶掉
        _followPausedUntil = DateTime.Now.AddMilliseconds(_chaseJumpDelayMs + JumpFlightMs + 120);

        var clip = diagonal
            ? SideClip("sjump_diag", "sjump_diag_r")
            : SideClip("sjump", "sjump_r");
        if (!_player.HasClip(clip))
            clip = SideClip("sjump", "sjump_r");
        if (_player.HasClip(clip))
            _ = _player.PlayStandClipAsync(clip, 1);

        _chaseJumpTimer?.Stop();
        _chaseJumpTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _chaseJumpTimer.Tick += (_, _) => JumpTick();
        _chaseJumpTimer.Start();
        Diag($"chase: jump peak={_chaseJumpPeak:0} diag={diagonal}");
    }

    /// <summary>空中每 16ms 更新一次 Top：4p(1-p) 抛物线，p=0.5 到顶。</summary>
    private void JumpTick()
    {
        var elapsed = (DateTime.Now - _chaseJumpStart).TotalMilliseconds;
        var total = _chaseJumpDelayMs + JumpFlightMs;
        if (elapsed >= total)
        {
            _chaseJumpTimer?.Stop();
            _chaseJumpTimer = null;
            _chaseJumping = false;
            Top = _chaseJumpGroundTop;          // 落回原地面的高度
            PetTilt(0);                         // 落地回正
            _chaseJumpCooldownUntil = DateTime.Now.AddMilliseconds(900);
            if (_chaseJumpCatchPending)
            {
                _chaseJumpCatchPending = false;
                Catch();
                return;
            }
            if (DateTime.Now > _chaseJumpMissUntil)
            {
                _chaseJumpMissUntil = DateTime.Now.AddSeconds(5);
                SayFrom(JumpMissTexts, 2);   // 扑空了，来一句
            }
            StopWalk("落地");
            _chaseDir = 0;
            _chaseSpeed = 0;
            // 不要在这里播站立待机：待机是 3/4 视角，走路/跑是纯侧视，
            // 一旦鼠标已经甩到反方向，先闪一帧待机会看到明显的 45° 转身。
            // 留空一拍，下一拍 ChaseTick 会直接按方向选走/跑/飞奔。
            _chaseClip = "";
            _chaseClipAt = DateTime.MinValue;
            Diag("chase: landed");
            return;
        }

        // 蓄力阶段：先蹲着不动（静止跳才有）
        var phase = (elapsed - _chaseJumpDelayMs) / JumpFlightMs;
        if (phase < 0)
        {
            Top = _chaseJumpGroundTop;
            PetTilt(0);
            return;
        }

        Top = _chaseJumpGroundTop - _chaseJumpPeak * 4 * phase * (1 - phase);

        // 斜向跳的倾角完全烘在素材里（左右各一套），程序不再叠加，避免左右符号再出错
        if (_chaseJumpDiagonal)
        {
            PetTilt(0);
            return;
        }

        // 沿抛物线切线倾斜：4p(1-p) 的导数是 4-8p（p<0.5 上升、p>0.5 下降）
        var slope = 4 - 8 * phase;
        // 屏幕坐标里正角是顺时针：朝左的猫要"抬头"就得顺时针转
        var tilt = Math.Clamp(slope * 1.5, -6, 6);
        PetTilt(_faceRight ? -tilt : tilt);
    }

    /// <summary>空中倾斜：绕身体中心旋转画面（只在小游戏跳跃期间用）。</summary>
    private void PetTilt(double degrees)
    {
        if (Math.Abs(degrees) < 0.6)
        {
            PetHolder.RenderTransform = null;
            return;
        }

        PetHolder.RenderTransformOrigin = new Point(0.5, 0.62);
        PetHolder.RenderTransform = new RotateTransform(degrees);
    }

    /// <summary>近距离：站住，让头部跟随鼠标（猫会"看着"鼠标）。</summary>
    private void EnterNearMode(double delta)
    {
        // 近场映射：±NearZone 对应 ±38°，比"全屏映射"更容易看出转头
        _chaseNearYaw = Math.Clamp(delta / NearZone, -1.0, 1.0) * 38.0;
        if (!_chaseNear)
        {
            _chaseNear = true;
            // 无条件停：_chaseDir 可能已经被别处归零，但位移定时器还活着 ——
            // 那样会看到"猫站着看你，窗口却还在慢慢滑"
            StopWalk("停下来看");
            _chaseDir = 0;
            _chaseSpeed = 0;
            // 打开头部跟随（沿用"跟随区间"这套机制，退出时还原）
            _followPausedUntil = DateTime.MinValue;
            _followUntil = DateTime.MaxValue;
            SetChaseClip(_faceRight && _player.HasClip("sidle_r") ? "sidle_r" : "sidle", allowSwitch: true);
            Diag("chase: near (head-follow)");
        }
    }

    /// <summary>鼠标走远了：收起跟随，恢复追。</summary>
    private void ExitNearMode()
    {
        _chaseNearYaw = null;
        if (!_chaseNear) return;
        _chaseNear = false;
        _followUntil = _followUntilOnChase;
        _followPausedUntil = DateTime.MaxValue;     // 追的时候别让跟随抢画面

        // 这里不能调 EndHeadFollow()：它会顺手播一次站立待机（3/4 视角），
        // 紧接着又要切纯侧视的走/跑，看起来就是"先转 45° 再跑"。
        // 只把跟随状态清掉，画面交给下面的步态。
        _followActive = false;
        _followCol = FollowNeutralCol;
        _followRow = 2;

        Diag("chase: far (chase)");
    }

    /// <summary>抓到：摇尾巴表示抓到，但小游戏继续（不退出）。</summary>
    private void Catch()
    {
        var now = DateTime.Now;
        Diag("chase: caught");
        StopWalk("抓到");
        _chaseSpeed = 0;
        _chaseDir = 0;
        _chaseNear = false;
        _chaseNearYaw = null;
        _chaseCelebrateUntil = now.AddMilliseconds(CelebrateMs);
        _chaseCatchCooldownUntil = now.AddMilliseconds(CatchCooldownMs);
        // 抓到后摇尾巴：按当前朝向选素材，朝右追到的就用 swag_r，别突然翻个身
        var wagClip = _faceRight && _player.HasClip("swag_r") ? "swag_r" : "swag";
        if (!_player.HasClip(wagClip)) wagClip = "swag";
        _chaseClip = wagClip;
        _chaseClipAt = now;

        _chaseJumpTries = 0;
        _chaseJumpNeedsMove = true;
        if (GetCursorPos(out var cc))
        {
            var dd = VisualTreeHelper.GetDpi(this);
            _chaseJumpAnchorX = cc.X / dd.DpiScaleX;
            _chaseJumpAnchorY = cc.Y / dd.DpiScaleY;
        }
        SayFrom(ChaseCatchTexts, 3);
        _ = _player.PlayStandClipAsync(wagClip, 2);
    }

    /// <summary>速度 → 步态（带滞回，避免在阈值附近来回切）。</summary>
    private string GaitClip(double speed, int dir)
    {
        var suffix = dir > 0 ? "r" : "l";
        var current = _chaseClip;

        // 滞回：已经在小跑就别因为速度掉到 110 以下立刻切回走，留 25 的余量
        var walkMax = current.StartsWith("srun", StringComparison.Ordinal) ? 165.0 : 140.0;
        var runMax = current.StartsWith("sgal", StringComparison.Ordinal) ? 345.0 : 320.0;

        if (speed <= walkMax && _player.HasClip($"swalk_{suffix}")) return $"swalk_{suffix}";
        if (speed <= runMax && _player.HasClip($"srun_{suffix}")) return $"srun_{suffix}";
        if (_player.HasClip($"sgal_{suffix}")) return $"sgal_{suffix}";
        return _player.HasClip($"srun_{suffix}") ? $"srun_{suffix}" : $"swalk_{suffix}";
    }

    /// <summary>切换播放的片子：同名不重启动画，避免每帧重刷。</summary>
    private void SetChaseClip(string name, bool allowSwitch)
    {
        if (!allowSwitch) return;
        if (name == _chaseClip) return;
        if (!_player.HasClip(name)) return;

        // 步态切换至少间隔 260ms，否则速度在阈值附近时会疯狂切换
        if ((DateTime.Now - _chaseClipAt).TotalMilliseconds < 260 && _chaseClip.Length > 0)
            return;

        _chaseClip = name;
        _chaseClipAt = DateTime.Now;
        _ = _player.PlayStandClipAsync(name, -1);
    }
}
