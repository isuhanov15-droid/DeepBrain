"""One-time user-confirmed Yandex QR login; retain only a music/Glagol token.

Protocol reference: AlexxIT/YandexStation core/yandex_session.py.
No browser profile, password, existing cookies, or DeepBrain data is read.
"""
import argparse,hashlib,http.cookiejar,json,os,re,socket,ssl,time
import urllib.error,urllib.parse,urllib.request
from pathlib import Path

class Login:
    def __init__(self):
        self.cookies=http.cookiejar.CookieJar()
        self.http=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(self.cookies))
        self.headers={}
    def call(self,url,form=None,value=None,headers=None,raw=False):
        data=None;h={'User-Agent':'DeepBrain-Station-Setup/0.13'}
        if form is not None:data=urllib.parse.urlencode(form).encode()
        if value is not None:data=json.dumps(value).encode();h['Content-Type']='application/json'
        h.update(headers or {})
        with self.http.open(urllib.request.Request(url,data=data,headers=h),timeout=15) as r:
            text=r.read(1024*1024).decode()
        return text if raw else json.loads(text)
    def begin(self):
        root='https://passport.yandex.ru/pwl-yandex'
        page=self.call(root,raw=True)
        csrf=re.search(r'__CSRF__\s*=\s*"([^"]+)"',page)
        if not csrf:raise RuntimeError('Yandex changed the login page')
        self.headers={'X-CSRF-Token':csrf[1]}
        self.track=self.call(root+'/api/passport/auth/password/submit',value={'retpath':'https://passport.yandex.ru/'},headers=self.headers)
        magic=self.call(root+'/api/passport/auth/magic/code',form={'location_id':'0','magic_track_id':self.track['track_id'],'track_id':''},headers=self.headers)
        link=magic['link'];url=urllib.parse.urlsplit(link)
        if url.scheme!='https' or url.hostname!='passport.yandex.ru':raise RuntimeError('Unexpected login destination')
        return link
    def finish(self):
        root='https://passport.yandex.ru/pwl-yandex/api/passport/'
        state=self.call(root+'auth/magic/code/status',value=self.track,headers=self.headers)
        if state.get('state')!='otp_auth_finished':return None
        self.call(root+'sessions/get_session',form={'track_id':state['trackId']},headers=self.headers)
        cookie='; '.join(f'{c.name}={c.value}' for c in self.cookies if c.domain.lstrip('.')=='yandex.ru' or c.domain.endswith('.yandex.ru'))
        # Client credentials are configured locally. Neither
        # these temporary session cookies nor the broad x-token are persisted.
        session=self.call('https://mobileproxy.passport.yandex.net/1/bundle/oauth/token_by_sessionid',
            form={'client_id':os.environ['DEEPBRAIN_YANDEX_PASSPORT_CLIENT_ID'],'client_secret':os.environ['DEEPBRAIN_YANDEX_PASSPORT_CLIENT_SECRET']},
            headers={'Ya-Client-Host':'passport.yandex.ru','Ya-Client-Cookie':cookie})
        music=self.call('https://oauth.mobile.yandex.net/1/token',form={
            'client_id':os.environ['DEEPBRAIN_YANDEX_CLIENT_ID'],'client_secret':os.environ['DEEPBRAIN_YANDEX_CLIENT_SECRET'],
            'grant_type':'x-token','access_token':session['access_token']})
        token=music['access_token']
        if not isinstance(token,str) or not 10<len(token)<4096:raise RuntimeError('Invalid access token')
        return token

def main():
    os.umask(0o077)
    parser=argparse.ArgumentParser();parser.add_argument('--data',default='/home/ivan/deepbrain-web-data')
    args=parser.parse_args();path=Path(args.data)/'station-output.json'
    cfg=json.loads(path.read_text())
    if cfg.get('musicToken'):raise RuntimeError('Station credentials already exist; nothing changed')
    login=Login();print('YANDEX_LOGIN_URL='+login.begin(),flush=True)
    deadline=time.monotonic()+600
    while time.monotonic()<deadline:
        token=login.finish()
        if token:
            save_token(path,login,token)
            print('YANDEX_CONNECTED: selected Station authorized; automatic speech remains disabled until verification.',flush=True)
            return
        time.sleep(3)
    raise RuntimeError('Sign-in expired; no credentials saved')

def save_token(path,login,token):
    cfg=json.loads(path.read_text())
    query=urllib.parse.urlencode({'device_id':cfg['deviceId'],'platform':cfg['platform']})
    result=login.call('https://quasar.yandex.net/glagol/token?'+query,headers={'Authorization':'OAuth '+token})
    if result.get('status')!='ok' or not result.get('token'):raise RuntimeError('Account cannot control this Station')
    cfg['musicToken']=token;cfg['enabled']=False
    tmp=path.with_suffix('.new');tmp.write_text(json.dumps(cfg,ensure_ascii=False,indent=2)+'\n');os.chmod(tmp,0o600);os.replace(tmp,path)

if __name__=='__main__':
    try:main()
    except urllib.error.HTTPError as ex:raise SystemExit('Yandex login HTTP '+str(ex.code)+'; no token printed.')
    except Exception as ex:raise SystemExit('Station setup stopped: '+type(ex).__name__+'. No secrets printed; DeepBrain unchanged.')
