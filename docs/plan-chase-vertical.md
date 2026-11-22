# 小游戏纵轴移动方案（B + A 组合）

> 目标：鼠标在猫咪上方时，猫咪不是"原地干瞪眼"，而是会**抬头/低头看**（近）或**跳起来扑**（远）。
> 选型理由：现有素材全是"脚踩同一条地面"的站立/行走姿态，直接改窗口 `Top` 会像贴墙滑行；
> 用**跳跃**把纵向位移变成一次短促的抛物线，落地回到原地面，观感最像猫。

---

## 一、现状（要改的地方）

| 项 | 现状 |
| --- | --- |
| 窗口位移 | 小游戏中只改 `Left`，`Top` 全程不动 |
| 纵向参与 | 仅用于"抓到"椭圆判定（`halfH = 宠物体高 × 0.40`） |
| 近场模式 | 只看横向 `dist ≤ NearZone(165)`，纵向完全不看 |
| 头部跟随素材 | `follow` / `sfollow` / `sfollow_r` 共 25 列 × 5 行，含 **−20°…+20° 俯仰** → 抬头/低头零成本 |

---

## 二、目标行为

```
鼠标在猫上方 / 下方
├─ |dy| ≤ 自身半高            → 算"贴脸"，照常走抓取判定（已有椭圆）
├─ 近（|dy| ≤ 220 且 dist ≤ NearZone）
│    └─ 停下，抬头/低头看（复用 follow 图集的 pitch 行），气泡「（抬头看…）」
└─ 远（|dy| > 220 或 dist > NearZone）
     ├─ 鼠标在上方 → 跳跃扑击（一次），空中可判定抓到
     └─ 鼠标在下方 → 不跳，只低头看（猫不会往下钻）
```

跳跃期间：

- 水平方向照常追（`Left` 按现有速度积分，空中允许继续靠近）
- 纵向走抛物线：起跳前 120ms 蓄力（压缩帧），飞行 T=550ms，落地 150ms 缓冲
- 落地后回到**起跳前的 `Top`**（不改变地面线）
- 跳跃冷却 900ms，避免连续弹跳
- 只在"正在追"时跳；近场看着鼠标时不跳

---

## 三、素材需求

新烘 2 套跳跃（左右各一，避免起跳瞬间翻面）：

| 素材 | 帧数 | 时长 | 内容 |
| --- | --- | --- | --- |
| `sjump` | 10 | 60ms/帧（0.6s） | 1-2 蓄力下蹲、3 蹬地伸展、4-7 空中收腿翘尾、8-9 伸腿准备落地、10 落地缓冲 |
| `sjump_r` | 10 | 同上 | 相机 az=−25°，同动作 |

烘焙脚本：`artwork/blender/bake_stand_jump.py`（复用 `plan_a_rig.blend`，与 `bake_stand_wag_side.py` 同结构，只换关键帧姿势）。
亮度：烘焙后用 `match_sides.py` 把 `sjump_r` 对齐 `sjump`。

---

## 四、代码改动

### 1. 新增状态（`PetWindow.Chase.cs`）

```csharp
private bool _chaseJumping;                 // 跳跃中
private DateTime _chaseJumpUntil;           // 落地时刻
private DateTime _chaseJumpCooldownUntil;   // 下次可跳
private double _jumpGroundTop;              // 起跳前的地面 Top
private double _jumpPeak;                   // 本次跳跃高度
private double _jumpStartTop, _jumpFlightStart, _jumpFlightMs;
```

### 2. 跳跃驱动（16ms `DispatcherTimer`，只在跳跃期间存在）

```
progress = (now - _jumpFlightStart) / _jumpFlightMs     // 0..1
Top = _jumpGroundTop - _jumpPeak * 4 * p * (1 - p)      // 抛物线，p=0.5 到顶
```

- 高度自适应：`peak = clamp(mouseY_above - petHeight*0.3, 80, 260)`
- 飞行时长固定 550ms（高度越高越"弹"，不靠拉长时间）
- 落地：`Top = _jumpGroundTop`，`_chaseJumping = false`，播 `sidle`（或直接接下一步追/看）

### 3. 判定顺序（改 `ChaseTick`）

```
0) 跳跃中? → 只更新 Top 与空中抓取判定，跳过走路逻辑
1) 抓到?   → 正在追 + 身体椭圆（含纵向）
2) 近?     → _chaseDir == 0 且 dist ≤ NearZone → 站住 + 抬头/低头（_chaseNearYaw + pitch）
3) 需要跳? → 正在追 且 dy < -220 且 |dx| ≤ 420 且冷却结束 → StartJump()
4) 否则追（横向，同现在）
```

### 4. 近场抬头（复用现成图集）

`EnterNearMode(delta)` 现在只算 yaw；增加 pitch：

```csharp
_chaseNearPitch = Math.Clamp(-deltaY / 120.0, -1, 1) * 20;   // ±20° → 对应行 0..4
```

`UpdateHeadFollow` 里把行号从固定 `2` 改成 `pitch → row`，这样"抬头看上面的鼠标"直接成立。

### 5. 边界

- 跳跃时 `Top` 夹在虚拟屏幕内：`Top ≥ VirtualScreenTop - 4`
- 屏幕顶部空间不足 → 高度自动降到可用空间
- 跳跃中按鼠标点击 = 退出小游戏（沿用现有逻辑），此时强制落地

---

## 五、验收标准

1. 鼠标在猫正上方 300px：猫会跳一次扑上去，空中"抓到"判定与现有椭圆一致
2. 鼠标在猫上方 150px 且横向很近：猫不跳，站着抬头看（头部俯仰可见）
3. 鼠标在猫下方：只低头看，不跳
4. 连续快速上下移动鼠标：不出现连续弹跳（900ms 冷却）
5. 屏幕顶端：跳跃高度自动收敛，猫不会被顶出屏幕
6. 跳跃中点击猫：立即退出游戏并落地
7. 左右朝向：跳跃不会让猫翻面（`sjump` / `sjump_r` 各用一次）

---

## 六、工作量

| 项 | 预估 |
| --- | --- |
| 烘 `sjump` / `sjump_r` + 亮度对齐 | 20 分钟（脚本可复用） |
| 跳跃状态机 + 抛物线 | 40 分钟 |
| 近场俯仰 + 判定顺序调整 | 20 分钟 |
| 自动化验证（跳跃高度、冷却、边界） | 20 分钟 |

---

## 七、可选加料（不在本次范围）

- 落地时轻微"压扁"（缩放 0.95 → 1.0，120ms）增强重量感
- 跳跃时脚下加一个淡影子（跟 `Top` 反向移动，暗示离地高度）
- 连续两次跳不过去就"放弃"，气泡「（够不着…）」
