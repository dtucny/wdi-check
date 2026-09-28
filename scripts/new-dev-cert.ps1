<#
.SYNOPSIS
  Creates (or reuses) a self-signed code-signing certificate for local MSIX installs.

.DESCRIPTION
  The certificate subject must match the Publisher in src/WdiCheck.App/Package.appxmanifest
  ("CN=WdiCheck Dev"). It is created in Cert:\CurrentUser\My so MSBuild can sign with it.

  Windows only installs packages whose signer it trusts. -Trust imports the public certificate
  into Cert:\LocalMachine\TrustedPeople, which needs an elevated prompt. Only do this on a
  development machine, and remove it when you no longer need it (-Untrust).

.EXAMPLE
  ./scripts/new-dev-cert.ps1            # create/reuse, print thumbprint
  ./scripts/new-dev-cert.ps1 -Trust     # (elevated) also trust it for MSIX installs
  ./scripts/build.ps1 -Msix -CertificateThumbprint <thumbprint>
  Add-AppxPackage artifacts\msix\WdiCheck.App_0.1.0.0_x64_Test\WdiCheck.App_0.1.0.0_x64.msix
#>
[CmdletBinding()]
param(
    [string]$Subject = 'CN=WdiCheck Dev',
    [switch]$Trust,
    [switch]$Untrust
)
$ErrorActionPreference = 'Stop'

$cert = Get-ChildItem Cert:\CurrentUser\My |
    Where-Object { $_.Subject -eq $Subject -and $_.NotAfter -gt (Get-Date) -and $_.HasPrivateKey } |
    Sort-Object NotAfter -Descending | Select-Object -First 1

if ($Untrust) {
    if ($cert) {
        Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Thumbprint -eq $cert.Thumbprint | Remove-Item
        Write-Host "Removed $($cert.Thumbprint) from LocalMachine\TrustedPeople."
    }
    return
}

if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type Custom -Subject $Subject -KeyUsage DigitalSignature `
        -FriendlyName 'WdiCheck development signing' -CertStoreLocation Cert:\CurrentUser\My `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}') -NotAfter (Get-Date).AddYears(2)
    Write-Host "Created certificate $($cert.Thumbprint)"
} else {
    Write-Host "Reusing certificate $($cert.Thumbprint)"
}

if ($Trust) {
    $isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator)
    if (-not $isAdmin) { throw '-Trust needs an elevated PowerShell (it writes to Cert:\LocalMachine\TrustedPeople).' }
    $cer = Join-Path ([IO.Path]::GetTempPath()) 'WdiCheckDev.cer'
    Export-Certificate -Cert $cert -FilePath $cer | Out-Null
    Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
    Remove-Item $cer
    Write-Host 'Trusted for MSIX installs on this machine (LocalMachine\TrustedPeople).'
}

$cert.Thumbprint
