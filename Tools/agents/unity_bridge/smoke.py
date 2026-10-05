"""Подготовка и явный запуск отдельного Unity-проекта; рабочий Editor не затрагивается."""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import time
import uuid

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from editor_broker.unity import BRIDGE_PATH, FileUnityAdapter, _atomic_json, install_bridge


def git(root, *arguments):
    result = subprocess.run(['git', '--no-pager', '-C', str(root), *arguments],
                            capture_output=True, text=True, encoding='utf-8', check=True)
    return result.stdout.strip()


def prepare(fixture: Path):
    root = fixture / 'project'
    state = fixture / 'state'
    if root.exists():
        if not (fixture / 'fixture.json').exists():
            raise RuntimeError('Отказ: существующая директория не принадлежит стенду')
        return root, state
    repository = fixture / 'repository'
    repository.mkdir(parents=True)
    git(repository, 'init', '-q')
    git(repository, 'config', 'user.name', 'Isolated Unity broker fixture')
    git(repository, 'config', 'user.email', 'fixture@example.invalid')
    (repository / '.gitignore').write_text(BRIDGE_PATH + '/\n' + BRIDGE_PATH + '.meta\n' +
        '/Library/\n/Temp/\n/Logs/\n/UserSettings/\n/obj/\n')
    (repository / 'Packages').mkdir()
    (repository / 'Packages/manifest.json').write_text('{"dependencies": {}}\n')
    (repository / 'ProjectSettings').mkdir()
    (repository / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 6000.4.1f1\n')
    smoke = repository / 'Assets/Editor/Smoke'
    smoke.mkdir(parents=True)
    shutil.copyfile(Path(__file__).with_name('EditorBrokerSmoke.cs.txt'), smoke / 'EditorBrokerSmoke.cs')
    (smoke / 'EditorBrokerSmoke.asmdef').write_text(json.dumps({
        'name': 'EditorBrokerSmoke', 'includePlatforms': ['Editor'], 'references': []}))
    git(repository, 'add', '.')
    git(repository, 'commit', '-qm', 'Isolated fixture seed')
    git(repository, 'worktree', 'add', '--detach', str(root), 'HEAD')
    install_bridge(root, state)
    _atomic_json(fixture / 'fixture.json', {'project_root': str(root), 'state_dir': str(state)})
    return root, state


def read_when_ready(path, timeout=180):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if path.exists():
            value = json.loads(path.read_text(encoding='utf-8-sig'))
            if value.get('error'):
                raise RuntimeError(value['error'])
            return value
        time.sleep(.05)
    raise TimeoutError(str(path))


def run(fixture, root, state, unity):
    log = fixture / 'Editor.log'
    process = subprocess.Popen([unity, '-batchmode', '-nographics', '-projectPath', str(root),
        '-executeMethod', 'EditorBrokerSmoke.Start', '-logFile', str(log)],
        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
        creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0)
    checks = []
    api = FileUnityAdapter(root, state, timeout=120)
    directory = state / 'smoke-fixture'

    def command(operation, path=None):
        request_id = uuid.uuid4().hex
        _atomic_json(directory / 'command.json', {'id': request_id, 'operation': operation, 'path': path})
        return read_when_ready(directory / (request_id + '.json'))

    def rejected(action, text=None):
        try:
            action()
        except RuntimeError as error:
            if text is not None:
                assert text in str(error), str(error)
        else:
            raise AssertionError('Изменение должно быть отклонено')

    def wait_archived(request_id):
        path = state / 'unity/archive' / (request_id + '.json')
        deadline = time.monotonic() + 30
        while time.monotonic() < deadline:
            if path.is_file() and not (state / 'unity/requests' / (request_id + '.json')).exists():
                return path
            time.sleep(.02)
        raise TimeoutError('Archive did not finish: ' + request_id)

    def fresh_heartbeat(after=0):
        deadline = time.monotonic() + 10
        while time.monotonic() < deadline:
            heartbeat = json.loads((state / 'unity/heartbeat.json').read_text())
            if heartbeat['updated_unix_ms'] > after:
                return heartbeat
            time.sleep(.05)
        raise TimeoutError('No fresh Unity heartbeat')

    try:
        read_when_ready(directory / 'ready.json')
        initial = api.inspect()
        assert initial['ready'] and not initial['compile_errors'], initial
        assert initial['process_id'] == process.pid
        assert command('inspect')['scenes'] == ['Assets/Scenes/A.unity', 'Assets/Scenes/B.unity']
        checks.append('inspect/root/process/clean')

        command('dirty_asset', 'Assets/Foreign/Foreign.mat')
        rejected(lambda: api.save_outputs(['Assets/Output']), 'Чужой dirty')
        rejected(api.park, 'Несохранённые')
        assert api.inspect()['dirty_assets'] == ['Assets/Foreign/Foreign.mat']
        command('clean')
        checks.append('foreign dirty refuses scoped save and park')

        command('dirty_asset', 'Assets/Output/Generated.mat')
        saved = api.save_outputs(['Assets/Output'])
        assert saved['ready'] and saved['saved_paths'] == ['Assets/Output/Generated.mat'], saved
        command('dirty_scene')
        rejected(lambda: api.save_outputs(['Assets/Output']), 'Чужой dirty')
        saved = api.save_outputs(['Assets/Scenes'])
        assert saved['ready'] and saved['saved_paths'] == ['Assets/Scenes/A.unity'], saved
        checks.append('scoped asset/scene saving')

        command('prefab')
        setup = api.park()
        assert setup['prefab_path'] == 'Assets/Prefabs/Fixture.prefab'
        assert setup['auto_refresh_suppressed']
        assert command('inspect')['scenes'] == ['']
        reload_before = command('inspect')['reload_count']
        (root / 'Assets/Editor/Smoke/EditorBrokerReloadProbe.cs').write_text('public static class EditorBrokerReloadProbe {}\n')
        refreshed = api.refresh()
        assert refreshed['ready'] and not refreshed['auto_refresh_suppressed']
        assert refreshed['session_id'] == initial['session_id']
        probe = command('inspect')
        assert probe['probe_loaded'] and probe['reload_count'] > reload_before, probe
        api.restore(setup)
        assert command('inspect')['prefab'] == setup['prefab_path']
        command('close_prefab')
        restored = command('inspect')
        assert restored['scenes'] == ['Assets/Scenes/A.unity', 'Assets/Scenes/B.unity'], restored
        assert restored['active'] == 'Assets/Scenes/A.unity'
        checks.append('park/empty/AutoRefresh/domain reload/scene and prefab restore')

        setup = api.park()
        broken = root / 'Assets/Editor/Smoke/CompileError.cs'
        broken.write_text('this is intentionally invalid C#;\n')
        failed_compile = api.refresh()
        assert failed_compile['compilation_failed'] and failed_compile['compile_errors']
        api.park()
        broken.unlink()
        (root / 'Assets/Editor/Smoke/CompileError.cs.meta').unlink(missing_ok=True)
        fixed_compile = api.refresh()
        assert not fixed_compile['compilation_failed'] and not fixed_compile['compile_errors']
        api.restore(setup)
        checks.append('compiler errors acknowledged; recovery park and refresh restore')

        api.timeout = .001
        try:
            api.inspect()
        except TimeoutError:
            pending = api.last_request_id
            settled = api.wait_response(pending, timeout=120)
            assert settled['ready']
            request = state / 'unity/requests' / (pending + '.json')
            wait_archived(pending)
            assert not request.exists()
            assert (state / 'unity/archive/requests' / request.name).exists()
        else:
            raise AssertionError('Строгий таймаут должен оставить один ожидаемый запрос')
        api.timeout = 120
        checks.append('timeout settles same durable request without replay')

        interrupted = uuid.uuid4().hex
        request = {'version': 1, 'request_id': interrupted, 'project_root': str(root),
                   'operation': 'park', 'created_unix_ms': time.time_ns() // 1_000_000}
        _atomic_json(state / 'unity/journals' / (interrupted + '.json'), {
            'request': request, 'session_id': initial['session_id'], 'stage': 'dispatched'})
        rejected(lambda: api.wait_response(interrupted), 'Повтор запрещён')
        assert command('inspect')['scenes'] == ['Assets/Scenes/A.unity', 'Assets/Scenes/B.unity']
        response = state / 'unity/responses' / (interrupted + '.json')
        wait_archived(interrupted)
        response.unlink()
        rejected(lambda: api.wait_response(interrupted), 'Повтор запрещён')
        time.sleep(.2)
        assert not response.exists(), 'Idle bridge should not scan history to regenerate deleted responses'
        _atomic_json(state / 'unity/requests' / (interrupted + '.json'), request)
        wait_archived(interrupted)
        assert command('inspect')['scenes'] == ['Assets/Scenes/A.unity', 'Assets/Scenes/B.unity']
        checks.append('interrupted intent and deleted response never replay mutation')

        blocked_id = uuid.uuid4().hex
        blocked_response = state / 'unity/responses' / (blocked_id + '.json')
        blocked_response.mkdir()
        blocked_journal = state / 'unity/journals' / (blocked_id + '.json')
        recovery_block = state / 'unity/recovery-block.json'
        try:
            _atomic_json(recovery_block, {'reason': 'isolated fixture IO boundary'})
            blocked_request = {
                'version': 1, 'request_id': blocked_id, 'project_root': str(root),
                'operation': 'inspect', 'created_unix_ms': time.time_ns() // 1_000_000}
            _atomic_json(blocked_journal, {'request': blocked_request,
                'session_id': initial['session_id'], 'stage': 'queued'})
            _atomic_json(state / 'unity/requests' / (blocked_id + '.json'), blocked_request)
            # На Windows обычный reader не даёт File.Replace удалить destination.
            # Сначала проверяем отказ записи terminal, потом отказ доставки response.
            with blocked_journal.open('rb') as reader:
                recovery_block.unlink()
                time.sleep(.6)
                if os.name == 'nt':
                    reader.seek(0)
                    assert json.load(reader)['stage'] == 'queued', 'Journal reader did not block replace as expected'
            deadline = time.monotonic() + 30
            while time.monotonic() < deadline:
                if blocked_journal.exists() and json.loads(blocked_journal.read_text())['stage'] == 'complete':
                    break
                time.sleep(.02)
            journal_state = json.loads(blocked_journal.read_text()) if blocked_journal.exists() else None
            heartbeat = json.loads((state / 'unity/heartbeat.json').read_text())
            assert journal_state and journal_state['stage'] == 'complete', {
                'request_id': blocked_id, 'journal': journal_state, 'heartbeat': heartbeat}
            completed_bytes = blocked_journal.read_bytes()
            completed_mtime = blocked_journal.stat().st_mtime_ns
            time.sleep(.6)
            assert blocked_journal.read_bytes() == completed_bytes, 'Terminal journal changed after reply IO failure'
            assert blocked_journal.stat().st_mtime_ns == completed_mtime, 'Terminal request was dispatched again after reply IO failure'
        finally:
            recovery_block.unlink(missing_ok=True)
            blocked_response.rmdir()
        assert api.wait_response(blocked_id)['ready']
        archive = wait_archived(blocked_id)
        assert archive.read_bytes() == completed_bytes and not blocked_journal.exists()
        checks.append('response IO failure preserves terminal journal and never redispatches')

        before = fresh_heartbeat(time.time_ns() // 1_000_000)
        last_seed = None
        for _ in range(1000):
            seed_id = uuid.uuid4().hex
            last_seed = {'version': 1, 'request_id': seed_id, 'project_root': str(root),
                         'operation': 'park', 'created_unix_ms': 1}
            _atomic_json(state / 'unity/archive' / (seed_id + '.json'), {
                'request': last_seed, 'session_id': initial['session_id'], 'stage': 'complete',
                'error': '', 'result': {'ready': True, 'process_id': process.pid,
                                      'session_id': initial['session_id']}})
        after = fresh_heartbeat(time.time_ns() // 1_000_000)
        for key in ('journal_reads', 'request_reads', 'archive_reads'):
            assert after[key] == before[key], (key, before, after)
        assert not list((state / 'unity/journals').glob('*.json'))
        assert not list((state / 'unity/requests').glob('*.json'))
        # Запрос старого id адресует ровно один архив и возвращает прежний результат park без park.
        _atomic_json(state / 'unity/requests' / (last_seed['request_id'] + '.json'), last_seed)
        wait_archived(last_seed['request_id'])
        assert api.wait_response(last_seed['request_id'])['process_id'] == process.pid
        duplicate = fresh_heartbeat(time.time_ns() // 1_000_000)
        assert duplicate['archive_reads'] - after['archive_reads'] == 1, (after, duplicate)
        assert duplicate['request_reads'] - after['request_reads'] == 1, (after, duplicate)
        assert duplicate['journal_reads'] == after['journal_reads'], (after, duplicate)
        assert command('inspect')['scenes'] == ['Assets/Scenes/A.unity', 'Assets/Scenes/B.unity']
        checks.append('1000 archived records cause zero idle reads; old id uses one lookup without park')

        command('play')
        deadline = time.monotonic() + 30
        while not api.inspect()['is_playing'] and time.monotonic() < deadline:
            time.sleep(.1)
        rejected(api.park, 'Unity занят')
        command('stop')
        deadline = time.monotonic() + 30
        while api.inspect()['is_playing'] and time.monotonic() < deadline:
            time.sleep(.1)
        assert not api.inspect()['is_playing']
        checks.append('Play Mode refuses mutation')

        # B фиксируется ПОСЛЕ первой инициализации Unity и создания ProjectSettings/метаданных.
        from editor_broker.git_state import GitState
        from editor_broker.service import EditorBroker
        git(root, 'add', '.')
        git(root, 'commit', '-qm', 'Warmed isolated fixture baseline')
        base = git(root, 'rev-parse', 'HEAD')
        EditorBroker.initialize(root, state, base)
        broker = EditorBroker(state, adapter=api)
        agent = fixture / 'agent'
        git(root, 'worktree', 'add', '--detach', str(agent), base)
        (agent / 'guest-input.txt').write_text('Immutable guest input A\n')
        incoming = GitState(agent).checkpoint(state, 'smoke-input')['sha']
        ticket = broker.request('isolated-smoke', base, incoming, agent, ['Assets/Output'], 'real-unity-smoke')
        ticket = broker.claim(ticket['id'], 'isolated-smoke')
        broker.begin(ticket['id'], ticket['token'])
        assert git(root, 'rev-parse', 'HEAD') == incoming
        (root / 'Assets/Output/generated.bytes').write_bytes(b'\x00broker-result\xff')
        api.refresh()
        command('generate_asset')
        command('dirty_asset', 'Assets/Output/Generated.mat')
        captured = broker.finish(ticket['id'], ticket['token'])
        assert captured['accepted'], captured
        assert git(root, 'rev-parse', 'HEAD') == base and GitState(root).is_clean()
        assert not (root / 'Assets/Output/generated.bytes').exists()
        broker.receive(ticket['id'], 'isolated-smoke', agent)
        assert (agent / 'Assets/Output/generated.bytes').read_bytes() == b'\x00broker-result\xff'
        assert (agent / 'Assets/Output/generated.bytes.meta').exists()
        assert (agent / 'Assets/Output/RuntimeGenerated.mat').exists()
        assert (agent / 'Assets/Output/RuntimeGenerated.mat.meta').exists()
        assert git(agent, 'rev-parse', 'HEAD') == base
        assert broker.store.get(ticket['id'])['phase'] == 'DONE'
        checks.append('real service A→generated asset/meta/binary→R→B→receive')

        _atomic_json(fixture / 'smoke-result.json', {'passed': True, 'checks': checks,
            'project_root': str(root), 'state_dir': str(state), 'log': str(log)})
        print(json.dumps({'passed': True, 'checks': len(checks), 'report': str(fixture / 'smoke-result.json')}))
    except BaseException as error:
        _atomic_json(fixture / 'smoke-result.json', {'passed': False, 'checks': checks,
            'error': str(error), 'log': str(log)})
        raise
    finally:
        if process.poll() is None:
            try:
                command('quit')
                process.wait(timeout=30)
            except Exception:
                process.terminate()
                process.wait(timeout=30)


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--fixture', type=Path)
    parser.add_argument('--run', action='store_true', help='Явно разрешить запуск изолированного Unity')
    parser.add_argument('--unity', default='C:/Program Files/Unity/Hub/Editor/6000.4.1f1/Editor/Unity.exe')
    args = parser.parse_args()
    fixture = (args.fixture or Path(tempfile.mkdtemp(prefix='editor-broker-smoke-'))).resolve()
    root, state = prepare(fixture)
    if args.run:
        run(fixture, root, state, args.unity)
    else:
        print(json.dumps({'prepared': True, 'fixture': str(fixture), 'project_root': str(root),
                          'run': [sys.executable, str(Path(__file__).resolve()), '--fixture', str(fixture), '--run']}))
