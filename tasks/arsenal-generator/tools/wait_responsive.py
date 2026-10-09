"""Ждёт, пока воркер аренды снова отвечает (guard), продлевая аренду; печатает итог guard."""
import json
import subprocess
import sys
import time

TICKET, TOKEN, LIMIT = sys.argv[1], sys.argv[2], float(sys.argv[3]) if len(sys.argv) > 3 else 540


def broker(*args):
    out = subprocess.run([sys.executable, '-X', 'utf8', 'Tools/agents/editor-broker.py', *args],
                         stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding='utf-8').stdout
    try:
        return json.loads(out.strip().splitlines()[-1])
    except (json.JSONDecodeError, IndexError):
        return {'ok': False, 'raw': out[-300:]}


deadline = time.time() + LIMIT
last = None
while time.time() < deadline:
    broker('renew', '--ticket', TICKET, '--token', TOKEN)
    last = (broker('guard', '--ticket', TICKET, '--token', TOKEN).get('result') or {})
    if last.get('unity_responsive') and last.get('ready'):
        break
    time.sleep(15)
print(json.dumps({k: last.get(k) for k in ('unity_responsive', 'ready', 'heartbeat_age_s', 'is_test_running', 'is_compiling', 'is_playing')}))
