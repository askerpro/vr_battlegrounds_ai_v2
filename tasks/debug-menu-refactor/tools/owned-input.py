"""Создаёт технический снимок четырёх UI-исходников поверх базы брокера.

Пишет Git objects, уникальный ref и ignored report. HEAD, обычный индекс и рабочие
файлы сохраняются. Этот снимок не означает приёмку или recorded stage proof.
"""
import argparse
import ast
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import uuid


REPO = Path(__file__).resolve().parents[3]
REPORTS = REPO / "tasks/debug-menu-refactor/reports"
OWNED = (
    "Assets/Scripts/UI/Menu/MenuDebug.cs",
    "Assets/Scripts/UI/Menu/Kit/MenuKit.cs",
    "Assets/Scripts/UI/Menu/MenuPlayersTeams.cs",
    "Assets/Scripts/UI/Menu/MenuPerfTests.cs",
)


def require(condition, message):
    if not condition:
        raise ValueError(message)


def full_sha(raw):
    value = raw.decode("ascii").strip()
    require(re.fullmatch(r"[0-9a-f]{40}", value), "Ожидается полный Git SHA")
    return value


def git(*args, payload=None, index=None, identity=False):
    environment = os.environ.copy()
    for key in list(environment):
        if key.upper() in {"GIT_INDEX_FILE", "GIT_DIR", "GIT_WORK_TREE", "GIT_COMMON_DIR", "GIT_ATTR_SOURCE"}:
            del environment[key]
    if index is not None:
        environment["GIT_INDEX_FILE"] = str(index)
    if identity:
        environment.update({"GIT_AUTHOR_NAME": "UI technical input", "GIT_AUTHOR_EMAIL": "ui@technical.invalid",
                            "GIT_COMMITTER_NAME": "UI technical input", "GIT_COMMITTER_EMAIL": "ui@technical.invalid"})
    result = subprocess.run(["git", "--no-pager", "-C", str(REPO), *args], input=payload,
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=environment)
    require(result.returncode == 0, result.stderr.decode("utf-8", errors="replace").strip())
    return result.stdout


def unique_pairs(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, "Повтор JSON key")
        result[key] = value
    return result


def broker_base():
    # Принятая origin/dev может уйти вперёд; B задаёт опубликованная база worker.
    common = Path(git("rev-parse", "--path-format=absolute", "--git-common-dir").decode().strip())
    config = common.parent / ".agent-state/editor-broker/config.json"
    document = json.loads(config.read_bytes(), object_pairs_hook=unique_pairs)
    require(isinstance(document, dict) and isinstance(document.get("baseline_sha"), str),
            "Нет корректной canonical broker baseline")
    return full_sha(document["baseline_sha"].encode("ascii"))


def safe_physical(path):
    # Не resolve(): он скрыл бы исходную ссылку или junction.
    candidate = Path(os.path.abspath(path))
    require(candidate.is_relative_to(REPO), "Путь выходит за собственный worktree")
    current = candidate
    while True:
        if current.exists() or current.is_symlink():
            info = current.lstat()
            require(not stat.S_ISLNK(info.st_mode) and not
                    (getattr(info, "st_file_attributes", 0) & 0x400), "Ссылка/reparse point запрещена")
        if current == REPO:
            return candidate
        current = current.parent


def read_source(path):
    physical = safe_physical(REPO / path)
    require(stat.S_ISREG(physical.lstat().st_mode), "Нужен regular source: " + path)
    require(physical.stat().st_size <= 4 * 1024 * 1024, "Исходник слишком велик: " + path)
    raw = physical.read_bytes()
    raw.decode("utf-8")
    require(b"\r" not in raw, "Исходник должен иметь LF: " + path)
    return raw


def selected_tree(oid):
    result = {}
    for record in git("ls-tree", "-rz", "--full-tree", oid, "--", *OWNED).split(b"\0"):
        if not record:
            continue
        header, path = record.split(b"\t", 1)
        mode, kind, blob = header.decode("ascii").split()
        result[path.decode("utf-8")] = (mode, kind, blob)
    require(set(result) == set(OWNED), "B/tree не содержит ровно четыре выбранных исходника")
    require(all(mode in ("100644", "100755") and kind == "blob" for mode, kind, _ in result.values()),
            "В выбранном tree обнаружены ссылки/submodules")
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.parse_args()
    # Проверяем совпадение allowlist без выполнения compose-input.py.
    composer = safe_physical(Path(__file__).with_name("compose-input.py"))
    syntax = ast.parse(composer.read_bytes())
    declarations = [node for node in syntax.body if isinstance(node, ast.Assign)
                    and any(isinstance(target, ast.Name) and target.id == "OWNED" for target in node.targets)]
    require(len(declarations) == 1 and ast.literal_eval(declarations[0].value) == set(OWNED),
            "Allowlist расходится с compose-input.py")
    require(all(path.startswith("Assets/Scripts/UI/Menu/") and "\\" not in path
                and all(part not in ("", ".", "..") for part in path.split("/")) for path in OWNED),
            "Неканонический owned path")
    base = broker_base()
    git("merge-base", "--is-ancestor", base, "refs/remotes/origin/dev")
    require(git("cat-file", "-t", base).strip() == b"commit", "B должен быть commit")
    before = selected_tree(base)
    sources = {path: read_source(path) for path in OWNED}
    # hash-object без -w здесь только вычисляет OID точных сырых bytes, без filters.
    blobs = {path: full_sha(git("hash-object", "--stdin", payload=raw)) for path, raw in sources.items()}
    require(all(blobs[path] != before[path][2] for path in OWNED),
            "Для этого пакета ожидается delta ровно четырёх UI-файлов; есть неизменный исходник")
    safe_physical(REPORTS)
    git("check-ignore", "--no-index", "--quiet", "tasks/debug-menu-refactor/reports/owned-input-probe")
    require(broker_base() == base, "Опубликованная база worker изменилась во время подготовки")
    token = uuid.uuid4().hex
    output = REPORTS / ("owned-input-" + token)
    output.mkdir(parents=True, exist_ok=False)
    index = output / "index"
    require(not index.exists(), "Частный индекс уже существует")
    for path, raw in sources.items():
        require(full_sha(git("hash-object", "-w", "--stdin", payload=raw)) == blobs[path], "Изменился Git blob hash")
    git("read-tree", base, index=index)
    records = b"".join(f"{before[path][0]} {blobs[path]}\t{path}".encode("utf-8") + b"\0" for path in OWNED)
    git("update-index", "-z", "--index-info", payload=records, index=index)
    tree_sha = full_sha(git("write-tree", index=index))
    require(selected_tree(tree_sha) == {path: (before[path][0], "blob", blobs[path]) for path in OWNED},
            "Частный tree не совпадает с физическими bytes/modes")
    delta = {record.decode("utf-8") for record in
             git("diff-tree", "--no-commit-id", "--name-only", "-r", "-z", base, tree_sha).split(b"\0") if record}
    require(delta == set(OWNED), "Delta содержит чужие пути или не равна четырём UI-файлам")
    require(all(read_source(path) == sources[path] for path in OWNED), "Физические исходники изменились при сборке")
    require(broker_base() == base, "Опубликованная база worker изменилась при сборке")
    commit = full_sha(git("commit-tree", tree_sha, "-p", base, identity=True,
                          payload=b"Technical scoped UI input; not accepted work or recorded stage proof\n"))
    ref = "refs/heads/codex/tmp/debug-menu-refactor-owned-" + token
    git("update-ref", ref, commit, "0" * 40)
    report = output / "summary.json"
    summary = {"owned_input": commit, "base_sha": base, "tree_sha": tree_sha, "ref": ref,
               "private_index": str(index), "report": str(report), "changed_paths": sorted(delta),
               "entries": [{"path": path, "mode": before[path][0], "base_blob": before[path][2],
                            "source_blob": blobs[path], "sha256": hashlib.sha256(sources[path]).hexdigest()}
                           for path in OWNED]}
    report.write_bytes((json.dumps(summary, ensure_ascii=False, indent=2) + "\n").encode("utf-8"))
    print(json.dumps(summary, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, TypeError, SyntaxError) as error:
        raise SystemExit("Подготовка own input остановлена: " + str(error))
