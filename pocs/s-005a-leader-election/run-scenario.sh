#!/usr/bin/env bash
# S-005a POC driver — orchestrates a leader-election failure-injection
# scenario against the local compose stack (Kafka + Karapace). Builds
# the LockCandidate once, then spawns / kills multiple instances while
# producing synthetic state-transition events.
#
# Outputs (all under evidence/<timestamp>/):
#   cand-<id>.log              — stdout of each candidate
#   state.jsonl                — applied state transitions (append-only)
#   handler-invocations.jsonl  — every handler call (including idempotent no-ops)
#   summary.md                 — post-run analysis
#
# Usage: ./run-scenario.sh [timestamp-suffix]

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

SUFFIX="${1:-$(date +%Y%m%d-%H%M%S)}"
EVIDENCE_DIR="$SCRIPT_DIR/evidence/$SUFFIX"
mkdir -p "$EVIDENCE_DIR"

STATE_FILE="$EVIDENCE_DIR/state.jsonl"
HANDLER_FILE="$EVIDENCE_DIR/handler-invocations.jsonl"
: > "$STATE_FILE"
: > "$HANDLER_FILE"

BOOTSTRAP="${KAFKA_BOOTSTRAP:-localhost:9092}"
TOPIC="dorc.poc.s005a.requests"
GROUP_ID="dorc.poc.s005a.monitors.$SUFFIX"

echo "=== S-005a POC scenario run ==="
echo "  evidence dir : $EVIDENCE_DIR"
echo "  bootstrap    : $BOOTSTRAP"
echo "  topic        : $TOPIC"
echo "  group id     : $GROUP_ID"

# Build once
echo "[1/8] Building LockCandidate..."
dotnet build "$SCRIPT_DIR/LockCandidate/LockCandidate.csproj" -c Debug --nologo -v quiet >/dev/null

CAND_DLL="$SCRIPT_DIR/LockCandidate/bin/Debug/net8.0/LockCandidate.dll"

# Create the topic with 12 partitions (idempotent: ignores AlreadyExists)
echo "[2/8] Creating topic (ignore 'already exists')..."
kafka_container="${KAFKA_CONTAINER:-dorc-kafka}"
MSYS_NO_PATHCONV=1 podman exec "$kafka_container" /opt/kafka/bin/kafka-topics.sh \
    --bootstrap-server localhost:29092 \
    --create --if-not-exists \
    --topic "$TOPIC" --partitions 12 --replication-factor 1 \
    2>&1 | tail -3

declare -A CAND_PIDS=()

start_candidate() {
    local id="$1"
    local pre_commit_delay_ms="${2:-0}"
    local logfile="$EVIDENCE_DIR/cand-$id.log"
    echo "  → start cand-$id (pre-commit-delay=${pre_commit_delay_ms}ms) → $logfile"
    INSTANCE_ID="cand-$id" \
    KAFKA_BOOTSTRAP="$BOOTSTRAP" \
    TOPIC="$TOPIC" \
    GROUP_ID="$GROUP_ID" \
    STATE_FILE="$STATE_FILE" \
    HANDLER_INVOCATIONS_FILE="$HANDLER_FILE" \
    FORCE_PRE_COMMIT_DELAY_MS="$pre_commit_delay_ms" \
        dotnet "$CAND_DLL" > "$logfile" 2>&1 &
    CAND_PIDS[$id]=$!
}

kill_candidate() {
    local id="$1"
    local pid="${CAND_PIDS[$id]:-}"
    if [[ -n "$pid" ]]; then
        echo "  → SIGKILL cand-$id (pid=$pid)"
        kill -9 "$pid" 2>/dev/null || true
        unset CAND_PIDS[$id]
    fi
}

produce_events() {
    local count="$1"
    local start_version="$2"
    local request_ids=("${@:3}")
    local total=0
    for rid in "${request_ids[@]}"; do
        for ((i=0; i<count; i++)); do
            local v=$((start_version + i))
            echo "${rid}|version:${v}|state:Running-${v}" | \
                MSYS_NO_PATHCONV=1 podman exec -i "$kafka_container" /opt/kafka/bin/kafka-console-producer.sh \
                    --bootstrap-server localhost:29092 \
                    --topic "$TOPIC" \
                    --property "parse.key=true" --property "key.separator=|" >/dev/null 2>&1
            total=$((total+1))
        done
    done
    echo "  → produced $total events across ${#request_ids[@]} keys (versions ${start_version}..$((start_version+count-1)))"
}

# Scenario:
#   [3] start cand-1 alone; produce 5 events/key × 4 keys — 20 events
#   [4] start cand-2 (rebalance 1); produce 5 more/key — 20 events
#   [5] SIGKILL cand-1 (rebalance 2); produce 5 more/key — 20 events
#   [6] start cand-3 + restart cand-1 (rebalances 3+4); produce 5/key — 20 events
#   [7] SIGKILL cand-3 (rebalance 5); wait for settle
#   [8] stop all, analyze
#
# Total events produced: 80 across 4 RequestIds × versions 0..19.
# Terminal state should have exactly 80 accepted transitions (the max
# version per key); handler invocations may exceed 80 (extras = idempotent
# no-ops from rebalance duplication).

REQUEST_IDS=("req-1001" "req-1002" "req-1003" "req-1004")

echo "[3/8] Starting cand-1 (alone)..."
start_candidate "1" "2000"
sleep 8   # let it take all partitions

echo "[4/8] Producing batch 1 (versions 0..4)..."
produce_events 5 0 "${REQUEST_IDS[@]}"
sleep 6

echo "[5/8] Starting cand-2 (rebalance 1)..."
start_candidate "2" "2000"
sleep 8
echo "       Producing batch 2 (versions 5..9)..."
produce_events 5 5 "${REQUEST_IDS[@]}"
sleep 6

echo "[6/8] SIGKILL cand-1 (rebalance 2)..."
kill_candidate "1"
sleep 12   # session timeout is 10s; give time for rebalance
echo "       Producing batch 3 (versions 10..14)..."
produce_events 5 10 "${REQUEST_IDS[@]}"
sleep 6

echo "[7/8] Start cand-3 + restart cand-1 (rebalances 3 & 4)..."
start_candidate "3" "2000"
sleep 2
start_candidate "1" "2000"   # NOTE: new process, will rejoin the group
sleep 10
echo "       Producing batch 4 (versions 15..19)..."
produce_events 5 15 "${REQUEST_IDS[@]}"
sleep 6

echo "[7b] SIGKILL cand-3 (rebalance 5)..."
kill_candidate "3"
sleep 12

echo "[8/8] Shutting down remaining candidates..."
for id in "${!CAND_PIDS[@]}"; do
    pid="${CAND_PIDS[$id]}"
    echo "  → SIGTERM cand-$id (pid=$pid)"
    kill -TERM "$pid" 2>/dev/null || true
done
sleep 3
for id in "${!CAND_PIDS[@]}"; do
    pid="${CAND_PIDS[$id]}"
    kill -9 "$pid" 2>/dev/null || true
done

echo ""
echo "=== Analysis ==="
TOTAL_STATE=$(wc -l < "$STATE_FILE")
TOTAL_HANDLER=$(wc -l < "$HANDLER_FILE")
ACCEPTED=$(grep -c '"outcome":"accepted"' "$HANDLER_FILE" || true)
NOOPS=$(grep -c '"outcome":"idempotent-noop"' "$HANDLER_FILE" || true)

echo "  state.jsonl rows (accepted transitions) : $TOTAL_STATE"
echo "  handler invocations (total)             : $TOTAL_HANDLER"
echo "  invocations — accepted                  : $ACCEPTED"
echo "  invocations — idempotent no-ops         : $NOOPS"

cat > "$EVIDENCE_DIR/summary.md" <<EOF
# S-005a POC run — $SUFFIX

- **Scenario:** 3 candidates, rolling restarts producing ≥5 rebalances.
- **Topic:** $TOPIC (12 partitions, RF=1 on local compose).
- **Group ID:** $GROUP_ID
- **Pre-commit delay:** 2000 ms per candidate (forces commit-window overlap with rebalances).
- **Events produced:** 4 RequestIds × 20 versions = 80 total.

## Terminal counts

| Metric | Value |
|---|---|
| state.jsonl rows (accepted transitions) | $TOTAL_STATE |
| handler invocations (total) | $TOTAL_HANDLER |
| invocations — accepted | $ACCEPTED |
| invocations — idempotent no-ops | $NOOPS |

## §5.1 Safety Property — three-outcome interpretation

- Accepted count **must** equal 80 (every (RequestId, version) applied exactly once → no lost events).
- Idempotent-noop count **may** be > 0 — each no-op is a duplicate handler
  invocation that was coalesced to a no-op DB write (idempotency working).
- **Failure mode:** accepted count > 80 (duplicate rows in state.jsonl for same (RequestId, version)).

## Re-run

\`\`\`bash
./run-scenario.sh [timestamp-suffix]
\`\`\`

EOF

echo ""
echo "=== Summary ==="
cat "$EVIDENCE_DIR/summary.md"
echo ""
echo "Evidence written to: $EVIDENCE_DIR"
