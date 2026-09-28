<#
.SYNOPSIS
  Prints the CHANGELOG.md section for one version (used as GitHub release notes).

.EXAMPLE
  ./scripts/release-notes.ps1 -Version 0.2.1 -OutFile notes.md
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Version,
    [string]$Path,
    # Written as UTF-8 without BOM; console output can lose non-ASCII characters on Windows PowerShell 5.1.
    [string]$OutFile
)
$ErrorActionPreference = 'Stop'
# $PSScriptRoot is not populated inside param() defaults on Windows PowerShell 5.1.
if (-not $Path) { $Path = Join-Path (Split-Path -Parent $PSScriptRoot) 'CHANGELOG.md' }
$lines = Get-Content -Path $Path -Encoding utf8
$header = "## [$Version]"
$start = [Array]::FindIndex($lines, [Predicate[string]] { param($l) $l.StartsWith($header) })
if ($start -lt 0) { throw "CHANGELOG.md has no '$header' section. Move the Unreleased notes under it before tagging." }

$body = New-Object System.Collections.Generic.List[string]
for ($i = $start + 1; $i -lt $lines.Count; $i++) {
    # Stop at the next version section or the link references at the bottom.
    if ($lines[$i].StartsWith('## [') -or $lines[$i] -match '^\[[^\]]+\]:\s') { break }
    $body.Add($lines[$i])
}
$notes = ($body -join "`n").Trim()
if ($OutFile) {
    $target = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutFile)  # relative to the PS location
    [IO.File]::WriteAllText($target, $notes + "`n", (New-Object Text.UTF8Encoding $false))
} else {
    $notes
}
