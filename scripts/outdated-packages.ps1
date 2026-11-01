<#
.SYNOPSIS
  Lists NuGet packages with a newer stable release, as a Markdown table.

.DESCRIPTION
  Dependabot's NuGet updater treats every package as incompatible with the net10.0-windows target,
  so it never proposes updates here; the weekly "Outdated packages" workflow runs this instead.
  Prints nothing when everything is current. Major-version jumps are marked, since they usually
  need code changes.

.EXAMPLE
  ./scripts/outdated-packages.ps1 -OutFile outdated.md
#>
[CmdletBinding()]
param(
    # Written as UTF-8 without BOM; console output can lose non-ASCII characters on Windows PowerShell 5.1.
    [string]$OutFile
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$solution = Join-Path $root 'WdiCheck.slnx'

# The app project only restores with a platform.
& dotnet restore $solution -p:Platform=x64 --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed ($LASTEXITCODE)." }
$json = & dotnet list $solution package --outdated --format json
if ($LASTEXITCODE -ne 0) { throw "dotnet list package failed ($LASTEXITCODE)." }
$report = ($json -join "`n") | ConvertFrom-Json

$rows = foreach ($project in $report.projects) {
    foreach ($framework in @($project.frameworks)) {
        foreach ($package in @($framework.topLevelPackages)) {
            if (-not $package) { continue }
            [pscustomobject]@{
                Id      = $package.id
                Project = [IO.Path]::GetFileNameWithoutExtension($project.path)
                From    = $package.resolvedVersion
                To      = $package.latestVersion
            }
        }
    }
}

$lines = @($rows | Group-Object Id, From, To | Sort-Object Name | ForEach-Object {
    $r = $_.Group[0]
    $major = ($r.From -split '\.')[0] -ne ($r.To -split '\.')[0]
    $projects = ($_.Group.Project | Sort-Object -Unique) -join ', '
    "| ``$($r.Id)`` | $projects | $($r.From) | $($r.To)$(if ($major) { ' (major)' }) |"
})
if ($lines.Count -eq 0) { return }

$markdown = @(
    '| Package | Project | Installed | Newest |'
    '|---|---|---|---|'
    $lines
) -join "`n"
if ($OutFile) {
    $target = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutFile)  # relative to the PS location
    [IO.File]::WriteAllText($target, $markdown + "`n", (New-Object Text.UTF8Encoding $false))
} else {
    $markdown
}
