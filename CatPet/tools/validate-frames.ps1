#requires -Version 5.1
<#
.SYNOPSIS
    校验桌宠素材（尺寸 / Alpha / 命名 / 序号 / 帧数范围 / 帧时长 / 贴底）。
.DESCRIPTION
    帧数不要求固定，只要落在规范范围内即可；但同一目录内前缀必须一致、序号必须从 000 连续。
    检查项与当前形象的动作集对齐：待机(含眨眼) / 喵叫 A-B-C / 转头 A-B-C。
.EXAMPLE
    # 校验工程内素材（默认 500x500）
    ./validate-frames.ps1 -Dir <repo>\CatPet\src\CatPet\mod\MyCat\pet\cat
.EXAMPLE
    # 校验模型刚出的原始产物（尺寸不固定，跳过尺寸检查）
    ./validate-frames.ps1 -Dir <repo>\artwork\inbox -Canvas 0
#>
param(
    [Parameter(Mandatory = $true)][string]$Dir,
    [int]$Canvas = 500,
    [switch]$Strict
)

$RepoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent   # 仓库根目录（CatPet/tools 往上两级）

Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

$Spec = [ordered]@{
    'idle'   = @{ Min = 8; Max = 32; MsMin = 60; MsMax = 200; Prefix = 'idle' }
    'say\a'  = @{ Min = 2; Max = 8;  MsMin = 60; MsMax = 150; Prefix = 'say_a' }
    'say\b'  = @{ Min = 4; Max = 24; MsMin = 60; MsMax = 200; Prefix = 'say_b' }
    'say\c'  = @{ Min = 2; Max = 8;  MsMin = 60; MsMax = 150; Prefix = 'say_c' }
    'turn\a' = @{ Min = 2; Max = 10; MsMin = 60; MsMax = 180; Prefix = 'turn_a' }
    'turn\b' = @{ Min = 4; Max = 24; MsMin = 60; MsMax = 200; Prefix = 'turn_b' }
    'turn\c' = @{ Min = 2; Max = 10; MsMin = 60; MsMax = 180; Prefix = 'turn_c' }
    'sleep\a' = @{ Min = 3; Max = 10; MsMin = 90; MsMax = 220; Prefix = 'sleep_a' }
    'sleep\b' = @{ Min = 10; Max = 40; MsMin = 100; MsMax = 260; Prefix = 'sleep_b' }
    'sleep\c' = @{ Min = 3; Max = 10; MsMin = 90; MsMax = 220; Prefix = 'sleep_c' }
    'touch_head\a' = @{ Min = 2; Max = 8; MsMin = 60; MsMax = 160; Prefix = 'head_a' }
    'touch_head\b' = @{ Min = 6; Max = 30; MsMin = 80; MsMax = 200; Prefix = 'head_b' }
    'touch_head\c' = @{ Min = 2; Max = 8; MsMin = 60; MsMax = 160; Prefix = 'head_c' }
    'yawn' = @{ Min = 8; Max = 24; MsMin = 60; MsMax = 160; Prefix = 'yawn' }
    'tilt' = @{ Min = 8; Max = 24; MsMin = 60; MsMax = 160; Prefix = 'tilt' }
    'lick\a' = @{ Min = 3; Max = 10; MsMin = 80; MsMax = 180; Prefix = 'lick_a' }
    'lick\b' = @{ Min = 8; Max = 32; MsMin = 60; MsMax = 160; Prefix = 'lick_b' }
    'lick\c' = @{ Min = 3; Max = 10; MsMin = 80; MsMax = 180; Prefix = 'lick_c' }
    'shake' = @{ Min = 10; Max = 28; MsMin = 40; MsMax = 120; Prefix = 'shake' }
    'look_around' = @{ Min = 20; Max = 60; MsMin = 60; MsMax = 120; Prefix = 'look_around' }
    'blink'        = @{ Min = 3; Max = 9; MsMin = 50; MsMax = 200; Prefix = 'blink' }
    'gaze' = @{ Min = 12; Max = 32; MsMin = 80; MsMax = 180; Prefix = 'gaze' }
    'follow' = @{ Min = 125; Max = 125; MsMin = 80; MsMax = 200; Prefix = 'follow' }
    'front\a' = @{ Min = 3; Max = 10; MsMin = 60; MsMax = 160; Prefix = 'front_a' }
    'front\b' = @{ Min = 10; Max = 32; MsMin = 80; MsMax = 180; Prefix = 'front_b' }
    'front\c' = @{ Min = 3; Max = 10; MsMin = 60; MsMax = 160; Prefix = 'front_c' }
    'stand\sidle' = @{ Min = 12; Max = 32; MsMin = 80; MsMax = 160; Prefix = 'sidle' }
    'stand\swalk_l' = @{ Min = 24; Max = 40; MsMin = 18; MsMax = 140; Prefix = 'swalk_l' }
    'stand\swalk_r' = @{ Min = 24; Max = 40; MsMin = 18; MsMax = 140; Prefix = 'swalk_r' }
    'stand\swag_r' = @{ Min = 8; Max = 30; MsMin = 60; MsMax = 140; Prefix = 'swag_r' }
    'stand\sjump'  = @{ Min = 24; Max = 40; MsMin = 20; MsMax = 340; Prefix = 'sjump'; Airborne = $true }
    'stand\sjump_r' = @{ Min = 24; Max = 40; MsMin = 20; MsMax = 340; Prefix = 'sjump_r'; Airborne = $true }
    'stand\sjump_diag'   = @{ Min = 24; Max = 40; MsMin = 12; MsMax = 240; Prefix = 'sjump_diag'; Airborne = $true }
    'stand\sjump_diag_r' = @{ Min = 24; Max = 40; MsMin = 12; MsMax = 240; Prefix = 'sjump_diag_r'; Airborne = $true }
    'stand\swag' = @{ Min = 10; Max = 28; MsMin = 60; MsMax = 160; Prefix = 'swag' }
    'stand\sidle_r' = @{ Min = 12; Max = 30; MsMin = 80; MsMax = 160; Prefix = 'sidle_r' }
    'stand\sfollow_r' = @{ Min = 125; Max = 125; MsMin = 80; MsMax = 200; Prefix = 'sfollow_r' }
    'stand\sfollow' = @{ Min = 125; Max = 125; MsMin = 80; MsMax = 200; Prefix = 'sfollow' }
    'stand\sgal_r'  = @{ Min = 20; Max = 40; MsMin = 10; MsMax = 120; Prefix = 'sgal_r'; Airborne = $true }
    'stand\sgal_l'  = @{ Min = 20; Max = 40; MsMin = 10; MsMax = 120; Prefix = 'sgal_l'; Airborne = $true }
    'stand\srun_r'  = @{ Min = 20; Max = 40; MsMin = 10; MsMax = 120; Prefix = 'srun_r' }
    'stand\srun_l'  = @{ Min = 20; Max = 40; MsMin = 10; MsMax = 120; Prefix = 'srun_l' }
    'stand\sclose_r' = @{ Min = 6; Max = 16; MsMin = 80; MsMax = 200; Prefix = 'sclose_r' }
    'stand\sclose' = @{ Min = 6; Max = 16; MsMin = 80; MsMax = 200; Prefix = 'sclose' }
    'stand\slook_r' = @{ Min = 20; Max = 60; MsMin = 60; MsMax = 120; Prefix = 'slook_r' }
    'stand\slook' = @{ Min = 20; Max = 60; MsMin = 60; MsMax = 120; Prefix = 'slook' }
}

$fail = 0
$warn = 0
$ok = 0

function Get-AlphaProfile([System.Drawing.Bitmap]$bmp) {
    $rect = [System.Drawing.Rectangle]::new(0, 0, $bmp.Width, $bmp.Height)
    $d = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    try {
        $stride = $d.Stride
        $bytes = New-Object byte[] ($stride * $bmp.Height)
        [System.Runtime.InteropServices.Marshal]::Copy($d.Scan0, $bytes, 0, $bytes.Length)

        $total = 0
        $opaque = 0
        $maxY = -1
        for ($y = 0; $y -lt $bmp.Height; $y++) {
            $row = $y * $stride
            for ($x = 0; $x -lt $bmp.Width; $x++) {
                $total++
                if ($bytes[$row + $x * 4 + 3] -gt 16) {
                    $opaque++
                    if ($y -gt $maxY) { $maxY = $y }
                }
            }
        }
        return [pscustomobject]@{ Ratio = $(if ($total -gt 0) { $opaque / $total } else { 0 }); MaxY = $maxY }
    }
    finally { $bmp.UnlockBits($d) }
}

Write-Host "校验目录：$Dir"
if ($Canvas -gt 0) { Write-Host "画布要求：${Canvas}x${Canvas}" } else { Write-Host "画布要求：不检查" }
Write-Host ""

foreach ($key in $Spec.Keys) {
    $rule = $Spec[$key]
    $path = Join-Path $Dir $key
    $label = $key.PadRight(14)

    if (-not (Test-Path -LiteralPath $path)) {
        Write-Host ("[FAIL] {0} 目录不存在（要求 {1}~{2} 帧）" -f $label, $rule.Min, $rule.Max) -ForegroundColor Red
        $fail++
        continue
    }

    $files = Get-ChildItem -LiteralPath $path -Filter *.png | Sort-Object Name
    $issues = @()
    $notes = @()

    if ($files.Count -lt $rule.Min -or $files.Count -gt $rule.Max) {
        $issues += "帧数 $($files.Count) 不在要求范围 $($rule.Min)~$($rule.Max)"
    }

    $msValues = @{}
    for ($i = 0; $i -lt $files.Count; $i++) {
        $n = $files[$i].BaseName
        if ($n -notmatch '^(?<prefix>.+)_(?<idx>\d{3})_(?<ms>\d+)$') {
            $issues += "命名不合规：$($files[$i].Name)"
            continue
        }
        if ($Matches.idx -ne ('{0:000}' -f $i)) {
            $issues += "序号不连续：$($files[$i].Name)（第 $i 个应为 $($i.ToString('000'))）"
        }
        if ($Matches.prefix -ne $rule.Prefix) {
            $issues += "前缀不符：$($files[$i].Name)（应为 $($rule.Prefix)_）"
        }
        $ms = [int]$Matches.ms
        $msValues[$ms] = 1
        if ($ms -lt $rule.MsMin -or $ms -gt $rule.MsMax) {
            $issues += "帧时长 $ms ms 超出范围 $($rule.MsMin)~$($rule.MsMax)：$($files[$i].Name)"
        }
    }

    if ($msValues.Count -gt 3) {
        $notes += "帧时长有 $($msValues.Count) 种取值（建议同一动作保持 1~3 种）"
    }

    $prof = $null
    foreach ($f in $files) {
        $bmp = [System.Drawing.Bitmap]::new($f.FullName)
        try {
            # 素材尺寸：默认窗口 275px 的 1.2 倍 = 330；运镜帧是 250。允许 250~500 的正方形
            if ($Canvas -gt 0 -and ($bmp.Width -ne $Canvas -or $bmp.Height -ne $Canvas)) {
                if ($bmp.Width -lt 250 -or $bmp.Width -gt 500) {
                    $issues += "尺寸超出范围（250~500）：$($f.Name) = $($bmp.Width)x$($bmp.Height)"
                }
            }
            if ($bmp.Width -ne $bmp.Height) {
                $issues += "不是正方形：$($f.Name)"
            }
            if ($bmp.PixelFormat -notmatch 'Argb') {
                $issues += "缺少 Alpha 通道：$($f.Name) ($($bmp.PixelFormat))"
            }
            $p = Get-AlphaProfile $bmp
            if ($p.Ratio -lt 0.02 -or $p.Ratio -gt 0.9) {
                $issues += "前景占比异常：$($f.Name) = $([Math]::Round($p.Ratio * 100))%"
            }
            # 腾空类动作（飞奔）允许有离地帧
            $airborne = $rule.ContainsKey('Airborne') -and $rule['Airborne']
            if (-not $airborne -and $p.MaxY -lt ($bmp.Height * 0.88)) {
                $issues += "未贴底：$($f.Name) 最低像素 y=$($p.MaxY)/$($bmp.Height)"
            }
            if ($null -eq $prof) { $prof = $p }
        }
        finally { $bmp.Dispose() }
        if ($issues.Count -gt 6) { break }
    }

    if ($issues.Count -eq 0) {
        $tail = 0
        if ($null -ne $prof) { $tail = [Math]::Round((1 - $prof.Ratio) * 100) }
        Write-Host ("[OK]   {0} {1,3} 帧（{2}~{3}）  透明 {4}%" -f $label, $files.Count, $rule.Min, $rule.Max, $tail) -ForegroundColor Green
        foreach ($nt in $notes) {
            Write-Host "         · $nt" -ForegroundColor DarkGray
            $warn++
        }
        $ok++
    }
    else {
        Write-Host ("[FAIL] {0} {1,3} 帧" -f $label, $files.Count) -ForegroundColor Red
        foreach ($it in ($issues | Select-Object -First 5)) {
            Write-Host "         - $it" -ForegroundColor DarkYellow
        }
        $fail++
    }
}

foreach ($d in Get-ChildItem -LiteralPath $Dir -Recurse -Directory) {
    $rel = $d.FullName.Substring((Resolve-Path $Dir).Path.Length).TrimStart('\')
    if ($Spec.Keys -notcontains $rel) {
        $n = (Get-ChildItem -LiteralPath $d.FullName -Filter *.png -ErrorAction SilentlyContinue).Count
        if ($n -gt 0) {
            Write-Host "[WARN] 目录 $rel 不在规范清单内（$n 帧），将被忽略" -ForegroundColor Yellow
            $warn++
        }
    }
}

Write-Host ""
Write-Host ("结果：{0} 通过 / {1} 失败 / {2} 警告" -f $ok, $fail, $warn) -ForegroundColor Cyan
if ($fail -gt 0 -or ($Strict -and $warn -gt 0)) { exit 1 }
exit 0
