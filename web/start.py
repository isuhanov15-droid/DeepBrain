#!/usr/bin/env python3
import json,os,secrets,shlex,subprocess,sys,time,urllib.request,base64,socket
from pathlib import Path
os.umask(0o077);base=Path(__file__).resolve().parent
settings=Path('/home/ivan/deepbrain-web-data/connection.json')
connection=json.loads(settings.read_text()) if settings.exists() else {}
upstream=connection.get('upstream','http://127.0.0.1:8787').rstrip('/')
assert upstream.startswith('http://127.0.0.1:'),'Expected local DeepBrain API'
request=urllib.request.Request(upstream+'/api/v1/system/status')
if os.environ.get('DEEPBRAIN_API_TOKEN'):request.add_header('Authorization','Bearer '+os.environ['DEEPBRAIN_API_TOKEN'])
with urllib.request.urlopen(request,timeout=10) as response:status=json.load(response)
assert status['api']['listening'],'DeepBrain API is not ready'
journal=status.get('llm',{}).get('journal',{}).get('path')
if not journal or not Path(journal).is_absolute():raise SystemExit('STOP: API has no absolute journal path')
epoch=Path(journal).parent.parent
if len(sys.argv)>1 and Path(sys.argv[1]).resolve()!=epoch:raise SystemExit('STOP: supplied epoch differs from live API')
settings.parent.mkdir(parents=True,exist_ok=True)
settings.write_text(json.dumps({'upstream':upstream,'epoch':str(epoch)},indent=2)+'\n')
print('Live epoch:',epoch,flush=True)
if subprocess.run(['tmux','has-session','-t','=deepbrain-web'],stderr=subprocess.DEVNULL).returncode==0:raise SystemExit('Web already running. No restart performed.')
with socket.socket() as probe:
 probe.setsockopt(socket.SOL_SOCKET,socket.SO_REUSEADDR,1)
 try:probe.bind(('0.0.0.0',8788))
 except OSError:raise SystemExit('STOP: port 8788 is occupied; existing service was not changed')
data=Path('/home/ivan/deepbrain-web-data');data.mkdir(exist_ok=True)
password=data/'password.txt'
if not password.exists():password.write_text(secrets.token_urlsafe(24)+'\n')
os.chmod(password,0o600)
args=[sys.executable,str(base/'server.py'),'--data',str(data),'--epoch',epoch.name,'--upstream',upstream]
command='exec '+shlex.join(args)+' >> '+shlex.quote(str(data/'web.log'))+' 2>&1'
# Pass upstream secret via inherited environment in tmux, never as a URL or log entry.
cmd=['tmux','new-session','-d','-s','deepbrain-web','-c',str(base)]
for name in ('DEEPBRAIN_API_TOKEN','DEEPBRAIN_YANDEX_CLIENT_ID','DEEPBRAIN_YANDEX_CLIENT_SECRET'):
 if os.environ.get(name):cmd+=['-e',name+'='+os.environ[name]]
subprocess.run(cmd+[command],check=True)
for _ in range(50):
 try:
  req=urllib.request.Request('http://127.0.0.1:8788/web/snapshot');req.add_header('Authorization','Basic '+base64.b64encode(('ivan:'+password.read_text().strip()).encode()).decode())
  with urllib.request.urlopen(req,timeout=1) as r:
   if r.status==200:break
 except OSError:time.sleep(.2)
else:raise SystemExit('Web startup failed. See '+str(data/'web.log'))
print('WEB STARTED. DeepBrain/ML were not restarted.\nLogin: ivan\nPassword file: '+str(password))
print('Addresses:')
for ip in subprocess.check_output(['hostname','-I'],text=True).split():
 if ':' not in ip:print('http://'+ip+':8788')
