# REVIEW-S-007: At-Rest Secret Encryptor — Diff Review

| Field       | Value                                          |
|-------------|------------------------------------------------|
| **Status**  | DRAFT                                          |
| **Author**  | Agent                                          |
| **Date**    | 2026-06-05                                     |
| **HLPS**    | HLPS-security-rbac-hardening.md (DRAFT) — SF-4 |
| **IS step** | S-007 (resolves U-3, U-4)                       |
| **Subject** | `QuantumResistantPropertyEncryptor` (live `IPropertyEncryptor`) |

---

## 1. Purpose

S-007 reviews the shipped, bot-authored `QuantumResistantPropertyEncryptor` — now the live
at-rest secret encryptor (registered in `CoreRegistry`, `Monitor/Program.cs`, and used by
`Tools.EncryptionMigrationCLI`). The review establishes whether the SF-4 concerns are cosmetic
or substantive **before** any code is touched, and resolves U-3 (key-derivation stability) and
U-4 (rename feasibility). No production code is changed by this step; recommended changes are
scoped as separate, separately-reviewed steps.

**Bottom line:** the encryption itself is sound and the on-disk format is safe to keep. The
concerns are (a) a misleading type name (cosmetic but worth fixing) and (b) a key-derivation
quirk that is *stable* (no data-loss risk) but should be documented. There is **no** finding
severe enough to warrant a format change or re-encryption.

---

## 2. What the code does (as shipped)

- **Format:** `v2:` + base64( nonce[12] ‖ ciphertext ‖ tag[16] ), AES-256-GCM.
- **Decryption** dispatches on prefix: `v2:` → GCM; `v1:` / unversioned → delegates to the legacy
  `PropertyEncryptor` (AES-CBC). Backward compatibility preserved.
- **Key derivation** (constructor): base64-decode the stored key; if ≥32 bytes use the first 32
  bytes; if <32 bytes use `SHA-256(keyBytes)`; if not valid base64 use `SHA-256(UTF8(key))`.
- **Nonce:** 12 random bytes per encryption from `RandomNumberGenerator`.

---

## 3. Findings

### F-1 — Misleading name (LOW, cosmetic) — *confirmed*
AES-256-GCM is a symmetric cipher believed to resist quantum attack (Grover gives only a
quadratic speedup); it is **not** post-quantum cryptography. The name `QuantumResistant…` and the
relocated `QUANTUM_ENCRYPTION_UPGRADE.md` overstate the property, and the name conflicts with the
CLAUDE.md cohesive-naming rule (it describes a *marketing claim*, not what the class *is*).
**Recommendation:** rename to a name that states what it does, e.g. `AesGcmPropertyEncryptor` or
`AuthenticatedPropertyEncryptor`. Pure refactor — see U-4.

### F-2 — Key-derivation quirk is stable, not a defect (U-3 → RESOLVED)
The "truncate-first-32-or-SHA-256" branch is a code smell, but it is **deterministic for a given
stored key**: the same key string always derives the same 32-byte AES key. Because every `v2:`
value was written with this same derivation, all existing values remain decryptable as long as
the `SecureKeys` row is unchanged. There is **no migration/data-loss risk** and therefore no
correctness reason to change it. Two notes for the record:
- Truncating a >32-byte base64 key discards the extra bytes — harmless (AES-256 needs exactly 32),
  but a defined KDF (e.g. always `SHA-256`) would be cleaner if the type is ever revised.
- The legacy `PropertyEncryptor` is constructed with the original `iv`/`key`, so `v1:`/unversioned
  decryption is unaffected.
**U-3 verdict:** key derivation is stable across the existing key configuration; safe to keep the
format. Any future KDF cleanup must be paired with a re-encryption migration (out of scope here).

### F-3 — Random nonce under a long-lived key (LOW, document — accept)
A random 96-bit nonce under a single static key carries a birthday-bound collision risk
(non-negligible only past ~2³² encryptions of secrets under the same key). At DOrc's secret
volume this is comfortably acceptable. GCM nonce reuse would be catastrophic, but the volume here
makes a collision implausible. **Recommendation:** accept, and record the assumption; if secret
volume ever approached that scale, a counter/random-prefix nonce scheme would be the mitigation.

### F-4 — Consistency across processes — *no split-brain* (informational)
All consumers resolve `IPropertyEncryptor` via DI, and every registration (API via `CoreRegistry`,
Monitor, migration CLI) uses the v2 encryptor. No process writes `v2:` that another cannot read.
Confirmed earlier in the review and unchanged.

---

## 4. U-4 — Rename feasibility (RESOLVED)

A rename is a pure refactor with a bounded, enumerable touch-list:
- Type definition: `src/Dorc.Core/VariableResolution/QuantumResistantPropertyEncryptor.cs`
- Registrations/instantiations: `src/Dorc.Core/Lamar/CoreRegistry.cs`,
  `src/Dorc.Monitor/Program.cs`, `src/Tools.EncryptionMigrationCLI/Program.cs`
- Tests: `src/Dorc.Core.Tests/QuantumResistantPropertyEncryptorTests.cs`
- Docs: `docs/secret-encryption/QUANTUM_ENCRYPTION_UPGRADE.md` (also re-title)

No interface, no DI shape, and **no on-disk format** change. The `v2:` prefix and all bytes stay
identical, so existing values keep decrypting. **U-4 verdict:** feasible as a pure refactor.

---

## 5. Recommendation

| Item | Action | Severity | Suggested step |
|------|--------|----------|----------------|
| F-1 name | Rename type + re-title doc (pure refactor) | LOW | New step S-008 (refactor; keep format byte-identical) |
| F-2 KDF | Document the derivation; **no change** without a re-encryption migration | — | Note only |
| F-3 nonce | Accept; record the volume assumption | LOW | Note only |
| F-4 | No action | — | — |

No code change is made under S-007. The only recommended code change (F-1 rename) is optional,
cosmetic, and must be a behaviour-preserving refactor; if the team wants it, it should be its own
small step with its own review. **U-3 and U-4 are resolved; SF-4's substantive concern is closed
with no format change required.**
