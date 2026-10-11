"""Технический, НЕ ACCEPTED input authoring: immutable blobs и private Git index.

Не пишет Assets, HEAD, рабочий index или stash; не запускает Unity и не даёт lease.
Перед обычным запуском principal должен проверить source SHA и разрешение owner.
"""

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import uuid


ROOT = Path(__file__).resolve().parents[3]
REPORT_ROOT = ROOT / "tasks/weapon-system/reports/use-context-prefab-authoring"
HANDOFF = Path("F:/CodexWorktrees/bots-fix/Vr_Battlegrounds_ai/tasks/bots-fix/reports/weapon-owner-handoff")
FOREIGN = "8435c2291cfe5d4e2c824bbe8093e217b21034af"
FOREIGN_PRIOR = "c05eabbfb77ebc68a197593c54d1567ee3ab68da"
FOREIGN_BASE = "6c0473fda8faa5e217117da0e5f6a4ed2866b634"
WEAPONS = "Assets/ThirdParty/UltimateXR/Runtime/Scripts/Mechanics/Weapons/"
GRAB = "Assets/ThirdParty/UltimateXR/Runtime/Scripts/Manipulation/"
OWN_PATHS = frozenset({
    "Assets/Scripts/Weapons/WeaponUseContext.cs",
    "Assets/Scripts/Weapons/WeaponUseContext.cs.meta",
    "Assets/Scripts/Weapons/WeaponSystem/Sensors/WeaponContextSensor.cs",
    "Assets/Scripts/Weapons/WeaponSystem/WeaponSystem.cs",
    "Assets/Scripts/Weapons/Core/WeaponEvents.cs",
    "Assets/Scripts/Weapons/Core/WeaponStateMachine.cs",
    WEAPONS + "IUxrFirearmRootRecoilPolicy.cs",
    WEAPONS + "IUxrFirearmRootRecoilPolicy.cs.meta",
    WEAPONS + "UxrFirearmWeapon.cs",
    GRAB + "UxrGrabber.cs",
    GRAB + "UxrGrabber.StateSave.cs",
    GRAB + "UxrGrabManager.cs",
    GRAB + "UxrGrabManager.StateSave.cs",
    WEAPONS + "IUxrWeaponOwnerProvider.cs",
    WEAPONS + "IUxrWeaponOwnerProvider.cs.meta",
    WEAPONS + "UxrWeapon.cs",
    WEAPONS + "UxrWeapon.Custom.cs",
    WEAPONS + "UxrWeaponManager.cs",
    "Assets/Editor/VR_Battlegrounds/Gameplay/WeaponSystemAuthoring.cs",
})
FOREIGN_PATHS = frozenset({
    "Assets/Scripts/Weapons/Equipment.meta",
    "Assets/Scripts/Weapons/Equipment/WeaponEquipmentBinding.cs",
    "Assets/Scripts/Weapons/Equipment/WeaponEquipmentBinding.cs.meta",
    GRAB + "IUxrGrabAvailability.cs",
    GRAB + "IUxrGrabAvailability.cs.meta",
    GRAB + "UxrGrabbableObject.cs",
    GRAB + "UxrGrabManager.Manipulation.cs",
    "Assets/Scripts/Bots/BotClipBody.cs",
    "Assets/Scripts/Bots/BotClipBody.cs.meta",
    "Assets/Scripts/Bots/BotCombatAssets.cs",
    "Assets/Scripts/Player/PlayerSession.cs",
})
ALL_PATHS = OWN_PATHS | FOREIGN_PATHS


def require(condition, message):
    if not condition:
        raise ValueError(message)


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def normalized(data):
    # Только сравнение baseline/handoff; graft никогда не нормализует source.
    return data.replace(b"\r\n", b"\n")


def git(*args, index=None, input_data=None, allowed_codes=(0,)):
    env = os.environ.copy()
    env.pop("GIT_INDEX_FILE", None)
    for key in ("GIT_DIR", "GIT_WORK_TREE", "GIT_COMMON_DIR", "GIT_OBJECT_DIRECTORY", "GIT_ALTERNATE_OBJECT_DIRECTORIES"):
        require(not env.get(key), "Неожиданная Git environment: " + key)
    if index is not None:
        env["GIT_INDEX_FILE"] = str(index)
    result = subprocess.run(
        ["git", "--no-pager", "-C", str(ROOT), *args],
        input=input_data, capture_output=True, env=env,
    )
    if result.returncode not in allowed_codes:
        raise RuntimeError("git " + " ".join(args) + ": " + result.stderr.decode("utf-8", errors="replace"))
    return result.stdout


def commit_sha(value):
    require(re.fullmatch(r"[0-9a-f]{40}", value) is not None, "Нужен полный immutable commit SHA: " + value)
    require(git("cat-file", "-t", value).strip() == b"commit", "Объект не commit: " + value)
    return value


def parent_of(commit):
    parents = git("rev-list", "--parents", "-n", "1", commit).decode("ascii").split()
    require(len(parents) == 2, "Candidate должен иметь ровно одного parent: " + commit)
    return parents[1]


def changed_paths(before, after):
    return {p.decode("utf-8") for p in git("diff", "--no-renames", "--name-only", "-z", before, after).split(b"\0") if p}


def entry(tree, path):
    raw = git("ls-tree", "-z", tree, "--", path)
    if not raw:
        return None
    records = raw.rstrip(b"\0").split(b"\0")
    require(len(records) == 1, "Неоднозначный tree path: " + path)
    header, found = records[0].split(b"\t", 1)
    mode, kind, blob = header.decode("ascii").split()
    require(found.decode("utf-8") == path and kind == "blob" and mode in ("100644", "100755"), "Не regular source blob: " + path)
    return {"mode": mode, "blob": blob}


def blob_bytes(item):
    return git("cat-file", "blob", item["blob"])


def local_git_state():
    # Только чтение настоящего index; private index не участвует в snapshot.
    index = Path(git("rev-parse", "--path-format=absolute", "--git-path", "index").decode("utf-8").strip())
    return {
        "head": git("rev-parse", "HEAD").decode("ascii").strip(),
        "headSymbolic": git("symbolic-ref", "-q", "HEAD", allowed_codes=(0, 1)).decode("utf-8").strip(),
        "indexPath": str(index),
        "indexSha256": sha256(index.read_bytes()) if index.exists() else None,
        "stash": git("rev-parse", "--verify", "--quiet", "refs/stash", allowed_codes=(0, 1)).decode("ascii").strip(),
    }


def validate_sources(own, base):
    require(len(OWN_PATHS) == 19 and len(FOREIGN_PATHS) == 11 and len(ALL_PATHS) == 30, "Ошибка scopes")
    own_parent = parent_of(own)
    own_delta = changed_paths(own_parent, own)
    require(own_delta and own_delta <= OWN_PATHS, "Own candidate delta вне exact19 или пуст")
    manifest_bytes = (HANDOFF / "manifest.json").read_bytes()
    foreign_manifest = json.loads(manifest_bytes)
    require(foreign_manifest["baseline"] == FOREIGN_BASE and foreign_manifest["priorCandidate"] == FOREIGN_PRIOR, "Foreign manifest baseline/parent изменён")
    items = foreign_manifest["paths"]
    require(len(items) == 11 and {p["path"] for p in items} == FOREIGN_PATHS, "Foreign manifest не exact11")
    require(parent_of(FOREIGN) == FOREIGN_PRIOR and parent_of(FOREIGN_PRIOR) == FOREIGN_BASE, "Foreign parent chain изменён")
    require(changed_paths(FOREIGN_BASE, FOREIGN) == FOREIGN_PATHS, "Foreign baseline delta не exact11")
    require(changed_paths(FOREIGN_PRIOR, FOREIGN) == {"Assets/Scripts/Weapons/Equipment/WeaponEquipmentBinding.cs"}, "Foreign adapter delta изменён")
    inventory = []
    own_new = foreign_new = 0
    for path in sorted(OWN_PATHS):
        candidate = entry(own, path)
        baseline, predecessor = entry(base, path), entry(own_parent, path)
        require(candidate is not None, "Own source отсутствует: " + path)
        require(predecessor == baseline, "Own source parent baseline не равен BASE: " + path)
        own_new += baseline is None
        inventory.append({"path": path, "owner": "weapon-system", "sourceCommit": own, **candidate,
                          "baselineBlob": baseline["blob"] if baseline else None, "baselineCheck": "raw-blob-and-mode", "sourceSha256": sha256(blob_bytes(candidate))})
    for item in sorted(items, key=lambda p: p["path"]):
        path = item["path"]
        candidate, foreign_baseline, baseline = entry(FOREIGN, path), entry(FOREIGN_BASE, path), entry(base, path)
        require(candidate is not None, "Foreign source отсутствует: " + path)
        physical = (HANDOFF / "candidate" / path).read_bytes()
        source = blob_bytes(candidate)
        require(sha256(physical) == item["candidateSha256"], "Foreign physical manifest hash mismatch: " + path)
        require(normalized(source) == normalized(physical), "Foreign physical/git blob mismatch: " + path)
        if item["baselineSha256"] is None:
            require(foreign_baseline is None and baseline is None, "Foreign new path существует в baseline: " + path)
            foreign_new += 1
        else:
            require(foreign_baseline is not None and baseline is not None, "Foreign existing baseline отсутствует: " + path)
            physical_base = (HANDOFF / "baseline-files" / path).read_bytes()
            require(sha256(physical_base) == item["baselineSha256"], "Foreign baseline physical hash mismatch: " + path)
            require(normalized(blob_bytes(foreign_baseline)) == normalized(physical_base), "Foreign baseline physical/blob mismatch: " + path)
            require(foreign_baseline["mode"] == baseline["mode"] and normalized(blob_bytes(foreign_baseline)) == normalized(blob_bytes(baseline)), "Foreign existing baseline не совместим с BASE: " + path)
        inventory.append({"path": path, "owner": "bots-fix", "sourceCommit": FOREIGN, **candidate,
                          "baselineBlob": baseline["blob"] if baseline else None, "foreignBaselineBlob": foreign_baseline["blob"] if foreign_baseline else None,
                          "baselineCheck": "CRLF-normalized-and-mode", "sourceSha256": sha256(source), "handoffSha256": sha256(physical)})
    require(own_new == 6 and foreign_new == 7, "Неверное число новых source paths")
    return {"base": base, "ownCandidate": own, "ownParent": own_parent, "ownDelta": sorted(own_delta),
            "foreignCandidate": FOREIGN, "foreignParent": FOREIGN_PRIOR, "foreignBaseline": FOREIGN_BASE,
            "handoffManifestSha256": sha256(manifest_bytes), "paths": sorted(inventory, key=lambda p: p["path"])}


def report_directory(value):
    directory = Path(value).resolve()
    require(directory.is_relative_to(REPORT_ROOT.resolve()), "report-dir должен находиться в own ignored authoring reports")
    suffix = directory.relative_to(REPORT_ROOT.resolve())
    require(not suffix.parts or suffix.parts[0].startswith("input-builder"), "report-dir вне input-builder scope")
    probe = directory / ("input-builder-" + uuid.uuid4().hex + ".json")
    git("check-ignore", "--no-index", "-q", "--", str(probe.relative_to(ROOT)))
    return directory


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--own-candidate", required=True, help="Полный SHA immutable own exact19 source checkpoint")
    parser.add_argument("--base", required=True, help="Полный SHA accepted BASE, единственный parent technical input")
    parser.add_argument("--report-dir", required=True, help="Ignored tasks/weapon-system/reports/use-context-prefab-authoring[/input-builder*]")
    parser.add_argument("--dry-run", action="store_true", help="Только source/baseline проверки и report; без index, Git objects и ref")
    parser.add_argument("--owner-permission-event", type=int, help="Hub event явного owner permission exact11; для composition обязателен")
    args = parser.parse_args()
    require(args.dry_run or (args.owner_permission_event is not None and args.owner_permission_event > 0), "Composition требует owner permission event от principal")
    directory = report_directory(args.report_dir)
    before = local_git_state()
    own, base = commit_sha(args.own_candidate), commit_sha(args.base)
    for sha in (FOREIGN, FOREIGN_PRIOR, FOREIGN_BASE):
        commit_sha(sha)
    report = validate_sources(own, base)
    run_id = uuid.uuid4().hex
    run = directory / ("input-builder-" + run_id)
    run.mkdir(parents=True, exist_ok=False)
    report.update({"developmentOnly": True, "accepted": False, "dryRun": args.dry_run,
                   "ownerPermissionEvent": args.owner_permission_event, "permissionEvidence": "supplied-by-principal-not-live-hub-verified",
                   "unityPermission": "pending-lease-and-principal-gates", "sourceContentPolicy": "immutable-git-blobs-no-conversion",
                   "stateBefore": before})
    try:
        if not args.dry_run:
            index = run / "input-builder.index"
            git("read-tree", base, index=index)
            for item in report["paths"]:
                git("update-index", "--add", "--cacheinfo", item["mode"] + "," + item["blob"] + "," + item["path"], index=index)
            tree = git("write-tree", index=index).decode("ascii").strip()
            require(changed_paths(base, tree) <= ALL_PATHS, "Technical tree содержит лишние пути")
            for item in report["paths"]:
                require(entry(tree, item["path"]) == {"mode": item["mode"], "blob": item["blob"]}, "Graft identity mismatch: " + item["path"])
            message = "Технический input authoring weapon-system; НЕ ACCEPTED\n\nExact19 own + exact11 owner-approved immutable blobs.\n"
            technical = git("commit-tree", tree, "-p", base, input_data=message.encode("utf-8"), index=index).decode("ascii").strip()
            require(parent_of(technical) == base, "Technical parent mismatch")
            ref = "refs/heads/codex/tmp/weapon-authoring-input-" + run_id
            report.update({"tree": tree, "technicalCommit": technical, "privateIndex": str(index)})
            git("update-ref", ref, technical, "0" * 40, index=index)
            report.update({"tree": tree, "technicalCommit": technical, "technicalRef": ref, "privateIndex": str(index), "treeDelta": sorted(changed_paths(base, tree))})
        report["stateAfter"] = local_git_state()
        require(report["stateAfter"] == before, "HEAD/index/stash изменены во время подготовки; не откатывать чужие изменения")
        report["ok"] = True
    except Exception as error:
        report["ok"] = False
        report["error"] = str(error)
        report["stateAfter"] = local_git_state()
        raise
    finally:
        (run / "input-builder-manifest.json").write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps({"ok": True, "dryRun": args.dry_run, "paths": len(report["paths"]), "manifest": str(run / "input-builder-manifest.json"), "technicalCommit": report.get("technicalCommit"), "accepted": False}, ensure_ascii=False))


if __name__ == "__main__":
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
    try:
        main()
    except (ValueError, RuntimeError, OSError, KeyError, json.JSONDecodeError) as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
