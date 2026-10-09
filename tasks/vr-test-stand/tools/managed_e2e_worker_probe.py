"""Конечный managed E2E пакет: запускать только в подготовленной RUNNING аренде."""
import argparse
import asyncio
from datetime import timedelta
import json
from pathlib import Path
import subprocess
import sys
import time
import uuid

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client

PROJECT_ROOT = Path(__file__).resolve().parents[3]
REPORTS = PROJECT_ROOT / "tasks" / "vr-test-stand" / "reports"
sys.path.insert(0, str(PROJECT_ROOT / "Tools" / "TestStand"))
from worker_probe import unpack
from probe_log_capture import ProbeLogs, EDITOR_IDENTITY
from mcp_observation_retry import execute_observation, verify_observation_contract

LAUNCH = "VrBattlegrounds.EditorTools.TestStand.PlayLaunch"
STAND = "VrBattlegrounds.EditorTools.TestStand.PlayModeTestStand"
SCENARIO = "session-recovery-on-reconnect"


def normalized(path):
    return str(path).replace("\\", "/").rstrip("/").casefold()


def literal(value):
    return json.dumps(json.dumps(value, separators=(",", ":")))


async def run(args):
    output = Path(args.output).resolve()
    if not output.is_relative_to(REPORTS.resolve()):
        raise ValueError("Отчёт должен находиться в tasks/vr-test-stand/reports/")
    ignored = subprocess.run(["git", "check-ignore", "--quiet", str(output)], cwd=PROJECT_ROOT)
    if ignored.returncode != 0:
        raise ValueError("Путь отчёта должен быть исключён из Git")
    common = subprocess.check_output(
        ["git", "rev-parse", "--path-format=absolute", "--git-common-dir"],
        cwd=PROJECT_ROOT, text=True).strip()
    gitdir = subprocess.check_output(
        ["git", "rev-parse", "--absolute-git-dir"], cwd=PROJECT_ROOT, text=True).strip()
    if normalized(common) == normalized(gitdir):
        raise ValueError("Probe разрешён только из assigned linked worktree")
    if normalized(args.editor_root) in (normalized(Path(common).parent), normalized(PROJECT_ROOT)):
        raise ValueError("editor-root должен указывать на broker worker, не main/assigned checkout")
    owner = "managed-e2e-probe-" + uuid.uuid4().hex
    report = {"passed": False, "owner": owner, "worktree": str(PROJECT_ROOT),
              "workerRoot": args.editor_root, "scenario": SCENARIO, "checks": [],
              "operations": [], "statusTrace": [], "results": [], "calls": []}
    report['observationContract'] = verify_observation_contract(args.editor_root)
    output.parent.mkdir(parents=True, exist_ok=True)
    logs = ProbeLogs(output, report, args.editor_root)

    def save():
        output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")

    def check(name, condition, detail=None):
        report["checks"].append({"name": name, "passed": bool(condition), "detail": detail})
        save()
        if not condition:
            raise AssertionError(name)

    save()
    server = StdioServerParameters(command="uv", args=["run", "--quiet", "Tools/agents/unity_mcp_proxy.py"], cwd=str(PROJECT_ROOT))
    try:
        async with stdio_client(server) as (reader, writer):
            async with ClientSession(reader, writer, read_timeout_seconds=timedelta(seconds=args.call_timeout)) as session:
                await asyncio.wait_for(session.initialize(), args.call_timeout)
                unpack(await asyncio.wait_for(session.call_tool("set_active_instance", {"instance": args.instance}), args.call_timeout))

                async def code(expression, mutation=False):
                    # Любая мутация отправляется один раз. Ошибка/таймаут не является разрешением повторить её.
                    item = {"expression": expression, "mutation": mutation, "started": time.time()}
                    report["calls"].append(item)
                    save()
                    try:
                        async def send():
                            return await asyncio.wait_for(session.call_tool('execute_code', {
                                'action': 'execute', 'code': 'return ' + expression + ';'}), args.call_timeout)
                        value = await execute_observation(expression, send, unpack, item, save, mutation=mutation)
                        item["result"] = value
                        if logs.sources:
                            await asyncio.to_thread(logs.capture, 'call', value)
                        return value
                    except Exception as error:
                        item["error"] = type(error).__name__ + ": " + str(error)
                        if mutation:
                            item["effectUnknown"] = True
                        raise
                    finally:
                        save()

                async def status():
                    value = await code(LAUNCH + ".Status()")
                    await asyncio.to_thread(logs.capture, 'status', value)
                    report["statusTrace"].append(value)
                    save()
                    return value

                async def wait_status(condition, seconds, fail_launch=True):
                    deadline = time.monotonic() + seconds
                    latest = None
                    while time.monotonic() < deadline:
                        latest = await status()
                        if condition(latest):
                            return latest
                        if fail_launch and latest.get("State") == "Failed":
                            raise AssertionError("PlayLaunch Failed: " + str(latest.get("Error")))
                        await asyncio.sleep(0.5)
                    report["timeoutStatus"] = latest
                    raise TimeoutError("Ожидание состояния превысило бюджет")

                run_id = None
                verified = False
                play_attempted = False
                peers = []

                async def preserve_results():
                    # VP процессы пишут в общий ProjectRoot, StandManifest удаляется при завершении.
                    directory = (Path(args.editor_root) / "Temp" / "VRBattlegroundsTestStand" / run_id).resolve()
                    for index, peer in enumerate(peers):
                        source = (directory / ("e2e-" + peer["ParticipantId"] + ".json")).resolve()
                        record = {"participant": peer, "workerPath": str(source)}
                        try:
                            if not source.is_relative_to(directory):
                                raise ValueError("Путь результата выходит за собственный run directory")
                            # Читаем durable файл после проверки точного worker, без лимита вывода MCP.
                            raw = source.read_text(encoding="utf-8") if source.is_file() else None
                            record["exists"] = raw is not None
                            if raw is not None:
                                local = output.with_name(output.stem + "-participant-" + str(index) + ".json")
                                local.write_text(raw, encoding="utf-8")
                                record["savedPath"] = str(local)
                                record["result"] = json.loads(raw)
                        except Exception as error:
                            record["error"] = type(error).__name__ + ": " + str(error)
                        report["results"].append(record)
                        save()

                try:
                    identity = await code("UnityEngine.Application.dataPath")
                    check("MCP адресует точный worker", normalized(identity) == normalized(args.editor_root) + "/assets", identity)
                    verified = True
                    logs.identify_main(await code(EDITOR_IDENTITY))
                    await asyncio.to_thread(logs.capture, 'before-play', force=True)
                    before = await status()
                    check("До запуска нет чужого Play/owner", before.get("State") == "Idle" and not before.get("Playing") and not before.get("Owner"), before)
                    editor_safe = await code("new { playing = UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode, dirty = System.Linq.Enumerable.Any(System.Linq.Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount), i => UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty), prefab = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() != null }")
                    check("Worker готов без грязных сцен/prefab stage", not any(editor_safe.values()), editor_safe)
                    configuration = {"Enabled": True, "Role": "server", "ClientCount": 1,
                                     "SceneSource": "scene", "ScenePath": "Assets/Scenes/Maps/TestMap2.unity",
                                     "ModeId": "elimination", "AutoGoLive": False, "BotCount": 0,
                                     "PauseOnFocusLoss": False, "Readiness": "map-playable",
                                     "TimeoutSeconds": args.ready_timeout}
                    report["configuration"] = configuration
                    plan = await code(LAUNCH + ".Plan(" + literal(configuration) + ")")
                    report["plan"] = plan
                    check("Plan прошёл", plan.get("Passed") and bool(plan.get("PlanHash")), plan)
                    play_attempted = True
                    active = await code(LAUNCH + ".Play(" + literal(configuration) + ", " + json.dumps(owner) + ", " + json.dumps(plan["PlanHash"]) + ")", True)
                    run_id = active.get("RunId")
                    report["runId"] = run_id
                    check("Play вернул свой RunId", bool(run_id) and active.get("Owner") == owner, active)

                    def ready(value):
                        ps = value.get("Participants", [])
                        return (value.get("Ready") and value.get("Owner") == owner and value.get("RunId") == run_id
                                and len(ps) == 2 and len({p.get("ParticipantId") for p in ps}) == 2
                                and all(p.get("RunId") == run_id and p.get("ProcessSessionId") and p.get("Active")
                                        and p.get("Playing") and p.get("LastSeenAgeSeconds", 99) <= 5
                                        and p.get("Scene") == "TestMap2" and p.get("MapPlayable") and p.get("MapRunValid") for p in ps)
                                and sum(bool(p.get("Server") and not p.get("Client")) for p in ps) == 1
                                and sum(bool(p.get("Client") and not p.get("Server") and p.get("Connected")
                                             and p.get("SessionNetId") and p.get("AvatarNetId")) for p in ps) == 1
                                and next(p for p in ps if p.get("Server")).get("ServerSessions", 0) == 1)

                    ready_state = await wait_status(ready, args.ready_timeout)
                    peers = sorted(ready_state["Participants"], key=lambda p: (not p["Server"], p["ParticipantId"]))
                    report["ready"] = ready_state
                    await asyncio.to_thread(logs.capture, 'ready', ready_state, True)
                    check("Свежие Ready/session/avatar всех участников", True)
                    for peer in peers:
                        request = {"RunId": run_id, "ParticipantId": peer["ParticipantId"],
                                   "ProcessSessionId": peer["ProcessSessionId"], "RequestId": uuid.uuid4().hex,
                                   "Action": "run-e2e", "TimeBudgetMs": 10000, "Scenario": SCENARIO,
                                   "ScenarioTimeoutSeconds": args.scenario_timeout}
                        record = {"request": request}
                        report["operations"].append(record)
                        operation = await code(STAND + ".Send(UnityEngine.JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.StandRequest>(" + literal(request) + "))", True)
                        deadline = time.monotonic() + 40
                        while not operation.get("Completed") and time.monotonic() < deadline:
                            await asyncio.sleep(0.5)
                            operation = await code(STAND + ".Operation(" + json.dumps(request["RequestId"]) + ")")
                        record["operation"] = operation
                        reply = operation.get("Reply") or {}
                        check("Команда подтверждена: " + peer["ParticipantId"], operation.get("Completed") and not operation.get("EffectUnknown")
                              and reply.get("Passed") and reply.get("Code") == "e2e-started"
                              and all(reply.get(k) == request[k] for k in ("RunId", "ParticipantId", "ProcessSessionId", "RequestId"))
                              and reply.get("ProcessId") == peer["ProcessId"], operation)

                    expected = {(p["ParticipantId"], p["ProcessSessionId"]) for p in peers}

                    def finished(value):
                        ps = value.get("Participants", [])
                        return (value.get("Owner") == owner and value.get("RunId") == run_id and len(ps) == 2
                                and {(p.get("ParticipantId"), p.get("ProcessSessionId")) for p in ps} == expected
                                and all(p.get("RunId") == run_id and p.get("LastSeenAgeSeconds", 99) <= 5
                                        and p.get("E2EFinished") for p in ps))

                    final = await wait_status(finished, args.scenario_timeout + 45)
                    report["e2eFinal"] = final
                    check("Все E2EFinished/E2EPassed", all(p.get("E2EPassed") for p in final["Participants"]), final)
                    report["passed"] = True
                except Exception as error:
                    report["error"] = type(error).__name__ + ": " + str(error)
                finally:
                    await asyncio.to_thread(logs.capture, 'before-cleanup', force=True)
                    if verified and play_attempted:
                        try:
                            current = await status()
                            await asyncio.to_thread(logs.capture, 'before-final-cancel', current, True)
                            if not run_id and current.get("Owner") == owner:
                                run_id = current.get("RunId")
                                report["runId"] = run_id
                            if run_id and peers:
                                await preserve_results()
                            if run_id and current.get("Owner") == owner and current.get("RunId") == run_id:
                                await code(LAUNCH + ".Cancel(" + json.dumps(run_id) + ", " + json.dumps(owner) + ")", True)
                                stopped = await wait_status(lambda s: s.get("State") == "Idle" and not s.get("Playing"), args.cleanup_timeout, False)
                                report["cleanup"] = stopped
                                check("Очистка своего запуска", stopped.get("CleanupPassed") and stopped.get("RunId") == run_id and not stopped.get("Owner"), stopped)
                            else:
                                check("Запуск уже очищен", bool(run_id) and current.get("State") == "Idle" and not current.get("Playing")
                                      and not current.get("Owner") and current.get("RunId") == run_id and current.get("CleanupPassed"), current)
                        except Exception as error:
                            report["cleanupError"] = type(error).__name__ + ": " + str(error)
                            report["passed"] = False
                    if report["passed"]:
                        results = report["results"]
                        report["passed"] = len(results) == 2 and all(r.get("result", {}).get("passed")
                            and r["result"].get("status") == "completed" and r["result"].get("scenario") == SCENARIO
                            and r["result"].get("role") == ("server" if r["participant"]["Server"] else "client")
                            and r["result"].get("checks") and all(c.get("passed") for c in r["result"]["checks"]) for r in results)
                        if not report["passed"]:
                            report["resultError"] = "Файлы участников отсутствуют или не подтверждают успех"
                    await asyncio.to_thread(logs.capture, 'after-cleanup', force=True)
                    report['passed'] = report['passed'] and report['logs']['coverageComplete']
                    save()
    except Exception as error:
        report["error"] = type(error).__name__ + ": " + str(error)
        report["passed"] = False
        save()
    return report


if __name__ == "__main__":
    def seconds_between(minimum, maximum):
        def parse(value):
            number = int(value)
            if not minimum <= number <= maximum:
                raise argparse.ArgumentTypeError("Нужен срок от " + str(minimum) + " до " + str(maximum) + " секунд")
            return number
        return parse

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--instance", required=True)
    parser.add_argument("--editor-root", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--ready-timeout", type=seconds_between(30, 600), default=180, metavar="SECONDS")
    parser.add_argument("--scenario-timeout", type=seconds_between(30, 600), default=240, metavar="SECONDS")
    parser.add_argument("--cleanup-timeout", type=seconds_between(30, 180), default=90, metavar="SECONDS")
    parser.add_argument("--call-timeout", type=seconds_between(10, 120), default=45, metavar="SECONDS")
    try:
        arguments = parser.parse_args()
        report = asyncio.run(run(arguments))
        print(json.dumps({"passed": report["passed"], "checks": len(report["checks"]),
                          "results": len(report["results"]), "runId": report.get("runId"),
                          "error": report.get("error"), "cleanupError": report.get("cleanupError"),
                          "report": str(Path(arguments.output).resolve())}, ensure_ascii=False))
        raise SystemExit(0 if report["passed"] else 1)
    except Exception as error:
        print(type(error).__name__ + ": " + str(error)[:1200], flush=True)
        raise SystemExit(1)
