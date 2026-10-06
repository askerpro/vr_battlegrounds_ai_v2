import importlib
import json
import pathlib
import subprocess
import sys
import tempfile
import threading
import time
import unittest

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parents[1]))


class UnityAdapterTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = pathlib.Path(self.tmp.name) / 'project'
        self.root.mkdir()
        self.state = pathlib.Path(self.tmp.name) / 'state'
        self.api = importlib.import_module('editor_broker.unity')
        self.adapter = self.api.FileUnityAdapter(self.root, self.state, timeout=.15)

    def response(self, result=None, error=None, root=None):
        def run():
            inbox = self.state / 'unity' / 'requests'
            deadline = time.monotonic() + 2
            while time.monotonic() < deadline:
                files = list(inbox.glob('*.json')) if inbox.exists() else []
                if files:
                    request = json.loads(files[0].read_text())
                    reply = {'request_id': request['request_id'], 'ok': error is None,
                             'project_root': str(root or self.root), 'error': error or '',
                             'result': result or {'ready': True}}
                    out = self.state / 'unity' / 'responses' / files[0].name
                    out.write_text(json.dumps(reply))
                    return
                time.sleep(.005)
        thread = threading.Thread(target=run)
        thread.start()
        self.addCleanup(thread.join)

    def test_inspect_returns_durable_response_and_unique_request(self):
        self.response({'ready': True, 'process_id': 123})
        self.assertEqual(self.adapter.inspect()['process_id'], 123)
        requests = list((self.state / 'unity' / 'requests').glob('*.json'))
        self.assertEqual(len(requests), 1)
        request = json.loads(requests[0].read_text())
        self.assertEqual(request['operation'], 'inspect')
        self.assertEqual(request['project_root'], str(self.root.resolve()))
        self.assertTrue((self.state / 'unity' / 'responses' / requests[0].name).exists())

    def test_timeout_never_resubmits_mutation(self):
        with self.assertRaises(TimeoutError) as raised:
            self.adapter.park()
        requests = list((self.state / 'unity' / 'requests').glob('*.json'))
        self.assertEqual(len(requests), 1)
        self.assertIn(requests[0].stem, str(raised.exception))
        self.assertEqual(self.adapter.last_request_id, requests[0].stem)

    def test_wait_response_settles_existing_request_without_replay(self):
        with self.assertRaises(TimeoutError):
            self.adapter.refresh()
        request_id = self.adapter.last_request_id
        self.response({'ready': True, 'session_id': 'same-session'})
        self.assertEqual(self.adapter.wait_response(request_id)['session_id'], 'same-session')
        self.assertEqual(len(list((self.state / 'unity' / 'requests').glob('*.json'))), 1)

    def test_state_directory_must_be_outside_switchable_root(self):
        with self.assertRaises(ValueError):
            self.api.FileUnityAdapter(self.root, self.root / 'state')

    def archived(self, request_id, **changes):
        path = self.state / 'unity/archive' / (request_id + '.json')
        value = {'stage': 'complete', 'error': '', 'result': {'ready': True, 'durable': True},
                 'request': {'version': 1, 'request_id': request_id, 'project_root': str(self.root)}}
        value.update(changes)
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(json.dumps(value))

    def test_deleted_response_is_read_from_exact_archived_terminal_without_request(self):
        request_id = 'a' * 32
        self.archived(request_id)
        self.assertTrue(self.adapter.wait_response(request_id)['durable'])
        self.assertFalse(list((self.state / 'unity/requests').glob('*.json')))

    def test_archive_error_is_completed_rejection_and_invalid_archive_fails_closed(self):
        request_id = 'b' * 32
        self.archived(request_id, error='durable refusal')
        with self.assertRaisesRegex(RuntimeError, 'durable refusal'):
            self.adapter.wait_response(request_id)
        for change in ({'stage': 'waiting'}, {'request': {'version': 2, 'request_id': request_id,
                        'project_root': str(self.root)}}, {'request': {'version': 1,
                        'request_id': 'c' * 32, 'project_root': str(self.root)}},
                       {'request': {'version': 1, 'request_id': request_id, 'project_root': str(self.root.parent)}}):
            self.archived(request_id, **change)
            with self.subTest(change=change), self.assertRaises(RuntimeError):
                self.adapter.wait_response(request_id)

    def test_archive_lookup_does_not_enumerate_history(self):
        from unittest.mock import patch
        request_id = 'd' * 32
        self.archived(request_id)
        with patch.object(pathlib.Path, 'iterdir', side_effect=AssertionError('history scan')), \
             patch.object(pathlib.Path, 'glob', side_effect=AssertionError('history scan')):
            self.assertTrue(self.adapter.wait_response(request_id)['ready'])

    def test_completed_refusal_is_acknowledged_but_wrong_root_is_not(self):
        self.response(result={'scenes': []}, error='completed refusal')
        with self.assertRaises(RuntimeError) as completed:
            self.adapter.park()
        self.assertTrue(completed.exception.acknowledged)
        self.assertIsInstance(completed.exception, self.api.UnityOperationError)
        self.assertEqual(completed.exception.result, {'scenes': []})
        path = self.adapter.mailbox / 'responses' / (self.adapter.last_request_id + '.json')
        payload = json.loads(path.read_text())
        payload['project_root'] = str(self.root.parent)
        path.write_text(json.dumps(payload))
        with self.assertRaises(RuntimeError) as invalid:
            self.adapter.wait_response(self.adapter.last_request_id)
        self.assertNotIsInstance(invalid.exception, self.api.UnityOperationError)
        self.assertFalse(getattr(invalid.exception, 'acknowledged', False))

    def test_archived_refusal_is_acknowledged_only_after_identity_and_terminal_validation(self):
        request_id = 'e' * 32
        self.archived(request_id, error='archive refusal')
        with self.assertRaises(RuntimeError) as completed:
            self.adapter.wait_response(request_id)
        self.assertTrue(completed.exception.acknowledged)
        self.assertEqual(completed.exception.result, {'ready': True, 'durable': True})
        self.archived(request_id, error='archive refusal', stage='waiting')
        with self.assertRaises(RuntimeError) as invalid:
            self.adapter.wait_response(request_id)
        self.assertNotIsInstance(invalid.exception, self.api.UnityOperationError)

    def test_on_request_callback_runs_before_publish_and_can_refuse_dispatch(self):
        def refuse(request_id, operation, arguments):
            self.assertEqual(operation, 'park')
            self.assertEqual(request_id, self.adapter.last_request_id)
            self.assertFalse(list((self.adapter.mailbox / 'requests').glob('*.json')))
            raise RuntimeError('durable intent failed')
        self.adapter.on_request = refuse
        with self.assertRaisesRegex(RuntimeError, 'durable intent failed'):
            self.adapter.park()
        self.assertFalse(list((self.adapter.mailbox / 'requests').glob('*.json')))

    def test_wrong_root_and_bridge_rejection_fail_closed(self):
        self.response(root=self.root.parent)
        with self.assertRaises(RuntimeError):
            self.adapter.inspect()

    def test_unsafe_state_is_reported_as_exception(self):
        self.response(error='dirty scenes')
        with self.assertRaisesRegex(RuntimeError, 'dirty scenes'):
            self.adapter.park()

    def test_save_outputs_rejects_escape_and_bridge_paths_before_request(self):
        for path in ['../outside', '/tmp/outside', 'Library', '.git',
                     'Assets/Editor/VR_Battlegrounds/Debug/EditorBrokerLocal']:
            with self.subTest(path=path), self.assertRaises(ValueError):
                self.adapter.save_outputs([path])
        self.assertFalse(list((self.state / 'unity' / 'requests').glob('*.json')))

    def test_restore_transports_scene_setup(self):
        setup = {'scenes': [{'path': 'Assets/Main.unity', 'is_loaded': True,
                             'is_active': True}], 'prefab_asset_path': ''}
        self.response()
        self.adapter.restore(setup)
        request = json.loads(next((self.state / 'unity' / 'requests').glob('*.json')).read_text())
        self.assertEqual(request['setup'], setup)


class BridgeInstallTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = pathlib.Path(self.tmp.name) / 'project'
        self.root.mkdir()
        subprocess.run(['git', 'init', '-q', str(self.root)], check=True)
        self.state = pathlib.Path(self.tmp.name) / 'state'
        self.api = importlib.import_module('editor_broker.unity')
        self.target = self.root / self.api.BRIDGE_PATH

    def ignore(self):
        (self.root / '.gitignore').write_text(self.api.BRIDGE_PATH + '/\n' +
                                             self.api.BRIDGE_PATH + '.meta\n')

    def test_install_requires_ignored_directory_and_meta(self):
        with self.assertRaises(RuntimeError):
            self.api.install_bridge(self.root, self.state)
        self.assertFalse(self.target.exists())
        (self.root / '.gitignore').write_text(self.api.BRIDGE_PATH + '/\n')
        with self.assertRaises(RuntimeError):
            self.api.install_bridge(self.root, self.state)

    def test_known_install_is_idempotent_and_unknown_source_is_preserved(self):
        self.ignore()
        first = self.api.install_bridge(self.root, self.state)
        self.assertTrue((self.target / 'EditorBrokerBridge.cs').exists())
        import hashlib
        expected = {str(path.relative_to(self.root)).replace('\\', '/'): hashlib.sha256(path.read_bytes()).hexdigest()
                    for path in self.target.iterdir() if not path.name.endswith('.meta')}
        self.assertEqual(first['managed_files'], expected)
        self.assertEqual(first, self.api.install_bridge(self.root, self.state))
        source = self.target / 'EditorBrokerBridge.cs'
        source.write_text('foreign source')
        with self.assertRaises(RuntimeError):
            self.api.install_bridge(self.root, self.state)
        self.assertEqual(source.read_text(), 'foreign source')

    def test_previously_installed_version_is_upgraded_to_template(self):
        self.ignore()
        import hashlib
        self.api.install_bridge(self.root, self.state)
        source = self.target / 'EditorBrokerBridge.cs'
        template = source.read_bytes()
        source.write_bytes(b'// previous managed bridge')
        relative = self.api.BRIDGE_PATH + '/EditorBrokerBridge.cs'
        with self.assertRaises(RuntimeError):
            self.api.install_bridge(self.root, self.state)
        previous = {relative: hashlib.sha256(b'// previous managed bridge').hexdigest()}
        result = self.api.install_bridge(self.root, self.state, previous=previous)
        self.assertEqual(source.read_bytes(), template)
        self.assertEqual(result['managed_files'][relative], hashlib.sha256(template).hexdigest())


if __name__ == '__main__':
    unittest.main()
