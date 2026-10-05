"""Проверки FIFO, сохранности событий и запрета кражи редакторской аренды."""

import importlib
import multiprocessing
from contextlib import contextmanager
from pathlib import Path
import sqlite3
import sys
import tempfile
import threading
import time
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
try:
    queue = importlib.import_module("editor_broker.queue")
except ModuleNotFoundError as error:
    if error.name != "editor_broker.queue":
        raise
    queue = None


def enqueue_worker(directory, start, results, number):
    start.wait(10)
    store = queue.BrokerStore(directory)
    result = store.enqueue(str(number), "B", "A", "root", ["Assets"], "key")
    results.put(result["ticket_id"])


def claim_worker(directory, start, results, ticket):
    start.wait(10)
    try:
        result = queue.BrokerStore(directory).claim(ticket, "owner")
        results.put(("ok", result["token"]))
    except (ValueError, RuntimeError):
        results.put(("denied", None))


class BrokerQueueTests(unittest.TestCase):
    def setUp(self):
        self.assertIsNotNone(queue, "FIFO/SQLite реализация ещё отсутствует")
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.now = 1000.0
        self.store = queue.BrokerStore(self.temp.name, clock=lambda: self.now,
                                       offer_seconds=10, lease_seconds=30)

    def enqueue(self, owner="owner", key="key"):
        return self.store.enqueue(owner, "B", "A", "agent-root", ["Assets"], key)

    def claim(self):
        ticket = self.enqueue()
        return self.store.claim(ticket["ticket_id"], "owner")

    def restore(self, ticket):
        for phase in ("SWITCHING", "RUNNING", "CAPTURING", "RESTORING"):
            ticket = self.store.transition(ticket["ticket_id"], ticket["token"], phase)
        return ticket

    def test_fifo_reserves_before_any_waiter_polls(self):
        first = self.enqueue("first", "1")
        second = self.enqueue("second", "2")
        self.assertEqual(first["phase"], "OFFERED")
        self.assertEqual(second["phase"], "QUEUED")
        with self.assertRaises((ValueError, RuntimeError)):
            self.store.claim(second["ticket_id"], "second")
        first = self.store.claim(first["ticket_id"], "first")
        self.store.complete(**{"ticket_id": first["ticket_id"], "token": first["token"]})
        self.assertEqual(self.store.get(second["ticket_id"])["phase"], "OFFERED")

    def test_read_only_ticket_can_declare_no_output_paths(self):
        ticket = self.store.enqueue("reader", "B", "A", "agent-root", [], "read-only")
        self.assertEqual(ticket["output_roots"], [])
        self.assertEqual(self.store.claim(ticket["id"], "reader")["phase"], "LEASED")

    def test_idempotency_is_owner_scoped_and_rejects_changed_payload(self):
        first = self.enqueue()
        self.assertEqual(self.enqueue()["ticket_id"], first["ticket_id"])
        self.assertNotEqual(self.enqueue("other")["ticket_id"], first["ticket_id"])
        with self.assertRaises(ValueError):
            self.store.enqueue("owner", "B", "changed", "agent-root", ["Assets"], "key")
        self.assertEqual(len(self.store.status()["tickets"]), 2)

    def test_offered_expiry_skips_absent_owner(self):
        first = self.enqueue("first", "1")
        second = self.enqueue("second", "2")
        self.now += 10
        self.assertEqual(self.store.get(second["ticket_id"])["phase"], "OFFERED")
        self.assertEqual(self.store.get(first["ticket_id"])["phase"], "CANCELLED")
        with self.assertRaises((ValueError, RuntimeError)):
            self.store.claim(first["ticket_id"], "first")

    def test_active_expiry_blocks_new_claims_and_renew(self):
        first = self.claim()
        second = self.enqueue("second", "2")
        self.now += 30
        status = self.store.status()
        self.assertTrue(status["recovery_required"])
        self.assertEqual(status["active"]["phase"], "RECOVERY_REQUIRED")
        self.assertEqual(self.store.get(second["ticket_id"])["phase"], "QUEUED")
        with self.assertRaises((ValueError, RuntimeError)):
            self.store.claim(second["ticket_id"], "second")
        with self.assertRaises((ValueError, RuntimeError)):
            self.store.renew(first["ticket_id"], first["token"])
        with self.assertRaises((ValueError, RuntimeError)):
            self.store.validate(first["ticket_id"], first["token"])

    def test_fencing_and_epoch_survive_store_restart(self):
        first = self.claim()
        with self.assertRaises((ValueError, RuntimeError)):
            self.store.validate(first["ticket_id"], "wrong-token")
        self.store.complete(first["ticket_id"], first["token"])
        second = self.enqueue("owner", "next")
        restarted = queue.BrokerStore(self.temp.name, clock=lambda: self.now)
        second = restarted.claim(second["ticket_id"], "owner")
        self.assertGreater(second["epoch"], first["epoch"])
        self.assertNotEqual(second["token"], first["token"])
        with self.assertRaises((ValueError, RuntimeError)):
            restarted.validate(first["ticket_id"], first["token"])

    def test_events_are_durable_addressed_and_cursor_based(self):
        first = self.enqueue()
        self.enqueue("other", "2")
        events = self.store.events("owner")
        self.assertEqual([event["kind"] for event in events], ["QUEUED", "OFFERED"])
        self.assertTrue(all(event["ticket_id"] == first["ticket_id"] for event in events))
        restarted = queue.BrokerStore(self.temp.name, clock=lambda: self.now)
        self.assertEqual(restarted.events("owner"), events)
        self.assertEqual(restarted.events("owner", after=events[-1]["id"]), [])

    def test_cancel_is_owner_checked_idempotent_and_advances_offer(self):
        first = self.enqueue()
        second = self.enqueue("other", "2")
        with self.assertRaises((ValueError, RuntimeError)):
            self.store.cancel(first["ticket_id"], "other")
        self.store.cancel(first["ticket_id"], "owner")
        count = len(self.store.events("owner"))
        self.store.cancel(first["ticket_id"], "owner")
        self.assertEqual(len(self.store.events("owner")), count)
        self.assertEqual(self.store.get(second["ticket_id"])["phase"], "OFFERED")

    def test_active_cancel_cannot_free_editor(self):
        first = self.claim()
        with self.assertRaises((ValueError, RuntimeError)):
            self.store.cancel(first["ticket_id"], "owner")
        self.assertEqual(self.store.get(first["ticket_id"])["phase"], "LEASED")

    def test_renew_does_not_reorder_and_late_token_is_fenced(self):
        first = self.claim()
        second = self.enqueue("second", "2")
        self.now += 20
        self.store.renew(first["ticket_id"], first["token"])
        self.now += 20
        self.assertEqual(self.store.validate(first["ticket_id"], first["token"])["phase"], "LEASED")
        self.store.complete(first["ticket_id"], first["token"])
        self.assertEqual(self.store.get(second["ticket_id"])["phase"], "OFFERED")

    def test_transitions_preserve_journal_and_forbid_skipping_or_terminal_reentry(self):
        first = self.claim()
        with self.assertRaises((ValueError, RuntimeError)):
            self.store.transition(first["ticket_id"], first["token"], "RUNNING")
        first = self.store.transition(first["ticket_id"], first["token"], "SWITCHING", setup={"scenes": []})
        self.assertEqual(self.store.get(first["ticket_id"])["details"]["setup"], {"scenes": []})
        with self.assertRaises((ValueError, RuntimeError)):
            self.store.complete(first["ticket_id"], first["token"])
        for phase in ("RUNNING", "CAPTURING", "RESTORING"):
            self.store.transition(first["ticket_id"], first["token"], phase)
        result = self.store.complete(first["ticket_id"], first["token"], result_sha="R")
        self.assertEqual((result["phase"], result["result_status"]), ("DONE", "AVAILABLE"))
        with self.assertRaises((ValueError, RuntimeError)):
            self.store.transition(first["ticket_id"], first["token"], "LEASED")

    def test_failed_restore_preserves_result_and_recovery_can_finish(self):
        first = self.restore(self.claim())
        result = self.store.complete(first["ticket_id"], first["token"], result_sha="R", restored=False)
        self.assertEqual((result["phase"], result["result_sha"]), ("RECOVERY_REQUIRED", "R"))
        self.store.transition(first["ticket_id"], first["token"], "RESTORING")
        result = self.store.complete(first["ticket_id"], first["token"])
        self.assertEqual((result["phase"], result["result_sha"]), ("DONE", "R"))

    def test_ack_result_requires_owner_and_is_independent_of_lease(self):
        first = self.restore(self.claim())
        self.store.complete(first["ticket_id"], first["token"], result_sha="R")
        self.enqueue("other", "2")
        with self.assertRaises((ValueError, RuntimeError)):
            self.store.ack_result(first["ticket_id"], "other")
        result = self.store.ack_result(first["ticket_id"], "owner")
        self.assertEqual(result["result_status"], "IMPORTED")
        self.assertEqual(self.store.ack_result(first["ticket_id"], "owner"), result)

    def test_wait_returns_own_reservation_and_rejects_foreign_owner(self):
        first = self.enqueue()
        started = time.monotonic()
        result = self.store.wait(first["ticket_id"], "owner", timeout=0.1)
        self.assertEqual(result["phase"], "OFFERED")
        self.assertLess(time.monotonic() - started, 1)
        with self.assertRaises((ValueError, RuntimeError)):
            self.store.wait(first["ticket_id"], "other", timeout=0)
        second = self.enqueue("second", "2")
        self.assertEqual(self.store.wait(second["ticket_id"], "second", timeout=0)["phase"], "QUEUED")
        with self.assertRaises(ValueError):
            self.store.wait(second["ticket_id"], "second", timeout=61)

    @contextmanager
    def hold_database_lock(self, mode):
        ready, release = threading.Event(), threading.Event()
        errors = []

        def concurrent_connection():
            try:
                connection = sqlite3.connect(self.store.path, isolation_level=None)
                try:
                    connection.execute("BEGIN " + mode)
                    connection.execute("SELECT * FROM tickets").fetchall()
                    if mode == "IMMEDIATE":
                        connection.execute("UPDATE tickets SET details = ?", ('{"private":"uncommitted"}',))
                    ready.set()
                    release.wait(0.6)
                    connection.rollback()
                finally:
                    connection.close()
            except BaseException as error:
                errors.append(error)
                ready.set()

        worker = threading.Thread(target=concurrent_connection)
        worker.start()
        try:
            self.assertTrue(ready.wait(5), "Конкурирующее соединение не взяло SQLite lock")
            self.assertFalse(errors, repr(errors))
            yield
        finally:
            release.set()
            worker.join(5)
            self.assertFalse(worker.is_alive())
            self.assertFalse(errors, repr(errors))

    def test_wait_deadline_includes_concurrent_writer_lock(self):
        first = self.enqueue()
        for timeout in (0, 0.08):
            with self.subTest(timeout=timeout), self.hold_database_lock("IMMEDIATE"):
                started = time.monotonic()
                response = self.store.wait(first["id"], "owner", timeout=timeout)
                self.assertLess(time.monotonic() - started, timeout + 0.1)
                self.assertEqual(response["phase"], "BUSY")
                self.assertTrue(response["busy"])
                self.assertTrue(response["timed_out"])
                self.assertNotIn("uncommitted", repr(response))
                self.assertNotIn("token", response)

    def test_wait_deadline_includes_commit_blocked_by_concurrent_reader(self):
        first = self.enqueue()
        self.now += 10
        for timeout in (0, 0.08):
            with self.subTest(timeout=timeout), self.hold_database_lock(""):
                started = time.monotonic()
                response = self.store.wait(first["id"], "owner", timeout=timeout)
                self.assertLess(time.monotonic() - started, timeout + 0.1)
                self.assertEqual(response["phase"], "BUSY")
                self.assertTrue(response["blocked"])
        self.assertEqual(self.store.get(first["id"])["phase"], "CANCELLED")

    def test_wait_recognizes_python310_busy_error_without_hiding_other_database_errors(self):
        first = self.enqueue()
        with patch.object(self.store, "_transaction", side_effect=sqlite3.OperationalError("database is locked")):
            response = self.store.wait(first["id"], "owner", timeout=0)
        self.assertEqual(response["phase"], "BUSY")
        with patch.object(self.store, "_transaction", side_effect=sqlite3.OperationalError("no such table: tickets")):
            with self.assertRaises(sqlite3.OperationalError):
                self.store.wait(first["id"], "owner", timeout=0)

    def test_public_status_events_and_wait_do_not_disclose_token(self):
        first = self.claim()
        self.assertEqual(first["id"], first["ticket_id"])
        public = (self.store.status(), self.store.events("owner"),
                  self.store.wait(first["ticket_id"], "owner", timeout=0))
        for response in public:
            self.assertNotIn(first["token"], repr(response))
        self.assertEqual(self.store.get(first["id"])["token"], first["token"])

    def test_transition_shallow_merges_details_and_preserves_immutable_fields(self):
        first = self.claim()
        self.store.transition(first["id"], first["token"], "SWITCHING", setup={"scenes": ["one"]})
        ticket = self.store.transition(first["id"], first["token"], "RUNNING", report="ready")
        self.assertEqual(ticket["details"], {"setup": {"scenes": ["one"]}, "report": "ready"})
        self.assertEqual((ticket["owner"], ticket["base_sha"], ticket["input_sha"]), ("owner", "B", "A"))
        with self.assertRaises(ValueError):
            self.store.transition(first["id"], first["token"], "CAPTURING", input_sha="changed")
        self.assertEqual(self.store.get(first["id"])["phase"], "RUNNING")

    def test_same_active_phase_can_checkpoint_journal_without_changing_fence(self):
        first = self.claim()
        self.store.transition(first["id"], first["token"], "SWITCHING", setup={"scenes": []})
        updated = self.store.transition(first["id"], first["token"], "SWITCHING", disk_switched=True)
        self.assertEqual(updated["details"], {"setup": {"scenes": []}, "disk_switched": True})
        self.assertEqual((updated["token"], updated["epoch"]), (first["token"], first["epoch"]))
        restarted = queue.BrokerStore(self.temp.name, clock=lambda: self.now)
        self.assertEqual(restarted.get(first["id"])["details"], updated["details"])

    def test_failure_before_switch_can_enter_recovery_without_freeing_queue(self):
        first = self.claim()
        second = self.enqueue("next", "next")
        recovering = self.store.transition(first["id"], first["token"], "RECOVERY_REQUIRED", failure="dirty")
        self.assertEqual(recovering["phase"], "RECOVERY_REQUIRED")
        self.assertEqual(self.store.get(second["id"])["phase"], "QUEUED")
        self.store.transition(first["id"], first["token"], "RESTORING")
        self.store.complete(first["id"], first["token"])
        self.assertEqual(self.store.get(second["id"])["phase"], "OFFERED")

    def test_rejected_expired_validate_durably_enters_recovery_and_retains_fence(self):
        first = self.claim()
        self.now += 30
        with self.assertRaises((ValueError, RuntimeError)):
            self.store.validate(first["id"], first["token"])
        restarted = queue.BrokerStore(self.temp.name, clock=lambda: self.now)
        recovering = restarted.get(first["id"])
        self.assertEqual(recovering["phase"], "RECOVERY_REQUIRED")
        self.assertEqual((recovering["token"], recovering["epoch"]), (first["token"], first["epoch"]))
        with self.assertRaises((ValueError, RuntimeError)):
            restarted.complete(first["id"], first["token"])
        restarted.transition(first["id"], first["token"], "RESTORING")
        self.assertEqual(restarted.complete(first["id"], first["token"])["phase"], "DONE")

    def test_result_cannot_be_acknowledged_before_verified_restore(self):
        first = self.restore(self.claim())
        self.store.complete(first["id"], first["token"], result_sha="R", restored=False)
        with self.assertRaises((ValueError, RuntimeError)):
            self.store.ack_result(first["id"], "owner")
        self.store.transition(first["id"], first["token"], "RESTORING")
        completed = self.store.complete(first["id"], first["token"])
        self.assertEqual(completed["result_sha"], "R")
        self.assertEqual(self.store.ack_result(first["id"], "owner")["result_status"], "IMPORTED")

    def run_workers(self, function, args):
        ctx = multiprocessing.get_context("spawn")
        start, results = ctx.Event(), ctx.Queue()
        processes = [ctx.Process(target=function, args=(self.temp.name, start, results, arg)) for arg in args]
        for process in processes:
            process.start()
        start.set()
        outcomes = [results.get(timeout=15) for _ in processes]
        for process in processes:
            process.join(10)
            if process.is_alive():
                process.terminate()
                process.join(5)
            self.assertEqual(process.exitcode, 0)
        results.close()
        return outcomes

    def test_multiprocess_enqueue_has_unique_order_and_single_offer(self):
        tickets = self.run_workers(enqueue_worker, range(6))
        self.assertEqual(len(set(tickets)), 6)
        state = queue.BrokerStore(self.temp.name).status()
        self.assertEqual([ticket["phase"] for ticket in state["tickets"]], ["OFFERED"] + ["QUEUED"] * 5)
        self.assertEqual(state["offered"]["ticket_id"], min(tickets))

    def test_multiprocess_claim_has_exactly_one_winner(self):
        ticket = queue.BrokerStore(self.temp.name).enqueue("owner", "B", "A", "root", ["Assets"], "key")
        outcomes = self.run_workers(claim_worker, [ticket["ticket_id"]] * 4)
        self.assertEqual([status for status, _ in outcomes].count("ok"), 1)


if __name__ == "__main__":
    unittest.main()
