---
status: LIVING
issue: sefe/dorc#423
owner: API split maintainers
codebase_anchor: 2b616740
last_verified: 2026-08-27
---

# Windows worker parity matrix

This matrix records the pre-split observable contract for each operation moved to
`Dorc.Api.WindowsWorker`. A row is complete only when its owning PR includes a
checked-in fixture and a test that fails if the public response or error behavior changes.

| Step | Behavior | Pre-split source | Fixture | Evidence | Status |
|---|---|---|---|---|---|
| S-004 | Remote registry operating-system lookup preserves `ProductName`, `CurrentVersion`, and missing-key HTTP 400 | `Dorc.Api/Controllers/RefDataServersController.cs` at `2b616740` | `Dorc.Api.WindowsWorker.Tests/Fixtures/registry-operating-system.json` | `RemoteServerControllerTests` | Complete |
| S-005 | Daemon probe/state-change preserves daemon identity fields, status strings, and response shape | `Dorc.Core/DaemonStatusProbe.cs` at `2b616740` | `Dorc.Api.WindowsWorker.Tests/Fixtures/daemon-probe.json` | `DaemonsControllerTests` | Complete |
| S-006 | SQL application-password reset parity | `Dorc.Api/Controllers/ResetAppPasswordController.cs` at `2b616740` | Added by S-006 | Added by S-006 | Pending S-006 |

## Maintenance

Any PR changing a moved worker endpoint, its DTO, or its primary client method must
update the owning fixture and this matrix in the same PR.
