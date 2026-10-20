"""Per-draw GPU-счётчики Quest через Meta RenderDoc Fork (renderdoccmd) со снимком и откатом.

Порядок: preflight-снимок состояния шлема (атомарный журнал до любых изменений) →
adb-launch с инъекцией → adb-capture одного кадра в новую пустую папку → остановка
приложения → adb-session --drawcall-profiling (fetch_drawcall_states, draws) → откат.
Откат меняет только ключи, которые изменились относительно чистого снимка и имеют
известное вендорское значение; чужие изменения не трогаются и отмечаются в отчёте.
После отката снимок снимается повторно и сравнивается с исходным.

Запуск требует явного разрешения пользователя на изменения шлема. Перф-метрики
прогона с инъекцией недействительны; счётчики — replay захваченного кадра на шлеме.
"""
import argparse
import hashlib
import json
import os
import re
import shlex
import subprocess
import sys
import time
from datetime import datetime, timezone
from pathlib import Path

SERVER_PACKAGES = ("com.oculus.renderdoccmd.arm64", "com.oculus.renderdoccmd.arm32")
GLOBAL_SETTINGS = ("enable_gpu_debug_layers", "gpu_debug_app", "gpu_debug_layer_app",
                   "gpu_debug_layers", "gpu_debug_layers_gles")
PROPS = ("security.perf_harden", "debug.vulkan.layers", "debug.rdoc.IGNORE_LAYERS",
         "debug.rdoc.RENDERDOC_CAPOPTS", "debug.rdoc.QUEUECAP_FRAME", "debug.rdoc.QUEUECAP_NUMFRAMES",
         "debug.oculus.guardian_pause", "debug.vr.profiler",
         "debug.oculus.usepackagedvvl.com.oculus.renderdoccmd.arm32",
         "debug.oculus.usepackagedvvl.com.oculus.renderdoccmd.arm64")
# Никогда не должен меняться: ветка JDWP требует root+reboot и запрещена.
FORBIDDEN_PROPS = ("persist.debug.dalvik.vm.jdwp.enabled",)
KGSL = ("/sys/class/kgsl/kgsl-3d0/devfreq/min_freq", "/sys/class/kgsl/kgsl-3d0/devfreq/max_freq")
PROFILER = "/system_ext/bin/ovrgpuprofiler"
FORWARD_RE = re.compile(r"^(\S+)\s+(tcp:\d+)\s+(localabstract:renderdoc_\d+)$")
DEFAULT_COUNTERS = ("Clocks,Shaders Busy,Texture Fetch Stall,Vertex Fetch Stall,"
                    "LRZ Prim Killed,LRZ Busy,PS Invocations")


def vendor_setting_value(key, value, package):
    """Известные значения, которые пишет renderdoc.dll (строки бинарника 68.18)."""
    if value is None:
        return False
    known = {
        "enable_gpu_debug_layers": {"1"},
        "gpu_debug_app": {package, *SERVER_PACKAGES},
        "gpu_debug_layer_app": {package, *SERVER_PACKAGES},
        "gpu_debug_layers": {"VK_LAYER_RENDERDOC_Capture"},
        "gpu_debug_layers_gles": {"libVkLayer_GLES_RenderDoc.so"},
    }
    return value in known[key]


def vendor_prop_value(key, value):
    if key.startswith("debug.rdoc."):
        return True
    known = {
        "security.perf_harden": {"0", "1"},
        "debug.vulkan.layers": {":", "VK_LAYER_RENDERDOC_Capture"},
        "debug.oculus.guardian_pause": {"1"},
        "debug.vr.profiler": {"1"},
    }
    if key.startswith("debug.oculus.usepackagedvvl."):
        return value in {"0", "1"}
    return value in known.get(key, set())


class Device:
    def __init__(self, adb, serial, log):
        self.adb, self.serial, self.log = adb, serial, log

    def raw(self, args, timeout=60, ok=(0,)):
        argv = [self.adb, "-s", self.serial] + args
        started = time.time()
        r = subprocess.run(argv, capture_output=True, text=True, encoding="utf-8",
                           errors="replace", timeout=timeout)
        out = (r.stdout or "").replace("\r", "")
        err = (r.stderr or "").replace("\r", "")
        self.log({"argv": argv, "exit": r.returncode, "seconds": round(time.time() - started, 3),
                  "stdout": out[-2000:], "stderr": err[-2000:]})
        if r.returncode not in ok:
            raise RuntimeError("adb %s: exit %d: %s" % (" ".join(args[:3]), r.returncode, err.strip()[:300]))
        return out

    def shell(self, command, timeout=60, ok=(0,)):
        return self.raw(["shell", command], timeout=timeout, ok=ok)

    def setting(self, key):
        v = self.shell("settings get global " + key).strip()
        return None if v == "null" else v

    def prop(self, key):
        return self.shell("getprop " + key).strip()

    def package_path(self, pkg):
        # pm path: exit 1 и пустой вывод, если пакета нет; проверяем по содержимому.
        out = self.shell("pm path " + pkg, ok=(0, 1)).strip()
        return out or None

    def pid(self, pkg):
        return self.shell("pidof " + pkg, ok=(0, 1)).strip() or None

    def forwards(self):
        out = self.raw(["forward", "--list"])
        rows = []
        for line in out.splitlines():
            parts = line.split()
            if len(parts) == 3 and parts[0] == self.serial:
                rows.append({"local": parts[1], "remote": parts[2]})
        return rows


def snapshot(dev, package):
    snap = {"settings": {k: dev.setting(k) for k in GLOBAL_SETTINGS},
            "props": {k: dev.prop(k) for k in PROPS + FORBIDDEN_PROPS},
            "packages": {p: dev.package_path(p) for p in SERVER_PACKAGES},
            "appPid": dev.pid(package),
            "serverPids": {p: dev.pid(p) for p in SERVER_PACKAGES},
            "detailed": None, "forwards": dev.forwards(), "kgsl": {}}
    mode = dev.shell(PROFILER + " -i").strip()
    if "is enabled" in mode:
        snap["detailed"] = True
    elif "is disabled" in mode:
        snap["detailed"] = False
    else:
        raise RuntimeError("непонятный ответ ovrgpuprofiler -i: %r" % mode[:200])
    for path in KGSL:
        out = dev.shell("cat " + path + " 2>&1; echo \"<exit $?>\"").strip()
        snap["kgsl"][path] = out
    return snap


def clean_preconditions(snap):
    problems = []
    for k, v in snap["settings"].items():
        if v is not None:
            problems.append("global %s=%r уже задан" % (k, v))
    for p, path in snap["packages"].items():
        if path:
            problems.append("сервер %s уже установлен" % p)
    if snap["appPid"]:
        problems.append("приложение уже запущено")
    if snap["detailed"]:
        problems.append("detailed GPU profiling уже включён")
    if snap["forwards"]:
        problems.append("у устройства уже есть adb forward: %s" % snap["forwards"])
    if snap["props"]["persist.debug.dalvik.vm.jdwp.enabled"] not in ("", "0"):
        problems.append("persist JDWP включён")
    return problems


class Journal:
    def __init__(self, path):
        self.path = Path(path)
        self.data = {"schemaVersion": 1, "kind": "vrb.meta-renderdoc-quest-run",
                     "created": datetime.now(timezone.utc).isoformat(), "state": "new",
                     "steps": [], "commands": [], "reasons": [], "restore": {"actions": [], "foreign": []}}

    def save(self):
        tmp = self.path.with_name(self.path.name + ".tmp")
        with tmp.open("w", encoding="utf-8", newline="\n") as f:
            f.write(json.dumps(self.data, ensure_ascii=False, indent=1) + "\n")
            f.flush()
            os.fsync(f.fileno())
        os.replace(tmp, self.path)

    def command(self, entry):
        self.data["commands"].append(entry)

    def step(self, name, **info):
        self.data["steps"].append({"step": name, "at": datetime.now(timezone.utc).isoformat(), **info})
        self.data["state"] = name
        self.save()


def run_tool(argv, env, timeout, journal, stdin_text=None, name=None):
    """renderdoccmd с таймаутом. Вывод идёт в файлы, а не в pipe: adb-сервер, который
    renderdoccmd может запустить, наследует дескрипторы, и pipe тогда не закрывается.
    По таймауту убивается только сам renderdoccmd (без /T), чтобы не задеть adb-сервер."""
    started = time.time()
    logs = journal.path.parent / "tool-logs"
    logs.mkdir(exist_ok=True)
    index = sum(1 for c in journal.data["commands"] if c.get("name"))
    paths = {k: logs / ("%02d-%s.%s.txt" % (index, name, k)) for k in ("stdin", "stdout", "stderr")}
    if stdin_text is not None:
        paths["stdin"].write_text(stdin_text, encoding="utf-8", newline="\n")
    timed_out, kill_exit = False, None
    with paths["stdout"].open("wb") as so_f, paths["stderr"].open("wb") as se_f, \
            (paths["stdin"].open("rb") if stdin_text is not None else open(os.devnull, "rb")) as si_f:
        proc = subprocess.Popen(argv, stdin=si_f, stdout=so_f, stderr=se_f, env=env)
        try:
            proc.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            timed_out = True
            kill_exit = subprocess.run(["taskkill", "/F", "/PID", str(proc.pid)], capture_output=True).returncode
            try:
                proc.wait(timeout=30)
            except subprocess.TimeoutExpired:
                pass
    out = paths["stdout"].read_text(encoding="utf-8", errors="replace").replace("\r", "")
    err = paths["stderr"].read_text(encoding="utf-8", errors="replace").replace("\r", "")
    entry = {"name": name, "argv": argv, "exit": proc.returncode, "timedOut": timed_out,
             "taskkillExit": kill_exit, "seconds": round(time.time() - started, 3),
             "stdoutTail": out[-3000:], "stderrTail": err[-3000:]}
    for kind, path in paths.items():
        if path.exists():
            entry[kind + "File"] = path.relative_to(journal.path.parent).as_posix()
    journal.command(entry)
    journal.save()
    if timed_out:
        raise RuntimeError("%s: таймаут %d с" % (name, timeout))
    return proc.returncode, out, err


SESSION_BUDGET = 720  # общий дедлайн сессии, с: finish укладывается в бюджет вызывающего (Run-QuestE2E)


def run_session(argv, env, commands, journal, init_timeout=300, command_timeout=600, budget=SESSION_BUDGET):
    """Интерактивный adb-session: ждём строку готовности, затем команда → одна строка ответа.
    stderr идёт в файл; stdout читает поток (EOF не нужен: adb-сервер может держать pipe).
    Все ожидания ограничены общим дедлайном budget."""
    session_deadline = time.time() + budget
    import queue
    import threading
    logs = journal.path.parent / "tool-logs"
    logs.mkdir(exist_ok=True)
    index = sum(1 for c in journal.data["commands"] if c.get("name"))
    err_path = logs / ("%02d-adb-session.stderr.txt" % index)
    replies_path = logs / ("%02d-adb-session.replies.jsonl" % index)
    started = time.time()
    replies, reasons = [], []
    with err_path.open("wb") as se_f:
        proc = subprocess.Popen(argv, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=se_f, env=env)
        lines = queue.Queue()

        def reader():
            for raw in proc.stdout:
                lines.put(raw.decode("utf-8", errors="replace").strip())

        threading.Thread(target=reader, daemon=True).start()

        def next_json(timeout):
            deadline = min(time.time() + timeout, session_deadline)
            while time.time() < deadline:
                try:
                    line = lines.get(timeout=min(5.0, max(0.1, deadline - time.time())))
                except queue.Empty:
                    if proc.poll() is not None and lines.empty():
                        raise RuntimeError("adb-session завершился (exit %s)" % proc.returncode)
                    continue
                if line.startswith("{"):
                    # Как вендорский клиент: NaN/Inf счётчиков → 0, иначе json не разберёт.
                    line = re.sub(r":\s*-?[Nn][Aa][Nn]\b", ": 0", line)
                    line = re.sub(r":\s*-?[Ii]nf(?:inity)?\b", ": 0", line)
                    return json.loads(line)
            raise RuntimeError("adb-session: нет ответа за %d с" % timeout)

        try:
            replies.append({"request": "init", "reply": next_json(init_timeout)})
            for cmd in commands:
                proc.stdin.write((json.dumps(cmd) + "\n").encode("utf-8"))
                proc.stdin.flush()
                if cmd.get("cmd") == "quit":
                    break
                replies.append({"request": cmd, "reply": next_json(command_timeout)})
        except (RuntimeError, ValueError, OSError) as exc:
            reasons.append(str(exc))
        finally:
            try:
                proc.wait(timeout=30)
            except subprocess.TimeoutExpired:
                subprocess.run(["taskkill", "/F", "/PID", str(proc.pid)], capture_output=True)
                reasons.append("adb-session не завершился после quit — остановлен")
    with replies_path.open("w", encoding="utf-8", newline="\n") as f:
        for r in replies:
            f.write(json.dumps(r, ensure_ascii=False) + "\n")
    journal.command({"name": "adb-session", "argv": argv, "exit": proc.returncode,
                     "seconds": round(time.time() - started, 3), "reasons": reasons,
                     "stderrFile": err_path.relative_to(journal.path.parent).as_posix(),
                     "repliesFile": replies_path.relative_to(journal.path.parent).as_posix()})
    journal.save()
    return proc.returncode, replies, reasons


def last_json(text):
    for line in reversed(text.splitlines()):
        line = line.strip()
        if line.startswith("{") and line.endswith("}"):
            try:
                return json.loads(line)
            except ValueError:
                continue
    return None


RESTORE_ERRORS = (RuntimeError, OSError, ValueError, KeyError, TypeError, AttributeError,
                  subprocess.SubprocessError)


def restore(dev, pre, package, journal):
    """Вернуть шлем к снимку pre. Каждое чтение/действие изолировано: сбой одного
    ключа не отменяет остальные. Возвращает список нерешённых расхождений."""
    prior = journal.data.get("restore") or {}
    history = prior.pop("history", [])
    if any(prior.get(k) for k in ("actions", "foreign", "errors")):
        history.append(prior)
    rs = journal.data["restore"] = {"history": history, "actions": [], "foreign": [], "errors": []}
    actions, foreign, errors = rs["actions"], rs["foreign"], rs["errors"]

    def guarded(name, fn):
        """Изолировать чтение одного ключа: ошибка записывается, цикл продолжается."""
        try:
            return True, fn()
        except RESTORE_ERRORS as exc:
            errors.append({"read": name, "error": str(exc)})
            journal.save()
            return False, None

    def act(desc, fn):
        try:
            fn()
            actions.append({"action": desc, "ok": True})
        except RESTORE_ERRORS as exc:
            actions.append({"action": desc, "ok": False, "error": str(exc)})
        journal.save()

    def section(name, fn):
        try:
            fn()
        except RESTORE_ERRORS as exc:
            errors.append({"section": name, "error": str(exc)})
            journal.save()

    def stop_processes():
        # pre чистый (приложение и сервер не запущены), поэтому их процессы созданы этим прогоном.
        for pkg in (package,) + SERVER_PACKAGES:
            if not (pre["appPid"] if pkg == package else pre["serverPids"].get(pkg)):
                act("force-stop " + pkg, lambda p=pkg: dev.shell("am force-stop " + p))

    def transient():
        # Переходные эффекты без наблюдаемого состояния: компоновщик отменяется штатным -1,
        # prox_close отменить нечем — истекает сам (duration), отражается в отчёте.
        act("compositor skip cancel", lambda: dev.shell(
            "am broadcast -a com.oculus.vrruntimeservice.COMPOSITOR_SKIP_RENDERING --ei milliseconds -1"))
        # Штатная отмена prox_close, как в finally Run-QuestE2E.
        act("automation_disable", lambda: dev.shell("am broadcast -a com.oculus.vrpowermanager.automation_disable"))
        rs["unrestorableTransient"] = ["prox_close вендора с duration (сброшен automation_disable)",
                                       "oom_score_adj процессов renderdoc (живёт до их завершения)"]

    def uninstall():
        for pkg in SERVER_PACKAGES:
            ok, present = guarded("pm path " + pkg, lambda p=pkg: dev.package_path(p))
            if ok and present and not pre["packages"][pkg]:
                act("uninstall " + pkg, lambda p=pkg: dev.raw(["uninstall", p], timeout=120))

    def detailed():
        if not pre["detailed"] and "is enabled" in dev.shell(PROFILER + " -i"):
            act("ovrgpuprofiler -d", lambda: dev.shell(PROFILER + " -d"))

    def settings():
        for key in GLOBAL_SETTINGS:
            ok, now = guarded("setting " + key, lambda k=key: dev.setting(k))
            if not ok or now == pre["settings"][key]:
                continue
            if pre["settings"][key] is None:
                # Предусловия требуют пустые gpu_debug_* до прогона: любое новое значение удаляется;
                # неизвестное вендорское значение дополнительно фиксируется для аудита.
                if not vendor_setting_value(key, now, package):
                    rs.setdefault("unexpectedValuesDeleted", []).append({"setting": key, "now": now})
                act("settings delete global " + key, lambda k=key: dev.shell("settings delete global " + k))
            else:
                foreign.append({"setting": key, "pre": pre["settings"][key], "now": now})

    def props():
        for key in PROPS:
            ok, now = guarded("prop " + key, lambda k=key: dev.prop(k))
            if not ok or now == pre["props"][key]:
                continue
            if vendor_prop_value(key, now):
                # Строка исполняется POSIX-shell устройства; пустое значение передаётся как ''.
                value = shlex.quote(pre["props"][key])
                act("setprop %s %s" % (key, value), lambda k=key, v=value: dev.shell("setprop %s %s" % (k, v)))
            else:
                foreign.append({"prop": key, "pre": pre["props"][key], "now": now})

    def forwards():
        pre_locals = {f["local"] for f in pre["forwards"]}
        ok, current = guarded("forward --list", dev.forwards)
        for f in current if ok else []:
            if f["local"] in pre_locals:
                continue
            if FORWARD_RE.match("%s %s %s" % (dev.serial, f["local"], f["remote"])):
                act("forward --remove " + f["local"], lambda l=f["local"]: dev.raw(["forward", "--remove", l]))
            else:
                foreign.append({"forward": f})

    for name, fn in (("processes", stop_processes), ("transient", transient), ("uninstall", uninstall),
                     ("detailed", detailed), ("settings", settings), ("props", props), ("forwards", forwards)):
        section(name, fn)

    diffs, info = [], []
    try:
        post = snapshot(dev, package)
    except RESTORE_ERRORS as exc:
        post = None
        diffs.append({"section": "post-snapshot", "error": str(exc)})
    journal.data["post"] = post
    if post is not None:
        for sec in ("settings", "props", "packages"):
            for k, v in pre[sec].items():
                if post[sec].get(k) != v:
                    diffs.append({"section": sec, "key": k, "pre": v, "post": post[sec].get(k)})
        for k, v in pre["kgsl"].items():
            # Частоты меняет и тепловой троттлинг; без root запись вендора невозможна — информационно.
            if post["kgsl"].get(k) != v:
                info.append({"section": "kgsl", "key": k, "pre": v, "post": post["kgsl"].get(k)})
        for k in ("appPid", "detailed"):
            if bool(post[k]) != bool(pre[k]):
                diffs.append({"section": k, "pre": pre[k], "post": post[k]})
        for pkg, pid in pre["serverPids"].items():
            if bool(post["serverPids"].get(pkg)) != bool(pid):
                diffs.append({"section": "serverPids", "key": pkg, "pre": pid, "post": post["serverPids"].get(pkg)})
        if sorted(f["local"] for f in post["forwards"]) != sorted(f["local"] for f in pre["forwards"]):
            diffs.append({"section": "forwards", "pre": pre["forwards"], "post": post["forwards"]})
    rs["remainingDiffs"] = diffs
    rs["informational"] = info
    rs["knownResidue"] = ["getprop не различает отсутствующее и пустое свойство: после setprop key '' "
                          "ранее отсутствовавшее debug.* свойство остаётся пустым до перезагрузки"]
    rs["verified"] = not diffs and not errors and not foreign
    journal.save()
    return diffs


def full_state(dev):
    """Полные getprop и settings global — для отчёта об изменениях вне отслеживаемых ключей."""
    return {"getprop": dev.shell("getprop", timeout=60), "global": dev.shell("settings list global", timeout=60)}


def state_lines_diff(before, after):
    out = {}
    for key in ("getprop", "global"):
        a, b = set(before[key].splitlines()), set(after[key].splitlines())
        out[key] = {"removed": sorted(a - b), "added": sorted(b - a)}
    return out


def adb_server_pids():
    """PID процесса, слушающего порт adb-сервера 5037 (клиенты adb.exe не учитываются)."""
    r = subprocess.run(["netstat", "-ano", "-p", "TCP"], capture_output=True, text=True, errors="replace")
    pids = set()
    for line in r.stdout.splitlines():
        parts = line.split()
        # Слушающий сокет узнаётся по пустому удалённому адресу, а не по локализованному слову состояния.
        if len(parts) >= 5 and parts[1].endswith(":5037") and parts[2] in ("0.0.0.0:0", "[::]:0"):
            pids.add(parts[4])
    return sorted(pids)


def tool_env(adb):
    adb = Path(adb)
    if adb.parent.name != "platform-tools":
        raise RuntimeError("--adb должен лежать в <sdk>/platform-tools: renderdoccmd ищет adb через SDK root")
    env = dict(os.environ)
    env["PATH"] = str(adb.parent) + os.pathsep + env.get("PATH", "")
    for key in ("ANDROID_HOME", "ANDROID_SDK_ROOT", "ANDROID_SDK"):
        env[key] = str(adb.parent.parent)
    return env


def emit(payload):
    try:
        print(json.dumps(payload, ensure_ascii=False), flush=True)
    except OSError:
        pass


def load_journal(journal_path):
    journal = Journal(journal_path)
    journal.data = json.loads(journal_path.read_text(encoding="utf-8"))
    journal.data.setdefault("restore", {"actions": [], "foreign": []})
    journal.data.setdefault("result", {"captured": None, "session": None})
    return journal


def has_step(journal, name):
    return any(s.get("step") == name for s in journal.data.get("steps", []))


def do_restore(args, journal):
    """Откат после собственных изменений (шаг launching). Ошибки не выходят наружу."""
    dev = Device(args.adb, args.serial, journal.command)
    diffs = None
    if not has_step(journal, "launching") or not journal.data.get("pre"):
        journal.data["restore"]["verified"] = True
        journal.data["restore"]["skipped"] = "renderdoccmd не запускался — шлем не менялся"
        return diffs
    try:
        diffs = restore(dev, journal.data["pre"], args.package, journal)
    except RESTORE_ERRORS as exc:
        journal.data["reasons"].append("restore: %s" % exc)
        journal.data["restore"]["verified"] = False
    if journal.data.get("preFull"):
        try:
            journal.data["restore"]["untrackedStateDiff"] = state_lines_diff(journal.data["preFull"], full_state(dev))
        except RESTORE_ERRORS as exc:
            journal.data["restore"]["untrackedStateDiff"] = {"error": str(exc)}
    # Решение пользователя 2026-10-20: launch будил шлем — после отката вернуть его в сон (не разряжать зря).
    # Вне сверки снимка: сон не входит в отслеживаемое состояние; повторный SLEEP безвреден.
    try:
        dev.shell("input keyevent KEYCODE_SLEEP")
        journal.data["restore"]["sleepSent"] = True
    except RESTORE_ERRORS as exc:
        journal.data["restore"]["sleepSent"] = "error: %s" % exc
    try:
        journal.data["adbServerPidsAfter"] = adb_server_pids()
        journal.data["adbServerRestarted"] = (journal.data.get("adbServerPidsBefore") is not None and
                                              journal.data["adbServerPidsAfter"] != journal.data["adbServerPidsBefore"])
    except OSError as exc:
        journal.data["adbServerPidsAfter"] = {"error": str(exc)}
    return diffs


def summary(journal, diffs):
    rs = journal.data["restore"]
    return {"state": journal.data.get("state"), "result": journal.data.get("result"),
            "reasons": journal.data["reasons"], "restoreVerified": rs.get("verified"),
            "restoreErrors": rs.get("errors"), "foreign": rs.get("foreign"), "remainingDiffs": diffs,
            "adbServerRestarted": journal.data.get("adbServerRestarted"),
            "untrackedStateDiff": {k: {"added": len(v.get("added", [])), "removed": len(v.get("removed", []))}
                                   for k, v in (rs.get("untrackedStateDiff") or {}).items() if isinstance(v, dict)},
            "journal": str(journal.path)}


def phase_launch(args, out):
    """Снимок + предусловия + adb-launch. При сбое — сразу откат; при успехе шлем остаётся
    с инъекцией до фазы finish (её обязан вызвать владелец прогона)."""
    if out.exists() and any(out.iterdir()):
        raise SystemExit("папка отчёта должна быть новой или пустой: %s" % out)
    (out / "captures").mkdir(parents=True, exist_ok=True)
    journal = Journal(out / "journal.json")
    journal.data["result"] = {"captured": None, "session": None}
    journal.data["args"] = {"package": args.package, "activity": args.activity, "serial": args.serial,
                            "intentArgs": args.intent_args}
    dev = Device(args.adb, args.serial, journal.command)
    try:
        env = tool_env(args.adb)
        journal.data["adbVersion"] = subprocess.run([args.adb, "version"], capture_output=True,
                                                    text=True).stdout.strip().replace("\r", "")
        rd = Path(args.renderdoccmd)
        journal.data["renderdoccmd"] = {"path": str(rd), "sha256": hashlib.sha256(rd.read_bytes()).hexdigest()}
        journal.data["preFull"] = full_state(dev)
        pre = snapshot(dev, args.package)
        # Сервер adb уже поднят первыми командами снимка; смена PID далее = перезапуск.
        journal.data["adbServerPidsBefore"] = adb_server_pids()
        journal.data["pre"] = pre
        problems = clean_preconditions(pre)
        journal.step("snapshotted", problems=problems)
        if problems:
            raise RuntimeError("шлем не в чистом состоянии: " + "; ".join(problems))
        launch = [args.renderdoccmd, "adb-launch", "--device", args.serial, "--package", args.package,
                  "--activity", args.activity]
        if args.intent_args:
            launch += ["--intent-args", args.intent_args]
        # Как Run-QuestE2E: без надетого шлема он засыпает и am start не находит процесс.
        journal.step("launching")
        dev.shell("input keyevent KEYCODE_WAKEUP")
        dev.shell("am broadcast -a com.oculus.vrpowermanager.prox_close")
        code, so, se = run_tool(launch, env, 240, journal, name="adb-launch")
        info = last_json(so)
        journal.data["launch"] = info
        ident = (info or {}).get("ident")
        if code != 0 or not ident:
            raise RuntimeError("adb-launch не вернул ident (exit %s)" % code)
        journal.step("launched", ident=ident)
        emit({"launched": True, "ident": ident, "journal": str(journal.path)})
        return 0
    except RESTORE_ERRORS as exc:
        journal.data["reasons"].append(str(exc))
        diffs = do_restore(args, journal)
        journal.step("finished", restoreVerified=journal.data["restore"].get("verified"))
        emit(summary(journal, diffs))
        return 2


def wait_phase(args, journal):
    """Ждать строку фазы perf-stress в logcat приложения (как collector)."""
    import threading
    pattern = re.compile(r"=== фаза «" + re.escape(args.phase) + r"»")
    seen = threading.Event()
    log_path = journal.path.parent / "tool-logs" / "capture-logcat.txt"
    log_path.parent.mkdir(exist_ok=True)
    proc = subprocess.Popen([args.adb, "-s", args.serial, "logcat", "-T", "1", "-v", "brief", "Unity:I", "*:S"],
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT)

    def reader():
        with log_path.open("w", encoding="utf-8", newline="\n") as f:
            for raw in proc.stdout:
                line = raw.decode("utf-8", errors="replace")
                f.write(line)
                if pattern.search(line):
                    seen.set()

    threading.Thread(target=reader, daemon=True).start()
    dev = Device(args.adb, args.serial, lambda entry: None)
    deadline = time.time() + args.max_wait
    try:
        while time.time() < deadline:
            if seen.wait(10):
                return True
            # Приложение завершилось раньше фазы — ждать дальше бессмысленно.
            try:
                if not dev.pid(args.package):
                    return False
            except RESTORE_ERRORS:
                pass
        return False
    finally:
        proc.terminate()
        try:
            proc.wait(timeout=5)
        except subprocess.TimeoutExpired:
            proc.kill()


def do_capture(args, journal):
    env = tool_env(args.adb)
    ident = journal.data.get("launch", {}).get("ident")
    captures_dir = journal.path.parent / "captures"
    code, so, se = run_tool([args.renderdoccmd, "adb-capture", "--device", args.serial,
                             "--ident", str(ident), "--frames", "1", "--output-dir", str(captures_dir)],
                            env, 300, journal, name="adb-capture")
    cap = last_json(so)
    journal.data["capture"] = cap
    paths = []
    for c in (cap or {}).get("captures", []):
        # 68.18 отдаёт список строк-путей (относительно cwd); объектная форма — на будущее.
        raw_path = c if isinstance(c, str) else (c.get("path") or c.get("local_path") or c.get("file") or "")
        path = Path(raw_path)
        if raw_path and path.is_file() and captures_dir.resolve() in path.resolve().parents:
            paths.append(path)
    path_source = "json"
    if not paths and code == 0:
        # Папка новая и принадлежит только этому прогону: единственный .rdc в ней — наш кадр.
        paths = sorted(captures_dir.rglob("*.rdc"))
        path_source = "fresh-dir"
    journal.data["capturePathSource"] = path_source
    if code != 0 or len(paths) != 1:
        raise RuntimeError("adb-capture: ожидался ровно один .rdc в %s, найдено %d (exit %s)"
                           % (captures_dir, len(paths), code))
    rdc = paths[0]
    rdc = rdc.resolve()  # 68.18 даёт путь относительно cwd; у finish cwd может быть другим
    journal.data["result"]["captured"] = {"path": str(rdc), "bytes": rdc.stat().st_size,
                                          "sha256": hashlib.sha256(rdc.read_bytes()).hexdigest()}
    journal.step("captured", **journal.data["result"]["captured"])


def phase_capture(args, out):
    """Фоновая фаза: дождаться фазы сценария и снять один кадр. Шлем не откатывает."""
    journal = load_journal(out / "journal.json")
    try:
        if journal.data.get("state") != "launched":
            raise RuntimeError("захват возможен только после launched (сейчас %s)" % journal.data.get("state"))
        if args.phase:
            journal.step("waiting-phase", phase=args.phase)
            if not wait_phase(args, journal):
                raise RuntimeError("фаза «%s» не наступила за %d с" % (args.phase, args.max_wait))
            journal.step("phase-seen", phase=args.phase)
        time.sleep(args.settle)
        if not Device(args.adb, args.serial, journal.command).pid(args.package):
            raise RuntimeError("приложение не работает к моменту захвата")
        do_capture(args, journal)
        emit({"captured": journal.data["result"]["captured"]})
        return 0
    except RESTORE_ERRORS as exc:
        journal.data["reasons"].append("capture: %s" % exc)
        journal.save()
        emit({"captured": None, "reason": str(exc)})
        return 2


def phase_finish(args, out):
    """Остановить приложение, при наличии кадра — PIL-сессия, затем откат и сверка."""
    journal_path = out / "journal.json"
    if not journal_path.exists():
        # launch не успел создать журнал — значит и снимка/изменений шлема ещё не было.
        emit({"restoreVerified": True, "skipped": "журнала нет: launch не начинал менять шлем"})
        return 2
    try:
        journal = load_journal(journal_path)
    except (OSError, ValueError) as exc:
        emit({"restoreVerified": False, "error": "журнал повреждён, автоматический откат невозможен: %s" % exc,
              "manual": "проверь gpu_debug_*, debug.rdoc.*, debug.vulkan.layers, security.perf_harden, "
                        "пакеты com.oculus.renderdoccmd.*, adb forward renderdoc_*, ovrgpuprofiler -i"})
        return 3
    result = journal.data["result"]
    try:
        if result.get("captured"):
            dev = Device(args.adb, args.serial, journal.command)
            dev.shell("am force-stop " + args.package)
            commands = [{"cmd": "info"},
                        {"cmd": "fetch_drawcall_states", "counters": args.counters, "sample_count": str(args.samples)},
                        {"cmd": "draws"}, {"cmd": "passes"}, {"cmd": "quit"}]
            code, replies, session_reasons = run_session(
                [args.renderdoccmd, "adb-session", "--device", args.serial, "--capture", result["captured"]["path"],
                 "--drawcall-profiling"], tool_env(args.adb), commands, journal)
            init = replies[0]["reply"] if replies else {}
            result["session"] = {"exit": code, "replies": len(replies), "reasons": session_reasons,
                                 "captureDrawcallCount": init.get("drawcall_count"),
                                 "drawcalls": max((len(r["reply"].get("drawcalls", [])) for r in replies
                                                   if isinstance(r.get("reply"), dict)), default=0)}
            journal.step("session-done", **result["session"])
    except RESTORE_ERRORS as exc:
        journal.data["reasons"].append("session: %s" % exc)
    finally:
        diffs = do_restore(args, journal)
        try:
            journal.step("finished", restoreVerified=journal.data["restore"].get("verified"))
        except OSError as exc:
            journal.data["reasons"].append("journal save: %s" % exc)
        emit(summary(journal, diffs))
    session = result.get("session")
    ok = (session is not None and session["exit"] == 0 and session["drawcalls"] > 0
          and journal.data["restore"].get("verified"))
    return 0 if ok else 2


def phase_restore_only(args, out):
    journal_path = out / "journal.json"
    try:
        journal = load_journal(journal_path)
        diffs = do_restore(args, journal)
        journal.step("restored-only", verified=journal.data["restore"].get("verified"))
        emit(summary(journal, diffs))
        return 0 if journal.data["restore"].get("verified") else 3
    except RESTORE_ERRORS as exc:
        emit({"verified": False, "error": str(exc)})
        return 3


def main():
    p = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    p.add_argument("phase_name", choices=("probe", "launch", "capture", "finish", "restore-only"),
                   help="probe = launch+capture+finish одним вызовом; остальные — для владельца прогона (Run-QuestE2E)")
    p.add_argument("--renderdoccmd", required=True)
    p.add_argument("--adb", required=True, help="тот же adb, что у Unity/runner (избежать kill-server)")
    p.add_argument("--serial", required=True)
    p.add_argument("--package", required=True)
    p.add_argument("--activity", required=True)
    p.add_argument("--intent-args", default=None)
    p.add_argument("--out", required=True, help="папка отчёта/журнала прогона")
    p.add_argument("--phase", default=None, help="capture: точное имя фазы perf-stress, после которой снимать кадр")
    p.add_argument("--max-wait", type=float, default=300.0, help="capture: ожидание фазы, с")
    p.add_argument("--settle", type=float, default=3.0, help="секунд после фазы (probe: после запуска) до захвата")
    p.add_argument("--counters", default=DEFAULT_COUNTERS)
    p.add_argument("--samples", type=int, default=3)
    args = p.parse_args()
    # CreateProcess не ищет относительный путь со слешами от cwd надёжно: только абсолютный.
    args.renderdoccmd = str(Path(args.renderdoccmd).resolve())
    out = Path(args.out).resolve()
    if args.phase_name == "launch":
        return phase_launch(args, out)
    if args.phase_name == "capture":
        return phase_capture(args, out)
    if args.phase_name == "finish":
        return phase_finish(args, out)
    if args.phase_name == "restore-only":
        return phase_restore_only(args, out)
    code = phase_launch(args, out)
    if code == 0:
        phase_capture(args, out)
        code = phase_finish(args, out)
    return code


if __name__ == "__main__":
    sys.exit(main())
