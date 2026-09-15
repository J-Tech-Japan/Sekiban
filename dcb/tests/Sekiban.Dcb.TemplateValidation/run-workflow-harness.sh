#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if ! python3 -c 'import yaml' >/dev/null 2>&1; then
  python3 -m pip install --user -q PyYAML
fi

exec python3 "$script_dir/run-workflow-harness.py" "$@"
