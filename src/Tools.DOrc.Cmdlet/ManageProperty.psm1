using module ".\Models.psm1"
using module ".\ApiCaller.psm1"
Class ManageProperty
{
    [ApiConnection]$Connection;
    ManageProperty()
    {
        $ApiConnection = Get-Variable -Name ApiConnection -Scope Global
        $this.Connection = $ApiConnection.Value
    }


    [ApiResult]
    GetProperty([string] $name)
    {
        if ($name)
        {
            # The API exposes single-property lookup as a path segment route
            # (GET /Properties/id={id}); passing "id=" as a query string instead
            # hits GET /Properties (get-all), which ignores the filter and returns
            # every property with HTTP 200 - making PropertyExists always true.
            $path = $this.Connection.Property + "/id=" + [uri]::EscapeDataString($name)
            $property = [ApiCaller]::InvokeGet($path, "")
        }
        else
        {
            $property = [ApiCaller]::InvokeGet($this.Connection.Property, "")
        }

        return $property
    }

    [bool]
    PropertyExists([string]$Name)
    {
        [bool]$exists = $false
        if ($Name)
        {
            [ApiResult]$property = $this.GetProperty($Name)
            if ($property.ReturnCode -eq 404)
            {
                $exists = $false
            }
            if ($property.ReturnCode -eq 200)
            {
                $exists = $true
            }
            return $exists
        }
        else
        {
            return $false
        }
    }

    [ApiResult]
    AddProperties([string]$body)
    {
        [ApiResult]$result = [ApiCaller]::InvokePost($this.Connection.Property, $body)
        return $result
    }

    [ApiResult]
    GetPropertyValue([string]$Environment, [string]$PropertyName)
    {
        $queryString = "environmentName=" + $Environment + "&propertyName=" + $PropertyName
        [ApiResult]$result = [ApiCaller]::InvokeGet($this.Connection.PropertyValues, $queryString)
        return $result
    }

    [bool]
    ValueExist([string]$Environment, [string]$PropertyName)
    {
        [ApiResult]$result = $this.GetPropertyValue($Environment, $PropertyName)
        switch ($result.ReturnCode)
        {
            200 {
                return $true
            }
            404 {
                return $false
            }
            Default {
                throw $result.Message
            }
        }
        return $false;
    }

    [ApiResult]
    AddPropertyValue([string]$Body)
    {
        [ApiResult]$result = [ApiCaller]::InvokePost($this.Connection.PropertyValues, $Body)
        return $result
    }

    [ApiResult]
    UpdatePropertyValue([string]$Body)
    {
        [ApiResult]$result = [ApiCaller]::InvokePut($this.Connection.PropertyValues, $Body)
        return $result
    }

    [ApiResult]
    GetEnvironmentProperties([string]$Environment)
    {
        [string]$query = "environmentName=" + $Environment
        [ApiResult]$result = [ApiCaller]::InvokeGet($this.Connection.PropertyValues, $query)
        return $result
    }

}