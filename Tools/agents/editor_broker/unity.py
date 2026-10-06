"""Файловый канал Unity; таймаут оставляет запрос для восстановления, без повтора."""
from __future__ import annotations

import json
import hashlib
import os
from pathlib import Path
import subprocess
import time
import uuid

BRIDGE_PATH = 'Assets/Editor/VR_Battlegrounds/Debug/EditorBrokerLocal'
BRIDGE_VERSION = 1
_TEMPLATES = Path(__file__).resolve().parents[1] / 'unity_bridge'
_FORBIDDEN = ('.git', 'Library', 'Temp', 'Logs', 'obj', BRIDGE_PATH)


class UnityOperationError(RuntimeError):
    """Проверенный terminal отказ; результат доступен контроллеру для recovery."""
    def __init__(self, message, result=None):
        super().__init__(message)
        self.result = result
        self.acknowledged = True


def _atomic_json(path: Path, value: dict):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + '.' + uuid.uuid4().hex + '.tmp')
    with temporary.open('w', encoding='utf-8', newline='\n') as stream:
        json.dump(value, stream, ensure_ascii=False)
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temporary, path)


def _paths(root: Path, paths):
    result = []
    for item in paths:
        path = str(item).replace('\\', '/')
        parts = path.split('/')
        if not path or Path(path).is_absolute() or ':' in path or any(p in ('', '.', '..') for p in parts):
            raise ValueError(f'Недопустимый выходной путь: {item}')
        if any(path.casefold() == p.casefold() or path.casefold().startswith(p.casefold() + '/')
               for p in _FORBIDDEN):
            raise ValueError(f'Запрещённый выходной путь: {item}')
        if not (root / path).resolve().is_relative_to(root):
            raise ValueError(f'Выход за project root: {item}')
        result.append(path)
    return result


class FileUnityAdapter:
    def __init__(self, editor_root, state_dir, timeout=120):
        self.root = Path(editor_root).resolve()
        self.state_dir = Path(state_dir).resolve()
        if self.state_dir.is_relative_to(self.root):
            raise ValueError('state_dir должен быть вне переключаемого checkout')
        self.timeout = timeout
        self.last_request_id = None
        self.on_request = None
        self.mailbox = self.state_dir / 'unity'
        for name in ('requests', 'responses'):
            (self.mailbox / name).mkdir(parents=True, exist_ok=True)

    def _request(self, operation, **arguments):
        request_id = uuid.uuid4().hex
        self.last_request_id = request_id
        # Внешний журнал обязан получить id до публикации изменяющего запроса.
        if self.on_request is not None:
            self.on_request(request_id, operation, arguments)
        _atomic_json(self.mailbox / 'requests' / (request_id + '.json'), {
            'version': BRIDGE_VERSION, 'request_id': request_id,
            'project_root': str(self.root), 'operation': operation,
            'created_unix_ms': time.time_ns() // 1_000_000, **arguments})
        return self.wait_response(request_id)

    def wait_response(self, request_id, timeout=None):
        """Прочитать ответ существующего запроса; изменяющая операция не отправляется снова."""
        if len(request_id) != 32 or any(c not in '0123456789abcdef' for c in request_id):
            raise ValueError('Некорректный request_id')
        response = self.mailbox / 'responses' / (request_id + '.json')
        archived = self.mailbox / 'archive' / (request_id + '.json')
        deadline = time.monotonic() + (self.timeout if timeout is None else timeout)
        while time.monotonic() < deadline:
            payload = None
            if response.is_file():
                payload = json.loads(response.read_text(encoding='utf-8-sig'))
            elif archived.is_file():
                journal = json.loads(archived.read_text(encoding='utf-8-sig'))
                request = journal.get('request', {})
                if journal.get('stage') != 'complete' or request.get('version') != BRIDGE_VERSION:
                    raise RuntimeError('Unity archive не является terminal известной версии')
                payload = {'request_id': request.get('request_id'),
                           'project_root': request.get('project_root'),
                           'ok': not journal.get('error'), 'error': journal.get('error'),
                           'result': journal.get('result')}
            if payload is not None:
                if payload.get('request_id') != request_id:
                    raise RuntimeError('Unity response request_id не совпадает')
                if Path(payload.get('project_root', '')).resolve() != self.root:
                    raise RuntimeError('Unity response project_root не совпадает')
                if not isinstance(payload.get('ok'), bool):
                    raise RuntimeError('Unity response ok отсутствует или повреждён')
                if not payload['ok']:
                    raise UnityOperationError(payload.get('error') or 'Unity bridge отказал', payload.get('result'))
                if not isinstance(payload.get('result'), dict):
                    raise RuntimeError('Unity terminal result отсутствует')
                return payload['result']
            time.sleep(min(.025, max(0, deadline - time.monotonic())))
        raise TimeoutError(f'Unity request {request_id} не завершён; не повторять операцию. '
                           f'Ответ: {response}')

    def inspect(self):
        return self._request('inspect')

    def park(self):
        return self._request('park')

    def refresh(self):
        return self._request('refresh')

    def save_outputs(self, output_roots):
        return self._request('save_outputs', output_roots=_paths(self.root, output_roots))

    def restore(self, setup):
        if not isinstance(setup, dict) or not isinstance(setup.get('scenes'), list):
            raise ValueError('restore требует setup.scenes')
        return self._request('restore', setup=setup)


def install_bridge(editor_root, state_dir):
    """Установка только в ignored папку; существующий чужой код не заменяется."""
    root, state = Path(editor_root).resolve(), Path(state_dir).resolve()
    if state.is_relative_to(root):
        raise ValueError('state_dir должен быть вне переключаемого checkout')
    for path in (BRIDGE_PATH + '/', BRIDGE_PATH + '.meta'):
        check = subprocess.run(['git', '--no-pager', '-C', str(root), 'check-ignore', '--no-index', '-q', path],
                               capture_output=True)
        if check.returncode:
            raise RuntimeError(f'Перед init добавить в .gitignore: {path}')
    target = root / BRIDGE_PATH
    if not target.resolve().is_relative_to(root):
        raise RuntimeError('Локальный мост выходит за project root')
    files = {
        'EditorBrokerBridge.cs': (_TEMPLATES / 'EditorBrokerBridge.cs.txt').read_bytes(),
        'EditorBrokerHumanPanel.cs': (_TEMPLATES / 'EditorBrokerHumanPanel.cs.txt').read_bytes(),
        'EditorBrokerLocal.asmdef': (_TEMPLATES / 'EditorBrokerLocal.asmdef.txt').read_bytes(),
    }
    configuration = {'version': BRIDGE_VERSION, 'project_root': str(root), 'state_dir': str(state)}
    config = target / 'bridge-config.json'
    if target.exists():
        for path in target.iterdir():
            if path.name not in (*files, 'bridge-config.json') and not path.name.endswith('.meta'):
                raise RuntimeError(f'Неизвестный файл локального моста: {path}')
        for name, data in files.items():
            path = target / name
            if path.exists() and path.read_bytes() != data:
                raise RuntimeError(f'Неизвестная версия локального моста: {path}')
        if config.exists() and json.loads(config.read_text(encoding='utf-8-sig')) != configuration:
            raise RuntimeError('Существующая bridge-config.json принадлежит другой конфигурации')
    target.mkdir(parents=True, exist_ok=True)
    for name, data in files.items():
        path = target / name
        if not path.exists():
            with path.open('xb') as stream:
                stream.write(data)
                stream.flush()
                os.fsync(stream.fileno())
    if not config.exists():
        _atomic_json(config, configuration)
    FileUnityAdapter(root, state)
    return {'bridge_path': str(target), 'version': BRIDGE_VERSION,
            'project_root': str(root), 'state_dir': str(state),
            'managed_files': {str((target / name).relative_to(root)).replace('\\', '/'):
                              hashlib.sha256((target / name).read_bytes()).hexdigest()
                              for name in (*files, 'bridge-config.json')}}
