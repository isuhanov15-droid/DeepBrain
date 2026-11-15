"""Durable automatic delivery; inference/history stay in the shared Dialogue.

An uncertain send is never automatically retried. Only new voice jobs explicitly
queued while automatic mode is healthy are eligible; old history is never read
aloud on activation. Each chunk has a one-use delivery number.
"""
import json,os,re,secrets,subprocess,sys,threading,time
from pathlib import Path

ACTIVE="('queued','awaiting','speaking')"

class StationDelivery:
    def __init__(self,bridge,transport=None):
        self.bridge=bridge;self.transport=transport or self.run_transport
        self.reachable_until=0;self.last_error=None;self.last_transport=None
        self.authorization={'authorizationState':'idle'};self.auth_lock=threading.Lock();self.next_auth=0
        self.auth_generation=0;self.auth_svg=None;self.auth_id=None;self.auth_expires=0
        with bridge.connect() as c:
            c.execute('''CREATE TABLE IF NOT EXISTS station_deliveries(
                job_id TEXT PRIMARY KEY,nonce TEXT UNIQUE,state TEXT NOT NULL,
                next_at REAL NOT NULL,expires_at REAL NOT NULL,part INTEGER NOT NULL DEFAULT 0)''')
            c.execute("UPDATE station_deliveries SET state='uncertain' WHERE state IN ('awaiting','speaking')")
    def enabled(self):
        try:
            cfg=json.loads((self.bridge.data/'station-output.json').read_text())
            return isinstance(cfg,dict) and cfg.get('enabled') is True and bool(cfg.get('musicToken'))
        except (OSError,ValueError,TypeError):return False
    def available(self):return self.enabled() and self.reachable_until>time.monotonic()
    def set_enabled(self,enabled):
        if type(enabled) is not bool:raise ValueError('Некорректная настройка.')
        with self.bridge.lock:
            path=self.bridge.data/'station-output.json'
            try:cfg=json.loads(path.read_text())
            except (OSError,ValueError):raise ValueError('Сначала подключи Станцию на сервере.')
            if enabled and not cfg.get('musicToken'):raise ValueError('Сначала заверши вход в Яндекс для автоозвучивания.')
            cfg['enabled']=enabled;tmp=path.with_suffix('.new')
            tmp.write_text(json.dumps(cfg,ensure_ascii=False,indent=2)+'\n');os.chmod(tmp,0o600);os.replace(tmp,path)
            if not enabled:
                with self.bridge.connect() as c:self.cancel(c)
        return self.status()
    def status(self):
        try:authorized=bool(json.loads((self.bridge.data/'station-output.json').read_text()).get('musicToken'))
        except (OSError,ValueError,TypeError,AttributeError):authorized=False
        with self.bridge.connect() as c:
            row=c.execute('SELECT state FROM station_deliveries ORDER BY rowid DESC LIMIT 1').fetchone()
        with self.auth_lock:
            authorization=dict(self.authorization)
            if authorization['authorizationState']=='waiting':
                seconds=max(0,int(self.auth_expires-time.monotonic()))
                if seconds:
                    authorization['authorizationSecondsRemaining']=seconds
                    if authorization.get('authorizationMethod')!='device':authorization['authorizationQrPath']='/web/alice/qr.svg?id='+self.auth_id
                else:authorization={'authorizationState':'expired'}
        return {'stationAuthorized':authorized,'automaticReplyEnabled':self.enabled(),'stationReachable':self.available(),
            'automaticReplyState':row[0] if row else None,'stationError':self.last_error,
            'lastDeliveryTransport':self.last_transport,**authorization}
    def qr_svg(self,identifier):
        with self.auth_lock:
            if (self.authorization['authorizationState']!='waiting' or not self.auth_svg
                or identifier!=self.auth_id or time.monotonic()>=self.auth_expires):return None
            return self.auth_svg
    def begin_authorization(self,refresh=False):
        with self.auth_lock:
            running=self.authorization['authorizationState']=='starting' or (self.authorization['authorizationState']=='waiting' and time.monotonic()<self.auth_expires)
            if running and not refresh:return dict(self.authorization)
            if time.monotonic()<self.next_auth:raise ValueError('Подожди немного перед повторным входом.')
            cfg=json.loads((self.bridge.data/'station-output.json').read_text())
            if cfg.get('musicToken'):raise ValueError('Аккаунт уже подключён.')
            self.authorization={'authorizationState':'starting'};self.next_auth=time.monotonic()+5
            self.auth_generation+=1;generation=self.auth_generation;self.auth_svg=None;self.auth_id=None
            threading.Thread(target=self.authorize,args=(generation,),daemon=True,name='station-login').start()
            return dict(self.authorization)
    def authorize(self,generation):
        from setup_station import Login,save_token
        from station_oauth import DeviceLogin,AuthorizationError
        try:
            with self.bridge.lock:
                path=self.bridge.data/'station-output.json';cfg=json.loads(path.read_text())
                if not cfg.get('oauthDeviceId'):
                    cfg['oauthDeviceId']=secrets.token_hex(16);tmp=path.with_suffix('.new')
                    tmp.write_text(json.dumps(cfg,ensure_ascii=False,indent=2)+'\n');os.chmod(tmp,0o600);os.replace(tmp,path)
                device_id=cfg['oauthDeviceId']
            login=DeviceLogin(device_id);details=login.begin();end=login.deadline
            with self.auth_lock:
                if generation!=self.auth_generation:return
                self.auth_svg=None;self.auth_id=secrets.token_hex(16);self.auth_expires=end
                self.authorization={'authorizationState':'waiting',**details}
            while not self.bridge.stop.is_set() and time.monotonic()<end and generation==self.auth_generation:
                if self.bridge.stop.wait(login.interval):break
                try:token=login.finish()
                except AuthorizationError as ex:
                    if ex.code=='network':continue
                    raise
                if token:
                    with self.bridge.lock:
                        with self.auth_lock:
                            if generation!=self.auth_generation:return
                            save_token(self.bridge.data/'station-output.json',Login(),token)
                            self.authorization={'authorizationState':'connected'};self.auth_svg=None
                    return
            with self.auth_lock:
                if generation==self.auth_generation:self.authorization={'authorizationState':'expired'};self.auth_svg=None
        except Exception as ex:
            with self.auth_lock:
                if generation==self.auth_generation:
                    self.authorization={'authorizationState':'failed','authorizationError':ex.code if isinstance(ex,AuthorizationError) else 'station_authorization_failed'};self.auth_svg=None
    def start(self):
        self.thread=threading.Thread(target=self.work,daemon=True,name='station-delivery');self.thread.start()
    def queue(self,c,job_id,part=0):
        now=self.bridge.clock()
        # Only a short grace period for the silent webhook response to reach
        # Alice. Delivery still requires a completed answer and an idle Station.
        c.execute("INSERT OR IGNORE INTO station_deliveries(job_id,state,next_at,expires_at,part) VALUES(?,'queued',?,?,?)",(job_id,now+.5,now+600,part))
    def pending(self,job):
        if not job:return False
        with self.bridge.connect() as c:
            row=c.execute('SELECT state FROM station_deliveries WHERE job_id=?',(job['id'],)).fetchone()
        return bool(row and row[0] in ('queued','awaiting','speaking'))
    def cancel(self,c,job_id=None):
        sql="UPDATE station_deliveries SET state='cancelled' WHERE state IN "+ACTIVE
        c.execute(sql+(' AND job_id=?' if job_id else ''),(job_id,) if job_id else ())
    def busy(self,job):
        if not job:return False
        with self.bridge.connect() as c:
            row=c.execute('SELECT state FROM station_deliveries WHERE job_id=?',(job['id'],)).fetchone()
        return bool(row and row[0] in ('awaiting','speaking'))
    def reply(self,number):
        from alice_bridge import speech_chunks
        with self.bridge.connect() as c:
            item=c.execute('SELECT * FROM station_deliveries WHERE nonce=?',(number,)).fetchone()
            job=c.execute('SELECT * FROM jobs WHERE id=?',(item['job_id'],)).fetchone() if item else None
        if (not self.enabled() or not item or item['state']!='awaiting' or self.bridge.clock()>item['expires_at']
            or not job or job['conversation']!=self.bridge.active_conversation() or job['cursor']!=item['part']):
            return 'Этот ответ уже обработан или его доставка отменена.',None,None,True
        result=json.loads(job['result'] or '{}')
        if job['status']=='completed':
            chunks=speech_chunks(result.get('answer') or '')
        elif job['status']=='failed':chunks=[result.get('error') or 'Не удалось подготовить ответ. Попробуй ещё раз.']
        else:return 'Ответ ещё готовится.',None,None,True
        part=item['part']
        if part>=len(chunks):return 'Ответ уже озвучен.',None,None,True
        end=part+1<len(chunks)
        suffix=' Достигнут лимит ответа. Можешь задать уточняющий вопрос.' if not end and result.get('finishReason')=='length' else ''
        return chunks[part]+suffix,(part+1,job['id']),number,end
    def run_transport(self,number=None):
        cmd=[sys.executable,str(Path(__file__).with_name('station_transport.py')),'--data',str(self.bridge.data)]
        if number:cmd+=['--deliver',number]
        process=subprocess.Popen(cmd,stdout=subprocess.PIPE,stderr=subprocess.DEVNULL,text=True)
        end=time.monotonic()+115
        try:
            while not self.bridge.stop.is_set() and time.monotonic()<end:
                if number and not self.enabled():return {'status':'uncertain','sent':True}
                try:
                    out,_=process.communicate(timeout=.2)
                    return json.loads(out) if process.returncode==0 else {'status':'uncertain','sent':bool(number)}
                except subprocess.TimeoutExpired:pass
            return {'status':'uncertain','sent':bool(number)}
        finally:
            if process.poll() is None:
                process.terminate()
                try:process.wait(timeout=2)
                except subprocess.TimeoutExpired:process.kill();process.wait()
    def step(self):
        from alice_bridge import speech_chunks
        b=self.bridge
        if not self.enabled():return False
        with b.lock:
            now=b.clock();conversation=b.active_conversation()
            with b.connect() as c:
                c.execute("UPDATE station_deliveries SET state='expired' WHERE state IN "+ACTIVE+' AND expires_at<?',(now,))
                c.execute("UPDATE station_deliveries SET state='cancelled' WHERE state IN "+ACTIVE+
                    ' AND job_id IN (SELECT id FROM jobs WHERE conversation!=?)',(conversation,))
                item=c.execute("SELECT s.* FROM station_deliveries s JOIN jobs j ON j.id=s.job_id WHERE s.state='queued' AND s.next_at<=? AND j.status IN ('completed','failed') ORDER BY s.rowid LIMIT 1",(now,)).fetchone()
                if not item:return False
                nonce=str(secrets.randbelow(900000000000)+100000000000)
                c.execute("UPDATE station_deliveries SET state='awaiting',nonce=? WHERE job_id=?",(nonce,item['job_id']))
        # No callback lock or SQLite transaction is held during network I/O.
        try:result=self.transport(nonce)
        except Exception:result={'status':'uncertain','sent':True}
        # Diagnostic metadata only: no command, nonce, credentials or payload.
        self.last_transport={k:v for k,v in result.items() if
            (k in ('status','aliceState','error') and isinstance(v,str) and re.fullmatch(r'[A-Za-z_]{1,64}',v)) or
            (k in ('sent','acknowledged','speechStarted') and type(v) is bool)}
        with b.lock:
            with b.connect() as c:
                current=c.execute('SELECT * FROM station_deliveries WHERE job_id=?',(item['job_id'],)).fetchone()
                if current['state'] not in ('awaiting','speaking'):return True
                if result.get('status') in ('busy','unavailable') and result.get('sent') is False:
                    delay=1 if result.get('status')=='busy' else 4
                    c.execute("UPDATE station_deliveries SET state='queued',next_at=? WHERE job_id=?",(b.clock()+delay,item['job_id']))
                    self.last_error=result.get('error') or result['status'];return True
                if result.get('status')=='spoken' and current['state']=='speaking':
                    job=c.execute('SELECT * FROM jobs WHERE id=?',(item['job_id'],)).fetchone()
                    r=json.loads(job['result'] or '{}');count=len(speech_chunks(r.get('answer') or '')) if job['status']=='completed' else 1
                    state='done' if job['cursor']>=count else ('queued' if result.get('aliceState')=='IDLE' else 'interrupted')
                    c.execute('UPDATE station_deliveries SET state=?,part=?,next_at=? WHERE job_id=?',(state,job['cursor'],b.clock()+1,item['job_id']))
                    self.last_error=None
                else:
                    c.execute("UPDATE station_deliveries SET state='uncertain' WHERE job_id=?",(item['job_id'],))
                    self.last_error='delivery_not_confirmed'
        return True
    def work(self):
        next_probe=0
        while not self.bridge.stop.is_set():
            try:
                if self.enabled():
                    if time.monotonic()>=next_probe:
                        result=self.transport();next_probe=time.monotonic()+30
                        self.reachable_until=time.monotonic()+45 if result.get('status')=='ready' else 0
                        if not self.reachable_until:self.last_error=result.get('error') or 'unavailable'
                    self.step()
            except Exception:self.last_error='delivery_error'
            self.bridge.stop.wait(.25)
