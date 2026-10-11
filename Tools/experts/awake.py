"""awake: стартовый пакет эксперта из его рабочего места и живых источников.

Статичная часть (роль, границы, архитектурная карта, правила оркестрации) берётся
из AGENT.md рабочего места. Состояние вычисляется при каждом запуске: штатный
read-only `coordination.py status`, inbox владельца (без тел событий и без ACK),
git и собственные backlog/журнал эксперта. Инструмент ничего не пишет, не выдаёт
допуск, аренду или ownership; перед действием нужны штатные проверки AGENTS.md.
"""

import sys

sys.dont_write_bytecode = True

import argparse
import datetime as dt
import io
import json
import os
import re
import subprocess
from pathlib import Path

SCHEMA_VERSION = 1
DEFAULT_MAX_BYTES = 24576
HOOK_MAX_CHARS = 9500  # потолок additionalContext SessionStart — 10 000 символов
COMMON_FILE = "COMMON.md"  # общие правила всех экспертов рядом с рабочими местами
HUB_TIMEOUT_S = 30
GIT_TIMEOUT_S = 15
INBOX_LIMIT = 10
JOURNAL_LIMIT = 10
FRESHNESS_LIMIT = 8
ACTIVE_WRITES_LIMIT = 6
ID_RE = re.compile(r"^[a-z0-9][a-z0-9._-]{0,63}$")
SHA_RE = re.compile(r"^[0-9a-f]{40}$")
DATE_RE = re.compile(r"^\d{4}-\d{2}-\d{2}$")
PATH_RE = re.compile(r"^([A-Za-z]:[\\/]|/|\.{1,2}[\\/]|[\w.-]+[\\/])")
STOP_REASON_LIMIT = 100
EVIDENCE_TEXT_LIMIT = 140
MANIFEST_KEYS = {"schema_version", "id", "owner", "tasks", "reviewed_sha", "reviewed_at", "watch_paths"}
NOTICE = ("Пакет только для ориентации: не выдаёт допуск, аренду, ownership и не ACK-ает inbox. "
          "Перед действием — штатные проверки AGENTS.md (status/inbox/guard).")


class AwakeError(Exception):
    def __init__(self, code, message):
        super().__init__(message)
        self.code = code
        self.message = message


# --- рабочее место -------------------------------------------------------------

def load_manifest(workspace):
    path = workspace / "expert.json"
    try:
        raw = path.read_bytes()
    except OSError:
        raise AwakeError("manifest_missing", f"нет {path.as_posix()}")
    try:
        data = json.loads(raw.decode("utf-8"))
    except (UnicodeDecodeError, json.JSONDecodeError) as exc:
        raise AwakeError("manifest_invalid", f"{path.as_posix()}: не UTF-8 JSON ({exc.__class__.__name__})")
    problems = []
    if not isinstance(data, dict):
        raise AwakeError("manifest_invalid", f"{path.as_posix()}: ожидается объект")
    unknown = sorted(set(data) - MANIFEST_KEYS)
    missing = sorted(MANIFEST_KEYS - {"watch_paths"} - set(data))
    if unknown:
        problems.append("неизвестные поля " + ", ".join(unknown))
    if missing:
        problems.append("нет полей " + ", ".join(missing))
    if data.get("schema_version") != SCHEMA_VERSION:
        problems.append("schema_version должен быть 1")
    for key in ("id", "owner"):
        if not isinstance(data.get(key), str) or not ID_RE.match(data.get(key, "")):
            problems.append(f"{key}: ожидается id [a-z0-9._-]")
    tasks = data.get("tasks")
    if not isinstance(tasks, list) or not all(isinstance(t, str) and ID_RE.match(t) for t in tasks):
        problems.append("tasks: список task-id (может быть пустым)")
    if not isinstance(data.get("reviewed_sha"), str) or not SHA_RE.match(data.get("reviewed_sha", "")):
        problems.append("reviewed_sha: полный SHA")
    if not isinstance(data.get("reviewed_at"), str) or not DATE_RE.match(data.get("reviewed_at", "")):
        problems.append("reviewed_at: YYYY-MM-DD")
    watch = data.get("watch_paths", [])
    if not isinstance(watch, list) or not all(isinstance(p, str) and p and not p.startswith("/") and ".." not in p.split("/") for p in watch):
        problems.append("watch_paths: относительные пути/glob без ..")
    if problems:
        raise AwakeError("manifest_invalid", f"{path.as_posix()}: " + "; ".join(problems))
    data.setdefault("watch_paths", [])
    return data


def read_text(path):
    try:
        return path.read_text(encoding="utf-8").replace("\r\n", "\n").strip("\n")
    except FileNotFoundError:
        return None
    except (OSError, UnicodeDecodeError) as exc:
        raise AwakeError("workspace_unreadable", f"{path.as_posix()}: {exc.__class__.__name__}")


def journal_entries(workspace):
    folder = workspace / "journal"
    if not folder.is_dir():
        return [], 0
    files = sorted((p for p in folder.glob("*.md") if p.is_file()), key=lambda p: p.name, reverse=True)
    rows = []
    for p in files[:JOURNAL_LIMIT]:
        title = p.stem
        try:
            with p.open(encoding="utf-8") as handle:
                for line in handle:
                    if line.startswith("#"):
                        title = line.lstrip("#").strip()
                        break
        except (OSError, UnicodeDecodeError):
            title = p.stem + " (не читается)"
        rows.append(f"- {p.name}: {title}")
    return rows, max(0, len(files) - JOURNAL_LIMIT)


# --- внешние источники (только чтение) ----------------------------------------

def run(args, cwd, timeout):
    env = dict(os.environ, PYTHONDONTWRITEBYTECODE="1", GIT_OPTIONAL_LOCKS="0")
    proc = subprocess.run(args, cwd=str(cwd), capture_output=True, timeout=timeout, env=env)
    return proc.returncode, proc.stdout.decode("utf-8", "replace"), proc.stderr.decode("utf-8", "replace")


def git(repo, *args):
    try:
        code, out, _ = run(["git", "--no-pager", *args], repo, GIT_TIMEOUT_S)
    except (OSError, subprocess.TimeoutExpired):
        return None
    return out.strip() if code == 0 else None


def load_json_output(source, label):
    """source: (code, stdout, stderr) или текст файла. Возвращает (data, error)."""
    code, out, err = source
    try:
        data = json.loads(out)
    except json.JSONDecodeError:
        tail = (err or out).strip().splitlines()[-1:] or ["нет вывода"]
        return None, f"{label}: код {code}, {tail[0][:160]}"
    if not isinstance(data, dict) or not data.get("ok"):
        message = data.get("error") if isinstance(data, dict) else None
        return None, f"{label}: {str(message or 'ok=false')[:200]}"
    return data.get("result"), None


def hub_call(repo, args, override):
    if override is not None:
        try:
            return (0, Path(override).read_text(encoding="utf-8"), "")
        except OSError as exc:
            return (1, "", f"{override}: {exc.__class__.__name__}")
    tool = repo / "Tools" / "agents" / "coordination.py"
    if not tool.is_file():
        return (1, "", "нет Tools/agents/coordination.py")
    try:
        return run([sys.executable, "-B", "-X", "utf8", str(tool), *args], repo, HUB_TIMEOUT_S)
    except subprocess.TimeoutExpired:
        return (1, "", f"timeout {HUB_TIMEOUT_S}s")
    except OSError as exc:
        return (1, "", exc.__class__.__name__)


def coordination_mode(repo):
    common = git(repo, "rev-parse", "--path-format=absolute", "--git-common-dir")
    if not common:
        return "unknown"
    config = Path(common).parent / ".agent-state" / "coordination" / "config.json"
    try:
        mode = json.loads(config.read_text(encoding="utf-8")).get("mode")
    except FileNotFoundError:
        return "off"
    except (OSError, ValueError, AttributeError):
        return "unknown"
    return mode if mode in ("off", "enforced") else "unknown"


# --- проекции хаба --------------------------------------------------------------

def short(sha):
    return sha[:10] if isinstance(sha, str) and sha else "—"


def stage_line(stage):
    parts = [f"`{stage.get('id')}` {stage.get('state', '?')}"]
    for key, label in (("merged_sha", "merged"), ("accepted_sha", "accepted"), ("verified_sha", "verified")):
        if stage.get(key):
            parts.append(f"{label} {short(stage[key])}")
            break
    if stage.get("stop_reason") and stage.get("state") != "merged":
        parts.append(f"stop: {clip(str(stage['stop_reason']), STOP_REASON_LIMIT)}")
    return ", ".join(parts)


def clip(text, limit):
    text = " ".join(text.split())
    return text if len(text) <= limit else text[: limit - 1] + "…"


def is_path(text):
    """evidence в хабе бывает путём или свободным описанием прогона."""
    return isinstance(text, str) and bool(text) and not any(ch.isspace() for ch in text.strip()) and bool(PATH_RE.match(text.strip()))


PUBLISHED_DOCS_REF = "refs/heads/agents/status"


def docs_ref(repo, doc_path):
    """Документ задачи: в checkout, иначе опубликованная хабом копия в agents/status (только чтение)."""
    if not isinstance(doc_path, str) or not doc_path or doc_path.startswith("/") or ".." in doc_path.split("/"):
        return "docs —"
    if (repo / doc_path).is_file():
        return f"docs {doc_path}"
    if git(repo, "cat-file", "-e", f"{PUBLISHED_DOCS_REF}:{doc_path}") is not None:
        return f"docs {doc_path} (в checkout нет; опубликовано: `git show agents/status:{doc_path}`)"
    return f"docs {doc_path} (в checkout и agents/status нет)"


def evidence_note(text):
    if not isinstance(text, str) or not text.strip():
        return None
    if not is_path(text):
        return f"evidence (описание в хабе): {clip(text, EVIDENCE_TEXT_LIMIT)}"
    exists = Path(text.strip()).exists()
    return f"evidence {'доступно' if exists else 'НЕДОСТУПНО в этой машине'}: {text.strip()}"


def glob_scope(pattern):
    """Литеральный путь или каталог-префикс (до последнего / перед первым wildcard)."""
    cut = min((i for i, ch in enumerate(pattern) if ch in "*?["), default=None)
    if cut is None:
        return pattern, True
    return pattern[:pattern.rfind("/", 0, cut) + 1], False


def may_overlap(a, b):
    (pa, literal_a), (pb, literal_b) = glob_scope(a), glob_scope(b)
    if literal_a and literal_b:
        return pa == pb
    if literal_a:
        return pa.startswith(pb)
    if literal_b:
        return pb.startswith(pa)
    return pa.startswith(pb) or pb.startswith(pa)


def project_hub(snapshot, my_tasks, my_patterns):
    """Возвращает словарь проекций для моих задач из snapshot штатного status."""
    tasks = {t.get("task_id"): t for t in snapshot.get("tasks", []) if isinstance(t, dict)}
    contracts = {c.get("id"): c for c in snapshot.get("contracts", []) if isinstance(c, dict)}
    mine = set(my_tasks)

    def contract_current(cid):
        c = contracts.get(cid)
        if not c:
            return None, "нет в хабе"
        versions = c.get("versions") or []
        last = versions[-1] if versions else {}
        return c, f"r{len(versions)} {last.get('state', '?')}"

    own_tasks, waits, used, dependents, intrusions = [], [], {}, [], []
    owned_contracts = sorted(cid for cid, c in contracts.items() if c.get("owner_task") in mine)
    for tid in my_tasks:
        task = tasks.get(tid)
        if task is None:
            own_tasks.append({"task_id": tid, "missing": True})
            continue
        own_tasks.append(task)
        for stage in task.get("stages", []):
            spec = stage.get("spec") or {}
            for dep in (spec.get("after", []) or []) if stage.get("state") != "merged" else []:
                other = tasks.get(dep.get("task"), {})
                current = next((s.get("state") for s in other.get("stages", []) if s.get("id") == dep.get("stage")), "нет в хабе")
                waits.append(f"`{stage.get('id')}` ждёт {dep.get('task')}/{dep.get('stage')} ≥ {dep.get('state')}: сейчас {current}")
            for need in spec.get("needs", []) or []:
                cid = need.get("contract")
                if cid in owned_contracts:
                    continue
                used.setdefault(cid, []).append(f"{stage.get('id')} нужна r{need.get('revision')}")
    # Контракты, которые мы подтвердили (ACK) как потребитель, тоже используемые.
    for cid, c in contracts.items():
        if c.get("owner_task") in mine:
            continue
        for version in c.get("versions") or []:
            if any(t in mine for t in (version.get("acks") or {})):
                used.setdefault(cid, []).append("ACK нашей задачи")
                break
    for tid, task in sorted(tasks.items()):
        if tid in mine:
            continue
        for stage in task.get("stages", []):
            if stage.get("state") == "merged":
                continue
            spec = stage.get("spec") or {}
            reasons = [f"after {d.get('task')}/{d.get('stage')} ≥ {d.get('state')}" for d in spec.get("after", []) or [] if d.get("task") in mine]
            reasons += [f"needs {n.get('contract')} r{n.get('revision')}" for n in spec.get("needs", []) or [] if n.get("contract") in owned_contracts]
            if reasons:
                dependents.append(f"{tid}/`{stage.get('id')}` {stage.get('state', '?')} (owner {task.get('owner')}): " + "; ".join(reasons))
            hits = sorted({w for w in spec.get("writes", []) or [] if isinstance(w, str) for m in my_patterns if may_overlap(w, m)})
            if hits:
                shown = ", ".join(f"`{w}`" for w in hits[:3]) + (f" (+{len(hits) - 3})" if len(hits) > 3 else "")
                intrusions.append(f"{tid}/`{stage.get('id')}` {stage.get('state', '?')} (owner {task.get('owner')}): {shown}")

    owned_lines = []
    for cid in owned_contracts:
        c, cur = contract_current(cid)
        last = (c.get("versions") or [{}])[-1]
        acks = ", ".join(f"{t}@{r}" for t, r in sorted((last.get("acks") or {}).items())) or "нет"
        impl = f", implementation {short(last.get('implementation_sha'))}" if last.get("implementation_sha") else ""
        owned_lines.append(f"- `{cid}` {cur}{impl}; ACK: {acks}")
    used_lines = []
    for cid in sorted(used):
        c, cur = contract_current(cid)
        owner = c.get("owner_task") if c else "?"
        used_lines.append(f"- `{cid}` {cur}, владелец {owner}: " + "; ".join(sorted(set(used[cid]))))
    return {"own_tasks": own_tasks, "owned": owned_lines, "used": used_lines, "waits": waits,
            "dependents": dependents, "intrusions": intrusions}


def watch_patterns(own_tasks, manifest):
    patterns = set(manifest.get("watch_paths", []))
    for task in own_tasks:
        for stage in task.get("stages", []) if isinstance(task, dict) else []:
            patterns.update((stage.get("spec") or {}).get("writes", []) or [])
    return sorted(p for p in patterns if isinstance(p, str) and p and not p.startswith("/") and ".." not in p.split("/"))


# --- сборка пакета ------------------------------------------------------------

class Section:
    def __init__(self, title, lines, trim_rank=None, source=None):
        self.title = title
        self.lines = list(lines)
        self.trim_rank = trim_rank      # None — обязательная часть, иначе порядок урезания (меньше — раньше)
        self.source = source            # куда смотреть за скрытыми строками
        self.hidden = 0

    def render(self):
        body = list(self.lines)
        if self.hidden:
            body.append(f"- … скрыто строк: {self.hidden}; полностью: {self.source}")
        return [f"## {self.title}", *body, ""]


def build(workspace, repo, manifest, status_override, inbox_override, use_hub, now):
    sections = []
    agent_md = read_text(workspace / "AGENT.md")
    if agent_md is None:
        raise AwakeError("agent_missing", f"нет {(workspace / 'AGENT.md').as_posix()}")
    ws_rel = workspace_rel(workspace, repo)

    header = [f"# awake `{manifest['id']}` — {now}", "", f"> {NOTICE}", ""]
    mode = coordination_mode(repo)

    hub_lines, hub = [], None
    if use_hub:
        result, error = load_json_output(hub_call(repo, ["status"], status_override), "status")
        snapshot = (result or {}).get("snapshot") if isinstance(result, dict) else None
        if error or not isinstance(snapshot, dict):
            hub_lines.append(f"- ХАБ НЕДОСТУПЕН: {error or 'нет snapshot'}. Состояние задач/контрактов неизвестно; не выводить его из файлов.")
        else:
            mine = [t for t in snapshot.get("tasks", []) if isinstance(t, dict) and t.get("task_id") in manifest["tasks"]]
            hub = project_hub(snapshot, manifest["tasks"], watch_patterns(mine, manifest))
    else:
        hub_lines.append("- Хаб отключён флагом --no-hub: состояние задач/контрактов неизвестно.")

    sections.append(Section("Роль и рабочее место", [agent_md, "", f"Рабочее место: `{ws_rel}/` (backlog.md, journal/). Режим координации: {mode}."]))
    common = read_text(workspace.parent / COMMON_FILE)
    if common:
        sections.append(Section("Общие правила экспертов", common.split("\n")))

    task_lines = list(hub_lines)
    if hub and not manifest["tasks"]:
        task_lines.append("- задач в хабе нет: зона ведётся через backlog; новая работа — новая задача по AGENTS.md")
    if hub:
        for task in hub["own_tasks"]:
            if task.get("missing"):
                task_lines.append(f"- `{task['task_id']}`: нет в хабе")
                continue
            task_lines.append(f"- `{task.get('task_id')}` owner {task.get('owner')}, rev {task.get('revision')}, worktree {task.get('worktree')}, {docs_ref(repo, task.get('doc_path'))}")
            merged = [s for s in task.get("stages", []) if s.get("state") == "merged"]
            if merged:
                # Влитые этапы — история: одной строкой, evidence только сводкой.
                task_lines.append("  - влиты: " + ", ".join(f"`{s.get('id')}` {short(s.get('merged_sha'))}" for s in merged))
                with_evidence = [s for s in merged if is_path(s.get("evidence"))]
                missing = [s.get("id") for s in with_evidence if not Path(s["evidence"].strip()).exists()]
                if with_evidence:
                    tail = f"; НЕДОСТУПНО: {', '.join(missing)}" if missing else ""
                    task_lines.append(f"    - evidence влитых доступно {len(with_evidence) - len(missing)} из {len(with_evidence)}{tail} (пути — coordination.py status)")
            for stage in task.get("stages", []):
                if stage.get("state") == "merged":
                    continue
                task_lines.append(f"  - {stage_line(stage)}")
                note = evidence_note(stage.get("evidence"))
                if note and stage.get("state") != "planned":
                    task_lines.append(f"    - {note}")
    sections.append(Section("Мои задачи и этапы (хаб, сейчас)", task_lines))

    if hub:
        active = []
        for task in hub["own_tasks"]:
            for stage in task.get("stages", []) if not task.get("missing") else []:
                if stage.get("state") in ("planned", "running", "verified", "accepted"):
                    spec = stage.get("spec") or {}
                    writes = spec.get("writes", []) or []
                    shown = ", ".join(f"`{w}`" for w in writes[:ACTIVE_WRITES_LIMIT])
                    more = f" (+{len(writes) - ACTIVE_WRITES_LIMIT})" if len(writes) > ACTIVE_WRITES_LIMIT else ""
                    needs = ", ".join(f"{n.get('contract')} r{n.get('revision')}" for n in spec.get("needs", []) or []) or "нет"
                    after = ", ".join(f"{a.get('task')}/{a.get('stage')}≥{a.get('state')}" for a in spec.get("after", []) or []) or "нет"
                    active.append(f"- `{task.get('task_id')}/{stage.get('id')}` {stage.get('state')}: writes {shown or 'нет'}{more}; контракты {needs}; после {after}")
        sections.append(Section("Входы для ТЗ субагентам (незавершённые этапы)", active or ["- незавершённых этапов нет"], 4, "coordination.py status"))
        sections.append(Section("Мои контракты", hub["owned"] or ["- нет"], 5, "coordination.py status"))
        sections.append(Section("Используемые чужие контракты", hub["used"] or ["- нет"], 5, "coordination.py status"))
        sections.append(Section("Чего жду от других", [f"- {w}" for w in hub["waits"]] or ["- нет"], 5, "coordination.py status"))
        sections.append(Section("Кто зависит от меня", [f"- {d}" for d in hub["dependents"]] or ["- нет"], 3, "coordination.py status"))
        sections.append(Section("Чужие незавершённые этапы в моих путях (по префиксам writes)",
                                [f"- {i}" for i in hub["intrusions"]] or ["- нет"], 3, "coordination.py status"))

    sections.append(Section("Inbox (без тел, без ACK)", inbox_lines(repo, manifest, inbox_override, use_hub, hub_owners(hub)), None))
    sections.append(Section("Репозиторий", repo_lines(repo), None))
    patterns = watch_patterns(hub["own_tasks"] if hub else [], manifest)
    sections.append(Section("Свежесть AGENT.md", freshness_lines(repo, manifest, patterns), 2, f"git log {short(manifest['reviewed_sha'])}..origin/dev -- <writes>"))

    backlog = read_text(workspace / "backlog.md")
    sections.append(Section("Backlog (backlog.md)", (backlog or "- backlog.md отсутствует").split("\n"), 1, f"{ws_rel}/backlog.md"))
    rows, older = journal_entries(workspace)
    if older:
        rows.append(f"- старше: {older} записей в {ws_rel}/journal/")
    sections.append(Section("Журнал (последние записи)", rows or ["- записей нет"], 0, f"{ws_rel}/journal/"))
    sections.append(Section("Дальше", [
        "- Свежая координация: `python -B -X utf8 Tools/agents/coordination.py status` и `inbox --task <task> --owner <owner>`.",
        f"- Подробности задачи — её doc_path в хабе (status); свои записи — `{ws_rel}/backlog.md`, `{ws_rel}/journal/`.",
        "- Задачу выбирает пользователь; пакет не начинает работу и не выбирает этап сам.",
    ]))
    return header, sections


def workspace_rel(workspace, repo):
    try:
        return workspace.resolve().relative_to(repo.resolve()).as_posix()
    except ValueError:
        return workspace.resolve().as_posix()


def hub_owners(hub):
    return {t.get("task_id"): t.get("owner") for t in (hub or {}).get("own_tasks", []) if t.get("owner")}


def inbox_lines(repo, manifest, override, use_hub, owners):
    if not use_hub:
        return ["- не запрашивался (--no-hub)"]
    if not manifest["tasks"]:
        return ["- у эксперта нет задач в хабе; общие события — `coordination.py inbox` из worktree задачи"]
    lines = []
    for task in manifest["tasks"]:
        code, out, err = hub_call(repo, ["inbox", "--task", task, "--owner", owners.get(task, manifest["owner"]), "--limit", str(INBOX_LIMIT)], override)
        try:
            data = json.loads(out)
        except json.JSONDecodeError:
            data = None
        if not isinstance(data, dict) or not data.get("ok"):
            reason = data.get("error") if isinstance(data, dict) else (err.strip().splitlines() or ["нет вывода"])[-1]
            lines.append(f"- `{task}`: недоступен ({str(reason)[:160]}); проверь из worktree владельца")
            continue
        page = data.get("pagination") or {}
        direct = [e for e in data.get("result") or [] if isinstance(e, dict) and e.get("recipient") == task]
        lines.append(f"- `{task}`: pending {page.get('pending_total', '?')}, адресных {page.get('direct_pending', '?')}, snapshot event {page.get('snapshot_event_id', '?')}")
        for event in direct:
            sender = (event.get("payload") or {}).get("from_task") if isinstance(event.get("payload"), dict) else None
            lines.append(f"  - #{event.get('id')} {event.get('kind')}" + (f" от {sender}" if sender else ""))
    return lines


def repo_lines(repo):
    branch = git(repo, "rev-parse", "--abbrev-ref", "HEAD") or "?"
    head = git(repo, "rev-parse", "HEAD")
    lines = [f"- Ветка {branch}, HEAD {short(head)}"]
    counts = git(repo, "rev-list", "--left-right", "--count", "HEAD...origin/dev")
    if counts:
        ahead, behind = counts.split()
        fetched = git(repo, "log", "-1", "--format=%h %cd", "--date=format:%Y-%m-%d %H:%M", "origin/dev") or "?"
        lines.append(f"- Относительно origin/dev ({fetched}, по последнему fetch): впереди {ahead}, позади {behind}")
    status = git(repo, "--no-optional-locks", "status", "--porcelain")
    if status is not None:
        changed = [l for l in status.splitlines() if l.strip()]
        lines.append(f"- Незакоммиченных путей: {len(changed)}")
    return lines


def freshness_lines(repo, manifest, patterns):
    reviewed = manifest["reviewed_sha"]
    lines = [f"- Сверено {manifest['reviewed_at']} на {short(reviewed)}."]
    if not patterns:
        return lines + ["- Наблюдаемых путей нет (нет writes в хабе и watch_paths)."]
    if git(repo, "cat-file", "-e", reviewed + "^{commit}") is None:
        return lines + ["- reviewed_sha отсутствует в этом клоне: свежесть неизвестна."]
    target = "origin/dev" if git(repo, "rev-parse", "--verify", "-q", "origin/dev") else "HEAD"
    spec = [f":(glob){p}" for p in patterns]
    log = git(repo, "log", "--format=%h %cd %s", "--date=short", f"{reviewed}..{target}", "--", *spec)
    if log is None:
        return lines + ["- git log недоступен: свежесть неизвестна."]
    commits = [l for l in log.splitlines() if l.strip()]
    if not commits:
        return lines + [f"- Коммитов в моих путях после сверки нет ({target})."]
    lines.append(f"- ВНИМАНИЕ: {len(commits)} коммитов в моих путях после сверки ({target}); сверь архитектурную карту и обнови reviewed_sha:")
    lines += [f"  - {c[:150]}" for c in commits[:FRESHNESS_LIMIT]]
    if len(commits) > FRESHNESS_LIMIT:
        lines.append(f"  - … ещё {len(commits) - FRESHNESS_LIMIT}")
    return lines


def render(header, sections, limit, agent_path, unit="bytes"):
    """Урезает необязательные блоки, пока текст не уложится в limit (байты UTF-8 или символы)."""
    def size(value):
        return len(value.encode("utf-8")) if unit == "bytes" else len(value)

    def text():
        out = list(header)
        for section in sections:
            out += section.render()
        return "\n".join(out).rstrip("\n") + "\n"

    def weight(section):
        # Урезаем самый крупный необязательный блок; при равенстве — с меньшим trim_rank.
        return (sum(size(line) + 1 for line in section.lines), -section.trim_rank)

    label = "байт" if unit == "bytes" else "символов"
    result = text()
    trimmable = [s for s in sections if s.trim_rank is not None]
    while size(result) > limit:
        candidates = [s for s in trimmable if s.lines]
        victim = max(candidates, key=weight) if candidates else None
        if victim is None:
            mandatory = sum(size("\n".join(s.render())) for s in sections if s.trim_rank is None)
            raise AwakeError("mandatory_context_over_budget",
                             f"обязательная часть ({mandatory} {label}: AGENT.md, задачи, inbox, репозиторий) не помещается в {limit} {label}. "
                             f"Пакет не выдан; прочитай {agent_path} напрямую и запусти awake с большим лимитом либо сократи AGENT.md")
        victim.lines.pop()
        victim.hidden += 1
        result = text()
    return result


def prepare(workspace, repo, status_override, inbox_override, use_hub):
    if not workspace.is_dir():
        raise AwakeError("workspace_missing", f"нет папки {workspace.as_posix()}")
    manifest = load_manifest(workspace)
    now = dt.datetime.now().astimezone().strftime("%Y-%m-%d %H:%M %z")
    header, sections = build(workspace, repo, manifest, status_override, inbox_override, use_hub, now)
    agent_path = (Path(workspace_rel(workspace, repo)) / "AGENT.md").as_posix()
    return header, sections, agent_path


def assemble(workspace, repo, status_override, inbox_override, use_hub, limit, unit):
    header, sections, agent_path = prepare(workspace, repo, status_override, inbox_override, use_hub)
    return render(header, sections, limit, agent_path, unit)


# Блоки пакета для хука: имя → (описание, префиксы заголовков разделов). Порядок — порядок в пакете.
HOOK_BLOCKS = {
    "role": ("роль и рабочее место", ("Роль", "Общие правила")),
    "tasks": ("задачи и входы для ТЗ", ("Мои задачи", "Входы")),
    "contracts": ("контракты и зависимости", ("Мои контракты", "Используемые", "Чего жду", "Кто зависит", "Чужие")),
    "status": ("inbox, репозиторий, свежесть", ("Inbox", "Репозиторий", "Свежесть")),
    "backlog": ("backlog и журнал", ("Backlog", "Журнал", "Дальше")),
}


def hook_block(sections, name):
    """Разделы блока по фиксированным заголовкам: не зависят от размера данных хаба."""
    prefixes = HOOK_BLOCKS[name][1]
    return [s for s in sections if s.title.startswith(prefixes)]


def hook_main(stream, out, status_override=None, inbox_override=None, block="compact"):
    """SessionStart-хук: по agent_type `<id>-expert` кладёт пакет в additionalContext.

    Потолок Claude Code — 10 000 символов на вывод одной команды хука, поэтому полный пакет
    выдаётся отдельной командой на каждый блок (`--hook role|tasks|contracts|status|backlog`).
    `--hook` без блока — одна сжатая часть. Чужие сессии и отсутствующее рабочее место — без вывода.
    Ошибка сборки не ломает старт: вместо блока — инструкция запустить awake вручную. Код выхода 0.
    """
    try:
        payload = json.loads(stream.read() or "{}")
    except (ValueError, UnicodeDecodeError):
        return 0
    agent = payload.get("agent_type") if isinstance(payload, dict) else None
    if not isinstance(agent, str) or not agent.endswith("-expert"):
        return 0
    expert_id = agent[: -len("-expert")]
    if not ID_RE.match(expert_id):
        return 0
    cwd = Path(payload.get("cwd") or os.environ.get("CLAUDE_PROJECT_DIR") or ".")
    repo = Path(git(cwd, "rev-parse", "--show-toplevel") or cwd)
    workspace = repo / "experts" / expert_id
    if not (workspace / "expert.json").is_file():
        return 0
    command = f"python -B -X utf8 Tools/experts/awake.py experts/{expert_id}"
    source = payload.get("source", "?")
    try:
        if block == "compact":
            tail = f"\n> Сжатый пакет SessionStart ({source}). Полный: `{command}`.\n"
            text = assemble(workspace, repo, status_override, inbox_override, True, HOOK_MAX_CHARS - len(tail), "chars") + tail
        else:
            header, sections, agent_path = prepare(workspace, repo, status_override, inbox_override, True)
            chosen = hook_block(sections, block)
            if not chosen:
                return 0
            names = list(HOOK_BLOCKS)
            title = f"блок `{block}` ({names.index(block) + 1}/{len(names)}): {HOOK_BLOCKS[block][0]}"
            if block == "role":
                lead = [f"{header[0]} — {title}", *header[1:],
                        f"> Пакет awake доставлен хуком SessionStart ({source}) блоками {', '.join(names)} (порядок "
                        f"в контексте может отличаться); вызывать awake вручную не нужно. Повторно: `{command}`.", ""]
            else:
                lead = [f"# awake `{expert_id}` — {title}", ""]
            text = render(lead, chosen, HOOK_MAX_CHARS, agent_path, "chars")
    except AwakeError as exc:
        text = (f"awake для `{expert_id}` не собран ({exc.code}): {exc.message}\n"
                f"Запусти вручную: `{command}`.\n")
    except Exception as exc:  # хук не должен ломать старт сессии
        text = f"awake для `{expert_id}` упал ({exc.__class__.__name__}). Запусти вручную: `{command}`.\n"
    # ASCII-экранирование JSON сохраняет кириллицу в старой Windows-консоли.
    out.write(json.dumps({"hookSpecificOutput": {"hookEventName": "SessionStart", "additionalContext": text}},
                         ensure_ascii=True).encode("ascii") + b"\n")
    return 0


def main(argv=None):
    parser = argparse.ArgumentParser(prog="awake", description="Стартовый пакет эксперта: AGENT.md + живое состояние. Только чтение.")
    parser.add_argument("workspace", nargs="?", help="папка рабочего места, например experts/<id>")
    parser.add_argument("--repo", help="корень checkout; по умолчанию git toplevel рабочего места")
    parser.add_argument("--max-bytes", type=int, default=DEFAULT_MAX_BYTES)
    parser.add_argument("--max-chars", type=int, help="лимит в символах вместо байт")
    parser.add_argument("--no-hub", action="store_true", help="не обращаться к хабу")
    parser.add_argument("--hook", nargs="?", const="compact", choices=("compact", *HOOK_BLOCKS),
                        help="режим SessionStart-хука (JSON со stdin, agent_type <id>-expert): блок пакета "
                             "role|tasks|contracts|status|backlog; без значения — сжатый пакет одной частью")
    parser.add_argument("--status-json", help="записанный вывод status вместо вызова (проверки)")
    parser.add_argument("--inbox-json", help="записанный вывод inbox вместо вызова (проверки)")
    args = parser.parse_args(argv)
    out = sys.stdout.buffer
    if args.hook:
        return hook_main(io.TextIOWrapper(sys.stdin.buffer, encoding="utf-8", errors="replace"), out,
                         args.status_json, args.inbox_json, args.hook)
    try:
        if not args.workspace:
            raise AwakeError("workspace_missing", "укажи папку рабочего места, например experts/<id>")
        workspace = Path(args.workspace)
        repo = Path(args.repo) if args.repo else Path(git(workspace, "rev-parse", "--show-toplevel") or workspace)
        if args.max_chars:
            text = assemble(workspace, repo, args.status_json, args.inbox_json, not args.no_hub, max(500, args.max_chars), "chars")
        else:
            text = assemble(workspace, repo, args.status_json, args.inbox_json, not args.no_hub, max(1024, args.max_bytes), "bytes")
        out.write(text.encode("utf-8"))
        return 0
    except AwakeError as exc:
        out.write(f"awake: ошибка {exc.code}: {exc.message}\n".encode("utf-8"))
        return 2


if __name__ == "__main__":
    sys.exit(main())
