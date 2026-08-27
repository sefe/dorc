---
name: SPEC-S-006 - password-reset impersonation move
description: Move SQL application-password reset impersonation to the Windows worker while preserving authorization, audit, and response behavior.
type: spec
status: READY FOR REVIEW
---

# SPEC-S-006 - password-reset impersonation move

## Scope

S-006 leaves caller authorization in the primary API and moves only the
Windows service-account impersonation and SQL password-reset execution to
`Dorc.Api.WindowsWorker`.

## Security and contract

- The primary authorizes the caller before forwarding the request.
- `CallerIdentity` is audit-only and is never used as the impersonated identity.
- The worker uses its configured password-reset service account.
- `ServerName`, `Username`, and the `ApiBoolResult` response remain unchanged.
- Missing inputs and service-account logon failures remain actionable HTTP 400
  responses; worker transport failure follows the S-003 HTTP 503 contract.

## Parity fixture

`Dorc.Api.WindowsWorker.Tests/Fixtures/password-reset.json` records a successful
pre-split request/response anchored to `ResetAppPasswordController.cs` at
`2b616740`. `PasswordResetControllerTests` drives the fixture through the worker
controller and verifies the exact target server, username, and result.

## Verification

```powershell
dotnet test src\Dorc.Api.WindowsWorker.Tests\Dorc.Api.WindowsWorker.Tests.csproj `
  --filter "FullyQualifiedName~PasswordResetControllerTests"
dotnet test src\Dorc.Api.Tests\Dorc.Api.Tests.csproj `
  --filter "FullyQualifiedName~ResetAppPasswordControllerTests"
```
