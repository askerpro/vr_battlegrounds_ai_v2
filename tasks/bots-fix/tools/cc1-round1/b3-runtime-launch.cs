// B3 MUTATION: штатный запуск стенда BotCombatStandEditor.Prepare -> общий PlayLaunch (сервер, сцена стенда, Respawn,
// map-playable) -> вход в Play с domain reload. Editor Tick стенда сам делает GoLive и запускает warmup (PPK, T07, 1 с,
// без съёмки) — только проверка fixture; замеры — очередь B4. Фаза (p1|p2) подставляется в const Phase при отправке;
// одинакова в B3/B4/B5/B6 одного Play.
// НЕ ПОВТОРЯТЬ после таймаута/обрыва MCP: сначала B5 (PlayLaunch.State, stand owned/stage, runtime/<phase>/launch.json).
const string Root = "tasks/bots-fix/reports/cc1-round1";
const string Phase = "p1";
string PhaseRoot = Root + "/runtime/" + Phase;
if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
    throw new InvalidOperationException("Нужен idle EditMode.");
if (System.IO.File.Exists(PhaseRoot + "/launch.json") || System.IO.Directory.Exists(PhaseRoot + "/warmup"))
    return new { executionCompleted = true, refused = true, reason = "Фаза " + Phase + " уже запускалась в этом output root; не повторять." };
Func<string, bool> anyPassed = pattern => System.IO.Directory.Exists(Root) && System.IO.Directory.GetFiles(Root, pattern)
    .Any(p => (bool?)Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(p))["passed"] == true);
if (!anyPassed("preflight-*.json")) return new { executionCompleted = true, refused = true, reason = "Нет PASS preflight B0." };
if (!anyPassed("controller-*.json")) return new { executionCompleted = true, refused = true, reason = "Нет PASS проверки контроллера B1." };
var options = new VrBattlegrounds.DevTools.BotCombatStand.BotStandRunOptions {
    OutputDirectory = PhaseRoot + "/warmup",
    Profile = VrBattlegrounds.DevTools.BotCombatStand.BotStandProfile.Production,
    Seeds = new[] { 101 }, CaseSeconds = 1f, Capture = false, Rifle = false, HandFit = false,
    WeaponName = "PPK_Weapon", AvatarIndex = -1,
    Cases = new[] { VrBattlegrounds.DevTools.BotCombatStand.BotStandScenarioId.T07 } };
System.IO.Directory.CreateDirectory(PhaseRoot);
// launch.json пишется до Prepare: durable отметка попытки, даже если ответ MCP потеряется на domain reload.
System.IO.File.WriteAllText(PhaseRoot + "/launch.json", Newtonsoft.Json.JsonConvert.SerializeObject(new {
    requestedUtc = DateTime.UtcNow.ToString("o"), phase = Phase, options, route = "BotCombatStandEditor.Prepare(options, smoke:false)",
    memory = new { monoBytes = GC.GetTotalMemory(false), unityAllocated = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() } },
    Newtonsoft.Json.Formatting.Indented));
string message = VrBattlegrounds.EditorTools.BotCombatStandEditor.Prepare(options, false);
return new { executionCompleted = true, started = true, phase = Phase, message,
    next = "Ждать B5: launch Ready, stand Running -> finished=true для warmup; затем B4." };
