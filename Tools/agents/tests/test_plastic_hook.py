"""Регрессии post-commit: настоящий Git, подменён только внешний Plastic CLI."""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[3]
BASH = shutil.which("bash") or "C:/Program Files/Git/bin/bash.exe"


class PlasticHookTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.repo = Path(self.temp.name)
        self.git("init", "-q")
        self.git("config", "user.name", "Hook Test")
        self.git("config", "user.email", "hook@example.invalid")
        self.git("config", "core.hooksPath", "disabled-hooks")
        self.git("config", "core.autocrlf", "false")
        (self.repo / "Assets").mkdir()
        (self.repo / "Assets/base.txt").write_text("base\n")
        (self.repo / "notes.md").write_text("notes\n")
        self.git("add", ".")
        self.git("commit", "-qm", "base")
        self.bin = self.repo / "fake-bin"
        self.bin.mkdir()
        (self.bin / "cm").write_text(
            '#!/bin/bash\nprintf "%s\\0" "$@" >> "$HOOK_CAPTURE"\n'
            'exit "${HOOK_CM_EXIT:-0}"\n', encoding="utf-8")
        self.capture = self.repo / "capture"

    def git(self, *args):
        return subprocess.run(["git", "--no-pager", *args], cwd=self.repo,
                              check=True, capture_output=True)

    def commit(self, paths):
        self.git("add", "--", *paths)
        self.git("commit", "-qm", "тест\n\nвторая строка")

    def hook(self, **extra):
        env = dict(os.environ, PATH=str(self.bin) + os.pathsep + os.environ["PATH"],
                   HOOK_CAPTURE=self.capture.as_posix(), **extra)
        result = subprocess.run([BASH, str(ROOT / ".githooks/post-commit")],
                                cwd=self.repo, env=env, capture_output=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        return result.stdout.decode("utf-8", errors="replace")

    def args(self):
        return self.capture.read_bytes().decode().split("\0")[:-1]

    def test_only_committed_paths(self):
        (self.repo / "Assets/имя с пробелами.txt").write_text("committed\n")
        self.commit(["Assets/имя с пробелами.txt"])
        (self.repo / "Assets/base.txt").write_text("unrelated dirty\n")
        (self.repo / "Assets/private.txt").write_text("private\n")
        self.hook()
        args = self.args()
        self.assertNotIn(".", args)
        self.assertIn("./Assets/имя с пробелами.txt", args)
        self.assertNotIn("./Assets/base.txt", args)
        self.assertNotIn("./Assets/private.txt", args)

    def test_only_unity_folders_reach_plastic(self):
        (self.repo / "notes.md").write_text("changed\n")
        for name in ("Assets/a.txt", "Packages/manifest.json", "ProjectSettings/p.asset"):
            (self.repo / name).parent.mkdir(exist_ok=True)
            (self.repo / name).write_text("unity\n")
        self.commit(["notes.md", "Assets/a.txt", "Packages/manifest.json",
                     "ProjectSettings/p.asset"])
        self.hook()
        args = self.args()
        self.assertNotIn("./notes.md", args)
        for name in ("./Assets/a.txt", "./Packages/manifest.json", "./ProjectSettings/p.asset"):
            self.assertIn(name, args)

    def test_non_unity_commit_does_not_call_plastic(self):
        (self.repo / "notes.md").write_text("changed\n")
        self.commit(["notes.md"])
        self.hook()
        self.assertFalse(self.capture.exists())

    def test_partial_commit_is_refused(self):
        (self.repo / "Assets/base.txt").write_text("committed\n")
        self.commit(["Assets/base.txt"])
        (self.repo / "Assets/base.txt").write_text("uncommitted second edit\n")
        self.hook()
        self.assertFalse(self.capture.exists(), "Dirty committed path reached Plastic")

    def test_rename_contains_both_paths(self):
        self.git("mv", "Assets/base.txt", "Assets/renamed.txt")
        self.git("commit", "-qm", "rename")
        self.hook()
        self.assertIn("./Assets/base.txt", self.args())
        self.assertIn("./Assets/renamed.txt", self.args())

    def test_deleted_path_recreated_is_refused(self):
        (self.repo / "Assets/base.txt").unlink()
        self.commit(["Assets/base.txt"])
        (self.repo / "Assets/base.txt").write_text("new private content\n")
        self.hook()
        self.assertFalse(self.capture.exists())

    def test_empty_commit_does_not_call_plastic(self):
        self.git("commit", "--allow-empty", "-qm", "empty")
        self.hook()
        self.assertFalse(self.capture.exists())

    def test_plastic_failure_does_not_claim_success(self):
        (self.repo / "Assets/base.txt").write_text("committed\n")
        self.commit(["Assets/base.txt"])
        output = self.hook(HOOK_CM_EXIT="7")
        self.assertIn("код 7", output)
        self.assertNotIn("✅", output)

    def test_deletion_is_explicit(self):
        (self.repo / "Assets/base.txt").unlink()
        self.commit(["Assets/base.txt"])
        self.hook()
        self.assertIn("./Assets/base.txt", self.args())

    def test_root_commit(self):
        self.hook()
        self.assertIn("./Assets/base.txt", self.args())

    def test_merge_diff_is_relative_to_first_parent(self):
        branch = self.git("branch", "--show-current").stdout.decode().strip()
        self.git("checkout", "-qb", "side")
        (self.repo / "Assets/side.txt").write_text("side\n")
        self.commit(["Assets/side.txt"])
        self.git("checkout", "-q", branch)
        (self.repo / "Assets/main.txt").write_text("main\n")
        self.commit(["Assets/main.txt"])
        self.git("merge", "--no-ff", "-m", "merge", "side")
        self.hook()
        self.assertIn("./Assets/side.txt", self.args())
        self.assertNotIn("./Assets/main.txt", self.args())

    def test_staged_post_commit_edit_is_refused(self):
        (self.repo / "Assets/base.txt").write_text("committed\n")
        self.commit(["Assets/base.txt"])
        (self.repo / "Assets/base.txt").write_text("staged second edit\n")
        self.git("add", "Assets/base.txt")
        self.hook()
        self.assertFalse(self.capture.exists())

    def test_deleted_file_replaced_by_directory_is_refused(self):
        (self.repo / "Assets/base.txt").unlink()
        self.commit(["Assets/base.txt"])
        (self.repo / "Assets/base.txt").mkdir()
        (self.repo / "Assets/base.txt/private.txt").write_text("private\n")
        self.hook()
        self.assertFalse(self.capture.exists())

    def test_assume_unchanged_does_not_hide_dirty_content(self):
        (self.repo / "Assets/base.txt").write_text("committed\n")
        self.commit(["Assets/base.txt"])
        self.git("update-index", "--assume-unchanged", "Assets/base.txt")
        (self.repo / "Assets/base.txt").write_text("hidden dirty content\n")
        self.hook()
        self.assertFalse(self.capture.exists())

    def test_crlf_clean_filter_matches_commit(self):
        (self.repo / ".gitattributes").write_text("Assets/base.txt text eol=crlf\n")
        (self.repo / "Assets/base.txt").write_bytes(b"committed\r\n")
        self.commit([".gitattributes", "Assets/base.txt"])
        self.hook()
        self.assertIn("./Assets/base.txt", self.args())


if __name__ == "__main__":
    unittest.main()
