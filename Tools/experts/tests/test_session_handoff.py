"""История сессий после плоского переноса: собственные cwd без соседних задач."""

import importlib.util
import json
from pathlib import Path
import tempfile
import unittest


SCRIPT = Path(__file__).resolve().parents[1] / "session_handoff.py"
SPEC = importlib.util.spec_from_file_location("session_handoff", SCRIPT)
handoff = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(handoff)


class SessionHandoffTests(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory()
        self.addCleanup(self.tmp.cleanup)
        self.root = Path(self.tmp.name)
        self.worktrees = self.root / "worktrees"
        self.codex = self.root / "sessions"
        self.claude = self.root / "claude"
        self.codex.mkdir()
        self.claude.mkdir()

    def session(self, name, cwd, moved_to=None):
        records = [{"type": "session_meta", "timestamp": "2026-10-18T10:00:00Z",
                    "payload": {"cwd": str(cwd), "thread_source": "user"}}]
        if moved_to is not None:
            records.append({"type": "turn_context", "timestamp": "2026-10-18T10:01:00Z",
                            "payload": {"cwd": str(moved_to)}})
        path = self.codex / (name + ".jsonl")
        path.write_text("".join(json.dumps(record, separators=(",", ":")) + "\n" for record in records),
                        encoding="utf-8")
        return path

    def selected(self, target):
        return {path.name for _, _, path in handoff.candidates(
            self.codex, self.claude, handoff.norm(target), target, days=1)}

    def test_flat_task_selects_own_and_legacy_sessions_without_shared_root_or_neighbors(self):
        target = self.worktrees / "my-task"
        self.session("own-flat", target)
        self.session("own-legacy", target / "Vr_Battlegrounds_ai")
        self.session("shared-root", self.worktrees)
        self.session("neighbor", self.worktrees / "other-task")
        self.session("neighbor-legacy", self.worktrees / "other-task" / "Vr_Battlegrounds_ai")

        self.assertEqual(self.selected(target), {"own-flat.jsonl", "own-legacy.jsonl"})

    def test_project_named_task_does_not_treat_shared_root_as_its_previous_cwd(self):
        target = self.worktrees / "Vr_Battlegrounds_ai"
        self.session("own-flat", target)
        self.session("own-legacy", target / "Vr_Battlegrounds_ai")
        self.session("shared-root", self.worktrees)
        self.session("neighbor", self.worktrees / "other-task")

        self.assertEqual(self.selected(target), {"own-flat.jsonl", "own-legacy.jsonl"})

    def test_session_started_elsewhere_is_found_after_turn_context_enters_own_legacy_cwd(self):
        target = self.worktrees / "my-task"
        elsewhere = self.root / "start"
        self.session("moved-here", elsewhere, moved_to=target / "Vr_Battlegrounds_ai")
        self.session("moved-neighbor", elsewhere,
                     moved_to=self.worktrees / "other-task" / "Vr_Battlegrounds_ai")
        self.session("moved-shared", elsewhere, moved_to=self.worktrees)

        self.assertEqual(self.selected(target), {"moved-here.jsonl"})


if __name__ == "__main__":
    unittest.main()
