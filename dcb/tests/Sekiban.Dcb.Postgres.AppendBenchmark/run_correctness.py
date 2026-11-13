import subprocess,pathlib
out=pathlib.Path('dcb/tests/Sekiban.Dcb.Postgres.AppendBenchmark/validation/rev5')
out.mkdir(parents=True,exist_ok=True)
commands=[('build',['dotnet','build','dcb/Sekiban.Dcb.slnx','-c','Release','-p:GenerateSBOM=false','--no-restore'])]
for name,project,filter in [('postgres','Sekiban.Dcb.Postgres.Tests',None),('withresult','Sekiban.Dcb.WithResult.Tests','FullyQualifiedName~Postgres|FullyQualifiedName~ConditionalAppend'),('orleans','Sekiban.Dcb.Orleans.Tests','FullyQualifiedName~TwoClusterPostgresProductTests')]:
 commands.append((name,['dotnet','test','dcb/tests/'+project,'-c','Release','-p:GenerateSBOM=false','--no-build','--no-restore','--logger','console;verbosity=detailed']+(['--filter',filter] if filter else [])))
for name,cmd in commands:
 print('START '+name,flush=True)
 with (out/(name+'.log')).open('w') as log:r=subprocess.run(cmd,stdout=log,stderr=subprocess.STDOUT)
 print('DONE '+name+' exit='+str(r.returncode),flush=True)
 if r.returncode:raise SystemExit(r.returncode)
