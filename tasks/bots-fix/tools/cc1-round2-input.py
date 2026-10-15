"""Только read-only аудит exact B→A/components; пишет отчёты, не изменяет Git/coordination."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import time

BASE = "bb28046079f0ad3251ab3bbc22988027bf00c107"
GRANT = "precache81-bots-r50-20261014"
PROOFS = {
    "binding-foundation": (1, "6849ba0e6ba3f6201b7ff0512583ea3828728e6e"),
    "armed-development": (21, "679c4c71773c73db677f16b0e0739d6869c6c6ab"),
    "pf-prototype": (5, "43531cea398c2ce8b84cd52deef05901d057c367"),
    "pf-equipment": (3, "a1040a95f5d01a6e11c4535dcf6607c56103c877"),
    "pf-network": (3, "5761bb36b1e7a737a58e779dfff8380458248ad2"),
}
ROOT = Path(__file__).resolve().parents[3]
OUT = ROOT / "tasks/bots-fix/reports/cc1-round6-input"


def command(argv):
    env = dict(os.environ, GIT_OPTIONAL_LOCKS="0", PYTHONIOENCODING="utf-8")
    return subprocess.check_output(argv, cwd=ROOT, env=env)


def git(*args):
    return command(["git", "--no-pager", *args])


def cli(*args):
    result = json.loads(command([sys.executable, "Tools/agents/coordination.py", *args]))
    if not result.get("ok"):
        raise RuntimeError("Read-only coordination command failed")
    return result["result"]


def tree(sha):
    result = {}
    for record in git("ls-tree", "-rz", sha).split(b"\0"):
        if not record:
            continue
        fields, path = record.split(b"\t", 1)
        mode, kind, oid = fields.decode().split()
        result[path.decode("utf-8")] = (mode, oid, kind)
    return result


def covers(pattern, path):
    pattern, path = pattern.casefold(), path.casefold()
    if pattern.endswith("/**"):
        return path == pattern[:-3] or path.startswith(pattern[:-2])
    return path == pattern or path == pattern + ".meta"


def scope(spec, path, operation):
    if operation == "D" and path in spec.get("deletes", []):
        return True
    return any(covers(pattern, path) for pattern in spec.get("writes", []))


def sha1_blob(raw):
    return hashlib.sha1(b"blob " + str(len(raw)).encode() + b"\0" + raw).hexdigest()


def entry(path, source, source_tree, base_tree):
    mode, oid, kind = source_tree[path]
    if kind != "blob" or mode not in ("100644", "100755"):
        raise RuntimeError("Unsupported component mode/type: " + path)
    raw = git("cat-file", "blob", oid)
    return {"path": path, "operation": "M" if path in base_tree else "A", "mode": mode,
            "base_blob": base_tree[path][1] if path in base_tree else None,
            "source_sha": source, "source_blob": oid, "sha256": hashlib.sha256(raw).hexdigest()}


def write(name, value):
    (OUT / name).write_text(json.dumps(value, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--audit", action="store_true")
    mode.add_argument("--dry-run", action="store_true")
    args = parser.parse_args()
    before_head = git("rev-parse", "HEAD").decode().strip()
    index_path = Path(git("rev-parse", "--git-path", "index").decode().strip())
    if not index_path.is_absolute():
        index_path = ROOT / index_path
    before_index = hashlib.sha256(index_path.read_bytes()).hexdigest()
    status = cli("status")
    task = next(t for t in status["snapshot"]["tasks"] if t["task_id"] == "bots-fix")
    stages = {s["id"]: s for s in task["stages"]}
    current = stages["clip-cutover"]
    grant = cli("input-grant-show", "--grant-id", GRANT)
    binding = {"task_id": "bots-fix", "owner": task["owner"], "revision": task["revision"],
               "stage_id": current["id"], "epoch": current["epoch"], "worktree": task["worktree"]}
    grant_binding_valid = binding == grant["consumer"] and current["spec_hash"] == grant["consumer_spec_hash"]
    assert grant["manifest"]["base_sha"] == BASE
    base, head = tree(BASE), tree(before_head)
    proof_trees, proof_summary = {}, []
    for name, (epoch, sha) in PROOFS.items():
        stage = stages[name]
        assert stage["epoch"] == epoch and stage["state"] in ("verified", "accepted", "merged")
        assert sha in {stage.get(k) for k in ("verified_sha", "accepted_sha", "merged_sha")}
        proof_trees[name] = tree(sha)
        proof_summary.append({"stage_id": name, "epoch": epoch, "source_sha": sha,
                              "state": stage["state"], "spec_hash": stage["spec_hash"], "writes": stage["spec"].get("writes", [])})
    dirty = set(x.decode() for x in git("diff", "--no-renames", "--name-only", "-z", "HEAD").split(b"\0") if x)
    dirty.update(x.decode() for x in git("ls-files", "--others", "--exclude-standard", "-z").split(b"\0") if x)
    committed = {p for p in set(base) | set(head) if base.get(p) != head.get(p)}
    candidates = committed | dirty
    records, selected, blocked, components = [], [], [], {}
    for path in sorted(candidates):
        # Не читать конфиги/credentials/private reports даже при случайной локальной правке.
        if not path.startswith(("Assets/", "Tools/", "Packages/", "ProjectSettings/", "tasks/bots-fix/", "Docs/", "experts/")):
            records.append({"path": path, "category": "excluded-private-or-unrelated"})
            continue
        actual = head.get(path)
        physical = ROOT / path
        canonical_note = None
        if path in dirty:
            if not physical.exists():
                actual = None
            elif physical.is_symlink() or physical.is_dir():
                records.append({"path": path, "category": "blocked-file-kind"})
                blocked.append(path)
                continue
            else:
                attrs = git("check-attr", "-z", "text", "eol", "filter", "--", path).split(b"\0")
                values = {attrs[i + 1].decode(): attrs[i + 2].decode() for i in range(0, len(attrs) - 1, 3)}
                if values.get("filter") not in ("unspecified", "unset", None):
                    records.append({"path": path, "category": "blocked-clean-filter", "filter": values["filter"]})
                    blocked.append(path)
                    continue
                raw = physical.read_bytes()
                text = values.get("text")
                if values.get("eol") in ("lf", "crlf") or text == "set" or (text == "auto" and b"\0" not in raw[:8000]):
                    canonical_note = "Git text canonical CRLF→LF computed in memory; no clean filter executed"
                    raw = raw.replace(b"\r\n", b"\n")
                actual = ((head.get(path) or base.get(path) or ("100644",))[0], sha1_blob(raw), "blob")
        if actual == base.get(path):
            records.append({"path": path, "category": "equals-B-after-dirty", "committed_delta": path in committed})
            continue
        operation = "D" if actual is None else "M" if path in base else "A"
        row = {"path": path, "operation": operation, "base": base.get(path), "head": head.get(path),
               "desired": actual, "dirty": path in dirty, "committed_delta": path in committed,
               "current_scope": scope(current["spec"], path, operation), "canonical_note": canonical_note}
        matches = []
        for name, proof_tree in proof_trees.items():
            if actual and proof_tree.get(path) == actual and scope(stages[name]["spec"], path, operation):
                matches.append(name)
        row["exact_recorded_proof_matches"] = matches
        runtime = path.startswith("Assets/") or path.startswith("tasks/bots-fix/tools/cc1-round1/")
        if runtime and row["current_scope"]:
            row["category"] = "current-mutation"
            selected.append({"path": path, "operation": operation, "desired": actual})
        elif runtime and matches and actual and operation != "D":
            name = matches[-1]
            row["category"] = "own_verified"
            source = PROOFS[name][1]
            components.setdefault(name, []).append(entry(path, source, proof_trees[name], base))
        elif runtime:
            row["category"] = "blocked-runtime-outside-current-without-exact-proof"
            blocked.append(path)
        else:
            row["category"] = "excluded-docs-backlog-or-unneeded-helper"
        records.append(row)
    own = []
    for name, entries in components.items():
        names = {e["path"] for e in entries}
        for value in list(entries):
            path = value["path"]
            if path.startswith("Assets/") and not path.endswith(".meta"):
                meta = path + ".meta"
                side = proof_trees[name].get(meta)
                assert side, "Missing proof meta: " + meta
                assert scope(stages[name]["spec"], meta, "M")
                if side != base.get(meta) and meta not in names:
                    entries.append(entry(meta, PROOFS[name][1], proof_trees[name], base))
                    names.add(meta)
        own.append({"kind": "own_verified", "stage_id": name, "epoch": PROOFS[name][0],
                    "source_sha": PROOFS[name][1], "entries": sorted(entries, key=lambda e: e["path"])})
    grant_pins = []
    for value in grant["entries"]:
        source_tree = tree(value["source_sha"])
        expected = (value["mode"], value["source_blob"], "blob")
        assert source_tree.get(value["path"]) == expected
        assert (base.get(value["path"]) or (None, None))[1] == value["base_blob"]
        assert hashlib.sha256(git("cat-file", "blob", value["source_blob"])).hexdigest() == value["sha256"]
        assert not scope(current["spec"], value["path"], "M")
        grant_pins.append(value)
    manifest = {"version": 1, "purpose": "technical-unity-authoring", "base_sha": BASE,
                "consumer": binding, "components": [{"kind": "grant", "grant_id": GRANT, "revision": grant["revision"]}, *own]}
    categories = {category: sum(r["category"] == category for r in records) for category in sorted({r["category"] for r in records})}
    after_index = hashlib.sha256(index_path.read_bytes()).hexdigest()
    assert before_index == after_index and git("rev-parse", "HEAD").decode().strip() == before_head
    allowlist = {"base_sha": BASE, "head_sha": before_head, "current_mutations": selected,
                 "grant_pins": grant_pins, "own_verified": own, "blocked_runtime_paths": blocked}
    audit = {"read_only_git": True, "head_sha": before_head, "base_sha": BASE, "index_sha256_before_after": before_index,
                         "current_spec_hash": current["spec_hash"], "current_stage_state": current["state"],
                         "stage_expires": current.get("expires"), "grant_expires": grant["expires"], "grant_active": grant["active"],
                         "consumer": binding, "grant_consumer": grant["consumer"], "grant_binding_valid": grant_binding_valid,
                         "grant_spec_hash": grant["consumer_spec_hash"], "grant_revision": grant["revision"],
                         "audit_utc_epoch": time.time(), "counts": categories, "proofs": proof_summary, "paths": records,
                         "construction_warning": "checkpoint --onto-baseline --path filters only dirty deltas against HEAD; committed excluded paths remain. Assemble A from B plus exact allowlist/pins, not from whole HEAD.",
                         "ready": not blocked and grant_binding_valid and grant["active"] and grant["expires"] > time.time(), "admission_not_validated": True}
    if args.dry_run:
        print(json.dumps({"components-proposed.json": manifest, "allowlist-proposed.json": allowlist, "audit.json": audit}))
        return
    OUT.mkdir(parents=True, exist_ok=True)
    write("components-proposed.json", manifest)
    write("allowlist-proposed.json", allowlist)
    write("audit.json", audit)
    print(json.dumps({"counts": categories, "own_verified_components": len(own), "own_verified_primary_entries": sum(len(o["entries"]) for o in own),
                      "blocked_runtime_paths": blocked[:10], "grant_pins": len(grant_pins), "grant_binding_valid": grant_binding_valid,
                      "ready": not blocked and grant_binding_valid and grant["active"] and grant["expires"] > time.time(),
                      "evidence": str(OUT / "audit.json"), "git_head_index_unchanged": True}))


if __name__ == "__main__":
    main()
