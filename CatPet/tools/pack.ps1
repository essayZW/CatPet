# 打包脚本：默认只产出安装包
#   dist\CatPet-Setup-<版本>.exe
#
# 用法：
#   .\pack.ps1                 # 只打安装包（默认）
#   .\pack.ps1 -Version 1.0.1  # 指定版本号
#   .\pack.ps1 -KeepStaging    # 保留 dist\_publish 中间目录（排查用）
#   .\pack.ps1 -WithZip        # 额外产出绿色版 zip
#   .\pack.ps1 -WithSingle     # 额外产出单文件版
#
# 说明：只做打包，不启动程序。

param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$Version = '',
    [string]$DotnetRoot = '',
    [string]$Iscc = '',
    [switch]$KeepStaging,
    [switch]$WithZip,
    [switch]$WithSingle,
    [switch]$SkipTagCheck              # 应急：跳过"当前提交必须打 tag"的校验
)

$RepoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent   # 仓库根目录（CatPet/tools 往上两级）

if (-not $DotnetRoot) { $DotnetRoot = Join-Path $RepoRoot '.tools\dotnet' }


$ErrorActionPreference = 'Stop'

# 版本号：命令行 -Version 优先，否则从 csproj 的 <Version> 读
if (-not $Version) {
    $csproj = Join-Path $PSScriptRoot '..\src\CatPet\CatPet.csproj'
    $m = Select-String -Path $csproj -Pattern '<Version>([^<]+)</Version>' | Select-Object -First 1
    $Version = if ($m) { $m.Matches[0].Groups[1].Value } else { '1.0.0' }
}
Write-Host "版本号: $Version" -ForegroundColor Cyan

# 打包只认"已经打过 tag 的提交"：tag 名形如 v1.3.1，必须和 csproj 版本一致
if (-not $SkipTagCheck) {
    $wantTag = "v$Version"
    $tagsHere = @(git -C $RepoRoot tag --points-at HEAD 2>$null)
    if ($tagsHere -notcontains $wantTag) {
        $have = if ($tagsHere.Count -gt 0) { $tagsHere -join ', ' } else { '（无）' }
        Write-Host "[ERR] 当前提交没有打 tag $wantTag，实际是：$have" -ForegroundColor Red
        Write-Host '      流程：改 csproj 版本 -> 提交 -> git tag -a v<版本> -m "..." -> 再打包' -ForegroundColor Yellow
        Write-Host '      应急可加 -SkipTagCheck' -ForegroundColor Yellow
        exit 1
    }
    Write-Host "tag 校验通过: $wantTag" -ForegroundColor Green
}

# 脚本位于 <仓库>\CatPet\tools\，仓库根目录往上两级
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$proj = Join-Path $root 'CatPet\src\CatPet\CatPet.csproj'
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist '_publish'

$env:PATH = "$DotnetRoot;$env:PATH"
$env:DOTNET_ROOT = $DotnetRoot
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'

function Step($text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }

# 0) Inno Setup 编译器（安装包必需）
if (-not $Iscc) {
    $Iscc = @(
        'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
        'C:\Program Files\Inno Setup 6\ISCC.exe',
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe')
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $Iscc -or -not (Test-Path $Iscc)) {
    Write-Host '[ERR] 找不到 Inno Setup 6 的 ISCC.exe' -ForegroundColor Red
    Write-Host '      安装：winget install JRSoftware.InnoSetup' -ForegroundColor Yellow
    Write-Host '      或用 -Iscc 指定路径' -ForegroundColor Yellow
    exit 1
}

# 1) 重建图标（换素材后 exe / 安装包图标跟着更新）
Step "生成 app.ico"
& (Get-Process -Id $PID).Path -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'make-icon.ps1')

# 2) 自包含文件夹（安装包的内容来源；目标机无需装 .NET）
Step "发布 $Version（自包含 $Runtime）"
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
dotnet publish $proj -c $Configuration -r $Runtime --self-contained true -o $stage 2>&1 | Select-Object -Last 2
$size = [math]::Round(((Get-ChildItem $stage -Recurse -File | Measure-Object Length -Sum).Sum) / 1MB, 1)
Write-Host "   内容 $size MB"

# 3) 编译安装包
Step '编译安装包'
$iss = Join-Path $PSScriptRoot 'installer\CatPet.iss'
$before = @(Get-ChildItem $dist -Filter 'CatPet-Setup-*.exe' -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })
& $Iscc "/DMySourceDir=$stage" "/DMyVersion=$Version" "/DMyOutputDir=$dist" $iss | Select-Object -Last 3

$setup = Join-Path $dist "CatPet-Setup-$Version.exe"
if (-not (Test-Path $setup)) {
    Write-Host '[ERR] 安装包没有生成，看上面的 ISCC 输出' -ForegroundColor Red
    exit 1
}
Write-Host "   -> $setup  ($([math]::Round((Get-Item $setup).Length / 1MB, 1)) MB)" -ForegroundColor Green

# 4) 可选产物
if ($WithSingle) {
    Step '发布单文件版'
    $single = Join-Path $dist 'CatPet-单文件'
    if (Test-Path $single) { Remove-Item $single -Recurse -Force }
    dotnet publish $proj -c $Configuration -r $Runtime --self-contained true `
        -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true `
        -p:DebugType=None -p:DebugSymbols=false -o $single 2>&1 | Select-Object -Last 2
    Write-Host "   -> $single  (exe $([math]::Round((Get-Item (Join-Path $single 'CatPet.exe')).Length / 1MB, 1)) MB + mod)"
}

if ($WithZip) {
    Step '打包绿色版 zip'
    $zip = Join-Path $dist 'CatPet-绿色版.zip'
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
    Write-Host "   -> $zip  ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
}

# 5) 清理中间目录（安装包已内嵌全部内容）
if (-not $KeepStaging) { Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue }

Write-Host "`n完成：$setup" -ForegroundColor Green
