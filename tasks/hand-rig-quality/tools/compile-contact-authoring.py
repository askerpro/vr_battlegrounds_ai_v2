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
    parser.add_argument("--sdk-editor-rsp", help="Сначала компилировать весь UltimateXR.Editor текущими исходниками")
    parser.add_argument("--review-editor-rsp", help="Отдельная Unity assembly boundary HandPoseReview (обязательна с SDK)")
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
    sources += sorted((avatar / "HandPoseReview").glob("*.cs"))
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
    sdk_result = None
    review_result = None
    if args.sdk_editor_rsp:
        if not args.review_editor_rsp:
            parser.error("С SDK требуется --review-editor-rsp: объединённая компиляция скрывает forbidden assembly references")
        sdk_rsp = Path(args.sdk_editor_rsp).resolve()
        worker = next((parent.parent for parent in sdk_rsp.parents if parent.name == "Library"), None)
        if worker is None:
            parser.error("SDK response file должен находиться в worker/Library")
        lines = sdk_rsp.read_text(encoding="utf-8-sig").splitlines()
        sdk_sources = sorted((root / "Assets/ThirdParty/UltimateXR/Editor").rglob("*.cs"))
        sdk_hashes = {path.relative_to(root).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
                      for path in sdk_sources}
        sdk_references = []
        for line in lines:
            if not line.startswith("-r:"):
                continue
            path = Path(line[3:].strip('"'))
            if not path.is_absolute():
                path = worker / path
            sdk_references.append('-r:"' + str(path) + '"')
        sdk_dll = output / "UltimateXR.Editor.dll"
        sdk_response = output / "sdk-compile.rsp"
        sdk_switches = ["-target:library", "-nologo", "-nostdlib+", "-langversion:9.0", "-unsafe",
                        '-out:"' + str(sdk_dll) + '"']
        sdk_response.write_text("\n".join(sdk_switches + [line for line in lines if line.startswith("-define:")]
                                         + sdk_references + ['"' + str(p) + '"' for p in sdk_sources]) + "\n",
                                encoding="utf-8", newline="\n")
        compiled_sdk = subprocess.run(["dotnet", args.csc, "@" + str(sdk_response)], cwd=root,
                                      capture_output=True, text=True, encoding="utf-8", errors="replace")
        sdk_log = compiled_sdk.stdout + compiled_sdk.stderr
        (output / "sdk-compile.log").write_text(sdk_log, encoding="utf-8", newline="\n")
        sdk_unchanged = all(hashlib.sha256((root / path).read_bytes()).hexdigest() == value
                            for path, value in sdk_hashes.items())
        sdk_result = {"exitCode": compiled_sdk.returncode, "sourceCount": len(sdk_sources),
                      "sourcesUnchangedDuringCompile": sdk_unchanged,
                      "errorCount": sdk_log.count(": error "), "warningCount": sdk_log.count(": warning "),
                      "sourceSha256": sdk_hashes}
        (output / "sdk-compile-result.json").write_text(json.dumps(sdk_result, ensure_ascii=False, indent=2) + "\n",
                                                       encoding="utf-8", newline="\n")
        if compiled_sdk.returncode or not sdk_unchanged:
            print(json.dumps({"sdk": {k: v for k, v in sdk_result.items() if k != "sourceSha256"}}, ensure_ascii=False))
            print(sdk_log[:10000])
            return 1
        # Новый SDK API проверяется по только что собранной сборке, не по старой worker DLL.
        references = [line for line in references
                      if Path(line[3:].strip('"')).name not in ("UltimateXR.Editor.dll", "UltimateXR.Editor.ref.dll")]
        references.append('-r:"' + str(sdk_dll) + '"')
        review_lines = Path(args.review_editor_rsp).read_text(encoding="utf-8-sig").splitlines()
        review_sources = sorted((avatar / "HandPoseReview").glob("*.cs"))
        review_hashes = {path.relative_to(root).as_posix(): hashlib.sha256(path.read_bytes()).hexdigest()
                         for path in review_sources}
        review_refs = []
        for line in review_lines:
            if not line.startswith("-r:"):
                continue
            path = Path(line[3:].strip('"'))
            if not path.is_absolute():
                path = worker / path
            if path.name in ("UltimateXR.Editor.dll", "UltimateXR.Editor.ref.dll"):
                path = sdk_dll
            review_refs.append('-r:"' + str(path) + '"')
        review_dll = output / "VrBattlegrounds.HandPoseReview.Editor.dll"
        review_response = output / "review-compile.rsp"
        review_response.write_text("\n".join(
            ["-target:library", "-nologo", "-nostdlib+", "-langversion:9.0", "-unsafe", '-out:"' + str(review_dll) + '"']
            + [line for line in review_lines if line.startswith("-define:")]
            + review_refs + ['"' + str(p) + '"' for p in review_sources]) + "\n", encoding="utf-8", newline="\n")
        compiled_review = subprocess.run(["dotnet", args.csc, "@" + str(review_response)], cwd=root,
                                         capture_output=True, text=True, encoding="utf-8", errors="replace")
        review_log = compiled_review.stdout + compiled_review.stderr
        (output / "review-compile.log").write_text(review_log, encoding="utf-8", newline="\n")
        review_unchanged = all(hashlib.sha256((root / path).read_bytes()).hexdigest() == value
                               for path, value in review_hashes.items())
        review_result = {"exitCode": compiled_review.returncode, "sourceCount": len(review_sources),
                         "sourcesUnchangedDuringCompile": review_unchanged, "sourceSha256": review_hashes,
                         "errorCount": review_log.count(": error "), "warningCount": review_log.count(": warning ")}
        (output / "review-compile-result.json").write_text(json.dumps(review_result, ensure_ascii=False, indent=2) + "\n",
                                                          encoding="utf-8", newline="\n")
        if compiled_review.returncode or not review_unchanged:
            print(json.dumps({"review": {k: v for k, v in review_result.items() if k != "sourceSha256"}}, ensure_ascii=False))
            print(review_log[:10000])
            return 1
        sources = [path for path in sources if path not in review_sources]
        references = [line for line in references if Path(line[3:].strip('"')).name not in
                      ("VrBattlegrounds.HandPoseReview.Editor.dll", "VrBattlegrounds.HandPoseReview.Editor.ref.dll")]
        references.append('-r:"' + str(review_dll) + '"')
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
        "sdk": None if sdk_result is None else {k: v for k, v in sdk_result.items() if k != "sourceSha256"},
        "review": None if review_result is None else {k: v for k, v in review_result.items() if k != "sourceSha256"},
    }
    (output / "compile-result.json").write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n",
                                               encoding="utf-8", newline="\n")
    print(json.dumps({k: v for k, v in result.items() if k != "sourceSha256"}, ensure_ascii=False))
    if completed.returncode:
        print(log[:10000])
    return 0 if result["passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
