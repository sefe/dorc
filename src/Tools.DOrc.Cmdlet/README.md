# DOrc.Cmdlet Module Usage Guide

**Version:** 0.0.0-demo  

## Overview

This version authenticates users via Identity Server and sends user identity to DORC for audit.

## Quick Start

### 1) Import the module

```powershell
Import-Module "C:\Path\To\DOrc.Cmdlet\0.0.0-demo\DOrc.Cmdlet.psd1" -Force
```

### 2) Configure Identity Server (once per session)

```powershell
Set-DOrcIdentityServerConfig `
	-AuthorityUrl "https://identityserver:5200" `
	-TokenEndpoint "/connect/token" `
	-ClientId "dorc-cli" `
	-Scope "openid profile"
```

### 3) Connect as current user

```powershell
Connect-DOrcIdentityServer
```

This opens the system browser to the Identity Server login page (Authorization Code + PKCE flow). Once you complete login, the access token is stored automatically and the tab can be closed.

### 4) Use export/import cmdlets

```powershell
Export-DOrcProperties -Environment "SAMPLE_ENV" -CsvFile "C:\Temp\sample-export.csv" -ApiUrl $DorcApiUrl
Import-DOrcProperties -CsvFile "C:\Temp\sample-import.csv" -ApiUrl $DorcApiUrl
```

If no token is present in the current session, these cmdlets trigger Identity Server authentication automatically.

## Impersonation Header and DORC Audit

This module sends `X-Impersonate-User` with the current user identity. During import, this is set automatically so the username appears in DORC audit records.

You can still set it manually when needed:

```powershell
Set-DOrcImpersonateUser
Import-DOrcProperties -CsvFile "C:\Temp\sample-import.csv" -ApiUrl $DorcApiUrl
```

Clear it when done:

```powershell
Clear-DOrcImpersonateUser
```

## Notes

- Primary authentication flow is Identity Server delegated user login via `Connect-DOrcIdentityServer`.
- Manual bearer-token entry is no longer used; token lifecycle is handled by `Connect-DOrcIdentityServer`.

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
