"""Independent, bounded cosmetic updates; never interrupts speech or music."""
import json
import subprocess
import sys
import threading
import time
from pathlib import Path
from lada_face import mood,FACES,LABELS

class StationFace:
    def __init__(self,bridge,transport=None):
        self.bridge=bridge;self.transport=transport or self.run
        self.last=None;self.updated_at=0;self.result='waiting';self.fetched=None;self.fetched_at=0
        self.next_attempt=0
    def status(self):
        name=mood(self.bridge.dialogue.snapshot_provider(),self.bridge.clock())
        return {'mood':name,'emoji':FACES.get(name,'—'),'label':LABELS.get(name,'Нет свежих данных'),
                'lastDisplayed':self.last,'lastSentAt':self.updated_at,'status':self.result,
                'lastFetched':self.fetched,'lastFetchedAt':self.fetched_at,'enabled':self.enabled()}
    def enabled(self):
        try:return json.loads((self.bridge.data/'station-output.json').read_text()).get('moodDisplayEnabled') is True
        except (OSError,ValueError,AttributeError):return False
    def run(self,name):
        p=subprocess.run([sys.executable,str(Path(__file__).with_name('station_transport.py')),
            '--data',str(self.bridge.data),'--face',name],capture_output=True,text=True,timeout=18)
        return json.loads(p.stdout) if p.returncode==0 else {'status':'unavailable'}
    def step(self):
        if not self.enabled():return
        name=mood(self.bridge.dialogue.snapshot_provider(),self.bridge.clock())
        if not name:self.result='stale';return
        now=time.monotonic()
        if now<self.next_attempt:return
        self.next_attempt=now+10
        # All dialogue jobs, including waiting answers, take priority.
        with self.bridge.connect() as c:
            if c.execute("SELECT 1 FROM jobs WHERE status IN ('queued','running') LIMIT 1").fetchone():return
            if c.execute("SELECT 1 FROM station_deliveries WHERE state IN ('queued','awaiting','speaking') LIMIT 1").fetchone():return
        result=self.transport(name);self.result=result.get('status','unavailable')
        if self.result=='displayed':self.last=name;self.updated_at=self.bridge.clock()
    def work(self):
        while not self.bridge.stop.is_set():
            try:self.step()
            except Exception:self.result='unavailable'
            self.bridge.stop.wait(3)
    def start(self):
        self.thread=threading.Thread(target=self.work,daemon=True,name='station-mood');self.thread.start()
