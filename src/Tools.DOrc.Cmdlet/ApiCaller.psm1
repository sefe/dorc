using module ".\Models.psm1"
Class ApiCaller
{
        # Static properties
        static [string]$AccessToken = $null
        static [string]$ImpersonateUser = $null

        # Public static method to set access token
        static [void]SetAccessToken([string]$token)
        {
                [ApiCaller]::AccessToken = $token
        }

        # Public static method to clear access token
        static [void]ClearAccessToken()
        {
                [ApiCaller]::AccessToken = $null
        }

        # Public static method to set impersonate user
        static [void]SetImpersonateUser([string]$username)
        {
                [ApiCaller]::ImpersonateUser = $username
        }

        # Public static method to clear impersonate user
        static [void]ClearImpersonateUser()
        {
                [ApiCaller]::ImpersonateUser = $null
        }

        static [ApiResult]InvokeGet([string]$Path,[string]$queryString)
        {
                try
                {
                        $headers = @{}

                        if ([ApiCaller]::AccessToken)
                        {
                                $headers["Authorization"] = "Bearer $([ApiCaller]::AccessToken)"
                        }

                        if ([ApiCaller]::ImpersonateUser)
                        {
                                $headers["X-Impersonate-User"] = [ApiCaller]::ImpersonateUser
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

                        if ([ApiCaller]::AccessToken)
                        {
                                $headers["Authorization"] = "Bearer $([ApiCaller]::AccessToken)"
                        }

                        if ([ApiCaller]::ImpersonateUser)
                        {
                                $headers["X-Impersonate-User"] = [ApiCaller]::ImpersonateUser
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

                        if ([ApiCaller]::AccessToken)
                        {
                                $headers["Authorization"] = "Bearer $([ApiCaller]::AccessToken)"
                        }

                        if ([ApiCaller]::ImpersonateUser)
                        {
                                $headers["X-Impersonate-User"] = [ApiCaller]::ImpersonateUser
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
