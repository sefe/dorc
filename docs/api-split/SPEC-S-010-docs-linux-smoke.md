---
name: SPEC-S-010 - documentation and Linux container smoke
description: Publish deployment guidance and prove the primary API and unchanged clients build and start on Linux.
type: spec
status: READY FOR REVIEW
---

# SPEC-S-010 - documentation and Linux container smoke

## Deliverables

- `docs/api-split/README.md` documents topology, worker configuration, Linux
  deployment, Entra permissions, migration cohorts, and the Windows release boundary.
- `src/Dorc.Api/Dockerfile` publishes the primary API on Linux.
- `/health/live` provides an unauthenticated process-liveness contract.
- `linux-container-smoke.yml` builds `Dorc.Api.Client` and `dorc-web` unchanged,
  builds the Linux image, starts it with `WindowsWorker:Enabled=false`, and verifies
  liveness plus the generated public OpenAPI surface.

## Acceptance mapping

- **SC-1:** the primary API starts in a Linux container.
- **SC-5:** `Dorc.Api.Client` and `dorc-web` build without API-split changes, and the
  running container generates the expected public API document.
- **SC-6:** the smoke job complements the existing Linux compile gate and the
  fixture/contract tests owned by S-003 through S-006.

## Local verification

```bash
docker build -f src/Dorc.Api/Dockerfile -t dorc-api-linux-smoke .
```

Run the image with the same environment variables used by
`.github/workflows/linux-container-smoke.yml`, then request `/health/live` and
`/swagger/v1/swagger.json`.
