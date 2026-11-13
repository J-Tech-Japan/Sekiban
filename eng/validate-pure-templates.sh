#!/usr/bin/env bash
# Static Pure template gate; Python 3 standard library only, no network or dotnet.
set -euo pipefail
script_path="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)/$(basename -- "${BASH_SOURCE[0]}")"
repo_root="$(cd -- "$(dirname -- "$script_path")/.." && pwd)"
command -v python3 >/dev/null 2>&1 || { printf 'ERROR: python3 is required\n' >&2; exit 1; }
exec python3 -I - "$repo_root" "$script_path" "$@" <<'PY'
"""Validate Pure template versions, frameworks, stale text, APIs and policy banners."""
import argparse
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import xml.etree.ElementTree as ET

TREE = Path('templates/Sekiban.Pure.Templates')
POLICY = Path('eng/pure-maintenance-mode/canonical-block.md')
ORLEANS = Path('content/Sekiban.Orleans.Aspire')
DAPR = Path('content/Sekiban.Dapr.Aspire')
PACKAGE = Path('Sekiban.Pure.Templates.csproj')
METADATA = [t / '.template.config/template.json' for t in (ORLEANS, DAPR)]
BANNERS = [Path('README.md'), DAPR / 'README.md'] + [
    t / 'Sekiban.README_Pure_For_LLM.md' for t in (ORLEANS, DAPR)]
EXPECTED = METADATA + [ORLEANS / 'OrleansSekiban.sln', DAPR / 'DaprSekiban.sln', Path('README.md')]
START = b'<!-- SEKIBAN_PURE_POLICY_START -->'
END = b'<!-- SEKIBAN_PURE_POLICY_END -->'
STALE = [r'net9\.0', r'dotnet/(sdk|aspnet):9\.', r'\.NET 9', r'DOTNET_VERSION: 9',
         r'DOTNETCORE\|9', r'v9\.0', r'1\.0\.1-preview']
OBSOLETE = ['AddKeyedAzureTableClient(', 'AddKeyedAzureBlobClient(',
            'AddKeyedAzureQueueClient(', 'ConfigureTableServiceClient(']


def require(condition, reason):
    if not condition:
        raise ValueError(reason)


def skipped_dir(parent, name):
    return name in ('bin', 'obj') or (parent.name == 'wwwroot' and name == 'lib')


def walk_error(error):
    raise error


def files(tree):
    require(tree.is_dir(), f'missing template tree: {tree}')
    for parent, dirs, names in os.walk(tree, onerror=walk_error):
        parent = Path(parent)
        dirs[:] = sorted(d for d in dirs if not skipped_dir(parent, d))
        for name in dirs + sorted(names):
            require(not (parent / name).is_symlink(), f'symlink is not supported: {parent / name}')
        for name in sorted(names):
            path = parent / name
            require(path.is_file(), f'not a regular file: {path}')
            yield path


def cut_banners(data, canonical, relative):
    """Compare raw bytes, preserving line endings; cut only complete canonical regions."""
    outside = []
    region = None
    count = 0
    for line in data.splitlines(keepends=True):
        marker = line.removesuffix(b'\n').removesuffix(b'\r')
        if marker == START:
            require(region is None, f'{relative}: second START before END')
            region = []
        if marker == END:
            require(region is not None, f'{relative}: END without START')
        if region is None:
            outside.append(line)
        else:
            region.append(line)
        if marker == END:
            block = b''.join(region).removesuffix(b'\n')
            require(block == canonical, f'{relative}: banner differs from canonical-block.md')
            count += 1
            region = None
    require(region is None, f'{relative}: START without END')
    if relative in BANNERS:
        require(count == 1, f'{relative}: expected exactly one policy banner, found {count}')
    return b''.join(outside).decode('utf-8'), count


def fields(project, tag):
    return [element for element in project.iter() if element.tag.rsplit('}', 1)[-1] == tag]


def validate(root):
    tree = root / TREE
    for relative in EXPECTED + [PACKAGE] + BANNERS:
        require((tree / relative).is_file(), f'missing expected file: {relative}')
    canonical = (root / POLICY).read_bytes().removesuffix(b'\n')
    require(canonical.startswith(START + b'\n') and canonical.endswith(END), 'invalid canonical block')
    texts = {}
    skipped = []
    banner_count = 0
    for path in files(tree):
        relative = path.relative_to(tree)
        if path.name.endswith('.png'):
            skipped.append(relative)
            continue
        data = path.read_bytes()
        try:
            text = data.decode('utf-8')
        except UnicodeDecodeError as error:
            raise ValueError(f'{relative}: invalid UTF-8') from error
        scanned, count = cut_banners(data, canonical, relative)
        banner_count += count
        for pattern in STALE:
            require(not re.search(pattern, scanned), f'{relative}: stale pattern {pattern}')
        if path.suffix == '.cs':
            for call in OBSOLETE:
                require(call not in text, f'{relative}: obsolete call {call}')
        texts[relative] = text

    project = ET.fromstring(texts[PACKAGE])
    versions = fields(project, 'Version')
    package_versions = fields(project, 'PackageVersion')
    require(len(versions) == 1, 'package project must have exactly one Version element')
    require(len(package_versions) == 1, 'package project must have exactly one PackageVersion element')
    version = package_versions[0].text
    require(bool(version) and version.strip() == version, 'PackageVersion must be nonempty and exact')
    require(versions[0].text == version, 'Version differs from PackageVersion')
    references = {ORLEANS: 0, DAPR: 0}
    projects = [p for p in texts if p == PACKAGE or (p.suffix == '.csproj' and p.parts[0] == 'content')]
    for relative in projects:
        project = ET.fromstring(texts[relative])
        frameworks = fields(project, 'TargetFramework')
        require(not fields(project, 'TargetFrameworks'), f'{relative}: TargetFrameworks is forbidden')
        require(bool(frameworks) and all(f.text == 'net10.0' for f in frameworks),
                f'{relative}: TargetFramework must be net10.0')
        if relative == PACKAGE:
            continue
        for reference in fields(project, 'PackageReference'):
            if not reference.get('Include', '').startswith('Sekiban.Pure'):
                continue
            values = ([reference.attrib['Version']] if 'Version' in reference.attrib else []) + [
                v.text for v in fields(reference, 'Version')]
            require(values == [version], f'{relative}: Sekiban.Pure reference version must equal {version}')
            for template in references:
                if relative.is_relative_to(template):
                    references[template] += 1
    for template, count in references.items():
        require(count > 0, f'{template}: no Sekiban.Pure references')
    metadata = {}
    for relative in METADATA:
        metadata[relative] = json.loads(texts[relative])
        require(isinstance(metadata[relative], dict), f'{relative}: template.json must be an object')
    framework = metadata[METADATA[1]].get('symbols', {}).get('Framework', {})
    require(isinstance(framework, dict), 'Dapr Framework must be an object')
    choices = framework.get('choices')
    require(isinstance(choices, list) and len(choices) == 1 and isinstance(choices[0], dict)
            and choices[0].get('choice') == 'net10.0', 'Dapr Framework must have exactly one choice net10.0')
    require(framework.get('defaultValue') == 'net10.0' and framework.get('replaces') == 'net10.0',
            'Dapr Framework defaultValue and replaces must be net10.0')
    print(f'V1-V8 checked: version {version}; {len(projects)} projects; '
          f'{sum(references.values())} Pure references; 2 template.json; 5 expected files; '
          f'{len(texts)} UTF-8 files; {banner_count} canonical banners; stale text and obsolete APIs absent.')
    print('Skipped directories only: wwwroot/lib, bin, obj')
    for relative in skipped:
        print(f'Skipped PNG: {relative}')
    print(f'Sekiban.Pure template validation passed ({len(skipped)} PNG files skipped).')


def replace(path, old, new):
    data = path.read_bytes()
    require(old in data, f'self-test mutation text missing before: {path}')
    changed = data.replace(old, new)
    require(old not in changed and changed != data, f'self-test mutation text remains after: {path}')
    path.write_bytes(changed)
    require(path.read_bytes() == changed, f'self-test mutation was not written: {path}')


def run_cli(script, root, expected, reason):
    result = subprocess.run([str(script), '--repo-root', str(root)], capture_output=True, text=True)
    require(result.returncode == expected and reason in result.stdout + result.stderr,
            f'self-test expected status {expected} and {reason!r}; got {result.returncode}:\n'
            + result.stdout + result.stderr)
    return result


def self_test(root, script):
    with tempfile.TemporaryDirectory(prefix='sekiban-pure-templates.') as temporary:
        base = Path(temporary) / 'base'
        def ignore(parent, names):
            return ['lib'] if Path(parent).name == 'wwwroot' and 'lib' in names else []
        shutil.copytree(root / TREE, base / TREE, ignore=ignore)
        (base / POLICY).parent.mkdir(parents=True)
        shutil.copyfile(root / POLICY, base / POLICY)
        favicons = [t / f'{name}.Web/wwwroot/favicon.png' for t, name in
                    ((ORLEANS, 'OrleansSekiban'), (DAPR, 'DaprSekiban'))]
        for icon in favicons:
            require((base / TREE / icon).is_file(), f'fixture missing favicon: {icon}')
        passed = run_cli(script, base, 0, 'validation passed')
        for icon in favicons:
            require(f'Skipped PNG: {icon}' in passed.stdout, f'favicon was not skipped: {icon}')
        print('pass case 1: unmodified fixture, 4 canonical banners and both favicon.png files present and skipped')
        png_case = Path(temporary) / 'png'
        shutil.copytree(base, png_case)
        icon = png_case / TREE / favicons[0]
        replace(icon, icon.read_bytes(), b'\xff\xfe')
        run_cli(script, png_case, 0, f'Skipped PNG: {favicons[0]}')
        print('pass case 2: non-UTF-8 PNG skipped')

        api = ORLEANS / 'OrleansSekiban.ApiService'
        project = api / 'OrleansSekiban.ApiService.csproj'
        program = api / 'Program.cs'
        docker = api / 'Dockerfile'
        bicep = ORLEANS / 'infrastructure/azure_appservice/7.backend/2.app-service-create.bicep'
        how = ORLEANS / 'infrastructure/azure_appservice/how.md'
        banner = (base / POLICY).read_bytes().removesuffix(b'\n')
        # (case name, file, exact old bytes, replacement bytes, diagnostic substring).
        cases = [
            ('version-differs', PACKAGE, b'<Version>10.0.0</Version>', b'<Version>10.0.1</Version>', 'Version differs from PackageVersion'),
            ('second-version', PACKAGE, b'<Version>10.0.0</Version>', b'<Version>10.0.1</Version><Version>10.0.2</Version>', 'exactly one Version'),
            ('preview-reference', ORLEANS / 'OrleansSekiban.Domain/OrleansSekiban.Domain.csproj', b'Include="Sekiban.Pure" Version="10.0.0"', b'Include="Sekiban.Pure" Version="1.0.1-preview21"', r'stale pattern 1\.0\.1-preview'),
            ('orleans-reference', project, b'Include="Sekiban.Pure.Orleans" Version="10.0.0"', b'Include="Sekiban.Pure.Orleans" Version="10.0.1"', 'Sekiban.Pure reference version'),
            ('net9-project', project, b'<TargetFramework>net10.0</TargetFramework>', b'<TargetFramework>net9.0</TargetFramework>', r'stale pattern net9\.0'),
            ('net8-project', project, b'<TargetFramework>net10.0</TargetFramework>', b'<TargetFramework>net8.0</TargetFramework>', 'TargetFramework must be net10.0'),
            ('plural-frameworks', project, b'<TargetFramework>net10.0</TargetFramework>', b'<TargetFrameworks>net10.0</TargetFrameworks>', 'TargetFrameworks is forbidden'),
            ('net9-choice', METADATA[1], b'"choice": "net10.0"', b'"choice": "net9.0"', r'stale pattern net9\.0'),
            ('second-choice', METADATA[1], b'"choice": "net10.0",', b'"choice": "net10.0"}, {"choice": "net8.0",', 'exactly one choice net10.0'),
            ('old-docker-sdk', docker, b'dotnet/sdk:10.0', b'dotnet/sdk:9.0', r'stale pattern dotnet/(sdk|aspnet):9\.'),
            ('old-bicep', bicep, b'DOTNETCORE|10.0', b'DOTNETCORE|9.0', r'stale pattern DOTNETCORE\|9'),
            ('readme-old-sdk', Path('README.md'), b'## Installation', b'## Requires .NET 9.0 SDK', r'stale pattern \.NET 9'),
            ('readme-old-sdk-pure', Path('README.md'), b'## Installation', b'## Requires .NET 9.0 SDK, see Sekiban.Pure 9.x', r'stale pattern \.NET 9'),
            ('stale-after-end', Path('README.md'), END + b'\n\n', END + b'\nRequires .NET 9.0 SDK\n', r'stale pattern \.NET 9'),
            ('changed-banner-word', Path('README.md'), b'For new projects we recommend', b'For new projects we suggest', 'banner differs from canonical-block.md'),
            ('second-banner', Path('README.md'), END + b'\n\n\n', END + b'\n' + banner + b'\n\n', 'expected exactly one policy banner'),
            ('missing-end', Path('README.md'), END, b'<!-- removed END -->', 'START without END'),
            ('missing-banner', Path('README.md'), banner, b'Banner removed.', 'expected exactly one policy banner'),
            ('old-how', how, b'DOTNET_VERSION: 10.0.x', b'DOTNET_VERSION: 9.0.x', 'stale pattern DOTNET_VERSION: 9'),
            ('obsolete-api', program, b'AddKeyedAzureTableServiceClient(', b'AddKeyedAzureTableClient(', 'obsolete call AddKeyedAzureTableClient('),
            ('deleted-template-json', METADATA[0], None, None, 'missing expected file'),
            ('non-utf8-markdown', Path('README.md'), b'## Installation', b'\xff\xfe', 'invalid UTF-8'),
            ('nul-docker', docker, b'dotnet/sdk:10.0', b'dotnet/sdk:9.0\x00', r'stale pattern dotnet/(sdk|aspnet):9\.'),
        ]
        for name, relative, old, new, reason in cases:
            fixture = Path(temporary) / name
            shutil.copytree(base, fixture)
            path = fixture / TREE / relative
            if old is None:
                require(path.is_file() and path.read_bytes(), 'deleted fixture must exist and have content')
                path.unlink()
                require(not path.exists(), 'deleted fixture still exists')
            else:
                replace(path, old, new)
            result = run_cli(script, fixture, 1, reason)
            require('ERROR:' in result.stderr, 'self-test failure must print ERROR:')
            print(f'mutant killed: {name} (exit 1; reason: {reason}; before/after asserted)')
        print(f'Self-test passed: 2 pass cases; {len(cases)} mutants killed (exit 1 and reason asserted).')


class Parser(argparse.ArgumentParser):
    def error(self, message):
        self.print_usage(sys.stderr)
        self.exit(2, f'ERROR: {message}\n')


def main():
    parser = Parser(description=__doc__)
    parser.add_argument('--repo-root', type=Path, default=Path(sys.argv[1]))
    parser.add_argument('--self-test', action='store_true', help='run offline fixtures through the real command line')
    arguments = parser.parse_args(sys.argv[3:])
    try:
        root = arguments.repo_root.resolve()
        if arguments.self_test:
            self_test(root, Path(sys.argv[2]))
        else:
            validate(root)
    except (OSError, ValueError, ET.ParseError, KeyError, TypeError, AttributeError) as error:
        print(f'ERROR: {error}', file=sys.stderr)
        return 1
    return 0


sys.exit(main())
PY
