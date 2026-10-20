"""Normal A/B двух разных сборок на Quest: перед каждым блоком подставляет нужные Windows e2e-сервер и APK.

Runner принимает artifact только по пути из build provenance (`Build/e2e`, `Build/Quest` в build root), а сервер e2e
ещё и разрешён брандмауэром по этому пути. Поэтому сборки лежат рядом как `Build/e2e-<sha12>`, `Build/Quest-<sha12>`
и на время блока переименовываются в штатные папки. Порядок прогонов: `--run <метка>:<bundle>` по порядку;
метка → `--build <метка>=<sha12>`. Подряд идущие прогоны одной метки — один вызов run_quest_series.py.
"""
import argparse
import json
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent


def current_sha(folder: Path):
    manifest = folder / "build.json"
    return json.loads(manifest.read_text(encoding="utf-8"))["sourceSha"][:12] if manifest.is_file() else None


def switch(build_root: Path, name: str, sha12: str):
    active = build_root / name
    have = current_sha(active) if active.exists() else None
    if have == sha12:
        return
    if active.exists():
        parked = build_root / (name + "-" + str(have))
        if parked.exists():
            raise SystemExit("уже есть %s — не перезаписываю" % parked)
        active.rename(parked)
    wanted = build_root / (name + "-" + sha12)
    if not wanted.is_dir():
        raise SystemExit("нет сборки %s" % wanted)
    wanted.rename(active)
    if current_sha(active) != sha12:
        raise SystemExit("после подмены sha %s не совпал" % active)


def main():
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("--serial", required=True)
    p.add_argument("--win-build", required=True, help="папка Build Windows build root")
    p.add_argument("--quest-build", required=True, help="папка Build Android build root")
    p.add_argument("--build", action="append", required=True, help="метка=sha12")
    p.add_argument("--run", action="append", required=True, help="метка:папка RunSpec bundle, по порядку")
    p.add_argument("--progress-dir", required=True)
    p.add_argument("--pause-guardian", action="store_true")
    args = p.parse_args()
    builds = dict(item.split("=", 1) for item in args.build)
    progress = Path(args.progress_dir)
    progress.mkdir(parents=True, exist_ok=True)
    # Подряд идущие прогоны одной сборки — один блок: один вызов, шлем между ними не засыпает.
    blocks = []
    for item in args.run:
        label, bundle = item.split(":", 1)
        if blocks and blocks[-1][0] == label:
            blocks[-1][1].append(bundle)
        else:
            blocks.append((label, [bundle]))
    results = []
    for index, (label, bundles) in enumerate(blocks, 1):
        sha12 = builds[label]
        switch(Path(args.win_build), "e2e", sha12)
        switch(Path(args.quest_build), "Quest", sha12)
        block_progress = progress / ("%02d-%s.json" % (index, label))
        cmd = [sys.executable, "-B", "-X", "utf8", str(HERE / "run_quest_series.py"), "--serial", args.serial,
               "--progress", str(block_progress)]
        if args.pause_guardian:
            cmd.append("--pause-guardian")
        cmd += bundles
        print(json.dumps({"block": index, "label": label, "runs": len(bundles), "build": sha12}, ensure_ascii=False), flush=True)
        subprocess.call(cmd)
        rows = json.loads(block_progress.read_text(encoding="utf-8")) if block_progress.is_file() else []
        verdicts = [r.get("verdict") for r in rows]
        results.append({"block": index, "label": label, "bundles": bundles, "verdicts": verdicts})
        (progress / "ab-progress.json").write_text(json.dumps(results, ensure_ascii=False, indent=1) + "\n",
                                                   encoding="utf-8", newline="\n")
        if len(verdicts) != len(bundles) or any(v != "GREEN" for v in verdicts):
            print(json.dumps({"stopped": index, "verdicts": verdicts}, ensure_ascii=False), flush=True)
            return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
