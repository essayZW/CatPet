# 跑纯逻辑单测（不需要启动桌宠、不需要素材）
#   .\test.ps1             # 全部
#   .\test.ps1 -Filter 贴边 # 只跑名字含"贴边"的用例
param(
    [string]$Filter = '',
    [string]$DotnetRoot = ''
)

$RepoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent   # 仓库根目录（CatPet/tools 往上两级）

if (-not $DotnetRoot) { $DotnetRoot = Join-Path $RepoRoot '.tools\dotnet' }
if (Test-Path $DotnetRoot) {
    $env:PATH = "$DotnetRoot;$env:PATH"
    $env:DOTNET_ROOT = $DotnetRoot
}

$proj = Join-Path $RepoRoot 'CatPet\tests\CatPet.Tests\CatPet.Tests.csproj'
$args = @('test', $proj, '--nologo', '--verbosity', 'quiet')
if ($Filter) { $args += @('--filter', "DisplayName~$Filter") }

dotnet @args
exit $LASTEXITCODE