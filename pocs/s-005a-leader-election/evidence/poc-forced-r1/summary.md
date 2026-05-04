# S-005a forced-duplicate POC run — poc-forced-r1

Deliberately forces the duplicate-handler-invocation path that option
(i)'s idempotency must absorb. cand-A is killed mid-pre-commit-delay
after writing state but before committing offset; cand-B takes over and
re-reads the uncommitted offsets.

## Terminal counts

| Metric | Value |
|---|---|
| state.jsonl rows (accepted transitions) | 6 |
| handler invocations (total) | 7 |
| invocations — accepted | 6 |
| invocations — idempotent no-ops | 1 |

## §5.1 Safety Property verdict: **PASS**

- 6 events produced.
- Expected `accepted = 6` (one per unique (RequestId, version)).
- Expected `idempotent-noop > 0` (cand-B re-invokes the handler for
  the offsets cand-A didn't commit; idempotency absorbs them as no-ops).
- No duplicate (RequestId, version) rows in state.jsonl.
