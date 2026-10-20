"""Tools/release/Build-Game.ps1: цель e2e — Windows e2e-плеер (E2EPlayerBuilder) для heavy --profile windows.

Функции берутся из скрипта через AST, без запуска сборки; отказ Prod проверяется настоящим запуском скрипта,
который выходит до поиска Unity и сборочных checkout. Запуск:
    python -m unittest discover -s Tools/release/tests -p "test_*.py"
"""

from pathlib import Path
import json
import shutil
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[3]
BUILD_GAME = ROOT / "Tools" / "release" / "Build-Game.ps1"
POWERSHELL = shutil.which("powershell") or shutil.which("pwsh")

DRIVER = """param([string] $BuildGame, [string] $Spec, [string] $Config)
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$ast = [System.Management.Automation.Language.Parser]::ParseFile($BuildGame, [ref]$null, [ref]$null)
foreach ($name in @('Get-BuildRuns', 'Get-UnityRunArguments')) {
    $fn = $ast.Find({ param($n) $n -is [System.Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -ceq $name }, $true)
    . ([scriptblock]::Create($fn.Extent.Text))
}
$sharedRevision = '0123456789ab'
try { $runs = @(Get-BuildRuns $Spec) } catch { [pscustomobject]@{ error = $_.Exception.Message } | ConvertTo-Json -Compress; exit 0 }
$out = foreach ($run in $runs) {
    $run | Add-Member -NotePropertyName ProjectPath -NotePropertyValue 'C:\\checkout' -Force
    [pscustomobject]@{ name = $run.Name; target = $run.BuildTarget; profiles = $run.Profiles;
                       arguments = @(Get-UnityRunArguments $run 'C:\\log.txt' @()) }
}
@($out) | ConvertTo-Json -Depth 5 -Compress
"""


@unittest.skipUnless(POWERSHELL, "нужен powershell или pwsh")
class BuildGameE2eTargetTests(unittest.TestCase):
    def setUp(self):
        self.temp = Path(tempfile.mkdtemp(prefix="build-game-e2e-"))
        self.driver = self.temp / "driver.ps1"
        self.driver.write_bytes(b"\xef\xbb\xbf" + DRIVER.encode("utf-8"))

    def tearDown(self):
        shutil.rmtree(self.temp, ignore_errors=True)

    def runs(self, spec, config="Test"):
        out = subprocess.run([POWERSHELL, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(self.driver),
                              "-BuildGame", str(BUILD_GAME), "-Spec", spec, "-Config", config],
                             check=True, capture_output=True).stdout.decode("utf-8-sig")
        data = json.loads(out.strip().splitlines()[-1])
        return data if isinstance(data, list) else [data]

    def test_e2e_is_one_win64_run_of_e2e_player_builder(self):
        (run,) = self.runs("e2e")
        self.assertEqual((run["name"], run["target"], run["profiles"]), ("e2e", "Win64", "e2e"))
        arguments = run["arguments"]
        self.assertIn("VrBattlegrounds.EditorTools.E2EPlayerBuilder.RunBatch", arguments)
        self.assertEqual(arguments[arguments.index("-buildRevision") + 1], "0123456789ab")
        self.assertNotIn("-buildProfile", arguments)

    def test_all_stays_release_profiles_with_game_builder(self):
        runs = self.runs("all")
        self.assertEqual([r["name"] for r in runs], ["android", "server"])
        self.assertIn("VrBattlegrounds.EditorTools.Release.GameBuilder.RunBatch", runs[1]["arguments"])
        self.assertEqual(runs[1]["arguments"][runs[1]["arguments"].index("-buildProfile") + 1], "server")

    def test_e2e_together_with_other_profile_is_refused(self):
        (result,) = self.runs("e2e,server")
        self.assertIn("e2e запускается отдельно", result["error"])

    def test_prod_e2e_exits_2_before_any_checkout_work(self):
        result = subprocess.run([POWERSHELL, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", str(BUILD_GAME),
                                 "-Targets", "e2e", "-Config", "Prod", "-DryRun",
                                 "-Win64Worktree", str(self.temp / "missing")], capture_output=True)
        self.assertEqual(result.returncode, 2)
        self.assertIn("только с -Config Test", result.stdout.decode("utf-8", "replace"))


if __name__ == "__main__":
    unittest.main()
