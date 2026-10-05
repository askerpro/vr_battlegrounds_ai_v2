"""Файловые клиенты: реальный Git; подменяется только снимок процессов/ответ Unity."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "agents"))
from editor_broker import client_guard as guard


class ClientGuardTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.main = Path(self.tmp.name) / "main"
        self.main.mkdir()
        self.git("init", "-q")
        self.git("-c", "user.name=fixture", "-c", "user.email=fixture@example.invalid",
                 "-c", "core.hooksPath=", "commit", "--allow-empty", "-qm", "fixture")
        self.agent = self.main.parent / "agent"
        self.worker = self.main.parent / "worker"
        self.git("worktree", "add", "--detach", str(self.agent), "HEAD")
        self.git("worktree", "add", "--detach", str(self.worker), "HEAD")
        self.state = self.main / ".agent-state/editor-broker"
        self.state.mkdir(parents=True)
        (self.state / "config.json").write_text(json.dumps({
            "editor_root": str(self.worker), "common_dir": str(self.main / ".git")}))
        self.real_inventory = guard.unity_project_roots
        self.processes = patch.object(guard, "unity_project_roots", return_value=[])
        self.processes.start()
        self.addCleanup(self.processes.stop)

    def git(self, *args):
        return subprocess.run(["git", "--no-pager", *args], cwd=self.main,
                              check=True, capture_output=True)

    def test_main_checkout_is_never_file_write_target(self):
        with self.assertRaises(ValueError):
            guard.ClientGuard(self.main).check()

    def test_separate_git_dir_main_is_not_a_linked_worktree(self):
        root = self.main.parent / "separate-main"
        root.mkdir()
        subprocess.run(["git", "--no-pager", "init", "-q", "--separate-git-dir",
                        str(self.main.parent / "separate-git"), str(root)], check=True, capture_output=True)
        with self.assertRaises(ValueError):
            guard.ClientGuard(root).check()

    def test_offline_linked_agent_needs_no_lease(self):
        self.assertEqual(guard.ClientGuard(self.agent).check()["mode"], "isolated")

    def test_live_agent_without_broker_lease_is_refused(self):
        with patch.object(guard, "unity_project_roots", return_value=[self.agent]):
            with self.assertRaises(ValueError):
                guard.ClientGuard(self.agent).check()

    def test_unavailable_process_identity_fails_closed(self):
        with patch.object(guard, "unity_project_roots", side_effect=ValueError("unknown")):
            with self.assertRaises(ValueError):
                guard.ClientGuard(self.agent).check()

    @unittest.skipUnless(os.name == "nt", "Windows CIM boundary")
    def test_disappeared_pid_can_be_skipped_only_after_exit_proof(self):
        with patch.object(guard, "unity_process_rows", return_value=[{"ProcessId": 7, "CommandLine": None}]), \
                patch.object(guard, "process_alive", return_value=False):
            self.assertEqual(self.real_inventory(), [])

    @unittest.skipUnless(os.name == "nt", "Windows CIM boundary")
    def test_unknown_live_pid_fails_after_bounded_retries(self):
        with patch.object(guard, "unity_process_rows", return_value=[{"ProcessId": 7, "CommandLine": None}]), \
                patch.object(guard, "process_alive", return_value=True):
            with self.assertRaises(ValueError):
                self.real_inventory()

    @unittest.skipUnless(os.name == "nt", "Windows CIM boundary")
    def test_short_retry_can_recover_project_identity(self):
        rows = [[{"ProcessId": 7, "CommandLine": None}],
                [{"ProcessId": 7, "CommandLine": 'Unity.exe -projectPath "' + str(self.agent) + '"'}]]
        with patch.object(guard, "unity_process_rows", side_effect=rows), \
                patch.object(guard, "process_alive", return_value=True):
            self.assertEqual(self.real_inventory(), [self.agent.resolve()])

    def test_registered_worker_needs_ticket_and_token(self):
        for kwargs in ({}, {"ticket": "1"}, {"token": "secret"}):
            with self.subTest(kwargs=kwargs), self.assertRaises(ValueError):
                guard.ClientGuard(self.worker, **kwargs).check()

    def test_guard_reply_must_match_exact_target_root(self):
        with patch.object(guard, "broker_command", return_value={"project_root": str(self.agent)}):
            with self.assertRaises(ValueError):
                guard.ClientGuard(self.worker, ticket="1", token="secret").check()

    def test_valid_worker_guard_grants_lease_mode(self):
        with patch.object(guard, "broker_command", return_value={"project_root": str(self.worker)}):
            self.assertEqual(guard.ClientGuard(self.worker, ticket="1", token="secret").check()["mode"], "lease")

    def test_real_broker_rejects_nonexistent_capability(self):
        with self.assertRaisesRegex(ValueError, "отклонил"):
            guard.ClientGuard(self.worker, ticket="1", token="foreign").check()

    def test_alternate_state_cannot_bypass_registered_controller(self):
        alternate = self.main.parent / "fake-controller"
        alternate.mkdir()
        (alternate / "config.json").write_bytes((self.state / "config.json").read_bytes())
        with patch.object(guard, "broker_command", return_value={"active": None, "offered": None, "tickets": []}):
            with self.assertRaises(ValueError):
                guard.ClientGuard(self.worker, offline_editor=True, state_dir=alternate).check()

    def test_lease_flags_cannot_be_used_on_agent_root(self):
        with self.assertRaises(ValueError):
            guard.ClientGuard(self.agent, ticket="1", token="secret").check()

    def test_closed_worker_bootstrap_requires_idle_controller(self):
        with patch.object(guard, "broker_command", return_value={"active": None, "offered": None, "tickets": []}):
            self.assertEqual(guard.ClientGuard(self.worker, offline_editor=True).check()["mode"], "bootstrap")
        with patch.object(guard, "broker_command", return_value={"active": {"phase": "RUNNING"}}):
            with self.assertRaises(ValueError):
                guard.ClientGuard(self.worker, offline_editor=True).check()

    def test_live_worker_is_refused_even_with_offline_switch(self):
        with patch.object(guard, "unity_project_roots", return_value=[self.worker]):
            with self.assertRaises(ValueError):
                guard.ClientGuard(self.worker, offline_editor=True).check()

    def test_offline_switch_and_capability_are_mutually_exclusive(self):
        with self.assertRaises(ValueError):
            guard.ClientGuard(self.worker, offline_editor=True, ticket="1", token="secret").check()

    def test_project_path_parser_preserves_windows_spaces_and_case(self):
        value = guard.project_path_from_command('"C:\\Unity\\Unity.exe" -projectPath "C:\\Projects\\My Game" -batchmode')
        self.assertEqual(str(value), str(Path("C:/Projects/My Game").resolve()))
        for command in ("Unity.exe", "Unity.exe -projectPath", "Unity.exe -projectPath relative"):
            with self.subTest(command=command), self.assertRaises(ValueError):
                guard.project_path_from_command(command)


if __name__ == "__main__":
    unittest.main()
