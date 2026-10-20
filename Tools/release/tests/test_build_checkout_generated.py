"""Tools/release/BuildCheckoutGenerated.ps1: какие изменения сборочного checkout считаются generated.

Каждый тест собирает временный git-репозиторий из реальных Packages/manifest.json, packages-lock.json и
URP-ассетов HEAD, вносит изменение и спрашивает модуль через Windows PowerShell. Отдельный класс проверяет,
как Build-Game.ps1 помечает generated-записи dirty в pair manifest. Запуск:
    python -m unittest discover -s Tools/release/tests -p "test_*.py"
"""

from pathlib import Path
import json
import re
import shutil
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[3]
MODULE = ROOT / "Tools" / "release" / "BuildCheckoutGenerated.ps1"
POWERSHELL = shutil.which("powershell") or shutil.which("pwsh")
LOCK = "Packages/packages-lock.json"
PC = "Assets/URPDefaultResources/PC.asset"
MCP = "com.coplaydev.unity-mcp"
FILES = ["Packages/manifest.json", LOCK, PC, "Assets/URPDefaultResources/Quest.asset",
         "Assets/URPDefaultResources/Default_Forward_Renderer.asset"]

DRIVER = """param([string] $Module, [string] $Worktree, [string] $Patch)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
. $Module
$r = Get-BuildCheckoutChanges $Worktree
if ($Patch -and $r.Generated.Count -gt 0) { Restore-GeneratedBuildChanges $Worktree $r.Generated $Patch | Out-Null }
[pscustomobject]@{ generated = @($r.Generated); unexpected = @($r.Unexpected) } | ConvertTo-Json -Compress
"""


def run(*args, cwd):
    return subprocess.run(args, cwd=cwd, check=True, capture_output=True).stdout.decode("utf-8")


@unittest.skipUnless(POWERSHELL, "нужен powershell или pwsh")
class BuildCheckoutGeneratedTests(unittest.TestCase):
    def setUp(self):
        self.temp = Path(tempfile.mkdtemp(prefix="build-generated-"))
        self.repo = self.temp / "checkout"
        self.repo.mkdir()
        run("git", "init", "-q", cwd=self.repo)
        for key, value in (("core.autocrlf", "false"), ("user.name", "test"), ("user.email", "test@example.invalid")):
            run("git", "config", key, value, cwd=self.repo)
        for rel in FILES:
            target = self.repo / rel
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_bytes(subprocess.check_output(["git", "-C", str(ROOT), "show", f"HEAD:{rel}"]))
        run("git", "add", "-A", cwd=self.repo)
        run("git", "commit", "-q", "-m", "base", cwd=self.repo)
        driver = self.temp / "driver.ps1"
        driver.write_bytes(b"\xef\xbb\xbf" + DRIVER.encode("utf-8"))
        self.driver = driver

    def tearDown(self):
        shutil.rmtree(self.temp, ignore_errors=True)

    def classify(self, restore=False):
        patch = self.temp / "logs" / "generated.patch"
        args = [POWERSHELL, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(self.driver),
                "-Module", str(MODULE), "-Worktree", str(self.repo)]
        if restore:
            args += ["-Patch", str(patch)]
        out = subprocess.run(args, check=True, capture_output=True).stdout.decode("utf-8-sig")
        return json.loads(out.strip().splitlines()[-1]), patch

    def status(self):
        return run("git", "status", "--porcelain", cwd=self.repo)

    def write_git_lock(self, mutate=None):
        manifest = json.loads((self.repo / "Packages/manifest.json").read_text(encoding="utf-8"))
        url = manifest["dependencies"][MCP]
        path = self.repo / LOCK
        lock = json.loads(path.read_text(encoding="utf-8"))
        entry = lock["dependencies"][MCP]
        self.assertEqual("embedded", entry["source"], "в репозитории lock MCP ожидается embedded")
        entry["version"] = url
        entry["source"] = "git"
        entry["hash"] = url.rsplit("#", 1)[1]
        if mutate:
            mutate(lock)
        path.write_text(json.dumps(lock, indent=2) + "\n", encoding="utf-8", newline="\n")

    def replace(self, rel, pattern, repl):
        path = self.repo / rel
        text = path.read_text(encoding="utf-8")
        updated, count = re.subn(pattern, repl, text, count=1, flags=re.M)
        self.assertEqual(1, count, f"нет {pattern} в {rel}")
        path.write_text(updated, encoding="utf-8", newline="\n")

    def assert_unexpected(self, result, rel, reason):
        self.assertEqual([], result["generated"])
        self.assertEqual(1, len(result["unexpected"]), result["unexpected"])
        self.assertIn(rel, result["unexpected"][0])
        self.assertIn(reason, result["unexpected"][0])

    def test_clean_checkout_has_no_changes(self):
        result, _ = self.classify()
        self.assertEqual({"generated": [], "unexpected": []}, result)

    def test_git_form_lock_without_embedded_copy_is_restored(self):
        self.write_git_lock()
        result, patch = self.classify(restore=True)
        self.assertEqual([LOCK], result["generated"])
        self.assertEqual([], result["unexpected"])
        self.assertEqual("", self.status())
        self.assertIn('"source": "git"', patch.read_text(encoding="utf-8"))

    def test_git_form_lock_with_embedded_copy_is_unexpected(self):
        self.write_git_lock()
        (self.repo / "Packages" / MCP).mkdir()
        self.assert_unexpected(self.classify()[0], LOCK, "не embedded-форма")

    def test_git_form_lock_with_wrong_hash_is_unexpected(self):
        self.write_git_lock(lambda lock: lock["dependencies"][MCP].update(hash="0" * 40))
        self.assert_unexpected(self.classify()[0], LOCK, "не git-форма")

    def test_lock_change_of_other_package_is_unexpected(self):
        def mutate(lock):
            name = next(n for n in lock["dependencies"] if n != MCP)
            lock["dependencies"][name]["depth"] = 99
        self.write_git_lock(mutate)
        self.assert_unexpected(self.classify()[0], LOCK, "изменены записи кроме")

    def test_staged_generated_lock_is_unexpected(self):
        self.write_git_lock()
        run("git", "add", LOCK, cwd=self.repo)
        result, _ = self.classify(restore=True)
        self.assert_unexpected(result, LOCK, "статус 'M '")
        self.assertTrue(self.status().startswith("M "))

    def test_prefilter_only_change_is_restored(self):
        self.replace(PC, r"^(  m_PrefilteringModeAdditionalLight: )\d+$", r"\g<1>4")
        self.replace(PC, r"^(  m_PrefilterSoftShadows: )\d+$", r"\g<1>1")
        result, _ = self.classify(restore=True)
        self.assertEqual([PC], result["generated"])
        self.assertEqual("", self.status())

    def test_prefilter_with_other_field_is_unexpected(self):
        self.replace(PC, r"^(  m_PrefilteringModeAdditionalLight: )\d+$", r"\g<1>4")
        self.replace(PC, r"^(  m_Name: ).*$", r"\g<1>Renamed")
        result, _ = self.classify(restore=True)
        self.assert_unexpected(result, PC, "строка вне полей m_Prefilter*")
        self.assertIn(PC, self.status())

    def test_prefilter_with_added_header_like_line_is_unexpected(self):
        # Добавленная строка "++ x" в hunk выглядит как заголовок "+++ " и не должна пропускаться.
        self.replace(PC, r"^(  m_PrefilteringModeAdditionalLight: )\d+$", r"\g<1>4\n++ added")
        result, _ = self.classify(restore=True)
        self.assert_unexpected(result, PC, "строка вне полей m_Prefilter*: +++ added")
        self.assertIn(PC, self.status())

    def test_prefilter_with_removed_header_like_line_is_unexpected(self):
        # Удалённая строка "-- x" в hunk выглядит как заголовок "--- " и не должна пропускаться.
        self.replace(PC, r"^(  m_Name: .*)$", r"\g<1>\n-- removed")
        run("git", "commit", "-q", "-am", "header-like line", cwd=self.repo)
        self.replace(PC, r"^-- removed\n", "")
        self.replace(PC, r"^(  m_PrefilteringModeAdditionalLight: )\d+$", r"\g<1>4")
        self.assert_unexpected(self.classify()[0], PC, "строка вне полей m_Prefilter*: --- removed")

    def test_other_asset_is_unexpected(self):
        self.replace("Assets/URPDefaultResources/Default_Forward_Renderer.asset", r"^(  m_Name: ).*$", r"\g<1>Renamed")
        self.assert_unexpected(self.classify()[0], "Default_Forward_Renderer.asset", "не generated-файл")

    def test_untracked_file_is_unexpected(self):
        (self.repo / "Assets" / "Resources").mkdir(parents=True)
        (self.repo / "Assets" / "Resources" / "PerformanceTestRunInfo.json").write_text("{}", encoding="utf-8")
        self.assert_unexpected(self.classify()[0], "PerformanceTestRunInfo.json", "не generated-файл")


BUILD_GAME = ROOT / "Tools" / "release" / "Build-Game.ps1"
LOCK_SHA = "a" * 64

# Write-BuildPairManifest из Build-Game.ps1 (через AST, без запуска сборки) с подставной валидацией профиля:
# builder manifest — Build/Server/build.json временного checkout, классификация — JSON путь -> sha256.
PAIR_DRIVER = """param([string] $BuildGame, [string] $Checkout, [string] $GeneratedFile)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$ast = [System.Management.Automation.Language.Parser]::ParseFile($BuildGame, [ref]$null, [ref]$null)
$fn = $ast.Find({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -ceq 'Write-BuildPairManifest' }, $true)
. ([scriptblock]::Create($fn.Extent.Text))
function Read-ValidatedBuildProfile($ManifestPath) {
    $m = Get-Content -LiteralPath $ManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    return [pscustomobject]@{ Manifest = $m; Record = [ordered]@{ profile = 'server'; success = $true } }
}
$RepoRoot = Split-Path $Checkout -Parent
$Config = 'Test'
$generated = @{}
if ($GeneratedFile) {
    $hashes = @{}
    (Get-Content -LiteralPath $GeneratedFile -Raw -Encoding UTF8 | ConvertFrom-Json).PSObject.Properties | ForEach-Object { $hashes[$_.Name] = [string]$_.Value }
    $generated[$Checkout] = $hashes
}
$runs = @([pscustomobject]@{ Name = 'r1'; BuildTarget = 'Win64'; Profiles = 'server'; ProjectPath = $Checkout })
$terminals = @{ r1 = @{ startedUtc = [DateTimeOffset]::UtcNow; endedUtc = [DateTimeOffset]::UtcNow.ToString('o'); exitCode = 0 } }
$r = Write-BuildPairManifest $runs @{ $Checkout = ('c' * 40) } $terminals $true $null $generated
(Get-Content -LiteralPath $r.Path -Raw -Encoding UTF8 | ConvertFrom-Json).dirty | ConvertTo-Json -Depth 10 -Compress
"""


@unittest.skipUnless(POWERSHELL, "нужен powershell или pwsh")
class BuildPairManifestGeneratedTests(unittest.TestCase):
    """Build-Game.ps1: dirty-запись builder manifest с sha256 generated-файла не делает pair manifest dirty."""

    def setUp(self):
        self.temp = Path(tempfile.mkdtemp(prefix="build-pair-"))
        self.checkout = self.temp / "build-test-win64" / "Vr_Battlegrounds_ai"
        (self.checkout / "Build" / "Server").mkdir(parents=True)
        self.driver = self.temp / "pair-driver.ps1"
        self.driver.write_bytes(b"\xef\xbb\xbf" + PAIR_DRIVER.encode("utf-8"))

    def tearDown(self):
        shutil.rmtree(self.temp, ignore_errors=True)

    def pair_dirty(self, entries, generated):
        raw = {"schemaVersion": 1, "dirty": {"value": bool(entries), "entries": entries}}
        (self.checkout / "Build" / "Server" / "build.json").write_text(json.dumps(raw), encoding="utf-8")
        args = [POWERSHELL, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(self.driver),
                "-BuildGame", str(BUILD_GAME), "-Checkout", str(self.checkout)]
        if generated is not None:
            generated_file = self.temp / "generated.json"
            generated_file.write_text(json.dumps(generated), encoding="utf-8")
            args += ["-GeneratedFile", str(generated_file)]
        out = subprocess.run(args, check=True, capture_output=True).stdout.decode("utf-8-sig")
        dirty = json.loads(out.strip().splitlines()[-1])
        return dirty["value"], [(e["evidence"]["path"], e["generated"]) for e in dirty["entries"]]

    @staticmethod
    def entry(path, sha, status=" M"):
        return {"status": status, "path": path, "sha256": sha}

    def test_generated_lock_with_same_sha_is_not_dirty(self):
        value, marks = self.pair_dirty([self.entry(LOCK, LOCK_SHA)], {LOCK: LOCK_SHA})
        self.assertIs(False, value)
        self.assertEqual([(LOCK, True)], marks)

    def test_generated_lock_with_other_sha_is_dirty(self):
        value, marks = self.pair_dirty([self.entry(LOCK, LOCK_SHA)], {LOCK: "d" * 64})
        self.assertIs(True, value)
        self.assertEqual([(LOCK, False)], marks)

    def test_foreign_change_keeps_pair_dirty(self):
        entries = [self.entry(LOCK, LOCK_SHA), self.entry("Assets/Scripts/Foo.cs", "b" * 64)]
        value, marks = self.pair_dirty(entries, {LOCK: LOCK_SHA})
        self.assertIs(True, value)
        self.assertEqual([(LOCK, True), ("Assets/Scripts/Foo.cs", False)], marks)

    def test_staged_lock_is_dirty(self):
        value, marks = self.pair_dirty([self.entry(LOCK, LOCK_SHA, status="M ")], {LOCK: LOCK_SHA})
        self.assertIs(True, value)
        self.assertEqual([(LOCK, False)], marks)

    def test_without_classification_lock_is_dirty(self):
        value, marks = self.pair_dirty([self.entry(LOCK, LOCK_SHA)], None)
        self.assertIs(True, value)
        self.assertEqual([(LOCK, False)], marks)

    def test_clean_builder_manifest_is_not_dirty(self):
        self.assertEqual((False, []), self.pair_dirty([], None))


if __name__ == "__main__":
    unittest.main()
