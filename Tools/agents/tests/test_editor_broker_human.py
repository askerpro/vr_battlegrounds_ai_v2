"""Передача человеком на настоящем Git; действующий редактор не используется."""
import unittest
import test_editor_broker_service as fixture
from editor_broker.service import BrokerError


class HumanTests(unittest.TestCase):
    setUp = fixture.EditorBrokerTests.setUp
    git = staticmethod(fixture.EditorBrokerTests.git)

    def test_main_advance_refuses_old_request(self):
        (self.repo / 'Assets/new.txt').write_text('accepted')
        self.git(self.repo, 'add', '.')
        self.git(self.repo, 'commit', '-qm', 'main advances')
        with self.assertRaises(BrokerError):
            self.broker.request('a', self.base, self.input, self.agent, [], 'old')

    def test_pause_preserves_waiter(self):
        ticket = self.broker.request('a', self.base, self.input, self.agent, [], 'wait')
        self.broker.store.set_paused(True)
        self.assertEqual(self.broker.store.get(ticket['id'])['phase'], 'QUEUED')
        with self.assertRaises(Exception):
            self.broker.store.claim(ticket['id'], 'a')
        self.broker.store.set_paused(False)
        self.assertEqual(self.broker.store.get(ticket['id'])['phase'], 'OFFERED')

    def test_stash_returns_staged_and_untracked_preserves_other_stash(self):
        from editor_broker.human import HumanHandoff
        path = self.editor / 'Assets/Base.unity'
        path.write_text('older stash')
        self.git(self.editor, 'stash', 'push', '-m', 'foreign')
        foreign = self.git(self.editor, 'rev-parse', 'refs/stash')
        path.write_text('staged change')
        self.git(self.editor, 'add', 'Assets/Base.unity')
        (self.editor / 'Assets/new.bytes').write_bytes(b'\x00\xff')
        human = HumanHandoff(self.broker)
        saved = human.release()
        self.assertTrue(self.broker.git.is_clean())
        self.assertNotEqual(saved['stash_sha'], foreign)
        human.resume()
        self.assertEqual(path.read_text(), 'staged change')
        self.assertEqual((self.editor / 'Assets/new.bytes').read_bytes(), b'\x00\xff')
        self.assertEqual(self.git(self.editor, 'rev-parse', 'refs/stash'), foreign)
        self.assertTrue(self.broker.store.is_paused())

    def test_resume_conflict_keeps_stash_and_pauses(self):
        from editor_broker.human import HumanHandoff
        human = HumanHandoff(self.broker)
        (self.editor / 'Assets/Base.unity').write_text('human')
        saved = human.release()
        (self.editor / 'Assets/Base.unity').write_text('foreign')
        with self.assertRaises(Exception):
            human.resume()
        self.assertEqual(self.git(self.editor, 'rev-parse', 'refs/stash'), saved['stash_sha'])
        self.assertEqual((self.editor / 'Assets/Base.unity').read_text(), 'foreign')
        self.assertTrue(self.broker.store.is_paused())

    def _blocked_pair(self):
        first = self.broker.request('a', self.base, self.input, self.agent, [], 'first')
        second = self.broker.request('b', self.base, self.input, self.agent, [], 'second')
        self.unity.dirty = True
        with self.assertRaises(BrokerError):
            self.broker.claim(first['id'], 'a')
        return first, second

    def notice(self):
        import json
        return json.loads((self.state / 'human-blocked.json').read_text(encoding='utf-8'))

    def test_defer_keeps_first_place_and_renotifies_after_deadline(self):
        from editor_broker.human import HumanHandoff
        import time
        first, second = self._blocked_pair()
        episode = self.notice()['episode']
        HumanHandoff(self.broker).defer(10)
        self.assertTrue(self.notice()['acknowledged'])
        self.assertEqual(self.broker.store.get(first['id'])['phase'], 'QUEUED')
        with self.assertRaises(BrokerError):
            self.broker.claim(first['id'], 'a')
        self.assertTrue(self.notice()['acknowledged'])
        self.broker.store.clock = lambda: time.time() + 601
        self.assertEqual(self.broker.store.get(first['id'])['phase'], 'OFFERED')
        self.assertEqual(self.broker.store.get(second['id'])['phase'], 'QUEUED')
        with self.assertRaises(BrokerError):
            self.broker.claim(first['id'], 'a')
        self.assertNotEqual(self.notice()['episode'], episode)
        self.assertFalse(self.notice().get('acknowledged'))

    def test_refuse_holds_queue_without_new_notice_until_release(self):
        from editor_broker.human import HumanHandoff
        first, _ = self._blocked_pair()
        human = HumanHandoff(self.broker)
        human.defer(0)
        episode = self.notice()['episode']
        with self.assertRaises(BrokerError):
            self.broker.claim(first['id'], 'a')
        self.assertEqual(self.notice()['episode'], episode)
        self.assertFalse(human.snapshot()['notify'])
        self.unity.dirty = False
        human.release()
        self.assertFalse(self.broker.store.is_paused())
        self.assertEqual(self.broker.claim(first['id'], 'a')['phase'], 'LEASED')
