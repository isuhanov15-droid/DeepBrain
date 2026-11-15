import sys
from pathlib import Path
sys.path[0]=str(Path(sys.executable).parent)
import concurrent.futures,hashlib,os,time,urllib.request
root=Path(os.environ['LOCALAPPDATA'])/'DeepBrain/downloads'
url='https://github.com/ollama/ollama/releases/download/v0.40.16/ollama-windows-amd64.zip'
size=1618244995
target=root/'ollama-windows-amd64-v0.40.16.zip'
parts=root/'parts-v0.40.16';parts.mkdir(exist_ok=True)
count=16;step=(size+count-1)//count
def download(i):
    start=i*step;end=min(size,start+step)-1;p=parts/str(i)
    if p.exists() and p.stat().st_size==end-start+1:return i
    for attempt in range(4):
        try:
            req=urllib.request.Request(url,headers={'Range':f'bytes={start}-{end}','User-Agent':'DeepBrain-local-setup'})
            with urllib.request.urlopen(req,timeout=45) as r:
                if r.status!=206 or r.headers.get('Content-Range')!=f'bytes {start}-{end}/{size}':raise RuntimeError('Range not honored')
                with p.open('wb') as f:
                    while chunk:=r.read(1024*1024):f.write(chunk)
            if p.stat().st_size!=end-start+1:raise RuntimeError('Short range')
            return i
        except Exception:
            if attempt==3:raise
            time.sleep(2)
with concurrent.futures.ThreadPoolExecutor(max_workers=count) as pool:
    futures=[pool.submit(download,i) for i in range(count)]
    while not all(f.done() for f in futures):
        total=sum(p.stat().st_size for p in parts.iterdir() if p.is_file())
        print(f'Ollama download: {total/size:.0%} ({total//1048576}/{size//1048576} MiB)',flush=True)
        time.sleep(10)
    for f in futures:f.result()
temporary=target.with_suffix('.complete')
digest=hashlib.sha256()
with temporary.open('wb') as out:
    for i in range(count):
        with (parts/str(i)).open('rb') as source:
            while chunk:=source.read(4*1024*1024):out.write(chunk);digest.update(chunk)
expected=next(line.split()[0] for line in (root/'sha256sum-v0.40.16.txt').read_text().splitlines() if line.endswith(' ./ollama-windows-amd64.zip'))
assert digest.hexdigest()==expected,'Checksum mismatch'
os.replace(temporary,target)
print('Official SHA256 verified: '+digest.hexdigest(),flush=True)
