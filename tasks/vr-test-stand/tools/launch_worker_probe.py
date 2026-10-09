"""Конечный пакет интеграции PlayLaunch через собственный broker MCP-proxy."""
import argparse
import asyncio
from datetime import timedelta
import json
from pathlib import Path
import subprocess
import time
import sys
import uuid
from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client
PROJECT_ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(PROJECT_ROOT / "Tools" / "TestStand"))
from worker_probe import unpack
from probe_log_capture import ProbeLogs, EDITOR_IDENTITY
from mcp_observation_retry import execute_observation, verify_observation_contract

LAUNCH = "VrBattlegrounds.EditorTools.TestStand.PlayLaunch"
STAND = "VrBattlegrounds.EditorTools.TestStand.PlayModeTestStand"
CONFIG = "VrBattlegrounds.DevTools.PlayLaunchConfiguration"


async def run(args):
    root = PROJECT_ROOT
    owner = "launch-probe-" + uuid.uuid4().hex
    output = Path(args.output).resolve()
    reports = root / 'tasks' / 'vr-test-stand' / 'reports'
    if not output.is_relative_to(reports.resolve()):
        raise ValueError('Отчёт должен находиться в tasks/vr-test-stand/reports/')
    if subprocess.run(['git', 'check-ignore', '--quiet', str(output)], cwd=root).returncode:
        raise ValueError('Путь отчёта должен быть исключён из Git')
    report = {"passed": False, "checks": [], "cases": [], "worktree": str(root), "owner": owner}
    report['observationContract'] = verify_observation_contract(args.editor_root)
    output.parent.mkdir(parents=True, exist_ok=True)
    logs = ProbeLogs(output, report, args.editor_root)
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")

    def check(name, condition, detail=None):
        report["checks"].append({"name": name, "passed": bool(condition), "detail": detail})
        if not condition:
            raise AssertionError(name + ": " + json.dumps(detail, ensure_ascii=False)[:1200])

    def save():
        output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')

    def literal(value):
        return json.dumps(json.dumps(value, separators=(",", ":")))

    server = StdioServerParameters(command="uv", args=["run", "--quiet", "Tools/agents/unity_mcp_proxy.py"], cwd=str(root))
    async with stdio_client(server) as (reader, writer):
        async with ClientSession(reader, writer, read_timeout_seconds=timedelta(seconds=600)) as session:
            await session.initialize()
            unpack(await session.call_tool("set_active_instance", {"instance": args.instance}))

            async def code(expression):
                mutation = expression.startswith((LAUNCH + '.Play(', LAUNCH + '.Cancel(', STAND + '.Send(')) or expression == 'VrBattlegrounds.EditorTools.TestStand.PlayLaunchSceneMigration.ApplyStandaloneMarkers()'
                item = {'expression': expression, 'mutation': mutation, 'started': time.time()}
                report.setdefault('calls', []).append(item)
                save()
                try:
                    async def send():
                        return await session.call_tool('execute_code', {'action': 'execute', 'code': 'return ' + expression + ';'})
                    value = await execute_observation(expression, send, unpack, item, save, mutation=mutation)
                    item['result'] = value
                    if logs.sources:
                        await asyncio.to_thread(logs.capture, 'call', value)
                    return value
                except Exception as error:
                    item['error'] = type(error).__name__ + ': ' + str(error)
                    if mutation:
                        item['effectUnknown'] = True
                    report["failedCall"] = {"expression": expression, "response": item['attempts'][-1].get('response'), 'error': item['error']}
                    raise
                finally:
                    save()

            async def wait(expression, condition, seconds=120):
                deadline = time.monotonic() + seconds
                latest = None
                while time.monotonic() < deadline:
                    latest = await code(expression)
                    if expression == LAUNCH + ".Status()":
                        sample = {"state": latest.get("State"), "error": latest.get("Error"), "run": latest.get("RunId"),
                                  "peers": [{k: p.get(k) for k in ("ParticipantId", "Role", "RequestedRole", "NetworkRole", "Scene", "Playing", "Server", "Client", "Connected", "StartupError")} for p in latest.get("Participants", [])]}
                        trace = report.setdefault("statusTrace", [])
                        if not trace or trace[-1] != sample:
                            trace.append(sample)
                            if len(trace) > 80:
                                del trace[0]
                    if condition(latest):
                        return latest
                    if expression == LAUNCH + ".Status()" and latest.get("State") == "Failed":
                        raise AssertionError("launch failed: " + json.dumps(latest, ensure_ascii=False)[:1800])
                    await asyncio.sleep(0.5)
                raise TimeoutError(expression + ": " + json.dumps(latest, ensure_ascii=False)[:1800])

            async def send(peer, run_id, action, marker="", epoch=None, avatar=0, request_id=None):
                request = {"RunId": run_id, "ParticipantId": peer["ParticipantId"], "ProcessSessionId": peer["ProcessSessionId"],
                           "RequestId": request_id or str(time.monotonic_ns()), "Action": action, "MarkerName": marker,
                           "TimeBudgetMs": 10000, "ExpectedConnectionEpoch": epoch if epoch is not None else peer["ConnectionEpoch"],
                           "ExpectedAvatarNetId": avatar}
                operation = await code(STAND + ".Send(UnityEngine.JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.StandRequest>(" + literal(request) + "))")
                if not operation["Completed"]:
                    operation = await wait(STAND + ".Operation(" + json.dumps(operation["RequestId"]) + ")", lambda o: o["Completed"], 40)
                check("команда имеет подтверждённый исход: " + action, not operation.get("EffectUnknown"), operation)
                return operation["Reply"]

            baseline_expression = ('new { nativeConfiguration = ' + STAND + '.Configuration(), '
                'startScene = UnityEditor.AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene), '
                'profile = System.IO.File.Exists(VrBattlegrounds.DevTools.PlayLaunchSettings.ProfilePath) ? '
                'System.IO.File.ReadAllText(VrBattlegrounds.DevTools.PlayLaunchSettings.ProfilePath) : null, '
                'prefs = string.Join("|", System.Linq.Enumerable.Select(new[] { "VrBattlegrounds.DebugBootstrap.Enabled", '
                '"VrBattlegrounds.DebugBootstrap.HostIsAdmin", "VrBattlegrounds.DebugBootstrap.AutoGoLive", '
                '"VrBattlegrounds.DebugBootstrap.AutoStartFallbackRole", "VrBattlegrounds.PauseXrWhenEditorUnfocused", '
                '"VrBattlegrounds.StartFromOffline" }, k => k + ":" + UnityEditor.EditorPrefs.HasKey(k) + ":" + UnityEditor.EditorPrefs.GetBool(k, false))), '
                'settings = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(new[] { "ProjectSettings/EditorSettings.asset", '
                '"ProjectSettings/EditorBuildSettings.asset" }, p => new { path = p, hash = System.Convert.ToBase64String('
                'System.Security.Cryptography.SHA256.Create().ComputeHash(System.IO.File.ReadAllBytes(' + json.dumps(args.editor_root.rstrip("/\\")) + ' + "/" + p))) })) }')
            active = None
            worker_verified = False
            try:
                identity = await code("UnityEngine.Application.dataPath")
                check("MCP указывает на worker", identity.replace("\\", "/").lower() == args.editor_root.replace("\\", "/").rstrip("/").lower() + "/assets", identity)
                worker_verified = True
                logs.identify_main(await code(EDITOR_IDENTITY))
                await asyncio.to_thread(logs.capture, 'before-compile', force=True)
                compile_result = await code("VrBattlegrounds.EditorTools.AndroidCompileGate.Run()")
                report["androidCompile"] = compile_result
                check("Android compile", compile_result.get("Passed", False), compile_result)
                if args.compile_only:
                    report["passed"] = True
                    return True
                migration = await code("VrBattlegrounds.EditorTools.TestStand.PlayLaunchSceneMigration.ApplyStandaloneMarkers()")
                report["migration"] = migration
                check("Standalone migration", migration["Passed"], migration)
                baseline = await code(baseline_expression)
                bad = await code(LAUNCH + '.Plan(' + literal({"Role": "unknown"}) + ')')
                check("неверная роль отказывает до Play", not bad["Passed"] and bad["Code"] == "RoleInvalid", bad)

                for name, configuration in [
                    ("server-two-clients", {"Role": "server", "ClientCount": 2, "SceneSource": "lobby", "ModeId": "", "AutoGoLive": False, "PauseOnFocusLoss": False}),
                    ("client-host", {"Role": "client", "ClientCount": 1, "AdditionalPlayerRoles": ["host"], "SceneSource": "lobby", "ModeId": "", "AutoGoLive": False, "PauseOnFocusLoss": False}),
                    ("game-server", {"Role": "server", "ClientCount": 0, "SceneSource": "scene", "ScenePath": "Assets/Scenes/Maps/TestMap2.unity", "ModeId": "elimination", "AutoGoLive": False, "PauseOnFocusLoss": False}),
                    ("disabled-game-server", {"Enabled": False, "Role": "server", "ClientCount": 0, "SceneSource": "scene", "ScenePath": "Assets/Scenes/Maps/TestMap2.unity", "ModeId": "elimination", "AutoGoLive": False, "PauseOnFocusLoss": False}),
                    ("standalone", {"Role": "host", "ClientCount": 0, "SceneSource": "scene", "ScenePath": "Assets/Scenes/Dev/ClipScrub.unity", "ModeId": "", "AutoGoLive": False, "PauseOnFocusLoss": False, "Readiness": "standalone"}),
                ]:
                    if name not in args.cases:
                        continue
                    print("case=" + name, flush=True)
                    await asyncio.to_thread(logs.capture, 'before-play-' + name, force=True)
                    plan = await code(LAUNCH + ".Plan(" + literal(configuration) + ")")
                    check("Plan " + name, plan["Passed"], plan)
                    active = await code(LAUNCH + ".Play(" + literal(configuration) + ', ' + json.dumps(owner) + ', ' + json.dumps(plan["PlanHash"]) + ")")
                    check("Play возвращает RunId " + name, bool(active.get("RunId")), active)
                    ready = await wait(LAUNCH + ".Status()", lambda s: s["Ready"] or s["State"] == "Failed", 150)
                    await asyncio.to_thread(logs.capture, 'ready-' + name, ready, True)
                    check("Ready " + name, ready["Ready"], ready)
                    report["cases"].append(ready)
                    peers = ready["Participants"]
                    check("все участники активны " + name, len(peers) == configuration["ClientCount"] + 1 and all(p["Active"] for p in peers), peers)
                    if name == "standalone":
                        check("standalone без сети", not any(p["Server"] or p["Client"] for p in peers), peers)
                    else:
                        check("ровно один сервер " + name, sum(p["Server"] for p in peers) == 1, peers)
                    if name == "server-two-clients":
                        # Ready подтверждает карту/сеть; появление игровых тел имеет отдельный срок.
                        bodies = await wait(LAUNCH + ".Status()", lambda s: s["Ready"] and
                            sum(p["Client"] for p in s["Participants"]) == 2 and
                            all(p["SessionNetId"] > 0 and p["AvatarNetId"] > 0 for p in s["Participants"] if p["Client"]), 90)
                        peers = bodies["Participants"]
                        report["initialBodies"] = bodies
                        target = next(p for p in peers if p["ParticipantId"] == "Player 2")
                        observer = next(p for p in peers if p["ParticipantId"] == "Player 3")
                        made = await send(target, ready["RunId"], "create-marker", "integration")
                        check("адресный маркер", made["Passed"] and made["MarkerExists"], made)
                        other = await send(observer, ready["RunId"], "read-marker", "integration")
                        check("наблюдатель не получил маркер", other["Passed"] and not other["MarkerExists"], other)
                        check("клиенты имеют сессии и аватары", all(p["SessionNetId"] > 0 and p["AvatarNetId"] > 0 for p in peers if p["Client"]), peers)
                        dropped = await send(target, ready["RunId"], "disconnect", avatar=target["AvatarNetId"])
                        check("disconnect принят владельцем", dropped["Passed"], dropped)
                        offline = await wait(LAUNCH + ".Status()", lambda s: any(p["ParticipantId"] == target["ParticipantId"] and not p["Connected"] and p["ClientManagerReady"] for p in s["Participants"]), 90)
                        disconnected = next(p for p in offline["Participants"] if p["ParticipantId"] == target["ParticipantId"])
                        check("сервер и второй клиент живы", any(p["Server"] for p in offline["Participants"]) and any(p["ParticipantId"] == observer["ParticipantId"] and p["Connected"] for p in offline["Participants"]), offline)
                        reconnect = await send(disconnected, ready["RunId"], "reconnect")
                        check("reconnect принят владельцем", reconnect["Passed"], reconnect)
                        restored = await wait(LAUNCH + ".Status()", lambda s: s["Ready"] and
                            any(p["ParticipantId"] == target["ParticipantId"] and p["ConnectionEpoch"] > target["ConnectionEpoch"] and p["AvatarNetId"] > 0 for p in s["Participants"]) and
                            any(p["Server"] and p["ServerSessions"] == 2 for p in s["Participants"]), 120)
                        returned = next(p for p in restored["Participants"] if p["ParticipantId"] == target["ParticipantId"])
                        check("identity сохранена", returned["DeviceToken"] == target["DeviceToken"], {"before": target, "after": returned})
                        check("нет лишних сессий", next(p for p in restored["Participants"] if p["Server"])["ServerSessions"] == 2, restored)
                        stale = await send(returned, ready["RunId"], "disconnect", epoch=target["ConnectionEpoch"])
                        check("старое подключение отклонено", not stale["Passed"] and stale["Code"] == "wrong-connection-epoch", stale)
                        if returned["AvatarNetId"] != target["AvatarNetId"]:
                            stale_body = await send(returned, ready["RunId"], "disconnect", avatar=target["AvatarNetId"])
                            check("старый аватар отклонён", not stale_body["Passed"] and stale_body["Code"] == "wrong-avatar", stale_body)
                        report["reconnect"] = restored
                    await asyncio.to_thread(logs.capture, 'before-cancel-' + name, force=True)
                    await code(LAUNCH + ".Cancel(" + json.dumps(ready["RunId"]) + ', ' + json.dumps(owner) + ')')
                    stopped = await wait(LAUNCH + ".Status()", lambda s: s["State"] == "Idle" and not s["Playing"], 90)
                    report.setdefault('caseCleanup', []).append({'case': name, 'status': stopped})
                    check("очистка " + name, stopped["CleanupPassed"], stopped)
                    active = None
                    after = await code(baseline_expression)
                    check("scenario/startScene/profile/prefs/ProjectSettings восстановлены " + name, baseline == after, {"before": baseline, "after": after})
                check("все заявленные случаи выполнены", len(report["cases"]) == len(args.cases))
                report["passed"] = True
            except Exception as error:
                report["error"] = type(error).__name__ + ": " + str(error)
                raise
            finally:
                await asyncio.to_thread(logs.capture, 'before-cleanup', force=True)
                try:
                    if worker_verified:
                        current = await code(LAUNCH + ".Status()")
                        report['cleanupBefore'] = current
                        await asyncio.to_thread(logs.capture, 'before-final-cancel', current, True)
                        if current.get("Owner") == owner:
                            await code(LAUNCH + ".Cancel(" + json.dumps(current["RunId"]) + ', ' + json.dumps(owner) + ')')
                            stopped = await wait(LAUNCH + ".Status()", lambda s: not s["Playing"] and s["State"] in ("Idle", "Failed"), 90)
                            report['cleanup'] = stopped
                            if not stopped.get("CleanupPassed"):
                                raise AssertionError("cleanup verification failed")
                        else:
                            report['cleanup'] = current
                except Exception as error:
                    report["cleanupError"] = str(error)
                    report["passed"] = False
                await asyncio.to_thread(logs.capture, 'after-cleanup', force=True)
                report['passed'] = report['passed'] and report['logs']['coverageComplete']
                output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    return report["passed"]


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--instance", required=True)
    parser.add_argument("--editor-root", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--compile-only", action="store_true")
    parser.add_argument("--case", dest="cases", action="append", choices=["server-two-clients", "client-host", "game-server", "disabled-game-server", "standalone"])
    args = parser.parse_args()
    args.cases = list(dict.fromkeys(args.cases or ["server-two-clients", "client-host", "game-server"]))
    try:
        completed = asyncio.run(run(args))
        saved = json.loads(Path(args.output).read_text(encoding="utf-8"))
        if not completed or not saved.get("passed"):
            raise SystemExit(1)
    except Exception as error:
        print(type(error).__name__ + ": " + str(error)[:1800], flush=True)
        raise SystemExit(1)
