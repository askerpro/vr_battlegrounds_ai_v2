"""Read-only аудит tracked Unity .meta и их владельцев по Git index/HEAD.

Физическое удаление tracked ассетов не является нарушением этого контракта.
Папки с tracked содержимым определяются префиксами дерева; folderAsset: yes
из Git blob также доказывает пустые папки, которые учитываются отдельно.
"""
from __future__ import annotations

import argparse
import json
import os
from pathlib import Path, PurePosixPath
import re
import subprocess
import sys


class AssetPairError(ValueError):
    """Git snapshot не удалось проверить полностью."""


def git(root, *arguments, data=None, accepted=(0,)):
    environment = os.environ.copy()
    for name in ("GIT_INDEX_FILE", "GIT_DIR", "GIT_WORK_TREE", "GIT_COMMON_DIR",
                 "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES"):
        environment.pop(name, None)
    environment["GIT_OPTIONAL_LOCKS"] = "0"
    result = subprocess.run(["git", "--no-pager", *arguments], cwd=root, env=environment,
                            input=data, capture_output=True)
    if result.returncode not in accepted:
        raise AssetPairError(result.stderr.decode("utf-8", "replace").strip())
    return result.stdout


def snapshot(root, source):
    if source == "index":
        records = git(root, "ls-files", "--stage", "-z")
    elif source == "head":
        records = git(root, "ls-tree", "-r", "-z", "HEAD")
    else:
        raise AssetPairError("source должен быть index или head")
    files, conflicts = {}, set()
    for record in records.split(b"\0"):
        if not record:
            continue
        header, name = record.split(b"\t", 1)
        columns = header.decode("ascii").split()
        path = os.fsdecode(name)
        if source == "index":
            if columns[2] != "0":
                conflicts.add(path)
                continue
            oid = columns[1]
        else:
            if columns[1] != "blob":
                continue
            oid = columns[2]
        files[path] = oid
    return files, conflicts


def meta_blobs(root, entries):
    """Одна cat-file batch операция вместо Git subprocess на каждую meta."""
    identifiers = list(dict.fromkeys(entries.values()))
    if not identifiers:
        return {}
    raw = git(root, "cat-file", "--batch", data=("\n".join(identifiers) + "\n").encode("ascii"))
    contents, offset = {}, 0
    for expected in identifiers:
        newline = raw.find(b"\n", offset)
        if newline < 0:
            raise AssetPairError("Неполный cat-file batch header")
        header = raw[offset:newline].decode("ascii").split()
        if len(header) != 3 or header[0] != expected or header[1] != "blob":
            raise AssetPairError("Meta object не является доступным Git blob")
        size = int(header[2])
        start, end = newline + 1, newline + 1 + size
        if end >= len(raw) or raw[end:end + 1] != b"\n":
            raise AssetPairError("Неполный cat-file batch blob")
        contents[expected] = raw[start:end]
        offset = end + 1
    return {path: contents[oid] for path, oid in entries.items()}


def audit(repo, *, source="index"):
    root = Path(repo).resolve()
    actual = Path(os.fsdecode(git(root, "rev-parse", "--show-toplevel")).strip()).resolve()
    if root != actual:
        raise AssetPairError("Требуется корень Git worktree")
    files, conflicts = snapshot(root, source)
    entries = {path: oid for path, oid in files.items() if path.endswith(".meta")}
    blobs = meta_blobs(root, entries)
    directories = set()
    for path in files:
        parent = PurePosixPath(path).parent
        while str(parent) != ".":
            directories.add(parent.as_posix())
            parent = parent.parent
    violations = [{"meta": path, "owner": path[:-5], "reason": "unmerged_meta",
                   "owner_state": "unmerged"} for path in sorted(conflicts) if path.endswith(".meta")]
    empty_folders, inferred_folders = [], []
    file_count, folder_count, valid_pairs = 0, 0, 0
    for meta, content in sorted(blobs.items()):
        owner = meta[:-5]
        is_folder = bool(re.search(rb"^folderAsset:[ \t]*yes[ \t]*\r?$", content, re.MULTILINE))
        if is_folder or owner in directories:
            folder_count += 1
            if owner in files:
                violations.append({"meta": meta, "owner": owner, "reason": "folder_meta_for_file",
                                   "owner_state": "tracked_file"})
            elif owner not in directories:
                empty_folders.append(owner)
            elif not is_folder:
                inferred_folders.append(owner)
            continue
        file_count += 1
        if owner in files:
            valid_pairs += 1
        else:
            violations.append({"meta": meta, "owner": owner, "reason": "owner_not_tracked",
                               "owner_state": "unmerged" if owner in conflicts else
                                              "untracked" if (root / owner).exists() else "missing",
                               "has_tracked_children": owner in directories})
    missing = [row["owner"] for row in violations if row["reason"] == "owner_not_tracked"]
    if missing:
        ignored_raw = git(root, "check-ignore", "--no-index", "-z", "--stdin",
                          data=b"\0".join(os.fsencode(path) for path in missing) + b"\0", accepted=(0, 1))
        ignored = {os.fsdecode(path) for path in ignored_raw.split(b"\0") if path}
        for row in violations:
            if row["reason"] == "owner_not_tracked" and row["owner"] in ignored:
                row["owner_state"] = "ignored"
    return {"version": 1, "repo": str(root), "source": source, "passed": not violations,
            "ignore_policy_source": "working_tree", "tracked_path_count": len(files),
            "meta_count": len(entries), "file_meta_count": file_count,
            "folder_meta_count": folder_count, "valid_file_pairs": valid_pairs,
            "empty_folders": empty_folders, "inferred_folders": inferred_folders,
            "violations": violations}


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--repo", type=Path, default=Path.cwd())
    parser.add_argument("--source", choices=("index", "head"), default="index")
    parser.add_argument("--report", type=Path, required=True)
    args = parser.parse_args(argv)
    report = args.report.resolve()
    try:
        result = audit(args.repo, source=args.source)
    except Exception as error:
        result = {"passed": False, "repo": str(args.repo.resolve()), "source": args.source,
                  "error": str(error), "violations": []}
    report.parent.mkdir(parents=True, exist_ok=True)
    report.write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding="utf-8")
    summary = {key: result[key] for key in ("passed", "source", "meta_count", "file_meta_count",
               "folder_meta_count", "valid_file_pairs", "error") if key in result}
    summary.update(violation_count=len(result["violations"]),
                   empty_folder_count=len(result.get("empty_folders", [])),
                   inferred_folder_count=len(result.get("inferred_folders", [])),
                   examples=result["violations"][:10], report_path=str(report))
    print(json.dumps(summary, ensure_ascii=False))
    return 0 if result["passed"] else 2


if __name__ == "__main__":
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    raise SystemExit(main())
