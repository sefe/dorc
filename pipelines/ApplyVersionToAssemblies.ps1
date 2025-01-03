<#
	.SYNOPSIS
		Script called before a solution/project is built to update the assembly version info

	.DESCRIPTION
         Look for a 0.0.0.0 pattern in the build number. If found use it to version the assemblies.

         For example, if the 'Build number format' build process parameter $(BuildDefinitionName)_$(Year:yyyy).$(Month).$(DayOfMonth)$(Rev:.r)
         then your build numbers come out like this:
             "Build HelloWorld_2013.07.19.1" - This script would then apply version 2013.07.19.1 to your assemblies.

	.PARAMETER  SourcesPath
		The path where to find the source files to update------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------

    .PARAMETER  VersionRegex
		Regular expression pattern to find the version in the build number and then apply it to the assemblies

	.NOTES
        <copyright file="ApplyVersionToAssemblies.ps1">(c) SEFE. All rights reserved.</copyright>
		Version: 0.0.0.0
#>

[CmdletBinding()]
Param(
    [ValidateNotNullOrEmpty()]
    [System.String]$SourcesPath = $Env:BUILD_SOURCESDIRECTORY,
    [ValidateNotNullOrEmpty()]
    [System.String]$BuildNumber = $Env:BUILD_BUILDNUMBER,
    [ValidateNotNullOrEmpty()]
    [System.String]$VersionRegex = "\d+\.\d+\.\d+\.\d+"
)

Clear-Host;
# If this script is not running on a build server, remind user to 
# set environment variables so that this script can be debugged
if(-not ($SourcesPath -and $BuildNumber))
{
    Write-Error "You must set the following environment variables or pass -SourcesPath / -BuildNumber"
    Write-Error "to test this script interactively."
    Write-Host '$Env:BUILD_SOURCESDIRECTORY - For example, enter something like:'
    Write-Host '$Env:BUILD_SOURCESDIRECTORY = "C:\code\FabrikamTFVC\HelloWorld"'
    Write-Host '$Env:BUILD_BUILDNUMBER - For example, enter something like:'
    Write-Host '$Env:BUILD_BUILDNUMBER = "Build HelloWorld_0000.00.00.0"'
    exit 1
}

# Make sure path to source code directory is available
if (-not $SourcesPath)
{
    Write-Error ("SourcesPath parameter value or BUILD_SOURCESDIRECTORY environment variable are missing.")
    exit 1
}
elseif (-not (Test-Path $SourcesPath))
{
    Write-Error "SourcesPath does not exist: $SourcesPath"
    exit 1
}
Write-Host "BUILD_SOURCESDIRECTORY: $SourcesPath"

# Make sure there is a build number
if (-not $BuildNumber)
{
    Write-Error ("BuildNumber parameter or BUILD_BUILDNUMBER environment variable is missing.")
    exit 1
}
Write-Host "BUILD_BUILDNUMBER: $BuildNumber"

# Get and validate the version data
$VersionData = [regex]::matches($BuildNumber,$VersionRegex)
switch($VersionData.Count)
{
   0        
      { 
         Write-Error "Could not find version number data in BUILD_BUILDNUMBER."
         exit 1
      }
   1 {}
   default 
      { 
         Write-Warning "Found more than instance of version data in BUILD_BUILDNUMBER." 
         Write-Warning "Will assume first instance is version."
      }
}
$NewVersion = $VersionData[0]
Write-Host "Version: $NewVersion"
Write-Host "##vso[task.setvariable variable=BuildVersion;]$($NewVersion)"

# Apply the version to the assembly property files
$files = gci $SourcesPath -recurse -include "*Properties*","*Includes*" | 
    ?{ $_.PSIsContainer } | 
    foreach { gci -Path $_.FullName -Recurse -include *AssemblyInfo.* }
if($files)
{
    Write-Host "Will check $($files.count) files."

    foreach ($file in $files) 
    {
        Write-Host "   $($file.FullName)" -NoNewline
        $filecontent = Get-Content($file)
        
        $versionInstances = Select-String $VersionRegex -InputObject $filecontent -AllMatches
        if ($versionInstances.Count -gt 0)
        {
            attrib $file -r
            $filecontent -replace $VersionRegex, $NewVersion | Out-File $file
            Write-Host " **version $($NewVersion) applied**"
        }
        else
        {
            Write-Host " No matches"
        }
    }
}
else
{
    Write-Warning "Found no files."
}