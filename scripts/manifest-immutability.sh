#!/usr/bin/env bash
# manifest immutability gate.
#
# Verifies that the PR (BASE_SHA..HEAD_SHA, three-dot range) has not modified any
# existing stock-modules-manifests/<name>-<version>.yaml file.
#
# Policy (see docs/Terraform/MODULE-CONTRACT.md "Manifest immutability"):
#   - Adds ('A') and deletes ('D') are allowed.
#   - A pure rename ('R100', content 100% identical) is allowed - it is
#     equivalent to a delete + add pair, both of which are individually allowed.
#   - A rename with content changes ('R<sim>' below 100) is a modification of
#     the old path in disguise and fails the build, exactly like 'M'.
#   - Any modification ('M'), type change ('T'), or unrecognized status fails
#     the build with a remediation hint (fail closed).
#   - Any git failure fails the build (fail closed) - the gate never passes
#     because it could not compute the diff.
#
# Inputs (env vars):
#   BASE_SHA  required. The PR base commit.
#   HEAD_SHA  optional, defaults to HEAD. The PR head commit.
#
# Or invoke with --self-test to exercise the gate's classification logic
# against synthetic diff-status fixtures.

set -euo pipefail

# Classify a raw `git diff --name-status` status token (e.g. 'A', 'D', 'M',
# 'R100', 'R083') as "allow" or "fail". This single function is the gate's
# decision logic: both production mode and --self-test drive it.
classify_status() {
    local status="$1"
    case "$status" in
        A|D)
            echo "allow" ;;   # add / delete: permitted by policy
        R100)
            echo "allow" ;;   # pure rename == delete + add, both permitted
        *)
            echo "fail" ;;    # M, T, R<sim<100>, C*, unknown: fail closed
    esac
}

# ----- Self-test mode -----
if [[ "${1:-}" == "--self-test" ]]; then
    assert_classify() {
        local status="$1" expected="$2" actual
        actual="$(classify_status "$status")"
        [[ "$actual" == "$expected" ]] \
            || { echo "self-test: ${status} should be ${expected}, got ${actual}" >&2; exit 1; }
    }
    assert_classify A    allow
    assert_classify D    allow
    assert_classify R100 allow   # pure rename (content identical)
    assert_classify R099 fail    # rename + content edit
    assert_classify R050 fail    # rename + heavy content edit
    assert_classify R    fail    # bare 'R' without similarity: fail closed
    assert_classify M    fail
    assert_classify T    fail
    assert_classify C100 fail    # copy detection is off; fail closed if seen
    assert_classify X    fail    # unknown status: fail closed
    echo "self-test passed"
    exit 0
fi

# ----- Production mode -----
: "${BASE_SHA:?BASE_SHA env var is required}"
HEAD_SHA="${HEAD_SHA:-HEAD}"

# Three-dot range so we compare PR head against the merge-base with the target
# branch independent of intermediate base-branch churn during the PR's life.
# -M turns on rename detection (so 'R<similarity>' surfaces); copy detection
# is left off (default), keeping the status set to {A,D,R,M,T,...}.
#
# Fail CLOSED on git errors: an unreachable BASE_SHA, shallow history, or any
# other git failure must fail the build, not silently pass the gate.
diff_output=$(git diff --name-status -M "${BASE_SHA}...${HEAD_SHA}" -- 'stock-modules-manifests/*.yaml') || {
    git_status=$?
    echo "manifest-immutability: 'git diff ${BASE_SHA}...${HEAD_SHA}' failed (exit code ${git_status}); failing closed. Ensure both commits are fetched (fetch-depth: 0) and BASE_SHA is reachable." >&2
    exit 1
}

if [[ -z "$diff_output" ]]; then
    echo "manifest-immutability: no manifest YAML changes in this PR; gate passes."
    exit 0
fi

violations=0
# For A/D/M/T lines the fields are <status>\t<path>; for R/C lines they are
# <status>\t<old-path>\t<new-path>.
while IFS=$'\t' read -r status old_path new_path; do
    [[ -z "$status" ]] && continue
    if [[ "$(classify_status "$status")" == "allow" ]]; then
        continue
    fi
    if [[ "${status:0:1}" == "R" ]]; then
        echo "::error file=${old_path}::Manifest ${old_path} was renamed to ${new_path} with content changes (diff status: ${status}). Manifest versions are immutable; a rename must not change content (only R100 is allowed). Create a new version (e.g. <name>-<next>.yaml) instead of editing the existing one." >&2
    else
        echo "::error file=${old_path}::Manifest ${old_path} was modified (diff status: ${status}). Manifest versions are immutable; create a new version (e.g. <name>-<next>.yaml) instead of editing the existing one." >&2
    fi
    violations=$((violations + 1))
done <<< "$diff_output"

if [[ $violations -gt 0 ]]; then
    echo "manifest-immutability: ${violations} violation(s); failing build." >&2
    exit 1
fi

echo "manifest-immutability: ${diff_output} (all permitted: A/D/R100 only); gate passes."
exit 0
