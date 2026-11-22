# VPet（虚拟桌宠模拟器）代码分析报告

> 分析对象：<https://github.com/LorisYounger/VPet>（`main` 分支，commit `b021af2`）
> 分析目的：为"猫咪桌宠"二次开发提取核心实现（桌宠窗口、动画资源管理、交互状态机），并给出最小可运行版本的落地路径。
> 代码位置：本工作区 `VPet/`（采用 partial clone + 按需下载，源码与关键资源齐全，6500+ 张 PNG 未全量拉取）。

---

## 0. 结论速览（TL;DR）

| 问题 | 结论 |
| --- | --- |
| 技术栈 | C# + WPF(.NET) + XAML；配置全部用自研的 **LinePutScript (LPS)** 文本格式；图像合成用 **SkiaSharp**；UI 控件库 Panuon.WPF.UI；Steam 创意工坊用 Facepunch.Steamworks |
| 目标框架 | Core / Interface：`net8.0-windows`；主程序：`net10.0-windows7.0`（想编译原版需要 .NET 10 SDK） |
| 形象资源格式 | **PNG 序列帧**（每帧一张带透明通道的 PNG），外加 APNG、单图 `Picture`、分层 `FoodAnimation` 四种 |
| 动画"动起来"的原理 | 把一整段动画的所有 PNG **横向拼成一张精灵图（sprite sheet）** 缓存到磁盘 → 播放时用 `CroppedBitmap` 从精灵图裁出第 N 帧 → 赋给 WPF `Image.Source`，后台线程按帧时长 `Thread.Sleep` 逐帧推进 |
| 帧时长在哪 | **文件名最后一段数字**：`循环A_005_250.png` 表示该帧 250ms（`PNGAnimation.cs:225`） |
| 形象目录结构 | `mod/{MOD}/pet/{形象名}.lps` + `mod/{MOD}/pet/{形象名}/{动画}/{状态}/{变体}/*.png`，层级本身参与"动画类型/状态/动作"的推断 |
| 窗口怎么透明置顶 | `WindowStyle=None` + 强制 `WS_EX_LAYERED`（Hook `WM_STYLECHANGING`）+ `Topmost`；可选 `WS_EX_TRANSPARENT` 做鼠标穿透 |
| 交互怎么实现 | 窗口上覆盖 500×500 逻辑画布；鼠标事件 + `.lps` 里配置的矩形热区（摸头/摸身体/拎起）；拖拽 = 鼠标位移 → 窗口位移 |
| 状态机 | `GameSave.ModeType`（Happy/Nomal/PoorCondition/Ill）× `GraphType`（Default/Touch_Head/Raised_Static/...）× `AnimatType`（A_Start/B_Loop/C_End/Single） |
| 二次开发入口 | ① 纯资源 MOD（换素材，零代码）② 代码插件 `MainPlugin` ③ 改 Core 源码 |

---

## 1. 仓库与工程结构

```
VPet/
├── VPet.sln
├── VPet-Simulator.Core/            # 核心库（动画引擎 + 游戏逻辑 + 通用 UI）  ← 重点
│   ├── Display/                    # Main 控件、工具栏、消息条、工作计时器
│   ├── Graph/                      # ★ 动画系统：GraphCore / GraphInfo / PNGAnimation / APNG / Picture / FoodAnimation
│   └── Handle/                     # GameCore / GameSave / IController / PetLoader / IFood
├── VPet-Simulator.Windows.Interface/  # 插件与 MOD 的公开 API（MainPlugin / IMainWindow / Mod 数据模型）
├── VPet-Simulator.Windows/         # 主程序（WinForms+WPF 混合），含 mod/ 内置资源
│   ├── Function/                   # CoreMOD(MOD加载) / MWController(窗口控制) / Win32 / Setting
│   ├── WinDesign/                  # 设置、背包、商店等子窗口
│   └── mod/0000_core/              # 内置 MOD：pet(vup 形象)/image/food/text/lang/theme/photo/file
├── VPet-Simulator.Tool/            # MOD 制作辅助工具
└── VPet.Solution/                  # 启动器/管理器（新版）
```

工程依赖（节选自 `VPet-Simulator.Windows.csproj`）：

| 依赖 | 用途 |
| --- | --- |
| `LinePutScript` / `LinePutScript.Localization.WPF` | 一切配置/存档/多语言的文本 DSL |
| `SkiaSharp` | 精灵图拼接、缩放、APNG 解码 |
| `Panuon.WPF.UI` / `Panuon.WPF` | 无边框窗口 `WindowX`、`MessageBoxX`、`ContentControlX` |
| `WpfAnimatedGif` | 相册/教程里的 GIF |
| `NAudio` | 音效（另用 `MediaPlayer` 播语音） |
| `Facepunch.Steamworks` | Steam 创意工坊 MOD 拉取 |
| `Mono.Cecil` | 插件 DLL 安全扫描 |

> 规模：源码约 130 个 `.cs`、47 个 `.xaml`；内置形象 `vup` 共 **6190 张** PNG 帧（25 种动画），单帧 1000×1000、约 150KB。

---

## 2. 启动与窗口机制（"桌宠长在桌面上"的实现）

### 2.1 启动链路

1. `App.xaml.cs` → `new MainWindow()`。
2. `MainWindow` 构造（`MainWindow.xaml.cs:39`）：
   - 解析命令行参数、初始化本地化、尝试 `SteamClient.Init(1920960)`（失败也无所谓）；
   - `_hwnd = new WindowInteropHelper(this).EnsureHandle()` 提前拿窗口句柄（后面挂样式用）；
   - `Task.Run` 里加载 MOD 列表（`mod/*` 目录 + Steam 工坊目录）→ 逐个 `new CoreMOD(dir, this)`；
   - 选形象：`Pets.Find(x => x.Name == Set.PetGraph)`；
   - `Core.Graph = petloader.Graph(Set.Resolution, Dispatcher)` ← **此处一次性构建全部动画索引并异步生成精灵图缓存**；
   - `Main = new Main(Core)`；`Main.Load_0_BaseConsole()`、`Load_2_TouchEvent()`、`Load_2_WaitGraph()`（等所有动画 IsReady）；
   - `DisplayGrid.Child = Main;` ← 把桌宠控件塞进主窗口（`MainWindow.cs:2194`）。

### 2.2 透明 / 置顶 / 无边框

`MainWindow.xaml`：

```xml
<pu:WindowX WindowStyle="None" ShowInTaskbar="False" SizeToContent="WidthAndHeight"
            Background="{x:Null}" ResizeMode="NoResize" ...>
  <WindowChrome.WindowChrome><WindowChrome GlassFrameThickness="-1" /></WindowChrome.WindowChrome>
  <Grid x:Name="MGHost">
    <Grid x:Name="MGrid" Width="250" Height="Auto">
      <Label x:Name="LoadingText" .../>
      <Border x:Name="DisplayGrid" />
```

关键点：

- `Background="{x:Null}"` + `WindowStyle=None` → 没有系统边框与底色，窗口就是"悬浮的图"。
- **不使用** WPF 自带的 `AllowsTransparency`（性能差、会禁用硬件加速）。而是通过 Hook 强制给窗口加 `WS_EX_LAYERED`，见 `MainWindow.xaml.cs:812-830`：

```csharp
((HwndSource)PresentationSource.FromVisual(this)).AddHook((hwnd, msg, wParam, lParam, ref handled) => {
    // WPF 在未设置 AllowsTransparency 时会自动去掉 WS_EX_LAYERED 样式
    // 这里 Hook WM_STYLECHANGING，强行保证 WS_EX_LAYERED 一直存在
    styleStruct.styleNew |= (int)Win32.ExtendedWindowStyles.WS_EX_LAYERED;
    ...
});
```

- 鼠标穿透开关 `SetTransparentHitThrough()`（`MainWindow.xaml.cs:853`）：在 `GWL_EXSTYLE` 上增删 `WS_EX_TRANSPARENT`。
- 置顶：`Topmost` 属性（设置项 `Set.TopMost`）。
- 尺寸 = 内容尺寸：`SizeToContent="WidthAndHeight"`，而内容宽度由缩放决定：

```csharp
public void SetZoomLevel(double zl) {          // MainWindow.cs:240
    Set.ZoomLevel = zl;
    MGrid.Width = 500 * zl;                     // 逻辑画布固定 500，整体按比例放大
    ...
}
```

### 2.3 画布与缩放（非常重要）

`VPet-Simulator.Core/Display/Main.xaml`：

```xml
<Viewbox>                          <!-- 整体缩放：500 逻辑像素 → 窗口实际宽度 -->
  <Grid x:Name="MainGrid" Width="500" ...>
    <MediaElement x:Name="VoicePlayer" />
    <Grid x:Name="UIGrid_Back" VerticalAlignment="Top" Width="500" />
    <Decorator x:Name="PetGrid"  Height="500" Width="500" />   <!-- 双层缓冲 A -->
    <Decorator x:Name="PetGrid2" Height="500" Width="500" />   <!-- 双层缓冲 B -->
    <Grid x:Name="UIGrid" VerticalAlignment="Top" Width="500" />
    <Label x:Name="LabelDisplay" ... />                        <!-- "体力-2 心情+1" 气泡 -->
  </Grid>
</Viewbox>
```

- **坐标系恒定为 500×500**，所有热区坐标（`touchhead: px#159 py#16 sw#189 sh#178`）都是这个 500 空间里的像素。
- 美术资源按 **1000×1000** 制作，`GraphCore.Resolution = 1000`（默认），加载时按 `分辨率` 缩放，再由 `Viewbox` 放大到 `500*ZoomLevel`。
- 两个 `Decorator` 交替使用是**防闪烁的双缓冲**：`Main.Display(IGraph)` 每次把新动画塞进"当前空闲的那个"，再隐藏另一个（`MainDisplay.cs:527-616`）。

---

## 3. ★ 虚拟形象 / 动画资源机制（核心）

### 3.1 四种图像实现，统一接口 `IGraph`

`VPet-Simulator.Core/Graph/IGraph.cs`

```csharp
public interface IGraph : IEquatable<object>, IDisposable {
    void Run(Decorator parant, Action? EndAction = null);  // 在指定容器里播放
    bool IsLoop { get; set; }        // 是否循环
    bool IsReady { get; }            // 异步加载完成？
    GraphInfo GraphInfo { get; }     // 元数据：名字/类型/动作/状态
    TaskControl? Control { get; }    // 播放控制（Stop/Continue）
    string? Path { get; }
}
public class TaskControl {
    public enum ControlType { Status_Quo, Stop, Continue, Status_Stoped }
    public void Stop(Action? endAction = null);
    public void SetContinue();      // 让 B 段"再循环一次"，用于"持续摸头"
}
```

四种实现（注册见 `PetLoader.IGraphConvert`）：

| 实现 | 资源 | 说明 |
| --- | --- | --- |
| `PNGAnimation` | 一个文件夹，里面若干 `*.png` | **主力**。序列帧动画 |
| `APNGAnimation` | 单个 `.png`（内含多帧 APNG） | 用 SkiaSharp 解析 APNG 帧，仍拼成精灵图 |
| `Picture` | 单个 `.png` | 静态图，`length`(ms) 后结束；若该 PNG 其实是 APNG 会自动转 APNG |
| `FoodAnimation` | 图层 + 轨迹配置 | 前/中/后三层，用于喝水/吃东西（杯子在中间层运动） |

```csharp
// PetLoader.cs:63
public static Dictionary<string, LoadGraphDelegate> IGraphConvert = new() {
    { "pnganimation", PNGAnimation.LoadGraph },
    { "apnganimation", APNGAnimation.LoadGraph },
    { "picture", Picture.LoadGraph },
    { "foodanimation", FoodAnimation.LoadGraph },
};
```

### 3.2 MOD 与形象目录结构

```
mod/                                  ← 每个子目录是一个 MOD
└── 0000_core/                        ← MOD 名（info.lps: vupmod#Core）
    ├── info.lps                      ← MOD 元信息（vupmod/intro/author/ver/lang...）
    ├── pet/
    │   ├── vup.lps                   ← ★ 一个"形象"的描述文件
    │   └── vup/                      ← ★ 该形象的所有动画资源根目录（path#vup）
    │       ├── Default/              ← 动画类型 GraphType.Default
    │       │   └── Happy/1/循环A_000_125.png ...
    │       ├── Touch_Head/           ← 摸头
    │       │   ├── A_Nomal/摸头_0_250.png ...
    │       │   ├── B_Nomal/...
    │       │   ├── C_Nomal/...
    │       │   └── A_ill/ B_ill/ C_ill ...
    │       ├── Raised_Static/ ... MOVE/ ... Sleep/ ... WORK/ ... (共 25 个)
    ├── image/  food/  text/  lang/  theme/  photo/  file/
```

`mod/0000_core/pet/vup.lps`（形象描述，节选真实内容）：

```lps
pet#vup:|intor#虚拟主播模拟器默认人物形象:|path#vup:|petname#萝莉斯:|
tag#all,vup,girl:|                                        ← 标签，用于文本筛选
touchhead:|px#159:|py#16:|sw#189:|sh#178:|                ← 摸头热区（500 坐标系）
touchbody:|px#166:|py#206:|sw#163:|sh#136:|               ← 摸身体热区
touchraised:|happy_px#0:|happy_py#50:|happy_sw#500:|...|   ← "拎起来"热区（4 种状态各一套）
raisepoint:|happy_x#290:|happy_y#128:|...|                ← 拎起时鼠标的"抓取点"
pinch:|px#149:|py#128:|sw#56:|sh#59:|
work:|Type#Work:|Name#直播:|Graph#worktwo:|MoneyBase#28:|Time#180:|...|   ← 工作/学习/玩耍
move:|graph#walk.right:|SpeedX#14:|Distance#7:|TriggerRight#200:|...|     ← 自动走动配置
duration:|state#10:|squat#20:|boring#20:|sleep#20:|       ← 各动画随机循环次数
bday:|x#184:|y#12:|w#60:|...|                             ← 生日蛋糕 UI 位置
side:|left#219:|right#281:|                               ← 侧边隐藏位置
```

### 3.3 动画声明的两种写法

#### 先说 LPS 语法（配置文件的"方言"）

VPet 的 `*.lps` 全部是 [LinePutScript](https://github.com/LorisYounger/LinePutScript) 格式，规则只有 4 条：

```
① 一行文本 = 一个 Line（换行才是行分隔符，这点最容易搞错）
② Line 写作   name#info:sub#info:sub2#info2:
③ '|' 结束一个"段"（sub），行尾通常也带一个 '|'
④ '//' 开头是注释；行首是 '|' 或 ':' 表示"上一行的续行"（用于多行文本）
```

所以 `pet#vup:|intor#...:|path#vup:|` 是**一行**：Line 名 `pet`、值 `vup`，带 `intor`、`path` 两个 sub。
读取方式就是 `lps["touchhead"]["px"]` 或 `line.GetString("path")`。

> 这条规则是复刻时踩坑最多的地方：按 `|` 切行会把一行拆成一堆垃圾 Line。

#### (A) 显式声明：目录里有 `info.lps`

`mod/0000_core/pet/vup/Drink/info.lps`（真实内容节选，每行一个动画）：

```lps
PNGAnimation#drink_back_lay:|path#Happy\back_lay:|mode#Happy:|graph#Common:|
FoodAnimation#drink:|mode#Happy:|graph#drink:|a0#1000:|a1#125,268,286,77,60:|a10#625:|front_lay#drink_front_lay:|back_lay#drink_back_lay:|
```

一行 = 一个动画：

```
{实现类}#{动画名}:| path#相对本目录的路径: | mode#状态: | graph#动画类型: | animat#动作段: | loop#true: | 其它
```

- `path` 可指向**文件夹**（PNG 序列）或**单个文件**（Picture/APNG）。
- `mode` / `graph` / `animat` 一旦写了，就**跳过路径推断**，最稳。
- **同一组 A/B/C 要用同一个"动画名"**，播放器才会把它们串成一段完整动作（VPet 靠名字索引）。

#### (B) 隐式推断：没有 `info.lps` 时按"目录路径"猜

`PetLoader.LoadGraph`（`PetLoader.cs:76`）是递归的：

```csharp
if (File.Exists(di.FullName + "\\info.lps")) { ...按声明加载... }
else if (!list.Any()) {          // 叶子目录：无子目录
    var paths = di.GetFiles();
    if (paths.Length == 1) Picture.LoadGraph(...);      // 单图 → 静态图
    else                   PNGAnimation.LoadGraph(...); // 多图 → 序列帧
}
else foreach (var p in list) LoadGraph(graph, p, startuppath);  // 继续递归
```

推断规则在 `GraphInfo(FileSystemInfo path, ILine info)`（`GraphInfo.cs:49`）：

```
相对路径 "Touch_Head/A_Nomal"  →  转小写、'\\'→'_'  →  ["touch_head","a","nomal"]
① mode：显式 mode ？否则找关键字 happy / nomal / poorcondition / ill
② graph：显式 graph ？否则按 GraphType 枚举名的下划线切分做前缀匹配（"touch_head" → Touch_Head）
③ animat：显式 animat ？否则 a/start → A_Start，b/loop → B_Loop，c/end → C_End，否则 Single
④ name：显式行名 ？否则取路径剩余的最后一段（去掉纯数字和 ~ 前缀）；再空就用 graph 名小写
```

这解释了为什么作者可以随手放 `Touch_Head/A_ill/生病摸头_000_125.png` 这种目录也能正确识别。

> 官方文档 `Secondary Development Support Documentation.md` 里的命名规范：
> `{startup_path}_{状态}_{动画类型}_{动画名称}_{动作}_{帧时间}.png`，`_` 与 `/` 等价、顺序不敏感。

### 3.4 ★ 帧文件名 = 帧时长

`PNGAnimation.cs:219-227`：

```csharp
for (int i = 0; i < paths.Length; i++) {
    FrameRects[i] = new Int32Rect(FrameWidth * i, 0, FrameWidth, FrameHeight);
    var noExtFileName = System.IO.Path.GetFileNameWithoutExtension(paths[i].Name);
    int time = int.Parse(noExtFileName.Substring(noExtFileName.LastIndexOf('_') + 1)); // ← 最后一段 = 毫秒
    Animations.Add(new Animation(this, time, i));
}
```

- 例：`循环A_000_125.png` → 第 0 帧，停留 **125ms**；`循环A_005_250.png` → 第 5 帧 250ms（做节奏变化、停顿）。
- **文件名必须以 `_数字.png` 结尾**，否则 `int.Parse` 抛异常 → 该动画 `IsFail`，被从列表里剔除（`Main.Load_2_WaitGraph`）。
- 帧顺序 = `DirectoryInfo.GetFiles("*.png")` 的返回顺序（NTFS 下即字典序），所以序号要**补零对齐**（`000`、`001`…），否则 `10` 会排在 `2` 前面。

### 3.5 ★ 精灵图（Sprite Sheet）机制 —— 性能关键

第一次播放前，所有帧会被**横向拼成一张大图**并缓存到磁盘（`PNGAnimation.cs:117-235`）：

```csharp
Path = Path.Combine(GraphCore.CachePath, $"{Resolution}_{Math.Abs(Sub.GetHashCode(path))}_{paths.Length}.png");
if (!File.Exists(Path)) {
    // 1) 用 SkiaSharp 解码首帧，决定目标尺寸
    w = min(firstImage.Width, Resolution);              // 默认 Resolution = 1000
    h = firstImage.Height * (Resolution / firstImage.Width);
    if (paths.Length * w >= 60000)                       // 防止超宽纹理
        w = 60000 / paths.Length;
    // 2) 建 w*n × h 的大画布，逐帧 DrawBitmap 上去
    using var combinedBitmap = new SKBitmap(w * paths.Length, h);
    ...
    // 3) 以 PNG 存到 cache 目录
    data.SaveTo(stream);
}
```

播放时**不重复解码**，只从这张大图里裁剪：

```csharp
private BitmapSource? GetFrameSource(int frameIndex) {
    if (SpriteSheetSource == null) {
        var sheet = new BitmapImage();
        sheet.CacheOption   = BitmapCacheOption.OnDemand;     // 懒加载
        sheet.CreateOptions = BitmapCreateOptions.DelayCreation;
        sheet.UriSource = new Uri(Path);                       // 指向 cache 里的大图
        sheet.EndInit(); sheet.Freeze();
        SpriteSheetSource = sheet;
    }
    var frame = new CroppedBitmap(SpriteSheetSource, FrameRects[frameIndex]); // 裁剪
    frame.Freeze();
    return frame;
}
```

配套的资源管理：

| 机制 | 位置 | 说明 |
| --- | --- | --- |
| 缓存目录 | `GraphCore.CachePath` = `{exe}\cache` | 换 MOD/分辨率会重建 |
| 并发锁 | `SpriteSheetBuildLocks`（`SemaphoreSlim`/路径） | 防止多线程同时拼同一张图 |
| 内存闸门 | `while (Function.MemoryUsage() > MaxLoadMemory) await Task.Delay(100);` | `MaxLoadMemory` = 可用内存一半（512MB~） |
| 空闲回收 | `CleanTimer` 每 30s 调 `CleanupIdleCache`，2 分钟未用就释放 `BitmapSource` | 只释放像素，元数据保留 |
| 大图体积上限 | `paths.Length * w >= 60000` 时压缩帧宽 | 避免超长纹理 |

### 3.6 播放循环（"动起来"的那一刻）

`PNGAnimation.Animation.Run`（`PNGAnimation.cs:269-310`）：

```csharp
public void Run(FrameworkElement This, TaskControl Control) {
    int index = FrameIndex;
    while (!parent.disposed && ReferenceEquals(parent.Control, Control) && Control.PlayState) {
        var animation  = parent.Animations[index];
        var frameSource = parent.GetFrameSource(animation.FrameIndex);
        This.Dispatcher.Invoke(() => {                       // 回 UI 线程换图
            if (!Control.PlayState) return;
            if (This is Image image) image.Source = frameSource;
            This.Margin = new Thickness(0);
        });
        Thread.Sleep(animation.Time);                        // 帧时长
        if (!Control.PlayState) break;
        if (++index < parent.Animations.Count) continue;
        if (parent.IsLoop) index = 0;                        // 循环
        else if (Control.Type == TaskControl.ControlType.Continue) { Control.Type = Status_Quo; index = 0; }
        else break;
    }
    Control.Type = TaskControl.ControlType.Status_Stoped;
    /* 结束后清引用 + 回调 EndAction */
}
```

要点：

- 每个动画实例一个**后台线程**（`Task.Run(() => Animations[0].Run(img, NEWControl))`），不做全局时钟统一驱动；`Thread.Sleep` 精度约 ±15ms，够用。
- 换帧时把 `Image.Source` 换成 `CroppedBitmap`，WPF 渲染线程负责上屏。
- 三个 `Image` 控件轮换（`Image1/2/3.PNGAnimation`，`Height=500`）防止切换时闪一下。

### 3.7 动画索引：两张字典 + 回退查找

`GraphCore.cs`：

```csharp
Dictionary<GraphType, HashSet<string>> GraphsName;                    // 类型 → 名字集合（随机取一个）
Dictionary<string, Dictionary<AnimatType, List<IGraph>>> GraphsList;  // 名字 → 动作段 → 动画列表（多状态变体）
```

`FindGraph(name, animat, mode)` 的回退策略（`GraphCore.cs:130`）：

```
精确匹配 mode → 相邻 mode（mode+1 再 mode-1）→ 任意非 Ill 的变体 → null
```

所以形象只需要做 `Nomal`，`Happy/PoorCondition` 会自动借用；只有 `Ill` 严格，缺了就返回 null（宁可不动也不出错）。

`GraphInfo` 元组的三要素：

```csharp
public GraphType  Type     // Default / Touch_Head / Touch_Body / Idel / Sleep / Say / Work / Move / Raised_Static ...
public AnimatType Animat   // Single / A_Start / B_Loop / C_End
public ModeType   ModeType // Happy / Nomal / PoorCondition / Ill
```

`GraphType` 完整枚举（`GraphInfo.cs:158`）共 20+ 种，其中标 `*` 为"必须有"：
`Raised_Dynamic`、`Raised_Static*`、`Move`、`Default*`、`Touch_Head`、`Touch_Body`、`Idel`、`Sleep*`、`Say*`、`StateONE`、`StateTWO`、`StartUP*`、`Shutdown`、`Work*`、`Switch_Up/Down/Thirsty/Hunger`、`SideHide_Left/Right_Main/Rise`、`Common`。

### 3.8 ABC 三段式

```
A_Start（进入，播一次） → B_Loop（循环，次数由 duration 配置或交互时长决定） → C_End（退出，播一次）
```

`Main.Display*` 系列把它们串起来，例如摸头（`MainDisplay.cs:168`）：

```csharp
Display(GraphType.Touch_Head, AnimatType.A_Start, name =>
   Display(name, AnimatType.B_Loop, name =>
      DisplayCEndtoNomal(name)));        // C_End → DisplayToNomal
```

"手不松开就一直摸"：`DisplayToTouchHead` 里若当前正处在 `B_Loop`，调用 `ig.SetContinue()` 让它**再循环一轮**而不是打断（`MainDisplay.cs:151-166`）。

---

## 4. 交互机制

### 4.1 鼠标事件 → 命中热区

`Main.xaml.cs` 里 `MainGrid` 挂了 `MouseLeftButtonDown/Up`、`MouseMove`、`MouseRightButtonDown`、`MouseEnter/Leave`。

点击处理（`Main.xaml.cs:353`）：

```csharp
private void MainGrid_MouseLeftButtonDown(...) {
    isPress = true; CountNomal = 0;
    Task.Run(() => {
        Thread.Sleep(Core.Controller!.PressLength);       // 长按阈值
        if (isPress && presstime == pth) {                 // 长按
            foreach (var x in Core.TouchEvent)
                if (x.IsPress && x.Touch(mp) && x.DoAction()) return;
            DefaultPressAction?.Invoke();
        } else {                                           // 单击
            foreach (var x in Core.TouchEvent)
                if (!x.IsPress && x.Touch(mp) && x.DoAction()) return;
            if (DisplayType.Type != GraphType.Default) { CleanState(); DisplayStop(DisplayToNomal); }
            DefaultClickAction?.Invoke();
        }
    });
}
```

热区数据结构（`GameCore.cs:32`）：

```csharp
public class TouchArea {
    public Point Locate; public Size Size;
    public Func<bool> DoAction;   // 返回 true 表示"已消费"
    public bool IsPress;          // false=单击触发, true=长按触发
    public bool Touch(Point p) => p.X-Locate.X is in [0, Size.Width] && p.Y-Locate.Y is in [0, Size.Height];
}
```

热区注册（`Main.xaml.cs:212`）：

```csharp
Core.TouchEvent.Add(new TouchArea(config.TouchHeadLocate, config.TouchHeadSize, () => { DisplayTouchHead(); return true; }));
Core.TouchEvent.Add(new TouchArea(config.TouchBodyLocate, config.TouchBodySize, () => { DisplayTouchBody(); return true; }));
// 4 种状态各一套"拎起"热区，长按触发
```

> 注意：**没有做 alpha 像素级命中检测**。窗口内任意位置（含透明处）都能点到，只是不同矩形映射到不同反应。

### 4.2 拖拽（"拎起来"）

```
MouseDown（落在 touchraised 热区）→ DisplayRaised()
  ↓ 记录鼠标相对"抓取点" raisepoint 的偏移
MouseMove → Core.Controller.MoveWindows(mp.X - raisepoint.X, mp.Y - raisepoint.Y)   // 窗口跟着鼠标走
  ↓ 同时按位移大小切换 Raised_Dynamic(Single) / Raised_Static(A→B→C)
MouseUp → rasetype=-1 → 播 Raised_Static.C_End → DisplayToNomal，并修正窗口位置回到屏幕内
```

`MWController.MoveWindows`（`MWController.cs:94`）：

```csharp
public void MoveWindows(double X, double Y) {
    mw.Dispatcher.Invoke(() => { mw.Left += X * ZoomRatio; mw.Top += Y * ZoomRatio; });
}
```

### 4.3 鼠标悬停 / 摇晃 / 右键

- `MouseEnter/Leave`：侧边隐藏模式（贴边只露一半）时播放 `SideHide_*_Rise`。
- `MainGrid_MouseWave`（`Main.xaml.cs:503`）：检测鼠标在宠物身上的**左右/上下快速移动**（>4 次），触发摸头/摸身体——就是"用鼠标挠它"。
- `MouseRightButtonDown` → 弹出 `ToolBar` 菜单（设置/工作/商店/退出…）。

### 4.4 自动走动（Move）

`vup.lps` 的 `move:` 行定义每个方向动画的行走参数：

```lps
move:|graph#walk.right:|TriggerRight#200:|TriggerType#32:|CheckRight#100:|CheckType#32:|SpeedX#14:|Distance#7:|ModeType#12:|
```

- `Trigger*`：距离屏幕哪条边多近时"可以开始走"；
- `Check*`：走的过程中距离边界多远"必须停"；
- `SpeedX/Y`：`MoveTimer` 每次 tick 移动的像素（乘 `ZoomRatio`）；
- `Distance`：随机走多少步后可能停；
- `ModeType`：该动作允许的宠物状态（位掩码）。

`Move.Display`（`GraphHelper.cs:466`）：播 A → 启动 `MoveTimer` 逐步位移 + 播 B 循环 → 到边界/随机停 → 播 C → `DisplayToNomal`。

---

## 5. 游戏数据与状态机

`GameSave`（`Handle/GameSave.cs`）核心字段：

```
Money / Exp(→Level) / Strength(体力) / StrengthFood(饱腹) / StrengthDrink(口渴)
/ Feeling(心情) / Health(健康) / Likability(好感度) / Mode
```

`Mode`（`IGameSave.ModeType`）：`Happy=0, Nomal=1, PoorCondition=2, Ill=3`。
`CalMode()` 按 Health / Feeling / Likability 阈值算出当前 Mode，**Mode 直接决定挑哪一套动画**（见 §3.7 回退链）。

`Main.DisplayToNomal()`（`MainDisplay.cs:29`）是状态机的"归位点"：

```csharp
switch (State) {
    case WorkingState.Nomal:  DisplayNomal();  return;   // 通常 = DisplayDefault（呼吸循环）
    case WorkingState.Sleep:  DisplaySleep(true); return;
    case WorkingState.Work:   NowWork?.Display(this); return;
    case WorkingState.Travel: DisplayTravel?.Invoke(); return;
}
```

一次"呼吸循环"结束 → 随机进入 Idel / Move / StateONE / StateTWO，再回到 Default，形成自然的行为节奏（`MainDisplay.cs:278-326`、`Main.xaml.cs` 的 `EventTimer`）。

---

## 6. 扩展点（二次开发看这里）

| 层次 | 入口 | 适用场景 |
| --- | --- | --- |
| 资源 MOD | `mod/{名字}/pet/{形象}.lps` + 帧目录 | **换成猫咪形象**，零代码 |
| 代码插件 | 继承 `MainPlugin`（`Interface/MainPlugin.cs`），放 `mod/{名字}/plugin/*.dll` | 加功能（聊天、日程、外部硬件…） |
| Core 源码 | `VPet-Simulator.Core` | 改动画引擎/交互模型 |
| 自定义动画格式 | `PetLoader.IGraphConvert["xxx"] = ...` | 接 Live2D / 视频 / WebP |

`MainPlugin` 生命周期：

```csharp
public abstract class MainPlugin {
    public abstract string PluginName { get; }
    public IMainWindow MW;
    public virtual void LoadPlugin() { }   // 初始化（无 UI 线程）
    public virtual void GameLoaded() { }   // 存档/动画就绪后
    public virtual void EndGame()  { }
    public virtual void Save()     { }
    public virtual void Setting()  { }
    public virtual void LoadDIY()  { }
}
```

---

## 7. 复刻一个"猫咪桌宠"最小版本

### 7.1 技术选型建议

| 方案 | 优点 | 缺点 | 结论 |
| --- | --- | --- | --- |
| **C# WPF（对齐 VPet）** | 与 VPet 同构，资源格式/交互逻辑可直接搬；WPF 透明窗口成熟 | 需 .NET SDK；Windows only | ✅ 推荐（本报告附带的 demo 即此方案） |
| Electron / Tauri | 跨平台、前端生态 | 透明/穿透/置顶在各平台坑多，帧动画性能不如 WPF | 备选 |
| Python + Qt | 快 | 打包、透明、性能一般 | 原型可 |

### 7.2 必须实现的 6 个模块

```
1. 透明置顶窗口   WindowStyle=None, AllowsTransparency=true(简单版), Topmost, ShowInTaskbar=false
2. 实体渲染层     Viewbox → 500×500 画布 → Image（或双 Decorator 防闪烁）
3. 资源加载器     mod/{MOD}/pet/{cat}.lps + 目录扫描 + PNG 序列 → 精灵图
4. 帧播放器       后台线程 + Thread.Sleep(帧时长) + Dispatcher 换 Source
5. 动画索引       名字 → 动作段(A/B/C/Single) → 候选列表；类型 → 随机名
6. 交互状态机     Idle / Touch / Drag / Sleep；热区 + 拖拽 + 右键菜单
```

### 7.3 猫咪资源规范（直接沿用 VPet 规则）

- **画布**：1000×1000 透明 PNG，猫身居中偏下（脚在 y≈950），四周留空给动作幅度。
- **每帧一个文件**，命名 `{任意名}_{序号3位}_{时长ms}.png`，例如 `idle_000_120.png`。
- **一个动作一个文件夹**，文件夹名承载语义（或写 `info.lps` 显式声明，更稳）。
- 建议动画清单（MVP → 完整）：

| 优先级 | GraphType | 动作段 | 帧数建议 | 时长/帧 |
| --- | --- | --- | --- | --- |
| P0 | `Default` | Single（循环） | 12~24 | 100~120ms |
| P0 | `Touch_Head` | A/B/C | 3 / 8 / 3 | 100ms |
| P0 | `Raised_Static` | A/B/C | 2 / 6 / 3 | 100ms |
| P1 | `Idel` | A/B/C | 4 / 12 / 4 | 120ms |
| P1 | `Sleep` | A/B/C | 4 / 16 / 4 | 150ms |
| P1 | `Say` | A/B/C | 2 / 6 / 2 | 100ms |
| P2 | `Move`（walk.left/right） | Single×3 | 6 | 100ms |
| P2 | `Switch_Up/Down`、`Shutdown` | Single | 4~8 | 100ms |

猫咪专属建议：`Default` 做"呼吸 + 眨眼 + 尾巴摆动"三段叠加（分帧画好即可）；`Touch_Head` 做"耳朵压平 + 眯眼 + 呼噜气泡"；`Raised_Static` 做"四肢下垂 + 惊讶眼"。

### 7.4 猫咪形象的 `cat.lps` 模板

```lps
pet#MyCat:|intor#一只会趴在桌面上的猫:|path#:|petname#咪咪:|
tag#all,cat:|

# 热区（500 坐标空间，务必按最终图调整）
touchhead:|px#140:|py#60:|sw#220:|sh#160:|
touchbody:|px#150:|py#220:|sw#200:|sh#180:|
touchraised:|happy_px#0:|happy_py#40:|happy_sw#500:|happy_sh#220:|
           |nomal_px#0:|nomal_py#40:|nomal_sw#500:|nomal_sh#220:|
           |poorcondition_px#0:|poorcondition_py#40:|poorcondition_sw#500:|poorcondition_sh#220:|
           |ill_px#0:|ill_py#160:|ill_sw#500:|ill_sh#260:|
raisepoint:|happy_x#250:|happy_y#180:|nomal_x#250:|nomal_y#180:|
           |poorcondition_x#250:|poorcondition_y#180:|ill_x#250:|ill_y#140:|
duration:|default#10:|head#8:|
```

### 7.5 关键实现伪代码（与 VPet 等价的最小子集）

```csharp
// —— 资源加载 ——
foreach (var dir in Directory.GetDirectories(modRoot))            // 每个 MOD
  if (File.Exists($"{dir}/pet/{cat}.lps"))
     LoadPet($"{dir}/pet/{cat}.lps");                             // 读形象描述 + 热区

// —— 动画目录 → 帧（等价 PetLoader.LoadGraph）——
void Scan(string dir, string root) {
    if (File.Exists($"{dir}/info.lps")) { foreach (var line in ParseLps(...)) Register(line, dir); return; }
    if (!Directory.EnumerateDirectories(dir).Any()) {             // 叶子目录
        var pngs = Directory.GetFiles(dir, "*.png");
        RegisterPngAnimation(dir, pngs, InferGraphInfo(dir, root)); // 路径推断
        return;
    }
    foreach (var sub in Directory.EnumerateDirectories(dir)) Scan(sub, root);
}

// —— 精灵图 ——
BitmapSource GetFrame(int i) {
    if (sheet == null) { sheet = Freeze(new BitmapImage(new Uri(cachePath))); }
    return Freeze(new CroppedBitmap(sheet, new Int32Rect(i * fw, 0, fw, fh)));
}
void BuildSheet(string[] pngs) {                                  // 仅第一次
    var w = Math.Min(frames[0].Width, 1000); var h = frames[0].Height * w / frames[0].Width;
    using var bmp = new SKBitmap(w * pngs.Length, h);
    using var canvas = new SKCanvas(bmp);
    for (int i = 0; i < pngs.Length; i++) canvas.DrawBitmap(SKBitmap.Decode(pngs[i]), new SKRect(w*i, 0, w*(i+1), h));
    bmp.Encode(Png, 100).SaveTo(File.OpenWrite(cachePath));
}

// —— 播放 ——
void Play(FrameAnimation anim, Image target, Action onEnd) {
    var ctrl = new TaskControl(onEnd);
    Task.Run(() => {
        int i = 0;
        while (ctrl.Playing) {
            var src = anim.GetFrame(i);
            target.Dispatcher.Invoke(() => target.Source = src);
            Thread.Sleep(anim.FrameTime[i]);
            if (++i >= anim.FrameCount) { if (anim.IsLoop) i = 0; else break; }
        }
        ctrl.StopAndInvokeEnd();
    });
}

// —— 窗口 ——
// XAML: WindowStyle=None AllowsTransparency=True Background=Transparent Topmost=True ShowInTaskbar=False
// MouseLeftButtonDown  → 判断热区 → 摸头 / 拎起(DragMove 手写位移)
// MouseRightButtonDown → ContextMenu
```

---

## 8. 踩坑清单（从源码里踩出来的）

1. **帧文件名必须以 `_数字.png` 结尾**，否则 `int.Parse` 抛异常，整段动画被静默剔除。
2. **序号要补零**：`2.png` 会排到 `10.png` 之后（按字符串序）。
3. 一个目录里**只有 1 张 PNG** 会被当成静态图 `Picture`，不是"1 帧动画"。
4. `info.lps` 里写错实现类名（必须是 `pnganimation`/`picture`/`apnganimation`/`foodanimation`）只会打印一行 `Unknow Graph Type`，不报错。
5. 精灵图缓存命中 `cache/{分辨率}_{哈希}_{帧数}.png`；**改了图但缓存没删**会看到旧画面（改代码时记得清 `cache`）。
6. 单段动画帧数过多会导致精灵图超宽，帧宽被压到 `60000/n`，画质骤降 → 单段尽量 ≤ 60 帧。
7. `Ill` 状态**不参与回退**：只做了 Nomal 而当前是 Ill，会找不到动画（表现为"卡住/不动"）。
8. WPF `AllowsTransparency=true` 会关闭硬件加速窗口路径，VPet 用 `WS_EX_LAYERED` Hook 绕开；如果只做 demo，直接 `AllowsTransparency=true` 更省事。
9. 热区是**矩形硬编码**，换形象/改缩放都要重新调 `px/py/sw/sh`。
10. `Topmost` + `ShowInTaskbar=false` 的窗口在"显示桌面"时仍会浮着，需自行处理（VPet 用 `IfInActivateScreen` 之类判定）。

---

## 9. 本项目要产出的东西（下一步）

1. ✅ 本报告：技术栈 / 资源格式 / 动画机制 / 交互与状态机
2. ✅ 最小可运行 Demo（`CatPet/`）：透明置顶窗口 + PNG 序列播放 + 摸头/拎起/打盹/右键菜单，**资源目录结构与 VPet 完全对齐**
3. ✅ 真实猫咪素材：四张照片 → 抠图 → 序列帧，已替换进 `mod/MyCat/pet/cat/**`（详见 §11）
4. ⏭ 想要更生动的动作，需补拍同一姿势的连续帧（眨眼/张嘴/甩尾），按 §11.1 流程切片即可

---

## 10. 验证记录（Demo 已跑通）

构建与运行环境：

```
.NET SDK 8.0.404（仓库内 .tools/dotnet，避免本机无 Desktop Runtime 时闪退）
dotnet build CatPet.csproj  →  0 警告 0 错误
```

实测通过的链路：

| 项 | 结果 |
| --- | --- |
| 资源加载 | `mod/MyCat/pet/cat.lps` + `info.lps` 解析出 10 段动画：`Default: idle`、`Touch_Head: head`、`Raised_Static: raise`、`Sleep: doze` |
| 精灵图 | 首次播放生成 `cache/500_xxxxxxxx_16.png`（8000×500，16 帧横排），二次启动直接命中缓存 |
| 待机 | 真实猫照片的呼吸 16 帧循环（120ms/帧） |
| 摸头 | 点中头顶热区 → A(3帧) → B(8帧，被按扁+轻蹭) → C(3帧) → 回待机，头顶弹出气泡 |
| 拖拽 | 按住拖动 → 窗口跟随鼠标、播放 Raised_Static（抬起+摇晃），松手播 C 段落地回待机 |
| 打盹 | 45s 无交互 → Sleep 动画（身子压低、呼吸变慢），任意点击唤醒 |
| 右键 | 总在最前 / 摸摸头 / 重新待机 / 退出 |
| 窗口 | 无边框、透明、置顶（遮住任务栏）、多屏可拖 |

![桌宠运行效果](img/desktop.png)

左：摸头（被按扁 + 气泡）　右：拖拽中（抬起摇晃 + 窗口跟手）　最右：打盹

![摸头](img/touch-head.png) ![拖拽](img/dragging.png) ![打盹](img/sleep.png)

> 复刻过程中遇到并修复的两个真实坑（已写进 README）：
> 1. `AnimationChanged` 是在**播放线程**触发的，直接改 `Window.Title` 会抛跨线程异常 → 整个播放循环静默中断，窗口透明得像没启动。必须 `Dispatcher.InvokeAsync`。
> 2. WPF 的 `CroppedBitmap`/`BitmapImage` 缩放拼图在后台线程上 stride 不可靠 → 拼精灵图改用 GDI+（等价于 VPet 用 SkiaSharp）在像素层完成，拼好落盘再由 WPF 读入裁剪。

---

## 11. 真实猫咪素材落地（照片 → 精灵帧）

### 11.1 流程

```
4 张手机照片
   │  ① 抠图：@imgly/background-removal-node（内置 U²-Net 模型，离线 2s/张）
   ▼
catN.cutout.png（透明背景）
   │  ② 裁到 alpha 包围盒 → 等比缩放到目标高度 → 底部居中对齐到 1000×1000 画布
   │  ③ 逐帧施加整体变换（以"脚底中心"为支点，避免猫飘起来）
   ▼
mod/MyCat/pet/cat/{动画}/{a|b|c}/xxx_000_120.png
```

变换只有 5 种，够把静态照"演出"动作：

| Move | 用途 | 做法 |
| --- | --- | --- |
| `breath` | 待机 | Y 轴缩放 ±1.6%，X 轴反向微缩（体积守恒感） |
| `squash` | 摸头 | 压扁 7%、X 放大 5%、加 1.2° 摆动 |
| `lift` | 拎起 | 抬高 60px、3° 摇摆、轻微拉伸 |
| `doze` | 打盹 | 整体压到 93% 高度 + 更慢更浅的呼吸 |
| `sway` | 备用 | 左右重心移动 + 1.6° 旋转 |

### 11.2 四张照片的实际可用性

| 照片 | 结果 | 原因 |
| --- | --- | --- |
| cat2（地板正面坐姿） | ✅ 采用 | 抠图干净，全身完整，作为所有动作的基础 |
| cat1 / cat4（窗台） | ❌ 仅参考 | 头顶那束玫瑰与猫头**粘连**，抠图会把花一起保留；按 y 轴切掉花会连耳朵一起切掉 |
| cat3（床上趴卧） | ❌ 仅参考 | 白毛压在浅色床单上，模型给的 alpha 普遍偏低；做 gamma 补偿后，床单被一起"补"成不透明方块 |

**结论（对后续拍素材的建议）**：桌宠要"活"，靠的不是把一张照片变形，而是**同一姿势的连续多帧**。
最有性价比的补拍清单：正面坐姿的 ①眨眼 ②张嘴 ③歪头 ④甩尾，每段 6~12 张连拍，
按 §11.1 的流程切帧即可直接替换，代码零改动。

### 11.3 换素材后必须同步的两件事

1. **热区要重算**：`cat.lps` 的 `touchhead/touchbody` 是 500 坐标系硬编码矩形。
   `build-sprites-from-photos.ps1` 跑完会打印按实际占位算出的建议值。
2. **删 `cache/`**：精灵图按 `{帧宽}_{路径哈希}_{帧数}` 命中，不校验内容，改图不删缓存会看到旧画面。

---

## 附：关键源码索引

| 主题 | 文件 | 关键行 |
| --- | --- | --- |
| 动画接口 | `VPet-Simulator.Core/Graph/IGraph.cs` | 10, 104 |
| 动画元数据 & 推断 | `VPet-Simulator.Core/Graph/GraphInfo.cs` | 49-153, 158, 257 |
| 动画索引/查找 | `VPet-Simulator.Core/Graph/GraphCore.cs` | 65-212, 245-352 |
| PNG 序列帧 | `VPet-Simulator.Core/Graph/PNGAnimation.cs` | 117-235（拼图）, 269-310（播放） |
| APNG | `VPet-Simulator.Core/Graph/APNGAnimation.cs` | 92-120 |
| 静态图 | `VPet-Simulator.Core/Graph/Picture.cs` | 45-77 |
| 分层动画 | `VPet-Simulator.Core/Graph/FoodAnimation.cs` | 30-54 |
| MOD 加载 | `VPet-Simulator.Windows/Function/CoreMOD.cs` | 98-414 |
| 形象载入/递归扫描 | `VPet-Simulator.Core/Handle/PetLoader.cs` | 26-131 |
| 显示状态机 | `VPet-Simulator.Core/Display/MainDisplay.cs` | 29-350, 527-616 |
| 鼠标/热区/拖拽 | `VPet-Simulator.Core/Display/Main.xaml.cs` | 212-249, 353-443, 503-573 |
| 窗口透明/穿透 | `VPet-Simulator.Windows/MainWindow.xaml.cs` | 812-880 |
| 窗口移动/边界 | `VPet-Simulator.Windows/Function/MWController.cs` | 58-101 |
| 存档/状态 | `VPet-Simulator.Core/Handle/GameSave.cs` | 251-300 |
| 移动行为 | `VPet-Simulator.Core/Graph/GraphHelper.cs` | 220-545 |
