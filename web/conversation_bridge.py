"""Durable, ordered delivery of accepted user messages to local cognitive experience.

The existing request journal is the outbox. Neither inference nor the Alice
webhook waits for this worker. Receipts advance a separate cursor only after
the core has durably saved exactly the same identity. No model output is sent.
"""
import datetime as dt
import json
import os
import threading
import urllib.request
from urllib.parse import urlsplit
from openai_dialogue import NoRedirect
from voice_protocol import delivery_command
from dialogue_memory import memory_command
from semantic_appraisal import SemanticAppraiser, Deferred, fallback


class ConversationBridge:
    def __init__(self, dialogue, upstream, stop, transport=None, appraiser=None):
        url=urlsplit(upstream)
        if url.scheme!='http' or url.hostname!='127.0.0.1' or url.username or url.password or url.query or url.fragment:
            raise ValueError('Conversation observations require local core')
        self.dialogue=dialogue;self.upstream=upstream.rstrip('/');self.stop=stop
        self.transport=transport or self.post;self.error=None;self.lock=threading.Lock()
        self.appraiser=appraiser or SemanticAppraiser(dialogue,stop);self.analyzing=False
        with dialogue.connect() as c:
            baseline=c.execute('SELECT COALESCE(MAX(seq),0) FROM requests').fetchone()[0]
            c.execute("INSERT OR IGNORE INTO settings(name,value) VALUES('experience_baseline',?)",(str(baseline),))
            c.execute("INSERT OR IGNORE INTO settings(name,value) VALUES('experience_cursor','0')")
            c.execute("INSERT OR IGNORE INTO settings(name,value) VALUES('semantic_baseline',?)",(str(baseline),))
            c.execute('''CREATE TABLE IF NOT EXISTS conversation_appraisals(
                request_id TEXT PRIMARY KEY, seq INTEGER UNIQUE NOT NULL,
                payload TEXT NOT NULL, analysis TEXT NOT NULL)''')

    def observation(self,row,baseline,semantic_baseline):
        with self.dialogue.connect() as c:
            prior=c.execute('SELECT payload FROM conversation_appraisals WHERE request_id=?',(row['id'],)).fetchone()
            if prior:return json.loads(prior['payload'])
            history=c.execute('''SELECT r.id,r.message,r.answer,r.at FROM requests r
                LEFT JOIN recall_sources m ON m.request_id=r.id WHERE r.conversation=? AND r.seq<?
                AND r.at>=? AND r.status='completed' AND COALESCE(m.excluded,0)=0 ORDER BY r.seq DESC LIMIT 8''',
                (row['conversation'],row['seq'],row['at']-7200)).fetchall()
        payload={'sequence':row['seq'],'requestId':row['id'],
            'at':dt.datetime.fromtimestamp(row['at'],dt.timezone.utc).isoformat(),
            'message':row['message'],'historical':row['seq']<=baseline}
        analysis={'source':'legacy'}
        if row['seq']>semantic_baseline:
            if row['status']=='pending' or self.dialogue.lock.locked():raise Deferred()
            if self.dialogue.clock()-row['at']>900:
                analysis=fallback('Сообщение сохранено в опыте; для текущих эмоций оно уже слишком давнее.')
            elif memory_command(row['message'])[0]:
                analysis=fallback('Команда памяти сохранена; её содержимое не считается текущей эмоцией.')
            else:
                selected=[r for r in reversed(history) if not delivery_command(r['message'])[0]][-4:]
                context=[{'user':r['message'],'assistant':r['answer'],'ageSeconds':round(row['at']-r['at'])} for r in selected]
                self.analyzing=True
                try:analysis=self.appraiser.assess(row['message'],context)
                finally:self.analyzing=False
                analysis['contextRequestIds']=[r['id'] for r in selected][-analysis['contextPairs']:] if analysis['contextPairs'] else []
            payload['semantic']=analysis['semantic']
        # Freeze the complete outgoing observation BEFORE sending it. A lost
        # receipt must never re-run Qwen or send a different appraisal for this ID.
        with self.dialogue.connect() as c:
            c.execute('INSERT INTO conversation_appraisals VALUES(?,?,?,?)',
                (row['id'],row['seq'],json.dumps(payload,ensure_ascii=False),json.dumps(analysis,ensure_ascii=False)))
        return payload

    def post(self, payload):
        headers={'Content-Type':'application/json'}
        token=os.environ.get('DEEPBRAIN_API_TOKEN')
        if token:headers['Authorization']='Bearer '+token
        req=urllib.request.Request(self.upstream+'/api/v1/conversation/observe',
            data=json.dumps(payload,ensure_ascii=False).encode(),headers=headers,method='POST')
        with urllib.request.build_opener(NoRedirect).open(req,timeout=3) as r:
            raw=r.read(4097)
        if len(raw)>4096:raise ValueError('Invalid core receipt')
        return json.loads(raw)

    def step(self, limit=16):
        with self.lock:
            try:
                with self.dialogue.connect() as c:
                    settings=dict(c.execute("SELECT name,value FROM settings WHERE name IN ('experience_baseline','experience_cursor','semantic_baseline')"))
                    cursor=int(settings['experience_cursor']);baseline=int(settings['experience_baseline'])
                    rows=c.execute('SELECT seq,id,at,message,status,conversation FROM requests WHERE seq>? ORDER BY seq LIMIT ?',
                        (cursor,limit)).fetchall()
                for row in rows:
                    if not delivery_command(row['message'])[0]:
                        payload=self.observation(row,baseline,int(settings['semantic_baseline']))
                        receipt=self.transport(payload)
                        if receipt != {'sequence':row['seq'],'requestId':row['id'],'status':'stored'}:
                            raise ValueError('Unconfirmed core receipt')
                    with self.dialogue.connect() as c:
                        c.execute("UPDATE settings SET value=? WHERE name='experience_cursor'",(str(row['seq']),))
                self.error=None
                return len(rows)
            except Deferred:
                self.error=None
                return 0
            except Exception:
                # No user text, endpoint or provider credentials in telemetry.
                self.error='core_unavailable'
                return 0

    def status(self):
        with self.dialogue.connect() as c:
            cursor=int(c.execute("SELECT value FROM settings WHERE name='experience_cursor'").fetchone()[0])
            pending=c.execute('SELECT COUNT(*) FROM requests WHERE seq>?',(cursor,)).fetchone()[0]
            latest=c.execute('SELECT analysis FROM conversation_appraisals ORDER BY seq DESC LIMIT 1').fetchone()
            analysis=json.loads(latest['analysis']) if latest else None
            if analysis and 'semantic' in analysis:
                analysis={k:analysis[k] for k in ('semantic','reason','contextPairs')}
            else:analysis=None
        return {'cursor':cursor,'pending':pending,'error':self.error,'analyzing':self.analyzing,'lastAssessment':analysis}

    def run(self):
        while not self.stop.is_set():
            count=self.step()
            self.stop.wait(.1 if count==16 else (3 if self.error else 1))

    def start(self):
        self.thread=threading.Thread(target=self.run,name='conversation-experience',daemon=True)
        self.thread.start()
