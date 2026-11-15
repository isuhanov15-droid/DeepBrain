import copy,http.client,json,tempfile,threading,unittest,uuid
from unittest.mock import patch
from pathlib import Path
from openai_dialogue import Dialogue,DialogueError,LOCAL_DEFAULTS,SERVER_MODEL,SERVER_URL,server_prompt,local_request
from test_local_dialogue import reply

class FailoverTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name)
        (self.root/'dialogue.json').write_text(json.dumps({'provider':'ollama','fallbackEnabled':True}))
        self.now=1000;self.available={'desktop':True,'server':True};self.calls=[];self.fail={}
        self.d=self.create()
    def tearDown(self):self.temp.cleanup()
    def create(self):
        return Dialogue(self.root,lambda:{},key_path=self.root/'missing',clock=lambda:self.now,
            route_clock=lambda:self.now,local_transport=self.transport,
            transport=lambda *a:self.fail_cloud())
    def fail_cloud(self):raise AssertionError('OpenAI must never be contacted')
    def transport(self,base,path,payload=None,timeout=120):
        backend='server' if base==SERVER_URL else 'desktop'
        self.calls.append((backend,path,copy.deepcopy(payload),timeout))
        if not self.available[backend]:raise DialogueError('local_unavailable')
        if path=='/api/tags':return {'models':[{'name':SERVER_MODEL if backend=='server' else LOCAL_DEFAULTS['model']}]}
        if backend in self.fail:raise DialogueError(self.fail[backend])
        return reply()
    def send(self,message='Привет',rid=None):
        return self.d.send(rid or uuid.uuid4().hex,self.d.view()['conversationId'],message)
    def chats(self):return [c for c in self.calls if c[1]=='/api/chat']
    def test_desktop_is_preferred_and_polling_never_generates(self):
        for _ in range(5):self.assertEqual('desktop',self.d.view()['activeBackend'])
        self.assertEqual([],self.chats());self.assertEqual(1,len(self.calls))
        self.assertEqual('desktop',self.send()['backend']);self.assertEqual(1,len(self.chats()))
    def test_offline_desktop_routes_to_server_without_a_desktop_generation(self):
        self.available['desktop']=False
        v=self.d.view();self.assertTrue(v['ready']);self.assertEqual(SERVER_MODEL,v['model'])
        r=self.send();self.assertEqual('completed',r['status']);self.assertEqual('server',r['backend'])
        self.assertEqual(['server'],[c[0] for c in self.chats()])
        p=self.chats()[0][2];self.assertEqual(4096,p['options']['num_ctx']);self.assertEqual(256,p['options']['num_predict'])
        self.assertEqual(240,self.chats()[0][3]);self.assertFalse(p['think']);self.assertNotIn('tools',p)
    def test_missing_desktop_model_uses_server(self):
        original=self.d.local_transport
        self.d.local_transport=lambda base,path,*a,**k: {'models':[]} if base!=SERVER_URL and path=='/api/tags' else original(base,path,*a,**k)
        self.assertEqual('server',self.send()['backend'])
    def test_desktop_recovers_for_next_message_with_same_conversation_and_history(self):
        conversation=self.d.view()['conversationId'];first=self.send('Запомни: кедр')
        self.now+=6;self.available['desktop']=False;second=self.send('Продолжим на сервере')
        self.assertEqual('server',second['backend']);self.assertIn('Запомни: кедр',[m['content'] for m in self.chats()[-1][2]['messages']])
        self.now+=6;self.available['desktop']=True;third=self.send('Вернулись на компьютер')
        self.assertEqual('desktop',third['backend']);self.assertEqual(conversation,self.d.view()['conversationId'])
        self.assertIn('Продолжим на сервере',[m['content'] for m in self.chats()[-1][2]['messages']])
        self.assertEqual([first,second,third],self.d.view()['history'])
    def test_disconnect_after_health_check_attempts_each_backend_once_and_one_saved_reply(self):
        self.fail['desktop']='local_unavailable';rid=uuid.uuid4().hex
        result=self.send(rid=rid);self.assertEqual('server',result['backend']);self.assertEqual('desktop_request_failed',result['routeReason'])
        self.assertEqual(['desktop','server'],[c[0] for c in self.chats()])
        self.d=self.create();self.assertEqual(result,self.send(rid=rid));self.assertEqual(2,len(self.chats()))
        with self.d.connect() as con:
            self.assertEqual(1,con.execute('SELECT count(*) FROM requests').fetchone()[0])
            self.assertEqual(['failed','completed'],[r[0] for r in con.execute('SELECT status FROM request_attempts ORDER BY rowid')])
    def test_short_cooldown_avoids_flapping_then_recovers(self):
        self.fail['desktop']='local_model';self.send();self.assertEqual('server',self.d.view()['activeBackend'])
        self.fail.clear();self.now+=11
        self.assertEqual('desktop',self.d.view()['activeBackend'])
    def test_no_alternate_generation_for_malformed_or_incomplete_desktop_answer(self):
        for code in ('local_response','local_incomplete'):
            self.fail['desktop']=code;self.now+=11
            self.assertEqual(code,self.send()['errorCode'])
        self.assertEqual(['desktop','desktop'],[c[0] for c in self.chats()])
    def test_both_offline_is_honest_and_never_uses_cloud(self):
        self.available=dict(desktop=False,server=False)
        self.assertFalse(self.d.view()['ready']);self.assertIsNone(self.d.view()['activeBackend'])
        self.assertEqual('local_offline',self.send()['errorCode']);self.assertEqual([],self.chats())
    def test_both_fail_during_generation_stops_after_two_attempts(self):
        self.fail=dict(desktop='local_unavailable',server='local_unavailable')
        self.assertEqual('server_unavailable',self.send()['errorCode'])
        self.assertEqual(['desktop','server'],[c[0] for c in self.chats()])
    def test_server_result_is_not_replaced_if_desktop_recovers_mid_request(self):
        self.available['desktop']=False;original=self.d.local_transport
        def recover(base,path,*a,**kw):
            if path=='/api/chat':self.available['desktop']=True;self.now+=6
            return original(base,path,*a,**kw)
        self.d.local_transport=recover
        r=self.send();self.assertEqual('server',r['backend']);self.assertEqual('desktop',self.d.view()['activeBackend'])
        self.assertEqual(1,len(self.chats()))
    def test_restart_does_not_retry_an_unknown_inflight_attempt(self):
        rid=uuid.uuid4().hex;conversation=self.d.view()['conversationId']
        with self.d.connect() as con:
            con.execute('INSERT INTO requests(id,conversation,at,day,message,model,status,provider,backend) VALUES(?,?,?,?,?,?,?,?,?)',
                        (rid,conversation,self.now,'day','Привет',SERVER_MODEL,'pending','ollama','server'))
            con.execute('INSERT INTO request_attempts VALUES(?,?,?,?,?,?)',(rid,'server',SERVER_MODEL,self.now,'pending',None))
        self.d=self.create();self.assertEqual('interrupted',self.send(rid=rid)['errorCode']);self.assertEqual([],self.chats())
        with self.d.connect() as con:self.assertEqual('interrupted',con.execute('SELECT error FROM request_attempts').fetchone()[0])
    def test_server_length_limit_keeps_partial_answer_and_marks_it(self):
        self.available['desktop']=False;original=self.d.local_transport
        def limited(base,path,*a,**kw):
            value=original(base,path,*a,**kw)
            if path=='/api/chat':value['done_reason']='length'
            return value
        self.d.local_transport=limited;r=self.send()
        self.assertEqual('completed',r['status']);self.assertEqual('length',r['finishReason']);self.assertEqual('Привет, Иван!',r['answer'])
    def test_long_current_question_is_not_silently_truncated_or_sent_to_cloud(self):
        self.available['desktop']=False;r=self.send('я'*4000)
        self.assertEqual('server_context',r['errorCode']);self.assertEqual('server',r['backend']);self.assertEqual('я'*4000,r['message'])
        self.assertEqual([],self.chats())
    def test_fallback_flag_must_be_boolean(self):
        (self.root/'dialogue.json').write_text('{"provider":"ollama","fallbackEnabled":"true"}')
        self.assertFalse(self.d.view()['ready'])

class ServerPromptTests(unittest.TestCase):
    def test_broken_http_response_is_classified_as_connection_loss(self):
        with patch('urllib.request.OpenerDirector.open',side_effect=http.client.IncompleteRead(b'partial',100)),self.assertRaises(DialogueError) as ctx:
            local_request('http://127.0.0.1:11435','/api/chat',{})
        self.assertEqual('local_unavailable',ctx.exception.code)

    def test_budget_keeps_current_question_and_whole_recent_pairs(self):
        messages=[{'role':'system','content':'original'},{'role':'user','content':'Снимок: {}'}]
        for i in range(6):messages.extend([{'role':'user','content':f'Вопрос {i} '+('я'*150)},{'role':'assistant','content':f'Ответ {i} '+('а'*150)}])
        messages.append({'role':'user','content':'Текущий вопрос'})
        result=server_prompt(messages)
        self.assertEqual(messages[-1],result[-1]);self.assertLess(len(result),len(messages))
        self.assertEqual(0,(len(result)-3)%2)
        self.assertLessEqual(sum(len(m['content'].encode())+32 for m in result),4096-256-512)
        self.assertEqual(messages[-3:-1],result[-3:-1])

if __name__=='__main__':unittest.main()
