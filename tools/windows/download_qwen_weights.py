import sys
from pathlib import Path
sys.path[0]=str(Path(sys.executable).parent)
import concurrent.futures,hashlib,os,time,urllib.request
root=Path(os.environ['LOCALAPPDATA'])/'DeepBrain/downloads'
url='https://registry.ollama.ai/v2/library/qwen3.5/blobs/sha256:02d45dc1cf451ba2475ac33b301c2dd8f985abe4c182ce04a1f2f5bf0260278d'
size=5629109120
target=root/'qwen35-9b-verified.gguf'
parts=root/'qwen35-9b-parts';parts.mkdir(exist_ok=True)
count=32;step=(size+count-1)//count
def download(i):
    start=i*step;end=min(size,start+step)-1;p=parts/str(i)
    if p.exists() and p.stat().st_size==end-start+1:return i
    for attempt in range(8):
        try:
            offset=p.stat().st_size if p.exists() else 0
            if offset==end-start+1:return i
            if offset>end-start+1:raise RuntimeError('Unexpected part size')
            actual_start=start+offset
            req=urllib.request.Request(url,headers={'Range':f'bytes={actual_start}-{end}','User-Agent':'DeepBrain-local-setup'})
            with urllib.request.urlopen(req,timeout=45) as r:
                if r.status!=206 or r.headers.get('Content-Range')!=f'bytes {actual_start}-{end}/{size}':raise RuntimeError('Range not honored')
                with p.open('ab') as f:
                    while chunk:=r.read(1024*1024):f.write(chunk)
            if p.stat().st_size!=end-start+1:raise RuntimeError('Short range')
            return i
        except Exception:
            if attempt==7:raise
            time.sleep(2)
with concurrent.futures.ThreadPoolExecutor(max_workers=16) as pool:
    futures=[pool.submit(download,i) for i in range(count)]
    while not all(f.done() for f in futures):
        total=sum(p.stat().st_size for p in parts.iterdir() if p.is_file())
        print(f'Qwen model download: {total/size:.0%} ({total//1048576}/{size//1048576} MiB)',flush=True)
        time.sleep(10)
    for f in futures:f.result()
temporary=target.with_suffix('.complete')
digest=hashlib.sha256()
with temporary.open('wb') as out:
    for i in range(count):
        with (parts/str(i)).open('rb') as source:
            while chunk:=source.read(4*1024*1024):out.write(chunk);digest.update(chunk)
expected='02d45dc1cf451ba2475ac33b301c2dd8f985abe4c182ce04a1f2f5bf0260278d'
assert digest.hexdigest()==expected,'Checksum mismatch'
os.replace(temporary,target)
print('Official SHA256 verified: '+digest.hexdigest(),flush=True)
