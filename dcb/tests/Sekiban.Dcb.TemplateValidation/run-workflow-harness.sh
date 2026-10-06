#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

if ! python3 -c 'import yaml' >/dev/null 2>&1; then
  # Ubuntu 24.04 marks system Python externally managed; install in an isolated venv.
  yaml_env="$(mktemp -d "${TMPDIR:-/tmp}/sek-workflow-yaml.XXXXXX")"
  trap 'rm -rf "$yaml_env"' EXIT
  python3 -m venv "$yaml_env"
  "$yaml_env/bin/python" -m pip install -q PyYAML==6.0.3
  "$yaml_env/bin/python" "$script_dir/run-workflow-harness.py" "$@"
  exit
fi

exec python3 "$script_dir/run-workflow-harness.py" "$@"
