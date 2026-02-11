# DOrc.Cmdlet Module Usage Guide

**Version:** 2026.02.10.1  
**Date:** February 10, 2026  
**Location:** C:\#Work\DOrc.Cmdlet\2026.02.10.1

## Overview

This version adds Identity Server token authentication and optional user impersonation headers for DORC API calls.

## Quick Start

### 1) Import the module

```powershell
Import-Module "C:\#Work\DOrc.Cmdlet\2026.02.10.1\DOrc.Cmdlet.psd1" -Force
```

### 2) Authenticate with Identity Server

```powershell
$clientId = "dorc-cli"
$clientSecret = "<client-secret>"
$scope = "dorc-api.manage"
$DorcApiUrl = "https://deploymentportal:8443"

Connect-DOrcWithIdentityServer -ApiUrl $DorcApiUrl -ClientId $clientId -ClientSecret $clientSecret -Scope $scope
```

### 3) Use export/import cmdlets

```powershell
Export-DOrcProperties -Environment "QA_04" -CsvFile "C:\Export\qa.csv" -ApiUrl $DorcApiUrl
Import-DOrcProperties -CsvFile "C:\Import\qa.csv" -ApiUrl $DorcApiUrl
```

## Impersonation Header (Optional)

If your DORC API is configured to accept a custom header, you can send the current user identity:

```powershell
Set-DOrcImpersonateUser
Import-DOrcProperties -CsvFile "C:\Import\qa.csv" -ApiUrl $DorcApiUrl
```

Clear it when done:

```powershell
Clear-DOrcImpersonateUser
```

## Notes

- This module requires a valid bearer token. It does not fall back to Windows Integrated Authentication.
- On-behalf-of authorization is not supported by the DORC application at this time.

## Tests and Docs

All tests and supporting documentation are stored under:

```
C:\#Work\DOrc.Cmdlet\2026.02.10.1\docs
```

Key files:
- docs\Test-Module.ps1
- docs\Test-Phase2.ps1
- docs\QUICK_REFERENCE.md
- docs\PHASE2-SETUP-GUIDE.md

## Build Helper

UpdateVersion.ps1 is kept in the module root for build pipelines that bump the manifest version.
