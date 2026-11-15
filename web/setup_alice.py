"""Configure the private skill. Prints a secret webhook URL only on the server."""
import argparse,json,os,re,secrets
from pathlib import Path
from urllib.parse import urlsplit

def main():
    p=argparse.ArgumentParser();p.add_argument('--data',default='/home/ivan/deepbrain-web-data')
    p.add_argument('--skill-id',required=True);p.add_argument('--public-url',required=True);a=p.parse_args()
    if not re.fullmatch(r'[a-f0-9]{8}(-[a-f0-9]{4}){3}-[a-f0-9]{12}',a.skill_id):p.error('Invalid skill ID')
    u=urlsplit(a.public_url)
    if u.scheme!='https' or not u.hostname or u.username or u.password or u.query or u.fragment or u.path not in ('','/'):
        p.error('Expected HTTPS origin without path')
    os.umask(0o077);data=Path(a.data);path=data/'alice.json'
    cfg=json.loads(path.read_text()) if path.exists() else {'secret':secrets.token_urlsafe(32)}
    if cfg.get('skillId') and cfg['skillId']!=a.skill_id:p.error('Skill ID already configured; explicit re-pairing required')
    cfg.update(enabled=True,skillId=a.skill_id,publicUrl=a.public_url.rstrip('/'))
    temporary=path.with_suffix('.new');temporary.write_text(json.dumps(cfg)+'\n');os.replace(temporary,path)
    print('Webhook URL (paste into your PRIVATE Yandex skill):')
    print(cfg['publicUrl']+'/alice/'+cfg['secret'])
    print('Then get a connection code in DeepBrain Web > Диалог and tell it to the skill.')

if __name__=='__main__':main()
