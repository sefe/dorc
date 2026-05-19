using module ".\EndurUsers.psm1"
using module ".\Models.psm1"
using module ".\PropertiesHelper.psm1"
using module ".\ApiCaller.psm1"

$script:DOrcIdentityServerAuthorityUrl = $null
$script:DOrcIdentityServerTokenEndpoint = "/connect/token"
$script:DOrcIdentityServerClientId = $null
$script:DOrcIdentityServerScope = $null
$script:DOrcActiveHttpListener = $null

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

    if ([string]::IsNullOrWhiteSpace([ApiCaller]::AccessToken))
    {
        Connect-DOrcIdentityServer -ApiUrl $ApiUrl | Out-Null
    }

    if ([string]::IsNullOrWhiteSpace([ApiCaller]::ImpersonateUser))
    {
        Set-DOrcImpersonateUser
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
        [string]$CsvFile
    )

    if ([string]::IsNullOrWhiteSpace([ApiCaller]::AccessToken))
    {
        Connect-DOrcIdentityServer -ApiUrl $ApiUrl | Out-Null
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
            Write-Host "[Open-ApiConnection] API endpoint discovery failed" -ForegroundColor Red
            Write-Host "[Open-ApiConnection] ApiUrl: $ApiUrl" -ForegroundColor Yellow
            Write-Host "[Open-ApiConnection] ReturnCode: $($result.ReturnCode)" -ForegroundColor Yellow
            if (-not [string]::IsNullOrWhiteSpace($result.Message))
            {
                Write-Host "[Open-ApiConnection] Message: $($result.Message)" -ForegroundColor Yellow
            }
            Write-Host "[Open-ApiConnection] Bearer token set: $(-not [string]::IsNullOrWhiteSpace([ApiCaller]::AccessToken))" -ForegroundColor Yellow
            throw "API returned non-success status code"
        }
    }
    catch
    {
        Write-Host "[Open-ApiConnection] Exception while connecting" -ForegroundColor Red
        Write-Host "[Open-ApiConnection] ApiUrl: $ApiUrl" -ForegroundColor Yellow
        Write-Host "[Open-ApiConnection] Error: $($_.Exception.Message)" -ForegroundColor Yellow
        if ($_.Exception.InnerException)
        {
            Write-Host "[Open-ApiConnection] InnerError: $($_.Exception.InnerException.Message)" -ForegroundColor Yellow
        }
        if ($_.ScriptStackTrace)
        {
            Write-Host "[Open-ApiConnection] Stack: $($_.ScriptStackTrace)" -ForegroundColor DarkYellow
        }
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

function Get-DOrcCurrentUserIdentity
{
    try
    {
        $azContext = Get-AzContext -ErrorAction SilentlyContinue
        if ($azContext -and $azContext.Account -and $azContext.Account.Id)
        {
            return $azContext.Account.Id
        }
    }
    catch
    {
        # Ignore if Az module is unavailable.
    }

    if (-not [string]::IsNullOrEmpty($env:USERDNSDOMAIN) -and -not [string]::IsNullOrEmpty($env:USERNAME))
    {
        return "$env:USERNAME@$($env:USERDNSDOMAIN.ToLower())"
    }

    return [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
}

function Resolve-DOrcUsernameFromToken
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Token
    )

    try
    {
        $parts = $Token.Split('.')
        if ($parts.Length -lt 2)
        {
            return $null
        }

        $payload = $parts[1].Replace('-', '+').Replace('_', '/')
        switch ($payload.Length % 4)
        {
            2 { $payload += '==' }
            3 { $payload += '=' }
        }

        $jsonPayload = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String($payload))
        $claims = $jsonPayload | ConvertFrom-Json

        if ($claims.preferred_username)
        {
            return [string]$claims.preferred_username
        }
        if ($claims.upn)
        {
            return [string]$claims.upn
        }
        if ($claims.unique_name)
        {
            return [string]$claims.unique_name
        }
        if ($claims.name)
        {
            return [string]$claims.name
        }
    }
    catch
    {
        return $null
    }

    return $null
}

function Set-DOrcIdentityServerConfig
{
    <#
    .SYNOPSIS
    Sets Identity Server settings used for delegated user authentication.

    .PARAMETER AuthorityUrl
    Base URL of Identity Server.

    .PARAMETER TokenEndpoint
    Relative token endpoint path. Defaults to /connect/token.

    .PARAMETER ClientId
    OAuth client ID used when requesting tokens.

    .PARAMETER Scope
    Space-separated OAuth scopes.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$AuthorityUrl,
        [Parameter(Mandatory = $false)]
        [ValidateNotNullOrEmpty()]
        [string]$TokenEndpoint = "/connect/token",
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$ClientId,
        [Parameter(Mandatory = $false)]
        [ValidateNotNullOrEmpty()]
        [string]$Scope = "openid profile"
    )

    $script:DOrcIdentityServerAuthorityUrl = $AuthorityUrl.TrimEnd('/')
    $script:DOrcIdentityServerTokenEndpoint = $TokenEndpoint
    $script:DOrcIdentityServerClientId = $ClientId
    $script:DOrcIdentityServerScope = $Scope

    Write-Host "Identity Server configuration updated" -ForegroundColor Green
}

function Get-DOrcIdentityServerConfig
{
    <#
    .SYNOPSIS
    Returns current Identity Server settings for this PowerShell session.
    #>
    return [pscustomobject]@{
        AuthorityUrl = $script:DOrcIdentityServerAuthorityUrl
        TokenEndpoint = $script:DOrcIdentityServerTokenEndpoint
        ClientId = $script:DOrcIdentityServerClientId
        Scope = $script:DOrcIdentityServerScope
    }
}

function Resolve-DOrcIdentityAuthorityFromApiConfig
{
    <#
    .SYNOPSIS
    Resolves Identity Server authority from DOrc API configuration endpoint.

    .DESCRIPTION
    Calls <ApiUrl>/ApiConfig and returns OAuthAuthority value.
    #>
    param(
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrEmpty()]
        [string]$ApiUrl
    )

    $normalizedApiUrl = $ApiUrl.TrimEnd('/')
    $apiConfigUrl = "$normalizedApiUrl/ApiConfig"

    try
    {
        $apiConfigResponse = Invoke-WebRequest -Uri $apiConfigUrl -Method GET -UseBasicParsing
        $apiConfig = $apiConfigResponse.Content | ConvertFrom-Json
    }
    catch
    {
        throw "Failed to retrieve ApiConfig from $apiConfigUrl. Error: $($_.Exception.Message)"
    }

    if (-not $apiConfig -or [string]::IsNullOrWhiteSpace($apiConfig.OAuthAuthority))
    {
        throw "ApiConfig at $apiConfigUrl does not contain OAuthAuthority."
    }

    $resolvedClientId = $null
    if (-not [string]::IsNullOrWhiteSpace($apiConfig.OAuthUiClientId))
    {
         $resolvedClientId = [string]$apiConfig.OAuthUiClientId }

    $resolvedScope = $null
    if (-not [string]::IsNullOrWhiteSpace($apiConfig.OAuthUiRequestedScopes)) 
    {
        $resolvedScope = [string]$apiConfig.OAuthUiRequestedScopes
    }


    if ([string]::IsNullOrWhiteSpace($resolvedClientId))
    {
        throw "ApiConfig at $apiConfigUrl does not contain OAuth client id (expected OAuthUiClientId)."
    }

    if ([string]::IsNullOrWhiteSpace($resolvedScope))
    {
        throw "ApiConfig at $apiConfigUrl does not contain OAuth scope (expected OAuthUiRequestedScopes)."
    }

    return [pscustomobject]@{
        AuthorityUrl = ([string]$apiConfig.OAuthAuthority).TrimEnd('/')
        ClientId = $resolvedClientId
        Scope = $resolvedScope
    }
}

function Connect-DOrcIdentityServer
{
    <#
    .SYNOPSIS
    Authenticates the current user against Identity Server via a browser login window.

    .DESCRIPTION
    Opens the system browser to the Identity Server login page using the
    Authorization Code + PKCE flow (RFC 7636). A local HTTP listener receives the
    redirect callback, exchanges the authorization code for tokens and stores the
    access token for subsequent DORC API calls.

    The username embedded in the returned JWT is automatically set as the audit
    identity (X-Impersonate-User header) so every import appears in DOrc Audit
    under the real user name.

    .PARAMETER Scope
    Optional scopes to request. Defaults to the configured module scope.

    .PARAMETER ApiUrl
    Optional DOrc API URL. If provided, Identity Server authority is resolved from
    <ApiUrl>/ApiConfig (OAuthAuthority) to avoid hardcoded Identity Server URL.

    .PARAMETER TimeoutSeconds
    Seconds to wait for the browser login to complete. Default: 120.

    .EXAMPLE
    Connect-DOrcIdentityServer
    #>
    param(
        [Parameter(Mandatory = $false)]
        [string]$Scope,
        [Parameter(Mandatory = $false)]
        [string]$ApiUrl,
        [Parameter(Mandatory = $false)]
        [int]$TimeoutSeconds = 120
    )

    $clientId = $script:DOrcIdentityServerClientId
    if (-not [string]::IsNullOrWhiteSpace($ApiUrl))
    {
        $oauthConfig = Resolve-DOrcIdentityAuthorityFromApiConfig -ApiUrl $ApiUrl
        $authorityBase = $oauthConfig.AuthorityUrl
        $clientId = $oauthConfig.ClientId
        if ([string]::IsNullOrWhiteSpace($Scope))
        {
            $Scope = $oauthConfig.Scope
        }
        Write-Host "Identity Server authority resolved from ApiConfig: $authorityBase" -ForegroundColor Cyan
    }
    else
    {
        $authorityBase = $script:DOrcIdentityServerAuthorityUrl.TrimEnd('/')
        if ([string]::IsNullOrWhiteSpace($Scope))
        {
            $Scope = $script:DOrcIdentityServerScope
        }
    }

    if ([string]::IsNullOrWhiteSpace($clientId))
    {
        throw "OAuth client id is not set. Provide -ApiUrl with valid /ApiConfig or configure it via Set-DOrcIdentityServerConfig."
    }

    if ([string]::IsNullOrWhiteSpace($Scope))
    {
        throw "OAuth scope is not set. Provide -ApiUrl with valid /ApiConfig or configure it via Set-DOrcIdentityServerConfig."
    }

    # --- PKCE: cryptographically random code verifier ---
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $verifierBytes = New-Object byte[] 64
    $rng.GetBytes($verifierBytes)
    $rng.Dispose()
    $codeVerifier = [System.Convert]::ToBase64String($verifierBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')

    # --- PKCE: code challenge = BASE64URL(SHA256(verifier)) ---
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try
    {
        $challengeBytes = $sha256.ComputeHash([System.Text.Encoding]::ASCII.GetBytes($codeVerifier))
        $codeChallenge = [System.Convert]::ToBase64String($challengeBytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
    }
    finally
    {
        $sha256.Dispose()
    }

    # --- State token to prevent CSRF ---
    $state = [System.Guid]::NewGuid().ToString("N")

    # --- Bind the redirect listener to the required fixed port ---
    $port = 8888

    if ($script:DOrcActiveHttpListener)
    {
        try
        {
            $script:DOrcActiveHttpListener.Stop()
            $script:DOrcActiveHttpListener.Close()
        }
        catch
        {
            # Best effort cleanup only.
        }
        finally
        {
            $script:DOrcActiveHttpListener = $null
        }
    }

    $listener = [System.Net.HttpListener]::new()
    $listenerPrefix = "http://localhost:$port/signin-callback.html/"
    try
    {
        $listener.Prefixes.Add($listenerPrefix)
        $listener.Start()
        $script:DOrcActiveHttpListener = $listener
    }
    catch
    {
        $bindError = $_.Exception.Message
        if ($bindError -match "Access is denied")
        {
            $currentUser = "$env:USERDOMAIN\$env:USERNAME"
            throw "Could not bind to localhost port 8888 for the OAuth2 callback listener. Register URL ACL once as administrator: netsh http add urlacl url=http://localhost:8888/ user=$currentUser. Original error: $bindError"
        }

        if ($bindError -match "being used by another process")
        {
            $ownerInfo = ""
            try
            {
                $tcp = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction Stop | Select-Object -First 1
                if ($tcp)
                {
                    $proc = Get-Process -Id $tcp.OwningProcess -ErrorAction SilentlyContinue
                    if ($proc)
                    {
                        $ownerInfo = " Port is owned by process $($proc.ProcessName) (PID $($proc.Id))."
                    }
                    else
                    {
                        $ownerInfo = " Port is owned by PID $($tcp.OwningProcess)."
                    }
                }
            }
            catch
            {
                # Best effort diagnostics only.
            }

            throw "Could not bind to localhost port 8888 for the OAuth2 callback listener.$ownerInfo Original error: $bindError"
        }

        throw "Could not bind to localhost port 8888 for the OAuth2 callback listener. Original error: $bindError"
    }

    $redirectUri = "http://localhost:$port/signin-callback.html"

    # --- Build authorisation URL ---
    $authUrl = "$authorityBase/connect/authorize" +
        "?client_id=$([Uri]::EscapeDataString($clientId))" +
        "&redirect_uri=$([Uri]::EscapeDataString($redirectUri))" +
        "&response_type=code" +
        "&scope=$([Uri]::EscapeDataString($Scope))" +
        "&code_challenge=$codeChallenge" +
        "&code_challenge_method=S256" +
        "&state=$state"

    Write-Host "Opening browser for Identity Server login..." -ForegroundColor Cyan
    Start-Process $authUrl

    try
    {
        # --- Wait for browser redirect with timeout ---
        $contextTask = $listener.GetContextAsync()
        if (-not $contextTask.Wait([TimeSpan]::FromSeconds($TimeoutSeconds)))
        {
            throw "Timed out waiting for browser authentication ($TimeoutSeconds seconds)."
        }

        $context = $contextTask.Result
        $requestUri = $context.Request.Url

        # Return a simple close-me page to the browser
        $html = '<html><head><title>DOrc Login</title></head><body style="font-family:sans-serif;text-align:center;margin-top:60px"><h2>Authentication complete &#10003;</h2><p>You can close this browser tab and return to PowerShell.</p></body></html>'
        $htmlBytes = [System.Text.Encoding]::UTF8.GetBytes($html)
        $context.Response.ContentType = 'text/html; charset=utf-8'
        $context.Response.ContentLength64 = $htmlBytes.Length
        $context.Response.OutputStream.Write($htmlBytes, 0, $htmlBytes.Length)
        $context.Response.Close()

        # --- Parse query parameters ---
        $queryParams = @{}
        foreach ($pair in $requestUri.Query.TrimStart('?').Split('&'))
        {
            $kv = $pair.Split('=', 2)
            if ($kv.Length -eq 2)
            {
                $queryParams[[Uri]::UnescapeDataString($kv[0])] = [Uri]::UnescapeDataString($kv[1])
            }
        }

        if ($queryParams.ContainsKey('error'))
        {
            $desc = if ($queryParams.ContainsKey('error_description')) { " - $($queryParams['error_description'])" } else { '' }
            throw "Identity Server returned error: $($queryParams['error'])$desc"
        }

        if (-not $queryParams.ContainsKey('code'))
        {
            throw "No authorization code received from Identity Server."
        }

        if ($queryParams['state'] -ne $state)
        {
            throw "OAuth2 state mismatch in callback. Possible CSRF attempt - authentication aborted."
        }

        # --- Exchange code for tokens ---
        $tokenUrl = "$authorityBase$script:DOrcIdentityServerTokenEndpoint"
        $tokenBody = @{
            grant_type   = 'authorization_code'
            client_id    = $clientId
            code         = $queryParams['code']
            redirect_uri = $redirectUri
            code_verifier = $codeVerifier
        }

        $tokenResponse = Invoke-RestMethod -Method POST -Uri $tokenUrl -Body $tokenBody -ContentType 'application/x-www-form-urlencoded'

        if (-not $tokenResponse.access_token)
        {
            throw "Identity Server did not return an access token."
        }

        $accessToken = ([string]$tokenResponse.access_token).Trim()
        if ($accessToken -match '^(?i)Bearer\s+')
        {
            $accessToken = $accessToken -replace '^(?i)Bearer\s+', ''
        }

        [ApiCaller]::SetAccessToken($accessToken)

        # Audit user: prefer claims from JWT, fall back to Windows identity
        if ([string]::IsNullOrWhiteSpace([ApiCaller]::ImpersonateUser))
        {
            $auditUser = Resolve-DOrcUsernameFromToken -Token $accessToken
            if ([string]::IsNullOrWhiteSpace($auditUser))
            {
                $auditUser = Get-DOrcCurrentUserIdentity
            }
            Set-DOrcImpersonateUser -Username $auditUser
        }

        Write-Host "Identity Server authentication succeeded" -ForegroundColor Green
        return $true
    }
    catch
    {
        $message = $_.Exception.Message
        if ($_.ErrorDetails -and $_.ErrorDetails.Message)
        {
            try { $message = ($_.ErrorDetails.Message | ConvertFrom-Json).error_description } catch {}
            if ([string]::IsNullOrWhiteSpace($message)) { $message = $_.ErrorDetails.Message }
        }
        throw "Identity Server authentication failed: $message"
    }
    finally
    {
        try
        {
            $listener.Stop()
            $listener.Close()
        }
        catch
        {
            # Listener might already be closed.
        }
        finally
        {
            if ($script:DOrcActiveHttpListener -eq $listener)
            {
                $script:DOrcActiveHttpListener = $null
            }
        }
    }
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
        $Username = Get-DOrcCurrentUserIdentity
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
