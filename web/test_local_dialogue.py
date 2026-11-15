import copy,io,json,sqlite3,tempfile,unittest,urllib.error,uuid
from pathlib import Path
from unittest.mock import patch
from openai_dialogue import Dialogue,DialogueError,LOCAL_DEFAULTS,local_request,parse_local_response

def reply(**extra):
    return dict(done=True,done_reason='stop',message={'role':'assistant','content':'Привет, Иван!'},
                prompt_eval_count=30,eval_count=5,**extra)

class LocalDialogueTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name)
        self.cfg=dict(LOCAL_DEFAULTS);self.save_config();self.calls=[];self.now=1000
        self.d=self.create()
    def tearDown(self):self.temp.cleanup()
    def save_config(self):(self.root/'dialogue.json').write_text(json.dumps(self.cfg))
    def create(self):
        return Dialogue(self.root,lambda:{},key_path=self.root/'absent-key',
            transport=lambda *a: self.fail('Local dialogue must never contact OpenAI'),
            clock=lambda:self.now,local_transport=self.local)
    def local(self,base,path,payload=None,timeout=120):
        self.calls.append((base,path,copy.deepcopy(payload)))
        return {'models':[{'name':self.cfg['model']}]} if path=='/api/tags' else reply()
    def send(self,rid=None):return self.d.send(rid or uuid.uuid4().hex,self.d.view()['conversationId'],'Привет')
    def test_local_works_without_api_key_and_uses_no_cloud_credentials(self):
        self.assertTrue(self.d.view()['ready']);r=self.send()
        self.assertEqual('completed',r['status']);self.assertEqual('ollama',r['provider'])
        payload=self.calls[-1][2]
        self.assertFalse(payload['think']);self.assertFalse(payload['stream'])
        self.assertEqual('system',payload['messages'][0]['role']);self.assertNotIn('store',payload)
        self.assertEqual(35,r['usage']['total_tokens'])
    def test_health_checks_never_generate_and_are_cached(self):
        self.d.view();self.d.view();self.assertEqual(['/api/tags'],[c[1] for c in self.calls])
    def test_old_misclassified_protocol_record_is_preserved_but_not_fed_back_to_model(self):
        conversation=self.d.view()['conversationId'];text='Скажи навыку Глубокий разум доставка 123456789012'
        self.d.send(uuid.uuid4().hex,conversation,text)
        self.now+=2;self.d.send(uuid.uuid4().hex,conversation,'Как работает доставка продуктов?')
        self.now+=2;self.send()
        messages=self.calls[-1][2]['messages']
        self.assertNotIn({'role':'user','content':text},messages)
        self.assertIn({'role':'user','content':'Как работает доставка продуктов?'},messages)
        history=self.d.view()['history'];self.assertEqual(3,len(history));self.assertEqual(text,history[0]['message'])
    def test_voice_retains_loaded_model_without_changing_shared_history_or_web_profile(self):
        conversation=self.d.view()['conversationId']
        first=self.d.send(uuid.uuid4().hex,conversation,'Голосовой вопрос',local_only=True)
        voice=self.calls[-1][2]
        self.assertEqual('30m',voice['keep_alive']);self.assertEqual(256,voice['options']['num_predict'])
        self.assertEqual(16384,voice['options']['num_ctx']);self.assertFalse(voice['think'])
        self.now+=2;second=self.send()
        web=self.calls[-1][2]
        self.assertEqual('10m',web['keep_alive'])
        self.assertIn({'role':'user','content':'Голосовой вопрос'},web['messages'])
        self.assertIn({'role':'assistant','content':first['answer']},web['messages'])
        self.assertEqual(2,len(self.d.view()['history']));self.assertEqual('completed',second['status'])
    def test_offline_is_visible_and_no_openai_fallback(self):
        self.d.local_transport=lambda *a,**k:(_ for _ in ()).throw(DialogueError('local_unavailable'))
        self.assertFalse(self.d.view()['ready']);self.assertEqual('offline',self.d.view()['connection'])
        r=self.send();self.assertEqual('local_unavailable',r['errorCode'])
    def test_missing_model_is_distinct_from_offline(self):
        self.d.local_transport=lambda *a,**k:{'models':[]}
        self.assertEqual('model_missing',self.d.view()['connection']);self.assertFalse(self.d.view()['ready'])
    def test_same_request_survives_restart_without_repeat_inference(self):
        rid=uuid.uuid4().hex;first=self.send(rid);self.d=self.create()
        self.assertEqual(first,self.send(rid))
        self.assertEqual(1,sum(c[1]=='/api/chat' for c in self.calls))
    def test_explicit_provider_switch_preserves_history_and_failed_request(self):
        rid=uuid.uuid4().hex;self.send(rid)
        (self.root/'dialogue.json').write_text('{"provider":"openai"}')
        self.d=self.create();self.assertEqual('openai',self.d.view()['provider'])
        self.assertEqual('ollama',self.d.view()['history'][0]['provider'])
        self.assertEqual('completed',self.send(rid)['status'])
    def test_bad_settings_never_fall_back_to_openai(self):
        for changes in ({'provider':'bad'},{'model':'qwen3.5:cloud'},{'baseUrl':'http://10.10.10.91:11434'},
                        {'baseUrl':'http://127.0.0.1:11435@evil.invalid'}, {'maxOutputTokens':True}):
            with self.subTest(changes=changes):
                self.cfg=dict(LOCAL_DEFAULTS,**changes);self.save_config()
                self.assertFalse(self.d.view()['ready'])
                with self.assertRaises(DialogueError):self.send()
        self.assertEqual([],self.calls)
    def test_legacy_database_migrates_without_losing_history_or_conversation(self):
        other=self.root/'legacy';other.mkdir();con=sqlite3.connect(other/'dialogue.sqlite3')
        conversation=uuid.uuid4().hex;rid=uuid.uuid4().hex
        con.executescript('CREATE TABLE settings(name TEXT PRIMARY KEY,value TEXT NOT NULL); CREATE TABLE requests(seq INTEGER PRIMARY KEY,id TEXT UNIQUE,conversation TEXT,at REAL,day TEXT,message TEXT,model TEXT,status TEXT,answer TEXT,error TEXT,usage TEXT);')
        con.execute('INSERT INTO settings VALUES(?,?)',('conversation',conversation))
        con.execute('INSERT INTO requests VALUES(?,?,?,?,?,?,?,?,?,?,?)',(1,rid,conversation,12,'day','old question','old model','completed','old answer',None,None))
        con.commit();before=con.execute('SELECT * FROM requests').fetchall();con.close()
        d=Dialogue(other,lambda:{},key_path=other/'missing')
        self.assertEqual(conversation,d.view()['conversationId'])
        self.assertEqual('openai',d.view()['history'][0]['provider'])
        with d.connect() as con:self.assertEqual(before,[tuple(r)[:len(before[0])] for r in con.execute('SELECT * FROM requests')])

class LocalProtocolTests(unittest.TestCase):
    def test_reasoning_is_not_exposed_as_answer(self):
        value=reply();value['message']['thinking']='private intermediate text'
        self.assertEqual('Привет, Иван!',parse_local_response(value)[0])
    def test_incomplete_tool_and_malformed_outputs_are_rejected(self):
        for changes in ({'done':False},{'done_reason':'length'},{'eval_count':-1},
                        {'message':{'role':'assistant','content':'','thinking':'only thoughts'}},
                        {'message':{'role':'assistant','content':'hi','tool_calls':[{}]}}):
            value=reply();value.update(changes)
            with self.subTest(changes=changes),self.assertRaises(DialogueError):parse_local_response(value)
    def test_local_transport_omits_authorization_and_redacts_errors(self):
        ex=urllib.error.HTTPError('http://127.0.0.1:11435/api/chat',404,'',{},io.BytesIO(b'secret payload'))
        with patch('urllib.request.OpenerDirector.open',side_effect=ex) as op,self.assertRaises(DialogueError) as ctx:
            local_request(LOCAL_DEFAULTS['baseUrl'],'/api/chat',{})
        self.assertEqual('local_model',ctx.exception.code)
        self.assertNotIn('Authorization',dict(op.call_args.args[0].header_items()))
        self.assertNotIn('secret',str(ctx.exception))

if __name__=='__main__':unittest.main()
