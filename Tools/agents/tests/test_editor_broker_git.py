"""Интеграционный контракт checkpoint на настоящем Git."""
import importlib.util
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import time
import unittest
from unittest import mock

MODULE = Path(__file__).parents[1] / "editor_broker" / "git_state.py"
if MODULE.exists():
    spec = importlib.util.spec_from_file_location("broker_git", MODULE)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    GitState = module.GitState
else:
    GitState = None


class GitStateTests(unittest.TestCase):
    def setUp(self):
        self.assertIsNotNone(GitState, "GitState checkpoint implementation is missing")
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / "repo"
        self.root.mkdir()
        self.state = Path(self.temp.name) / "state"
        self.git("init", "-q")
        self.git("config", "user.name", "Fixture")
        self.git("config", "user.email", "fixture@example.invalid")
        self.git("config", "core.autocrlf", "false")
        self.write(".gitignore", b"Library/\nTemp/\nAssets/Editor/VR_Battlegrounds/Debug/EditorBrokerLocal/\n")
        self.write("Assets/a.txt", b"base\n")
        self.write("Assets/delete.txt", b"delete\n")
        self.git("add", "-A")
        self.git("commit", "-qm", "base")
        self.base = self.git("rev-parse", "HEAD").decode().strip()
        self.gs = GitState(self.root)

    def git(self, *args):
        return subprocess.run(["git", "--no-pager", *args], cwd=self.root,
                              check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE).stdout

    def write(self, name, content):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(content)

    def index(self):
        return (self.root / ".git" / "index").read_bytes()

    def capture(self):
        return self.gs.capture_result(self.state, self.base, ["Assets"], "result")

    def test_checkpoint_preserves_head_index_hooks_config_and_binary_meta(self):
        self.write("Assets/a.txt", b"staged\n")
        self.git("add", "Assets/a.txt")
        self.write("Assets/a.txt", b"unstaged\n")
        self.write("Assets/new.asset", b"\x00\xff\x80binary")
        self.write("Assets/new.asset.meta", b"guid: sample\n")
        (self.root / "Assets/delete.txt").unlink()
        before = self.index()
        config = (self.root / ".git/config").read_bytes()
        hook = self.root / ".git/hooks/pre-commit"
        hook.write_text("#!/bin/sh\ntouch hook-ran\nexit 1\n")
        hook.chmod(0o755)
        result = self.gs.checkpoint(self.state, "input")
        self.assertEqual(self.gs.head(), self.base)
        self.assertEqual(self.index(), before)
        self.assertEqual((self.root / ".git/config").read_bytes(), config)
        self.assertFalse((self.root / "hook-ran").exists())
        self.assertEqual(self.git("show", result["sha"] + ":Assets/new.asset"), b"\x00\xff\x80binary")
        self.assertEqual(self.git("show", result["sha"] + ":Assets/a.txt"), b"unstaged\n")
        self.assertEqual(self.git("rev-parse", result["ref"]).decode().strip(), result["sha"])
        self.assertEqual(set(result["paths"]), {"Assets/a.txt", "Assets/delete.txt", "Assets/new.asset", "Assets/new.asset.meta"})
        self.assertTrue(self.gs.is_ancestor(self.base, result["sha"]))

    def check_hidden_index_flag(self, flag):
        self.git("update-index", flag, "Assets/a.txt")
        before = self.index()
        self.write("Assets/a.txt", b"hidden edit\n")
        self.assertEqual(self.git("diff", "--name-only", "HEAD"), b"")
        result = self.gs.checkpoint(self.state, "hidden-flag")
        self.assertEqual(result["paths"], ["Assets/a.txt"])
        self.assertEqual(self.git("show", result["sha"] + ":Assets/a.txt"), b"hidden edit\n")
        self.assertEqual(self.index(), before)
        self.assertEqual(self.gs.head(), self.base)

    def test_checkpoint_sees_assume_unchanged_without_changing_author_index(self):
        self.check_hidden_index_flag("--assume-unchanged")

    def test_checkpoint_sees_skip_worktree_without_changing_author_index(self):
        self.check_hidden_index_flag("--skip-worktree")

    def test_attribute_change_renormalizes_unedited_nonracy_cached_file(self):
        self.git("config", "core.trustctime", "false")
        self.write(".gitattributes", b"Assets/a.txt -text\n")
        self.write("Assets/a.txt", b"cached\r\nbytes\r\n")
        path = self.root / "Assets/a.txt"
        old_time = 1_600_000_000_000_000_000
        os.utime(path, ns=(old_time, old_time))
        self.git("add", ".gitattributes", "Assets/a.txt")
        self.git("commit", "-qm", "cached binary policy")
        self.base = self.gs.head()
        self.assertEqual(self.git("diff", "--name-only", "HEAD"), b"")
        self.write(".gitattributes", b"Assets/a.txt text eol=lf\n")
        self.assertEqual(self.git("diff", "--name-only", "HEAD").strip(), b".gitattributes")
        before = self.index()
        result = self.gs.checkpoint(self.state, "new-text-policy")
        self.assertEqual(result["paths"], [".gitattributes", "Assets/a.txt"])
        self.assertEqual(self.git("show", result["sha"] + ":Assets/a.txt"), b"cached\nbytes\n")
        self.assertEqual(self.index(), before)
        self.assertEqual(path.stat().st_mtime_ns, old_time)

    def test_attribute_change_does_not_run_clean_filters_of_unaffected_files(self):
        self.write(".gitattributes", b"Assets/unaffected.asset filter=untouched -text\nAssets/a.txt -text\n")
        self.write("Assets/unaffected.asset", b"unchanged binary")
        os.utime(self.root / "Assets/unaffected.asset", ns=(1_600_000_000_000_000_000,) * 2)
        self.git("add", ".gitattributes", "Assets/unaffected.asset")
        self.git("commit", "-qm", "unaffected filter policy")
        self.git("config", "filter.untouched.clean", 'python -c "import sys; sys.exit(7)"')
        self.git("config", "filter.untouched.required", "true")
        self.write(".gitattributes", b"Assets/unaffected.asset filter=untouched -text\nAssets/a.txt text eol=lf\n")
        before = self.index()
        result = self.gs.checkpoint(self.state, "scoped-attribute-policy")
        self.assertEqual(result["paths"], [".gitattributes"])
        self.assertEqual(self.index(), before)

    def test_partial_checkpoint_only_selected_paths(self):
        self.write("Assets/a.txt", b"changed")
        self.write("extra.txt", b"outside")
        result = self.gs.checkpoint(self.state, "partial", ["Assets"])
        self.assertEqual(result["paths"], ["Assets/a.txt"])
        self.assertEqual(self.gs.changed_paths(result["sha"]), ["extra.txt"])

    def test_capture_pins_unexpected_outputs_and_parent_a(self):
        self.write("extra.txt", b"unexpected")
        result = self.capture()
        self.assertFalse(result["accepted"])
        self.assertEqual(result["unexpected"], ["extra.txt"])
        self.assertEqual(self.git("rev-parse", result["sha"] + "^" ).decode().strip(), self.base)
        self.assertEqual(self.git("show", result["ref"] + ":extra.txt"), b"unexpected")

    def test_restore_preserves_ignored_cache_bridge_and_pinned_result(self):
        self.write("Assets/a.txt", b"generated")
        self.write("Assets/new.meta", b"new")
        self.write("Library/cache", b"cache")
        self.write("Assets/Editor/VR_Battlegrounds/Debug/EditorBrokerLocal/bridge.cs", b"bridge")
        result = self.capture()
        self.gs.restore_captured(result["sha"], self.base)
        self.assertTrue(self.gs.is_clean())
        self.assertFalse((self.root / "Assets/new.meta").exists())
        self.assertEqual((self.root / "Library/cache").read_bytes(), b"cache")
        self.assertEqual((self.root / "Assets/Editor/VR_Battlegrounds/Debug/EditorBrokerLocal/bridge.cs").read_bytes(), b"bridge")
        self.assertEqual(self.git("show", result["ref"] + ":Assets/a.txt"), b"generated")

    def test_restore_does_not_rehash_unchanged_files(self):
        # clean-фильтр журналирует каждый перехэшируемый файл: в worker это LFS на десятках ГБ.
        log = Path(self.temp.name) / "cleaned.log"
        script = Path(self.temp.name) / "clean.py"
        script.write_text("import shutil, sys\nopen(sys.argv[1], 'a').write(sys.argv[2] + '\\n')\n"
                          "shutil.copyfileobj(sys.stdin.buffer, sys.stdout.buffer)\n", encoding="utf-8")
        self.git("config", "filter.counter.clean", f'"{sys.executable}" "{script}" "{log}" %f')
        self.write(".gitattributes", b"*.txt filter=counter\n")
        self.git("add", ".gitattributes")
        self.git("commit", "-qm", "counted filter")
        self.base = self.gs.head()
        # Как в настоящем worker: файлы старше индекса, иначе Git перехэширует их как «racy».
        past = time.time() - 3600
        for name in ("Assets/a.txt", "Assets/delete.txt", ".gitattributes", ".gitignore"):
            os.utime(self.root / name, (past, past))
        self.git("update-index", "--really-refresh")
        self.write("Assets/new.txt", b"generated\n")
        result = self.capture()
        log.unlink(missing_ok=True)
        self.gs.restore_captured(result["sha"], self.base)
        cleaned = log.read_text(encoding="utf-8").split() if log.exists() else []
        self.assertNotIn("Assets/a.txt", cleaned)
        self.assertTrue(self.gs.is_clean())

    def test_restore_rejects_changes_after_capture_without_mutation(self):
        self.write("Assets/a.txt", b"generated")
        result = self.capture()
        self.write("late.txt", b"foreign")
        before = self.index()
        with self.assertRaises(ValueError):
            self.gs.restore_captured(result["sha"], self.base)
        self.assertEqual(self.gs.head(), self.base)
        self.assertEqual(self.index(), before)
        self.assertEqual((self.root / "Assets/a.txt").read_bytes(), b"generated")

    def test_restore_requires_pinned_result(self):
        self.write("Assets/a.txt", b"generated")
        result = self.capture()
        self.git("update-ref", "-d", result["ref"])
        with self.assertRaises(ValueError):
            self.gs.restore_captured(result["sha"], self.base)
        self.assertEqual((self.root / "Assets/a.txt").read_bytes(), b"generated")

    def prepare_receive(self):
        self.write("Assets/a.txt", b"input-dirty\n")
        a = self.gs.checkpoint(self.state, "input")["sha"]
        self.write("Assets/a.txt", b"output\n")
        self.write("Assets/new.asset", b"\x00\xff")
        self.write("Assets/new.asset.meta", b"guid: result\n")
        (self.root / "Assets/delete.txt").unlink()
        result = self.gs.capture_result(self.state, a, ["Assets"], "result")
        self.write("Assets/a.txt", b"input-dirty\n")
        self.write("Assets/delete.txt", b"delete\n")
        (self.root / "Assets/new.asset").unlink()
        (self.root / "Assets/new.asset.meta").unlink()
        return a, result

    def test_receive_applies_a_to_r_preserving_original_dirty_head_index(self):
        a, result = self.prepare_receive()
        self.write("foreign.txt", b"keep")
        before = self.index()
        delivery = self.gs.receive_result(a, result["sha"])
        self.assertTrue(delivery["received"])
        self.assertEqual(self.gs.head(), self.base)
        self.assertEqual(self.index(), before)
        self.assertEqual((self.root / "Assets/a.txt").read_bytes(), b"output\n")
        self.assertEqual((self.root / "Assets/new.asset").read_bytes(), b"\x00\xff")
        self.assertFalse((self.root / "Assets/delete.txt").exists())
        self.assertEqual((self.root / "foreign.txt").read_bytes(), b"keep")

    def test_receive_conflict_checks_all_paths_before_mutation(self):
        a, result = self.prepare_receive()
        self.write("Assets/new.asset.meta", b"foreign collision")
        before = self.index()
        with self.assertRaises(ValueError):
            self.gs.receive_result(a, result["sha"])
        self.assertEqual((self.root / "Assets/a.txt").read_bytes(), b"input-dirty\n")
        self.assertTrue((self.root / "Assets/delete.txt").exists())
        self.assertEqual(self.index(), before)

    def test_receive_rechecks_after_filters_before_any_mutation(self):
        a, result = self.prepare_receive()
        original = self.gs._git
        before = self.index()
        mutated = False

        def mutate_after_filter(*args, **kwargs):
            nonlocal mutated
            value = original(*args, **kwargs)
            if "cat-file" in args and "--filters" in args and not mutated:
                mutated = True
                self.write("Assets/a.txt", b"foreign during filters\n")
            return value

        with mock.patch.object(self.gs, "_git", side_effect=mutate_after_filter):
            with self.assertRaises(ValueError):
                self.gs.receive_result(a, result["sha"])
        self.assertEqual((self.root / "Assets/a.txt").read_bytes(), b"foreign during filters\n")
        self.assertTrue((self.root / "Assets/delete.txt").exists())
        self.assertFalse((self.root / "Assets/new.asset").exists())
        self.assertEqual(self.index(), before)
        self.assertEqual(self.git("rev-parse", result["ref"]).decode().strip(), result["sha"])

    def test_receive_rechecks_file_before_atomic_publish(self):
        a, result = self.prepare_receive()
        original = module.os.fsync
        mutated = False

        def mutate_during_temp_flush(handle):
            nonlocal mutated
            original(handle)
            if not mutated:
                mutated = True
                self.write("Assets/a.txt", b"foreign before publish\n")

        with mock.patch.object(module.os, "fsync", side_effect=mutate_during_temp_flush):
            with self.assertRaises(ValueError):
                self.gs.receive_result(a, result["sha"])
        self.assertEqual((self.root / "Assets/a.txt").read_bytes(), b"foreign before publish\n")
        self.assertEqual(self.git("rev-parse", result["ref"]).decode().strip(), result["sha"])

    def test_receive_refuses_changed_existing_file(self):
        a, result = self.prepare_receive()
        self.write("Assets/a.txt", b"new local changes")
        with self.assertRaises(ValueError):
            self.gs.receive_result(a, result["sha"])
        self.assertFalse((self.root / "Assets/new.asset").exists())

    def test_receive_retry_confirms_result_without_rewriting_files(self):
        a, result = self.prepare_receive()
        self.gs.receive_result(a, result["sha"])
        path = self.root / "Assets/a.txt"
        os.utime(path, ns=(1_700_000_000_000_000_000, 1_700_000_000_000_000_000))
        before = self.index()
        delivery = self.gs.receive_result(a, result["sha"])
        self.assertTrue(delivery["received"])
        self.assertEqual(path.stat().st_mtime_ns, 1_700_000_000_000_000_000)
        self.assertEqual(self.index(), before)

    def test_receive_resumes_partial_delivery_and_rejects_third_state(self):
        a, result = self.prepare_receive()
        self.write("Assets/a.txt", b"output\n")
        self.gs.receive_result(a, result["sha"])
        self.assertEqual((self.root / "Assets/new.asset").read_bytes(), b"\x00\xff")
        self.write("Assets/a.txt", b"post-delivery user edit\n")
        (self.root / "Assets/new.asset").unlink()
        with self.assertRaises(ValueError):
            self.gs.receive_result(a, result["sha"])
        self.assertEqual((self.root / "Assets/a.txt").read_bytes(), b"post-delivery user edit\n")
        self.assertFalse((self.root / "Assets/new.asset").exists())

    def test_paths_reject_traversal_cache_and_reserved_bridge(self):
        for name in ["../outside", ".git/config", ".plastic", "Library", "Temp", "Assets/Editor/VR_Battlegrounds/Debug/EditorBrokerLocal", "C:/external"]:
            with self.subTest(name=name), self.assertRaises(ValueError):
                self.gs.checkpoint(self.state, "unsafe", [name])

    def test_symlink_output_cannot_escape_repository(self):
        outside = Path(self.temp.name) / "outside"
        outside.write_bytes(b"protected")
        try:
            (self.root / "Assets/link").symlink_to(outside)
        except OSError:
            self.skipTest("OS does not permit symlink fixture")
        with self.assertRaises(ValueError):
            self.capture()
        self.assertEqual(outside.read_bytes(), b"protected")

    def test_checkpoint_does_not_run_reference_transaction_hook(self):
        hook = self.root / ".git/hooks/reference-transaction"
        hook.write_text("#!/bin/sh\ntouch reference-hook-ran\nexit 1\n")
        hook.chmod(0o755)
        result = self.gs.checkpoint(self.state, "without-ref-hook")
        self.assertFalse((self.root / "reference-hook-ran").exists())
        self.assertEqual(self.git("rev-parse", result["ref"]).decode().strip(), result["sha"])

    def test_directory_meta_is_part_of_declared_output_root(self):
        self.write("Assets/NewFolder/asset.bin", b"\x00\xff")
        self.write("Assets/NewFolder.meta", b"guid: directory\n")
        result = self.gs.capture_result(self.state, self.base, ["Assets/NewFolder"], "directory")
        self.assertTrue(result["accepted"])
        self.assertEqual(result["paths"], ["Assets/NewFolder.meta", "Assets/NewFolder/asset.bin"])

    def test_receive_rejects_case_collision_without_mutation(self):
        a, result = self.prepare_receive()
        self.write("assets/foreign.txt", b"case collision")
        oid = self.git("hash-object", "-w", "assets/foreign.txt").decode().strip()
        # На Windows каталог физически тот же; индекс Git хранит точный регистр.
        self.git("update-index", "--add", "--cacheinfo", "100644," + oid + ",assets/foreign.txt")
        before = self.index()
        with self.assertRaises(ValueError):
            self.gs.receive_result(a, result["sha"])
        self.assertEqual(self.index(), before)
        self.assertEqual((self.root / "Assets/a.txt").read_bytes(), b"input-dirty\n")

    def commit_link(self, name, target, message="link"):
        """Ссылка в дереве без прав на OS symlink: core.symlinks=false, как в worker на Windows."""
        self.git("config", "core.symlinks", "false")
        self.write(name, target)
        oid = self.git("hash-object", "-w", name).decode().strip()
        self.git("update-index", "--add", "--cacheinfo", "120000," + oid + "," + name)
        self.git("commit", "-qm", message)
        return self.gs.head()

    def test_modified_git_tree_symlink_is_rejected_without_os_symlink_support(self):
        self.commit_link("Assets/link", b"Assets/a.txt")
        self.write("Assets/link", b"../../outside")
        before = self.index()
        with self.assertRaises(ValueError):
            self.gs.checkpoint(self.state, "bad-tree")
        self.assertEqual(self.index(), before)

    def test_removed_symlink_entry_is_checkpointed(self):
        parent = self.commit_link("Assets/link", b"Assets/a.txt")
        (self.root / "Assets/link").unlink()
        result = self.gs.checkpoint(self.state, "drop-link")
        self.assertEqual(result["paths"], ["Assets/link"])
        self.assertEqual(self.git("ls-tree", result["sha"], "Assets/link"), b"")
        self.assertEqual(self.gs.head(), parent)

    def test_switch_replaces_symlink_with_regular_file(self):
        linked = self.commit_link("CLAUDE.md", b"AGENTS.md")
        self.git("rm", "-q", "--cached", "CLAUDE.md")
        self.write("CLAUDE.md", b"@AGENTS.md\n")
        self.git("add", "CLAUDE.md")
        self.git("commit", "-qm", "regular file")
        regular = self.gs.head()
        self.git("checkout", "-q", "--detach", linked)
        self.gs.switch_detached(regular)
        self.assertEqual(self.gs.head(), regular)
        self.assertEqual((self.root / "CLAUDE.md").read_bytes(), b"@AGENTS.md\n")
        self.assertTrue(self.gs.is_clean())

    def test_switch_to_new_or_changed_symlink_is_rejected(self):
        plain = self.gs.head()
        linked = self.commit_link("CLAUDE.md", b"AGENTS.md")
        changed = self.commit_link("CLAUDE.md", b"../outside", "changed link")
        self.git("checkout", "-q", "--detach", plain)
        with self.assertRaises(ValueError):
            self.gs.switch_detached(linked)
        self.git("checkout", "-q", "--detach", linked)
        with self.assertRaises(ValueError):
            self.gs.switch_detached(changed)
        self.assertEqual(self.gs.head(), linked)

    def test_checkpoint_uses_canonical_text_and_clean_crlf_has_no_delta(self):
        self.write(".gitattributes", b"Assets/a.txt text eol=crlf\n")
        self.write("Assets/a.txt", b"raw\r\nbytes\r\n")
        self.git("add", ".gitattributes", "Assets/a.txt")
        self.git("commit", "-qm", "canonical text")
        self.assertEqual(self.gs.changed_paths(self.gs.head()), [])
        self.write("Assets/a.txt", b"changed\r\nbytes\r\n")
        before = self.index()
        result = self.gs.checkpoint(self.state, "raw")
        self.assertEqual(result["paths"], ["Assets/a.txt"])
        self.assertEqual(self.git("show", result["sha"] + ":Assets/a.txt"), b"changed\nbytes\n")
        self.assertEqual(self.index(), before)

    def test_receive_and_restore_materialize_target_crlf(self):
        self.write(".gitattributes", b"*.txt text eol=crlf\n")
        self.git("add", ".gitattributes")
        self.git("commit", "-qm", "text policy")
        self.base = self.gs.head()
        self.write("Assets/a.txt", b"generated\r\n")
        result = self.capture()
        self.gs.restore_captured(result["sha"], self.base)
        self.assertEqual((self.root / "Assets/a.txt").read_bytes(), b"base\r\n")
        before = self.index()
        self.gs.receive_result(self.base, result["sha"])
        self.assertEqual((self.root / "Assets/a.txt").read_bytes(), b"generated\r\n")
        self.assertEqual(self.index(), before)

    def test_unchanged_tracked_symlink_pointer_is_preserved(self):
        self.git("config", "core.symlinks", "false")
        self.write("AGENTS.md", b"instructions")
        self.write("CLAUDE.md", b"AGENTS.md")
        self.git("add", "AGENTS.md")
        oid = self.git("hash-object", "-w", "CLAUDE.md").decode().strip()
        self.git("update-index", "--add", "--cacheinfo", "120000," + oid + ",CLAUDE.md")
        self.git("commit", "-qm", "pointer")
        self.write("Assets/a.txt", b"changed\n")
        result = self.gs.checkpoint(self.state, "pointer")
        self.assertEqual(result["paths"], ["Assets/a.txt"])
        self.assertEqual(self.git("ls-tree", result["sha"], "CLAUDE.md").split()[0], b"120000")

    def test_receive_uses_result_attributes_even_when_input_policy_differs(self):
        self.write(".gitattributes", b"*.txt text eol=crlf\n")
        self.git("add", ".gitattributes")
        self.git("commit", "-qm", "input policy")
        self.base = self.gs.head()
        self.write(".gitattributes", b"*.txt text eol=lf\n")
        self.write("Assets/a.txt", b"result\n")
        result = self.gs.capture_result(self.state, self.base, ["Assets", ".gitattributes"], "policy")
        self.gs.restore_captured(result["sha"], self.base)
        self.gs.receive_result(self.base, result["sha"])
        self.assertEqual((self.root / "Assets/a.txt").read_bytes(), b"result\n")
        self.assertEqual((self.root / ".gitattributes").read_bytes(), b"*.txt text eol=lf\n")

    def test_lfs_clean_and_smudge_round_trip_remains_materialized(self):
        try:
            self.git("lfs", "version")
        except subprocess.CalledProcessError:
            self.skipTest("Git LFS is not installed")
        self.git("config", "filter.lfs.clean", "git-lfs clean -- %f")
        self.git("config", "filter.lfs.smudge", "git-lfs smudge -- %f")
        self.git("config", "filter.lfs.process", "git-lfs filter-process")
        self.git("config", "filter.lfs.required", "true")
        self.write(".gitattributes", b"*.bin filter=lfs diff=lfs merge=lfs -text\n")
        self.write("Assets/lfs.bin", b"\x00original\xff")
        self.git("add", ".gitattributes", "Assets/lfs.bin")
        self.git("commit", "-qm", "lfs fixture")
        self.base = self.gs.head()
        self.assertEqual(self.gs.changed_paths(self.base), [])
        self.write("Assets/lfs.bin", b"\x00generated\xfe")
        before = self.index()
        result = self.capture()
        self.assertEqual(self.index(), before)
        self.assertTrue(self.git("show", result["sha"] + ":Assets/lfs.bin").startswith(b"version https://git-lfs.github.com/spec/v1\n"))
        self.gs.restore_captured(result["sha"], self.base)
        self.assertEqual((self.root / "Assets/lfs.bin").read_bytes(), b"\x00original\xff")
        self.gs.receive_result(self.base, result["sha"])
        self.assertEqual((self.root / "Assets/lfs.bin").read_bytes(), b"\x00generated\xfe")

    def test_receive_preserves_preexisting_staged_changes(self):
        a, result = self.prepare_receive()
        self.write("staged.txt", b"staged")
        self.git("add", "staged.txt")
        self.write("staged.txt", b"unstaged")
        before = self.index()
        self.gs.receive_result(a, result["sha"])
        self.assertEqual(self.index(), before)
        self.assertEqual((self.root / "staged.txt").read_bytes(), b"unstaged")

    def prepare_ignored_asset_adoption(self):
        original = (self.root / ".gitignore").read_bytes()
        ignored = original + b"Assets/cached.asset\n"
        self.write(".gitignore", ignored)
        self.git("add", ".gitignore")
        self.git("commit", "-qm", "cache policy")
        baseline = self.gs.head()
        self.write("Assets/cached.asset", b"canonical font\n")
        self.write(".gitignore", original)
        target = self.gs.checkpoint(self.state, "track-cache", paths=[".gitignore", "Assets/cached.asset"])["sha"]
        self.write(".gitignore", ignored)
        return baseline, target

    def test_switch_adopts_identical_ignored_asset_into_tracked_target(self):
        _, target = self.prepare_ignored_asset_adoption()
        self.gs.switch_detached(target)
        self.assertEqual(self.gs.head(), target)
        self.assertEqual((self.root / "Assets/cached.asset").read_bytes(), b"canonical font\n")
        self.assertTrue(self.gs.is_clean())

    def test_switch_refuses_different_ignored_asset_without_mutation(self):
        baseline, target = self.prepare_ignored_asset_adoption()
        self.write("Assets/cached.asset", b"private newer cache\n")
        before_index = self.index()
        with self.assertRaises(ValueError):
            self.gs.switch_detached(target)
        self.assertEqual(self.gs.head(), baseline)
        self.assertEqual(self.index(), before_index)
        self.assertEqual((self.root / "Assets/cached.asset").read_bytes(), b"private newer cache\n")

    def test_switch_rechecks_ignored_asset_after_filter_before_checkout(self):
        baseline, target = self.prepare_ignored_asset_adoption()
        match = self.gs._matches
        changed = False
        def concurrent_write(*args):
            nonlocal changed
            result = match(*args)
            if not changed:
                changed = True
                self.write("Assets/cached.asset", b"concurrent local cache\n")
            return result
        with mock.patch.object(self.gs, "_matches", side_effect=concurrent_write):
            with self.assertRaises(ValueError):
                self.gs.switch_detached(target)
        self.assertEqual(self.gs.head(), baseline)
        self.assertEqual((self.root / "Assets/cached.asset").read_bytes(), b"concurrent local cache\n")

    def test_detached_switch_and_common_dir(self):
        result = self.gs.checkpoint(self.state, "same")
        self.gs.switch_detached(result["sha"])
        self.assertEqual(self.gs.head(), result["sha"])
        self.assertTrue(Path(self.gs.common_dir()).is_dir())
        self.write("dirty.txt", b"foreign")
        with self.assertRaises(ValueError):
            self.gs.switch_detached(self.base)


if __name__ == "__main__":
    unittest.main()
