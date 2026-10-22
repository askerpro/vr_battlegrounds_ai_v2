"""Разбор набора heavy: python -m unittest discover -s Tools/e2e/heavy/tests -t Tools/e2e (из корня репозитория)."""
import copy
import unittest

from heavy import suite as S

VALID = {
    "version": 1,
    "defaults": {"map": "Assets/Scenes/Maps/TestMap2.unity", "mode": "elimination",
                 "readyTimeoutSeconds": 180, "scenarioTimeoutSeconds": 240},
    "scenarios": [
        {"name": "scripted-input-smoke", "clients": 1, "profiles": ["editor"]},
        {"name": "scripted-wall-purchase", "clients": 2, "profiles": ["editor", "windows"],
         "scenarioTimeoutSeconds": 300},
    ],
}


def parse(change=None):
    data = copy.deepcopy(VALID)
    if change:
        change(data)
    return S.parse(data, "test")


class SuiteTests(unittest.TestCase):
    def test_default_suite_has_accepted_scenarios_with_topology(self):
        suite = S.load("default")
        chosen = {s.name: s.topology for s in S.select(suite, "editor")}
        self.assertEqual(chosen, {"scripted-input-smoke": "S+1", "scripted-wall-purchase": "S+2",
                                  "scripted-combat-hit": "S+2", "scripted-combat-round": "S+2",
                                  "scripted-combat-next-round": "S+2", "scripted-admin-pause": "S+2",
                                  "scripted-series-next-map": "S+2", "scripted-series-stop": "S+2"})

    def test_defaults_apply_and_scenario_overrides(self):
        smoke, purchase = parse().scenarios
        self.assertEqual(smoke.scenario_timeout, 240)
        self.assertEqual(purchase.scenario_timeout, 300)
        self.assertEqual(purchase.scene_name, "TestMap2")

    def test_unknown_field_is_error(self):
        with self.assertRaisesRegex(S.SuiteError, "неизвестные поля clientz"):
            parse(lambda d: d["scenarios"][0].update(clientz=1))

    def test_duplicate_scenario_is_error(self):
        with self.assertRaisesRegex(S.SuiteError, "повторяется"):
            parse(lambda d: d["scenarios"].append(dict(d["scenarios"][0])))

    def test_clients_out_of_range_is_error(self):
        with self.assertRaisesRegex(S.SuiteError, "clients"):
            parse(lambda d: d["scenarios"][0].update(clients=0))

    def test_missing_map_without_default_is_error(self):
        with self.assertRaisesRegex(S.SuiteError, "map"):
            parse(lambda d: d["defaults"].pop("map"))

    def test_map_must_be_scene_path(self):
        with self.assertRaisesRegex(S.SuiteError, "Assets/"):
            parse(lambda d: d["defaults"].update(map="TestMap2"))

    def test_unknown_profile_is_error(self):
        with self.assertRaisesRegex(S.SuiteError, "profiles"):
            parse(lambda d: d["scenarios"][0].update(profiles=["vr"]))

    def test_select_filters_by_profile_and_names(self):
        suite = parse()
        self.assertEqual([s.name for s in S.select(suite, "windows")], ["scripted-wall-purchase"])
        self.assertEqual([s.name for s in S.select(suite, "editor", ["scripted-wall-purchase"])],
                         ["scripted-wall-purchase"])
        with self.assertRaisesRegex(S.SuiteError, "Нет в наборе"):
            S.select(suite, "windows", ["scripted-input-smoke"])
        with self.assertRaisesRegex(S.SuiteError, "нет сценариев"):
            S.select(suite, "quest")


if __name__ == "__main__":
    unittest.main()
