"""Профиль editor: сценарии набора на Editor-стенде (MPPM) через фасад PlayLaunch и PlayModeTestStand.

Для каждого сценария: Plan → Play (Role=server, ClientCount по сценарию) → Status до Ready →
run-e2e всем участникам (сначала сервер) → Status до E2EFinished → копия e2e-*.json → Cancel → архивы логов.
Стенд, его запросы и уборка принадлежат PlayLaunch; здесь только последовательность вызовов фасада.
"""
import asyncio
import json
from pathlib import Path
import shutil
import subprocess
import time
import uuid

from . import editmode
from . import freshness
from . import verdict as V
from .unity_mcp import LAUNCH, STAND, UnityCallError, UnityMcp, literal

MAX_EDITOR_CLIENTS = 3  # MPPM: не больше 4 игроков, сервер — один из них.
LAUNCH_ATTEMPTS = 2  # Один повтор старта стенда: клон MPPM изредка не подключается к MPE (infra_issue 10300).
POLL_SECONDS = 1.0
START_ACK_SECONDS = 40
CLEANUP_SECONDS = 120
EDITOR_SAFE = ("new { playing = UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode, "
               "compiling = UnityEditor.EditorApplication.isCompiling, "
               "dirty = System.Linq.Enumerable.Any(System.Linq.Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount), "
               "i => UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty), "
               "prefab = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() != null }")


class StandError(RuntimeError):
    """Стенд не дошёл до нужного состояния; сообщение — для вердикта."""


def launch_configuration(scenario):
    return {"Enabled": True, "Role": "server", "ClientCount": scenario.clients, "HostIsAdmin": False,
            "SceneSource": "scene", "ScenePath": scenario.map, "ModeId": scenario.mode,
            "AutoGoLive": False, "BotCount": 0, "PauseOnFocusLoss": False, "Readiness": "map-playable",
            "TimeoutSeconds": scenario.ready_timeout}


def is_ready(status, owner, run_id, clients, scene):
    """Свежий Ready своего запуска: сервер + clients подключённых клиентов с сессией и аватаром на нужной карте."""
    ps = status.get("Participants") or []
    if not (status.get("Ready") and status.get("Owner") == owner and status.get("RunId") == run_id):
        return False
    if len(ps) != clients + 1 or len({p.get("ParticipantId") for p in ps}) != clients + 1:
        return False
    if not all(p.get("RunId") == run_id and p.get("ProcessSessionId") and p.get("Active") and p.get("Playing")
               and p.get("LastSeenAgeSeconds", 99) <= 5 and p.get("Scene") == scene
               and p.get("MapPlayable") and p.get("MapRunValid") for p in ps):
        return False
    servers = [p for p in ps if p.get("Server") and not p.get("Client")]
    joined = [p for p in ps if p.get("Client") and not p.get("Server") and p.get("Connected")
              and p.get("SessionNetId") and p.get("AvatarNetId")]
    return len(servers) == 1 and len(joined) == clients and servers[0].get("ServerSessions", 0) == clients


def is_finished(status, owner, run_id, expected):
    """Все участники ready-набора (ParticipantId + ProcessSessionId) закончили свой сценарий."""
    ps = status.get("Participants") or []
    return (status.get("Owner") == owner and status.get("RunId") == run_id
            and {(p.get("ParticipantId"), p.get("ProcessSessionId")) for p in ps} == expected
            and all(p.get("LastSeenAgeSeconds", 99) <= 5 and p.get("E2EFinished") for p in ps))


def is_stopped(status):
    """Стенд остановлен: нет Play и владельца. Failed после своего таймаута PlayLaunch тоже остановленный стенд —
    ошибка остаётся в SessionState до следующего Acquire."""
    # После остановки Participants хранит последний heartbeat клонов (playing=true давностью в минуты): живым
    # считается только свежий.
    return (status.get("State") in ("Idle", "Failed") and not status.get("Playing") and not status.get("Owner")
            and not any((p.get("Playing") or p.get("Active")) and p.get("LastSeenAgeSeconds", 99) <= 5
                        for p in status.get("Participants") or []))


def short_status(status):
    ps = status.get("Participants") or []
    return (str(status.get("State")) + ", участников " + str(len(ps))
            + (", ошибка: " + str(status.get("Error")) if status.get("Error") else ""))


def participants_text(status):
    """Кто из участников где: для диагноза таймаута Ready."""
    rows = []
    for p in status.get("Participants") or []:
        role = "server" if p.get("Server") else "client" if p.get("Client") else str(p.get("RequestedRole") or "?")
        rows.append(str(p.get("ParticipantId")) + "(" + role + ", scene=" + str(p.get("Scene") or "-")
                    + (", connected" if p.get("Connected") else "") + (", avatar" if p.get("AvatarNetId") else "")
                    + ", seen " + str(round(p.get("LastSeenAgeSeconds", 0))) + " с)")
    return "; ".join(rows) or "нет"


class EditorRun:
    def __init__(self, project_root, outdir, log, trace):
        self.project_root = Path(project_root)
        self.outdir = Path(outdir)
        self.log = log
        self.trace = trace
        self.unity = None
        self.editor_root = None

    async def status(self):
        return await self.unity.code(LAUNCH + ".Status()")

    async def wait_status(self, condition, seconds, what, fail_on_failed=True):
        deadline = time.monotonic() + seconds
        latest, last_note = None, 0
        while time.monotonic() < deadline:
            latest = await self.status()
            if condition(latest):
                return latest
            if fail_on_failed and latest.get("State") == "Failed":
                raise StandError(what + ": PlayLaunch Failed: " + str(latest.get("Error"))
                                 + "; участники: " + participants_text(latest))
            if time.monotonic() - last_note > 15:
                self.log("    " + what + ": " + short_status(latest))
                last_note = time.monotonic()
            await asyncio.sleep(POLL_SECONDS)
        raise StandError(what + ": не дождались за " + str(seconds) + " с (" + short_status(latest or {})
                         + "; участники: " + participants_text(latest or {}) + ")")

    async def preflight(self):
        """Проверки до первого Play; любая неудача — вердикт не получен."""
        checks = []
        data_path = await self.unity.code("UnityEngine.Application.dataPath")
        self.editor_root = Path(str(data_path)).parent
        checks.append({"name": "Unity доступна через MCP своего worktree", "verdict": V.PASS,
                       "detail": str(self.editor_root)})
        if self.editor_root.resolve() == self.project_root.resolve():
            checks.append({"name": "Unity — worker, не этот checkout", "verdict": V.INCONCLUSIVE,
                           "detail": "MCP адресует Editor этого же checkout"})
            return checks
        safe = await self.unity.code(EDITOR_SAFE)
        before = await self.status()
        idle = is_stopped(before)
        clean = isinstance(safe, dict) and not any(safe.values())
        checks.append({"name": "Editor свободен: нет Play, компиляции, грязных сцен, prefab stage, чужого запуска",
                       "verdict": V.PASS if idle and clean else V.INCONCLUSIVE,
                       "detail": "" if idle and clean else "editor=" + str(safe) + "; стенд: " + short_status(before)})
        return checks

    def copy_result(self, run_id, participant, target_dir):
        source_dir = (self.editor_root / "Temp" / "VRBattlegroundsTestStand" / run_id).resolve()
        source = (source_dir / ("e2e-" + participant + ".json")).resolve()
        if not source.is_relative_to(source_dir) or not source.is_file():
            return None, str(source)
        target = target_dir / source.name
        shutil.copyfile(source, target)
        return json.loads(target.read_text(encoding="utf-8")), str(target)

    async def run_scenario(self, scenario, framework):
        record = {"name": scenario.name, "topology": scenario.topology, "participants": [],
                  "configuration": launch_configuration(scenario), "logs": {}}
        target_dir = self.outdir / scenario.name
        target_dir.mkdir(parents=True, exist_ok=True)
        if scenario.clients > MAX_EDITOR_CLIENTS:
            record["launchError"] = "Editor-стенд (MPPM) держит не больше " + str(MAX_EDITOR_CLIENTS) + " клиентов"
            return record
        for attempt in range(1, LAUNCH_ATTEMPTS + 1):
            retry = await self.run_attempt(scenario, framework, record, target_dir, attempt)
            if not retry:
                break
        return record

    async def run_attempt(self, scenario, framework, record, target_dir, attempt):
        """Один запуск стенда; True — стенд не дошёл до Ready, подтверждённо остановлен и можно повторить."""
        owner = "heavy-" + uuid.uuid4().hex[:12]
        record["owner"] = owner
        run_id, peers, played, launching = None, [], False, False
        started = time.monotonic()
        try:
            config = record["configuration"]
            plan = await self.unity.code(LAUNCH + ".Plan(" + literal(config) + ")")
            record["plan"] = plan
            if not (plan.get("Passed") and plan.get("PlanHash")):
                raise StandError("Plan отказал: " + str(plan.get("Code")) + " " + str(plan.get("Message")))
            self.log("  " + scenario.name + " " + scenario.topology + ": Play")
            played = True
            active = await self.unity.code(LAUNCH + ".Play(" + literal(config) + ", " + json.dumps(owner)
                                           + ", " + json.dumps(plan["PlanHash"]) + ")")
            run_id = active.get("RunId")
            record["runId"] = run_id
            if not run_id or active.get("Owner") != owner:
                raise StandError("Play не вернул свой RunId: " + short_status(active))
            launching = True
            ready = await self.wait_status(lambda s: is_ready(s, owner, run_id, scenario.clients, scenario.scene_name),
                                           # Запас сверх TimeoutSeconds: свой LaunchNotReady стенда виднее нашего таймаута.
                                           scenario.ready_timeout + 20, "Ready")
            launching = False
            record["readySeconds"] = round(time.monotonic() - started, 1)
            self.log("  " + scenario.name + ": Ready за " + str(record["readySeconds"]) + " с, run-e2e")
            peers = sorted(ready["Participants"], key=lambda p: (not p.get("Server"), p.get("ParticipantId")))
            for peer in peers:
                await self.start_e2e(scenario, run_id, peer, record)
            expected = {(p["ParticipantId"], p["ProcessSessionId"]) for p in peers}
            final = await self.wait_status(lambda s: is_finished(s, owner, run_id, expected),
                                           scenario.scenario_timeout + 45, "E2EFinished")
            record["scenarioSeconds"] = round(time.monotonic() - started - record["readySeconds"], 1)
            record["e2eStatus"] = [{k: p.get(k) for k in ("ParticipantId", "E2EStatus", "E2EPassed")}
                                   for p in final["Participants"]]
        except (StandError, UnityCallError) as error:
            record["launchError"] = str(error)
        finally:
            if run_id and peers:
                self.collect_results(scenario, run_id, peers, target_dir, record)
            if played:
                await self.cleanup(owner, run_id, target_dir, record, framework)
        # Повтор только сбоя старта (стенд не дошёл до Ready, run-e2e не отправлялся) после подтверждённой уборки.
        retry = (launching and attempt < LAUNCH_ATTEMPTS and record.get("launchError")
                 and not record.get("cleanupError"))
        if retry:
            failed_dir = target_dir / ("failed-launch-" + str(attempt))
            if (target_dir / "logs").is_dir():
                (target_dir / "logs").rename(failed_dir)
            record.setdefault("launchRetries", []).append({
                "attempt": attempt, "runId": run_id, "error": record.pop("launchError"),
                "logs": str(failed_dir) if failed_dir.is_dir() else None})
            record["logs"] = {}
            record.pop("cleanup", None)
            self.log("  " + scenario.name + ": стенд не дошёл до Ready, повтор запуска (" + str(attempt + 1)
                     + "/" + str(LAUNCH_ATTEMPTS) + ")")
        return retry

    async def start_e2e(self, scenario, run_id, peer, record):
        request = {"RunId": run_id, "ParticipantId": peer["ParticipantId"], "ProcessSessionId": peer["ProcessSessionId"],
                   "RequestId": "heavy-" + uuid.uuid4().hex, "Action": "run-e2e", "TimeBudgetMs": 10000,
                   "Scenario": scenario.name, "ScenarioTimeoutSeconds": scenario.scenario_timeout}
        operation = await self.unity.code(
            STAND + ".Send(UnityEngine.JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.StandRequest>("
            + literal(request) + "))")
        deadline = time.monotonic() + START_ACK_SECONDS
        while not operation.get("Completed") and time.monotonic() < deadline:
            await asyncio.sleep(0.5)
            operation = await self.unity.code(STAND + ".Operation(" + json.dumps(request["RequestId"]) + ")")
        reply = operation.get("Reply") or {}
        record.setdefault("starts", []).append({"participant": peer["ParticipantId"], "reply": reply})
        if not (operation.get("Completed") and not operation.get("EffectUnknown") and reply.get("Passed")
                and reply.get("Code") == "e2e-started" and reply.get("ProcessSessionId") == peer["ProcessSessionId"]):
            raise StandError("run-e2e не подтверждён участником " + peer["ParticipantId"] + ": "
                             + str(reply.get("Code") or "нет ответа") + " " + str(reply.get("Message") or ""))

    def collect_results(self, scenario, run_id, peers, target_dir, record):
        for peer in peers:
            role_kind = "server" if peer.get("Server") else "client"
            try:
                data, path = self.copy_result(run_id, peer["ParticipantId"], target_dir)
            except Exception as error:
                data, path = None, type(error).__name__ + ": " + str(error)
            verdict, reasons = V.evaluate_participant(data, scenario.name, role_kind)
            role = (data or {}).get("role") or (role_kind + ":" + peer["ParticipantId"])
            checks = (data or {}).get("checks") or []
            record["participants"].append({"participant": peer["ParticipantId"], "role": role, "verdict": verdict,
                                           "reasons": reasons, "result": path, "data": data,
                                           "checksPassed": sum(1 for c in checks if c.get("passed")),
                                           "checksTotal": len(checks)})

    async def cleanup(self, owner, run_id, target_dir, record, framework):
        try:
            current = await self.status()
            if not run_id and current.get("Owner") == owner:
                run_id = current.get("RunId")
                record["runId"] = run_id
            if run_id and current.get("Owner") == owner and current.get("RunId") == run_id:
                await self.unity.code(LAUNCH + ".Cancel(" + json.dumps(run_id) + ", " + json.dumps(owner) + ")")
            stopped = await self.wait_status(is_stopped, CLEANUP_SECONDS, "Cancel", fail_on_failed=False)
            record["cleanup"] = {"State": stopped.get("State"), "Error": stopped.get("Error"),
                                 "CleanupPassed": stopped.get("CleanupPassed")}
            if stopped.get("RunId") != run_id:
                raise StandError("после уборки в статусе чужой RunId: " + short_status(stopped))
            if stopped.get("State") == "Idle" and not stopped.get("CleanupPassed"):
                raise StandError("уборка не подтверждена: " + short_status(stopped))
            self.collect_logs(stopped, run_id, target_dir, record, framework)
        except (StandError, UnityCallError) as error:
            record["cleanupError"] = str(error)

    def collect_logs(self, stopped, run_id, target_dir, record, framework):
        archives = [Path(p) for p in stopped.get("LogArchives") or [] if run_id and run_id in p.replace("\\", "/")]
        logs_dir = target_dir / "logs"
        logs_dir.mkdir(exist_ok=True)
        roles = {p["participant"]: p["role"] for p in record["participants"]}
        scans = []
        for archive in archives:
            if not archive.is_file():
                continue
            local = logs_dir / archive.name
            shutil.copyfile(archive, local)
            if archive.suffix.lower() != ".log":
                continue  # Рядом лежит startup JSONL диагностики — копия для разбора, без счётчиков.
            # Имя архива — <участник>-<pid>.log.
            participant = archive.stem.rsplit("-", 1)[0]
            role = roles.get(participant, participant)
            scans.append(V.scan_log(local, "server" if role == "server" else role, framework))
        record["logs"]["archived"] = len(scans)
        record["logs"]["archiveDirectory"] = stopped.get("LogArchiveDirectory")
        record["logs"]["scans"] = scans
        record["logs"]["classified"] = V.classify_log_issues(
            [(p["role"], p["data"]) for p in record["participants"]], framework)


def worker_revision(editor_root, origin_dev):
    """HEAD worker и его отставание от origin/dev по игровым путям; только чтение Git."""
    def git(*args):
        return subprocess.run(["git", "-C", str(editor_root), *args], capture_output=True, text=True,
                              encoding="utf-8", errors="replace", timeout=60)
    head = git("rev-parse", "HEAD")
    if head.returncode:
        return {"head": None, "error": head.stderr.strip()[:300]}
    info = {"head": head.stdout.strip(), "subject": git("log", "-1", "--format=%s").stdout.strip()[:200]}
    info["drift"] = freshness.drift(git, info["head"], origin_dev)
    return info


async def run(project_root, scenarios, outdir, log, trace, framework, origin_dev, editmode_known=None):
    """Возвращает (checks, scenario_records, extra). editmode_known — словарь известных красных: после
    сценариев прогнать EditMode (стенд к этому моменту убран)."""
    runner = EditorRun(project_root, outdir, log, trace)
    checks, records, extra = [], [], {}
    try:
        async with UnityMcp(project_root, trace) as unity:
            runner.unity = unity
            checks.extend(await runner.preflight())
            if any(c["verdict"] != V.PASS for c in checks):
                return checks, records, extra
            worker = await asyncio.to_thread(worker_revision, runner.editor_root, origin_dev)
            extra["worker"] = worker
            if worker.get("head"):
                checks.append(freshness.check("Код worker содержит игровые изменения свежей origin/dev",
                                              worker["drift"], worker["head"], origin_dev))
            else:
                checks.append({"name": "Код worker прочитан", "verdict": V.INCONCLUSIVE, "detail": worker.get("error")})
            for scenario in scenarios:
                log("Сценарий " + scenario.name + " (" + scenario.topology + ")")
                record = await runner.run_scenario(scenario, framework)
                records.append(record)
                if record.get("cleanupError"):
                    log("  Стенд не убран — остальные сценарии не запускаются")
                    break
            if editmode_known is not None:
                if any(r.get("cleanupError") for r in records):
                    extra["editmode"] = {"verdict": V.INCONCLUSIVE, "reasons": ["не запускался: стенд не убран"]}
                else:
                    result, info = await editmode.run(unity, log)
                    record = editmode.compare(result, editmode_known)
                    record["job"] = info
                    if info.get("error") and record["verdict"] == V.INCONCLUSIVE:
                        record["reasons"].append(str(info["error"]))
                    extra["editmode"] = record
    except UnityCallError as error:
        checks.append({"name": "Unity доступна через MCP своего worktree", "verdict": V.INCONCLUSIVE,
                       "detail": str(error) + ". Нужна своя RUNNING аренда worker и прокси этого worktree."})
    except Exception as error:  # noqa: BLE001 — запуск прокси, транспорт MCP (ExceptionGroup anyio) и прочее
        checks.append({"name": "Дирижёр стенда завершился без сбоя", "verdict": V.INCONCLUSIVE,
                       "detail": type(error).__name__ + ": " + str(error)[:600]})
    return checks, records, extra
