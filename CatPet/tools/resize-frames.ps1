# 把已有素材帧批量缩放（默认 500×500），文件名保持不变。
#   .\resize-frames.ps1                      # 处理 src\CatPet\mod\MyCat\pet\cat
#   .\resize-frames.ps1 -Size 600
#   .\resize-frames.ps1 -Dir <目录> -Size 700

param(
    [string]$Dir = (Join-Path $PSScriptRoot '..\src\CatPet\mod\MyCat\pet\cat'),
    [int]$Size = 500
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$Dir = (Resolve-Path $Dir).Path
Write-Host "目录: $Dir" -ForegroundColor Cyan
Write-Host "目标尺寸: ${Size}x${Size}" -ForegroundColor Cyan

$before = 0
$after = 0
$count = 0
$skipped = 0

Get-ChildItem $Dir -Recurse -Filter *.png | ForEach-Object {
    $file = $_
    if ($file.Name -like '_sheet*') { $skipped++; return }

    $img = [System.Drawing.Image]::FromFile($file.FullName)
    try {
        if ($img.Width -eq $Size -and $img.Height -eq $Size) {
            $skipped++
            return
        }

        $before += $file.Length
        $bmp = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
        $g.Clear([System.Drawing.Color]::Transparent)
        $g.DrawImage($img, (New-Object System.Drawing.Rectangle 0, 0, $Size, $Size))
        $g.Dispose()
        $img.Dispose()

        $tmp = $file.FullName + '.tmp.png'
        $bmp.Save($tmp, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        Move-Item -Force $tmp $file.FullName
        $after += (Get-Item $file.FullName).Length
        $count++
    }
    finally {
        if ($img) { $img.Dispose() }
    }
}

$mb = { param($b) [math]::Round($b / 1MB, 1) }
Write-Host ("缩放 {0} 张，跳过 {1} 张" -f $count, $skipped) -ForegroundColor Green
Write-Host ("体积 {0} MB -> {1} MB" -f (& $mb $before), (& $mb $after)) -ForegroundColor Green
