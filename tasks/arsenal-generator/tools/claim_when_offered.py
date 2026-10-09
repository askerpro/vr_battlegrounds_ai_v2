"""Ждёт предложения своей заявки, сразу забирает и начинает аренду; печатает токен и guard."""
import json
import subprocess
import sys
import time

TICKET, OWNER = sys.argv[1], 'arsenal-generator'


def broker(*args):
    out = subprocess.run([sys.executable, '-X', 'utf8', 'Tools/agents/editor-broker.py', *args],
                         stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding='utf-8').stdout
    try:
        return json.loads(out)
    except json.JSONDecodeError:
        return {'ok': False, 'raw': out[-500:]}


deadline = time.time() + 3 * 3600
while time.time() < deadline:
    watch = broker('watch-ticket', '--ticket', TICKET, '--owner', OWNER, '--until', 'offered', '--timeout', '120')
    phase = (watch.get('result') or {}).get('phase')
    if phase in ('CANCELLED', 'DONE', 'FAILED'):
        print(json.dumps({'stop': phase}))
        sys.exit(1)
    if phase != 'OFFERED':
        continue
    claim = broker('claim', '--ticket', TICKET, '--owner', OWNER)
    token = (claim.get('result') or {}).get('token')
    if not token:
        print(json.dumps({'claim_failed': claim}, ensure_ascii=False)[:800])
        time.sleep(3)
        continue
    begin = broker('begin', '--ticket', TICKET, '--token', token)
    guard = broker('guard', '--ticket', TICKET, '--token', token)
    print(json.dumps({'token': token, 'begin_ok': begin.get('ok'), 'begin_error': begin.get('error'),
                      'guard': guard.get('result'), 'guard_error': guard.get('error')}, ensure_ascii=False))
    sys.exit(0)
print(json.dumps({'timeout': True}))
sys.exit(2)
