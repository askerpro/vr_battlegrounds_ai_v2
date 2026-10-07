"""Свежая установка MCP-патчей из закреплённого offline ZIP в собственную копию."""
from pathlib import Path
from contextlib import contextmanager
import shutil
import subprocess
import os
import stat
import sys
import tempfile
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
        def remove_readonly(function, path, error):
            checked = Path(path[4:] if path.startswith("\\\\?\\") else path).resolve()
            if not checked.is_relative_to(project):
                raise ValueError("Cleanup вышел за пределы своего fixture") from error
            os.chmod(path, stat.S_IREAD | stat.S_IWRITE)
            function(path)
        shutil.rmtree("\\\\?\\" + str(project) if os.name == "nt" else str(project), onexc=remove_readonly)

with temporary_project() as temporary:
    repository = Path(temporary).resolve()
    assert repository.parent == (root / "tmp").resolve()
    subprocess.run(["git", "--no-pager", "init", "--quiet"], cwd=repository, check=True, capture_output=True)
    subprocess.run(["git", "--no-pager", "-c", "user.name=fixture", "-c", "user.email=fixture@example.invalid",
                    "-c", "core.hooksPath=", "commit", "--allow-empty", "-qm", "fixture"],
                   cwd=repository, check=True, capture_output=True)
    project = repository / "worker"
    subprocess.run(["git", "--no-pager", "worktree", "add", "--detach", str(project), "HEAD"],
                   cwd=repository, check=True, capture_output=True)
    tooling = project / "Tools/UnityMcp"
    tooling.mkdir(parents=True)
    for name in ("Install-Upstream.ps1", "discovery-10.2.0.patch", "output-guard-10.2.0.patch", "codex-config-10.2.0.patch"):
        shutil.copy2(root / "Tools/UnityMcp" / name, tooling / name)
    shutil.copytree(root / "Tools/UnityMcp/OutputGuard", tooling / "OutputGuard")
    (project / "Tools/agents").mkdir(parents=True, exist_ok=True)
    # Брокер развёрнут из agent-infra в общий runtime; стенду — заглушки и копия runtime.
    for name in ("broker_runtime.py", "editor-broker.py"):
        shutil.copy2(root / "Tools/agents" / name, project / "Tools/agents" / name)
    sys.path.insert(0, str(root / "Tools/agents"))
    import broker_runtime
    shutil.copytree(broker_runtime.require(), repository / ".agent-state/editor-broker/runtime",
                    ignore=shutil.ignore_patterns("__pycache__"))
    (project / "tmp").mkdir()
    # Только собственный fixture в дочернем процессе; политика Windows не изменяется.
    command = [shell, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(tooling / "Install-Upstream.ps1"),
               "-OfflineEditor", "-PythonExecutable", sys.executable, "-ArchivePath", str(archive)]
    for extra in (["-LockOwner", "fixture"], ["-Ticket", "1"]):
        denied = subprocess.run(command + extra, cwd=project, env=environment, capture_output=True, timeout=30)
        assert denied.returncode != 0, "legacy/incomplete capability accepted"
        assert not (project / "Packages/com.coplaydev.unity-mcp").exists()
    shutil.copytree(project / "Tools", repository / "Tools")
    main_command = [shell, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                    str(repository / "Tools/UnityMcp/Install-Upstream.ps1"), "-OfflineEditor",
                    "-PythonExecutable", sys.executable, "-ArchivePath", str(archive)]
    denied_main = subprocess.run(main_command, cwd=repository, env=environment, capture_output=True, timeout=30)
    assert denied_main.returncode != 0 and b"main checkout" in denied_main.stderr
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
    installed_codex = (package / "Editor/Helpers/CodexConfigHelper.cs").read_text(encoding="utf-8")
    assert "EnsureRmcpClientFeature" not in installed_codex
    assert 'features.Delete("rmcp_client")' in installed_codex
    print("PASS fresh offline installation: pinned hashes, all patches, helper/meta, overwrite refusal, scoped ZIP extraction")
