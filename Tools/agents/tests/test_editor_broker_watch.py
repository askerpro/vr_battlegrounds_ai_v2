"""Обычный монитор команды: процесс заканчивается только на значимом событии."""
import importlib
from pathlib import Path
import sys
import tempfile
import threading
import time
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from editor_broker.queue import BrokerStore


class WatchTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.store = BrokerStore(self.tmp.name)
        try:
            self.watch = importlib.import_module("editor_broker.watch").watch_ticket
        except ModuleNotFoundError:
            self.fail("watch-ticket implementation missing")

    def enqueue(self, owner, key):
        return self.store.enqueue(owner, "B", "A", "agent-root", [], key)

    def test_monitor_waits_for_reserved_offer_without_claiming(self):
        first = self.enqueue("first", "1")
        lease = self.store.claim(first["id"], "first")
        next_ticket = self.enqueue("next", "2")
        def release():
            time.sleep(.08)
            self.store.complete(lease["id"], lease["token"])
        thread = threading.Thread(target=release)
        thread.start()
        self.addCleanup(thread.join)
        code, event = self.watch(self.store, next_ticket["id"], "next", timeout=2)
        self.assertEqual(code, 0)
        self.assertEqual(event["event"], "offered")
        self.assertEqual(self.store.get(next_ticket["id"])["phase"], "OFFERED")
        self.assertNotIn("token", event)

    def test_timeout_is_not_reported_as_successful_grant(self):
        first = self.enqueue("first", "1")
        self.store.claim(first["id"], "first")
        next_ticket = self.enqueue("next", "2")
        code, event = self.watch(self.store, next_ticket["id"], "next", timeout=.08)
        self.assertEqual(code, 5)
        self.assertEqual(event["event"], "timeout")
        self.assertEqual(self.store.get(next_ticket["id"])["phase"], "QUEUED")

    def test_cancel_and_recovery_have_distinct_exit_codes(self):
        ticket = self.enqueue("owner", "1")
        self.store.cancel(ticket["id"], "owner")
        self.assertEqual(self.watch(self.store, ticket["id"], "owner", timeout=0)[0], 3)
        ticket = self.enqueue("owner", "2")
        lease = self.store.claim(ticket["id"], "owner")
        self.store.transition(ticket["id"], lease["token"], "RECOVERY_REQUIRED")
        code, event = self.watch(self.store, ticket["id"], "owner", timeout=0)
        self.assertEqual(code, 4)
        self.assertEqual(event["event"], "recovery_required")

    def test_result_monitor_ignores_offer_and_wakes_on_available_result(self):
        ticket = self.enqueue("owner", "1")
        def finish():
            time.sleep(.05)
            lease = self.store.claim(ticket["id"], "owner")
            self.store.complete(ticket["id"], lease["token"], result_sha="result")
        thread = threading.Thread(target=finish)
        thread.start()
        self.addCleanup(thread.join)
        code, event = self.watch(self.store, ticket["id"], "owner", until="result", timeout=2)
        self.assertEqual(code, 0)
        self.assertEqual(event["event"], "result_available")
        self.assertEqual(event["result_sha"], "result")

    def test_queued_monitor_wakes_when_another_owner_blocks_recovery(self):
        first = self.enqueue("first", "1")
        lease = self.store.claim(first["id"], "first")
        second = self.enqueue("second", "2")
        self.store.transition(first["id"], lease["token"], "RECOVERY_REQUIRED")
        code, event = self.watch(self.store, second["id"], "second", timeout=.1)
        self.assertEqual(code, 4)
        self.assertEqual(event["event"], "recovery_required")
        self.assertEqual(event["blocking_ticket_id"], first["id"])
        self.assertNotIn("token", event)


if __name__ == "__main__":
    unittest.main()
