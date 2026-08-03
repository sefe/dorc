<#
.SYNOPSIS
    Compares the installer tables of one or more MSIs against a baseline.

.DESCRIPTION
    Acceptance gate for the installer decomposition (docs/msi-decomposition/).

    G-1 requires that the union of the split packages installs exactly what the
    single Setup.Dorc.msi installed. Three things are compared, and drift in
    any of them fails the run:

      files        (target directory, file name, version) — File Ids are
                   deliberately ignored, because re-harvesting into new wixproj
                   files legitimately regenerates them, and a naive Id
                   comparison reports near-total churn and tells you nothing.
      registry     (root, key, name, value) — RegistryEntries carries no file,
                   so the file comparison cannot see it at all.
      certificates (name, store location, store name) — iis:Certificate is a
                   custom action rather than a refcounted resource, which makes
                   it the resource least likely to survive the split intact and
                   the one nothing else here would notice losing.

    -Package accepts several MSIs so the union can be compared against the
    baseline. During the extraction steps (S-005..S-008) that union is the
    extracted packages PLUS the residual monolith, which lets the invariant be
    checked at every step rather than only at the end.

    Reads via the WindowsInstaller COM API, so it runs on the build agent with
    no additional tooling.

.PARAMETER Baseline
    The reference MSI, typically the last single-package build.

.PARAMETER Package
    One or more MSIs whose union is compared against the baseline.

.PARAMETER Table
    Additional tables to compare by row count. Structural drift in these is
    reported but, unlike the three sets above, does not by itself fail the
    run — a split legitimately redistributes components between packages.

.EXAMPLE
    .\Compare-MsiTables.ps1 -Baseline .\baseline\Setup.Dorc.msi `
                            -Package .\out\Setup.Dorc.Api.msi, .\out\Setup.Dorc.msi

.NOTES
    Exit 0 = no drift. Exit 1 = drift in files, registry or certificates, or a
    package could not be read. A comparison that cannot fail is not a test:
    verify this reports zero against itself AND non-zero against an unrelated
    MSI before trusting it (S-001 verification intent).
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]   $Baseline,
    [Parameter(Mandatory)][string[]] $Package,
    [string[]] $Table = @('Component', 'Directory', 'Feature', 'FeatureComponents',
                          'Registry', 'ServiceInstall', 'CustomAction')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# MSI stores short and long names as "SHORT|Long". The long name is the one
# that lands on disk, and the one a human recognises.
function Get-LongName([string] $Value) {
    if ([string]::IsNullOrEmpty($Value)) { return $Value }
    $parts = $Value -split '\|', 2
    return $parts[-1]
}

function Get-MsiTable {
    param([string] $Path, [string] $TableName, [string[]] $Columns)

    $installer = New-Object -ComObject WindowsInstaller.Installer
    try {
        # 0 = read-only
        $database = $installer.GetType().InvokeMember(
            'OpenDatabase', 'InvokeMethod', $null, $installer, @($Path, 0))

        # Absent tables are normal (not every package has ServiceInstall).
        $names = $database.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $database,
                    @("SELECT Name FROM _Tables WHERE Name = '$TableName'"))
        $names.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $names, $null)
        $exists = $names.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $names, $null)
        # Same unary comma as below, so an absent table returns an empty array
        # rather than nothing at all.
        if ($null -eq $exists) { return , @() }

        # The query names its columns, so the caller already knows the shape and
        # nothing has to ask the view for it. Deriving the names from
        # View.ColumnInfo is what the first version did, and it failed on the
        # runner with DISP_E_TYPEMISMATCH: the field index widened to Int64 and
        # Record.StringData takes a VT_I4. Naming the columns removes the call
        # rather than fixing its arguments. Indexes are still cast explicitly,
        # because StringData is reached either way.
        $select = if ($Columns) { ($Columns | ForEach-Object { "``$_``" }) -join ', ' } else { '*' }
        $view = $database.GetType().InvokeMember('OpenView', 'InvokeMethod', $null, $database,
                    @("SELECT $select FROM ``$TableName``"))
        $view.GetType().InvokeMember('Execute', 'InvokeMethod', $null, $view, $null)

        $rows = New-Object System.Collections.Generic.List[object]
        while ($true) {
            $record = $view.GetType().InvokeMember('Fetch', 'InvokeMethod', $null, $view, $null)
            if ($null -eq $record) { break }

            if (-not $Columns) {
                # Counted, not read — the caller only wants the row count.
                $rows.Add($null)
                continue
            }

            # Positional, not named. Rows come back as a plain array in the
            # order the caller asked for, so nothing downstream depends on a
            # property name matching an MSI column name — which is what broke
            # twice here already, once on a genuinely wrong name and once on a
            # correct one that still would not resolve.
            $row = New-Object 'string[]' $Columns.Count
            for ([int] $i = 1; $i -le $Columns.Count; $i++) {
                $row[$i - 1] = [string] $record.GetType().InvokeMember(
                    'StringData', 'GetProperty', $null, $record, @([int] $i))
            }
            $rows.Add($row)
        }
        # Unary comma: returning the collection bare would let PowerShell
        # enumerate it on the way out, and each row is itself an array, so the
        # rows would arrive flattened into a stream of strings.
        return , $rows.ToArray()
    }
    finally {
        [System.Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer) | Out-Null
    }
}

# Resolve each Directory row to a full target path by walking Directory_Parent.
# Rows are [Directory, Directory_Parent, DefaultDir], in the order selected.
function Get-DirectoryPaths([object[]] $Directories) {
    $byId = @{}
    foreach ($d in $Directories) { $byId[$d[0]] = $d }

    $resolved = @{}
    function Resolve-One([string] $Id) {
        if ($resolved.ContainsKey($Id)) { return $resolved[$Id] }
        if (-not $byId.ContainsKey($Id)) { return $Id }

        $row  = $byId[$Id]
        $name = Get-LongName $row[2]
        # '.' means "same as parent" and contributes no path segment.
        if ($name -eq '.') { $name = '' }

        $parentId = $row[1]
        if ([string]::IsNullOrEmpty($parentId) -or $parentId -eq $Id) {
            $path = $name
        }
        else {
            $parent = Resolve-One $parentId
            $path = if ($name) { "$parent/$name".TrimStart('/') } else { $parent }
        }
        $resolved[$Id] = $path
        return $path
    }

    foreach ($d in $Directories) { Resolve-One $d[0] | Out-Null }
    return $resolved
}

# The comparable identity of a package's payload: where each file lands, what
# it is called, and which version it is.
function Get-FileSet([string] $Path) {
    $files      = Get-MsiTable -Path $Path -TableName 'File'      -Columns 'Component_', 'FileName', 'Version'
    $components = Get-MsiTable -Path $Path -TableName 'Component' -Columns 'Component', 'Directory_'
    $dirs       = Get-MsiTable -Path $Path -TableName 'Directory' -Columns 'Directory', 'Directory_Parent', 'DefaultDir'

    # Component rows are [Component, Directory_]; File rows are
    # [Component_, FileName, Version].
    $componentDir = @{}
    foreach ($c in $components) { $componentDir[$c[0]] = $c[1] }
    $dirPaths = Get-DirectoryPaths $dirs

    $set = @{}
    foreach ($f in $files) {
        $dirId = $componentDir[$f[0]]
        $dir   = if ($dirId -and $dirPaths.ContainsKey($dirId)) { $dirPaths[$dirId] } else { '<unresolved>' }
        $key   = '{0}/{1}|{2}' -f $dir, (Get-LongName $f[1]), $f[2]
        $set[$key] = $true
    }
    return $set
}

# Machine-scoped registry writes. Keyed on what lands in the registry, not on
# the Registry row Id, for the same reason File Ids are ignored above.
function Get-RegistrySet([string] $Path) {
    $set = @{}
    foreach ($r in Get-MsiTable -Path $Path -TableName 'Registry' -Columns 'Root', 'Key', 'Name', 'Value') {
        $set[('{0}\{1}\{2}={3}' -f $r[0], $r[1], $r[2], $r[3])] = $true
    }
    return $set
}

# Certificates installed into a machine store by the IIS extension. The table
# is absent in packages that install none, which Get-MsiTable already handles.
function Get-CertificateSet([string] $Path) {
    $set = @{}
    foreach ($c in Get-MsiTable -Path $Path -TableName 'Certificate' -Columns 'Name', 'StoreLocation', 'StoreName') {
        $set[('{0}|{1}|{2}' -f $c[0], $c[1], $c[2])] = $true
    }
    return $set
}

# --- compare -----------------------------------------------------------------

# Returns the number of differences, having reported them.
function Compare-Set {
    param(
        [string]    $Label,
        [hashtable] $BaselineSet,
        [hashtable] $UnionSet,
        [string[]]  $Duplicates
    )

    $missing = @($BaselineSet.Keys | Where-Object { -not $UnionSet.ContainsKey($_) } | Sort-Object)
    $extra   = @($UnionSet.Keys    | Where-Object { -not $BaselineSet.ContainsKey($_) } | Sort-Object)

    Write-Host ("{0}: baseline={1} union={2}" -f $Label, $BaselineSet.Count, $UnionSet.Count)

    if ($Duplicates.Count -gt 0) {
        # The same entry in two packages is not automatically wrong — but it is
        # never accidental, so surface it.
        Write-Host ("  NOTE: {0} entr(ies) appear in more than one package:" -f $Duplicates.Count)
        $Duplicates | Sort-Object -Unique | Select-Object -First 20 | ForEach-Object { Write-Host "     = $_" }
    }

    if ($missing.Count -eq 0 -and $extra.Count -eq 0) {
        Write-Host '  no drift.'
        return 0
    }

    if ($missing.Count -gt 0) {
        Write-Host ("  MISSING ({0}) — these would stop being installed:" -f $missing.Count)
        $missing | Select-Object -First 40 | ForEach-Object { Write-Host "     - $_" }
    }
    if ($extra.Count -gt 0) {
        Write-Host ("  EXTRA ({0}) — these were not in the baseline:" -f $extra.Count)
        $extra | Select-Object -First 40 | ForEach-Object { Write-Host "     + $_" }
    }
    return ($missing.Count + $extra.Count)
}

$comparisons = @(
    @{ Label = 'Files';        Reader = ${function:Get-FileSet} }
    @{ Label = 'Registry';     Reader = ${function:Get-RegistrySet} }
    @{ Label = 'Certificates'; Reader = ${function:Get-CertificateSet} }
)

Write-Host "Baseline : $Baseline"
foreach ($p in $Package) { Write-Host "Package  : $p" }
Write-Host ''

$drift = 0
foreach ($c in $comparisons) {
    $baselineSet = & $c.Reader $Baseline

    $unionSet   = @{}
    $duplicates = New-Object System.Collections.Generic.List[string]
    foreach ($p in $Package) {
        foreach ($k in (& $c.Reader $p).Keys) {
            if ($unionSet.ContainsKey($k)) { $duplicates.Add($k) }
            $unionSet[$k] = $true
        }
    }

    $drift += Compare-Set -Label $c.Label -BaselineSet $baselineSet -UnionSet $unionSet -Duplicates $duplicates.ToArray()
    Write-Host ''
}

Write-Host 'Reported only (a split legitimately redistributes these):'
foreach ($t in $Table) {
    $b = @(Get-MsiTable -Path $Baseline -TableName $t).Count
    $u = 0
    foreach ($p in $Package) { $u += @(Get-MsiTable -Path $p -TableName $t).Count }
    $flag = if ($b -eq $u) { 'match' } else { 'differs' }
    Write-Host ("  [{0,-7}] {1,-18} baseline={2,-6} union={3}" -f $flag, $t, $b, $u)
}
Write-Host ''

if ($drift -eq 0) {
    Write-Host 'RESULT: no drift in files, registry or certificates.'
    exit 0
}

Write-Host ("RESULT: DRIFT DETECTED ({0} difference(s))." -f $drift)
exit 1
