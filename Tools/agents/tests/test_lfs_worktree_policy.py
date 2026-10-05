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
            if not (Path(__file__).parents[1] / "editor_broker/git_state.py").is_file():
                return
            sys.path.insert(0, str(Path(__file__).parents[1]))
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


if __name__ == "__main__":
    unittest.main()
