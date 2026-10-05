"""Контроли безопасного переноса: копия, preflight и сохранение используемых GUID."""
import contextlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import subprocess
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location("separate_installed_packs", Path(__file__).parents[1] / "separate_installed_packs.py")
tool = importlib.util.module_from_spec(spec)
spec.loader.exec_module(tool)


class SeparationTests(unittest.TestCase):
    def fixture(self, root):
        game, destination, report = root / "Game", root / "Catalog", root / "Report"
        repository = root / "Repository"
        repository.mkdir()
        subprocess.run(["git", "--no-pager", "init", "-q", str(repository)], check=True, capture_output=True)
        subprocess.run(["git", "--no-pager", "-C", str(repository), "-c", "user.name=fixture",
                        "-c", "user.email=fixture@example.invalid", "-c", "core.hooksPath=",
                        "commit", "--allow-empty", "-qm", "fixture"], check=True, capture_output=True)
        subprocess.run(["git", "--no-pager", "-C", str(repository), "worktree", "add", "--detach",
                        str(game), "HEAD"], check=True, capture_output=True)
        def write(path, content):
            target = game / path
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(content, encoding="utf-8")
        for name in ["Keep", "Unused"]:
            write("Assets/env_packs/P/" + name + ".prefab", name)
            write("Assets/env_packs/P/" + name + ".prefab.meta", "guid: " + name)
        write("Assets/env_packs/P.meta", "folder")
        write("Assets/env_packs.meta", "root")
        write(tool.REVIEW + ".unity", "review")
        write(tool.REVIEW + ".unity.meta", "reviewmeta")
        write("ProjectSettings/ProjectVersion.txt", "6000.4.1f1")
        write("ProjectSettings/TagManager.asset", "tags")
        report.mkdir()
        (report / "retention-unity.json").write_text(json.dumps({"retained": [{"path": "Assets/env_packs/P/Keep.prefab"}], "reviewDependencies": []}))
        (report / "pipeline.json").write_text(json.dumps({"ids": ["a" * 32, "b" * 32], "dependencies": []}))
        with contextlib.redirect_stdout(io.StringIO()):
            tool.stage(game, destination, report)
        return game, destination, report

    def setUp(self):
        inventory = patch("editor_broker.client_guard.unity_project_roots", return_value=[])
        inventory.start()
        self.addCleanup(inventory.stop)

    def test_verified_copy_then_prune_keeps_used_asset_and_parent_meta(self):
        with tempfile.TemporaryDirectory() as temporary:
            game, catalog, report = self.fixture(Path(temporary))
            with contextlib.redirect_stdout(io.StringIO()):
                tool.prune(game, catalog, report)
            self.assertTrue((game / "Assets/env_packs/P/Keep.prefab").is_file())
            self.assertTrue((game / "Assets/env_packs/P.meta").is_file())
            self.assertFalse((game / "Assets/env_packs/P/Unused.prefab").exists())
            self.assertTrue((catalog / "Assets/env_packs/P/Unused.prefab").is_file())
            self.assertTrue((catalog / ("Archive/" + tool.REVIEW + ".unity")).is_file())
            self.assertFalse((game / (tool.REVIEW + ".unity")).exists())

    def test_corrupted_backup_stops_before_any_deletion(self):
        with tempfile.TemporaryDirectory() as temporary:
            game, catalog, report = self.fixture(Path(temporary))
            (catalog / "Assets/env_packs/P/Unused.prefab").write_text("corrupted")
            with self.assertRaises(ValueError):
                tool.prune(game, catalog, report)
            self.assertTrue((game / "Assets/env_packs/P/Unused.prefab").is_file())
            self.assertTrue((game / (tool.REVIEW + ".unity")).is_file())

    def test_wrong_root_capability_stops_before_any_deletion(self):
        with tempfile.TemporaryDirectory() as temporary:
            game, catalog, report = self.fixture(Path(temporary))
            with self.assertRaises(ValueError):
                tool.prune(game, catalog, report, ticket="1", token="foreign")
            self.assertTrue((game / "Assets/env_packs/P/Unused.prefab").is_file())

    def test_path_traversal_is_rejected(self):
        with tempfile.TemporaryDirectory() as temporary:
            with self.assertRaises(ValueError):
                tool.checked_path(Path(temporary), "../outside")


if __name__ == "__main__":
    unittest.main()
