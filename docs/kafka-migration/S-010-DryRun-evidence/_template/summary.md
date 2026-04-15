# Dry-run summary — `<timestamp>`

| Field | Value |
|---|---|
| **Date** | `<UTC>` |
| **Operator** | `<role / name>` |
| **Staging environment** | `<which Aiven cluster + which DOrc env>` |
| **Cutover artefact** | `feat/kafka-migration` HEAD `<sha>` |
| **Rollback target** | `release/pre-kafka-cutover` @ `481f4830` |
| **Verdict** | `Pass` \| `Pass-with-runbook-revision` \| `Fail-rerun-required` |

## Wall-clock vs budgets

| Section | Budget | Actual | Notes |
|---|---|---|---|
| §3 GATE A → verified | 240 min | `<x min>` | |
| §5 rollback decision → complete | 60 min | `<y min>` | |
| Post-rollback §2 smoke | n/a | `<z min>` | every ST PASS? |

## Runbook revisions made during dry-run

| Section | Revision | Reason | Disposition |
|---|---|---|---|
| | | | |

## Trigger fires (if any)

| Trigger (§4 class.id) | Wall-clock | Authority decision |
|---|---|---|
| | | |

## Sign-off

- Tech lead: `<role / name>` — `<date>`
- Release engineer: `<role / name>` — `<date>`
- On-call SRE: `<role / name>` — `<date>`
