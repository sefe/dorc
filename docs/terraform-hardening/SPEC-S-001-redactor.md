# SPEC: S-001 — Sensitive-property redactor + new test project

| Field      | Value                                |
|------------|--------------------------------------|
| **Status** | DRAFT (executing) |
| **IS step**| S-001                                |
| **SC**     | SC-04 (foundation), SC-16 (foundation) |

## Cohesion-first name decision

Candidates:
- `Redactor` — verb-noun, ambiguous (redact what?). Borderline grab-bag.
- `SensitivePropertyRedactor` — explicit. Single responsibility: redacts sensitive property values. **CHOSEN.**
- `LogRedactor` — boundary-specific; risks the "what counts as a log?" leak. Rejected.

Per CLAUDE.md cohesion test: "this class owns *redacting sensitive properties from a property bag (or its JSON serialization)*." One sentence, no "and". Pass.

## Surface

```
namespace Dorc.TerraformRunner.Logging

public sealed class SensitivePropertyRedactionOptions
    Patterns: IReadOnlyList<Regex>             // configured; default below
    DefaultPatterns(): yields one Regex
        pattern: (?i)(token|pat|secret|password|key|connectionstring)

public sealed class SensitivePropertyRedactor
    ctor(SensitivePropertyRedactionOptions)
    RedactProperties(IDictionary<string, string?>) → IDictionary<string, string?>
        for each entry, if key matches any pattern → value becomes "[REDACTED]"
        result is a new dict (input not mutated)
    RedactJson(string json) → string
        replaces the *value* of any "name": "value" pair where name matches a pattern
        non-string values: numbers, bools, null — left untouched (low risk)
        nested objects scanned recursively
```

## Test plan (test-first; MSTest + NSubstitute idiom)

1. `RedactProperties` with all-non-sensitive bag → values unchanged.
2. `RedactProperties` with `Token` property → value `[REDACTED]`.
3. `RedactProperties` with `password` (case insensitive) → redacted.
4. `RedactProperties` with custom pattern only → default pattern *not* applied.
5. `RedactProperties` does not mutate the input dictionary.
6. `RedactProperties` is idempotent (running twice == once).
7. `RedactJson` on `{"a":"x","Token":"abc"}` → `{"a":"x","Token":"[REDACTED]"}`.
8. `RedactJson` on nested JSON `{"outer":{"AzureBearerToken":"x"}}` → token redacted.
9. `RedactJson` on JSON with non-string values (numbers, nulls) → untouched.
10. `RedactJson` on malformed input → input returned (best-effort, no throw).

## Out of scope

- Wiring the redactor into existing log sites (S-004).
- A type-system marker (`[Sensitive]` attribute) — deliberately deferred; configuration-driven keeps the property-bag string model intact.

## Adversarial review

Single reviewer (Sonnet, security focus) on this diff after tests pass.
