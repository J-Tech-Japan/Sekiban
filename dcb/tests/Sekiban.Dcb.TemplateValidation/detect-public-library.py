#!/usr/bin/env python3
"""Read-only applicability check; only explicit 404/version absence means absent."""
import json
import os
from pathlib import Path
import re
import subprocess
import urllib.error
import urllib.request

root = Path(os.environ['GITHUB_WORKSPACE'])
version = subprocess.check_output(['bash', str(root/'dcb/tests/Sekiban.Dcb.TemplateValidation/read-template-version.sh'), '--repo-root', str(root)], text=True).strip()
lookup = subprocess.run(['gh', 'api', '--method', 'GET', f'repos/J-Tech-Japan/Sekiban/git/ref/tags/dcb-v{version}'], text=True, capture_output=True)
if lookup.returncode:
    if not re.search(r'\bHTTP 404\b', lookup.stderr):
        raise RuntimeError(lookup.stderr)
    public = False
else:
    reference = json.loads(lookup.stdout)
    assert reference['ref'] == f'refs/tags/dcb-v{version}'
    assert reference['object']['type'] in ('tag', 'commit')
    assert re.fullmatch(r'[0-9a-fA-F]{40}', reference['object']['sha'])
    public = True
    packages = re.findall(r'"(Sekiban\.Dcb[^\"]+)"', (root/'dcb/tests/Sekiban.Dcb.TemplateValidation/PackageManifest.cs').read_text())
    assert len(packages) == 26 and len(set(packages)) == 26
    for package in packages:
        try:
            with urllib.request.urlopen(f'https://api.nuget.org/v3-flatcontainer/{package.lower()}/index.json', timeout=20) as response:
                versions = json.load(response)['versions']
            assert isinstance(versions, list) and all(isinstance(v, str) for v in versions)
            if version not in versions:
                public = False
        except urllib.error.HTTPError as error:
            if error.code != 404:
                raise
            public = False
with open(os.environ['GITHUB_OUTPUT'], 'a') as output:
    output.write(f'applicable={str(public).lower()}\n')
if not public:
    print(f'::notice::Library {version} is not fully public; published-library template consumer is skipped.')
