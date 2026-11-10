# 生成仅用于本地测试的 MSIX 自签名证书。
# 生成后需要把证书导入“受信任的根证书颁发机构”，否则 Windows 会拒绝安装。
param(
    [string]$Subject = "CN=Maber",
    [string]$OutputPath = (Join-Path $PSScriptRoot "MaberDev.pfx"),
    [string]$Password = "MaberDev",
    [int]$Years = 5
)

$ErrorActionPreference = "Stop"
$cert = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $Subject `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -KeyExportPolicy Exportable `
    -NotAfter (Get-Date).AddYears($Years)

$secure = ConvertTo-SecureString -String $Password -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath $OutputPath -Password $secure | Out-Null
$cerPath = [System.IO.Path]::ChangeExtension($OutputPath, ".cer")
Export-Certificate -Cert $cert -FilePath $cerPath | Out-Null
Write-Output "PFX: $OutputPath"
Write-Output "Public cert: $cerPath"
Write-Output "Thumbprint: $($cert.Thumbprint)"
Write-Output "信任证书（当前用户）：certutil -addstore Root `"$cerPath`""
