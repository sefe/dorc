using module ".\Models.psm1"
Class DorcApiClient
{
        # Static properties
        static [string]$AccessToken = $null
        static [string]$ImpersonateUser = $null

        # Public static method to set access token
        static [void]SetAccessToken([string]$token)
        {
                [DorcApiClient]::AccessToken = $token
        }

        # Public static method to clear access token
        static [void]ClearAccessToken()
        {
                [DorcApiClient]::AccessToken = $null
        }

        # Public static method to set impersonate user
        static [void]SetImpersonateUser([string]$username)
        {
                [DorcApiClient]::ImpersonateUser = $username
        }

        # Public static method to clear impersonate user
        static [void]ClearImpersonateUser()
        {
                [DorcApiClient]::ImpersonateUser = $null
        }

        static [ApiResult]InvokeGet([string]$Path,[string]$queryString)
        {
                try
                {
                        $headers = @{}

                        if ([DorcApiClient]::AccessToken)
                        {
                                $headers["Authorization"] = "Bearer $([DorcApiClient]::AccessToken)"
                        }

                        if ([DorcApiClient]::ImpersonateUser)
                        {
                                $headers["X-Impersonate-User"] = [DorcApiClient]::ImpersonateUser
                        }

                        if ($queryString){
                                $url=$Path + "?" + $queryString
                                $response = Invoke-WebRequest -Method GET -Uri $url -ContentType "application/json; charset=utf-8" -Headers $headers -UseBasicParsing
                        }else{
                                $response = Invoke-WebRequest -Method GET -Uri $Path -ContentType "application/json; charset=utf-8" -Headers $headers -UseBasicParsing
                        }
                        $result=[ApiResult]::new()
                        $result.Value=($response.Content | ConvertFrom-Json)
                        $result.ReturnCode=$response.StatusCode
                        return  $result

                }catch
                {
                        $result=[ApiResult]::new()
                        $result.ReturnCode=$_.Exception.Response.StatusCode.value__
                        $result.Message=$_.Exception.Response.StatusDescription
                        return $result
                }
        }

        static [ApiResult]InvokePost([string]$Path,[string]$Body)
        {
                try
                {
                        $headers = @{}

                        if ([DorcApiClient]::AccessToken)
                        {
                                $headers["Authorization"] = "Bearer $([DorcApiClient]::AccessToken)"
                        }

                        if ([DorcApiClient]::ImpersonateUser)
                        {
                                $headers["X-Impersonate-User"] = [DorcApiClient]::ImpersonateUser
                        }

                        $response = Invoke-WebRequest -Method POST -Uri $Path -ContentType "application/json; charset=utf-8" -Body $Body -Headers $headers -UseBasicParsing

                        $result=[ApiResult]::new()
                        $result.Value=($response.Content | ConvertFrom-Json)
                        $result.ReturnCode=$response.StatusCode
                        return  $result

                }catch
                {
                        $result=[ApiResult]::new()
                        $result.ReturnCode=$_.Exception.Response.StatusCode.value__
                        $result.Message=$_.Exception.Response.StatusDescription
                        return $result
                }
        }

        static [ApiResult]InvokePut([string]$Path,[string]$Body)
        {
                try
                {
                        $headers = @{}

                        if ([DorcApiClient]::AccessToken)
                        {
                                $headers["Authorization"] = "Bearer $([DorcApiClient]::AccessToken)"
                        }

                        if ([DorcApiClient]::ImpersonateUser)
                        {
                                $headers["X-Impersonate-User"] = [DorcApiClient]::ImpersonateUser
                        }

                        $response = Invoke-WebRequest -Method PUT -Uri $Path -ContentType "application/json; charset=utf-8" -Body $Body -Headers $headers -UseBasicParsing

                        $result=[ApiResult]::new()
                        $result.Value=($response.Content | ConvertFrom-Json)
                        $result.ReturnCode=$response.StatusCode
                        return  $result

                }catch
                {
                        $result=[ApiResult]::new()
                        $result.ReturnCode=$_.Exception.Response.StatusCode.value__
                        $result.Message=$_.Exception.Response.StatusDescription
                        return $result
                }
        }
}
