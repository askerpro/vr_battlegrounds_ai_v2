"""Набор heavy-регрессии: сценарии и их топология, файл Tools/e2e/suites/<имя>.json."""
from dataclasses import dataclass
import json
from pathlib import Path
import re

SUITES_DIR = Path(__file__).resolve().parents[1] / "suites"
PROFILES = ("editor", "windows", "quest")
NAME = re.compile(r"^[a-z0-9][a-z0-9-]*$")
SUITE_KEYS = {"version", "description", "defaults", "scenarios"}
SCENARIO_KEYS = {"name", "clients", "profiles", "map", "mode", "readyTimeoutSeconds", "scenarioTimeoutSeconds",
                 "args", "lateClientDelaySeconds"}
DEFAULT_KEYS = SCENARIO_KEYS - {"name", "clients", "profiles", "args", "lateClientDelaySeconds"}
# Параметры -e2eArg уходят в Run-E2E одной строкой k=v,k=v: без пробелов, запятых, = и кавычек.
ARG_TOKEN = re.compile(r"^[A-Za-z0-9_.:/-]+$")


class SuiteError(ValueError):
    """Набор не прочитан: битый JSON, неизвестное поле, неверное значение."""


@dataclass(frozen=True)
class Scenario:
    name: str
    clients: int
    profiles: tuple
    map: str
    mode: str
    ready_timeout: int
    scenario_timeout: int
    args: tuple = ()
    late_client_delay: int = 0

    @property
    def topology(self):
        return "S+" + str(self.clients)

    @property
    def scene_name(self):
        return Path(self.map).stem


@dataclass(frozen=True)
class Suite:
    name: str
    path: str
    description: str
    scenarios: tuple


def resolve_path(name_or_path):
    """Имя набора — файл в Tools/e2e/suites; путь с .json — как есть."""
    if name_or_path.endswith(".json") or "/" in name_or_path or "\\" in name_or_path:
        return Path(name_or_path).resolve()
    if not NAME.match(name_or_path):
        raise SuiteError("Имя набора '" + name_or_path + "': только a-z, 0-9 и дефис")
    return SUITES_DIR / (name_or_path + ".json")


def load(name_or_path):
    path = resolve_path(name_or_path)
    if not path.is_file():
        known = sorted(p.stem for p in SUITES_DIR.glob("*.json"))
        raise SuiteError("Набор не найден: " + str(path) + ". Есть: " + ", ".join(known))
    try:
        data = json.loads(path.read_text(encoding="utf-8"))
    except ValueError as error:
        raise SuiteError(path.name + ": битый JSON: " + str(error)) from None
    return parse(data, path.stem, str(path))


def _integer(where, value, low, high):
    if not isinstance(value, int) or isinstance(value, bool) or not low <= value <= high:
        raise SuiteError(where + ": нужно целое от " + str(low) + " до " + str(high) + ", получено " + json.dumps(value))
    return value


def _text(where, value):
    if not isinstance(value, str) or not value.strip():
        raise SuiteError(where + ": нужна непустая строка")
    return value


def _unknown(where, item, allowed):
    extra = sorted(set(item) - allowed)
    if extra:
        raise SuiteError(where + ": неизвестные поля " + ", ".join(extra) + " (допустимо: " + ", ".join(sorted(allowed)) + ")")


def _settings(where, item):
    result = {}
    if "map" in item:
        result["map"] = _text(where + ".map", item["map"])
        if not (result["map"].startswith("Assets/") and result["map"].endswith(".unity")):
            raise SuiteError(where + ".map: нужен путь сцены Assets/....unity")
    if "mode" in item:
        result["mode"] = _text(where + ".mode", item["mode"])
    if "readyTimeoutSeconds" in item:
        result["ready_timeout"] = _integer(where + ".readyTimeoutSeconds", item["readyTimeoutSeconds"], 30, 600)
    if "scenarioTimeoutSeconds" in item:
        # Предел протокола стенда StandRequest.ScenarioTimeoutSeconds — 1..3600.
        result["scenario_timeout"] = _integer(where + ".scenarioTimeoutSeconds", item["scenarioTimeoutSeconds"], 10, 3600)
    return result


def parse(data, name, path=""):
    if not isinstance(data, dict):
        raise SuiteError(name + ": корень набора — объект")
    _unknown(name, data, SUITE_KEYS)
    if data.get("version") != 1:
        raise SuiteError(name + ": поддерживается version 1")
    defaults = data.get("defaults", {})
    if not isinstance(defaults, dict):
        raise SuiteError(name + ".defaults: нужен объект")
    _unknown(name + ".defaults", defaults, DEFAULT_KEYS)
    base = _settings(name + ".defaults", defaults)
    items = data.get("scenarios")
    if not isinstance(items, list) or not items:
        raise SuiteError(name + ".scenarios: нужен непустой список")
    scenarios, seen = [], set()
    for index, item in enumerate(items):
        where = name + ".scenarios[" + str(index) + "]"
        if not isinstance(item, dict):
            raise SuiteError(where + ": нужен объект")
        _unknown(where, item, SCENARIO_KEYS)
        scenario_name = _text(where + ".name", item.get("name"))
        if not NAME.match(scenario_name):
            raise SuiteError(where + ".name: только a-z, 0-9 и дефис")
        if scenario_name in seen:
            raise SuiteError(where + ": сценарий '" + scenario_name + "' повторяется")
        seen.add(scenario_name)
        profiles = item.get("profiles", list(PROFILES))
        if not isinstance(profiles, list) or not profiles or any(p not in PROFILES for p in profiles):
            raise SuiteError(where + ".profiles: непустой список из " + ", ".join(PROFILES))
        settings = dict(base)
        settings.update(_settings(where, item))
        missing = [k for k in ("map", "mode", "ready_timeout", "scenario_timeout") if k not in settings]
        if missing:
            raise SuiteError(where + ": не заданы ни в сценарии, ни в defaults: " + ", ".join(missing))
        args = item.get("args", {})
        if not isinstance(args, dict) or any(not isinstance(k, str) or not isinstance(v, str) or not ARG_TOKEN.match(k)
                                             or not ARG_TOKEN.match(v) for k, v in args.items()):
            raise SuiteError(where + ".args: объект строк ключ→значение из A-Z a-z 0-9 _ . : / -")
        late = _integer(where + ".lateClientDelaySeconds", item.get("lateClientDelaySeconds", 0), 0, 600)
        scenarios.append(Scenario(name=scenario_name,
                                  clients=_integer(where + ".clients", item.get("clients"), 1, 16),
                                  profiles=tuple(profiles), args=tuple(sorted(args.items())),
                                  late_client_delay=late, **settings))
    return Suite(name=name, path=path, description=str(data.get("description", "")), scenarios=tuple(scenarios))


def select(suite, profile, only=None):
    """Сценарии набора для профиля; only — явный список имён (неизвестное имя — ошибка)."""
    if profile not in PROFILES:
        raise SuiteError("Профиль '" + profile + "': допустимо " + ", ".join(PROFILES))
    chosen = [s for s in suite.scenarios if profile in s.profiles]
    if only:
        names = {s.name for s in chosen}
        unknown = [n for n in only if n not in names]
        if unknown:
            raise SuiteError("Нет в наборе " + suite.name + " для профиля " + profile + ": " + ", ".join(unknown))
        chosen = [s for s in chosen if s.name in only]
    if not chosen:
        raise SuiteError("В наборе " + suite.name + " нет сценариев для профиля " + profile)
    return chosen
