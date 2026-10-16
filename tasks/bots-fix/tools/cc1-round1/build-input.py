"""Технический Unity input без borrowed SDK, отдельным index/ref. HEAD/index сохраняются; LF-нормализация явная."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess
import tempfile
import time

ROOT = Path(__file__).resolve().parents[4]
SCOPES = [
    "Assets/Scripts/Bots", "Assets/Scripts/Debug/BotCombatStand",
    "Assets/Resources/Bots", "Assets/Resources/Bots.meta",
    "Assets/Editor/VR_Battlegrounds/Bots/BotBlazeProxySetup.cs",
    "Assets/Editor/VR_Battlegrounds/Bots/BotBlazeProxySetup.cs.meta",
    "Assets/Editor/VR_Battlegrounds/Bots/BotCombatSetup.cs",
    "Assets/Editor/VR_Battlegrounds/Bots/BotCombatSetup.cs.meta",
    "Assets/Editor/VR_Battlegrounds/Bots/BotStandHandMotionRecorder.cs",
    "Assets/Editor/VR_Battlegrounds/Bots/BotStandHandMotionRecorder.cs.meta",
    "Assets/Editor/VR_Battlegrounds/Bots/BotStandIdleMotionProbe.cs",
    "Assets/Editor/VR_Battlegrounds/Bots/BotStandIdleMotionProbe.cs.meta",
    "Assets/Editor/VR_Battlegrounds/Bots/BotStandRecorder.cs",
    "Assets/Tests/EditMode/Bots/BotAuthorityTests.cs",
    "tasks/bots-fix/tools/cc1-round1", "tasks/bots-fix/tools/cc1-offline",
    "tasks/bots-fix/tools/cc2-director",
    "tasks/bots-fix/tools/cc1-visual",
]


def git(*args, data=None, index=None):
    env = dict(os.environ, GIT_OPTIONAL_LOCKS="0", PYTHONIOENCODING="utf-8")
    if index is not None:
        env["GIT_INDEX_FILE"] = str(index)
    return subprocess.check_output(["git", "--no-pager", *args], cwd=ROOT, env=env, input=data)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--base", required=True, help="Точная accepted база брокера; проверяется ancestry origin/dev.")
    parser.add_argument("--label", required=True)
    parser.add_argument("--output", required=True, help="Новый JSON в tasks/bots-fix/reports; существующий не перезаписывается.")
    parser.add_argument("--build", action="store_true")
    parser.add_argument("--normalize-owned", action="store_true", help="Только CRLF→LF собственных text paths, с byte backup; Unity YAML не меняется.")
    args = parser.parse_args()
    if not re.fullmatch(r"[a-z0-9][a-z0-9-]{0,63}", args.label):
        parser.error("label должен содержать только lowercase/digits/hyphen")
    output = (ROOT / args.output).resolve()
    reports = (ROOT / "tasks/bots-fix/reports").resolve()
    if not output.is_relative_to(reports) or output.exists():
        parser.error("output должен быть новым файлом внутри собственных reports")
    base = git("rev-parse", "--verify", args.base + "^{commit}").decode().strip()
    git("merge-base", "--is-ancestor", base, "origin/dev")
    head = git("rev-parse", "HEAD").decode().strip()
    git("merge-base", "--is-ancestor", "origin/dev", head)
    merge_base = git("merge-base", base, head).decode().strip()
    paths = set(git("diff", "--no-renames", "--name-only", "-z", merge_base, head, "--", *SCOPES).split(b"\0"))
    paths.update(git("diff", "--cached", "--no-renames", "--name-only", "-z", head, "--", *SCOPES).split(b"\0"))
    paths.update(git("ls-files", "-m", "-o", "--exclude-standard", "-z", "--", *SCOPES).split(b"\0"))
    paths.discard(b"")
    paths.discard(b"tasks/bots-fix/tools/cc1-round1/b7-tests-direct.cs")
    rows, inventory, normalized = [], [], []
    backup_root = output.parent / ("lf-backup-" + str(time.time_ns()))
    for encoded in sorted(paths):
        name = encoded.decode("utf-8")
        physical = ROOT / name
        old = git("ls-tree", base, "--", name).decode().strip().split()
        if not physical.exists():
            if old:
                rows.append(b"0 " + b"0" * 40 + b"\t" + encoded + b"\0")
                inventory.append({"path": name, "operation": "D"})
            continue
        raw = physical.read_bytes()
        raw.decode("utf-8")
        if b"\r" in raw:
            if not args.normalize_owned or physical.suffix not in {".cs", ".md", ".json", ".py", ".ps1"}:
                raise RuntimeError("Owned physical path должен быть LF: " + name)
            replacement = raw.replace(b"\r\n", b"\n")
            if b"\r" in replacement:
                raise RuntimeError("Одиночный CR, нормализация остановлена: " + name)
            backup = backup_root / name
            backup.parent.mkdir(parents=True, exist_ok=True)
            with backup.open("xb") as stream:
                stream.write(raw)
            assert physical.read_bytes() == raw
            physical.write_bytes(replacement)
            normalized.append({"path": name, "backup": str(backup), "before_sha256": hashlib.sha256(raw).hexdigest()})
            raw = replacement
        oid = hashlib.sha1(b"blob " + str(len(raw)).encode() + b"\0" + raw).hexdigest()
        if old and old[2] == oid:
            continue
        if args.build:
            assert git("hash-object", "-w", "--stdin", data=raw).decode().strip() == oid
        rows.append((old[0] if old else "100644").encode() + b" " + oid.encode() + b"\t" + encoded + b"\0")
        inventory.append({"path": name, "operation": "M" if old else "A", "blob": oid,
                          "sha256": hashlib.sha256(raw).hexdigest()})
    result = {"base": base, "source_head": head, "label": args.label, "borrowed_sdk": False,
              "accepted_gameplay": False, "count": len(inventory), "changed": inventory, "normalized": normalized}
    if args.build:
        real_index = Path(git("rev-parse", "--git-path", "index").decode().strip())
        if not real_index.is_absolute():
            real_index = ROOT / real_index
        before = hashlib.sha256(real_index.read_bytes()).hexdigest()
        with tempfile.TemporaryDirectory(prefix="bots-technical-input-") as temporary:
            index = Path(temporary) / "index"
            git("read-tree", base, index=index)
            git("update-index", "-z", "--index-info", data=b"".join(rows), index=index)
            tree = git("write-tree", index=index).decode().strip()
            commit = git("commit-tree", tree, "-p", base,
                         data=("checkpoint(bots): technical " + args.label + ", not gameplay acceptance\n").encode()).decode().strip()
            ref = "refs/heads/codex/tmp/bots-" + args.label + "-" + str(time.time_ns())
            git("update-ref", ref, commit, "0" * 40)
        assert hashlib.sha256(real_index.read_bytes()).hexdigest() == before
        assert git("rev-parse", "HEAD").decode().strip() == head
        result.update(commit=commit, tree=tree, ref=ref, real_index_unchanged=True)
    output.parent.mkdir(parents=True, exist_ok=True)
    with output.open("x", encoding="utf-8", newline="\n") as stream:
        json.dump(result, stream, ensure_ascii=False, indent=2)
        stream.write("\n")
    print(json.dumps({k: v for k, v in result.items() if k not in {"changed", "normalized"}}, ensure_ascii=False))


if __name__ == "__main__":
    main()
