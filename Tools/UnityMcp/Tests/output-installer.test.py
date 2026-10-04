"""Частичная установка/повтор/защита чужих правок в собственной временной копии."""
from pathlib import Path
import os
import shutil
import stat
import subprocess
import sys
import tempfile
import time

root = Path(__file__).resolve().parents[3]
shell = sys.argv[1] if len(sys.argv) > 1 else "powershell.exe"
with tempfile.TemporaryDirectory(prefix="mcp-installer-", dir=root / "tmp") as temporary:
    project = Path(temporary).resolve()
    assert project.parent == (root / "tmp").resolve()
    tooling = project / "Tools/UnityMcp"
    tooling.mkdir(parents=True)
    source = root / "Tools/UnityMcp"
    for name in ("Apply-OutputGuard.ps1", "output-guard-10.2.0.patch"):
        shutil.copy2(source / name, tooling / name)
    shutil.copytree(source / "OutputGuard", tooling / "OutputGuard")
    package = project / "Packages/com.coplaydev.unity-mcp"
    (package / "Editor/Tools").mkdir(parents=True)
    (package / "Editor/Helpers").mkdir(parents=True)
    (package / "package.json").write_text('{"version":"10.2.0"}', encoding="utf-8")
    execute = package / "Editor/Tools/ExecuteCode.cs"
    shutil.copy2(root / "Packages/com.coplaydev.unity-mcp/Editor/Tools/ExecuteCode.cs", execute)
    subprocess.run(["git", "--no-pager", "init", "--quiet"], cwd=project, check=True, capture_output=True)
    # Стенд работает и после установки патча: восстанавливает только собственную копию.
    if "ExecuteCodeOutputGuard.Limit" in execute.read_text(encoding="utf-8-sig"):
        reverse = subprocess.run(["git", "--no-pager", "-c", "core.autocrlf=false", "apply", "--reverse", "--ignore-whitespace",
                        "--directory=Packages/com.coplaydev.unity-mcp", str(tooling / "output-guard-10.2.0.patch")],
                       cwd=project, capture_output=True)
        if reverse.returncode:
            raise AssertionError(reverse.stderr.decode("utf-8", errors="replace")[:2000])
        # Reverse добавляет старые строки с EOL патча; восстановить байты LF upstream.
        execute.write_bytes(execute.read_bytes().replace(b"\r\n", b"\n"))
    lock = project / "tmp/unity-lock"
    lock.mkdir(parents=True)
    (lock / "info").write_text(f"owner=fixture\nuntil={int(time.time()) + 300}\n", encoding="utf-8")
    # Разрешение только этому дочернему процессу выполнить наш fixture; политика машины не меняется.
    command = [shell, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(tooling / "Apply-OutputGuard.ps1"), "-LockOwner", "fixture"]
    def install():
        return subprocess.run(command, cwd=project, capture_output=True, timeout=30)
    execute.chmod(stat.S_IREAD)
    try:
        first = install()
        assert first.returncode != 0, "read-only destination did not fail"
        assert (package / "Editor/Helpers/ExecuteCodeOutputGuard.cs").exists(), "partial-copy scenario not reached: " + first.stderr.decode("utf-8", errors="replace")[:2000]
    finally:
        execute.chmod(stat.S_IREAD | stat.S_IWRITE)
    resumed = install()
    if resumed.returncode:
        raise AssertionError(resumed.stderr.decode("utf-8", errors="replace")[:2000])
    assert "ExecuteCodeOutputGuard.Limit" in execute.read_text(encoding="utf-8-sig")
    assert install().returncode == 0, "repeat of a verified installation failed"
    helper = package / "Editor/Helpers/ExecuteCodeOutputGuard.cs"
    helper.write_text(helper.read_text(encoding="utf-8") + "\n// foreign modification\n", encoding="utf-8")
    changed = helper.read_bytes()
    assert install().returncode != 0, "foreign helper change was overwritten"
    assert helper.read_bytes() == changed
    print("PASS installer: partial-copy failure, verified resume, idempotent repeat, foreign-change refusal")
