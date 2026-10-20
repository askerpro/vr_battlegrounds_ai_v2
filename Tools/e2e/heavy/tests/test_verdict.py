"""Вердикт heavy на реальных результатах участников (fixtures из прогонов на worker 2026-10-20)."""
import json
from pathlib import Path
import tempfile
import unittest

from heavy import verdict as V

FIXTURES = Path(__file__).resolve().parent / "fixtures"
SCENARIO = "scripted-wall-purchase"


def fixture(name):
    return json.loads((FIXTURES / name).read_text(encoding="utf-8"))


def framework(*entries):
    path = Path(tempfile.mkdtemp()) / "known-noise.json"
    path.write_text(json.dumps({"version": 1, "entries": list(entries)}), encoding="utf-8")
    return V.load_known_noise(path)


DEFECT = {"id": "teleport", "kind": "defect", "owner": "network", "ticket": "t-1", "scope": "server",
          "regex": "UpdateTeleportState", "reason": "известный дефект"}
NOISE = {"id": "pm", "kind": "noise", "scope": "client", "regex": "Package Manager", "reason": "клон MPPM"}


class ParticipantTests(unittest.TestCase):
    def test_real_green_server_and_client_pass(self):
        self.assertEqual(V.evaluate_participant(fixture("wall-purchase-server-pass.json"), SCENARIO, "server"), (V.PASS, []))
        self.assertEqual(V.evaluate_participant(fixture("wall-purchase-client-pass.json"), SCENARIO, "client"), (V.PASS, []))

    def test_real_timeout_fails_with_named_checks(self):
        verdict, reasons = V.evaluate_participant(fixture("wall-purchase-server-timeout.json"), SCENARIO, "server")
        self.assertEqual(verdict, V.FAIL)
        self.assertTrue(reasons[0].startswith("status=timeout"))
        self.assertTrue(any("касса провела покупку" in r for r in reasons))

    def test_missing_or_unfinished_result_is_inconclusive(self):
        self.assertEqual(V.evaluate_participant(None, SCENARIO, "server")[0], V.INCONCLUSIVE)
        started = dict(fixture("wall-purchase-server-pass.json"), status="started")
        self.assertEqual(V.evaluate_participant(started, SCENARIO, "server")[0], V.INCONCLUSIVE)

    def test_wrong_scenario_or_role_fails(self):
        result = fixture("wall-purchase-client-pass.json")
        self.assertEqual(V.evaluate_participant(result, "scripted-input-smoke", "client")[0], V.FAIL)
        self.assertEqual(V.evaluate_participant(result, SCENARIO, "server")[0], V.FAIL)

    def test_no_checks_fails(self):
        result = dict(fixture("wall-purchase-server-pass.json"), checks=[])
        self.assertEqual(V.evaluate_participant(result, SCENARIO, "server"), (V.FAIL, ["нет ни одной проверки"]))


class LogIssueTests(unittest.TestCase):
    def issues(self, *messages, noise=None, dropped=0):
        return {"logIssues": [{"type": "Error", "message": m, "stack": "", "count": 1} for m in messages],
                "knownNoise": noise or [], "logIssuesDropped": dropped}

    def test_scenario_noise_framework_defect_and_unexpected(self):
        rules = framework(DEFECT)
        scenario_noise = [{"id": "s", "regex": "ожидаемо", "reason": "сценарий"}]
        report = V.classify_log_issues([
            ("server", self.issues("ожидаемо", "Event is Method call UpdateTeleportState", "новая ошибка",
                                   noise=scenario_noise)),
            ("client-1", self.issues("Event is Method call UpdateTeleportState"))], rules)
        self.assertEqual([k["id"] for k in report["known"]], ["s"])
        self.assertEqual([d["id"] for d in report["defects"]], ["teleport"])
        # scope=server: у клиента та же строка — неожиданная.
        self.assertEqual([(u["process"], u["message"]) for u in report["unexpected"]],
                         [("server", "новая ошибка"), ("client-1", "Event is Method call UpdateTeleportState")])

    def test_missing_result_or_capture_is_noted(self):
        report = V.classify_log_issues([("server", None), ("client-1", {})], [])
        self.assertEqual(len(report["notes"]), 2)
        self.assertFalse(any(p["captured"] for p in report["perProcess"]))

    def test_defect_requires_owner_and_ticket(self):
        with self.assertRaisesRegex(V.NoiseError, "owner и ticket"):
            framework(dict(DEFECT, owner=""))


class LogScanTests(unittest.TestCase):
    def scan(self, text, process="client-1", rules=()):
        path = Path(tempfile.mkdtemp()) / "Player 2-1.log"
        path.write_text(text, encoding="utf-8")
        return V.scan_log(path, process, list(rules))

    def test_stack_frame_signatures_are_not_counted(self):
        result = self.scan("UnityEditor.MPE.EventService:Request (string,System.Action`2<System.Exception, object[]>)\n"
                           "  at Foo (NullReferenceException e)\n")
        self.assertEqual(result["counts"], {"uxrComponentNotFound": 0, "nullReference": 0, "exception": 0})

    def test_exception_messages_are_counted_and_gated(self):
        result = self.scan("NullReferenceException: Object reference not set\n"
                           "UltimateXR.Core.Unique.UxrComponentNotFoundException: Target\n"
                           "System.InvalidOperationException: x\n")
        self.assertEqual(result["counts"], {"uxrComponentNotFound": 1, "nullReference": 1, "exception": 3})
        self.assertEqual(result["gated"], {"uxrComponentNotFound": 1, "nullReference": 1})

    def test_known_noise_line_is_counted_but_not_gated(self):
        rule = {"id": "n", "kind": "noise", "scope": "client", "regex": "Bootstrap", "reason": "шум"}
        result = self.scan("NullReferenceException: Bootstrap\n", rules=framework(rule))
        self.assertEqual(result["counts"]["nullReference"], 1)
        self.assertEqual(result["gated"]["nullReference"], 0)

    def test_native_crash_is_found(self):
        self.assertEqual(len(self.scan("Crash!!!\n")["native"]), 1)


class ScenarioAndAggregateTests(unittest.TestCase):
    def record(self, **overrides):
        record = {"participants": [{"role": "server", "verdict": V.PASS, "reasons": []}],
                  "logs": {"archived": 1, "scans": [], "classified": V.classify_log_issues([], [])}}
        record.update(overrides)
        return record

    def test_green_record_passes(self):
        self.assertEqual(V.scenario_verdict(self.record()), (V.PASS, []))

    def test_participant_fail_dominates_missing_logs(self):
        record = self.record(participants=[{"role": "server", "verdict": V.FAIL, "reasons": ["x"]}],
                             logs={"archived": 0})
        self.assertEqual(V.scenario_verdict(record)[0], V.FAIL)

    def test_launch_error_or_missing_archives_is_inconclusive(self):
        self.assertEqual(V.scenario_verdict(self.record(participants=[], launchError="Ready: таймаут"))[0], V.INCONCLUSIVE)
        self.assertEqual(V.scenario_verdict(self.record(logs={"archived": 0}))[0], V.INCONCLUSIVE)

    def test_gated_log_counter_fails(self):
        scan = {"process": "client-1", "gated": {"nullReference": 2}, "native": []}
        verdict, reasons = V.scenario_verdict(self.record(logs={"archived": 1, "scans": [scan]}))
        self.assertEqual(verdict, V.FAIL)
        self.assertIn("client-1: nullReference 2 строк в логе", reasons)

    def test_aggregate_exit_codes(self):
        ok = {"verdict": V.PASS}
        self.assertEqual(V.aggregate([ok], [ok]), ("mergeable", 0))
        self.assertEqual(V.aggregate([ok], [{"verdict": V.INCONCLUSIVE}, {"verdict": V.FAIL}]), ("blocked", 1))
        self.assertEqual(V.aggregate([{"verdict": V.INCONCLUSIVE}], [ok]), ("inconclusive", 2))
        self.assertEqual(V.aggregate([ok], []), ("inconclusive", 2))


if __name__ == "__main__":
    unittest.main()
