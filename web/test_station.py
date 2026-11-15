import json,threading,time,unittest
from unittest.mock import patch
import test_alice
from alice_bridge import AliceBridge,speech_chunks
from station_delivery import StationDelivery
from station_transport import invocation,StationError,exchange
from voice_protocol import delivery_command

class StationTests(unittest.TestCase):
    setUp=test_alice.AliceTests.setUp
    tearDown=test_alice.AliceTests.tearDown
    dialogue=test_alice.AliceTests.dialogue
    local=test_alice.AliceTests.local
    request=test_alice.AliceTests.request
    pair=test_alice.AliceTests.pair
    done=test_alice.AliceTests.done
    def prepare(self):
        self.pair();self.sent=[];self.spoken=[]
        (self.data/'station-output.json').write_text(json.dumps({'enabled':True,'musicToken':'fake-local-test-only'}))
        self.a.delivery.reachable_until=time.monotonic()+100
        self.a.delivery.transport=self.deliver
    def deliver(self,number=None):
        if number is None:return {'status':'ready'}
        self.sent.append(number)
        response=self.a.handle(self.request('доставка '+number));self.spoken.append(response)
        return {'status':'spoken','sent':True,'aliceState':'IDLE' if response['response']['end_session'] else 'LISTENING'}
    def generate(self):
        self.a.worker=threading.Thread(target=self.a.work,daemon=True);self.a.worker.start();self.done()
    def queued_question(self):
        self.a.wait_seconds=0
        result=self.a.handle(self.request('Как твои дела?'))
        self.assertTrue(result['response']['end_session']);self.assertIn('автоматически',result['response']['text'])
        self.generate();self.now+=20
        return result
    def row(self):
        with self.a.connect() as c:return c.execute('SELECT * FROM station_deliveries ORDER BY rowid DESC LIMIT 1').fetchone()
    def test_full_invocation_is_delivery_not_a_new_question_and_replays_once(self):
        self.prepare();self.queued_question();replays=[]
        def full_delivery(number):
            request=self.request('Скажи навыку Глубокий разум доставка '+number)
            request['request']['command']='доставка '+number
            first=self.a.handle(request);again=self.a.handle(request)
            self.assertEqual(first,again);replays.append(first)
            return {'status':'spoken','sent':True,'aliceState':'LISTENING'}
        self.a.delivery.transport=full_delivery;self.a.delivery.step()
        self.assertEqual('done',self.row()['state']);self.assertEqual(self.answer,replays[0]['response']['text'])
        self.assertEqual(1,len(self.calls));self.assertEqual(1,len(self.d.view()['history']))
        with self.a.connect() as c:self.assertEqual(1,c.execute('SELECT count(*) FROM jobs').fetchone()[0])
    def test_stale_prefixed_delivery_cannot_cancel_current_answer_or_reach_qwen(self):
        self.prepare();self.queued_question()
        for utterance in ('скажи навыку глубокий разум доставка 000000000000',
                          'скажи навыку глубокий разум доставка 123',
                          'доставка 000000000000'):
            result=self.a.handle(self.request(utterance))
            self.assertTrue(result['response']['end_session']);self.assertNotEqual(self.answer,result['response']['text'])
            self.assertEqual('queued',self.row()['state']);self.assertEqual(1,len(self.calls))
        with self.a.connect() as c:self.assertEqual(1,c.execute('SELECT count(*) FROM jobs').fetchone()[0])
    def test_delivery_grammar_only_matches_protocol_and_still_checks_owner(self):
        for text in ('доставка 123456789012','Алиса, скажи навыку Глубокий разум: доставка 1 2 3 4 5 6 7 8 9 0 1 2'):
            self.assertEqual((True,'123456789012'),delivery_command(text))
        for text in ('Как работает доставка 123456789012?', 'Доставка продуктов', 'доставка 123'):
            self.assertEqual((False,None),delivery_command(text))
        self.prepare();self.queued_question()
        response=self.a.handle(self.request('скажи навыку глубокий разум доставка 123456789012',user='other'))
        self.assertTrue(response['response']['end_session']);self.assertNotEqual(self.answer,response['response']['text'])
        self.assertEqual('queued',self.row()['state']);self.assertEqual(1,len(self.calls))
    def test_transport_diagnostics_do_not_include_credentials_or_commands(self):
        self.prepare();self.queued_question()
        def send(number):return {**self.deliver(number),'musicToken':'private-token','text':'private-command','error':'sensitive error text'}
        self.a.delivery.transport=send;self.a.delivery.step()
        metadata=self.a.delivery.status()['lastDeliveryTransport']
        self.assertEqual('spoken',metadata['status']);self.assertNotIn('private',json.dumps(metadata));self.assertNotIn('error',metadata)
    def test_wait_is_silent_quick_and_replay_safe(self):
        self.prepare();self.a.wait_seconds=1;self.gate=threading.Event()
        self.a.worker=threading.Thread(target=self.a.work,daemon=True);self.a.worker.start()
        request=self.request('Вопрос для тихого ожидания')
        started=time.monotonic();response=self.a.handle(request)
        self.assertLess(time.monotonic()-started,.7)
        self.assertEqual('sil <[100]>',response['response']['tts']);self.assertTrue(response['response']['end_session'])
        self.assertEqual(response,self.a.handle(request))
        self.assertEqual('sil <[100]>',self.a.handle(self.request('Готово?'))['response']['tts'])
        self.assertEqual('sil <[100]>',self.a.handle(self.request('Второй вопрос'))['response']['tts'])
        self.gate.set();self.done();self.assertEqual(1,len(self.calls))
        self.assertEqual(1,len(self.d.view()['history']))
    def test_ready_answer_delivered_without_eight_second_floor(self):
        self.prepare();self.a.wait_seconds=0
        self.a.handle(self.request('Короткий вопрос'));self.generate()
        self.assertFalse(self.a.delivery.step())
        self.now+=.6
        self.assertTrue(self.a.delivery.step());self.assertEqual('done',self.row()['state'])
        self.assertNotIn('tts',self.spoken[0]['response'])
    def test_busy_retry_does_not_wait_four_more_seconds_or_send_early(self):
        self.prepare();self.queued_question()
        self.a.delivery.transport=lambda n:{'status':'busy','sent':False}
        self.a.delivery.step();self.assertEqual('queued',self.row()['state'])
        self.a.delivery.transport=self.deliver
        self.assertFalse(self.a.delivery.step());self.now+=1.1
        self.assertTrue(self.a.delivery.step());self.assertEqual('done',self.row()['state'])
    def test_automatic_delivery_uses_saved_answer_once_and_shared_history(self):
        self.prepare();self.queued_question();self.assertTrue(self.a.delivery.step())
        self.assertEqual('done',self.row()['state']);self.assertEqual(self.answer,self.spoken[0]['response']['text'])
        self.assertEqual(1,len(self.calls));self.assertEqual(1,len(self.d.view()['history']))
        self.assertFalse(self.a.delivery.step());self.assertEqual(1,len(self.sent))
        response=self.a.handle(self.request('доставка '+self.sent[0]))
        self.assertNotEqual(self.answer,response['response']['text']);self.assertEqual(1,len(self.calls))
    def test_all_long_answer_chunks_delivered_in_order_without_poll(self):
        self.prepare();self.answer='Первое предложение. '*150;self.queued_question()
        for _ in range(10):
            self.a.delivery.step();self.now+=3
            if self.row()['state']=='done':break
        self.assertEqual('done',self.row()['state'])
        self.assertEqual(speech_chunks(self.answer),[r['response']['text'] for r in self.spoken])
        self.assertTrue(all(r['response']['end_session'] for r in self.spoken[:-1]))
        self.assertFalse(self.spoken[-1]['response']['end_session']);self.assertEqual(1,len(self.calls))
    def test_fast_long_answer_queues_only_remaining_chunks(self):
        self.prepare();self.answer='Первое предложение. '*90;self.generate()
        self.a.wait_seconds=.3
        first=self.a.handle(self.request('Вопрос'));self.done();self.now+=20
        self.assertEqual(speech_chunks(self.answer)[0],first['response']['text'])
        self.assertEqual(1,self.row()['part']);self.a.delivery.step()
        self.assertEqual(speech_chunks(self.answer)[1],self.spoken[0]['response']['text'])
    def test_old_history_never_enqueued_when_enabling_output(self):
        self.pair();self.generate();self.a.handle(self.request('Старый вопрос'));self.done()
        (self.data/'station-output.json').write_text('{"enabled":true,"musicToken":"test"}')
        self.assertFalse(self.a.delivery.step())
        with self.a.connect() as c:self.assertEqual(0,c.execute('SELECT count(*) FROM station_deliveries').fetchone()[0])
    def test_manual_poll_cancels_queued_automatic_delivery(self):
        self.prepare();self.queued_question()
        response=self.a.handle(self.request('Готово?'));self.assertEqual(self.answer,response['response']['text'])
        self.assertEqual('cancelled',self.row()['state']);self.assertFalse(self.a.delivery.step());self.assertEqual([],self.sent)
    def test_uncertain_send_not_retried_answer_remains_available(self):
        self.prepare();self.queued_question()
        self.a.delivery.transport=lambda n:{'status':'uncertain','sent':True}
        self.a.delivery.step();self.assertEqual('uncertain',self.row()['state'])
        self.now+=50;self.assertFalse(self.a.delivery.step())
        self.assertEqual(self.answer,self.a.handle(self.request('Готово?'))['response']['text'])
        self.assertEqual(self.answer,self.d.view()['history'][0]['answer'])
    def test_busy_station_retries_only_before_a_send(self):
        self.prepare();self.queued_question();self.a.delivery.transport=lambda n:{'status':'busy','sent':False}
        self.a.delivery.step();self.assertEqual('queued',self.row()['state'])
        self.a.delivery.transport=self.deliver;self.now+=5;self.a.delivery.step()
        self.assertEqual('done',self.row()['state']);self.assertEqual(1,len(self.calls))
    def test_new_conversation_or_expiration_prevents_late_speech(self):
        for change in ('conversation','expiry'):
            with self.subTest(change=change):
                if change=='conversation':
                    self.prepare();self.queued_question();self.d.new_conversation()
                else:
                    with self.a.connect() as c:c.execute("UPDATE station_deliveries SET state='queued',expires_at=0")
                self.a.delivery.step();self.assertIn(self.row()['state'],('cancelled','expired'));self.assertEqual([],self.sent)
    def test_exit_cancels_pending_speech(self):
        self.prepare();self.queued_question();self.a.handle(self.request('Хватит'))
        self.assertEqual('cancelled',self.row()['state']);self.assertFalse(self.a.delivery.step())
    def test_restart_resumes_unsent_but_not_uncertain_delivery(self):
        self.prepare();self.queued_question();self.a.delivery=StationDelivery(self.a,self.deliver)
        self.a.delivery.step();self.assertEqual('done',self.row()['state'])
        with self.a.connect() as c:c.execute("UPDATE station_deliveries SET state='awaiting'")
        self.a.delivery=StationDelivery(self.a,self.deliver)
        self.assertEqual('uncertain',self.row()['state']);self.assertFalse(self.a.delivery.step());self.assertEqual(1,len(self.sent))
    def test_callback_wrong_owner_cannot_read_or_advance_answer(self):
        self.prepare();self.queued_question()
        def bad(number):
            response=self.a.handle(self.request('доставка '+number,user='other'))
            self.assertTrue(response['response']['end_session']);self.assertNotIn(self.answer,response['response']['text'])
            return {'status':'spoken','sent':True,'aliceState':'IDLE'}
        self.a.delivery.transport=bad;self.a.delivery.step()
        self.assertEqual('uncertain',self.row()['state']);self.assertEqual(0,self.a.latest()['cursor'])
    def test_poll_while_transport_in_flight_does_not_advance(self):
        self.prepare();self.queued_question()
        def send(number):
            result=self.a.handle(self.request('Готово?'));self.assertIn('передаётся',result['response']['text'])
            self.assertEqual(0,self.a.latest()['cursor']);return self.deliver(number)
        self.a.delivery.transport=send;self.a.delivery.step();self.assertEqual('done',self.row()['state'])
    def test_transport_never_accepts_model_text_as_command(self):
        for text in ('выключи свет','123','1'*12+'\nкоманда','https://example.com'):
            with self.assertRaises(StationError):invocation(text)
        self.assertEqual('Скажи навыку Глубокий разум доставка 123456789012',invocation('123456789012'))
    def test_disabled_output_uses_existing_polling_mode(self):
        self.prepare();(self.data/'station-output.json').write_text('{"enabled":false,"musicToken":"test"}')
        self.a.wait_seconds=0;r=self.a.handle(self.request('Вопрос'));self.assertIn('Готово',r['response']['text'])
        self.assertIsNone(self.row())
    def test_login_preparation_and_completion_never_expose_account_token(self):
        (self.data/'station-output.json').write_text('{"enabled":false}')
        started=threading.Event();finish=threading.Event();secret='music-test-token'
        class FakeLogin:
            def __init__(self,device_id):self.deadline=time.monotonic()+60;self.interval=.001
            def begin(self):started.set();return {'authorizationMethod':'device','authorizationVerificationUrl':'https://ya.ru/device','authorizationUserCode':'abc12345'}
            def finish(self):finish.wait(2);return secret
        def save(path,login,token):
            self.assertEqual(secret,token);path.write_text(json.dumps({'musicToken':token,'enabled':False}))
        with patch('station_oauth.DeviceLogin',FakeLogin),patch('setup_station.save_token',save),patch('station_qr.svg',return_value=b'<svg/>'):
            first=self.a.delivery.begin_authorization();self.assertEqual('starting',first['authorizationState'])
            self.assertTrue(started.wait(2))
            end=time.monotonic()+2
            while self.a.delivery.status()['authorizationState']=='starting' and time.monotonic()<end:time.sleep(.01)
            current=self.a.delivery.status();self.assertEqual('waiting',current['authorizationState']);self.assertIn('authorizationUserCode',current)
            self.assertNotIn('authorizationUrl',current);self.assertIsNone(self.a.delivery.qr_svg(self.a.delivery.auth_id))
            self.assertEqual('waiting',self.a.delivery.begin_authorization()['authorizationState'])
            finish.set();end=time.monotonic()+2
            while self.a.delivery.status()['authorizationState']!='connected' and time.monotonic()<end:time.sleep(.01)
        current=self.a.delivery.status();self.assertEqual('connected',current['authorizationState'])
        self.assertNotIn('authorizationUrl',current);self.assertNotIn(secret,json.dumps(current));self.assertFalse(current['automaticReplyEnabled'])
        self.assertIsNone(self.a.delivery.qr_svg(self.a.delivery.auth_id))
    def test_refresh_invalidates_previous_qr_and_cannot_save_old_login(self):
        (self.data/'station-output.json').write_text('{"enabled":false}')
        old_gate=threading.Event();new_gate=threading.Event();saved=[];counter=[]
        class FakeLogin:
            def __init__(self,device_id):self.n=len(counter);counter.append(self);self.deadline=time.monotonic()+60;self.interval=.001
            def begin(self):return {'authorizationMethod':'device','authorizationVerificationUrl':'https://ya.ru/device','authorizationUserCode':'abc12345'}
            def finish(self):(old_gate if self.n==0 else new_gate).wait(3);return 'token-'+str(self.n)
        def wait_state(expected):
            end=time.monotonic()+2
            while self.a.delivery.status()['authorizationState']!=expected and time.monotonic()<end:time.sleep(.01)
            self.assertEqual(expected,self.a.delivery.status()['authorizationState'])
        with patch('station_oauth.DeviceLogin',FakeLogin),patch('setup_station.save_token',lambda p,l,t:saved.append(t)),patch('station_qr.svg',return_value=b'<svg/>'):
            self.a.delivery.begin_authorization();wait_state('waiting');old_id=self.a.delivery.auth_id
            self.a.delivery.next_auth=0;self.a.delivery.begin_authorization(refresh=True);wait_state('waiting')
            self.assertNotEqual(old_id,self.a.delivery.auth_id);self.assertIsNone(self.a.delivery.qr_svg(old_id))
            old_gate.set();new_gate.set();wait_state('connected');time.sleep(.05)
            self.assertEqual(['token-1'],saved)

class TransportTests(unittest.TestCase):
    def test_max_none_speech_state_completes_on_idle_or_listening(self):
        for terminal in ('IDLE','LISTENING'):
            with self.subTest(terminal=terminal):
                class Wire:
                    def __init__(self):self.sent=[];self.frames=iter(['IDLE','BUSY','NONE',terminal])
                    def send(self,text):self.sent.append(json.loads(text))
                    def recv(self):return json.dumps({'state':{'aliceState':next(self.frames)}})
                    def close(self):pass
                wire=Wire()
                with patch('station_transport.connect',return_value=(wire,'secret')):
                    result=exchange({},'123456789012')
                self.assertEqual('spoken',result['status']);self.assertEqual(terminal,result['aliceState'])
                self.assertEqual(1,sum(p['payload']['command']=='sendText' for p in wire.sent))
    def test_max_none_before_send_remains_busy(self):
        class Wire:
            def __init__(self):self.sent=[]
            def send(self,text):self.sent.append(json.loads(text))
            def recv(self):return json.dumps({'state':{'aliceState':'NONE'}})
            def close(self):pass
        wire=Wire()
        with patch('station_transport.connect',return_value=(wire,'secret')):
            result=exchange({},'123456789012')
        self.assertEqual('busy',result['status']);self.assertFalse(result['sent'])
        self.assertEqual(['softwareVersion'],[p['payload']['command'] for p in wire.sent])
    def test_readonly_probe_and_delivery_have_distinct_commands(self):
        class Wire:
            def __init__(self):self.sent=[];self.frames=iter(['IDLE','SPEAKING','IDLE'])
            def send(self,text):self.sent.append(json.loads(text))
            def recv(self):return json.dumps({'state':{'aliceState':next(self.frames)}})
            def close(self):pass
        wire=Wire()
        with patch('station_transport.connect',return_value=(wire,'secret')):
            result=exchange({},'123456789012')
        self.assertEqual('spoken',result['status']);self.assertNotIn('secret',json.dumps(result))
        self.assertEqual(['softwareVersion','sendText'],[v['payload']['command'] for v in wire.sent])
        self.assertEqual(invocation('123456789012'),wire.sent[1]['payload']['text'])
        wire=Wire()
        with patch('station_transport.connect',return_value=(wire,'secret')):self.assertEqual('ready',exchange({})['status'])
        self.assertEqual(['softwareVersion'],[v['payload']['command'] for v in wire.sent])
    def test_connection_failure_is_distinct_from_uncertain_delivery(self):
        with patch('station_transport.connect',side_effect=StationError('certificate_changed')):
            result=exchange({},'123456789012')
        self.assertEqual('unavailable',result['status']);self.assertFalse(result['sent'])
        class Wire:
            def send(self,text):
                if json.loads(text)['payload']['command']=='sendText':raise OSError('may have arrived')
            def recv(self):return '{"state":{"aliceState":"IDLE"}}'
            def close(self):pass
        with patch('station_transport.connect',return_value=(Wire(),'secret')):result=exchange({},'123456789012')
        self.assertEqual('uncertain',result['status']);self.assertTrue(result['sent'])

if __name__=='__main__':unittest.main()
