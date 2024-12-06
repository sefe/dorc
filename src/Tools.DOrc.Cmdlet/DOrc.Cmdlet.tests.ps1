#
# This is a PowerShell Unit Test file.
# You need a unit test framework such as Pester to run PowerShell Unit tests. 
# You can download Pester from http://go.microsoft.com/fwlink/?LinkID=534084
#
Using module ".\DOrc.Cmdlet.psm1"
Import-Module Pester -RequiredVersion "4.4.2"
Remove-Module DOrc.Cmdlet
Import-Module ".\DOrc.Cmdlet.psm1"
$projectDir = $MyInvocation.MyCommand.Path | Split-Path -Parent | Split-Path -Parent 

Context "Open-ApiConnection test" {
    
    #It 'run without parameters '{
    #    {Open-ApiConnection} | Should -Throw "Cannot establish connection with"
    #}
    $ApiUrl = "test string"    
    It 'Open-ApiConnection fails to connect ' {
        { Open-ApiConnection  $ApiUrl } | Should -Throw "Cannot establish connection with test string"
    }
}

Context "Import-DOrcProperties" {

    It 'Fails with empty $ApiUrl'{
        {Import-DOrcProperties $ApiUrl $CsvFile} | Should -Throw "Cannot bind argument to parameter 'ApiUrl' because it is an empty string"
    }
    $ApiUrl = "test string" 

    It 'Fails with empty $CsvFile'{
        {Import-DOrcProperties $ApiUrl $CsvFile} | Should -Throw "Cannot bind argument to parameter 'CsvFile' because it is an empty string"
    }
    $CsvFile = "test csv string"
       
    Mock Write-Host -ParameterFilter {$Object -match "Cannot establish connection with test string"} -ModuleName DOrc.Cmdlet
    It 'cannot connect' {
        Import-DOrcProperties $ApiUrl $CsvFile
        Assert-MockCalled Write-Host -Exactly 1 -Scope It { $Object -match "Cannot establish connection with test string" } -ModuleName DOrc.Cmdlet
    }
    
    Mock Write-Host -ParameterFilter {$Object -match "Loading properties data from test csv string"} -ModuleName DOrc.Cmdlet
    it 'load properties' {
        Import-DOrcProperties $ApiUrl $CsvFile
        Assert-MockCalled Write-Host -Exactly 1 -Scope It { $Object -and [string]$Object.Contains("Loading properties data from") } -ModuleName DOrc.Cmdlet
    }
    
    Mock Open-ApiConnection -ParameterFilter {$ApiUrl -match "test string" } -ModuleName DOrc.Cmdlet
    Mock Get-Variable -ModuleName DOrc.Cmdlet
    $path=Get-Location
    $file="Could not find file '" + $path +"\"+ $CsvFile
    It 'Goes to helper' {
        {Import-DOrcProperties $ApiUrl $CsvFile} | Should -Throw $file
    }
}


Context "Export-DOrcProperties" {

    It 'Fails with empty $ApiUrl'{
        {Export-DOrcProperties $ApiUrl $Environment $CsvFile} | Should -Throw "Cannot bind argument to parameter 'ApiUrl' because it is an empty string"
    }
    $ApiUrl = "test string" 

    It 'Fails with empty $Environment'{
        {Export-DOrcProperties $ApiUrl $Environment $CsvFile} | Should -Throw "Cannot bind argument to parameter 'Environment' because it is an empty string."
    }
    $Environment = "test Environment string"

    It 'Fails with empty $CsvFile'{
        {Export-DOrcProperties $ApiUrl $Environment $CsvFile} | Should -Throw "Cannot bind argument to parameter 'CsvFile' because it is an empty string"
    }
    $CsvFile = "test csv string"
    
    Mock Write-Host -ParameterFilter {$Object -match "Cannot establish connection with test string"} -ModuleName DOrc.Cmdlet
    It 'cannot connect' {
        {Export-DOrcProperties $ApiUrl $Environment $CsvFile} | Should -Not -Throw #silently fails
        Assert-MockCalled Write-Host -Exactly 1 -Scope It { $Object -and [string]$Object.Contains("Cannot establish connection with") } -ModuleName DOrc.Cmdlet
    }

    Mock Open-ApiConnection -ParameterFilter {$ApiUrl -match "test string" } -ModuleName DOrc.Cmdlet
    Mock Get-Variable -ModuleName DOrc.Cmdlet    
    $path=Get-Location
    $file="Could not find file '" + $path +"\"+ $CsvFile
    It 'Goes to helper' {
        {Import-DOrcProperties $ApiUrl $CsvFile} | Should -Throw $file
    }
}
