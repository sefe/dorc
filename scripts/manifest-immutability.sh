#!/usr/bin/env bash
# Compare complete release identities and payloads, not diff status/path names.
# Requires Python 3 and scripts/manifest-requirements.txt.
set -euo pipefail
script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
if [[ "${1:-}" == "--self-test" ]]; then
    exec "${PYTHON:-python3}" "${script_dir}/test_manifest_immutability.py"
fi
: "${BASE_SHA:?BASE_SHA env var is required}"
exec "${PYTHON:-python3}" "${script_dir}/manifest_immutability.py" "$BASE_SHA" "${HEAD_SHA:-HEAD}"
