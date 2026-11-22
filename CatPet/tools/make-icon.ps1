# 从形象帧生成 app.ico（多尺寸），供 exe 图标 / 安装包使用
param(
    [string]$Frame = 'D:\code\my_pet\CatPet\src\CatPet\mod\MyCat\pet\cat\idle\idle_000_100.png',
    [string]$Out = 'D:\code\my_pet\CatPet\src\CatPet\app.ico'
)

Add-Type -AssemblyName System.Drawing

$src = [System.Drawing.Bitmap]::new($Frame)
$size = [math]::Min($src.Width, $src.Height)
$crop = [System.Drawing.Rectangle]::new([int]($size * 0.30), [int]($size * 0.09), [int]($size * 0.40), [int]($size * 0.40))

$sizes = @(16, 24, 32, 48, 64, 128, 256)
$entries = @()

foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = 'HighQualityBicubic'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($src, (New-Object System.Drawing.Rectangle 0, 0, $s, $s), $crop, [System.Drawing.GraphicsUnit]::Pixel)
    $g.Dispose()

    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $entries += [pscustomobject]@{ Size = $s; Data = $ms.ToArray() }
    $bmp.Dispose()
}
$src.Dispose()

$fs = [System.IO.File]::Create($Out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([int16]0)
$bw.Write([int16]1)
$bw.Write([int16]$entries.Count)

$offset = 6 + 16 * $entries.Count
foreach ($e in $entries) {
    $dim = 0
    if ($e.Size -lt 256) { $dim = $e.Size }
    $bw.Write([byte]$dim)
    $bw.Write([byte]$dim)
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([int16]1)
    $bw.Write([int16]32)
    $bw.Write([int]$e.Data.Length)
    $bw.Write([int]$offset)
    $offset += $e.Data.Length
}
foreach ($e in $entries) { $bw.Write($e.Data) }
$bw.Flush()
$bw.Close()
$fs.Close()

# 验证：能被 System.Drawing 正常读回
$ico = [System.Drawing.Icon]::new($Out)
"OK: $Out  ({0} KB, {1}x{2})" -f [math]::Round((Get-Item $Out).Length / 1KB, 1), $ico.Width, $ico.Height
$ico.Dispose()
