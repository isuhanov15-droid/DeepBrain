#!/usr/bin/env python3
"""Update only Web, retaining agent data, Web history and password."""
import base64,hashlib,json,os,shutil,subprocess,sys,tempfile,time,urllib.request
from pathlib import Path
os.umask(0o077)
base=Path(__file__).resolve().parent
target=Path(sys.argv[1] if len(sys.argv)>1 else '/home/ivan/deepbrain-web-01').resolve()
files=['server.py','start.py','dist/index.html','dist/app.js','dist/style.css','openai_dialogue.py','setup_openai.py','alice_bridge.py','setup_alice.py','station_delivery.py','station_transport.py','setup_station.py','station_qr.py','station_oauth.py','voice_protocol.py','dialogue_memory.py','lada_face.py','station_face.py','conversation_bridge.py','semantic_appraisal.py']
def session():return subprocess.run(['tmux','has-session','-t','=deepbrain-web'],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL).returncode==0
def web_pane():
 rows=subprocess.check_output(['tmux','list-panes','-s','-t','=deepbrain-web','-F','#{pane_id}\t#{pane_pid}'],text=True).strip().splitlines()
 if len(rows)!=1:raise RuntimeError('Expected exactly one Web pane; nothing changed')
 pane,pid=rows[0].split('\t')
 if not pane.startswith('%') or not pid.isdigit():raise RuntimeError('Cannot identify Web process; nothing changed')
 proc=Path('/proc')/pid
 args=[os.fsdecode(v) for v in (proc/'cmdline').read_bytes().split(b'\0') if v]
 scripts=[]
 for arg in args[1:]:
  if Path(arg).name=='server.py':
   script=Path(arg)
   if not script.is_absolute():script=(proc/'cwd').resolve(strict=True)/script
   scripts.append(script.resolve())
 if scripts!=[(target/'server.py').resolve()]:raise RuntimeError('Web process does not match '+str(target/'server.py')+'; nothing changed')
 return pane
def stop():
 if session():
  pane=web_pane()
  subprocess.run(['tmux','send-keys','-t',pane,'C-c'],check=True)
  for _ in range(60):
   if not session():return
   time.sleep(.2)
  raise RuntimeError('Web did not stop. DeepBrain/ML untouched.')
def confirm_version():
 password=Path('/home/ivan/deepbrain-web-data/password.txt').read_text().strip()
 for _ in range(30):
  req=urllib.request.Request('http://127.0.0.1:8788/web/snapshot')
  req.add_header('Authorization','Basic '+base64.b64encode(('ivan:'+password).encode()).decode())
  try:
   with urllib.request.urlopen(req,timeout=2) as r:data=json.load(r)
   if data.get('version')=='0.17.1':
    print('VERIFIED: running Web version 0.17.1',flush=True)
    return
  except OSError:pass
  time.sleep(.5)
 raise RuntimeError('Running Web did not confirm version 0.17.1')
def main():
 if not (target/'server.py').is_file() or not (target/'start.py').is_file():raise RuntimeError('Existing Web not found: '+str(target))
 if target==base:raise RuntimeError('Target must be existing Web folder, not update package')
 hashes=json.loads((base/'manifest.json').read_text())
 for name in files:
  if hashlib.sha256((base/name).read_bytes()).hexdigest()!=hashes[name]:raise RuntimeError('Package checksum mismatch: '+name)
 connection=Path('/home/ivan/deepbrain-web-data/connection.json')
 cfg=json.loads(connection.read_text()) if connection.exists() else {}
 upstream=cfg.get('upstream','http://127.0.0.1:8787').rstrip('/')
 if not upstream.startswith('http://127.0.0.1:'):raise RuntimeError('Expected local API')
 req=urllib.request.Request(upstream+'/api/v1/system/status')
 if os.environ.get('DEEPBRAIN_API_TOKEN'):req.add_header('Authorization','Bearer '+os.environ['DEEPBRAIN_API_TOKEN'])
 with urllib.request.urlopen(req,timeout=10) as r:status=json.load(r)
 journal=status.get('llm',{}).get('journal',{}).get('path')
 if not status.get('api',{}).get('listening') or not journal or not Path(journal).is_absolute():raise RuntimeError('Live API has no valid journal path; update not started')
 if session():web_pane()
 backup=Path(tempfile.mkdtemp(prefix='deepbrain-web0171-backup-',dir=target.parent))
 for name in files:
  dest=backup/name;dest.parent.mkdir(parents=True,exist_ok=True)
  if (target/name).exists():shutil.copy2(target/name,dest)
 print('BACKUP='+str(backup),flush=True)
 stop()
 try:
  for name in files:
   dest=target/name;temporary=dest.with_name(dest.name+'.web0171-new');shutil.copy2(base/name,temporary);os.replace(temporary,dest)
  subprocess.run([sys.executable,str(target/'start.py')],check=True)
  confirm_version()
 except Exception:
  stop()
  for name in files:
   if (backup/name).exists():shutil.copy2(backup/name,target/name)
  print('Previous Web files restored. Trying to restart previous Web.',flush=True)
  subprocess.run([sys.executable,str(target/'start.py')])
  raise
 print('WEB 0.17.1 INSTALLED. Navigation, password, history and agent data preserved.',flush=True)
if __name__=='__main__':
 try:main()
 except Exception as ex:print('STOP: '+str(ex),file=sys.stderr);sys.exit(1)
