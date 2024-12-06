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
            $queryString = "id=" + $name
            $property = [ApiCaller]::InvokeGet($this.Connection.Property, $queryString)
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