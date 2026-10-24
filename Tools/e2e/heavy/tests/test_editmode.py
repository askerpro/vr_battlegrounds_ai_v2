"""EditMode в heavy: список известных красных и поимённое сравнение (форма ответа — get_test_job MCP for Unity)."""
import json
from pathlib import Path
import tempfile
import unittest

from heavy import editmode as M
from heavy import verdict as V

KNOWN = {"A.Old": "карты", "B.Flaky": "выстрел"}


def result(*rows, total=100, failed=None):
    rows = [{"fullName": name, "state": state, "message": message} for name, state, message in rows]
    red = sum(1 for r in rows if M.is_red(r["state"]))
    return {"summary": {"total": total, "passed": total - len(rows), "failed": red if failed is None else failed,
                        "skipped": len(rows) - red}, "results": rows}


def write(data):
    path = Path(tempfile.mkdtemp()) / "editmode-known-red.json"
    path.write_text(json.dumps(data, ensure_ascii=False), encoding="utf-8")
    return path


class KnownRedFileTests(unittest.TestCase):
    def test_repository_file_is_valid(self):
        self.assertGreater(len(M.load_known_red()), 0)

    def test_duplicate_or_empty_name_is_error(self):
        with self.assertRaisesRegex(M.KnownRedError, "повторяется"):
            M.load_known_red(write({"version": 1, "tests": [{"name": "A"}, {"name": "A"}]}))
        with self.assertRaisesRegex(M.KnownRedError, "пустой name"):
            M.load_known_red(write({"version": 1, "tests": [{"reason": "x"}]}))
        with self.assertRaisesRegex(M.KnownRedError, "version 1"):
            M.load_known_red(write({"tests": []}))


class CompareTests(unittest.TestCase):
    def test_only_known_red_passes(self):
        record = M.compare(result(("A.Old", "Failed", "x"), ("C.Skip", "Skipped", "")), KNOWN)
        self.assertEqual(record["verdict"], V.PASS)
        self.assertEqual(record["stillRed"], ["A.Old"])
        self.assertEqual(record["fixed"], ["B.Flaky"])
        self.assertTrue(record["notes"])

    def test_new_red_fails_with_first_message_line(self):
        record = M.compare(result(("A.Old", "Failed", "x"), ("N.New", "Failed:Error", "  boom\nstack")), KNOWN)
        self.assertEqual(record["verdict"], V.FAIL)
        self.assertEqual([r["name"] for r in record["newRed"]], ["N.New"])
        self.assertIn("новый красный: N.New — boom", record["reasons"])

    def test_warn_inconclusive_is_warning_note_and_keeps_pass(self):
        record = M.compare(result(("A.Old", "Failed", "x"),
                                  ("L.Map", "Inconclusive", "[Warn:lighting] Assets/Scenes/Maps/M.unity: 2\nдетали"),
                                  ("E.Env", "Inconclusive", "[TestEnvironment] нет Quest")), KNOWN)
        self.assertEqual(record["verdict"], V.PASS)
        self.assertEqual([w["name"] for w in record["warnings"]], ["L.Map"])
        self.assertIn("предупреждение: L.Map — [Warn:lighting] Assets/Scenes/Maps/M.unity: 2", record["notes"])
        self.assertTrue(any(n.startswith("Inconclusive без маркера (1): E.Env") for n in record["notes"]))

    def test_skipped_and_inconclusive_are_not_red(self):
        self.assertFalse(M.is_red("Skipped"))
        self.assertFalse(M.is_red("Inconclusive"))
        self.assertTrue(M.is_red("Failed:Cancelled"))

    def test_missing_or_inconsistent_result_is_inconclusive(self):
        self.assertEqual(M.compare(None, KNOWN)["verdict"], V.INCONCLUSIVE)
        # results=null: get_test_job без include_failed_tests — красных не видно, вердикта нет.
        self.assertEqual(M.compare({"summary": {"total": 10, "failed": 1}, "results": None}, KNOWN)["verdict"],
                         V.INCONCLUSIVE)
        self.assertEqual(M.compare(result(("A.Old", "Failed", ""), failed=2), KNOWN)["verdict"], V.INCONCLUSIVE)

    def test_candidate_keeps_reasons_of_known_entries(self):
        record = M.compare(result(("A.Old", "Failed", ""), ("N.New", "Failed", "")), KNOWN)
        candidate = M.candidate_known_red(record, KNOWN, "abcdef1234567890")
        self.assertEqual(candidate["tests"], [{"name": "A.Old", "reason": "карты"},
                                              {"name": "N.New", "reason": "красный на abcdef123456"}])


if __name__ == "__main__":
    unittest.main()
