"""Run the unchanged harness in main-1, branch-1, main-2, branch-2 order.

Run from the repository root after correctness checks. Only the store source is
swapped; a temporary-file backup is restored byte-for-byte after each invocation.
Rev-4 output names preserve both earlier attempts. The main count probe is
expected to fail the branch's constant multi-key assertions on both TFMs.
"""
import hashlib
import json
import pathlib
import subprocess
import tempfile
import time

root = pathlib.Path.cwd()
bench = root / 'dcb/tests/Sekiban.Dcb.Postgres.AppendBenchmark'
store = root / 'dcb/src/Sekiban.Dcb.Postgres/PostgresEventStore.cs'
branch_bytes = store.read_bytes()
main = subprocess.check_output(['git', 'show', 'origin/main:dcb/src/Sekiban.Dcb.Postgres/PostgresEventStore.cs'])


def run(args, path, expected=0):
    with open(path, 'w') as log:
        result = subprocess.run(args, stdout=log, stderr=subprocess.STDOUT)
    if result.returncode != expected:
        raise RuntimeError(f'{args}: exit {result.returncode}; log {path}')


completed = []
with tempfile.NamedTemporaryFile(prefix='g162-rev4-store-', suffix='.cs') as backup:
    backup.write(branch_bytes)
    backup.flush()
    backup_path = pathlib.Path(backup.name)
    try:
        for block in [1, 2]:
            for label, source in [('main', main), ('branch', branch_bytes)]:
                try:
                    store.write_bytes(source)
                    if block == 1 and label == 'main':
                        run(['dotnet', 'test', 'dcb/tests/Sekiban.Dcb.Postgres.Tests', '-c', 'Release',
                             '-p:GenerateSBOM=false', '--no-restore', '--filter',
                             'FullyQualifiedName~StatementCounts_AreConstant', '--logger',
                             'console;verbosity=detailed'], bench / 'validation/rev4/main-count.log', 1)
                    run(['dotnet', 'build', str(bench), '-c', 'Release', '-f', 'net10.0',
                         '-p:GenerateSBOM=false', '--no-restore'],
                        bench / f'validation/rev4/{label}-{block}-build.log')
                    start = time.time()
                    print(f'START {label} block {block}', flush=True)
                    run(['dotnet', 'run', '--project', str(bench), '-c', 'Release', '-f', 'net10.0',
                         '--no-build', '--', f'{label}-{block}', '5'],
                        bench / f'results-rev4-{label}-r{block}-net10.jsonl')
                    completed.append(dict(label=label, block=block, seconds=time.time() - start,
                                          source_sha256=hashlib.sha256(source).hexdigest()))
                    print(f'DONE {label} block {block}', flush=True)
                finally:
                    store.write_bytes(backup_path.read_bytes())
                    assert store.read_bytes() == branch_bytes
                    (bench / 'alternating-rev4-verification.json').write_text(json.dumps(dict(
                        branch_sha256=hashlib.sha256(branch_bytes).hexdigest(),
                        temporary_file_backup=True, restored_byte_for_byte=True, completed=completed), indent=2) + '\n')
    finally:
        store.write_bytes(backup_path.read_bytes())
        assert store.read_bytes() == branch_bytes
