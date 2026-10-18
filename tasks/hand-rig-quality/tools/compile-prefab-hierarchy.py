"""Offline-компиляция Editor assembly; Unity и проверки поведения не запускаются."""

import argparse
import hashlib
import json
from pathlib import Path
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-rsp", required=True)
    parser.add_argument("--reference-root", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--extra-source", action="append", default=[])
    parser.add_argument("--csc", default="C:/Program Files/dotnet/sdk/9.0.311/Roslyn/bincore/csc.dll")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[3]
    output = (root / args.output).resolve()
    reports = (root / "tasks/hand-rig-quality/reports").resolve()
    if reports not in output.parents:
        parser.error("Вывод разрешён только в отдельный каталог task/reports")
    metadata_root = Path(args.reference_root).resolve()
    lines = Path(args.source_rsp).read_text(encoding="utf-8-sig").splitlines()
    sources, references, options = [], [], []
    for line in lines:
        line = line.strip()
        if line.startswith(("-r:", "/reference:")):
            prefix, value = line.split(":", 1)
            path = Path(value.strip('"'))
            if not path.is_absolute():
                path = metadata_root / path
            references.append('-r:"' + str(path.resolve()) + '"')
        elif line.strip('"').endswith(".cs"):
            path = Path(line.strip('"'))
            if path.is_absolute():
                # Сохранённый task response file уже содержит пути этого checkout.
                try:
                    path = path.relative_to(root)
                except ValueError:
                    path = path.relative_to(metadata_root)
            sources.append(root / path)
        elif line.startswith(("-define:", "/define:", "-unsafe", "/unsafe", "-langversion:", "-nullable:", "-nowarn:", "/nowarn:")):
            options.append(line)
    sources += [root / path for path in args.extra_source]
    sources = list(dict.fromkeys(path.resolve() for path in sources))
    for path in sources + [Path(args.csc)]:
        if not path.is_file():
            parser.error("Не найден вход компиляции: " + str(path))
    if not sources or not references:
        parser.error("Response file не содержит исходников или references")
    before = {str(p.relative_to(root)): hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}
    output.mkdir(parents=True, exist_ok=True)
    rsp = output / "compile.rsp"
    rsp.write_text("\n".join(["-target:library", "-nologo", "-nostdlib+", "-utf8output",
        '-out:"' + str(output / "Assembly-CSharp-Editor.dll") + '"'] + options + references +
        ['"' + str(p) + '"' for p in sources]) + "\n", encoding="utf-8", newline="\n")
    completed = subprocess.run(["dotnet", args.csc, "@" + str(rsp)], cwd=root,
                               capture_output=True, text=True, encoding="utf-8", errors="replace")
    log = completed.stdout + completed.stderr
    (output / "compile.log").write_text(log, encoding="utf-8", newline="\n")
    result = {"exitCode": completed.returncode, "sourceCount": len(sources), "referenceCount": len(references),
              "errors": log.count(": error "), "warnings": log.count(": warning "),
              "sourceHashes": before, "metadataRoot": str(metadata_root),
              "sourcesUnchanged": all(hashlib.sha256((root / p).read_bytes()).hexdigest() == digest
                                      for p, digest in before.items())}
    (output / "compile-result.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n",
                                                encoding="utf-8", newline="\n")
    print(json.dumps({k: v for k, v in result.items() if k != "sourceHashes"}, ensure_ascii=False))
    if completed.returncode:
        print("\n".join(line for line in log.splitlines() if ": error " in line)[:8000])
    return completed.returncode or (0 if result["sourcesUnchanged"] else 1)


if __name__ == "__main__":
    raise SystemExit(main())
