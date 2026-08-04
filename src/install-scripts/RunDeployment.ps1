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

$settings = Load-InstallSettings -Path (Join-Path $SettingsFolder "DeploySettings.json")

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

# The packages this run will actually install. Everything below is scoped to
# these rather than to the environment as a whole: with one package there was
# no difference, with four there is. Deploying the UI used to stop both
# monitors on every target server and nothing ever started them again, which
# made independent deployability unreachable no matter how well the packages
# were split.
$msisToDeploy = @($settings.MsiFileNames | Where-Object { ($ExcludeMSI -split ";") -notcontains $_.Name })

# Each package declares the Windows services it owns in its .msi.json sidecar.
# A sidecar without that array falls back to the environment-wide list, so a
# drop produced before the sidecars carried it still quiesces as it used to.
function Get-ServicesInScope
{
    Param([object[]]$Packages, [string]$Drop, $Settings)

    $services = @()
    foreach ($package in $Packages)
    {
        $sidecar = Get-ChildItem -Path $Drop -Filter "$($package.Name).json" -Recurse -ErrorAction SilentlyContinue |
                   Select-Object -First 1
        if (-not $sidecar)
        {
            Write-Host "  No sidecar for $($package.Name); falling back to the environment service list."
            $services += ($Settings.DeploymentServices -split ";")
            continue
        }

        $content = Get-Content -Raw -Path $sidecar.FullName | ConvertFrom-Json
        if ($null -eq $content.PSObject.Properties['Services'])
        {
            Write-Host "  $($sidecar.Name) declares no Services; falling back to the environment service list."
            $services += ($Settings.DeploymentServices -split ";")
            continue
        }

        $services += $content.Services
    }
    return @($services | Where-Object { $_ } | Sort-Object -Unique)
}

$servicesInScope = Get-ServicesInScope -Packages $msisToDeploy -Drop $DropFolder -Settings $settings
if ($servicesInScope.Count -gt 0)
{
    Write-Host "Services in scope for this deployment:" ($servicesInScope -join ", ")
}
else
{
    Write-Host "No services belong to the packages being deployed; none will be stopped."
}

foreach ($serverName in $envsettings.TargetServers)
{
    $AllServers += $serverName
    Write-Host "Closing RDP sessions on:" $ServerName
    LogOffUsers $ServerName
    if ($servicesInScope.Count -gt 0)
    {
        Stop-Services $servicesInScope $ServerName
    }
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

    # One atomic install became four, so a mid-run failure can now leave an
    # environment with some packages installed and some not. The bare loop
    # reported success either way. Each outcome is recorded, the run stops at
    # the first failure rather than installing on top of a broken state, and
    # the summary says which products are installed and which are not.
    $installed = @()
    $failed = $null

    foreach ($msiObject in $settings.MsiFileNames)
    {
        if (($ExcludeMSI -split ";") -contains $msiObject.Name)
        {
            Write-Host "SKIPPED: Deploy $($msiObject.Name) to" $envSettings.TargetServers
            continue
        }

        Write-Host "Deploy $($msiObject.Name) to" $envSettings.TargetServers
        try
        {
            DeployMSI -MSIFile $msiObject.Name -DropFolder $DropFolder -ProductNames $msiObject.ProductNames
            $installed += $msiObject.Name
        }
        catch
        {
            $failed = $msiObject.Name
            Write-Host "FAILED: $($msiObject.Name) — $($_.Exception.Message)"
            break
        }
    }

    Write-Host "----------Installer outcome----------"
    Write-Host "  Installed:   $(if ($installed) { $installed -join ', ' } else { 'none' })"
    if ($failed)
    {
        $notAttempted = @($msisToDeploy | Where-Object { $installed -notcontains $_.Name -and $_.Name -ne $failed } |
                          ForEach-Object { $_.Name })
        Write-Host "  Failed:      $failed"
        Write-Host "  Not tried:   $(if ($notAttempted) { $notAttempted -join ', ' } else { 'none' })"
        throw "Deployment stopped at $failed. The environment is part-installed: $(if ($installed) { $installed -join ', ' } else { 'nothing' }) present."
    }

    # Nothing restarted the services this run stopped, so a deployment that
    # touched the monitors left them down until someone noticed.
    if ($servicesInScope.Count -gt 0)
    {
        Write-Host "----------Starting services----------"
        foreach ($serverName in $envSettings.TargetServers)
        {
            Start-Services $servicesInScope $serverName
        }

        # A service that is absent is not a failure: TargetServers is the whole
        # environment, and only the servers the Monitors package installed on
        # host these services at all. Failing on absence would fail every
        # deployment to an environment with a server that does not run them.
        # A service that exists and is not running is a failure — that is the
        # state this step exists to catch.
        $notRunning = @()
        $absent = @()
        foreach ($serverName in $envSettings.TargetServers)
        {
            foreach ($service in $servicesInScope)
            {
                $state = Invoke-Command -ComputerName $serverName -ScriptBlock {
                    param($svc) (Get-Service $svc -ErrorAction SilentlyContinue).Status
                } -ArgumentList $service

                if (-not $state)
                {
                    Write-Host "  $service is not installed on $serverName"
                    $absent += "$service on $serverName"
                }
                else
                {
                    Write-Host "  $service on $serverName is $state"
                    if ($state -ne "Running") { $notRunning += "$service on $serverName" }
                }
            }
        }
        if ($absent.Count -gt 0)
        {
            Write-Host "Not installed, so not started: $($absent -join '; ')"
        }
        if ($notRunning.Count -gt 0)
        {
            throw "Deployment installed successfully but these services are installed and not running: $($notRunning -join '; ')"
        }
    }
}
