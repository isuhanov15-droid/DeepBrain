import base64
import http.client
import json
from pathlib import Path
import tempfile
import threading
import unittest
import uuid
import time
from http.server import ThreadingHTTPServer
from openai_dialogue import Dialogue
from server import Studio,Handler
from test_dialogue import response


class WebDialogueTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name)
        self.calls=0
        self.studio=Studio(self.root,'test-epoch','http://127.0.0.1:1','test-password-only')
        self.key=self.root/'test-key';self.key.write_text('sk-'+('test-only-'*5));self.key.chmod(0o600)
        self.studio.dialogue=Dialogue(self.root,self.studio.dialogue_snapshot,self.key,self.fake)
        self.http=ThreadingHTTPServer(('127.0.0.1',0),Handler);self.http.studio=self.studio
        self.thread=threading.Thread(target=self.http.serve_forever,daemon=True);self.thread.start()
        self.auth='Basic '+base64.b64encode(b'ivan:test-password-only').decode()
    def fake(self,payload,key):self.calls+=1;return response('<script>alert(1)</script>')
    def tearDown(self):
        self.http.shutdown();self.http.server_close();self.thread.join();self.temp.cleanup()
    def request(self,path,method='GET',value=None,auth=True,csrf=True,content_type='application/json'):
        headers={'Content-Type':content_type}
        if auth:headers['Authorization']=self.auth
        if csrf:headers['X-CSRF-Token']=self.studio.csrf
        body=json.dumps(value).encode() if value is not None else b''
        c=http.client.HTTPConnection('127.0.0.1',self.http.server_port,timeout=5)
        try:
            # Send the small test request together. On Windows, separate header/body
            # writes can race an intentional early rejection and reset the socket.
            headers.update({'Host':'127.0.0.1','Content-Length':str(len(body)),'Connection':'close'})
            wire=(method+' '+path+' HTTP/1.1\r\n'+''.join(k+': '+v+'\r\n' for k,v in headers.items())+'\r\n').encode()+body
            c.connect();c.sock.sendall(wire)
            with http.client.HTTPResponse(c.sock) as r:
                r.begin();return r.status,r.read(),dict(r.getheaders())
        finally:c.close()
    def payload(self):return {'requestId':uuid.uuid4().hex,'conversationId':self.studio.dialogue.view()['conversationId'],'message':'Привет'}
    def test_auth_and_csrf_required_before_paid_call(self):
        self.assertEqual(401,self.request('/web/dialogue/message','POST',self.payload(),auth=False)[0])
        self.assertEqual(403,self.request('/web/dialogue/message','POST',self.payload(),csrf=False)[0])
        self.assertEqual(401,self.request('/web/dialogue',auth=False)[0]);self.assertEqual(0,self.calls)
    def test_message_and_history_routes_use_adapter(self):
        status,body,headers=self.request('/web/dialogue/message','POST',self.payload())
        self.assertEqual(200,status);self.assertEqual('completed',json.loads(body)['status']);self.assertEqual(1,self.calls)
        status,body,_=self.request('/web/dialogue');self.assertEqual(1,len(json.loads(body)['history']))
        self.assertIn("script-src 'self'",headers['Content-Security-Policy'])
    def test_invalid_json_and_body_size_are_rejected_without_calls(self):
        self.assertEqual(400,self.request('/web/dialogue/message','POST',[],content_type='application/json')[0])
        self.assertEqual(400,self.request('/web/dialogue/message','POST',self.payload(),content_type='text/plain')[0])
        self.assertEqual(413,self.request('/web/dialogue/message','POST',{'large':'a'*21000})[0]);self.assertEqual(0,self.calls)
    def test_key_cannot_be_set_from_browser(self):
        value=self.payload();value['apiKey']='sk-not-accepted'
        self.assertEqual(400,self.request('/web/dialogue/message','POST',value)[0]);self.assertEqual(0,self.calls)
    def test_automatic_voice_requires_auth_csrf_and_prior_station_login(self):
        route='/web/alice/automatic'
        self.assertEqual(401,self.request(route,'POST',{'enabled':True},auth=False)[0])
        self.assertEqual(403,self.request(route,'POST',{'enabled':True},csrf=False)[0])
        self.assertEqual(400,self.request(route,'POST',{'enabled':True})[0])
        cfg=self.root/'station-output.json';cfg.write_text('{"enabled":false,"musicToken":"local-test-secret"}')
        code,body,_=self.request(route,'POST',{'enabled':True})
        self.assertEqual(200,code);self.assertTrue(json.loads(body)['automaticReplyEnabled']);self.assertNotIn(b'local-test-secret',body)
        self.assertEqual(400,self.request(route,'POST',{'enabled':'yes'})[0])
        self.assertEqual(400,self.request(route,'POST',{'enabled':True,'musicToken':'malicious'})[0])
        self.assertEqual(200,self.request(route,'POST',{'enabled':False})[0])
        self.assertFalse(json.loads(cfg.read_text())['enabled']);self.assertEqual(0,self.calls)
    def test_station_login_requires_auth_csrf_and_prepared_station(self):
        route='/web/alice/authorize'
        self.assertEqual(401,self.request(route,'POST',auth=False)[0])
        self.assertEqual(403,self.request(route,'POST',csrf=False)[0])
        self.assertEqual(409,self.request(route,'POST')[0])
        self.assertEqual(0,self.calls)
        self.assertEqual(403,self.request(route+'/refresh','POST',csrf=False)[0])
    def test_qr_is_local_authenticated_uncached_and_expires(self):
        from station_qr import svg
        delivery=self.studio.alice.delivery;identifier='f'*32
        delivery.authorization={'authorizationState':'waiting'};delivery.auth_id=identifier;delivery.auth_expires=time.monotonic()+60
        delivery.auth_svg=svg('https://passport.yandex.ru/am/push/qrsecure?track_id=TEST&magic=TEST')
        route='/web/alice/qr.svg?id='+identifier
        self.assertEqual(401,self.request(route,auth=False)[0]);self.assertEqual(404,self.request(route+'0')[0])
        code,body,headers=self.request(route)
        self.assertEqual(200,code);self.assertIn(b'<svg',body);self.assertNotIn(b'<script',body)
        self.assertEqual('no-store',headers['Cache-Control']);self.assertEqual('image/svg+xml',headers['Content-Type'])
        delivery.auth_expires=time.monotonic()-1
        self.assertEqual(404,self.request(route)[0]);self.assertEqual('expired',delivery.status()['authorizationState'])
    def test_alice_pairing_requires_password_and_csrf(self):
        self.assertEqual(401,self.request('/web/alice',auth=False)[0])
        self.assertEqual(401,self.request('/web/alice/pair','POST',auth=False)[0])
        self.assertEqual(403,self.request('/web/alice/pair','POST',csrf=False)[0])
        (self.root/'alice.json').write_text(json.dumps({'enabled':True,'secret':'s'*43,'skillId':'11111111-1111-1111-1111-111111111111'}))
        status,body,_=self.request('/web/alice/pair','POST');self.assertEqual(200,status)
        self.assertEqual(6,len(json.loads(body)['code']))
    def test_new_conversation_preserves_old_request_and_never_calls_provider(self):
        self.request('/web/dialogue/message','POST',self.payload())
        status,body,_=self.request('/web/dialogue/new','POST')
        self.assertEqual(200,status);self.assertEqual([],json.loads(body)['history']);self.assertEqual(1,self.calls)
    def test_page_contains_dialogue_and_escapes_model_text_in_javascript(self):
        status,body,_=self.request('/');self.assertEqual(200,status)
        self.assertIn('Диалог с Ладой',body.decode());self.assertIn('Web Studio 0.17',body.decode())
        _,body,_=self.request('/app.js');self.assertIn("esc(r.status==='completed'?r.answer",body.decode())


if __name__=='__main__':unittest.main(verbosity=2)
