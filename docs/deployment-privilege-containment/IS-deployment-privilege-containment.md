# IS: Deployment Privilege Containment — Implementation Sequence

| Field       | Value                                                        |
|-------------|--------------------------------------------------------------|
| **Status**  | DELIVERED — every step S-000..S-023 built or handed to an operator |
| **Author**  | Agent                                                        |
| **Date**    | 2026-08-10                                                   |
| **HLPS**    | HLPS-deployment-privilege-containment.md (APPROVED)          |
| **Folder**  | docs/deployment-privilege-containment/                       |

---

## Sequencing Principles

Four rules determine the order below. They are stated up front because several are counter-intuitive and an implementer who reorders steps without them will produce either a regression or a false sense of containment.

1. **Attacker capability, ascending.** The HLPS ranks weaknesses by what an attacker must already hold. Steps closing login-level holes come before steps closing local-host holes, regardless of which is easier. The cheapest steps happen to also be the highest-ranked, so this costs nothing.
2. **Rotation is gated on two steps, not one.** Rotating the deployment credentials before *both* S-004 and S-014 have landed re-discloses the fresh credential immediately — S-014 removes it from the property bag, S-004 removes the path by which arbitrary code reads it directly from the Monitor process. Either alone is insufficient.
3. **Confine before enforcing.** Every validation step lands at the write path first, then remediates existing data, then enforces at dispatch. Enforcing first is a flag day and breaches C-02.
4. **Disclosure channels close together.** Closing the property-bag channel while a log channel stays open produces a step that looks complete and is not.

**Operational steps** (marked ⚙) change data or credentials rather than code. They are sequenced here because their ordering relative to code steps is load-bearing, but they are executed by an operator, not delivered by a build.

---

## Step Index

| ID | Title | Addresses | Depends On |
|----|-------|-----------|------------|
| ⚙ S-000 | Interim exposure containment via `IsForProd` | SD-0, W-4 | **APPLIED in production 2026-08-10** (Ids 9, 13, 16). Verification outstanding; ST not yet changed. |
| S-001 | Implement the Terraform approval predicates | SD-10, W-10, SC-05a | **DONE** |
| S-002 | Authorize the daemon status endpoint | W-15, SC-05a | **DONE** |
| S-003 | Enforce the API scope policy in combined auth mode | W-17 | **DONE** |
| S-004 | Contain expression evaluation by provenance | SD-1a, W-1, SC-01, SC-02 | **DONE** |
| S-005 | Confine the build drop location | W-14, SC-05 | **DONE** |
| S-006 | Remove resolved property values from runner logging | SD-6, W-7, SC-06 | **DONE** |
| S-007 | Expire execution artefacts | SD-7, W-8, W-12, SC-08, SC-08a | **DONE** (W-8a deferred to S-021, closed there) |
| S-008 | Replace the null process security descriptor | SD-2, W-2, SC-04 | **DONE** — see the W-2 correction |
| S-009 | Authenticate the script-group transport | SD-2, W-3, SC-04 | **DONE** — must ship in lockstep, see the step |
| S-010 | Validate script paths at the write path | SD-5, W-5 | **DONE** (W-5a recorded, deferred to S-011) |
| S-011 | Validate source URLs at the write path | SD-9, W-11, W-16, W-5a | **DONE** — unenforced until configured, see the step |
| S-012 | Bind source credentials to validated hosts | SD-9, W-11, W-16, SC-05b | **DONE** — partly inert until S-011 is configured |
| ⚙ S-013 | Migrate the three `DORC_NonProdDeployPassword` consumers | SD-3a precondition | **Runbook delivered**; operator-executed |
| S-014 | Classify config values: reserved-key denylist | SD-3a, W-4, SC-03 | **DONE for the three zero-consumer keys**; the rest still gated on S-013 |
| S-014a | Restrict `CanReadSecrets` to service principals | W-19 | **DONE** — has a blast radius, see the step |
| ⚙ S-015 | Rotate the deployment credentials | SD-3, W-4 | **Runbook delivered**; gate satisfied, operator-executed |
| ⚙ S-016 | Remediate off-share script paths | SD-5 precondition | **Worklist queries delivered** (`S-016-off-share-script-paths.sql`); operator-executed |
| S-017 | Confine script paths at dispatch | SD-5, W-5, SC-05 | **DONE** — reports by default, enforce after S-016 |
| S-018 | Verify script content at the point of read | SD-5, W-5, SC-05 | **DONE** — U-14 resolved in `SPEC-S-018-*`; **b) is a lockstep release** |
| S-019 | Introduce config-value visibility classification | SD-3b, W-4 | **DONE** — server and web UI |
| S-020 | Introduce the credential provider abstraction | SD-4 | **DONE** |
| S-021 | Bind execution identity to the environment | SD-4, W-6, W-15, SC-07, W-8a | **DONE** — opt-in per environment; W-8a closed here |
| S-022 | Record attribution reached and not reached | SD-8, W-9 | **DONE** — `S-022-attribution-reached-and-not-reached.md` |
| S-023 | Replace the expression compiler with a fixed grammar | SD-1b, W-1 | **DONE** |

**Independently shippable today, in any order:** S-001, S-002, S-003, S-004, S-005, S-006, S-007, S-008, S-009, S-010, S-011. Eleven steps, covering every rank-1 and rank-2 weakness and the write-path half of every rank-3 weakness. Nothing in that set waits on an unknown.

---

## Phase 0 — Interim containment

### ⚙ S-000 — Interim exposure containment via `IsForProd`

**What changes.** The production deployment password's production-scope flag is changed from unset to production-only. Data, not code, and reversible by reverting one row.

**Why it changes.** W-4, **confirmed live in production**. With the flag unset, the value is published into *every* deployment — in the production instance, all 1,083 non-production environments as well as the 202 production ones. Setting it removes the production credential from the lower-trust population entirely, an immediate reduction of roughly 84% in exposed environments. It is **not a fix**: production deployments still receive the value, and S-014 remains the actual remedy. It exists because the plan otherwise offers nothing between "active, maximal disclosure" and a remediation that cannot begin until S-004 and S-014 both land.

**Dependencies.** **None remaining — this step is cleared to apply.** U-13 confirmed production carries the unset flag. U-12's completed scan of the production share found **zero consumers** of `DORC_ProdDeployPassword`, `DORC_WebDeployPassword` or `DorcApiAccessPassword` — the three keys this step targets — so there is no script to break and no migration to sequence first.

Apply the same change to `DORC_WebDeployPassword` and `DorcApiAccessPassword`, which carry the same unset flag. Leave `DORC_NonProdDeployPassword` unset for now — scoping it to non-production is correct in principle but changes behaviour for production deployments, which is S-014's business rather than an interim step's.

**This is the one step in the sequence with a live incident behind it.** Three of the estate's seven secure keys are already split into production and non-production pairs, so this is a correction toward an existing convention rather than a novel restriction.

**Verification intent.** Non-production deployments continue to succeed after the change — this is the regression to watch, and the reason for the second precondition. The value no longer appears in a non-production deployment's resolved property set. Production deployments are unaffected. The change is recorded so that S-015's rotation is not confused with it.

---

## Phase A — Weaknesses reachable from a DOrc login

### S-001 — Implement the Terraform approval predicates

**What changes.** The three authorization predicates in the Terraform controller — view, confirm, decline — are implemented against the deployment result's environment, replacing bodies that unconditionally return true. The controller already injects the privileges checker and the claims reader and uses neither for these decisions.

Two policy decisions must be made explicitly rather than defaulted:
- Whether approving an apply requires the same privilege as modifying the environment, or a distinct approver privilege. Segregation of duties argues for the latter, and it additionally prevents a requester approving their own plan.
- Whether plan *content* requires the secret-reading privilege on the environment rather than mere view rights, since plan output embeds resolved variable values.

**Why it changes.** W-10, rank 1. Any authenticated user can currently approve a production apply and read any deployment's plan. This is the highest-severity finding in the HLPS and the cheapest to close.

**Dependencies.** None.

**Verification intent.** A user without rights on the target environment receives a forbidden response from all three operations. A user with rights succeeds. If a distinct approver privilege is adopted, a user holding only modify rights cannot confirm. A test asserts that none of the three predicates is a constant expression — this is the concrete form of SC-05a and guards against the same regression recurring.

### S-002 — Authorize the daemon status endpoint

**What changes.** The daemon status retrieval endpoint gains an authorization check against the environment identified in the request, matching the pattern used elsewhere for environment-scoped reads.

**Why it changes.** W-15, rank 1. The endpoint takes an environment identifier and drives a production-credential logon inside the API process, with no check beyond requiring authentication. This step closes the *reachability*; S-021 addresses the underlying fact that the API process resolves deployment credentials at all.

**Dependencies.** None.

**Verification intent.** A user without rights on the identified environment is refused before any credential resolution occurs — verified by asserting the probe is not invoked, not merely that the response is a failure. Users with rights are unaffected.

### S-003 — Enforce the API scope policy in combined auth mode

**What changes.** The composition root's conditional that applies the global scope authorization policy is corrected so that it also applies in the combined OAuth-plus-Windows authentication mode, not only in the OAuth-only mode. The current test is an exact string comparison that the combined mode's distinct value does not satisfy.

**Why it changes.** W-17. In combined mode the scope requirement is silently never enforced. Audience validation and the authenticated-principal requirement still hold, so this is a narrowing of an existing gap rather than an open door — but a policy that is registered and not applied is worse than one that was never written, because it reads as protection.

**Dependencies.** None.

**Verification intent.** A token for the API audience lacking the required scope is refused in combined mode as it already is in OAuth-only mode. Windows-authenticated principals continue to reach controllers unaffected. A test covers each configured authentication mode rather than only the default, since the defect is precisely that one mode was untested.

---

## Phase B — Weaknesses reachable by submitting a request

### S-004 — Contain expression evaluation by provenance

**What changes.** Property values are tagged with their provenance as they enter the variable resolver, and expression evaluation is performed only for values originating from administrator-curated sources — never for values supplied on the deployment request. Provenance must be explicit: every setup path currently writes into the same local property collection, so that collection cannot serve as a proxy for origin.

Two defects in the same component are corrected in this step: the evaluation result cache becomes per-request rather than a process-wide static, and the marker-offset assumption in expression parsing is fixed.

**Why it changes.** W-1, rank 2. A user who can submit a deployment request can currently execute arbitrary code inside the Monitor process, which holds both credential pairs. Provenance containment closes that without depending on the expression inventory, because it removes no capability from any existing curated value. The static cache is a separate leak: a value first computed during a non-production deployment is returned to a subsequent production one.

**Dependencies.** None. This is the containment half; S-023 removes the compiler entirely and may follow at any distance.

**Verification intent.** A request whose submitted property value contains an expression with an observable side effect completes without that effect occurring. All inventoried curated expressions continue to evaluate identically. The same expression text evaluated under two request contexts resolves independently. Note for the implementer: the config-value setup path invokes the resolver's read accessor as part of its own guard, so that call site is itself an evaluation trigger and must be accounted for.

### S-005 — Confine the build drop location

**What changes.** The file-share build validator is extended to require that the supplied build location resolves, after canonicalisation, beneath the project's configured artefact root. The check is applied at request submission and again before the drop folder is published into script scope. The bundled-request path that resubmits a stored build location is covered by the same validation.

**Why it changes.** W-14, rank 2. The build location is currently validated only for well-formedness and existence, is published to scripts as a variable, and DOrc's own shipped deployment script dot-sources code from it — so an arbitrary location is arbitrary code as the deployment account. The Azure DevOps build path already validates builds against the project's own definitions; this extends the same principle to the file-share path that lacks it.

**Dependencies.** None.

**Verification intent.** A request naming a build location outside the project's artefact root is rejected at submission with a clear message. A location inside it succeeds. A stored bundle carrying an out-of-root location is rejected on resubmission rather than inheriting trust from having been stored. Traversal sequences and rooted paths that escape the root are rejected.

---

## Phase C — Secret disclosure channels

### S-006 — Remove resolved property values from runner logging

**What changes.** Serialization of resolved property values is removed from every runner logging site — the file readers and the pipe clients in all three runner implementations, the Terraform command stdout capture, and the console write on variable-assignment failure. Where diagnostics require it, property *names* are logged and values are not.

**Why it changes.** W-7. This is the second disclosure channel for exactly the values S-014 exists to protect, and it is not closed by anything that fixes the property bag. Two of the sites are on the release pipe path, not debug-only, and the .NET Framework runner is the default selection for any script without an explicit PowerShell version. The criterion is an invariant over runner logging rather than a fixed list of sites, because the enumeration was believed complete at five and a sixth was found later.

**Dependencies.** None, but it **must land with or before S-014**, or that step closes one channel while leaving another open for the same values.

**Verification intent.** A deployment carrying a secure property produces no runner log containing its value, on either transport, in either build configuration. Diagnostic value is preserved — property names still appear where they previously aided troubleshooting.

### S-007 — Expire execution artefacts

**What changes.** Two artefact families gain a lifecycle. The serialized script-group bundle is deleted once consumed or when the runner process exits. The Terraform working directory, which contains the variables file carrying the full resolved property set, is deleted in a failure-safe construct so that it is removed on the failure path as well as on success — currently only the success path cleans up. That directory is additionally created under a restricted access control list rather than inheriting permissions.

The bundle directory's privileged-identity set is corrected to grant the deployment identity explicitly, rather than relying on the Monitor and deployment accounts coinciding. As in S-008 and S-009, that principal is derived from the credential resolution point rather than a constant, so S-021 becomes a drop-in.

**Why it changes.** W-8 and W-12. Bundles currently persist indefinitely. Terraform variables files survive every failed plan, and failures are the common case while a component is being developed. Deleting the working directory does not reach the plan blob uploaded to storage — that has its own retention and its own disclosure path through W-10, closed by S-001.

**Dependencies.** None.

**Deliberately excluded, found while implementing.** The *local* copies of the plan files under `%ProgramData%\dorc\terraform-plans` are a third artefact family with no lifecycle at all: the Monitor creates that directory with inherited permissions, the Runner writes the binary plan and the rendered plan content into it, and nothing ever removes either. The rendered content lists variable values, so this is the same disclosure as the variables file. It is excluded from S-007 for two reasons. Deleting the local copies means relying on the blob upload having succeeded, and the local file is the only remaining copy if it did not — a correctness risk in the plan/apply handshake that this step has no business taking. Restricting the directory needs the deployment identity on a directory shared across deployments, which is S-021's problem rather than a constant this step can supply. **Recorded as W-8a**; it belongs with S-021, and is noted here so that closing S-007 is not mistaken for closing the artefact question.

**Verification intent.** After a successful deployment and after a failed one, no bundle and no Terraform working directory remains. A newly created working directory carries the restricted access control list rather than inherited permissions. The deployment identity can read the bundle it is meant to consume — this is the regression the current identity set would produce if corrected carelessly.

**Which half ships where, corrected after implementation.** The bundle half reaches **Debug builds only**. The file transport is selected under `#if DEBUG` on both sides — the Monitor's registration and the `--useFile=true` argument it puts on the Runner's command line — so a Release build never writes a bundle. The Terraform working-directory half is unconditional and does reach production. This step therefore closes W-12 in production and W-8 in development, and the production exposure of the same payload remains **W-3**, closed by S-009. S-009 is consequently the higher-value step of the two and should not be sequenced behind anything.

**Failure posture of the reader grant.** The per-file grant to the deployment account only ever widens access; the confinement is the directory access control list, applied before it. It is therefore best-effort: a failure to resolve the account — a directory-service blip, an unjoined host — is logged and the deployment proceeds. Throwing would convert a transient directory-service fault into a failed production deployment, which is worse than the exposure being guarded against, and cannot make the bundle over-readable in any case.

---

## Phase D — Process and transport confinement

### S-008 — Replace the null process security descriptor

**What changes.** The security descriptor applied to runner process creation is given an explicit discretionary access control list admitting the Monitor service account and the local system account, replacing the present null list which grants full access to every local principal. The logon token that is currently leaked is disposed. The use of a cleartext logon type is reassessed, and the credential's residence in a non-zeroable managed string is recorded for S-020 to address at the storage layer.

**Why it changes.** W-2. Any local principal can currently open a runner process for full access — reading the decrypted property bag from its memory or injecting code to execute as the deployment account. This also bounds what S-009 can achieve: transport confidentiality is worth little when the consuming process object is world-writable.

**Dependencies.** None. Derive the principal from the credential resolution point.

**Corrected during implementation.** The NULL access list this step was written to replace never reached `CreateProcessAsUser`: the interop modified a managed copy of the descriptor while the pointer that was passed addressed a separate unmanaged buffer with no access list *present*, which makes Windows apply the token's default instead. See the correction under W-2. The step still lands, and matters more for it — the defect was one routine tidy-up away from becoming the exposure it was mistaken for. The hand-rolled descriptor interop is deleted rather than repaired, and the descriptor is built in managed code from the logon token's own user, so it needs no directory lookup and cannot fail a deployment over a transient one.

Two leaks are closed alongside it: the `LogonUser` token, which was never closed because only its duplicate was returned and disposed, and the unmanaged descriptor buffer, which was allocated per script group and never freed. Both accrued for the life of the service.

The cleartext logon type is reassessed and retained. Deployment scripts reach target servers and script shares over the network as this account, which needs a logon that keeps the credential available for outbound authentication; the alternatives either drop it or require the interactive logon right on every Monitor host. The credential's residence in a non-zeroable managed string stays recorded against S-020.


**Verification intent.** A local principal that is neither the Monitor service account nor system cannot open a runner process for write access. The runner still starts, runs and exits normally under the deployment identity. No handle is leaked across a deployment cycle.

### S-009 — Authenticate the script-group transport

**What changes.** Three changes to the named-pipe transport. The server is created with an explicit access control list rather than the platform default. The client verifies the server's identity before accepting a script group, rather than accepting from whatever process owns the name. The server is confirmed ready before the runner process is started, closing the creation race.

If an unpredictable pipe name is adopted as defence in depth, it must be generated per server instance rather than per request: the name is currently composed once per request while the enclosing loop iterates per script group, so a per-request random name would collide with itself on a multi-version request.

**Why it changes.** W-3. Confidentiality and integrity both fail today, in opposite directions — the first local process to connect receives the cleartext bundle, and a process that wins the creation race can feed the runner an attacker-authored script group, controlling both the script location and path. Server-side access control alone addresses only the first; the client-side check is what closes the escalation.

**Dependencies.** None. Derive the principal from the credential resolution point.

**Verification intent.** A process other than the Monitor cannot read a script group in transit. A runner refuses a script group offered by a process that is not the expected server. A squatter holding the pipe name before the Monitor creates it does not receive the bundle and does not cause the runner to execute attacker-supplied content. Normal deployments are unaffected on both transports.

**Release constraint, established during implementation — this step must ship in lockstep.** The Runner cannot know which account the Monitor runs as, so it is told, on the command line: the Monitor emits `--serverSid=<its own SID>` and the Runner refuses any pipe not owned by it. Command-line arguments are parsed strictly, so **a Monitor carrying this change will fail every deployment against a Runner that predates it**, at argument parsing, before anything else. The Monitor and all three Runners are installed together, which is what makes this acceptable; an upgrade that replaces the Monitor while leaving a Runner path pointing at an older deployment will break. This is the concrete instance of the release-ordering risk S-018 owns.

**Two decisions worth recording.**

*An absent or malformed server identity is a refusal, not a pass.* A Runner that was never told whom to trust cannot authenticate anything, and defaulting to trust in exactly that case would leave the weakness open wherever the argument failed to arrive.

*The pipe's owner is set explicitly rather than left to default.* The owner of a kernel object comes from the creating token's default owner, which for an account belonging to Administrators is `BUILTIN\Administrators` rather than the account itself. Left at the default, the check would either fail for a Monitor running as an administrator, or — if relaxed to accept the group — admit anyone who can become a local administrator as a legitimate server.

**One trade accepted.** A squatter that claims the pipe name first now causes the Monitor's create to fail, and the deployment fails loudly rather than proceeding against an unknown counterparty. Any local user can therefore deny deployments on a Monitor host by squatting names. That is strictly better than the present behaviour, where the same act yields code execution as the deployment account, but it is a denial of service that did not previously exist and should be expected in operation.

---

## Phase E — Write-path validation

### S-010 — Validate script paths at the write path

**What changes.** Component validation is extended to reject a script path that, after canonicalisation, resolves outside the configured script root. Validation currently covers component names, identifiers, project ownership, duplicates and name lengths, and does not inspect the script path at all.

**Why it changes.** W-5, rank 3. The path-combining behaviour discards the script root entirely when the supplied path is rooted, so a user holding modify rights on a single project can point a component at any location and have it executed as the deployment account — bypassing the gated promotion pipeline that protects the share. This is the write half; S-017 enforces at dispatch once existing data is remediated.

**Dependencies.** None. Additive and non-breaking: it constrains new and updated components only.

**Verification intent.** A component update supplying a rooted path outside the script root is rejected with a clear message. A relative path within the root succeeds. Traversal sequences are rejected. Existing components are untouched — this step deliberately does not validate stored data.

**Implemented as a rule over the stored value, not the joined result.** A path that is relative and free of traversal cannot resolve outside whatever root it is later joined to, so the check is exactly equivalent to canonicalising against the root and comparing — and needs no knowledge of the root. That matters: the write path lives in `Dorc.PersistentData`, which has no reason to know where scripts live and no access to `Dorc.Core`'s existing path helper, since the dependency runs the other way. Both stored forms are covered, the plain path and the JSON document carrying one under `ScriptPath`; checking only the plain form would have left the JSON form as an open door to the identical escape.

**Scoped to PowerShell components, and this is load-bearing rather than incidental.** A Terraform component's `ScriptPath` is not a script relative to the root: for the `SharedFolder` source type it *is* the location, and is legitimately an absolute UNC path. Applying a relativity rule to it would reject every Terraform component on that source type. Confining it needs a host allow-list rather than a relativity rule — a different control, recorded as **W-5a**.

### S-011 — Validate source URLs at the write path

**Also picks up W-5a**, deferred out of S-010: a Terraform component's `ScriptPath` under the `SharedFolder` source type is an absolute UNC location rather than a path relative to the script root, and belongs against the same host allow-list as the two project URLs.

**What changes.** Project validation is extended to check both the Terraform repository URL and the artefacts URL against a host allow-list. Neither is inspected today: project validation runs five checks, none of which examines a URL's host, and the artefacts URL is accepted on a bare scheme-prefix test.

**Why it changes.** W-11 and W-16, rank 3. Both fields are settable at per-project modify rights and both become execution or credential-bearing inputs. The pattern to follow already exists in the codebase for the GitHub branch, with its rationale recorded in a comment; this extends it to the two branches that lack it.

**Dependencies.** None.

**Verification intent.** A project update naming a host outside the allow-list is rejected for either field. Allowed hosts succeed. The allow-list is configurable rather than compiled in, since it is deployment-specific.

**Two lists, not one.** `AllowedArtefactHosts` and `AllowedTerraformSourceHosts`, because permission to drop build artefacts somewhere is not permission to clone and execute infrastructure code from it. The Terraform list also governs W-5a's component-level shared folder, which is the same kind of source reached by another route.

**The control is inert until configured, and this is deliberate.** There is no safe built-in default for a host allow-list — every estate's hosts are its own — and an empty list cannot be distinguished from one an operator has not filled in yet. Enforcing against an unfilled list would reject every project and component edit in every deployment on the day this ships, which is precisely the flag day sequencing principle 3 exists to prevent. So an unconfigured list admits everything, and says so: `ISourceHostAllowList.IsUnconfigured` is reportable, and the write path logs a warning naming both settings each time it declines to check. Filling either list turns that list on; the other stays inert until filled.

This makes S-011 a two-part delivery: the code ships here, and the estate's hosts are entered by an operator afterwards. Until they are, W-11, W-16 and W-5a remain open — the step should not be read as closing them on merge.

**The host is parsed, never matched.** A substring test for `build.corp.example.com` is satisfied by `build.corp.example.com.attacker.net`, and by the same host placed in a path, a query, a fragment or userinfo. Only comparing the parsed authority avoids that. UNC paths are read directly rather than through `Uri`, which does not treat `\\host\share` as a URI on a non-Windows host — the check has to answer identically wherever it runs.

### S-012 — Bind source credentials to validated hosts

**What changes.** Source credentials are released only to hosts on the validated allow-list. Specifically: the Terraform personal access token is no longer supplied to an arbitrary clone target; the Entra bearer token attaches on a parsed-host comparison rather than a substring test against attacker-controlled text; and the build-server client's fallback that offers the API service account's Windows credentials to an arbitrary host is removed or constrained to allow-listed hosts.

**Why it changes.** W-11 and W-16. The substring test is satisfiable by any host containing the expected string, and the Windows-credential fallback is the leg no token-scoping fix would catch — it offers an interactive service identity rather than a scoped token.

**Dependencies.** S-011, whose allow-list this consumes.

**Verification intent.** A URL crafted to contain an allow-listed host as a substring of an attacker-controlled host attracts no credential. Legitimate hosts continue to receive the correct credential. No path offers default Windows credentials to a host that is not allow-listed.

**Three legs, and they do not all depend on the allow-list.** Two of the fixes are unconditional and take effect on merge; one inherits S-011's inertness.

*Unconditional.* Every host classification is now a comparison against the parsed authority instead of a substring test over the URL text. That covers `dev.azure.com` / `visualstudio.com` in the Monitor's source configurator, `github.com` / `dev.azure.com` in the Runner's git provider, and the configured Azure endpoint in the build-server client. A substring test is satisfied by an attacker's host that merely mentions the expected one — in a subdomain, a path, a query, a fragment, or userinfo — and all five shapes are covered by tests.

*Unconditional, and the one no allow-list would have caught.* The git credentials callback ignored the URL it was asked about. libgit2 invokes it for every URL it authenticates against during a clone, **redirect targets included**, so a repository that redirected elsewhere collected the Terraform PAT or the Entra token without the redirect ever appearing in project configuration. The callback now refuses any host other than the configured repository's.

*Inherits S-011's inertness.* Whether a repository may be offered credentials at all, and whether an endpoint may be offered default Windows credentials, are decided against the allow-list — so both remain permissive until the estate's hosts are entered, and both log when they decline to check.

**Default Windows credentials are constrained, not removed.** An on-premises Azure DevOps Server legitimately authenticates that way, so removing the branch would break those estates. It is now reachable only for a host on the artefact allow-list; where none is configured, it warns and proceeds as before. This is the leg the HLPS singles out as the one no token-scoping fix would catch, because it presents an interactive service identity rather than a scoped token — it is closed by configuration rather than by code, and should be tracked as such until the list is filled.

---

## Phase F — Configuration value classification and rotation

### ⚙ S-013 — Migrate the credential-consuming deployment scripts

**What changes.** The three deployment scripts confirmed to consume a deployment password as an injected variable are migrated to a supported credential-retrieval path. A fourth line in a recycling script matched the scan with its key truncated and must be classified before this step is considered complete.

The migration target is the provider abstraction introduced in S-020 if the sequence permits, or an interim supported mechanism if this step must precede it. That choice is the step's principal design decision and should be made when the step is specified, not now.

One of the three sits in the production script folder and consumes the *non-production* password. That is either intentional or a latent defect, and it belongs to the script's owner to determine — it is recorded here so the migration does not silently preserve it.

**Why it changes.** Precondition for S-014. The denylist has no off switch; without this migration it breaks these scripts on the first deployment after release.

**Dependencies.** **U-12** — the completed scans used the original four-key pattern, so consumption of the four additional secure keys the denylist now covers has never been checked. Re-run the scan with the widened key set before treating the migration set as final.

**Verification intent.** Each migrated script deploys successfully without reading any credential from the injected variable scope. A scan of both script folders with the widened key set returns only the migrated call sites.

### S-014 — Classify config values: reserved-key denylist

**What changes.** Configuration values whose keys are on a reserved list are excluded from the property set published into script scope. The list is split by key type on the evidence gathered:

- **Password and secret keys are denylisted unconditionally.** The list is derived by enumerating secure configuration values rather than fixed in code — seven distinct keys at the time of writing, a set that grew twice during planning as fuller listings arrived. Deriving it removes the failure mode where the code and the estate drift apart.
- **Username keys remain visible.** Multiple deployment scripts legitimately consume them to grant logon rights, and an account name is an identity rather than a credential — already visible in target-server access control lists, process listings and event logs. Denying them would break working deployments for negligible gain.

**Why it changes.** W-4. Every secure configuration value is currently decrypted and injected into every deployment matching a coarse production flag, with no exclusion for the credentials DOrc itself needs to operate.

**Dependencies.** S-006 (or same release), S-013, U-12.

**Verification intent.** No denylisted key appears in the serialized script group for any environment, asserted as an invariant over the classified set rather than a fixed list. Username keys remain present. The scripts migrated in S-013 continue to deploy. Coverage sits at script-group serialization and at variable assignment in the runner, not only at dispatch.

**Delivered against the evidence, which does not permit the step as originally written.** "Password and secret keys are denylisted unconditionally, the list derived by enumerating secure configuration values" would withhold `DeploymentServiceAccountPassword` — roughly eighty call sites, and the mechanism by which scripts reach target servers at all — along with `ProgetAccountPassword` and `DorcCliSecret`. That is not a denylist, it is an outage. U-12's scan is what makes the difference, and it reversed this half of the design.

So the withheld set is the three keys the scan showed have **zero** consumers: `DORC_ProdDeployPassword`, `DORC_WebDeployPassword`, `DorcApiAccessPassword`. Those are a drop-in, and they include the credential behind the live incident. The remaining secure keys stay published until S-013 migrates their consumers; the list is configurable so that migration extends it without a code change.

**The derivation survives as reporting rather than as enforcement.** Enumerating secure values and subtracting the withheld set gives the set still reaching script scope, which the Monitor logs on every deployment. That keeps S-013's backlog measured against the estate rather than against a list in code, and surfaces a secure value added after this was written — without that derivation being able to break a deployment.

**Two gates, as required.** Configuration values are excluded where they become properties, and the script group is filtered again as it is assembled. The second is not redundant: a property reaching the set by another route — an environment property, or a request-supplied value bearing the same name — would otherwise carry an operating credential across the process boundary into a Runner.

**Not closed by this step:** W-4 remains open for the four secure keys that are still published, and S-015's rotation still depends on S-013 completing first.

### S-014a — Restrict `CanReadSecrets` to service principals

**What changes.** Two parts.

*Enforcement.* The machine-principal determination that already exists in the OAuth claims reader — currently a private test of an `m2m` claim — is surfaced on the claims-reader interface so the authorization layer can consult it. `CanReadSecrets` then requires a service principal in addition to the environment privilege. A human principal fails the check regardless of what they have been granted or what they own.

*Grant.* The `| Owner` term is removed from the privilege's resolution, so ownership no longer implies it. After this, holding the privilege requires an explicit grant, and an audit of holders is meaningful.

Whether `AccessControlController`'s use of the same predicate for ACL visibility should move to a different predicate is a decision this step must make explicitly — it is a human-facing UI question that has nothing to do with runtime secret retrieval, so it should almost certainly not share the predicate.

**Why it changes.** W-19. The privilege exists for service accounts belonging to live running systems. As implemented it is granted implicitly to every environment owner and cannot be restricted to machines, so human owners currently receive decrypted secret property values through the API and the web UI — the exact outcome it was designed to prevent. The mechanism to fix it is already in the codebase, one layer away from the decision that needs it.

**Dependencies.** None. **The usage decision is made: humans get the redaction already implemented for non-holders** — the secure value is replaced with an empty string. No new privilege, no new UI affordance, no audit trail to build. Smallest change, and it reuses a path that already exists and is already exercised.

**But that path's edge cases become the mainline, and two of them need fixing as part of this step.** Environment owners currently take the *decrypt* branch; after this step they take the *redact* branch. Behaviour that today affects a small population becomes the default experience for every owner:

1. **The all-secure query throws instead of redacting.** When every value in a result set is secure, the redaction path raises a rights exception rather than returning an empty-valued list. Partial result sets redact; wholly-secure ones error. For an owner opening a property set that happens to contain only secure values, that surfaces as a failure rather than as masked data. Redaction should be uniform: return the list with values emptied, and let the caller present it.
2. **Redaction is skipped entirely when no environment is in scope.** The branch is conditioned on a non-empty environment name, so an unscoped query redacts nothing — and because the decrypt branch is also skipped, the caller receives the stored **ciphertext** rather than either the plaintext or a blank. That is not a plaintext disclosure, but it leaks value presence and encrypted material to a caller who is not entitled to the value at all. Unscoped queries should redact too.

Neither is introduced by this step; both are made prominent by it, which makes this the right moment to correct them.

**Blast radius, stated plainly.** Every environment owner who today sees decrypted secret property values in the web UI will stop seeing them. If anyone relies on that to operate — debugging a deployment, confirming a credential rotation landed — this breaks their workflow. That is the same shape of risk as S-013's script migration, and deserves the same treatment: find out who relies on it before shipping, not after.

**The predicate-separation decision, made.** `AccessControlController`'s second use of the predicate is not ACL visibility — it is the guard that stops a caller granting `ReadSecrets` when they do not hold it. Left on the restricted predicate, **no human could ever grant the privilege to anyone** and it would become unadministrable the moment this step shipped. So the two questions are now two predicates: `CanReadSecrets` for exercising it (service principal + explicit grant) and `CanGrantReadSecrets` for administering it (the original privilege-or-ownership test, unchanged). Administration behaves exactly as before. The `UserCanReadSecrets` flag on the access model keeps the restricted predicate, because it tells a client whether values will actually be readable, which is now false for people.

**One consequence worth stating before this is deployed.** Windows authentication carries no machine-versus-person discriminator, so `IsServicePrincipal` is false for every Windows-authenticated caller and the privilege cannot be exercised over that scheme at all. That is the intended shape — service accounts of live running systems authenticate as OAuth machine clients, which is where the `m2m` claim exists — but a consumer that reads secrets over Windows authentication would stop working. The Monitor is unaffected: it uses the direct-tool reader, which reports itself a machine.

**Verification intent.** A service principal with the privilege still retrieves decrypted values — the path that must not break. A human principal does not, whether they hold an explicit grant or own the environment; the owner case specifically confirms the implicit grant is closed. A result set consisting entirely of secure values returns redacted entries rather than raising an exception. An unscoped query redacts rather than returning ciphertext. Existing clients degrade to blank values rather than failing. ACL visibility is verified independently of the secret-retrieval path, per the predicate-separation decision above.

### ⚙ S-015 — Rotate the deployment credentials

**What changes.** Every secure configuration value that has been published into a runspace is rotated: the deployment passwords, the web deployment password, the API access password, and the CLI secret and deployment service account password within their respective populations.

**Why it changes.** W-4 establishes that these have been delivered in cleartext to deployment scripts. By the principle that any credential that has been in a runspace is disclosed, all of them require rotation — not only the two deployment passwords.

**Dependencies.** **S-004 and S-014, both.** S-014 removes the credentials from the property bag; S-004 removes the path by which arbitrary code reads them directly from the Monitor process. Rotating after only one re-discloses the fresh credential to the lowest-privileged user in the system. Note that rotating before S-020 migrates credential storage means rotating twice — expected, not a surprise.

**Verification intent.** Deployments succeed against every environment class after rotation. No component still holds the previous value. The rotation is recorded against S-000 if that interim step was applied, so the two are not confused.

---

## Phase G — Script integrity

### ⚙ S-016 — Remediate off-share script paths

**What changes.** The three script rows confirmed to execute from a file server outside the configured script root are relocated into the gated share or explicitly exempted by a documented decision. A fourth row that is rooted but resolves to the script root itself is rewritten as a relative path. The rows using the JSON path form are reviewed for the same defect, which pattern matching cannot detect reliably.

Two of the three point at what appears to be an individual's incident-workaround folder. Relocation is therefore also a process question for whoever owns those components, not solely a data fix.

**Why it changes.** Precondition for S-017. Enforcing at dispatch without this is a flag day for those deployments.

**Dependencies.** S-010, so that remediated rows cannot be immediately re-broken through the write path.

**Verification intent.** The inventory query returns no rows resolving outside the script root, with the comparison performed against the configured root after canonicalising both sides rather than by pattern. Each remediated component deploys successfully.

**Worklist delivered; the remediation itself is operator-executed.** `S-016-off-share-script-paths.sql` classifies every stored script path by the rule S-017 enforces, counts the population, lists the JSON-path rows separately for manual inspection, and orders the off-share set by how often it actually deploys so remediation can be sequenced by risk.

Everything in it is read-only except a remediation template that is commented out deliberately. There is no safe bulk update: each off-share script is either already on the share under another name (repoint), belongs on the share but was never promoted (**promote through the gated pipeline first, then repoint** — repointing before the file exists turns a security finding into a broken deployment), dead (disable the component), or deliberate with the root being wrong (a design decision to escalate, not a data fix).

### S-017 — Confine script paths at dispatch

**What changes.** Path resolution at dispatch rejects any script path that resolves outside the configured script root after canonicalisation, covering both the direct and JSON path forms.

**Why it changes.** W-5. The write-path check in S-010 constrains new data; stored rows predate it and rows can arrive by other means. Both ends are required — validating only at write leaves stored rows unchecked, and validating only at dispatch means the API accepts values it will later refuse.

**Dependencies.** S-010, S-016.

**Verification intent.** A component whose stored path resolves outside the root fails with a clear, attributable result rather than executing. All remediated components succeed. Comparison is against the configured root, not a pattern — the inventory query's pattern-based classification over-flags, and the enforcement must not inherit that.

**Delivered reporting-first, and enforcement is a configuration switch.** `AppSettings:EnforceScriptPathConfinement` is absent by default, which means report. In that mode a script path that would be refused is logged as a warning and the deployment proceeds; with enforcement on, it is refused. That ordering is sequencing principle 3 applied literally — the estate holds paths that were never validated, and enforcing on the day this ships would fail every deployment of an off-share component at once.

**The report is S-016's worklist.** It names the path, the script root and the reason, so the population to remediate is discoverable from the Monitor log without waiting for a scan. `S-016-off-share-script-paths.sql` finds the same population directly in the database, which is faster and does not require the components to be deployed first.

**The primary check is on the STORED path, not the joined result, and this is load-bearing.** `Path.Combine` is platform-dependent: given a rooted second argument it discards the first on Windows, and merely concatenates on other hosts. A check applied to the joined result therefore passes on a non-Windows build host and refuses in production, or the reverse — the first version of this step had exactly that defect and a test caught it. Checking the stored value uses the same rule the write path uses, so a validator and its enforcer cannot disagree, and it answers identically wherever it runs. The joined result is checked as a second gate, but nothing rests on it alone.

### S-018 — Verify script content at the point of read

**What changes.** A content hash is recorded for each script and carried to the runner, which verifies it against the bytes it is about to execute, immediately before execution.

**Why it changes.** W-5. Verifying at dispatch would be defeated by a race the design itself creates: dispatch happens in the Monitor, the executed bytes are read later in another process, and anyone with share write access — precisely the threat — can swap the file in the window. Restoring the platform's own authorization manager is worth doing as defence in depth but does not substitute, because scripts are executed as in-memory blocks that file signature checks do not reach.

**Dependencies.** S-017. **U-14** — nothing yet specifies who records the hash or how it is re-recorded when the gated pipeline promotes a new version. Scripts change on the share outside DOrc's knowledge, so a naive implementation breaks dispatch on every legitimate update. The adoption path needs a mode that tolerates an unrecorded hash, and that decision must be made before this step is specified.

**Release constraint.** **Lockstep, not additive.** The script group is deserialized by a serializer that ignores unknown members, so a runner that has not been upgraded would silently ignore an added hash and execute unverified — the criterion unmet with no signal. All three runners and both dispatchers ship together; they are packaged in a single installer, so this is achievable. The Monitor must also define its behaviour when it cannot confirm the runner performed verification.

**Verification intent.** A script whose content changed after its hash was recorded fails verification and does not execute, with the outcome recorded against the deployment result. An unmodified script executes normally. An unrecorded hash follows the adopted tolerance mode rather than failing arbitrarily.

**U-14 is resolved in `SPEC-S-018-script-content-verification.md`.** Four decisions, summarised here because they change what this step is.

**DOrc records the hash on first execution — trust on first use.** Not an operator by hand: there are hundreds of registered scripts and a control needing hundreds of manual acts is never adopted. Not the promotion pipeline as a precondition: that is the correct end state and the spec makes it reachable, but requiring pipeline work outside this repository first means the step delivers nothing until that work happens. What trust on first use is worth, stated plainly: it does not establish that a script was legitimate when first seen — if the share was already compromised, the compromised bytes become the baseline. What it detects is **change after recording**, which is the actual attack, and it converts a silent indefinite compromise into a refusal at the next deployment.

**Re-recording on promotion is an administrator-gated endpoint**, which a pipeline calls after publishing a new version. Clearing a hash means "re-record on next execution", not "stop verifying this one" — there is deliberately no per-script opt-out, because a per-script opt-out is a per-script hole.

**Three modes, defaulting to report**: `Off`, `Report`, `Enforce`, from `ScriptContentVerification` in AppSettings. Same shape as script path confinement and for the same reason — the population that would be refused on release day is unknown until something reports it. An unrecorded hash is **never** a refusal in any mode.

**The Monitor does not attempt to confirm that verification happened.** The IS asks for that behaviour to be defined, and this is the definition. The runner reports its outcome, the Monitor records what was reported, and records when nothing was. Anything stronger would be a false claim: the Monitor cannot distinguish "verified silently" from "old build" without a protocol version, and an old build ignores a protocol version too. The real guard against a stale runner is the lockstep release, not a runtime check.

**Delivered in two parts.** **S-018a** — schema column, entity, API model, the hash computation in the shared assembly, the mode setting, and the recording endpoint. Nothing reads the column, so it changes no behaviour and ships on its own. **S-018b** — the hash travels in the script group, both PowerShell runners verify before execution, first use records, outcome reported. That half carries the lockstep constraint.

**The baseline is captured by the Monitor at dispatch, not by the runner.** This was decided while implementing and is worth recording, because the obvious alternative is worse in exactly the scenario the step is about: a runner that recorded the bytes in front of it would record *the attacker's* bytes as the baseline and report a match. Capturing at dispatch and verifying at the point of read covers the dispatch-to-read window on the very first deployment too, rather than merely baselining it. Nothing in capture refuses — a share that cannot be read fails the deployment moments later, for its own reasons and with its own message.

**The verification mode travels on the script group.** The runner does not read it from its own configuration: that would let a deployment host with a stale or edited configuration file quietly execute unverified, and the deployment host is the least trustworthy place to keep the answer.

**Two residuals, recorded not closed.** Trust on first use does not authenticate the first read. And verification covers the entry script, not what it dot-sources — a verified script that dot-sources an unverified file from the same share executes unverified code, and no check placed at a single point of read closes that.

---

## Phase H — Execution identity

### S-019 — Introduce config-value visibility classification

**What changes.** Configuration values gain an additive visibility attribute controlling whether they may be resolved into script scope, with an administrative surface in the web application for maintaining it. Existing values default to visible; newly created secure values default to hidden.

**Why it changes.** W-4, generalising S-014's fixed list. The default-visible inversion is what makes this safe to ship without an estate-wide inventory: no existing deployment can break, and administrators tighten per key at their own pace.

**Dependencies.** S-014.

**Verification intent.** An existing value's behaviour is unchanged until an administrator changes its classification. A newly created secure value is hidden by default. A hidden value does not appear in the serialized script group. The administrative surface is reachable only by administrators.

**Delivered server side first; the web UI followed.** Schema, entity, API model, persistence and enforcement are complete, and the classification is maintainable through the existing `RefDataConfig` endpoints — which already gate every write on `IsAdmin`, so the "reachable only by administrators" criterion needed no new code. What is missing is the *convenient* surface: a column on the config values page and a control on the create form.

That half needed the generated TypeScript API client regenerated from the OpenAPI document — `dorc-web/src/apis/dorc-api/models/ConfigValueApiModel.ts` carries a "do not edit manually" banner, and hand-editing generated output is how a client and a contract drift apart. It was regenerated rather than hand-edited, and only the one changed model taken from the output: the checked-in `swagger.json` is behind the checked-in client in other places, so regenerating the whole client from it would have silently *removed* properties the UI depends on. That drift is pre-existing and is left recorded rather than repaired here — repairing it means re-exporting the OpenAPI document from a running API, which is a separate piece of work with its own review.

**The UI half.** A "Visible To Scripts" column on the config values page and a control on the create form, both administrator-gated like the existing classifications. Two decisions worth stating.

The checkbox is readable on every row but changeable only on secure ones. A non-secure value is ordinary configuration, visible to scripts by definition, and the server forces the flag true when one is created — an editable control there would suggest a restriction that does not exist.

The create form defaults it **off**, matching the server's asymmetry: a new secure value is hidden from script scope unless its creator says otherwise, while existing values default the other way in the column default. Defaulting the form the other way would make every newly-added credential readable by every deployment script the moment it was created.

**The reserved-key list is not duplicated into the UI.** A value on that list is withheld from script scope whatever the checkbox says, and the server is where that is decided. Showing it per row would need either an endpoint exposing the list or a second copy of it in the client — and a second copy of a security decision is a second place for it to drift. The residual is that an administrator can tick "visible" on a reserved key and see it stay ticked while the value remains withheld; the display misleads, the enforcement does not. Recorded rather than closed.

**The asymmetric default is what makes this shippable.** Existing values default to visible, in the column default, so adding the column changes no deployment's behaviour on upgrade. New *secure* values are created hidden. A symmetric default would force a choice between breaking deployments on day one and shipping a control that protects nothing new.

**The classification and S-014's reserved-key list are independent reasons to withhold**, and either is sufficient. Classifying a reserved key visible does not un-reserve it. The backlog report subtracts both, so a value hidden by classification drops out of the reported exposure exactly as a reserved key does — otherwise the report would overstate what remains and never converge.

### S-020 — Introduce the credential provider abstraction

**What changes.** Credential resolution moves behind a provider abstraction with two implementations — one backed by configuration values, one by the existing secrets-vault client — selected by configuration. The vault client and its reader are relocated out of the API assembly into a shared assembly, correcting the namespace so it conforms to the repository's naming standard rather than propagating a violation.

**Why it changes.** SD-4's foundation, and it removes vault reachability from the Monitor hosts as a design dependency: it becomes a deployment-time choice between two implementations.

**Dependencies.** S-019, if the configuration-value-backed variant is used — a fixed denylist cannot cover keys minted per environment, so the classification must exist first. No dependency if the vault-backed variant is chosen.

**Verification intent.** Both implementations resolve the same credential for the same reference. Switching implementations by configuration requires no code change. The relocated client is consumed identically by the API and the Monitor.

**All four resolution sites now go through it**, which was not stated as part of this step but is what makes it worth doing: the PowerShell dispatcher, the Terraform dispatcher, the daemon status probe in the API process, and the password reset controller each carried their own copy of the same four key names and the same production boolean. Four copies of a security decision is four places for it to drift, and it is why the probe could be reached without authorization while the dispatchers could not.

**One thing preserved rather than quietly corrected.** The password reset controller resolves the **non-production** credential regardless of which server it is resetting a password on. That is either deliberate or a latent defect; it belongs to whoever owns the endpoint, and making it environment-keyed is S-021's business. It is now visible at a single call site instead of being buried in a hard-coded key name.

**The relocation is what makes the vault option real.** The reader moved from `Dorc.Api.Services` — an assembly the Monitor cannot reach, and a namespace the repository's naming standard names as a dumping ground to avoid — into `Dorc.Core.Secrets`. Before that the Monitor could not have read a credential from the vault however it was configured, so "vault-backed" would have been an API-only capability. The vendor-shaped `OnePassword.Connect.Client` namespace is corrected to `Dorc.Core.Secrets.OnePassword` at the same time.

**Configuration values stay the default.** Switching where the whole estate's deployment credentials come from is a deliberate act, not something a release performs on an operator's behalf. The vault-backed source **refuses rather than falling back** when it is selected but its item identifiers are unset — falling back would silently undo the decision to stop keeping those credentials in DOrc's database.

**The S-019 dependency did not bind.** It applies only if the configuration-value variant needs to cover keys minted per environment, which is S-021's concern; the abstraction itself is tier-based and needed no classification first.


### S-021 — Bind execution identity to the environment

**What changes.** Environments gain an optional identity reference, and credential resolution becomes environment-keyed at **every** resolution site — the PowerShell dispatcher, the Terraform dispatcher, the daemon status probe running in the API process, and the password reset controller. Sites with no configured identity fall back to the current pair, and the count still on fallback is reportable so migration progress is visible.

The Terraform component branch has no equivalent of the production-only gate that exists on the PowerShell branch, and components on that branch may have no script record at all. That gate's insertion point must be established on the Terraform branch before identity gating can be applied uniformly.

Binding granularity is by sensitivity tier rather than per environment: the estate holds well over a thousand environments, so per-environment accounts are not viable, while the data model retains per-environment binding for cases that warrant it.

**Why it changes.** W-6 and W-15. Execution identity is currently a boolean, and four sites across two processes duplicate the same resolution logic.

**Dependencies.** S-020. **Revisits S-007, S-008 and S-009** — each sets an access control list naming the deployment identity, which this step makes environment-dependent. If those steps derived their principal from the credential resolution point as instructed, this is a drop-in; if not, they must be corrected here.

**Also picks up W-8a**, deferred out of S-007: the local Terraform plan directory is shared across deployments, so restricting it needs the environment-dependent identity this step introduces, and its retention question is entangled with the plan/apply blob handshake rather than being housekeeping.

**Verification intent.** An environment with a configured identity deploys under it on both dispatch paths and both API-side sites. An environment without one deploys exactly as before. No resolution site returns a credential for an environment it is not bound to. The fallback count is reportable.

**Nothing changes until an operator names an identity.** The environment's identity reference is nullable and null on every existing row, and a null reference resolves the tier default by exactly the path it did before. That is what makes this shippable without a migration event: the estate holds well over a thousand environments and they move one at a time, on whatever schedule the target-server access control lists allow (**U-9**).

**An unresolvable named identity refuses rather than falling back.** If an environment names an identity and that identity cannot be resolved — the configuration keys are absent, or the vault item is not there — the resolution returns nothing and the deployment fails with a named reason. Falling back to the shared account would be the worst of both worlds: the operator would believe the environment was bound while it was in fact still deploying under the credential the binding exists to stop using, and the fallback count would report progress that had not happened.

**Where the count is reported.** Once per request, from the Monitor's request processor, as bound-versus-fallback resolutions. A refused identity counts as neither, deliberately — it is a failure, not a migration state, and folding it into either number would make an outage look like progress or like regression.

The two dispatchers accumulate into one request-owned counter passed down by the request processor. The processor reports it from its `finally` path, including zero-resolution requests, so successful, failed, cancelled, and early-return requests each produce exactly one adoption report.

**How an identity is bound.** Only the administrator-only `PUT /RefDataEnvironments/{environmentId}/ExecutionIdentity` endpoint can set or clear the reference. The persistence method repeats the administrator check, accepts null/empty only for clearing, validates the shared 128-character identifier grammar, and records the old and new references in environment history. Generic create, update, and clone paths do not assign the reference.

**The password reset controller is bound by identity but not by tier.** It resolves the environment's identity reference like every other site, so an environment that names one resets passwords under it. Its hard-coded non-production tier is left exactly as it was: correcting it would begin making production logons from the API process where none were made before, which is a change of behaviour for whoever owns that endpoint to make, not a side effect of this step. Both halves are now visible at one call site.

**The Terraform gate insertion point was not established.** This step recorded that the Terraform component branch has no equivalent of the PowerShell branch's production-only gate, and that its insertion point must be found before identity gating can be applied uniformly. It has not been found. What this step delivers on the Terraform branch is identity *binding*, not identity *gating* — the Terraform dispatcher resolves the environment's identity exactly as the PowerShell one does, but there is still no place on that branch where a component is refused for being production-bound. That is unchanged from before this step and is not made worse by it; it is recorded here rather than closed.

#### W-8a, picked up here

The local Terraform plan directory is now one directory per deployment result under `%ProgramData%\dorc\terraform-plans`, with a protected access control list admitting the Monitor, SYSTEM, Administrators, and that deployment's own identity. The shared directory is what made restriction impossible before: an access control list over a folder every deployment writes into would have to admit every identity that deploys on the host, which is no restriction at all. Splitting the layout is what lets a single identity be named, and that identity only became environment-dependent in this step.

The grant is on the directory rather than on the files because the files do not exist yet — `terraform plan -out` creates the binary plan and the Runner writes the rendered content, both as the deployment account. That is the difference from the script-group bundle, which the Monitor writes and can therefore grant per file.

**Retention resolves the entanglement rather than ignoring it.** The plan path expires its local artefacts only after both blob uploads have returned, so a failed upload leaves the local copy in place — it is the only copy, and deleting it would turn a recoverable failure into a lost plan. The apply path expires unconditionally, on success and failure alike, because there the local file is a cache of the blob that was downloaded into it. A sweep on each reservation clears what an earlier deployment left behind, including artefacts written directly into the root before this layout existed — on a long-lived deployment host that backlog is every plan it has ever produced.

Two things the sweep deliberately does not do: it never removes the directory being reserved, however old that directory looks, because an apply confirmed after the retention window has passed reserves a directory older than the cutoff; and it judges a directory's age by the newest artefact inside it rather than by the directory's own write time, which does not move when a file within it is written.

**What is not covered by test.** The access control list itself. Applying one needs a Windows security authority, and these tests run wherever the build does — the same boundary the runner process descriptor tests draw. What is covered is the layout and the lifetime, which is what decides who *could* be admitted. The dispatcher's half of the lifetime is not reachable either: dispatch builds a Windows process security context from a real account before it ever touches the plan store.

### S-022 — Record attribution reached and not reached

**What changes.** Documentation, not code: a record of what per-environment identity delivers for attribution in target-server telemetry, and what it does not.

**Why it changes.** W-9. Environment-granularity attribution is a substantial improvement over none. Per-request attribution would require impersonation semantics well beyond this scope and is not proposed. Recording the residual prevents it being assumed closed.

**Dependencies.** S-021.

**Verification intent.** Not applicable — this step is deliberately exempt from a testable criterion, as stated in the HLPS.

**Delivered as `S-022-attribution-reached-and-not-reached.md`.** Three things in it are worth carrying back here.

**Attribution is a property of migration state, not of the release.** Shipping S-021 changed the attribution of nothing: every environment's identity reference is null, and a null reference resolves the tier default exactly as before. What changes attribution is an operator naming an identity on an environment *and* provisioning that account on its target servers. This is why the bound-versus-fallback count exists — "we have begun binding identities" and "the estate is bound" are different claims, and only the second means anything for attribution.

**What improves is the join, not the target's own record.** DOrc's side of the seam is fully populated — request, raiser, environment, project, component, result, Monitor host, Runner process identifier, Runner log path. The target server holds the account, the source host and the time. The join is therefore `(account, source host, time window)`, which identifies a deployment uniquely only when one deployment under that account was in flight from that host in that window. DOrc runs deployments concurrently, so that condition often fails at tier granularity and usually holds at environment granularity. That is the honest statement of the improvement.

**Per-user attribution is not narrowed at all and is not proposed.** The requesting user never reaches the target server in any form. Carrying it there would require the user to hold rights on the target servers, which inverts the reason DOrc exists — people deploy precisely because they do not hold those rights. The document records the three routes to finer attribution and why each is out of scope, so the decision is visible rather than re-derived later under time pressure.

---

## Phase I — Remove the compiler

### S-023 — Replace the expression compiler with a fixed grammar

**What changes.** The scripting-compiler dependency is removed from the core assembly and replaced with a parser for the grammar the estate actually uses: a string literal containing token interpolations, followed by a chain of lower-casing, upper-casing and replacement operations.

Design notes from the inventory: interpolation must handle adjacent tokens and tokens abutting literal text, both of which occur in live values; replacement is always called with two literal strings, including the empty string; and the grammar must **fail closed** — an expression it cannot parse is an error, never a fallback to compilation. Failing closed is what contains the residual from secure configuration values, which could not be inventoried but do reach the evaluator.

**Why it changes.** W-1, completing what S-004 contained. The inventory returned eleven distinct expressions over one hundred and thirty-three values, every one of that single shape, with no loops, reflection, assembly references or input/output. A parser is sufficient; a compiler is not required and should not remain reachable.

**Dependencies.** S-004. Distant — the containment has already removed the reachable path, so this is cleanup rather than urgent.

**Verification intent.** A differential test evaluates every inventoried expression under both the old and new implementations and asserts identical results. An unparseable expression fails with a clear error and does not fall back. The scripting-compiler package is absent from the assembly's dependencies.

**One correction to the design notes.** They call for interpolation to be handled by the grammar, including adjacent tokens and tokens abutting literal text. It must not be: interpolation has already happened by the time an expression reaches the evaluator, so what the parser sees is the *substituted* literal. Adjacent tokens and abutting text reduce to ordinary literal content before parsing and need no special handling. The differential corpus therefore uses post-interpolation forms, which is what actually reaches the evaluator.

That ordering is also why parsing is strict rather than forgiving: a substituted value containing a quote makes the literal's extent ambiguous, and an ambiguous expression is refused rather than guessed at.

**The differential is real, not recorded.** The scripting package moves to the test project so both implementations can be evaluated side by side; production no longer carries it. The corpus covers every grammar feature the inventory recorded rather than the verbatim eleven expressions, which the HLPS records by shape rather than in full.

**The dependency's absence is asserted by a test**, against the production assembly's own reference list rather than by reading a project file, so it stays true if the package is reintroduced transitively. Verified absent from the `Dorc.Core`, `Dorc.Monitor` and `Dorc.Api` dependency graphs.

**Failing closed has a real cost, accepted deliberately.** Secure configuration values are encrypted at rest, could not be inventoried, and do reach the evaluator. If one of them carries an `fn:` expression outside this grammar, it evaluated before and will now fail the deployment. That is the trade the HLPS chose; the failure names the shape of the problem and its position, and deliberately does **not** quote the literal, which is a resolved property value and may be a secret.

---

## Open Items Carried From the HLPS

These are step preconditions, not approval blockers. Each is named in the step it gates.

| Item | Gates | Action |
|------|-------|--------|
| U-12 | S-013, S-014 | Re-run the script-share scan with the widened secure key set. Both completed scans used the original four-key pattern. |
| U-13 | S-000 | Query the production instance's configuration table. All database inventory to date came from the System Test instance. Read-only. **Answered**; S-000 applied in production. |
| ~~U-14~~ | S-018 | **Resolved** in `SPEC-S-018-script-content-verification.md`: DOrc records the baseline on first dispatch, an administrator-gated endpoint re-records it on promotion, and an unrecorded baseline is never a refusal. |
| U-5 | Bounds S-008's value | Whether the deployment account is a local administrator on Monitor hosts. The step is correct either way. |
| U-9 | S-021 adoption pace | Target-server access control lists naming the shared account. |
| Sibling HLPS | — | The API authorization enforcement document has no owner and does not exist. W-17 and two low findings recorded in the HLPS belong there. The HLPS makes raising it a pre-approval condition; that condition is unmet and was accepted at escalation rather than satisfied. |


---

## Closing State

Every step is built or handed to an operator. What that does and does not mean:

**Delivered as code**, on stacked branches so each step is reviewable on its own: S-001 to S-012, S-014, S-014a, S-017 to S-023. Each carries its own tests and its own residuals; the residuals are recorded in the steps above rather than closed.

**Delivered to an operator**, and not executed from here: S-000 (applied in production), S-013, S-015, S-016. Credential rotation, script-share migration and stored-row remediation touch live systems, and this sequence prepares and verifies them rather than performing them. **S-013 and S-015 are unexecuted at the time of writing**, and S-014's reserved-key list therefore still covers only the three keys with zero consumers.

**Two steps ship inert until configured.** S-011's source-URL validation does nothing until the allowed hosts are set, which makes S-012 partly inert with it. S-017 reports rather than enforces until S-016's worklist is cleared. That ordering is deliberate — confine, remediate, then enforce — and it means shipping the code is not the same as closing the weakness.

**Two steps must ship in lockstep, not additively**: S-009 (the script-group transport, which an old runner would fail to authenticate to) and S-018b (content verification, which an old runner would silently skip). Both span all three runners and both dispatchers, and both are packaged in one installer, so this is achievable. It must be confirmed rather than assumed.

**What remains genuinely open**, beyond execution of the operational steps: the sibling HLPS on API authorization enforcement, which has no owner and does not exist. The HLPS made raising it a pre-approval condition; that condition was accepted at escalation rather than satisfied, and W-17's neighbours still live there.
