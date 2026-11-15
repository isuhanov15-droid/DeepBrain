import json
import threading
import unittest
import uuid
from unittest.mock import patch
from conversation_bridge import ConversationBridge
from openai_dialogue import compact_state
from semantic_appraisal import fallback
import test_local_dialogue as fixtures


class ConversationTests(unittest.TestCase):
    setUp=fixtures.LocalDialogueTests.setUp
    tearDown=fixtures.LocalDialogueTests.tearDown
    create=fixtures.LocalDialogueTests.create
    local=fixtures.LocalDialogueTests.local
    save_config=fixtures.LocalDialogueTests.save_config
    def ask(self,message):
        self.now+=6
        return self.d.send(uuid.uuid4().hex,self.d.view()['conversationId'],message)
    def bridge(self,transport=None):
        def post(p):
            self.received.append(p)
            return dict(sequence=p['sequence'],requestId=p['requestId'],status='stored')
        if not hasattr(self,'received'):self.received=[]
        class Appraiser:
            def assess(self,message,history):return fallback('Тестовая неопределённость')
        return ConversationBridge(self.d,'http://127.0.0.1:8787',threading.Event(),transport or post,appraiser=Appraiser())
    def test_old_history_is_preserved_and_imported_without_current_emotion(self):
        row=self.ask('Спасибо, Лада')
        before=self.d.archive()
        b=self.bridge();self.assertEqual(1,b.step());self.assertTrue(self.received[0]['historical'])
        self.assertEqual(before,self.d.archive());self.assertEqual(0,b.status()['pending'])
        self.assertEqual(0,b.step());self.assertEqual(1,len(self.received))
    def test_new_chat_and_restart_keep_cursor_and_new_messages_are_live(self):
        b=self.bridge();self.ask('Мне грустно');b.step()
        self.assertFalse(self.received[0]['historical'])
        self.d.new_conversation();self.ask('Как дела?')
        self.d=self.create();b=self.bridge();b.step()
        self.assertEqual(2,len(self.received));self.assertFalse(self.received[1]['historical'])
    def test_failure_or_lost_ack_retries_same_identity_without_losing_history(self):
        b=self.bridge();self.ask('Спасибо');sent=[]
        def fail(p):sent.append(p);raise TimeoutError('accepted but receipt lost')
        b.transport=fail;self.assertEqual(0,b.step());self.assertEqual(1,b.status()['pending'])
        b=self.bridge();b.step();self.assertEqual(sent[0],self.received[0]);self.assertEqual(0,b.status()['pending'])
    def test_wrong_receipt_does_not_advance(self):
        b=self.bridge(lambda p:{'sequence':p['sequence']+1,'requestId':p['requestId'],'status':'stored'})
        self.ask('Привет');b.step();self.assertEqual(1,b.status()['pending']);self.assertTrue(b.status()['error'])
    def test_delivery_protocol_is_retained_in_history_but_never_experience(self):
        self.ask('Скажи навыку Глубокий разум доставка 123456789012')
        b=self.bridge();b.step();self.assertEqual([],self.received);self.assertEqual(0,b.status()['pending'])
        self.assertEqual(1,len(self.d.archive()['history']))
    def test_no_assistant_text_or_secrets_leave_bridge_and_order_is_stable(self):
        b=self.bridge();self.ask('Первое сообщение');self.ask('Второе сообщение');b.step()
        self.assertEqual(sorted(x['sequence'] for x in self.received),[x['sequence'] for x in self.received])
        self.assertEqual({'sequence','requestId','at','message','historical','semantic'},set(self.received[0]))
    def test_forget_exclusions_cannot_leak_via_core_working_memory(self):
        state=compact_state({'stateAt':self.now,'state':{'cognition':{'enabled':True,'workingMemory':[
            {'kind':'conversation','text':'forgotten private fact','ageTicks':2}]},
            'conversation':{'experiences':4,'last':{'excerpt':'secret'},'valenceInfluence':.1}}},self.now)
        self.assertNotIn('private',json.dumps(state));self.assertNotIn('secret',json.dumps(state))
        self.assertEqual(4,state['conversationExperience']['experiences'])
    def test_remote_destination_rejected(self):
        with self.assertRaises(ValueError):ConversationBridge(self.d,'https://example.com',threading.Event())

if __name__=='__main__':unittest.main()
