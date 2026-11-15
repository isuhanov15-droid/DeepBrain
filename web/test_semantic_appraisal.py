import copy
import json
import socket
import threading
import time
import unittest
import uuid
from unittest.mock import patch
from conversation_bridge import ConversationBridge
from semantic_appraisal import SemanticAppraiser,Deferred,Unavailable,Oversized,validate,make_messages,cancellable_request,MODELS
import test_local_dialogue as fixtures

def result(kind='correction',irony='likely',confidence=.9,ambiguous=False):
    return dict(kind=kind,irony=irony,confidence=confidence,ambiguous=ambiguous,target='assistant',reason='Противоречие между благодарностью и неудачным советом.')
def reply(value=None):return {'done':True,'done_reason':'stop','message':{'role':'assistant','content':json.dumps(value or result())}}

class AppraisalTests(unittest.TestCase):
    setUp=fixtures.LocalDialogueTests.setUp
    tearDown=fixtures.LocalDialogueTests.tearDown
    create=fixtures.LocalDialogueTests.create
    local=fixtures.LocalDialogueTests.local
    save_config=fixtures.LocalDialogueTests.save_config
    def ask(self,text):
        self.now+=6
        return self.d.send(uuid.uuid4().hex,self.d.view()['conversationId'],text)
    def bridge(self,transport=None,post=None):
        self.receipts=[]
        def send(p):self.receipts.append(copy.deepcopy(p));return dict(sequence=p['sequence'],requestId=p['requestId'],status='stored')
        appraiser=SemanticAppraiser(self.d,threading.Event(),transport or (lambda *a:reply()))
        return ConversationBridge(self.d,'http://127.0.0.1:8787',threading.Event(),post or send,appraiser)
    def test_context_uses_prior_pairs_but_not_current_reply_or_other_chat(self):
        self.ask('Другой диалог');self.d.new_conversation();self.ask('Твой совет не помог')
        b=self.bridge();captured=[]
        b.appraiser.transport=lambda url,p,c,t:(captured.append(copy.deepcopy(p)) or reply())
        self.ask('Ну спасибо, отлично помогла');b.step()
        self.assertEqual(1,len(captured))
        data=json.loads(captured[0]['messages'][1]['content'])
        self.assertEqual('Ну спасибо, отлично помогла',data['current']);self.assertEqual(1,len(data['history']))
        self.assertEqual('Твой совет не помог',data['history'][0]['user']);self.assertNotIn('Другой диалог',str(data))
        self.assertEqual('qwen',self.receipts[-1]['semantic']['source'])
    def test_receipt_loss_reuses_frozen_assessment_after_restart(self):
        calls=[];sent=[]
        def analyse(*a):calls.append(1);return reply()
        def lose(p):sent.append(p);raise TimeoutError()
        b=self.bridge(analyse,lose);self.ask('Ну спасибо');b.step()
        self.d=self.create();b=self.bridge(lambda *a:self.fail('Saved analysis must not be repeated'))
        b.step();self.assertEqual(sent[0],self.receipts[0]);self.assertEqual(1,len(calls))
    def test_bad_model_data_becomes_uncertainty_not_lexical_gratitude(self):
        b=self.bridge(lambda *a:reply(dict(result(),confidence=float('nan'))));self.ask('Спасибо');b.step()
        s=self.receipts[-1]['semantic'];self.assertEqual('unavailable',s['source']);self.assertEqual(0,s['confidence'])
        self.assertTrue(s['ambiguous']);self.assertEqual('neutral',s['kind'])
    def test_failover_uses_4b_without_cloud_and_records_actual_model(self):
        self.cfg['fallbackEnabled']=True;self.save_config();calls=[]
        def transport(url,p,c,t):
            calls.append((url,p))
            if '11435' in url:raise Unavailable()
            return reply({k:v for k,v in result().items() if k not in ('reason','target')})
        b=self.bridge(transport);self.ask('Ну спасибо');b.step()
        self.assertEqual(MODELS[1],self.receipts[-1]['semantic']['model'])
        self.assertEqual(4096,calls[-1][1]['options']['num_ctx']);self.assertEqual(2,len(calls))
    def test_new_foreground_question_preempts_analysis_without_busy_error(self):
        started=threading.Event();cancelled=threading.Event()
        def wait(url,p,signal,timeout):
            started.set()
            while not signal.is_set():time.sleep(.01)
            cancelled.set();raise Deferred()
        b=self.bridge(wait);self.ask('Предыдущий вопрос')
        thread=threading.Thread(target=b.step);thread.start();self.assertTrue(started.wait(2))
        r=self.ask('Новый вопрос важнее');thread.join(2)
        self.assertEqual('completed',r['status']);self.assertTrue(cancelled.is_set());self.assertFalse(thread.is_alive())
        self.assertEqual([],self.receipts);self.assertIsNone(b.status()['error'])
    def test_pending_inference_is_not_blocked_or_analysed(self):
        b=self.bridge(lambda *a:self.fail('Pending foreground must take precedence'))
        r=self.ask('Привет')
        with self.d.connect() as c:c.execute("UPDATE requests SET status='pending' WHERE id=?",(r['requestId'],))
        b.step();self.assertEqual([],self.receipts)
    def test_forgetting_excludes_context_and_command_does_not_go_to_qwen(self):
        self.ask('Лада, запомни: любимый цвет бирюзовый')
        self.ask('Лада, забудь бирюзовый');b=self.bridge();calls=[]
        b.appraiser.transport=lambda u,p,c,t:(calls.append(p) or reply())
        self.ask('Как дела?');b.step();self.assertNotIn('бирюзовый',json.dumps(calls,ensure_ascii=False))
    def test_historical_upgrade_does_not_reanalyse_or_change_existing_experience(self):
        self.ask('Спасибо');b=self.bridge(lambda *a:self.fail('No historical reinterpretation'))
        b.step();self.assertNotIn('semantic',self.receipts[0]);self.assertTrue(self.receipts[0]['historical'])
    def test_failed_complete_response_or_tool_call_is_not_applied(self):
        for value in (dict(reply(),done_reason='length'),{'done':True,'done_reason':'stop','message':{'role':'assistant','content':'{}','tool_calls':[{}]}}):
            a=SemanticAppraiser(self.d,threading.Event(),lambda *a:value)
            self.assertEqual('unavailable',a.assess('Привет',[])['semantic']['source'])
    def test_deferred_analysis_is_not_persisted_as_failure(self):
        def defer(*a):raise Deferred()
        b=self.bridge(defer);self.ask('Привет');b.step()
        with self.d.connect() as c:self.assertEqual(0,c.execute('SELECT count(*) FROM conversation_appraisals').fetchone()[0])
        self.assertIsNone(b.error)

class ValidationTests(unittest.TestCase):
    def test_only_bounded_categories_and_finite_confidence_are_allowed(self):
        for update in ({'confidence':True},{'confidence':float('inf')},{'confidence':-1},{'kind':'run_command'},
            {'ambiguous':'false'},{'target':'other_account'},{'reason':'x'*241},{'action':'delete'}):
            with self.subTest(update=update),self.assertRaises(ValueError):validate(dict(result(),**update))
    def test_complete_current_message_and_pairs_fit_4b_or_abstain(self):
        history=[{'user':'Вопрос'+str(i),'assistant':'Ответ'+str(i)} for i in range(8)]
        messages,count=make_messages('Ну спасибо',history,True)
        self.assertLessEqual(sum(len(m['content'].encode()) for m in messages)+64,4096-256-600)
        self.assertEqual('Ну спасибо',json.loads(messages[1]['content'])['current'])
        with self.assertRaises(Oversized):make_messages('я'*4000,history,True)
    def test_actual_http_connection_closes_when_foreground_cancels(self):
        # A tiny local stand-in waits forever after reading the request; cancellation
        # must release it rather than leave an inference occupying the model queue.
        listener=socket.socket();listener.bind(('127.0.0.1',0));listener.listen(1)
        port=listener.getsockname()[1];received=threading.Event();disconnected=threading.Event();signal=threading.Event()
        def serve():
            conn,_=listener.accept();conn.settimeout(3)
            try:
                conn.recv(32768);received.set()
                while conn.recv(32768):pass
                disconnected.set()
            finally:conn.close();listener.close()
        server=threading.Thread(target=serve);server.start()
        original=http_connection=__import__('http.client',fromlist=['HTTPConnection']).HTTPConnection
        def connection(host,unused,timeout):return original(host,port,timeout=timeout)
        errors=[]
        def request():
            try:cancellable_request('http://127.0.0.1:11435',{},signal,3)
            except Exception as ex:errors.append(type(ex))
        with patch('semantic_appraisal.http.client.HTTPConnection',connection):
            client=threading.Thread(target=request);client.start();self.assertTrue(received.wait(2));signal.set();client.join(2);server.join(2)
        self.assertEqual([Deferred],errors);self.assertTrue(disconnected.is_set())

if __name__=='__main__':unittest.main()
