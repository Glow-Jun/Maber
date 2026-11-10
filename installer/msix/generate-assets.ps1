# 从应用图标生成 MSIX 所需的 PNG 资源。
# 仅在图标变化时需要重新运行：pwsh installer/msix/generate-assets.ps1
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$appAssets = Join-Path $root "..\..\src\Maber.App\Assets"
$out = Join-Path $root "Assets"
New-Item -ItemType Directory -Force -Path $out | Out-Null

function Save-Resized {
    param(
        [System.Drawing.Bitmap]$Source,
        [int]$Width,
        [int]$Height,
        [string]$Path,
        [System.Drawing.Color]$Background
    )
    $bmp = New-Object System.Drawing.Bitmap($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear($Background)
    $scale = [Math]::Min($Width / $Source.Width, $Height / $Source.Height)
    $dw = [int]($Source.Width * $scale)
    $dh = [int]($Source.Height * $scale)
    $x = [int](($Width - $dw) / 2)
    $y = [int](($Height - $dh) / 2)
    $g.DrawImage($Source, $x, $y, $dw, $dh)
    $g.Dispose()
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

$appIcon = New-Object System.Drawing.Icon((Join-Path $appAssets "Maber.ico"), 256, 256)
$docIcon = New-Object System.Drawing.Icon((Join-Path $appAssets "MaberMarkdown.ico"), 256, 256)
$app = $appIcon.ToBitmap()
$doc = $docIcon.ToBitmap()
$paper = [System.Drawing.Color]::FromArgb(255, 253, 253, 252)
$transparent = [System.Drawing.Color]::Transparent

Save-Resized $app 44 44 (Join-Path $out "Square44x44Logo.png") $transparent
Save-Resized $app 150 150 (Join-Path $out "Square150x150Logo.png") $transparent
Save-Resized $app 310 310 (Join-Path $out "Square310x310Logo.png") $transparent
Save-Resized $app 71 71 (Join-Path $out "SmallTile.png") $transparent
Save-Resized $app 310 310 (Join-Path $out "LargeTile.png") $transparent
Save-Resized $app 50 50 (Join-Path $out "StoreLogo.png") $transparent
Save-Resized $app 24 24 (Join-Path $out "LockScreenLogo.png") $transparent
Save-Resized $app 310 150 (Join-Path $out "Wide310x150Logo.png") $paper
Save-Resized $app 620 300 (Join-Path $out "SplashScreen.png") $paper
Save-Resized $doc 44 44 (Join-Path $out "MaberMarkdown.png") $transparent

$app.Dispose()
$doc.Dispose()
Write-Output "MSIX assets written to $out"
