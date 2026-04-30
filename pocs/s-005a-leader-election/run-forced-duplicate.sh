#!/usr/bin/env bash
# S-005a POC — forced-duplicate-invocation sub-scenario.
#
# Goal: prove option (i) idempotency handles the duplicate-invocation path
# that COULD occur when a consumer crashes between handler-complete and
# offset-commit. We force this by starting a single candidate with a
# long pre-commit delay, producing events, then SIGKILL'ing the candidate
# before it can commit. A fresh candidate takes over, re-reads the
# uncommitted offsets, re-invokes the handler, and idempotency should
# coalesce the second invocation to a no-op.
#
# Expected evidence (passes):
#   state.jsonl       : exactly N accepted rows (one per event)
#   handler file      : > N total invocations (extras = idempotent no-ops)
#   idempotent-noop   : > 0 (at least one forced duplicate absorbed)
#
# Failure mode:
#   accepted > N      : duplicate rows in state.jsonl → idempotency broken

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

SUFFIX="${1:-forced-dup-$(date +%Y%m%d-%H%M%S)}"
EVIDENCE_DIR="$SCRIPT_DIR/evidence/$SUFFIX"
mkdir -p "$EVIDENCE_DIR"

STATE_FILE="$EVIDENCE_DIR/state.jsonl"
HANDLER_FILE="$EVIDENCE_DIR/handler-invocations.jsonl"
: > "$STATE_FILE"
: > "$HANDLER_FILE"

BOOTSTRAP="${KAFKA_BOOTSTRAP:-localhost:9092}"
TOPIC="dorc.poc.s005a.forcedup.$SUFFIX"
GROUP_ID="dorc.poc.s005a.monitors.$SUFFIX"
kafka_container="${KAFKA_CONTAINER:-dorc-kafka}"

CAND_DLL="$SCRIPT_DIR/LockCandidate/bin/Debug/net8.0/LockCandidate.dll"

echo "=== S-005a forced-duplicate sub-scenario ==="
echo "  evidence dir : $EVIDENCE_DIR"
echo "  topic        : $TOPIC"
echo "  group id     : $GROUP_ID"

# Build if needed
if [[ ! -f "$CAND_DLL" ]]; then
    echo "[0] Building LockCandidate..."
    dotnet build "$SCRIPT_DIR/LockCandidate/LockCandidate.csproj" -c Debug --nologo -v quiet >/dev/null
fi

MSYS_NO_PATHCONV=1 podman exec "$kafka_container" /opt/kafka/bin/kafka-topics.sh \
    --bootstrap-server localhost:29092 \
    --create --if-not-exists \
    --topic "$TOPIC" --partitions 3 --replication-factor 1 2>&1 | tail -1

start_candidate() {
    local id="$1"
    local delay="$2"
    local logfile="$EVIDENCE_DIR/cand-$id.log"
    INSTANCE_ID="cand-$id" \
    KAFKA_BOOTSTRAP="$BOOTSTRAP" \
    TOPIC="$TOPIC" \
    GROUP_ID="$GROUP_ID" \
    STATE_FILE="$STATE_FILE" \
    HANDLER_INVOCATIONS_FILE="$HANDLER_FILE" \
    FORCE_PRE_COMMIT_DELAY_MS="$delay" \
        dotnet "$CAND_DLL" > "$logfile" 2>&1 &
    echo $!
}

# Scenario:
#   Phase 1: start cand-A with an 8-second pre-commit delay.
#   Phase 2: produce 6 events (2 per RequestId × 3 RequestIds).
#   Phase 3: wait ~2s — cand-A has consumed the events and is sleeping
#            in its pre-commit delay; offsets are NOT YET COMMITTED.
#   Phase 4: SIGKILL cand-A. Session timeout (10s) expires, rebalance.
#   Phase 5: cand-B takes over, re-reads the uncommitted offsets,
#            re-invokes the handler → idempotency kicks in for each
#            (RequestId, version) whose terminal state already matches.
#
# Because cand-A was killed mid-pre-commit-delay, it never wrote to the
# STATE file — so cand-B's invocations are the FIRST to land state.
# To force a genuine "duplicate handler invocation with no-op DB write"
# we need cand-A to have WRITTEN STATE but NOT COMMITTED OFFSET.
# Our ApplyIdempotently writes state inside the handler (before the
# pre-commit delay), so this works as designed: cand-A writes state
# then sleeps, gets killed, cand-B re-reads the same offset, re-invokes
# the handler, the state-file lookup finds the existing (rid,version)
# at ≥ requested version, returns "idempotent-noop".

echo "[1] Start cand-A with 8s pre-commit delay..."
PID_A=$(start_candidate "A" "8000")
sleep 8   # let cand-A form the group + take all partitions

echo "[2] Producing 6 events (3 keys × 2 versions)..."
for rid in "req-D-1" "req-D-2" "req-D-3"; do
    for v in 0 1; do
        echo "${rid}|version:${v}|state:Running-${v}" | \
            MSYS_NO_PATHCONV=1 podman exec -i "$kafka_container" /opt/kafka/bin/kafka-console-producer.sh \
                --bootstrap-server localhost:29092 --topic "$TOPIC" \
                --property "parse.key=true" --property "key.separator=|" >/dev/null 2>&1
    done
done

echo "[3] Waiting 3s so cand-A consumes events + enters pre-commit delay..."
sleep 3

echo "[4] SIGKILL cand-A (mid pre-commit-delay)..."
kill -9 "$PID_A" 2>/dev/null || true

echo "[5] Waiting 14s for session timeout + rebalance..."
sleep 14

echo "[6] Start cand-B (takes over, should re-read uncommitted offsets)..."
PID_B=$(start_candidate "B" "0")
sleep 15   # let cand-B consume + commit

echo "[7] Shutting down cand-B..."
kill -TERM "$PID_B" 2>/dev/null || true
sleep 2
kill -9 "$PID_B" 2>/dev/null || true

echo ""
echo "=== Analysis ==="

TOTAL_STATE=$(wc -l < "$STATE_FILE" | tr -d ' ')
TOTAL_HANDLER=$(wc -l < "$HANDLER_FILE" | tr -d ' ')
ACCEPTED=$(grep -c '"outcome":"accepted"' "$HANDLER_FILE" || true)
NOOPS=$(grep -c '"outcome":"idempotent-noop"' "$HANDLER_FILE" || true)

echo "  state.jsonl rows (accepted transitions) : $TOTAL_STATE"
echo "  handler invocations (total)             : $TOTAL_HANDLER"
echo "  invocations — accepted                  : $ACCEPTED"
echo "  invocations — idempotent no-ops         : $NOOPS"

# Per-request-per-version uniqueness check (the real Safety Property).
DUPES=$(jq -r '.requestId + "|" + (.version|tostring)' "$STATE_FILE" 2>/dev/null | sort | uniq -d || true)
if [[ -n "$DUPES" ]]; then
    echo "  !!! FAILURE: duplicate (requestId, version) rows in state.jsonl:"
    echo "$DUPES"
    VERDICT="FAIL"
else
    echo "  ✓ no duplicate (requestId, version) rows in state.jsonl"
    VERDICT="PASS"
fi

cat > "$EVIDENCE_DIR/summary.md" <<EOF
# S-005a forced-duplicate POC run — $SUFFIX

Deliberately forces the duplicate-handler-invocation path that option
(i)'s idempotency must absorb. cand-A is killed mid-pre-commit-delay
after writing state but before committing offset; cand-B takes over and
re-reads the uncommitted offsets.

## Terminal counts

| Metric | Value |
|---|---|
| state.jsonl rows (accepted transitions) | $TOTAL_STATE |
| handler invocations (total) | $TOTAL_HANDLER |
| invocations — accepted | $ACCEPTED |
| invocations — idempotent no-ops | $NOOPS |

## §5.1 Safety Property verdict: **$VERDICT**

- 6 events produced.
- Expected \`accepted = 6\` (one per unique (RequestId, version)).
- Expected \`idempotent-noop > 0\` (cand-B re-invokes the handler for
  the offsets cand-A didn't commit; idempotency absorbs them as no-ops).
- No duplicate (RequestId, version) rows in state.jsonl.
EOF

echo ""
cat "$EVIDENCE_DIR/summary.md"
echo ""
echo "Evidence: $EVIDENCE_DIR"
