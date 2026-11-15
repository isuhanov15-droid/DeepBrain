"""Windows per-user local Qwen and loopback-only reverse SSH tunnel supervisor."""
import os,sys
from pathlib import Path
ROOT=Path(os.environ['LOCALAPPDATA'])/'DeepBrain'
# Safe when launched from the recovery source directory too.
sys.path[0]=str(Path(sys.executable).parent)
import atexit,json,msvcrt,subprocess,time,urllib.request

ROOT.mkdir(exist_ok=True)
lock=(ROOT/'qwen-supervisor.lock').open('a+b')
lock.seek(0);lock.write(b'1');lock.flush();lock.seek(0)
try:msvcrt.locking(lock.fileno(),msvcrt.LK_NBLCK,1)
except OSError:sys.exit(0)
stop=ROOT/'qwen-stop'
if stop.exists():stop.unlink()
logs=ROOT/'logs';logs.mkdir(exist_ok=True)
children={};streams={}
def launch(name,args,env=None):
    path=logs/(name+'.log')
    if path.exists() and path.stat().st_size>5*1024*1024:
        os.replace(path,path.with_suffix('.previous.log'))
    if name in streams:streams[name].close()
    streams[name]=path.open('ab')
    children[name]=subprocess.Popen(args,stdin=subprocess.DEVNULL,stdout=streams[name],stderr=subprocess.STDOUT,
        env=env,creationflags=subprocess.CREATE_NO_WINDOW,cwd=ROOT)
def cleanup():
    for child in children.values():
        if child.poll() is None:child.terminate()
    for child in children.values():
        try:child.wait(timeout=8)
        except subprocess.TimeoutExpired:child.kill()
atexit.register(cleanup)
env=os.environ.copy()
env.update(OLLAMA_HOST='127.0.0.1:11434',OLLAMA_NO_CLOUD='1',OLLAMA_CONTEXT_LENGTH='16384',
           OLLAMA_NUM_PARALLEL='1',OLLAMA_MAX_LOADED_MODELS='1',OLLAMA_KEEP_ALIVE='10m')
ssh=Path(os.environ['WINDIR'])/'System32/OpenSSH/ssh.exe'
keys=Path.home()/'.ssh'
ssh_args=[str(ssh),'-N','-T','-o','BatchMode=yes','-o','IdentitiesOnly=yes','-o','StrictHostKeyChecking=yes',
    '-o','KexAlgorithms=curve25519-sha256','-o','ExitOnForwardFailure=yes','-o','ConnectTimeout=8',
    '-o','ServerAliveInterval=15','-o','ServerAliveCountMax=3',
    '-o','UserKnownHostsFile='+str(Path(os.environ.get('DEEPBRAIN_KNOWN_HOSTS',str(keys/'known_hosts')))),
    '-i',str(keys/'deepbrain-qwen-tunnel'),'-p',os.environ.get('DEEPBRAIN_SSH_PORT','28534'),'-R','127.0.0.1:11435:127.0.0.1:11434',os.environ['DEEPBRAIN_SSH_TARGET']]
last={}
while not stop.exists():
    now=time.monotonic()
    for name,args,childenv in [('ollama',[str(ROOT/'ollama/ollama.exe'),'serve'],env),('tunnel',ssh_args,None)]:
        if name not in children or children[name].poll() is not None:
            if now-last.get(name,-100)>10:
                launch(name,args,childenv);last[name]=now
    status={'supervisorPid':os.getpid(),'updatedAt':time.time(),
            'processes':{name:{'pid':p.pid,'running':p.poll() is None,'exitCode':p.poll()} for name,p in children.items()}}
    temp=ROOT/'qwen-status.tmp';temp.write_text(json.dumps(status),encoding='utf-8');os.replace(temp,ROOT/'qwen-status.json')
    time.sleep(1)
