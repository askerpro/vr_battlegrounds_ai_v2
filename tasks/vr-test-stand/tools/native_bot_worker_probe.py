"""Конечная native Start/T01 проба; запускать только под собственным worker lease/guard."""
import argparse
import asyncio
import base64
from datetime import timedelta
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import sys
import time

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client

PROJECT_ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(PROJECT_ROOT / "Tools" / "TestStand"))
from worker_probe import unpack
from probe_log_capture import ProbeLogs, EDITOR_IDENTITY
from mcp_observation_retry import execute_observation, verify_observation_contract

LAUNCH = "VrBattlegrounds.EditorTools.TestStand.PlayLaunch"
STAND = "VrBattlegrounds.EditorTools.TestStand.PlayModeTestStand"
SETTINGS = "VrBattlegrounds.DevTools.PlayLaunchSettings"
BOT = "VrBattlegrounds.EditorTools.BotCombatStandEditor"
RUNTIME_BOT = "VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand"
PREFIX = '''var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static;
var manager = System.Linq.Enumerable.First(System.Linq.Enumerable.Select(System.AppDomain.CurrentDomain.GetAssemblies(), a => a.GetType("Unity.PlayMode.Editor.PlayModeScenarioManager", false)), t => t != null);
'''
CONFIGS = '''var utils = System.Linq.Enumerable.First(System.Linq.Enumerable.Select(System.AppDomain.CurrentDomain.GetAssemblies(), a => a.GetType("Unity.PlayMode.Editor.PlayModeScenarioUtils", false)), t => t != null);
var configs = System.Linq.Enumerable.Where(System.Linq.Enumerable.Cast<UnityEngine.Object>((System.Collections.IEnumerable)utils.GetMethod("GetAllConfigs", flags).Invoke(null, null)), s => s != null);
'''
SNAPSHOT = PREFIX + '''var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
var bootstrap = VrBattlegrounds.Maps.Runtime.MapBootstrap.ForScene(scene);
var launch = UnityEngine.JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
return new {
    NativeState = manager.GetProperty("State", flags).GetValue(null).ToString(),
    Playing = UnityEditor.EditorApplication.isPlaying, Transition = UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode,
    Compiling = UnityEditor.EditorApplication.isCompiling,
    PrefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() != null,
    Dirty = !UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode && System.Linq.Enumerable.Any(System.Linq.Enumerable.Range(0, UnityEngine.SceneManagement.SceneManager.sceneCount), i => UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty),
    Scene = scene.name, Server = Mirror.NetworkServer.active, Client = Mirror.NetworkClient.active,
    Connected = Mirror.NetworkClient.isConnected, Loading = Mirror.NetworkServer.isLoadingScene,
    MapPlayable = VrBattlegrounds.Maps.Runtime.MapRunAdmission.IsLocalPlayable,
    MapRunValid = bootstrap != null && bootstrap.LocalRunKey.IsValid,
    MapRunKey = bootstrap == null ? "" : bootstrap.LocalRunKey.ToString(),
    RequestActive = VrBattlegrounds.DevTools.PlayLaunchSettings.RequestActive,
    Effective = VrBattlegrounds.DevTools.PlayLaunchSettings.Effective,
    Launch = launch
};'''
BASELINE = PREFIX + '''var scenario = (UnityEngine.Object)manager.GetProperty("ActiveScenario", flags).GetValue(null);
return new {
    ScenarioPath = UnityEditor.AssetDatabase.GetAssetPath(scenario), ScenarioName = scenario.name,
    ScenarioJson = UnityEditor.EditorJsonUtility.ToJson(scenario),
    StartScene = UnityEditor.AssetDatabase.GetAssetPath(UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene),
    Scenes = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(UnityEditor.SceneManagement.EditorSceneManager.GetSceneManagerSetup(), s => new { s.path, s.isLoaded, s.isActive })),
    ProfilePath = VrBattlegrounds.DevTools.PlayLaunchSettings.ProfilePath,
    Profile = VrBattlegrounds.DevTools.PlayLaunchSettings.ReadProfile(),
    Prefs = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(new[] { "VrBattlegrounds.DebugBootstrap.Enabled", "VrBattlegrounds.DebugBootstrap.HostIsAdmin", "VrBattlegrounds.DebugBootstrap.AutoGoLive", "VrBattlegrounds.DebugBootstrap.AutoStartFallbackRole", "VrBattlegrounds.PauseXrWhenEditorUnfocused", "VrBattlegrounds.StartFromOffline" }, k => new { Key = k, Present = UnityEditor.EditorPrefs.HasKey(k), Value = UnityEditor.EditorPrefs.GetBool(k, false) })),
    TokenPresent = UnityEngine.PlayerPrefs.HasKey("DeviceToken"),
    TokenHash = System.Convert.ToBase64String(System.Security.Cryptography.SHA256.Create().ComputeHash(System.Text.Encoding.UTF8.GetBytes(UnityEngine.PlayerPrefs.GetString("DeviceToken", ""))))
};'''
CONFIG = dict(Enabled=True, SceneSource="scene", ScenePath="Assets/Scenes/Maps/TestMap2.unity",
              Role="host", ClientCount=0, HostIsAdmin=True, ModeId="elimination",
              AutoGoLive=False, BotCount=0, PauseOnFocusLoss=False, Readiness="map-playable")


def literal(value):
    return json.dumps(json.dumps(value, ensure_ascii=False), ensure_ascii=False)


def idle(value):
    return (value["NativeState"] == "Idle" and not value["Transition"] and not value["RequestActive"]
            and not value["Launch"].get("Owner") and not value["Server"] and not value["Client"])


async def run(args):
    output = Path(args.output).resolve()
    reports = (PROJECT_ROOT / "tasks/vr-test-stand/reports").resolve()
    if not output.is_relative_to(reports):
        raise ValueError("Отчёт должен быть в собственном tasks/vr-test-stand/reports")
    if subprocess.run(["git", "check-ignore", "--quiet", str(output)], cwd=PROJECT_ROOT).returncode:
        raise ValueError("Отчёт не игнорируется Git")
    output.parent.mkdir(parents=True, exist_ok=True)
    evidence = output.with_name(output.stem + "-artifacts")
    evidence.mkdir(exist_ok=True)
    worker = Path(args.editor_root).resolve()
    report = {"passed": False, "owner": "native-play", "entrypoint": "native Start; physical toolbar click not verified",
              "userAcceptance": "pending", "checks": [], "calls": [], "artifacts": str(evidence)}
    logs = ProbeLogs(output, report, worker)

    def save():
        output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding="utf-8")

    def check(name, passed, details=None):
        report["checks"].append({"name": name, "passed": bool(passed), "details": details})
        save()
        if not passed:
            raise RuntimeError(name)

    def hashes():
        paths = [worker / "ProjectSettings/EditorBuildSettings.asset", worker / "ProjectSettings/EditorSettings.asset",
                 worker / "Assets/Data/Maps/MapRuntimeCatalog.asset"]
        paths += list((worker / "Assets/Settings/PlayMode").glob("*.asset*"))
        paths += list((worker / "Assets/Settings").rglob("*Bootstrap*.asset"))
        return {str(p.relative_to(worker)): hashlib.sha256(p.read_bytes()).hexdigest() if p.is_file() else None for p in paths}

    verified = False
    main_identified = False
    baseline = None
    profile_path = None
    profile_bytes = None
    mutated = False
    phase = None
    run_ids = {}
    stop_attempted = set()
    restore_progress = {}
    restored = set()
    server = StdioServerParameters(command="uv", args=["run", "--quiet", "Tools/agents/unity_mcp_proxy.py"], cwd=str(PROJECT_ROOT))
    try:
        async with stdio_client(server) as (reader, writer):
            async with ClientSession(reader, writer, read_timeout_seconds=timedelta(seconds=args.call_timeout)) as session:
                await session.initialize()
                selected = await session.call_tool("set_active_instance", {"instance": args.instance})
                report["instanceSelection"] = selected.model_dump(mode="json")
                save()
                unpack(selected)

                async def code(source, mutation=False, expression=None, safety_checks=True):
                    item = {"code": source, "mutation": mutation, "started": time.time(), "safety_checks": safety_checks}
                    report["calls"].append(item)
                    save()
                    try:
                        async def send():
                            return await asyncio.wait_for(session.call_tool("execute_code", {"action": "execute", "code": source, "safety_checks": safety_checks}), args.call_timeout)
                        result = await execute_observation(expression or "bounded-native-observation", send, unpack, item, save,
                                                           mutation=mutation, budget=args.call_timeout)
                        item["result"] = result
                        if isinstance(result, dict) and "__mcp_output" in result:
                            raise RuntimeError("MCP output guard: результат сохранён в durable report, вызов не повторяется")
                        return result
                    except Exception as error:
                        item["error"] = type(error).__name__ + ": " + str(error)
                        if mutation:
                            item["effectUnknown"] = True
                        raise
                    finally:
                        if verified and main_identified:
                            await asyncio.to_thread(logs.capture, "call")
                        save()

                async def poll(source, condition, seconds, expression=None):
                    deadline = time.monotonic() + seconds
                    latest = None
                    while time.monotonic() < deadline:
                        try:
                            latest = await code(source, expression=expression)
                        except Exception as error:
                            # Независимые read-only observations после reload; mutation никогда не повторяется.
                            report.setdefault("pollErrors", []).append(str(error))
                            await asyncio.sleep(1)
                            continue
                        if condition(latest):
                            return latest
                        await asyncio.sleep(1)
                    raise TimeoutError("Конечный срок наблюдения: " + json.dumps(latest, ensure_ascii=False)[:1200])

                async def launch():
                    state = await code("return " + LAUNCH + ".Status();", expression=LAUNCH + ".Status()")
                    logs.owner = "BotCombatStand" if phase == "bot" else "native-play"
                    logs.observe(state)
                    return state

                async def cleanup():
                    current = await poll(SNAPSHOT, lambda s: True, args.cleanup_timeout)
                    state = current["Launch"]
                    expected_owner = "native-play" if phase == "native" else "BotCombatStand"
                    if state.get("Owner") and phase not in stop_attempted:
                        check("Cleanup owner совпадает", state.get("Owner") == expected_owner, state)
                        check("Cleanup run совпадает", bool(state.get("RunId")) and
                              (phase not in run_ids or run_ids[phase] == state["RunId"]), state)
                        run_ids[phase] = state["RunId"]
                        logs.owner = expected_owner
                        logs.observe(state)
                        stop_attempted.add(phase)
                        if phase == "native":
                            check("Native cleanup frozen config", all(current["Effective"].get(k) == v for k, v in CONFIG.items()), current["Effective"])
                            await code(PREFIX + 'manager.GetMethod("Stop", flags).Invoke(null, null); return "native-stop-issued";', True)
                        else:
                            await code("return " + LAUNCH + ".Cancel(" + json.dumps(state["RunId"]) + ', "BotCombatStand");', True)
                    elif not idle(current) and phase not in stop_attempted:
                        raise RuntimeError("Нет подтверждённого собственного owner для остановки занятого Editor")
                    stopped = await poll(SNAPSHOT, idle, args.cleanup_timeout)
                    report.setdefault("cleanup", {})[phase] = stopped
                    if phase == "bot" and phase in run_ids:
                        check("Managed cleanup подтверждён", stopped["Launch"].get("CleanupPassed") and stopped["Launch"].get("RunId") == run_ids[phase], stopped)
                        bot_state = await code("return " + BOT + ".Status();")
                        check("Bot owner освобождён", not bot_state["owned"], bot_state)
                    await asyncio.to_thread(logs.capture, phase + "-after-cleanup", stopped["Launch"], True)

                async def restore():
                    guard = await code(SNAPSHOT)
                    check("Restore только idle/clean/no prefab/compile", idle(guard) and not guard["Dirty"] and not guard["PrefabStage"] and not guard["Compiling"], guard)
                    if phase in restored:
                        check("Restored baseline остаётся точным", await code(BASELINE) == baseline)
                        return
                    progress = restore_progress.setdefault(phase, {})
                    report["restoreProgress"] = restore_progress
                    errors = []

                    async def step(name, source, matches, safety_checks=True):
                        observed = await code(BASELINE)
                        item = progress.setdefault(name, {"attempted": False, "completed": False})
                        if matches(observed):
                            item["completed"] = True
                            item["verifiedByFreshObservation"] = True
                            save()
                            return
                        if item["attempted"]:
                            errors.append(name + ": effect not established; mutation replay forbidden")
                            return
                        item["attempted"] = True
                        save()
                        try:
                            await code(source, True, safety_checks=safety_checks)
                            await asyncio.sleep(1)
                            item["completed"] = bool(matches(await code(BASELINE)))
                            if not item["completed"]:
                                errors.append(name + ": postcondition differs")
                        except Exception as error:
                            item["error"] = str(error)
                            # Остальные independent cleanup steps всё равно нужны; finally проверит эффект заново.
                            errors.append(name + ": " + str(error))
                        save()
                    # Worker изменяется через защищённый MCP, включая временный профиль.
                    path_literal = json.dumps(str(profile_path))
                    if profile_bytes is None:
                        profile_restore = ('if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode || ' + SETTINGS + '.RequestActive || '
                                           + 'System.IO.Path.GetFullPath(' + SETTINGS + '.ProfilePath) != System.IO.Path.GetFullPath(' + path_literal + ')) '
                                           + 'throw new System.InvalidOperationException("Temporary profile restore guard failed"); '
                                           + 'if (System.IO.File.Exists(' + path_literal + ') && UnityEngine.JsonUtility.ToJson(' + SETTINGS + '.ReadProfile()) != '
                                           + 'UnityEngine.JsonUtility.ToJson(' + SETTINGS + '.Decode(' + literal(CONFIG) + '))) '
                                           + 'throw new System.InvalidOperationException("Temporary profile changed; refusing delete"); '
                                           + 'System.IO.File.Delete(' + path_literal + ');')
                    else:
                        encoded = json.dumps(base64.b64encode(profile_bytes).decode())
                        profile_restore = ('System.IO.File.WriteAllBytes(' + path_literal + ', System.Convert.FromBase64String(' + encoded + ')); '
                                           + 'System.IO.File.SetLastWriteTimeUtc(' + path_literal + ', System.DateTime.UtcNow.AddSeconds(2));')
                    # Только удаление известного временного UserSettings файла требует отключения static pattern guard.
                    await step("profile", profile_restore + ' return "profile-exact-bytes-restored";',
                               lambda s: (profile_path.read_bytes() if profile_path.exists() else None) == profile_bytes
                               and s["Profile"] == baseline["Profile"], safety_checks=profile_bytes is not None)
                    scenario_match = ('UnityEditor.AssetDatabase.GetAssetPath(s) == ' + json.dumps(baseline["ScenarioPath"]) if baseline["ScenarioPath"]
                                      else 'string.IsNullOrEmpty(UnityEditor.AssetDatabase.GetAssetPath(s)) && s.name == ' + json.dumps(baseline["ScenarioName"]))
                    await step("scenario", PREFIX + CONFIGS + 'var matches = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Where(configs, s => ' + scenario_match + ')); '
                               + 'if (matches.Length != 1 || UnityEditor.EditorJsonUtility.ToJson(matches[0]) != ' + json.dumps(baseline["ScenarioJson"]) + ') throw new System.InvalidOperationException("Original native scenario unknown/ambiguous/changed"); '
                               + 'manager.GetProperty("ActiveScenario", flags).SetValue(null, matches[0]); return UnityEngine.JsonUtility.ToJson(' + SETTINGS + '.ReadProfile());',
                               lambda s: all(s[k] == baseline[k] for k in ("ScenarioPath", "ScenarioName", "ScenarioJson")))
                    scene_guard = PREFIX + '''if (UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode || VrBattlegrounds.DevTools.PlayLaunchSettings.RequestActive || manager.GetProperty("State", flags).GetValue(null).ToString() != "Idle" || UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() != null || UnityEditor.EditorApplication.isCompiling)
throw new System.InvalidOperationException("Scene restore requires owned idle Editor");
for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++) if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty) throw new System.InvalidOperationException("Dirty scene blocks restore");
'''
                    if not baseline["Scenes"]:
                        scene_restore = scene_guard + 'UnityEditor.SceneManagement.EditorSceneManager.NewScene(UnityEditor.SceneManagement.NewSceneSetup.EmptyScene, UnityEditor.SceneManagement.NewSceneMode.Single); return "empty-scene-setup-restored";'
                    else:
                        check("Saved scene setup named/valid", all(s["path"] and (worker / s["path"]).is_file() for s in baseline["Scenes"])
                              and sum(s["isActive"] for s in baseline["Scenes"]) == 1)
                        entries = ["new UnityEditor.SceneManagement.SceneSetup { path = " + json.dumps(s["path"]) + ", isLoaded = " + str(s["isLoaded"]).lower() + ", isActive = " + str(s["isActive"]).lower() + " }" for s in baseline["Scenes"]]
                        scene_restore = scene_guard + 'UnityEditor.SceneManagement.EditorSceneManager.RestoreSceneManagerSetup(new UnityEditor.SceneManagement.SceneSetup[] { ' + ', '.join(entries) + ' }); return "named-scene-setup-restored";'
                    await step("scenes", scene_restore, lambda s: s["Scenes"] == baseline["Scenes"])
                    await step("startScene", 'UnityEditor.SceneManagement.EditorSceneManager.playModeStartScene = UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEditor.SceneAsset>(' + json.dumps(baseline["StartScene"]) + '); return "start-scene-restored";',
                               lambda s: s["StartScene"] == baseline["StartScene"])
                    after = await code(BASELINE)
                    report["baselineAfter"] = after
                    report.setdefault("restoreObservations", []).append({"phase": phase, "errors": errors, "after": after})
                    check("Baseline native/profile/scenes/prefs/token восстановлен", after == baseline, {"before": baseline, "after": after})
                    check("Baseline assets/settings hashes восстановлены", hashes() == report["baselineHashes"], hashes())
                    check("Profile exact bytes восстановлены", (profile_path.read_bytes() if profile_path.exists() else None) == profile_bytes)
                    restored.add(phase)

                try:
                    identity_root = await code("return UnityEngine.Application.dataPath;")
                    check("Точный worker root", str(identity_root).replace("\\", "/").casefold() == (str(worker).replace("\\", "/") + "/Assets").casefold(), identity_root)
                    verified = True
                    logs.identify_main(await code("return " + EDITOR_IDENTITY + ";"))
                    main_identified = True
                    report["observationContract"] = verify_observation_contract(worker)
                    before = await code(SNAPSHOT)
                    check("Worker idle/clean/no prefab/compile", idle(before) and not before["Compiling"] and not before["Dirty"] and not before["PrefabStage"], before)
                    baseline = await code(BASELINE)
                    original_match = ('UnityEditor.AssetDatabase.GetAssetPath(s) == ' + json.dumps(baseline["ScenarioPath"]) if baseline["ScenarioPath"]
                                      else 'string.IsNullOrEmpty(UnityEditor.AssetDatabase.GetAssetPath(s)) && s.name == ' + json.dumps(baseline["ScenarioName"]))
                    candidates = await code(PREFIX + CONFIGS + 'return System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(System.Linq.Enumerable.Where(configs, s => '
                                            + original_match + '), s => new { Path = UnityEditor.AssetDatabase.GetAssetPath(s), Name = s.name, Json = UnityEditor.EditorJsonUtility.ToJson(s) }));')
                    matches = [s for s in candidates if (s["Path"] == baseline["ScenarioPath"] if baseline["ScenarioPath"]
                                                        else not s["Path"] and s["Name"] == baseline["ScenarioName"])]
                    check("Один исходный native scenario GetAllConfigs", len(matches) == 1 and matches[0]["Json"] == baseline["ScenarioJson"], matches)
                    profile_path = Path(baseline["ProfilePath"]).resolve()
                    check("Профиль внутри worker UserSettings", profile_path == worker / "UserSettings/VrBattlegrounds/play-launch.json")
                    profile_bytes = profile_path.read_bytes() if profile_path.exists() else None
                    report["baseline"] = baseline
                    report["baselineHashes"] = hashes()
                    report["profileBytesBase64"] = base64.b64encode(profile_bytes).decode() if profile_bytes is not None else None
                    save()
                    await asyncio.to_thread(logs.capture, "before-native", force=True)
                    mutated = True
                    phase = "native"
                    await code(PREFIX + 'manager.GetProperty("ActiveScenario", flags).SetValue(null, UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.ScriptableObject>("Assets/Settings/PlayMode/Host.asset")); ' + SETTINGS + '.SaveProfile(' + SETTINGS + '.Decode(' + literal(CONFIG) + ')); return "native-profile-prepared";', True)
                    topology = await code(PREFIX + '''var active = (UnityEngine.Object)manager.GetProperty("ActiveScenario", flags).GetValue(null);
var serialized = new UnityEditor.SerializedObject(active);
return new { Path = UnityEditor.AssetDatabase.GetAssetPath(active), Enabled = serialized.FindProperty("m_EnableEditors").boolValue,
    MainRole = serialized.FindProperty("m_MainEditorInstance").FindPropertyRelative("m_PlayerTag").stringValue,
    Editors = serialized.FindProperty("m_EditorInstances").arraySize, Local = serialized.FindProperty("m_LocalInstances").arraySize,
    Remote = serialized.FindProperty("m_RemoteInstances").arraySize };''')
                    check("Native одиночный Host scenario", topology["Path"] == "Assets/Settings/PlayMode/Host.asset" and topology["Enabled"]
                          and topology["MainRole"].lower() == "host" and topology["Editors"] == topology["Local"] == topology["Remote"] == 0, topology)
                    plan = await code("return " + LAUNCH + ".Plan();")
                    check("Native plan Game/TestMap2", plan.get("Passed") and plan.get("SceneKind") == "Game" and plan.get("TargetSceneName") == "TestMap2", plan)
                    await code(PREFIX + 'manager.GetMethod("Start", flags).Invoke(null, null); return "native-start-issued";', True)

                    def native_ready(s):
                        return (s["Playing"] and s["Scene"] == "TestMap2" and s["Server"] and s["Client"] and s["Connected"]
                                and not s["Loading"] and s["MapPlayable"] and s["MapRunValid"] and s["RequestActive"]
                                and s["Launch"].get("Owner") == "native-play" and s["Launch"].get("RunId")
                                and not s["Launch"].get("Error") and all(s["Effective"].get(k) == v for k, v in CONFIG.items()))

                    ready = await poll(SNAPSHOT, native_ready, args.ready_timeout)
                    run_ids[phase] = ready["Launch"]["RunId"]
                    logs.owner = "native-play"
                    logs.observe(ready["Launch"])
                    report["nativeReady"] = ready
                    await asyncio.sleep(1)
                    stable = await code(SNAPSHOT)
                    check("Native direct readiness stable", native_ready(stable) and stable["MapRunKey"] == ready["MapRunKey"], stable)
                    await asyncio.to_thread(logs.capture, "native-before-cleanup", ready["Launch"], True)
                    await cleanup()
                    await restore()
                    phase = "bot"
                    logs.owner = "BotCombatStand"
                    check("Bot начинает только idle", idle(await code(SNAPSHOT)))
                    options = '{ OutputDirectory = ' + json.dumps(str(worker / "Temp/VrTestStand" / output.stem / "bot-t01")) + ', Profile = VrBattlegrounds.DevTools.BotCombatStand.BotStandProfile.Production, Seeds = new[] { 101 }, CaseSeconds = 10f, Capture = true, Cases = new[] { VrBattlegrounds.DevTools.BotCombatStand.BotStandScenarioId.T01 } }'
                    await code("return " + BOT + ".Prepare(new VrBattlegrounds.DevTools.BotCombatStand.BotStandRunOptions " + options + ", true);", True)
                    state = await poll("return " + LAUNCH + ".Status();", lambda s: s.get("Owner") == "BotCombatStand" and s.get("RunId"), args.ready_timeout, LAUNCH + ".Status()")
                    run_ids[phase] = state["RunId"]
                    logs.observe(state)
                    terminal = await poll("return " + BOT + ".Status();", lambda s: s.get("finished") or s.get("stage") == "Failed", args.ready_timeout + 120)
                    report["botTerminal"] = terminal
                    state = await launch()
                    report["botLaunch"] = state
                    check("Bot managed launch readiness", state.get("Ready") and state.get("RunId") == run_ids[phase] and state.get("Owner") == "BotCombatStand", state)
                    check("Bot завершён без editor ошибки", terminal.get("finished") and not terminal.get("error") and terminal.get("launchId") == run_ids[phase], terminal)
                    source = Path(terminal["reportPath"]).resolve()
                    expected = (worker / "Temp/VrTestStand" / output.stem / "bot-t01").resolve()
                    check("Bot artifact own path", source.is_relative_to(expected) and source.name == "summary.json" and source.is_file(), str(source))
                    shutil.copytree(expected, evidence / "bot-t01", dirs_exist_ok=True)
                    result = json.loads(source.read_text(encoding="utf-8-sig"))
                    report["botResult"] = result
                    status = result["status"]
                    cases = result["cases"]
                    check("T01 integration completion; gameplay review pending", status["IsFinished"] and status["Total"] == status["Completed"] == 1
                          and all(status[k] == 0 for k in ("Failed", "InvalidFixture", "Unsupported"))
                          and status["NeedsReview"] == 1 and status["Passed"] is None and not status.get("Error")
                          and len(cases) == 1 and cases[0]["Id"] == "T01" and cases[0]["Seed"] == 101
                          and cases[0]["Status"] == "NeedsReview" and cases[0]["Frames"], status)
                    check("Bot HTML сохранён", (evidence / "bot-t01/report.html").is_file())
                    images = cases[0].get("Images", [])
                    check("Bot capture artifacts сохранены", len(images) >= 5 and all(Path(p).resolve().is_relative_to(expected)
                          and (evidence / "bot-t01" / Path(p).resolve().relative_to(expected)).is_file() for p in images), len(images))
                    await asyncio.to_thread(logs.capture, "bot-before-cleanup", state, True)
                    await cleanup()
                    await restore()
                    report["functionalPassed"] = True
                except Exception as error:
                    report["error"] = type(error).__name__ + ": " + str(error)
                finally:
                    if verified and mutated:
                        try:
                            await cleanup()
                            await restore()
                        except Exception as error:
                            report["cleanupError"] = type(error).__name__ + ": " + str(error)
                    if verified and main_identified:
                        await asyncio.to_thread(logs.capture, "after-final-cleanup", force=True)
                    report["passed"] = bool(report.get("functionalPassed") and not report.get("error") and not report.get("cleanupError")
                                            and report["logs"]["coverageComplete"] and all(c["passed"] for c in report["checks"]))
                    report["runIds"] = run_ids
                    save()
    except Exception as error:
        report["transportError"] = type(error).__name__ + ": " + str(error)
        report["passed"] = False
        save()
    return report


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--instance", required=True)
    parser.add_argument("--editor-root", required=True)
    parser.add_argument("--output", required=True)
    parser.add_argument("--ready-timeout", type=int, default=90)
    parser.add_argument("--cleanup-timeout", type=int, default=90)
    parser.add_argument("--call-timeout", type=int, default=45)
    args = parser.parse_args()
    if not all(10 <= getattr(args, k) <= 180 for k in ("ready_timeout", "cleanup_timeout", "call_timeout")):
        parser.error("Все сроки должны быть от 10 до 180 секунд")
    result = asyncio.run(run(args))
    print(json.dumps({"passed": result["passed"], "checks": len(result["checks"]), "userAcceptance": result["userAcceptance"],
                      "error": result.get("error"), "cleanupError": result.get("cleanupError"), "report": str(Path(args.output).resolve())}, ensure_ascii=False))
    raise SystemExit(0 if result["passed"] else 1)
