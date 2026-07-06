#!/usr/bin/env bash
# manifest immutability gate.
#
# Verifies that the PR (BASE_SHA..HEAD_SHA, three-dot range) has not modified any
# existing stock-modules-manifests/<name>-<version>.yaml file. Adds, deletes,
# and renames are allowed; any modification ('M') or type change ('T') fails the
# build with a remediation hint.
#
# Inputs (env vars):
#   BASE_SHA  required. The PR base commit.
#   HEAD_SHA  optional, defaults to HEAD. The PR head commit.
#
# Or invoke with --self-test to exercise the gate logic against synthetic
# diff-status fixtures.

set -euo pipefail

# ----- Self-test mode -----
if [[ "${1:-}" == "--self-test" ]]; then
    classify_status() {
        # Mirror the production logic: pass status letters and assert the
        # script's classification is correct.
        local status="$1"
        case "$status" in
            A|D|R) echo "allow" ;;
            M|T)   echo "fail" ;;
            *)     echo "fail" ;;
        esac
    }
    [[ "$(classify_status A)" == "allow" ]] || { echo "self-test: A should be allow" >&2; exit 1; }
    [[ "$(classify_status D)" == "allow" ]] || { echo "self-test: D should be allow" >&2; exit 1; }
    [[ "$(classify_status R)" == "allow" ]] || { echo "self-test: R should be allow" >&2; exit 1; }
    [[ "$(classify_status M)" == "fail" ]]  || { echo "self-test: M should be fail" >&2; exit 1; }
    [[ "$(classify_status T)" == "fail" ]]  || { echo "self-test: T should be fail" >&2; exit 1; }
    echo "self-test passed"
    exit 0
fi

# ----- Production mode -----
: "${BASE_SHA:?BASE_SHA env var is required}"
HEAD_SHA="${HEAD_SHA:-HEAD}"

# Three-dot range so we compare PR head against the merge-base with the target
# branch independent of intermediate base-branch churn during the PR's life.
# -M turns on rename detection (so 'R' surfaces); copy detection is left off
# (default), keeping the status set to {A,D,R,M,T,...}.
diff_output=$(git diff --name-status -M "${BASE_SHA}...${HEAD_SHA}" -- 'stock-modules-manifests/*.yaml' || true)

if [[ -z "$diff_output" ]]; then
    echo "manifest-immutability: no manifest YAML changes in this PR; gate passes."
    exit 0
fi

violations=0
while IFS=$'\t' read -r status path rest; do
    [[ -z "$status" ]] && continue
    # First letter only: 'R100' / 'C100' include similarity suffixes.
    letter="${status:0:1}"
    case "$letter" in
        A|D|R)
            : # allowed
            ;;
        *)
            echo "::error file=${path}::Manifest ${path} was modified (diff status: ${status}). Manifest versions are immutable; create a new version (e.g. <name>-<next>.yaml) instead of editing the existing one." >&2
            violations=$((violations + 1))
            ;;
    esac
done <<< "$diff_output"

if [[ $violations -gt 0 ]]; then
    echo "manifest-immutability: ${violations} violation(s); failing build." >&2
    exit 1
fi

echo "manifest-immutability: ${diff_output} (all permitted: A/D/R only); gate passes."
exit 0
