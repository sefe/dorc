---
name: SPEC-S-009 — Log-injection carve-outs for primary API controllers
description: JIT Specification for S-009 — adds a small sanitisation helper and reworks four log sites in the primary API so user-controlled values cannot inject newlines or break structured-logging templates. Closes HLPS SC-8b for everything except the ResetAppPasswordController log site (S-006 territory).
type: spec
status: APPROVED
---

# SPEC-S-009 — Log-injection carve-outs for primary API controllers

| Field       | Value                                              |
|-------------|----------------------------------------------------|
| **Status**  | APPROVED (auto-pilot per user direction)           |
| **Step**    | S-009                                              |
| **Author**  | Agent                                              |
| **Date**    | 2026-05-28                                         |
| **IS**      | [IS-api-split.md](IS-api-split.md) (APPROVED)      |
| **HLPS**    | [HLPS-api-split.md](HLPS-api-split.md) (APPROVED)  |

---

## 1. Context

The IS lists four controllers (`ResetAppPasswordController`, `BundledRequestsController`, `MakeLikeProdController`, and the former `Deployment/Requests.cs`) carrying log-injection findings from the PR #424 SAST. The PR #424 file paths and line numbers are stale; current `main` (anchor `f0fbf917`) has equivalent sites at:

- `src/Dorc.Api/Controllers/BundledRequestsController.cs` lines 51–53, 75–77, 176–178 — `_logger.LogError(ex, error)` where `error` is a string built via `$`-interpolation / concatenation around user input (`projectNames`, `bundleName`, `id`). The interpolated string is then used as the message template, which (a) breaks structured-logging caching, (b) lets user input introduce `{}` tokens that corrupt downstream args, and (c) allows newline/control characters to forge log lines.
- `src/Dorc.Api/Services/RequestService.cs` lines 29 and 52–53 — same antipattern: `_log.LogError($"...{userInput}...")`.
- `src/Dorc.Api/Controllers/MakeLikeProdController.cs` line 223 — already uses structured templates (`{TargetEnv}`, `{@Request}`), but the user-controlled `TargetEnv` value can still contain newlines that appear literally in formatted output. `{@Request}` JSON-serialises the whole DTO; out of scope here.
- `src/Dorc.Api/Controllers/ResetAppPasswordController.cs` line 82 — structured template, but values (`username`, `envName`, `envFilter`) are user-controlled. The IS routes the fix here through S-006 (move to worker); this SPEC therefore *does not* modify the ResetAppPasswordController log site, only flags it.

S-009 fixes the controllers that are not on the S-006 move path.

## 2. Production code change

### 2.1 `LogSanitizer` helper (new)
**Target**: `src/Dorc.Core/LogSanitizer.cs`.

Single static `Sanitize(string?)` method:
- Returns `null` for null/empty input.
- Removes ASCII control characters except space (`< 0x20`, plus `0x7F`) so `\r`, `\n`, `\t`, and ESC cannot forge new log lines or smuggle terminal escape sequences.
- Caps length at 512 chars (defensive — pathologically long values shouldn't ruin a log file).
- Cheap allocation: one `Span<char>` pass, no LINQ.

Lives in `Dorc.Core` so it's reachable from both `Dorc.Api` and (later) `Dorc.Api.WindowsWorker`.

### 2.2 `BundledRequestsController` rework
Replace `_logger.LogError(ex, error)` with structured-template calls:
- L51–53 → `_logger.LogError(ex, "Error locating bundled requests for projects {ProjectNames}", LogSanitizer.Sanitize(string.Join('|', projectNames)));`
- L75–77 → `_logger.LogError(ex, "Error locating requests for bundle {BundleName}", LogSanitizer.Sanitize(bundleName));`
- L112–113, L149–150 — fixed strings, swap to structured form for consistency but no user value to sanitise.
- L176–178 → `_logger.LogError(ex, "Error deleting bundled request {Id}", id);` — `id` is int, no sanitiser needed.

The `error` local that's returned to the caller via `BadRequest(error)` keeps its current shape (separate concern: response-body content).

### 2.3 `RequestService` rework
- L29 → `_log.LogError("Wrong build type for request {RequestProject} {BuildUrl}", LogSanitizer.Sanitize(request?.Project), LogSanitizer.Sanitize(request?.BuildUrl));`
- L41 → `build.ValidationResult` may contain user-controlled content; wrap in sanitiser.
- L52–53 → `_log.LogError("Unable to locate a project with the name {ProjectName}", LogSanitizer.Sanitize(projectName));`

The `Exception` thrown still uses the original `$`-interpolated message — exception messages are not log injection (they're caught and serialised on their own path).

### 2.4 `MakeLikeProdController` L223
Wrap `mlpRequest?.TargetEnv` in `LogSanitizer.Sanitize(...)`. `{@Request}` is left as-is (out of scope; its concern is data exposure, not log injection).

### 2.5 `ResetAppPasswordController` L82
**Not modified by this SPEC.** S-006 moves this controller's logic to the worker; the sanitisation will be applied there per the same pattern. This SPEC adds a one-line comment at the call site referencing the deferral so the SAST scan post-S-009 has a documented exception.

## 3. Test plan
- `src/Dorc.Core.Tests/LogSanitizerTests.cs` — new test file covering:
  - Null and empty inputs return themselves
  - `\r`, `\n`, `\t`, `\0`, `\x1B` are stripped
  - Plain ASCII survives unchanged
  - Length cap at 512 chars
  - Multi-line input collapsed without producing fake log lines

No new tests for the controllers themselves — the existing test coverage exercises the success paths; the log statements run on exception paths only and are mechanical changes.

## 4. Verification
- `dotnet build` succeeds across the AD-relevant projects.
- All existing tests pass; new `LogSanitizerTests` pass.
- `git grep -nE '_log(ger)?\\.Log[A-Za-z]+\\(\\$\"' src/Dorc.Api src/Dorc.Core` returns no hits inside the four target files.
- PR's SAST re-scan returns no log-injection findings on `BundledRequestsController`, `MakeLikeProdController`, or `RequestService`. `ResetAppPasswordController` finding (if still flagged) is documented as deferred to S-006.

## 5. Out of scope
- ResetAppPasswordController log site (S-006 handles it as part of the worker move).
- Response-body content from `BadRequest(error + " - " + ex)` — separate concern (information disclosure via API error responses).
- `{@Request}` DTO serialisation in MakeLikeProdController L223 — separate concern.
- Any controller not named in §1.
