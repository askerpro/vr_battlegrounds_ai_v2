"""Точный immutable grant и проверка защищённых текстур; Git и ассеты только читаются."""
import argparse
import hashlib
import json
import re
import subprocess
from pathlib import Path


def git(*args):
    return subprocess.check_output(["git", *args])


def blob_at(commit, path):
    record = git("ls-tree", "-z", commit, "--", path)
    if not record:
        return None
    header, found = record.rstrip(b"\0").split(b"\t", 1)
    mode, kind, oid = header.decode("ascii").split()
    if found.decode("utf-8") != path or kind != "blob" or mode not in ("100644", "100755"):
        raise ValueError("Недопустимая запись Git: " + path)
    return mode, oid, git("cat-file", "blob", oid)


def belongs(path):
    return (
        path == "Assets/Scripts/Maps/Detail.meta"
        or path.startswith("Assets/Scripts/Maps/Detail/")
        or path in (
            "Assets/Scenes/Lobby.unity", "ProjectSettings/QualitySettings.asset",
            "Assets/Scenes/Lobby.unity.meta", "Assets/Scenes/Lobby.meta",
            "Assets/Scenes/Lobby/LobbyDetailProfile.asset",
            "Assets/Scenes/Lobby/LobbyDetailProfile.asset.meta",
        )
        or (path.endswith(".meta") and path.startswith(("Assets/Art/Lobby/", "Assets/Materials/Lobby/")))
    )


def guid_from(raw):
    values = [line.removeprefix("guid: ").strip() for line in raw.decode("utf-8").splitlines() if line.startswith("guid: ")]
    if len(values) != 1 or not re.fullmatch(r"[0-9a-fA-F]{32}", values[0]):
        raise ValueError("Нужна ровно одна корректная GUID-строка")
    return values[0].lower()


def read_textures(path):
    report = json.loads(Path(path).read_text(encoding="utf-8"))
    entries = report["entries"]
    if report["textureCount"] != 252 or len(entries) != 252 or len({t["path"] for t in entries}) != 252 or len({t["guid"] for t in entries}) != 252:
        raise ValueError("Экспорт должен содержать252 уникальных paths/GUID")
    return report


def required_files():
    paths = ["Assets/Scripts/Maps/Detail.meta", "Assets/Scenes/Lobby.meta",
             "Assets/Scenes/Lobby.unity", "Assets/Scenes/Lobby.unity.meta",
             "ProjectSettings/QualitySettings.asset",
             "Assets/Scenes/Lobby/LobbyDetailProfile.asset", "Assets/Scenes/Lobby/LobbyDetailProfile.asset.meta"]
    for name in ("SceneDetailProfile", "SceneDetailApplier", "SceneDetailDebugSection"):
        path = "Assets/Scripts/Maps/Detail/" + name + ".cs"
        paths.extend((path, path + ".meta"))
    return paths


def working_evidence(args):
    # Это доказательство physical snapshot, а не grant или записанный verified stage.
    textures = read_textures(args.textures)
    changed = set(git("diff", "HEAD", "--name-only", "-z", "--", "Assets/Art/Lobby", "Assets/Materials/Lobby").decode().split("\0"))
    changed.update(git("ls-files", "--others", "--exclude-standard", "-z", "--", "Assets/Art/Lobby", "Assets/Materials/Lobby").decode().split("\0"))
    changed.discard("")
    head = git("rev-parse", "HEAD").decode().strip()
    protected, changed_guids, records = [], set(), []
    for item in textures["entries"]:
        path = item["path"] + ".meta"
        raw = Path(path).read_bytes()
        guid = guid_from(raw)
        if guid != item["guid"]:
            raise ValueError("GUID отличается от authoring export: " + path)
        if item["protectedTexture"]:
            old = blob_at(head, path)
            if path in changed or not old or old[2] != raw:
                raise ValueError("Изменена защищённая texture-meta: " + path)
            protected.append({"guid": guid, "path": item["path"], "head_blob": old[1]})
        if path in changed:
            changed_guids.add(guid)
        records.append({"path": path, "sha256": hashlib.sha256(raw).hexdigest()})
    intersection = sorted({p["guid"] for p in protected} & changed_guids)
    if textures["textureCount"] != 252 or len(protected) != 112 or len(changed) != 140 or len(changed_guids) != 140 or intersection:
        raise ValueError("Неверный состав или пересечение snapshot 252/112/140")
    for path in ("Assets/Scenes/Lobby.unity", "ProjectSettings/QualitySettings.asset",
                 "Assets/Scenes/Lobby/LobbyDetailProfile.asset",
                 "Assets/Scripts/Maps/Detail/SceneDetailApplier.cs",
                 "Assets/Scripts/Maps/Detail/SceneDetailProfile.cs",
                 "Assets/Scripts/Maps/Detail/SceneDetailDebugSection.cs",
                 "Assets/Editor/VR_Battlegrounds/Maps/Detail/SceneDetailSetup.cs"):
        records.append({"path": path, "sha256": hashlib.sha256(Path(path).read_bytes()).hexdigest()})
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    report = {"passed": True, "scope": "Current physical snapshot against HEAD; not immutable grant or runtime acceptance",
              "head_sha": head, "protected_count": 112, "changed_count": 140, "intersection": intersection,
              "protected": protected, "changed_guids": sorted(changed_guids), "physical_files": records}
    out.write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps({"passed": True, "protected_count": 112, "changed_count": 140,
                      "disjoint": True, "proof": str(out), "sha256": hashlib.sha256(out.read_bytes()).hexdigest()}, ensure_ascii=False))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base")
    parser.add_argument("--source")
    parser.add_argument("--evidence-only", action="store_true")
    parser.add_argument("--grant-id", default="scene-detail-to-debug-menu-runtime")
    parser.add_argument("--revision", type=int, default=1)
    parser.add_argument("--textures", default="tasks/scene-detail/reports/protected-textures/lobby-textures.json")
    parser.add_argument("--out")
    args = parser.parse_args()
    if args.out is None:
        args.out = ("tasks/scene-detail/reports/protected-textures/working-snapshot-proof.json" if args.evidence_only
                    else "tasks/scene-detail/reports/ui-provider/grant.json")
    if args.evidence_only:
        working_evidence(args)
        return
    if not args.base or not args.source:
        parser.error("Для immutable grant нужны --base и --source")
    base = git("rev-parse", "--verify", args.base + "^{commit}").decode().strip()
    source = git("rev-parse", "--verify", args.source + "^{commit}").decode().strip()
    subprocess.run(["git", "merge-base", "--is-ancestor", base, source], check=True)
    changed = git("diff", "--name-only", "--no-renames", "-z", base, source).decode().split("\0")
    plan = json.loads(Path("tasks/scene-detail/plan.json").read_text(encoding="utf-8"))
    writes = next(stage["writes"] for stage in plan["stages"] if stage["id"] == "core")
    companion_eol_equivalence = []
    for path in required_files():
        current = blob_at(source, path)
        if not current:
            raise ValueError("Required input отсутствует или отличается: " + path)
        physical = Path(path).read_bytes()
        if physical != current[2]:
            old = blob_at(base, path)
            # Untouched legacy companion закрепляется Git blob; сам файл не нормализуется.
            if (not path.endswith(".meta") or not old or old[:2] != current[:2]
                    or physical.replace(b"\r\n", b"\n") != current[2]
                    or guid_from(physical) != guid_from(current[2])):
                raise ValueError("Required input отсутствует или отличается: " + path)
            companion_eol_equivalence.append({"path": path, "source_blob": current[1],
                                              "physical_sha256": hashlib.sha256(physical).hexdigest()})
    entries, changed_metas = [], set()
    for path in sorted(p for p in changed if p and belongs(p)):
        if not any(path == scope or (scope.endswith("/**") and path.startswith(scope[:-2])) for scope in writes):
            raise ValueError("Путь вне declared core scope: " + path)
        current = blob_at(source, path)
        if current is None:
            raise ValueError("Удаление в provider input запрещено: " + path)
        old = blob_at(base, path)
        mode, oid, raw = current
        # Authoring-экспорт относится к фактическим bytes основного checkout.
        if Path(path).read_bytes() != raw:
            raise ValueError("Snapshot отличается от проверенного checkout: " + path)
        entries.append({
            "path": path, "operation": "M" if old else "A", "mode": mode,
            "base_blob": old[1] if old else None, "source_sha": source,
            "source_blob": oid, "sha256": hashlib.sha256(raw).hexdigest(),
        })
        if path.endswith(".meta") and path.startswith(("Assets/Art/Lobby/", "Assets/Materials/Lobby/")):
            changed_metas.add(path)
    textures = read_textures(args.textures)
    protected = [t for t in textures["entries"] if t["protectedTexture"]]
    if textures["textureCount"] != 252 or len(protected) != 112 or len(changed_metas) != 140:
        raise ValueError("Snapshot не соответствует проверенному составу 252/112/140")
    protected_guids, changed_guids, protected_evidence = set(), set(), []
    for item in protected:
        path = item["path"] + ".meta"
        old, current = blob_at(base, path), blob_at(source, path)
        if not old or not current or old[1] != current[1] or path in changed_metas or Path(path).read_bytes() != current[2]:
            raise ValueError("Изменена защищённая текстура: " + path)
        guid = guid_from(current[2])
        if guid != item["guid"]:
            raise ValueError("Source GUID отличается от export: " + path)
        protected_guids.add(guid)
        protected_evidence.append({"guid": guid, "path": item["path"], "base_blob": old[1], "source_blob": current[1]})
    exported_guids = {item["path"] + ".meta": item["guid"] for item in textures["entries"]}
    for path in changed_metas:
        guid = guid_from(blob_at(source, path)[2])
        if exported_guids.get(path) != guid:
            raise ValueError("Changed source GUID отличается от export: " + path)
        changed_guids.add(guid)
    overlap = sorted(protected_guids & changed_guids)
    if overlap or len(protected_guids) != 112 or len(changed_guids) != 140:
        raise ValueError("Защищённые и изменённые GUID пересекаются: " + ",".join(overlap))
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    manifest = {"version": 1, "grant_id": args.grant_id, "revision": args.revision,
                "base_sha": base, "source_sha": source, "entries": entries}
    out.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    evidence = {"passed": True, "scope": "Exact Git snapshot and texture GUID disjointness; no runtime acceptance",
                "base_sha": base, "source_sha": source, "protected_count": len(protected_guids),
                "changed_count": len(changed_guids), "intersection": overlap,
                "unchanged_companion_eol_equivalence": companion_eol_equivalence,
                "protected": protected_evidence, "changed_guids": sorted(changed_guids)}
    evidence_path = out.with_name("protected-texture-proof.json")
    evidence_path.write_text(json.dumps(evidence, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps({"entries": len(entries), "manifest": str(out),
                      "manifest_sha256": hashlib.sha256(out.read_bytes()).hexdigest(),
                      "protected_count": len(protected_guids), "changed_count": len(changed_guids),
                      "disjoint": True, "proof": str(evidence_path)}, ensure_ascii=False))


if __name__ == "__main__":
    main()
