// B4: четыре завершённых раунда -> adminStop либо naturalEnd -> один GoLive. C# 6; не повторять установку.
const string Root = "tasks/bots-fix/reports/cc2-lifecycle-round2";
const string Attempt = "attempt-1";
const string Run = "M2";
const string Owner = "bots-fix.cc2-lifecycle";
const double CapSeconds = 720;
const double NoLiveSeconds = 120;
const int TransitionCapacity = 256;
string runRoot = Root + "/runtime/" + Attempt + "/" + Run;
string marker = Owner + ".watch." + Attempt + "." + Run;
if (AppDomain.CurrentDomain.GetData(marker) != null || System.IO.File.Exists(runRoot + "/watch.json"))
    return new { executionCompleted = true, refused = true, reason = "Watcher уже был установлен; неизвестный исход не повторять." };
if (!EditorApplication.isPlaying || !Mirror.NetworkServer.active || Mirror.NetworkClient.active)
    throw new InvalidOperationException("Нужен ServerOnly Play.");
var launch = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
if (!launch.Ready || launch.Owner != Owner) throw new InvalidOperationException("Нужен собственный Ready launch.");
var launchEvidence = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(runRoot + "/launch-status.json"));
if ((string)launchEvidence["RunId"] != launch.RunId) throw new InvalidOperationException("RunId не совпадает с B3.");
var probe = VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.Active;
if (probe == null) throw new InvalidOperationException("Probe не активен.");
string probePath = probe.ReportPath;
string expectedProbeRoot = System.IO.Path.GetFullPath(runRoot + "/probe") + System.IO.Path.DirectorySeparatorChar;
if (string.IsNullOrEmpty(probePath) || !System.IO.Path.GetFullPath(probePath).StartsWith(expectedProbeRoot, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Active probe принадлежит другой заявке.");
string requestPath = VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.RequestPath;
if (System.IO.File.ReadAllText(requestPath) != System.IO.File.ReadAllText(runRoot + "/probe-request-immutable.json"))
    throw new InvalidOperationException("Immutable probe request изменился.");
var director = VrBattlegrounds.Bots.BotDirector.Instance;
var players = VrBattlegrounds.Managers.PlayersManager.Instance;
if (director == null || director.Bots.Count != 2 || players == null || players.Sessions.Count != 2 ||
    players.Sessions.Any(s => s == null || !director.Bots.Contains(s) || !VrBattlegrounds.Bots.BotDirector.IsBotToken(s.DeviceToken)))
    throw new InvalidOperationException("Нужны ровно 2 бота и 0 человеческих/чужих сессий.");
var initialBots = director.Bots.OrderBy(s => s.netId).ToArray();
uint[] sessionIds = initialBots.Select(s => s.netId).ToArray();
int[] sessionInstances = initialBots.Select(s => s.GetInstanceID()).ToArray();
var initialRoster = initialBots.Select(s => (object)new { sessionNetId = s.netId, sessionInstanceId = s.GetInstanceID(), name = s.PlayerName, token = s.DeviceToken }).ToArray();
string runId = launch.RunId;
double installed = EditorApplication.timeSinceStartup, phaseSince = installed, lastTick = -1, lastWrite = -1, lastProbeRead = -1;
string state = "waiting-first-live", finishReason = null, firstKey = null, lastSignature = null, firstCompletionKind = null;
ulong firstEpoch = 0, warmupEpoch = 0, secondEpoch = 0;
uint refereeId = 0, firstModeId = 0, secondModeId = 0;
int firstMaxRound = 0, secondMaxRound = 0, warmupCycles = 0, stopFrame = -1, transitionsDropped = 0, lastWarmupFrame = -1;
int runtimeErrors = 0;
string firstRuntimeError = null;
bool terminal = false, completed = false, stopIntent = false, goLiveIntent = false, firstNaturalProof = false, sameMindProof = false, secondRoundOneSeen = false;
int firstRoundWonEvents = 0;
int[] firstObservedBeganRounds = new int[0];
int[] firstScoredRounds = new int[0];
bool firstBaseProof = false, naturalEndProof = false, firstFinishObserved = false;
string firstFinishError = null;
int firstFinishFrame = -1, firstFinishWinner = 0, firstFinishRound = 0;
double firstFinishAt = 0;
uint warmupModeId = 0;
object firstFinishLatch = null, naturalEndEvidence = null;
VrBattlegrounds.Managers.MapReferee firstReferee = null;
VrBattlegrounds.GameModes.EliminationMode firstMode = null;
Action<VrBattlegrounds.TeamData> onFirstFinished = null;
var firstMinds = new Dictionary<uint, int>();
var secondMinds = new Dictionary<uint, int>();
var transitions = new List<object>();
var control = new Dictionary<string, object>();
Action<string, object> durable = (path, value) => {
    byte[] bytes = new System.Text.UTF8Encoding(false).GetBytes(Newtonsoft.Json.JsonConvert.SerializeObject(value, Newtonsoft.Json.Formatting.Indented).Replace("\r\n", "\n"));
    using (var stream = new System.IO.FileStream(path, System.IO.FileMode.CreateNew, System.IO.FileAccess.Write, System.IO.FileShare.Read)) {
        stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
    }
};
Action<string> write = note => {
    if (note != null) {
        if (transitions.Count >= TransitionCapacity) { transitionsDropped++; throw new InvalidOperationException("Transition capacity exceeded."); }
        transitions.Add(new { utc = DateTime.UtcNow.ToString("o"), elapsed = Math.Round(EditorApplication.timeSinceStartup - installed, 3), frame = Time.frameCount, state, note });
    }
    var referee = VrBattlegrounds.Managers.MapReferee.Instance;
    var mode = referee != null ? referee.ActiveGameMode as VrBattlegrounds.GameModes.EliminationMode : null;
    var authority = VrBattlegrounds.Maps.Runtime.MapRunAuthority.Instance;
    var snapshot = authority != null ? authority.Current : default(VrBattlegrounds.Maps.Runtime.MapRunSnapshot);
    var result = new {
        schema = "cc2-lifecycle-watch@2", state, completed, finishReason, owner = Owner, attempt = Attempt, run = Run, runId,
        elapsed = Math.Round(EditorApplication.timeSinceStartup - installed, 3), capSeconds = CapSeconds, noLiveSeconds = NoLiveSeconds,
        sessionIds, sessionInstances, initialRoster, firstKey, firstEpoch, warmupEpoch, secondEpoch, refereeId, firstModeId, secondModeId,
        firstMaxRound, secondMaxRound, firstRoundWonEvents, firstObservedBeganRounds, firstScoredRounds, firstBaseProof,
        firstNaturalProof, firstCompletionKind, naturalEndProof, naturalEndEvidence, firstFinishObserved, firstFinishLatch, firstFinishError, warmupModeId,
        secondRoundOneSeen, sameMindProof, firstMinds, secondMinds,
        stopIntent, goLiveIntent, transitionsDropped, runtimeErrors, firstRuntimeError,
        descriptor = new { ready = snapshot.IsReady, key = snapshot.Key.ToString(), epoch = snapshot.ModeEpoch, mode = snapshot.ActiveModeId, modeNetId = snapshot.ActiveModeNetId, refereeNetId = snapshot.RefereeNetId },
        mapState = referee != null ? referee.CurrentState.ToString() : null,
        round = mode != null ? mode.CurrentRoundNumber : -1, phase = mode != null ? mode.CurrentRoundPhase.ToString() : null,
        updatedUtc = DateTime.UtcNow.ToString("o"), probePath, transitions,
        scope = "FUNCTIONAL ServerOnly M2; 4 завершённых раунда, adminStop либо naturalEnd различаются; один GoLive; не perf/Quest; seed UNKNOWN"
    };
    string temp = runRoot + "/watch.json.tmp";
    System.IO.File.WriteAllText(temp, Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented).Replace("\r\n", "\n"), new System.Text.UTF8Encoding(false));
    if (System.IO.File.Exists(runRoot + "/watch.json")) System.IO.File.Replace(temp, runRoot + "/watch.json", null);
    else System.IO.File.Move(temp, runRoot + "/watch.json");
};
// Читаем только собственный durable report; сохраняем все данные в самой probe, не копируем raw в watch.
Action readProbe = () => {
    if (!System.IO.File.Exists(requestPath) || System.IO.File.ReadAllText(requestPath) != System.IO.File.ReadAllText(runRoot + "/probe-request-immutable.json"))
        throw new InvalidOperationException("Immutable probe request потерян/изменён.");
    if (!System.IO.File.Exists(probePath)) return;
    var report = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(probePath));
    if ((string)report["runTag"] != "cc2-lifecycle-round2/" + Attempt + "/" + Run || (bool?)report["directorEquipEnabled"] != true)
        throw new InvalidOperationException("Probe report другой заявки либо DirectorEquip выключен.");
    if ((bool?)report["final"] == true) throw new InvalidOperationException("Probe завершилась раньше watcher.");
    var console = report["console"];
    if (console == null || (int?)console["errors"] != 0 || (int?)console["exceptions"] != 0 || (int?)console["asserts"] != 0 || (int?)console["relayErrors"] != 0)
        throw new InvalidOperationException("Probe console содержит ошибку/исключение/assert/Relay.");
    var journal = report["directorEquip"];
    var match = report["match"];
    if (journal == null || match == null || (int?)journal["droppedEvents"] != 0 || (int?)journal["droppedInventory"] != 0 ||
        (int?)journal["errors"] != 0 || (int?)match["timelineDropped"] != 0)
        throw new InvalidOperationException("Журнал probe неполон/ошибочен.");
    var events = journal["events"] as Newtonsoft.Json.Linq.JArray;
    var timeline = match["timeline"] as Newtonsoft.Json.Linq.JArray;
    if (events == null || timeline == null) throw new InvalidOperationException("Нет director events/timeline.");
    for (int i = 0; i < sessionIds.Length; i++) {
        uint sid = sessionIds[i];
        foreach (var entry in events) {
            if ((uint?)entry["sessionNetId"] != sid || (string)entry["runKey"] != firstKey) continue;
            if ((int?)entry["sessionInstanceId"] != sessionInstances[i]) throw new InvalidOperationException("Сменился instance сессии.");
            ulong epoch = (ulong?)entry["modeEpoch"] ?? 0;
            int mind = (int?)entry["mindId"] ?? 0;
            if (mind <= 0) throw new InvalidOperationException("Нет mindId.");
            var target = epoch == firstEpoch ? firstMinds : epoch == secondEpoch && secondEpoch != 0 ? secondMinds : null;
            if (target != null) {
                if (target.ContainsKey(sid) && target[sid] != mind) throw new InvalidOperationException("Mind изменился в пределах режима.");
                target[sid] = mind;
            }
        }
    }
    if (state == "first-match" || state == "waiting-natural-proof") {
        int firstModeEvents = timeline.Count(e => (string)e["kind"] == "mode" && (string)e["type"] == "EliminationMode");
        if (firstModeEvents > 1) throw new InvalidOperationException("Первый Elimination timeline неоднозначен.");
        bool scored = Enumerable.Range(1, 4).All(round => timeline.Any(e => (string)e["kind"] == "roundScored" && (int?)e["round"] == round));
        firstScoredRounds = timeline.Where(e => (string)e["kind"] == "roundScored").Select(e => (int?)e["round"] ?? -1).Distinct().OrderBy(round => round).ToArray();
        firstObservedBeganRounds = timeline.Where(e => (string)e["kind"] == "roundBegan").Select(e => (int?)e["round"] ?? -1).Distinct().OrderBy(round => round).ToArray();
        firstRoundWonEvents = timeline.Count(e => (string)e["kind"] == "roundWon");
        bool bought = sessionIds.All(sid => events.Any(e => (uint?)e["sessionNetId"] == sid && (ulong?)e["modeEpoch"] == firstEpoch &&
            (string)e["kind"] == "buy" && (int?)e["shopToken"] == 1 && (string)e["runKey"] == firstKey));
        // Ничья даёт roundScored, но не roundWon. Полный цикл доказывает Round5 либо naturalEnd.
        firstBaseProof = firstModeEvents == 1 && scored && firstMinds.Count == 2 && bought;
        var matchEnds = timeline.Where(e => (string)e["kind"] == "matchFinished").ToArray();
        var modeEnds = timeline.Where(e => (string)e["kind"] == "modeFinished").ToArray();
        if (matchEnds.Length > 1 || modeEnds.Length > 1) throw new InvalidOperationException("Первый naturalEnd timeline неоднозначен.");
        if (firstFinishObserved && matchEnds.Length == 1 && modeEnds.Length == 1) {
            var matchEnd = matchEnds[0];
            var modeEnd = modeEnds[0];
            if ((int?)matchEnd["winner"] != firstFinishWinner || (int?)modeEnd["winner"] != firstFinishWinner ||
                (int?)matchEnd["rounds"] != firstFinishRound || matchEnd["scores"] == null ||
                !Newtonsoft.Json.Linq.JToken.DeepEquals(matchEnd["scores"], modeEnd["scores"]) ||
                timeline.IndexOf(matchEnd) >= timeline.IndexOf(modeEnd))
                throw new InvalidOperationException("Durable naturalEnd не совпадает с exact referee latch.");
            bool endAfterScored = Enumerable.Range(1, 4).All(round => timeline.Take(timeline.IndexOf(matchEnd)).Any(e =>
                (string)e["kind"] == "roundScored" && (int?)e["round"] == round));
            naturalEndProof = firstBaseProof && firstFinishRound >= 4 && endAfterScored;
            if (naturalEndProof) naturalEndEvidence = new { matchFinished = matchEnd, modeFinished = modeEnd, reportUpdatedUtc = (string)report["updatedUtc"] };
        }
    }
    if (secondEpoch != 0) {
        bool firstRoundBuys = sessionIds.All(sid => events.Any(e => (uint?)e["sessionNetId"] == sid && (ulong?)e["modeEpoch"] == secondEpoch &&
            (string)e["kind"] == "buy" && (int?)e["shopToken"] == 1 && (string)e["runKey"] == firstKey));
        bool reset = sessionIds.All(sid => events.Any(e => (uint?)e["sessionNetId"] == sid && (ulong?)e["modeEpoch"] == secondEpoch &&
            (string)e["kind"] == "contextReset" && (string)e["runKey"] == firstKey));
        sameMindProof = firstRoundBuys && reset && firstMinds.Count == 2 && secondMinds.Count == 2 &&
            firstMinds.Values.Distinct().Count() == 2 && secondMinds.Values.Distinct().Count() == 2 &&
            sessionIds.All(sid => firstMinds.ContainsKey(sid) && secondMinds.ContainsKey(sid) && firstMinds[sid] == secondMinds[sid]);
    }
};
EditorApplication.CallbackFunction tick = null;
Action quitting = null;
// Finished публикуется до Warmup-коммита. Только чтение/фиксация, исключения не уходят в publisher.
onFirstFinished = winner => {
    if (terminal || firstFinishObserved || firstFinishError != null || state != "first-match" || firstCompletionKind != null || stopIntent || goLiveIntent) return;
    try {
        var own = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
        var authority = VrBattlegrounds.Maps.Runtime.MapRunAuthority.Instance;
        var snapshot = authority != null ? authority.Current : default(VrBattlegrounds.Maps.Runtime.MapRunSnapshot);
        if (own.Owner != Owner || own.RunId != runId || !own.Ready ||
            VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.Active != probe ||
            !System.IO.File.Exists(requestPath) || System.IO.File.ReadAllText(requestPath) != System.IO.File.ReadAllText(runRoot + "/probe-request-immutable.json") ||
            firstReferee == null || firstMode == null || VrBattlegrounds.Managers.MapReferee.Instance != firstReferee ||
            firstReferee.ActiveGameMode != firstMode || firstReferee.CurrentState != VrBattlegrounds.Managers.MapState.Live ||
            !snapshot.IsReady || snapshot.Key.ToString() != firstKey || snapshot.ModeEpoch != firstEpoch ||
            snapshot.RefereeNetId != refereeId || snapshot.ActiveModeNetId != firstModeId ||
            firstReferee.netId != refereeId || firstMode.netId != firstModeId)
            throw new InvalidOperationException("Finished source/identity не совпадает с собственным первым mode.");
        firstFinishFrame = Time.frameCount; firstFinishAt = EditorApplication.timeSinceStartup;
        firstFinishWinner = winner != null ? winner.teamIndex : 0; firstFinishRound = firstMode.CurrentRoundNumber;
        firstFinishLatch = new { utc = DateTime.UtcNow.ToString("o"), runId, owner = Owner, key = firstKey, epoch = firstEpoch,
            refereeNetId = refereeId, modeNetId = firstModeId, frame = firstFinishFrame, winner = firstFinishWinner, round = firstFinishRound };
        firstFinishObserved = true;
    } catch (Exception error) { firstFinishError = error.GetType().Name + ": " + error.Message; }
};
Application.LogCallback onLog = (message, stack, type) => {
    if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) {
        System.Threading.Interlocked.Increment(ref runtimeErrors);
        if (firstRuntimeError == null) firstRuntimeError = (message ?? "").Substring(0, Math.Min(500, (message ?? "").Length));
    }
};
Action<string, bool> finish = (reason, success) => {
    if (terminal) return;
    terminal = true; completed = success; finishReason = reason; state = "finishing";
    if (!object.ReferenceEquals(firstReferee, null)) firstReferee.Finished -= onFirstFinished;
    control["terminal"] = true;
    string failure = null;
    try { write("Завершение: " + reason); } catch (Exception e) { failure = "watch-write: " + e.Message; completed = false; }
    try {
        durable(runRoot + "/finish-intent.json", new { utc = DateTime.UtcNow.ToString("o"), runId, owner = Owner, reason, completed });
        if (VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.Active != probe)
            throw new InvalidOperationException("Active probe изменился; чужой FinishActive запрещён.");
        string path = VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.FinishActive(reason, completed);
        if (string.IsNullOrEmpty(path) || System.IO.Path.GetFullPath(path) != System.IO.Path.GetFullPath(probePath) || !System.IO.File.Exists(path))
            throw new InvalidOperationException("FinishActive не сохранил ожидаемый полный отчёт.");
        var finalReport = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));
        if ((bool?)finalReport["final"] != true || (string)finalReport["runTag"] != "cc2-lifecycle-round2/" + Attempt + "/" + Run)
            throw new InvalidOperationException("Нет собственного durable final probe; FinishActive не повторять.");
        var finalJournal = finalReport["directorEquip"];
        if (completed && (finalJournal == null || (bool?)finalJournal["completed"] != true || (bool?)finalJournal["incomplete"] != false ||
            (int?)finalJournal["droppedEvents"] != 0 || (int?)finalJournal["droppedInventory"] != 0 || (int?)finalJournal["errors"] != 0 ||
            (int?)finalReport["match"]?["timelineDropped"] != 0 || (int?)finalReport["console"]?["errors"] != 0 ||
            (int?)finalReport["console"]?["exceptions"] != 0 || (int?)finalReport["console"]?["asserts"] != 0 ||
            (int?)finalReport["console"]?["relayErrors"] != 0))
            throw new InvalidOperationException("Финальный lifecycle journal неполон или содержит ошибки.");
        durable(runRoot + "/finish-return.json", new { utc = DateTime.UtcNow.ToString("o"), probePath = path, completed, durableFinalVerified = true });
    } catch (Exception e) { failure = (failure ?? "") + " finish: " + e.Message; completed = false; }
    try {
        var own = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
        if (own.Owner == Owner && own.RunId == runId && own.State != "Idle") {
            durable(runRoot + "/cancel-intent.json", new { utc = DateTime.UtcNow.ToString("o"), runId, owner = Owner, reason });
            var after = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Cancel(runId, Owner));
            durable(runRoot + "/cancel-return.json", after);
        } else if (own.State != "Idle") throw new InvalidOperationException("Owner/RunId сменился: чужой launch не отменять.");
    } catch (Exception e) { failure = (failure ?? "") + " cancel: " + e.Message; completed = false; }
    state = failure == null ? "stopped" : "stop-error";
    if (failure != null) finishReason += "; " + failure;
    try { write(null); } catch { }
    EditorApplication.update -= tick;
    EditorApplication.quitting -= quitting;
    Application.logMessageReceivedThreaded -= onLog;
    AppDomain.CurrentDomain.SetData(marker, null);
};
quitting = () => finish("editor-quit", false);
tick = () => {
    if (terminal) return;
    try {
        double now = EditorApplication.timeSinceStartup;
        if (now - installed >= CapSeconds) { finish("cap-720s", false); return; }
        if ((state == "waiting-first-live" || state == "waiting-natural-proof" || state == "waiting-warmup" || state == "waiting-second-live") && now - phaseSince >= NoLiveSeconds) {
            finish("no-live-or-warmup-120s", false); return;
        }
        if (runtimeErrors != 0) { finish("runtime-error", false); return; }
        if (firstFinishError != null) { finish("first-finish-latch-error: " + firstFinishError, false); return; }
        if (!EditorApplication.isPlaying || !Mirror.NetworkServer.active || Mirror.NetworkClient.active) { finish("play-or-serveronly-ended", false); return; }
        if (now - lastTick < .25) return;
        lastTick = now;
        var own = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
        if (own.Owner != Owner || own.RunId != runId || !own.Ready) { finish("launch-owner-run-or-ready-lost", false); return; }
        if (VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.Active != probe) { finish("probe-lost", false); return; }
        var liveDirector = VrBattlegrounds.Bots.BotDirector.Instance;
        var livePlayers = VrBattlegrounds.Managers.PlayersManager.Instance;
        if (liveDirector == null || livePlayers == null || liveDirector.Bots.Count != 2 || livePlayers.Sessions.Count != 2 ||
            initialBots.Any(s => s == null || !liveDirector.Bots.Contains(s) || !livePlayers.Sessions.Contains(s)) ||
            initialBots.Where((s, i) => s.netId != sessionIds[i] || s.GetInstanceID() != sessionInstances[i]).Any()) {
            finish("foreign-or-session-loss", false); return;
        }
        var referee = VrBattlegrounds.Managers.MapReferee.Instance;
        var authority = VrBattlegrounds.Maps.Runtime.MapRunAuthority.Instance;
        var snapshot = authority != null ? authority.Current : default(VrBattlegrounds.Maps.Runtime.MapRunSnapshot);
        var mode = referee != null ? referee.ActiveGameMode : null;
        var elimination = mode as VrBattlegrounds.GameModes.EliminationMode;
        bool ready = referee != null && mode != null && snapshot.IsReady && snapshot.Key.IsValid && snapshot.ModeEpoch != 0 &&
            snapshot.RefereeNetId == referee.netId && snapshot.ActiveModeNetId == mode.netId;
        bool live = ready && referee.CurrentState == VrBattlegrounds.Managers.MapState.Live && elimination != null && !mode.IsWarmup;
        if (firstKey != null && ready && (snapshot.Key.ToString() != firstKey || snapshot.RefereeNetId != refereeId)) {
            finish("map-run-or-referee-changed", false); return;
        }
        if (ready && referee.CurrentState == VrBattlegrounds.Managers.MapState.Paused) { finish("unexpected-pause", false); return; }
        if (state == "waiting-first-live" && live) {
            firstKey = snapshot.Key.ToString(); firstEpoch = snapshot.ModeEpoch; refereeId = referee.netId; firstModeId = mode.netId;
            firstReferee = referee; firstMode = elimination; firstReferee.Finished += onFirstFinished;
            state = "first-match"; phaseSince = now; write("Первый Live descriptor принят.");
        }
        if (now - lastProbeRead >= 5) { lastProbeRead = now; readProbe(); }
        if (state == "first-match") {
            if (!live || snapshot.ModeEpoch != firstEpoch) {
                if (!firstFinishObserved) { finish("first-mode-changed-without-exact-natural-end", false); return; }
                firstCompletionKind = "naturalEnd"; state = "waiting-natural-proof"; phaseSince = firstFinishAt;
                lastProbeRead = now; readProbe();
                write("Exact naturalEnd наблюдён; ждём durable pair, обычный flush не повторяется.");
            } else {
                firstMaxRound = Math.Max(firstMaxRound, elimination.CurrentRoundNumber);
            }
            if (state == "first-match" && !firstFinishObserved && firstMaxRound >= 5 && firstBaseProof) {
                if (!VrBattlegrounds.Network.StateEventAuthority.IsWorldAuthority) { finish("authority-lost-before-stop", false); return; }
                firstCompletionKind = "adminStop"; firstNaturalProof = true;
                stopIntent = true; stopFrame = Time.frameCount; state = "waiting-warmup"; phaseSince = now;
                durable(runRoot + "/admin-stop-intent.json", new { utc = DateTime.UtcNow.ToString("o"), runId, sessionIds, firstEpoch, firstMaxRound, firstRoundWonEvents, firstNaturalProof, frame = stopFrame });
                write("Один штатный Stop администратора; не естественный конец матча.");
                referee.Stop();
                durable(runRoot + "/admin-stop-return.json", new { utc = DateTime.UtcNow.ToString("o"), returned = true });
                return;
            }
        }
        if (state == "waiting-natural-proof" && now - phaseSince >= NoLiveSeconds) {
            finish("no-live-or-warmup-120s", false); return;
        }
        if (state == "waiting-natural-proof" && naturalEndProof) {
            firstNaturalProof = true; state = "waiting-warmup";
            write("Durable naturalEnd первого mode подтверждён; adminStop этой веткой не проверен.");
        }
        if (state == "waiting-natural-proof" || state == "waiting-warmup") {
            bool ownWarmup = ready && mode.IsWarmup && referee.CurrentState == VrBattlegrounds.Managers.MapState.Warmup &&
                snapshot.ModeEpoch > firstEpoch && snapshot.ActiveModeId == snapshot.Config.WarmupModeId && snapshot.ActiveModeNetId != firstModeId;
            if (ready && !ownWarmup) { finish("unexpected-mode-before-second-live", false); return; }
            if (ownWarmup) {
                if (warmupEpoch == 0) { warmupEpoch = snapshot.ModeEpoch; warmupModeId = snapshot.ActiveModeNetId; }
                else if (snapshot.ModeEpoch != warmupEpoch || snapshot.ActiveModeNetId != warmupModeId) { finish("warmup-descriptor-changed", false); return; }
            }
            int completionFrame = firstCompletionKind == "adminStop" ? stopFrame : firstFinishFrame;
            bool warmup = ready && mode.IsWarmup && referee.CurrentState == VrBattlegrounds.Managers.MapState.Warmup &&
                ownWarmup && state == "waiting-warmup" && firstNaturalProof && Time.frameCount > completionFrame + 1;
            if (!warmup) { warmupCycles = 0; lastWarmupFrame = -1; }
            else if (Time.frameCount != lastWarmupFrame) { warmupCycles++; lastWarmupFrame = Time.frameCount; }
            if (warmupCycles >= 2) {
                if (!VrBattlegrounds.Network.StateEventAuthority.IsWorldAuthority) { finish("authority-lost-before-golive", false); return; }
                warmupEpoch = snapshot.ModeEpoch; goLiveIntent = true; state = "waiting-second-live"; phaseSince = now;
                durable(runRoot + "/admin-golive-intent.json", new { utc = DateTime.UtcNow.ToString("o"), runId, sessionIds, firstEpoch, warmupEpoch, key = firstKey, warmupCycles, firstCompletionKind, naturalEndProof });
                write("Готовый Warmup descriptor подтверждён; один GoLive администратора.");
                bool accepted = referee.GoLive();
                durable(runRoot + "/admin-golive-return.json", new { utc = DateTime.UtcNow.ToString("o"), accepted });
                if (!accepted) finish("golive-returned-false-no-retry", false);
                return;
            }
        } else if (state == "waiting-second-live" && live) {
            if (snapshot.ModeEpoch <= warmupEpoch || snapshot.ActiveModeNetId == firstModeId) { finish("second-descriptor-not-new", false); return; }
            if (secondEpoch != 0 && snapshot.ModeEpoch != secondEpoch) { finish("second-descriptor-changed-before-round1", false); return; }
            secondEpoch = snapshot.ModeEpoch; secondModeId = mode.netId;
            // Ready descriptor публикуется до BeginWhenReady: Round0/WaitingForPlayers ещё штатен.
            if (elimination.CurrentRoundNumber > 1) { finish("second-round1-not-observed", false); return; }
            if (elimination.CurrentRoundNumber == 1) {
                secondRoundOneSeen = true;
                state = "second-match"; phaseSince = now; lastProbeRead = -1; write("Второй матч: Round1 и новый ModeEpoch, те же сессии.");
            }
        }
        if (state == "second-match") {
            if (!live || snapshot.ModeEpoch != secondEpoch) { finish("second-match-ended-or-mode-changed", false); return; }
            secondMaxRound = Math.Max(secondMaxRound, elimination.CurrentRoundNumber);
            if (secondMaxRound >= 2 && sameMindProof && firstNaturalProof) { finish("normal-" + firstCompletionKind + "-warmup-second-round2-sameMind", true); return; }
        }
        string signature = state + "/" + (ready ? snapshot.ModeEpoch.ToString() : "not-ready") + "/" +
            (elimination != null ? elimination.CurrentRoundNumber + "/" + elimination.CurrentRoundPhase : "no-elimination");
        if (signature != lastSignature) { lastSignature = signature; write("Переход " + signature); }
        if (now - lastWrite >= 5) { lastWrite = now; write(null); }
    } catch (Exception error) { finish("watch-error: " + error.Message, false); }
};
control["finish"] = finish; control["runId"] = runId; control["owner"] = Owner; control["terminal"] = false;
durable(runRoot + "/watch-install.json", new { utc = DateTime.UtcNow.ToString("o"), runId, owner = Owner, sessionIds, sessionInstances, initialRoster, probePath, capSeconds = CapSeconds });
write("Watcher установлен: 2 бота, 0 людей; adminStop/naturalEnd различаются; один GoLive, снаряжение/урон не меняются.");
AppDomain.CurrentDomain.SetData(marker, control);
Application.logMessageReceivedThreaded += onLog;
EditorApplication.quitting += quitting;
EditorApplication.update += tick;
return new { executionCompleted = true, installed = true, runId, sessionIds, probePath, capSeconds = CapSeconds,
    next = "B5 read-only; main сохраняет lease/guard. После stopped/Idle — B6 cleanup заявки, затем finish/receive." };
