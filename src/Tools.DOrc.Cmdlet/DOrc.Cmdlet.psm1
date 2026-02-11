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
        [string]$CsvFile
    )

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
        [string]$CsvFile
    )

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



# ============================================================================
# Identity Server Authentication Functions for DOrc.Cmdlet Module
# ============================================================================

function Connect-DOrcWithIdentityServer
{
    <#
    .SYNOPSIS
    Authenticates with Identity Server using credentials and configures the module to use bearer token authentication.
    
    .DESCRIPTION
    Automatically discovers the Identity Server endpoint from the DORC API configuration,
    obtains an OAuth2 access token using client credentials flow, and sets it for all
    subsequent API calls in the module.
    
    .PARAMETER ApiUrl
    The base URL of the DORC API (e.g., "https://deploymentportal:8443/")
    
    .PARAMETER ClientId
    The OAuth2 client ID (e.g., "dorc-cli")
    
    .PARAMETER ClientSecret
    The OAuth2 client secret
    
    .PARAMETER Scope
    The OAuth2 scope to request. Use "dorc-api.manage" for production or "dorc-api-np.manage" for non-production.
    
    .EXAMPLE
    # Production
    Connect-DOrcWithIdentityServer -ApiUrl "https://deploymentportal:8443/" `
                                    -ClientId "dorc-cli" `
                                    -ClientSecret $secret `
                                    -Scope "dorc-api.manage"
    
    .EXAMPLE
    # Non-Production
    Connect-DOrcWithIdentityServer -ApiUrl "https://deploymentportalqa:8443/" `
                                    -ClientId "dorc-cli" `
                                    -ClientSecret $secret `
                                    -Scope "dorc-api-np.manage"
    
    .NOTES
    The function auto-discovers the Identity Server endpoint by calling /ApiConfig on the DORC API.
    #>
    param (
        [Parameter(Mandatory = $true)]
        [string]$ApiUrl,
        
        [Parameter(Mandatory = $true)]
        [string]$ClientId,
        
        [Parameter(Mandatory = $true)]
        [string]$ClientSecret,
        
        [Parameter(Mandatory = $true)]
        [string]$Scope
    )
    
    try
    {
        Write-Host "Discovering Identity Server endpoint from DORC API..." -ForegroundColor Yellow
        
        # Normalize API URL
        $ApiUrl = $ApiUrl.TrimEnd('/')
        
        # Auto-discover Identity Server endpoint from DORC API
        $apiConfigUrl = "$ApiUrl/ApiConfig"
        $apiConfig = Invoke-RestMethod -Method GET -Uri $apiConfigUrl
        
        if (-not $apiConfig.OAuthAuthority)
        {
            throw "Unable to retrieve OAuthAuthority from DORC API configuration at $apiConfigUrl"
        }
        
        $tokenEndpoint = $apiConfig.OAuthAuthority + "/connect/token"
        Write-Host "Identity Server endpoint: $tokenEndpoint" -ForegroundColor Cyan
        
        # Prepare OAuth2 client credentials request
        $headers = @{
            "Content-Type" = "application/x-www-form-urlencoded"
        }
        
        $formData = @{
            "grant_type"    = "client_credentials"
            "client_id"     = $ClientId
            "client_secret" = $ClientSecret
            "scope"         = $Scope
        }
        
        Write-Host "Authenticating with Identity Server..." -ForegroundColor Yellow
        
        # Request token
        $response = Invoke-WebRequest -Uri $tokenEndpoint -Method POST -Headers $headers -Body $formData -UseBasicParsing
        $tokenResponse = $response.Content | ConvertFrom-Json
        
        if (-not $tokenResponse.access_token)
        {
            throw "No access token received from Identity Server"
        }
        
        # Set the token in ApiCaller for all subsequent calls
        [ApiCaller]::SetAccessToken($tokenResponse.access_token)
        
        Write-Host "Successfully authenticated!" -ForegroundColor Green
        Write-Host "Token expires in: $($tokenResponse.expires_in) seconds" -ForegroundColor Cyan
        
        # Store token metadata globally for reference
        $global:DOrcAuthToken = @{
            Token           = $tokenResponse.access_token
            ExpiresIn       = $tokenResponse.expires_in
            ExpiresAt       = (Get-Date).AddSeconds($tokenResponse.expires_in)
            Scope           = $Scope
            TokenEndpoint   = $tokenEndpoint
        }
    }
    catch
    {
        Write-Host "Authentication failed: $($_.Exception.Message)" -ForegroundColor Red
        Write-Host "`nPlease verify:" -ForegroundColor Yellow
        Write-Host "  1. DORC API URL is correct and accessible: $ApiUrl" -ForegroundColor Yellow
        Write-Host "  2. Client ID exists in Identity Server: $ClientId" -ForegroundColor Yellow
        Write-Host "  3. Client secret is correct" -ForegroundColor Yellow
        Write-Host "  4. Client has 'client_credentials' grant type enabled" -ForegroundColor Yellow
        Write-Host "  5. Client has access to scope: $Scope" -ForegroundColor Yellow
        throw
    }
}

function Disconnect-DOrcIdentityServer
{
    <#
    .SYNOPSIS
    Clears the stored Identity Server access token.
    
    .DESCRIPTION
    Removes the access token from memory. Subsequent API calls will use
    Windows Integrated Authentication (UseDefaultCredentials) unless a new token is set.
    
    .EXAMPLE
    Disconnect-DOrcIdentityServer
    #>
    
    [ApiCaller]::ClearAccessToken()
    Remove-Variable -Name DOrcAuthToken -Scope Global -ErrorAction SilentlyContinue
    Write-Host "Identity Server token cleared. Module will use Windows Authentication." -ForegroundColor Green
}

function Get-DOrcTokenInfo
{
    <#
    .SYNOPSIS
    Displays information about the current authentication token.
    
    .DESCRIPTION
    Shows token expiration time and whether re-authentication is needed.
    
    .EXAMPLE
    Get-DOrcTokenInfo
    #>
    
    if (-not $global:DOrcAuthToken)
    {
        Write-Host "No Identity Server token is currently set." -ForegroundColor Yellow
        Write-Host "Module is using Windows Integrated Authentication." -ForegroundColor Cyan
        return
    }
    
    $now = Get-Date
    $expiresAt = $global:DOrcAuthToken.ExpiresAt
    $timeRemaining = $expiresAt - $now
    
    Write-Host "`nCurrent Token Information:" -ForegroundColor Cyan
    Write-Host "  Scope:            $($global:DOrcAuthToken.Scope)" -ForegroundColor White
    Write-Host "  Token Endpoint:   $($global:DOrcAuthToken.TokenEndpoint)" -ForegroundColor White
    Write-Host "  Expires At:       $expiresAt" -ForegroundColor White
    
    if ($timeRemaining.TotalSeconds -gt 0)
    {
        Write-Host "  Time Remaining:   $([int]$timeRemaining.TotalMinutes) minutes, $($timeRemaining.Seconds) seconds" -ForegroundColor Green
        Write-Host "  Status:           VALID" -ForegroundColor Green
    }
    else
    {
        Write-Host "  Time Remaining:   EXPIRED" -ForegroundColor Red
        Write-Host "  Status:           EXPIRED - Re-authentication required" -ForegroundColor Red
    }
}

function Test-DOrcTokenExpiration
{
    <#
    .SYNOPSIS
    Checks if the current token is expired or about to expire.
    
    .DESCRIPTION
    Returns $true if no token is set, token is expired, or will expire in the next 60 seconds.
    Returns $false if token is valid and has more than 60 seconds remaining.
    
    .PARAMETER BufferSeconds
    Number of seconds before expiration to consider token as expired (default: 60)
    
    .EXAMPLE
    if (Test-DOrcTokenExpiration) { 
        Connect-DOrcWithIdentityServer -ApiUrl $url -ClientId $id -ClientSecret $secret -Scope $scope
    }
    #>
    param (
        [int]$BufferSeconds = 60
    )
    
    if (-not $global:DOrcAuthToken)
    {
        return $true  # No token set, needs authentication
    }
    
    $expiresAt = $global:DOrcAuthToken.ExpiresAt.AddSeconds(-$BufferSeconds)
    $isExpired = (Get-Date) -gt $expiresAt
    
    return $isExpired
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
