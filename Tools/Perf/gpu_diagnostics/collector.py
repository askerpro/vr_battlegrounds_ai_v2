"""Raw GPU evidence: runtime-каталоги и независимые границы logcat.

Legacy logcat не доказывает workload.ready, общие часы GPU/runtime или
аппаратную приёмку. Нормализованный run создаётся отдельным импортёром.
"""
from dataclasses import dataclass
import hashlib
import json
import math
from pathlib import Path
import queue
import re
import subprocess
import threading
import time

ADB_DEFAULT = r"C:/Program Files/Unity/Hub/Editor/6000.4.1f1/Editor/Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe"
PACKAGE = "com.UnityTechnologies.com.unity.template.urpblank"
PROFILER = "/system_ext/bin/ovrgpuprofiler"
# Только обратная совместимость CLI. Новый workload передаёт точный --phases.
LEGACY_PHASES = ["база", "куклы: по карте", "куклы: арена", "куклы"]
DEFAULT_REALTIME = ["GPU % Utilization", "GPU % Bus Busy", "% Vertex Fetch Stall",
                    "% Texture Fetch Stall", "% Stalled on System Memory",
                    "% Shaders Busy", "% Shaders Stalled", "% Texture Pipes Busy",
                    "% Time Shading Vertices", "% Time Shading Fragments",
                    "Vertices Shaded / Second", "Fragments Shaded / Second",
                    "Average Polygon Area", "% Prims Trivially Rejected", "LRZ Pixels Killed"]
DEFAULT_STAGE = ["Clocks", "% Time Shading Fragments", "Fragments Shaded",
                 "% Texture Fetch Stall", "% Time Shading Vertices"]


@dataclass
class Config:
    mode: str
    out: Path
    phases: list[str]
    adb: str = ADB_DEFAULT
    serial: str | None = None
    package: str = PACKAGE
    metrics: list[str] | None = None
    capture_kind: str = "stage"
    settle: float = 2.0
    seconds: float = 5.0
    max_wait: float = 900.0
    capture_timeout: float = 120.0
    command_timeout: float = 30.0
    poll_interval: float = 0.05
    enable_detailed: bool = False


def parse_phases(value):
    """Точные уникальные имена; capture filenames не выводятся из фазы."""
    try:
        if isinstance(value, str):
            text = value if value.lstrip().startswith(("[", "{")) else Path(value).read_text(encoding="utf-8-sig")
            phases = json.loads(text)
        else:
            phases = value
    except (OSError, ValueError) as exc:
        raise ValueError(f"Не удалось прочитать --phases: {exc}") from exc
    if (not isinstance(phases, list) or not phases or
            any(not isinstance(p, str) or not p.strip() for p in phases) or
            len(set(phases)) != len(phases)):
        raise ValueError("--phases должен содержать непустой JSON список уникальных точных имён")
    return list(phases)


def parse_catalog(text):
    catalog, identifiers = {}, set()
    declared = None
    for line in text.splitlines():
        header = re.match(r"^\s*(\d+)\s+metrics supported\s*:\s*$", line)
        if header:
            declared = int(header.group(1))
            continue
        match = re.match(r"^\s*(\d+)\s+(.+?)\s*$", line)
        if not match:
            continue
        identifier, name = match.groups()
        if name in catalog or identifier in identifiers:
            raise ValueError(f"Неоднозначный runtime catalog: {identifier} {name}")
        catalog[name] = identifier
        identifiers.add(identifier)
    if not catalog:
        raise ValueError("Runtime catalog пуст или имеет неизвестный формат")
    if declared is not None and declared != len(catalog):
        raise ValueError(f"Неполный runtime catalog: declared={declared}, parsed={len(catalog)}")
    return catalog


def select_metrics(catalog, names):
    if not names or len(set(names)) != len(names):
        raise ValueError("Нужен непустой список уникальных точных имён метрик")
    missing = [name for name in names if name not in catalog]
    if missing:
        raise ValueError("В runtime catalog отсутствуют точные имена: " + ", ".join(missing))
    return [catalog[name] for name in names]


def _has_errors(text):
    return bool(re.search(r"no metrics to track|\berror\b|\bfailed\b|\bignored\b|not supported|unsupported", text, re.I))


def _mode(text):
    match = re.search(r"Detailed GPU profiling is (enabled|disabled)\b", text, re.I)
    if not match:
        raise ValueError("Неизвестное состояние detailed profiling; изменение запрещено")
    return match.group(1).lower() == "enabled"


def _stop(process):
    """Останавливает только переданный собственный subprocess и обязательно ждёт exit."""
    if process.poll() is None:
        process.terminate()
        try:
            return process.wait(timeout=2)
        except subprocess.TimeoutExpired:
            process.kill()
    return process.wait(timeout=2)


class _Reader:
    def __init__(self, process, path, clock):
        self.process, self.path, self.clock = process, path, clock
        self.events = queue.Queue()
        self.lock = threading.Lock()
        self.boundaries = []
        self.thread = threading.Thread(target=self._read, name="gpu-logcat-reader", daemon=True)

    def _emit(self, kind, phase=None):
        with self.lock:
            event = {"kind": kind, "phase": phase, "at": self.clock(),
                     "generation": len(self.boundaries) + 1}
            self.boundaries.append(event)
            self.events.put(event)

    def _read(self):
        try:
            with self.path.open("w", encoding="utf-8", newline="\n") as output:
                for line in self.process.stdout:
                    output.write(line)
                    output.flush()
                    match = re.search(r"=== фаза «([^»]+)»", line)
                    if "=== итог прогона" in line:
                        self._emit("runEnded")
                    elif match:
                        self._emit("phaseChanged", match.group(1))
        except (OSError, ValueError):
            self._emit("logcatReadError")
        finally:
            self._emit("logcatEOF")

    def snapshot(self):
        with self.lock:
            return self.clock(), list(self.boundaries)


def _data_reasons(path, names, mode):
    text = path.read_text(encoding="utf-8", errors="replace")
    reasons = []
    if _has_errors(text):
        reasons.append("profilerReportedError")
    values = {name: [] for name in names}
    for line in text.splitlines():
        if ":" not in line:
            continue
        name, value = line.rsplit(":", 1)
        name = name.strip()
        if name not in values:
            continue
        try:
            number = float(value.strip())
        except ValueError:
            reasons.append("invalidMetricData")
            continue
        if math.isfinite(number) and number >= 0:
            values[name].append(number)
        else:
            reasons.append("invalidMetricData")
    if any(not numbers for numbers in values.values()):
        reasons.append("missingMetricData")
    if mode == "detailed":
        valid_stages = 0
        for line in text.splitlines():
            stage = re.match(r"^\s*Stage\s+\d+\s*:\s*(.*)$", line)
            if not stage:
                continue
            # Проверяется вся duration, включая знак; одна корректная строка
            # не может скрыть отрицательное/нечисленное время другой стадии.
            timing = re.fullmatch(r".+?\s+(\S+)\s+ms", stage.group(1).strip())
            try:
                duration = float(timing.group(1)) if timing else float("nan")
            except ValueError:
                duration = float("nan")
            if math.isfinite(duration) and duration >= 0:
                valid_stages += 1
            else:
                reasons.append("invalidStageData")
        if not valid_stages:
            reasons.append("missingStageData")
    return list(dict.fromkeys(reasons)), {name: len(numbers) for name, numbers in values.items()}


def collect(config, *, run=subprocess.run, popen=subprocess.Popen, clock=time.monotonic):
    """Сохраняет capture.json даже при отказе preflight; не перезаписывает evidence.

    run/popen — явные зависимости для автономных проверок без устройства.
    Времена — host monotonic receive time, не подтверждённый clock runtime.
    """
    out = Path(config.out)
    out.mkdir(parents=True, exist_ok=True)
    if (out / "capture.json").exists() or (out / "raw").exists():
        raise ValueError("Каталог уже содержит collector evidence; выберите новый --out")
    raw = out / "raw"
    raw.mkdir()
    started = clock()
    receipt = {"schemaVersion": 1, "requested": list(config.phases), "complete": [],
               "missing": list(config.phases), "captures": [], "commands": [],
               "catalogHashes": {}, "mode": config.mode,
               "captureKind": "realtime" if config.mode == "realtime" else config.capture_kind,
               "serial": config.serial, "package": config.package,
               "clock": "host.monotonic.receive", "started": started, "ended": None,
               "quality": "limited", "workloadReadyVerified": False, "hardwarePerformancePass": False,
               "limitations": ["legacyPhaseEvidenceOnly", "workload.ready not verified",
                               "host receive time cannot verify runtime/GPU clock or stale buffered logcat",
                               "no normalized app GPU frame samples; no hardware performance PASS"],
               "reasons": [], "modeOwnership": {"initiallyEnabled": None,
                                                 "enabledByCollector": False, "restored": False},
               "cleanup": {"processes": [], "readerStopped": True}, "exitCode": 2}
    processes = []
    reader, active, log_process = None, None, None
    owned_mode = False
    adb_prefix = [config.adb] + (["-s", config.serial] if config.serial else [])

    def command(flags, name, allowed_codes=(0,), cleanup=False):
        argv = adb_prefix + ["shell"] + flags
        record = {"argv": argv, "rawPath": f"raw/{name}.txt", "start": clock(), "exit": None}
        receipt["commands"].append(record)
        try:
            remaining = config.max_wait - (clock() - started)
            if not cleanup and remaining <= 0:
                raise ValueError("maxWaitBeforeCommand")
            timeout = config.command_timeout if cleanup else min(config.command_timeout, remaining)
            result = run(argv, capture_output=True, text=True, encoding="utf-8", errors="replace",
                         timeout=timeout)
        except subprocess.TimeoutExpired as exc:
            record["reason"] = "commandTimeoutUnknownOutcome"
            partial = exc.stdout or ""
            if isinstance(partial, bytes):
                partial = partial.decode("utf-8", "replace")
            (raw / f"{name}.txt").write_text(partial, encoding="utf-8", newline="\n")
            raise ValueError(f"{name}: timeout, исход неизвестен; retry не выполняется") from exc
        finally:
            record["end"] = clock()
        output = (result.stdout or "") + (result.stderr or "")
        (raw / f"{name}.txt").write_text(output, encoding="utf-8", newline="\n")
        record["exit"] = result.returncode
        if result.returncode not in allowed_codes or _has_errors(output):
            raise ValueError(f"{name}: exit={result.returncode}, см. {record['rawPath']}")
        return output, result.returncode

    def finish_capture(reason=None):
        nonlocal active
        if active is None:
            return
        item, process, output = active
        if reason:
            item["reasons"].append(reason)
        try:
            item["exit"] = _stop(process)
        finally:
            output.close()
        ended, boundaries = reader.snapshot()
        item["end"] = ended
        for boundary in boundaries:
            if boundary["generation"] > item["generation"] and boundary["at"] <= ended:
                item["reasons"].append(boundary["kind"])
        if config.mode == "detailed" and item["exit"] != 0:
            item["reasons"].append("captureNonzeroExit")
        data_reasons, counts = _data_reasons(out / item["rawPath"], names, config.mode)
        item["reasons"] = list(dict.fromkeys(item["reasons"] + data_reasons))
        item["metricObservations"] = counts
        item["complete"] = not item["reasons"]
        if item["complete"] and item["phase"] not in receipt["complete"]:
            receipt["complete"].append(item["phase"])
        active = None

    try:
        config.phases = parse_phases(config.phases)
        if not re.fullmatch(r"[A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z][A-Za-z0-9_]*)+", config.package):
            raise ValueError("package должен быть точным Android application ID")
        if config.mode not in ("realtime", "detailed") or config.capture_kind not in ("stage", "draw"):
            raise ValueError("Неизвестный mode/capture-kind")
        for key in ("seconds", "max_wait", "capture_timeout", "command_timeout", "poll_interval"):
            if not math.isfinite(getattr(config, key)) or getattr(config, key) <= 0:
                raise ValueError(f"{key} должен быть конечным положительным числом")
        if not math.isfinite(config.settle) or config.settle < 0:
            raise ValueError("settle должен быть конечным неотрицательным числом")
        help_text, _ = command([PROFILER, "--help"], "profiler-help")
        catalog_flags = {"realtime": ["-m"], "stage": ["-m", "-t"], "draw": ["-x", "-m"]}
        catalogs, catalog_errors = {}, {}
        for kind, flags in catalog_flags.items():
            try:
                text, _ = command([PROFILER] + flags, f"catalog-{kind}")
                receipt["catalogHashes"][kind] = hashlib.sha256(text.encode("utf-8")).hexdigest()
                catalogs[kind] = parse_catalog(text)
            except ValueError as exc:
                catalog_errors[kind] = str(exc)
        kind = "realtime" if config.mode == "realtime" else config.capture_kind
        # Старый -x игнорирует -s; каталог не доказывает рабочий capture API.
        # Новые синтаксисы требуют отдельного проверенного adapter.
        capabilities = {"schemaVersion": 1, "catalogs": catalogs, "catalogErrors": catalog_errors,
                        "catalogHashes": receipt["catalogHashes"],
                        "stageCapture": "--renderstage-metrics" in help_text and "--trace" in help_text,
                        "drawCapture": False,
                        "drawReason": "no validated draw capture adapter for this runtime help"}
        (out / "capabilities.json").write_text(json.dumps(capabilities, ensure_ascii=False, indent=2),
                                              encoding="utf-8", newline="\n")
        if kind not in catalogs:
            raise ValueError(f"Нужный runtime catalog недоступен: {kind}: {catalog_errors.get(kind)}")
        names = list(config.metrics if config.metrics is not None else
                     DEFAULT_REALTIME if config.mode == "realtime" else DEFAULT_STAGE)
        identifiers = select_metrics(catalogs[kind], names)
        receipt["selectedMetrics"] = [{"name": n, "id": i, "catalog": kind} for n, i in zip(names, identifiers)]
        if config.mode == "detailed":
            if config.capture_kind == "draw" or not capabilities["stageCapture"]:
                raise ValueError("Запрошенный detailed capture не имеет проверенного runtime adapter; catalog не доказывает поддержку")
        if config.mode == "realtime" and "--realtime" not in help_text:
            raise ValueError("Runtime help не подтверждает realtime capture")
        mode_text, _ = command([PROFILER, "-i"], "mode-initial")
        initial_mode = _mode(mode_text)
        receipt["modeOwnership"]["initiallyEnabled"] = initial_mode
        if config.mode == "realtime" and initial_mode:
            raise ValueError("Обычный realtime требует detailed OFF; чужой режим не меняется")
        if config.mode == "detailed" and not initial_mode:
            if not config.enable_detailed:
                raise ValueError("Detailed выключен: подготовьте режим отдельно или явно --enable-detailed до запуска приложения")
            pid_text, pid_exit = command(["pidof", config.package], "application-pid", (0, 1))
            if pid_exit != 1 or pid_text.strip():
                raise ValueError("Нельзя включить detailed: отсутствие запущенного приложения не подтверждено")
            command([PROFILER, "-e", config.package], "mode-enable")
            owned_mode = True
            receipt["modeOwnership"]["enabledByCollector"] = True
            verify_text, _ = command([PROFILER, "-i"], "mode-after-enable")
            if not _mode(verify_text):
                raise ValueError("Detailed enable не подтверждён")
        log_argv = adb_prefix + ["logcat", "-T", "1", "-v", "brief", "Unity:I", "*:S"]
        log_process = popen(log_argv, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                            text=True, encoding="utf-8", errors="replace")
        processes.append(("logcat", log_process))
        receipt["logcatArgv"] = log_argv
        reader = _Reader(log_process, raw / "logcat.txt", clock)
        reader.thread.start()
        current, attempted_generation, terminal = None, None, None
        while True:
            now = clock()
            while True:
                try:
                    event = reader.events.get_nowait()
                except queue.Empty:
                    break
                if active:
                    finish_capture(event["kind"])
                if event["kind"] == "phaseChanged":
                    current = event
                else:
                    terminal = event["kind"]
                    current = None
            if terminal:
                receipt["reasons"].append(terminal)
                break
            if now - started >= config.max_wait:
                finish_capture("maxWait")
                receipt["reasons"].append("maxWait")
                break
            if active:
                item, process, _ = active
                elapsed = now - item["start"]
                code = process.poll()
                if code is not None:
                    if config.mode == "realtime" and code != 0:
                        finish_capture("captureNonzeroExit")
                    else:
                        finish_capture("captureExitedEarly" if config.mode == "realtime" and elapsed < config.seconds else None)
                elif config.mode == "realtime" and elapsed >= config.seconds:
                    finish_capture()
                elif elapsed >= config.capture_timeout:
                    finish_capture("captureTimeout")
            if len(receipt["complete"]) == len(config.phases):
                break
            if (active is None and current and current["phase"] in config.phases and
                    current["phase"] not in receipt["complete"] and
                    attempted_generation != current["generation"] and now - current["at"] >= config.settle):
                _, boundaries = reader.snapshot()
                if boundaries and boundaries[-1]["generation"] != current["generation"]:
                    continue
                attempted_generation = current["generation"]
                argv = adb_prefix + ["shell", PROFILER]
                if config.mode == "realtime":
                    argv += ["--realtime=" + ",".join(identifiers)]
                else:
                    argv += ["-v", "--renderstage-metrics=" + ",".join(identifiers), "-t"]
                path = raw / f"capture-{len(receipt['captures']) + 1:03d}-{kind}.txt"
                output = path.open("w", encoding="utf-8", newline="\n")
                item = {"phase": current["phase"], "phaseStart": current["at"],
                        "generation": current["generation"], "start": clock(), "end": None,
                        "argv": argv, "rawPath": path.relative_to(out).as_posix(), "exit": None,
                        "reasons": [], "complete": False}
                receipt["captures"].append(item)
                try:
                    process = popen(argv, stdout=output, stderr=subprocess.STDOUT, text=True,
                                    encoding="utf-8", errors="replace")
                except BaseException:
                    output.close()
                    item["reasons"].append("captureStartFailed")
                    raise
                processes.append((path.name, process))
                active = item, process, output
            time.sleep(config.poll_interval)
    except (ValueError, OSError, subprocess.SubprocessError) as exc:
        receipt["reasons"].append(str(exc))
    except KeyboardInterrupt:
        receipt["reasons"].append("interrupted")
    finally:
        try:
            finish_capture("collectorStopped")
        except (OSError, subprocess.SubprocessError) as exc:
            receipt["reasons"].append(f"capture cleanup: {exc}")
        for name, process in reversed(processes):
            entry = {"name": name, "exit": None, "reaped": False}
            try:
                entry["exit"] = _stop(process)
                entry["reaped"] = True
            except (OSError, subprocess.SubprocessError) as exc:
                entry["error"] = str(exc)
                receipt["reasons"].append("processCleanupFailed")
            receipt["cleanup"]["processes"].append(entry)
        if reader:
            reader.thread.join(timeout=2)
            receipt["cleanup"]["readerStopped"] = not reader.thread.is_alive()
            if reader.thread.is_alive():
                receipt["reasons"].append("readerCleanupFailed")
            receipt["phaseBoundaries"] = reader.snapshot()[1]
        if log_process and log_process.stdout:
            # close не должен блокироваться при зависшем reader.readline.
            if not reader or not reader.thread.is_alive():
                log_process.stdout.close()
        if owned_mode:
            try:
                command([PROFILER, "-d"], "mode-restore", cleanup=True)
                restored, _ = command([PROFILER, "-i"], "mode-after-restore", cleanup=True)
                receipt["modeOwnership"]["restored"] = not _mode(restored)
                if not receipt["modeOwnership"]["restored"]:
                    receipt["reasons"].append("modeRestoreNotConfirmed")
            except (ValueError, OSError, subprocess.SubprocessError) as exc:
                receipt["reasons"].append(f"mode restore: {exc}")
        receipt["complete"] = [phase for phase in config.phases if phase in receipt["complete"]]
        receipt["missing"] = [phase for phase in config.phases if phase not in receipt["complete"]]
        receipt["ended"] = clock()
        receipt["exitCode"] = 0 if not receipt["missing"] and not receipt["reasons"] else 2
        receipt["partial"] = receipt["exitCode"] != 0
        (out / "capture.json").write_text(json.dumps(receipt, ensure_ascii=False, indent=2),
                                           encoding="utf-8", newline="\n")
    return receipt
