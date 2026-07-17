# SPEC S-004 — API: RefData controllers for environment components

| Field      | Value                                |
|------------|--------------------------------------|
| **Status** | APPROVED (executed under auto-pilot) |
| **IS step**| S-004 (IS v2, APPROVED)              |
| **Date**   | 2026-07-17                           |

DTOs were delivered in S-003 (recorded boundary adjustment). Complete reads satisfy the
"paged/complete" requirement (SPEC-S-003 adjustment #2 — no paging consumer in scope).

## Endpoints per controller (`RefDataContainersController`, `RefDataCloudResourcesController`, `RefDataApiRegistrationsController`)

| Route | Method | Auth | Outcomes |
|-------|--------|------|----------|
| `/` | GET | authenticated | 200 list |
| `ById/{id}` | GET | authenticated | 200 / 404 |
| `ByEnvId/{envId}` | GET | authenticated | 200 list (S-007 tabs consume this) |
| `/` | POST | PowerUser or Admin | 403 / 409 duplicate name / 200 + Create audit |
| `{id}` | PUT | env-write on every mapped env; PowerUser/Admin fallback when unattached | 403 / 404 / 409 name-conflict / 200 + Update audit (before/after JSON) |
| `{id}` | DELETE | same as PUT | 403 / 404 / 200 + Delete audit (before JSON) |
| `{id}/environments/{envId}` | PUT (attach) | env-write on the **target** environment | 404 item/env; 409 already attached; 200 + Attach audit |
| `{id}/environments/{envId}` | DELETE (detach) | env-write on the target environment | 404 item/env; 409 not attached; 200 + Detach audit |

Notes:
- Authorization failures return **403 with a reason string** (Daemons precedent); no
  200-with-false anywhere.
- Attach/detach map `EnvironmentAttachmentOutcome` → HTTP; the composite-PK race escape
  (`DbUpdateException`) maps to 409 as well, closing SC-4's "clean 4xx, never 500".
- Target-environment resolution via `IEnvironmentsPersistentSource.GetEnvironment(int,
  ClaimsPrincipal)`; a null result is 404 before any source call.
- Execution/review is pattern-first: Containers controller + tests set the pattern;
  CloudResources and ApiRegistrations replicate; the gate reviews the pattern deeply and
  the replicas as parity-diffs.

## Tests (`Dorc.Api.Tests/Controllers` or repo-conventional location)
Per controller: each write endpoint × (privileged, unprivileged→403); unattached-item
fallback both ways; duplicate name → 409; attach duplicate → 409; detach not-attached →
409; DbUpdateException from attach → 409; audit insert verified per action (incl. Attach/
Detach rows). Reads: ById 404, ByEnvId delegates to source.

## Gate
Diff-only review; tests green; suites at baseline; Swashbuckle annotations complete for
S-006 client generation.
