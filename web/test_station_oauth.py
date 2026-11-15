import time,unittest
from station_oauth import DeviceLogin,AuthorizationError

class DeviceOAuthTests(unittest.TestCase):
    def setUp(self):
        self.calls=[];self.responses=[]
        self.start={'user_code':'test1234','device_code':'private-device-code','verification_url':'https://ya.ru/device','expires_in':300,'interval':5}
        def call(path,fields):self.calls.append((path,fields));return self.start.copy() if path=='/device/code' else self.responses.pop(0)
        self.login=DeviceLogin('stable-device-id',call,client_id='test-client',client_secret='test-secret')
    def test_request_and_pending_do_not_expose_private_code(self):
        public=self.login.begin();self.assertEqual('test1234',public['authorizationUserCode'])
        self.assertNotIn('private-device-code',str(public));self.assertEqual('stable-device-id',self.calls[0][1]['device_id'])
        self.responses=[{'error':'authorization_pending'},{'access_token':'test-token-long-enough'}]
        self.assertIsNone(self.login.finish());self.assertEqual('test-token-long-enough',self.login.finish())
        self.assertEqual('device_code',self.calls[1][1]['grant_type']);self.assertEqual('private-device-code',self.calls[1][1]['code'])
    def test_missing_credentials_fail_before_network_request(self):
        login=DeviceLogin('device',lambda *args:self.fail('unexpected network call'),client_id='',client_secret='')
        with self.assertRaisesRegex(AuthorizationError,'invalid_client'):login.begin()
    def test_configured_secret_is_only_sent_to_token_endpoint(self):
        self.login.begin();self.responses=[{'error':'authorization_pending'}];self.login.finish()
        self.assertNotIn('client_secret',self.calls[0][1])
        self.assertEqual('test-secret',self.calls[1][1]['client_secret'])
    def test_expiry_and_denial_stop_polling(self):
        self.login.begin();self.responses=[{'error':'access_denied'}]
        with self.assertRaisesRegex(AuthorizationError,'access_denied'):self.login.finish()
        self.login.deadline=time.monotonic()-1
        with self.assertRaisesRegex(AuthorizationError,'invalid_grant'):self.login.finish()
        self.assertEqual(2,len(self.calls))
    def test_provider_rate_limit_increases_wait(self):
        self.login.begin();self.responses=[{'error':'slow_down'}]
        self.assertIsNone(self.login.finish());self.assertEqual(10,self.login.interval)
    def test_untrusted_verification_url_or_malformed_codes_rejected(self):
        for key,value in [('verification_url','https://evil.invalid/device'),('verification_url','http://ya.ru/device'),('user_code','<script>'),('expires_in',0),('interval',-1)]:
            with self.subTest(key=key,value=value):
                original=self.start[key];self.start[key]=value
                with self.assertRaises(AuthorizationError):self.login.begin()
                self.start[key]=original
    def test_invalid_client_and_missing_token_fail_closed(self):
        self.start={'error':'invalid_client'}
        with self.assertRaisesRegex(AuthorizationError,'invalid_client'):self.login.begin()
        self.login.deadline=time.monotonic()+10;self.responses=[{'access_token':'bad\ntoken\ninjection'}]
        with self.assertRaises(AuthorizationError):self.login.finish()

if __name__=='__main__':unittest.main()
