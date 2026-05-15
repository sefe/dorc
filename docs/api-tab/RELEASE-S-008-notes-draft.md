# Release notes — Environment APIs tab

## What changed

The placeholder **APIs** sub-tab under an environment's **Components** tab is now functional. Users can record the HTTP endpoints an environment exposes (name, type, auth type, owner project, tags, description, health-check path). Endpoints support `$VarName$` placeholders that are resolved against the environment's own scoped variables — both the raw and the resolved form are shown side-by-side.

## Why

Endpoints often differ between environments by host / port / path-segment values that are already held as environment-scoped variables. The new tab gives operators one place to find and verify those endpoints without consulting wikis or tribal knowledge, and reuses the existing variable-substitution machinery (`Dorc.Core.VariableResolution.PropertyParser`) so the data stays in sync with the source-of-truth property values.

## Migration / DBA action

A new `deploy.Api` table is added with `FK_Api_Environment` (CASCADE), `FK_Api_Project` (SET NULL), CHECK constraints on `Type` and `AuthType`, and a unique constraint on `(EnvId, Name)`. Apply via the existing SQL project deployment — no data backfill is required.

## Permissions

Existing environment-level `UserEditable` flag governs access. No new privileges are introduced.

## Out of scope (follow-ups)

- Live health-check probing of resolved endpoints (would require outbound HTTP from the API host and a security review).
- Sharing one API across many environments (attach/detach pattern).
- Variable-name autocomplete in the endpoint input.
- Secure-flagged endpoints for credentials in the path.

See `docs/api-tab/HLPS-api-tab.md` and `docs/api-tab/IS-api-tab.md` for the full design and step-by-step implementation record.
