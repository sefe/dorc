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

