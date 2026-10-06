#!/usr/bin/env bash
set -euo pipefail
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(cd "$script_dir/../../.." && pwd)"
if (( $# )); then
  [[ $# == 2 && "$1" == --repo-root ]] || { echo 'Usage: read-template-version.sh [--repo-root <path>]' >&2; exit 2; }
  repo_root="$2"
fi
python3 - "$repo_root" <<'PY'
import sys, re
from pathlib import Path
from xml.etree import ElementTree as ET
root = Path(sys.argv[1]) / 'templates/Sekiban.Dcb.Templates/content'
names = ['Sekiban.Dcb.Orleans', 'Sekiban.Dcb.Orleans.WithoutResult', 'Sekiban.Dcb.Orleans.WithoutResult.Aws', 'Sekiban.Dcb.Orleans.Decider', 'Sekiban.Dcb.Orleans.Decider.Aws']
versions = []
for name in names:
    values = [e.text.strip() for e in ET.parse(root / name / 'SekibanDcbTemplateVersion.props').iter('SekibanDcbVersion') if e.text]
    if len(values) != 1 or not re.fullmatch(r'(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)', values[0]):
        sys.exit(f'Invalid version authority: {name}')
    versions += values
if len(set(versions)) != 1:
    sys.exit(f'The five template version authorities disagree: {versions}')
print(versions[0])
PY
