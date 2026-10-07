#!/usr/bin/env python3
"""Check Production route exposure and configured initial-admin login.

Usage: python3 production-smoke.py PORT TEMPLATE_KIND
Configure Auth:InitialAdmin to initialadmin@g126.example / Initial1234%
in an isolated smoke environment before starting the generated API.
"""
import urllib.request,urllib.error,json,sys,time,http.cookiejar
port=sys.argv[1];kind=sys.argv[2];base='http://localhost:'+port
email='initialadmin@g126.example';password='Initial1234%'
def req(method,path,body=None,token=None):
 try:
  r=urllib.request.urlopen(urllib.request.Request(base+path,data=json.dumps(body).encode() if body is not None else None,method=method,headers={'Content-Type':'application/json',**({'Authorization':'Bearer '+token} if token else {})}),timeout=20)
  return r.status,r.read().decode()
 except urllib.error.HTTPError as e:return e.code,e.read().decode()
for method,path in [('GET','/api/debug/events'),('POST','/api/projections/persist?name=WeatherForecastProjection'),('GET','/api/projections/snapshot?name=WeatherForecastProjection'),('POST','/api/projections/deactivate?name=WeatherForecastProjection'),('POST','/api/projections/refresh?name=WeatherForecastProjection'),('POST','/api/projections/overwrite-version?name=WeatherForecastProjection&newVersion=1.0.0'),('POST','/api/test-data/generate')]:
 status,body=req(method,path);print(method,path,status,flush=True);assert status==404,(status,body)
if 'Decider' in kind:
 for i in range(40):
  status,body=req('POST','/auth/login',{'email':email,'password':password,'useCookies':False})
  if status==200:break
  time.sleep(1)
 assert status==200,(status,body)
 token=json.loads(body)['accessToken'];status,body=req('GET','/auth/me',token=token);assert status==200 and 'Admin' in body;print('Configured administrator token login and /auth/me: 200 Admin',flush=True)
 status,body=req('POST','/auth/login',{'email':'admin@example.com','password':'Sekiban1234%','useCookies':False});assert status==401;print('Sample administrator absent: 401',flush=True)
 jar=http.cookiejar.CookieJar();opener=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))
 r=opener.open(urllib.request.Request(base+'/auth/login',data=json.dumps({'email':email,'password':password,'useCookies':True}).encode(),headers={'Content-Type':'application/json'}));assert r.status==200
 assert opener.open(base+'/auth/me').status==200;print('Production direct cookie /auth/me: 200',flush=True)
print(kind+' Production smoke passed',flush=True)
