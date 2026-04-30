Clear-Host

#region Function Load-JSONFromFile
Function Load-JSONFromFile {
    [CmdletBinding()]
    Param(
        [Parameter(Position=0, Mandatory=$true)]
        [ValidateNotNullOrEmpty()]
        [System.String]$Path
    )
    try {
        return (Get-Content $Path) -join "`n" | ConvertFrom-Json
    }
    catch {
        throw
    }
}
#endregion Function Load-JSONFromFile

#region Function Load-InstallSettings
Function Load-InstallSettings 
{
Param
(
    [Parameter(Position=0, Mandatory=$true)]
    [ValidateNotNullOrEmpty()]
    [string]$Path
)
try 
    {
        if (-not (Test-Path -Path $Path -PathType Leaf))
        {
            throw "Settings file $($Path) not found"
        }
        else
        {
            $settings = Load-JSONFromFile -Path $Path
        }
        
        # Add methods / properties
        
        $getEnvNames = {
            Param
            (
                [string]$Exclude = ""
            )
            $Value = @()
            foreach ($EnvName in ($this.Environments | Select -Property Name))
            {
                if ($envName -ne $Exclude)
                {
                    $Value += $EnvName.Name
                }
            }
            return $value
        }
        Add-Member -InputObject $settings -MemberType ScriptMethod -Name GetEnvNames -Value $getEnvNames
        
        $loadModules = {
            Param
            (
                [Parameter(Position=0, Mandatory=$true)]
                [ValidateNotNullOrEmpty()]
                [string]$Path
            )
            foreach ($module in $this.Modules)
            {
                if (-not (Get-Module -Name $module.Name))
                {
                    if ([string]::IsNullOrEmpty($module.Path) -and [string]::IsNullOrEmpty($module.Server))
                    {
                        if (-not (Get-Module -Name $module.Name -ListAvailable))
                        {
                            throw "Module $($module.Name) not available"
                        }
                        
                        Push-Location
                        Import-Module $module.Name -DisableNameChecking
                        Pop-Location
                    }
                    elseif (![string]::IsNullOrEmpty($module.Server))
                    {
                        Write-Host "About to Load " $module.Name " From " $module.Server
                        $RemoteSession = New-PSSession -ComputerName $module.Server
                        Import-Module -PSSession $RemoteSession -Name $module.Name
                    }
                    elseif (![string]::IsNullOrEmpty($module.Path)){
                        Write-Host "About to Load " $module.Name " From " $module.Name
                        Import-Module (Join-Path $module.Path $module.Name) -DisableNameChecking
                    }
                    Write-Host "Module $($module.Name) loaded"
                }
                else
                {
                    Write-Host "Module $($module.Name) already loaded"
                }
            }
            Set-Location $Path
        }
        Add-Member -InputObject $settings -MemberType ScriptMethod -Name LoadModules -Value $loadModules

        return $settings
    }
    catch 
    {
        throw
    }
}
#endregion Function Load-InstallSettings

#region Function Get-EnvSettings
Function Get-EnvSettings {
    [CmdletBinding()]
    Param(
        [Parameter(Position=0, Mandatory=$true)]
        [PSObject]$settings,
        [Parameter(Position=1, Mandatory=$true)]
        [ValidateNotNullOrEmpty()]
        [System.String]$EnvName
    )
    try 
    {
        if ($settings.GetEnvNames() -contains $EnvName)
        {
            $envSettings = $settings.Environments | Where-Object { $_.Name -eq $envName}
        }
        else
        {
            throw "Invalid environment"
        }
        # Add methods to environment object

        $envSettings | Add-Member -MemberType NoteProperty -Name EnvName -Value $EnvName
        
        $getProperty = {
            Param([String]$Name)
            return $this.MsiProperties | Where-Object {$_.Name -eq $Name}
        }
        Add-Member -InputObject $envSettings -MemberType ScriptMethod -Name GetProperty -Value $getProperty

        $getPropertyValue = {
            Param([String]$Name)
            
            $prop = $this.GetProperty($Name)
            if ($prop)
            {
                $value = $prop.Value
                if ($prop.IsSecure)
                {
                    $tempPass = $value | ConvertTo-SecureString
                    $BSTR = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($tempPass)
                    $value = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR)
                }
                if ($prop.IsPath)
                {
                    $value = ($value -replace '/','\')
                }
                return $value
            }
            else
            {
                return $null
            }
        }
        Add-Member -InputObject $envSettings -MemberType ScriptMethod -Name GetPropertyValue -Value $getPropertyValue

        $getCredentials = {
            Param([String]$UserProperty, [string]$PassProperty)
            
            $propName = $this.GetProperty($UserProperty)
            $propPass = $this.GetProperty($PassProperty)
            if ($propName -and $propPass)
            {
                $userName = $propName.Value
                                
                if ($propPass.IsSecure)
                {
                    $password = $propPass.Value | ConvertTo-SecureString
                }
                else
                {
                    throw "Password property $PassProperty is not a secure string"
                }
                return New-Object -TypeName System.Management.Automation.PSCredential -ArgumentList $userName,$password
            }
            else
            {
                return $null
            }
        }
        Add-Member -InputObject $envSettings -MemberType ScriptMethod -Name GetCredentials -Value $getCredentials

        $setPropertyValue = {
            Param([String]$Name, [String]$Value)
            $prop = $this.GetProperty($Name)
            if ($prop)
            {
                $prop.Value = $Value
            }                
            else
            {
                throw "Property $Name not defined"
            }
        }
        Add-Member -InputObject $envSettings -MemberType ScriptMethod -Name SetPropertyValue -Value $SetPropertyValue

        $getSecurePropertyValues = {
            foreach ($prop in $this.MsiProperties)
            {
                if ($prop.IsSecure)
                {
                    # Check if this is a service account password or a general secret
                    if (-not [string]::IsNullOrEmpty($prop.AccountNameProperty))
                    {
                        # This is a service account password
                        if ([string]::IsNullOrEmpty($prop.Value))
                        {
                            $svcAccountName = $this.GetPropertyValue($prop.AccountNameProperty)
                            if ([System.Environment]::UserInteractive)
                            {
                                $prop.Value = Read-Host -Prompt "Enter Password for Account $($svcAccountName)" -AsSecureString | ConvertFrom-SecureString
                            }
                            else
                            {
                                Write-Host "Non-interactive mode: Missing secure property '$($prop.Name)' for account '$($svcAccountName)'. Expected value in DeploySettings.json"
                                $prop.Value = ""
                            }
                        }
                    }
                    else
                    {
                        # This is a general secret (not tied to an account)
                        if ([string]::IsNullOrEmpty($prop.Value))
                        {
                            if ([System.Environment]::UserInteractive)
                            {
                                $prop.Value = Read-Host -Prompt "Enter value for secret property '$($prop.Name)'" -AsSecureString | ConvertFrom-SecureString
                            }
                            else
                            {
                                Write-Host "Non-interactive mode: Missing secret property '$($prop.Name)'. Expected value in DeploySettings.json"
                                $prop.Value = ""
                            }
                        }
                        else
                        {
                            # Secret is already set, no action needed
                        }
                    }
                }
            }
        }
        Add-Member -InputObject $envSettings -MemberType ScriptMethod -Name GetSecurePropertyValues -Value $getSecurePropertyValues

        $loadPropertiesToVariables = {
            Set-Variable -Name EnvironmentName -Force -Scope Global -Value $this.EnvName
            $this.GetSecurePropertyValues()
            foreach ($prop in $this.MsiProperties)
            {
                if ($prop.IsPath)
                {
                    $Value = $prop.Value.Replace("/","\")
                }
                else
                {
                    $Value = $prop.Value
                }
                Set-Variable -Name $prop.Name -Force -Scope Global -Value $Value
            }
        }
        Add-Member -InputObject $envSettings -MemberType ScriptMethod -Name LoadPropertiesToVariables -Value $loadPropertiesToVariables

        $getPropertiesAsArrayList = {
            $this.GetSecurePropertyValues()
            $arrParameters = New-Object System.Collections.ArrayList($null)
            foreach ($prop in $this.MsiProperties)
            {
                if ($prop.IsPath)
                {
                    $Value = $prop.Value.Replace("/","\")
                }
                else
                {
                    $Value = $prop.Value
                }
                
                
                [Void]$arrParameters.add("$($prop.Name)=" + [char]34 + "$($Value)" + [char]34)
            }
            $dbServer = $this.GetPropertyValue("DEPLOYMENT_DBSERVER")
            $dbName   = $this.GetPropertyValue("DEPLOYMENT_DB")
            [Void]$arrParameters.add("DB.CONNECTIONSTRING=" + [char]34 + "Data Source=$($dbServer);Initial Catalog=$($dbName);Integrated Security=SSPI;MultipleActiveResultSets=true" + [char]34)
            
            return $arrParameters
        }
        Add-Member -InputObject $envSettings -MemberType ScriptMethod -Name GetPropertiesAsArrayList -Value $getPropertiesAsArrayList
        
        return $envSettings
    }
    catch 
    {
        throw
    }
}
#endregion Function Get-EnvSettings

#region Function Merge-DeploySettings
function Merge-DeploySettings {
    param(
        [Parameter(Mandatory=$true)]
        [string]$SourceFolder,
        
        [Parameter(Mandatory=$true)]
        [string]$EnvName,
        
        [Parameter(Mandatory=$true)]
        [string]$OutputPath
    )

    $commonPath = Join-Path $SourceFolder "DeploySettings.Common.json"
    $envPath = Join-Path $SourceFolder "environments\DeploySettings.$EnvName.json"
    
    # Verify files exist
    if (-not (Test-Path $commonPath)) {
        throw "Common settings file not found: $commonPath"
    }
    Write-Host "  [OK] Found: DeploySettings.Common.json" -ForegroundColor Green
    
    if (-not (Test-Path $envPath)) {
        throw "Environment settings file not found: $envPath (DeploySettings.$EnvName.json)"
    }
    Write-Host "  [OK] Found: DeploySettings.$EnvName.json" -ForegroundColor Green
    
    # Load JSON files
    Write-Host "Loading and merging settings..." -ForegroundColor Yellow
    try {
        $common = Get-Content $commonPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $envSettings = Get-Content $envPath -Raw -Encoding UTF8 | ConvertFrom-Json
        
        # Validate environment settings
        if ($null -eq $envSettings) {
            throw "Environment settings is null after loading!"
        }
        
        # Clone the common object and add environment settings
        $merged = $common | ConvertTo-Json -Depth 100 | ConvertFrom-Json
        $merged | Add-Member -MemberType NoteProperty -Name "Environments" -Value @($envSettings) -Force
        
        # Ensure output directory exists
        $outputDir = Split-Path -Parent $OutputPath
        if (-not [string]::IsNullOrEmpty($outputDir) -and -not (Test-Path $outputDir)) {
            New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
        }
        
        # Save merged file
        $merged | ConvertTo-Json -Depth 100 | Set-Content $OutputPath -Encoding UTF8
        
        return $true
    }
    catch {
        Write-Error "Failed to merge settings: $_"
        throw
    }
}

#endregion Function Merge-DeploySettings
