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
expected = "395272a8507666a4d88d3b72e69023a03f25499712f1cb895877dfb2ccff4a01"

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
    for name in ("Apply-TestJobLifecycle.ps1", "test-job-lifecycle-10.2.0.patch"):
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
    meta = target.with_name(target.name + ".meta")
    meta.write_bytes(b"fileFormatVersion: 2\nguid: 1234567890abcdef1234567890abcdef\n")
    meta_bytes = meta.read_bytes()
    command = [shell, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(tooling / "Apply-TestJobLifecycle.ps1"),
               "-OfflineEditor", "-PythonExecutable", sys.executable]
    def run(argv=command, cwd=project):
        return subprocess.run(argv, cwd=cwd, env=environment, capture_output=True, timeout=30)
    for extra in (["-LockOwner", "fixture"], ["-Ticket", "1"]):
        assert run(command + extra).returncode != 0, "legacy/incomplete capability принята"
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
    assert meta.read_bytes() == meta_bytes
    backups = list((project / "tmp/McpTestJobLifecycleInstall").glob("*/TestJobManager.cs.backup"))
    assert len(backups) == 1 and backups[0].read_bytes() == original, "backup исходных байтов потерян"
    installed_bytes = target.read_bytes()
    assert run().returncode == 0 and target.read_bytes() == installed_bytes, "повтор не идемпотентен"
    foreign = installed_bytes + b"\n// foreign fixture\n"
    target.write_bytes(foreign)
    assert run().returncode != 0 and target.read_bytes() == foreign, "чужой hash перезаписан"
    assert meta.read_bytes() == meta_bytes
    print("PASS test-job installer: real guards + simulated OS inventory, main/active/unknown refusal, CRLF, raw backup, apply/reapply, foreign hash, meta unchanged")
