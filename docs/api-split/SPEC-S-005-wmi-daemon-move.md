---
name: SPEC-S-005 - WMI and daemon service-control move
description: Move daemon probing and service control to the Windows worker without changing the primary API contract.
type: spec
status: READY FOR REVIEW
---

# SPEC-S-005 - WMI and daemon service-control move

## Scope

S-005 moves ping, service-status probing, service start/stop/restart, and reboot
operations from the primary compile graph to `Dorc.Api.WindowsWorker`. The primary
continues to own daemon selection, credentials, authorization, and observation storage.

## Contract

- `POST /daemons/probe` accepts the existing daemon identity fields and returns the
  same fields plus the pre-split status string.
- `POST /daemons/change-state` uses `Status` as the requested action and returns the
  resulting daemon shape.
- `POST /remote-server/reboot` retains the existing success contract.
- Worker rejections remain HTTP 400; transport unavailability follows S-003 HTTP 503.

## Parity fixture

`Dorc.Api.WindowsWorker.Tests/Fixtures/daemon-probe.json` records a representative
pre-split request and response anchored to `Dorc.Core/DaemonStatusProbe.cs` at
`2b616740`. `DaemonsControllerTests` verifies every public daemon field and the
state-change response through the worker controller seam.

## Verification

```powershell
dotnet test src\Dorc.Api.WindowsWorker.Tests\Dorc.Api.WindowsWorker.Tests.csproj `
  --filter "FullyQualifiedName~DaemonsControllerTests"
dotnet test src\Dorc.Api.Tests\Dorc.Api.Tests.csproj `
  --filter "FullyQualifiedName~WorkerDaemonOperationsTests"
```
