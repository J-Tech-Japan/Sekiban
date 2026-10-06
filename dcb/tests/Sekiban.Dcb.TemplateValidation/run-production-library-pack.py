#!/usr/bin/env python3
"""Run the release workflow's build/pack commands, without publication steps.

The named run blocks are read from packagesDcb.yml so production settings,
project order, SBOM and symbols cannot diverge in the two-runner CI packs.
"""
import argparse
import os
from pathlib import Path
import re
import shutil
import subprocess

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--repo-root', required=True, type=Path)
parser.add_argument('--output', required=True, type=Path)
parser.add_argument('--version', required=True)
args = parser.parse_args()
root = args.repo_root.resolve()
output = args.output.resolve()
if (root / 'out').exists() or output.exists():
    parser.error('Production pack requires absent out and output directories; use a clean checkout.')
workflow = (root / '.github/workflows/packagesDcb.yml').read_text()
names = ['Restore dependencies', 'Build with dotnet',
         'Build release-record and package validators', 'Pack NuGet packages']
commands = []
for name in names:
    match = re.search(r'^      - name: ' + re.escape(name) +
                      r'\n        run: (.*?)(?=\n      - |\Z)', workflow, re.M | re.S)
    if not match:
        parser.error(f'Missing production command block: {name}')
    block = match[1].rstrip()
    if block.startswith('|\n'):
        block = '\n'.join(line[10:] for line in block.splitlines()[1:])
    commands.append(block)
subprocess.run(['bash', str(root / 'dcb/tests/Sekiban.Dcb.TemplateValidation/validate-release-tags.sh'),
                '--check-package-manifest', '--repo-root', str(root)], cwd=root, check=True)
env = dict(os.environ, VERSION=args.version)
for name, command in zip(names, commands):
    print(f'Production pack: {name}', flush=True)
    subprocess.run(['bash', '-euo', 'pipefail', '-c', command], cwd=root, env=env, check=True)
packages = list((root / 'out').glob('*.nupkg'))
if len(packages) != 26:
    raise SystemExit(f'Expected 26 production packages, found {len(packages)}')
output.parent.mkdir(parents=True, exist_ok=True)
shutil.move(str(root / 'out'), output)
print(f'Production pack: 26 packages at {args.version} (SBOM and symbols as in tag workflow).')
