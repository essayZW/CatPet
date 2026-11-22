# AGENTS.md · 开发与维护约定

面向在本仓库里干活的人（和 agent）。目标是**改动可复现、素材一致、观感统一**。
逐条实现历史见 [`docs/dev-notes.md`](docs/dev-notes.md)，项目简介见 [`README.md`](README.md)。

---

## 0. 铁律

1. **改完代码/素材默认不重新打包**。只有明确要求时才跑 `CatPet\tools\pack.ps1`。
2. **打包产物不用安装/运行验证**（安装需要管理员权限）。打完核对版本号与体积即可；功能验证一律用 Debug 构建。
3. **改素材前先备份**：素材、`info.lps`、`.git` 都可能被误改，重要操作前先复制。
4. **每次改完素材都要跑校验**：`CatPet\tools\validate-frames.ps1`。0 失败才算完。
5. 说话口吻：气泡里才有「喵」，UI 文案用拟声词/括号，别在正文里带「喵」。

---

## 1. 目录约定

| 路径 | 说明 | 入库 |
| --- | --- | --- |
| `CatPet/src/CatPet/` | WPF 源码 | ✅ |
| `CatPet/src/CatPet/mod/MyCat/pet/cat/` | **成品美术资源**（唯一权威） | ✅ |
| `CatPet/tools/` | 校验 / 导入 / 打包 / 图标脚本 | ✅ |
| `artwork/blender/*.py` | 烘焙脚本 | ✅ |
| `artwork/blender/frames_stand3/` | 站立动作烘焙帧（运镜合成要读） | ❌ 本地 |
| `artwork/blender/*.blend`、`texture_*.png` | 3D 源模型与贴图 | ❌ 本地 |
| `artwork/inbox/`、`artwork/source/` | 原始参考图 / 原始模型 | ❌ 本地 |
| `cat_images/` | 作者的照片素材库 | ❌ 本地 |
| `archive/` | 旧方案、旧预览图、历史 frames | ❌ 本地 |
| `dist/` | 安装包存档 | ❌ 本地 |
| `.tools/dotnet/` | 便携 .NET 8 SDK | ❌ 本地 |

**能复现的都不入库**：整帧序列可以从 `.blend` + 脚本重跑，所以只把成品（`mod/`）和脚本提交。

### 当前在用的 3D 源模型

| 文件 | 用途 |
| --- | --- |
| `artwork/blender/plan_a_rig.blend` | **站立**动作全部（走/跑/飞奔/跳/45° 走/站立跟随/落定过渡） |
| `artwork/blender/cat_rigged.blend` | **坐姿**动作全部（待机/喵叫/打盹/舔毛/坐姿跟随图集…） |
| `artwork/blender/hybrid_rig.blend` | 旧版混合体，已不再使用（保留备查） |

烘焙脚本用 `os.path.join(OUT, "plan_a_rig.blend")` 打开模型，**不要把 .blend 挪走**，否则贴图相对路径会断。

---

## 2. 素材规格（必须统一）

### 2.1 尺寸

| 项 | 值 |
| --- | --- |
| 逻辑画布 | 500×500（`Viewbox` 内统一分辨率，窗口缩放由 `Zoom` 决定） |
| 常规素材 | **330×330**（= 默认窗口 275px 的 1.2 倍） |
| 运镜素材（`cin_*`） | **250×250**（只在大/小尺寸间过渡，够用且省一半） |
| 默认缩放 | `Settings.DefaultZoom = 0.452` → 窗口 226×283 |

- 一律**正方形、带 alpha**、脚底贴在画布 `y = 0.9606 × 画布高`（330 → y=317）
- 降分辨率用 `CatPet/tools/downscale-assets.py`（幂等，跳过已 ≤330 的文件）
- 素材总体积：约 **64 MB / 1314 帧 / 50 个动作目录**

### 2.2 命名与清单

```
stand/swalk_l/swalk_l_000_24.png     ← {目录}/{前缀}_{三位序号}_{帧时长ms}.png
```

- 帧时长写进文件名，播放器直接按它排期
- 新动作必须同时在 `mod/MyCat/pet/cat/info.lps` 里声明一行：
  `PNGAnimation#名字:|path#目录\名字:|mode#Nomal:|graph#Stand:|animat#Single:|`
- 帧数 / 帧时长范围要同步到 `CatPet/tools/validate-frames.ps1` 与 `import-artwork.ps1`

### 2.3 帧数与时长（现状）

| 动作 | 帧数 | 每帧 | 总时长 |
| --- | --- | --- | --- |
| 走 `swalk_l/r` | 30 | 24ms | 720ms/圈 |
| 跑 `srun_l/r` | 30 | 16ms | 480ms |
| 飞奔 `sgal_l/r` | 30 | 14ms | 420ms |
| 跳 `sjump` | 30 | 32ms | 960ms |
| 斜跳 `sjump_diag` | 30 | 27ms | 800ms |
| 45° 走（运镜用） | 30 | 36ms | 1080ms |
| 落定过渡 `sstand_in/out` | 20 | 40ms | 800ms |
| 运镜 `cin_*` | 44 | 45ms | 1980ms |
| 坐姿待机 `idle` | 24 | 100ms | 2400ms |
| 站立待机 `sidle` | 20 | 110ms | 2200ms |
| 跟随图集 `follow/sfollow/sfollow_r` | 125（25 列 × 5 行） | 110ms | — |

> 帧数取舍：移动类统一 **30 帧**、运镜 **44 帧**。加密到 245 帧的跟随图集试过，观感提升有限、体积翻倍，已回退。

---

## 3. 视觉统一（最容易翻车的地方）

### 3.1 机位角度

| 用途 | 方位角 az | 说明 |
| --- | --- | --- |
| 坐姿待机/正脸 | 0 / -25 | 坐姿模型正面、3/4 |
| 站立待机 `sidle` / `sidle_r` | **+25 / −25** | 3/4 视角，一切都以这套为基准 |
| 站立跟随图集 | +25 / −25 | 与 `sidle` 同机位，切换才不"转头" |
| 运镜"出现" | **+25 / −25** | 必须和 `sidle` 同机位 |
| 运镜"离开" | **195 / 165** | 身后 15°；45° 时剪影宽 50%，看着变壮 |
| 侧视走/跑/飞奔 | 90 / 270 | 正侧 |

相机约定：`d = (sin(az)·cos(el), -cos(az)·cos(el), sin(el))`，el ≈ 7°，**猫面朝 −Y**。
正交取景：`L = h / 0.82`，`center.z = mn.z + 0.444·L`。

### 3.2 大小对齐

不同批次烘焙的取景会有 1%~11% 差异，**必须显式对齐**，否则切换时会"跳一下/变一下"：

- 出现/退场素材：末帧（出现）/首帧（退场）对齐 `sidle` 的像素高度
- 运镜内部"行走段"要对齐"落定段同侧交界帧"，不是直接对齐待机
- 跟随图集（`sfollow*`）：中心帧（平视·正前）对齐 `sidle` / `sidle_r` 的高度与宽度
- **站姿跟随的左右两套必须用同一个脚本烘焙**（`bake_follow_stand.py` + `FOLLOW_AZ=±25`）。
  历史上右侧用了另一个脚本，姿态/光照都不同：同高度下宽 12px、暗 4.4%，导致"跟随比待机大一圈"
- 对齐基准值（330 画布）：`sidle` 高 **289px**、脚底 **0.9606**

### 3.3 亮度对齐

同一模型不同机位的渲染亮度不同（正面 ~171，右向 ~154，身后 ~142）。
凡是会**接在待机后面**的素材，都要按待机亮度做一次整段增益（`compose_cinematic.py` 里已内置）：

```
gain = 待机平均亮度 / 本段平均亮度     # 限制在 0.8 ~ 1.45
```

只做整段常数增益，**不要逐帧调整**，否则会闪。

### 3.4 移动类素材的亮度/对比度

站姿移动类（走/跑/飞奔/跳/斜跳）是在正侧机位（az=90/270）烘的，比 3/4 待机暗很多：

| 素材 | 改前均值 | 改后 | 对比度（改前→改后 / 目标） |
| --- | --- | --- | --- |
| `swalk_l` 等左向 | 156 | **170.3** | 34.8 → 34.6 / 34.4 |
| `swalk_r` 等右向 | 142 | **170.6** | 24.5 → 27.7 / 35.1 |

一条命令搞定，幂等：

```powershell
python artwork/blender/match_motion_brightness.py --dry-run   # 先看参数
python artwork/blender/match_motion_brightness.py             # 实际执行
```

算法：先用 **gamma** 把平均亮度对齐到同侧待机（0/255 不动，高光不糊），
再用**线性对比度**把 std 拉向目标（限制在 0.75~1.35）。每个目录一组常数参数，整段共用，不会逐帧闪。

> 右侧背光片源对比度本身只有 24.5，拉到 27.7 已到线性拉伸上限；再拉会显得发灰/发假，所以不再追。
> 改完记得跑 `validate-frames.ps1`，并清 `bin\Debug\...\cache` 再验证。

### 3.5 左右两套素材的完整性

站姿一共 **12 对**（`sidle/slook/swag/sclose/sfollow/sjump/sjump_diag/走/跑/飞奔/进出场运镜`），
素材层面左右齐全；**缺的往往在代码**——凡是"按朝向该换边"的地方都必须走 `SideClip(left, right)`：

```csharp
private string SideClip(string left, string right) =>
    _faceRight && _player.HasClip(right) ? right : left;
```

历史上漏过的点（已修）：`ChurnPosture` 站起来待机（`sidle`）、站着东张西望（`slook`）、
右键菜单「站立待机」「闭眼」项。新增任何站姿动作时，先想一句：**朝右时播哪个？**

> **朝向状态**：`UpdateFacingFromClip` 靠片子名判断朝向——`_r` 结尾=朝右，
> `_l` 结尾**或** `LeftFacingClips`（`sidle`/`slook`/`swag`/`sclose`/`sjump`/`sjump_diag`/`sfollow`）=朝左。
> 不带后缀的那批其实是朝左素材；漏掉它们会出现"画面朝左但 `_faceRight` 还是 true"，
> 于是**头部跟随一接管就切回 `sfollow_r`，看着像转身被撤销**。

---

## 4. 烘焙流程

```powershell
$bl = "<Blender>\blender.exe"
$env:W45_PREFIX="swalk_l"; $env:W45_AZ="90"; $env:W45_N="30"; $env:W45_MS="24"
& $bl -b --python artwork/blender/bake_stand_walk_angle.py
```

| 脚本 | 环境变量 | 产物 |
| --- | --- | --- |
| `bake_stand_walk_angle.py` | `W45_PREFIX/AZ/N/MS` | 任意方位角行走（侧视、45°） |
| `bake_stand_run.py` | `RUN_PREFIX/AZ/N/MS` | 跑 |
| `bake_stand_gallop.py` | `RUN_PREFIX/AZ/N/MS` | 飞奔 |
| `bake_stand_jump.py` / `_diag.py` | `JUMP_PREFIX/AZ/STEP_MS` | 原地跳 / 斜跳 |
| `bake_stand_idle_side.py` | `IDLE_PREFIX/AZ` | 站立待机 |
| `bake_stand_settle.py` | `SET_PREFIX/AZ/DIR/N/MS` | 行走 ↔ 待机 的落定过渡 |
| `bake_follow.py`（坐姿）/ `bake_follow_stand.py`（站姿） | `FOLLOW_AZ` / `FOLLOW_STEP` / `FOLLOW_PREFIX` / `FOLLOW_FRAMES_DIR` | 头部跟随图集（25 列 × 5 行）；站姿左右两套用**同一个脚本**换 `FOLLOW_AZ=25 / -25` |
| `compose_cinematic.py` | — | 把行走 + 落定合成运镜，并做尺寸/亮度/脚底对齐 |

流程：**烘焙 → 装进 `mod/`（必要时降到 330）→ 改 `info.lps` → 跑校验 → 构建验证**。

素材装进 `mod/` 后要清缓存再验证：
```powershell
Remove-Item -Recurse -Force .\CatPet\src\CatPet\bin\Debug\net8.0-windows\cache
```

---

## 5. 代码约定

### 5.1 构建与运行

```powershell
$env:PATH = "<repo>\.tools\dotnet;$env:PATH"
$env:DOTNET_ROOT = "<repo>\.tools\dotnet"
dotnet build .\CatPet\src\CatPet\CatPet.csproj -c Debug
# 运行（不要直接 CatPet.exe，走 dll 更稳）
Start-Process "$env:DOTNET_ROOT\dotnet.exe" -ArgumentList 'CatPet.dll' -WorkingDirectory .\CatPet\src\CatPet\bin\Debug\net8.0-windows
```

**构建前必须杀掉在跑的实例**，否则 `MSB3021/MSB3027 文件被 .NET Host 锁定`：

```powershell
Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" | Where-Object { $_.CommandLine -like '*CatPet.dll*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
```

### 5.2 状态机关键点（踩过的坑，改之前先读）

- `PlayStandAsync` 里 **`_standAction` / `_walkGen` / `_player.Generation`** 三个"代际"各有用途：
  - `_player.Generation`：谁在占用画面
  - `_standAction`：站立动作进行中，跟随不许抢画面
  - `_walkGen`：位移归属，旧动作收尾不能停掉新动作的位移
- **位移只能由 `StartWalk` 起、`StopWalk` 停**。看到"猫没走但窗口在滑"，先查有没有孤儿 `_walkTimer`
- `AnimateScaleTo` 的早退只比当前尺寸；它和走路都会改 `Left/Top`，同时跑会互相盖
- 跟随（`UpdateHeadFollow`）在追逐阶段（`ChaseActive && !_chaseNear`）必须直接 return
- 小游戏里 **移动中不进入头部跟随**：近场分支要 `_chaseDir == 0 && _walkTimer is null`
- 运镜期间 `_cineActive = true`，点击/拖拽一律早退
- 退出走 `OnClosing` 拦截，播完 `_exitAnimDone` 才真正关窗

### 5.3 速度

| 动作 | 速度 | 备注 |
| --- | --- | --- |
| 走 | 48 px/s | 步频 720ms/圈，速度÷步幅要匹配，否则脚底打滑 |
| 跑 | 150 px/s | 3 圈 |
| 飞奔 | 430 px/s | 3 圈，追上小游戏上限 480 |
| 小游戏抓取 | 身体椭圆 `halfW = W*0.42, halfH = H*0.40` | |

改走路速度时**必须同步重烘步频**（`)ms = 34.6 / 速度 × 1000 / 30` 附近），否则观感是滑行。

### 5.4 贴边偏好（行为规则）

猫倾向于待在屏幕边缘，这是有意为之，改行为时别破坏它：

| 规则 | 实现 |
| --- | --- |
| 距边缘 ≤ **96px** 视为"贴着边" | `NearLeftEdge` / `NearRightEdge`（`EdgeComfort`） |
| 两侧边缘都远于 **260px** 视为"在中间" | `AwayFromEdges`（`EdgeFar`） |
| 在中间 → 大概率往最近的边挪 | `ChurnPosture` ②：70% 概率 `StrollOnce(方向, fast: 距离>320)` |
| 贴着边却朝屏幕外 → 转身 | `ChurnPosture` ①：80% 概率播 `InwardClip("slook"/"swag"/"sidle")` |
| 站着＝背对边缘 | 靠右 → 朝左（`sidle`/`slook`/`swag`）；靠左 → 朝右（`*_r`） |

- **判定只看当前位置，不做额外记忆**：所以"被手动拖回边上"自然就满足条件，不会再跑一次。
- 站起来时也走 `InwardClip("sidle")`，否则会站起来面朝屏幕外。
- 普通溜达方向也有 65% 概率朝"更近的那条边"（`StrollOnce` 里）。
- 测试钩子 `CATPET_EDGETEST=center|right|left`：把猫放到对应位置并立刻强制一次轮换。

> 调试提醒：`Diag(...)` 如果写在 `PlayStandAsync(...)` **之后**，这条日志会丢（实测多次）。
> 需要看日志就把 `Diag` 放到动作调用**之前**。

### 5.5 菜单/文案

- 站立动作在右键菜单「站立动作」下，轮数写在元组里，改轮数要连菜单项一起改
- 文案池（`StrollTexts`/`RunTexts`/`JumpMissTexts`…）在 `PetWindow.xaml.cs`，新增文案放进对应池
- 气泡时长用秒；退场气泡要挂到窗口消失（3.2s）

---

## 6. 诊断与测试钩子

| 环境变量 | 作用 |
| --- | --- |
| `CATPET_DIAG=1` | 写 `%LOCALAPPDATA%\CatPet\diag.log`（`[walk]` / `[scale]` / chase tick 等） |
| `CATPET_CHASE=1` | 启动 1.5s 后自动进小游戏 |
| `CATPET_WALKTEST=1` | 2s 向左走、4s 切向左跑 |
| `CATPET_RUNTEST=1` | 5s 点跑、9.5s 点飞奔 |
| `CATPET_EDGETEST=center\|right\|left` | 把猫放到对应位置并立刻强制一次姿态轮换（验证贴边逻辑） |
| `CATPET_ZOOM=0.6` | 强制窗口缩放 |
| `CATPET_NOW=2026-02-17` | 模拟日期（验证节日问候） |

窗口标题形如 `CatPet - [clip] Graph/Animat/Mode`，自动化里用它判断当前动作：
窗口筛选 `-match '^CatPet - \['`。

---

## 7. 路径与隐私

- **不要写死绝对路径**。Python 用 `os.path.dirname(os.path.abspath(__file__))` 推导，
  PowerShell 用 `$RepoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent`，
  文档里用 `<repo>` / `<Blender>` 这类占位。
- `param()` 里的默认值不能引用 `$RepoRoot`（绑定早于赋值），默认留空、在 param 之后再赋值。
- 提交前扫一遍：`git grep -nE '[A-Z]:\\\\(code|Users|Program Files)'` 应为空。
- 截图只截宠物窗口区域，避免带上桌面/任务栏/其他程序。

---

## 8. PowerShell 自动化注意事项

- 脚本文件必须存成 **UTF-8 with BOM**，否则 Windows PowerShell 5.1 读中文会语法错误
- 多行字符串 + 引号通过 `-File` 传参容易被破坏：优先用文件写入（`[IO.File]::WriteAllText`）
- 命令行太长（>32k）会 `The filename or extension is too long`：用 `--pathspec-from-file=-` 或分批
- `git ls-files` 对非 ASCII 文件名会加引号，用 `-c core.quotepath=false` 或 `-z`
- `-Filter` 不支持 `[0-9]` 这类字符类，用 `Where-Object { $_.Name -like ... }`

---

## 9. 打包（先打 tag，再打包）

```powershell
# 1) 改版本号
#    CatPet/src/CatPet/CatPet.csproj 的 <Version>
# 2) 提交
git add -A; git commit -m "发版：1.3.1"
# 3) 打 tag（原名必须和 csproj 版本一致，带 v 前缀）
git tag -a v1.3.1 -m "1.3.1"
# 4) 打包（脚本会校验 HEAD 上确实有 v<版本> 这个 tag，否则拒绝）
& .\CatPet\tools\pack.ps1          # publish -> Inno Setup -> dist\CatPet-Setup-<ver>.exe
```

**tag 规则：只增不改。** tag 一旦创建就固定在那次提交上，**任何情况下都不要 `git tag -f` 移动它**
（移动 tag 等于改写历史，已经推出去的 tag 还会导致本地/远端不一致）。
要发新内容就递增版本号、打一个新 tag。

**版本号怎么升**（按改动大小判断）：

| 变更 | 升法 | 例子 |
| --- | --- | --- |
| 修 bug、文案、素材微调（尺寸/亮度对齐等） | patch `x.y.Z+1` | 1.3.0 → 1.3.1 |
| 新功能、新动作、新交互（行为变化但不破坏兼容） | minor `x.Y+1.0` | 1.3.1 → 1.4.0 |
| 不兼容变更（素材规格/配置格式/目录结构大改） | major `X+1.0.0` | 1.4.0 → 2.0.0 |

- **CI 发版**：`.github/workflows/release.yml` 监听 `v*` tag，自动在 `windows-latest` 上
  跑 `pack.ps1 -WithZip`，把 `CatPet-Setup-<ver>.exe` 和 `CatPet-Portable-<ver>.zip`
  挂到 GitHub Release。所以"本地打 tag 并 push tag"就等于发版。
- CI 里没有 `.tools\dotnet`，`pack.ps1` 会退回 PATH 上的 dotnet（`Test-Path $DotnetRoot` 判断）。
- 版本号来自 csproj 的 `<Version>`，会进安装包文件名与「关于」
- `pack.ps1` 的 tag 校验可以用 `-SkipTagCheck` 应急跳过，但正式发版必须打 tag
- 打包脚本会顺手重生成 `app.ico`（`make-icon.ps1`），产出会出现在 `git status` 里，一起提交
- 产物约 109 MB（自包含 win-x64），`[InstallDelete]` 会清掉旧的 `mod` 和 `cache`
- **打完不用装**（见铁律 2）

---

## 10. 提交规范

- **身份用全局配置**：`git config --global user.name / user.email`。
  不要在仓库里设 `user.name`/`user.email`（`--local`），也不要用 `git -c user.name=…` 临时覆盖。
- 提交信息用中文，格式：`修/新增/文档：一句话` + 正文写清根因与实测数据
- 一次提交只做一件事；素材换代和逻辑修复分开
- 提交前：`validate-frames.ps1` 0 失败、`git status` 无遗漏

---

## 11. 已知限制

- 只支持 Windows（WPF、透明置顶窗口、`SetWindowDisplayAffinity`）
- 缩放上限 2.0、下限 0.25；素材按 330px 烘焙，缩到 2 倍会略糊
- 全屏应用检测靠 2s 轮询，切全屏瞬间可能闪一下
- 头部跟随是 25 档离散图集，超大屏幕下会看出档位（加密到 49 档收益不明显）
