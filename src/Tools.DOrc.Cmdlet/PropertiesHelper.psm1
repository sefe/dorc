using module ".\ManageProperty.psm1"
using module ".\Models.psm1"
class PropertiesHelper 
{
	[ManageProperty]$repository
	PropertiesHelper() 
	{
		$this.repository=[ManageProperty]::new()
	}

	AddProperties ([CsvProperties[]]$Properties)
	{		
		$PropertiesToAdd=@()	
		foreach ($prop in $Properties){
			$exists=$this.repository.PropertyExists($prop.PropertyName) 
			if (-not $exists){
				$PropertiesToAdd+=$prop
			}
		}
		if ($PropertiesToAdd){
			write-host -NoNewline "Creating missing properties... "
			[Property[]]$propertyArray=$PropertiesToAdd | Convert-CsvPropertiesToListProperties
			$body=ConvertTo-Json $propertyArray
			[ApiResult]$result=$this.repository.AddProperties($body)
			if ($result.ReturnCode -eq 200)
			{
				Write-Host -ForegroundColor Green "Done"				
				$props=$result.Value
				foreach ($p in $props)
				{
					Write-Host -NoNewline $p.Item.Property.Name": " 
					if ($p.Status -eq "Success"){
						Write-Host -ForegroundColor green $p.Status
					}
					else {
						Write-Host -ForegroundColor red $p.Status
					}
				}
			}else{
				Write-Host -ForegroundColor Red "Fail!"
				Write-Host -ForegroundColor red "Reason: "$result.Message
				throw $result.Message
			}
		}	
	}

	AddPropertyValues ([CsvProperties[]]$Properties)
	{
		$valuesArray=@()
		#$Properties | Convert-CsvPropertiesToValuesList
		foreach ($p in $Properties)
		{
			if (-not $this.repository.ValueExist($p.Environment,$p.PropertyName) )
			{
				$valuesArray+=$p
			}
		}
		if ($valuesArray)
		{
			Write-Host -NoNewline "Creating missing property values... "
			[PropertyValue[]]$values=$valuesArray | Convert-CsvPropertiesToValuesList
			$body=ConvertTo-Json $values
			[ApiResult]$result=$this.repository.AddPropertyValue($body)
			if ($result.ReturnCode -eq 200)
			{
				write-host -ForegroundColor green "Done"
				foreach ($v in $result.Value)
				{
					Write-Host -NoNewline $v.Item.Property.Name": "  
					if ($v.Status -eq "Success"){
						Write-Host -ForegroundColor Green $v.Status
					}
					else {
						Write-Host -ForegroundColor red $v.Status
					}
				}
			}else{
				Write-Host -ForegroundColor Red "Fail!"
				Write-Host -ForegroundColor red "Reason: "$result.Message
				throw $result.Message
			}
		}	
	}

	UpdatePropertyValues ([CsvProperties[]]$Properties)
	{		
		$valuesArray=@()
		foreach ($p in $Properties)
		{			
			[ApiResult]$value=$this.repository.GetPropertyValue($p.Environment,$p.PropertyName)
			if ($value.ReturnCode -eq 200){
				if ($value.Value[0].Value  -ne $p.Value )
				{
					$valuesArray+=$p
				}
			}
			
		}
		if ($valuesArray)
		{
			Write-Host -NoNewline "Updating property values... "
			[PropertyValue[]]$values=$valuesArray | Convert-CsvPropertiesToValuesList
			$body=ConvertTo-Json $values
			[ApiResult]$result=$this.repository.UpdatePropertyValue($body)
			if ($result.ReturnCode -eq 200)
			{
				Write-Host -ForegroundColor Green "Done"
				foreach ($v in $result.Value)
				{
					Write-Host -NoNewline $v.Item.Property.Name": " 
					if ($v.Status -eq "Success")
					{
						Write-Host -ForegroundColor Green $v.Status
					}
					else {
						Write-Host -ForegroundColor Red $v.Status
					}
					
				}
			}else{
				Write-Host -ForegroundColor Red "Fail!"
				Write-Host -ForegroundColor red "Reason: "$result.Message
				throw $result.Message
			}
		}
	}
	[CsvProperties[]]ExportProperies([string]$Environment)
	{
		[ApiResult]$result=$this.repository.GetEnvironmentProperties($Environment)
		if ($result.ReturnCode -eq 200)
		{
			[CsvProperties[]]$properties=$result.Value | Convert-PropertyValueToCsvProperty
			return $properties
		}else
		{
			throw $result.Message
		}
	}
}