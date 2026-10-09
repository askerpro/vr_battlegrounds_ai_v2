"""Читает входящие задачи порциями по 100 и подтверждает прочитанное: печатает только адресованное этой задаче."""
import json
import subprocess
import sys

TASK = OWNER = 'arsenal-generator'


def call(*args):
    out = subprocess.run([sys.executable, '-X', 'utf8', 'Tools/agents/coordination.py', *args],
                         stdout=subprocess.PIPE, text=True, encoding='utf-8').stdout
    return json.loads(out)


for _ in range(50):
    reply = call('inbox', '--task', TASK, '--owner', OWNER, '--limit', '100')
    events = reply.get('result') or []
    if not events:
        break
    for event in events:
        if event.get('recipient') == TASK:
            print(event['id'], event['kind'], json.dumps(event['payload'], ensure_ascii=False)[:1500])
    last = max(e['id'] for e in events)
    ack = call('ack-events', '--task', TASK, '--owner', OWNER, '--event', str(last))
    if not ack.get('ok'):
        print('ack failed', ack)
        break
    if len(events) < 100:
        break
