// B4 MUTATION (Play стенда после warmup): очередь раунда pf-round2 этапа pf-prototype в ТЕКУЩЕМ Live.
// (A) pf-mef-ak105: PlayerFlow=true, MEF + AK105, T07+T05 (те же case/seed, что в раунде IV);
// (B) base-mef-ak105: тот же вызов с PlayerFlow=false (гибрид dev) — база скольжения стоп/выстрелов/времени;
// (C) pf-mef-ppk: PlayerFlow=true, MEF + PPK, T07 — дымовая проверка пистолета (необязательная).
// CaseSeconds 30 (предел стенда: BotCombatStand.RunCases Mathf.Clamp(CaseSeconds, 1, 30)). Тело — штатный выбор
// (AvatarIndex = -1 -> BotSkin.PickHittable команды бота; для Terrorists это MEF, проверяется по inputs.json/b-subject.json).
// Payload возвращается сразу; дальше EditorApplication.update сам запускает BotCombatStand.RunCases по очереди.
// Прогресс — durable runtime/queue.json (читать B5). Замеры пишет сам стенд (BotStandPlayerFlowProbe ->
// summary.json cases[].Metrics["playerFlow"]); payload только один раз на case снимает состав компонентов тела
// (CaseStarted/CaseFinished, без покадровой выборки — не загрязнять gcAllocBytesPerFrame).
// НЕ ПОВТОРЯТЬ после таймаута: сначала B5. Повторный вызов в том же Play отказывает по AppDomain marker,
// в новом Play — по существующему queue.json.
const string Root = "tasks/bots-fix/reports/pf-round2/runtime";
const string Marker = "bots-fix.pf-round2.queue";
const float CaseSeconds = 30f;
float effectiveCaseSeconds = Mathf.Clamp(CaseSeconds, 1f, 30f);
var rifleCases = new[] { VrBattlegrounds.DevTools.BotCombatStand.BotStandScenarioId.T07,   // открытое поле: strafe/стрельба
                         VrBattlegrounds.DevTools.BotCombatStand.BotStandScenarioId.T05 }; // смещение угрозы: повороты
var pistolCases = new[] { VrBattlegrounds.DevTools.BotCombatStand.BotStandScenarioId.T07 };
var combos = new[] {
    new { id = "pf-mef-ak105", playerFlow = true, weapon = "AK105_Weapon", rifle = true, cases = rifleCases },
    new { id = "base-mef-ak105", playerFlow = false, weapon = "AK105_Weapon", rifle = true, cases = rifleCases },
    new { id = "pf-mef-ppk", playerFlow = true, weapon = "PPK_Weapon", rifle = false, cases = pistolCases } };
int totalCases = combos.Sum(c => c.cases.Length);
if (AppDomain.CurrentDomain.GetData(Marker) != null)
    return new { executionCompleted = true, refused = true, reason = "Очередь уже подключена в этом Play; читать B5." };
if (System.IO.File.Exists(Root + "/queue.json"))
    return new { executionCompleted = true, refused = true, reason = "queue.json уже существует (прежний Play); не перезапускать." };
if (!EditorApplication.isPlaying || !Mirror.NetworkServer.active || !VrBattlegrounds.Network.StateEventAuthority.IsWorldAuthority ||
    VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.Instance == null)
    throw new InvalidOperationException("Нужен Play серверного стенда с authority.");
var standStatus = VrBattlegrounds.EditorTools.BotCombatStandEditor.Status();
if (!(bool)standStatus["owned"]) throw new InvalidOperationException("Play не принадлежит стенду.");
if (!(bool)standStatus["finished"])
    return new { executionCompleted = true, refused = true, reason = "Warmup ещё не завершён; повторить B4 позже.", stage = standStatus["stage"] };
if (Convert.ToInt32(standStatus["invalidFixture"] ?? 0) > 0)
    return new { executionCompleted = true, refused = true, reason = "Warmup InvalidFixture — fixture стенда сломан (terminal).",
        warmupReport = standStatus["reportPath"] };
if (VrBattlegrounds.Bots.BotSenses.Stage() != VrBattlegrounds.Bots.BotStage.Combat)
    throw new InvalidOperationException("Live не в стадии Combat: " + VrBattlegrounds.Bots.BotSenses.Stage());
var samplerErrors = new List<string>();
var entries = new List<Dictionary<string, object>>();
var subjects = new Dictionary<VrBattlegrounds.DevTools.BotCombatStand.BotStandCaseResult, object[]>();
var sessions = new Dictionary<VrBattlegrounds.DevTools.BotCombatStand.BotStandCaseResult, VrBattlegrounds.DevTools.BotCombatStand.BotStandSession>();
string queueState = "running", queueError = null, runId = null, comboId = null;
int index = -1;
double startedAt = 0;
bool cancelRequested = false;
double runLimit = 0;
Action<string> note = text => { if (samplerErrors.Count < 64) samplerErrors.Add(DateTime.UtcNow.ToString("HH:mm:ss.fff") + " " + text); };
Action write = () => {
    try {
        System.IO.Directory.CreateDirectory(Root);
        System.IO.File.WriteAllText(Root + "/queue.json", Newtonsoft.Json.JsonConvert.SerializeObject(new {
            state = queueState, error = queueError, index, currentCombo = comboId, currentRunId = runId,
            liveStage = EditorApplication.isPlaying ? VrBattlegrounds.Bots.BotSenses.Stage().ToString() : "NotPlaying",
            caseSeconds = CaseSeconds, effectiveCaseSeconds, totalCases, combos = entries,
            samplerErrors = samplerErrors.ToArray(), updatedUtc = DateTime.UtcNow.ToString("o") }, Newtonsoft.Json.Formatting.Indented));
    } catch (Exception error) { note("queue.json: " + error.Message); }
};
// --- Read-only снимок тела проверяемого бота (один раз в начале и в конце case). Никогда не бросает. ---
Func<VrBattlegrounds.DevTools.BotCombatStand.BotStandSession, string, object> snapshot = (session, when) => {
    try {
        var player = session != null && session.Subject != null ? session.Subject.ActiveAvatar : null;
        if (player == null) return new { when, missingSubject = true };
        var avatar = player.GetComponent<UltimateXR.Avatar.UxrAvatar>();
        var identity = player.GetComponent<Mirror.NetworkIdentity>();
        var gun = player.GetComponent<VrBattlegrounds.Bots.BotGunner>();
        var hands = player.GetComponent<VrBattlegrounds.Bots.BotHands>();
        var runtime = player.GetComponent<VrBattlegrounds.Bots.BotRuntimeController>();
        var driver = player.GetComponent<VrBattlegrounds.Bots.BotAvatarDriver>();
        var body = player.GetComponent<VrBattlegrounds.Bots.BotBody>();
        var outcomes = new Dictionary<string, int>();
        if (runtime != null)
            foreach (VrBattlegrounds.Bots.BotShotOutcome o in Enum.GetValues(typeof(VrBattlegrounds.Bots.BotShotOutcome)))
                outcomes[o.ToString()] = runtime.OutcomeCount(o);
        var calibration = session.Subject.Calibration;
        var target = session.Target != null ? session.Target.ActiveAvatar : null;
        return new {
            when, t = Time.time, frame = Time.frameCount, netId = player.netId, alive = player.IsAlive,
            source = AssetDatabase.GetAssetPath(player.SourcePrefab), team = session.Subject.TeamIndex, avatarIndex = session.Subject.AvatarIndex,
            targetSource = target != null ? AssetDatabase.GetAssetPath(target.SourcePrefab) : null,
            avatarMode = avatar != null ? avatar.AvatarMode.ToString() : null,
            connectionToClientNull = identity != null && identity.connectionToClient == null,
            calibration = new { calibration.EyeHeight, calibration.FloorOffset, calibration.IsCalibrated },
            components = new { botBody = body != null, poseDrivenExternally = body != null && body.PoseDrivenExternally,
                driver = driver != null, driverCanDrive = driver != null && driver.CanDrive, driverAppliedFrame = driver != null ? driver.AppliedFrame : -1,
                hands = hands != null, weaponAim = player.GetComponent<VrBattlegrounds.Bots.BotWeaponAim>() != null, runtime = runtime != null,
                combatDriver = player.GetComponent<VrBattlegrounds.Bots.BotCombatDriver>() != null },
            gunner = gun == null ? null : (object)new { gun.PlayerFlow, holding = gun.Firearm != null, selected = gun.SelectedFirearm != null ? gun.SelectedFirearm.name : null,
                gun.ShotsFired, gun.HasAimPoint, author = gun.SelectedFirearm != null && VrBattlegrounds.Network.StateEventAuthority.IsAuthorOfItem(gun.SelectedFirearm) },
            grabs = hands == null ? null : (object)new { hands.PrimaryGrabs, hands.SupportGrabs },
            outcomes };
    } catch (Exception error) { note("snapshot " + when + ": " + error.Message); return new { when, error = error.Message }; }
};
Action<VrBattlegrounds.DevTools.BotCombatStand.BotStandCaseResult, VrBattlegrounds.DevTools.BotCombatStand.BotStandSession> onStarted = (result, session) => {
    try { sessions[result] = session; subjects[result] = new[] { snapshot(session, "started"), null }; }
    catch (Exception error) { note("started: " + error.Message); }
};
Action<VrBattlegrounds.DevTools.BotCombatStand.BotStandCaseResult> onFinished = result => {
    try {
        VrBattlegrounds.DevTools.BotCombatStand.BotStandSession session;
        sessions.TryGetValue(result, out session);
        object[] pair;
        if (!subjects.TryGetValue(result, out pair)) pair = new object[2];
        pair[1] = snapshot(session, "finished");
        var folder = System.IO.Path.Combine(VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.LastOutput, result.Id + "-" + result.Seed);
        System.IO.Directory.CreateDirectory(folder);
        System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "b-subject.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new {
            combo = comboId, caseId = result.Id, seed = result.Seed, caseStatus = result.Status, caseError = result.Error,
            started = pair[0], finished = pair[1], samplerErrors = samplerErrors.ToArray(),
            scope = "Read-only snapshot of the subject body at CaseStarted and CaseFinished (no per-frame sampling)." },
            Newtonsoft.Json.Formatting.Indented));
        subjects.Remove(result); sessions.Remove(result);
    } catch (Exception error) { note("finish: " + error.Message); }
};
EditorApplication.CallbackFunction tick = null;
Action detach = () => {
    VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.CaseStarted -= onStarted;
    VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.CaseFinished -= onFinished;
    EditorApplication.update -= tick;
};
tick = () => {
    try {
        if (queueState != "running") { detach(); return; }
        if (!EditorApplication.isPlaying) { queueState = "aborted"; queueError = "Play завершён до конца очереди."; write(); detach(); return; }
        if (runId != null) {
            var status = VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.GetStatus(runId);
            if (status == null) { queueState = "failed"; queueError = "Статус запуска потерян: " + runId; write(); detach(); return; }
            if (!status.IsFinished) {
                double elapsed = EditorApplication.timeSinceStartup - startedAt;
                if (!cancelRequested && elapsed > runLimit) {
                    VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.Cancel(runId);
                    cancelRequested = true; entries[index]["watchdogCancelUtc"] = DateTime.UtcNow.ToString("o"); write();
                } else if (cancelRequested && elapsed > runLimit + 30.0) {
                    queueState = "failed"; queueError = "Запуск не завершился после Cancel: " + runId; write(); detach();
                }
                return;
            }
            entries[index]["finishedUtc"] = DateTime.UtcNow.ToString("o");
            entries[index]["status"] = new { status.Passed, status.Total, status.Completed, status.Failed, status.NeedsReview,
                status.InvalidFixture, status.Unsupported, status.Error, status.ReportPath };
            runId = null; cancelRequested = false; write();
        }
        if (index + 1 >= combos.Length) { queueState = "completed"; write(); detach(); return; }
        if (VrBattlegrounds.Bots.BotSenses.Stage() != VrBattlegrounds.Bots.BotStage.Combat) {
            queueState = "live-ended"; queueError = "Live завершён до сочетания " + combos[index + 1].id; write(); detach(); return;
        }
        index++;
        var combo = combos[index];
        comboId = combo.id;
        var options = new VrBattlegrounds.DevTools.BotCombatStand.BotStandRunOptions {
            OutputDirectory = Root + "/" + combo.id,
            Profile = VrBattlegrounds.DevTools.BotCombatStand.BotStandProfile.Production,
            Seeds = new[] { 101 }, CaseSeconds = CaseSeconds, Capture = true, Rifle = combo.rifle, HandFit = false,
            WeaponName = combo.weapon, AvatarIndex = -1, PlayerFlow = combo.playerFlow, Cases = combo.cases };
        var entry = new Dictionary<string, object> { { "combo", combo.id }, { "playerFlow", combo.playerFlow }, { "weapon", combo.weapon },
            { "rifle", combo.rifle }, { "cases", combo.cases.Select(c => c.ToString()).ToArray() },
            { "output", options.OutputDirectory }, { "startedUtc", DateTime.UtcNow.ToString("o") } };
        entries.Add(entry);
        runLimit = combo.cases.Length * effectiveCaseSeconds + 60.0;
        var handle = VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.RunCases(options, combo.cases);
        runId = handle.RunId; entry["runId"] = handle.RunId; startedAt = EditorApplication.timeSinceStartup;
        write();
    } catch (Exception error) {
        queueState = "failed"; queueError = error.ToString(); write(); detach();
    }
};
AppDomain.CurrentDomain.SetData(Marker, DateTime.UtcNow.ToString("o"));
VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.CaseStarted += onStarted;
VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.CaseFinished += onFinished;
write();
EditorApplication.update += tick;
return new { executionCompleted = true, started = true,
    combos = combos.Select(c => c.id + (c.playerFlow ? "[P]" : "[Hybrid]") + ":" + string.Join("+", c.cases.Select(x => x.ToString()).ToArray())).ToArray(),
    caseSeconds = CaseSeconds, effectiveCaseSeconds, queue = Root + "/queue.json",
    warmupReport = standStatus["reportPath"], expectedSeconds = totalCases * effectiveCaseSeconds };
