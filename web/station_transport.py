"""Small, isolated client for the community-documented Station local protocol.

Only sends a fixed skill invocation with a one-use delivery number. Model text
can never become a Station command. Credentials and raw Station frames are not
written to stdout or logs. See AlexxIT/YandexStation core/yandex_glagol.py.
"""
import argparse,hashlib,hmac,ipaddress,json,re,ssl,sys,time,urllib.parse,urllib.request,uuid
from pathlib import Path

class StationError(Exception):pass

def config(data):
    value=json.loads((Path(data)/'station-output.json').read_text())
    ip=ipaddress.ip_address(value['host'])
    if not ip.is_private or ip.is_loopback or ip.is_link_local or ip.is_multicast:raise StationError('address')
    if value['port']!=1961 or not re.fullmatch('[A-Z0-9]{6,40}',value['deviceId']):raise StationError('device')
    if value['platform']!='yandexstation_2':raise StationError('platform')
    if not re.fullmatch('[a-f0-9]{64}',value['certificateSha256']):raise StationError('certificate')
    if value['skillName'] not in ('Глубокий разум','Лада'):raise StationError('skill')
    if not isinstance(value.get('musicToken'),str) or not 10<len(value['musicToken'])<4096:raise StationError('authorization')
    return value

def invocation(number,skill='Глубокий разум'):
    if not re.fullmatch('[0-9]{12}',number):raise StationError('delivery_number')
    if skill not in ('Глубокий разум','Лада'):raise StationError('skill')
    return 'Скажи навыку '+skill+' доставка '+number

def connect(cfg):
    # A pure-Python dependency installed in a separate, versioned directory;
    # the Web server and its Python installation remain unchanged.
    deps=Path('/home/ivan/deepbrain-runtime/station-output-deps')
    if deps.is_dir():sys.path.insert(0,str(deps))
    import websocket
    query=urllib.parse.urlencode({'device_id':cfg['deviceId'],'platform':cfg['platform']})
    req=urllib.request.Request('https://quasar.yandex.net/glagol/token?'+query,headers={'Authorization':'OAuth '+cfg['musicToken']})
    with urllib.request.urlopen(req,timeout=8) as r:value=json.loads(r.read(32768))
    if value.get('status')!='ok' or not isinstance(value.get('token'),str):raise StationError('authorization')
    ws=websocket.create_connection('wss://'+cfg['host']+':1961',timeout=3,
        sslopt={'cert_reqs':ssl.CERT_NONE,'check_hostname':False},http_no_proxy=[cfg['host']])
    fingerprint=hashlib.sha256(ws.sock.getpeercert(binary_form=True)).hexdigest()
    if not hmac.compare_digest(fingerprint,cfg['certificateSha256']):
        ws.close();raise StationError('certificate_changed')
    return ws,value['token']

def packet(token,payload):
    return {'conversationToken':token,'id':str(uuid.uuid4()),'payload':payload,'sentTime':round(time.time()*1000)}

def exchange(cfg,number=None):
    sent=False;started=False;ack=False;last_state=None;ws=None
    try:
        ws,token=connect(cfg)
        ws.send(json.dumps(packet(token,{'command':'softwareVersion'})))
        deadline=time.monotonic()+6
        while time.monotonic()<deadline:
            frame=json.loads(ws.recv());last_state=(frame.get('state') or {}).get('aliceState')
            if last_state:break
        if not last_state:raise StationError('no_state')
        if number is None:return {'status':'ready','aliceState':last_state}
        if last_state!='IDLE':return {'status':'busy','aliceState':last_state,'sent':False}
        msg=packet(token,{'command':'sendText','text':invocation(number,cfg.get('skillName','Глубокий разум'))})
        sent=True # Even a failed send may have reached the device; never replay.
        ws.send(json.dumps(msg,ensure_ascii=False))
        deadline=time.monotonic()+100;sent_at=time.monotonic();next_ping=sent_at+2
        while time.monotonic()<deadline:
            if time.monotonic()>=next_ping:
                ws.send(json.dumps(packet(token,{'command':'ping'})));next_ping=time.monotonic()+2
            try:frame=json.loads(ws.recv())
            except TimeoutError:continue
            except Exception as ex:
                if type(ex).__name__=='WebSocketTimeoutException':continue
                raise
            if frame.get('requestId')==msg['id']:
                ack=frame.get('status')=='SUCCESS'
            state=(frame.get('state') or {}).get('aliceState')
            if state:
                last_state=state
                # Max firmware also reports NONE while speaking (observed on
                # this device; AlexxIT/YandexStation issue #740). It is NOT idle
                # for the pre-send check above; only recognize it after send.
                if state in ('SPEAKING','NONE'):started=True
                elif started and state in ('IDLE','LISTENING'):
                    return {'status':'spoken','sent':True,'acknowledged':ack,'aliceState':state}
            if not started and time.monotonic()-sent_at>15:break
        return {'status':'uncertain','sent':True,'speechStarted':started,'acknowledged':ack,'aliceState':last_state}
    except Exception as ex:
        return {'status':'uncertain' if sent else 'unavailable','sent':sent,
            'error':str(ex) if isinstance(ex,StationError) else type(ex).__name__}
    finally:
        if ws:
            try:ws.close()
            except Exception:pass

def display(cfg,name):
    from lada_face import command
    ws=None
    try:
        payload=command(name) # closed allow-list; never arbitrary URLs
        ws,token=connect(cfg);ws.send(json.dumps(packet(token,{'command':'softwareVersion'})))
        end=time.monotonic()+4;state=None
        while time.monotonic()<end:
            frame=json.loads(ws.recv());state=frame.get('state')
            if state and state.get('aliceState'):break
        if not state or state.get('aliceState')!='IDLE' or state.get('playing'):
            return {'status':'busy','sent':False}
        msg=packet(token,payload);ws.send(json.dumps(msg));end=time.monotonic()+4
        while time.monotonic()<end:
            frame=json.loads(ws.recv())
            if frame.get('requestId')==msg['id']:
                return {'status':'displayed' if frame.get('status')=='SUCCESS' else 'rejected','sent':True}
        return {'status':'uncertain','sent':True}
    except Exception:return {'status':'unavailable'}
    finally:
        if ws:ws.close()

def main():
    p=argparse.ArgumentParser();p.add_argument('--data',required=True);p.add_argument('--deliver');p.add_argument('--face')
    args=p.parse_args()
    try:
        cfg=config(args.data)
        if args.deliver and not cfg.get('enabled'):raise StationError('disabled')
        if args.face:
            if not cfg.get('moodDisplayEnabled'):raise StationError('disabled')
            result=display(cfg,args.face)
        else:result=exchange(cfg,args.deliver)
    except Exception as ex:result={'status':'unavailable','sent':False,'error':str(ex) if isinstance(ex,StationError) else type(ex).__name__}
    print(json.dumps(result),flush=True)

if __name__=='__main__':main()
