// B3 MUTATION: штатный запуск стенда BotCombatStandEditor.Prepare -> общий PlayLaunch (сервер, сцена стенда,
// Respawn, map-playable) -> вход в Play с domain reload. Editor Tick стенда сам делает GoLive и запускает
// короткий warmup (MEF + PPK, T07, 1 с, без съёмки). Warmup только проверяет fixture; критерии B — в B4.
// НЕ ПОВТОРЯТЬ после таймаута/обрыва MCP: сначала B5 (PlayLaunch.State, stand owned/stage, runtime/launch.json).
// Prepare сам отказывает при занятом редакторе или уже owned стенде.
const string Root = "tasks/bots-fix/reports/armed-runtime-b";
const string Mef = "b8146035f3f436a4799882b3a0d1dd45"; // обычный MEF; MEF Black — контрольная цель стенда
if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
    throw new InvalidOperationException("Нужен idle EditMode.");
if (System.IO.File.Exists(Root + "/runtime/launch.json") || System.IO.Directory.Exists(Root + "/runtime/warmup"))
    return new { executionCompleted = true, refused = true, reason = "runtime уже запускался в этом output root; не повторять." };
// Гейт A и идентичности: без PASS runtime не начинается (terminal failure -> finish и диагностика).
Func<string, bool?> passedOf = path => {
    if (!System.IO.File.Exists(path)) return null;
    return (bool?)Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path))["passed"];
};
bool? authoring = passedOf(Root + "/result.json");
bool? identity = passedOf(Root + "/identity-post-authoring.json");
if (authoring != true || identity != true)
    return new { executionCompleted = true, refused = true, authoringPassed = authoring, identityPassed = identity,
        reason = "A или идентичность после A не PASS — runtime B не запускается." };
var options = new VrBattlegrounds.DevTools.BotCombatStand.BotStandRunOptions {
    OutputDirectory = Root + "/runtime/warmup",
    Profile = VrBattlegrounds.DevTools.BotCombatStand.BotStandProfile.Production,
    Seeds = new[] { 101 }, CaseSeconds = 1f, Capture = false, Rifle = false, HandFit = false,
    WeaponName = "PPK_Weapon", AvatarIndex = -1, AvatarSourceGuid = Mef,
    Cases = new[] { VrBattlegrounds.DevTools.BotCombatStand.BotStandScenarioId.T07 } };
System.IO.Directory.CreateDirectory(Root + "/runtime");
// launch.json пишется до Prepare: durable отметка попытки, даже если ответ MCP потеряется на domain reload.
System.IO.File.WriteAllText(Root + "/runtime/launch.json", Newtonsoft.Json.JsonConvert.SerializeObject(new {
    requestedUtc = DateTime.UtcNow.ToString("o"), options, route = "BotCombatStandEditor.Prepare(options, smoke:false)" },
    Newtonsoft.Json.Formatting.Indented));
string message = VrBattlegrounds.EditorTools.BotCombatStandEditor.Prepare(options, false);
return new { executionCompleted = true, started = true, message,
    next = "Ждать B5: launch Ready, stand stage Running -> finished=true для warmup; затем B4." };
