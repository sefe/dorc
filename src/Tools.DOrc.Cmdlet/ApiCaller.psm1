using module ".\Models.psm1"
Class ApiCaller
{	
	static [ApiResult]InvokeGet([string]$Path,[string]$queryString)
	{
		try
		{
			if ($queryString){
				$url=$Path + "?" + $queryString
				$response = Invoke-WebRequest -Method GET -Uri $url -ContentType "application/json; charset=utf-8" -UseDefaultCredentials;
			}else{
                #$url=$this.ApiUrl + "/" + $Path
				$response = Invoke-WebRequest -Method GET -Uri $Path -ContentType "application/json; charset=utf-8" -UseDefaultCredentials
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
			$response = Invoke-WebRequest -Method POST -Uri $Path -ContentType "application/json; charset=utf-8" -Body $Body -UseDefaultCredentials
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
			$response = Invoke-WebRequest -Method PUT -Uri $Path -ContentType "application/json; charset=utf-8" -Body $Body -UseDefaultCredentials
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

