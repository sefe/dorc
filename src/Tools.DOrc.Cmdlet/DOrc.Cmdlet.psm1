using module ".\EndurUsers.psm1"
using module ".\Models.psm1"
using module ".\PropertiesHelper.psm1"
using module ".\ApiCaller.psm1"

function Import-DOrcProperties
{
    param (
    # Api Url
        [Parameter(Mandatory = $true)]
        [string]$ApiUrl,
    # Imported Csv File
        [Parameter(Mandatory = $true)]
        [string]$CsvFile,
    # Optional user JWT/Bearer token
        [Parameter(Mandatory = $false)]
        [string]$BearerToken
    )

    if (-not [string]::IsNullOrWhiteSpace($BearerToken))
    {
        Set-DOrcBearerToken -Token $BearerToken | Out-Null
    }

    try
    {
        Open-ApiConnection -ApiUrl $ApiUrl
    }
    catch
    {
        $ErrorMessage = $_.Exception.Message
        Write-Host $ErrorMessage
        return
    }
    #if (!$global:ApiConnection){
    #	Write-Host "Use Open-ApiConnection to create API connection"
    #	return
    #}else{
    #	$api=Get-Variable -Name ApiConnection -Scope Global
    #	Write-Host "Using API url:"$api.Value.Root
    #}
    $helper = [PropertiesHelper]::new()
    Write-Host "Loading properties data from"$CsvFile
    $newProperties = Import-Csv -Path $CsvFile | Convert-CsvToCsvProperties
    try
    {
        $helper.AddProperties($newProperties)
        $helper.AddPropertyValues($newProperties)
        $helper.UpdatePropertyValues($newProperties)
        Write-Host "Imoprt complete"
    }
    catch
    {
        $ErrorMessage = $_.Exception.Message
        Write-Host $ErrorMessage
        write-host "Execution aborted"
    }

}

function Export-DOrcProperties
{
    param (
    # Api Url
        [Parameter(Mandatory = $true)]
        [string]$ApiUrl,
    # Environment Name
        [Parameter(Mandatory = $true)]
        [string]$Environment,
    # Output CSV file path
        [Parameter(Mandatory = $true)]
        [string]$CsvFile,
    # Optional user JWT/Bearer token
        [Parameter(Mandatory = $false)]
        [string]$BearerToken
    )

    if (-not [string]::IsNullOrWhiteSpace($BearerToken))
    {
        Set-DOrcBearerToken -Token $BearerToken | Out-Null
    }

    try
    {
        Open-ApiConnection -ApiUrl $ApiUrl
    }
    catch
    {
        $ErrorMessage = $_.Exception.Message
        Write-Host $ErrorMessage
        return
    }
    Write-Host "Exporting properties for "$Environment" to "$CsvFile
    $helper = [PropertiesHelper]::new()
    try
    {
        $properties = $helper.ExportProperies($Environment)
        if ($properties)
        {
            $properties | Export-Csv -Path $CsvFile
            Write-Host "Export complete"
        }
        else
        {
            Write-Host "No Properties fround for" $Environment
        }
    }
    catch
    {
        $ErrorMessage = $_.Exception.Message
        Write-Host $ErrorMessage
        Write-Host "Export failed"
    }

}

function Open-ApiConnection
{
    param (
        [Parameter(Mandatory = $true)]
        [string]$ApiUrl
    )

    try
    {
        $result = [ApiCaller]::InvokeGet($ApiUrl, "")
        if ($result.ReturnCode -eq 200)
        {
            $connection = [ApiConnection]::new()
            $connection.Property = $result.Value.Property
            $connection.PropertyValues = $result.Value.PropertyValues
            $connection.Root = $ApiUrl
            Set-Variable -Name ApiConnection -Scope Global -Value $connection -Visibility Public
            write-host "API endpoints obtained"
        }
        else
        {
            #write-host "Cant get API endpoints"
            #write-host "Reason:"$result.Message
            throw $result.Message
        }
    }
    catch
    {
        throw "Cannot establish connection with " + $ApiUrl
    }

}

function Add-NonProdEndurUser
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Account,
        [Parameter(Mandatory = $true)]
        [string]$ApiUrl
    )
    [EndurUsers]$users = [EndurUsers]::new($ApiUrl)
    if ($Account -like "*.adm")
    {
        Write-Host -ForegroundColor Red "Only non ADM accounts allowed"
        return
    }
    if (-Not $users.HasRights())
    {
        write-host -ForegroundColor Red "Only users of Development Admins or Applications Admins AD group can perform this operation"
        return
    }
    if (-Not $users.UserExists($Account))
    {
        if ( $users.AddEndurUser($Account))
        {
            write-host "User created"
        }
        else
        {
            Write-Host "User not created"
        }

    }
    else
    {
        Write-Host "User already exists"
        return
    }
}
filter Convert-CsvToCsvProperties([Parameter(Mandatory = $true, ValueFromPipeline = $true)]$csvProperty)
{
    # Validate PropertyName is not blank or empty
    if ([string]::IsNullOrWhiteSpace($csvProperty.PropertyName))
    {
        throw "PropertyName cannot be blank or empty. Found blank PropertyName in CSV data."
    }
    
    # Validate Value is not blank or empty
    if ([string]::IsNullOrWhiteSpace($csvProperty.Value))
    {
        throw "Property value cannot be blank or empty. Found blank value for property '$($csvProperty.PropertyName)'."
    }
    
    $property = new-object -TypeName CsvProperties
    $property.Environment = $csvProperty.Environment
    $property.Value = $csvProperty.Value
    $property.IsSecured = [bool]::parse($csvProperty.IsSecured)
    $property.PropertyName = $csvProperty.PropertyName
    $property
}
filter Convert-CsvPropertiesToListProperties([Parameter(Mandatory = $true, ValueFromPipeline = $true)]$csvProperty)
{
    $property = [Property]::new()
    $property.Name = $csvProperty.PropertyName
    $property.Secure = $csvProperty.IsSecured
    $property
}

filter Convert-CsvPropertiesToValuesList([Parameter(Mandatory = $true, ValueFromPipeline = $true)]$csvProperty)
{
    $value = [PropertyValue]::new()
    $value.PropertyValueFilter = $csvProperty.Environment
    $value.Value = $csvProperty.Value
    $value.Property = [Property]::new()
    $value.Property.Secure = $csvProperty.IsSecured
    $value.Property.Name = $csvProperty.PropertyName
    $value
}

filter Convert-PropertyValueToCsvProperty([Parameter(Mandatory = $true, ValueFromPipeline = $true)]$PropertyValue)
{
    $property = [CsvProperties]::new()
    $property.Environment = $PropertyValue.PropertyValueFilter
    $property.Value = $PropertyValue.Value
    $property.PropertyName = $PropertyValue.Property.Name
    $property.IsSecured = $PropertyValue.Property.Secure
    $property
}

function Set-DOrcBearerToken
{
    <#
    .SYNOPSIS
    Sets a user JWT/Bearer token for DORC API requests.

    .DESCRIPTION
    Stores the supplied JWT in memory and applies it to subsequent API calls as
    Authorization: Bearer <token>.

    .PARAMETER Token
    JWT token value. Accepts both raw JWT and values prefixed with "Bearer ".

    .EXAMPLE
    Set-DOrcBearerToken -Token $jwt

    .EXAMPLE
    Set-DOrcBearerToken -Token "Bearer eyJ..."
    #>
    param (
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$Token
    )

    $normalizedToken = $Token.Trim()
    if ($normalizedToken -match '^(?i)Bearer\s+')
    {
        $normalizedToken = $normalizedToken -replace '^(?i)Bearer\s+', ''
    }

    if ([string]::IsNullOrWhiteSpace($normalizedToken))
    {
        throw "Token cannot be blank."
    }

    [ApiCaller]::SetAccessToken($normalizedToken)
    Write-Host "Bearer token set for DORC API requests" -ForegroundColor Green
    return $true
}

function Clear-DOrcBearerToken
{
    <#
    .SYNOPSIS
    Clears the current user JWT/Bearer token.
    #>

    [ApiCaller]::ClearAccessToken()
    Write-Host "Bearer token cleared" -ForegroundColor Green
}

function Get-DOrcBearerTokenStatus
{
    <#
    .SYNOPSIS
    Shows whether a bearer token is currently set.
    #>

    if ([string]::IsNullOrWhiteSpace([ApiCaller]::AccessToken))
    {
        Write-Host "No bearer token is currently set" -ForegroundColor Yellow
        return $false
    }

    Write-Host "Bearer token is set" -ForegroundColor Cyan
    return $true
}


# ============================================================================
# User Impersonation Functions
# ============================================================================

function Set-DOrcImpersonateUser
{
    <#
    .SYNOPSIS
    Sets the current Windows username to be sent with DORC API requests.
    
    .DESCRIPTION
    Adds X-Impersonate-User header to API requests with the specified username.
    This requires DORC API to be configured to read and log this custom header.
    
    .PARAMETER Username
    The username to impersonate. If not specified, uses current Windows username.
    
    .EXAMPLE
    # Use current Windows user
    Set-DOrcImpersonateUser
    
    .EXAMPLE
    # Use specific username
    Set-DOrcImpersonateUser -Username "john.doe@company.com"
    #>
    param(
        [Parameter(Mandatory = $false)]
        [string]$Username
    )
    
    if ([string]::IsNullOrEmpty($Username))
    {
        # Get current user's email/UPN
        # Try Azure/EntraID context first (most accurate for federated users)
        try
        {
            $azContext = Get-AzContext -ErrorAction SilentlyContinue
            if ($azContext -and $azContext.Account -and $azContext.Account.Id)
            {
                $Username = $azContext.Account.Id
            }
        }
        catch
        {
            # Azure module not available or not connected
        }
        
        # If Azure context didn't work, try environment variables
        if ([string]::IsNullOrEmpty($Username))
        {
            if (-not [string]::IsNullOrEmpty($env:USERDNSDOMAIN) -and -not [string]::IsNullOrEmpty($env:USERNAME))
            {
                $Username = "$env:USERNAME@$($env:USERDNSDOMAIN.ToLower())"
            }
            else
            {
                # Final fallback to SAMAccountName format
                $Username = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
            }
        }
    }
    
    [ApiCaller]::SetImpersonateUser($Username)
    
    Write-Host "Properties are imported by: $Username" -ForegroundColor Green
}

function Clear-DOrcImpersonateUser
{
    <#
    .SYNOPSIS
    Clears the impersonation username.
    
    .DESCRIPTION
    Removes the X-Impersonate-User header from subsequent API requests.
    
    .EXAMPLE
    Clear-DOrcImpersonateUser
    #>
    
    [ApiCaller]::ClearImpersonateUser()
    Write-Host "User impersonation cleared" -ForegroundColor Green
}

function Get-DOrcImpersonateUser
{
    <#
    .SYNOPSIS
    Gets the current impersonation username.
    
    .DESCRIPTION
    Returns the username being sent in the X-Impersonate-User header, or $null if not set.
    
    .EXAMPLE
    Get-DOrcImpersonateUser
    #>
    
    $username = [ApiCaller]::ImpersonateUser
    
    if ([string]::IsNullOrEmpty($username))
    {
        Write-Host "No impersonation user set" -ForegroundColor Yellow
        return $null
    }
    else
    {
        Write-Host "Current impersonation user: $username" -ForegroundColor Cyan
        return $username
    }
}
