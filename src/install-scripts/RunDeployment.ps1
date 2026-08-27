Param
(
    [String]$EnvName = $Env:TARGET_ENVNAME,
    [String]$DropFolder = (Join-Path $env:SYSTEM_WORKFOLDER "drop"),
    [String]$SettingsFolder,
    [String]$DeploymentInstallAccount,
    [String]$DeploymentInstallAccountPassword,
    [String]$ExcludeMSI = $Env:Exclude_MSI,
    [String]$ExcludeDACPAC = $Env:Exclude_DACPAC,
    [Switch]$DatabaseInstalls = ($Env:InstallDacPac -eq "True"),
    [Switch]$MsiInstalls = ($Env:InstallMsi -eq "True")
)

Clear-Host

# Uninstall any existing versions of the modules
Uninstall-Module DOrcDeployModule -AllVersions -ErrorAction Silent
Uninstall-Module Internal-DOrcDeployModule -AllVersions -ErrorAction Silent

# Install the public DOrcDeployModule from PowerShell Gallery
Install-Module -Name "DOrcDeployModule" -Repository "PSGallery" -AllowClobber -Force
Import-Module -Name DOrcDeployModule

# Install SqlServer module from PowerShell Gallery
Uninstall-Module SqlServer -AllVersions -ErrorAction Silent
Install-Module -Name "SqlServer" -RequiredVersion "22.3.0" -Repository "PSGallery" -AllowClobber -Force
Import-Module -Name SqlServer -RequiredVersion "22.3.0"

# set all mandatory variables for DOrcDeployModule functions
$DACPACSQLScripts = $Env:DACPACSQLScriptsDir # This should be set for DeployDACPAC
$WiLogUtlPath = $Env:WiLogUtlPathDir # This should be set for DeployMSI
$DOrcSupportEmailSMTPServer = $Env:DOrcSupportEmailSMTPServer # This should be set for DeployMSI:CheckDiskSpace
$DOrcSupportEmailFrom = $Env:DOrcSupportEmailFrom # This should be set for DeployMSI:CheckDiskSpace
$DOrcSupportEmailTo = $Env:DOrcSupportEmailTo # This should be set for DeployMSI:CheckDiskSpace

$commonCode = Join-Path $DropFolder "DeploymentCommon.ps1"
if (Test-Path $commonCode -PathType Leaf)
{
    .$($commonCode)
}
else
{
    throw [System.IO.FileNotFoundException] $commonCode
}

$mergedSettingsPath = Join-Path $SettingsFolder "DeploySettings.json"
Write-Host "=== Preparing Deployment Settings ===" -ForegroundColor Cyan
Merge-DeploySettings -SourceFolder $SettingsFolder -EnvName $EnvName -OutputPath $mergedSettingsPath

# Load the merged settings
$settings = Load-InstallSettings -Path $mergedSettingsPath

$settings.LoadModules($DropFolder)

if ([string]::IsNullOrEmpty($EnvName))
{
    throw "EnvName is required"
}
if ($settings.GetEnvNames() -notcontains $EnvName) 
{
    throw "Selected environment name '$($EnvName)' not valid ($($settings.GetEnvNames()))"
}

Write-Host "----------Environment: $($EnvName)----------"
$envSettings = Get-EnvSettings $settings $EnvName

$envSettings.LoadPropertiesToVariables()

[string[]]$AllServers = @()
# TODO: Stop all DOrc activity, suspend / stop services, websites etc..
foreach ($serverName in $envsettings.TargetServers)
{
    $AllServers += $serverName
    Write-Host "Closing RDP sessions on:" $ServerName
    LogOffUsers $ServerName
    Stop-Services ($settings.DeploymentServices -split ";") $ServerName
}

# Deploy Database updates (should be added to existing settings JSON)

if ($DatabaseInstalls)
{
    if (-not (Test-Path -Path $settings.SQLPackagePath -PathType Leaf))
    {
        throw [System.IO.FileNotFoundException] $settings.SQLPackagePath
    }
    
	$dacPacsToExclude = ($ExcludeDACPAC -split ";")
    foreach ($dacPacName in $settings.DacPacFileNames)
    {

		if ($dacPacsToExclude -notcontains $dacPacName)
		{
			$DacPacPath = (Get-ChildItem -Path $DropFolder -Filter $dacPacName -Recurse | Select-Object -First 1).FullName
    
			if ([string]::IsNullOrEmpty($DacPacPath))
			{
				throw [System.IO.FileNotFoundException] $dacPacName
			}

			# Add Variables here, required even if blank
			
			$arrVariables = New-Object System.Collections.ArrayList($null)
            		[Void]$arrVariables.add([char]34 + "EnvironmentName" + [char]34 + "=" + [char]34 + $EnvName + [char]34)

			if (($ExcludeDACPAC -split ";") -contains $dacPacName)
			{
				Write-Host "DACPAC $dacPacName has not been deployed; excluded"
			}
			else
			{
				DeployDACPAC $settings.SQLPackagePath $envSettings.GetPropertyValue("DEPLOYMENT_DBSERVER") $envSettings.GetPropertyValue("DEPLOYMENT_DB") $DacPacPath $settings.DACPACPublishProfile $arrVariables $settings.DACPACBlackList
			}
		}
		else
		{
			Write-Host "SKIPPED: DACPAC $dacPacName"
		}
    }
}
if ($MsiInstalls)
{
    $global:DeploymentServiceAccount = $DeploymentInstallAccount
    $global:DeploymentServiceAccountPassword = $DeploymentInstallAccountPassword

    foreach ($msiObject in $settings.MsiFileNames)
    {
        if (($ExcludeMSI -split ";") -contains $msiObject.Name)
        {
            Write-Host "SKIPPED: Deploy $($msiObject.Name) to" $envSettings.TargetServers
        }
        else
        {
            Write-Host "Deploy $($msiObject.Name) to" $envSettings.TargetServers
            DeployMSI -MSIFile $msiObject.Name -DropFolder $DropFolder -ProductNames $msiObject.ProductNames
        }
    }
}
