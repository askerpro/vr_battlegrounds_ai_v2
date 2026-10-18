#!/usr/bin/env python3
"""Техническая упаковка H/C в Git-деревья без checkout и игрового допуска."""

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import sys
import tempfile
import uuid


# Техническая упаковка common36 из C* 5489395093f9f36c89b79df95e1b3c2de561583b.
# Source tree 1d0d59e88472c0aeb2d89b0f84451a32375c47ad; на момент упаковки accepted=false, компиляция/runtime ещё не выполнялись.
# Последующая приёмка source: 044888e1b247f3257771c52a97dcd2a21bd40673; merge ожидает подтверждения.
# Затем exact H/C скомпилированы: каждый 13 rows/errors0; runtime H800 без выстрелов, C801 incomplete.
# Это не N8/perf PASS; подробности и evidence — build-inputs.md. H′804 остаётся отдельной диагностикой.
# H 4d9f8d4e229788de43f6d9fe9817c761faa756f9; tree 834fe10e1cf69a4ac08c7dd9a3f0447924d992fa.
# C 1152421fcd958d15036c669b284b7e3db0e5f9ae; tree 7754fe7c6a4d0d1b212089af4cfde706aab0d2c6.
# Inventory tasks/bots-fix/reports/cc3-perf-inputs/common36-attempt-1.json;
# SHA256 c84e3033b2ca20db7abe70b198dcbaa37ebd8fa2f5045076b3642e5cca101c21.
# Эти ссылки дописаны после упаковки и не входят в tool/docs blobs исходного C* 54893950.
# Историческая техническая упаковка common34, не актуальный source common36:
# C* 53e3a85fd6c2f88d7593ffe2a9aa94bd5e9eb1a2; source tree ac321d34695ffe76c7ae269bae994670d4adec22.
# H 1a20c093aca8075b0ff772685510f46ccf119883; tree a6991b2ff5b38b00649ccd85afc7d9fd9d166dc3.
# C 1b8497e673cfc4397a6c03412067e436f9a7e0bd; tree 403351049f3c1ff5766fe543779420106ee39f49.
# Inventory: tasks/bots-fix/reports/cc3-perf-inputs/sdk88-mirror7-attempt-1.json.
# SHA256 8e7cadd4ed425d3b97a30c64d632d48a6bb8ccdd71ad71d6caffded970b13afc; отчёт только упаковки, не запись последующих компиляции/runtime.
BASE = "50c27ae71da47d43245cbd4eb1ba6747a2a7e44c"
SCOPE_SOURCE = "48b27cf2bb0c9eb2dc646cc8f058bdd9d0eaf5a0"
PROBE = "2fa11b2767f23d4c709f739deb629fd378307c97"
PROBE_PATH = "Assets/Scripts/Debug/BotCombatStand/BotStandMatchProbe.cs"
PROBE_HASHES = {
    PROBE_PATH: "5a0611c9aa311f9ae9b4eed7258041defac5bf43a7193cd2435b4df5a3e130cc",
    PROBE_PATH + ".meta": "d2ceec02d7db6a2309abba927c721a1e75591df9d1d90a8471884c99d5a261ca",
}
PROBE_GUID = "5d236333c33745b59741cdb31bebf141"
TOOLS = "tasks/bots-fix/tools/cc3-perf/"
EXCLUDED_TEST = "Assets/Tests/EditMode/Bots/BotAuthorityTests.cs"
CUTOVER_ROOTS = (
    "Assets/Scripts/Bots/", "Assets/Editor/VR_Battlegrounds/Bots/",
    "Assets/Resources/Bots/", "Assets/Scripts/Debug/BotCombatStand/",
)
FROZEN_COMMON = frozenset("""
Assets/Data/WeaponSystem/Profiles/MagazineOnlyReadiness.asset
Assets/Data/WeaponSystem/Profiles/MagazineOnlyReadiness.asset.meta
Assets/Editor/VR_Battlegrounds/Gameplay/WeaponSystemAuthoring.cs
Assets/Prefabs/Weapons/R08/R08.prefab
Assets/Prefabs/Weapons/Revolver/Revolver.prefab
Assets/Scripts/Debug/Bootstrap/DebugOrchestrator.cs
Assets/Scripts/GameModes/EliminationMode/EliminationMode.cs
Assets/Scripts/GameModes/RespawnMode.cs
Assets/Scripts/GameModes/TeamChangeRequests.cs
Assets/Scripts/Managers/PlayersManager.cs
Assets/Scripts/Network/GameNetworkManager.cs
Assets/Scripts/Player/PlayerSession.cs
Assets/Scripts/Weapons/Core/WeaponStateMachine.cs
Assets/Scripts/Weapons/WeaponReadinessProfile.cs
Assets/Scripts/Weapons/WeaponSystem/UxrReadinessLedgerPort.cs
Assets/Scripts/Weapons/WeaponSystem/WeaponSystem.cs
Assets/ThirdParty/UltimateXR/Runtime/Scripts/Avatar/Controllers/UxrStandardAvatarController.cs
Assets/ThirdParty/UltimateXR/Runtime/Scripts/Avatar/UxrAvatar.HandState.cs
Assets/ThirdParty/UltimateXR/Runtime/Scripts/Avatar/UxrAvatar.cs
Assets/ThirdParty/UltimateXR/Runtime/Scripts/Core/UxrManager.cs
Assets/ThirdParty/UltimateXR/Runtime/Scripts/Manipulation/UxrGrabPointInfo.cs
Assets/ThirdParty/UltimateXR/Runtime/Scripts/Mechanics/Weapons/UxrFirearmReadinessTypes.cs
Assets/ThirdParty/UltimateXR/Runtime/Scripts/Mechanics/Weapons/UxrFirearmWeapon.AmmoAdmission.cs
Assets/ThirdParty/UltimateXR/Runtime/Scripts/Mechanics/Weapons/UxrFirearmWeapon.Readiness.cs
Assets/ThirdParty/UltimateXR/Runtime/Scripts/Mechanics/Weapons/UxrFirearmWeapon.StateSave.cs
Assets/ThirdParty/UltimateXR/Runtime/Scripts/Mechanics/Weapons/UxrFirearmWeapon.cs
""".split())
ADDITIONAL_COMMON = frozenset({
    "Assets/Editor/VR_Battlegrounds/Testing/PlayModeStandParticipant.cs",
    "Assets/Editor/VR_Battlegrounds/Testing/StandProtocol.cs",
    "Assets/Scripts/Network/HeadlessPrecacheGuard.cs",
    "Assets/ThirdParty/Mirror/Core/NetworkIdentity.cs",
    "Assets/ThirdParty/Mirror/Core/NetworkManager.cs",
    "Assets/ThirdParty/UltimateXR/Runtime/Scripts/Core/Components/UxrComponent.cs",
    "Assets/ThirdParty/UltimateXR/Runtime/Scripts/Core/Unique/UxrUniqueIdImplementer_1.cs",
    "Assets/ThirdParty/UltimateXR/Runtime/Scripts/Manipulation/UxrGrabbableObject.cs",
    "Assets/ThirdParty/UltimateXR/Runtime/Scripts/Manipulation/UxrGrabbableObjectAnchor.cs",
    "Assets/ThirdParty/UltimateXR/Runtime/Scripts/Networking/Integrations/Net/Mirror/UxrMirrorAvatar.cs",
})
COMMON = FROZEN_COMMON | ADDITIONAL_COMMON
REQUIRED_TOOLS = frozenset(TOOLS + name for name in (
    "acceptance.md", "analyze.py", "b0-preflight.cs", "b3-runtime-launch.cs",
    "b4-runtime-watch.cs", "b5-runtime-status.cs", "b6-runtime-stop.cs",
    "common-probe-source-manifest.json", "compile-check.ps1", "package.json",
    "build-inputs.py", "build-inputs.md",
))
TEXT_SUFFIXES = {".cs", ".py", ".ps1", ".md", ".json", ".meta", ".prefab", ".asset", ".controller", ".asmdef"}


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def sha256(data):
    return hashlib.sha256(data).hexdigest()


class Git:
    def __init__(self, root):
        self.root = root
        self.environment = os.environ.copy()
        self.environment["GIT_OPTIONAL_LOCKS"] = "0"
        self.blobs = {}

    def run(self, *args, data=None, index=None, extra_env=None):
        env = self.environment.copy()
        if index is not None:
            env["GIT_INDEX_FILE"] = str(index)
        if extra_env:
            env.update(extra_env)
        result = subprocess.run(["git", "--no-pager", "-C", str(self.root), *args],
                                input=data, stdout=subprocess.PIPE, stderr=subprocess.PIPE, env=env)
        if result.returncode:
            raise RuntimeError("git " + " ".join(args) + ": " + result.stderr.decode("utf-8", "replace").strip())
        return result.stdout

    def line(self, *args, **kwargs):
        return self.run(*args, **kwargs).decode("utf-8").strip()

    def entries(self, revision, *paths):
        result = {}
        for row in self.run("ls-tree", "-r", "-z", revision, "--", *paths).split(b"\0"):
            if not row:
                continue
            header, raw_path = row.split(b"\t", 1)
            mode, kind, oid = header.decode("ascii").split()
            path = raw_path.decode("utf-8")
            require(kind == "blob" and mode in {"100644", "100755"}, "Неподдерживаемая запись: " + path)
            require(not any(c in path for c in "\r\n\t"), "Небезопасный путь: " + repr(path))
            result[path] = {"mode": mode, "blob": oid}
        return result

    def changes(self, before, after, *paths):
        fields = self.run("diff", "--name-status", "--no-renames", "-z", before, after, "--", *paths).split(b"\0")
        result = {}
        for i in range(0, len(fields) - 1, 2):
            operation, path = fields[i].decode("ascii"), fields[i + 1].decode("utf-8")
            require(operation in {"A", "M", "D"}, "Неподдерживаемая операция " + operation + ": " + path)
            result[path] = operation
        return result

    def blob(self, oid):
        if oid not in self.blobs:
            self.blobs[oid] = self.run("cat-file", "blob", oid)
        return self.blobs[oid]

    def overlay(self, index, source, paths):
        rows = []
        for path in sorted(paths):
            entry = source.get(path)
            mode, oid = (entry["mode"], entry["blob"]) if entry else ("0", "0" * 40)
            rows.append((mode + " " + oid + "\t" + path).encode("utf-8") + b"\0")
        self.run("update-index", "-z", "--index-info", index=index, data=b"".join(rows))


def state(git):
    index_path = Path(git.line("rev-parse", "--path-format=absolute", "--git-path", "index"))
    return {"head": git.line("rev-parse", "HEAD"),
            "headRef": git.line("rev-parse", "--symbolic-full-name", "HEAD"),
            "indexPath": str(index_path), "indexExists": index_path.exists(),
            "indexSha256": sha256(index_path.read_bytes()) if index_path.exists() else None}


def inventory(git, revision, entries, paths, deleted=()):
    rows = []
    for path in sorted(paths):
        entry = entries.get(path)
        row = {"path": path, "sourceRevision": revision, "present": entry is not None,
               "mode": entry["mode"] if entry else None, "blob": entry["blob"] if entry else None,
               "sha256": None, "bytes": None}
        if entry and path not in deleted:
            data = git.blob(entry["blob"])
            row.update(sha256=sha256(data), bytes=len(data))
        elif entry:
            row["contentRead"] = False
            row["reason"] = "Удаляемый blob: сохранены только mode и object ID."
        rows.append(row)
    return rows


def write_report(path, report):
    payload = (json.dumps(report, ensure_ascii=False, indent=2, sort_keys=True) + "\n").encode("utf-8")
    temporary = path.with_name(path.name + "." + uuid.uuid4().hex + ".tmp")
    try:
        temporary.write_bytes(payload)
        os.replace(temporary, path)
    finally:
        if temporary.exists():
            temporary.unlink()


def build(args, git, output, report):
    before = state(git)
    report["checkoutBefore"] = before
    base = git.line("rev-parse", "--verify", args.base + "^{commit}")
    candidate = git.line("rev-parse", "--verify", args.candidate + "^{commit}")
    probe = git.line("rev-parse", "--verify", args.probe + "^{commit}")
    accepted = git.line("rev-parse", "--verify", "origin/dev^{commit}")
    require(base == BASE, "Этот frozen пакет поддерживает только base " + BASE)
    require(probe == PROBE, "Проба должна быть закреплена на " + PROBE)
    git.run("merge-base", "--is-ancestor", base, accepted)
    git.run("merge-base", "--is-ancestor", base, candidate)
    frozen = git.changes(base, SCOPE_SOURCE, "Assets")
    cutover = {p for p in frozen if p.startswith(CUTOVER_ROOTS) and p not in PROBE_HASHES}
    require(len(FROZEN_COMMON) == 26 and len(cutover) == 48 and len(frozen) == 77,
            "Frozen scope не соответствует common26/cutover48/77.")
    require(set(frozen) == FROZEN_COMMON | cutover | set(PROBE_HASHES) | {EXCLUDED_TEST}, "Frozen scope изменён.")
    require(not ADDITIONAL_COMMON & set(frozen) and len(COMMON) == 36,
            "Additional common должен расширять legacy frozen77 ровно до common36.")
    frozen.update({path: "M" for path in ADDITIONAL_COMMON})
    operations = git.changes(base, candidate, "Assets")
    require(operations == frozen, "Assets diff C* должен сохранять ровно frozen87 путей и A/M/D: " +
            json.dumps({"missing": sorted(set(frozen) - set(operations)),
                        "extra": sorted(set(operations) - set(frozen)),
                        "changedOperations": sorted(p for p in set(frozen) & set(operations) if frozen[p] != operations[p])}))
    selected = set(operations)
    source = git.entries(candidate, *sorted(selected), TOOLS)
    probe_entries = git.entries(probe, *PROBE_HASHES)
    tools = {p for p in source if p.startswith(TOOLS)}
    base_entries = git.entries(base, *sorted(selected), TOOLS)
    require(ADDITIONAL_COMMON <= base_entries.keys() and ADDITIONAL_COMMON <= source.keys(),
            "Additional common M-зависимости должны существовать в B и C*.")
    all_tools = tools | {p for p in base_entries if p.startswith(TOOLS)}
    tool_operations = git.changes(base, candidate, TOOLS)
    require(REQUIRED_TOOLS <= tools, "C* не содержит полный cc3tools пакет: " + repr(sorted(REQUIRED_TOOLS - tools)))
    require(all(p in source for p in COMMON), "Common overlay содержит удаление вместо обязательной зависимости.")
    for path, expected in PROBE_HASHES.items():
        require(path in probe_entries, "Отсутствует pinned probe: " + path)
        require(sha256(git.blob(probe_entries[path]["blob"])) == expected, "Pinned probe SHA256 mismatch: " + path)
    meta = git.blob(probe_entries[PROBE_PATH + ".meta"]["blob"]).decode("utf-8")
    require(re.search(r"^guid: " + PROBE_GUID + r"$", meta, re.MULTILINE) is not None, "Pinned probe GUID mismatch.")
    deleted = {p for p, operation in operations.items() if operation == "D"}
    for path, operation in operations.items():
        require((path not in source) == (operation == "D"), "Операция не совпадает с tree: " + path)
    expected_common = {p: source[p] for p in COMMON | tools}
    expected_common.update(probe_entries)
    lf_rows = []
    for path, entry in sorted({**expected_common, **{p: source[p] for p in cutover if p in source}}.items()):
        if Path(path).suffix.lower() not in TEXT_SUFFIXES:
            continue
        data = git.blob(entry["blob"])
        require(b"\r" not in data, "CR в изменённом text blob: " + path)
        lf_rows.append({"path": path, "blob": entry["blob"], "containsCR": False})
    report.update(sources={"base": base, "candidate": candidate, "probe": probe, "originDev": accepted,
                           "scopeSource": SCOPE_SOURCE},
                  counts={"assetsDiff": 87, "common": 36, "cutover": 48, "probe": 2,
                          "excludedTests": 1, "tools": len(tools), "toolOperations": len(tool_operations)},
                  operations=[{"path": p, "operation": operations[p],
                               "group": "common" if p in COMMON else "cutover" if p in cutover else
                                        "pinned-probe-replaced" if p in PROBE_HASHES else "excluded-test"}
                              for p in sorted(operations)],
                  toolOperations=[{"path": p, "operation": tool_operations[p]} for p in sorted(tool_operations)],
                  inventory={"base": inventory(git, base, base_entries, selected | all_tools,
                                               deleted | (all_tools - tools)),
                             "candidate": inventory(git, candidate, source, selected | all_tools),
                             "pinnedProbe": inventory(git, probe, probe_entries, PROBE_HASHES)},
                  lfChecks=lf_rows)
    with tempfile.TemporaryDirectory(prefix="bots-cc3-index-") as directory:
        private_index = Path(directory) / "index"
        git.run("read-tree", base, index=private_index)
        git.overlay(private_index, expected_common, set(expected_common) | all_tools)
        h_tree = git.line("write-tree", index=private_index)
        git.overlay(private_index, source, cutover)
        c_tree = git.line("write-tree", index=private_index)
        actual_diff = git.changes(h_tree, c_tree)
        require(actual_diff == {p: operations[p] for p in cutover}, "H/C symmetric diff вышел за exact cutover48.")
        h_entries = git.entries(h_tree, *sorted(set(expected_common) | all_tools))
        c_entries = git.entries(c_tree, *sorted(set(expected_common) | all_tools))
        require(h_entries == expected_common == c_entries, "H/C common/probe/tools blob или mode mismatch.")
        allowed = set(expected_common) | all_tools
        require(set(git.changes(base, h_tree)) <= allowed, "H содержит пути вне common/probe/tools.")
        require(set(git.changes(base, c_tree)) <= allowed | cutover, "C содержит пути вне разрешённого пакета.")
        tests_base = git.line("rev-parse", base + ":Assets/Tests")
        require(git.line("rev-parse", h_tree + ":Assets/Tests") == tests_base ==
                git.line("rev-parse", c_tree + ":Assets/Tests"), "Tests subtree должен оставаться из B.")
        report["trees"] = {"hybrid": h_tree, "candidate": c_tree, "testsFromBase": tests_base,
                           "symmetricDiff": [{"path": p, "operation": actual_diff[p]} for p in sorted(actual_diff)]}
        report["inventory"]["hybrid"] = inventory(git, h_tree, git.entries(h_tree, *sorted(allowed | cutover)), allowed | cutover, deleted)
        report["inventory"]["comparisonCandidate"] = inventory(git, c_tree, git.entries(c_tree, *sorted(allowed | cutover)), allowed | cutover)
    require(state(git) == before, "HEAD/index изменились во время упаковки; refs не публикуются.")
    timestamp = git.line("show", "-s", "--format=%ct", candidate)
    identity = {"GIT_AUTHOR_NAME": "bots-fix technical packaging", "GIT_AUTHOR_EMAIL": "bots-fix@local.invalid",
                "GIT_COMMITTER_NAME": "bots-fix technical packaging", "GIT_COMMITTER_EMAIL": "bots-fix@local.invalid",
                "GIT_AUTHOR_DATE": "@" + timestamp + " +0000", "GIT_COMMITTER_DATE": "@" + timestamp + " +0000"}
    commits = {}
    for variant, tree in (("hybrid", h_tree), ("candidate", c_tree)):
        message = ("technical(bots-fix): cc3 " + variant + " snapshot\n\n" +
                   "base=" + base + "\ncandidate=" + candidate + "\nprobe=" + probe +
                   "\nOnly technical packaging; gameplay/user acceptance pending.\n")
        commits[variant] = git.line("commit-tree", tree, "-p", base, data=message.encode("utf-8"), extra_env=identity)
    report["commits"] = commits
    suffix = args.label + "-" + uuid.uuid4().hex
    refs = {kind: "refs/heads/codex/tmp/cc3-" + suffix + "-" + kind for kind in commits}
    require(state(git) == before, "HEAD/index изменились перед публикацией technical refs.")
    report["refs"] = refs
    report["status"] = "ready-to-create-refs"
    write_report(output, report)
    transaction = "start\n" + "".join("create " + refs[k] + " " + commits[k] + "\n" for k in sorted(commits)) + "prepare\ncommit\n"
    git.run("update-ref", "--stdin", data=transaction.encode("ascii"))
    report["refsPublication"] = "created"
    report["checkoutAfter"] = state(git)
    require(report["checkoutAfter"] == before, "HEAD/index изменились; technical refs сохранены в отчёте.")
    report["status"] = "technical-snapshots-created"
    report["checks"] = {"acceptedBaseAncestry": True, "candidateBaseAncestry": True,
                        "privateIndexOnly": True, "headAndIndexUnchanged": True,
                        "sameCommonProbeTools": True, "exactCutover48": True, "testsFromBase": True}


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", required=True, help="Принятая B; frozen пакет поддерживает " + BASE)
    parser.add_argument("--candidate", required=True, help="Итоговый common36 C* commit с SDK81/82/84/86/87/88, Mirror6/7, companion stand и cc3tools")
    parser.add_argument("--probe", required=True, help="Закреплённый собственный probe commit " + PROBE)
    parser.add_argument("--output", required=True, help="Новый ignored JSON внутри tasks/bots-fix/reports/")
    parser.add_argument("--label", required=True, help="Метка technical refs: a-z, 0-9, дефис; до 40 символов")
    parser.add_argument("--build", action="store_true", help="Явно разрешить object/tree/technical ref и report writes")
    args = parser.parse_args()
    if not args.build:
        parser.error("Создание snapshots требует явного --build; без него записи запрещены.")
    require(re.fullmatch(r"[a-z0-9][a-z0-9-]{0,39}", args.label) is not None, "Некорректная --label.")
    root = Path(__file__).resolve().parents[4]
    git = Git(root)
    require(Path(git.line("rev-parse", "--show-toplevel")).resolve() == root, "Неожиданный repository root.")
    output = Path(args.output)
    output = (output if output.is_absolute() else root / output).resolve()
    reports = (root / "tasks/bots-fix/reports").resolve()
    require(reports.is_relative_to(root), "reports/ не должен выходить из own checkout через symlink.")
    require(output.is_relative_to(reports) and output.suffix == ".json", "--output должен быть JSON внутри own ignored reports/.")
    git.run("check-ignore", "--no-index", "-q", "--", str(output))
    require(not output.exists(), "Отчёт уже существует; выберите новый --output.")
    report = {"schema": "bots-fix-cc3-technical-inputs@1", "label": args.label, "status": "building",
              "limitations": ["Только упаковка Git: не компиляция, Unity, runtime, Quest или пользовательская приёмка.",
                              "H и C содержат непринятые common dependencies; H не является чистой accepted baseline.",
                              "Нет grants, broker admission, Unity lease, accepted commit или push.",
                              "Assets, HEAD и реальный index не записываются; новые объекты и codex/tmp refs остаются для аудита.",
                              "Содержимое удалённых source blobs не читается; tree metadata сохранены.",
                              "Tree и commit детерминированы входными SHA; имена temporary refs уникальны."]}
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("x", encoding="utf-8", newline="\n") as handle:
        handle.write(json.dumps(report, ensure_ascii=False) + "\n")
    try:
        build(args, git, output, report)
    except Exception as error:
        report["status"] = "failed"
        report["error"] = str(error)
        write_report(output, report)
        raise
    write_report(output, report)
    print(json.dumps({"status": report["status"], "output": str(output),
                      "trees": report["trees"], "commits": report["commits"], "refs": report["refs"]}, ensure_ascii=False))


if __name__ == "__main__":
    try:
        main()
    except Exception as error:
        print("build-inputs: " + str(error), file=sys.stderr)
        sys.exit(1)
