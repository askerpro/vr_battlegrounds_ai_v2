"""Чекпоинт и заявка аренды: выходы — данные этапа composer-presentation из plan.json (без кода и документов)."""
import json
import subprocess
import sys

LABEL = sys.argv[1]
plan = json.load(open('tasks/arsenal-generator/plan.json', encoding='utf-8'))
writes = next(s['writes'] for s in plan['stages'] if s['id'] == 'composer-presentation')
code = ('Assets/Scripts/', 'Assets/Editor/', 'Assets/Tests/', 'Docs/', 'tasks/')
outputs = [w[:-3] if w.endswith('/**') else w for w in writes if not w.startswith(code)]


def broker(*args):
    out = subprocess.run([sys.executable, '-X', 'utf8', 'Tools/agents/editor-broker.py', *args],
                         stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True, encoding='utf-8').stdout
    return json.loads(out.strip().splitlines()[-1])


checkpoint = broker('checkpoint', '--label', LABEL, '--path', 'Assets', '--path', 'Docs/Arsenal',
                    '--path', 'tasks/arsenal-generator')
sha = (checkpoint.get('result') or {}).get('sha') or (checkpoint.get('result') or {}).get('checkpoint')
if not checkpoint.get('ok') or not sha:
    print(json.dumps({'checkpoint': checkpoint}, ensure_ascii=False)[:1500])
    sys.exit(1)
args = ['request', '--owner', 'arsenal-generator', '--input', sha, '--key', LABEL + '-1']
for o in outputs:
    args += ['--output', o]
request = broker(*args)
print(json.dumps({'input': sha, 'outputs': len(outputs), 'request': request}, ensure_ascii=False)[:2000])
