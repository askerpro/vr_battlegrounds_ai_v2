"""Guard, backup, повтор и чужой hash: только собственный offline fixture."""
from pathlib import Path
import os
import shutil
import subprocess
import sys
import tempfile
import zipfile
import hashlib
import json
import importlib.util

root = Path(__file__).resolve().parents[3]
if len(sys.argv) < 2:
    raise SystemExit("Usage: test-job-installer.test.py <upstream-10.2.0.zip> [powershell-path]")
archive = Path(sys.argv[1]).resolve()
shell = sys.argv[2] if len(sys.argv) > 2 else "powershell.exe"
environment = {key: value for key, value in os.environ.items() if key.casefold() != "psmodulepath"}
expected = "cbc4952435ab4ef0a710726038eb4d4edd254e30592916d9b4c3d92e1bb0eaca"

def normalized_hash(path):
    return hashlib.sha256(path.read_text(encoding="utf-8-sig").encode("utf-8")).hexdigest()

with tempfile.TemporaryDirectory(prefix="mcp-test-job-installer-", dir=root / "tmp") as temporary:
    repository = Path(temporary).resolve()
    assert repository.parent == (root / "tmp").resolve()
    subprocess.run(["git", "init", "--quiet"], cwd=repository, check=True, capture_output=True)
    subprocess.run(["git", "-c", "user.name=fixture", "-c", "user.email=fixture@example.invalid",
                    "-c", "core.hooksPath=", "commit", "--allow-empty", "-qm", "fixture"],
                   cwd=repository, check=True, capture_output=True)
    project = repository / "worker"
    subprocess.run(["git", "worktree", "add", "--detach", str(project), "HEAD"], cwd=repository, check=True, capture_output=True)
    tooling = project / "Tools/UnityMcp"
    tooling.mkdir(parents=True)
    for name in ("Apply-TestJobLifecycle.ps1", "test-job-lifecycle-10.2.0.patch", "test-job-lifecycle-mcp2-to-current.patch", "test-job-terminal-results-10.2.0.patch"):
        shutil.copy2(root / "Tools/UnityMcp" / name, tooling / name)
    (project / "Tools/agents").mkdir(parents=True)
    for name in ("broker_runtime.py", "editor-broker.py"):
        shutil.copy2(root / "Tools/agents" / name, project / "Tools/agents" / name)
    sys.path.insert(0, str(root / "Tools/agents"))
    import broker_runtime
    shutil.copytree(broker_runtime.require(), repository / ".agent-state/editor-broker/runtime",
                    ignore=shutil.ignore_patterns("__pycache__"))
    spec = importlib.util.spec_from_file_location("fixture_guard", Path(__file__).with_name("installer-fixture-guard.py"))
    fixture_guard = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(fixture_guard)
    inventory = fixture_guard.configure_fixture_inventory(repository / ".agent-state/editor-broker/runtime", project)
    package = project / "Packages/com.coplaydev.unity-mcp"
    target = package / "Editor/Services/TestJobManager.cs"
    target.parent.mkdir(parents=True)
    (package / "package.json").write_text('{"version":"10.2.0"}', encoding="utf-8")
    with zipfile.ZipFile(archive) as upstream:
        name = next(name for name in upstream.namelist() if name.endswith("/MCPForUnity/Editor/Services/TestJobManager.cs"))
        # Установка должна принимать CRLF, сохраняя сырые исходные байты в backup.
        original = upstream.read(name).replace(b"\r\n", b"\n").replace(b"\n", b"\r\n")
    target.write_bytes(original)
    service_target = target.with_name("TestRunnerService.cs")
    with zipfile.ZipFile(archive) as upstream:
        service_name = next(name for name in upstream.namelist() if name.endswith("/MCPForUnity/Editor/Services/TestRunnerService.cs"))
        service_original = upstream.read(service_name).replace(b"\r\n", b"\n")
    assert hashlib.sha256(service_original).hexdigest() == "b5f25bffbcee394d746f4a67b7cfaba1bf3e24279f581d79fec7c20098d6ee23"
    service_target.write_bytes(service_original)
    service_meta = service_target.with_name(service_target.name + ".meta")
    service_meta_bytes = b"fileFormatVersion: 2\nguid: fedcba0987654321fedcba0987654321\n"
    service_meta.write_bytes(service_meta_bytes)
    meta = target.with_name(target.name + ".meta")
    meta.write_bytes(b"fileFormatVersion: 2\nguid: 1234567890abcdef1234567890abcdef\n")
    meta_bytes = meta.read_bytes()
    command = [shell, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(tooling / "Apply-TestJobLifecycle.ps1"),
               "-OfflineEditor", "-PythonExecutable", sys.executable]
    def run(argv=command, cwd=project):
        return subprocess.run(argv, cwd=cwd, env=environment, capture_output=True, timeout=30)
    for extra in (["-LockOwner", "fixture"], ["-Ticket", "1"]):
        assert run(command + extra).returncode != 0, "legacy/incomplete capability РїСЂРёРЅСЏС‚Р°"
        assert target.read_bytes() == original
    shutil.copytree(project / "Tools", repository / "Tools")
    main_command = [shell, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
                    str(repository / "Tools/UnityMcp/Apply-TestJobLifecycle.ps1"), "-OfflineEditor",
                    "-PythonExecutable", sys.executable]
    denied = run(main_command, repository)
    assert denied.returncode != 0 and b"main checkout" in denied.stderr, (denied.stdout + denied.stderr).decode("utf-8", errors="replace")[:2500]
    for native_roots in ([str(project)], "unknown"):
        inventory.write_text(json.dumps(native_roots), encoding="utf-8")
        assert run().returncode != 0 and target.read_bytes() == original, "active/unknown inventory допущен"
    inventory.write_text("[]", encoding="utf-8")
    installed = run()
    assert installed.returncode == 0, installed.stderr.decode("utf-8", errors="replace")[:2500]
    assert normalized_hash(target) == expected
    assert normalized_hash(service_target) == "291801256c293ef07f51bf53668fec4a5a64e5cede7c89f755614d75f5b545f9"
    assert service_meta.read_bytes() == service_meta_bytes
    assert meta.read_bytes() == meta_bytes
    backups = list((project / "tmp/McpTestJobLifecycleInstall").glob("*/TestJobManager.cs.backup"))
    assert len(backups) == 1 and backups[0].read_bytes() == original, "backup исходных байтов потерян"
    installed_bytes = target.read_bytes()
    installed_service_bytes = service_target.read_bytes()
    assert run().returncode == 0 and target.read_bytes() == installed_bytes, "повтор не идемпотентен"
    # Upgrade принимает только точный прежний MCP2 и получает те же bytes, что fresh cumulative patch.
    previous_stage = project / "tmp/previous-mcp2/Editor/Services/TestJobManager.cs"
    previous_stage.parent.mkdir(parents=True)
    previous_stage.write_bytes(original.replace(b"\r\n", b"\n"))
    previous_relative = (project / "tmp/previous-mcp2").relative_to(project).as_posix()
    subprocess.run(["git", "-c", "core.autocrlf=false", "apply", "--ignore-space-change",
                    "--directory=" + previous_relative, str(root / "Tools/UnityMcp/Tests/Fixtures/test-job-lifecycle-mcp2.patch")],
                   cwd=project, check=True, capture_output=True)
    previous_bytes = previous_stage.read_bytes().replace(b"\r\n", b"\n")
    assert hashlib.sha256(previous_bytes).hexdigest() == "395272a8507666a4d88d3b72e69023a03f25499712f1cb895877dfb2ccff4a01"
    target.write_bytes(previous_bytes)
    service_target.write_bytes(service_original)
    upgraded = run()
    assert upgraded.returncode == 0, upgraded.stderr.decode("utf-8", errors="replace")[:2500]
    assert target.read_bytes() == installed_bytes, "upgrade и cumulative fresh дают разные bytes"
    assert meta.read_bytes() == meta_bytes
    backups = list((project / "tmp/McpTestJobLifecycleInstall").glob("*/TestJobManager.cs.backup"))
    assert len(backups) == 2 and any(p.read_bytes() == previous_bytes for p in backups), "upgrade raw backup РїРѕС‚РµСЂСЏРЅ"
    assert run().returncode == 0 and target.read_bytes() == installed_bytes, "upgrade повтор не идемпотентен"
    target.write_bytes(installed_bytes)
    service_target.write_bytes(service_original)
    assert run().returncode == 0 and service_target.read_bytes() == installed_service_bytes, "callback-only pair upgrade не прошёл"
    foreign_service = installed_service_bytes + b"\n// foreign service fixture\n"
    service_target.write_bytes(foreign_service)
    assert run().returncode != 0 and target.read_bytes() == installed_bytes and service_target.read_bytes() == foreign_service, "foreign service перезаписан"
    service_target.write_bytes(service_original)
    target.write_bytes(previous_bytes)
    # Реальный Windows sharing lock не позволяет replace второго source. Первый должен rollback.
    import ctypes
    from ctypes import wintypes
    native = ctypes.WinDLL("kernel32", use_last_error=True)
    native.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p, wintypes.DWORD, wintypes.DWORD, ctypes.c_void_p]
    native.CreateFileW.restype = wintypes.HANDLE
    native.CloseHandle.argtypes = [wintypes.HANDLE]
    handle = native.CreateFileW(str(service_target), 0x80000000, 1, None, 3, 0, None)
    assert handle != ctypes.c_void_p(-1).value, "fixture sharing lock не создан"
    before_records = set((project / "tmp/McpTestJobLifecycleInstall").glob("*/transaction.json"))
    try:
        blocked = run()
    finally:
        native.CloseHandle(handle)
    assert blocked.returncode != 0 and target.read_bytes() == previous_bytes and service_target.read_bytes() == service_original, "частичная two-source установка не откатилась"
    records = set((project / "tmp/McpTestJobLifecycleInstall").glob("*/transaction.json")) - before_records
    assert len(records) == 1
    receipt = json.loads(next(iter(records)).read_text(encoding="utf-8-sig"))
    assert receipt["phase"] == "rolled_back" and "TestJobManager.cs" in receipt["changed_sources"], "rollback path не проверен после первого replace"
    assert meta.read_bytes() == meta_bytes and service_meta.read_bytes() == service_meta_bytes
    assert run().returncode == 0 and target.read_bytes() == installed_bytes and service_target.read_bytes() == installed_service_bytes
    foreign = installed_bytes + b"\n// foreign fixture\n"
    target.write_bytes(foreign)
    assert run().returncode != 0 and target.read_bytes() == foreign, "чужой hash перезаписан"
    assert meta.read_bytes() == meta_bytes
    print("PASS test-job installer: real guards + simulated OS inventory, main/active/unknown refusal, CRLF, raw backup, two-source apply/reapply + MCP2/callback-only upgrades, foreign pair refusal, locked-second-file rollback, both meta unchanged")
