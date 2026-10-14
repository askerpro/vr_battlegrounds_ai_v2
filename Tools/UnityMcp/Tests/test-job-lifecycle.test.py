"""Production C# из pinned ZIP: RED (--upstream) или три GREEN native API варианта.

Пример: python Tools/UnityMcp/Tests/test-job-lifecycle.test.py <upstream-10.2.0.zip> [--upstream]
Исходники и build/log остаются в собственном ignored tmp. Unity здесь не запускается.
"""
from pathlib import Path
import argparse
import hashlib
import os
import subprocess
import tempfile
import zipfile
from xml.sax.saxutils import escape

root = Path(__file__).resolve().parents[3]
tests = Path(__file__).resolve().parent
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument("archive", type=Path)
parser.add_argument("--upstream", action="store_true", help="Прогон RED: вернуть фактический exit code unmodified upstream")
args = parser.parse_args()
fixture = Path(tempfile.mkdtemp(prefix="mcp-test-job-harness-", dir=root / "tmp")).resolve()
assert fixture.parent == (root / "tmp").resolve()
source = fixture / "source/Editor/Services/TestJobManager.cs"
source.parent.mkdir(parents=True)
with zipfile.ZipFile(args.archive) as upstream:
    entries = [name for name in upstream.namelist() if name.endswith("/MCPForUnity/Editor/Services/TestJobManager.cs")]
    assert len(entries) == 1, "Неоднозначный pinned source"
    original = upstream.read(entries[0]).replace(b"\r\n", b"\n")
    service_entries = [name for name in upstream.namelist() if name.endswith("/MCPForUnity/Editor/Services/TestRunnerService.cs")]
    assert len(service_entries) == 1
    service = upstream.read(service_entries[0]).replace(b"\r\n", b"\n")
    assert hashlib.sha256(service).hexdigest() == "b5f25bffbcee394d746f4a67b7cfaba1bf3e24279f581d79fec7c20098d6ee23"
    source.with_name("TestRunnerService.cs").write_bytes(service)
    assert hashlib.sha256(original).hexdigest() == "80be4984f63398ba8d3484a514e131f131b9a562757e713b07d0ac73fc164476", "Неизвестный upstream"
source.write_bytes(original)
if not args.upstream:
    relative = (fixture / "source").relative_to(root).as_posix()
    subprocess.run(["git", "-c", "safe.directory=" + str(root), "-c", "core.autocrlf=false", "apply",
                    "--ignore-space-change", "--directory=" + relative,
                    str(root / "Tools/UnityMcp/test-job-lifecycle-10.2.0.patch")], cwd=root, check=True)
project = fixture / "TestJobHarness.csproj"
project.write_text((tests / "TestJobHarness.csproj.txt").read_text(encoding="utf-8")
                   .replace("__MCP_PACKAGE_ROOT__", escape((fixture / "source").as_posix()))
                   .replace("__HARNESS_ROOT__", escape(tests.as_posix())), encoding="utf-8")
environment = dict(os.environ, DOTNET_CLI_HOME=str(root / "tmp/mcp-test-job-dotnet-home"),
                   DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1",
                   DOTNET_GENERATE_ASPNET_CERTIFICATE="false")
def run(command, name):
    result = subprocess.run(command, cwd=root, env=environment, capture_output=True, text=True, encoding="utf-8", errors="replace", timeout=90)
    (fixture / (name + ".log")).write_text(result.stdout + result.stderr, encoding="utf-8")
    print(result.stdout + result.stderr, end="")
    return result.returncode
restored = run(["dotnet", "restore", str(project), "--configfile", str(tests / "NuGet.Config")], "restore")
if restored:
    raise SystemExit(restored)
variants = [""] if args.upstream else ["", "TEST_NATIVE_MISSING", "TEST_NATIVE_WRONG_TYPE"]
for variant in variants:
    code = run(["dotnet", "run", "--no-restore", "--project", str(project), "-p:UseSharedCompilation=false",
                "-p:NativeApiVariant=" + variant], "upstream-red" if args.upstream else "green-" + (variant or "available"))
    if code:
        print("Evidence fixture:", fixture)
        raise SystemExit(code)
print("Evidence fixture:", fixture)
