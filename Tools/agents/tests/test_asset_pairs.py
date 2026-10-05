"""Аудит Unity meta по настоящему Git, независимо от чужих рабочих удалений."""
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from asset_pairs import audit


class AssetPairTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.git("init", "-q")
        self.git("config", "core.autocrlf", "false")

    def git(self, *args):
        return subprocess.run(["git", "--no-pager", *args], cwd=self.root, check=True,
                              capture_output=True).stdout

    def write(self, path, text):
        target = self.root / path
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(text, encoding="utf-8")

    def meta(self, owner, *, folder=False):
        self.write(owner + ".meta", "fileFormatVersion: 2\nguid: fixture\n" +
                   ("folderAsset: yes\n" if folder else ""))
        self.git("add", "--", owner + ".meta")

    def test_ignored_owner_with_tracked_meta_is_violation(self):
        self.write(".gitignore", "Assets/font.asset\n")
        self.write("Assets/font.asset", "font")
        self.meta("Assets/font.asset")
        result = audit(self.root)
        self.assertFalse(result["passed"])
        self.assertEqual(result["violations"][0]["owner_state"], "ignored")

    def test_removing_ignore_requires_owner_to_be_staged_before_pair_is_valid(self):
        self.write(".gitignore", "Assets/font.asset\n")
        self.write("Assets/font.asset", "canonical font bytes")
        self.meta("Assets/font.asset")
        self.assertFalse(audit(self.root)["passed"])
        self.write(".gitignore", "")
        self.assertFalse(audit(self.root)["passed"])
        self.git("add", "Assets/font.asset")
        self.assertTrue(audit(self.root)["passed"])

    def test_untracked_owner_is_violation(self):
        self.write("Assets/new.asset", "new")
        self.meta("Assets/new.asset")
        result = audit(self.root)
        self.assertFalse(result["passed"])
        self.assertEqual(result["violations"][0]["owner_state"], "untracked")

    def test_tracked_pair_passes_even_when_working_files_are_deleted(self):
        self.write("Assets/tracked.asset", "tracked")
        self.meta("Assets/tracked.asset")
        self.git("add", "Assets/tracked.asset")
        (self.root / "Assets/tracked.asset").unlink()
        (self.root / "Assets/tracked.asset.meta").unlink()
        index_before = (self.root / ".git/index").read_bytes()
        self.assertTrue(audit(self.root)["passed"])
        self.assertEqual((self.root / ".git/index").read_bytes(), index_before)

    def test_empty_folder_meta_is_valid_and_reported_separately(self):
        self.meta("Assets/Empty", folder=True)
        result = audit(self.root)
        self.assertTrue(result["passed"])
        self.assertEqual(result["empty_folders"], ["Assets/Empty"])

    def test_folder_tree_prefix_does_not_require_physical_directory(self):
        self.meta("Assets/Folder", folder=True)
        self.write("Assets/Folder/child.txt", "child")
        self.git("add", "Assets/Folder/child.txt")
        (self.root / "Assets/Folder/child.txt").unlink()
        (self.root / "Assets/Folder").rmdir()
        result = audit(self.root)
        self.assertTrue(result["passed"])
        self.assertEqual(result["empty_folders"], [])

    def test_missing_file_owner_is_violation(self):
        self.meta("Assets/missing.asset")
        result = audit(self.root)
        self.assertFalse(result["passed"])
        self.assertEqual(result["violations"][0]["owner_state"], "missing")

    def test_tree_prefix_proves_vendor_folder_without_folder_marker(self):
        self.meta("Assets/Malformed")
        self.write("Assets/Malformed/child.txt", "child")
        self.git("add", "Assets/Malformed/child.txt")
        result = audit(self.root)
        self.assertTrue(result["passed"])
        self.assertEqual(result["inferred_folders"], ["Assets/Malformed"])

    def test_index_and_head_are_distinct_sources(self):
        self.meta("Assets/staged.asset")
        self.git("-c", "user.name=fixture", "-c", "user.email=fixture@example.invalid",
                 "-c", "core.hooksPath=", "commit", "-qm", "orphan fixture")
        self.write("Assets/staged.asset", "accepted")
        self.git("add", "Assets/staged.asset")
        self.assertTrue(audit(self.root, source="index")["passed"])
        self.assertFalse(audit(self.root, source="head")["passed"])

    def test_cli_failure_report_is_complete_and_stdout_is_bounded(self):
        for index in range(12):
            self.meta("Assets/missing" + str(index) + ".asset")
        script = Path(__file__).resolve().parents[1] / "asset_pairs.py"
        report = self.root / "report.json"
        result = subprocess.run([sys.executable, str(script), "--repo", str(self.root),
                                 "--report", str(report)], capture_output=True)
        self.assertNotEqual(result.returncode, 0)
        summary = json.loads(result.stdout)
        self.assertEqual(summary["violation_count"], 12)
        self.assertLessEqual(len(summary["examples"]), 10)
        self.assertEqual(len(json.loads(report.read_text(encoding="utf-8"))["violations"]), 12)

    def test_inherited_git_environment_cannot_redirect_audit(self):
        self.meta("Assets/missing.asset")
        previous = os.environ.get("GIT_INDEX_FILE")
        os.environ["GIT_INDEX_FILE"] = str(self.root / "foreign-index")
        try:
            self.assertFalse(audit(self.root)["passed"])
        finally:
            if previous is None:
                os.environ.pop("GIT_INDEX_FILE", None)
            else:
                os.environ["GIT_INDEX_FILE"] = previous


if __name__ == "__main__":
    unittest.main()
