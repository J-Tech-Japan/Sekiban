"""Run main twice back to back with the unchanged rev-4 harness and method.
Only the store source is swapped, with a temporary-file byte-for-byte restore.
Run after correctness checks with no concurrent builds/tests/profiling.
"""
import hashlib, json, pathlib, subprocess, tempfile
root = pathlib.Path.cwd()
bench = root / 'dcb/tests/Sekiban.Dcb.Postgres.AppendBenchmark'
(bench / 'validation/rev5').mkdir(parents=True, exist_ok=True)
store = root / 'dcb/src/Sekiban.Dcb.Postgres/PostgresEventStore.cs'
branch = store.read_bytes()
commit = 'e1478efefc578db03e68b06d66a1898270530db0'
main = subprocess.check_output(['git', 'show', commit + ':dcb/src/Sekiban.Dcb.Postgres/PostgresEventStore.cs'])
completed = []
def run(args, path):
 with path.open('w') as log:
  subprocess.run(args, stdout=log, stderr=subprocess.STDOUT, check=True)
with tempfile.NamedTemporaryFile(prefix='g162-rev5-control-') as backup:
 backup.write(branch); backup.flush()
 try:
  store.write_bytes(main)
  run(['dotnet', 'build', str(bench), '-c', 'Release', '-f', 'net10.0', '-p:GenerateSBOM=false', '--no-restore'], bench / 'validation/rev5/control-build.log')
  for block in [1, 2]:
   print(f'START main control {block}', flush=True)
   run(['dotnet', 'run', '--project', str(bench), '-c', 'Release', '-f', 'net10.0', '--no-build', '--', f'main-control-{block}', '5'], bench / f'results-rev5-main-control-{block}-net10.jsonl')
   completed.append(block)
   print(f'DONE main control {block}', flush=True)
 finally:
  store.write_bytes(pathlib.Path(backup.name).read_bytes())
  assert store.read_bytes() == branch
  (bench / 'validation/rev5/control-verification.json').write_text(json.dumps(dict(main_commit=commit, main_sha256=hashlib.sha256(main).hexdigest(), branch_sha256=hashlib.sha256(branch).hexdigest(), temporary_file_backup=True, restored_byte_for_byte=True, completed=completed), indent=2)+'\n')
