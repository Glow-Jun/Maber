# 构建 exe 安装包：dotnet publish 到 publish\app，再交给 Inno Setup 打包。
# 用法：pwsh installer/build-exe.ps1
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$stage = Join-Path $root "publish\app"
$csproj = Join-Path $root "src\Maber.App\Maber.App.csproj"
$version = ([xml](Get-Content $csproj -Raw)).Project.PropertyGroup.Version
if (-not $version) { throw "未能从 Maber.App.csproj 读取 Version。" }

if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Write-Output "Publishing Maber $version ($Runtime)…"
dotnet publish $csproj `
    -c $Configuration -r $Runtime --self-contained false `
    -p:Version=$version -p:DebugType=None -p:DebugSymbols=false -o $stage
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败（exit $LASTEXITCODE）。" }

$iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $iscc)) { throw "未找到 Inno Setup 6（ISCC.exe）。" }

Write-Output "Compiling installer…"
& $iscc "/DMyAppVersion=$version" (Join-Path $PSScriptRoot "Maber.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup 编译失败（exit $LASTEXITCODE）。" }

Write-Output "Installer ready: $(Join-Path $root "Maber-Setup-$version.exe")"
