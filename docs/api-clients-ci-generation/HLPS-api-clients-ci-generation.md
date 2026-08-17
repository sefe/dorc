# HLPS — Generated API Clients in CI

**Status:** APPROVED (executed)
**Scope:** `src/dorc-web/src/apis/*`, `.github/workflows/release.yml`, `src/dorc-web/package.json`

## Problem

The repository carries two generated TypeScript API clients —
`src/apis/dorc-api` (from `swagger.json`) and `src/apis/azure-devops-build`
(from `build.json`) — but CI never ran the generator. Clients were
regenerated ad hoc on developer machines and sometimes hand-patched, so the
committed clients, the committed specs, and the C# API had all drifted apart:

- `swagger.json` was missing the `/DaemonStatus/discover/{envName}` endpoint,
  the `DiscoverDaemonsResult` schema, `ProjectApiModel.LeanIXUrl`,
  `ApiBoolResult.RequiresConfirmation`, and the `confirmed` query parameter on
  `DELETE /RefDataServers` — all of which exist in the C# API and were
  hand-patched into the client instead.
- The committed client still exposed integer enum members
  (`AccessControlType.NUMBER_0`) although the API serialises enums as strings
  (`JsonStringEnumConverter`), plus twelve orphaned per-endpoint Analytics API
  classes from before the endpoints were consolidated under one `Analytics`
  tag.
- The web UI called `/RefDataEnvironments/IsEnvironmentOwnerOrDelegate`, an
  endpoint that does not exist in the C# API (the call 404s at runtime).
- The `dorc-api-gen` npm script resolved a *different* generator version
  (6.6.0 from `src/dorc-web/openapitools.json`) than the one that produced
  the committed client (7.13.0), so even running the documented command
  produced a spurious mass diff.
- `npm run format` passed `--ignore-path .gitignore`, which *replaces* the
  default `.prettierignore` (which excludes `src/apis`), so formatting
  reformatted generated code and created further drift.

## Decisions

1. **CI always generates.** Both workflow jobs regenerate both clients
   (`npm run api-gen`) after `npm ci`; the build job then fails if
   `git status` shows any difference under `src/dorc-web/src/apis`. The
   committed clients are therefore guaranteed to be exactly what the pinned
   generator produces from the committed specs, and the release build
   consumes freshly generated code.
2. **No hand-maintained files inside the generated tree.** (Originally
   `runtime.ts`/`servers.ts` were kept as protected customisations via
   `.openapi-generator-ignore`; that was reworked in favour of the
   generator's supported extension points.) Every DOrc API class is now
   constructed with the shared `Configuration` instance from
   `src/services/dorc-api-configuration.ts`, which supplies:
   - `basePath` from `AppConfig` (what the `servers.ts` edit did), and
   - `accessToken` under the OAuth scheme (what the `runtime.ts` edit did) —
     the generated per-operation `Authorization` headers come from the spec's
     `oauth2` security scheme, and a missing/expired session redirects to
     `/signin.html` before the request instead of after a 401 round-trip.
   Both parameters are property getters because the auth scheme and base URL
   only become known after the initial `/ApiConfig` fetch. One behavioural
   delta: a server-side 401 for an *unexpired* token no longer hard-redirects
   to sign-in (the generated runtime has no error hook); silent renew plus
   the pre-request expiry check cover the expiry cases that redirect handled.
3. **The spec follows the C# API.** The five spec gaps above were fixed in
   `swagger.json`; the client-side hand patches they replaced were dropped in
   favour of regenerated code. The dead
   `IsEnvironmentOwnerOrDelegate` call was migrated to the real
   `IsEnvironmentOwner` endpoint.
4. **Generator versions are pinned per client** in each client directory's
   `openapitools.json` (`dorc-api` 7.13.0, `azure-devops-build` 6.0.1), and
   the npm scripts pass `--openapitools` explicitly so the pin in use is
   never ambient. The stray `src/apis/openapitools.json` (7.9.0) was removed
   and the `src/dorc-web` default aligned to 7.13.0.
5. **`azure-devops-build` is kept as raw generator output.** Nothing imports
   it today; its committed copy had only formatting drift (a prior
   `npm run format` had prettier-formatted it). It is reset to generator
   output so the drift gate covers it too, and `npm run format` now honours
   `.prettierignore` so formatting no longer touches `src/apis`.
6. **The Azure DevOps pipeline (`pipelines/dorc-build.yml`) is unchanged.**
   It runs on a self-hosted agent whose Java availability cannot be verified
   from this repo; the GitHub Actions workflow is the enforced path. Mirror
   the two steps there once the agent is known to have a JRE.

## Behavioural changes shipped with the reconciliation

- String-valued enum members replace `NUMBER_*` placeholders
  (`AccessControlType`, `UserAccountType`, `AccountGranularity`,
  `TerraformSourceType`) — call sites updated; requests now send the string
  values the API's `JsonStringEnumConverter` expects and returns.
- Generated per-operation OAuth `Authorization` headers (from the spec's
  `oauth2` security scheme) are now present in the client, alongside the
  existing global injection in the hand-maintained `runtime.ts`.
- `page-analytics.ts` uses the consolidated `AnalyticsApi`; the twelve
  orphaned per-endpoint Analytics classes were deleted.
- The environment-owner check now calls an endpoint that exists
  (`IsEnvironmentOwner`), fixing a silent 404.

## Success criteria

- `npm run api-gen` is a no-op on a clean checkout (verified — generation is
  idempotent).
- `npm run build` (eslint + tsc + vite) and `npm test` pass against the
  regenerated clients (verified: 122/122 tests).
- CI fails when a spec changes without regeneration, or generated files are
  edited by hand.

## Unknowns Register

| # | Unknown | Status |
|---|---------|--------|
| U-1 | Is `azure-devops-build` still needed at all? Nothing imports it. | OPEN — candidate for removal in a separate change. |
| U-2 | Should `swagger.json` itself be generated from the C# build (e.g. Swashbuckle CLI) instead of hand-maintained? | OPEN — would close the remaining C#→spec drift gap; needs API bootstrapping work. |
| U-3 | Java availability on the ADO self-hosted agent (`TRADING-DOTNET-03`). | OPEN — blocks mirroring the gate into `pipelines/dorc-build.yml`. |
