"""Собирает scoped accepted commit отдельным индексом; HEAD, рабочие файлы и основной индекс сохраняются."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess

from prepare_provider_grant import belongs


def git(*args, env=None, data=None):
    return subprocess.check_output(["git", "--no-optional-locks", *args], env=env, input=data)


def entry(commit, path):
    raw = git("ls-tree", "-z", commit, "--", path)
    if not raw:
        return None
    header, found = raw.rstrip(b"\0").split(b"\t", 1)
    mode, kind, oid = header.decode().split()
    if found.decode() != path or kind != "blob" or mode not in ("100644", "100755"):
        raise ValueError("Недопустимый Git input: " + path)
    return mode, oid


def product(path):
    return belongs(path) or path == "Assets/Editor/VR_Battlegrounds/Maps/Detail.meta" or path.startswith("Assets/Editor/VR_Battlegrounds/Maps/Detail/")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", required=True)
    parser.add_argument("--source", required=True)
    parser.add_argument("--branch", required=True)
    parser.add_argument("--out", required=True)
    args = parser.parse_args()
    root = Path(git("rev-parse", "--show-toplevel").decode().strip())
    out = (root / args.out).resolve()
    if not out.is_relative_to(root / "tasks/scene-detail/reports") or out.exists():
        raise ValueError("Нужна новая квитанция в reports своей задачи")
    if not args.branch.startswith("codex/"):
        raise ValueError("Имя candidate branch должно начинаться с codex/")
    git("check-ref-format", "refs/heads/" + args.branch)
    base = git("rev-parse", args.base + "^{commit}").decode().strip()
    source = git("rev-parse", args.source + "^{commit}").decode().strip()
    source_base = git("rev-parse", source + "^").decode().strip()
    selected = sorted(p for p in git("diff", "--name-only", "--no-renames", "-z", source_base, source).decode().split("\0") if p and product(p))
    if len(selected) != 154:
        raise ValueError("Ожидалось154 проверенных product paths")
    docs = sorted(set(git("ls-files", "--cached", "--others", "--exclude-standard", "-z", "--", "tasks/scene-detail").decode().split("\0")) - {""})
    if not docs or len(docs) > 30 or any("/reports/" in p or Path(p).suffix not in (".md", ".json", ".py", ".gitattributes") and Path(p).name != ".gitattributes" for p in docs):
        raise ValueError("Неожиданный состав task material")
    main_index = Path(git("rev-parse", "--path-format=absolute", "--git-path", "index").decode().strip())
    before = {"head": git("rev-parse", "HEAD").decode().strip(), "index_sha256": hashlib.sha256(main_index.read_bytes()).hexdigest()}
    out.parent.mkdir(parents=True, exist_ok=True)
    temp_index = out.with_suffix(".index")
    if temp_index.exists():
        raise ValueError("Временный индекс уже существует; проверить предыдущий исход")
    env = dict(os.environ, GIT_INDEX_FILE=str(temp_index))
    git("read-tree", base, env=env)
    updates = []
    for path in selected:
        current, old, latest = entry(source, path), entry(source_base, path), entry(base, path)
        if not current or latest not in (old, current):
            raise ValueError("Accepted base конфликтует с product path: " + path)
        raw = git("cat-file", "blob", current[1])
        if (root / path).read_bytes() != raw or b"\r" in raw:
            raise ValueError("Product snapshot отличается или содержит CR: " + path)
        updates.append((path, current[0], current[1]))
    for path in docs:
        raw = (root / path).read_bytes()
        if b"\r" in raw:
            raise ValueError("Task material содержит CR: " + path)
        oid = git("hash-object", "-w", "--stdin", data=raw).decode().strip()
        updates.append((path, "100644", oid))
    git("update-index", "-z", "--index-info", env=env,
        data=b"".join((mode + " " + oid + "\t" + path).encode() + b"\0" for path, mode, oid in updates))
    staged = set(git("diff", "--cached", "--name-only", "-z", base, env=env).decode().split("\0")) - {""}
    if not staged or staged - {p for p, _, _ in updates}:
        raise ValueError("Candidate содержит чужие paths")
    tree = git("write-tree", env=env).decode().strip()
    message = out.with_suffix(".message.txt")
    message.write_text("feat(maps): добавить принятый профиль детализации сцен\n\nПрофиль, единственный runtime-применитель, UI Build/Snapshot и редакторский сбор/импорт.\nLobby:690LOD/361renderer/252textures,112protected/140DetailBand; прежний tuner снят.\nПользователь15Oct разрешил вливать как есть; размещение с UI согласуется в Перф-анализ/GPU.\nПроверены компиляция, authoring, static review и PC-host startup/cleanup; Quest/GPU7ms не подтверждены.\n", encoding="utf-8", newline="\n")
    commit = git("commit-tree", tree, "-p", base, "-F", str(message), env=env).decode().strip()
    git("update-ref", "refs/heads/" + args.branch, commit, "0" * 40)
    after = {"head": git("rev-parse", "HEAD").decode().strip(), "index_sha256": hashlib.sha256(main_index.read_bytes()).hexdigest()}
    report = {"base": base, "source": source, "commit": commit, "branch": args.branch, "tree": tree,
              "product_count": len(selected), "task_count": len(docs), "paths": sorted(staged),
              "before": before, "after": after, "main_state_preserved": before == after}
    out.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    if before != after:
        raise RuntimeError("Основной HEAD/index изменились; публикацию остановить")
    print(json.dumps({k: v for k, v in report.items() if k not in ("paths", "before", "after")}, ensure_ascii=False))


if __name__ == "__main__":
    main()
