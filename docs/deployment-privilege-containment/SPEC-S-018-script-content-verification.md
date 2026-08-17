# SPEC S-018 — Verify script content at the point of read

| Field       | Value                                                        |
|-------------|--------------------------------------------------------------|
| **Status**  | DELIVERED                                                    |
| **Step**    | S-018                                                        |
| **IS**      | IS-deployment-privilege-containment.md                       |
| **Address** | W-5 (execution half), SD-5, SC-05                            |
| **Depends** | S-017 (delivered), **U-14 (resolved below)**                 |

---

## 1. What this step is for

W-5 has two halves. The write path — which paths a component may register — is closed by S-010 and S-017. This is the other half: **the bytes actually executed are never checked against anything.**

The share is protected by a gated promotion pipeline, so the threat is not "anyone can write a script". It is that anyone who *does* obtain write access to the share, by any route, gets arbitrary code execution as the deployment account on every deployment that runs that script — with nothing in DOrc that would notice.

Verification cannot be done at dispatch. Dispatch happens in the Monitor; the executed bytes are read later, in a different process, from a network share. Anything checked at dispatch can be swapped in the window between the check and the read, by exactly the actor this exists to stop. **The check has to happen where the bytes are read**, which is `PowerShellScriptRunner.Run`, immediately before `AddScript`.

---

## 2. U-14, resolved

> *Nothing yet specifies who records the hash or how it is re-recorded when the gated pipeline promotes a new version.*

### 2.1 Who records it

**DOrc records it, on first dispatch of a script whose hash is unrecorded — trust on first use.**

More precisely: the **Monitor** captures the baseline from the share at dispatch, and the **runner** verifies against it at the point of read. The split matters and is not an implementation detail — see R-2.

Rejected alternatives, and why:

- **An operator records it by hand.** There are hundreds of registered scripts. A control whose adoption requires hundreds of manual acts is a control that is never adopted, and one whose records go stale the first time anyone forgets.
- **The promotion pipeline records it at promotion.** This is the correct end state and this spec makes it possible — but it is work in a pipeline outside this repository, and making it a *precondition* means S-018 delivers nothing until that work happens. It becomes the migration target, not the entry condition.

**What trust on first use is worth, stated honestly.** It does not establish that a script was legitimate when first seen. If the share was already compromised, the compromised bytes are what get recorded. What it detects is **change after recording** — which is the actual attack: the share is written to at some point, and every subsequent deployment executes the altered script. It converts a silent, indefinite compromise into a refusal at the next deployment.

It is also what makes the control adoptable at all, because it needs no inventory of what every script currently is.

### 2.2 How it is re-recorded on promotion

Two routes, and both must exist.

**An API endpoint** on the script record that sets or clears the recorded hash. The gated pipeline calls it after promoting a new version; an administrator can call it to clear a hash and let the next execution re-record. Writes are administrator-gated like every other `RefData` write.

**Clearing is not the same as disabling.** A cleared hash means "re-record on next execution". There is no per-script switch that means "never verify this one" — a per-script opt-out is a per-script hole, and the mode configuration below already provides the only opt-out that should exist, at the estate level.

### 2.3 The tolerance mode

Three states, one setting, `ScriptContentVerification` in AppSettings:

| Value | Unrecorded hash | Mismatch |
|---|---|---|
| `Off` | not recorded | ignored — no verification at all |
| `Report` *(default)* | recorded | logged against the deployment result; **script executes** |
| `Enforce` | recorded | **script does not execute**; deployment result fails |

`Report` is the default, for the same reason S-017 reports by default: shipping straight to `Enforce` would fail every deployment whose script had legitimately changed since its hash was recorded, on the day the release lands, across the whole estate. `Report` produces the population of mismatches; `Enforce` is turned on once that population is understood and the pipeline records hashes on promotion.

`Off` exists so that an operator can withdraw the control without a rollback if it misbehaves in an unforeseen way. It is not the default and should not be a resting state.

**An unrecorded hash is never a refusal, in any mode.** It is recorded and execution proceeds. A script executed for the first time after this ships has no recorded hash through no fault of anyone's; refusing it would be arbitrary, and it is the exact case the IS names.

---

## 3. Requirements

### R-1 — The hash

SHA-256 over the **exact bytes read**, not over decoded text. Hashing decoded text would make the answer depend on how each runner decodes it — and the two PowerShell runners are separate assemblies on separate frameworks (.NET 8 and .NET Framework 4.8), so a text-based hash would eventually disagree with itself across the two. The bytes are what execute; the bytes are what is hashed.

Recorded as lower-case hexadecimal. A string rather than a binary column, because it is compared, displayed and set over an API far more often than it is stored.

### R-2 — Where the baseline is captured, and why not in the runner

The Monitor captures it at dispatch. The obvious alternative — the runner records what it is about to execute when it finds no baseline — is worse, and worse in the exact scenario the step is about: a runner recording the bytes in front of it would record *the attacker's* bytes as the baseline and report a match.

Capturing at dispatch and verifying at the point of read means the first deployment after adoption is covered across the dispatch-to-read window too, not merely baselined. Two processes reading the same file at two moments is not a weaker check than one process reading it once — it is a stronger one, provided the comparison happens at the later read. Which is R-3.

Nothing in capture refuses. A share that cannot be read at that moment, or a script that is not there, fails the deployment moments later for its own reasons and with its own message; failing it at capture would replace that message with a worse one.

### R-3 — Where verification happens

Immediately before the script content is handed to PowerShell, in both `Dorc.PowerShell.PowerShellScriptRunner` and `Dorc.NetFramework.PowerShell.PowerShellScriptRunner`. Not earlier: any gap between the read and the execution is the window the check exists to close. The content is read once and both hashed and executed from that single read.

### R-4 — What the runner is told

The expected hash travels in the script group, per script, alongside the path it applies to. A script the Monitor has no hash for carries none, which is the unrecorded case.

**The mode travels with it, on the group.** The runner does not read the estate's setting from its own configuration: that would let a deployment host with a stale or edited configuration file quietly execute unverified, and the deployment host is the least trustworthy place to keep the answer.

### R-5 — The Monitor's behaviour when it cannot confirm verification happened

The IS requires this to be defined. **It is defined as: the Monitor does not attempt to confirm it.**

The runner writes the outcome to the deployment's own log — a warning for an unrecorded baseline or a reported mismatch, an error for a refusal — and an enforced refusal fails the deployment by refusing to execute. The Monitor does not ask whether verification took place and does not infer that it passed; a runner that performed none simply logs none.

Anything stronger is a false claim. The Monitor cannot distinguish "the runner verified and said nothing" from "the runner is an old build" without a protocol version, and a protocol version that an old build also ignores establishes nothing. The genuine guard against a stale runner is the release constraint below, not a runtime check.

### R-6 — Release constraint

**Lockstep, not additive.** The script group is deserialized by a serializer that ignores unknown members, so an un-upgraded runner would silently ignore the added hash and execute unverified — the criterion unmet, with no signal. All three runners and both dispatchers ship together. They are packaged in a single installer, so this is achievable; it must not be assumed.

### R-7 — Recording is administrator-gated

Setting or clearing a recorded hash is a write to the script record and follows the existing `RefData` authorization, which already requires `IsAdmin`. Trust-on-first-use recording is performed by the deployment path itself, not by a user.

---

## 4. Delivery

Two changes, in order, because one is safe to ship alone and the other is not.

**S-018a — the record and its API.** Schema column, entity, API model, the hash computation, the mode setting, and the endpoint that sets or clears a recorded hash. **No behaviour change to dispatch or execution**: nothing reads the column yet. Ships independently and is reversible by ignoring the column.

**S-018b — carry and verify.** The Monitor captures a first-seen baseline and puts it in the script group along with the estate's mode; both PowerShell runners verify the bytes they read before executing them. **This is the lockstep half** and carries the release constraint of R-6.

---

## 5. Verification intent

- A script whose content changed after its hash was recorded fails verification. Under `Enforce` it does not execute and the deployment result records why; under `Report` it executes and the mismatch is recorded.
- An unmodified script executes normally in every mode.
- A script with no recorded hash executes and has its hash recorded, in every mode except `Off`.
- The hash is computed over bytes, and the two runner implementations agree on the same file.
- Clearing a recorded hash causes the next execution to re-record it.

## 6. Residual

**Trust on first use does not authenticate the first read.** Stated in 2.1 and worth restating: if the share is already compromised when this ships, the compromised bytes become the baseline. Closing that needs hashes recorded by the promotion pipeline from the artefact it promoted — which this spec makes possible and does not itself deliver.

**Verification covers the entry script, not what it dot-sources.** A verified script that dot-sources an unverified file from the same share executes unverified code. The estate's script conventions decide how much this matters; it is not closed here, and it is not closed by any check placed at a single point of read.
