<#
.SYNOPSIS
  Tests, builds and packages WdiCheck (unpackaged folder + zip, and/or MSIX).

.EXAMPLE
  ./scripts/build.ps1
  Runs the tests, then produces artifacts/WdiCheck-<version>-win-x64 (+ .zip) and
  artifacts/WdiCheck-<version>-win-x64-unsigned.msix. The version comes from Directory.Build.props.

.EXAMPLE
  ./scripts/build.ps1 -Msix -CertificateThumbprint 0123ABCD...
  Signed MSIX using a certificate from Cert:\CurrentUser\My (see new-dev-cert.ps1).

.EXAMPLE
  ./scripts/build.ps1 -Platform ARM64 -Unpackaged -SkipTests
#>
[CmdletBinding()]
param(
    [ValidateSet('x64', 'ARM64')]
    [string]$Platform = 'x64',
    [string]$Configuration = 'Release',
    [switch]$SkipTests,
    [switch]$Unpackaged,
    [switch]$Msix,
    [string]$CertificateThumbprint
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$app = Join-Path $root 'src\WdiCheck.App\WdiCheck.App.csproj'
$rid = if ($Platform -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
$out = Join-Path $root 'artifacts'
if (-not $Unpackaged -and -not $Msix) { $Unpackaged = $true; $Msix = $true }

function Invoke-Dotnet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args -join ' ') failed with exit code $LASTEXITCODE" }
}

# Single source of truth: <Version> in Directory.Build.props, as MSBuild evaluates it.
$version = (& dotnet msbuild $app -getProperty:Version "-p:Platform=$Platform" -nologo | Select-Object -Last 1).Trim()
if ($LASTEXITCODE -ne 0 -or $version -notmatch '^\d+\.\d+\.\d+') { throw "Could not read the version from MSBuild (got '$version')." }
$name = "WdiCheck-$version-$rid"
Write-Host "Building WdiCheck $version ($rid)"

if (-not $SkipTests) {
    Invoke-Dotnet test (Join-Path $root 'tests\WdiCheck.Core.Tests\WdiCheck.Core.Tests.csproj') -c $Configuration --nologo
}

if ($Unpackaged) {
    $dir = Join-Path $out $name
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
    Invoke-Dotnet publish $app -c $Configuration "-p:Platform=$Platform" -r $rid -p:WindowsPackageType=None -o $dir --nologo
    $zip = "$dir.zip"
    if (Test-Path $zip) { Remove-Item $zip -Force }
    Compress-Archive -Path (Join-Path $dir '*') -DestinationPath $zip
    Write-Host "Unpackaged build: $(Join-Path $dir 'WdiCheck.exe')"
    Write-Host "Zip:              $zip"
}

if ($Msix) {
    # @(...) keeps a one-element result an array; a bare string would splat as characters.
    $signing = @(if ($CertificateThumbprint) {
        '-p:AppxPackageSigningEnabled=true', "-p:PackageCertificateThumbprint=$CertificateThumbprint"
    } else {
        '-p:AppxPackageSigningEnabled=false'
    })
    # MSBuild's own package layout (WdiCheck.App_<version>.0_<arch>_Test\) stays under msix\; the
    # deliverable is copied next to the other artifacts with a versioned name.
    $msixDir = Join-Path $out 'msix\'
    Invoke-Dotnet build $app -c $Configuration "-p:Platform=$Platform" "-p:RuntimeIdentifier=$rid" `
        -p:WindowsPackageType=MSIX -p:GenerateAppxPackageOnBuild=true -p:AppxBundle=Never `
        "-p:AppxPackageDir=$msixDir" @signing --nologo
    $pkg = Get-ChildItem $msixDir -Recurse -Filter "*_$version.0_$($Platform.ToLowerInvariant()).msix" |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $pkg) { throw "MSBuild produced no MSIX for version $version under $msixDir" }
    $suffix = if ($CertificateThumbprint) { '' } else { '-unsigned' }
    $msixFile = Join-Path $out "$name$suffix.msix"   # not $msix: PowerShell names are case-insensitive and -Msix is a switch
    Copy-Item $pkg.FullName $msixFile -Force
    Write-Host "MSIX: $msixFile"
    if (-not $CertificateThumbprint) {
        Write-Host 'The MSIX is unsigned. To install it locally, create a dev certificate with scripts/new-dev-cert.ps1 and rebuild with -CertificateThumbprint.'
    }
}
