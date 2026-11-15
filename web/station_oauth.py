"""Yandex's documented device-code OAuth flow, without Passport QR sessions.

https://yandex.ru/dev/id/doc/ru/codes/screen-code-oauth
The public music client is also used by YandexStation for Glagol authorization.
User confirmation happens only on Yandex; private device/token values stay here.
"""
import json,os,re,time,urllib.error,urllib.parse,urllib.request



class AuthorizationError(Exception):
    def __init__(self,code):
        self.code=code if code in ('access_denied','invalid_grant','invalid_client','unauthorized_client','invalid_scope','network','invalid_response') else 'authorization_failed'
        super().__init__(self.code)

class DeviceLogin:
    def __init__(self,device_id,transport=None,client_id=None,client_secret=None):
        self.device_id=device_id;self.transport=transport or self.call
        self.client_id=client_id if client_id is not None else os.environ.get("DEEPBRAIN_YANDEX_CLIENT_ID", "")
        self.client_secret=client_secret if client_secret is not None else os.environ.get("DEEPBRAIN_YANDEX_CLIENT_SECRET", "")
        self.interval=5;self.deadline=0;self.device_code=None
    @staticmethod
    def call(path,fields):
        request=urllib.request.Request('https://oauth.yandex.ru'+path,
            data=urllib.parse.urlencode(fields).encode(),headers={'Content-Type':'application/x-www-form-urlencoded'})
        try:
            with urllib.request.urlopen(request,timeout=12) as r:return json.loads(r.read(65536))
        except urllib.error.HTTPError as ex:
            if ex.code>=500:raise AuthorizationError('network')
            try:return json.loads(ex.read(65536))
            except (ValueError,UnicodeError):raise AuthorizationError('invalid_response')
        except (OSError,ValueError):raise AuthorizationError('network')
    def begin(self):
        if not self.client_id or not self.client_secret:raise AuthorizationError("invalid_client")
        value=self.transport('/device/code',{'client_id':self.client_id,'device_id':self.device_id,'device_name':'DeepBrain Ninkear'})
        if value.get('error'):raise AuthorizationError(value['error'])
        url=urllib.parse.urlsplit(value.get('verification_url',''))
        if url.scheme!='https' or url.hostname not in ('ya.ru','oauth.yandex.ru','oauth.yandex.com') or url.path!='/device' or url.query or url.fragment:
            raise AuthorizationError('invalid_response')
        code=value.get('user_code','');private=value.get('device_code','')
        if not isinstance(code,str) or not re.fullmatch(r'[A-Za-z0-9-]{4,20}',code):raise AuthorizationError('invalid_response')
        if not isinstance(private,str) or not 10<=len(private)<=4096:raise AuthorizationError('invalid_response')
        duration=value.get('expires_in');interval=value.get('interval',5)
        if type(duration) is not int or not 1<=duration<=3600 or type(interval) is not int or not 1<=interval<=60:raise AuthorizationError('invalid_response')
        self.interval=max(5,interval);self.deadline=time.monotonic()+duration;self.device_code=private
        return {'authorizationMethod':'device','authorizationVerificationUrl':value['verification_url'],'authorizationUserCode':code,'expiresIn':duration}
    def finish(self):
        if time.monotonic()>=self.deadline:raise AuthorizationError('invalid_grant')
        value=self.transport('/token',{'grant_type':'device_code','code':self.device_code,'client_id':self.client_id,'client_secret':self.client_secret})
        error=value.get('error')
        if error=='authorization_pending':return None
        if error=='slow_down':self.interval=min(60,self.interval+5);return None
        if error:raise AuthorizationError(error)
        token=value.get('access_token')
        if not isinstance(token,str) or not 10<len(token)<4096 or re.search(r'\s',token):raise AuthorizationError('invalid_response')
        return token
