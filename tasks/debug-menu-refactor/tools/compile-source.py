"""Проверка изменённых UI-исходников Roslyn; не Unity import и не тест игры."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import xml.etree.ElementTree as ET


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--reference-project", type=Path, required=True)
    parser.add_argument("--unity-data", type=Path, required=True)
    parser.add_argument("--dependency-source", help="Полный SHA опубликованного commit с тремя provider-исходниками SceneDetail")
    parser.add_argument("--snippet", type=Path, help="Тело CaptureProbe.Run из tasks/debug-menu-refactor/tools; только компиляция")
    args = parser.parse_args()
    if args.dependency_source and not re.fullmatch(r"[0-9a-fA-F]{40}", args.dependency_source):
        parser.error("--dependency-source должен быть полным SHA commit (40 hex)")
    workspace = Path(__file__).resolve().parents[3]
    snippet = None
    if args.snippet:
        snippet = (workspace / args.snippet).resolve()
        if not snippet.is_relative_to((workspace / "tasks/debug-menu-refactor/tools").resolve()) or not snippet.is_file():
            parser.error("--snippet должен быть существующим файлом внутри tasks/debug-menu-refactor/tools")
    output = workspace / "tasks/debug-menu-refactor/reports/managed-compile"
    output.mkdir(parents=True, exist_ok=True)
    project = ET.parse(args.reference_project / "Assembly-CSharp.csproj").getroot()
    elements = list(project.iter())
    tag = lambda node: node.tag.split("}")[-1]
    defines = next(node.text for node in elements if tag(node) == "DefineConstants")
    refs = set((args.reference_project / "Library/ScriptAssemblies").glob("*.dll"))
    for node in elements:
        if tag(node) != "Reference":
            continue
        hint = next((child.text for child in node if tag(child) == "HintPath"), None)
        if hint:
            path = Path(hint)
            if not path.is_absolute():
                path = args.reference_project / path
            if path.exists():
                refs.add(path.resolve())
    sources = [workspace / "Assets/Scripts/UI/Menu/MenuDebug.cs",
               workspace / "Assets/Scripts/UI/Menu/Kit/MenuKit.cs",
               workspace / "Assets/Scripts/UI/Menu/MenuPlayersTeams.cs",
               workspace / "Assets/Scripts/UI/Menu/MenuPerfTests.cs"]
    dependency_sources = []
    if args.dependency_source:
        dependency_sha = args.dependency_source.lower()
        # Читаем только опубликованные blobs; Assets и Git остаются неизменными.
        resolved = subprocess.run(
            ["git", "-C", str(workspace), "rev-parse", "--verify", dependency_sha + "^{commit}"],
            check=True, capture_output=True, text=True).stdout.strip()
        if resolved != dependency_sha:
            parser.error("--dependency-source не указывает непосредственно на commit")
        for name in ("SceneDetailProfile", "SceneDetailApplier", "SceneDetailDebugSection"):
            source_path = "Assets/Scripts/Maps/Detail/" + name + ".cs"
            revision_path = dependency_sha + ":" + source_path
            content = subprocess.run(
                ["git", "-C", str(workspace), "show", revision_path],
                check=True, capture_output=True).stdout
            blob = subprocess.run(
                ["git", "-C", str(workspace), "rev-parse", revision_path],
                check=True, capture_output=True, text=True).stdout.strip()
            extracted = output / "dependency-source" / dependency_sha / source_path
            extracted.parent.mkdir(parents=True, exist_ok=True)
            extracted.write_bytes(content)
            sources.append(extracted)
            dependency_sources.append({"source_path": source_path, "blob_sha": blob,
                                       "sha256": hashlib.sha256(content).hexdigest(),
                                       "extracted_path": str(extracted)})
    if snippet:
        wrapper = output / "CaptureProbe.cs"
        wrapper.write_text(
            "public static class TechnicalMenuCaptureProbe\n{\npublic static object Run()\n{\n" +
            snippet.read_text(encoding="utf-8-sig") + "\n}\n}\n",
            encoding="utf-8", newline="\n")
        sources.append(wrapper)
    # Дубликаты типов ожидаемы: reference содержит прежние классы, компилируются новые исходники.
    lines = ["/nologo", "/target:library", "/langversion:9", "/nostdlib+", "/unsafe+",
             "/nowarn:0436", "/define:" + defines, '/out:"' + str(output / "UiSourceCheck.dll") + '"']
    lines += ['/reference:"' + str(path) + '"' for path in sorted(refs)]
    lines += ['"' + str(path) + '"' for path in sources]
    response = output / "compile.rsp"
    response.write_text("\n".join(lines) + "\n", encoding="utf-8", newline="\n")
    command = [str(args.unity_data / "NetCoreRuntime/dotnet.exe"),
               str(args.unity_data / "DotNetSdkRoslyn/csc.dll"), "@" + str(response)]
    result = subprocess.run(command, cwd=workspace, capture_output=True, text=True, encoding="utf-8", errors="replace")
    log = output / "compiler.log"
    log.write_text(result.stdout + result.stderr, encoding="utf-8", newline="\n")
    digest = lambda path: hashlib.sha256(path.read_bytes()).hexdigest()
    assembly = args.reference_project / "Library/ScriptAssemblies/Assembly-CSharp.dll"
    report = {"passed": result.returncode == 0, "exit_code": result.returncode,
              "scope": "Roslyn C#9, четыре UI-исходника" +
                       (" и три опубликованных SceneDetail provider-исходника" if dependency_sources else "") +
                       (" и capture snippet (без исполнения)" if snippet else "") +
                       " с локальными reference DLL; без Unity/Mirror weaving/Quest",
              "dependency_source": args.dependency_source.lower() if args.dependency_source else None,
              "dependency_sources": dependency_sources,
              "snippet_path": str(snippet.relative_to(workspace)) if snippet else None,
              "snippet_sha256": digest(snippet) if snippet else None,
              "reference_assembly_sha256": digest(assembly), "reference_count": len(refs),
              "source_sha256": {str(path.relative_to(workspace)): digest(path) for path in sources},
              "errors": [line for line in (result.stdout + result.stderr).splitlines() if "error CS" in line],
              "report_path": str(output / "report.json"), "log_path": str(log)}
    Path(report["report_path"]).write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    print(json.dumps(report, ensure_ascii=False))
    return result.returncode


if __name__ == "__main__":
    raise SystemExit(main())
