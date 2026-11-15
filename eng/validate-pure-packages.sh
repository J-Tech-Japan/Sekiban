#!/usr/bin/env bash
# Validates the packed Sekiban.Pure packages before they are pushed: the version
# authority (project files, nuspec, assemblies), the package set, the target
# frameworks, the sibling dependencies, the README banner. Python 3, standard library only.
set -euo pipefail
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
exec python3 - "$repo_root" "$@" <<'PY'
"""Validate the packed Sekiban.Pure packages (see eng/validate-pure-packages.sh)."""
import argparse
import contextlib
import io
import re
import subprocess
import sys
import tempfile
import zipfile
import xml.etree.ElementTree as ET
from pathlib import Path

EXPECTED_PACKAGE_COUNT = 12
GENERATOR = 'Sekiban.Pure.SourceGenerator'
RUNTIME_FRAMEWORKS = ('net10.0', 'net11.0')
BANNER_FILE = 'eng/pure-maintenance-mode/canonical-block.md'
HEX = re.compile(r'^[0-9a-f]{7,40}$')


def strip_namespace(tag):
    return tag.rsplit('}', 1)[-1]


def discover(root):
    """Return the shipped (package id, project file) pairs, sorted by id."""
    found = []
    for project in sorted((root / 'pure' / 'src').glob('**/Sekiban.Pure*.csproj')):
        text = project.read_text(encoding='utf-8')
        match = re.search(r'<PackageId>(Sekiban\.Pure[^<]*)</PackageId>', text)
        if match:
            found.append((match.group(1), project))
    return sorted(found)


def informational_blob(data, version, commit):
    """Is there an informational-version custom-attribute blob for this version?

    A custom-attribute blob is 01 00, a one-byte string length, the string and
    00 00. The string is "<version>+<commit>" (SourceLink) or, when no commit is
    known, "<version>" optionally followed by "+" and 7 to 40 hex characters.
    """
    needle = version.encode()
    position = data.find(needle)
    while position != -1:
        if position >= 3:
            length = data[position - 1]
            text = data[position:position + length]
            structured = (
                data[position - 3:position - 1] == b'\x01\x00'
                and len(text) == length
                and data[position + length:position + length + 2] == b'\x00\x00'
            )
            if structured:
                value = text.decode('latin-1')
                if commit:
                    if value == f'{version}+{commit}':
                        return True
                elif value == version or (value.startswith(version + '+') and HEX.fullmatch(value[len(version) + 1:])):
                    return True
        position = data.find(needle, position + 1)
    return False


def read_nuspec(archive):
    names = [name for name in archive.namelist() if name.endswith('.nuspec') and '/' not in name]
    if len(names) != 1:
        return None
    return ET.fromstring(archive.read(names[0]))


def check_package(path, package_id, version, commit, banner, errors):
    """Append one error per violated rule of a single package."""
    with zipfile.ZipFile(path) as archive:
        names = archive.namelist()
        nuspec = read_nuspec(archive)
        if nuspec is None:
            errors.append(f'{package_id}: the package must contain exactly one .nuspec')
            return
        metadata = next(child for child in nuspec if strip_namespace(child.tag) == 'metadata')
        fields = {strip_namespace(child.tag): (child.text or '').strip() for child in metadata}
        if fields.get('id') != package_id:
            errors.append(f'{package_id}: nuspec id is {fields.get("id")}')
        if fields.get('version') != version:
            errors.append(f'{package_id}: nuspec version is {fields.get("version")}, expected {version}')
        if fields.get('readme') != 'README.md' or 'README.md' not in names:
            errors.append(f'{package_id}: README.md must be declared in the nuspec and packed')
        elif banner.strip() not in archive.read('README.md').decode('utf-8'):
            errors.append(f'{package_id}: the packed README.md lacks the canonical banner block')

        groups = set()
        for child in metadata:
            if strip_namespace(child.tag) != 'dependencies':
                continue
            for group in child:
                framework = group.attrib.get('targetFramework', '')
                if strip_namespace(group.tag) == 'group':
                    groups.add(framework)
                for dependency in group:
                    if strip_namespace(dependency.tag) == 'dependency' and dependency.attrib.get('id', '').startswith('Sekiban.Pure'):
                        if dependency.attrib.get('version') != version:
                            errors.append(
                                f'{package_id}: dependency {dependency.attrib["id"]} has version '
                                f'{dependency.attrib.get("version")}, expected {version}')

        if package_id == GENERATOR:
            if any(name.startswith('lib/') for name in names):
                errors.append(f'{package_id}: the source generator must not contain lib/ files')
            analyzer = f'analyzers/dotnet/cs/{package_id}.dll'
            if analyzer not in names:
                errors.append(f'{package_id}: missing {analyzer}')
            else:
                if not informational_blob(archive.read(analyzer), version, commit):
                    errors.append(f'{package_id}: {analyzer} lacks the informational version {version}'
                                  + (f'+{commit}' if commit else ''))
            if groups != {'.NETStandard2.0'}:
                errors.append(f'{package_id}: dependency groups are {sorted(groups)}, expected [.NETStandard2.0]')
            return

        for name in names:
            if name.startswith('lib/') and name.count('/') == 1 and not name.endswith('/'):
                errors.append(f'{package_id}: unexpected file {name}')
        frameworks = sorted({name.split('/')[1] for name in names if name.startswith('lib/') and name.count('/') >= 2})
        for framework in RUNTIME_FRAMEWORKS:
            if framework not in frameworks:
                errors.append(f'{package_id}: missing lib framework {framework}')
        for framework in frameworks:
            if framework not in RUNTIME_FRAMEWORKS:
                errors.append(f'{package_id}: unexpected lib framework {framework}')
        for framework in RUNTIME_FRAMEWORKS:
            assembly = f'lib/{framework}/{package_id}.dll'
            if assembly not in names:
                if framework in frameworks:
                    errors.append(f'{package_id}: missing {assembly}')
            elif not informational_blob(archive.read(assembly), version, commit):
                errors.append(f'{package_id}: {assembly} lacks the informational version {version}'
                              + (f'+{commit}' if commit else ''))
        if groups != set(RUNTIME_FRAMEWORKS):
            errors.append(f'{package_id}: dependency groups are {sorted(groups)}, expected {list(RUNTIME_FRAMEWORKS)}')


def validate(root, directory, version, commit):
    """Return (errors, ok_lines)."""
    errors = []
    oks = []
    projects = discover(root)
    if len(projects) != EXPECTED_PACKAGE_COUNT:
        errors.append(f'repository: expected {EXPECTED_PACKAGE_COUNT} package projects, found {len(projects)}')
    owners = {}
    for package_id, project in projects:
        owners.setdefault(package_id, []).append(project.relative_to(root).as_posix())
    for package_id, files in sorted(owners.items()):
        if len(files) > 1:
            errors.append(f'{package_id}: duplicate PackageId in {", ".join(files)}')
    banner_path = root / BANNER_FILE
    if not banner_path.is_file():
        errors.append(f'repository: missing {BANNER_FILE}')
        banner = ''
    else:
        banner = banner_path.read_text(encoding='utf-8')

    for package_id, project in projects:
        text = project.read_text(encoding='utf-8')
        for tag in ('Version', 'PackageVersion'):
            match = re.search(rf'<{tag}>([^<]*)</{tag}>', text)
            actual = match.group(1).strip() if match else None
            if actual != version:
                errors.append(f'{package_id}: project file {project.relative_to(root).as_posix()} has <{tag}> {actual}, expected {version}')

    expected = {f'{package_id}.{version}.nupkg' for package_id, _ in projects}
    candidates = list(directory.glob('*.nupkg')) if directory.is_dir() else []
    present = {path.name for path in candidates if path.is_file()}
    for path in candidates:
        if not path.is_file():
            errors.append(f'{path.name}: not a regular file')
    for name in sorted(expected - present):
        errors.append(f'{name[:-len(f".{version}.nupkg")]}: missing package {name}')
    for name in sorted(present - expected):
        errors.append(f'{name}: unexpected package')

    for package_id, _ in projects:
        path = directory / f'{package_id}.{version}.nupkg'
        if not path.is_file():
            continue  # already reported as a missing package
        before = len(errors)
        check_package(path, package_id, version, commit, banner, errors)
        if len(errors) == before:
            oks.append(f'ok: {package_id} {version}')
    return errors, oks


# --- self-test ----------------------------------------------------------------

NUSPEC = ('<?xml version="1.0"?><package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">'
          '<metadata><id>{id}</id><version>{version}</version>{readme}<dependencies>{groups}</dependencies></metadata></package>')


def fake_assembly(version, commit):
    value = f'{version}+{commit}' if commit else version
    raw = value.encode()
    return b'MZ\x90\x00' + b'\x00' * 40 + b'\x01\x00' + bytes([len(raw)]) + raw + b'\x00\x00' + b'\x00' * 40


def make_fixture(workdir, version, commit, options=None):
    """Write a fixture repository and package directory; options mutate one aspect."""
    options = options or {}
    root = workdir / 'repo'
    packages = workdir / 'packages'
    (root / 'eng' / 'pure-maintenance-mode').mkdir(parents=True)
    (root / BANNER_FILE).write_text('<!-- START -->\nbanner text\n<!-- END -->\n', encoding='utf-8')
    packages.mkdir()
    ids = [f'Sekiban.Pure.Fixture{index}' for index in range(1, EXPECTED_PACKAGE_COUNT)] + [GENERATOR]
    ids = sorted(ids)
    for package_id in ids:
        project_version = options.get('project_version', {}).get(package_id, version)
        project_package_version = options.get('project_package_version', {}).get(package_id, version)
        project_dir = root / 'pure' / 'src' / package_id
        project_dir.mkdir(parents=True)
        declared_id = options.get('package_id_override', {}).get(package_id, package_id)
        (project_dir / f'{package_id}.csproj').write_text(
            f'<Project><PropertyGroup><PackageId>{declared_id}</PackageId><Version>{project_version}</Version>'
            f'<PackageVersion>{project_package_version}</PackageVersion></PropertyGroup></Project>', encoding='utf-8')
    migration = root / 'pure' / 'src' / 'Sekiban.Pure.Postgres.MigrationHost'
    migration.mkdir(parents=True)
    (migration / 'Sekiban.Pure.Postgres.MigrationHost.csproj').write_text('<Project />', encoding='utf-8')

    for package_id in ids:
        if package_id in options.get('skip_packages', ()):
            continue
        spec = options.get('package', {}).get(package_id, {})
        nuspec_version = spec.get('nuspec_version', version)
        file_version = spec.get('file_version', version)
        dependency_version = spec.get('dependency_version', version)
        generator = package_id == GENERATOR
        readme_declared = '' if spec.get('no_readme') else '<readme>README.md</readme>'
        framework_groups = ['.NETStandard2.0'] if generator else list(RUNTIME_FRAMEWORKS)
        groups = ''.join(
            f'<group targetFramework="{framework}"><dependency id="Sekiban.Pure.Fixture1" version="{dependency_version}" />'
            f'<dependency id="Newtonsoft.Json" version="13.0.3" /></group>' for framework in framework_groups)
        entries = {}
        if not spec.get('no_readme'):
            entries['README.md'] = ('no banner here\n' if spec.get('readme_without_banner')
                                    else (root / BANNER_FILE).read_text(encoding='utf-8') + 'text\n')
        default_info = {'net10.0': (version, commit), 'net11.0': (version, commit), 'analyzer': (version, commit)}
        info = {**default_info, **spec.get('info', {})}
        if generator:
            if not spec.get('no_analyzer'):
                entries[f'analyzers/dotnet/cs/{package_id}.dll'] = spec.get('analyzer_bytes') or fake_assembly(*info['analyzer'])
            if spec.get('with_lib'):
                entries[f'lib/netstandard2.0/{package_id}.dll'] = fake_assembly(version, commit)
        else:
            lib_frameworks = [framework for framework in RUNTIME_FRAMEWORKS if framework not in spec.get('drop_lib', ())]
            lib_frameworks += list(spec.get('extra_lib', ()))
            for stray in spec.get('stray_files', ()):
                entries[stray] = b'x'
            for framework in lib_frameworks:
                data = spec.get('bytes', {}).get(framework) or fake_assembly(*info.get(framework, (version, commit)))
                entries[f'lib/{framework}/{package_id}.dll'] = data
        nuspec = NUSPEC.format(id=package_id, version=nuspec_version, readme=readme_declared, groups=groups)
        with zipfile.ZipFile(packages / f'{package_id}.{file_version}.nupkg', 'w') as archive:
            archive.writestr(f'{package_id}.nuspec', nuspec)
            for name, data in entries.items():
                archive.writestr(name, data)
    for extra in options.get('extra_packages', ()):
        with zipfile.ZipFile(packages / extra, 'w') as archive:
            archive.writestr('x.nuspec', '<package />')
    return root, packages


def run_cli(root, packages, version, commit):
    """Run the real entry point on a fixture; return (exit status, stdout, stderr)."""
    argv = ['--repo-root', str(root), '--dir', str(packages), '--version', version]
    if commit:
        argv += ['--commit', commit]
    out, err = io.StringIO(), io.StringIO()
    with contextlib.redirect_stdout(out), contextlib.redirect_stderr(err):
        status = cli(argv, root)
    return status, out.getvalue(), err.getvalue()


def self_test():
    version = '10.0.0'
    commit = 'a' * 40
    other = 'b' * 40
    failures = 0
    first = 'Sekiban.Pure.Fixture1'
    second = 'Sekiban.Pure.Fixture2'
    cases = [
        ('version-differs-in-the-nuspec', {'package': {first: {'nuspec_version': '9.9.9'}}}, 'nuspec version is 9.9.9'),
        ('missing-package-11-of-12', {'skip_packages': (second,)}, f'missing package {second}.{version}.nupkg'),
        ('extra-package-13', {'extra_packages': (f'Sekiban.Pure.Extra.{version}.nupkg',)}, 'unexpected package'),
        ('project-version-differs', {'project_version': {first: '9.0.0'}}, 'has <Version> 9.0.0'),
        ('project-packageversion-differs', {'project_package_version': {first: '9.0.0'}}, 'has <PackageVersion> 9.0.0'),
        ('extra-net9-lib-folder', {'package': {first: {'extra_lib': ('net9.0',)}}}, 'unexpected lib framework net9.0'),
        ('missing-net11-lib-folder', {'package': {first: {'drop_lib': ('net11.0',)}}}, 'missing lib framework net11.0'),
        ('missing-readme', {'package': {first: {'no_readme': True}}}, 'README.md must be declared'),
        ('readme-without-banner', {'package': {first: {'readme_without_banner': True}}}, 'lacks the canonical banner block'),
        ('sibling-dependency-version-differs', {'package': {second: {'dependency_version': '9.0.0'}}},
         'dependency Sekiban.Pure.Fixture1 has version 9.0.0'),
        ('old-informational-version-in-net10', {'package': {first: {'info': {'net10.0': ('9.0.0', commit)}}}},
         f'lib/net10.0/{first}.dll lacks the informational version'),
        ('old-informational-version-only-in-net11', {'package': {first: {'info': {'net11.0': ('9.0.0', commit)}}}},
         f'lib/net11.0/{first}.dll lacks the informational version'),
        ('wrong-commit-in-net10', {'package': {first: {'info': {'net10.0': (version, other)}}}},
         f'lib/net10.0/{first}.dll lacks the informational version'),
        ('wrong-commit-only-in-net11', {'package': {first: {'info': {'net11.0': (version, other)}}}},
         f'lib/net11.0/{first}.dll lacks the informational version'),
        ('misleading-superstring-in-net10', {'package': {first: {'bytes': {'net10.0': fake_assembly('1' + version, commit)}}}},
         f'lib/net10.0/{first}.dll lacks the informational version'),
        ('source-generator-with-a-lib-folder', {'package': {GENERATOR: {'with_lib': True}}}, 'must not contain lib/ files'),
        ('source-generator-without-the-analyzer', {'package': {GENERATOR: {'no_analyzer': True}}}, 'missing analyzers/dotnet/cs/'),
        ('stray-file-directly-under-lib', {'package': {first: {'stray_files': ('lib/stray.dll',)}}}, 'unexpected file lib/stray.dll'),
        ('old-informational-version-in-the-analyzer', {'package': {GENERATOR: {'info': {'analyzer': ('9.0.0', commit)}}}},
         f'analyzers/dotnet/cs/{GENERATOR}.dll lacks the informational version'),
    ]

    def run(name, options, needle, expect_pass=False, with_commit=commit):
        nonlocal failures
        with tempfile.TemporaryDirectory(prefix='sekiban-pure-packages.') as temp:
            root, packages = make_fixture(Path(temp), version, commit, options)
            status, out, err = run_cli(root, packages, version, with_commit)
        if expect_pass:
            if status != 0 or 'validation passed (12 packages' not in out or out.count('ok: ') != EXPECTED_PACKAGE_COUNT:
                print(f'case FAILED (expected exit 0 and a passed line): {name}', file=sys.stderr)
                print(err, file=sys.stderr)
                failures += 1
            else:
                print(f'case passed: {name} (pass)')
            return
        if status != 1:
            print(f'case FAILED (expected exit status 1, got {status}): {name}', file=sys.stderr)
            failures += 1
        elif needle not in err or 'rule(s) violated' not in err:
            print(f'case FAILED (stderr lacks "{needle}" or the violation count): {name}', file=sys.stderr)
            print(err, file=sys.stderr)
            failures += 1
        else:
            print(f'mutant killed: {name}')

    # two projects declaring the same PackageId collapse to eleven expected packages and must be rejected for that reason
    run('duplicate-package-id', {'package_id_override': {second: first}, 'skip_packages': (second,)}, 'duplicate PackageId')
    run('valid-fixture-set', {}, '', expect_pass=True)
    run('valid-fixture-set-without-a-known-commit', {}, '', expect_pass=True, with_commit=None)
    run('valid-fixture-with-a-short-commit-suffix-and-no-known-commit',
        {'package': {first: {'info': {'net10.0': (version, 'abcdef1'), 'net11.0': (version, 'abcdef1')}}}}, '',
        expect_pass=True, with_commit=None)
    for name, options, needle in cases:
        run(name, options, needle)
    run('hex-suffix-with-a-trailing-newline-without-a-known-commit',
        {'package': {first: {'info': {'net10.0': (version, 'abcdef1\n')}}}},
        f'lib/net10.0/{first}.dll lacks the informational version', with_commit=None)
    run('misleading-superstring-without-a-known-commit',
        {'package': {first: {'bytes': {'net10.0': fake_assembly('1' + version, None)}}}},
        f'lib/net10.0/{first}.dll lacks the informational version', with_commit=None)
    # a directory (or anything that is not a regular file) in place of a package must be reported, never counted as present
    with tempfile.TemporaryDirectory(prefix='sekiban-pure-packages.') as temp:
        root, packages = make_fixture(Path(temp), version, commit, {})
        victim = packages / f'{second}.{version}.nupkg'
        victim.unlink()
        victim.mkdir()
        status, out, err = run_cli(root, packages, version, commit)
    if (status == 1 and f'{second}.{version}.nupkg: not a regular file' in err
            and f'missing package {second}.{version}.nupkg' in err
            and out.count('ok: ') == EXPECTED_PACKAGE_COUNT - 1):
        print('mutant killed: directory-in-place-of-a-package')
    else:
        print('case FAILED: a directory in place of a package was not rejected for its reason', file=sys.stderr)
        print(err, file=sys.stderr)
        failures += 1
    # the validator must also notice a repository with the wrong number of project files
    with tempfile.TemporaryDirectory(prefix='sekiban-pure-packages.') as temp:
        root, packages = make_fixture(Path(temp), version, commit, {})
        for victim in (root / 'pure' / 'src' / 'Sekiban.Pure.Fixture3').glob('*.csproj'):
            victim.unlink()
        status, out, err = run_cli(root, packages, version, commit)
    if status == 1 and 'expected 12 package projects, found 11' in err:
        print('mutant killed: eleven-package-projects-in-the-repository')
    else:
        print('case FAILED: eleven package projects were not noticed', file=sys.stderr)
        failures += 1
    if failures:
        print(f'ERROR: {failures} self-test case(s) failed', file=sys.stderr)
        return 1
    print('Self-test passed.')
    return 0


def make_parser():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo-root', type=Path)
    parser.add_argument('--dir', type=Path, help='directory with the packed .nupkg files')
    parser.add_argument('--version', help='the version being released')
    parser.add_argument('--commit', help='the commit the assemblies were built from (default: git HEAD of the repository root)')
    parser.add_argument('--self-test', action='store_true', help='run the offline fixture tests')
    return parser


def cli(argv, default_root):
    """The validation entry point: prints the report and returns the process exit status."""
    parser = make_parser()
    arguments = parser.parse_args(argv)
    root = (arguments.repo_root or Path(default_root)).resolve()
    if not arguments.dir or not arguments.version:
        parser.error('--dir and --version are required')
    commit = arguments.commit
    if not commit:
        result = subprocess.run(['git', '-C', str(root), 'rev-parse', 'HEAD'], capture_output=True, text=True)
        commit = result.stdout.strip() if result.returncode == 0 else None
    errors, oks = validate(root, arguments.dir.resolve(), arguments.version, commit)
    for line in oks:
        print(line)
    for error in errors:
        print(f'ERROR: {error}', file=sys.stderr)
    if errors:
        print(f'ERROR: {len(errors)} rule(s) violated', file=sys.stderr)
        return 1
    print(f'Sekiban.Pure package validation passed ({EXPECTED_PACKAGE_COUNT} packages, version {arguments.version})')
    return 0


def main():
    arguments = make_parser().parse_args(sys.argv[2:])
    if arguments.self_test:
        return self_test()
    return cli(sys.argv[2:], sys.argv[1])


sys.exit(main())
PY
