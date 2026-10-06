# HLPS — Generated API Clients in CI

**Status:** IN REVIEW (executed; round 1 of adversarial review complete)

> Process note: this work was executed before the `CLAUDE.md` gate was
> applied — there is no IS, no JIT specs, and this HLPS was written after the
> fact. A four-reviewer adversarial panel has since run (round 1) and its
> accepted findings are folded into the decisions below; the missing planning
> artefacts are a known process debt, not a claim of compliance.
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
   - `withCredentials` on every request via a `pre` middleware. The
     hand-edited `runtime.ts` set this too, and it is what makes the browser
     send Negotiate/NTLM credentials cross-origin. Missing it silently broke
     *every* call on a Windows-authenticated deployment — the API's default
     scheme — since those requests carry no bearer token either. Found by the
     adversarial panel, not by the build or the tests, and now covered by
     `tests/services/dorc-api-configuration.test.ts`.
   Both parameters are property getters because the auth scheme and base URL
   only become known after the initial `/ApiConfig` fetch. Two behavioural
   deltas remain, both accepted:
   - A server-side 401 on a token the client believes valid (revocation,
     failed introspection, clock skew) no longer hard-redirects to sign-in:
     the generated runtime exposes no error hook, and `post` middleware runs
     only on success. **This is unresolved and escalated — see U-5.** Two
     attempts to close it were made and both withdrawn under review; the
     history is recorded there because the obvious fixes are wrong in
     non-obvious ways.
   - An expiring token is now sent rather than short-circuited, because
     `user.expired` is true during silent renewal and redirecting there would
     throw the user out mid-renewal. Sign-in is triggered only when there is
     no session at all, once per page, via `signIn()` so the current URL is
     preserved.
3. **The spec follows the C# API.** The five spec gaps above were fixed in
   `swagger.json`; the client-side hand patches they replaced were dropped in
   favour of regenerated code. The dead
   `IsEnvironmentOwnerOrDelegate` call was migrated to the real
   `IsEnvironmentOwner` endpoint.
4. **Generator versions are pinned per client** in each client directory's
   `openapitools.json`, and the npm scripts pass `--openapitools` explicitly
   so the pin in use is never ambient. All clients are pinned to the latest
   generator (7.24.0 at the time of writing; originally reconciled to the
   versions that had produced the committed code, then upgraded). The stray
   `src/apis/openapitools.json` was removed and the `src/dorc-web` default
   aligned to the same pin. The generator still emits a C# test-stub project
   (`Org.OpenAPITools.Test`) — vacuous placeholders that CI never runs, since
   test discovery matches `*Tests.csproj` not `*Test.csproj`. It is excluded
   via `.openapi-generator-ignore` and deleted. Deleting it alone does not
   work: an earlier attempt removed the files *and* the ignore entry, and the
   next generation wrote all ~190 back. The exclusion is what makes the
   removal hold.
5. **The TypeScript `azure-devops-build` client is removed.** Nothing in the
   web app ever imported it (build information reaches the web through the
   DOrc API), so the dead copy and its spec/scripts are deleted rather than
   kept regenerating. (An earlier iteration kept it as raw generator output;
   removal supersedes that.) `npm run format` now honours `.prettierignore`
   so formatting no longer touches `src/apis`.
6. **The C# Azure DevOps client (`src/Dorc.AzureDevOps`) is under the same
   gate.** It is the load-bearing generated client — `Dorc.Core`'s
   `AzureDevOpsServerWebClient` (used by the API and, via the
   TerraformRunner/`TerraformSourceConfigurator`, the Monitor) pulls build
   numbers (`BuildsApi`/`DefinitionsApi`) and artifact locations
   (`ArtifactsApi`) through it. The committed tree was a stratified hybrid
   (6.5.0 models/client, ~7.2–7.4 Api files, a hand-ported RestSharp 112
   `ApiClient`, hand-written AAD auth inside the generated tree, a
   dependabot-bumped csproj). It was regenerated as pure
   `csharp`/restsharp generator output (see decision 4 for the pin), with the DOrc-owned pieces moved to a new
   `Dorc.AzureDevOps.Client` project:
   - `Auth/*` — the AAD client-credentials token generation
     (`Configuration.AccessToken` is the generated client's supported seam,
     which consumers already used).
   - `AzureDevOpsListResponseConverter` — Azure DevOps list endpoints return
     a `{"count":n,"value":[...]}` envelope while the vendored spec declares
     bare arrays; the old build relied on a hand-patch inside the generated
     `ApiClient` to unwrap it. The converter now does this through
     `ApiClient.SerializerSettings` (injected via
     `AzureDevOpsApiClientFactory` and the Api classes'
     `(client, asyncClient, configuration)` constructors), covered by unit
     tests in `Dorc.Core.Tests`.
   Two things are excluded via `.openapi-generator-ignore`: the client csproj
   (dependabot owns its package versions) and the vacuous generated test
   project (see decision 4).
   The spec is authoritative at MicrosoftDocs/vsts-rest-api-specs
   (`specification/build/6.0/build.json`), adopted via a **scheduled refresh
   rather than a fetch during generation**. Generation reads only committed
   *specs*, so the same commit always produces the same clients and a document
   Microsoft republishes cannot turn an unrelated pull request red. This is
   not full hermeticity: `openapi-generator-cli` is an npm wrapper that
   downloads the ~30 MB generator jar from Maven Central at run time, with no
   checksum pinning and no caching, once per generating job — a residual
   availability and supply-chain dependency (U-4). The `ado-build-spec-refresh` workflow runs weekly and on
   demand: it runs `scripts/fetch-ado-build-spec.mjs` (which overwrites
   `build.json` only when the response validates as the Swagger 2.0 Build
   spec, and otherwise warns and leaves the committed copy alone),
   regenerates, and opens a pull request when anything changed. Upstream
   stays authoritative; every adoption of it is reviewable and revertible.
   (An earlier iteration fetched inside `api-gen`; it was moved out because
   it made every build depend on an external resource. Verified at adoption
   time: the committed copy was byte-identical to the official 6.0 spec.)
7. **The Azure DevOps pipeline (`pipelines/dorc-build.yml`) is unchanged.**
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
| U-1 | Is the **TypeScript** `azure-devops-build` client still needed? Nothing in the web app imports it. (The **C#** Azure DevOps client is in active use — build numbers and artifact locations — and is not in question.) | RESOLVED — removed (decision 5). |
| U-2 | Should `swagger.json` itself be generated from the C# build (e.g. Swashbuckle CLI) instead of hand-maintained? | OPEN — would close the remaining C#→spec drift gap; needs API bootstrapping work. |
| U-3 | Java availability on the ADO self-hosted agent (`TRADING-DOTNET-03`). | OPEN — blocks mirroring the gate into `pipelines/dorc-build.yml`. |
| U-4 | The generator jar is downloaded from Maven Central per generating job, unpinned by checksum and uncached. A Maven outage reddens unrelated PRs; a compromised artefact would author committed source. | OPEN — mitigations: cache `~/.openapi-generator-cli`, or vendor/pin the jar by hash. |
| U-5 | **No 401 recovery path. ESCALATED — needs a designed fix, not a patch.** The generated runtime exposes no error seam (`post` middleware runs only on success). Two attempts were made and both reverted after review: (a) redirecting when `user.expired` — wrong, because `expired` is true during normal silent renewal, so it evicts users mid-renewal; (b) calling `signIn()` when silent renewal rejects — worse, and reverted in round 3. Silent renew uses a hidden iframe, so Safari/Firefox third-party-cookie blocking returns `login_required` while the *same* session succeeds top-level: that produces an unbreakable redirect loop with period `expires_in - 60`s, floored at 1s. It also pre-empts oidc-client-ts's own `ErrorTimeout` retry (every 5s, indefinitely), cancelling recovery that already worked. A correct fix must use `events.addSilentRenewError` (which fires only after the library's retry is exhausted), discriminate on `err instanceof ErrorResponse && err.error === 'login_required' \| 'interaction_required'`, drop the duplicate `addAccessTokenExpiring` registration in `OAuthService` (the library registers its own, so `signinSilent()` currently runs twice per expiry), and carry a persisted attempt marker so a renew-fails-immediately-after-sign-in condition cannot loop. Out of scope for this branch. | OPEN — own PR. |
| U-7 | Nothing enforces that generated APIs are constructed with `dorcApiConfiguration`; `new FooApi()` compiles and silently loses both credentials and bearer token. All 175 sites are correct today. | OPEN — wants a lint rule. |
| U-6 | `.gitignore` inside the generated tree (`dist`, `typings`, `node_modules`, `wwwroot/*.js`) would hide drift in those paths from `git ls-files --others`. Dormant: the pinned generator emits none of them. | OPEN — monitor on generator upgrades. |
