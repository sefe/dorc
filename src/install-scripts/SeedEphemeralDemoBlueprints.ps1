<#
.SYNOPSIS
    Idempotently seeds the Ephemeral Environments & Cloud Terraform Catalog showcase
    project, non-prod base environments, catalog/validation components, saved module
    inputs, and SEFE tier-weighted blueprint templates against a non-production DOrc API.

.DESCRIPTION
    Standalone post-deployment demo/showcase seeding script (intentionally kept separate
    from RunDeployment.ps1 so it only runs when explicitly invoked).

    Safety guarantees:
      - Unconditionally refuses to run against any Production or DR target (PR/PR01/PR02,
        bare https://deploymentportal/, GMPR-* hosts, ENVIRONMENTISPRODUCTION=TRUE,
        /ApiConfig.IsProduction=true, dorc-ui-pr / dorc-api-pr OAuth scopes, or any
        existing environment with EnvironmentIsProd=true). There is no override switch.
      - Fully idempotent: safe to re-run after every non-prod deployment. Existing base
        environments, project metadata, ACL entries, component pins, environment property
        values, saved catalog inputs, and blueprint templates are updated in place without
        duplicating records or clobbering existing permissions.

.EXAMPLE
    .\src\install-scripts\SeedEphemeralDemoBlueprints.ps1 -ApiBaseUrl "https://deploymentportalut2:8443"

.EXAMPLE
    .\src\install-scripts\SeedEphemeralDemoBlueprints.ps1 -ApiBaseUrl "https://deploymentportalut2:8443" -BearerToken $token
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [ValidateNotNullOrEmpty()]
    [string]$ApiBaseUrl = "https://deploymentportalut2:8443",

    [Parameter(Mandatory = $false)]
    [string]$BearerToken = $env:DORC_BEARER_TOKEN,

    [Parameter(Mandatory = $false)]
    [string]$ClientId,

    [Parameter(Mandatory = $false)]
    [string]$ClientSecret,

    [Parameter(Mandatory = $false)]
    [switch]$UseDefaultCredentials,

    [Parameter(Mandatory = $false)]
    [ValidateRange(15, 600)]
    [int]$AuthTimeoutSeconds = 120,

    [Parameter(Mandatory = $false)]
    [ValidateLength(3, 64)]
    [string]$ProjectName = "Ephemeral Cloud Blueprints Demo",

    [Parameter(Mandatory = $false)]
    [ValidateLength(3, 64)]
    [string]$DevBaseEnvironmentName = "TRV-DV-01",

    [Parameter(Mandatory = $false)]
    [ValidateLength(3, 64)]
    [string]$UatBaseEnvironmentName = "TRV-UT-01",

    [Parameter(Mandatory = $false)]
    [ref]$OutBearerToken,

    [Parameter(Mandatory = $false)]
    [switch]$PassThru,

    [Parameter(Mandatory = $false)]
    [switch]$RunProductionGuardSelfTest
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

#region Production Refusal Guards (Fail-Closed, Non-Bypassable)

function Assert-NonProductionLocalContext {
    param(
        [Parameter(Mandatory = $true)]
        [string]$TargetApiBaseUrl,
        [Parameter(Mandatory = $true)]
        [string[]]$CandidateEnvironmentNames,
        [Parameter(Mandatory = $false)]
        [string]$EnvIsProductionVar = $env:ENVIRONMENTISPRODUCTION,
        [Parameter(Mandatory = $false)]
        [string]$TargetEnvNameVar = $env:TARGET_ENVNAME
    )

    if (-not [string]::IsNullOrWhiteSpace($EnvIsProductionVar) -and
        $EnvIsProductionVar.Trim() -match '^(?i)(true|1|yes)$') {
        throw "PRODUCTION GUARD REFUSED: Environment variable ENVIRONMENTISPRODUCTION='$EnvIsProductionVar'. Demo seeding is strictly prohibited against production."
    }

    if (-not [string]::IsNullOrWhiteSpace($TargetEnvNameVar) -and
        $TargetEnvNameVar -match '(?i)(^|[\s._-])(pr|pr\d+|prod|production|live|dr|dr\d+)($|[\s._-])') {
        throw "PRODUCTION GUARD REFUSED: TARGET_ENVNAME='$TargetEnvNameVar' matches a production or DR tier."
    }

    $parsedUri = $null
    if (-not [Uri]::TryCreate($TargetApiBaseUrl.Trim(), [UriKind]::Absolute, [ref]$parsedUri) -or
        ($parsedUri.Scheme -ne 'https' -and $parsedUri.Scheme -ne 'http')) {
        throw "Invalid ApiBaseUrl '$TargetApiBaseUrl'. Provide an absolute http(s) DOrc API URL."
    }

    $hostLower = $parsedUri.Host.ToLowerInvariant()

    $knownProdHosts = @(
        'deploymentportal',
        'deploymentportal.sefe.eu',
        'deploymentportal.gazprom-mt.com',
        'deploymentportal.gazpromuk.intra'
    )
    if ($knownProdHosts -contains $hostLower) {
        throw "PRODUCTION GUARD REFUSED: Host '$hostLower' is the production DOrc portal. Demo seeding is strictly prohibited against production."
    }

    if ($hostLower -match '(?i)(^|[-_.])(pr|pr\d+|prod|production|live|dr|dr\d+)($|[-_.])' -or
        $hostLower -match '(?i)deploymentportal(pr|prod|production|live|dr)\d*($|\.)' -or
        $hostLower -match '(?i)^gm(pr|dr)[-_0-9]') {
        throw "PRODUCTION GUARD REFUSED: Host '$hostLower' matches a production or DR naming pattern."
    }

    foreach ($candidate in $CandidateEnvironmentNames) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            throw "Base environment names must not be empty."
        }
        if ($candidate -match '(?i)(^|[\s._-])(pr|pr\d+|prod|production|live|dr|dr\d+)($|[\s._-])') {
            throw "PRODUCTION GUARD REFUSED: Base environment name '$candidate' uses a production or DR tier token."
        }
    }
}

function Assert-NonProductionApiConfig {
    param(
        [Parameter(Mandatory = $true)]
        [string]$TargetApiBaseUrl,
        [Parameter(Mandatory = $false)]
        $ApiConfig
    )

    if ($null -eq $ApiConfig) {
        throw "PRODUCTION GUARD REFUSED: '$TargetApiBaseUrl/ApiConfig' returned null; failing closed because non-production status could not be verified."
    }

    $isProdProp = $ApiConfig.PSObject.Properties['IsProduction']
    if ($null -eq $isProdProp -or $null -eq $isProdProp.Value) {
        throw "PRODUCTION GUARD REFUSED: '$TargetApiBaseUrl/ApiConfig' did not report an explicit IsProduction boolean; failing closed."
    }

    if ([bool]$isProdProp.Value) {
        throw "PRODUCTION GUARD REFUSED: '$TargetApiBaseUrl/ApiConfig' reports IsProduction=true. Demo seeding is strictly prohibited against production."
    }

    $oauthClientProp = $ApiConfig.PSObject.Properties['OAuthUiClientId']
    $oauthScopesProp = $ApiConfig.PSObject.Properties['OAuthUiRequestedScopes']
    $oauthClient = if ($null -ne $oauthClientProp -and $null -ne $oauthClientProp.Value) { [string]$oauthClientProp.Value } else { '' }
    $oauthScopes = if ($null -ne $oauthScopesProp -and $null -ne $oauthScopesProp.Value) { [string]$oauthScopesProp.Value } else { '' }
    $oauthCombined = "$oauthClient $oauthScopes"
    if ($oauthCombined -match '(?i)(^|[\s._-])(dorc-ui-pr|dorc-api-pr|dorc-ids-client-pr)($|[\s._-])' -or
        $oauthClient -match '(?i)-(pr|prod)\d*$') {
        throw "PRODUCTION GUARD REFUSED: '$TargetApiBaseUrl/ApiConfig' references production OAuth client/scopes ('$oauthCombined')."
    }
}

function Invoke-ProductionGuardSelfTest {
    Write-Host "Running production guard self-tests..." -ForegroundColor Cyan

    $blockedUrls = @(
        'https://deploymentportal:8443',
        'https://deploymentportal.sefe.eu:8443',
        'https://deploymentportalpr:8443',
        'https://deploymentportalpr2:8443',
        'https://deploymentportal-prod:8443',
        'https://dorc-pr.sefe.eu:8443',
        'https://gmpr-dorapp01.gazpromuk.intra:8443',
        'https://dorc-dr01:8443'
    )
    foreach ($badUrl in $blockedUrls) {
        $threw = $false
        try {
            Assert-NonProductionLocalContext -TargetApiBaseUrl $badUrl -CandidateEnvironmentNames @('TRV-DV-01', 'TRV-UT-01') -EnvIsProductionVar 'FALSE' -TargetEnvNameVar 'DOrc UT 02'
        } catch {
            $threw = $true
        }
        if (-not $threw) {
            throw "Self-test failed: expected URL '$badUrl' to be blocked by production guard."
        }
    }

    $threwEnvVar = $false
    try {
        Assert-NonProductionLocalContext -TargetApiBaseUrl 'https://deploymentportalut2:8443' -CandidateEnvironmentNames @('TRV-DV-01') -EnvIsProductionVar 'TRUE' -TargetEnvNameVar ''
    } catch {
        $threwEnvVar = $true
    }
    if (-not $threwEnvVar) {
        throw "Self-test failed: expected ENVIRONMENTISPRODUCTION=TRUE to be blocked."
    }

    $threwTargetEnv = $false
    try {
        Assert-NonProductionLocalContext -TargetApiBaseUrl 'https://deploymentportalut2:8443' -CandidateEnvironmentNames @('TRV-DV-01') -EnvIsProductionVar 'FALSE' -TargetEnvNameVar 'DOrc PR 02'
    } catch {
        $threwTargetEnv = $true
    }
    if (-not $threwTargetEnv) {
        throw "Self-test failed: expected TARGET_ENVNAME='DOrc PR 02' to be blocked."
    }

    $threwApiConfigProd = $false
    try {
        Assert-NonProductionApiConfig -TargetApiBaseUrl 'https://deploymentportalut2:8443' -ApiConfig ([pscustomobject]@{ IsProduction = $true; OAuthUiClientId = 'dorc-ui-np' })
    } catch {
        $threwApiConfigProd = $true
    }
    if (-not $threwApiConfigProd) {
        throw "Self-test failed: expected IsProduction=true in ApiConfig to be blocked."
    }

    $threwApiConfigMissing = $false
    try {
        Assert-NonProductionApiConfig -TargetApiBaseUrl 'https://deploymentportalut2:8443' -ApiConfig ([pscustomobject]@{ AuthenticationScheme = 'OAuth' })
    } catch {
        $threwApiConfigMissing = $true
    }
    if (-not $threwApiConfigMissing) {
        throw "Self-test failed: expected missing IsProduction in ApiConfig to fail closed."
    }

    $threwOAuthPr = $false
    try {
        Assert-NonProductionApiConfig -TargetApiBaseUrl 'https://deploymentportalut2:8443' -ApiConfig ([pscustomobject]@{ IsProduction = $false; OAuthUiClientId = 'dorc-ui-pr'; OAuthUiRequestedScopes = 'dorc-api-pr.manage' })
    } catch {
        $threwOAuthPr = $true
    }
    if (-not $threwOAuthPr) {
        throw "Self-test failed: expected production OAuth client/scopes to be blocked."
    }

    # Verify legitimate non-prod targets pass
    Assert-NonProductionLocalContext -TargetApiBaseUrl 'https://deploymentportalut2:8443' -CandidateEnvironmentNames @('TRV-DV-01', 'TRV-UT-01') -EnvIsProductionVar 'FALSE' -TargetEnvNameVar 'DOrc UT 02'
    Assert-NonProductionLocalContext -TargetApiBaseUrl 'https://deploymentportaldv1:8443' -CandidateEnvironmentNames @('TRV-DV-01', 'TRV-UT-01') -EnvIsProductionVar 'FALSE' -TargetEnvNameVar 'DOrc DV 01'
    Assert-NonProductionApiConfig -TargetApiBaseUrl 'https://deploymentportalut2:8443' -ApiConfig ([pscustomobject]@{ IsProduction = $false; OAuthUiClientId = 'dorc-ui-np'; OAuthUiRequestedScopes = 'openid profile dorc-api-np.manage' })

    Write-Host "All production guard self-tests passed." -ForegroundColor Green
}

if ($RunProductionGuardSelfTest) {
    Invoke-ProductionGuardSelfTest
    return
}

#endregion Production Refusal Guards

#region HTTP & OAuth PKCE Helpers

$normalizedApiBaseUrl = $ApiBaseUrl.TrimEnd('/')

function ConvertTo-Base64Url {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)
    return [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Resolve-JwtPrincipalLabel {
    param([Parameter(Mandatory = $false)][string]$Token)
    if ([string]::IsNullOrWhiteSpace($Token)) { return $null }
    try {
        $parts = $Token.Split('.')
        if ($parts.Length -lt 2) { return $null }
        $payload = $parts[1].Replace('-', '+').Replace('_', '/')
        switch ($payload.Length % 4) {
            2 { $payload += '==' }
            3 { $payload += '=' }
        }
        $claims = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payload)) | ConvertFrom-Json
        foreach ($field in @('preferred_username', 'upn', 'unique_name', 'email', 'name', 'sub', 'client_id')) {
            $prop = $claims.PSObject.Properties[$field]
            if ($null -ne $prop -and -not [string]::IsNullOrWhiteSpace([string]$prop.Value)) {
                return [string]$prop.Value
            }
        }
    } catch {
        return $null
    }
    return $null
}

function Get-DOrcOAuthTokenClientCredentials {
    param(
        [Parameter(Mandatory = $true)][string]$AuthorityUrl,
        [Parameter(Mandatory = $true)][string]$OAuthClientId,
        [Parameter(Mandatory = $true)][string]$OAuthClientSecret,
        [Parameter(Mandatory = $false)][string]$Scope
    )

    $tokenUrl = "$($AuthorityUrl.TrimEnd('/'))/connect/token"
    $body = @{
        grant_type    = 'client_credentials'
        client_id     = $OAuthClientId
        client_secret = $OAuthClientSecret
    }
    if (-not [string]::IsNullOrWhiteSpace($Scope)) {
        $body['scope'] = $Scope
    }

    $resp = Invoke-RestMethod -Method Post -Uri $tokenUrl -Body $body -ContentType 'application/x-www-form-urlencoded'
    if ($null -eq $resp -or [string]::IsNullOrWhiteSpace($resp.access_token)) {
        throw "OAuth client_credentials response from '$tokenUrl' did not contain an access_token."
    }
    return ([string]$resp.access_token).Trim()
}

function Get-DOrcOAuthTokenInteractivePkce {
    param(
        [Parameter(Mandatory = $true)][string]$AuthorityUrl,
        [Parameter(Mandatory = $true)][string]$OAuthClientId,
        [Parameter(Mandatory = $true)][string]$Scope,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds
    )

    $port = 8888
    $redirectUri = "http://localhost:$port/signin-callback.html"

    $verifierBytes = New-Object byte[] 48
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $rng.GetBytes($verifierBytes)
    } finally {
        $rng.Dispose()
    }
    $codeVerifier = ConvertTo-Base64Url -Bytes $verifierBytes

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $challengeBytes = $sha256.ComputeHash([System.Text.Encoding]::ASCII.GetBytes($codeVerifier))
    } finally {
        $sha256.Dispose()
    }
    $codeChallenge = ConvertTo-Base64Url -Bytes $challengeBytes
    $state = [Guid]::NewGuid().ToString('N')

    # Bind loopback TCP listeners (IPv4 127.0.0.1 and IPv6 ::1) so no elevated netsh http urlacl is required.
    $listeners = New-Object System.Collections.Generic.List[System.Net.Sockets.TcpListener]
    try {
        foreach ($ip in @([System.Net.IPAddress]::Loopback, [System.Net.IPAddress]::IPv6Loopback)) {
            try {
                $tl = [System.Net.Sockets.TcpListener]::new($ip, $port)
                $tl.Start()
                $listeners.Add($tl)
            } catch {
                # Continue if one address family is unavailable, as long as at least one binds.
            }
        }

        if ($listeners.Count -eq 0) {
            throw "Could not bind loopback port $port for OAuth PKCE callback (check if port $port is already in use by 'npm run dev' or another process)."
        }

        $authUrl = "$($AuthorityUrl.TrimEnd('/'))/connect/authorize" +
            "?client_id=$([Uri]::EscapeDataString($OAuthClientId))" +
            "&redirect_uri=$([Uri]::EscapeDataString($redirectUri))" +
            "&response_type=code" +
            "&scope=$([Uri]::EscapeDataString($Scope))" +
            "&code_challenge=$codeChallenge" +
            "&code_challenge_method=S256" +
            "&state=$state"

        Write-Host "Opening system browser for DOrc non-prod OAuth sign-in ($AuthorityUrl)..." -ForegroundColor Cyan
        [void](Start-Process $authUrl)

        $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
        $callbackQuery = $null

        while ([DateTime]::UtcNow -lt $deadline -and $null -eq $callbackQuery) {
            foreach ($tl in $listeners) {
                if (-not $tl.Pending()) { continue }

                $tcpClient = $tl.AcceptTcpClient()
                try {
                    $tcpClient.ReceiveTimeout = 5000
                    $tcpClient.SendTimeout = 5000
                    $stream = $tcpClient.GetStream()
                    $reader = New-Object System.IO.StreamReader($stream, [System.Text.Encoding]::ASCII, $false, 4096, $true)
                    $requestLine = $reader.ReadLine()
                    # Drain HTTP headers
                    while ($true) {
                        $headerLine = $reader.ReadLine()
                        if ([string]::IsNullOrEmpty($headerLine)) { break }
                    }

                    if (-not [string]::IsNullOrWhiteSpace($requestLine) -and
                        $requestLine -match '^GET\s+(\S+)\s+HTTP/') {
                        $rawTarget = $Matches[1]
                        if ($rawTarget.StartsWith('/signin-callback.html', [StringComparison]::OrdinalIgnoreCase) -and
                            ($rawTarget.Contains('code=') -or $rawTarget.Contains('error='))) {
                            $fullUri = [Uri]("http://localhost:$port$rawTarget")
                            $callbackQuery = $fullUri.Query

                            $htmlBody = '<!DOCTYPE html><html><head><meta charset="utf-8"><title>DOrc Non-Prod Seed Auth</title></head><body style="font-family:Segoe UI,sans-serif;text-align:center;margin-top:64px"><h2>DOrc Authentication Complete &#10003;</h2><p>Returning to PowerShell to seed ephemeral demo blueprints. You may close this tab.</p><script>setTimeout(function(){window.close();},1200);</script></body></html>'
                            $bodyBytes = [System.Text.Encoding]::UTF8.GetBytes($htmlBody)
                            $respHeader = "HTTP/1.1 200 OK`r`nContent-Type: text/html; charset=utf-8`r`nContent-Length: $($bodyBytes.Length)`r`nConnection: close`r`n`r`n"
                            $headerBytes = [System.Text.Encoding]::ASCII.GetBytes($respHeader)
                            $stream.Write($headerBytes, 0, $headerBytes.Length)
                            $stream.Write($bodyBytes, 0, $bodyBytes.Length)
                            $stream.Flush()
                        } else {
                            $notFound = [System.Text.Encoding]::ASCII.GetBytes("HTTP/1.1 204 No Content`r`nContent-Length: 0`r`nConnection: close`r`n`r`n")
                            $stream.Write($notFound, 0, $notFound.Length)
                            $stream.Flush()
                        }
                    }
                } finally {
                    $tcpClient.Close()
                }

                if ($null -ne $callbackQuery) { break }
            }

            if ($null -eq $callbackQuery) {
                Start-Sleep -Milliseconds 150
            }
        }

        if ($null -eq $callbackQuery) {
            throw "Timed out after $TimeoutSeconds seconds waiting for OAuth callback on $redirectUri."
        }
    } finally {
        foreach ($tl in $listeners) {
            try { $tl.Stop() } catch {}
        }
    }

    $queryParams = @{}
    foreach ($pair in $callbackQuery.TrimStart('?').Split([char[]]@('&'), [StringSplitOptions]::RemoveEmptyEntries)) {
        $kv = $pair.Split([char[]]@('='), 2)
        if ($kv.Length -eq 2) {
            $queryParams[[Uri]::UnescapeDataString($kv[0])] = [Uri]::UnescapeDataString($kv[1])
        }
    }

    if ($queryParams.ContainsKey('error')) {
        $errDesc = if ($queryParams.ContainsKey('error_description')) { " ($($queryParams['error_description']))" } else { '' }
        throw "Identity Server authorization failed: $($queryParams['error'])$errDesc"
    }
    if (-not $queryParams.ContainsKey('code')) {
        throw "OAuth callback did not include an authorization code."
    }
    if ($queryParams['state'] -ne $state) {
        throw "OAuth state mismatch on callback; aborting."
    }

    $tokenUrl = "$($AuthorityUrl.TrimEnd('/'))/connect/token"
    $tokenBody = @{
        grant_type    = 'authorization_code'
        client_id     = $OAuthClientId
        code          = $queryParams['code']
        redirect_uri  = $redirectUri
        code_verifier = $codeVerifier
    }

    $tokenResp = Invoke-RestMethod -Method Post -Uri $tokenUrl -Body $tokenBody -ContentType 'application/x-www-form-urlencoded'
    if ($null -eq $tokenResp -or [string]::IsNullOrWhiteSpace($tokenResp.access_token)) {
        throw "Token exchange at '$tokenUrl' did not return an access_token."
    }
    return ([string]$tokenResp.access_token).Trim()
}

Add-Type -AssemblyName System.Net.Http
if ($PSVersionTable.PSVersion.Major -lt 6) {
    [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.SecurityProtocolType]::Tls12
}
$script:HttpHandler = [System.Net.Http.HttpClientHandler]::new()
$script:HttpHandler.UseDefaultCredentials = [bool]$UseDefaultCredentials
$script:HttpClient = [System.Net.Http.HttpClient]::new($script:HttpHandler)
$script:HttpClient.Timeout = [TimeSpan]::FromSeconds(60)
$script:AuthToken = $null

function Invoke-DOrcApi {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('GET', 'POST', 'PUT', 'DELETE')]
        [string]$Method,
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $false)]
        $Body = $null,
        [Parameter(Mandatory = $false)]
        [int[]]$AllowStatusCodes = @()
    )

    $uri = if ($Path.StartsWith('http://') -or $Path.StartsWith('https://')) {
        $Path
    } else {
        "$normalizedApiBaseUrl/$($Path.TrimStart('/'))"
    }

    $httpMethod = [System.Net.Http.HttpMethod]::new($Method)
    $req = [System.Net.Http.HttpRequestMessage]::new($httpMethod, $uri)
    $req.Headers.Accept.Add([System.Net.Http.Headers.MediaTypeWithQualityHeaderValue]::new('application/json'))
    if (-not [string]::IsNullOrWhiteSpace($script:AuthToken)) {
        $req.Headers.Authorization = [System.Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $script:AuthToken)
    }

    if ($null -ne $Body) {
        $json = if ($Body -is [string]) { $Body } else { ConvertTo-Json -InputObject $Body -Depth 20 -Compress }
        $req.Content = [System.Net.Http.StringContent]::new($json, [System.Text.Encoding]::UTF8, 'application/json')
    }

    $resp = $script:HttpClient.SendAsync($req).GetAwaiter().GetResult()
    $statusCode = [int]$resp.StatusCode
    $rawContent = $resp.Content.ReadAsStringAsync().GetAwaiter().GetResult()

    if ($resp.IsSuccessStatusCode) {
        if ([string]::IsNullOrWhiteSpace($rawContent)) {
            return $null
        }
        try {
            return $rawContent | ConvertFrom-Json
        } catch {
            return $rawContent
        }
    }

    if ($AllowStatusCodes -contains $statusCode) {
        return $null
    }

    throw "DOrc API $Method $Path failed with HTTP $statusCode`: $rawContent"
}

#endregion HTTP & OAuth PKCE Helpers

#region Step 0: Enforce Production Guards & Authenticate

Write-Host "=== DOrc Ephemeral Environments Showcase Seeder ===" -ForegroundColor Cyan
Write-Host "Target API: $normalizedApiBaseUrl" -ForegroundColor Gray

Assert-NonProductionLocalContext `
    -TargetApiBaseUrl $normalizedApiBaseUrl `
    -CandidateEnvironmentNames @($DevBaseEnvironmentName, $UatBaseEnvironmentName)

$apiConfig = Invoke-DOrcApi -Method GET -Path '/ApiConfig'
Assert-NonProductionApiConfig -TargetApiBaseUrl $normalizedApiBaseUrl -ApiConfig $apiConfig
Write-Host "  [GUARD OK] Verified non-production target (IsProduction=False, AuthScheme=$($apiConfig.AuthenticationScheme))." -ForegroundColor Green

if (-not [string]::IsNullOrWhiteSpace($BearerToken)) {
    $cleanToken = $BearerToken.Trim()
    if ($cleanToken -match '^(?i)Bearer\s+(.+)$') {
        $cleanToken = $Matches[1].Trim()
    }
    $script:AuthToken = $cleanToken
} elseif (-not [string]::IsNullOrWhiteSpace($ClientId) -and -not [string]::IsNullOrWhiteSpace($ClientSecret)) {
    if ([string]::IsNullOrWhiteSpace($apiConfig.OAuthAuthority)) {
        throw "ApiConfig does not advertise an OAuthAuthority for client_credentials authentication."
    }
    $defaultScope = [string]$apiConfig.OAuthUiRequestedScopes
    $apiManageScope = ($defaultScope -split '\s+' | Where-Object { $_ -like '*.manage' } | Select-Object -First 1)
    $script:AuthToken = Get-DOrcOAuthTokenClientCredentials `
        -AuthorityUrl $apiConfig.OAuthAuthority `
        -OAuthClientId $ClientId `
        -OAuthClientSecret $ClientSecret `
        -Scope $apiManageScope
} elseif (-not $UseDefaultCredentials -and $apiConfig.AuthenticationScheme -eq 'OAuth') {
    $script:AuthToken = Get-DOrcOAuthTokenInteractivePkce `
        -AuthorityUrl $apiConfig.OAuthAuthority `
        -OAuthClientId $apiConfig.OAuthUiClientId `
        -Scope $apiConfig.OAuthUiRequestedScopes `
        -TimeoutSeconds $AuthTimeoutSeconds
}

if ($null -ne $OutBearerToken) {
    $OutBearerToken.Value = $script:AuthToken
}

$callerLabel = Resolve-JwtPrincipalLabel -Token $script:AuthToken
if (-not [string]::IsNullOrWhiteSpace($callerLabel)) {
    Write-Host "  [AUTH OK] Authenticated as '$callerLabel'." -ForegroundColor Green
} else {
    Write-Host "  [AUTH OK] Session ready." -ForegroundColor Green
}

#endregion Step 0

#region Step 1: Discover Optional Non-Prod Shared Principals for ACLs

$sharedPrincipals = New-Object System.Collections.ArrayList
foreach ($searchTerm in @('DOrc-Admins-NP', 'DOrc-PowerUsers-NP')) {
    try {
        $matches = @(Invoke-DOrcApi -Method GET -Path "/AccessControl/SearchUsers?search=$([Uri]::EscapeDataString($searchTerm))")
        foreach ($m in $matches) {
            if ($null -ne $m -and (-not [string]::IsNullOrWhiteSpace($m.Sid) -or -not [string]::IsNullOrWhiteSpace($m.Pid))) {
                $already = $sharedPrincipals | Where-Object {
                    (-not [string]::IsNullOrWhiteSpace($m.Sid) -and $_.Sid -eq $m.Sid) -or
                    (-not [string]::IsNullOrWhiteSpace($m.Pid) -and $_.Pid -eq $m.Pid)
                }
                if (-not $already) {
                    [void]$sharedPrincipals.Add($m)
                }
            }
        }
    } catch {
        # Best-effort group lookup; caller ownership ACL is already created automatically by DOrc.
    }
}

function Ensure-SecurableAccessControl {
    param(
        [Parameter(Mandatory = $true)]
        [ValidateSet('Project', 'Environment')]
        [string]$AccessControlType,
        [Parameter(Mandatory = $true)]
        [string]$ObjectName,
        [Parameter(Mandatory = $true)]
        [int]$GrantAllowMask
    )

    if ($sharedPrincipals.Count -eq 0) { return }

    $aclDoc = Invoke-DOrcApi -Method GET -Path "/AccessControl?accessControlType=$AccessControlType&accessControlName=$([Uri]::EscapeDataString($ObjectName))" -AllowStatusCodes @(400, 403, 404)
    if ($null -eq $aclDoc -or -not $aclDoc.UserIsOwner) { return }

    $privList = New-Object System.Collections.ArrayList
    foreach ($existing in @($aclDoc.Privileges)) {
        if ($null -ne $existing) {
            [void]$privList.Add([ordered]@{
                Id    = [int]$existing.Id
                Name  = [string]$existing.Name
                Sid   = $existing.Sid
                Pid   = $existing.Pid
                Allow = [int]$existing.Allow
                Deny  = [int]$existing.Deny
            })
        }
    }

    $changed = $false
    foreach ($principal in $sharedPrincipals) {
        $pSid = [string]$principal.Sid
        $pPid = [string]$principal.Pid
        $pName = if (-not [string]::IsNullOrWhiteSpace($principal.DisplayName)) { [string]$principal.DisplayName } else { [string]$principal.Username }

        $found = $privList | Where-Object {
            (-not [string]::IsNullOrWhiteSpace($pSid) -and $_.Sid -eq $pSid) -or
            (-not [string]::IsNullOrWhiteSpace($pPid) -and $_.Pid -eq $pPid)
        } | Select-Object -First 1

        if ($null -eq $found) {
            [void]$privList.Add([ordered]@{
                Id    = 0
                Name  = $pName
                Sid   = if ([string]::IsNullOrWhiteSpace($pSid)) { $null } else { $pSid }
                Pid   = if ([string]::IsNullOrWhiteSpace($pPid)) { $null } else { $pPid }
                Allow = $GrantAllowMask
                Deny  = 0
            })
            $changed = $true
        } elseif ((($found.Allow -band $GrantAllowMask) -ne $GrantAllowMask)) {
            $found.Allow = ($found.Allow -bor $GrantAllowMask)
            $changed = $true
        }
    }

    if ($changed) {
        $putPayload = [ordered]@{
            Type       = $AccessControlType
            Name       = $ObjectName
            ObjectId   = $aclDoc.ObjectId
            Privileges = @($privList.ToArray())
        }
        [void](Invoke-DOrcApi -Method PUT -Path '/AccessControl' -Body $putPayload)
    }
}

#endregion Step 1

#region Step 2: Upsert Persistent Non-Prod Base Environments

function Ensure-NonProdBaseEnvironment {
    param(
        [Parameter(Mandatory = $true)][string]$EnvName,
        [Parameter(Mandatory = $true)][string]$Description,
        [Parameter(Mandatory = $true)][string]$Notes
    )

    $foundList = Invoke-DOrcApi -Method GET -Path "/RefDataEnvironments?env=$([Uri]::EscapeDataString($EnvName))" -AllowStatusCodes @(404)
    $existing = if ($null -ne $foundList) { @($foundList) | Select-Object -First 1 } else { $null }

    if ($null -ne $existing -and [int]$existing.EnvironmentId -gt 0) {
        if ([bool]$existing.EnvironmentIsProd) {
            throw "PRODUCTION GUARD REFUSED: Existing environment '$EnvName' (Id=$($existing.EnvironmentId)) has EnvironmentIsProd=true!"
        }
        if ([bool]$existing.IsEphemeral) {
            throw "Existing environment '$EnvName' is marked IsEphemeral=true and cannot serve as a persistent base environment."
        }

        $existingDetails = $existing.Details
        $curDesc = if ($null -ne $existingDetails -and $null -ne $existingDetails.Description) { [string]$existingDetails.Description } else { '' }
        $curNotes = if ($null -ne $existingDetails -and $null -ne $existingDetails.Notes) { [string]$existingDetails.Notes } else { '' }
        if (($curDesc -ne $Description -or $curNotes -ne $Notes) -and [bool]$existing.UserEditable) {
            $updateBody = [ordered]@{
                EnvironmentId     = [int]$existing.EnvironmentId
                EnvironmentName   = [string]$existing.EnvironmentName
                EnvironmentSecure = [bool]$existing.EnvironmentSecure
                EnvironmentIsProd = $false
                ParentId          = $existing.ParentId
                IsEphemeral       = $false
                Details           = [ordered]@{
                    EnvironmentOwner   = if ($null -ne $existingDetails) { $existingDetails.EnvironmentOwner } else { $null }
                    EnvironmentOwnerId = if ($null -ne $existingDetails) { $existingDetails.EnvironmentOwnerId } else { $null }
                    Description        = $Description
                    FileShare          = if ($null -ne $existingDetails -and $null -ne $existingDetails.FileShare) { [string]$existingDetails.FileShare } else { '' }
                    ThinClient         = if ($null -ne $existingDetails -and $null -ne $existingDetails.ThinClient) { [string]$existingDetails.ThinClient } else { '' }
                    Notes              = $Notes
                }
            }
            $existing = Invoke-DOrcApi -Method PUT -Path '/RefDataEnvironments' -Body $updateBody
            Write-Host "  [ENV UPDATED] $($existing.EnvironmentName) (Id=$($existing.EnvironmentId))" -ForegroundColor Green
        } else {
            Write-Host "  [ENV OK] $($existing.EnvironmentName) (Id=$($existing.EnvironmentId))" -ForegroundColor Gray
        }
    } else {
        $createBody = [ordered]@{
            EnvironmentId     = 0
            EnvironmentName   = $EnvName
            EnvironmentSecure = $false
            EnvironmentIsProd = $false
            IsEphemeral       = $false
            Details           = [ordered]@{
                Description = $Description
                Notes       = $Notes
            }
        }
        $existing = Invoke-DOrcApi -Method POST -Path '/RefDataEnvironments' -Body $createBody
        if ($null -eq $existing -or [int]$existing.EnvironmentId -le 0) {
            throw "Failed to create non-prod base environment '$EnvName'."
        }
        Write-Host "  [ENV CREATED] $($existing.EnvironmentName) (Id=$($existing.EnvironmentId))" -ForegroundColor Green
    }

    # AccessLevel: Write(1) | ReadSecrets(2) | Owner(4) = 7
    Ensure-SecurableAccessControl -AccessControlType 'Environment' -ObjectName $existing.EnvironmentName -GrantAllowMask 7
    return $existing
}

Write-Host "`n[1/6] Ensuring persistent non-prod base environments..." -ForegroundColor Cyan
$devBaseEnv = Ensure-NonProdBaseEnvironment `
    -EnvName $DevBaseEnvironmentName `
    -Description "Traveler Trading Dev Baseline - shared Aiven & Azure non-prod services for xx/dv/in ephemeral sandboxes" `
    -Notes "Managed by SeedEphemeralDemoBlueprints.ps1 (non-prod showcase baseline)"

$uatBaseEnv = Ensure-NonProdBaseEnvironment `
    -EnvName $UatBaseEnvironmentName `
    -Description "Traveler Trading UAT Baseline - dedicated non-prod sizing baseline for ut/pp/ff ephemeral rehearsals" `
    -Notes "Managed by SeedEphemeralDemoBlueprints.ps1 (non-prod showcase baseline)"

#endregion Step 2

#region Step 3: Upsert Showcase Project & Map Base Environments

Write-Host "`n[2/6] Ensuring showcase project '$ProjectName'..." -ForegroundColor Cyan

$allProjects = @(Invoke-DOrcApi -Method GET -Path '/RefDataProjects')

# Choose an artefact URL guaranteed to satisfy SourceHostAllowList if configured on this instance
$defaultArtefactsUrl = 'file://localhost/Drop/EphemeralDemo'
$defaultSubPaths = ''
$defaultBuildRegex = ''

$sampleFileProj = $allProjects | Where-Object {
    $null -ne $_ -and -not [string]::IsNullOrWhiteSpace($_.ArtefactsUrl) -and $_.ArtefactsUrl.StartsWith('file://', [StringComparison]::OrdinalIgnoreCase)
} | Select-Object -First 1
$sampleHttpProj = $allProjects | Where-Object {
    $null -ne $_ -and -not [string]::IsNullOrWhiteSpace($_.ArtefactsUrl) -and $_.ArtefactsUrl -match '^https?://'
} | Select-Object -First 1

if ($null -ne $sampleHttpProj) {
    $defaultArtefactsUrl = ($sampleHttpProj.ArtefactsUrl -split ';')[0].Trim()
    $defaultSubPaths = if (-not [string]::IsNullOrWhiteSpace($sampleHttpProj.ArtefactsSubPaths)) { [string]$sampleHttpProj.ArtefactsSubPaths } else { 'EphemeralDemo' }
    $defaultBuildRegex = if ($null -ne $sampleHttpProj.ArtefactsBuildRegex) { [string]$sampleHttpProj.ArtefactsBuildRegex } else { '.*' }
} elseif ($null -ne $sampleFileProj) {
    $defaultArtefactsUrl = ($sampleFileProj.ArtefactsUrl -split ';')[0].Trim()
}

$defaultBackend = @(
    [ordered]@{
        EnvironmentId  = $null
        StorageAccount = 'sadvtrvtfstate01'
        ContainerName  = 'tfstate'
        SubscriptionId = '11111111-2222-3333-4444-555555555555'
        ResourceGroup  = 'rg-dv-trv-tfstate-uks-01'
    }
)

$existingProject = Invoke-DOrcApi -Method GET -Path "/RefDataProjects/$([Uri]::EscapeDataString($ProjectName))" -AllowStatusCodes @(404)
if ($null -ne $existingProject -and [int]$existingProject.ProjectId -gt 0) {
    $needsProjUpdate = (
        $existingProject.BusinessVertical -ne 'tr' -or
        $existingProject.ApplicationCode -ne 'trv' -or
        [string]::IsNullOrWhiteSpace($existingProject.ProjectDescription) -or
        @($existingProject.TerraformStateBackends).Count -eq 0
    )

    if ($needsProjUpdate) {
        $backendsToSave = if (@($existingProject.TerraformStateBackends).Count -gt 0) {
            $existingProject.TerraformStateBackends
        } else {
            $defaultBackend
        }
        $putProjectBody = [ordered]@{
            ProjectId              = [int]$existingProject.ProjectId
            ProjectName            = [string]$existingProject.ProjectName
            ProjectDescription     = 'Reference showcase for SEFE tier-weighted Cloud Terraform Catalog modules, base environment inheritance, and Ephemeral Environment Blueprints'
            ArtefactsUrl           = if (-not [string]::IsNullOrWhiteSpace($existingProject.ArtefactsUrl)) { [string]$existingProject.ArtefactsUrl } else { $defaultArtefactsUrl }
            ArtefactsSubPaths      = if ($null -ne $existingProject.ArtefactsSubPaths) { [string]$existingProject.ArtefactsSubPaths } else { $defaultSubPaths }
            ArtefactsBuildRegex    = if ($null -ne $existingProject.ArtefactsBuildRegex) { [string]$existingProject.ArtefactsBuildRegex } else { $defaultBuildRegex }
            TerraformGitRepoUrl    = $existingProject.TerraformGitRepoUrl
            TerraformStateBackends = $backendsToSave
            LeanIXUrl              = $existingProject.LeanIXUrl
            BusinessVertical       = 'tr'
            ApplicationCode        = 'trv'
            SourceDatabase         = $existingProject.SourceDatabase
            SourceControlType      = [int]$existingProject.SourceControlType
            Revision               = [string]$existingProject.Revision
        }
        $project = Invoke-DOrcApi -Method PUT -Path '/RefDataProjects' -Body $putProjectBody
        Write-Host "  [PROJECT UPDATED] $($project.ProjectName) (Id=$($project.ProjectId), AppCode=trv, Vertical=tr)" -ForegroundColor Green
    } else {
        $project = $existingProject
        Write-Host "  [PROJECT OK] $($project.ProjectName) (Id=$($project.ProjectId), AppCode=$($project.ApplicationCode), Vertical=$($project.BusinessVertical))" -ForegroundColor Gray
    }
} else {
    $postProjectBody = [ordered]@{
        ProjectId              = 0
        ProjectName            = $ProjectName
        ProjectDescription     = 'Reference showcase for SEFE tier-weighted Cloud Terraform Catalog modules, base environment inheritance, and Ephemeral Environment Blueprints'
        ArtefactsUrl           = $defaultArtefactsUrl
        ArtefactsSubPaths      = $defaultSubPaths
        ArtefactsBuildRegex    = $defaultBuildRegex
        TerraformStateBackends = $defaultBackend
        BusinessVertical       = 'tr'
        ApplicationCode        = 'trv'
        SourceControlType      = 0
    }
    $project = Invoke-DOrcApi -Method POST -Path '/RefDataProjects' -Body $postProjectBody
    Write-Host "  [PROJECT CREATED] $($project.ProjectName) (Id=$($project.ProjectId), AppCode=trv, Vertical=tr)" -ForegroundColor Green
}

$projectId = [int]$project.ProjectId
# AccessLevel: Write(1) | Owner(4) = 5
Ensure-SecurableAccessControl -AccessControlType 'Project' -ObjectName $project.ProjectName -GrantAllowMask 5

$mappedEnvsResp = Invoke-DOrcApi -Method GET -Path "/RefDataProjectEnvironmentMappings?project=$([Uri]::EscapeDataString($project.ProjectName))&includeRead=true" -AllowStatusCodes @(404)
$mappedEnvNames = @()
if ($null -ne $mappedEnvsResp -and $null -ne $mappedEnvsResp.Items) {
    $mappedEnvNames = @($mappedEnvsResp.Items | ForEach-Object { [string]$_.EnvironmentName })
}

foreach ($baseEnv in @($devBaseEnv, $uatBaseEnv)) {
    $alreadyMapped = $mappedEnvNames | Where-Object { $_ -ieq $baseEnv.EnvironmentName }
    if (-not $alreadyMapped) {
        [void](Invoke-DOrcApi -Method POST -Path "/RefDataProjectEnvironmentMappings?project=$([Uri]::EscapeDataString($project.ProjectName))&environment=$([Uri]::EscapeDataString($baseEnv.EnvironmentName))")
        Write-Host "  [MAPPED] $($project.ProjectName) <-> $($baseEnv.EnvironmentName)" -ForegroundColor Green
    } else {
        Write-Host "  [MAPPED OK] $($project.ProjectName) <-> $($baseEnv.EnvironmentName)" -ForegroundColor Gray
    }
}

#endregion Step 3

#region Step 4: Instantiate Catalog Terraform Components & PowerShell Validation Components

Write-Host "`n[3/6] Ensuring Catalog Terraform & post-provision validation components..." -ForegroundColor Cyan

$catalogTemplates = @(Invoke-DOrcApi -Method GET -Path '/Terraform/templates')
if ($catalogTemplates.Count -eq 0) {
    throw "Catalog returned 0 stock templates from GET /Terraform/templates on '$normalizedApiBaseUrl'. Ensure catalog manifests are deployed."
}

function Resolve-CatalogVersion {
    param(
        [Parameter(Mandatory = $true)][string]$TemplateName,
        [Parameter(Mandatory = $true)][string]$PreferredVersion
    )
    $exact = $catalogTemplates | Where-Object { $_.Name -eq $TemplateName -and $_.Version -eq $PreferredVersion } | Select-Object -First 1
    if ($null -ne $exact) { return [string]$exact.Version }

    $fallback = $catalogTemplates | Where-Object { $_.Name -eq $TemplateName -and -not $_.Deprecated } |
        Sort-Object -Property Version -Descending | Select-Object -First 1
    if ($null -ne $fallback) { return [string]$fallback.Version }

    throw "Required stock module '$TemplateName' was not found in GET /Terraform/templates."
}

$desiredCatalogSpecs = @(
    [ordered]@{ Key = 'pg'; ComponentName = 'TRV Demo - PostgreSQL Analytics DB';   TemplateName = 'postgresql-database'; PreferredVersion = '2.0.0' },
    [ordered]@{ Key = 'ch'; ComponentName = 'TRV Demo - ClickHouse Market Ticks';   TemplateName = 'clickhouse-database'; PreferredVersion = '2.0.0' },
    [ordered]@{ Key = 'kf'; ComponentName = 'TRV Demo - Trade Events Kafka Topic';  TemplateName = 'kafka-topic';         PreferredVersion = '2.1.0' },
    [ordered]@{ Key = 'cs'; ComponentName = 'TRV Demo - Session Store Cosmos DB';   TemplateName = 'cosmosdb';            PreferredVersion = '1.0.0' },
    [ordered]@{ Key = 'sb'; ComponentName = 'TRV Demo - Order Dispatch Service Bus'; TemplateName = 'service-bus';        PreferredVersion = '1.0.0' }
)

$desiredValidationSpecs = @(
    [ordered]@{ Key = 'valSchema'; ComponentName = 'TRV Demo - Verify Schema and Connectivity'; ScriptPath = 'Scripts\Validate-EphemeralSchema.ps1' },
    [ordered]@{ Key = 'valSmoke';  ComponentName = 'TRV Demo - Run Trading API Smoke Suite';    ScriptPath = 'Scripts\Invoke-EphemeralSmokeTests.ps1' }
)

function Get-FlattenedComponents {
    param($Nodes)
    $flat = New-Object System.Collections.ArrayList
    foreach ($n in @($Nodes)) {
        if ($null -eq $n) { continue }
        [void]$flat.Add($n)
        if ($null -ne $n.Children -and @($n.Children).Count -gt 0) {
            foreach ($c in @(Get-FlattenedComponents -Nodes $n.Children)) {
                [void]$flat.Add($c)
            }
        }
    }
    return @($flat.ToArray())
}

$refData = Invoke-DOrcApi -Method GET -Path "/RefData/$projectId"
$existingFlat = @(Get-FlattenedComponents -Nodes $refData.Components)

foreach ($spec in $desiredCatalogSpecs) {
    $resolvedVersion = Resolve-CatalogVersion -TemplateName $spec.TemplateName -PreferredVersion $spec.PreferredVersion
    $spec['ResolvedVersion'] = $resolvedVersion

    $match = $existingFlat | Where-Object { $_.ComponentName -ieq $spec.ComponentName } | Select-Object -First 1
    if ($null -eq $match) {
        $instBody = [ordered]@{
            ProjectId     = $projectId
            ComponentName = $spec.ComponentName
        }
        $instResp = Invoke-DOrcApi -Method POST -Path "/Terraform/templates/$($spec.TemplateName)/$resolvedVersion/instantiate" -Body $instBody
        Write-Host "  [CATALOG COMPONENT CREATED] $($spec.ComponentName) ($($spec.TemplateName)@$resolvedVersion, Id=$($instResp.Component.ComponentId))" -ForegroundColor Green
    } else {
        Write-Host "  [CATALOG COMPONENT OK] $($match.ComponentName) ($($match.TerraformTemplateName)@$($match.TerraformTemplateVersion), Id=$($match.ComponentId))" -ForegroundColor Gray
    }
}

# Reload RefData to capture latest Revision & component tree, then ensure validation components + enabled flags
$refData = Invoke-DOrcApi -Method GET -Path "/RefData/$projectId"
$updatedTree = New-Object System.Collections.ArrayList
$treeModified = $false

foreach ($c in @($refData.Components)) {
    if ($null -eq $c) { continue }
    if (-not [bool]$c.IsEnabled) {
        $c.IsEnabled = $true
        $treeModified = $true
    }
    [void]$updatedTree.Add($c)
}

$existingFlat = @(Get-FlattenedComponents -Nodes $updatedTree)
foreach ($vSpec in $desiredValidationSpecs) {
    $vMatch = $existingFlat | Where-Object { $_.ComponentName -ieq $vSpec.ComponentName } | Select-Object -First 1
    if ($null -eq $vMatch) {
        [void]$updatedTree.Add([ordered]@{
            Children      = @()
            ComponentId   = 0
            ComponentName = $vSpec.ComponentName
            ScriptPath    = $vSpec.ScriptPath
            NonProdOnly   = $true
            StopOnFailure = $true
            ParentId      = 0
            IsEnabled     = $true
            PSVersion     = 'v7'
            ComponentType = 'PowerShell'
        })
        $treeModified = $true
        Write-Host "  [VALIDATION COMPONENT QUEUED] $($vSpec.ComponentName) ($($vSpec.ScriptPath))" -ForegroundColor Green
    } else {
        Write-Host "  [VALIDATION COMPONENT OK] $($vMatch.ComponentName) (Id=$($vMatch.ComponentId))" -ForegroundColor Gray
    }
}

if ($treeModified) {
    $putRefBody = [ordered]@{
        Project    = $refData.Project
        Components = @($updatedTree.ToArray())
        Revision   = [string]$refData.Revision
    }
    $refData = Invoke-DOrcApi -Method PUT -Path '/RefData' -Body $putRefBody
    Write-Host "  [REFDATA SAVED] Updated component tree (Revision=$($refData.Revision))." -ForegroundColor Green
}

$finalFlat = @(Get-FlattenedComponents -Nodes $refData.Components)
$componentByKey = @{}
foreach ($spec in ($desiredCatalogSpecs + $desiredValidationSpecs)) {
    $foundComp = $finalFlat | Where-Object { $_.ComponentName -ieq $spec.ComponentName } | Select-Object -First 1
    if ($null -eq $foundComp -or [int]$foundComp.ComponentId -le 0) {
        throw "Component '$($spec.ComponentName)' was not found in project $projectId after save."
    }
    $componentByKey[$spec.Key] = $foundComp
}

#endregion Step 4

#region Step 5: Seed Base Environment DOrc Properties & Saved Catalog Inputs

Write-Host "`n[4/6] Ensuring base environment DOrc properties & saved Catalog inputs..." -ForegroundColor Cyan

function Ensure-EnvironmentProperty {
    param(
        [Parameter(Mandatory = $true)][string]$PropertyName,
        [Parameter(Mandatory = $true)][string]$EnvName,
        [Parameter(Mandatory = $true)][string]$Value
    )

    $existingProp = Invoke-DOrcApi -Method GET -Path "/Properties/id=$([Uri]::EscapeDataString($PropertyName))" -AllowStatusCodes @(404)
    if ($null -eq $existingProp) {
        $propCreate = @(
            [ordered]@{
                Name   = $PropertyName
                Secure = $false
                IsArray = $false
            }
        )
        [void](Invoke-DOrcApi -Method POST -Path '/Properties' -Body $propCreate)
        $existingProp = Invoke-DOrcApi -Method GET -Path "/Properties/id=$([Uri]::EscapeDataString($PropertyName))"
        Write-Host "  [PROPERTY CREATED] $PropertyName (Secure=False)" -ForegroundColor Green
    }

    if ([bool]$existingProp.Secure) {
        Write-Host "  [PROPERTY SKIP] '$PropertyName' is marked Secure on this DOrc instance; leaving existing values untouched." -ForegroundColor Yellow
        return
    }

    $existingVals = Invoke-DOrcApi -Method GET -Path "/PropertyValues?propertyName=$([Uri]::EscapeDataString($PropertyName))&environmentName=$([Uri]::EscapeDataString($EnvName))" -AllowStatusCodes @(404)
    $scopedVal = if ($null -ne $existingVals) {
        @($existingVals) | Where-Object { $_.PropertyValueFilter -ieq $EnvName } | Select-Object -First 1
    } else {
        $null
    }

    if ($null -eq $scopedVal) {
        $postVal = @(
            [ordered]@{
                Value               = $Value
                PropertyValueFilter = $EnvName
                Property            = [ordered]@{
                    Id      = [int]$existingProp.Id
                    Name    = [string]$existingProp.Name
                    Secure  = $false
                    IsArray = [bool]$existingProp.IsArray
                }
            }
        )
        [void](Invoke-DOrcApi -Method POST -Path '/PropertyValues' -Body $postVal)
        Write-Host "  [PROP VALUE CREATED] $EnvName :: $PropertyName = '$Value'" -ForegroundColor Green
    } elseif ([string]$scopedVal.Value -ne $Value) {
        $putVal = @(
            [ordered]@{
                Id                    = [long]$scopedVal.Id
                Value                 = $Value
                PropertyValueFilter   = $EnvName
                PropertyValueFilterId = $scopedVal.PropertyValueFilterId
                Property              = [ordered]@{
                    Id      = [int]$existingProp.Id
                    Name    = [string]$existingProp.Name
                    Secure  = $false
                    IsArray = [bool]$existingProp.IsArray
                }
            }
        )
        [void](Invoke-DOrcApi -Method PUT -Path '/PropertyValues' -Body $putVal)
        Write-Host "  [PROP VALUE UPDATED] $EnvName :: $PropertyName = '$Value'" -ForegroundColor Green
    } else {
        Write-Host "  [PROP VALUE OK] $EnvName :: $PropertyName = '$Value'" -ForegroundColor Gray
    }
}

Ensure-EnvironmentProperty -PropertyName 'location' -EnvName $devBaseEnv.EnvironmentName -Value 'uksouth'
Ensure-EnvironmentProperty -PropertyName 'location' -EnvName $uatBaseEnv.EnvironmentName -Value 'uksouth'

function Ensure-SavedCatalogConfiguration {
    param(
        [Parameter(Mandatory = $true)]$Component,
        [Parameter(Mandatory = $true)][string]$EnvName,
        [Parameter(Mandatory = $true)][hashtable]$DesiredInputs
    )

    $compId = [int]$Component.ComponentId
    $tName = [string]$Component.TerraformTemplateName
    $tVer = [string]$Component.TerraformTemplateVersion

    $manifest = $catalogTemplates | Where-Object { $_.Name -eq $tName -and $_.Version -eq $tVer } | Select-Object -First 1
    $validParamNames = @($manifest.Parameters | Where-Object { -not $_.Sensitive } | ForEach-Object { [string]$_.Name })

    $filteredInputs = [ordered]@{}
    foreach ($k in $DesiredInputs.Keys) {
        if ($validParamNames -contains $k) {
            $filteredInputs[$k] = [string]$DesiredInputs[$k]
        }
    }

    $resPath = "/Terraform/templates/$tName/$tVer/resolution?projectId=$projectId&environmentName=$([Uri]::EscapeDataString($EnvName))&componentId=$compId"
    $resolution = Invoke-DOrcApi -Method GET -Path $resPath
    $expectedRev = if ($null -ne $resolution -and $null -ne $resolution.SavedConfiguration -and $null -ne $resolution.SavedConfiguration.Revision) {
        [long]$resolution.SavedConfiguration.Revision
    } else {
        [long]0
    }

    $savePath = "/Terraform/templates/$tName/$tVer/configuration?projectId=$projectId&componentId=$compId&environmentName=$([Uri]::EscapeDataString($EnvName))"
    $saveBody = [ordered]@{
        ExpectedRevision = $expectedRev
        Inputs           = $filteredInputs
    }
    $saved = Invoke-DOrcApi -Method PUT -Path $savePath -Body $saveBody
    Write-Host "  [CATALOG INPUTS SAVED] $EnvName :: $($Component.ComponentName) (Rev=$($saved.Revision), Keys=$($filteredInputs.Keys -join ','))" -ForegroundColor Green
}

# Dev Base Environment (TRV-DV-01) saved non-sensitive baseline inputs
Ensure-SavedCatalogConfiguration -Component $componentByKey['pg'] -EnvName $devBaseEnv.EnvironmentName -DesiredInputs @{
    project    = 'trading-traveler-np'
    cloud_name = 'custom-sefe-nprod-workload-azure-uksouth'
}
Ensure-SavedCatalogConfiguration -Component $componentByKey['ch'] -EnvName $devBaseEnv.EnvironmentName -DesiredInputs @{
    project    = 'trading-traveler-np'
    cloud_name = 'custom-sefe-nprod-workload-azure-uksouth'
}
Ensure-SavedCatalogConfiguration -Component $componentByKey['kf'] -EnvName $devBaseEnv.EnvironmentName -DesiredInputs @{
    project          = 'trading-traveler'
    data_grouping    = 'trades'
    data_description = 'tradeevents'
    integrity_level  = 'il2'
    origin           = 'trv'
}
Ensure-SavedCatalogConfiguration -Component $componentByKey['cs'] -EnvName $devBaseEnv.EnvironmentName -DesiredInputs @{
    resource_group_name = 'rg-dv-trv-uks-01'
    consistency_level   = 'Session'
}
Ensure-SavedCatalogConfiguration -Component $componentByKey['sb'] -EnvName $devBaseEnv.EnvironmentName -DesiredInputs @{
    resource_group_name = 'rg-dv-trv-uks-01'
    queue_name          = 'trv.orders.dispatch'
}

# UAT Base Environment (TRV-UT-01) saved non-sensitive baseline inputs
Ensure-SavedCatalogConfiguration -Component $componentByKey['pg'] -EnvName $uatBaseEnv.EnvironmentName -DesiredInputs @{
    project    = 'trading-traveler-np'
    cloud_name = 'custom-sefe-nprod-workload-azure-uksouth'
}
Ensure-SavedCatalogConfiguration -Component $componentByKey['ch'] -EnvName $uatBaseEnv.EnvironmentName -DesiredInputs @{
    project    = 'trading-traveler-np'
    cloud_name = 'custom-sefe-nprod-workload-azure-uksouth'
}
Ensure-SavedCatalogConfiguration -Component $componentByKey['kf'] -EnvName $uatBaseEnv.EnvironmentName -DesiredInputs @{
    project          = 'trading-traveler'
    data_grouping    = 'trades'
    data_description = 'tradeevents'
    integrity_level  = 'il2'
    origin           = 'trv'
}
Ensure-SavedCatalogConfiguration -Component $componentByKey['cs'] -EnvName $uatBaseEnv.EnvironmentName -DesiredInputs @{
    resource_group_name = 'rg-ut-trv-uks-01'
    consistency_level   = 'BoundedStaleness'
}
Ensure-SavedCatalogConfiguration -Component $componentByKey['sb'] -EnvName $uatBaseEnv.EnvironmentName -DesiredInputs @{
    resource_group_name = 'rg-ut-trv-uks-01'
    queue_name          = 'trv.orders.dispatch'
}

#endregion Step 5

#region Step 6: Upsert SEFE Tier-Weighted Blueprint Templates

Write-Host "`n[5/6] Ensuring SEFE tier-weighted Ephemeral Blueprint Templates..." -ForegroundColor Cyan

$pgId = [int]$componentByKey['pg'].ComponentId
$chId = [int]$componentByKey['ch'].ComponentId
$kfId = [int]$componentByKey['kf'].ComponentId
$csId = [int]$componentByKey['cs'].ComponentId
$sbId = [int]$componentByKey['sb'].ComponentId
$valSchemaId = [int]$componentByKey['valSchema'].ComponentId
$valSmokeId  = [int]$componentByKey['valSmoke'].ComponentId

$pgName = [string]$componentByKey['pg'].ComponentName
$chName = [string]$componentByKey['ch'].ComponentName
$kfName = [string]$componentByKey['kf'].ComponentName
$csName = [string]$componentByKey['cs'].ComponentName
$sbName = [string]$componentByKey['sb'].ComponentName

$blueprintSpecs = @(
    [ordered]@{
        Name                      = 'PR / Feature Sandbox (xx - Shared Host Services)'
        Description               = 'Fast temporary sandbox (SEFE xx tier) reusing shared Aiven & Azure dev host services with 1-partition Kafka topics, Basic Service Bus, and forced termination_protection=false.'
        DefaultTier               = 'xx'
        BaseEnvironmentId         = [int]$devBaseEnv.EnvironmentId
        DefaultTtlMinutes         = 240
        MaxTtlMinutes             = 720
        ProvisionComponentIds     = @($pgId, $kfId, $sbId)
        ValidationComponentIds    = @($valSchemaId)
        TierParameterOverridesObj = [ordered]@{
            "$sbName`:max_delivery_count" = '5'
            xx                            = [ordered]@{
                "$kfName`:cleanup_policy" = 'delete'
            }
            ephemeral                     = [ordered]@{
                "$kfName`:cleanup_policy" = 'delete'
            }
        }
    },
    [ordered]@{
        Name                      = 'Integration Full Stack (in - Shared Cluster Sizing)'
        Description               = 'Full 5-module cloud integration environment (SEFE in/it tier) across PostgreSQL, ClickHouse, Kafka, Cosmos DB, and Service Bus with schema + API smoke validation.'
        DefaultTier               = 'in'
        BaseEnvironmentId         = [int]$devBaseEnv.EnvironmentId
        DefaultTtlMinutes         = 480
        MaxTtlMinutes             = 2880
        ProvisionComponentIds     = @($pgId, $chId, $kfId, $csId, $sbId)
        ValidationComponentIds    = @($valSchemaId, $valSmokeId)
        TierParameterOverridesObj = [ordered]@{
            "$sbName`:enable_partitioning" = 'true'
            'in'                           = [ordered]@{
                "$kfName`:partitions" = '6'
            }
            it                             = [ordered]@{
                "$kfName`:partitions" = '6'
            }
        }
    },
    [ordered]@{
        Name                      = 'UAT Release Rehearsal (ut - Dedicated Sizing)'
        Description               = 'Dedicated UAT release rehearsal environment (SEFE ut tier) inheriting from TRV-UT-01 with dedicated Aiven services (startup-16), BoundedStaleness Cosmos DB, and full validation.'
        DefaultTier               = 'ut'
        BaseEnvironmentId         = [int]$uatBaseEnv.EnvironmentId
        DefaultTtlMinutes         = 1440
        MaxTtlMinutes             = 4320
        ProvisionComponentIds     = @($pgId, $chId, $kfId, $csId, $sbId)
        ValidationComponentIds    = @($valSchemaId, $valSmokeId)
        TierParameterOverridesObj = [ordered]@{
            "$csName`:consistency_level" = 'BoundedStaleness'
            ut                           = [ordered]@{
                "$sbName`:max_delivery_count" = '15'
            }
        }
    },
    [ordered]@{
        Name                      = 'Pre-Prod Benchmark & Trial (pp - High Throughput)'
        Description               = 'High-throughput Pre-Prod rehearsal stack (SEFE pp tier) with Aiven business-8/business-16 plans, 8000 RU/s Cosmos DB override, 24-partition Kafka topic, and Premium Service Bus.'
        DefaultTier               = 'pp'
        BaseEnvironmentId         = [int]$uatBaseEnv.EnvironmentId
        DefaultTtlMinutes         = 720
        MaxTtlMinutes             = 2880
        ProvisionComponentIds     = @($pgId, $chId, $kfId, $csId, $sbId)
        ValidationComponentIds    = @($valSchemaId, $valSmokeId)
        TierParameterOverridesObj = [ordered]@{
            "$sbName`:public_network_access_enabled" = 'false'
            pp                                       = [ordered]@{
                "$csName`:throughput" = '8000'
                "$kfName`:partitions" = '24'
                "$chName`:plan"       = 'business-16'
            }
        }
    },
    [ordered]@{
        Name                      = 'Fix-on-Fail Incident Diagnostics (ff)'
        Description               = 'Rapid incident reproduction stack (SEFE ff Fix-on-Fail tier) provisioning PostgreSQL, ClickHouse, Kafka (14-day retention override), and Service Bus with schema verification.'
        DefaultTier               = 'ff'
        BaseEnvironmentId         = [int]$uatBaseEnv.EnvironmentId
        DefaultTtlMinutes         = 360
        MaxTtlMinutes             = 1440
        ProvisionComponentIds     = @($pgId, $chId, $kfId, $sbId)
        ValidationComponentIds    = @($valSchemaId)
        TierParameterOverridesObj = [ordered]@{
            ff = [ordered]@{
                "$kfName`:retention_ms"       = '1209600000'
                "$sbName`:max_delivery_count" = '20'
            }
        }
    }
)

$existingTemplates = @(Invoke-DOrcApi -Method GET -Path "/EphemeralEnvironments/templates?projectId=$projectId")
$seededTemplates = New-Object System.Collections.ArrayList

foreach ($bp in $blueprintSpecs) {
    $overridesJson = $bp.TierParameterOverridesObj | ConvertTo-Json -Depth 10 -Compress
    $existingBp = $existingTemplates | Where-Object { $_.Name -ieq $bp.Name } | Select-Object -First 1

    $payload = [ordered]@{
        Id                         = if ($null -ne $existingBp) { [int]$existingBp.Id } else { 0 }
        ProjectId                  = $projectId
        Name                       = $bp.Name
        Description                = $bp.Description
        DefaultTier                = $bp.DefaultTier
        BaseEnvironmentId          = $bp.BaseEnvironmentId
        DefaultTtlMinutes          = $bp.DefaultTtlMinutes
        MaxTtlMinutes              = $bp.MaxTtlMinutes
        ProvisionComponentIds      = $bp.ProvisionComponentIds
        ValidationComponentIds     = $bp.ValidationComponentIds
        TierParameterOverridesJson = $overridesJson
    }

    if ($null -ne $existingBp) {
        $savedBp = Invoke-DOrcApi -Method PUT -Path "/EphemeralEnvironments/templates/$($existingBp.Id)" -Body $payload
        Write-Host "  [BLUEPRINT UPDATED] $($savedBp.Name) (Id=$($savedBp.Id), DefaultTier=$($savedBp.DefaultTier))" -ForegroundColor Green
    } else {
        $savedBp = Invoke-DOrcApi -Method POST -Path '/EphemeralEnvironments/templates' -Body $payload
        Write-Host "  [BLUEPRINT CREATED] $($savedBp.Name) (Id=$($savedBp.Id), DefaultTier=$($savedBp.DefaultTier))" -ForegroundColor Green
    }
    [void]$seededTemplates.Add($savedBp)
}

#endregion Step 6

#region Step 7: Verify Live Tier-Weighted Parameter Previews Across All Seeded Blueprints

Write-Host "`n[6/6] Verifying live tier-weighted parameter resolution previews..." -ForegroundColor Cyan
$previewSummaries = New-Object System.Collections.ArrayList

foreach ($tmpl in $seededTemplates) {
    $sampleEnvName = "EPH-$($tmpl.DefaultTier.ToUpperInvariant())-DEMO01"
    $previewReq = [ordered]@{
        ProjectId         = $projectId
        TemplateId        = [int]$tmpl.Id
        EnvironmentName   = $sampleEnvName
        EnvironmentTier   = [string]$tmpl.DefaultTier
        BaseEnvironmentId = $tmpl.BaseEnvironmentId
    }
    $preview = Invoke-DOrcApi -Method POST -Path '/EphemeralEnvironments/preview-tier-parameters' -Body $previewReq
    $compCount = @($preview.ComponentPreviews).Count
    $sharedCount = @($preview.ComponentPreviews | Where-Object { [bool]$_.IsSharedService }).Count
    Write-Host "  [PREVIEW OK] '$($tmpl.Name)' -> Tier='$($preview.EnvironmentTier)', CatalogComponents=$compCount, SharedHostServices=$sharedCount" -ForegroundColor Green
    [void]$previewSummaries.Add([pscustomobject]@{
        TemplateId         = [int]$tmpl.Id
        TemplateName       = [string]$tmpl.Name
        Tier               = [string]$preview.EnvironmentTier
        CatalogComponents  = $compCount
        SharedHostServices = $sharedCount
    })
}

Write-Host "`n=== Showcase Seeding Complete ===" -ForegroundColor Cyan
Write-Host "  Project          : $($project.ProjectName) (Id=$projectId)" -ForegroundColor White
Write-Host "  Base Environments: $($devBaseEnv.EnvironmentName) (Id=$($devBaseEnv.EnvironmentId)), $($uatBaseEnv.EnvironmentName) (Id=$($uatBaseEnv.EnvironmentId))" -ForegroundColor White
Write-Host "  Components       : $($desiredCatalogSpecs.Count) Catalog Terraform + $($desiredValidationSpecs.Count) PowerShell Validation" -ForegroundColor White
Write-Host "  Blueprints       : $($seededTemplates.Count) SEFE tier-weighted templates ready" -ForegroundColor White

if ($PassThru) {
    return [pscustomobject]@{
        ApiBaseUrl       = $normalizedApiBaseUrl
        ProjectId        = $projectId
        ProjectName      = [string]$project.ProjectName
        BaseEnvironments = @($devBaseEnv, $uatBaseEnv)
        Templates        = @($seededTemplates.ToArray())
        Previews         = @($previewSummaries.ToArray())
    }
}

#endregion Step 7
