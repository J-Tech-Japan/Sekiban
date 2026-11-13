"""Capture one-key SQL using a temporary main store swap, then restore byte-for-byte."""
import pathlib,subprocess,tempfile,os,hashlib,json
p=pathlib.Path('dcb/src/Sekiban.Dcb.Postgres/PostgresEventStore.cs');original=p.read_bytes()
out=pathlib.Path('dcb/tests/Sekiban.Dcb.Postgres.Tests/MainSql');out.mkdir(exist_ok=True)
main=subprocess.check_output(['git','show','e1478efefc578db03e68b06d66a1898270530db0:'+str(p)])
with tempfile.NamedTemporaryFile(prefix='g162-main-capture-') as backup:
 backup.write(original);backup.flush()
 try:
  p.write_bytes(main)
  with open('dcb/tests/Sekiban.Dcb.Postgres.AppendBenchmark/validation/rev5/main-capture.log','w') as log:
   result=subprocess.run(['dotnet','test','dcb/tests/Sekiban.Dcb.Postgres.Tests','-c','Release','-f','net10.0','-p:GenerateSBOM=false','--no-restore','--filter','FullyQualifiedName~OneKey_RecordedTextsAndParameterNames_EqualMain'],env=dict(os.environ,G162_MAIN_SQL_CAPTURE=str(out.resolve())),stdout=log,stderr=subprocess.STDOUT)
  assert result.returncode==0
 finally:
  p.write_bytes(pathlib.Path(backup.name).read_bytes());assert p.read_bytes()==original
  pathlib.Path('dcb/tests/Sekiban.Dcb.Postgres.AppendBenchmark/validation/rev5/main-capture.json').write_text(json.dumps(dict(main_commit='e1478efefc578db03e68b06d66a1898270530db0',main_sha256=hashlib.sha256(main).hexdigest(),branch_sha256=hashlib.sha256(original).hexdigest(),restored_byte_for_byte=p.read_bytes()==original),indent=2)+'\n')
