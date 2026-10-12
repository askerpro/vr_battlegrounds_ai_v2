"""Интеграционный контракт LFS: новый агент лёгкий, main/worker получают исходные байты."""
import importlib.util
import json
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest

spec = importlib.util.spec_from_file_location("configure_lfs_worktree", Path(__file__).parents[1] / "configure_lfs_worktree.py")
policy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(policy)


HAS_LFS = shutil.which("git-lfs") is not None
POINTER = b"version https://git-lfs.github.com/spec/v1"


@unittest.skipUnless(HAS_LFS, "git-lfs недоступен")
class LfsWorktreeTests(unittest.TestCase):
    def test_agent_pointer_main_and_worker_bytes(self):
        with tempfile.TemporaryDirectory() as temporary:
            base = Path(temporary)
            main, worker, before, agent = [base / name for name in ("main", "worker", "before", "agent")]
            main.mkdir()
            def git(root, *args):
                return policy.git(root, *args)
            git(main, "init")
            git(main, "config", "user.name", "LFS fixture")
            git(main, "config", "user.email", "fixture@example.invalid")
            policy.filters(main, "--local", True)
            (main / ".gitattributes").write_text("*.tga filter=lfs diff=lfs merge=lfs -text\n")
            payload = b"\0" + b"fixture-pixels" * 80000
            (main / "image.tga").write_bytes(payload)
            config = main / "Tools/agents/lfs-worktree-policy.json"
            config.parent.mkdir(parents=True)
            config.write_text(json.dumps({"workerRegistry": ".agent-state/editor-broker/config.json"}))
            shutil.copy2(Path(__file__).parents[1] / "configure_lfs_worktree.py", config.parent / "configure_lfs_worktree.py")
            hook = main / ".githooks/post-checkout"
            hook.parent.mkdir()
            shutil.copy2(Path(__file__).parents[3] / ".githooks/post-checkout", hook)
            shutil.copy2(Path(__file__).parents[3] / ".githooks/pre-push", hook.parent / "pre-push")
            hook.chmod(0o755)
            git(main, "add", ".gitattributes", "image.tga", "Tools")
            git(main, "add", ".githooks")
            git(main, "-c", "core.hooksPath=", "commit", "-m", "fixture")
            git(main, "worktree", "add", "--detach", str(worker), "HEAD")
            git(main, "worktree", "add", "--detach", str(before), "HEAD")
            # Положительный контроль стоимости до политики: настоящий большой файл.
            self.assertEqual((before / "image.tga").read_bytes(), payload)
            registry = main / ".agent-state/editor-broker/config.json"
            registry.parent.mkdir(parents=True)
            registry.write_text(json.dumps({"editor_root": str(worker)}))
            result = policy.apply(main, install_defaults=True)
            self.assertEqual(result["hydration"], "full_after_checkout")
            git(main, "config", "core.hooksPath", ".githooks")
            git(main, "worktree", "add", "--detach", str(agent), "HEAD")
            pointer = (agent / "image.tga").read_bytes()
            self.assertTrue(pointer.startswith(b"version https://git-lfs.github.com/spec/v1"))
            self.assertLess(len(pointer), 200)
            self.assertEqual(policy.apply(agent)["hydration"], "skip")
            self.assertEqual(policy.apply(worker)["hydration"], "full")
            # --filters — тот же маршрут получения рабочего содержимого, что у broker.
            for root in (worker,):
                data = subprocess.check_output(["git", "-C", str(root), "cat-file", "--filters", "HEAD:image.tga"])
                self.assertEqual(data, payload)
            pointer_again = subprocess.check_output(["git", "-C", str(agent), "cat-file", "--filters", "HEAD:image.tga"])
            self.assertEqual(pointer_again, pointer)
            self.assertEqual((main / "image.tga").read_bytes(), payload)
            # При обычном checkout main получает указатель, а hook восстанавливает кеш.
            (main / "image.tga").unlink()
            git(main, "-c", "core.hooksPath=", "checkout", "--", "image.tga")
            self.assertTrue((main / "image.tga").read_bytes().startswith(b"version https://"))
            self.assertTrue(policy.after_checkout(main)["hydrated"])
            self.assertEqual((main / "image.tga").read_bytes(), payload)
            (main / "image.tga").unlink()
            git(main, "checkout", "--", "image.tga")
            self.assertEqual((main / "image.tga").read_bytes(), payload)
            with self.assertRaises(ValueError):
                policy.apply(agent, install_defaults=True)
            # Broker внедряется отдельной задачей и может отсутствовать в этой ветке.
            # Основные LFS/hook проверки выше остаются обязательными в чистом clone.
            sys.path.insert(0, str(Path(__file__).parents[1]))
            import broker_runtime
            try:
                broker_runtime.ensure_importable()
            except RuntimeError:
                return  # брокер не развёрнут (чистый clone): LFS/hook проверки выше обязательны
            from editor_broker.git_state import GitState
            changed = b"\0changed-pixels" * 70000
            (agent / "image.tga").write_bytes(changed)
            checkpoint = GitState(agent).checkpoint(base / "state", "lfs-fixture")
            saved = subprocess.check_output(["git", "-C", str(main), "cat-file", "-p", checkpoint["sha"] + ":image.tga"])
            self.assertTrue(saved.startswith(b"version https://git-lfs.github.com/spec/v1"))
            worker_state = GitState(worker)
            self.assertEqual(worker_state.changed_paths(worker_state.head()), [])
            worker_state.switch_detached(checkpoint["sha"])
            self.assertEqual((worker / "image.tga").read_bytes(), changed)



@unittest.skipUnless(HAS_LFS, "git-lfs недоступен")
class LfsRewriteMergeHookTests(unittest.TestCase):
    """Регрессия 4567: rebase/merge в main оставляли новые LFS-файлы указателями."""

    def fixture(self, base):
        main, worker, agent = base / "main", base / "worker", base / "agent"
        main.mkdir()
        # Пустой core.hooksPath заставляет git-lfs ставить свои хуки в корень рабочей копии;
        # контроль «без хуков» использует отдельный пустой каталог вне репозитория.
        nohooks = base / "nohooks"
        nohooks.mkdir()
        self.bare = "core.hooksPath=" + nohooks.as_posix()
        def git(root, *args):
            return policy.git(root, *args)
        git(main, "init", "-b", "dev")
        git(main, "config", "user.name", "LFS fixture")
        git(main, "config", "user.email", "fixture@example.invalid")
        policy.filters(main, "--local", True)
        (main / ".gitattributes").write_text("*.tga filter=lfs diff=lfs merge=lfs -text\n")
        tools = main / "Tools/agents"
        tools.mkdir(parents=True)
        (tools / "lfs-worktree-policy.json").write_text(json.dumps({"workerRegistry": ".agent-state/editor-broker/config.json"}))
        shutil.copy2(Path(__file__).parents[1] / "configure_lfs_worktree.py", tools / "configure_lfs_worktree.py")
        hooks = main / ".githooks"
        hooks.mkdir()
        for name in ("post-checkout", "post-merge", "post-rewrite", "pre-push"):
            shutil.copy2(Path(__file__).parents[3] / ".githooks" / name, hooks / name)
            (hooks / name).chmod(0o755)
        (main / "a.txt").write_text("base\n")
        (main / "seed.tga").write_bytes(b"\0seed" * 1000)
        git(main, "add", ".")
        git(main, "-c", self.bare, "commit", "-m", "base")
        git(main, "worktree", "add", "--detach", str(worker), "HEAD")
        registry = main / ".agent-state/editor-broker/config.json"
        registry.parent.mkdir(parents=True)
        registry.write_text(json.dumps({"editor_root": str(worker)}))
        (main / ".git/info/exclude").write_text(".agent-state/\n")
        self.assertEqual(policy.apply(main, install_defaults=True)["hydration"], "full_after_checkout")
        git(main, "config", "core.hooksPath", ".githooks")
        # Ветка с новым LFS-файлом: объект попадает в локальный кеш clean-фильтром.
        git(main, "checkout", "-b", "feature")
        payload = b"\0" + b"lobby-pixels" * 90000
        (main / "big.tga").write_bytes(payload)
        git(main, "add", "big.tga")
        git(main, "commit", "-m", "lfs asset")
        git(main, "checkout", "dev")
        self.assertFalse((main / "big.tga").exists())
        git(main, "worktree", "add", "--detach", str(agent), "dev")
        return git, main, worker, agent, payload

    def test_rebase_hydrates_main_agent_keeps_pointer(self):
        with tempfile.TemporaryDirectory() as temporary:
            git, main, worker, agent, payload = self.fixture(Path(temporary))
            (main / "a.txt").write_text("dev moves\n")
            git(main, "commit", "-am", "dev moves")
            git(main, "checkout", "feature")
            self.assertEqual((main / "big.tga").read_bytes(), payload)
            original = git(main, "rev-parse", "HEAD")
            # Без хуков воспроизводится дефект: коммит ветки переписан, файл — указатель.
            git(main, "-c", self.bare, "rebase", "dev")
            self.assertTrue((main / "big.tga").read_bytes().startswith(POINTER))
            git(main, "-c", self.bare, "reset", "--hard", "ORIG_HEAD")
            git(main, "rebase", "dev")
            self.assertEqual((main / "big.tga").read_bytes(), payload)
            self.assertEqual(git(main, "status", "--porcelain"), "")
            git(main, "commit", "--amend", "-m", "lfs asset amended")
            self.assertEqual((main / "big.tga").read_bytes(), payload)
            # Агент (hydration skip): указатель остаётся, worktree config не меняется.
            git(agent, "checkout", "--detach", original)
            before = git(agent, "config", "--worktree", "--list")
            git(agent, "rebase", "dev")
            self.assertTrue((agent / "big.tga").read_bytes().startswith(POINTER))
            self.assertEqual(git(agent, "config", "--worktree", "--list"), before)
            self.assertEqual(policy.hydrate(agent)["hydrated"], False)

    def test_merge_and_pull_hydrate_main_agent_keeps_pointer(self):
        with tempfile.TemporaryDirectory() as temporary:
            git, main, worker, agent, payload = self.fixture(Path(temporary))
            # Fast-forward: без хуков указатель, с post-merge — настоящие байты.
            git(main, "-c", self.bare, "merge", "--ff-only", "feature")
            self.assertTrue((main / "big.tga").read_bytes().startswith(POINTER))
            git(main, "-c", self.bare, "reset", "--hard", "ORIG_HEAD")
            git(main, "merge", "--ff-only", "feature")
            self.assertEqual((main / "big.tga").read_bytes(), payload)
            # Настоящий merge через pull из локального репозитория (без сети).
            git(main, "reset", "--hard", "HEAD~1")
            (main / "a.txt").write_text("dev moves\n")
            git(main, "commit", "-am", "dev moves")
            git(main, "pull", "--no-rebase", "--no-edit", ".", "feature")
            self.assertEqual((main / "big.tga").read_bytes(), payload)
            self.assertEqual(git(main, "status", "--porcelain"), "")
            # Агент: merge оставляет указатель, worktree config не меняется.
            before = git(agent, "config", "--worktree", "--list")
            git(agent, "merge", "--ff-only", "feature")
            self.assertTrue((agent / "big.tga").read_bytes().startswith(POINTER))
            self.assertEqual(git(agent, "config", "--worktree", "--list"), before)


if __name__ == "__main__":
    unittest.main()
