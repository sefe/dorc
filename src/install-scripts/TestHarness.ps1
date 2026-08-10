Clear-Host

# Remove-Module TfsCommon -ErrorAction SilentlyContinue

# $steps = @()
# $steps += "RunDeployment.ps1"

# # $scriptsFolder = "c:\VS\DevTools\DeploymentOrchestrator\Main\Source\Deployment\Scripts\"
# foreach ($name in $steps)
# {
#     $runScript = Join-Path $PSScriptRoot $name

#     .$($runScript) -EnvName "Deployment UAT"
# }

function GetServersOfType_V2([string] $strType = "") {
    $varName = 'ServerNames_'+ $strType
    $AllServerVariable=get-variable -Name $varName
    $AllTags=(get-variable -Name 'ServerNames_*').name.Replace('ServerNames_','')
    $table=new-object "System.Data.DataTable"
    $ColumnNames='Name','Tags'
    foreach ($ColumnName in $ColumnNames) {
    $Col = New-Object system.Data.DataColumn $ColumnName, ([string])
    $table.columns.add($col)
    }
    foreach ($Server in $AllServerVariable.Value) {
        $ServerTags = $NULL
        Foreach ($ServerVariable in $AllServerVariable) {
            If ($ServerVariable.Value -like $Server) {
                $ServerTags+=$ServerVariable.name.Replace('ServerNames_','')
            }
        }
        $Row = $table.NewRow()
        $Row.Name = $Server
        $Row.Tags = $ServerTags
        $table.Rows.Add($Row)        
    }

    return @(, $table)
}

$EnvName = "DOrc DV 01"

$commonCode = ".\DeploymentCommon.ps1"
if (Test-Path $commonCode -PathType Leaf)
{
    .$($commonCode)
}
else
{
    throw [System.IO.FileNotFoundException] $commonCode
}


$settings = Load-InstallSettings -Path ".\DeploySettings.json"

$envSettings = Get-EnvSettings $settings $EnvName
$envSettings.LoadPropertiesToVariables()

$svrs = GetServersOfType_V2("WebServer")

if ($svrs.Rows.Count -gt 0) {
    Write-Host "found server"
}

Write-Host "----------Environment: $($envSettings)----------"
