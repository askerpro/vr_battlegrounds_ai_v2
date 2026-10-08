"""Таймаут после первого ответа не должен считаться успехом обмена."""
import contextlib
import importlib.util
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, "F:/UnityProjects/agent-infra")
from agent_coordination.store import CoordinationStore

spec = importlib.util.spec_from_file_location("codex_probe", Path(__file__).with_name("probe_codex_live.py"))
probe = importlib.util.module_from_spec(spec)
spec.loader.exec_module(probe)


class ProbeTests(unittest.TestCase):
    def test_timeout_after_first_turn_is_failure(self):
        with tempfile.TemporaryDirectory() as temporary:
            root = Path(temporary)
            state = root / "state"
            store = CoordinationStore(state)
            for name in ("codex-probe", "claude-probe"):
                store.register({"task_id": name, "owner": name + "-owner", "client": "codex",
                    "worktree": str(root / name), "base_sha": "a" * 40,
                    "doc_path": "tasks/" + name + "/Readme.md", "title": name,
                    "stages": [{"id": "probe", "writes": ["tasks/" + name + "/**"], "after": [], "needs": []}]}, 0)
            event = store.send_message("claude-probe", "claude-probe-owner", "codex-probe",
                "pilot_request", json.dumps({"nonce": "test-nonce"}))
            literal = "EVENT_ACK_" + str(event["id"]) + "_test-nonce"
            responses = [
                {"id": 1, "result": {}},
                {"id": 2, "result": {"thread": {"id": "test-thread"}}},
                {"id": 3, "result": {"turn": {"id": "test-turn"}}},
                {"method": "item/completed", "params": {"item": {"type": "agentMessage", "text": literal}}},
                {"method": "turn/completed", "params": {"turn": {"id": "test-turn", "status": "completed"}}},
            ]

            class Process:
                stdin = io.StringIO()
                stdout = io.StringIO("\n".join(json.dumps(r) for r in responses) + "\n")
                stderr = io.StringIO()
                returncode = 0

                def poll(self):
                    return None

                def wait(self, timeout=None):
                    return 0

            clock = iter(range(1000))
            report = root / "report.json"
            argv = ["probe", "--executable", "unused", "--report", str(report),
                    "--exchange-state", str(state), "--notify-file", str(root / "notification.json")]
            with patch.object(sys, "argv", argv), patch.object(probe.subprocess, "Popen", return_value=Process()), \
                    patch.object(probe.time, "monotonic", side_effect=lambda: next(clock)), \
                    patch.object(probe.time, "sleep"), contextlib.redirect_stdout(io.StringIO()):
                result = probe.main()
            data = json.loads(report.read_text(encoding="utf-8"))
            self.assertEqual(data["model_turns_started"], 1)
            self.assertEqual(data["error"], "Claude final event not received")
            self.assertFalse(data["ok"])
            self.assertEqual(result, 1)


if __name__ == "__main__":
    unittest.main()
