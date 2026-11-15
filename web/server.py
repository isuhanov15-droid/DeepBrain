#!/usr/bin/env python3
"""DeepBrain Web 0.17. Explicit local or OpenAI dialogue; conversation observations are delivered locally."""
import argparse,base64,hashlib,hmac,json,os,secrets,sqlite3,threading,time,urllib.request,urllib.error
from http.server import BaseHTTPRequestHandler,ThreadingHTTPServer
from pathlib import Path
from urllib.parse import urlsplit,parse_qs
from openai_dialogue import Dialogue,DialogueError
from alice_bridge import AliceBridge,AliceHTTPServer
from contextlib import contextmanager
from conversation_bridge import ConversationBridge

@contextmanager
def database(path):
 con=sqlite3.connect(path)
 try:
  with con:yield con
 finally:con.close()

class Studio:
 def __init__(self,data,epoch,upstream,password):
  self.data=data;self.epoch=epoch;self.upstream=upstream.rstrip('/');self.password=password
  self.lock=threading.Lock();self.snapshot={};self.stop=threading.Event();self.csrf=secrets.token_urlsafe(32)
  self.db=data/'history.sqlite3'
  self.dialogue=Dialogue(data,self.dialogue_snapshot)
  self.alice=AliceBridge(data,self.dialogue)
  self.conversation=ConversationBridge(self.dialogue,self.upstream,self.stop)
  with database(self.db) as c:
   c.execute('CREATE TABLE IF NOT EXISTS samples (id INTEGER PRIMARY KEY, epoch TEXT, at REAL, tick INTEGER, payload TEXT)')
   c.execute('CREATE INDEX IF NOT EXISTS samples_epoch_at ON samples(epoch,at)')
 def dialogue_snapshot(self):
  with self.lock:return dict(self.snapshot)
 def fetch(self,path,post=False):
  headers={};token=os.environ.get('DEEPBRAIN_API_TOKEN')
  if token:headers['Authorization']='Bearer '+token
  req=urllib.request.Request(self.upstream+'/api/v1/'+path,headers=headers,method='POST' if post else 'GET',data=b'' if post else None)
  with urllib.request.urlopen(req,timeout=3) as r:return json.load(r)
 def collect(self):
  with database(self.db) as c:row=c.execute('SELECT epoch,tick FROM samples WHERE epoch=? OR epoch LIKE ? ORDER BY id DESC LIMIT 1',(self.epoch,self.epoch+'@%')).fetchone()
  previous=row[1] if row else None
  if row:self.epoch=row[0]
  last_tick_at=time.time();last_journal=0;last_store=0
  while not self.stop.is_set():
   with self.lock:out=dict(self.snapshot)
   errors={}
   for key,path in [('state','state'),('status','system/status'),('thought','llm/last')]:
    try:out[key]=self.fetch(path);out[key+'At']=time.time()
    except Exception as ex:errors[key]=str(ex)
   if time.time()-last_journal>=30:
    try:out['journal']=self.fetch('llm/journal?count=50');out['journalAt']=time.time();last_journal=time.time()
    except Exception as ex:errors['journal']=str(ex)
   state=out.get('state') or {};tick=state.get('tick');now=time.time()
   if 'state' not in errors and isinstance(tick,int):
    if previous is not None and tick<previous:self.epoch=self.epoch.split('@')[0]+'@'+str(int(now))
    if tick!=previous:last_tick_at=now
    previous=tick
    if now-last_store>=10:
     metric={**state.get('homeostasis',{}),'q':state.get('ml',{}).get('avgQ'),'loss':state.get('ml',{}).get('lastLoss'),'trainSteps':state.get('ml',{}).get('trainSteps')}
     try:
      with database(self.db) as c:
       c.execute('INSERT INTO samples(epoch,at,tick,payload) VALUES(?,?,?,?)',(self.epoch,now,tick,json.dumps(metric,allow_nan=False)))
       c.execute('DELETE FROM samples WHERE at<?',(now-7*86400,))
     except Exception as ex:errors['history']=str(ex)
     last_store=now
   out.update(epoch=self.epoch,errors=errors,collectedAt=now,lastTickAt=last_tick_at,csrf=self.csrf,version='0.17.0')
   with self.lock:self.snapshot=out
   self.stop.wait(5)
 def history(self,seconds=86400):
  with database(self.db) as c:rows=c.execute('SELECT at,tick,payload FROM samples WHERE epoch=? AND at>? ORDER BY id',(self.epoch,time.time()-seconds)).fetchall()
  stride=max(1,(len(rows)+719)//720)
  selected=rows[::stride]
  if rows and selected[-1]!=rows[-1]:selected.append(rows[-1])
  return [dict(at=a,tick=t,**json.loads(p)) for a,t,p in selected]

class Handler(BaseHTTPRequestHandler):
 def log_message(self,*args):pass
 def reply(self,code,data,mime='application/json; charset=utf-8'):
  body=json.dumps(data,ensure_ascii=False).encode() if not isinstance(data,bytes) else data
  self.send_response(code);self.send_header('Content-Type',mime);self.send_header('Content-Length',str(len(body)))
  self.send_header('Cache-Control','no-store');self.send_header('X-Content-Type-Options','nosniff');self.send_header('X-Frame-Options','DENY')
  self.send_header('Content-Security-Policy',"default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; frame-ancestors 'none'")
  self.end_headers();self.wfile.write(body)
 def auth(self):
  expected='Basic '+base64.b64encode(('ivan:'+self.server.studio.password).encode()).decode()
  if not hmac.compare_digest(self.headers.get('Authorization',''),expected):
   self.send_response(401);self.send_header('WWW-Authenticate','Basic realm="DeepBrain", charset="UTF-8"');self.send_header('Content-Length','0');self.end_headers();return False
  return True
 def do_GET(self):
  if not self.auth():return
  path=urlsplit(self.path).path;s=self.server.studio
  if path=='/web/snapshot':
   with s.lock:out=dict(s.snapshot)
   return self.reply(200,out)
  if path=='/web/dialogue':
   try:return self.reply(200,s.dialogue.view())
   except Exception:return self.reply(503,{'error':'Не удалось открыть локальную историю диалога.'})
  if path=='/web/alice':return self.reply(200,s.alice.status())
  if path=='/web/lada':
   face=getattr(s.alice,'face',None)
   return self.reply(200,{'memory':s.dialogue.memory_view(),'face':face.status() if face else {},'experience':s.conversation.status()})
  if path=='/web/dialogue/archive':
   try:return self.reply(200,s.dialogue.archive(int(parse_qs(urlsplit(self.path).query).get('before',['0'])[0])))
   except ValueError:return self.reply(400,{'error':'Некорректная страница архива.'})
  if path=='/web/alice/qr.svg':
   identifier=parse_qs(urlsplit(self.path).query).get('id',[''])[0]
   qr=s.alice.delivery.qr_svg(identifier)
   return self.reply(200,qr,'image/svg+xml') if qr else self.reply(404,{'error':'QR-код истёк. Получи новый код в разделе Диалог.'})
  if path=='/web/history':
   period=parse_qs(urlsplit(self.path).query).get('range',['24h'])[0]
   periods={'15m':900,'1h':3600,'6h':21600,'24h':86400}
   if period not in periods:return self.reply(400,{'error':'Unknown history range'})
   return self.reply(200,s.history(periods[period]))
  assets={'/':('index.html','text/html; charset=utf-8'),'/app.js':('app.js','text/javascript; charset=utf-8'),'/style.css':('style.css','text/css; charset=utf-8')}
  if path in assets:
   name,mime=assets[path];return self.reply(200,(Path(__file__).parent/'dist'/name).read_bytes(),mime)
  self.reply(404,{'error':'Not found'})
 def do_POST(self):
  if not self.auth():return
  s=self.server.studio
  if self.path not in ('/web/observe','/web/dialogue/message','/web/dialogue/new','/web/alice/pair','/web/alice/automatic','/web/alice/authorize','/web/alice/authorize/refresh'):return self.reply(404,{'error':'Not found'})
  if not hmac.compare_digest(self.headers.get('X-CSRF-Token',''),s.csrf):return self.reply(403,{'error':'Refresh page'})
  if self.path in ('/web/alice/authorize','/web/alice/authorize/refresh'):
   try:return self.reply(202,s.alice.delivery.begin_authorization(refresh=self.path.endswith('/refresh')))
   except (ValueError,OSError,TypeError):return self.reply(409,{'error':'Не удалось начать вход. Проверь состояние подключения Станции.'})
  if self.path=='/web/alice/automatic':
   try:
    if self.headers.get_content_type()!='application/json' or self.headers.get('Transfer-Encoding') or len(self.headers.get_all('Content-Length',[]))!=1:raise ValueError('Ожидается настройка JSON.')
    length=int(self.headers['Content-Length'])
    if not 0<length<=128:raise ValueError('Слишком большой запрос.')
    self.connection.settimeout(3);v=json.loads(self.rfile.read(length))
    if not isinstance(v,dict) or set(v)!={'enabled'}:raise ValueError('Некорректная настройка.')
    return self.reply(200,s.alice.delivery.set_enabled(v['enabled']))
   except (ValueError,OSError) as ex:return self.reply(400,{'error':'Не удалось изменить автоозвучивание. Проверь подключение Станции.'})
  if self.path=='/web/alice/pair':
   try:return self.reply(200,s.alice.pair_code())
   except ValueError as ex:return self.reply(409,{'error':str(ex)})
  if self.path.startswith('/web/dialogue/'):
   try:
    if self.path=='/web/dialogue/new':return self.reply(200,s.dialogue.new_conversation())
    if self.headers.get_content_type()!='application/json' or self.headers.get('Transfer-Encoding'):
     return self.reply(400,{'error':'Ожидается JSON сообщения.'})
    length=int(self.headers.get('Content-Length','0'))
    if not 0<length<=20000:return self.reply(413,{'error':'Сообщение слишком большое или пустое.'})
    self.connection.settimeout(10)
    raw=self.rfile.read(length)
    if len(raw)!=length:return self.reply(400,{'error':'Сообщение передано не полностью.'})
    value=json.loads(raw)
    if not isinstance(value,dict) or set(value)!={'requestId','conversationId','message'}:
     return self.reply(400,{'error':'Некорректный запрос диалога.'})
    result=s.dialogue.send(value['requestId'],value['conversationId'],value['message'])
    return self.reply(200 if result['status']=='completed' else 502,result)
   except DialogueError as ex:return self.reply(ex.status,{'error':str(ex),'errorCode':ex.code})
   except (ValueError,UnicodeError):return self.reply(400,{'error':'Некорректное сообщение.'})
   except Exception:return self.reply(503,{'error':'Не удалось сохранить или получить ответ. Обнови историю перед повторной отправкой.'})
  try:
   value=s.fetch('llm/observe',True)
   try:
    with (s.data/'commands.jsonl').open('a') as f:f.write(json.dumps({'at':time.time(),'epoch':s.epoch,'command':'llm.observe','result':value})+'\n')
   except OSError:value['auditError']='Запрос принят, но журнал команд не сохранён'
   self.reply(202,value)
  except urllib.error.HTTPError as ex:self.reply(ex.code,{'error':'LLM занят или недоступен; запрос не принят'})
  except Exception:self.reply(502,{'error':'Нет связи с DeepBrain'})

def main():
 p=argparse.ArgumentParser();p.add_argument('--data',required=True);p.add_argument('--epoch',required=True);p.add_argument('--host',default='0.0.0.0');p.add_argument('--port',type=int,default=8788);p.add_argument('--upstream',default='http://127.0.0.1:8787');a=p.parse_args()
 os.umask(0o077);data=Path(a.data).resolve();data.mkdir(parents=True,exist_ok=True)
 password=(data/'password.txt').read_text().strip();assert len(password)>=20,'password too short'
 s=Studio(data,a.epoch,a.upstream,password);http=ThreadingHTTPServer((a.host,a.port),Handler);http.studio=s
 voice=AliceHTTPServer(('127.0.0.1',8789),s.alice)
 s.conversation.start()
 s.alice.start();threading.Thread(target=voice.serve_forever,daemon=True).start()
 threading.Thread(target=s.collect,daemon=True).start()
 print('DeepBrain Web 0.17 on port',a.port,flush=True)
 try:http.serve_forever()
 except KeyboardInterrupt:pass
 finally:
  s.stop.set();s.alice.stop.set()
  s.conversation.thread.join(4)
  if hasattr(s.alice.delivery,'thread'):s.alice.delivery.thread.join(4)
  voice.shutdown();voice.server_close();http.server_close()
if __name__=='__main__':main()
