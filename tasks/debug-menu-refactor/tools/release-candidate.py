"""Готовит изолированный release candidate UI; не принимает и не публикует работу.

Пишет Git objects, новый ref и ignored summary. HEAD, обычный индекс и рабочие
файлы не меняются. Решение о проверке, приёмке и push принимает ведущий агент.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import uuid


REPO = Path(__file__).resolve().parents[3]
TASK_ROOT = "tasks/debug-menu-refactor/"
REPORTS = REPO / TASK_ROOT / "reports"
OWNED = {"Assets/Scripts/UI/Menu/MenuDebug.cs", "Assets/Scripts/UI/Menu/Kit/MenuKit.cs",
         "Assets/Scripts/UI/Menu/MenuPlayersTeams.cs", "Assets/Scripts/UI/Menu/MenuPerfTests.cs"}


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha(raw):
    value = raw.decode("ascii").strip()
    require(re.fullmatch(r"[0-9a-f]{40}", value), "Нужен полный Git SHA")
    return value


def git(*args, payload=None, index=None):
    environment = os.environ.copy()
    for key in list(environment):
        if key.upper() in {"GIT_INDEX_FILE", "GIT_DIR", "GIT_WORK_TREE", "GIT_COMMON_DIR", "GIT_ATTR_SOURCE"}:
            del environment[key]
    if index is not None:
        environment["GIT_INDEX_FILE"] = str(index)
    result = subprocess.run(["git", "--no-pager", "-C", str(REPO), *args], input=payload,
                            stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=environment)
    require(result.returncode == 0, result.stderr.decode("utf-8", errors="replace").strip())
    return result.stdout


def allowed(path):
    if path in OWNED:
        return True
    if not path.startswith(TASK_ROOT) or "\\" in path:
        return False
    parts = path.split("/")
    return all(part not in ("", ".", "..") and not part.startswith(".")
               and part.casefold() not in {"reports", "private", "credentials", "secrets", "localconfigs",
                                           "credentials.json", "secrets.json", "auth.json"}
               and not part.casefold().endswith((".pem", ".key"))
               and not part.endswith((".", " "))
               and not any(ord(c) < 32 or c in ':*?"<>|' for c in part) for part in parts)


def physical(path):
    candidate = Path(os.path.abspath(path))
    require(candidate.is_relative_to(REPO), "Путь выходит из своего checkout")
    current = candidate
    while True:
        if current.exists() or current.is_symlink():
            info = current.lstat()
            require(not stat.S_ISLNK(info.st_mode) and not
                    (getattr(info, "st_file_attributes", 0) & 0x400), "Symlink/reparse запрещён: " + str(current))
        if current == REPO:
            return candidate
        current = current.parent


def raw_file(path):
    source = physical(REPO / path)
    require(stat.S_ISREG(source.lstat().st_mode) and source.stat().st_size <= 8 * 1024 * 1024,
            "Нужен ограниченный regular file: " + path)
    raw = source.read_bytes()
    raw.decode("utf-8")
    require(b"\r" not in raw and b"\0" not in raw, "Ожидается текст с LF: " + path)
    return raw


def tree(oid):
    result = {}
    seen = set()
    for record in git("ls-tree", "-rz", "--full-tree", oid).split(b"\0"):
        if record:
            header, path = record.split(b"\t", 1)
            mode, kind, blob = header.decode("ascii").split()
            path = path.decode("utf-8")
            require(path.casefold() not in seen, "Case alias в tree")
            seen.add(path.casefold())
            result[path] = (mode, kind, blob)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", required=True, help="Полный SHA точной текущей origin/dev")
    parser.add_argument("--ref", required=True, help="Новая уникальная ветка codex/debug-menu-refactor-release-...")
    parser.add_argument("--message", required=True, help="Сообщение candidate commit")
    args = parser.parse_args()
    base = sha(args.base.encode("ascii"))
    require(args.ref.startswith("codex/debug-menu-refactor-release-") and args.message.strip(),
            "Нужны release ref и непустое сообщение")
    ref = "refs/heads/" + args.ref
    git("check-ref-format", ref)
    require(not git("for-each-ref", "--format=%(refname)", ref).strip(), "Ref уже существует")
    require(sha(git("rev-parse", "refs/remotes/origin/dev")) == base, "--base отличается от origin/dev")
    require(git("cat-file", "-t", base).strip() == b"commit", "Base должен быть commit")
    base_tree = tree(base)
    names = {p.decode("utf-8") for p in
             git("ls-files", "-z", "--cached", "--others", "--exclude-standard", "--", TASK_ROOT, *sorted(OWNED)).split(b"\0") if p}
    paths = []
    for path in sorted(names):
        if not allowed(path):
            continue
        # --exclude-standard не исключает ignored tracked files: проверяем все paths явно.
        ignored = subprocess.run(["git", "--no-pager", "-C", str(REPO), "check-ignore", "--no-index", "--quiet", "--", path],
                                 stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                                 env={k: v for k, v in os.environ.items() if k.upper() not in
                                      {"GIT_INDEX_FILE", "GIT_DIR", "GIT_WORK_TREE", "GIT_COMMON_DIR", "GIT_ATTR_SOURCE"}})
        require(ignored.returncode in (0, 1), "Не удалось проверить ignore: " + path)
        if ignored.returncode == 0:
            continue
        try:
            physical(REPO / path)
        except ValueError:
            require(path not in OWNED, "Owned source является ссылкой: " + path)
            continue
        paths.append(path)
    require(OWNED <= set(paths), "Не найдены все четыре regular owned source")
    require(len(paths) <= 1024 and len({p.casefold() for p in paths}) == len(paths), "Paths budget/alias")
    aliases = {p.casefold(): p for p in base_tree}
    require(all(p.casefold() not in aliases or aliases[p.casefold()] == p for p in paths), "Case alias с base tree")
    sources = {path: raw_file(path) for path in paths}
    require(sum(map(len, sources.values())) <= 64 * 1024 * 1024, "Превышен budget 64 MiB")
    modes = {}
    blobs = {}
    for path, raw in sources.items():
        old = base_tree.get(path)
        require(old is None or old[:2] in (("100644", "blob"), ("100755", "blob")), "Base path не regular blob")
        modes[path] = old[0] if old else "100644"
        # --path применяет реальные Git attributes/filters; физические bytes не переписываются.
        blobs[path] = sha(git("hash-object", "--path=" + path, "--stdin", payload=raw))
    physical(REPORTS)
    git("check-ignore", "--no-index", "--quiet", "tasks/debug-menu-refactor/reports/release-candidate-probe")
    require(sha(git("rev-parse", "refs/remotes/origin/dev")) == base, "Origin/dev изменилась при подготовке")
    output = REPORTS / ("release-candidate-" + uuid.uuid4().hex)
    output.mkdir(parents=True, exist_ok=False)
    index = output / "index"
    require(not index.exists(), "Private index уже существует")
    checksums = {}
    for path, raw in sources.items():
        require(sha(git("hash-object", "-w", "--path=" + path, "--stdin", payload=raw)) == blobs[path], "Изменились filters/blob")
        stored = git("cat-file", "blob", blobs[path])
        stored.decode("utf-8")
        require(b"\r" not in stored and b"\0" not in stored, "Неканонический filtered blob: " + path)
        checksums[path] = hashlib.sha256(stored).hexdigest()
    git("read-tree", base, index=index)
    payload = b"".join(f"{modes[p]} {blobs[p]}\t{p}".encode("utf-8") + b"\0" for p in paths)
    git("update-index", "-z", "--index-info", payload=payload, index=index)
    tree_sha = sha(git("write-tree", index=index))
    expected = dict(base_tree)
    expected.update({p: (modes[p], "blob", blobs[p]) for p in paths})
    actual = tree(tree_sha)
    require(actual == expected, "Candidate tree отличается от exact own overlay")
    delta = sorted(p for p in base_tree.keys() | actual.keys() if base_tree.get(p) != actual.get(p))
    require(delta and all(allowed(p) and p in paths for p in delta), "Пустая или чужая delta")
    require(all(raw_file(p) == sources[p] for p in paths), "Физические source изменились во время сборки")
    require(sha(git("rev-parse", "refs/remotes/origin/dev")) == base, "Origin/dev изменилась перед commit")
    commit = sha(git("commit-tree", tree_sha, "-p", base, payload=(args.message + "\n").encode("utf-8")))
    require(sha(git("rev-parse", "refs/remotes/origin/dev")) == base, "Origin/dev изменилась перед ref")
    git("update-ref", ref, commit, "0" * 40)
    report = output / "summary.json"
    summary = {"candidate_sha": commit, "base_sha": base, "tree_sha": tree_sha, "ref": ref,
               "report": str(report), "private_index": str(index), "delta": delta, "accepted": False,
               "entries": [{"path": p, "mode": modes[p], "blob": blobs[p], "blob_sha256": checksums[p],
                            "physical_sha256": hashlib.sha256(sources[p]).hexdigest()} for p in paths]}
    report.write_bytes((json.dumps(summary, ensure_ascii=False, indent=2) + "\n").encode("utf-8"))
    print(json.dumps(summary, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, TypeError) as error:
        raise SystemExit("Release candidate остановлен: " + str(error))
