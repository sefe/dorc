# IS: Deployment Privilege Containment — Implementation Sequence

| Field       | Value                                                        |
|-------------|--------------------------------------------------------------|
| **Status**  | DRAFT                                                        |
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
| S-002 | Authorize the daemon status endpoint | W-15, SC-05a | **DECLINED** — daemon status reads remain available to authenticated users |
| S-003 | Enforce the API scope policy in combined auth mode | W-17 | **DONE** |
| S-004 | Contain expression evaluation by provenance | SD-1a, W-1, SC-01, SC-02 | — |
| S-005 | Confine the build drop location | W-14, SC-05 | — |
| S-006 | Remove resolved property values from runner logging | SD-6, W-7, SC-06 | — |
| S-007 | Expire execution artefacts | SD-7, W-8, W-12, SC-08, SC-08a | — |
| S-008 | Replace the null process security descriptor | SD-2, W-2, SC-04 | — |
| S-009 | Authenticate the script-group transport | SD-2, W-3, SC-04 | — |
| S-010 | Validate script paths at the write path | SD-5, W-5 | — |
| S-011 | Validate source URLs at the write path | SD-9, W-11, W-16 | — |
| S-012 | Bind source credentials to validated hosts | SD-9, W-11, W-16, SC-05b | S-011 |
| ⚙ S-013 | Migrate the three `DORC_NonProdDeployPassword` consumers | SD-3a precondition | **U-12 CLOSED** |
| S-014 | Denylist the operator-only secret keys | SD-3a, W-4, SC-03 | S-006, S-013 |
| S-014a | Restrict `CanReadSecrets` to service principals | W-19 | Usage decision (see step) |
| ⚙ S-015 | Rotate the deployment credentials | SD-3, W-4 | **S-004 and S-014** |
| ⚙ S-016 | Remediate off-share script paths | SD-5 precondition | S-010 |
| S-017 | Confine script paths at dispatch | SD-5, W-5, SC-05 | S-010, S-016 |
| S-018 | Verify script content at the point of read | SD-5, W-5, SC-05 | S-017, U-14; **lockstep release** |
| S-019 | Introduce config-value visibility classification | SD-3b, W-4 | S-014 |
| S-020 | Introduce the credential provider abstraction | SD-4 | S-019 (ConfigValues variant only) |
| S-021 | Bind execution identity to the environment | SD-4, W-6, W-15, SC-07 | S-020; revisits S-007, S-008, S-009 |
| S-022 | Record attribution reached and not reached | SD-8, W-9 | S-021 |
| S-023 | Replace the expression compiler with a fixed grammar | SD-1b, W-1 | S-004 |

**Independently shippable today, in any order:** S-001, S-003, S-004, S-005, S-006, S-007, S-008, S-009, S-010, S-011. Ten steps, covering every accepted rank-1 and rank-2 remediation and the write-path half of every rank-3 weakness. Nothing in that set waits on an unknown.

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

**Decision.** Declined after review. Daemon status retrieval remains available to any authenticated user and does not require environment write permission.

**Residual risk.** W-15 remains open: the endpoint takes an environment identifier and drives a production-credential logon inside the API process. S-021 remains the planned remediation for removing that credential resolution from the API process without restricting status reads.

**Verification intent.** Authenticated users retain the existing read behaviour. S-021 must preserve that behaviour while removing the privileged credential use.

### S-003 — Enforce the API scope policy in combined auth mode

**What changes.** The composition root's conditional that applies the global scope authorization policy is corrected so that it also applies in the combined OAuth-plus-Windows authentication mode, not only in the OAuth-only mode. The current test is an exact string comparison that the combined mode's distinct value does not satisfy.

**Why it changes.** W-17. In combined mode the scope requirement is silently never enforced. Audience validation and the authenticated-principal requirement still hold, so this is a narrowing of an existing gap rather than an open door — but a policy that is registered and not applied is worse than one that was never written, because it reads as protection.

This is transitional hardening while combined authentication remains supported. It does not reverse the planned removal of Negotiate/Windows authentication from the primary API in the API-split plan's S-007; once that removal lands, only the OAuth policy path remains.

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

**Verification intent.** After a successful deployment and after a failed one, no bundle and no Terraform working directory remains. A newly created working directory carries the restricted access control list rather than inherited permissions. The deployment identity can read the bundle it is meant to consume — this is the regression the current identity set would produce if corrected carelessly.

---

## Phase D — Process and transport confinement

### S-008 — Replace the null process security descriptor

**What changes.** The security descriptor applied to runner process creation is given an explicit discretionary access control list admitting the Monitor service account and the local system account, replacing the present null list which grants full access to every local principal. The logon token that is currently leaked is disposed. The use of a cleartext logon type is reassessed, and the credential's residence in a non-zeroable managed string is recorded for S-020 to address at the storage layer.

**Why it changes.** W-2. Any local principal can currently open a runner process for full access — reading the decrypted property bag from its memory or injecting code to execute as the deployment account. This also bounds what S-009 can achieve: transport confidentiality is worth little when the consuming process object is world-writable.

**Dependencies.** None. Derive the principal from the credential resolution point.

**Verification intent.** A local principal that is neither the Monitor service account nor system cannot open a runner process for write access. The runner still starts, runs and exits normally under the deployment identity. No handle is leaked across a deployment cycle.

### S-009 — Authenticate the script-group transport

**What changes.** Three changes to the named-pipe transport. The server is created with an explicit access control list rather than the platform default. The client verifies the server's identity before accepting a script group, rather than accepting from whatever process owns the name. The server is confirmed ready before the runner process is started, closing the creation race.

If an unpredictable pipe name is adopted as defence in depth, it must be generated per server instance rather than per request: the name is currently composed once per request while the enclosing loop iterates per script group, so a per-request random name would collide with itself on a multi-version request.

**Why it changes.** W-3. Confidentiality and integrity both fail today, in opposite directions — the first local process to connect receives the cleartext bundle, and a process that wins the creation race can feed the runner an attacker-authored script group, controlling both the script location and path. Server-side access control alone addresses only the first; the client-side check is what closes the escalation.

**Dependencies.** None. Derive the principal from the credential resolution point.

**Verification intent.** A process other than the Monitor cannot read a script group in transit. A runner refuses a script group offered by a process that is not the expected server. A squatter holding the pipe name before the Monitor creates it does not receive the bundle and does not cause the runner to execute attacker-supplied content. Normal deployments are unaffected on both transports.

---

## Phase E — Write-path validation

### S-010 — Validate script paths at the write path

**What changes.** Component validation is extended to reject a script path that, after canonicalisation, resolves outside the configured script root. Validation currently covers component names, identifiers, project ownership, duplicates and name lengths, and does not inspect the script path at all.

**Why it changes.** W-5, rank 3. The path-combining behaviour discards the script root entirely when the supplied path is rooted, so a user holding modify rights on a single project can point a component at any location and have it executed as the deployment account — bypassing the gated promotion pipeline that protects the share. This is the write half; S-017 enforces at dispatch once existing data is remediated.

**Dependencies.** None. Additive and non-breaking: it constrains new and updated components only.

**Verification intent.** A component update supplying a rooted path outside the script root is rejected with a clear message. A relative path within the root succeeds. Traversal sequences are rejected. Existing components are untouched — this step deliberately does not validate stored data.

### S-011 — Validate source URLs at the write path

**What changes.** Project validation is extended to check both the Terraform repository URL and the artefacts URL against a host allow-list. Neither is inspected today: project validation runs five checks, none of which examines a URL's host, and the artefacts URL is accepted on a bare scheme-prefix test.

**Why it changes.** W-11 and W-16, rank 3. Both fields are settable at per-project modify rights and both become execution or credential-bearing inputs. The pattern to follow already exists in the codebase for the GitHub branch, with its rationale recorded in a comment; this extends it to the two branches that lack it.

**Dependencies.** None.

**Verification intent.** A project update naming a host outside the allow-list is rejected for either field. Allowed hosts succeed. The allow-list is configurable rather than compiled in, since it is deployment-specific.

### S-012 — Bind source credentials to validated hosts

**What changes.** Source credentials are released only to hosts on the validated allow-list. Specifically: the Terraform personal access token is no longer supplied to an arbitrary clone target; the Entra bearer token attaches on a parsed-host comparison rather than a substring test against attacker-controlled text; and the build-server client's fallback that offers the API service account's Windows credentials to an arbitrary host is removed or constrained to allow-listed hosts.

**Why it changes.** W-11 and W-16. The substring test is satisfiable by any host containing the expected string, and the Windows-credential fallback is the leg no token-scoping fix would catch — it offers an interactive service identity rather than a scoped token.

**Dependencies.** S-011, whose allow-list this consumes.

**Verification intent.** A URL crafted to contain an allow-listed host as a substring of an attacker-controlled host attracts no credential. Legitimate hosts continue to receive the correct credential. No path offers default Windows credentials to a host that is not allow-listed.

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

### S-017 — Confine script paths at dispatch

**What changes.** Path resolution at dispatch rejects any script path that resolves outside the configured script root after canonicalisation, covering both the direct and JSON path forms.

**Why it changes.** W-5. The write-path check in S-010 constrains new data; stored rows predate it and rows can arrive by other means. Both ends are required — validating only at write leaves stored rows unchecked, and validating only at dispatch means the API accepts values it will later refuse.

**Dependencies.** S-010, S-016.

**Verification intent.** A component whose stored path resolves outside the root fails with a clear, attributable result rather than executing. All remediated components succeed. Comparison is against the configured root, not a pattern — the inventory query's pattern-based classification over-flags, and the enforcement must not inherit that.

### S-018 — Verify script content at the point of read

**What changes.** A content hash is recorded for each script and carried to the runner, which verifies it against the bytes it is about to execute, immediately before execution.

**Why it changes.** W-5. Verifying at dispatch would be defeated by a race the design itself creates: dispatch happens in the Monitor, the executed bytes are read later in another process, and anyone with share write access — precisely the threat — can swap the file in the window. Restoring the platform's own authorization manager is worth doing as defence in depth but does not substitute, because scripts are executed as in-memory blocks that file signature checks do not reach.

**Dependencies.** S-017. **U-14** — nothing yet specifies who records the hash or how it is re-recorded when the gated pipeline promotes a new version. Scripts change on the share outside DOrc's knowledge, so a naive implementation breaks dispatch on every legitimate update. The adoption path needs a mode that tolerates an unrecorded hash, and that decision must be made before this step is specified.

**Release constraint.** **Lockstep, not additive.** The script group is deserialized by a serializer that ignores unknown members, so a runner that has not been upgraded would silently ignore an added hash and execute unverified — the criterion unmet with no signal. All three runners and both dispatchers ship together; they are packaged in a single installer, so this is achievable. The Monitor must also define its behaviour when it cannot confirm the runner performed verification.

**Verification intent.** A script whose content changed after its hash was recorded fails verification and does not execute, with the outcome recorded against the deployment result. An unmodified script executes normally. An unrecorded hash follows the adopted tolerance mode rather than failing arbitrarily.

---

## Phase H — Execution identity

### S-019 — Introduce config-value visibility classification

**What changes.** Configuration values gain an additive visibility attribute controlling whether they may be resolved into script scope, with an administrative surface in the web application for maintaining it. Existing values default to visible; newly created secure values default to hidden.

**Why it changes.** W-4, generalising S-014's fixed list. The default-visible inversion is what makes this safe to ship without an estate-wide inventory: no existing deployment can break, and administrators tighten per key at their own pace.

**Dependencies.** S-014.

**Verification intent.** An existing value's behaviour is unchanged until an administrator changes its classification. A newly created secure value is hidden by default. A hidden value does not appear in the serialized script group. The administrative surface is reachable only by administrators.

### S-020 — Introduce the credential provider abstraction

**What changes.** Credential resolution moves behind a provider abstraction with two implementations — one backed by configuration values, one by the existing secrets-vault client — selected by configuration. The vault client and its reader are relocated out of the API assembly into a shared assembly, correcting the namespace so it conforms to the repository's naming standard rather than propagating a violation.

**Why it changes.** SD-4's foundation, and it removes vault reachability from the Monitor hosts as a design dependency: it becomes a deployment-time choice between two implementations.

**Dependencies.** S-019, if the configuration-value-backed variant is used — a fixed denylist cannot cover keys minted per environment, so the classification must exist first. No dependency if the vault-backed variant is chosen.

**Verification intent.** Both implementations resolve the same credential for the same reference. Switching implementations by configuration requires no code change. The relocated client is consumed identically by the API and the Monitor.

### S-021 — Bind execution identity to the environment

**What changes.** Environments gain an optional identity reference, and credential resolution becomes environment-keyed at **every** resolution site — the PowerShell dispatcher, the Terraform dispatcher, the daemon status probe running in the API process, and the password reset controller. Sites with no configured identity fall back to the current pair, and the count still on fallback is reportable so migration progress is visible.

The Terraform component branch has no equivalent of the production-only gate that exists on the PowerShell branch, and components on that branch may have no script record at all. That gate's insertion point must be established on the Terraform branch before identity gating can be applied uniformly.

Binding granularity is by sensitivity tier rather than per environment: the estate holds well over a thousand environments, so per-environment accounts are not viable, while the data model retains per-environment binding for cases that warrant it.

**Why it changes.** W-6 and W-15. Execution identity is currently a boolean, and four sites across two processes duplicate the same resolution logic.

**Dependencies.** S-020. **Revisits S-007, S-008 and S-009** — each sets an access control list naming the deployment identity, which this step makes environment-dependent. If those steps derived their principal from the credential resolution point as instructed, this is a drop-in; if not, they must be corrected here.

**Verification intent.** An environment with a configured identity deploys under it on both dispatch paths and both API-side sites. An environment without one deploys exactly as before. No resolution site returns a credential for an environment it is not bound to. The fallback count is reportable.

### S-022 — Record attribution reached and not reached

**What changes.** Documentation, not code: a record of what per-environment identity delivers for attribution in target-server telemetry, and what it does not.

**Why it changes.** W-9. Environment-granularity attribution is a substantial improvement over none. Per-request attribution would require impersonation semantics well beyond this scope and is not proposed. Recording the residual prevents it being assumed closed.

**Dependencies.** S-021.

**Verification intent.** Not applicable — this step is deliberately exempt from a testable criterion, as stated in the HLPS.

---

## Phase I — Remove the compiler

### S-023 — Replace the expression compiler with a fixed grammar

**What changes.** The scripting-compiler dependency is removed from the core assembly and replaced with a parser for the grammar the estate actually uses: a string literal containing token interpolations, followed by a chain of lower-casing, upper-casing and replacement operations.

Design notes from the inventory: interpolation must handle adjacent tokens and tokens abutting literal text, both of which occur in live values; replacement is always called with two literal strings, including the empty string; and the grammar must **fail closed** — an expression it cannot parse is an error, never a fallback to compilation. Failing closed is what contains the residual from secure configuration values, which could not be inventoried but do reach the evaluator.

**Why it changes.** W-1, completing what S-004 contained. The inventory returned eleven distinct expressions over one hundred and thirty-three values, every one of that single shape, with no loops, reflection, assembly references or input/output. A parser is sufficient; a compiler is not required and should not remain reachable.

**Dependencies.** S-004. Distant — the containment has already removed the reachable path, so this is cleanup rather than urgent.

**Verification intent.** A differential test evaluates every inventoried expression under both the old and new implementations and asserts identical results. An unparseable expression fails with a clear error and does not fall back. The scripting-compiler package is absent from the assembly's dependencies.

---

## Open Items Carried From the HLPS

These are step preconditions, not approval blockers. Each is named in the step it gates.

| Item | Gates | Action |
|------|-------|--------|
| U-12 | S-013, S-014 | Re-run the script-share scan with the widened secure key set. Both completed scans used the original four-key pattern. |
| U-13 | S-000 | Query the production instance's configuration table. All database inventory to date came from the System Test instance. Read-only. |
| U-14 | S-018 | Decide who records a script's content hash and how it is re-recorded on pipeline promotion. |
| U-5 | Bounds S-008's value | Whether the deployment account is a local administrator on Monitor hosts. The step is correct either way. |
| U-9 | S-021 adoption pace | Target-server access control lists naming the shared account. |
| Sibling HLPS | — | The API authorization enforcement document has no owner and does not exist. W-17 and two low findings recorded in the HLPS belong there. The HLPS makes raising it a pre-approval condition; that condition is unmet and was accepted at escalation rather than satisfied. |
