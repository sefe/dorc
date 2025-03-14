
<#  ---------------------------------------------------------------------
    This script copiies all the deployable files to standardised folders
    --------------------------------------------------------------------#>
Param
(
    [string]$sourceRoot = $env:BUILD_SOURCESDIRECTORY
)

Set-Location  $sourceRoot

$commonCode = Join-Path $PSScriptRoot "deploymentCommon.ps1"
if (Test-Path $commonCode -PathType Leaf)
{
    .$($commonCode)
}
else
{
    throw [System.IO.FileNotFoundException] $commonCode
}

$settings = Load-InstallSettings -Path (Join-Path $PSScriptRoot "DeploySettings.json")

Copy-BuildArtefacts -TypeName "DACPAC" -FileNames $settings.DacPacFileNames -AdditionalExtnSearch @(".PreSQL.sql", ".PostSQL.sql") 
Copy-BuildArtefacts -TypeName "MSI"    -FileNames $settings.MsiFileNames    -AdditionalExtnSearch @(".msi.json") 