"""Контракт контроллера: настоящий Git, подменён только отдельный Unity Editor."""
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from editor_broker.git_state import GitState
from editor_broker.queue import BrokerStore
from editor_broker.service import EditorBroker, BrokerError, process_alive, default_state_dir


class FakeUnity:
    def __init__(self, root):
        self.root = Path(root)
        self.busy = False
        self.dirty = False
        self.fail_refresh = False
        self.calls = []
        self.session = "fixture"
        self.parked = False

    def inspect(self):
        return {"project_root": str(self.root), "ready": not self.busy,
                "is_playing": self.busy, "is_compiling": False, "is_updating": False,
                "is_test_running": False, "dirty_scenes": ["foreign"] if self.dirty else [],
                "dirty_assets": [], "prefab_stage": None, "process_id": 0, "session_id": self.session,
                "auto_refresh_suppressed": self.parked,
                "scenes": [{"path": "Assets/Base.unity", "is_active": True, "is_loaded": True}],
                "prefab_path": None}

    def park(self):
        # Как мост: повторная парковка без refresh запрещена.
        if self.parked:
            raise RuntimeError("Editor уже припаркован")
        self.parked = True
        self.calls.append("park")
        return {"scenes": [{"path": "Assets/Base.unity", "is_active": True, "is_loaded": True}],
                "prefab_path": None}

    def refresh(self):
        self.calls.append("refresh")
        self.parked = False
        if self.fail_refresh:
            raise TimeoutError("editor did not acknowledge refresh")
        return self.inspect()

    def save_outputs(self, output_roots):
        self.calls.append("save_outputs")
        return self.inspect()

    def restore(self, setup):
        self.calls.append("restore")
        return self.inspect()


class EditorBrokerTests(unittest.TestCase):
    def test_initialize_refuses_tracked_meta_without_versioned_owner(self):
        orphan = self.editor / "Assets/ignored.asset.meta"
        orphan.write_text("fileFormatVersion: 2\nguid: ignored-owner\n", encoding="utf-8")
        self.git(self.editor, "add", "Assets/ignored.asset.meta")
        self.git(self.editor, "commit", "-qm", "broken asset pair")
        broken_base = GitState(self.editor).head()
        new_state = Path(self.tmp.name) / "uninitialized-controller"
        with self.assertRaises(BrokerError):
            EditorBroker.initialize(self.editor, new_state, broken_base, install=False)
        self.assertFalse((new_state / "config.json").exists())

    def test_default_state_is_persistent_and_shared_across_worktrees(self):
        expected = self.repo / ".agent-state" / "editor-broker"
        self.assertEqual(default_state_dir(self.repo), expected)
        self.assertEqual(default_state_dir(self.editor), expected)
        self.assertEqual(default_state_dir(self.agent), expected)

    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.repo = self.root / "repo"
        self.repo.mkdir()
        self.git(self.repo, "init", "-q")
        self.git(self.repo, "config", "user.name", "Broker fixture")
        self.git(self.repo, "config", "user.email", "broker@example.invalid")
        (self.repo / "Assets").mkdir()
        (self.repo / "Assets/Base.unity").write_text("base scene\n", encoding="utf-8")
        (self.repo / ".gitignore").write_text("/Library/\n/Temp/\n/tmp/\n", encoding="utf-8")
        self.git(self.repo, "add", ".")
        self.git(self.repo, "commit", "-qm", "baseline")
        self.base = self.git(self.repo, "rev-parse", "HEAD")
        self.editor = self.root / "editor"
        self.agent = self.root / "agent"
        self.git(self.repo, "worktree", "add", "--detach", str(self.editor), self.base)
        self.git(self.repo, "worktree", "add", "--detach", str(self.agent), self.base)
        self.state = self.root / "coordination"
        EditorBroker.initialize(self.editor, self.state, self.base, install=False)
        self.unity = FakeUnity(self.editor)
        self.broker = EditorBroker(self.state, adapter=self.unity)
        (self.agent / "Assets/Generator.cs").write_text("generator\n", encoding="utf-8")
        (self.agent / "Assets/Generator.cs.meta").write_text("guid: generator\n", encoding="utf-8")
        self.input = GitState(self.agent).checkpoint(self.state, "fixture-input")["sha"]

    @staticmethod
    def git(root, *args):
        result = subprocess.run(["git", "--no-pager", "-C", str(root), *args],
                                capture_output=True, text=True, encoding="utf-8")
        if result.returncode:
            raise AssertionError(result.stderr)
        return result.stdout.strip()

    def claim(self, owner="agent", key="job"):
        ticket = self.broker.request(owner, self.base, self.input, self.agent,
                                     ["Assets/Generated"], key)
        return self.broker.store.claim(ticket["id"], owner)

    def test_recover_after_cleanup_park_does_not_park_again(self):
        ticket = self.claim()
        self.broker.begin(ticket["id"], ticket["token"])
        # Сбой после cleanup_park: Editor припаркован, worker ещё на входе агента.
        self.unity.park()
        self.broker.store.transition(ticket["id"], ticket["token"], "RECOVERY_REQUIRED")
        done = self.broker.recover(ticket["id"], ticket["token"])
        self.assertEqual(done["phase"], "DONE")
        self.assertFalse(self.unity.parked)
        self.assertEqual(GitState(self.editor).head(), self.base)

    def test_bidirectional_generated_binary_and_meta_then_clean_baseline(self):
        ticket = self.claim()
        self.broker.begin(ticket["id"], ticket["token"])
        self.assertEqual(GitState(self.editor).head(), self.input)
        generated = self.editor / "Assets/Generated"
        generated.mkdir()
        (generated / "mesh.asset").write_bytes(b"\x00mesh\xff")
        (generated / "mesh.asset.meta").write_text("guid: durable-result\n", encoding="utf-8")
        (self.editor / "Assets/Generated.meta").write_text("guid: generated-folder\n", encoding="utf-8")
        result = self.broker.finish(ticket["id"], ticket["token"])
        self.assertTrue(result["accepted"])
        self.assertEqual(GitState(self.editor).head(), self.base)
        self.assertTrue(GitState(self.editor).is_clean())
        self.assertFalse(generated.exists())
        self.broker.receive(ticket["id"], "agent", self.agent)
        self.assertEqual((self.agent / "Assets/Generated/mesh.asset").read_bytes(), b"\x00mesh\xff")
        self.assertIn("durable-result", (self.agent / "Assets/Generated/mesh.asset.meta").read_text())
        self.assertEqual(GitState(self.agent).head(), self.base)
        self.assertEqual(self.broker.store.get(ticket["id"])["phase"], "DONE")

    def test_dirty_human_scene_refuses_before_git_switch(self):
        ticket = self.claim()
        self.unity.dirty = True
        with self.assertRaises(Exception):
            self.broker.begin(ticket["id"], ticket["token"])
        self.assertEqual(GitState(self.editor).head(), self.base)
        self.assertNotIn("park", self.unity.calls)

    def test_busy_editor_refuses_before_git_switch(self):
        ticket = self.claim()
        self.unity.busy = True
        with self.assertRaises(Exception):
            self.broker.begin(ticket["id"], ticket["token"])
        self.assertEqual(GitState(self.editor).head(), self.base)

    def test_unexpected_output_is_pinned_but_not_delivered(self):
        ticket = self.claim()
        self.broker.begin(ticket["id"], ticket["token"])
        (self.editor / "Assets/Base.unity").write_text("unexpected rewrite\n", encoding="utf-8")
        result = self.broker.finish(ticket["id"], ticket["token"])
        self.assertFalse(result["accepted"])
        self.assertIn("Assets/Base.unity", result["unexpected"])
        self.assertEqual(GitState(self.editor).head(), self.base)
        self.assertIn("unexpected rewrite", self.git(self.editor, "show", result["sha"] + ":Assets/Base.unity"))
        with self.assertRaises(Exception):
            self.broker.receive(ticket["id"], "agent", self.agent)

    def test_timeout_blocks_queue_until_explicit_verified_recovery(self):
        ticket = self.claim()
        self.unity.fail_refresh = True
        with self.assertRaises(Exception):
            self.broker.begin(ticket["id"], ticket["token"])
        self.assertEqual(self.broker.store.get(ticket["id"])["phase"], "RECOVERY_REQUIRED")
        next_ticket = self.broker.request("next", self.base, self.input, self.agent,
                                          ["Assets/Generated"], "next")
        with self.assertRaises(Exception):
            self.broker.store.claim(next_ticket["id"], "next")
        self.unity.fail_refresh = False
        self.broker.recover(ticket["id"], ticket["token"])
        self.assertEqual(GitState(self.editor).head(), self.base)
        self.assertTrue(GitState(self.editor).is_clean())
        self.assertEqual(self.broker.store.claim(next_ticket["id"], "next")["phase"], "LEASED")

    def test_stale_baseline_and_unrelated_repo_are_rejected(self):
        with self.assertRaises(Exception):
            self.broker.request("agent", "0" * 40, self.input, self.agent, ["Assets"], "stale")
        with self.assertRaises(Exception):
            self.broker.request("agent", self.base, self.input, self.root, ["Assets"], "foreign")

    def test_wrong_token_cannot_switch_or_capture(self):
        ticket = self.claim()
        with self.assertRaises(Exception):
            self.broker.begin(ticket["id"], "wrong-token")
        self.assertEqual(GitState(self.editor).head(), self.base)

    def test_dead_editor_disk_recovery_preserves_result_but_keeps_queue_blocked(self):
        ticket = self.claim()
        self.broker.begin(ticket["id"], ticket["token"])
        generated = self.editor / "Assets/Generated"
        generated.mkdir()
        (generated / "mesh.asset").write_bytes(b"durable crash output")
        child = subprocess.Popen([sys.executable, "-c", "pass"])
        child.wait(timeout=10)
        self.broker.store.transition(ticket["id"], ticket["token"], "RECOVERY_REQUIRED",
                                    process_id=child.pid)
        result = self.broker.recover_stopped(ticket["id"], ticket["token"])
        self.assertTrue(result["disk_restored"])
        self.assertFalse(result["editor_released"])
        self.assertFalse(result["result"]["accepted"])
        self.assertEqual(GitState(self.editor).head(), self.base)
        self.assertTrue(GitState(self.editor).is_clean())
        self.assertEqual(self.broker.store.get(ticket["id"])["phase"], "RECOVERY_REQUIRED")
        self.assertIn("durable crash output", self.git(self.editor, "show",
            result["result"]["sha"] + ":Assets/Generated/mesh.asset"))
        self.broker.recover(ticket["id"], ticket["token"])
        self.assertEqual(self.broker.store.get(ticket["id"])["phase"], "DONE")

    def test_live_pid_cannot_be_claimed_stopped(self):
        import os
        self.assertTrue(process_alive(os.getpid()))
        ticket = self.claim()
        self.broker.begin(ticket["id"], ticket["token"])
        self.broker.store.transition(ticket["id"], ticket["token"], "RECOVERY_REQUIRED",
                                    process_id=os.getpid())
        with self.assertRaises(Exception):
            self.broker.recover_stopped(ticket["id"], ticket["token"])
        self.assertEqual(GitState(self.editor).head(), self.input)

    def test_report_artifact_is_copied_before_restore(self):
        ticket = self.claim()
        self.broker.begin(ticket["id"], ticket["token"])
        report = self.editor / "tmp/report.json"
        report.parent.mkdir()
        report.write_text('{"passed":true}', encoding="utf-8")
        result = self.broker.finish(ticket["id"], ticket["token"], ["tmp/report.json"])
        artifact = result["artifacts"][0]
        self.assertEqual(Path(artifact["stored_path"]).read_text(), '{"passed":true}')
        self.assertEqual(len(artifact["sha256"]), 64)

    def test_guard_allows_cleanup_of_own_play_but_finish_stays_blocked(self):
        ticket = self.claim()
        self.broker.begin(ticket["id"], ticket["token"])
        self.unity.busy = True
        result = self.broker.guard(ticket["id"], ticket["token"])
        self.assertTrue(result["is_playing"])
        self.assertEqual(result["input_sha"], self.input)
        with self.assertRaises(Exception):
            self.broker.finish(ticket["id"], ticket["token"])
        self.assertEqual(GitState(self.editor).head(), self.input)

    def test_restarted_editor_session_requires_recovery(self):
        ticket = self.claim()
        self.broker.begin(ticket["id"], ticket["token"])
        self.unity.session = "restarted"
        with self.assertRaises(Exception):
            self.broker.guard(ticket["id"], ticket["token"])
        self.assertEqual(self.broker.store.get(ticket["id"])["phase"], "RECOVERY_REQUIRED")

    def test_publish_accepted_base_and_reject_old_requests(self):
        result = self.broker.publish_base(self.input, self.agent)
        self.assertEqual(result["baseline_sha"], self.input)
        self.assertEqual(GitState(self.editor).head(), self.input)
        self.assertTrue(GitState(self.editor).is_clean())
        with self.assertRaises(Exception):
            self.broker.request("old", self.base, self.input, self.agent, ["Assets"], "old")
        ticket = self.broker.request("new", self.input, self.input, self.agent, ["Assets"], "new")
        self.assertEqual(ticket["phase"], "OFFERED")

    def test_publish_does_not_bypass_queued_work(self):
        self.claim()
        with self.assertRaises(Exception):
            self.broker.publish_base(self.input, self.agent)
        self.assertEqual(GitState(self.editor).head(), self.base)

    def test_publish_runs_during_maintenance_pause_and_keeps_it(self):
        self.broker.store.set_paused(True)
        result = self.broker.publish_base(self.input, self.agent)
        self.assertEqual(result["baseline_sha"], self.input)
        self.assertTrue(self.broker.store.is_paused())
        phases = [ticket["phase"] for ticket in self.broker.store.status()["tickets"]]
        self.assertEqual(phases, ["DONE"])

    def test_refused_publish_leaves_no_maintenance_ticket(self):
        waiting = self.broker.request("agent", self.base, self.input, self.agent, [], "waiting")
        self.broker.store.set_paused(True)
        with self.assertRaises(Exception):
            self.broker.publish_base(self.input, self.agent)
        owners = [ticket["owner"] for ticket in self.broker.store.status()["tickets"]]
        self.assertEqual(owners, ["agent"])
        self.assertEqual(self.broker.store.get(waiting["id"])["phase"], "QUEUED")

    def test_hard_stop_after_park_restores_durable_original_setup(self):
        ticket = self.claim()
        park = self.unity.park
        def crash_after_park():
            park()
            raise SystemExit("controller killed after park response")
        self.unity.park = crash_after_park
        with self.assertRaises(SystemExit):
            self.broker.begin(ticket["id"], ticket["token"])
        journal = self.broker.store.get(ticket["id"])
        self.assertEqual(journal["phase"], "SWITCHING")
        self.assertIsNotNone(journal["details"]["original_setup"])
        self.unity.park = park
        self.broker.recover(ticket["id"], ticket["token"])
        self.assertIn("restore", self.unity.calls)
        self.assertEqual(self.broker.store.get(ticket["id"])["phase"], "DONE")

    def test_recovery_keeps_original_result_after_partial_rollback(self):
        ticket = self.claim()
        self.broker.begin(ticket["id"], ticket["token"])
        generated = self.editor / "Assets/Generated"
        generated.mkdir()
        asset = generated / "mesh.asset"
        asset.write_bytes(b"complete generated asset")
        (generated / "mesh.asset.meta").write_text("guid: preserved-output\n", encoding="utf-8")
        restore = self.broker.git.restore_captured
        def partial_restore(*args):
            asset.unlink()
            raise OSError("disk failure halfway through restoration")
        self.broker.git.restore_captured = partial_restore
        with self.assertRaises(OSError):
            self.broker.finish(ticket["id"], ticket["token"])
        saved = self.broker.store.get(ticket["id"])["details"]["result"]
        self.assertTrue(saved["accepted"])
        self.broker.git.restore_captured = restore
        self.broker.recover(ticket["id"], ticket["token"])
        done = self.broker.store.get(ticket["id"])
        self.assertEqual(done["result_sha"], saved["sha"])
        self.assertEqual(done["details"]["result"]["sha"], saved["sha"])
        self.broker.receive(ticket["id"], "agent", self.agent)
        self.assertEqual((self.agent / "Assets/Generated/mesh.asset").read_bytes(), b"complete generated asset")

    def test_request_during_publish_cannot_enqueue_old_base(self):
        caught = []
        park = self.unity.park
        def racing_request():
            try:
                self.broker.request("racer", self.base, self.input, self.agent, ["Assets"], "race")
            except Exception as error:
                caught.append(str(error))
            return park()
        self.unity.park = racing_request
        self.broker.publish_base(self.input, self.agent)
        self.assertTrue(caught)
        self.assertFalse(any(ticket["owner"] == "racer" for ticket in self.broker.store.status()["tickets"]))
        fresh = EditorBroker(self.state, adapter=self.unity)
        with self.assertRaises(Exception):
            fresh.request("racer", self.base, self.input, self.agent, ["Assets"], "after")


if __name__ == "__main__":
    unittest.main()
