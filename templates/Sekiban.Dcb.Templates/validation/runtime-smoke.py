#!/usr/bin/env python3
"""Smoke a generated TemplateApp API in Development.

Usage: TEMPLATE_APP_DIR=/path/to/app TEMPLATE_EVENT_STORE_CONNECTION='...' \
    python3 runtime-smoke.py PORT TEMPLATE_KIND
The connection is required only for the three PostgreSQL-aware CLIs.
The two AWS templates ship a list-only CLI.
"""
import urllib.request,urllib.error,json,uuid,sys,time,subprocess,os
port=int(sys.argv[1]);kind=sys.argv[2];base=f'http://localhost:{port}';token=None
def request(method,path,body=None,expected=200):
 data=json.dumps(body).encode() if body is not None else None
 try:
  r=urllib.request.urlopen(urllib.request.Request(base+path,data=data,method=method,headers={'Content-Type':'application/json',**({'Authorization':'Bearer '+token} if token else {})}),timeout=50);status=r.status;raw=r.read().decode()
 except urllib.error.HTTPError as e:status=e.code;raw=e.read().decode()
 print(f'{method} {path}: {status}',flush=True)
 if status!=expected:print(raw[:500]);raise RuntimeError(f'Expected {expected}, got {status}')
 try:return json.loads(raw)
 except:return raw
token=None
if 'Decider' in kind:
 for i in range(40):
  try:
   login=request('POST','/auth/login',{'email':'admin@example.com','password':'Sekiban1234%','useCookies':False})
   token=login['accessToken'];break
  except Exception:
   if i==39:raise
   time.sleep(1)
 request('GET','/auth/me')
 request('POST','/api/test-data/generate')
 rooms=request('GET','/api/rooms');request('GET','/api/reservations');request('GET','/api/approvals');request('GET','/api/users')
 room=rooms[0]['roomId']
 import datetime,http.cookiejar
 start=datetime.datetime.now(datetime.timezone.utc)+datetime.timedelta(days=14)
 body={'roomId':room,'startTime':start.isoformat(),'endTime':(start+datetime.timedelta(hours=1)).isoformat(),'purpose':'G126 smoke'}
 reservation=request('POST','/api/reservations/quick',body)
 rp=reservation['sortableUniqueId'];rp=rp.get('value') if isinstance(rp,dict) else rp
 started=time.monotonic();listed=request('GET','/api/reservations?waitForSortableUniqueId='+str(rp));elapsed=time.monotonic()-started
 assert elapsed<2 and reservation['reservationId'] in json.dumps(listed),(elapsed,listed)
 print(f'LIVE reservations: {elapsed:.3f} s; new reservation present',flush=True)
 request('POST','/api/reservations/quick',body,400)
 jar=http.cookiejar.CookieJar();opener=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))
 r=opener.open(urllib.request.Request(base+'/auth/login',data=json.dumps({'email':'admin@example.com','password':'Sekiban1234%','useCookies':True}).encode(),headers={'Content-Type':'application/json'}));assert r.status==200
 r=opener.open(base+'/auth/me');assert r.status==200;print('Direct cookie login then /auth/me: 200',flush=True)
 r=opener.open(urllib.request.Request(base+'/auth/logout',data=b'',method='POST'));assert r.status==200
 try:opener.open(base+'/auth/me');raise RuntimeError('Cookie logout failed')
 except urllib.error.HTTPError as e:assert e.code==401;print('Cookie logout then /auth/me: 401',flush=True)
s=str(uuid.uuid4());c=str(uuid.uuid4());w=str(uuid.uuid4())
request('GET','/health')
request('GET','/api/students')
a=request('POST','/api/students',{'studentId':s,'name':'G126 Alice','maxClassCount':3})
ap=a['sortableUniqueId'];ap=ap.get('value') if isinstance(ap,dict) else ap
started=time.monotonic();listed=request('GET','/api/students?waitForSortableUniqueId='+str(ap));elapsed=time.monotonic()-started
assert elapsed<2 and s in json.dumps(listed),(elapsed,listed)
print(f'LIVE students: {elapsed:.3f} s; new student present',flush=True)
request('POST','/api/classrooms',{'classRoomId':c,'name':'G126 Physics','maxStudents':25})
e=request('POST','/api/enrollments/add',{'studentId':s,'classRoomId':c})
position=e['sortableUniqueId'];position=position.get('value') if isinstance(position,dict) else position
for path in ['/api/students','/api/classrooms','/api/mv/students','/api/mv/classrooms','/api/mv/enrollments']:
 data=request('GET',path+'?waitForSortableUniqueId='+str(position))
 if '/mv/' in path:
  assert s in json.dumps(data) or c in json.dumps(data),data
request('GET','/api/students/'+s);request('GET','/api/classrooms/'+c)
request('POST','/api/enrollments/add',{'studentId':s,'classRoomId':c},400)
request('POST','/api/students',{'studentId':s,'name':'Duplicate','maxClassCount':3},400)
request('POST','/api/classrooms',{'classRoomId':c,'name':'Duplicate','maxStudents':25},400)
request('POST','/api/enrollments/drop',{'studentId':s,'classRoomId':c})
request('POST','/api/enrollments/drop',{'studentId':s,'classRoomId':c},400)
request('GET','/api/mv/status')
wp=request('POST','/api/inputweatherforecast',{'forecastId':w,'location':'G126 Tokyo','date':'2026-10-06','temperatureC':{'value':22} if kind=='Sekiban.Dcb.Orleans' else 22,'summary':'Clear'})['sortableUniqueId']
wp=wp.get('value') if isinstance(wp,dict) else wp
request('GET','/api/weatherforecast?waitForSortableUniqueId='+str(wp))
for i in range(30):
 data=request('GET','/api/weatherforecast-uwmv')
 if w in json.dumps(data):break
 time.sleep(1)
else:raise RuntimeError('UWMV did not reflect weather')
request('GET','/api/debug/events')
request('POST','/api/projections/persist?name=WeatherForecastProjection')
request('GET','/api/projections/snapshot?name=WeatherForecastProjection')
try:
 urllib.request.urlopen(urllib.request.Request(base+'/api/students',data=b'{"studentId":"x"',method='POST',headers={'Content-Type':'application/json'}))
 raise AssertionError('Malformed JSON was accepted')
except urllib.error.HTTPError as e:
 print('Malformed JSON:',e.code);assert e.code==400
if 'Aws' not in kind:
 env={**os.environ,'ConnectionStrings__DcbPostgres':os.environ['TEMPLATE_EVENT_STORE_CONNECTION'],'DOTNET_USE_POLLING_FILE_WATCHER':'true','DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE':'false'}
 app=os.environ['TEMPLATE_APP_DIR'];result=subprocess.run(['dotnet','run','--project','TemplateApp.Cli','--no-build','--','status','--cache-mode','off','--connection-string',env['ConnectionStrings__DcbPostgres']],cwd=app,env=env,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,timeout=30)
 print(result.stdout[-1500:]);assert result.returncode==0;assert 'Total Events in Store: 0' not in result.stdout and 'Total Events in Store:' in result.stdout,result.stdout
else:
 app=os.environ['TEMPLATE_APP_DIR'];result=subprocess.run(['dotnet','run','--project','TemplateApp.Cli','--no-build','--','list'],cwd=app,stdout=subprocess.PIPE,stderr=subprocess.STDOUT,text=True,timeout=30)
 assert result.returncode==0 and 'Projectors' in result.stdout,result.stdout
 print('AWS list-only CLI: passed',flush=True)
print(kind+' smoke passed',flush=True)
