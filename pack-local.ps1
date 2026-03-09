<#
.SYNOPSIS
  Pack extractable NuGet packages and copy to local feed.

.DESCRIPTION
  Runs `dotnet pack` on each extractable project and copies the resulting
  .nupkg/.snupkg files to the ./packages/ local feed directory.

.PARAMETER VersionSuffix
  Optional pre-release suffix (e.g., "local.1"). Produces versions like 1.0.0-local.1.

.PARAMETER Configuration
  Build configuration. Default: Release.

.EXAMPLE
  .\pack-local.ps1
  .\pack-local.ps1 -VersionSuffix "local.1"
#>
[CmdletBinding()]
param(
  [string]$VersionSuffix,
  [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$RepoRoot = $PSScriptRoot

# Projects to pack — add new extractable projects here
$PackableProjects = @(
  "src/RTLSDRCore/RTLSDRCore.csproj"
  "src/Radio.AudioAnalysis/Radio.AudioAnalysis.csproj"
  "src/Radio.Metrics/Radio.Metrics.csproj"
  "src/Radio.Configuration/Radio.Configuration.csproj"
  # Future:
  # "src/Radio.Fingerprinting/Radio.Fingerprinting.csproj"
  # "src/Radio.Core/Radio.Core.csproj"
)

$PackagesDir = Join-Path $RepoRoot "packages"
if (-not (Test-Path $PackagesDir)) {
  New-Item -ItemType Directory -Path $PackagesDir | Out-Null
}

$packArgs = @(
  "pack"
  "--configuration", $Configuration
  "--output", $PackagesDir
)

if ($VersionSuffix) {
  $packArgs += "--version-suffix"
  $packArgs += $VersionSuffix
}

$failed = @()

foreach ($project in $PackableProjects) {
  $projectPath = Join-Path $RepoRoot $project
  if (-not (Test-Path $projectPath)) {
    Write-Warning "Project not found: $project — skipping"
    continue
  }

  Write-Host "`nPacking $project ..." -ForegroundColor Cyan
  & dotnet @packArgs $projectPath

  if ($LASTEXITCODE -ne 0) {
    $failed += $project
    Write-Host "  Pack failed for $project" -ForegroundColor Red
  } else {
    Write-Host "  Packed $project" -ForegroundColor Green
  }
}

Write-Host "`n--- Results ---" -ForegroundColor Yellow
$succeeded = $PackableProjects.Count - $failed.Count
Write-Host "Packed: $succeeded / $($PackableProjects.Count)"

if ($failed.Count -gt 0) {
  Write-Host "Failed:" -ForegroundColor Red
  $failed | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
  exit 1
}

Write-Host "`nPackages in ${PackagesDir}:"
Get-ChildItem $PackagesDir -Filter "*.nupkg" | ForEach-Object { Write-Host "  $($_.Name)" }
