"""Инфраструктурные проверки awake. Запуск:
python -B -X utf8 -m unittest discover -s Tools/experts/tests -v
"""

import hashlib
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path

TOOLS = Path(__file__).resolve().parent.parent
AWAKE = TOOLS / "awake.py"
FIXTURES = Path(__file__).resolve().parent / "fixtures"
SECRETS = ("LEASE-TOKEN-SECRET", "MERGE-TOKEN-SECRET", "SESSION-SECRET-1", "INBOX BODY SECRET", "broadcast body", "LONG SPEC BODY")


def git(repo, *args):
    subprocess.run(["git", "-c", "user.name=t", "-c", "user.email=t@t", "-c", "commit.gpgsign=false", *args],
                   cwd=repo, check=True, capture_output=True)


def tree_state(root):
    state = {}
    for path in sorted(Path(root).rglob("*")):
        if ".git" in path.relative_to(root).parts:
            continue
        stat = path.stat()
        digest = hashlib.sha256(path.read_bytes()).hexdigest() if path.is_file() else "dir"
        state[path.relative_to(root).as_posix()] = (digest, stat.st_mtime_ns)
    return state


class AwakeTest(unittest.TestCase):
    def setUp(self):
        self.tmp = Path(tempfile.mkdtemp(prefix="awake test "))  # пробел в пути намеренно
        self.repo = self.tmp / "repo"
        self.repo.mkdir()
        git(self.repo, "init", "-q", "-b", "main")
        (self.repo / "docs").mkdir()
        (self.repo / "docs" / "audio.md").write_text("v1\n", encoding="utf-8")
        git(self.repo, "add", "-A")
        git(self.repo, "commit", "-q", "-m", "base")
        self.base = subprocess.run(["git", "rev-parse", "HEAD"], cwd=self.repo, capture_output=True, text=True).stdout.strip()
        self.ws = self.repo / "experts" / "audio-assets"
        shutil.copytree(FIXTURES / "audio-assets", self.ws)
        manifest = json.loads((self.ws / "expert.json").read_text(encoding="utf-8"))
        manifest["reviewed_sha"] = self.base
        (self.ws / "expert.json").write_text(json.dumps(manifest), encoding="utf-8")

    def tearDown(self):
        shutil.rmtree(self.tmp, ignore_errors=True)

    def awake(self, *extra, status="status.json", inbox="inbox.json"):
        args = [sys.executable, "-B", "-X", "utf8", str(AWAKE), str(self.ws), "--repo", str(self.repo)]
        if status:
            args += ["--status-json", str(FIXTURES / status)]
        if inbox:
            args += ["--inbox-json", str(FIXTURES / inbox)]
        proc = subprocess.run([*args, *extra], capture_output=True)
        return proc.returncode, proc.stdout.decode("utf-8")

    def test_projection_of_live_state(self):
        code, out = self.awake()
        self.assertEqual(code, 0, out)
        self.assertIn("Один владелец настроек импорта", out)  # AGENT.md целиком
        self.assertIn("`loudness` running", out)
        self.assertIn("влиты: `import-rules` 1111111111", out)
        self.assertIn("evidence влитых доступно 0 из 1; НЕДОСТУПНО: import-rules", out)
        self.assertIn("- `audio-bank-format` r1 agreed; ACK: build-pipeline@9", out)
        self.assertIn("`build-hooks` r3 agreed, владелец build-pipeline: ACK нашей задачи; loudness нужна r3", out)
        self.assertIn("`loudness` ждёт build-pipeline/cache ≥ merged: сейчас verified", out)
        self.assertIn("build-pipeline/`bundle` planned (owner build): after audio-assets/loudness ≥ merged; needs audio-bank-format r1", out)
        self.assertNotIn("`old`", out)  # merged чужой этап не шумит
        self.assertIn("- build-pipeline/`docs-sync` running (owner build): `docs/**`", out)
        self.assertNotIn("`src/audiox/**`", out)  # соседний префикс не считается пересечением
        self.assertNotIn("unrelated", out)
        self.assertIn("writes `src/audio/loudness/**`, `docs/audio.md`; контракты build-hooks r3; после build-pipeline/cache≥merged", out)
        self.assertIn("pending 7, адресных 1", out)
        self.assertIn("#42 contract_question от build-pipeline", out)
        self.assertNotIn("#40", out)
        self.assertIn("2026-10-02-import-rules.md: Правила импорта согласованы", out)
        self.assertIn("`bank-split`", out)

    def test_never_prints_secrets_or_bodies(self):
        code, out = self.awake()
        self.assertEqual(code, 0)
        for secret in SECRETS:
            self.assertNotIn(secret, out)

    def test_hub_unavailable_keeps_static_context(self):
        code, out = self.awake(status="missing-status.json", inbox="missing-inbox.json")
        self.assertEqual(code, 0, out)
        self.assertIn("ХАБ НЕДОСТУПЕН", out)
        self.assertIn("Один владелец настроек импорта", out)
        self.assertIn("`audio-assets`: недоступен", out)
        self.assertNotIn("Мои контракты", out)  # неизвестное состояние не выдумывается

    def test_no_hub_flag(self):
        code, out = self.awake("--no-hub", status=None, inbox=None)
        self.assertEqual(code, 0, out)
        self.assertIn("Хаб отключён флагом --no-hub", out)

    def test_freshness_reports_commits_in_watched_paths(self):
        code, out = self.awake()
        self.assertIn("Коммитов в моих путях после сверки нет", out)
        (self.repo / "docs" / "audio.md").write_text("v2\n", encoding="utf-8")
        (self.repo / "other.txt").write_text("x\n", encoding="utf-8")
        git(self.repo, "add", "docs/audio.md", "other.txt")
        git(self.repo, "commit", "-q", "-m", "change audio doc")
        code, out = self.awake()
        self.assertIn("ВНИМАНИЕ: 1 коммитов в моих путях после сверки", out)
        self.assertIn("change audio doc", out)

    def test_budget_trims_optional_parts_with_counts(self):
        lines = "\n".join(f"{i}. `item-{i}` — длинное описание кандидата бэклога на кириллице" for i in range(1, 400))
        (self.ws / "backlog.md").write_text(lines + "\n", encoding="utf-8")
        for i in range(30):
            (self.ws / "journal" / f"2026-09-{i:02d}-note.md").write_text(f"# Запись {i}\n", encoding="utf-8")
        code, out = self.awake("--max-bytes", "6000")
        self.assertEqual(code, 0, out)
        self.assertLessEqual(len(out.encode("utf-8")), 6000)
        self.assertIn("Один владелец настроек импорта", out)
        self.assertRegex(out, r"скрыто строк: \d+; полностью: experts/audio-assets/backlog.md")
        # урезается крупнейший блок (backlog), короткий журнал сохраняется
        self.assertIn("2026-09-29-note.md: Запись 29", out)
        code, out = self.awake()
        self.assertLessEqual(len(out.encode("utf-8")), 24576)
        self.assertIn("старше: 21 записей", out)

    def test_mandatory_over_budget_fails_explicitly(self):
        (self.ws / "AGENT.md").write_text("Инвариант. " * 2000, encoding="utf-8")
        code, out = self.awake()
        self.assertEqual(code, 2)
        self.assertIn("mandatory_context_over_budget", out)
        self.assertIn("прочитай experts/audio-assets/AGENT.md напрямую", out)
        self.assertNotIn("Инвариант. Инвариант.", out)

    def test_expert_without_hub_tasks(self):
        manifest = json.loads((self.ws / "expert.json").read_text(encoding="utf-8"))
        manifest["tasks"] = []
        (self.ws / "expert.json").write_text(json.dumps(manifest), encoding="utf-8")
        code, out = self.awake()
        self.assertEqual(code, 0, out)
        self.assertIn("задач в хабе нет", out)
        self.assertIn("у эксперта нет задач в хабе", out)
        self.assertNotIn("#42", out)

    def hook(self, payload, block=None):
        args = [sys.executable, "-B", "-X", "utf8", str(AWAKE), "--hook", *([block] if block else []),
                "--status-json", str(FIXTURES / "status.json"), "--inbox-json", str(FIXTURES / "inbox.json")]
        proc = subprocess.run(args, input=json.dumps(payload).encode("utf-8"), capture_output=True)
        return proc.returncode, proc.stdout.decode("ascii")

    def test_hook_injects_compact_package_for_expert_agent(self):
        code, out = self.hook({"agent_type": "audio-assets-expert", "source": "compact", "cwd": str(self.repo)})
        self.assertEqual(code, 0)
        context = json.loads(out)["hookSpecificOutput"]["additionalContext"]
        self.assertLessEqual(len(context), 9500)
        self.assertIn("Один владелец настроек импорта", context)
        self.assertIn("Сжатый пакет SessionStart (compact)", context)
        for secret in SECRETS:
            self.assertNotIn(secret, context)

    def test_hook_blocks_deliver_full_package(self):
        payload = {"agent_type": "audio-assets-expert", "source": "startup", "cwd": str(self.repo)}
        blocks = {}
        for block in ("role", "tasks", "contracts", "status", "backlog"):
            code, out = self.hook(payload, block)
            self.assertEqual(code, 0)
            blocks[block] = json.loads(out)["hookSpecificOutput"]["additionalContext"]
            self.assertLessEqual(len(blocks[block]), 9500)
        self.assertIn("Один владелец настроек импорта", blocks["role"])
        self.assertIn("вызывать awake вручную не нужно", blocks["role"])
        self.assertIn("блок `role` (1/5)", blocks["role"])
        self.assertIn("## Входы для ТЗ", blocks["tasks"])
        self.assertIn("## Мои контракты", blocks["contracts"])
        self.assertIn("build-pipeline/`docs-sync` running", blocks["contracts"])
        self.assertIn("## Inbox", blocks["status"])
        self.assertIn("`bank-split`", blocks["backlog"])
        # каждый раздел полного пакета — ровно в одном блоке
        full = self.awake()[1]
        expected = sorted(line for line in full.splitlines() if line.startswith("## "))
        delivered = sorted(line for text in blocks.values() for line in text.splitlines() if line.startswith("## "))
        self.assertEqual(expected, delivered)
        joined = "".join(blocks.values())
        for secret in SECRETS:
            self.assertNotIn(secret, joined)

    def test_hook_is_silent_for_other_sessions(self):
        for payload in ({"agent_type": "Explore", "cwd": str(self.repo)}, {"cwd": str(self.repo)},
                        {"agent_type": "unknown-expert", "cwd": str(self.repo)}):
            code, out = self.hook(payload)
            self.assertEqual((code, out), (0, ""), payload)

    def test_hook_falls_back_when_mandatory_part_overflows(self):
        (self.ws / "AGENT.md").write_text("Инвариант. " * 2000, encoding="utf-8")
        code, out = self.hook({"agent_type": "audio-assets-expert", "cwd": str(self.repo)})
        self.assertEqual(code, 0)
        context = json.loads(out)["hookSpecificOutput"]["additionalContext"]
        self.assertIn("mandatory_context_over_budget", context)
        self.assertIn("Запусти вручную", context)

    def test_free_text_evidence_is_clipped_not_marked_missing(self):
        status = json.loads((FIXTURES / "status.json").read_text(encoding="utf-8"))
        stage = status["result"]["snapshot"]["tasks"][0]["stages"][1]
        stage["evidence"] = "Прогон без отчёта: " + "длинное описание " * 40
        path = self.tmp / "status-text.json"
        path.write_text(json.dumps(status, ensure_ascii=False), encoding="utf-8")
        args = [sys.executable, "-B", "-X", "utf8", str(AWAKE), str(self.ws), "--repo", str(self.repo),
                "--status-json", str(path), "--inbox-json", str(FIXTURES / "inbox.json")]
        out = subprocess.run(args, capture_output=True).stdout.decode("utf-8")
        self.assertIn("evidence (описание в хабе): Прогон без отчёта:", out)
        self.assertNotIn("НЕДОСТУПНО в этой машине: Прогон", out)
        self.assertNotIn("длинное описание " * 12, out)

    def test_invalid_manifest_is_controlled_error(self):
        (self.ws / "expert.json").write_text('{"schema_version": 1, "id": "Bad Id", "extra": 1}', encoding="utf-8")
        code, out = self.awake()
        self.assertEqual(code, 2)
        self.assertIn("manifest_invalid", out)
        self.assertNotIn("Traceback", out)

    def test_read_only_and_no_bytecode(self):
        before = tree_state(self.repo)
        tools_before = tree_state(TOOLS)
        self.awake()
        self.awake(status="missing-status.json")
        self.assertEqual(before, tree_state(self.repo))
        self.assertEqual(tools_before, tree_state(TOOLS))
        self.assertFalse(list(TOOLS.rglob("__pycache__")))

    def test_core_has_no_domain_names(self):
        source = AWAKE.read_text(encoding="utf-8")
        for word in ("audio", "vr-test-stand", "Unity", "PlayLaunch", "Mirror", "Quest", "weapon"):
            self.assertIsNone(re.search(re.escape(word), source, re.IGNORECASE), word)


if __name__ == "__main__":
    unittest.main()
