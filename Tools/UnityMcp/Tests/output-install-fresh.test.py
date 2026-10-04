"""Свежая установка обоих MCP-патчей из закреплённого offline ZIP в собственную копию."""
from pathlib import Path
from contextlib import contextmanager
import shutil
import subprocess
import os
import sys
import tempfile
import time
import zipfile

root = Path(__file__).resolve().parents[3]
if len(sys.argv) < 2:
    raise SystemExit("Usage: output-install-fresh.test.py <upstream-10.2.0.zip> [powershell-path]")
archive = Path(sys.argv[1]).resolve()
shell = sys.argv[2] if len(sys.argv) > 2 else "powershell.exe"
# PowerShell 5.1 должен найти собственные модули, а не унаследованный PSModulePath PowerShell 7.
environment = {key: value for key, value in os.environ.items() if key.casefold() != "psmodulepath"}
@contextmanager
def temporary_project():
    project = Path(tempfile.mkdtemp(prefix="mcp-fresh-", dir=root / "tmp")).resolve()
    assert project.parent == (root / "tmp").resolve()
    try:
        yield str(project)
    finally:
        # ZIP содержит тестовые пути >MAX_PATH; удаляем только собственный проверенный tmp.
        shutil.rmtree("\\\\?\\" + str(project))

with temporary_project() as temporary:
    project = Path(temporary).resolve()
    assert project.parent == (root / "tmp").resolve()
    tooling = project / "Tools/UnityMcp"
    tooling.mkdir(parents=True)
    for name in ("Install-Upstream.ps1", "discovery-10.2.0.patch", "output-guard-10.2.0.patch"):
        shutil.copy2(root / "Tools/UnityMcp" / name, tooling / name)
    shutil.copytree(root / "Tools/UnityMcp/OutputGuard", tooling / "OutputGuard")
    subprocess.run(["git", "--no-pager", "init", "--quiet"], cwd=project, check=True, capture_output=True)
    lock = project / "tmp/unity-lock"
    lock.mkdir(parents=True)
    (lock / "info").write_text(f"owner=fixture\nuntil={int(time.time()) + 300}\n", encoding="utf-8")
    # Только собственный fixture в дочернем процессе; политика Windows не изменяется.
    command = [shell, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(tooling / "Install-Upstream.ps1"),
               "-LockOwner", "fixture", "-ArchivePath", str(archive)]
    unsafe_archive = project / "tmp/unsafe-fixture.zip"
    shutil.copy2(archive, unsafe_archive)
    with zipfile.ZipFile(unsafe_archive, "a") as fixture_zip:
        upstream_root = next(name.split("/", 1)[0] for name in fixture_zip.namelist() if "/MCPForUnity/" in name)
        fixture_zip.writestr(f"{upstream_root}/MCPForUnity/../../escaped-fixture.txt", "must not extract")
    unsafe_command = command[:-1] + [str(unsafe_archive)]
    refused_unsafe = subprocess.run(unsafe_command, cwd=project, env=environment, capture_output=True, timeout=60)
    assert refused_unsafe.returncode != 0, "ZIP traversal accepted"
    assert not list(project.rglob("escaped-fixture.txt")), "ZIP entry escaped package"
    assert not (project / "Packages/com.coplaydev.unity-mcp").exists()
    installed = subprocess.run(command, cwd=project, env=environment, capture_output=True, timeout=60)
    if installed.returncode:
        raise AssertionError(installed.stderr.decode("utf-8", errors="replace")[:3000])
    package = project / "Packages/com.coplaydev.unity-mcp"
    execute = package / "Editor/Tools/ExecuteCode.cs"
    assert "ExecuteCodeOutputGuard.Limit" in execute.read_text(encoding="utf-8-sig")
    assert "TypeCache" in (package / "Editor/Services/ToolDiscoveryService.cs").read_text(encoding="utf-8-sig")
    helper = package / "Editor/Helpers/ExecuteCodeOutputGuard.cs"
    assert helper.read_bytes() == (tooling / "OutputGuard/ExecuteCodeOutputGuard.cs").read_bytes()
    assert (helper.parent / (helper.name + ".meta")).read_bytes() == (tooling / "OutputGuard/ExecuteCodeOutputGuard.cs.meta.txt").read_bytes()
    expected = execute.read_bytes()
    refused = subprocess.run(command, cwd=project, env=environment, capture_output=True, timeout=15)
    assert refused.returncode != 0, "existing embedded package was overwritten"
    assert execute.read_bytes() == expected
    for staged_root in (project / "tmp/UnityMcpInstall").glob("*/source/*"):
        assert {child.name for child in staged_root.iterdir()} == {"MCPForUnity"}, "unneeded upstream extracted"
    print("PASS fresh offline installation: pinned hashes, both patches, helper/meta, overwrite refusal, scoped ZIP extraction")
