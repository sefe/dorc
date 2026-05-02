# .NET Coding Standard

## Language
All .NET code must be written in C#.

## Naming
- Classes and namespaces must group together cohesive, related functionality, and the name must reflect that grouping. A well-named class tells you what it is; a badly-named class tells you where to dump things.
- **Avoid grab-bag names** that invite dumping-ground classes: `Manager`, `Helper`, `Service`, `Util(ity)(s)`, `Common`, `Misc`, `Shared`, team names, and similar. This list is illustrative, not exhaustive — the rule is the principle (cohesive naming), not the blacklist. If you catch yourself reaching for one of these because you can't summarise what the class does, that's the signal the class itself is wrong, not just the name.
- Namespace pattern: `Dorc.[Component].[Feature](.[Sub-feature])`.
- Assembly pattern: `Dorc.<Component>.dll`.

## .NET Versions
New applications must use the current LTS release or later.

## Restricted Features
Not permitted without dev manager approval: C# language extensions (`language-ext`), functional programming libraries.

---

# Development Process

## Principles
- Deliver quality over speed. Favour low-risk, low-complexity solutions.
- SOLID, OOP, test-driven development. Cognitive complexity is the enemy.
- All artifacts persisted to `docs/{feature}/` — not session-local.

## Planning (HLPS → IS → JIT Specs)
1. **HLPS** (High-Level Problem Statement) — `docs/<topic-slug>/HLPS-<slug>.md`. Defines problem, scope, constraints, success criteria. Must include an Unknowns Register. **Blocking unknowns halt progress.**
2. **IS** (Implementation Sequence) — `docs/<topic-slug>/IS-<slug>.md`. Ordered, atomic steps (S-001, S-002, …). Strategic roadmap — no exact method signatures or line numbers.
3. **JIT Specs** — `docs/<topic-slug>/SPEC-S-00X-<title>.md`. Requirements for the next step. Pseudocode / plain-language, not copy-pasteable code.

Each document progresses: `DRAFT` → `IN REVIEW` → `REVISION` → `APPROVED`. Only the adversarial panel can approve.

Reference pattern in this repo: `docs/monitor-robustness/` (HLPS + IS + SPEC-S-001..004).

## Checkpoints (stop and wait for user approval)
- After HLPS approved
- After IS approved
- Before executing each step (unless auto-pilot enabled)

## Delivery Loop
For each IS step: Synchronize → Lookahead specs → Pre-audit checklist → Execute (test-first) → Adversarial Review → Impact Assessment → Recurse.

## Adversarial Review Quality Gate
Every task must pass a panel of 2–4 sub-agents (diverse model architectures). Each reviews independently. Triage findings as Accept / Downgrade / Defer / Reject. Max 3 rounds — escalate unresolved items to the user.

### Review Scope
- **Plans**: Evaluate clarity, ordering, completeness — not syntactic correctness.
- **Code**: Evaluate the diff only — pre-existing issues are out of scope.
- **Severity**: Defect = HIGH/CRITICAL; Ambiguity = MEDIUM max; Improvement = LOW or defer.

## Tone
Professional, analytical, quality-focused. Flag contradictions to plan or architecture. Use Markdown.
