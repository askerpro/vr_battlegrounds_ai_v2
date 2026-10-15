"""Технический A от exact B: --dry-run только проверяет; --build пишет отдельный index/objects/new ref.

Реальный index, HEAD, working tree и SDK-файлы не изменяются. Stale audit/file/grant требует повторного аудита.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import stat
import subprocess
import sys
import time
import uuid

ROOT = Path(__file__).resolve().parents[3]
REPORTS = ROOT / "tasks/bots-fix/reports/cc1-round6-input"
BASE = "bb28046079f0ad3251ab3bbc22988027bf00c107"


def require(condition, reason):
    if not condition:
        raise RuntimeError(reason + "; повторите --audit/--dry-run после исправления входов")


def run(argv, data=None, index=None):
    env = dict(os.environ, GIT_OPTIONAL_LOCKS="0", PYTHONIOENCODING="utf-8")
    if index is not None:
        env["GIT_INDEX_FILE"] = str(index)
    return subprocess.check_output(argv, cwd=ROOT, env=env, input=data)


def git(*argv, data=None, index=None):
    return run(["git", "--no-pager", *argv], data=data, index=index)


def cli(*argv):
    result = json.loads(run([sys.executable, "Tools/agents/coordination.py", *argv]))
    require(result.get("ok"), "Read-only coordination command failed")
    return result["result"]


def file_path(path):
    require(isinstance(path, str) and path and "\\" not in path and ":" not in path and "\0" not in path,
            "Некорректный путь")
    require(all(p not in ("", ".", "..") for p in path.split("/")), "Некорректные сегменты пути")
    physical = ROOT / path
    require(physical.resolve().is_relative_to(ROOT.resolve()), "Путь вне worktree")
    # Запрещены также directory junctions/reparse points родителей.
    for candidate in (physical, *physical.parents):
        if candidate == ROOT.parent:
            break
        if candidate.exists():
            attrs = getattr(candidate.lstat(), "st_file_attributes", 0)
            require(not candidate.is_symlink() and not attrs & getattr(stat, "FILE_ATTRIBUTE_REPARSE_POINT", 1024), "Reparse point: " + path)
    return physical


def tree(sha):
    rows = {}
    for record in git("ls-tree", "-rz", sha).split(b"\0"):
        if record:
            fields, path = record.split(b"\t", 1)
            mode, kind, oid = fields.decode().split()
            rows[path.decode()] = (mode, oid, kind)
    return rows


def covers(pattern, path):
    pattern, path = pattern.casefold(), path.casefold()
    if pattern.endswith("/**"):
        return path == pattern[:-3] or path.startswith(pattern[:-2])
    return path == pattern or path == pattern + ".meta"


def scope(spec, path, operation):
    return (operation == "D" and path in spec.get("deletes", [])) or any(covers(p, path) for p in spec.get("writes", []))


def canonical(path):
    physical = file_path(path)
    require(physical.is_file(), "Нет текущего файла: " + path)
    parts = git("check-attr", "-z", "text", "eol", "filter", "--", path).split(b"\0")
    attrs = {parts[i + 1].decode(): parts[i + 2].decode() for i in range(0, len(parts) - 1, 3)}
    require(attrs.get("filter") in (None, "unset", "unspecified"), "Не поддерживается clean filter: " + path)
    raw = physical.read_bytes()
    require(len(raw) <= 64 * 1024 * 1024, "Слишком большой current input: " + path)
    text = attrs.get("text")
    if attrs.get("eol") in ("lf", "crlf") or text == "set" or (text == "auto" and b"\0" not in raw[:8000]):
        raw = raw.replace(b"\r\n", b"\n")
    return raw


def blob_oid(raw):
    return hashlib.sha1(b"blob " + str(len(raw)).encode() + b"\0" + raw).hexdigest()


def load_inputs():
    values, hashes = {}, {}
    for name in ("audit.json", "allowlist-proposed.json", "components-proposed.json"):
        path = file_path((REPORTS.relative_to(ROOT) / name).as_posix())
        raw = path.read_bytes()
        require(len(raw) <= 2 * 1024 * 1024, "Слишком большой input manifest")
        values[name] = json.loads(raw)
        hashes[name] = hashlib.sha256(raw).hexdigest()
    return values, hashes


def validate(inputs):
    audit, allow, manifest = (inputs[n] for n in ("audit.json", "allowlist-proposed.json", "components-proposed.json"))
    require(audit["base_sha"] == allow["base_sha"] == manifest["base_sha"] == BASE, "Несовпадение exact B")
    require(git("rev-parse", "HEAD").decode().strip() == audit["head_sha"] == allow["head_sha"], "HEAD изменился после audit")
    task = next(t for t in cli("status")["snapshot"]["tasks"] if t["task_id"] == "bots-fix")
    stage = next(s for s in task["stages"] if s["id"] == "clip-cutover")
    binding = {"task_id": task["task_id"], "owner": task["owner"], "revision": task["revision"],
               "stage_id": stage["id"], "epoch": stage["epoch"], "worktree": task["worktree"]}
    require(binding == manifest["consumer"] == audit["consumer"], "Consumer revision/epoch/worktree изменился")
    require(Path(binding["worktree"]).resolve() == ROOT.resolve(), "Consumer относится к другому worktree")
    require(stage["state"] == "running" and stage.get("expires", 0) > time.time(), "Current stage не RUNNING/lease expired")
    require(stage["spec_hash"] == audit["current_spec_hash"], "Current scope/spec изменился")
    # Читается только metadata-конфигурация брокера; lease/tokens/private state не загружаются.
    common = Path(git("rev-parse", "--path-format=absolute", "--git-common-dir").decode().strip())
    config = common.parent / ".agent-state/editor-broker/config.json"
    require(json.loads(config.read_bytes())["baseline_sha"] == BASE, "Текущая B брокера изменилась")
    base = tree(BASE)
    desired, buffers, pins = {}, {}, {}
    names = set()
    for value in allow["current_mutations"]:
        path, operation = value["path"], value["operation"]
        physical = file_path(path)
        require(path.casefold() not in names, "Duplicate/alias current path")
        names.add(path.casefold())
        require(scope(stage["spec"], path, operation), "Current input вне scope: " + path)
        require(path.startswith("Assets/") or path.startswith("tasks/bots-fix/tools/cc1-round1/"), "Несогласованный current input: " + path)
        if operation == "D":
            require(path in base and not physical.exists() and value["desired"] is None, "Удаление stale/некорректно: " + path)
            desired[path] = None
        else:
            require(operation == ("M" if path in base else "A"), "Некорректная A/M операция: " + path)
            expected = tuple(value["desired"])
            require(expected[0] in ("100644", "100755") and expected[2] == "blob", "Некорректный mode/type")
            raw = canonical(path)
            require(blob_oid(raw) == expected[1], "Current bytes изменились после audit: " + path)
            desired[path], buffers[path] = expected, raw
    proofs = {s["id"]: s for s in task["stages"]}
    for component in manifest["components"]:
        if component["kind"] == "grant":
            grant = cli("input-grant-show", "--grant-id", component["grant_id"])
            require(grant["revision"] == component["revision"] and grant["active"] and grant["expires"] > time.time(), "Grant revision/activity/TTL изменился")
            require(grant["consumer"] == binding and grant["consumer_spec_hash"] == stage["spec_hash"], "Grant consumer/spec stale")
            require(grant["manifest"]["base_sha"] == BASE, "Grant относится к другой B")
            require(grant["entries"] == allow["grant_pins"], "Grant pins изменились после audit")
            entries = grant["entries"]
        elif component["kind"] == "own_verified":
            own = proofs[component["stage_id"]]
            require(own["state"] in ("verified", "accepted", "merged") and own["epoch"] == component["epoch"], "Own proof state/epoch stale")
            require(component["source_sha"] in {own.get(k) for k in ("verified_sha", "accepted_sha", "merged_sha")}, "Не exact recorded own proof")
            entries = list(component["entries"])
            explicit = {e["path"] for e in entries}
            for entry in list(entries):
                require(entry["source_sha"] == component["source_sha"] and scope(own["spec"], entry["path"], entry["operation"]), "Own proof source/scope mismatch")
                if entry["path"].startswith("Assets/") and not entry["path"].endswith(".meta"):
                    meta = entry["path"] + ".meta"
                    source = tree(entry["source_sha"])
                    require(meta in source, "Own proof companion отсутствует")
                    if meta not in explicit:
                        require(source[meta] == base.get(meta) and scope(own["spec"], meta, "M"), "Changed .meta не явный own entry")
                        raw = git("cat-file", "blob", source[meta][1])
                        entries.append({"path": meta, "operation": "M", "mode": source[meta][0], "base_blob": source[meta][1],
                                        "source_sha": entry["source_sha"], "source_blob": source[meta][1], "sha256": hashlib.sha256(raw).hexdigest(), "companion": True})
        else:
            raise RuntimeError("Unknown component kind")
        for entry in entries:
            path = entry["path"]
            file_path(path)
            require(path.casefold() not in names, "Duplicate/alias or current/component overlap: " + path)
            names.add(path.casefold())
            expected = (entry["mode"], entry["source_blob"], "blob")
            require(entry["operation"] in ("A", "M") and entry["mode"] in ("100644", "100755"), "Component operation/mode unsupported")
            require((base.get(path) or (None, None))[1] == entry["base_blob"], "Component base blob mismatch")
            require(tree(entry["source_sha"]).get(path) == expected, "Component source tree mismatch")
            require(hashlib.sha256(git("cat-file", "blob", entry["source_blob"])).hexdigest() == entry["sha256"], "Component SHA256 mismatch")
            require(not scope(stage["spec"], path, "M"), "Immutable component пересекает current scope")
            desired[path] = expected
            pins[path] = entry
    require(sum(len(raw) for raw in buffers.values()) <= 64 * 1024 * 1024, "Input buffer budget exceeded")
    expected_tree = dict(base)
    for path, value in desired.items():
        if value is None:
            expected_tree.pop(path, None)
        else:
            expected_tree[path] = value
    # Отсутствующие метаданные/alias-файлы запрещены до записи index.
    folded = {}
    for path in expected_tree:
        require(path.casefold() not in folded, "Case alias в итоговом tree")
        folded[path.casefold()] = path
        if path in desired and desired[path] is not None and path.startswith("Assets/") and not path.endswith(".meta"):
            require(path + ".meta" in expected_tree, "Итоговый Assets path без .meta: " + path)
    for row in audit["paths"]:
        if row["category"].startswith("excluded-") or row["category"] == "equals-B-after-dirty":
            require(expected_tree.get(row["path"]) == base.get(row["path"]), "Excluded path попал в A: " + row["path"])
    return base, expected_tree, desired, buffers, pins, binding


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    action = parser.add_mutually_exclusive_group(required=True)
    action.add_argument("--dry-run", action="store_true")
    action.add_argument("--build", action="store_true")
    args = parser.parse_args()
    real_index = Path(git("rev-parse", "--git-path", "index").decode().strip())
    if not real_index.is_absolute():
        real_index = ROOT / real_index
    head_before = git("rev-parse", "HEAD").decode().strip()
    index_before = hashlib.sha256(real_index.read_bytes()).hexdigest()
    inputs, checksums = load_inputs()
    base, expected, desired, buffers, pins, binding = validate(inputs)
    require(hashlib.sha256(real_index.read_bytes()).hexdigest() == index_before and git("rev-parse", "HEAD").decode().strip() == head_before, "HEAD/index изменился во время проверки")
    count = sum(expected.get(path) != base.get(path) for path in set(base) | set(expected))
    if args.dry_run:
        print(json.dumps({"dry_run": True, "base_sha": BASE, "consumer": binding, "delta_paths": count,
                          "current_mutations": len(inputs["allowlist-proposed.json"]["current_mutations"]), "immutable_pins": len(pins),
                          "manifest_checksums": checksums, "head_index_unchanged": True, "git_mutations": False}))
        return
    # Повторная live проверка/CAS непосредственно перед первым Git write.
    repeated, repeated_hashes = load_inputs()
    require(checksums == repeated_hashes, "Audit/manifest bytes изменились")
    validate(repeated)
    nonce = uuid.uuid4().hex
    temp_index = REPORTS / ("component-build-" + nonce + ".index")
    require(not temp_index.exists() and temp_index.resolve() != real_index.resolve(), "Некорректный temporary index")
    ref = "refs/heads/codex/tmp/bots-cc1-round6-components-" + nonce
    git("read-tree", BASE, index=temp_index)
    rows = []
    for path, value in sorted(desired.items()):
        if path in buffers:
            written = git("hash-object", "-w", "--stdin", data=buffers[path]).decode().strip()
            require(written == value[1], "Written blob CAS mismatch")
        mode, oid = ("0", "0" * 40) if value is None else value[:2]
        rows.append((mode + " " + oid + "\t" + path).encode() + b"\0")
    git("update-index", "-z", "--index-info", data=b"".join(rows), index=temp_index)
    tree_sha = git("write-tree", index=temp_index).decode().strip()
    require(tree(tree_sha) == expected, "Полный A tree не совпадает с exact allowlist")
    # Проверка stale inputs после построения tree до создания checkpoint/ref.
    latest, latest_hashes = load_inputs()
    require(latest_hashes == checksums, "Manifest изменился во время build")
    validate(latest)
    commit = git("-c", "user.name=Editor Broker", "-c", "user.email=editor-broker@local.invalid", "commit-tree", tree_sha, "-p", BASE,
                 data="Технический checkpoint bots cc1-round6 immutable components; не приёмка gameplay\n".encode()).decode().strip()
    git("update-ref", ref, commit, "0" * 40)
    require(hashlib.sha256(real_index.read_bytes()).hexdigest() == index_before and git("rev-parse", "HEAD").decode().strip() == head_before, "HEAD/index изменился во время build")
    receipt = {"sha": commit, "ref": ref, "tree": tree_sha, "base_sha": BASE, "temporary_index": str(temp_index),
               "delta_paths": count, "immutable_pins": list(pins.values()), "consumer": binding, "manifest_checksums": checksums,
               "technical_checkpoint_only": True, "head_index_unchanged": True}
    receipt_path = REPORTS / ("component-build-" + nonce + ".json")
    receipt_path.write_text(json.dumps(receipt, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps(receipt, ensure_ascii=True))


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, OSError, subprocess.CalledProcessError) as error:
        print(json.dumps({"ok": False, "error": str(error), "build_not_accepted": True}, ensure_ascii=True), file=sys.stderr)
        raise SystemExit(1)
