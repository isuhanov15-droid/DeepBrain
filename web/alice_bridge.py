"""Private Alice webhook. One durable job; inference uses Studio's shared Dialogue."""
import hashlib,hmac,json,re,secrets,sqlite3,threading,time
from contextlib import contextmanager
from http.server import BaseHTTPRequestHandler,ThreadingHTTPServer
from openai_dialogue import DialogueError
from station_delivery import StationDelivery
from voice_protocol import delivery_command,is_launch
from dialogue_memory import strip_name
from station_face import StationFace
from lada_face import FACES,gif

WAIT='Готовлю ответ. Скажи «Готово?» через несколько секунд. На резервной модели ожидание может занять несколько минут.'
HELP='Я Лада, помощница DeepBrain. Помню наши разговоры. Задай вопрос. Для следующей части скажи «Продолжить», для повторения — «Повтори».'

def digest(value):return hashlib.sha256(value.encode()).hexdigest()

def speech_chunks(text):
    text=re.sub(r'```[^\n]*\n?(.*?)```',r'\1',text,flags=re.S)
    text=re.sub(r'!?\[([^\]]+)\]\([^)]*\)',r'\1',text)
    text=re.sub(r'<[^>]*>',' ',text)
    text=re.sub(r'[*_`#<>]|[\x00-\x08\x0b-\x1f]','',text)
    text=re.sub(r'\s+',' ',text).strip() or 'Ответ пуст. Попробуй задать вопрос иначе.'
    chunks=[]
    while len(text)>880:
        point=max(text.rfind('. ',0,881),text.rfind('? ',0,881),text.rfind('! ',0,881))
        if point<400:point=text.rfind(' ',0,881)
        if point<1:point=879
        chunks.append(text[:point+1].strip());text=text[point+1:].lstrip()
    chunks.append(text)
    return chunks

class AliceBridge:
    def __init__(self,data,dialogue,clock=time.time,wait_seconds=1.0):
        self.data=data;self.dialogue=dialogue;self.clock=clock;self.wait_seconds=wait_seconds
        self.db=data/'alice.sqlite3';self.lock=threading.Lock();self.stop=threading.Event();self.wake=threading.Event()
        with self.connect() as c:
            c.executescript('''
              CREATE TABLE IF NOT EXISTS settings(name TEXT PRIMARY KEY,value TEXT NOT NULL);
              CREATE TABLE IF NOT EXISTS events(id TEXT PRIMARY KEY,fingerprint TEXT NOT NULL,reply TEXT NOT NULL,at REAL NOT NULL);
              CREATE TABLE IF NOT EXISTS jobs(id TEXT PRIMARY KEY,conversation TEXT NOT NULL,message TEXT NOT NULL,
                at REAL NOT NULL,status TEXT NOT NULL,result TEXT,cursor INTEGER NOT NULL DEFAULT 0);
            ''')
        # Dialogue has already reconciled interrupted inference. Reconcile our second
        # durable ledger without repeating a possibly completed model request.
        with self.connect() as c:
            rows=c.execute("SELECT id FROM jobs WHERE status IN ('queued','running')").fetchall()
        for row in rows:
            with self.dialogue.connect() as c:r=c.execute('SELECT * FROM requests WHERE id=?',(row['id'],)).fetchone()
            with self.connect() as c:
                if r:
                    result=self.dialogue.public_row(r)
                    c.execute('UPDATE jobs SET status=?,result=? WHERE id=?',(result['status'],json.dumps(result),row['id']))
                else:c.execute("UPDATE jobs SET status='queued' WHERE id=?",(row['id'],))
        self.delivery=StationDelivery(self)

    @contextmanager
    def connect(self):
        c=sqlite3.connect(self.db,timeout=.2);c.row_factory=sqlite3.Row
        try:
            with c:yield c
        finally:c.close()

    def config(self):
        try:
            v=json.loads((self.data/'alice.json').read_text())
            if not isinstance(v,dict) or type(v.get('enabled')) is not bool:raise ValueError()
            if not re.fullmatch(r'[a-zA-Z0-9_-]{43,128}',v.get('secret','')):raise ValueError()
            if not re.fullmatch(r'[a-f0-9-]{36}',v.get('skillId','')):raise ValueError()
            return v
        except (OSError,ValueError,TypeError):return None

    @staticmethod
    def setting(c,name,default=''):
        r=c.execute('SELECT value FROM settings WHERE name=?',(name,)).fetchone()
        return r[0] if r else default

    @staticmethod
    def put(c,name,value):c.execute('INSERT OR REPLACE INTO settings VALUES(?,?)',(name,str(value)))

    def status(self):
        cfg=self.config()
        with self.connect() as c:
            return {'configured':bool(cfg),'enabled':bool(cfg and cfg['enabled']),
                'paired':bool(self.setting(c,'owner')),
                'pending':c.execute("SELECT count(*) FROM jobs WHERE status IN ('queued','running')").fetchone()[0],
                **self.delivery.status()}

    def pair_code(self):
        if not self.config():raise ValueError('Сначала настрой навык на сервере.')
        code=str(secrets.randbelow(900000)+100000)
        with self.connect() as c:
            if self.setting(c,'owner'):raise ValueError('Станция уже подключена.')
            self.put(c,'pair_hash',digest(code));self.put(c,'pair_until',self.clock()+600);self.put(c,'pair_attempts',0)
        return {'code':code,'expiresInSeconds':600}

    def start(self):
        self.worker=threading.Thread(target=self.work,daemon=True,name='alice-inference');self.worker.start()
        self.delivery.start()
        self.face=StationFace(self);self.face.start()

    def work(self):
        while not self.stop.is_set():
            try:
                with self.connect() as c:job=c.execute("SELECT * FROM jobs WHERE status='queued' ORDER BY at LIMIT 1").fetchone()
                cfg=self.config()
                if not job or not cfg or not cfg['enabled']:
                    self.wake.wait(.25);self.wake.clear();continue
                with self.connect() as c:c.execute("UPDATE jobs SET status='running' WHERE id=?",(job['id'],))
                try:r=self.dialogue.send(job['id'],job['conversation'],job['message'],local_only=True)
                except DialogueError as e:
                    if e.code in ('busy','rate_limit'):
                        with self.connect() as c:c.execute("UPDATE jobs SET status='queued' WHERE id=?",(job['id'],))
                        self.stop.wait(1);continue
                    r={'status':'failed','error':str(e),'errorCode':e.code}
                except Exception:
                    # Never blindly retry a transport whose outcome could have been saved.
                    r={'status':'failed','error':'Обработка прервалась. Проверь историю в DeepBrain Web.','errorCode':'interrupted'}
                with self.connect() as c:c.execute('UPDATE jobs SET status=?,result=? WHERE id=?',(r['status'],json.dumps(r),job['id']))
            except sqlite3.Error:self.stop.wait(.25)

    @staticmethod
    def response(text,end=False,silent=False):
        response={'text':text[:1024],'end_session':end}
        # Yandex's documented pause markup overrides the spoken text. Retain
        # readable status on screens without saying a waiting acknowledgment.
        if silent:response['tts']='sil <[100]>'
        return {'version':'1.0','response':response}

    def active_conversation(self):
        c=sqlite3.connect(self.dialogue.db,timeout=.2)
        try:return c.execute("SELECT value FROM settings WHERE name='conversation'").fetchone()[0]
        finally:c.close()

    def latest(self):
        conversation=self.active_conversation()
        with self.connect() as c:return c.execute('SELECT * FROM jobs WHERE conversation=? ORDER BY at DESC,rowid DESC LIMIT 1',(conversation,)).fetchone()

    def answer(self,job,repeat=False):
        if not job:return 'Задай вопрос, и я подготовлю ответ.',None
        if job['status'] in ('queued','running'):return WAIT,None
        r=json.loads(job['result'] or '{}')
        if job['status']!='completed':return r.get('error') or 'Не удалось получить ответ. Попробуй задать вопрос ещё раз.',None
        chunks=speech_chunks(r.get('answer') or '')
        index=max(0,job['cursor']-1) if repeat else min(job['cursor'],len(chunks)-1)
        suffix=' Скажи «Продолжить», чтобы услышать следующую часть.' if index+1<len(chunks) else (' Достигнут лимит ответа. Можешь задать уточняющий вопрос.' if r.get('finishReason')=='length' else '')
        return chunks[index]+suffix,(max(job['cursor'],index+1),job['id'])

    def handle(self,value):
        if not isinstance(value,dict) or value.get('version')!='1.0':raise ValueError('protocol')
        session=value.get('session');req=value.get('request')
        if not isinstance(session,dict) or not isinstance(req,dict):raise ValueError('request')
        cfg=self.config()
        if not cfg or not cfg['enabled']:return self.response('Подключение навыка ещё настраивается.')
        if session.get('skill_id')!=cfg['skillId']:raise PermissionError()
        user=session.get('user')
        if not isinstance(user,dict) or not isinstance(user.get('user_id'),str) or not 1<=len(user['user_id'])<=128:
            return self.response('Войди в аккаунт Яндекса, к которому подключена твоя Станция.',True)
        sid=session.get('session_id');mid=session.get('message_id')
        if not isinstance(sid,str) or not 1<=len(sid)<=64 or type(mid) is not int or not 0<=mid<100000000:raise ValueError('session')
        if req.get('type')!='SimpleUtterance':return self.response('Произнеси вопрос голосом или напиши его текстом.')
        command=req.get('original_utterance',req.get('command',''))
        if not isinstance(command,str) or len(command)>4000:raise ValueError('utterance')
        command=command.strip();normal=re.sub(r'[^\w\s]','',strip_name(command).lower()).strip()
        owner=digest(cfg['skillId']+'\0'+user['user_id'])
        event_id=digest(json.dumps([owner,sid,mid],ensure_ascii=False))[:32]
        fingerprint=digest(json.dumps([command,bool(session.get('new'))],ensure_ascii=False))
        if not self.lock.acquire(timeout=.1):return self.response('Проверяю предыдущий запрос. Скажи «Готово?» ещё раз.')
        try:
            with self.connect() as c:
                bound=self.setting(c,'owner')
                if bound and not hmac.compare_digest(bound,owner):return self.response('Этот навык подключён к другому аккаунту.',True)
                cached=c.execute('SELECT * FROM events WHERE id=?',(event_id,)).fetchone()
                if cached:
                    if cached['fingerprint']!=fingerprint:raise ValueError('reused message id')
                    return json.loads(cached['reply'])
                if not bound:
                    # No automatic first-caller ownership. A code from authenticated Web
                    # links only this Yandex account to the existing private conversation.
                    digits={'ноль':'0','нуль':'0','один':'1','одна':'1','два':'2','две':'2','три':'3','четыре':'4','пять':'5','шесть':'6','семь':'7','восемь':'8','девять':'9'}
                    pin=''.join(re.findall(r'\d',' '.join(digits.get(w,w) for w in normal.split())))
                    valid_time=self.clock()<float(self.setting(c,'pair_until','0'))
                    attempts=int(self.setting(c,'pair_attempts','0'))
                    if len(pin)==6 and valid_time and attempts<5:
                        self.put(c,'pair_attempts',attempts+1)
                        if hmac.compare_digest(digest(pin),self.setting(c,'pair_hash')):
                            self.put(c,'owner',owner);self.put(c,'pair_until',0)
                            result=self.response('Станция подключена к DeepBrain. '+HELP)
                        else:result=self.response('Код не подошёл. Проверь код в разделе Диалог в DeepBrain Web.')
                    else:result=self.response('Открой Диалог в DeepBrain Web, получи код подключения и произнеси его по цифрам.')
                    c.execute('INSERT INTO events VALUES(?,?,?,?)',(event_id,fingerprint,json.dumps(result),self.clock()))
                    return result
            advance=None;delivery_number=None;automatic=False
            with self.connect() as c:existing=c.execute('SELECT * FROM jobs WHERE id=?',(event_id,)).fetchone()
            if existing:
                if existing['message']!=command:raise ValueError('reused message id')
                spoken,advance=self.answer(existing);result=self.response(spoken)
            elif normal in ('выход','хватит','закончить','до свидания'):
                with self.connect() as c:self.delivery.cancel(c)
                result=self.response('До встречи. Переписка сохранена.',True)
            elif not command or is_launch(command) or normal in ('','помощь','что ты умеешь'):
                result=self.response('Я Лада. Рада продолжить наш разговор. О чём поговорим?' if self.delivery.available() else HELP)
            elif delivery_command(command)[0]:
                # original_utterance sometimes retains the whole skill-launch
                # phrase even though request.command has already stripped it.
                # Both forms are protocol traffic, never a new Qwen question.
                number=delivery_command(command)[1]
                if number:
                    spoken,advance,delivery_number,end=self.delivery.reply(number)
                    result=self.response(spoken,end)
                else:result=self.response('Этот запрос доставки не распознан.',True)
            elif normal in ('готово','готов','готова','ответ готов','продолжить','продолжи','повтори','повторить'):
                job=self.latest()
                if self.delivery.busy(job):result=self.response('Ответ уже передаётся на Станцию Макс.',True,silent=True)
                elif job and job['status'] in ('queued','running') and self.delivery.pending(job):
                    result=self.response('Ответ ещё готовится. Станция Макс озвучит его автоматически.',True,silent=True)
                else:
                    spoken,advance=self.answer(job,repeat=normal in ('повтори','повторить'));result=self.response(spoken)
            else:
                with self.connect() as c:pending=c.execute("SELECT id FROM jobs WHERE status IN ('queued','running') LIMIT 1").fetchone()
                if pending:
                    automatic_pending=self.delivery.pending(self.latest())
                    result=self.response('Предыдущий вопрос ещё обрабатывается. Станция Макс озвучит ответ автоматически. Новый вопрос задай после ответа.' if automatic_pending else 'Предыдущий вопрос ещё обрабатывается. Скажи «Готово?». Новый вопрос задай после ответа.',end=automatic_pending,silent=automatic_pending)
                else:
                    with self.connect() as c:
                        self.delivery.cancel(c)
                        c.execute('INSERT INTO jobs(id,conversation,message,at,status) VALUES(?,?,?,?,?)',
                            (event_id,self.active_conversation(),command,self.clock(),'queued'))
                    self.wake.set()
                    # Automatic delivery does not need a one-second wait in
                    # the webhook. Keep the existing manual-mode fast path.
                    deadline=time.monotonic()+(min(.15,self.wait_seconds) if self.delivery.available() else self.wait_seconds)
                    with self.connect() as c:job=c.execute('SELECT * FROM jobs WHERE id=?',(event_id,)).fetchone()
                    while time.monotonic()<deadline:
                        with self.connect() as c:job=c.execute('SELECT * FROM jobs WHERE id=?',(event_id,)).fetchone()
                        if job['status'] not in ('queued','running'):break
                        self.stop.wait(.025)
                    spoken,advance=self.answer(job);result=self.response(spoken)
                    if job['status'] in ('queued','running') and self.delivery.available():
                        automatic=True;result=self.response('Ответ готовится и будет озвучен автоматически.',True,silent=True)
                    elif job['status']=='completed' and self.delivery.available():
                        chunks=speech_chunks(json.loads(job['result'])['answer'])
                        if len(chunks)>1:
                            automatic=True;result=self.response(chunks[0],True)
            with self.connect() as c:
                if advance:
                    c.execute('UPDATE jobs SET cursor=? WHERE id=?',advance)
                    if delivery_number:c.execute("UPDATE station_deliveries SET state='speaking' WHERE nonce=?",(delivery_number,))
                    else:self.delivery.cancel(c,advance[1])
                if automatic:self.delivery.queue(c,event_id,advance[0] if advance else 0)
                c.execute('INSERT INTO events VALUES(?,?,?,?)',(event_id,fingerprint,json.dumps(result),self.clock()))
                # Short protocol replay cache. Conversation and jobs are never pruned.
                c.execute('DELETE FROM events WHERE at<?',(self.clock()-7*86400,))
            return result
        finally:self.lock.release()

class AliceHTTPServer(ThreadingHTTPServer):
    daemon_threads=True
    def __init__(self,address,bridge):
        self.bridge=bridge;self.slots=threading.BoundedSemaphore(8)
        super().__init__(address,AliceHandler)
    def process_request(self,request,client_address):
        if not self.slots.acquire(blocking=False):self.shutdown_request(request);return
        try:super().process_request(request,client_address)
        except Exception:self.slots.release();raise
    def process_request_thread(self,*args):
        try:super().process_request_thread(*args)
        finally:self.slots.release()

class AliceHandler(BaseHTTPRequestHandler):
    server_version='DeepBrainVoice';sys_version=''
    def log_message(self,*args):pass # Path contains a bearer secret; never log it.
    def setup(self):
        super().setup();self.connection.settimeout(2)
    def reply(self,code,value):
        body=json.dumps(value,ensure_ascii=False).encode()
        self.send_response(code);self.send_header('Content-Type','application/json; charset=utf-8')
        self.send_header('Content-Length',str(len(body)));self.send_header('Cache-Control','no-store')
        self.end_headers();self.wfile.write(body)
    def do_GET(self):
        match=re.fullmatch(r'/lada-face/v2/([a-z]+)\.gif',self.path)
        if not match or match[1] not in FACES:return self.reply(404,{'error':'Not found'})
        name=match[1];body=gif(name)
        self.send_response(200);self.send_header('Content-Type','image/gif')
        self.send_header('Content-Length',str(len(body)));self.send_header('Cache-Control','public, max-age=86400')
        self.end_headers();self.wfile.write(body)
        face=getattr(self.server.bridge,'face',None)
        if face:face.fetched=name;face.fetched_at=time.time()
    def do_POST(self):
        cfg=self.server.bridge.config()
        if not cfg or not hmac.compare_digest(self.path,'/alice/'+cfg['secret']):return self.reply(404,{'error':'Not found'})
        try:
            if self.headers.get_content_type()!='application/json' or self.headers.get('Transfer-Encoding'):raise ValueError()
            if len(self.headers.get_all('Content-Length',[]))!=1:raise ValueError()
            length=int(self.headers['Content-Length'])
            if not 0<length<=16384:return self.reply(413,{'error':'Request too large'})
            raw=self.rfile.read(length)
            if len(raw)!=length:raise ValueError()
            return self.reply(200,self.server.bridge.handle(json.loads(raw)))
        except PermissionError:return self.reply(403,{'error':'Forbidden'})
        except (ValueError,UnicodeError,RecursionError):return self.reply(400,{'error':'Invalid request'})
        except (OSError,sqlite3.Error):return self.reply(503,{'error':'Temporarily unavailable'})
