---
name: SPEC-S-007 — Remove Negotiate / WinAuth scheme from primary API
description: JIT Specification for S-007 — deletes WinAuthClaimsPrincipalReader, WinAuthLoggingMiddleware, ClaimsTransformer; simplifies Program.cs to OAuth-only; collapses ClaimsPrincipalReaderFactory to its one remaining branch. Throws InvalidOperationException at startup if AuthenticationScheme is set to WinAuth or Both, with a clear migration message.
type: spec
status: APPROVED
---

# SPEC-S-007 — Remove Negotiate / WinAuth scheme from primary API

| Field       | Value                                              |
|-------------|----------------------------------------------------|
| **Status**  | APPROVED (auto-pilot per user direction)           |
| **Step**    | S-007                                              |
| **Author**  | Agent                                              |
| **Date**    | 2026-05-28                                         |
| **IS**      | [IS-api-split.md](IS-api-split.md) (APPROVED)      |
| **HLPS**    | [HLPS-api-split.md](HLPS-api-split.md) (APPROVED)  |

---

## 1. Context

HLPS Scope E. With S-001's Graph-backed claims path in place, the Negotiate / WinAuth code is dead weight on the primary and the only remaining Windows-only authentication path. Removal also serves HLPS SC-1 (no Windows-only auth dependencies in the primary's compile graph).

## 2. Production code change

### 2.1 Deletions
- `src/Dorc.Api/Services/ClaimsTransformer.cs` — the `[SupportedOSPlatform("windows")]` class that expanded AD-derived role claims on Negotiate requests. Dead with Negotiate gone.
- `src/Dorc.Api/Security/WinAuthClaimsPrincipalReader.cs` — Negotiate-derived `IClaimsPrincipalReader`.
- `src/Dorc.Api/Security/WinAuthLoggingMiddleware.cs` — request-logging middleware that depended on Windows identity.

### 2.2 `Dorc.Api/Security/ClaimsPrincipalReaderFactory.cs`
The class is no longer a factory — it just delegates everything to `OAuthClaimsPrincipalReader`. The `IUserGroupReader`-based `GetUserId` / `GetSidsForUser` AD-SID paths (gated on `IsUseAdSidsForAccessControl`) are kept because S-001's `CachedUserGroupReader` / `AzureEntraSearcher` still implement those correctly post-Graph migration. The class name is not changed in this PR (CLAUDE.md C-2: naming changes belong in their own scoped PRs).

### 2.3 `Dorc.Api/Program.cs`
- Drop `using Microsoft.AspNetCore.Authentication.Negotiate`.
- Replace the `switch (authenticationScheme)` block with a guard: if `WinAuth` or `Both` is configured, throw `InvalidOperationException` at startup with a clear migration message pointing at `docs/api-split/`.
- Remove `ConfigureWinAuth` and `ConfigureBoth` static helpers.
- Remove `app.UseMiddleware<WinAuthLoggingMiddleware>()`.
- Simplify `MapControllers()` to always `RequireAuthorization(apiScopeAuthorizationPolicy)` (was conditional on OAuth-only mode).

### 2.4 Config defaults
`appsettings.json` already has `"AuthenticationScheme": "OAuth+WinAuth"` (the legacy `Both` value). This value will trip the new guard at startup. Fixing the default to `"OAuth"` belongs to the deployment / installer step (S-008) so customer config gets updated as part of the upgrade — not in this PR.

## 3. Test plan

Existing tests: all currently-green tests continue to pass (Negotiate-specific tests would already have been broken when the Graph migration landed — none were present per S-001's test run).

No new tests added. The S-007 change is mechanical deletion. The startup-guard exception is exercised by deployment validation, not unit tests — adding a startup-guard unit test would require booting the full host with a hostile config, which is integration territory (deferred to S-010's smoke test).

## 4. Verification

- `Dorc.Api`, `Dorc.Core`, `Dorc.PersistentData`, `Dorc.Api.Tests` build clean.
- `Dorc.Api.Tests`: 190 / 190 pass (no regressions).
- `git grep "WinAuth\|Negotiate" src/Dorc.Api` returns hits only in the new startup-guard message (string literal) and in `appsettings.json` (legacy default value to be fixed by S-008).
- The Linux build CI gate from S-001 still passes; removing Negotiate further reduces the primary's compile graph's Windows-API surface.

## 5. Out of scope

- Renaming `ClaimsPrincipalReaderFactory` (CLAUDE.md C-2 — naming pass is separate).
- Changing the `appsettings.json` default `AuthenticationScheme` (S-008's installer wires the new default for fresh installs; existing installs trip the startup guard with a migration message).
- The `ConfigAuthScheme` constants (`WinAuth`, `Both`) — left in place for the startup guard's `switch` to recognise. They become dead after every customer has migrated.
- Documentation of the migration (S-010).
