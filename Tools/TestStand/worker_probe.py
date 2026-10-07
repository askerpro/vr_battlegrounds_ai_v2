"""Ограниченная проба через штатный stdio-прокси своего worktree, только в своей аренде."""
import argparse
import asyncio
from datetime import timedelta
import json
from pathlib import Path
import time

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client

FACADE = "VrBattlegrounds.EditorTools.TestStand.PlayModeTestStand"


def unpack(reply):
    if getattr(reply, "isError", False):
        raise RuntimeError("MCP отказал: " + " ".join(c.text for c in reply.content if hasattr(c, "text"))[:1000])
    values = [c.text for c in reply.content if hasattr(c, "text")]
    if not values:
        raise RuntimeError("Нет ответа MCP")
    value = json.loads(values[0])
    for _ in range(10):
        if isinstance(value, str):
            try:
                value = json.loads(value)
                continue
            except json.JSONDecodeError:
                return value
        if not isinstance(value, dict):
            return value
        if value.get("success") is False:
            raise RuntimeError(str(value.get("error") or value.get("message"))[:1200])
        if "result" in value:
            value = value["result"]
        elif "data" in value:
            value = value["data"]
        else:
            return value
    raise RuntimeError("Неизвестная обёртка MCP")


async def main(args):
    root = Path(__file__).resolve().parents[2]
    output = Path(args.output)
    report = {"passed": False, "checks": [], "cases": [], "worktree": str(root)}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    def check(name, condition, detail=None):
        report["checks"].append({"name": name, "passed": bool(condition), "detail": detail})
        if not condition:
            raise AssertionError(name + ": " + str(detail))

    server = StdioServerParameters(command="uv", args=["run", "--quiet", "Tools/agents/unity_mcp_proxy.py"], cwd=str(root))
    async with stdio_client(server) as (reader, writer):
        async with ClientSession(reader, writer, read_timeout_seconds=timedelta(seconds=600)) as session:
            await session.initialize()
            unpack(await session.call_tool("set_active_instance", {"instance": args.instance}))
            async def code(expression):
                reply = await session.call_tool("execute_code", {"action": "execute", "code": "return " + expression + ";"})
                return unpack(reply)
            async def wait(expression, predicate, seconds=120):
                deadline = time.monotonic() + seconds
                last = None
                while time.monotonic() < deadline:
                    last = await code(expression)
                    if predicate(last):
                        return last
                    await asyncio.sleep(0.5)
                raise TimeoutError("Ожидание " + expression + ": " + json.dumps(last, ensure_ascii=False)[:1200])
            async def send(request):
                literal = json.dumps(json.dumps(request, separators=(",", ":")))
                op = await code(FACADE + ".Send(UnityEngine.JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.StandRequest>(" + literal + "))")
                if not op["Completed"]:
                    op = await wait(FACADE + ".Operation(" + json.dumps(op["RequestId"]) + ")", lambda x: x["Completed"], 40)
                check("ответ имеет известный исход", not op.get("EffectUnknown"), op)
                return op["Reply"]
            def request(state, run, action, marker="probe", request_id=None):
                return {"RunId": run, "ParticipantId": state["ParticipantId"], "ProcessSessionId": state["ProcessSessionId"],
                        "RequestId": request_id or str(time.monotonic_ns()), "Action": action, "MarkerName": marker, "TimeBudgetMs": 10000}
            manager = ('System.Linq.Enumerable.First(System.Linq.Enumerable.Select(System.AppDomain.CurrentDomain.GetAssemblies(), '
                       'a => a.GetType("Unity.PlayMode.Editor.PlayModeScenarioManager", false)), t => t != null)')
            flags = "System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic"
            async def normal_play():
                await code(manager + '.GetMethod("Start", ' + flags + ').Invoke(null, null)')
                await wait("UnityEditor.EditorApplication.isPlaying", bool, 120)
                await asyncio.sleep(2)
                profile = await code('new { profile = VrBattlegrounds.Core.LocalClientProfile.OverrideStateKey, temporary = VrBattlegrounds.Core.LocalClientProfile.HasTemporaryOverride, identityTemporary = VrBattlegrounds.Core.ClientDeviceIdentity.HasTemporaryToken, token = VrBattlegrounds.Core.ClientDeviceIdentity.BaseToken }')
                await code(manager + '.GetMethod("Stop", ' + flags + ').Invoke(null, null)')
                await wait('new { playing = UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode, state = ' + manager + '.GetProperty("State", ' + flags + ').GetValue(null).ToString() }', lambda s: not s["playing"] and s["state"] == "Idle", 90)
                return profile
            play_options = None
            normal_active = False
            try:
                identity = await code("new { root = UnityEngine.Application.dataPath, main = Unity.Multiplayer.PlayMode.CurrentPlayer.IsMainEditor }")
                check("MCP направлен в worker", identity["root"].replace("\\", "/").lower() == args.editor_root.replace("\\", "/").rstrip("/").lower() + "/assets" and identity["main"], identity)
                compile_result = await code("VrBattlegrounds.EditorTools.AndroidCompileGate.Run()")
                report["androidCompile"] = compile_result
                check("AndroidCompileGate", bool(compile_result.get("Passed", compile_result.get("passed", False))), compile_result)
                if args.compile_only:
                    report["passed"] = True
                    return True
                play_options = await code("new { enabled = UnityEditor.EditorSettings.enterPlayModeOptionsEnabled, options = (int)UnityEditor.EditorSettings.enterPlayModeOptions }")
                normal_active = True
                normal_before = await normal_play()
                normal_active = False
                check("обычный Play до стенда без временных областей", not normal_before["temporary"] and not normal_before["identityTemporary"], normal_before)
                old_request = None
                for enabled in (False, True):
                    print("case bootstrap=" + str(enabled), flush=True)
                    await code("(UnityEditor.EditorSettings.enterPlayModeOptionsEnabled = true)")
                    await code("(UnityEditor.EditorSettings.enterPlayModeOptions = " + ("UnityEditor.EnterPlayModeOptions.DisableDomainReload" if enabled else "UnityEditor.EnterPlayModeOptions.None") + ")")
                    configuration = await code(FACADE + ".Configuration()")
                    before = await code("new { profile = VrBattlegrounds.Core.LocalClientProfile.OverrideStateKey, token = UnityEngine.PlayerPrefs.GetString(\"DeviceToken\", \"\"), present = UnityEngine.PlayerPrefs.HasKey(\"DeviceToken\") }")
                    await code(FACADE + ".StartProbe(" + str(enabled).lower() + ")")
                    status = await wait(FACADE + ".Status()", lambda s: len(s.get("Participants", [])) == 3 and all(p["Playing"] and p["Active"] for p in s["Participants"]) and sum(p["Server"] and not p["Client"] for p in s["Participants"]) == 1 and sum(p["Client"] and p["Connected"] for p in s["Participants"]) == 2, 180)
                    report["cases"].append(status)
                    peers = status["Participants"]
                    check("три адреса участника", len({p["ParticipantId"] for p in peers}) == 3)
                    check("явный профиль каждого участника", all(p["ProfileTemporary"] and p["DeviceType"] == "VR" and not p["Admin"] and p["GameRole"] == "Player" and p["PersistentTokenUnchanged"] for p in peers), peers)
                    if old_request:
                        stale = await send(old_request)
                        check("старый запуск отклонён", not stale["Passed"] and stale["Code"] == "wrong-run", stale)
                    target = next(p for p in peers if p["ParticipantId"] == "Player 2")
                    create = request(target, status["RunId"], "create-marker", request_id="create")
                    made = await send(create)
                    check("маркер у выбранного клиента", made["Passed"] and made["MarkerExists"] and made["MarkerCount"] == 1 and made["ProcessId"] == target["ProcessId"], made)
                    duplicate = await send(create)
                    check("повтор без второго маркера", duplicate["Passed"] and duplicate["MarkerCount"] == 1, duplicate)
                    conflict = dict(create, MarkerName="conflict")
                    denied = await send(conflict)
                    check("конфликт RequestId отклонён", not denied["Passed"] and denied["Code"] == "request-conflict", denied)
                    for peer in peers:
                        read = await send(request(peer, status["RunId"], "read-marker"))
                        check("маркер только у цели " + peer["ParticipantId"], read["Passed"] and read["MarkerExists"] == (peer["ParticipantId"] == target["ParticipantId"]), read)
                    bad = request(target, status["RunId"], "create-marker", "invalid_target")
                    bad["ProcessSessionId"] = "old-session"
                    denied = await send(bad)
                    check("неверная сессия отклонена", not denied["Passed"] and denied["Code"] == "wrong-session", denied)
                    bad_target = dict(bad, ParticipantId="missing-player", RequestId="wrong-participant")
                    denied = await send(bad_target)
                    check("неверный участник отклонён", not denied["Passed"] and denied["Code"] == "target-unavailable", denied)
                    old_request = request(target, status["RunId"], "create-marker", "old_run", "old-run-request")
                    await code(FACADE + ".Stop()")
                    stopped = await wait(FACADE + ".Status()", lambda s: s["Phase"] == "idle" and not s["Playing"], 90)
                    report["cases"].append(stopped)
                    check("очистка всех участников подтверждена", stopped["CleanupPassed"], stopped)
                    after = await code("new { profile = VrBattlegrounds.Core.LocalClientProfile.OverrideStateKey, token = UnityEngine.PlayerPrefs.GetString(\"DeviceToken\", \"\"), present = UnityEngine.PlayerPrefs.HasKey(\"DeviceToken\") }")
                    check("исходный профиль и идентичность восстановлены", before == after, {"before": before, "after": after})
                    restored_configuration = await code(FACADE + ".Configuration()")
                    check("сценарий и личные EditorPrefs восстановлены", configuration == restored_configuration, {"before": configuration, "after": restored_configuration})
                    denied = await send(dict(old_request, RequestId="after-stop"))
                    check("новая команда после Stop отклонена", not denied["Passed"] and denied["Code"] == "inactive", denied)
                await code("(UnityEditor.EditorSettings.enterPlayModeOptions = (UnityEditor.EnterPlayModeOptions)" + str(play_options["options"]) + ")")
                await code("(UnityEditor.EditorSettings.enterPlayModeOptionsEnabled = " + str(play_options["enabled"]).lower() + ")")
                normal_active = True
                normal_after = await normal_play()
                normal_active = False
                check("обычный Play после стенда сохраняет профиль и идентичность", normal_before == normal_after, {"before": normal_before, "after": normal_after})
                check("непустой полный набор выполненных проверок", len(report["cases"]) == 4 and len(report["checks"]) >= 20 and all(c["passed"] for c in report["checks"]))
                report["passed"] = True
            except Exception as error:
                report["error"] = type(error).__name__ + ": " + str(error)
                raise
            finally:
                if not args.compile_only:
                    try:
                        if normal_active:
                            await code(manager + '.GetMethod("Stop", ' + flags + ').Invoke(null, null)')
                            await wait("UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode", lambda s: not s, 90)
                        await code(FACADE + ".Stop()")
                        await wait(FACADE + ".Status()", lambda s: s["Phase"] == "idle" and not s["Playing"], 90)
                    except Exception as cleanup_error:
                        report["cleanupError"] = str(cleanup_error)
                        report["passed"] = False
                if play_options is not None:
                    try:
                        await code("(UnityEditor.EditorSettings.enterPlayModeOptions = (UnityEditor.EnterPlayModeOptions)" + str(play_options["options"]) + ")")
                        await code("(UnityEditor.EditorSettings.enterPlayModeOptionsEnabled = " + str(play_options["enabled"]).lower() + ")")
                    except Exception as restore_error:
                        report["restoreOptionsError"] = str(restore_error)
                        report["passed"] = False
                output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")
    return report["passed"]


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--instance", required=True)
    parser.add_argument("--editor-root", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--compile-only", action="store_true")
    arguments = parser.parse_args()
    try:
        if not asyncio.run(main(arguments)):
            raise SystemExit(1)
    except Exception as error:
        output = Path(arguments.output)
        if not output.exists():
            output.parent.mkdir(parents=True, exist_ok=True)
            output.write_text(json.dumps({"passed": False, "error": type(error).__name__ + ": " + str(error), "checks": []}, ensure_ascii=False, indent=2), encoding="utf-8")
        print(type(error).__name__ + ": " + str(error)[:1600], flush=True)
        raise SystemExit(1)
