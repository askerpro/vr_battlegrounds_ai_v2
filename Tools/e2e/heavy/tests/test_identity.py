"""Сверка кода worker с файлами ветки, распаковка ответов MCP и код 2 при сбое самого heavy."""
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
from types import SimpleNamespace
import unittest

from heavy import cli
from heavy import freshness as F
from heavy import verdict as V
from heavy.unity_mcp import unpack_envelope


def reply(payload, error=False):
    return SimpleNamespace(isError=error, content=[SimpleNamespace(text=json.dumps(payload))])


class EnvelopeTests(unittest.TestCase):
    def test_job_result_field_is_not_unwrapped(self):
        # Форма get_test_job (аренда 894): у job своё поле result, во время прогона — null.
        job = {"job_id": "j", "status": "running", "progress": {"completed": 5}, "result": None}
        self.assertEqual(unpack_envelope(reply({"result": {"success": True, "message": "m", "data": job}})), job)

    def test_failure_envelope_raises(self):
        with self.assertRaisesRegex(RuntimeError, "нет аренды"):
            unpack_envelope(reply({"success": False, "error": "нет аренды"}))
        with self.assertRaises(RuntimeError):
            unpack_envelope(reply({"x": 1}, error=True))


class RepoTests(unittest.TestCase):
    def setUp(self):
        self.root = Path(tempfile.mkdtemp())
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)
        self.git("init", "-q")
        self.git("config", "user.email", "t@t")
        self.git("config", "user.name", "t")
        self.git("config", "core.autocrlf", "false")
        (self.root / "Assets").mkdir()
        (self.root / "Assets" / "a.cs").write_text("a\n", encoding="utf-8")
        (self.root / "Docs").mkdir()
        (self.root / "Docs" / "d.md").write_text("d\n", encoding="utf-8")
        self.git("add", "-A")
        self.git("commit", "-q", "-m", "c")
        self.head = self.git("rev-parse", "HEAD").stdout.strip()

    def git(self, *args, env=None):
        import os
        return subprocess.run(["git", *args], cwd=self.root, capture_output=True, text=True,
                              env=dict(os.environ, **env) if env else None)

    def identity(self):
        tree, error = F.snapshot_tree(self.git, self.root / "snap.index")
        self.assertIsNone(error)
        return F.code_identity(self.git, self.head, tree)

    def test_same_files_match(self):
        self.assertEqual(self.identity(), {"known": True, "paths": []})

    def test_docs_change_is_ignored_game_change_is_reported(self):
        (self.root / "Docs" / "d.md").write_text("changed\n", encoding="utf-8")
        self.assertEqual(self.identity()["paths"], [])
        (self.root / "Assets" / "b.cs").write_text("new\n", encoding="utf-8")
        info = self.identity()
        self.assertEqual(info["paths"], ["Assets/b.cs"])
        self.assertEqual(F.identity_check(info, self.head)["verdict"], V.INCONCLUSIVE)

    def test_snapshot_does_not_touch_real_index(self):
        (self.root / "Assets" / "b.cs").write_text("new\n", encoding="utf-8")
        self.identity()
        self.assertEqual(self.git("diff", "--cached", "--name-only").stdout, "")


class ExitCodeTests(unittest.TestCase):
    def test_report_dir_inside_git_is_refused_with_code_2_and_not_created(self):
        target = cli.PROJECT_ROOT / "Docs" / "heavy-unittest-not-ignored"
        self.assertEqual(cli.main(["--output-dir", str(target), "--no-fetch"]), 2)
        self.assertFalse(target.exists())

    def test_unexpected_failure_is_code_2(self):
        original = cli.run
        cli.run = lambda arguments: (_ for _ in ()).throw(OSError("git не найден"))
        try:
            self.assertEqual(cli.main([]), 2)
        finally:
            cli.run = original


if __name__ == "__main__":
    unittest.main()
