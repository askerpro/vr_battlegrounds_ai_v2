"""Компиляция текущего editor-прототипа; не запускает Unity и не принимает хват."""

import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import subprocess


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", required=True, help="Каталог внутри task/reports")
    parser.add_argument("--references", required=True, help="Существующий compile.rsp с абсолютными -r:")
    parser.add_argument("--editor-rsp", required=True, help="Read-only Unity response file для defines")
    parser.add_argument("--snippet", action="append", default=[], help="Дополнительное тело execute_code; только компиляция")
    parser.add_argument("--csc", default="C:/Program Files/dotnet/sdk/9.0.311/Roslyn/bincore/csc.dll")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[3]
    reports = (root / "tasks/hand-rig-quality/reports").resolve()
    output = (root / args.output).resolve()
    if output == reports or reports not in output.parents:
        parser.error("Выход разрешён только в отдельный каталог task/reports")
    output.mkdir(parents=True, exist_ok=True)
    avatar = root / "Assets/Editor/VR_Battlegrounds/Avatars"
    gameplay = root / "Assets/Editor/VR_Battlegrounds/Gameplay"
    sources = sorted((avatar / "GripExtraction").glob("*.cs"))
    sources += sorted((avatar / "GripAuthoring").rglob("*.cs"))
    sources += [avatar / "HandPoseReview" / name for name in (
        "HandPosePotentialContactMask.cs", "HandPosePotentialContactCoverage.cs")]
    sources += [avatar / name for name in (
        "HandsPackPoseExtractor.cs", "HandsPackPoseImporter.cs",
        "HandsPackGripAligner.cs", "KinemationPoseExtractor.cs")]
    # Потребители GripSample включены исходниками: старый DTO в metadata не должен скрыть ошибку API.
    sources += [gameplay / name for name in (
        "KinemationWeapon.cs", "KinemationWeaponBuilder.cs", "IWeaponModel.cs",
        "WeaponModelPreviewScope.cs", "HandsPackWeaponBuilder.cs",
        "HandsPackWeaponImporter.cs", "WeaponInteractionRecipes.cs")]
    sources = list(dict.fromkeys(sources))
    for path in sources:
        if not path.is_file():
            parser.error("Исходник отсутствует: " + str(path))
    references = [line for line in Path(args.references).read_text(encoding="utf-8-sig").splitlines()
                  if line.startswith("-r:")]
    defines = [line for line in Path(args.editor_rsp).read_text(encoding="utf-8-sig").splitlines()
               if line.startswith("-define:")]
    if not references or not defines:
        parser.error("Нет reference/define inventory")
    hashes = {path.relative_to(root).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
              for path in sources}
    snippet_hashes = {}
    for index, name in enumerate(args.snippet):
        snippet = (root / name).resolve()
        if root not in snippet.parents:
            parser.error("Snippet должен быть внутри checkout")
        content = snippet.read_text(encoding="utf-8-sig")
        snippet_hashes[snippet.relative_to(root).as_posix()] = hashlib.sha256(snippet.read_bytes()).hexdigest()
        wrapper = output / ("Snippet" + str(index) + ".cs")
        wrapper.write_text("using System;\nusing System.Linq;\nusing UnityEngine;\nusing UnityEditor;\n"
                           + "public static class ContactAuthoringProbe" + str(index) + " { public static object Run() {\n"
                           + content + "\n} }\n", encoding="utf-8", newline="\n")
        sources.append(wrapper)
    switches = ["-target:library", "-nologo", "-nostdlib+", "-langversion:9.0", "-unsafe",
                "-nowarn:0436", '-out:"' + str(output / "ContactAuthoringCompileOnly.dll") + '"']
    response = output / "compile.rsp"
    response.write_text("\n".join(switches + defines + references + ['"' + str(p) + '"' for p in sources]) + "\n",
                        encoding="utf-8", newline="\n")
    completed = subprocess.run(["dotnet", args.csc, "@" + str(response)], cwd=root,
                               capture_output=True, text=True, encoding="utf-8", errors="replace")
    log = completed.stdout + completed.stderr
    (output / "compile.log").write_text(log, encoding="utf-8", newline="\n")
    unchanged = all(hashlib.sha256((root / path).read_bytes()).hexdigest() == value
                    for path, value in hashes.items())
    unchanged = unchanged and all(hashlib.sha256((root / path).read_bytes()).hexdigest() == value
                                  for path, value in snippet_hashes.items())
    result = {
        "utc": datetime.now(timezone.utc).isoformat(),
        "scope": "Offline whole contact-authoring source integration; Unity import, rendering, BakeMesh and human acceptance are not checked.",
        "exitCode": completed.returncode, "passed": completed.returncode == 0 and unchanged,
        "sourceCount": len(sources), "referenceCount": len(references),
        "sourcesUnchangedDuringCompile": unchanged,
        "errorCount": log.count(": error "), "warningCount": log.count(": warning "),
        "sourceSha256": hashes, "snippetSha256": snippet_hashes,
    }
    (output / "compile-result.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n",
                                               encoding="utf-8", newline="\n")
    print(json.dumps({k: v for k, v in result.items() if k != "sourceSha256"}, ensure_ascii=False))
    if completed.returncode:
        print(log[:10000])
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
