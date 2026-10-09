"""Держит аренду на время ручной проверки: продлевает каждые 3 мин, пока нет стоп-файла (не дольше лимита)."""
import json
import pathlib
import subprocess
import sys
import time

TICKET, TOKEN = sys.argv[1], sys.argv[2]
LIMIT = float(sys.argv[3]) if len(sys.argv) > 3 else 2 * 3600
STOP = pathlib.Path('tasks/arsenal-generator/reports/new-flow/stop-keep-' + TICKET)

deadline = time.time() + LIMIT
while time.time() < deadline and not STOP.exists():
    out = subprocess.run([sys.executable, '-X', 'utf8', 'Tools/agents/editor-broker.py', 'renew', '--ticket', TICKET,
                          '--token', TOKEN], stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True,
                         encoding='utf-8').stdout
    if '"ok": true' not in out:
        print(json.dumps({'renew_failed': out[-400:]}, ensure_ascii=False))
        sys.exit(1)
    for _ in range(18):
        if STOP.exists():
            break
        time.sleep(10)
print(json.dumps({'stopped': STOP.exists(), 'timeout': time.time() >= deadline}))
