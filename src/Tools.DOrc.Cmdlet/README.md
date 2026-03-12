# DOrc.Cmdlet Module Usage Guide

**Version:** 0.0.0-demo  

## Overview

This version uses user JWT/Bearer token authentication and optional user impersonation headers for DORC API calls.

## Quick Start

### 1) Import the module

```powershell
Import-Module "C:\Path\To\DOrc.Cmdlet\0.0.0-demo\DOrc.Cmdlet.psd1" -Force
```

### 2) Use export/import cmdlets

```powershell
Export-DOrcProperties -Environment "SAMPLE_ENV" -CsvFile "C:\Temp\sample-export.csv" -ApiUrl $DorcApiUrl
Import-DOrcProperties -CsvFile "C:\Temp\sample-import.csv" -ApiUrl $DorcApiUrl
```

## Use User JWT/Bearer Token (Postman-style)

If you already have a user access token (for example from Postman login flow),
you can use it directly for properties import/export.

### Set token once, run multiple commands

```powershell
$userJwt = "<JWT_TOKEN>"
Set-DOrcBearerToken -Token $userJwt

Export-DOrcProperties -Environment "SAMPLE_ENV" -CsvFile "C:\Temp\sample-export.csv" -ApiUrl $DorcApiUrl
Import-DOrcProperties -CsvFile "C:\Temp\sample-import.csv" -ApiUrl $DorcApiUrl
```

### Pass token inline per command

```powershell
$userJwt = "<JWT_TOKEN>"
Export-DOrcProperties -Environment "SAMPLE_ENV" -CsvFile "C:\Temp\sample-export.csv" -ApiUrl $DorcApiUrl -BearerToken $userJwt
Import-DOrcProperties -CsvFile "C:\Temp\sample-import.csv" -ApiUrl $DorcApiUrl -BearerToken $userJwt
```

### Optional token maintenance

```powershell
Get-DOrcBearerTokenStatus
Clear-DOrcBearerToken
```

### Optional inline token usage

```powershell
$userJwt = "<JWT_TOKEN>"
Export-DOrcProperties -Environment "SAMPLE_ENV" -CsvFile "C:\Temp\sample-export.csv" -ApiUrl $DorcApiUrl -BearerToken $userJwt
Import-DOrcProperties -CsvFile "C:\Temp\sample-import.csv" -ApiUrl $DorcApiUrl -BearerToken $userJwt
```

## Impersonation Header (Optional)

If your DORC API is configured to accept a custom header, you can send the current user identity:

```powershell
Set-DOrcImpersonateUser
Import-DOrcProperties -CsvFile "C:\Temp\sample-import.csv" -ApiUrl $DorcApiUrl
```

Clear it when done:

```powershell
Clear-DOrcImpersonateUser
```

## Notes

- This module requires a valid bearer token. It does not fall back to Windows Integrated Authentication.
- Only JWT/Bearer token authentication is supported in this module.

## Tests and Docs

All tests and supporting documentation are stored under:

```
C:\Path\To\DOrc.Cmdlet\docs
```

Key files:
- docs\Test-Module.ps1
- docs\Test-Phase2.ps1
- docs\QUICK_REFERENCE.md
- docs\PHASE2-SETUP-GUIDE.md

## Build Helper

UpdateVersion.ps1 is kept in the module root for build pipelines that bump the manifest version.
