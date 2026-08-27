---
name: SPEC-S-004 - registry probe move
description: Move remote operating-system registry access behind the Windows worker while preserving the public response and error contract.
type: spec
status: READY FOR REVIEW
---

# SPEC-S-004 - registry probe move

## Scope

S-004 moves the `RefDataServers` remote-registry operation from the primary API to
`Dorc.Api.WindowsWorker`. The primary endpoint and `ServerOperatingSystemApiModel`
remain unchanged. Only the implementation boundary moves.

## Contract

- The primary calls `GET /remote-server/operating-system?serverName=...` over the
  authenticated loopback worker client.
- A successful worker response preserves `ProductName` and `CurrentVersion`.
- A missing registry key or expected remote-registry access failure remains HTTP 400.
- Worker transport failure follows the S-003 `windows_worker_unavailable` HTTP 503 contract.

## Parity fixture

`Dorc.Api.WindowsWorker.Tests/Fixtures/registry-operating-system.json` records the
pre-split request, registry values, and public response shape anchored to `main`
commit `2b616740`. `RemoteServerControllerTests` drives those values through the
production mapping function and controller seam. Changing either response field or
the missing-key 400 behavior fails the fixture test.

## Verification

```powershell
dotnet test src\Dorc.Api.WindowsWorker.Tests\Dorc.Api.WindowsWorker.Tests.csproj `
  --filter "FullyQualifiedName~RemoteServerControllerTests"
dotnet test src\Dorc.Api.Tests\Dorc.Api.Tests.csproj `
  --filter "FullyQualifiedName~WindowsWorkerClientTests"
```

The fixture format established here is extended by S-005 and S-006 for daemon and
password-reset parity.
