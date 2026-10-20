"""Профиль windows: набор с args/поздним клиентом, build.json плеера, занятость сборочного checkout, ревизия плеера."""
from datetime import datetime, timedelta, timezone
import json
from pathlib import Path
import shutil
import tempfile
import unittest

from heavy import cli
from heavy import suite as S
from heavy import windows_profile as W

SHA = "1f3454f7e5d4d8bc883808f87be9076ad9b1811b"


def checkout_with_build(sha=SHA, success=True, ended=None):
    root = Path(tempfile.mkdtemp(prefix="heavy-win-"))
    (root / "Build" / "e2e").mkdir(parents=True)
    # Форма Build/e2e/build.json BuildProvenance (сборка Build-Game -Targets e2e, 2026-10-20).
    data = {"schemaVersion": 1, "sourceSha": sha, "profiles": [{
        "profile": "e2e", "success": success,
        "terminal": {"result": "Succeeded" if success else "Failed",
                     "endedUtc": (ended or datetime.now(timezone.utc) - timedelta(hours=1)).isoformat()},
        "actual": {"appVersion": "0.1.0+" + sha[:12]}}]}
    (root / "Build" / "e2e" / "build.json").write_text(json.dumps(data), encoding="utf-8")
    return root


class SuiteWindowsTests(unittest.TestCase):
    def test_default_windows_scenarios(self):
        chosen = S.select(S.load("default"), "windows")
        self.assertEqual([(s.name, s.scene_name, s.clients, s.late_client_delay) for s in chosen],
                         [("e2e-kit-selftest", "TestMap1", 2, 0), ("map-run-startup-route", "TestMap1", 2, 40)])

    def test_args_must_be_plain_tokens(self):
        data = {"version": 1, "defaults": {"map": "Assets/Scenes/Maps/TestMap1.unity", "mode": "elimination",
                                           "readyTimeoutSeconds": 180, "scenarioTimeoutSeconds": 300},
                "scenarios": [{"name": "x", "clients": 1, "args": {"map1": "TestMap1"}}]}
        self.assertEqual(S.parse(data, "t").scenarios[0].args, (("map1", "TestMap1"),))
        data["scenarios"][0]["args"] = {"map1": "Test Map,1"}
        with self.assertRaisesRegex(S.SuiteError, "args"):
            S.parse(data, "t")


class BuildStateTests(unittest.TestCase):
    def setUp(self):
        self.root = checkout_with_build()
        self.addCleanup(shutil.rmtree, self.root, ignore_errors=True)

    def test_read_build(self):
        info = W.read_build(self.root)
        self.assertEqual((info["sourceSha"], info["success"], info["appVersion"]), (SHA, True, "0.1.0+1f3454f7e5d4"))
        self.assertIsNone(W.read_build(self.root / "missing"))

    def test_busy_by_unity_or_build_on_this_checkout_only(self):
        here = str(self.root).replace("/", "\\")
        processes = [{"ProcessId": 1, "Name": "Unity.exe", "CommandLine": "Unity.exe -projectPath \"" + here + "\""},
                     {"ProcessId": 2, "Name": "Unity.exe", "CommandLine": "Unity.exe -projectPath F:\\other"},
                     {"ProcessId": 3, "Name": "powershell.exe", "CommandLine": "powershell -File Tools/release/Build-Game.ps1 -Win64Worktree " + here},
                     {"ProcessId": 4, "Name": "VrBattlegrounds.exe", "CommandLine": " " + here + "\\Build\\e2e\\VrBattlegrounds.exe"},
                     {"ProcessId": 5, "Name": "powershell.exe", "CommandLine": "powershell -File heavy " + here}]
        self.assertEqual(W.busy_reasons(self.root, processes, own_pid=None),
                         ["Unity.exe pid 1", "powershell.exe pid 3", "VrBattlegrounds.exe pid 4"])

    def test_fresh_foreign_build_waits_ten_minutes(self):
        fresh = checkout_with_build("a" * 40, ended=datetime.now(timezone.utc) - timedelta(minutes=2))
        self.addCleanup(shutil.rmtree, fresh, ignore_errors=True)
        self.assertIn("свежая чужая сборка", W.fresh_foreign_build(fresh, SHA))
        self.assertIsNone(W.fresh_foreign_build(fresh, "a" * 40))
        self.assertIsNone(W.fresh_foreign_build(self.root, "b" * 40))


class RunTests(unittest.TestCase):
    def test_run_e2e_command(self):
        scenario = S.Scenario(name="map-run-startup-route", clients=2, profiles=("windows",),
                              map="Assets/Scenes/Maps/TestMap1.unity", mode="elimination", ready_timeout=180,
                              scenario_timeout=300, args=(("a", "1"), ("b", "2")), late_client_delay=40)
        command = W.run_e2e_command(Path("R"), scenario, Path("P.exe"), Path("out"))
        for pair in (["-Map", "TestMap1"], ["-Clients", "2"], ["-Timeout", "300"], ["-LateClientDelay", "40"],
                     ["-Arg", "a=1,b=2"], ["-ResultsDir", "out"]):
            index = command.index(pair[0])
            self.assertEqual(command[index + 1], pair[1])
        self.assertIn("-SkipStaleCheck", command)

    def test_revision_mismatch_is_reported(self):
        results = [("server", {"appVersion": "0.1.0+1f3454f7e5d4"}), ("client-1", {"appVersion": "0.1.0+334068144d51"}),
                   ("client-2", None)]
        self.assertEqual(W.revision_reasons(results, "1f3454f7e5d4"),
                         ["client-1: appVersion 0.1.0+334068144d51 не ревизия 1f3454f7e5d4"])

    def test_editmode_with_windows_profile_is_code_2(self):
        self.assertEqual(cli.main(["--profile", "windows", "--editmode", "also"]), 2)


if __name__ == "__main__":
    unittest.main()
