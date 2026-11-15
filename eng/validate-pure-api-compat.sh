#!/usr/bin/env bash
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
exec python3 - "$repo_root" "$@" <<'PY'
"""Compare published 9.0.0 APIs using independent restored reference closures."""
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
import zipfile

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--self-test', action='store_true', help='also run all three negative controls')
parser.add_argument('--work-dir', type=Path, help='retain downloads, reference closures and logs here')
args = parser.parse_args(sys.argv[2:])
root = Path(sys.argv[1])
os.chdir(root)
os.environ.update(DOTNET_USE_POLLING_FILE_WATCHER='1', MSBUILDUSESERVER='0', MSBUILDDISABLENODEREUSE='1')
runtime = ['Sekiban.Pure' + suffix for suffix in (
    '', '.AspNetCore', '.CosmosDb', '.Dapr', '.NUnit', '.Orleans', '.Orleans.NUnit',
    '.Orleans.xUnit', '.Postgres', '.ReadModel', '.xUnit')]
generator = 'Sekiban.Pure.SourceGenerator'
owned_temp = None
if args.work_dir:
    work = args.work_dir.resolve()
    work.mkdir(parents=True, exist_ok=True)
else:
    owned_temp = tempfile.TemporaryDirectory(prefix='pure-apicompat-')
    work = Path(owned_temp.name)


def run(command, label, cwd=root, check=True):
    result = subprocess.run([str(x) for x in command], cwd=cwd, text=True,
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (work / (label + '.log')).write_text(result.stdout)
    if check and result.returncode:
        raise RuntimeError(f'{label} failed ({result.returncode}):\n{result.stdout}')
    return result


def download(package, destination, url=None):
    lower = package.lower()
    url = url or f'https://api.nuget.org/v3-flatcontainer/{lower}/9.0.0/{lower}.9.0.0.nupkg'
    run(['curl', '--fail', '--location', '--silent', '--show-error', '--connect-timeout', '20',
         '--max-time', '120', url, '--output', destination], 'download-' + package)
    if not destination.is_file() or not zipfile.is_zipfile(destination):
        raise RuntimeError(f'Missing or invalid baseline: {destination}')


def restore_baseline(package):
    directory = work / ('baseline-' + package)
    directory.mkdir(exist_ok=True)
    if package == generator:
        tfm = 'netstandard2.0'
        # The 9.0.0 generator project's compiler reference, also pinned in the new project.
        extra = '<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.14.0" />'
    else:
        tfm = 'net9.0'
        extra = '<FrameworkReference Include="Microsoft.AspNetCore.App" />'
    project = directory / 'Baseline.csproj'
    project.write_text(f'<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>{tfm}</TargetFramework>'
                       '<EnableDefaultCompileItems>false</EnableDefaultCompileItems></PropertyGroup><ItemGroup>'
                       f'<PackageReference Include="{package}" Version="9.0.0" />{extra}</ItemGroup></Project>')
    run(['dotnet', 'restore', project, '--source', 'https://api.nuget.org/v3/index.json',
         '--disable-build-servers'], 'restore-' + package)
    return directory / 'obj/project.assets.json', tfm


def references(assets_path, tfm, destination):
    assets = json.loads(assets_path.read_text())
    found = {}
    for key, lib in assets['targets'][tfm].items():
        for relative in lib.get('compile', {}):
            if not relative.endswith('.dll'):
                continue
            meta = assets['libraries'][key]
            if lib['type'] == 'project':
                project = (assets_path.parent.parent / meta['msbuildProject']).resolve()
                path = project.parent / 'bin/Release' / tfm / Path(relative).name
            else:
                candidates = [Path(folder) / meta['path'] / relative for folder in assets['packageFolders']]
                path = next((p for p in candidates if p.is_file()), None)
            if path is None or not path.is_file():
                raise RuntimeError(f'Missing reference: {key}: {relative} ({assets_path})')
            found[path.name] = path
    if tfm == 'netstandard2.0':
        # NETStandard.Library supplies its facade closure via build assets, not compile assets.
        meta = assets['libraries']['NETStandard.Library/2.0.3']
        directories = [Path(folder) / meta['path'] / 'build/netstandard2.0/ref'
                       for folder in assets['packageFolders']]
        directory = next((p for p in directories if (p / 'netstandard.dll').is_file()), None)
        if directory is None:
            raise RuntimeError(f'Missing netstandard2.0 reference assemblies: {assets_path}')
        found.update((p.name, p) for p in directory.glob('*.dll'))
    elif tfm.startswith('net'):
        major = tfm[3:].split('.')[0]
        for pack in ['Microsoft.NETCore.App.Ref', 'Microsoft.AspNetCore.App.Ref']:
            base = dotnet_root / 'packs' / pack
            versions = [p for p in base.glob(major + '.*') if '-' not in p.name]
            if not versions:
                raise RuntimeError(f'Missing {pack} for {tfm}')
            version = max(versions, key=lambda p: tuple(map(int, p.name.split('.'))))
            assemblies = list((version / 'ref' / tfm).glob('*.dll'))
            if not assemblies:
                raise RuntimeError(f'Missing reference assemblies: {version}/ref/{tfm}')
            found.update((p.name, p) for p in assemblies)
    if not found:
        raise RuntimeError(f'Empty reference closure: {assets_path} {tfm}')
    if destination.exists():
        shutil.rmtree(destination)
    destination.mkdir()
    for name, path in found.items():
        (destination / name).symlink_to(path.resolve())
    (work / (destination.name + '.json')).write_text(json.dumps({k: str(v) for k, v in found.items()}, indent=2))
    return destination


def compare(left, right, lref, rref, label, suppression):
    for assembly in (left, right):
        if not assembly.is_file():
            raise RuntimeError(f'Missing comparison assembly: {assembly}')
    command = [tool, '-l', left, '-r', right, '--lref', lref, '--rref', rref]
    if suppression is not None:
        command += ['--suppression-file', suppression]
    result = run(command, label, check=False)
    # Treat any loader warning or unexpected output as failure, even if the tool exits zero.
    clean = result.returncode == 0 and result.stdout.strip() == 'APICompat ran successfully without finding any breaking changes.'
    return clean, result.stdout


try:
    info = run(['dotnet', '--list-sdks'], 'sdks').stdout
    sdk_paths = re.findall(r'^\S+ \[(.+)\]$', info, re.M)
    if not sdk_paths:
        raise RuntimeError('Cannot locate dotnet targeting packs')
    dotnet_root = Path(sdk_paths[-1]).parent
    tool_dir = work / 'tool'
    if tool_dir.exists():
        shutil.rmtree(tool_dir)
    run(['dotnet', 'tool', 'install', '--tool-path', tool_dir, 'Microsoft.DotNet.ApiCompat.Tool',
         '--version', '11.0.100', '--add-source', 'https://api.nuget.org/v3/index.json'], 'install-tool')
    tool = tool_dir / 'apicompat'
    run([tool, '--help'], 'apicompat-help')
    suppressions = root / 'eng/pure-api-compat-suppressions.xml'
    baselines = {}
    left_refs = {}
    right_refs = {}
    for package in runtime + [generator]:
        archive = work / (package + '.9.0.0.nupkg')
        download(package, archive)
        extracted = work / ('package-' + package)
        extracted.mkdir(exist_ok=True)
        with zipfile.ZipFile(archive) as z:
            z.extractall(extracted)
        relative = f'analyzers/dotnet/cs/{package}.dll' if package == generator else f'lib/net9.0/{package}.dll'
        baselines[package] = extracted / relative
        if not baselines[package].is_file():
            raise RuntimeError(f'Missing baseline assembly: {baselines[package]}')
        assets, tfm = restore_baseline(package)
        left_refs[package] = references(assets, tfm, work / ('left-' + package))
        project = root / 'pure/src' / package / (package + '.csproj')
        run(['dotnet', 'build', project, '-c', 'Release', '--disable-build-servers', '-m:1', '-nr:false'], 'build-' + package)
        for target in (['netstandard2.0'] if package == generator else ['net10.0', 'net11.0']):
            right_refs[package, target] = references(project.parent / 'obj/project.assets.json', target,
                                                   work / ('right-' + package + '-' + target))
    packed = work / 'packed'
    run(['dotnet', 'pack', root / 'pure/src' / generator / (generator + '.csproj'), '-c', 'Release',
         '-p:PackageVersion=10.0.0', '-o', packed, '--disable-build-servers', '-m:1', '-nr:false'], 'pack-generator')
    with zipfile.ZipFile(packed / (generator + '.10.0.0.nupkg')) as z:
        z.extractall(work / 'new-generator')
    comparisons = []
    for package in runtime:
        for tfm in ['net10.0', 'net11.0']:
            comparisons.append((package, tfm, root / 'pure/src' / package / 'bin/Release' / tfm / (package + '.dll')))
    comparisons.append((generator, 'netstandard2.0', work / 'new-generator/analyzers/dotnet/cs' / (generator + '.dll')))
    count = 0
    for package, tfm, right in comparisons:
        ok, output = compare(baselines[package], right, left_refs[package], right_refs[package, tfm],
                             'compare-' + package + '-' + tfm,
                             suppressions if package == 'Sekiban.Pure.Orleans.xUnit' else None)
        if not ok:
            raise RuntimeError(f'{package} {tfm}:\n{output}')
        count += 1
        print(f'PASS {count:02d}/23 {package}: 9.0.0 -> {tfm}', flush=True)
    if count != 23:
        raise RuntimeError(f'Expected exactly 23 comparisons, ran {count}')
    print('PASS API compatibility: exactly 23 comparisons; no unsuppressed findings or unresolved references.', flush=True)
    if args.self_test:
        # Keep the mutation entirely under .scratch-evidence, never in the real source tree.
        scratch = root / '.scratch-evidence'
        scratch.mkdir(exist_ok=True)
        with tempfile.TemporaryDirectory(prefix='api-negative-', dir=scratch) as negative:
            copy = Path(negative)
            shutil.copy2(root / 'Directory.Build.props', copy)
            shutil.copy2(root / 'global.json', copy)
            (copy / 'pure/src').mkdir(parents=True)
            shutil.copy2(root / 'pure/Directory.Build.props', copy / 'pure')
            shutil.copy2(root / 'pure/README.md', copy / 'pure')
            project_dir = copy / 'pure/src/Sekiban.Pure'
            shutil.copytree(root / 'pure/src/Sekiban.Pure', project_dir,
                            ignore=shutil.ignore_patterns('bin', 'obj'))
            source = project_dir / 'Documents/SortableUniqueIdValue.cs'
            original = source.read_text()
            changed = original.replace('public const int SafeMilliseconds', 'internal const int SafeMilliseconds')
            if changed == original:
                raise RuntimeError('Negative-control member was not found')
            source.write_text(changed)
            run(['dotnet', 'build', project_dir / 'Sekiban.Pure.csproj', '-c', 'Release', '-f', 'net10.0',
                 '--disable-build-servers', '-m:1', '-nr:false'], 'negative-member-build', cwd=copy)
            ok, output = compare(baselines['Sekiban.Pure'], project_dir / 'bin/Release/net10.0/Sekiban.Pure.dll',
                                 left_refs['Sekiban.Pure'], right_refs['Sekiban.Pure', 'net10.0'],
                                 'negative-member', None)
            diagnostics = re.findall(r'^(CP\d+):.*$', output, re.M)
            if ok or diagnostics != ['CP0002'] or 'SafeMilliseconds' not in output:
                raise RuntimeError(f'Wrong removed-member result:\n{output}')
            print('PASS negative control (a): removed public SafeMilliseconds; gate failed with CP0002.', flush=True)
            print(output.strip(), flush=True)
        try:
            download('Sekiban.Pure', work / 'unreachable.nupkg', 'https://127.0.0.1:1/unreachable.nupkg')
        except RuntimeError as error:
            print(f'PASS negative control (b): unreachable baseline failed: {error}', flush=True)
        else:
            raise RuntimeError('Unreachable baseline was skipped')
        empty = work / 'empty-suppressions.xml'
        empty.write_text('<?xml version="1.0" encoding="utf-8"?><Suppressions />')
        findings = []
        failed = []
        for package, tfm, right in comparisons:
            ok, output = compare(baselines[package], right, left_refs[package], right_refs[package, tfm],
                                 'negative-empty-' + package + '-' + tfm,
                                 empty if package == 'Sekiban.Pure.Orleans.xUnit' else None)
            if not ok:
                failed.append((package, tfm))
                lines = re.findall(r'^CP\d+:.*$', output, re.M)
                if len(lines) != 2 or not lines[0].startswith('CP0002:') or not lines[1].startswith('CP0019:'):
                    raise RuntimeError(f'Unexpected unsuppressed finding:\n{output}')
                findings.extend(lines)
                print(output.strip(), flush=True)
        if failed != [('Sekiban.Pure.Orleans.xUnit', 'net10.0'), ('Sekiban.Pure.Orleans.xUnit', 'net11.0')]:
            raise RuntimeError(f'Wrong empty-suppression failures: {failed}')
        print('PASS negative control (c): empty suppressions failed on exactly CP0002 Configure and CP0019 ConfigureServices, on both TFMs; all other 21 comparisons passed.', flush=True)
except Exception as error:
    print(f'FAIL: {error}', file=sys.stderr, flush=True)
    sys.exit(1)
finally:
    if owned_temp:
        owned_temp.cleanup()
PY
