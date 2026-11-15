from pathlib import Path
import sys
sys.path[0]=str(Path(sys.executable).parent)
import json,time,urllib.request
request=urllib.request.Request('http://127.0.0.1:11434/api/pull',data=json.dumps({'model':'qwen3.5:9b-q4_K_M','stream':True}).encode(),headers={'Content-Type':'application/json'})
opener=urllib.request.build_opener(urllib.request.ProxyHandler({}))
last=0
with opener.open(request,timeout=180) as response:
    for line in response:
        value=json.loads(line)
        if value.get('error'):raise RuntimeError(value['error'])
        if time.monotonic()-last>10 or value.get('status')=='success':
            total=value.get('total',0);completed=value.get('completed',0)
            print(value.get('status','')+(f': {completed/total:.0%} ({completed//1048576}/{total//1048576} MiB)' if total else ''),flush=True)
            last=time.monotonic()
        final=value
assert final.get('status')=='success'
print('Qwen pull and verification complete',flush=True)
