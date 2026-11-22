BeforeAll {
    $modulePath = Join-Path $PSScriptRoot "DOrc.Cmdlet.psm1"
    Remove-Module DOrc.Cmdlet, ManageProperty, PropertiesHelper, DorcApiClient, Models, EndurUsers -ErrorAction SilentlyContinue
    Import-Module $modulePath -Force
}

Describe "Open-ApiConnection" {
    It "Throws when API endpoint cannot be reached" {
        { Open-ApiConnection "http://localhost:59999/invalid" } | Should -Throw "*Cannot establish connection with*"
    }

    It "Populates global ApiConnection when endpoint discovery returns HTTP 200" {
        Mock Invoke-WebRequest -ModuleName DorcApiClient {
            [PSCustomObject]@{
                StatusCode = 200
                Content    = '{"Property":"https://dorc.local/api/Properties","PropertyValues":"https://dorc.local/api/PropertyValues"}'
            }
        }

        Open-ApiConnection -ApiUrl "https://dorc.local/api/Root"
        $global:ApiConnection.Property | Should -Be "https://dorc.local/api/Properties"
        $global:ApiConnection.PropertyValues | Should -Be "https://dorc.local/api/PropertyValues"
        $global:ApiConnection.Root | Should -Be "https://dorc.local/api/Root"
    }
}

Describe "ManageProperty single-property lookup route" {
    It "Uses path segment /id=<escapedName> so PropertyExists distinguishes 404 from 200" {
        $global:CapturedUris = [System.Collections.Generic.List[string]]::new()
        Mock Invoke-WebRequest -ModuleName DorcApiClient {
            param($Method, $Uri)
            $global:CapturedUris.Add($Uri)
            if ($Method -eq "GET" -and $Uri -eq "https://dorc.local/api/Root") {
                return [PSCustomObject]@{
                    StatusCode = 200
                    Content    = '{"Property":"https://dorc.local/api/Properties","PropertyValues":"https://dorc.local/api/PropertyValues"}'
                }
            }
            if ($Uri -like "*Existing*Prop*") {
                return [PSCustomObject]@{
                    StatusCode = 200
                    Content    = '{"Id":1,"Name":"Existing Prop","Secure":false}'
                }
            }

            $resp = New-Object PSObject -Property @{
                StatusCode        = [pscustomobject]@{ value__ = 404 }
                StatusDescription = "Not Found"
            }
            $ex = New-Object System.Exception("404")
            $ex | Add-Member -NotePropertyName Response -NotePropertyValue $resp -Force
            throw $ex
        }

        Open-ApiConnection -ApiUrl "https://dorc.local/api/Root"
        InModuleScope DOrc.Cmdlet {
            $helper = [PropertiesHelper]::new()
            $helper.repository.PropertyExists("Existing Prop") | Should -Be $true
            $helper.repository.PropertyExists("MissingProp") | Should -Be $false
        }

        $global:CapturedUris | Should -Contain "https://dorc.local/api/Properties/id=Existing Prop"
        $global:CapturedUris | Should -Contain "https://dorc.local/api/Properties/id=MissingProp"
        Remove-Variable -Name CapturedUris -Scope Global -ErrorAction SilentlyContinue
    }
}

Describe "Import-DOrcProperties" {
    BeforeEach {
        InModuleScope DOrc.Cmdlet {
            [DorcApiClient]::SetAccessToken("test-jwt-token")
            [DorcApiClient]::SetImpersonateUser("tester@sefe.eu")
        }
    }

    It "Fails with empty ApiUrl" {
        { Import-DOrcProperties "" "dummy.csv" } | Should -Throw "*Cannot bind argument to parameter 'ApiUrl'*"
    }

    It "Fails with empty CsvFile" {
        { Import-DOrcProperties "https://dorc.local/api" "" } | Should -Throw "*Cannot bind argument to parameter 'CsvFile'*"
    }

    It "Handles connection failure gracefully" {
        Mock Open-ApiConnection { throw "Cannot establish connection with test string" } -ModuleName DOrc.Cmdlet
        Mock Write-Host -ModuleName DOrc.Cmdlet

        Import-DOrcProperties "test string" "test csv string"

        Should -Invoke Write-Host -ModuleName DOrc.Cmdlet -Times 1 -Exactly -ParameterFilter {
            $Object -like "*Cannot establish connection with test string*"
        }
    }

    It "Imports CSV properties and creates missing properties and values via PropertiesHelper" {
        $tempCsv = Join-Path ([System.IO.Path]::GetTempPath()) ("dorc-cmdlet-" + [guid]::NewGuid().ToString("N") + ".csv")
        try {
            @(
                [PSCustomObject]@{
                    PropertyName = "NewProp"
                    Environment  = "DV 01"
                    Value        = "Val1"
                    IsSecured    = "false"
                }
            ) | Export-Csv -Path $tempCsv -NoTypeInformation

            Mock Invoke-WebRequest -ModuleName DorcApiClient {
                param($Method, $Uri, $Body)
                if ($Method -eq "GET" -and $Uri -eq "https://dorc.local/api/Root") {
                    return [PSCustomObject]@{
                        StatusCode = 200
                        Content    = '{"Property":"https://dorc.local/api/Properties","PropertyValues":"https://dorc.local/api/PropertyValues"}'
                    }
                }
                if ($Method -eq "GET" -and $Uri -like "*/Properties/id=*") {
                    $resp = New-Object PSObject -Property @{
                        StatusCode        = [pscustomobject]@{ value__ = 404 }
                        StatusDescription = "Not Found"
                    }
                    $ex = New-Object System.Exception("404")
                    $ex | Add-Member -NotePropertyName Response -NotePropertyValue $resp -Force
                    throw $ex
                }
                if ($Method -eq "GET" -and $Uri -like "*/PropertyValues?environmentName=*") {
                    $resp = New-Object PSObject -Property @{
                        StatusCode        = [pscustomobject]@{ value__ = 404 }
                        StatusDescription = "Not Found"
                    }
                    $ex = New-Object System.Exception("404")
                    $ex | Add-Member -NotePropertyName Response -NotePropertyValue $resp -Force
                    throw $ex
                }
                if ($Method -eq "POST") {
                    return [PSCustomObject]@{
                        StatusCode = 200
                        Content    = '[{"Status":"Success","Item":{"Property":{"Name":"NewProp"}}}]'
                    }
                }
            }

            { Import-DOrcProperties -ApiUrl "https://dorc.local/api/Root" -CsvFile $tempCsv } | Should -Not -Throw
        }
        finally {
            if (Test-Path $tempCsv) { Remove-Item $tempCsv -Force }
        }
    }
}

Describe "Export-DOrcProperties" {
    BeforeEach {
        InModuleScope DOrc.Cmdlet {
            [DorcApiClient]::SetAccessToken("test-jwt-token")
        }
    }

    It "Fails with empty ApiUrl" {
        { Export-DOrcProperties "" "DV 01" "out.csv" } | Should -Throw "*Cannot bind argument to parameter 'ApiUrl'*"
    }

    It "Fails with empty Environment" {
        { Export-DOrcProperties "https://dorc.local/api" "" "out.csv" } | Should -Throw "*Cannot bind argument to parameter 'Environment'*"
    }

    It "Fails with empty CsvFile" {
        { Export-DOrcProperties "https://dorc.local/api" "DV 01" "" } | Should -Throw "*Cannot bind argument to parameter 'CsvFile'*"
    }

    It "Handles connection failure without throwing" {
        Mock Open-ApiConnection { throw "Cannot establish connection with test string" } -ModuleName DOrc.Cmdlet
        Mock Write-Host -ModuleName DOrc.Cmdlet

        { Export-DOrcProperties "test string" "DV 01" "out.csv" } | Should -Not -Throw
        Should -Invoke Write-Host -ModuleName DOrc.Cmdlet -Times 1 -Exactly -ParameterFilter {
            $Object -like "*Cannot establish connection with*"
        }
    }
}

Describe "Convert-CsvToCsvProperties validation" {
    It "Accepts valid property data" {
        $validData = [PSCustomObject]@{
            PropertyName = "ValidProperty"
            Environment  = "Development"
            Value        = "ValidValue"
            IsSecured    = "false"
        }

        $result = $validData | Convert-CsvToCsvProperties
        $result.PropertyName | Should -Be "ValidProperty"
        $result.Value | Should -Be "ValidValue"
        $result.Environment | Should -Be "Development"
        $result.IsSecured | Should -Be $false
    }

    It "Rejects blank PropertyName" {
        $invalidData = [PSCustomObject]@{
            PropertyName = ""
            Environment  = "Development"
            Value        = "SomeValue"
            IsSecured    = "false"
        }

        { $invalidData | Convert-CsvToCsvProperties } | Should -Throw "*PropertyName cannot be blank or empty*"
    }

    It "Rejects whitespace-only PropertyName" {
        $invalidData = [PSCustomObject]@{
            PropertyName = "   "
            Environment  = "Development"
            Value        = "SomeValue"
            IsSecured    = "false"
        }

        { $invalidData | Convert-CsvToCsvProperties } | Should -Throw "*PropertyName cannot be blank or empty*"
    }

    It "Rejects blank Value" {
        $invalidData = [PSCustomObject]@{
            PropertyName = "ValidProperty"
            Environment  = "Development"
            Value        = ""
            IsSecured    = "false"
        }

        { $invalidData | Convert-CsvToCsvProperties } | Should -Throw "*Property value cannot be blank or empty. Found blank value for property 'ValidProperty'*"
    }

    It "Rejects whitespace-only Value" {
        $invalidData = [PSCustomObject]@{
            PropertyName = "ValidProperty"
            Environment  = "Development"
            Value        = "   "
            IsSecured    = "false"
        }

        { $invalidData | Convert-CsvToCsvProperties } | Should -Throw "*Property value cannot be blank or empty. Found blank value for property 'ValidProperty'*"
    }
}

Describe "OAuth Authority Discovery, JWT Claim Extraction, and Impersonation" {
    It "Resolves OAuthAuthority, ClientId, and Scope from /ApiConfig" {
        InModuleScope DOrc.Cmdlet {
            Mock Invoke-WebRequest {
                [PSCustomObject]@{
                    StatusCode = 200
                    Content    = '{"OAuthAuthority":"https://idp.sefe.eu/","OAuthUiClientId":"dorc-ui","OAuthUiRequestedScopes":"openid profile dorc-api"}'
                }
            }

            $cfg = Resolve-DOrcIdentityAuthorityFromApiConfig -ApiUrl "https://dorc.local/api/"
            $cfg.AuthorityUrl | Should -Be "https://idp.sefe.eu"
            $cfg.ClientId | Should -Be "dorc-ui"
            $cfg.Scope | Should -Be "openid profile dorc-api"
        }
    }

    It "Extracts preferred_username from base64url-encoded JWT payload and sets X-Impersonate-User header" {
        $payloadJson = '{"preferred_username":"dmitry.repin@sefe.eu","sub":"123"}'
        $payloadBase64 = [Convert]::ToBase64String([System.Text.Encoding]::UTF8.GetBytes($payloadJson)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
        $fakeJwt = "eyJhbGciOiJSUzI1NiJ9.$payloadBase64.signature"

        $resolved = Resolve-DOrcUsernameFromToken -Token $fakeJwt
        $resolved | Should -Be "dmitry.repin@sefe.eu"

        Set-DOrcImpersonateUser -Username $resolved
        Get-DOrcImpersonateUser | Should -Be "dmitry.repin@sefe.eu"

        Clear-DOrcImpersonateUser
        Get-DOrcImpersonateUser | Should -Be $null
    }
}


