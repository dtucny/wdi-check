<#
.SYNOPSIS
  Lists dependency changes that ship to users since the last release, as CHANGELOG lines.

.DESCRIPTION
  Compares the working tree with a release tag: the .NET SDK in global.json (the app is
  self-contained, so the SDK sets the .NET runtime bundled with it) and the NuGet packages of the
  projects under src/. Test projects, build-only packages (PrivateAssets="all", such as CsWin32)
  and the Windows SDK build tools are left out: they don't change what users get. It doesn't
  matter whether a change came from Dependabot, the outdated-packages issue or a manual edit.

  With -Check, it fails when the CHANGELOG section for -Version mentions neither a changed
  package's name nor its new version (for the SDK: its new version); the release workflow runs that.

.EXAMPLE
  ./scripts/dependency-changes.ps1                      # since the last tag, to paste under Unreleased
.EXAMPLE
  ./scripts/dependency-changes.ps1 -Check -Version 0.2.8
#>
[CmdletBinding()]
param(
    # Defaults to the newest tag before HEAD, so it works both before tagging and on the tagged commit.
    [string]$Since,
    [switch]$Check,
    [string]$Version
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ($Check -and -not $Version) { throw '-Check needs -Version.' }
if (-not $Since) {
    $Since = (& git -C $root describe --tags --abbrev=0 'HEAD~1' 2>$null)
    if (-not $Since) { throw 'No earlier release tag found; pass -Since.' }
}

# Shipped NuGet packages: id → version, from csproj contents.
function Get-Packages([string[]]$Contents) {
    $packages = @{}
    foreach ($content in $Contents) {
        foreach ($ref in ([xml]$content).SelectNodes('//PackageReference[@Include]')) {
            $privateAssets = if ($ref.PrivateAssets -is [string]) { $ref.PrivateAssets } else { $ref.GetAttribute('PrivateAssets') }
            $id = $ref.GetAttribute('Include')
            if ($privateAssets -eq 'all' -or $id -like '*.BuildTools') { continue }
            $packages[$id] = if ($ref.GetAttribute('Version')) { $ref.GetAttribute('Version') } else { "$($ref.Version)" }
        }
    }
    $packages
}

function Get-SdkVersion([string]$Json) { ($Json | ConvertFrom-Json).sdk.version }

$oldProjects = & git -C $root ls-tree -r --name-only $Since -- src | Where-Object { $_ -like '*.csproj' }
$old = Get-Packages @($oldProjects | ForEach-Object { (& git -C $root show "${Since}:$_") -join "`n" })
$new = Get-Packages @(Get-ChildItem (Join-Path $root 'src') -Recurse -Filter *.csproj |
    Where-Object FullName -notmatch '[\\/](bin|obj)[\\/]' | ForEach-Object { Get-Content $_.FullName -Raw })
$oldSdk = Get-SdkVersion ((& git -C $root show "${Since}:global.json") -join "`n")
$newSdk = Get-SdkVersion (Get-Content (Join-Path $root 'global.json') -Raw)

# Each change: the CHANGELOG line, and texts of which the release's section must contain one.
$changes = @()
if ($oldSdk -ne $newSdk) {
    $changes += [pscustomobject]@{ Line = "- .NET SDK $oldSdk → $newSdk (sets the .NET runtime bundled with the app)"; Mention = @($newSdk) }
}
foreach ($id in ($old.Keys + $new.Keys | Sort-Object -Unique)) {
    if (-not $new.ContainsKey($id)) {
        $changes += [pscustomobject]@{ Line = "- Removed ``$id`` $($old[$id])"; Mention = @($id) }
    } elseif (-not $old.ContainsKey($id)) {
        $changes += [pscustomobject]@{ Line = "- Added ``$id`` $($new[$id])"; Mention = @($id, $new[$id]) }
    } elseif ($old[$id] -ne $new[$id]) {
        $changes += [pscustomobject]@{ Line = "- ``$id`` $($old[$id]) → $($new[$id])"; Mention = @($id, $new[$id]) }
    }
}

if (-not $Check) {
    if ($changes.Count -gt 0) { "Dependency changes since ${Since}:"; ''; $changes.Line }
    return
}

$notes = & (Join-Path $PSScriptRoot 'release-notes.ps1') -Version $Version
$notes = $notes -join "`n"
$missing = @($changes | Where-Object { $m = $_.Mention; -not ($m | Where-Object { $notes.Contains($_) }) })
if ($missing.Count -gt 0) {
    throw ("CHANGELOG.md's [$Version] section doesn't mention these dependency changes since ${Since}:`n" +
        ($missing.Line -join "`n") + "`nAdd them (./scripts/dependency-changes.ps1 prints the lines) and tag again.")
}
"CHANGELOG.md's [$Version] section covers the $($changes.Count) dependency change(s) since $Since."
