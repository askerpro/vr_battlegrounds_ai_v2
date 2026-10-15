"""Собирает технический Unity input из своего checkpoint и точного immutable grant.

Не меняет HEAD, рабочие файлы или настоящий индекс. Живой допуск проверяет брокер.
"""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import re
import stat
import subprocess
import time
import uuid


TASK = "debug-menu-refactor"
OWNER = "ui"
STAGE = "implementation"
PURPOSE = "technical-unity-authoring"
REPO = Path(__file__).resolve().parents[3]
REPORTS = REPO / "tasks" / TASK / "reports"
OWNED = {"Assets/Scripts/UI/Menu/MenuDebug.cs", "Assets/Scripts/UI/Menu/Kit/MenuKit.cs",
         "Assets/Scripts/UI/Menu/MenuPlayersTeams.cs", "Assets/Scripts/UI/Menu/MenuPerfTests.cs"}
MAX_BYTES = 64 * 1024 * 1024


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha(value):
    require(isinstance(value, str) and re.fullmatch(r"[0-9a-f]{40}", value), "Нужен полный Git SHA")
    return value


def integer(value):
    require(type(value) is int and value > 0, "Нужен положительный целый revision/epoch")
    return value


def safe_path(value):
    require(isinstance(value, str) and value and "\\" not in value, "Неканонический Git path")
    parts = value.split("/")
    reserved = {"con", "prn", "aux", "nul", *[f"com{i}" for i in range(1, 10)],
                *[f"lpt{i}" for i in range(1, 10)]}
    require(all(p not in ("", ".", "..") and not p.endswith((".", " "))
                and not any(ord(c) < 32 or c in ':*?"<>|' for c in p)
                and p.split(".")[0].casefold() not in reserved for p in parts), "Небезопасный Git path")
    require(not any(p.startswith(".") or p.casefold() in
                    {"reports", "credentials", "secrets", "localconfigs"} for p in parts),
            "Private state/config/reports запрещены")
    return value


def env(index=None):
    result = os.environ.copy()
    for key in list(result):
        if key.upper() in {"GIT_INDEX_FILE", "GIT_DIR", "GIT_WORK_TREE", "GIT_COMMON_DIR", "GIT_ATTR_SOURCE"}:
            del result[key]
    if index is not None:
        result["GIT_INDEX_FILE"] = str(index)
    return result


def git(*args, payload=None, index=None, custom_env=None):
    process = subprocess.run(["git", "--no-pager", "-C", str(REPO), *args], input=payload,
                             stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                             env=custom_env if custom_env is not None else env(index))
    require(process.returncode == 0, process.stderr.decode("utf-8", errors="replace").strip())
    return process.stdout


def tree(commit):
    entries = {}
    aliases = set()
    for record in git("ls-tree", "-rz", "--full-tree", commit).split(b"\0"):
        if not record:
            continue
        header, raw_path = record.split(b"\t", 1)
        mode, kind, oid = header.decode("ascii").split()
        path = raw_path.decode("utf-8")
        require(path.casefold() not in aliases, "Case alias в Git tree")
        aliases.add(path.casefold())
        entries[path] = (mode, kind, oid)
    return entries


def broker_base():
    # Принятая dev может уйти вперёд очереди; B определяет опубликованная база worker.
    common = Path(git("rev-parse", "--path-format=absolute", "--git-common-dir").decode().strip())
    config = common.parent / ".agent-state/editor-broker/config.json"
    document = json.loads(config.read_bytes(), object_pairs_hook=unique_pairs)
    return sha(document.get("baseline_sha"))


def no_links(path):
    # Проверяем физические предки до resolve(), чтобы не скрыть junction/symlink.
    candidate = Path(os.path.abspath(path))
    require(candidate.is_relative_to(REPO), "Report должен находиться в собственном worktree")
    current = candidate
    while True:
        if current.exists() or current.is_symlink():
            info = current.lstat()
            require(not stat.S_ISLNK(info.st_mode) and not
                    (getattr(info, "st_file_attributes", 0) & 0x400), "Ссылка/reparse point запрещена")
        if current == REPO:
            break
        current = current.parent
    return candidate


def unique_pairs(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "Повтор JSON key")
        result[key] = value
    return result


def fresh(grant):
    require(grant.get("active") is True, "Grant неактивен")
    expires = grant.get("expires")
    require(type(expires) in (int, float) and math.isfinite(expires) and expires > time.time(),
            "Grant истёк или TTL некорректен")
    created = grant.get("created")
    require(type(created) in (int, float) and math.isfinite(created)
            and 0 < expires - created <= 86400, "Некорректный bounded TTL")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--owned-input", required=True, help="Полный SHA собственного checkpoint")
    parser.add_argument("--grant-report", required=True, type=Path,
                        help="Собственный JSON output input-grant-show; используется result.entries")
    parser.add_argument("--consumer-revision", required=True, type=int)
    parser.add_argument("--consumer-epoch", required=True, type=int)
    args = parser.parse_args()
    owned = sha(args.owned_input)
    integer(args.consumer_revision)
    integer(args.consumer_epoch)
    report = no_links(args.grant_report if args.grant_report.is_absolute() else REPO / args.grant_report)
    require(report.is_file() and report.stat().st_size <= 4 * 1024 * 1024, "Некорректный grant report")
    document = json.loads(report.read_bytes(), object_pairs_hook=unique_pairs)
    require(isinstance(document, dict) and document.get("ok") is True,
            "input-grant-show не подтвердил успешное чтение grant")
    grant = document.get("result")
    require(isinstance(grant, dict), "Ожидается canonical input-grant-show output с result")
    require(type(grant.get("version")) is int and grant["version"] == 1
            and grant.get("purpose") == PURPOSE, "Неизвестный grant format/purpose")
    fresh(grant)
    binding = {"task_id": TASK, "owner": OWNER, "revision": args.consumer_revision,
               "stage_id": STAGE, "epoch": args.consumer_epoch, "worktree": str(REPO)}
    consumer = grant.get("consumer")
    require(isinstance(consumer, dict) and set(consumer) == set(binding), "Некорректный consumer binding")
    integer(consumer.get("revision"))
    integer(consumer.get("epoch"))
    require(all(consumer[k] == v for k, v in binding.items() if k != "worktree"), "Consumer binding изменился")
    require(isinstance(consumer["worktree"], str) and Path(consumer["worktree"]).is_absolute()
            and os.path.normcase(os.path.abspath(consumer["worktree"])) == os.path.normcase(str(REPO)),
            "Grant адресован другому worktree")
    require(re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9_.-]{0,127}", str(grant.get("grant_id", ""))), "Некорректный grant_id")
    integer(grant.get("revision"))
    manifest = grant.get("manifest")
    require(isinstance(manifest, dict), "Нет provider manifest")
    base = broker_base()
    require(sha(manifest.get("base_sha")) == base, "Grant B отличается от опубликованной базы worker")
    git("merge-base", "--is-ancestor", base, "refs/remotes/origin/dev")
    source = sha(manifest.get("source_sha"))
    for commit in (base, owned, source):
        require(git("cat-file", "-t", commit).strip() == b"commit", "SHA должен указывать на commit")
    git("merge-base", "--is-ancestor", base, owned)
    base_tree, owned_tree, source_tree = tree(base), tree(owned), tree(source)
    delta = {p for p in base_tree.keys() | owned_tree.keys() if base_tree.get(p) != owned_tree.get(p)}
    for path in delta:
        safe_path(path)
        require(path in OWNED or path.startswith(f"tasks/{TASK}/"), "Собственный checkpoint содержит чужой путь: " + path)
        require(path in owned_tree and owned_tree[path][:2] in (("100644", "blob"), ("100755", "blob")),
                "Удаления/ссылки в owned delta запрещены")
    entries = grant.get("entries")
    require(isinstance(entries, list) and 0 < len(entries) <= 4096, "Нет ограниченного canonical result.entries")
    seen = set()
    records = []
    total = 0
    owned_aliases = {p.casefold() for p in delta}
    all_aliases = {p.casefold(): p for p in owned_tree}
    for entry in entries:
        require(isinstance(entry, dict), "Некорректный entry")
        path = safe_path(entry.get("path"))
        require(path.startswith(("Assets/", "ProjectSettings/")), "Grant scope ограничен Assets/ProjectSettings")
        require(path.casefold() not in seen and path.casefold() not in owned_aliases, "Grant overlap/alias: " + path)
        require(path.casefold() not in all_aliases or all_aliases[path.casefold()] == path, "Case alias: " + path)
        seen.add(path.casefold())
        mode = entry.get("mode")
        require(mode in ("100644", "100755") and entry.get("operation") in ("A", "M")
                and type(entry.get("companion")) is bool, "Некорректные mode/operation/companion")
        blob = sha(entry.get("source_blob"))
        require(sha(entry.get("source_sha")) == source and source_tree.get(path) == (mode, "blob", blob),
                "Source tree не совпадает с grant entry: " + path)
        old = base_tree.get(path)
        if entry["operation"] == "A":
            require(old is None and entry.get("base_blob") is None and not entry["companion"], "Неверный A base pin")
        else:
            require(old is not None and old[1] == "blob" and old[2] == sha(entry.get("base_blob")), "Неверный M base pin")
        if entry["companion"]:
            require(path.endswith(".meta") and old == source_tree[path], "Companion должен быть неизменной .meta")
        size = int(git("cat-file", "-s", blob).strip())
        total += size
        require(total <= MAX_BYTES, "Blob budget превышает 64 MiB")
        raw = git("cat-file", "blob", blob)
        require(len(raw) == size and hashlib.sha256(raw).hexdigest() == entry.get("sha256"), "Raw SHA256 не совпадает: " + path)
        records.append(f"{mode} {blob}\t{path}".encode("utf-8") + b"\0")
    no_links(REPORTS)
    # Проверка ignore выполняется до создания файлов и любых Git mutations.
    relative_reports = f"tasks/{TASK}/reports/compose-input-probe"
    git("check-ignore", "--no-index", "--quiet", relative_reports)
    token = uuid.uuid4().hex
    output = REPORTS / ("compose-input-" + token)
    output.mkdir(parents=True, exist_ok=False)
    index = output / "index"
    require(not index.exists(), "Private index уже существует")
    components = {"version": 1, "purpose": PURPOSE, "base_sha": base, "consumer": dict(consumer),
                  "components": [{"kind": "grant", "grant_id": grant["grant_id"], "revision": grant["revision"]}]}
    manifest_path = output / "components.json"
    manifest_bytes = (json.dumps(components, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    fresh(grant)
    require(broker_base() == base, "Опубликованная база worker изменилась во время проверки")
    git("read-tree", owned, index=index)
    git("update-index", "-z", "--index-info", payload=b"".join(records), index=index)
    composed_tree = sha(git("write-tree", index=index).decode().strip())
    expected = dict(owned_tree)
    expected.update({entry["path"]: (entry["mode"], "blob", entry["source_blob"]) for entry in entries})
    actual = tree(composed_tree)
    require(actual == expected, "Composed tree отличается от exact overlay")
    changed = {p for p in base_tree.keys() | actual.keys() if base_tree.get(p) != actual.get(p)}
    require(changed <= delta | {entry["path"] for entry in entries}, "B→A содержит лишние пути")
    fresh(grant)
    commit_env = env(index)
    commit_env.update({"GIT_AUTHOR_NAME": "UI technical composition", "GIT_AUTHOR_EMAIL": "ui@technical.invalid",
                       "GIT_COMMITTER_NAME": "UI technical composition", "GIT_COMMITTER_EMAIL": "ui@technical.invalid"})
    composed = sha(git("commit-tree", composed_tree, "-p", owned,
                       payload=b"Technical immutable SceneDetail input; not accepted work\n", custom_env=commit_env).decode().strip())
    ref = "refs/heads/codex/tmp/debug-menu-refactor-scene-" + token
    git("update-ref", ref, composed, "0" * 40)
    manifest_path.write_bytes(manifest_bytes)
    summary = {"A": composed, "ref": ref, "B": base, "owned_input": owned, "source_sha": source,
               "grant_id": grant["grant_id"], "grant_revision": grant["revision"], "entries_count": len(entries),
               "paths": [entry["path"] for entry in entries], "changed_paths": sorted(changed),
               "input_components": str(manifest_path), "input_components_sha256": hashlib.sha256(manifest_bytes).hexdigest(),
               "private_index": str(index), "grant_expires": grant["expires"]}
    (output / "summary.json").write_bytes((json.dumps(summary, ensure_ascii=False, indent=2) + "\n").encode("utf-8"))
    print(json.dumps(summary, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, KeyError, TypeError, json.JSONDecodeError) as error:
        raise SystemExit("Композиция остановлена: " + str(error))
