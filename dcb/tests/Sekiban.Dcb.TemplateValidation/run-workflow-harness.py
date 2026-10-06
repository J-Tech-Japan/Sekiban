#!/usr/bin/env python3
"""Offline schema-3 tests and literal guard sequences from BOTH tag workflows.

Only checkout/SDK setup, builds, pack and expensive consumers are simulated.
Reader, record/body/package validators, every shell guard, waits and equality run
for real. gh/curl serve deterministic API/feed fixtures; push and Release are
local simulations with a recorded trace. No validator outputs are fabricated.
"""
from __future__ import annotations
import argparse
import base64
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile
import zipfile
import yaml

REL = Path('dcb/tests/Sekiban.Dcb.TemplateValidation')
REPO = 'J-Tech-Japan/Sekiban'
HOST = 'J-Tech-Japan/SekibanIntentHost'
HOST_REF = 'a' * 40
TREE = 'b' * 40
PATH_TREES = [TREE, 'd' * 40, 'e' * 40, 'f' * 40]
CHECKS = [('dcbTestsNet9', 'run_test_dcb.yml', 'dcbTestsNet9'),
          ('dcbTestsNet10', 'run_test_dcb.yml', 'dcbTestsNet10'),
          ('packagedConsumer', 'dcb_azure_queue_packaged_consumer.yml', 'packaged-consumer'),
          ('templateConsumer', 'dcb_template_validation.yml', 'Pack, install, generate, restore, build, and test templates')]
VERSIONS = ('10.22.0', '42.7.3', '10.23.1')  # Explicit fixture inputs; never production defaults.

class Failure(RuntimeError):
    pass

def run(args, cwd, env=None):
    result = subprocess.run(args, cwd=cwd, env=env, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    if result.returncode:
        raise Failure(result.stdout.strip())
    return result.stdout

def write_json(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False) + '\n')

def executable(path, content):
    path.write_text(content)
    path.chmod(0o755)

# All external calls are intercepted. Unexpected endpoints fail closed.
SHIM = '''#!/usr/bin/env python3
import sys, json, os, shutil
from pathlib import Path
args=sys.argv[1:]; root=Path(os.environ['FIXTURE_ROOT']); tool=Path(sys.argv[0]).name
state=json.loads((root/'api.json').read_text())
if tool=='gh':
    assert args[0]=='api', args
    assert '--method' not in args or args[args.index('--method')+1]=='GET', args
    route=next(a for a in args[1:] if a.startswith('repos/'))
    token='host-token' if '/SekibanIntentHost/' in route else 'workflow-token'
    assert os.environ.get('GH_TOKEN')==token, (route, 'wrong token')
    with (root/'api-trace').open('a') as trace: trace.write(route+'\\n')
    if route in state and '_error' in state[route]:
        print('API failed (HTTP '+str(state[route]['_error'])+')', file=sys.stderr); sys.exit(1)
    if route not in state:
        if '-i' in args: print('HTTP/2 404')
        print('Not Found (HTTP 404)', file=sys.stderr); sys.exit(1)
    if '-i' in args: print('HTTP/2 200\\n')
    print(json.dumps(state[route])); sys.exit(0)
if tool=='curl':
    url=next(a for a in args if a.startswith(('https://','file://')))
    relative=url.split('/v3-flatcontainer/',1)[-1] if '/v3-flatcontainer/' in url else url.removeprefix('file://')
    path=root/'public'/relative if not url.startswith('file://') else Path(relative)
    exists=path.is_file(); output=None
    for flag in ['--output','-o']:
        if flag in args: output=args[args.index(flag)+1]
    if output: Path(output).write_bytes(path.read_bytes() if exists else b'not found')
    elif exists: sys.stdout.buffer.write(path.read_bytes())
    if '--write-out' in args: print('200' if exists else '404', end='')
    if not exists and '--fail' in args: sys.exit(22)
    sys.exit(0)
raise AssertionError(tool)
'''

class Fixture:
    def __init__(self, source, root, version):
        self.root, self.version = root, version
        self.repo = root/'checkout'
        self.repo.mkdir(parents=True)
        self.dotnet = shutil.which('dotnet')
        for relative in ['.github/workflows', 'dcb/src', str(REL), 'templates/Sekiban.Dcb.Templates', 'docs/releases']:
            shutil.copytree(source/relative, self.repo/relative, ignore=shutil.ignore_patterns('bin','obj','__pycache__'))
        shutil.copy2(source/'CONTRIBUTING.md', self.repo/'CONTRIBUTING.md')
        self.dll = source/REL/'bin/Release/net10.0/Sekiban.Dcb.TemplateValidation.dll'
        output = self.repo/REL/'bin/Release/net10.0'
        shutil.copytree(self.dll.parent, output)
        self.dll = output/self.dll.name
        authority_version = run(['bash', str(source/REL/'read-template-version.sh'), '--repo-root', str(source)], source).strip()
        for props in (self.repo/'templates/Sekiban.Dcb.Templates/content').glob('*/SekibanDcbTemplateVersion.props'):
            props.write_text(props.read_text().replace(authority_version, version))
        assert run(['bash',str(self.repo/REL/'read-template-version.sh'),'--repo-root',str(self.repo)],self.repo).strip()==version
        template_readme=self.repo/'templates/Sekiban.Dcb.Templates/README.md'
        template_readme.write_text(template_readme.read_text().replace(authority_version, version))
        self.bodies = {'library_en_sha256': f'dcb-v{version}-library.en.md', 'library_ja_sha256': f'dcb-v{version}-library.ja.md',
                       'template_en_sha256': f'dcbTemplates-v{version}.en.md', 'template_ja_sha256': f'dcbTemplates-v{version}.ja.md'}
        if version != authority_version:
            for key, name in self.bodies.items():
                text = f'{version} template release.\n' if '_en_' in key else f'{version} テンプレートのリリース。\n'
                (self.repo/'docs/releases'/name).write_text(text)
        run(['git','init','-b','main'], self.repo)
        run(['git','config','user.name','SEK-G122 offline fixture'], self.repo)
        run(['git','config','user.email','offline@example.invalid'], self.repo)
        run(['git','add','.'], self.repo)
        run(['git','commit','-m','offline merged candidate'], self.repo)
        self.sha=run(['git','rev-parse','HEAD'], self.repo).strip()
        # Main has advanced: the candidate must pass as an older ancestor.
        run(['git','commit','--allow-empty','-m','newer main tip'], self.repo)
        self.main=run(['git','rev-parse','HEAD'],self.repo).strip()
        run(['git','update-ref','refs/remotes/origin/main',self.main],self.repo)
        run(['git','checkout','--detach',self.sha],self.repo)
        self.tags={}
        for tag, date in [(f'dcb-v{version}', '2026-09-14T18:00:00Z'),(f'dcbTemplates-v{version}', '2026-09-14T19:00:00Z')]:
            env=os.environ.copy(); env['GIT_COMMITTER_DATE']=date
            run(['git','tag','-a',tag,'-m','offline fixture'],self.repo,env)
            self.tags[tag]=run(['git','rev-parse',f'{tag}^{{tag}}'],self.repo).strip()
        if version == '10.23.1':
            # Incomplete predecessor: library tag exists, but its template tag
            # and GitHub Release have no fixture endpoint (unexpected GETs fail).
            for old in ['dcb-v10.22.0', 'dcbTemplates-v10.22.0', 'dcb-v10.23.0']:
                run(['git','tag','-a',old,'-m','historical fixture'],self.repo)
        # Fetches use only this local bare fixture; never the real repository.
        run(['git','clone','--no-local','--bare',str(self.repo),str(root/'origin.git')],root)
        run(['git','remote','add','origin',str(root/'origin.git')],self.repo)
        self.api={}
        self.record={'schema_version':3,'version':version,'stage':'prepared','merged_sha':self.sha,'candidate_pr':1316,
                     'checks':[{'name':name,'run_id':100+i} for i,(name,_,_) in enumerate(CHECKS)],
                     'release_bodies':{key:hashlib.sha256((self.repo/'docs/releases'/name).read_bytes()).hexdigest() for key,name in self.bodies.items()}}
        for i,(_,workflow,job) in enumerate(CHECKS):
            route=f'repos/{REPO}/actions/runs/{100+i}'
            self.api[route]={'id':100+i,'repository':{'full_name':REPO},'head_repository':{'full_name':REPO},
                             'path':f'.github/workflows/{workflow}','event':'workflow_dispatch','head_sha':self.sha,
                             'status':'completed','conclusion':'success','run_attempt':2}
            self.api[route+'/attempts/2/jobs?per_page=100&page=1']={'total_count':1,'jobs':[{'name':job,'run_id':100+i,'run_attempt':2,
                               'head_sha':self.sha,'status':'completed','conclusion':'success'}]}
        # Attempt 1 may have failed; only the successful current attempt is used.
        for tag,obj in self.tags.items():
            self.api[f'repos/{REPO}/git/ref/tags/{tag}']={'ref':'refs/tags/'+tag,'object':{'sha':obj,'type':'tag'}}
            self.api[f'repos/{REPO}/git/tags/{obj}']={'sha':obj,'tag':tag,'object':{'sha':self.sha,'type':'commit'},
                        'tagger':{'date':'2026-09-14T19:00:00Z' if tag.startswith('dcbTemplates') else '2026-09-14T18:00:00Z'}}
        self.ids=re.findall(r'"(Sekiban\.Dcb[^\"]+)"',(source/REL/'PackageManifest.cs').read_text())
        for tag in self.tags:
            library=tag.startswith('dcb-v')
            names=[f'{id}.{version}.nupkg' for id in self.ids] if library else [f'Sekiban.Dcb.Templates.{version}.nupkg']
            self.api[f'repos/{REPO}/releases/tags/{tag}']={'tag_name':tag,'html_url':f'https://github.com/{REPO}/releases/tag/{tag}',
                         'draft':False,'published_at':'2026-09-14T18:30:00Z','assets':[{'name':name} for name in names],
                         'body':''.join((self.repo/'docs/releases'/self.bodies[k]).read_text() for k in (['library_en_sha256','library_ja_sha256'] if library else ['template_en_sha256','template_ja_sha256']))}
        self.shims=root/'shims'; self.shims.mkdir()
        for tool in ['gh','curl']: executable(self.shims/tool, SHIM)
        self.temp=root/'runner-temp'; self.temp.mkdir()
        self.env=os.environ.copy(); self.env.update({'FIXTURE_ROOT':str(root),'PATH':str(self.shims)+':'+os.environ['PATH'],
           'GITHUB_REPOSITORY':REPO,'GITHUB_WORKSPACE':str(self.repo),'RUNNER_TEMP':str(self.temp),
           'GITHUB_ENV':str(root/'github.env'),'GITHUB_RUN_ATTEMPT':'1',
           'ImageOS':'offline-fixture','ImageVersion':'offline-fixture',
           'SEKIBAN_RELEASE_RECORD_REF':HOST_REF,'VERSION':version})
        self.env.pop('GH_TOKEN', None)
        self.local=root/'packed'; self.local.mkdir()
        for id in self.ids: self.package(self.local/f'{id}.{version}.nupkg',id)
        self.template_package=self.local/f'Sekiban.Dcb.Templates.{version}.nupkg'
        self.package(self.template_package,'Sekiban.Dcb.Templates',template=True)
        (root/'public').mkdir()
        self.save()

    def package(self,path,id,template=False,version=None):
        version=version or self.version
        groups=''
        for framework, relational in [('net9.0','9.0.13'),('net10.0','10.0.3')]:
            deps=[]
            if id=='Sekiban.Dcb.Postgres': deps=[('Microsoft.EntityFrameworkCore.Relational',relational)]
            if id=='Sekiban.Dcb.Orleans.AzureQueue': deps=[('Azure.Storage.Queues','12.25.0'),('Microsoft.Orleans.Streaming.AzureStorage','10.3.1')]
            groups+=f'<group targetFramework="{framework}">'+''.join(f'<dependency id="{n}" version="{v}" />' for n,v in deps)+'</group>'
        with zipfile.ZipFile(path,'w') as archive:
            archive.writestr(f'{id}.nuspec',f'<package><metadata><id>{id}</id><version>{version}</version><dependencies>{groups}</dependencies></metadata></package>')
            archive.writestr('content/offline.txt',id+' '+version)
            archive.writestr('lib/net10.0/fixture.dll', b'unchanged assembly')
            manifest = json.dumps({'SPDXID':'SPDXRef-DOCUMENT','fixture':id}).encode()
            archive.writestr('_manifest/spdx_2.2/manifest.spdx.json', manifest)
            archive.writestr('_manifest/spdx_2.2/manifest.spdx.json.sha256', hashlib.sha256(manifest).hexdigest())
            if template:
                carrier=self.repo/'templates/Sekiban.Dcb.Templates'
                archive.writestr('README.md',(carrier/'README.md').read_bytes())
                for file in (carrier/'content').rglob('*'):
                    if file.is_file() and file.name!='Directory.Build.props': archive.write(file, file.relative_to(carrier))

    def save(self):
        content=(json.dumps(self.record,ensure_ascii=False)+'\n').encode()
        blob=subprocess.run(['git','hash-object','--stdin'],input=content,stdout=subprocess.PIPE,check=True).stdout.decode().strip()
        path=f'intents/sekiban/releases/dcb-v{self.version}-release-record.json'
        self.api[f'repos/{HOST}/contents/{path}?ref={HOST_REF}']={'type':'file','encoding':'base64','path':path,'sha':blob,'content':base64.b64encode(content).decode()}
        self.api[f'repos/{HOST}/commits/{HOST_REF}']={'sha':HOST_REF,'commit':{'tree':{'sha':TREE}}}
        for i, component in enumerate(path.split('/')):
            last = i == len(PATH_TREES)-1
            self.api[f'repos/{HOST}/git/trees/{PATH_TREES[i]}']={'sha':PATH_TREES[i],'truncated':False,
                'tree':[{'path':component,'type':'blob' if last else 'tree','sha':blob if last else PATH_TREES[i+1]}]}
        # A large host repository cannot safely be verified with recursive listing.
        self.api[f'repos/{HOST}/git/trees/{TREE}?recursive=1']={'sha':TREE,'truncated':True,'tree':[]}
        write_json(self.root/'api.json',self.api)

    def validate(self,state='prepared'):
        bundle=self.temp/'dcb-release-record-bundle'
        shutil.rmtree(bundle,ignore_errors=True)
        env=self.env.copy(); env['GH_TOKEN']='host-token'
        run(['bash',str(self.repo/REL/'read-host-release-record.sh'),'--version',self.version,'--state',state,
             '--output-dir',str(bundle),'--manifest',str(bundle/'bundle.json')],self.repo,env)
        return self.validate_bundle(state)

    def validate_bundle(self,state='prepared'):
        bundle=self.temp/'dcb-release-record-bundle'
        return run([self.dotnet,str(self.dll),'release-record','--bundle',str(bundle),'--manifest',str(bundle/'bundle.json'),
             '--repo-root',str(self.repo),'--expected-version',self.version,'--state',state,'--merged-sha-output',str(self.temp/'validated-merged-sha.txt')],self.repo,{**self.env,'GH_TOKEN':'workflow-token'})

    def publish(self,template=False):
        for pkg in (self.repo/'out').glob('*.nupkg'):
            if pkg.name.startswith('Sekiban.Dcb.Templates.') != template: continue
            lower=pkg.name[:-len('.'+self.version+'.nupkg')].lower()
            dest=self.root/'public'/lower/self.version/pkg.name.lower(); dest.parent.mkdir(parents=True,exist_ok=True)
            # Model NuGet --skip-duplicate: never replace an existing artifact.
            if not dest.exists(): shutil.copy2(pkg,dest)
            write_json(dest.parent.parent/'index.json',{'versions':[self.version]})

    def workflow(self,mode):
        template=mode=='template'
        tag=f'dcbTemplates-v{self.version}' if template else f'dcb-v{self.version}'
        self.env.update({'GITHUB_REF_NAME':tag,'GITHUB_REF':'refs/tags/'+tag})
        data=yaml.safe_load((self.repo/'.github/workflows'/('packagesDcbTemplate.yml' if template else 'packagesDcb.yml')).read_text())
        expr={'github.event.after':getattr(self,'trigger_override',self.tags[tag]),'github.ref_name':tag,'github.token':'workflow-token',
              'secrets.SEKIBAN_RELEASE_RECORD_TOKEN':'host-token','secrets.NUGET_APIKEY':'offline-never-used',
              'vars.SEKIBAN_RELEASE_RECORD_REF':HOST_REF}
        trace=[]
        self.workflow_trace=trace
        out=self.repo/'out'; shutil.rmtree(out,ignore_errors=True); out.mkdir()
        if not template:
            self.api.pop(f'repos/{REPO}/git/ref/tags/dcbTemplates-v{self.version}',None)
        self.save()
        for step in data['jobs']['build']['steps']:
            name=step.get('name',step.get('uses','')); trace.append(name)
            if name == getattr(self, 'failure_point', None):
                self.failure_point = None
                raise Failure('injected post-push failure at '+name)
            if step.get('uses','').startswith('actions/checkout'):
                assert step.get('with',{}).get('fetch-depth')==0
                run(['git','update-ref','refs/tags/'+tag,self.sha],self.repo)
                continue
            if step.get('uses','').startswith('actions/setup-dotnet'): continue
            if name in ['Restore dependencies','Build with dotnet','Build release-record and package validators','Build release validators']: continue
            if name in ['Pack NuGet packages','Pack Template']:
                for pkg in self.local.glob('*.nupkg'):
                    if pkg.name.startswith('Sekiban.Dcb.Templates.') == template: shutil.copy2(pkg,out/pkg.name)
                continue
            if name=='Validate Azure Queue V2 packaged consumer and dependency groups': continue
            if name=='Validate packed consumer path':
                # Full SDK restore/build/test consumers remain in CI; artifact checks still run for real here.
                run([self.dotnet,str(self.dll),'package','--package',str(out/self.template_package.name),'--expected-version',self.version],self.repo,self.env)
                continue
            if name in ['Push to NuGet.org','Push Template']:
                self.publish(template); continue
            if name=='Create GitHub Release':
                assert step['with']['draft'] is False
                assert list(out.glob('*.nupkg'))
                assert (out/('template-release-body.md' if template else 'library-release-body.md')).stat().st_size
                continue
            def substitute(value):
                return re.sub(r'\$\{\{\s*([^}]+?)\s*\}\}',lambda m:expr[m[1].strip()],str(value))
            if template and name=='Enforce template live guard before push':
                case=getattr(self,'late_mutation',None)
                release=self.api[f'repos/{REPO}/releases/tags/dcb-v{self.version}']
                if case=='late-library-release-draft': release['draft']=True
                elif case=='late-library-release-25-assets': release['assets'].pop()
                elif case=='late-library-release-wrong-body': release['body']='edited after verification'
                elif case=='late-library-release-recreated': release['published_at']='2026-09-14T20:00:00Z'
                elif case=='late-library-release-deleted':
                    self.api[f'repos/{REPO}/releases/tags/dcb-v{self.version}']={'_error':404}
                elif case=='late-library-wrong-peel':
                    self.api[f'repos/{REPO}/git/tags/{self.tags[f"dcb-v{self.version}"]}']['object']['sha']='c'*40
                if case: write_json(self.root/'api.json',self.api)
                if getattr(self,'missing_guard_token',False): step.get('env',{}).pop('GH_TOKEN',None)
            env=self.env.copy(); env.update({k:substitute(v) for k,v in step.get('env',{}).items()})
            script=substitute(step['run']).replace('--timeout-seconds 3600','--timeout-seconds 10').replace('--interval-seconds 15','--interval-seconds 1')
            # Replaced only the external timeout; every guard's literal run block executes.
            run(['bash','-euo','pipefail','-c',script],self.repo,env)
            env_file=Path(self.env['GITHUB_ENV'])
            if env_file.exists():
                for line in env_file.read_text().splitlines():
                    key,value=line.split('=',1); self.env[key]=value
        expected=['Pack Template','Validate packed consumer path','Push Template','Wait for exact public template visibility','Prove public template package matches local pack','Create GitHub Release'] if template else ['Pack NuGet packages','Inspect exact package set and dependency groups before push','Push to NuGet.org','Wait for exact public library visibility','Prove public library packages match local pack','Create GitHub Release']
        assert [trace.index(n) for n in expected]==sorted(trace.index(n) for n in expected)
        assert (self.temp/'validated-merged-sha.txt').read_text()==self.sha+'\n'
        assert not (self.temp/'release-facts.json').exists()
        self.api[f'repos/{REPO}/git/ref/tags/dcbTemplates-v{self.version}']={'object':{'sha':self.tags[f'dcbTemplates-v{self.version}'],'type':'tag'}}
        self.save()
        return trace

def mutate_retry_package(path, kind):
    with zipfile.ZipFile(path) as archive:
        entries = {entry.filename: archive.read(entry) for entry in archive.infolist()}
    manifest = b'{"SPDXID":"SPDXRef-DOCUMENT","fixture":"repacked"}'
    entries['_manifest/spdx_2.2/manifest.spdx.json'] = manifest
    entries['_manifest/spdx_2.2/manifest.spdx.json.sha256'] = hashlib.sha256(manifest).hexdigest().encode()
    if kind == 'assembly': entries['lib/net10.0/fixture.dll'] = b'changed assembly'
    elif kind == 'nuspec':
        name = next(name for name in entries if name.endswith('.nuspec'))
        entries[name] = entries[name].replace(b'</metadata>', b'<description>changed</description></metadata>')
    elif kind == 'nested-manifest': entries['lib/_manifest/x'] = b'compared payload'
    else: assert kind == 'sbom'
    with zipfile.ZipFile(path, 'w') as archive:
        for name, content in entries.items(): archive.writestr(name, content)


def check_incomplete_history_drift(f):
    script = str(f.repo/REL/'validate-release-tags.sh')
    library, template = 'dcb-v10.23.1', 'dcbTemplates-v10.23.1'
    for phase in ['before', 'between', 'after']:
        for tag, present in [(library, phase != 'before'), (template, phase == 'after')]:
            if present: run(['git','update-ref','refs/tags/'+tag,f.tags[tag]],f.repo)
            else: run(['git','update-ref','-d','refs/tags/'+tag],f.repo)
        try: result = run(['bash',script,'--check-drift','--repo-root',str(f.repo)],f.repo,f.env)
        except Failure as error:
            assert phase != 'after' and 'drift' in str(error).lower(), str(error)
            print('PASS incomplete 10.23.0 history drift '+phase+': rejects as required',flush=True)
        else:
            assert phase == 'after', result
            print('PASS incomplete 10.23.0 history drift after: equal at 10.23.1',flush=True)


def retry_cases(source, root):
    for mode in ['library', 'template']:
        for kind in ['sbom', 'assembly', 'nuspec', 'nested-manifest']:
            f = Fixture(source, root/(mode+'-retry-'+kind), '10.23.1')
            f.workflow('library')
            if mode == 'template': f.workflow('template')
            f.env['GITHUB_RUN_ATTEMPT'] = '2'
            paths = [f.template_package] if mode == 'template' else [f.local/f'{id}.{f.version}.nupkg' for id in f.ids]
            for path in paths: mutate_retry_package(path, kind)
            # The fixture must actually contain different SBOM bytes on each side.
            remote = f.root/'public'/('sekiban.dcb.templates' if mode == 'template' else f.ids[0].lower())/f.version/paths[0].name.lower()
            with zipfile.ZipFile(paths[0]) as left, zipfile.ZipFile(remote) as right:
                for name in ['_manifest/spdx_2.2/manifest.spdx.json','_manifest/spdx_2.2/manifest.spdx.json.sha256']:
                    assert left.read(name) != right.read(name)
            try: f.workflow(mode)
            except Failure as error:
                assert kind != 'sbom' and 'Semantic package manifests differ' in str(error), str(error)
                if mode == 'library':
                    assert '0/26 equal; 26 failed' in str(error)
                    for id in f.ids: assert f'Package equality failed: {id}: content differs' in str(error)
                assert 'Create GitHub Release' not in f.workflow_trace
                print(f'PASS {mode} retry rejects {kind} (root SBOM also differs)',flush=True)
            else:
                assert kind == 'sbom'
                print(f'PASS {mode} retry SBOM-only difference reaches Release',flush=True)
        points = (['Wait for exact public library visibility','Prove public library packages match local pack'] if mode == 'library' else
                  ['Wait for exact public template visibility','Prove public template package matches local pack']) + ['Create GitHub Release']
        for i, point in enumerate(points):
            f = Fixture(source, root/(mode+'-recovery-'+str(i)), '10.23.1')
            if mode == 'template': f.workflow('library')
            f.failure_point = point
            try: f.workflow(mode)
            except Failure as error: assert 'injected post-push failure' in str(error)
            else: raise Failure('injected failure did not fire')
            f.env['GITHUB_RUN_ATTEMPT'] = '2'
            for path in f.local.glob('*.nupkg'): mutate_retry_package(path, 'sbom')
            f.workflow(mode)
            print(f'PASS {mode} retry after failure at {point}: reaches Release',flush=True)
    # All diagnostics are collected even when different failure classes coexist.
    f = Fixture(source, root/'equality-diagnostics', '10.23.1'); f.workflow('library')
    (f.local/f'{f.ids[0]}.{f.version}.nupkg').unlink()
    missing = f.root/'public'/f.ids[1].lower()/f.version/f'{f.ids[1]}.{f.version}.nupkg'.lower()
    missing.unlink()
    mutate_retry_package(f.local/f'{f.ids[2]}.{f.version}.nupkg','assembly')
    try:
        run(['bash',str(f.repo/REL/'validate-release-tags.sh'),'--check-library-post-push-equality',
             '--version',f.version,'--local-out-dir',str(f.local)],f.repo,f.env)
    except Failure as error:
        for id, reason in zip(f.ids[:3], ['missing locally','could not download','content differs']):
            assert f'{id}: {reason}' in str(error)
        assert '23/26 equal; 3 failed' in str(error)
        print('PASS library equality aggregates missing locally, could not download, content differs',flush=True)
    else: raise Failure('diagnostics case unexpectedly passed')

def mutate(f,case):
    route=f'repos/{REPO}/actions/runs/100'; run_api=f.api[route]
    if case.startswith('late-'): f.late_mutation=case
    elif case=='template-guard-missing-token': f.missing_guard_token=True
    elif case=='wrong-merged-sha': f.record['merged_sha']='c'*40
    elif case=='merged-sha-not-on-main': pass
    elif case=='wrong-check-head': run_api['head_sha']='c'*40
    elif case=='wrong-workflow': run_api['path']='.github/workflows/dcb_postgres_packaged_consumer.yml'
    elif case=='fork-run': run_api['head_repository']['full_name']='fork/Sekiban'
    elif case=='wrong-repository': run_api['repository']['full_name']='fork/Sekiban'
    elif case=='pull-request-run': run_api['event']='pull_request'
    elif case=='success-then-failed-rerun': run_api['conclusion']='failure'
    elif case=='rerun-in-progress': run_api.update(status='in_progress',conclusion=None)
    elif case=='body-hash-mismatch': f.record['release_bodies']['library_en_sha256']='0'*64
    elif case=='empty-body':
        (f.repo/'docs/releases'/f.bodies['library_en_sha256']).write_text('')
        f.record['release_bodies']['library_en_sha256']=hashlib.sha256(b'').hexdigest()
    elif case=='missing-package': (f.local/f'Sekiban.Dcb.Core.{f.version}.nupkg').unlink()
    elif case=='wrong-version-package': f.package(f.local/f'Sekiban.Dcb.Core.{f.version}.nupkg','Sekiban.Dcb.Core',version='99.0.0')
    elif case=='duplicate-alias': f.record['checks'][1]['name']='dcbTestsNet9'
    elif case=='missing-alias': f.record['checks'].pop()
    elif case=='failed-job': f.api[route+'/attempts/2/jobs?per_page=100&page=1']['jobs'][0]['conclusion']='failure'
    elif case=='wrong-job-attempt': f.api[route+'/attempts/2/jobs?per_page=100&page=1']['jobs'][0]['run_attempt']=1
    elif case=='schema-2': f.record['schema_version']=2
    elif case=='wrong-record-version': f.record['version']='99.0.0'
    elif case in ['template-not-later-than-library-tag','template-not-later-than-library-release']:
        f.api[f'repos/{REPO}/git/tags/{f.tags[f"dcbTemplates-v{f.version}"]}']['tagger']['date']='2026-09-14T18:00:00Z' if case.endswith('library-tag') else '2026-09-14T18:30:00Z'
    elif case=='library-preexisting-package' or case=='template-preexisting-package':
        id='sekiban.dcb.core' if case.startswith('library') else 'sekiban.dcb.templates'
        write_json(f.root/'public'/id/'index.json',{'versions':[f.version]})
    elif case in ['library-retry-changed-tag','template-retry-changed-tag']:
        f.env['GITHUB_RUN_ATTEMPT']='2'
        f.trigger_override='c'*40
    elif case in ['library-lightweight-tag','template-lightweight-tag']:
        tag=f'dcb-v{f.version}' if case.startswith('library') else f'dcbTemplates-v{f.version}'
        f.api[f'repos/{REPO}/git/ref/tags/{tag}']['object']={'sha':f.sha,'type':'commit'}
    elif case=='library-wrong-peel': f.api[f'repos/{REPO}/git/tags/{f.tags[f"dcb-v{f.version}"]}']['object']['sha']='c'*40
    elif case=='template-wrong-peel': f.api[f'repos/{REPO}/git/tags/{f.tags[f"dcbTemplates-v{f.version}"]}']['object']['sha']='c'*40
    elif case=='library-release-draft': f.api[f'repos/{REPO}/releases/tags/dcb-v{f.version}']['draft']=True
    elif case=='library-release-25-assets': f.api[f'repos/{REPO}/releases/tags/dcb-v{f.version}']['assets'].pop()
    elif case=='library-release-wrong-body': f.api[f'repos/{REPO}/releases/tags/dcb-v{f.version}']['body']='wrong'
    elif case=='authority-mismatch':
        props=next((f.repo/'templates/Sekiban.Dcb.Templates/content').glob('*/SekibanDcbTemplateVersion.props'))
        props.write_text(props.read_text().replace(f.version,'99.0.0'))
    elif case=='draft-complete-release': f.api[f'repos/{REPO}/releases/tags/dcb-v{f.version}']['draft']=True
    elif case=='wrong-complete-url': f.record['published']['library_release_url']='https://example.invalid/release'
    elif case=='wrong-complete-peel': f.api[f'repos/{REPO}/git/tags/{f.tags[f"dcb-v{f.version}"]}']['object']['sha']='c'*40
    else: raise AssertionError(case)
    if case=='merged-sha-not-on-main':
        run(['git','checkout','--orphan','unrelated'],f.repo)
        run(['git','commit','-m','unrelated main'],f.repo)
        off=run(['git','rev-parse','HEAD'],f.repo).strip()
        run(['git','update-ref','refs/remotes/origin/main',off],f.repo)
        run(['git','checkout','--detach',f.sha],f.repo)
    f.save()

# Expected reason ensures mutations fail at the intended gate, not incidental setup.
RECORD_CASES={'wrong-merged-sha':'merged_sha does not equal', 'merged-sha-not-on-main':'git failed',
 'wrong-check-head':'Run event or head_sha', 'wrong-workflow':'Wrong workflow', 'fork-run':'Run repository',
 'wrong-repository':'Run repository','pull-request-run':'Run event', 'success-then-failed-rerun':'Latest run attempt',
 'rerun-in-progress':'Latest run attempt','body-hash-mismatch':'Body hash mismatch','empty-body':'must name DCB',
 'duplicate-alias':'duplicate or unknown','missing-alias':'Exactly four','failed-job':'Latest attempt job',
 'wrong-job-attempt':'Latest attempt job','schema-2':'requested schema-3','wrong-record-version':'requested schema-3'}
INTEGRATION_CASES={**{k:v for k,v in RECORD_CASES.items() if k in ['wrong-merged-sha','merged-sha-not-on-main','wrong-check-head','wrong-workflow','fork-run','pull-request-run','success-then-failed-rerun','rerun-in-progress','body-hash-mismatch','empty-body']},
 'missing-package':'Expected exactly 26', 'wrong-version-package':'must be version',
 'template-not-later-than-library-tag':'strictly later than the library tag',
 'template-not-later-than-library-release':'strictly later than the library release',
 'library-preexisting-package':'already exists', 'template-preexisting-package':'already exists',
 'library-retry-changed-tag':'differs from triggering tag object', 'template-retry-changed-tag':'differs from triggering tag object',
 'library-lightweight-tag':'must be an annotated tag', 'template-lightweight-tag':'must be an annotated tag',
 'library-wrong-peel':'does not point at the validated merged SHA', 'template-wrong-peel':'peeled commit does not match',
 'library-release-draft':'non-draft', 'library-release-25-assets':'exactly 26 package assets',
 'library-release-wrong-body':'body does not exactly match', 'authority-mismatch':'expected',
 'late-library-release-draft':'non-draft', 'late-library-release-25-assets':'exactly 26 package assets',
 'late-library-release-wrong-body':'body does not exactly match',
 'late-library-release-recreated':'strictly later than the library release',
 'late-library-release-deleted':'Unable to read live library GitHub Release',
 'late-library-wrong-peel':'Library tag does not point at the validated merged SHA',
 'template-guard-missing-token':'wrong token'}

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo-root',type=Path,default=Path(__file__).resolve().parents[3])
    parser.add_argument('--self-test',action='store_true',help='Validator and reader tests only')
    args=parser.parse_args(); source=args.repo_root.resolve()
    with tempfile.TemporaryDirectory(prefix='sek-g122-') as temp:
        root=Path(temp)
        # Always run the incremental build: MSBuild detects missing/stale outputs,
        # including changed sources and project/import inputs, for every caller.
        build_host=root/'build-host'; build_host.mkdir()
        write_json(build_host/'global.json',{'sdk':{'version':'10.0.100','rollForward':'latestFeature','allowPrerelease':False}})
        print(run(['dotnet','build',str(source/REL/'Sekiban.Dcb.TemplateValidation.csproj'),
                   '-c','Release','--nologo','-p:NuGetAudit=false'],build_host),flush=True)
        for version in VERSIONS:
            f=Fixture(source,root/('pass-'+version),version)
            if version == '10.23.1': check_incomplete_history_drift(f)
            f.validate()
            assert f'repos/{HOST}/git/trees/{TREE}?recursive=1' not in (f.root/'api-trace').read_text().splitlines()
            print(f'PASS reader recursive listing truncated but path walk succeeds {version}',flush=True)
            print(f'PASS validator prepared {version} (older main ancestor; successful rerun attempt 2)',flush=True)
            f.record.update(stage='complete',published={key:f'https://github.com/{REPO}/releases/tag/{tag}' for key,tag in [('library_release_url',f'dcb-v{version}'),('template_release_url',f'dcbTemplates-v{version}')]}); f.save(); f.validate('complete')
            print(f'PASS validator complete {version}',flush=True)
            f.record.pop('published'); f.record['stage']='prepared'; f.save()
            if not args.self_test:
                for mode in ['library','template']: f.workflow(mode)
                print(f'PASS BOTH workflow guard sequences {version}: real schema-3 outputs; pack -> local validation -> simulated push -> visibility -> equality -> simulated Release',flush=True)
        if not args.self_test:
            f=Fixture(source,root/'retry-pass',VERSIONS[0]); f.env['GITHUB_RUN_ATTEMPT']='2'
            for mode in ['library','template']: f.workflow(mode)
            print('PASS BOTH workflows retry with same triggering tag object',flush=True)
        if not args.self_test: retry_cases(source, root)
        cases=RECORD_CASES if args.self_test else INTEGRATION_CASES
        for case,reason in cases.items():
            f=Fixture(source,root/case,VERSIONS[0])
            template_case=not args.self_test and (case.startswith('template-') or case.startswith('library-release-') or case.startswith('late-') or case=='authority-mismatch')
            if template_case: f.workflow('library')
            mutate(f,case)
            # Prove a failed validation removes stale output.
            if not template_case and case not in ['schema-2','wrong-record-version']: (f.temp/'validated-merged-sha.txt').write_text('stale\n')
            try:
                if args.self_test: f.validate()
                else:
                    if not template_case: f.workflow('library')
                    f.workflow('template')
            except Failure as error:
                if reason not in str(error): raise Failure(f'{case}: wrong failure (wanted {reason}): {error}')
                if case.startswith('late-') or case=='template-guard-missing-token':
                    assert 'Verify published library/template parity before pack' in f.workflow_trace
                    assert f.workflow_trace[-1]=='Enforce template live guard before push'
                    assert 'Push Template' not in f.workflow_trace
                if case in RECORD_CASES and (f.temp/'validated-merged-sha.txt').exists(): raise Failure('Stale validated output survived rejection')
                print(f'PASS rejects {case}: {reason}',flush=True)
            else: raise Failure(f'{case} unexpectedly passed')
        if args.self_test:
            for case,reason in [('draft-complete-release','non-draft'),('wrong-complete-url','non-draft'),('wrong-complete-peel','does not peel')]:
                f=Fixture(source,root/case,VERSIONS[0]); f.record.update(stage='complete',published={key:f'https://github.com/{REPO}/releases/tag/{tag}' for key,tag in [('library_release_url',f'dcb-v{f.version}'),('template_release_url',f'dcbTemplates-v{f.version}')]}); mutate(f,case)
                try: f.validate('complete')
                except Failure as error:
                    if reason not in str(error): raise
                    print(f'PASS rejects {case}: {reason}',flush=True)
                else: raise Failure(case+' unexpectedly passed')
        # Reader rejection coverage applies to both the full and self-test entry points.
        for case,reason in [('reader-blob-mismatch','record bytes do not match'),('reader-commit-mismatch','Host commit does not match'),('reader-tree-blob-mismatch','does not bind'),('reader-truncated-tree','Host tree is truncated'),('reader-intermediate-tree-mismatch','Host tree is truncated'),('reader-intermediate-wrong-type','does not bind')]:
            f=Fixture(source,root/case,VERSIONS[0])
            path=f'intents/sekiban/releases/dcb-v{f.version}-release-record.json'
            if case=='reader-blob-mismatch': f.api[f'repos/{HOST}/contents/{path}?ref={HOST_REF}']['sha']='c'*40
            elif case=='reader-commit-mismatch': f.api[f'repos/{HOST}/commits/{HOST_REF}']['sha']='c'*40
            elif case=='reader-tree-blob-mismatch': f.api[f'repos/{HOST}/git/trees/{PATH_TREES[-1]}']['tree'][0]['sha']='c'*40
            elif case=='reader-intermediate-tree-mismatch': f.api[f'repos/{HOST}/git/trees/{PATH_TREES[1]}']['sha']='c'*40
            elif case=='reader-intermediate-wrong-type': f.api[f'repos/{HOST}/git/trees/{PATH_TREES[1]}']['tree'][0]['type']='blob'
            else: f.api[f'repos/{HOST}/git/trees/{TREE}']['truncated']=True
            write_json(f.root/'api.json',f.api)
            try: f.validate()
            except Failure as error:
                if reason not in str(error): raise
                assert not (f.temp/'validated-merged-sha.txt').exists()
                print(f'PASS rejects {case}: {reason}',flush=True)
            else: raise Failure(case+' unexpectedly passed')
        for case,reason in [('validator-tree-chain-mismatch','Host tree is mismatched'),
                            ('validator-tree-chain-incomplete','Host tree chain is incomplete')]:
            f=Fixture(source,root/case,VERSIONS[0]); f.validate()
            path=f.temp/'dcb-release-record-bundle/tree.json'
            trees=json.loads(path.read_text())
            if case.endswith('incomplete'): trees.pop()
            else: trees[1]['sha']='c'*40
            write_json(path,trees)
            try: f.validate_bundle()
            except Failure as error:
                if reason not in str(error): raise
                assert not (f.temp/'validated-merged-sha.txt').exists()
                print(f'PASS rejects {case}: {reason}',flush=True)
            else: raise Failure(case+' unexpectedly passed')
    print('All offline tests passed.',flush=True)

if __name__=='__main__':
    try: main()
    except (Failure,AssertionError) as error: sys.exit(str(error))
