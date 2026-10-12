// B3 MUTATION: штатный запуск стенда BotCombatStandEditor.Prepare -> общий PlayLaunch (сервер, сцена стенда,
// Respawn, map-playable) -> вход в Play с domain reload. Editor Tick стенда сам делает GoLive и запускает
// короткий warmup (гибрид dev, PPK, T07, 1 с, без съёмки) — только проверка fixture; замеры — в очереди B4.
// НЕ ПОВТОРЯТЬ после таймаута/обрыва MCP: сначала B5 (PlayLaunch.State, stand owned/stage, runtime/launch.json).
// Prepare сам отказывает при занятом редакторе или уже owned стенде.
const string Root = "tasks/bots-fix/reports/pf-round5";
// Фаза Play: p1 (PE, PF, E) или p2 (F) — Live Respawn 300 с не вмещает все 12 case одним Play.
const string Phase = "p1";
string PhaseRoot = Root + "/runtime/" + Phase;
if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
    throw new InvalidOperationException("Нужен idle EditMode.");
if (System.IO.File.Exists(PhaseRoot + "/launch.json") || System.IO.Directory.Exists(PhaseRoot + "/warmup"))
    return new { executionCompleted = true, refused = true, reason = "runtime уже запускался в этом output root; не повторять." };
var preflights = System.IO.Directory.Exists(Root) ? System.IO.Directory.GetFiles(Root, "preflight-*.json") : new string[0];
if (preflights.Length == 0 || !preflights.Any(p => (bool?)Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(p))["passed"] == true))
    return new { executionCompleted = true, refused = true, reason = "Нет PASS preflight B0 — runtime не запускается." };
var options = new VrBattlegrounds.DevTools.BotCombatStand.BotStandRunOptions {
    OutputDirectory = PhaseRoot + "/warmup",
    Profile = VrBattlegrounds.DevTools.BotCombatStand.BotStandProfile.Production,
    Seeds = new[] { 101 }, CaseSeconds = 1f, Capture = false, Rifle = false, HandFit = false,
    WeaponName = "PPK_Weapon", AvatarIndex = -1, PlayerFlow = false,
    Cases = new[] { VrBattlegrounds.DevTools.BotCombatStand.BotStandScenarioId.T07 } };
System.IO.Directory.CreateDirectory(PhaseRoot);
// launch.json пишется до Prepare: durable отметка попытки, даже если ответ MCP потеряется на domain reload.
System.IO.File.WriteAllText(PhaseRoot + "/launch.json", Newtonsoft.Json.JsonConvert.SerializeObject(new {
    requestedUtc = DateTime.UtcNow.ToString("o"), options, route = "BotCombatStandEditor.Prepare(options, smoke:false)" },
    Newtonsoft.Json.Formatting.Indented));
string message = VrBattlegrounds.EditorTools.BotCombatStandEditor.Prepare(options, false);
return new { executionCompleted = true, started = true, message,
    next = "Ждать B5: launch Ready, stand stage Running -> finished=true для warmup; затем B4." };
