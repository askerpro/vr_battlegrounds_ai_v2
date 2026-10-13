// B4 MUTATION (Play после B3, launch Ready): GoLive стенда и очередь net-round1 этапа pf-network в ТЕКУЩЕМ Live.
// Все прогоны PlayerFlow, T07, seed 101; замеры сервера — BotStandPlayerFlowProbe (summary.json), клиентов — BotStandNetObserver
// (runtime/<topology>-<phase>/clients/*.json). Поздний вход/reconnect — штатные адресные команды стенда
// (PlayModeTestStand.Send disconnect/reconnect клиенту "Player 3"; "Player 2" остаётся подключённым наблюдателем).
// Фазы (Live Respawn 300 с):
//   S2C p1 = E (эталон ≤1°), A (с отдачей), P (кобура+покупка), G (смерть/призрак/возрождение)        — 135 с
//   S2C p2 = LJ (опоздавший к бою с AK), LH (опоздавший: пистолет в кобуре, AK в руке), RC (reconnect в бою) — 135 с + ожидания
//   H1C p1 = E, P, G                                                                                   — 105 с
// attempt-6 (A/B «отдача копии наблюдателя»): S2C p1 = A (с отдачей) и LJ (опоздавший с AK) — 75 с + ожидания, два
// запуска B3..B6: Variant A1 (ClientNoRecoil=false) и A2 (ClientNoRecoil=true). Опция доходит до клиентов заявкой
// наблюдателя (B3); очередь сверяет заявку с вариантом и пишет то же значение в BotStandRunOptions.ClientNoRecoil.
// attempt-2: клиенты — игроки без команды режима (TeamIndex=0: в Respawn команду выбирает игрок, разминка без команд) —
// штатно не враги бота (BotCombatDriver.IsEnemy, BotSenses.NearestEnemy). Перед каждым case очередь проверяет, что у
// клиентов TeamIndex=0 (иначе стоп: клиент мог стать целью). Их аватары без нейтральной зоны появляются в начале
// координат — раскладка case сдвинута по X (BotStandRunOptions.LaneOffsetX), позиции клиентов пишутся в queue.json.
// НЕ ПОВТОРЯТЬ после таймаута: сначала B5 (queue.json фазы, AppDomain marker).
const string Root = "tasks/bots-fix/reports/net-round1";
const string Topology = "S2C";   // S2C | H1C
const string Phase = "p1";       // p1 | p2
const string LateClient = "Player 3";
const string Attempt = "attempt-9";
const string Variant = "A1";     // attempt-6 A/B: A1 — ClientNoRecoil=false, A2 — ClientNoRecoil=true; "" — без варианта
const float LaneOffsetX = 4f;   // бот (4,0,-5), цель (4,0,5): клиенты в (0,0,0) в 4 м от линии огня
string RunRoot = Root + "/runtime/" + Attempt + "/" + Topology + "-" + Phase + (Variant.Length > 0 ? "-" + Variant : "");
bool ClientNoRecoil = Variant == "A2";
string Marker = "bots-fix.net-round1.queue." + Attempt + "." + Topology + "." + Phase + "." + Variant;
var t07 = new[] { VrBattlegrounds.DevTools.BotCombatStand.BotStandScenarioId.T07 };
// preDisconnect — отключить LateClient до начала case (он не видел бота); reconnectAt/disconnectAt — секунды от начала case.
var all = new[] {
    new { id = "E-ak105-ref", phases = "H1C-p1", weapon = "AK105_Weapon", rifle = true, seconds = 30f, zero = true, noRecoil = true, holster = false, purchase = (string)null, purchaseAt = -1f, killAt = -1f, respawnAt = -1f, preDisconnect = false, disconnectAt = -1f, reconnectAt = -1f },
    new { id = "A-ak105-recoil", phases = "", weapon = "AK105_Weapon", rifle = true, seconds = 30f, zero = false, noRecoil = false, holster = false, purchase = (string)null, purchaseAt = -1f, killAt = -1f, respawnAt = -1f, preDisconnect = false, disconnectAt = -1f, reconnectAt = -1f },
    new { id = "P-holster-purchase", phases = "S2C-p1,H1C-p1", weapon = "PPK_Weapon", rifle = false, seconds = 35f, zero = false, noRecoil = false, holster = true, purchase = "AK105_Weapon", purchaseAt = 10f, killAt = -1f, respawnAt = -1f, preDisconnect = false, disconnectAt = -1f, reconnectAt = -1f },
    new { id = "LH-late-holster", phases = "S2C-p1,S2C-p2", weapon = "PPK_Weapon", rifle = false, seconds = 45f, zero = false, noRecoil = false, holster = true, purchase = "AK105_Weapon", purchaseAt = 8f, killAt = -1f, respawnAt = -1f, preDisconnect = true, disconnectAt = -1f, reconnectAt = 22f },
    new { id = "G-ghost-respawn-ak105", phases = "S2C-p1,H1C-p1", weapon = "AK105_Weapon", rifle = true, seconds = 40f, zero = false, noRecoil = false, holster = false, purchase = (string)null, purchaseAt = -1f, killAt = 8f, respawnAt = 20f, preDisconnect = false, disconnectAt = -1f, reconnectAt = -1f },
    new { id = "LJ-late-ak105", phases = "S2C-p1,S2C-p2", weapon = "AK105_Weapon", rifle = true, seconds = 45f, zero = false, noRecoil = false, holster = false, purchase = (string)null, purchaseAt = -1f, killAt = -1f, respawnAt = -1f, preDisconnect = true, disconnectAt = -1f, reconnectAt = 15f },
    new { id = "RC-reconnect-ak105", phases = "S2C-p2", weapon = "AK105_Weapon", rifle = true, seconds = 45f, zero = false, noRecoil = false, holster = false, purchase = (string)null, purchaseAt = -1f, killAt = -1f, respawnAt = -1f, preDisconnect = false, disconnectAt = 12f, reconnectAt = 22f } };
string phaseKey = Topology + "-" + Phase;
var combos = all.Where(c => c.phases.Split(',').Contains(phaseKey)).ToArray();
if (combos.Length == 0) throw new InvalidOperationException("Нет case для " + phaseKey);
if (AppDomain.CurrentDomain.GetData(Marker) != null) return new { executionCompleted = true, refused = true, reason = "Очередь уже подключена." };
// Заявка наблюдателя (B3) должна нести тот же ClientNoRecoil, что и вариант очереди: клоны прочитали её при входе в Play.
VrBattlegrounds.DevTools.BotCombatStand.BotStandNetObserverRequest observerRequest;
string observerReason;
if (!VrBattlegrounds.DevTools.BotCombatStand.BotStandNetObserverGate.TryReadRequest(out observerRequest, out observerReason))
    return new { executionCompleted = true, refused = true, reason = "Заявка наблюдателя: " + observerReason };
if (observerRequest.ClientNoRecoil != ClientNoRecoil)
    return new { executionCompleted = true, refused = true, reason = "ClientNoRecoil заявки (" + observerRequest.ClientNoRecoil + ") не совпадает с вариантом " + Variant + "." };
if (System.IO.File.Exists(RunRoot + "/queue.json")) return new { executionCompleted = true, refused = true, reason = "queue.json уже существует." };
if (!EditorApplication.isPlaying || !Mirror.NetworkServer.active || VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.Instance == null)
    throw new InvalidOperationException("Нужен Play стенда с сервером в главном редакторе.");
var launchNow = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
if (!launchNow.Ready) return new { executionCompleted = true, refused = true, reason = "PlayLaunch не Ready: " + launchNow.State + " " + launchNow.Error };
if (VrBattlegrounds.Bots.BotDirector.Instance != null && VrBattlegrounds.Bots.BotDirector.Instance.Bots.Count > 0)
    return new { executionCompleted = true, refused = true, reason = "Есть боты до очереди." };

var entries = new List<Dictionary<string, object>>();
var notes = new List<string>();
var stand = new List<Dictionary<string, object>>();   // адресные команды и их исходы
string queueState = "golive", queueError = null, runId = null, step = "golive";
int index = -1, requestCounter = 0;
double startedAt = 0, stepSince = EditorApplication.timeSinceStartup, runLimit = 0;
bool cancelRequested = false, goLiveCalled = false, disconnectSent = false, reconnectSent = false;
float caseStart = -1f;
string pendingRequest = null;
Action write = () => { try { System.IO.Directory.CreateDirectory(RunRoot);
    System.IO.File.WriteAllText(RunRoot + "/queue.json", Newtonsoft.Json.JsonConvert.SerializeObject(new { state = queueState, error = queueError, step, index,
        currentRunId = runId, topology = Topology, phase = Phase, variant = Variant, clientNoRecoil = ClientNoRecoil,
        liveStage = EditorApplication.isPlaying ? VrBattlegrounds.Bots.BotSenses.Stage().ToString() : "NotPlaying",
        combos = entries, stand, notes = notes.ToArray(), updatedUtc = DateTime.UtcNow.ToString("o") }, Newtonsoft.Json.Formatting.Indented)); } catch { } };
Func<string, VrBattlegrounds.EditorTools.TestStand.StandParticipantState> participant = name => {
    var status = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
    return status.Participants == null ? null : status.Participants.FirstOrDefault(p => p.ParticipantId == name); };
// Одна адресная команда (disconnect/reconnect) клиенту LateClient: адрес и epoch — из свежего Status; исход читается в tick.
Func<string, string, bool> send = (action, comboId) => {
    var target = participant(LateClient);
    var record = new Dictionary<string, object> { { "action", action }, { "combo", comboId }, { "utc", DateTime.UtcNow.ToString("o") } };
    stand.Add(record);
    if (target == null) { record["error"] = "нет участника " + LateClient; return false; }
    var request = new VrBattlegrounds.EditorTools.TestStand.StandRequest {
        RunId = target.RunId, ParticipantId = target.ParticipantId, ProcessSessionId = target.ProcessSessionId,
        RequestId = "net-round1-" + Attempt + "-" + phaseKey + "-" + (++requestCounter) + "-" + action, Action = action, TimeBudgetMs = 5000,
        ExpectedConnectionEpoch = target.ConnectionEpoch, ExpectedAvatarNetId = 0 };
    record["requestId"] = request.RequestId; record["epochBefore"] = target.ConnectionEpoch; record["connectedBefore"] = target.Connected;
    record["operation"] = Newtonsoft.Json.Linq.JObject.Parse(VrBattlegrounds.EditorTools.TestStand.PlayModeTestStand.Send(request));
    pendingRequest = request.RequestId;
    return true; };
Action pollRequest = () => {
    if (pendingRequest == null) return;
    var operation = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.StandOperation>(VrBattlegrounds.EditorTools.TestStand.PlayModeTestStand.Operation(pendingRequest));
    if (!operation.Completed) return;
    var record = stand.LastOrDefault(r => r.ContainsKey("requestId") && (string)r["requestId"] == pendingRequest);
    if (record != null) record["reply"] = new { operation.EffectUnknown, passed = operation.Reply != null && operation.Reply.Passed,
        code = operation.Reply != null ? operation.Reply.Code : null, message = operation.Reply != null ? operation.Reply.Message : null,
        pid = operation.Reply != null ? operation.Reply.ProcessId : 0, completedUtc = DateTime.UtcNow.ToString("o") };
    pendingRequest = null; write(); };
Func<bool> lateConnected = () => { var p = participant(LateClient); return p != null && p.Connected; };
// Клиенты-люди на сервере: команда, место аватара, расстояние до линии огня (отрезок x=LaneOffsetX, z от -5 до 5).
Func<List<Dictionary<string, object>>> humans = () => {
    var list = new List<Dictionary<string, object>>();
    if (VrBattlegrounds.Managers.PlayersManager.Instance == null) return list;
    foreach (var s in VrBattlegrounds.Managers.PlayersManager.Instance.Sessions) {
        if (s == null || VrBattlegrounds.Bots.BotDirector.IsBotToken(s.DeviceToken) || (s.DeviceToken ?? "").StartsWith("bot-stand-target-")) continue;
        var avatar = s.ActiveAvatar;
        Vector3 p = avatar != null ? avatar.transform.position : Vector3.zero;
        float dz = Mathf.Clamp(p.z, -5f, 5f);
        float lane = avatar != null ? new Vector2(p.x - LaneOffsetX, p.z - dz).magnitude : -1f;
        list.Add(new Dictionary<string, object> { { "name", s.PlayerName }, { "token", s.DeviceToken }, { "role", s.Role.ToString() },
            { "teamIndex", s.TeamIndex }, { "avatarNetId", avatar != null ? avatar.netId : 0u },
            { "position", avatar != null ? new[] { p.x, p.y, p.z } : null }, { "distanceToLaneM", lane } });
    }
    return list; };
Action<VrBattlegrounds.DevTools.BotCombatStand.BotStandCaseResult, VrBattlegrounds.DevTools.BotCombatStand.BotStandSession> onStarted = (result, session) => {
    caseStart = Time.time;
    try {
        if (index >= 0 && index < entries.Count) {
            var subject = session != null ? session.Subject : null;
            var target = session != null ? session.Target : null;
            entries[index]["caseStartedUtc"] = DateTime.UtcNow.ToString("o");
            entries[index]["botToken"] = subject != null ? subject.DeviceToken : null;
            entries[index]["botSessionNetId"] = subject != null ? subject.netId : 0;
            entries[index]["botAvatarNetId"] = subject != null && subject.ActiveAvatar != null ? subject.ActiveAvatar.netId : 0;
            entries[index]["targetToken"] = target != null ? target.DeviceToken : null;
            entries[index]["botTeamIndex"] = subject != null ? subject.TeamIndex : 0;
            entries[index]["clientsAtStart"] = humans();
            write();
        }
    } catch { } };
EditorApplication.CallbackFunction tick = null;
Action detach = () => {
    VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.CaseStarted -= onStarted;
    EditorApplication.update -= tick; };
Action<string, string> fail = (state, error) => { queueState = state; queueError = error; write(); detach(); };
Action startNext = () => {
    index++;
    var combo = combos[index];
    var options = new VrBattlegrounds.DevTools.BotCombatStand.BotStandRunOptions {
        OutputDirectory = RunRoot + "/" + combo.id, Profile = VrBattlegrounds.DevTools.BotCombatStand.BotStandProfile.Production,
        Seeds = new[] { 101 }, CaseSeconds = combo.seconds, Capture = false, Rifle = combo.rifle, HandFit = false,
        WeaponName = combo.weapon, AvatarIndex = -1, PlayerFlow = true, ZeroAiAimSpread = combo.zero, NoRecoil = combo.noRecoil,
        HolsterStart = combo.holster, Unarmed = false, PurchaseWeaponName = combo.purchase, PurchaseAtSeconds = combo.purchaseAt,
        KillAtSeconds = combo.killAt, RespawnAtSeconds = combo.respawnAt, LaneOffsetX = LaneOffsetX, ClientNoRecoil = ClientNoRecoil, Cases = t07 };
    entries[index]["options"] = options;
    entries[index]["requestedUtc"] = DateTime.UtcNow.ToString("o");
    runLimit = combo.seconds + 60.0;
    caseStart = -1f; disconnectSent = false; reconnectSent = false;
    var handle = VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.RunCases(options, t07);
    runId = handle.RunId; entries[index]["runId"] = handle.RunId; startedAt = EditorApplication.timeSinceStartup;
    step = "running"; stepSince = EditorApplication.timeSinceStartup; write(); };
tick = () => {
    try {
        if (queueState != "golive" && queueState != "running") { detach(); return; }
        if (!EditorApplication.isPlaying) { fail("aborted", "Play завершён"); return; }
        pollRequest();
        double now = EditorApplication.timeSinceStartup;
        double inStep = now - stepSince;
        if (step == "golive") {
            var referee = VrBattlegrounds.Managers.MapReferee.Instance;
            if (referee == null) { if (inStep > 30) fail("failed", "нет MapReferee"); return; }
            if (!referee.IsLiveOrPaused) { if (!goLiveCalled) { goLiveCalled = true; notes.Add("GoLive=" + referee.GoLive()); write(); } else if (inStep > 30) fail("failed", "Live не начался"); return; }
            if (VrBattlegrounds.Bots.BotSenses.Stage() != VrBattlegrounds.Bots.BotStage.Combat) { if (inStep > 30) fail("failed", "стадия не Combat"); return; }
            queueState = "running"; step = "next"; stepSince = now; write(); return;
        }
        if (step == "next") {
            if (index + 1 >= combos.Length) { queueState = "completed"; step = "done"; write(); detach(); return; }
            if (VrBattlegrounds.Bots.BotSenses.Stage() != VrBattlegrounds.Bots.BotStage.Combat) { fail("live-ended", "Live завершён до " + combos[index + 1].id); return; }
            // Клиент с командой режима мог бы стать целью (или союзником) — сценарий недействителен, стоп.
            var teamed = humans().Where(h => h["role"].ToString() == "Player" && (int)h["teamIndex"] != 0).ToList();
            if (teamed.Count > 0) { notes.Add("клиенты с командой: " + Newtonsoft.Json.JsonConvert.SerializeObject(teamed)); fail("failed", "у клиента есть команда режима — он может стать целью бота"); return; }
            entries.Add(new Dictionary<string, object> { { "combo", combos[index + 1].id } });
            if (combos[index + 1].preDisconnect && Topology == "S2C") { step = "pre-disconnect"; stepSince = now; disconnectSent = false; write(); return; }
            startNext(); return;
        }
        if (step == "pre-disconnect") {
            // Опоздавший: отключить до начала case и дождаться Connected=false у стенда.
            if (pendingRequest != null) return;
            if (!disconnectSent) { disconnectSent = true; if (!send("disconnect", combos[index + 1].id)) fail("failed", "disconnect не отправлен"); return; }
            if (!lateConnected()) { notes.Add(combos[index + 1].id + ": " + LateClient + " отключён до начала case"); startNext(); return; }
            if (inStep > 20) fail("failed", LateClient + " не отключился за 20 с");
            return;
        }
        if (step == "running") {
            var combo = combos[index];
            if (caseStart >= 0f && Topology == "S2C") {
                float elapsed = Time.time - caseStart;
                if (combo.disconnectAt >= 0f && !disconnectSent && elapsed >= combo.disconnectAt && pendingRequest == null) { disconnectSent = true; send("disconnect", combo.id); }
                if (combo.reconnectAt >= 0f && !reconnectSent && elapsed >= combo.reconnectAt && pendingRequest == null) { reconnectSent = true; send("reconnect", combo.id); }
            }
            var status = VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.GetStatus(runId);
            if (status == null) { fail("failed", "status lost"); return; }
            if (!status.IsFinished) {
                double elapsedRun = now - startedAt;
                if (!cancelRequested && elapsedRun > runLimit) { VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.Cancel(runId); cancelRequested = true; entries[index]["watchdogCancelUtc"] = DateTime.UtcNow.ToString("o"); write(); }
                else if (cancelRequested && elapsedRun > runLimit + 30.0) fail("failed", "no finish after cancel");
                return;
            }
            entries[index]["finishedUtc"] = DateTime.UtcNow.ToString("o");
            entries[index]["status"] = new { status.Passed, status.Total, status.Completed, status.Failed, status.NeedsReview, status.InvalidFixture, status.Error, status.ReportPath };
            runId = null; cancelRequested = false; caseStart = -1f;
            step = "settle"; stepSince = now; reconnectSent = false; write(); return;
        }
        if (step == "settle") {
            // Перед следующим case опоздавший снова подключён (наблюдает с начала следующего case).
            if (pendingRequest != null) return;
            if (Topology != "S2C" || lateConnected()) { step = "next"; stepSince = now; write(); return; }
            if (!reconnectSent) { reconnectSent = true; send("reconnect", combos[index].id + ":settle"); return; }
            if (inStep > 40) fail("failed", LateClient + " не переподключился за 40 с после case");
            return;
        }
    } catch (Exception error) { fail("failed", error.ToString()); }
};
AppDomain.CurrentDomain.SetData(Marker, DateTime.UtcNow.ToString("o"));
VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.CaseStarted += onStarted;
write();
EditorApplication.update += tick;
return new { executionCompleted = true, started = true, attempt = Attempt, topology = Topology, phase = Phase, variant = Variant, clientNoRecoil = ClientNoRecoil, clients = humans(), combos = combos.Select(c => c.id).ToArray(),
    seconds = combos.Sum(c => c.seconds), nowUtc = DateTime.UtcNow.ToString("o") };
