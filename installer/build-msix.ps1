# 构建 MSIX 包：发布应用 → 合并清单与图标 → makeappx 打包（可选签名）。
# 用法：
#   pwsh installer/build-msix.ps1
#   pwsh installer/build-msix.ps1 -SelfContained:$false
#   pwsh installer/build-msix.ps1 -Sign -CertificatePath installer/msix/MaberDev.pfx -CertificatePassword MaberDev
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [bool]$SelfContained = $true,
    [switch]$Sign,
    [string]$CertificatePath = "",
    [string]$CertificatePassword = ""
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$manifestPath = Join-Path $PSScriptRoot "msix\AppxManifest.xml"
$manifest = [xml](Get-Content $manifestPath -Raw)
$packageVersion = $manifest.Package.Identity.Version
$productVersion = $packageVersion.Split(".")[0..2] -join "."
$stage = Join-Path $root "publish\msix"
$output = Join-Path $root "Maber-v$productVersion.msix"

if (Test-Path $stage) { Remove-Item -Recurse -Force $stage }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Write-Output "Publishing Maber $productVersion ($Runtime, self-contained=$SelfContained)…"
dotnet publish (Join-Path $root "src\Maber.App\Maber.App.csproj") `
    -c $Configuration -r $Runtime --self-contained $SelfContained `
    -p:Version=$productVersion -p:DebugType=None -p:DebugSymbols=false -o $stage

Copy-Item $manifestPath (Join-Path $stage "AppxManifest.xml")
# publish 输出里已经有应用自身的 Assets 目录，必须复制内容而不是复制整个目录
New-Item -ItemType Directory -Force -Path (Join-Path $stage "Assets") | Out-Null
Copy-Item -Recurse -Force (Join-Path $PSScriptRoot "msix\Assets\*") (Join-Path $stage "Assets")

$makeappx = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter makeappx.exe |
    Where-Object { $_.FullName -match '\\x64\\' } |
    Sort-Object FullName -Descending |
    Select-Object -First 1
if (-not $makeappx) { throw "未找到 makeappx.exe，请安装 Windows SDK。" }

Write-Output "Packing $output…"
& $makeappx.FullName pack /d $stage /p $output /o
if ($LASTEXITCODE -ne 0) { throw "makeappx 打包失败（exit $LASTEXITCODE）。" }

if ($Sign) {
    if (-not $CertificatePath -or -not (Test-Path $CertificatePath)) {
        throw "签名需要 -CertificatePath（可用 installer/msix/make-cert.ps1 生成开发证书）。"
    }
    $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe |
        Where-Object { $_.FullName -match '\\x64\\' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if (-not $signtool) { throw "未找到 signtool.exe，请安装 Windows SDK。" }
    & $signtool.FullName sign /fd SHA256 /a /f $CertificatePath /p $CertificatePassword $output
    if ($LASTEXITCODE -ne 0) { throw "签名失败（exit $LASTEXITCODE）。" }
}

Write-Output "MSIX ready: $output"
