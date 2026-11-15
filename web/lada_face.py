"""Fixed 25x16 monochrome faces; no text, URLs or commands from the model."""
import base64
import json
import struct

FACES={'calm':'😌','curiosity':'🤔','joy':'😊','fear':'😟','sadness':'😢',
       'frustration':'😣','surprise':'😮','relief':'😌','warmth':'🥰','boredom':'😐','sleep':'😴','neutral':'😐'}
ALIASES={'curious':'curiosity','anxious':'fear','low':'sadness','tender':'warmth','affection':'warmth','frustrated':'frustration'}
LABELS=dict(zip(FACES,('Спокойствие','Любопытство','Радость','Страх','Грусть','Фрустрация',
                     'Удивление','Облегчение','Теплота','Скука','Сон','Нейтральное')))

def mood(snapshot,now):
    try:
        if not 0<=now-snapshot['stateAt']<=15:return None
        state=snapshot['state'];c=state.get('cognition') or {}
        if (state.get('circadian') or {}).get('isSleeping') or c.get('sleeping'):return 'sleep'
        name=c.get('dominantEmotion') if c.get('enabled') else (state.get('affect') or {}).get('mood')
        name=ALIASES.get(name,name)
        return name if name in FACES else 'neutral'
    except (KeyError,TypeError):return None

def pixels(name,blink=False):
    if name not in FACES:raise ValueError('face')
    p=[[0]*25 for _ in range(16)]
    def dot(x,y):
        if 0<=x<25 and 0<=y<16:p[y][x]=1
    def line(x,y,x2,y2):
        n=max(abs(x2-x),abs(y2-y)) or 1
        for i in range(n+1):dot(round(x+(x2-x)*i/n),round(y+(y2-y)*i/n))
    for x in (7,17):
        if blink or name in ('calm','relief','sleep','boredom'):line(x-2,6,x+1,6)
        elif name=='warmth':
            for dx,dy in ((-1,0),(1,0),(-2,1),(0,1),(2,1),(-1,2),(1,2),(0,3)):dot(x+dx,4+dy)
        elif name=='joy':line(x-2,6,x,4);line(x,4,x+2,6)
        else:line(x,4,x,7);line(x+1,4,x+1,7)
    if name in ('joy','warmth','relief','calm'):
        line(8,10,10,12);line(10,12,14,12);line(14,12,16,10)
    elif name in ('sadness','fear','frustration'):
        line(8,12,10,10);line(10,10,14,10);line(14,10,16,12)
    elif name in ('surprise','sleep'):
        line(11,10,13,10);line(11,10,11,12);line(13,10,13,12);line(11,12,13,12)
    else:line(9,11,15,11)
    if name=='sadness':line(19,9,19,11)
    if name=='frustration':line(5,2,9,4);line(15,4,19,2)
    if name=='curiosity':line(15,2,19,2)
    if name=='fear':line(5,3,9,1);line(15,1,19,3)
    if name=='sleep':line(20,1,23,1);line(23,1,20,4);line(20,4,23,4)
    return [v for row in p for v in row]

def gif(name):
    # Reset LZW before each literal: tiny deterministic GIF, no dependency or
    # frame optimization. Every image descriptor covers the entire display.
    out=b'GIF89a'+struct.pack('<HHBBB',25,16,0x80,0,0)+bytes((0,0,0,180,180,180))
    out+=b'\x21\xff\x0bNETSCAPE2.0\x03\x01\x00\x00\x00'
    # Max ignores the GIF loop count and returns to its clock after the last
    # frame. Explicitly repeat 5 cycles (13.5 s); refresh while idle every 10 s.
    for blink,delay in ((False,250),(True,20))*5:
        out+=b'\x21\xf9\x04\x04'+struct.pack('<H',delay)+b'\x00\x00'
        out+=b'\x2c'+struct.pack('<HHHHB',0,0,25,16,0)+b'\x02'
        codes=[]
        for pixel in pixels(name,blink):codes.extend((4,pixel))
        codes.append(5);bits=0;count=0;data=bytearray()
        for code in codes:
            bits|=code<<count;count+=3
            while count>=8:data.append(bits&255);bits>>=8;count-=8
        if count:data.append(bits&255)
        for start in range(0,len(data),255):chunk=data[start:start+255];out+=bytes([len(chunk)])+chunk
        out+=b'\x00'
    return out+b'\x3b'

def command(name):
    if name not in FACES:raise ValueError('face')
    url='https://psserv-1.taildea9b4.ts.net/lada-face/v2/'+name+'.gif'
    payload=json.dumps({'animation_sequence':[{'frontal_led_image':url}]},separators=(',',':'))
    def field(number,value):
        raw=value.encode();n=len(raw);length=bytearray()
        while n>=128:length.append((n&127)|128);n>>=7
        length.append(n)
        return bytes([number*8+2])+length+raw
    return {'command':'externalCommandBypass',
            'data':base64.b64encode(field(1,'draw_led_screen')+field(2,payload)).decode()}
