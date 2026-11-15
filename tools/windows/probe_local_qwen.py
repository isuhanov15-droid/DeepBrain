from pathlib import Path
import sys
sys.path[0]=str(Path(sys.executable).parent)
import json,time,urllib.request
model='qwen3.5:9b-q4_K_M'
opener=urllib.request.build_opener(urllib.request.ProxyHandler({}))
def api(path,body=None):
    req=urllib.request.Request('http://127.0.0.1:11434/api/'+path,headers={'Content-Type':'application/json'},
        data=json.dumps(body,ensure_ascii=False).encode() if body is not None else None)
    with opener.open(req,timeout=120) as r:return json.load(r)
show=api('show',{'model':model})
print(json.dumps({'model':model,'capabilities':show.get('capabilities'),'details':show.get('details')},ensure_ascii=False),flush=True)
started=time.monotonic()
value=api('chat',{'model':model,'messages':[{'role':'system','content':'Отвечай кратко по-русски.'},{'role':'user','content':'Скажи: Привет, Иван! Локальный Qwen готов к диалогу.'}],
    'stream':False,'think':False,'keep_alive':'10m','options':{'num_ctx':16384,'num_predict':256}})
print(json.dumps({'done':value.get('done'),'doneReason':value.get('done_reason'),'answer':value.get('message',{}).get('content'),
    'elapsedSeconds':round(time.monotonic()-started,2),'outputTokens':value.get('eval_count'),
    'tokensPerSecond':round(value.get('eval_count',0)/(value.get('eval_duration',1)/1e9),1)},ensure_ascii=False),flush=True)
assert value.get('done') and value.get('message',{}).get('content')
print(json.dumps(api('ps'),ensure_ascii=False),flush=True)
