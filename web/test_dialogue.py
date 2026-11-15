import copy
import io
import json
from pathlib import Path
import tempfile
import threading
import unittest
from unittest.mock import patch
import urllib.error
import uuid

from openai_dialogue import Dialogue,DialogueError,NoRedirect,compact_state,parse_response,response_transport


def response(text='Привет!', **extra):
    return {'status':'completed','output':[{'type':'reasoning'},{'type':'message','role':'assistant',
            'content':[{'type':'output_text','text':text}]}],
            'usage':{'input_tokens':20,'output_tokens':5,'total_tokens':25}, **extra}


class DialogueTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory()
        self.root=Path(self.temp.name)
        self.key=self.root/'key';self.key.write_text('sk-'+('test-key-only-'*4));self.key.chmod(0o600)
        self.now=1794740400.0
        self.calls=[]
        self.snapshot={'stateAt':self.now,'state':{'tick':42,'homeostasis':{'energy':.7},
            'scenario':{'name':'novelty_walk'},'circadian':{'isSleeping':False},
            'journal':'SECRET JOURNAL','semanticNotesTop':['PRIVATE MEMORY'], 'password':'NEVER SEND'}}
        self.original=copy.deepcopy(self.snapshot)
        self.d=self.create()
    def create(self, transport=None):
        return Dialogue(self.root,lambda:self.snapshot,self.key,transport or self.transport,lambda:self.now)
    def transport(self,payload,key):
        self.calls.append((copy.deepcopy(payload),key));return response()
    def tearDown(self):self.temp.cleanup()
    def send(self,message='Как дела?',request_id=None):
        return self.d.send(request_id or uuid.uuid4().hex,self.d.view()['conversationId'],message)
    def config(self,**kwargs):
        (self.root/'openai.json').write_text(json.dumps(kwargs))
    def expect_error(self,code,call):
        with self.assertRaises(DialogueError) as ctx:call()
        self.assertEqual(code,ctx.exception.code)
    def test_only_explicit_send_calls_openai_and_state_is_not_mutated(self):
        self.d.view();self.d.new_conversation();self.assertEqual([],self.calls)
        result=self.send();self.assertEqual('completed',result['status']);self.assertEqual(1,len(self.calls))
        payload,key=self.calls[0]
        self.assertEqual('gpt-5.6-luna',payload['model']);self.assertIs(False,payload['store'])
        self.assertNotIn('tools',payload);self.assertNotIn('previous_response_id',payload)
        self.assertEqual(self.original,self.snapshot)
        self.assertNotIn('PRIVATE MEMORY',json.dumps(payload));self.assertNotIn('NEVER SEND',json.dumps(payload))
        self.assertNotIn(key,json.dumps(self.d.view()))
    def test_no_key_or_disabled_never_contacts_api(self):
        self.key.unlink();self.expect_error('missing_key',self.send)
        self.config(enabled=False);self.expect_error('disabled',self.send);self.assertFalse(self.calls)
    def test_key_is_read_without_restart(self):
        self.key.unlink();self.assertFalse(self.d.view()['ready'])
        self.key.write_text('sk-'+('x'*30));self.key.chmod(0o600)
        self.assertTrue(self.d.view()['ready']);self.assertEqual('completed',self.send()['status'])
    def test_duplicate_request_survives_restart_without_second_charge(self):
        rid=uuid.uuid4().hex;first=self.send(request_id=rid)
        self.d=self.create();second=self.send(request_id=rid)
        self.assertEqual(first,second);self.assertEqual(1,len(self.calls))
        self.expect_error('request_conflict',lambda:self.send('Different',rid))
    def test_history_is_persistent_and_new_conversation_does_not_delete_it(self):
        self.send();old_id=self.d.view()['conversationId'];self.d=self.create()
        self.assertEqual(1,len(self.d.view()['history']))
        self.d.new_conversation();self.assertEqual([],self.d.view()['history'])
        with self.d.connect() as con:self.assertEqual(1,con.execute('select count(*) from requests where conversation=?',(old_id,)).fetchone()[0])
        self.now+=6;self.send('Новое сообщение');self.assertNotIn('Как дела?',json.dumps(self.calls[-1][0],ensure_ascii=False))
    def test_history_is_sent_in_chronological_order(self):
        self.send('Первый');self.now+=6;self.send('Второй')
        messages=self.calls[-1][0]['input']
        self.assertEqual(['Первый','Привет!','Второй'],[r['content'] for r in messages[-3:]])
    def test_persistent_daily_quota_and_interval_include_failed_requests(self):
        self.config(dailyRequests=2)
        self.send();self.expect_error('rate_limit',self.send)
        self.now+=6;self.d=self.create(lambda p,k: (_ for _ in ()).throw(DialogueError('network')))
        self.assertEqual('failed',self.send()['status'])
        self.now+=6;self.d=self.create();self.expect_error('daily_limit',self.send)
        self.now+=86400;self.assertEqual('completed',self.send()['status'])
    def test_restart_marks_pending_unknown_without_resubmitting(self):
        rid=uuid.uuid4().hex;conversation=self.d.view()['conversationId']
        with self.d.connect() as con:
            con.execute("insert into requests(id,conversation,at,day,message,model,status) values(?,?,?,?,?,?,?)",
                        (rid,conversation,self.now,'2026-11-15','Привет','test','pending'))
        self.d=self.create();result=self.send('Привет',rid)
        self.assertEqual('interrupted',result['errorCode']);self.assertFalse(self.calls)
    def test_parallel_request_is_rejected_without_extra_call(self):
        entered=threading.Event();release=threading.Event()
        def slow(p,k):entered.set();release.wait(3);return self.transport(p,k)
        self.d=self.create(slow);thread=threading.Thread(target=self.send);thread.start()
        try:
            self.assertTrue(entered.wait(2));self.expect_error('busy',self.send)
            self.expect_error('busy',self.d.new_conversation)
        finally:release.set();thread.join()
        self.assertEqual(1,len(self.calls))
    def test_bad_input_is_rejected_before_network(self):
        for value in ('',' '*5,'x'*4001,None,{}):self.expect_error('invalid_message',lambda:self.send(value))
        self.expect_error('invalid_request',lambda:self.send(request_id='../bad'))
        self.expect_error('conversation_changed',lambda:self.d.send(uuid.uuid4().hex,uuid.uuid4().hex,'Привет'))
        self.assertFalse(self.calls)
    def test_malformed_config_does_not_leak_secrets_or_enable_requests(self):
        self.config(apiKey='sk-SHOULD-NOT-BE-CONFIG')
        view=self.d.view();self.assertFalse(view['ready']);self.assertNotIn('sk-',json.dumps(view))
        self.expect_error('configuration',self.send)
    def test_context_history_has_a_character_bound(self):
        self.d=self.create(lambda p,k:(self.calls.append((p,k)) or response('a'*3000)))
        for i in range(8):self.send(str(i)+'b'*3000);self.now+=6
        self.assertLess(sum(len(m['content']) for m in self.calls[-1][0]['input']),16000)
    def test_unknown_transport_error_is_redacted(self):
        self.d=self.create(lambda p,k:(_ for _ in ()).throw(RuntimeError(k)))
        result=self.send();self.assertEqual('upstream',result['errorCode'])
        self.assertNotIn(self.key.read_text(),json.dumps(self.d.view()))


class ProtocolTests(unittest.TestCase):
    def test_message_output_need_not_be_first(self):
        self.assertEqual('Привет!',parse_response(response())[0])
    def test_multiple_text_parts_and_refusal(self):
        value=response();value['output'][1]['content']=[{'type':'output_text','text':'Один'},{'type':'output_text','text':'Два'}]
        self.assertEqual('Один\nДва',parse_response(value)[0])
        value['output'][1]['content']=[{'type':'refusal','refusal':'Не могу помочь с этим.'}]
        self.assertIn('Не могу',parse_response(value)[0])
    def test_incomplete_invalid_and_tool_output_are_rejected(self):
        for value in (None,{},response(status='incomplete'),response(output=[]),response(output=[{'type':'function_call'}]),response(usage={'total_tokens':-1})):
            with self.subTest(value=value),self.assertRaises(DialogueError):parse_response(value)
    def test_stale_or_missing_state_is_labelled_unavailable(self):
        self.assertEqual({'available':False},compact_state({},100))
        self.assertEqual({'available':False},compact_state({'state':{},'stateAt':1},100))
        self.assertEqual({'available':False},compact_state({'state':{},'stateAt':float('nan')},100))
    def test_real_deepbrain_contract_exposes_mood_action_and_goal_only(self):
        state={'affect':{'mood':'calm','valence':.5},'decision':{'actionName':'rest_short','reason':'private'},
               'executive':{'plan':{'goalId':'restore_energy','goalTitle':'Восстановить силы','progress':.3,'rationale':'private'}}}
        out=compact_state({'stateAt':100,'state':state},101)
        self.assertEqual('calm',out['affect']['mood']);self.assertEqual('rest_short',out['decision']['actionName'])
        self.assertEqual('restore_energy',out['goal']['goalId']);self.assertEqual(.3,out['goal']['progress'])
        self.assertNotIn('private',json.dumps(out))
    def test_redirect_never_forwards_authorization(self):
        with self.assertRaises(DialogueError):NoRedirect().redirect_request(None,None,302,'',{},'https://elsewhere.invalid/')
    def test_http_error_mapping_never_exposes_body(self):
        for status,code,expected in [(401,'invalid_api_key','authentication'),(403,'','access_denied'),(404,'model_not_found','model_unavailable'),(429,'insufficient_quota','quota'),(429,'rate_limit_exceeded','upstream_limit'),(500,'','upstream')]:
            with self.subTest(status=status,code=code):
                body=json.dumps({'error':{'code':code,'message':'sk-SECRET'}}).encode()
                ex=urllib.error.HTTPError('https://api.openai.com/v1/responses',status,'',{},io.BytesIO(body))
                with patch('urllib.request.OpenerDirector.open',side_effect=ex),self.assertRaises(DialogueError) as ctx:response_transport({},'sk-SECRET')
                self.assertEqual(expected,ctx.exception.code);self.assertNotIn('sk-SECRET',str(ctx.exception))


if __name__=='__main__':unittest.main(verbosity=2)
