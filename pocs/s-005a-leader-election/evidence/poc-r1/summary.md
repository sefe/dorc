# S-005a POC run — poc-r1

- **Scenario:** 3 candidates, rolling restarts producing ≥5 rebalances.
- **Topic:** dorc.poc.s005a.requests (12 partitions, RF=1 on local compose).
- **Group ID:** dorc.poc.s005a.monitors.poc-r1
- **Pre-commit delay:** 2000 ms per candidate (forces commit-window overlap with rebalances).
- **Events produced:** 4 RequestIds × 20 versions = 80 total.

## Terminal counts

| Metric | Value |
|---|---|
| state.jsonl rows (accepted transitions) | 80 |
| handler invocations (total) | 80 |
| invocations — accepted | 80 |
| invocations — idempotent no-ops | 0 |

## §5.1 Safety Property — three-outcome interpretation

- Accepted count **must** equal 80 (every (RequestId, version) applied exactly once → no lost events).
- Idempotent-noop count **may** be > 0 — each no-op is a duplicate handler
  invocation that was coalesced to a no-op DB write (idempotency working).
- **Failure mode:** accepted count > 80 (duplicate rows in state.jsonl for same (RequestId, version)).

## Re-run

```bash
./run-scenario.sh [timestamp-suffix]
```

