# HLPS: Component Version and Configuration Visibility

| Field       | Value                                    |
|-------------|------------------------------------------|
| **Status**  | DRAFT — pending adversarial review       |
| **Author**  | Agent                                    |
| **Date**    | 2026-08-03                               |
| **Folder**  | docs/msi-decomposition/                  |
| **Related** | `HLPS-msi-decomposition.md` — this document exists because of that one's C-1 and C-6 |

> **Why this is separate.** These requirements began as a single goal (G-5) inside the installer decomposition HLPS. Both adversarial reviewers independently observed that they had grown into a second project — a new API contract, a reporting channel, a digest scheme and UI work — while the installer split itself was ready to sequence. Keeping them together would have let the smaller, better-understood change be blocked by the larger, vaguer one.

---

## 1. Problem Statement

Splitting `Setup.Dorc.msi` into four independently deployable installers makes two new kinds of divergence possible, both of which are currently impossible by construction.

**Version skew.** The four components share one database schema. Deploying them independently permits API `v1` running against Monitor `v2`. Per the user decision of 2026-08-03 this is **accepted rather than prevented** — no start-up gate, no refusal to run. Accepting it creates an obligation to make it observable.

**Configuration skew — the likelier and less visible failure.** The installers do not merely place files; they write each component's configuration at install time. `Json:JsonFile` elements set specific paths in the deployed `appsettings.json` from MSI properties:

```xml
<Json:JsonFile ElementPath="$.ConnectionStrings.DOrcConnectionString"
               Value="Data Source=[DEPLOYMENT.DBSERVER];Initial Catalog=[DEPLOYMENT.DB];…" />
<Json:JsonFile ElementPath="$.AppSettings.RefDataApiUrl" Value="[DEPLOYAPI.ENDPOINT]/" />
```

With one installer, changing a shared property propagates everywhere in one operation. With four, it reaches only the packages actually run. Rotate `KAFKA_SASL_PASSWORD` and deploy the API alone: the Monitors keep the stale credential and stop consuming, **while every component still reports the same version**. Displaying versions is structurally incapable of exposing this.

**Today there is no per-component visibility at all.** The only version surface is `MetadataController`, returning the unstructured string `"{env} - {assembly version}"` — describing the API alone.

---

## 2. What Already Exists

The first draft of the decomposition HLPS claimed no reporting channel existed. That was wrong; the adversarial panel found three relevant surfaces, since verified:

| Surface | State |
|---|---|
| `DaemonObservation` table (`ServerId`, `DaemonId`, `ObservedAt`, `ObservedStatus`, `ErrorMessage`) | Exists in the schema |
| `DaemonStatusProbe` (`Dorc.Core`) — `LogonUser` impersonation, remote `ServiceController` query, `InsertObservation` | Exists, runs in the API |
| `DaemonStatusController` → `DaemonStatusApiModel` → `map-daemons.ts` | Exists, already a UI surface |
| OpenTelemetry `serviceVersion` for `Dorc.Monitor` and `Dorc.Api` | Emitted on every log record and metric |

`DaemonStatusApiModel` currently carries five string fields. **This is a small extension of working plumbing, not a new subsystem** — which is the main reason for treating it as its own tractable piece of work rather than a blocker on the split.

---

## 3. Scope

**In scope**

- Extending the daemon status model with a version and a configuration digest per component.
- Extending `DaemonStatusProbe` to collect both, under the impersonation token it already holds.
- Surfacing them in the existing UI, with divergence made visually obvious rather than merely listed.
- A digest scheme that does not leak secret material (U-2).

**Out of scope**

- Any enforcement. Per the user decision, skew is permitted; this is observability only.
- The CLIs. They run ad hoc on operator machines with no service to probe. Either scoped out or covered only by an installed-product registry read — see U-3.
- Changing how configuration is applied.

---

## 4. Goals and Success Criteria

| ID | Goal | Measured by |
|----|------|-------------|
| G-1 | Per-component version visible | For a given environment the UI shows the installed version of each component separately, never an aggregate. |
| G-2 | Per-component configuration digest visible | Two components whose applied configuration differs show different digests, on the same version. |
| G-3 | Divergence is obvious, not merely available | An operator viewing an environment where one component holds stale configuration notices without comparing strings by eye. |
| G-4 | No secret disclosure | The published digest cannot be used to recover or confirm a configuration value. See U-2. |

---

## 5. Constraints

- **C-1 — The configuration file contains secrets.** DB connection string, `DORC.API.CLIENTSECRET`, Kafka SASL credentials, OpenSearch credentials. A plain digest of the file is derived from secret material and published to a UI.
- **C-2 — Runners and CLIs differ from services.** The probe reaches Windows services on known servers. Runners are invoked by the monitors; CLIs run ad hoc. The same mechanism does not obviously cover all four.
- **C-3 — The digest must describe deployed state, not intended state.** Digesting what the installer *applied* would miss manual edits. Digesting the file *as deployed* catches both, and is what the existing probe is positioned to read.
- **C-4 — Depends on the decomposition's C-9.** `ProductVersion` currently binds to an API-only file, so every component reports the API's version. Until that is fixed, a per-component version display would faithfully report the same illusion it exists to dispel.

---

## 6. Proposed Solution Directions

### SD-1 — Extend the existing probe rather than build a channel
Add `Version` and `ConfigDigest` to `DaemonStatusApiModel`. Have `DaemonStatusProbe` read the service binary's `FileVersionInfo` (or the installed product's registry `DisplayVersion`) and hash the deployed configuration file, using the impersonation token it already holds. Surfaces through `DaemonStatusController` and `map-daemons.ts` with no new transport.

### SD-2 — A digest scheme that is not an oracle
Candidates, in rough order of preference:
1. **HMAC** the configuration content with a key held server-side. The published value is comparable across components but not a function of the secrets alone.
2. **Digest element paths plus non-secret values**, listing secret paths as present-or-absent. Weaker divergence detection: rotating a secret would not change the digest, which is the exact case C-6 of the parent HLPS is about — so this is likely disqualifying.
3. **Digest the whole file** and document that the high-entropy secrets make it impractical to attack.

Option 2's weakness is worth stating plainly, since it is the intuitive choice and it fails the motivating scenario.

### SD-3 — Present divergence, not data
A column of digests invites nobody to compare them. The display should mark an environment whose components disagree, defaulting to the common case where they agree and nothing needs attention.

---

## 7. Unknowns Register

| ID  | Description | Owner | Blocking | Resolution |
|-----|-------------|-------|---------|------------|
| U-1 | Is the OTEL `serviceVersion` already collected somewhere queryable, making the probe unnecessary for versions? | Agent | Non-blocking | **Unresolved.** `OTEL_COLLECTOR_URL` is a deployed property, so the data leaves the components; whether anything retains it in a form the API could query is unknown. |
| U-2 | Which digest scheme (SD-2)? | User + Agent | **Blocking** for G-4 | **Unresolved.** Option 2 appears disqualified by the motivating scenario; the choice is realistically between HMAC and a documented full-file digest. |
| U-3 | Are the CLIs in or out? | User | **Blocking** for scope | **Unresolved.** They have no service to probe. Out of scope is defensible; an installed-product registry read is the alternative. |
| U-4 | Does the probe run often enough, and against every relevant server? | Agent | Non-blocking | **Unresolved.** The display is only as fresh as the observation that produced it. |

---

## 8. Out-of-Scope Risks

- **Visibility is not mitigation.** An operator must look. A stale Kafka credential still silently stops consumption; the digest only shortens diagnosis. If that is judged insufficient, the answer is enforcement, which the U-1 decision in the parent HLPS explicitly ruled out — revisiting it is a change of that decision, not of this document.
- **A digest that changes for benign reasons will be ignored.** If ordinary redeployment perturbs it, operators will learn to disregard differences, and the signal is worth less than nothing.
