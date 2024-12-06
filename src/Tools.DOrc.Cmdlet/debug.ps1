#Remove-Module DOrc.Cmdlet -Force
Import-Module .\DOrc.Cmdlet.psm1 -Force

$ApiUrl = "http://localhost:32194/api" 
$Environment = "Comms Tool DV"
$CsvFile = "m:\out.csv"
#Import-DOrcProperties -CsvFile M:\props.csv -ApiUrl $ApiUrl
#Import-DOrcProperties  -ApiUrl $ApiUrl -CsvFile M:\elasticDorcProperties.csv
Import-DOrcProperties  -ApiUrl http://depapp02dv:8080/api -CsvFile M:\elasticDorcProperties.csv
#Export-DOrcProperties -Environment $Environment  -CsvFile $CsvFile -ApiUrl $ApiUrl
#Invoke-Pester .\DOrc.Cmdlet.tests.ps1
# $s = New-PSSession 
# Invoke-Command -ScriptBlock {
#     Set-Location "E:\Data\Repos\Deployment\Orchestrator\Tools.DOrc.Cmdlet"
#     Import-Module .\DOrc.Cmdlet.psm1
#     Add-EndurUser -account pmarchanka -url http://depapp02dv/referencedataapi/api } -Session $s

#Add-NonProdEndurUser -account pm -Apiurl http://depapp02dv/referencedataapi/api 