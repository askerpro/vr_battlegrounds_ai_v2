// B4 MUTATION (Play стенда после warmup): очередь eq-round3 этапа pf-equipment в ТЕКУЩЕМ Live. Все прогоны PlayerFlow, T07, seed 101.
// Фазы (Live Respawn 300 с): p1 = H, HE, U, P, D1 (135 с); p2 = D2, G, E, PE (120 с). Опции снаряжения исполняет сам стенд
// (BotStandEquipmentScript); замеры — BotStandPlayerFlowProbe (summary.json cases[].Metrics["playerFlow"]["equipment"]).
// Снимки: Capture стенда выключен (рекордер снимает только тело с оружием и по смене клипа); вместо этого payload сам
// снимает BotStandVisualCapture в заданные секунды case (подход к кобуре/подъём, уборка, призрак) — snapshots в папке case.
// НЕ ПОВТОРЯТЬ после таймаута: сначала статус (queue.json фазы, AppDomain marker).
const string Phase = "p1";
const string Root = "tasks/bots-fix/reports/eq-round3/runtime";
string PhaseRoot = Root + "/" + Phase;
string Marker = "bots-fix.eq-round3.queue." + Phase;
var t07 = new[] { VrBattlegrounds.DevTools.BotCombatStand.BotStandScenarioId.T07 };
var none = new float[0];
var all = new[] {
    new { id = "H-ppk-holster", phase = "p1", weapon = "PPK_Weapon", rifle = false, seconds = 30f, zero = false, noRecoil = false, holster = true, unarmed = false, purchase = (string)null, purchaseAt = -1f, killAt = -1f, respawnAt = -1f, shots = new[] { 0.5f, 0.7f, 0.9f, 1.2f, 1.6f } },
    new { id = "HE-ppk-holster-ref", phase = "p1", weapon = "PPK_Weapon", rifle = false, seconds = 30f, zero = true, noRecoil = true, holster = true, unarmed = false, purchase = (string)null, purchaseAt = -1f, killAt = -1f, respawnAt = -1f, shots = none },
    new { id = "U-unarmed", phase = "p1", weapon = "PPK_Weapon", rifle = false, seconds = 20f, zero = false, noRecoil = false, holster = false, unarmed = true, purchase = (string)null, purchaseAt = -1f, killAt = -1f, respawnAt = -1f, shots = new[] { 5f, 12f } },
    new { id = "P-purchase-ak105", phase = "p1", weapon = "PPK_Weapon", rifle = false, seconds = 35f, zero = false, noRecoil = false, holster = true, unarmed = false, purchase = "AK105_Weapon", purchaseAt = 10f, killAt = -1f, respawnAt = -1f, shots = new[] { 10.3f, 10.6f, 11.0f, 11.5f, 12.5f } },
    new { id = "D1-death-ak105", phase = "p1", weapon = "AK105_Weapon", rifle = true, seconds = 20f, zero = false, noRecoil = false, holster = false, unarmed = false, purchase = (string)null, purchaseAt = -1f, killAt = 8f, respawnAt = -1f, shots = new[] { 9f } },
    new { id = "D2-death-ppk-holster", phase = "p2", weapon = "PPK_Weapon", rifle = false, seconds = 20f, zero = false, noRecoil = false, holster = true, unarmed = false, purchase = (string)null, purchaseAt = -1f, killAt = 10f, respawnAt = -1f, shots = none },
    new { id = "G-ghost-respawn-ak105", phase = "p2", weapon = "AK105_Weapon", rifle = true, seconds = 40f, zero = false, noRecoil = false, holster = false, unarmed = false, purchase = (string)null, purchaseAt = -1f, killAt = 8f, respawnAt = 20f, shots = new[] { 10f, 15f, 21f } },
    new { id = "E-ak105", phase = "p2", weapon = "AK105_Weapon", rifle = true, seconds = 30f, zero = true, noRecoil = true, holster = false, unarmed = false, purchase = (string)null, purchaseAt = -1f, killAt = -1f, respawnAt = -1f, shots = none },
    new { id = "PE-ppk", phase = "p2", weapon = "PPK_Weapon", rifle = false, seconds = 30f, zero = true, noRecoil = true, holster = false, unarmed = false, purchase = (string)null, purchaseAt = -1f, killAt = -1f, respawnAt = -1f, shots = none } };
var combos = all.Where(c => c.phase == Phase).ToArray();
if (AppDomain.CurrentDomain.GetData(Marker) != null) return new { executionCompleted = true, refused = true, reason = "Очередь уже подключена." };
if (System.IO.File.Exists(PhaseRoot + "/queue.json")) return new { executionCompleted = true, refused = true, reason = "queue.json уже существует." };
if (!EditorApplication.isPlaying || !Mirror.NetworkServer.active || VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.Instance == null) throw new InvalidOperationException("Нужен Play стенда.");
var standStatus = VrBattlegrounds.EditorTools.BotCombatStandEditor.Status();
if (!(bool)standStatus["owned"] || !(bool)standStatus["finished"] || Convert.ToInt32(standStatus["invalidFixture"] ?? 0) > 0) return new { executionCompleted = true, refused = true, reason = "warmup не готов", standStatus };
if (VrBattlegrounds.Bots.BotSenses.Stage() != VrBattlegrounds.Bots.BotStage.Combat) throw new InvalidOperationException("Live не Combat");
var entries = new List<Dictionary<string, object>>();
var notes = new List<string>();
string queueState = "running", queueError = null, runId = null;
int index = -1; double startedAt = 0; bool cancelRequested = false; double runLimit = 0;
float caseStart = -1f; int nextShot = 0;
VrBattlegrounds.EditorTools.BotStandVisualCapture capture = null;
Action write = () => { try { System.IO.Directory.CreateDirectory(PhaseRoot);
    System.IO.File.WriteAllText(PhaseRoot + "/queue.json", Newtonsoft.Json.JsonConvert.SerializeObject(new { state = queueState, error = queueError, index, currentRunId = runId,
        liveStage = EditorApplication.isPlaying ? VrBattlegrounds.Bots.BotSenses.Stage().ToString() : "NotPlaying", combos = entries, notes = notes.ToArray(),
        updatedUtc = DateTime.UtcNow.ToString("o") }, Newtonsoft.Json.Formatting.Indented)); } catch { } };
Action<VrBattlegrounds.DevTools.BotCombatStand.BotStandCaseResult, VrBattlegrounds.DevTools.BotCombatStand.BotStandSession> onStarted = (result, session) => {
    caseStart = Time.time; nextShot = 0;
    try { if (index >= 0 && index < entries.Count) { entries[index]["caseStartedLocal"] = DateTime.Now.ToString("HH:mm:ss"); write(); } } catch { } };
Action<VrBattlegrounds.DevTools.BotCombatStand.BotStandCaseResult, VrBattlegrounds.DevTools.BotCombatStand.BotStandSession> onSampled = (result, session) => {
    try {
        if (index < 0 || caseStart < 0f) return;
        var plan = combos[index].shots;
        if (nextShot >= plan.Length || Time.time - caseStart < plan[nextShot]) return;
        float at = plan[nextShot]; nextShot++;
        var player = session != null && session.Subject != null ? session.Subject.ActiveAvatar : null;
        if (player == null) { notes.Add(result.Id + " " + at + "s: нет тела"); return; }
        if (capture == null) capture = new VrBattlegrounds.EditorTools.BotStandVisualCapture();
        var folder = System.IO.Path.Combine(VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.LastOutput, result.Id + "-" + result.Seed);
        capture.Capture(player, folder, "t" + at.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + (player.IsAlive ? "" : "-dead") + "-f" + Time.frameCount);
    } catch (Exception error) { if (notes.Count < 32) notes.Add("capture: " + error.Message); } };
EditorApplication.CallbackFunction tick = null;
Action detach = () => {
    VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.CaseStarted -= onStarted;
    VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.CaseSampled -= onSampled;
    EditorApplication.update -= tick;
    if (capture != null) { capture.Dispose(); capture = null; } };
tick = () => {
    try {
        if (queueState != "running") { detach(); return; }
        if (!EditorApplication.isPlaying) { queueState = "aborted"; write(); detach(); return; }
        if (runId != null) {
            var status = VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.GetStatus(runId);
            if (status == null) { queueState = "failed"; queueError = "status lost"; write(); detach(); return; }
            if (!status.IsFinished) {
                double elapsed = EditorApplication.timeSinceStartup - startedAt;
                if (!cancelRequested && elapsed > runLimit) { VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.Cancel(runId); cancelRequested = true; entries[index]["watchdogCancelUtc"] = DateTime.UtcNow.ToString("o"); write(); }
                else if (cancelRequested && elapsed > runLimit + 30.0) { queueState = "failed"; queueError = "no finish after cancel"; write(); detach(); }
                return;
            }
            entries[index]["finishedLocal"] = DateTime.Now.ToString("HH:mm:ss");
            entries[index]["status"] = new { status.Passed, status.Total, status.Completed, status.Failed, status.NeedsReview, status.InvalidFixture, status.Error, status.ReportPath };
            runId = null; cancelRequested = false; caseStart = -1f; write();
        }
        if (index + 1 >= combos.Length) { queueState = "completed"; write(); detach(); return; }
        if (VrBattlegrounds.Bots.BotSenses.Stage() != VrBattlegrounds.Bots.BotStage.Combat) { queueState = "live-ended"; queueError = "Live завершён до " + combos[index + 1].id; write(); detach(); return; }
        index++;
        var combo = combos[index];
        var options = new VrBattlegrounds.DevTools.BotCombatStand.BotStandRunOptions {
            OutputDirectory = Root + "/" + combo.id, Profile = VrBattlegrounds.DevTools.BotCombatStand.BotStandProfile.Production,
            Seeds = new[] { 101 }, CaseSeconds = combo.seconds, Capture = false, Rifle = combo.rifle, HandFit = false,
            WeaponName = combo.weapon, AvatarIndex = -1, PlayerFlow = true, ZeroAiAimSpread = combo.zero, NoRecoil = combo.noRecoil,
            HolsterStart = combo.holster, Unarmed = combo.unarmed, PurchaseWeaponName = combo.purchase, PurchaseAtSeconds = combo.purchaseAt,
            KillAtSeconds = combo.killAt, RespawnAtSeconds = combo.respawnAt, Cases = t07 };
        var entry = new Dictionary<string, object> { { "combo", combo.id }, { "options", options }, { "requestedLocal", DateTime.Now.ToString("HH:mm:ss") } };
        entries.Add(entry);
        runLimit = combo.seconds + 60.0;
        var handle = VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.RunCases(options, t07);
        runId = handle.RunId; entry["runId"] = handle.RunId; startedAt = EditorApplication.timeSinceStartup;
        write();
    } catch (Exception error) { queueState = "failed"; queueError = error.ToString(); write(); detach(); }
};
AppDomain.CurrentDomain.SetData(Marker, DateTime.UtcNow.ToString("o"));
VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.CaseStarted += onStarted;
VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.CaseSampled += onSampled;
write();
EditorApplication.update += tick;
return new { executionCompleted = true, started = true, phase = Phase, combos = combos.Select(c => c.id).ToArray(), seconds = combos.Sum(c => c.seconds), nowLocal = DateTime.Now.ToString("HH:mm:ss") };
