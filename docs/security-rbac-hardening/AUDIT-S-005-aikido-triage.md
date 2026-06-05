# AUDIT-S-005: Aikido Findings Triage — CLI, PowerShell, and Wider Surface

| Field       | Value                                          |
|-------------|------------------------------------------------|
| **Status**  | DRAFT                                          |
| **Author**  | Agent                                          |
| **Date**    | 2026-06-05                                     |
| **HLPS**    | HLPS-security-rbac-hardening.md (DRAFT) — SF-3 |
| **IS step** | S-005 (resolves U-5)                           |
| **Source**  | Aikido export (43 findings) provided 2026-06-05|

---

## 1. Purpose

This is the deliverable of IS step **S-005** and the resolution of **U-1/U-5**'s SF-3 branch:
a categorised, in-repo record of every Aikido finding, with an **in-context exploitability
assessment** (not a restatement of the scanner) and a remediation recommendation. From this
record, S-006 fix steps are scoped.

**Headline:** issue #605 framed the problem as "CLI tools and PowerShell module." The actual
Aikido surface is broader, and the **two most genuinely exploitable findings are outside that
framing** — an unescaped LDAP filter in `Dorc.Core` and an AD-display-name `innerHTML` sink in
`dorc-web`. The bulk of the "critical" path-traversal hits, by contrast, are on
operator-supplied or internally-generated (same-trust) inputs and rate lower in real-world risk
than their scanner score suggests. The plan's scope and the S-006 ordering should reflect this.

This document does not include exploit detail beyond what is needed to scope and verify fixes
(HLPS C-06).

---

## 2. Triage summary

| # | Finding (Aikido rule) | Location(s) | Scanner | In-context verdict | Real risk |
|---|---|---|---|---|---|
| T-1 | LDAP query injection | `Dorc.Core/ActiveDirectorySearcher.cs:256` | critical (100) | **True positive** | **HIGH** |
| T-2 | XSS via `innerHTML` (AD search results) | `dorc-web/.../addUserOrGroupTemplateHelper.ts:10-15` | high (80) | **True positive** | **MED–HIGH** |
| T-3 | Dependency CVE — `System.Security.Cryptography.Xml` 8.0.2→8.0.3 (CVE-2026-26171, CVE-2026-33116) | `Dorc.PowerShell.csproj` | high (75) ×2 | **True positive** | **HIGH (easy)** |
| T-4 | Unpinned 3rd-party GitHub Action | `.github/workflows/release.yml:214` | high (70) | **True positive** | **MED (easy)** |
| T-5 | XSS via `innerHTML` (other web sinks) | `page-project-bundles.ts`, `page-project-components.ts`, `edit-database-permissions.ts`, `attach-server.ts`, `attach-database.ts`, `add-edit-database.ts`, `add-sql-port.ts`, `page-scripts-list.ts`, `page-deploy.ts` | high/medium | **True positive (pattern)** | **LOW–MED** (mostly enum/controlled data) |
| T-6 | Path traversal — script-group pipe files | `Dorc.Runner`, `Dorc.NetFramework.Runner`, `Dorc.TerraformRunner` `Pipes/ScriptGroupFileReader.cs`; `Dorc.Monitor/Pipes/ScriptGroupFileWriter.cs` | critical/medium | **True positive (defence-in-depth)** | **LOW** (internally-generated pipe name) |
| T-7 | Path traversal — Terraform code sources / copy | `TerraformProcessor.cs:125`, `AzureArtifactCodeSourceProvider.cs:226`, `SharedFolderCodeSourceProvider.cs:39,52` | critical | **True positive (defence-in-depth)** | **LOW–MED** (admin-configured source path; zip-slip-style copy) |
| T-8 | Path traversal — CLI file argument | `Tools.PropertyValueCreationCLI/Program.cs:94,113` | critical | **False positive / accepted** | **LOW** (operator-supplied path to a local admin tool) |
| T-9 | Dependency CVE — `Microsoft.Identity.Client` →4.81.0 (AIKIDO-2026-10042, sensitive info in logs) | `Dorc.Api`, `Dorc.Core`, `Dorc.PersistentData`, `Org.OpenAPITools`, `Tests.Acceptance` | low (25) ×5 | **True positive** | **LOW (easy)** |
| T-10 | Dependency CVE — `System.Text.Json` 8.0.0→8.0.4/8.0.5 (CVE-2024-43485, CVE-2024-30105) | `Tools.DeployCopyEnvBuildCLI.csproj` | low (38) ×2 | **True positive** | **LOW (easy)** |
| T-11 | Leaked secret (generic api key) | `OAUTH_400_ERROR_DIAGNOSIS.md:194` | high (80) | **False positive** | **LOW** (truncated example JWT) |
| T-12 | File inclusion via file read | `dorc-web/vite.config.js:80,81` | high (70/75) | **False positive / accepted** | **LOW** (build-time tooling, not runtime) |

---

## 3. Detail and rationale

### T-1 — LDAP query injection (TRUE POSITIVE, HIGH) — *outside #605 scope*
`GetSidsForUser` builds `ds.Filter = $"(&(objectClass=user)(sAMAccountName={name}))"` with the
account name interpolated directly into the LDAP filter, unescaped. This is a textbook LDAP
filter injection (CWE-90). Exploitability hinges on whether `samAccountName` can carry filter
metacharacters from a caller-influenced path; even constrained, injecting `*` or boolean
clauses can alter which account/SIDs are resolved — a security-relevant outcome since SIDs drive
authorization. This is the single highest-scored finding and the most clearly worth fixing.
**Recommendation:** escape the value with the standard LDAP filter-escaping routine, or bind it
via a parameterised search. Add a unit test asserting metacharacters are neutralised.

### T-2 — XSS via AD-display-name `innerHTML` (TRUE POSITIVE, MED–HIGH) — *outside #605 scope*
`renderSearchResults` sets `root.innerHTML` by string-concatenating `searchResult.DisplayName`
and `searchResult.FullLogonName` from directory search results. AD object display names are not
guaranteed HTML-safe; a crafted directory object renders into the combo-box dropdown as live
markup (stored/reflected XSS, CWE-79). **Recommendation:** render via `textContent` / Lit
templated nodes rather than `innerHTML` string building, or HTML-escape the interpolated fields.

### T-3 — `System.Security.Cryptography.Xml` CVE (TRUE POSITIVE, HIGH, easy)
Two HIGH CVEs fixed in 8.0.3; project pins 8.0.2. **Recommendation:** bump the package
reference in `Dorc.PowerShell.csproj` to ≥8.0.3. No code change expected.

### T-4 — Unpinned GitHub Action (TRUE POSITIVE, MED, easy)
`release.yml:214` references a third-party action by a mutable tag. Supply-chain exposure
(CWE-829). **Recommendation:** pin to a full commit SHA (and optionally add a comment with the
human-readable version). Audit the rest of both workflow files for the same pattern while there.

### T-5 — Other web `innerHTML` sinks (TRUE POSITIVE pattern, LOW–MED)
Several Lit grid/column renderers assign `root.innerHTML = \`...${value}...\``. Most interpolate
controlled/enum values (e.g. `page-project-bundles.ts:374` interpolates the bundle `Type` enum
string), so live exploitability is low, but the pattern is fragile and each site should be
assessed for whether any user/DB-controlled string can reach it. **Recommendation:** standardise
on safe rendering (`textContent` / templated nodes); fix any site where free-text or
directory/user data flows in (treat those as T-2-class). Good candidate to pair with the
frontend-test bootstrap (issue #595).

### T-6 / T-7 — Path traversal in file readers/writers and Terraform code sources (TRUE POSITIVE, defence-in-depth, LOW / LOW–MED)
The script-group pipe files (T-6) derive a path from `pipeName`, which is an internally-generated
identifier passed Monitor→Runner on the same host within the same trust domain — not an
anonymous-attacker input. The Terraform code-source providers (T-7) copy from
`scriptGroup.ScriptsLocation`, an **admin-configured** component path, and build destinations via
`Path.Combine(destDir, relativePath)` (zip-slip-style risk on copy). These are real CWE-22
patterns but the inputs are privileged/internal, so they are defence-in-depth rather than open
remote vulnerabilities. **Recommendation:** add path-containment validation — canonicalise with
`Path.GetFullPath` and assert the result stays under the intended root before opening/copying;
validate `pipeName` matches the expected GUID shape. Batch these as one hardening change.

### T-8 — CLI file-path argument (FALSE POSITIVE / ACCEPTED, LOW)
`Tools.PropertyValueCreationCLI` reads a file path supplied as a command-line argument by the
operator running the tool. An operator choosing which local file to read is expected behaviour,
not a traversal attack — the operator already holds the tool's privileges. (Line 94 is an
exception-message interpolation, not a file operation at all — a scanner mis-attribution.)
**Recommendation:** mark accepted-risk with justification; no code change. Optionally validate
the path exists / has the expected extension for UX.

### T-9 / T-10 — `Microsoft.Identity.Client` and `System.Text.Json` CVEs (TRUE POSITIVE, LOW, easy)
Transitive/direct dependency CVEs with available patched versions. **Recommendation:** bump
`Microsoft.Identity.Client` to ≥4.81.0 across the five projects and `System.Text.Json` to the
patched 8.x in `Tools.DeployCopyEnvBuildCLI`. Verify no breaking API changes; these are minor
bumps.

### T-11 — Leaked secret in OAuth doc (FALSE POSITIVE, LOW)
`OAUTH_400_ERROR_DIAGNOSIS.md:194` is a **truncated, illustrative** JWT
(`"eyJ0eXAiOiJKV1QiLCJhbGc..."`) in a sample response block — not a live credential.
**Recommendation:** no secret rotation needed; the alert is cleared for free by **S-002**, which
already relocates/removes this root-level document. Good intersection — fold the verification
into S-002.

### T-12 — vite.config file inclusion (FALSE POSITIVE / ACCEPTED, LOW)
Flagged file reads occur in the Vite build configuration (build-time, developer-controlled),
not in a runtime request path. **Recommendation:** mark accepted-risk; no change.

---

## 4. Recommended remediation grouping (feeds S-006)

| Sub-step | Scope | Findings | Effort | Priority |
|----------|-------|----------|--------|----------|
| S-006a | Dependency bumps (Xml, Identity.Client, Text.Json) | T-3, T-9, T-10 | XS | **First** (clears 9 alerts incl. 2 HIGH, no code change) |
| S-006b | Pin GitHub Actions to SHA + sweep both workflows | T-4 | XS | First |
| S-006c | LDAP filter escaping + test | T-1 | S | **High** (highest real risk) |
| S-006d | Safe rendering for directory/user data sinks; assess all `innerHTML` sites | T-2, T-5 | S–M | High (T-2), Med (T-5) |
| S-006e | Path-containment hardening for file readers/copiers + pipe-name validation | T-6, T-7 | M | Med (defence-in-depth) |
| S-006f | Accept-with-justification records | T-8, T-11, T-12 | XS | Doc only (T-11 closed by S-002) |

S-006a/S-006b are zero-code-risk quick wins that clear the largest count of open alerts and
should land first. S-006c is the highest-value code fix. S-006e is a single batched hardening
change rather than per-file edits.

---

## 5. Plan amendments this triage implies

1. **U-5 resolved** — finding list obtained and assessed (this document).
2. **SF-3 scope was too narrow.** The HLPS SF-3 description ("CLI tools and PowerShell module")
   undercounts the surface; the highest-risk findings (T-1 in `Dorc.Core`, T-2 in `dorc-web`)
   are elsewhere. SF-3 should be restated as "the Aikido SAST/SCA surface," and the IS S-006
   replaced by the S-006a–f grouping above.
3. **T-11 intersects S-002** — relocating/removing `OAUTH_400_ERROR_DIAGNOSIS.md` also clears
   the leaked-secret alert; note this in S-002's verification intent.
4. **No blocking unknowns remain for SF-3.** S-006a/b/c are ready to scope into JIT Specs once
   the plan is approved.
