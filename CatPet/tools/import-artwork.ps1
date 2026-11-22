#requires -Version 5.1
<#
.SYNOPSIS
    把「中间目录」里的模型出图 导入 到工程素材目录。
.DESCRIPTION
    流程：inbox（模型产物原样堆放） → 归一化(fit) → 校验 → 备份旧帧 → 覆盖到 mod 目录。
    这样项目目录只在"确认导入"的一刻被改动，出图过程中随时可以重来。

    默认路径：
      inbox   <repo>\artwork\inbox
      工程    <repo>\CatPet\src\CatPet\mod\MyCat\pet\cat
      备份    <repo>\artwork\backup\<时间戳>\
.EXAMPLE
    # 先干跑，看看会导哪些
    ./import-artwork.ps1 -WhatIf

.EXAMPLE
    # 真正导入
    ./import-artwork.ps1
#>
param(
    [string]$Inbox = '',
    [string]$Target = '',
    [string]$BackupRoot = '',
    [string]$CatgenDll = '',
    [string]$DotnetExe = '',
    [int]$Canvas = 1000,
    [switch]$Fit,            # 需要把非 1000×1000 的源图归一化（默认不做，Blender 出来的就是 1000）
    [switch]$NoBackup,
    [switch]$WhatIf
)

$RepoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent   # 仓库根目录（CatPet/tools 往上两级）

if (-not $Inbox) { $Inbox = Join-Path $RepoRoot 'artwork\inbox' }
if (-not $Target) { $Target = Join-Path $RepoRoot 'CatPet\src\CatPet\mod\MyCat\pet\cat' }
if (-not $BackupRoot) { $BackupRoot = Join-Path $RepoRoot 'artwork\backup' }
if (-not $CatgenDll) { $CatgenDll = Join-Path $RepoRoot 'CatPet\tools\catgen\bin\Release\net8.0-windows\catgen.dll' }
if (-not $DotnetExe) { $DotnetExe = Join-Path $RepoRoot '.tools\dotnet\dotnet.exe' }


Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

# 动画清单：目录 -> 帧数范围 + 前缀（帧数不固定，多则更顺滑）
$Expect = [ordered]@{
    'idle'         = @{ Min = 12; Max = 32; Prefix = 'idle' }
    'say\a'        = @{ Min = 2;  Max = 6;  Prefix = 'say_a' }
    'say\b'        = @{ Min = 6;  Max = 20; Prefix = 'say_b' }
    'say\c'        = @{ Min = 2;  Max = 6;  Prefix = 'say_c' }
    'turn\a'       = @{ Min = 3;  Max = 10; Prefix = 'turn_a' }
    'turn\b'       = @{ Min = 6;  Max = 24; Prefix = 'turn_b' }
    'turn\c'       = @{ Min = 3;  Max = 10; Prefix = 'turn_c' }
    'sleep\a'      = @{ Min = 3;  Max = 10; Prefix = 'sleep_a' }
    'sleep\b'      = @{ Min = 10; Max = 40; Prefix = 'sleep_b' }
    'sleep\c'      = @{ Min = 3;  Max = 10; Prefix = 'sleep_c' }
    'touch_head\a' = @{ Min = 2;  Max = 8;  Prefix = 'head_a' }
    'touch_head\b' = @{ Min = 6;  Max = 30; Prefix = 'head_b' }
    'touch_head\c' = @{ Min = 2;  Max = 8;  Prefix = 'head_c' }
    'yawn'         = @{ Min = 8;  Max = 24; Prefix = 'yawn' }
    'tilt'         = @{ Min = 8;  Max = 24; Prefix = 'tilt' }
    'lick\a'       = @{ Min = 3;  Max = 10; Prefix = 'lick_a' }
    'lick\b'       = @{ Min = 8;  Max = 32; Prefix = 'lick_b' }
    'lick\c'       = @{ Min = 3;  Max = 10; Prefix = 'lick_c' }
    'shake'        = @{ Min = 10; Max = 28; Prefix = 'shake' }
    'look_around'  = @{ Min = 20; Max = 60; Prefix = 'look_around' }
    'blink'        = @{ Min = 3; Max = 9; MsMin = 50; MsMax = 200; Prefix = 'blink' }
    'gaze'         = @{ Min = 12; Max = 32; Prefix = 'gaze' }
    'follow'       = @{ Min = 125; Max = 125; Prefix = 'follow' }
    'front\a'      = @{ Min = 3;  Max = 10; Prefix = 'front_a' }
    'front\b'      = @{ Min = 10; Max = 32; Prefix = 'front_b' }
    'front\c'      = @{ Min = 3;  Max = 10; Prefix = 'front_c' }
    'stand\sidle'    = @{ Min = 12; Max = 32; Prefix = 'sidle' }
    'stand\swalk_l'  = @{ Min = 8;  Max = 24; Prefix = 'swalk_l' }
    'stand\swalk_r'  = @{ Min = 8;  Max = 24; Prefix = 'swalk_r' }
    'stand\swag_r' = @{ Min = 8; Max = 30; MsMin = 60; MsMax = 140; Prefix = 'swag_r' }
    'stand\sjump'  = @{ Min = 24; Max = 40; MsMin = 20; MsMax = 340; Prefix = 'sjump'; Airborne = $true }
    'stand\sjump_r' = @{ Min = 24; Max = 40; MsMin = 20; MsMax = 340; Prefix = 'sjump_r'; Airborne = $true }
    'stand\sjump_diag'   = @{ Min = 24; Max = 40; MsMin = 12; MsMax = 240; Prefix = 'sjump_diag'; Airborne = $true }
    'stand\sjump_diag_r' = @{ Min = 24; Max = 40; MsMin = 12; MsMax = 240; Prefix = 'sjump_diag_r'; Airborne = $true }
    'stand\swag'     = @{ Min = 10; Max = 28; Prefix = 'swag' }
    'stand\sidle_r' = @{ Min = 12; Max = 30; MsMin = 80; MsMax = 160; Prefix = 'sidle_r' }
    'stand\sfollow_r' = @{ Min = 125; Max = 125; MsMin = 80; MsMax = 200; Prefix = 'sfollow_r' }
    'stand\sfollow' = @{ Min = 125; Max = 125; Prefix = 'sfollow' }
    'stand\sgal_r'  = @{ Min = 20; Max = 40; MsMin = 10; MsMax = 120; Prefix = 'sgal_r' }
    'stand\sgal_l'  = @{ Min = 20; Max = 40; MsMin = 10; MsMax = 120; Prefix = 'sgal_l' }
    'stand\srun_r'  = @{ Min = 20; Max = 40; MsMin = 10; MsMax = 120; Prefix = 'srun_r' }
    'stand\srun_l'  = @{ Min = 20; Max = 40; MsMin = 10; MsMax = 120; Prefix = 'srun_l' }
    'stand\sclose_r' = @{ Min = 6; Max = 16; MsMin = 80; MsMax = 200; Prefix = 'sclose_r' }
    'stand\sclose' = @{ Min = 6; Max = 16; MsMin = 80; MsMax = 200; Prefix = 'sclose' }
    'stand\slook_r' = @{ Min = 20; Max = 60; MsMin = 60; MsMax = 120; Prefix = 'slook_r' }
    'stand\slook'  = @{ Min = 20; Max = 60; Prefix = 'slook' }
}

if (-not (Test-Path -LiteralPath $Inbox)) {
    Write-Host "[ERR] inbox 不存在：$Inbox" -ForegroundColor Red
    exit 1
}
# 归一化（fit）只在显式 -Fit 时需要 catgen；Blender 烘焙出来的帧本来就是 1000×1000，默认不做
if ($Fit -and -not (Test-Path -LiteralPath $CatgenDll)) {
    Write-Host "[ERR] 指定了 -Fit，但找不到归一化工具：$CatgenDll" -ForegroundColor Red
    exit 1
}

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$backupDir = Join-Path $BackupRoot $stamp
$processedRoot = Join-Path (Split-Path $Inbox -Parent) 'processed'

function Invoke-Fit([string]$src, [string]$dst) {
    $dir = Split-Path -Parent $dst
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    & $DotnetExe $CatgenDll --mode fit --src $src --out $dst 2>&1 | Out-Null
    return (Test-Path -LiteralPath $dst)
}

function Get-AlphaStats([string]$path) {
    $bmp = [System.Drawing.Bitmap]::new($path)
    try {
        $rect = [System.Drawing.Rectangle]::new(0, 0, $bmp.Width, $bmp.Height)
        $d = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        try {
            $stride = $d.Stride
            $bytes = New-Object byte[] ($stride * $bmp.Height)
            [System.Runtime.InteropServices.Marshal]::Copy($d.Scan0, $bytes, 0, $bytes.Length)
            $opaque = 0; $maxY = -1
            for ($y = 0; $y -lt $bmp.Height; $y++) {
                $row = $y * $stride
                for ($x = 0; $x -lt $bmp.Width; $x++) {
                    if ($bytes[$row + $x * 4 + 3] -gt 16) { $opaque++; if ($y -gt $maxY) { $maxY = $y } }
                }
            }
            return [pscustomobject]@{ W = $bmp.Width; H = $bmp.Height; Ratio = $opaque / [double]($bmp.Width * $bmp.Height); MaxY = $maxY }
        }
        finally { $bmp.UnlockBits($d) }
    }
    finally { $bmp.Dispose() }
}

$plan = @()
$errors = @()

foreach ($key in $Expect.Keys) {
    $srcDir = Join-Path $Inbox $key
    if (-not (Test-Path -LiteralPath $srcDir)) {
        $errors += "缺少目录：$srcDir"
        continue
    }
    $rule = $Expect[$key]
    $files = Get-ChildItem -LiteralPath $srcDir -Filter *.png | Sort-Object Name
    if ($files.Count -lt $rule.Min -or $files.Count -gt $rule.Max) {
        $errors += "$key 帧数 $($files.Count) 不在范围 $($rule.Min)~$($rule.Max)"
        continue
    }
    for ($i = 0; $i -lt $files.Count; $i++) {
        if ($files[$i].BaseName -notmatch '^(?<prefix>.+)_(?<idx>\d{3})_(?<ms>\d+)$') {
            $errors += "$key 命名不合规：$($files[$i].Name)"
            break
        }
        if ($Matches.prefix -ne $rule.Prefix) {
            $errors += "$key 前缀不符：$($files[$i].Name)（应为 $($rule.Prefix)_）"
            break
        }
        if ($Matches.idx -ne ('{0:000}' -f $i)) {
            $errors += "$key 序号不连续：$($files[$i].Name)"
            break
        }
    }
    for ($i = 0; $i -lt $files.Count; $i++) {
        $plan += [pscustomobject]@{
            Anim = $key
            Index = $i
            Src = $files[$i].FullName
            Ms = if ($files[$i].BaseName -match '_(\d+)$') { $Matches[1] } else { '100' }
        }
    }
}

if ($errors.Count -gt 0) {
    Write-Host "预检未通过，未做任何改动：" -ForegroundColor Red
    $errors | Select-Object -First 12 | ForEach-Object { Write-Host "  - $_" -ForegroundColor DarkYellow }
    exit 1
}

Write-Host ("预检通过：{0} 个动作、{1} 帧" -f $Expect.Count, $plan.Count) -ForegroundColor Green
if ($WhatIf) {
    $plan | Select-Object -First 8 | ForEach-Object { Write-Host ("  {0,-14} #{1:000}  {2}" -f $_.Anim, $_.Index, $_.Src) }
    Write-Host "  ...（-WhatIf：到此为止，未写入工程）"
    exit 0
}

# 归一化
$fitted = @()
foreach ($p in $plan) {
    $dst = Join-Path $processedRoot ($p.Anim + '\' + ('{0}_{1:000}_{2}.png' -f ($p.Anim -replace '\\', '_'), $p.Index, $p.Ms))
    if (-not $Fit) {
        $dir = Split-Path -Parent $dst
        if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
        Copy-Item -LiteralPath $p.Src -Destination $dst -Force
    }
    elseif (-not (Invoke-Fit $p.Src $dst)) {
        Write-Host "[ERR] 归一化失败：$($p.Src)" -ForegroundColor Red
        exit 1
    }
    $st = Get-AlphaStats $dst
    if ($st.W -ne $Canvas -or $st.H -ne $Canvas) { Write-Host "[ERR] 尺寸异常：$dst" -ForegroundColor Red; exit 1 }
    if ($st.Ratio -lt 0.02 -or $st.Ratio -gt 0.85) { Write-Host "[ERR] 前景占比异常：$dst ($([Math]::Round($st.Ratio*100))%)" -ForegroundColor Red; exit 1 }
    $fitted += [pscustomobject]@{ Anim = $p.Anim; Index = $p.Index; Path = $dst }
}

Write-Host "归一化完成：$processedRoot" -ForegroundColor Green

# 备份旧帧
if (-not $NoBackup -and (Test-Path -LiteralPath $Target)) {
    New-Item -ItemType Directory -Force -Path $backupDir | Out-Null
    Copy-Item -LiteralPath $Target -Destination $backupDir -Recurse -Force
    Write-Host "旧素材已备份：$backupDir" -ForegroundColor DarkGray
}

# 覆盖
$copied = 0
foreach ($f in $fitted) {
    $dstDir = Join-Path $Target $f.Anim
    if (-not (Test-Path -LiteralPath $dstDir)) { New-Item -ItemType Directory -Force -Path $dstDir | Out-Null }
    $dstFile = Join-Path $dstDir (Split-Path $f.Path -Leaf)
    Copy-Item -LiteralPath $f.Path -Destination $dstFile -Force
    $copied++
}

# 清掉目标目录里不在本次导入清单中的旧帧
$keepNames = $fitted | ForEach-Object { (Split-Path $_.Path -Leaf) } | Sort-Object -Unique
foreach ($key in $Expect.Keys) {
    $dir = Join-Path $Target $key
    if (-not (Test-Path -LiteralPath $dir)) { continue }
    Get-ChildItem -LiteralPath $dir -Filter *.png | Where-Object { $keepNames -notcontains $_.Name } | Remove-Item -Force
}

Write-Host ""
Write-Host ("导入完成：{0} 帧 → {1}" -f $copied, $Target) -ForegroundColor Cyan
Write-Host "下一步：重建并清缓存"
Write-Host '  dotnet build <repo>\CatPet\src\CatPet\CatPet.csproj'
Write-Host '  Remove-Item -Recurse -Force <repo>\CatPet\src\CatPet\bin\Debug\net8.0-windows\cache'
