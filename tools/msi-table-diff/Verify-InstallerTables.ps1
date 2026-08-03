<#
.SYNOPSIS
    CI entry point for the installer table comparison.

.DESCRIPTION
    Wraps Compare-MsiTables.ps1 so both pipelines run the same checks rather
    than each carrying its own copy of the logic. Two distinct things happen:

      1. A self-test, on every build, needing nothing external. Comparing a
         package against itself must report zero drift, and comparing it
         against an unrelated package must report drift. A comparison that
         cannot fail is not a test, and this is the only way to know the
         harness still works before trusting what it says about a real change.

      2. The G-1 invariant, when -BaselinePath names the single-package
         baseline. The union of every package produced by this build must
         install exactly what the baseline installed.

    Without -BaselinePath the second check does not run, and the script says so
    rather than exiting quietly green.

.PARAMETER PackageFolder
    Folder containing this build's MSIs. Setup.Acceptance.msi is treated as the
    unrelated package for the self-test, not as one of the products.

.PARAMETER BaselinePath
    An MSI, or a folder searched recursively for BaselineName. Optional.

.PARAMETER BaselineName
    File name of the baseline package within BaselinePath.

.NOTES
    Exit 0 = the checks that could run, passed. Exit 1 = a check failed.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $PackageFolder,
    [string] $BaselinePath,
    [string] $BaselineName = 'Setup.Dorc.msi',
    [string] $UnrelatedName = 'Setup.Acceptance.msi'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$harness = Join-Path $PSScriptRoot 'Compare-MsiTables.ps1'
if (-not (Test-Path $harness)) { Write-Error "Comparison harness not found at $harness"; exit 1 }

if (-not (Test-Path $PackageFolder)) { Write-Error "Package folder not found: $PackageFolder"; exit 1 }

$packages = @(Get-ChildItem -Path $PackageFolder -Filter *.msi |
              Where-Object { $_.Name -ne $UnrelatedName })
if ($packages.Count -eq 0) { Write-Error "No installer packages under $PackageFolder"; exit 1 }

Write-Host ("Packages : {0}" -f (($packages | ForEach-Object { $_.Name }) -join ', '))
Write-Host ''

Write-Host '=== self-test: a package against itself (expect no drift)'
& $harness -Baseline $packages[0].FullName -Package $packages[0].FullName
if ($LASTEXITCODE -ne 0) {
    Write-Error 'Drift reported between a package and itself. The harness is wrong, not the package.'
    exit 1
}
Write-Host ''

$unrelated = Join-Path $PackageFolder $UnrelatedName
if (Test-Path $unrelated) {
    Write-Host '=== self-test: an unrelated package (expect drift)'
    & $harness -Baseline $packages[0].FullName -Package $unrelated
    if ($LASTEXITCODE -eq 0) {
        Write-Error 'No drift reported between two unrelated packages. The comparison cannot fail, so it is not a gate.'
        exit 1
    }
    Write-Host 'Drift reported as expected.'
    Write-Host ''
}
else {
    Write-Warning "$UnrelatedName was not produced, so the negative half of the self-test did not run."
}

if ([string]::IsNullOrWhiteSpace($BaselinePath)) {
    Write-Warning 'No baseline supplied, so the G-1 comparison did not run. Point BaselinePath at the single-package baseline to arm it.'
    exit 0
}

$baseline = if (Test-Path $BaselinePath -PathType Leaf) {
    Get-Item $BaselinePath
} else {
    Get-ChildItem -Path $BaselinePath -Recurse -Filter $BaselineName | Select-Object -First 1
}
if (-not $baseline) { Write-Error "No $BaselineName found under $BaselinePath"; exit 1 }

Write-Host "=== G-1: union of this build against $($baseline.FullName)"
& $harness -Baseline $baseline.FullName -Package ($packages | ForEach-Object { $_.FullName })
if ($LASTEXITCODE -ne 0) {
    Write-Error 'G-1 violated: the packages no longer install what the baseline installed.'
    exit 1
}

Write-Host 'Installer tables verified.'
exit 0
