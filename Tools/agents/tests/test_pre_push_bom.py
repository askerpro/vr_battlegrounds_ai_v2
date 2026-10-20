"""Контракт .githooks/pre-push: BOM для Tools/**/*.ps1|psm1 и совместный отчёт с EOL.

Windows PowerShell 5.1 читает .ps1/.psm1 без BOM как ANSI, поэтому хук отклоняет
отправку blob с байтами >= 0x80 без EF BB BF. Хук вызывается напрямую со stdin
формата pre-push; git-lfs подменён заглушкой, чтобы не трогать сеть и LFS.
"""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest


REPO_ROOT = Path(__file__).parents[3]
HOOK = REPO_ROOT / ".githooks" / "pre-push"
ATTRIBUTES = REPO_ROOT / ".gitattributes"
ZERO = "0" * 40
BOM = b"\xef\xbb\xbf"
CYRILLIC = "Write-Host 'Проверка'\n".encode("utf-8")
ASCII = b"Write-Host 'check'\n"


def find_shells():
    """POSIX sh (на Windows — sh из Git for Windows) и, если есть, dash для строгого POSIX."""
    shells = []
    sh = shutil.which("sh")
    if sh is None and os.name == "nt":
        exec_path = subprocess.check_output(["git", "--exec-path"], text=True).strip()
        candidate = Path(exec_path).parents[2] / "usr" / "bin" / "sh.exe"
        if candidate.exists():
            sh = str(candidate)
    if sh:
        shells.append(sh)
    dash = shutil.which("dash")
    if dash:
        shells.append(dash)
    return shells


SHELLS = find_shells()


@unittest.skipUnless(SHELLS and shutil.which("git"), "нет sh или git")
class PrePushBomTests(unittest.TestCase):
    def setUp(self):
        self._tmp = tempfile.TemporaryDirectory()
        base = Path(self._tmp.name)
        self.repo = base / "repo"
        self.repo.mkdir()
        nohooks = base / "nohooks"
        nohooks.mkdir()
        self.nohooks = nohooks.as_posix()
        stub_dir = base / "stub"
        stub_dir.mkdir()
        self.lfs_mark = base / "lfs-called"
        # Заглушка git-lfs: отмечает вызов и поглощает stdin; настоящий git-lfs не нужен.
        stub = stub_dir / "git-lfs"
        stub.write_bytes(b'#!/bin/sh\ncat >/dev/null\necho "$*" >> "$LFS_STUB_MARK"\nexit 0\n')
        stub.chmod(0o755)
        self.env = dict(os.environ)
        self.env["PATH"] = str(stub_dir) + os.pathsep + self.env.get("PATH", "")
        self.env["LFS_STUB_MARK"] = str(self.lfs_mark)
        self.env["LC_ALL"] = "C"
        self.git("init", "-q")
        self.git("config", "user.name", "BOM fixture")
        self.git("config", "user.email", "fixture@example.invalid")
        hooks = self.repo / ".githooks"
        hooks.mkdir()
        shutil.copy2(HOOK, hooks / "pre-push")
        self.base = self.commit("base", {
            ".gitattributes": ATTRIBUTES.read_bytes(),
            "Tools/e2e/Existing.ps1": BOM + CYRILLIC,
            "Tools/e2e/Legacy.ps1": CYRILLIC,  # исторический файл без BOM: не в диапазоне
        })

    def tearDown(self):
        self._tmp.cleanup()

    def git(self, *args, data=None):
        return subprocess.run(
            ["git", "-c", "core.hooksPath=" + self.nohooks, *args],
            cwd=self.repo, input=data, capture_output=True, check=True, env=self.env,
        ).stdout.decode("utf-8").strip()

    def commit(self, message, files):
        """Коммит точных raw blob (без фильтров/EOL-нормализации); None — удаление пути."""
        for path, content in files.items():
            if content is None:
                self.git("update-index", "--force-remove", "--", path)
                continue
            sha = self.git("hash-object", "-w", "--no-filters", "--stdin", data=content)
            self.git("update-index", "--add", "--cacheinfo", "100644," + sha + "," + path)
        self.git("commit", "-q", "--no-verify", "-m", message)
        return self.git("rev-parse", "HEAD")

    def push(self, shell, commit, remote_sha):
        if self.lfs_mark.exists():
            self.lfs_mark.unlink()
        line = "refs/heads/feature %s refs/heads/feature %s\n" % (commit, remote_sha)
        result = subprocess.run(
            [shell, ".githooks/pre-push", "origin", "file:///nowhere"],
            cwd=self.repo, input=line.encode("ascii"), capture_output=True, env=self.env,
        )
        return result.returncode, result.stderr.decode("utf-8", "replace")

    def assert_ok(self, commit, remote_sha=None):
        for shell in SHELLS:
            with self.subTest(shell=shell):
                code, err = self.push(shell, commit, remote_sha or self.base)
                self.assertEqual(code, 0, err)
                self.assertNotIn("pre-push BOM", err)
                self.assertTrue(self.lfs_mark.exists(), "git lfs pre-push не вызван")

    def assert_rejected(self, commit, paths, remote_sha=None, absent=()):
        reports = []
        for shell in SHELLS:
            with self.subTest(shell=shell):
                code, err = self.push(shell, commit, remote_sha or self.base)
                self.assertEqual(code, 1, err)
                self.assertIn("pre-push BOM", err)
                self.assertIn("UTF-8 с BOM", err)
                self.assertIn("Docs/agents/text-eol.md", err)
                for path in paths:
                    self.assertIn("  refs/heads/feature: " + path + "\n", err)
                for path in absent:
                    self.assertNotIn(path, err)
                self.assertFalse(self.lfs_mark.exists(), "git lfs pre-push вызван после отказа")
                reports.append(err)
        return reports

    def test_ascii_without_bom_ok(self):
        commit = self.commit("ascii", {"Tools/e2e/Plain.ps1": ASCII, "Tools/e2e/Plain.psm1": ASCII})
        self.assert_ok(commit)

    def test_cyrillic_with_bom_ok(self):
        commit = self.commit("bom", {
            "Tools/e2e/WithBom.ps1": BOM + CYRILLIC,
            "Tools/e2e/WithBom.psm1": BOM + CYRILLIC,
        })
        self.assert_ok(commit)

    def test_cyrillic_without_bom_rejected_case_insensitive(self):
        commit = self.commit("nobom", {
            "Tools/e2e/NoBom.ps1": CYRILLIC,
            "Tools/e2e/Module.PSM1": CYRILLIC,
            "Tools/e2e/Fine.ps1": BOM + CYRILLIC,
        })
        self.assert_rejected(
            commit, ["Tools/e2e/NoBom.ps1", "Tools/e2e/Module.PSM1"],
            absent=["Fine.ps1", "Legacy.ps1"],
        )

    def test_bom_stripped_on_modify_rejected(self):
        # Регрессия: редактор с charset = utf-8 снял BOM при сохранении.
        commit = self.commit("strip", {"Tools/e2e/Existing.ps1": CYRILLIC})
        self.assert_rejected(commit, ["Tools/e2e/Existing.ps1"])

    def test_outside_tools_not_checked(self):
        commit = self.commit("outside", {
            "Scripts/Other.ps1": CYRILLIC,
            "Assets/Tools/Nested.psm1": CYRILLIC,
            "Tools/e2e/notes.txt": CYRILLIC,
        })
        self.assert_ok(commit)

    def test_deletion_not_checked(self):
        commit = self.commit("delete", {"Tools/e2e/Legacy.ps1": None})
        self.assert_ok(commit)

    def test_new_branch_uses_merge_base_with_dev(self):
        self.git("update-ref", "refs/remotes/origin/dev", self.base)
        good = self.commit("good", {"Tools/e2e/Good.psm1": BOM + CYRILLIC})
        self.assert_ok(good, remote_sha=ZERO)
        bad = self.commit("bad", {"Tools/e2e/Bad.ps1": CYRILLIC})
        # Legacy.ps1 без BOM лежит до merge-base и не попадает в отчёт.
        self.assert_rejected(bad, ["Tools/e2e/Bad.ps1"], remote_sha=ZERO, absent=["Legacy.ps1"])

    def test_eol_and_bom_reported_together(self):
        commit = self.commit("both", {
            "Tools/e2e/Crlf.py": b"print('x')\r\n",
            "Tools/e2e/NoBom.psm1": CYRILLIC,
        })
        for err in self.assert_rejected(commit, ["Tools/e2e/NoBom.psm1"]):
            self.assertIn("pre-push EOL", err)
            self.assertIn("  refs/heads/feature: Tools/e2e/Crlf.py\n", err)


if __name__ == "__main__":
    unittest.main()
