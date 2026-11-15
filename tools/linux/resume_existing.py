#!/usr/bin/env python3
"""Resume the existing DeepBrain v1.4 epoch; never create fresh memory or weights."""
from pathlib import Path
import datetime
import fcntl
import hashlib
import json
import os
import shlex
import signal
import socket
import struct
import subprocess
import tarfile
import time
import urllib.request
import uuid

BASE = Path(os.environ.get('DEEPBRAIN_RUNTIME_DIR', str(Path.home()/'deepbrain-runtime')))
CONFIG = BASE / 'brainconfig-production.json'
EPOCH = Path(os.environ['DEEPBRAIN_EPOCH'])
API = 'http://127.0.0.1:8787/api/v1/'
SESSIONS = ('mlhost-v14', 'deepbrain-v14')


def read_json(path):
    return json.loads(Path(path).read_text(encoding='utf-8-sig'))


def http_json(url):
    with urllib.request.urlopen(url, timeout=5) as response:
        return json.load(response)


def rpc(method, params):
    data = json.dumps({'v': 1, 'id': uuid.uuid4().hex, 'method': method, 'params': params}).encode()
    with socket.create_connection(('127.0.0.1', 7777), timeout=5) as connection:
        connection.settimeout(5)
        connection.sendall(struct.pack('<I', len(data)) + data)
        def receive(size):
            value = b''
            while len(value) < size:
                chunk = connection.recv(size - len(value))
                if not chunk:
                    raise RuntimeError('ML closed its RPC connection')
                value += chunk
            return value
        size = struct.unpack('<I', receive(4))[0]
        if not 0 < size <= 4 * 1024 * 1024:
            raise RuntimeError('Invalid ML RPC response length')
        response = json.loads(receive(size))
    if not response.get('ok'):
        raise RuntimeError(f'ML RPC {method} failed: {response.get("error")}')
    return response['result']


def has_session(name):
    return subprocess.run(['tmux', 'has-session', '-t', '='+name], stdout=subprocess.DEVNULL,
                          stderr=subprocess.DEVNULL).returncode == 0


def launch(session, cwd, argv, log):
    command = 'exec ' + shlex.join([str(x) for x in argv]) + ' >> ' + shlex.quote(str(log)) + ' 2>&1'
    subprocess.run(['tmux', 'new-session', '-d', '-s', session, '-c', str(cwd), command], check=True)


def ensure_web():
    if not has_session('deepbrain-web'):
        subprocess.run(['python3', os.environ.get('DEEPBRAIN_WEB_START', str(Path.home()/'deepbrain-web-01/start.py'))], check=True)


def wait_for(check, seconds=30):
    deadline = time.monotonic() + seconds
    last_error = None
    while time.monotonic() < deadline:
        try:
            return check()
        except (OSError, ValueError, RuntimeError) as error:
            last_error = error
            time.sleep(0.5)
    raise RuntimeError(f'Readiness check failed: {last_error}')


def main():
    os.umask(0o077)
    with (BASE/'start.lock').open('a') as lock:
        fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        existing = [name for name in SESSIONS if has_session(name)]
        if existing:
            if len(existing) == 2:
                status = http_json(API+'system/status')
                state = http_json(API+'state')
                assert status['api']['listening'] and state['ml']['remoteConnected']
                ensure_web()
                print(json.dumps({'alreadyRunning': True, 'tick': state['tick'], 'ml': state['ml']}, ensure_ascii=False))
                return
            raise RuntimeError(f'Partial existing run: {existing}; inspect before starting anything')
        for port in (5001, 7777, 5555, 8787):
            with socket.socket() as probe:
                probe.settimeout(1)
                if probe.connect_ex(('127.0.0.1', port)) == 0:
                    raise RuntimeError(f'Port {port} already in use')

        config = read_json(CONFIG)
        assert config['memory']['path'] == str(EPOCH/'memory/episodes.jsonl')
        assert config['habits']['path'] == str(EPOCH/'memory/habits.json')
        assert config['llm']['journalPath'] == str(EPOCH/'memory/inner-voice.jsonl')
        assert config['ml']['checkpointPath'] == str(EPOCH/'checkpoints/brain_ml.chk')
        checkpoint = read_json(config['ml']['checkpointPath'])
        remote = read_json(checkpoint['WeightsPath'])
        assert checkpoint['TrainSteps'] == remote['trainSteps'] and checkpoint['TrainSteps'] > 0
        weights = Path(remote['weightsPath'])
        assert weights.is_relative_to(EPOCH/'checkpoints')
        model = read_json(weights)
        assert len(model['Layers']) > 0
        habits = read_json(config['habits']['path'])
        assert habits['schemaVersion'] == 3 and len(habits['habits']) == 5
        assert habits['clockTick'] >= max(max(h['lastCueTick'], h['lastExecutionTick']) for h in habits['habits'])
        for name in ('episodes.jsonl', 'inner-voice.jsonl'):
            records = [json.loads(line) for line in (EPOCH/'memory'/name).read_text().splitlines() if line.strip()]
            assert records, f'Empty existing {name}'
        assert read_json(EPOCH/'memory/experience.json')
        models = http_json(config['llm']['baseUrl'].rstrip('/')+'/api/tags')['models']
        assert any(x.get('name') == config['llm']['model'] for x in models), 'Required Ollama model is absent'

        run = BASE/'runs'/datetime.datetime.now().strftime('%Y%m%d-%H%M%S')
        run.mkdir(parents=True, mode=0o700)
        manifest = []
        for directory in (EPOCH/'memory', EPOCH/'checkpoints'):
            for file in directory.rglob('*'):
                if file.is_file():
                    manifest.append({'path':str(file.relative_to(EPOCH)), 'sha256':hashlib.sha256(file.read_bytes()).hexdigest()})
        with tarfile.open(run/'before-start.tar.gz', 'w:gz') as archive:
            archive.add(EPOCH/'memory', arcname='memory')
            archive.add(EPOCH/'checkpoints', arcname='checkpoints')
            archive.add(CONFIG, arcname='brainconfig-production.json')
        with tarfile.open(run/'before-start.tar.gz', 'r:gz') as archive:
            for row in manifest:
                assert hashlib.sha256(archive.extractfile(row['path']).read()).hexdigest() == row['sha256']
                assert hashlib.sha256((EPOCH/row['path']).read_bytes()).hexdigest() == row['sha256']
        (run/'before-start-manifest.json').write_text(json.dumps(manifest, indent=2)+'\n')
        print('VERIFIED_BACKUP='+str(run/'before-start.tar.gz'), flush=True)
        launch('mlhost-v14', BASE/'v1.4.2-replay-r2/ml', [BASE/'v1.4.2-replay-r2/ml/ML.Host', '5001', '7777'], run/'ml.log')
        wait_for(lambda: rpc('ml.ping', {}))
        restored = rpc('ml.checkpoint.load', {'path':checkpoint['WeightsPath']})
        if not restored.get('ok'):
            raise RuntimeError(f'Existing model could not be loaded: {restored}')
        if json.loads(restored['meta'])['trainSteps'] != checkpoint['TrainSteps']:
            raise RuntimeError('ML restored a different training counter')
        print('RESTORED_TRAIN_STEPS='+str(checkpoint['TrainSteps']), flush=True)
        launch('deepbrain-v14', EPOCH, [BASE/'v1.12.0-semantic/deepbrain/DeepBrain.Host', '--config', CONFIG, '--console', 'logs'], run/'deepbrain.log')

        def ready():
            state = http_json(API+'state')
            ml = state.get('ml') or {}
            if not ml.get('remoteConnected') or ml.get('trainSteps', 0) < checkpoint['TrainSteps']:
                raise RuntimeError('DeepBrain has not yet restored the connected ML model')
            if ml.get('lastRemoteError'):
                raise RuntimeError(ml['lastRemoteError'])
            return state
        state = wait_for(ready)
        status = http_json(API+'system/status')
        assert status['llm']['journal']['path'] == str(EPOCH/'memory/inner-voice.jsonl')
        (run/'initial-status.json').write_text(json.dumps(status, ensure_ascii=False, indent=2)+'\n')
        (run/'initial-state.json').write_text(json.dumps(state, ensure_ascii=False, indent=2)+'\n')
        (BASE/'current-run').write_text(str(run)+'\n')
        print(json.dumps({'started':True,'run':str(run),'epoch':str(EPOCH),'tick':state['tick'],'ml':state['ml'],'memory':status['memory'],'habits':status['habits']},ensure_ascii=False),flush=True)
        ensure_web()


if __name__ == '__main__':
    main()
