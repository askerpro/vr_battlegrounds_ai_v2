"""Регрессии SessionStart: роль Codex, совместимость Claude и только чтение."""

import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile
import unittest


REPO = Path(__file__).resolve().parents[3]
HOOK = REPO / ".codex" / "hooks" / "session_context.py"
RULES = REPO / ".agents" / "rules" / "terminal.md"
MARKER = "## Редактирование файлов и передача кода (находки 2026-10-02)"
ROLE = "You are the avatar body (avatar-ik) expert. Общайся по-русски.\nStartup: python -B -X utf8 Tools/experts/awake.py experts/avatar-ik\n"


class SessionContextTest(unittest.TestCase):
    def setUp(self):
        self.tmp = tempfile.TemporaryDirectory(prefix="session hook test ")
        self.repo = Path(self.tmp.name) / "repo"
        self.script = self.repo / ".codex" / "hooks" / "session_context.py"
        self.script.parent.mkdir(parents=True)
        shutil.copyfile(HOOK, self.script)
        self.rules = self.repo / ".agents" / "rules" / "terminal.md"
        self.rules.parent.mkdir(parents=True)
        shutil.copyfile(RULES, self.rules)
        self.settings = self.repo / ".claude" / "settings.local.json"
        self.settings.parent.mkdir()
        self.settings.write_text('{"agent":"avatar-ik-expert"}', encoding="utf-8")
        self.profile = self.repo / ".codex" / "agents" / "avatar-ik-expert.toml"
        self.profile.parent.mkdir()
        self.profile.write_text('name = "avatar-ik-expert"\ndescription = "Эксперт тела аватара"\ndeveloper_instructions = """' + ROLE + '"""\n', encoding="utf-8")
        self.terminal = self.rules.read_text(encoding="utf-8").split(MARKER, 1)[1]
        self.terminal = MARKER + self.terminal
        self.payload = {"hook_event_name": "SessionStart", "source": "startup", "cwd": str(self.repo), "session_id": "offline-only"}

    def tearDown(self):
        self.tmp.cleanup()

    def invoke(self, payload=None, flag=True, audited=False):
        argv = [str(self.script), *(["--codex-main-role"] if flag else [])]
        command = [sys.executable, "-B", "-X", "utf8", *argv]
        if audited:
            # Проверка реальных side effects, не подмена зависимостей хука.
            wrapper = "import sys,runpy,os\ndef guard(event,args):\n if event.startswith(('subprocess.','os.spawn','os.system')): raise RuntimeError('external operation')\n if event=='open' and ((isinstance(args[1],str) and any(c in args[1] for c in 'wax+')) or (isinstance(args[2],int) and args[2] & (os.O_WRONLY|os.O_RDWR))): raise RuntimeError('write operation')\nsys.addaudithook(guard)\nsys.argv=sys.argv[1:]\nrunpy.run_path(sys.argv[0],run_name='__main__')"
            command = [sys.executable, "-B", "-X", "utf8", "-c", wrapper, *argv]
        proc = subprocess.run(command, input=json.dumps(self.payload if payload is None else payload).encode("utf-8"), capture_output=True, cwd=self.repo, timeout=10)
        self.assertEqual(proc.returncode, 0, proc.stderr.decode("utf-8", "replace"))
        self.assertEqual(proc.stderr, b"")
        context = json.loads(proc.stdout)["hookSpecificOutput"]["additionalContext"]
        return context, proc.stdout

    def assert_diagnostic(self, context):
        self.assertIn("Роль Codex не загружена", context)
        self.assertIn(MARKER, context)
        self.assertNotIn("SETTINGS-SECRET", context)
        self.assertNotIn(ROLE, context)

    def test_role_is_materialized_before_terminal_for_main_start_sources(self):
        for source in ("startup", "resume", "clear", "compact"):
            with self.subTest(source=source):
                context, _ = self.invoke(dict(self.payload, source=source))
                self.assertIn(ROLE, context)
                self.assertIn("Эксперт тела аватара", context)
                self.assertLess(context.index(ROLE), context.index(MARKER))
                self.assertIn("только основной сессии", context)
                self.assertIn("Делегированные", context)

    def test_default_claude_output_is_byte_identical(self):
        _, raw = self.invoke(flag=False)
        expected = json.dumps({"hookSpecificOutput": {"hookEventName": "SessionStart", "additionalContext": self.terminal}}, ensure_ascii=True) + os.linesep
        self.assertEqual(raw, expected.encode("ascii"))

    def test_no_assignment_preserves_existing_role(self):
        for value in ({}, {"agent": ""}, {"agent": None}):
            self.settings.write_text(json.dumps(value), encoding="utf-8")
            self.assertEqual(self.invoke()[0], self.terminal)
        self.settings.unlink()
        self.assertEqual(self.invoke()[0], self.terminal)

    def test_explicit_delegation_fork_and_unknown_events_do_not_load_role(self):
        cases = [dict(self.payload, source="fork"), dict(self.payload, source="unknown"),
                 dict(self.payload, agent_id="child"), dict(self.payload, agent_type="avatar-ik-expert"),
                 dict(self.payload, hook_event_name="SubagentStart"), dict(self.payload, hook_event_name="Unknown"), {}]
        for payload in cases:
            with self.subTest(payload=payload):
                self.assertEqual(self.invoke(payload)[0], self.terminal)

    def test_malformed_event_values_do_not_break_terminal_context(self):
        for payload in ([], "bad event", None, dict(self.payload, source=[]), dict(self.payload, source={}),
                        dict(self.payload, agent_id=None), dict(self.payload, agent_type="")):
            with self.subTest(payload=payload):
                actual = payload if payload is not None else []
                self.assertEqual(self.invoke(actual)[0], self.terminal)

    def test_bad_settings_and_traversal_are_redacted(self):
        for raw in ('{"agent":"SETTINGS-SECRET"', '["SETTINGS-SECRET"]', '{"agent":42}',
                    '{"agent":"../SETTINGS-SECRET"}', '{"agent":"..\\\\SETTINGS-SECRET"}',
                    '{"agent":"C:/SETTINGS-SECRET"}', '{"agent":"avatar-ik-expert.toml"}'):
            with self.subTest(raw=raw):
                self.settings.write_text(raw, encoding="utf-8")
                self.assert_diagnostic(self.invoke()[0])

    def test_missing_malformed_or_empty_profile_is_diagnostic(self):
        self.profile.unlink()
        self.assert_diagnostic(self.invoke()[0])
        for raw in ('developer_instructions = "SETTINGS-SECRET', 'developer_instructions = ""', 'description = "only"', 'developer_instructions = 4'):
            self.profile.write_text(raw, encoding="utf-8")
            self.assert_diagnostic(self.invoke()[0])

    def test_symlink_profile_outside_repo_is_not_read(self):
        outside = Path(self.tmp.name) / "outside.toml"
        outside.write_text('developer_instructions = "SETTINGS-SECRET"', encoding="utf-8")
        self.profile.unlink()
        try:
            self.profile.symlink_to(outside)
        except OSError as exc:
            self.skipTest(f"ОС не разрешила symlink: {exc.__class__.__name__}")
        self.assert_diagnostic(self.invoke()[0])

    @unittest.skipUnless(os.name == "nt", "NTFS junction проверяется на Windows")
    def test_junction_profile_directory_outside_repo_is_not_read(self):
        outside = Path(self.tmp.name) / "outside agents"
        outside.mkdir()
        (outside / self.profile.name).write_text('developer_instructions = "SETTINGS-SECRET"', encoding="utf-8")
        self.profile.unlink()
        self.profile.parent.rmdir()
        proc = subprocess.run(["cmd.exe", "/c", "mklink", "/J", str(self.profile.parent), str(outside)], capture_output=True)
        self.assertEqual(proc.returncode, 0, proc.stderr.decode("utf-8", "replace"))
        try:
            self.assert_diagnostic(self.invoke()[0])
        finally:
            # Удаляется сама junction; внешний каталог и файл сохраняются.
            os.rmdir(self.profile.parent)
        self.assertTrue((outside / self.profile.name).is_file())

    def test_unicode_role_survives_ascii_json(self):
        context, raw = self.invoke()
        raw.decode("ascii")
        self.assertIn("Общайся по-русски", context)

    def test_oversized_role_is_diagnostic_without_partial_role(self):
        self.profile.write_text('developer_instructions = """' + ROLE * 40 + '"""', encoding="utf-8")
        context, _ = self.invoke()
        self.assert_diagnostic(context)
        self.assertLessEqual(len(context), 12000)

    def test_non_utf8_or_oversized_assignment_is_diagnostic(self):
        for raw in (b'\xff', b'{"agent":"' + b'A' * 20000 + b'"}'):
            self.settings.write_bytes(raw)
            self.assert_diagnostic(self.invoke()[0])

    def test_large_terminal_keeps_whole_role_and_bounded_preview(self):
        self.rules.write_text(MARKER + "\n" + "Терминал. " * 10000, encoding="utf-8")
        context, _ = self.invoke()
        self.assertIn(ROLE, context)
        self.assertIn(".agents/rules/terminal.md", context)
        self.assertLessEqual(len(context), 12000)

    def test_readonly_no_external_subprocess_or_bytecode(self):
        before = {p.relative_to(self.repo): (hashlib.sha256(p.read_bytes()).hexdigest(), p.stat().st_mtime_ns) for p in self.repo.rglob("*") if p.is_file()}
        context, _ = self.invoke(audited=True)
        self.assertIn(ROLE, context)
        after = {p.relative_to(self.repo): (hashlib.sha256(p.read_bytes()).hexdigest(), p.stat().st_mtime_ns) for p in self.repo.rglob("*") if p.is_file()}
        self.assertEqual(before, after)
        self.assertFalse(list(self.repo.rglob("__pycache__")))


if __name__ == "__main__":
    unittest.main()
