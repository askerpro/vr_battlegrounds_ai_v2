"""Продление одной собственной аренды только во время разрешённой пользователем Play Mode проверки."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[3]

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--ticket', required=True)
    parser.add_argument('--hold-until-release', action='store_true')
    args = parser.parse_args()
    token = os.environ.pop('VRBG_AVATAR_LEASE_TOKEN', None)
    if not token:
        raise SystemExit('Нет private token собственной аренды')
    deadline = float('inf') if args.hold_until_release else time.monotonic() + 3600
    while time.monotonic() < deadline:
        def call(action):
            result = subprocess.run(
                [sys.executable, str(ROOT / 'Tools/agents/editor-broker.py'), action,
                 '--ticket', args.ticket, '--token=' + token],
                cwd=ROOT, capture_output=True, text=True, encoding='utf-8', timeout=45,
                creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0)
            return json.loads(result.stdout)
        try:
            guard = call('guard')
            if not guard.get('ok'):
                print(json.dumps({'ticket': args.ticket, 'status': 'guard-rejected'}), flush=True)
                return
            if not args.hold_until_release and not guard.get('result', {}).get('is_playing'):
                print(json.dumps({'ticket': args.ticket, 'status': 'play-stopped'}), flush=True)
                return
            renewal = call('renew')
            if not renewal.get('ok'):
                print(json.dumps({'ticket': args.ticket, 'status': 'renew-rejected'}), flush=True)
                return
            print(json.dumps({'ticket': args.ticket, 'status': 'renewed-user-requested-hold' if args.hold_until_release else 'renewed-during-play'}), flush=True)
        except (subprocess.TimeoutExpired, ValueError, OSError) as error:
            print(json.dumps({'ticket': args.ticket, 'status': 'keepalive-error',
                              'error_type': type(error).__name__}), flush=True)
            return
        time.sleep(60)
    print(json.dumps({'ticket': args.ticket, 'status': 'one-hour-limit'}), flush=True)

if __name__ == '__main__':
    main()
