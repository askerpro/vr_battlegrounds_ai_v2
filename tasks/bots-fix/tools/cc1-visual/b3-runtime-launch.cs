// Под своей RUNNING арендой: штатный Prepare -> PlayLaunch -> warmup, с domain reload.
const string Root = "tasks/bots-fix/reports/cc1-visual-candidate1";
if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) throw new InvalidOperationException("Нужен idle EditMode.");
if (!System.IO.File.Exists(Root + "/preflight.json")) throw new InvalidOperationException("Сначала B0.");
if (System.IO.File.Exists(Root + "/launch.json")) return new { executionCompleted = true, refused = true, reason = "Попытка уже записана: сначала B5." };
var options = new VrBattlegrounds.DevTools.BotCombatStand.BotStandRunOptions {
    OutputDirectory = Root + "/warmup", Profile = VrBattlegrounds.DevTools.BotCombatStand.BotStandProfile.Production,
    Seeds = new[] { 101 }, CaseSeconds = 1f, Capture = false, HandFit = false, WeaponName = "PPK_Weapon", AvatarIndex = -1,
    Cases = new[] { VrBattlegrounds.DevTools.BotCombatStand.BotStandScenarioId.T07 } };
var intent = System.Text.Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(new {
    utc = DateTime.UtcNow.ToString("o"), options, route = "BotCombatStandEditor.Prepare" }, Newtonsoft.Json.Formatting.Indented));
using (var file = new System.IO.FileStream(Root + "/launch.json", System.IO.FileMode.CreateNew, System.IO.FileAccess.Write, System.IO.FileShare.Read)) {
    file.Write(intent, 0, intent.Length); file.Flush(true);
}
string message = VrBattlegrounds.EditorTools.BotCombatStandEditor.Prepare(options, false);
var launchedStand = VrBattlegrounds.EditorTools.BotCombatStandEditor.Status();
var launched = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
if (string.IsNullOrEmpty(launched.RunId) || launched.Owner != "BotCombatStand" || (string)launchedStand["launchId"] != launched.RunId)
    throw new InvalidOperationException("Launch identity не подтверждён: inspect, не повторять Prepare.");
var receipt = System.Text.Encoding.UTF8.GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(new {
    root = Root, launchRunId = launched.RunId, owner = launched.Owner, utc = DateTime.UtcNow.ToString("o") }));
using (var file = new System.IO.FileStream(Root + "/launch-identity.json", System.IO.FileMode.CreateNew, System.IO.FileAccess.Write, System.IO.FileShare.Read)) {
    file.Write(receipt, 0, receipt.Length); file.Flush(true);
}
return new { executionCompleted = true, requested = true, message, next = "B5: own warmup finished; затем B4 один раз." };
