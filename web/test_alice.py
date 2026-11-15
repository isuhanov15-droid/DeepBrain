import json,sqlite3,tempfile,threading,time,unittest,urllib.request,urllib.error
from pathlib import Path
from alice_bridge import AliceBridge,AliceHTTPServer,speech_chunks,WAIT
from openai_dialogue import Dialogue,LOCAL_DEFAULTS

SKILL='11111111-1111-1111-1111-111111111111'

class AliceTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.data=Path(self.temp.name);self.calls=[];self.now=1000
        self.answer='Привет, Иван!';self.gate=None;self.mid=0;self.http=None
        (self.data/'dialogue.json').write_text(json.dumps(LOCAL_DEFAULTS))
        self.cfg={'enabled':True,'secret':'s'*43,'skillId':SKILL}
        (self.data/'alice.json').write_text(json.dumps(self.cfg))
        self.d=self.dialogue();self.a=AliceBridge(self.data,self.d,clock=lambda:self.now,wait_seconds=.2)
    def dialogue(self):
        return Dialogue(self.data,lambda:{},clock=lambda:self.now,local_transport=self.local,
            transport=lambda *a,**k:self.fail('Voice must never call OpenAI'))
    def local(self,base,path,payload=None,timeout=None):
        if path=='/api/tags':return {'models':[{'name':LOCAL_DEFAULTS['model']} ]}
        self.calls.append(payload)
        if self.gate:self.gate.wait(3)
        return {'done':True,'done_reason':getattr(self,'finish_reason','stop'),'message':{'role':'assistant','content':self.answer},'prompt_eval_count':10,'eval_count':4}
    def tearDown(self):
        if self.gate:self.gate.set()
        self.a.stop.set()
        if hasattr(self.a,'worker'):self.a.worker.join(4)
        if self.http:self.http.shutdown();self.http.server_close()
        self.temp.cleanup()
    def request(self,text='',user='ivan',**extra):
        self.mid+=1;self.now+=2
        v={'version':'1.0','session':{'skill_id':SKILL,'session_id':'session-1','message_id':self.mid,
            'user':{'user_id':user},'new':not text},'request':{'type':'SimpleUtterance','command':text,'original_utterance':text}}
        v.update(extra);return v
    def pair(self):
        code=self.a.pair_code()['code'];r=self.a.handle(self.request('код '+code));self.assertIn('подключена',r['response']['text'])
    def done(self):
        deadline=time.monotonic()+3
        while self.a.status()['pending'] and time.monotonic()<deadline:time.sleep(.02)
        self.assertFalse(self.a.status()['pending'])
    def test_pair_required_and_other_account_cannot_access(self):
        r=self.a.handle(self.request('расскажи секрет'));self.assertIn('код',r['response']['text']);self.assertEqual(0,len(self.calls))
        self.pair();r=self.a.handle(self.request('Готово?',user='outsider'));self.assertTrue(r['response']['end_session'])
    def test_pair_code_expiration_attempt_limit_and_restart(self):
        code=self.a.pair_code()['code']
        bad='000000'
        for i in range(5):self.a.handle(self.request(bad))
        self.a=AliceBridge(self.data,self.d,clock=lambda:self.now)
        self.a.handle(self.request(code));self.assertFalse(self.a.status()['paired'])
        code=self.a.pair_code()['code'];self.now+=601;self.a.handle(self.request(code));self.assertFalse(self.a.status()['paired'])
    def test_pair_spoken_digits(self):
        code=self.a.pair_code()['code'];words=['ноль','один','два','три','четыре','пять','шесть','семь','восемь','девять']
        self.a.handle(self.request('код '+' '.join(words[int(n)] for n in code)));self.assertTrue(self.a.status()['paired'])
    def test_anonymous_wrong_skill_and_invalid_protocol(self):
        v=self.request();v['session'].pop('user');self.assertTrue(self.a.handle(v)['response']['end_session'])
        v=self.request();v['session']['skill_id']='foreign'
        with self.assertRaises(PermissionError):self.a.handle(v)
        for v in (None,{},[],{'version':'2.0'}):
            with self.assertRaises(ValueError):self.a.handle(v)
    def test_fast_reply_shared_history_and_duplicate(self):
        self.pair();self.a.start();v=self.request('Привет');r=self.a.handle(v);self.done()
        self.assertEqual(self.answer,r['response']['text']);self.assertEqual(r,self.a.handle(v));self.assertEqual(1,len(self.calls))
        self.assertEqual(self.answer,self.d.view()['history'][-1]['answer'])
        self.assertLessEqual(self.calls[0]['options']['num_predict'],256)
        self.now+=2;conv=self.d.view()['conversationId'];self.d.send('f'*32,conv,'Из веба')
        self.assertIn(self.answer,[m['content'] for m in self.calls[-1]['messages']])
    def test_slow_response_bounded_and_poll_recovers_without_duplicate(self):
        self.pair();self.gate=threading.Event();self.a.start();v=self.request('Долгий вопрос')
        start=time.monotonic();r=self.a.handle(v);self.assertLess(time.monotonic()-start,1)
        self.assertEqual(WAIT,r['response']['text']);self.assertEqual(r,self.a.handle(v))
        self.assertIn('Предыдущий',self.a.handle(self.request('ещё вопрос'))['response']['text'])
        self.gate.set();self.done();self.assertEqual(self.answer,self.a.handle(self.request('Готово?'))['response']['text'])
        self.assertEqual(1,len(self.calls))
    def test_asr_gotova_retrieves_saved_answer_without_new_dialogue_turn(self):
        self.pair();self.a.wait_seconds=0
        self.a.handle(self.request('Какими знаниями ты обладаешь?'))
        self.assertEqual(WAIT,self.a.handle(self.request('готова'))['response']['text'])
        self.a.start();self.done()
        poll=self.request('Готова?');answer=self.a.handle(poll)
        self.assertEqual(self.answer,answer['response']['text'])
        self.assertEqual(answer,self.a.handle(poll))
        self.assertEqual(1,len(self.calls))
        self.assertEqual(1,len(self.d.view()['history']))
        with self.a.connect() as c:self.assertEqual(1,c.execute('SELECT count(*) FROM jobs').fetchone()[0])
    def test_queue_restores_after_restart_before_inference(self):
        self.pair();self.a.wait_seconds=0;self.a.handle(self.request('Сохранённый вопрос'))
        self.d=self.dialogue();self.a=AliceBridge(self.data,self.d,clock=lambda:self.now);self.a.start();self.done()
        self.assertEqual(1,len(self.calls));self.assertEqual(self.answer,self.a.handle(self.request('Готово?'))['response']['text'])
    def test_crash_after_saved_completion_reconciles_without_reinference(self):
        self.pair();self.a.wait_seconds=0;v=self.request('Вопрос');self.a.handle(v)
        with self.a.connect() as c:job=c.execute('SELECT * FROM jobs').fetchone()
        self.d.send(job['id'],job['conversation'],job['message'],local_only=True)
        with self.a.connect() as c:c.execute('DELETE FROM events WHERE id=?',(job['id'],))
        self.d=self.dialogue();self.a=AliceBridge(self.data,self.d,clock=lambda:self.now);self.a.start();self.done()
        self.assertEqual(self.answer,self.a.handle(v)['response']['text']);self.assertEqual(1,len(self.calls))
    def test_interrupted_model_request_is_not_replayed(self):
        self.pair();self.a.wait_seconds=0;self.a.handle(self.request('Вопрос'))
        with self.a.connect() as c:job=c.execute('SELECT * FROM jobs').fetchone()
        with self.d.connect() as c:c.execute('INSERT INTO requests(id,conversation,at,day,message,model,status,provider) VALUES(?,?,?,?,?,?,?,?)',
            (job['id'],job['conversation'],self.now,'day',job['message'],'model','pending','ollama'))
        self.d=self.dialogue();self.a=AliceBridge(self.data,self.d,clock=lambda:self.now);self.a.start();self.done()
        self.assertEqual(0,len(self.calls));self.assertNotEqual(WAIT,self.a.handle(self.request('Готово?'))['response']['text'])
    def test_long_reply_chunks_repeat_and_duplicate_poll(self):
        self.pair();self.answer=('Первый фрагмент. '*65)+('Второй фрагмент. '*65);self.a.start()
        r1=self.a.handle(self.request('Длинный вопрос'));self.done();self.assertLessEqual(len(r1['response']['text']),1024)
        v=self.request('Продолжить');r2=self.a.handle(v);self.assertNotEqual(r1,r2);self.assertEqual(r2,self.a.handle(v))
        self.assertEqual(r2,self.a.handle(self.request('Повтори')));self.assertEqual(1,len(self.calls))
    def test_untrusted_session_state_does_not_change_job_or_owner(self):
        self.pair();self.a.start();v=self.request('Привет',state={'session':{'conversation':'evil','owner':'outsider','job':'x'}})
        self.a.handle(v);self.done();self.assertEqual('Привет',self.d.view()['history'][-1]['message'])
    def test_voice_refuses_openai_even_if_web_provider_changed(self):
        self.pair();(self.data/'dialogue.json').write_text('{"provider":"openai"}')
        self.a.start();r=self.a.handle(self.request('Вопрос'));self.done();self.assertEqual(0,len(self.calls))
        with self.d.connect() as c:self.assertEqual(0,c.execute('SELECT count(*) FROM requests').fetchone()[0])
    def test_busy_web_does_not_drop_voice_question(self):
        self.pair();self.d.lock.acquire();self.a.start()
        try:self.a.handle(self.request('Вопрос в очереди'));self.assertTrue(self.a.status()['pending'])
        finally:self.d.lock.release()
        self.done();self.assertEqual(1,len(self.calls))
    def test_changed_conversation_fails_queued_job_without_cross_contamination(self):
        self.pair();self.a.wait_seconds=0;self.a.handle(self.request('старый вопрос'));self.d.new_conversation();self.a.start();self.done()
        self.assertEqual(0,len(self.calls));self.assertEqual([],self.d.view()['history'])
    def test_http_exposes_only_secret_callback_and_bounds_payload(self):
        self.http=AliceHTTPServer(('127.0.0.1',0),self.a);threading.Thread(target=self.http.serve_forever,daemon=True).start()
        base='http://127.0.0.1:'+str(self.http.server_address[1]);path='/alice/'+self.cfg['secret']
        def call(path,raw=None):
            req=urllib.request.Request(base+path,data=raw,headers={'Content-Type':'application/json'})
            try:
                with urllib.request.urlopen(req,timeout=3) as r:return r.status,json.load(r)
            except urllib.error.HTTPError as e:return e.code,None
        self.assertEqual(404,call('/web/dialogue')[0]);self.assertEqual(404,call('/alice/bad',b'{}')[0])
        self.assertEqual(413,call(path,b' '*17000)[0]);self.assertEqual(400,call(path,b'[]')[0])
        self.assertEqual(200,call(path,json.dumps(self.request()).encode())[0])
        v=self.request();v['session']['skill_id']='bad';self.assertEqual(403,call(path,json.dumps(v).encode())[0])
    def test_plain_speech_does_not_emit_markup_or_drop_long_words(self):
        text='<speaker audio="evil">Hello</speaker> **Мир** [ссылка](https://evil.invalid)'
        spoken=speech_chunks(text)[0];self.assertNotIn('<',spoken);self.assertNotIn('https',spoken)
        self.assertEqual('a'*2500,''.join(speech_chunks('a'*2500)))
    def test_voice_preserves_partial_desktop_answer_and_announces_limit(self):
        self.pair();self.finish_reason='length';self.a.start()
        r=self.a.handle(self.request('Очень длинный вопрос'));self.done()
        self.assertIn(self.answer,r['response']['text']);self.assertIn('лимит',r['response']['text'])
        self.assertEqual('length',self.d.view()['history'][-1]['finishReason'])

if __name__=='__main__':unittest.main()
