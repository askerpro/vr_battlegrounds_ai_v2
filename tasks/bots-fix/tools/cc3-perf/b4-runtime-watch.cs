// B4: guarded-сторож ячейки cc-3 (4 завершённых раунда roundScored 1..4 -> adminStop на Round>=5 либо exact naturalEnd).
// Использует 1-аргументный BotStandMatchProbeGate.FinishActive(reason); без B7/Blaze/NetObserver подписок. C# 6; не повторять установку.
const string Root = "tasks/bots-fix/reports/cc3-perf";
const string Attempt = "attempt-1";
const string Cell = "C8"; // H8 | C8 | H10 | C10 | RH | S8
const string Owner = "bots-fix.cc3-perf";
const double CapSeconds = 600;
const double NoLiveSeconds = 120;
const int TransitionCapacity = 256;
string runRoot = Root + "/runtime/" + Attempt + "/" + Cell;
string tag = "cc3-perf/" + Attempt + "/" + Cell;
string marker = Owner + ".watch." + Attempt + "." + Cell;
if (AppDomain.CurrentDomain.GetData(marker) != null || System.IO.File.Exists(runRoot + "/watch.json"))
    return new { executionCompleted = true, refused = true, reason = "Watcher уже был установлен; неизвестный исход не повторять." };
if (!EditorApplication.isPlaying || !Mirror.NetworkServer.active)
    throw new InvalidOperationException("Нужен Play с активным сервером в главном процессе.");
bool expectHost = Cell == "H8" || Cell == "C8" || Cell == "H10" || Cell == "C10";
if (expectHost && !Mirror.NetworkClient.active)
    throw new InvalidOperationException("Для Host-ячейки нужен активный локальный клиент хоста.");
if (!expectHost && Mirror.NetworkClient.active)
    throw new InvalidOperationException("Для Server-ячейки (RH/S8) главный процесс должен быть Dedicated Server без локального клиента.");
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
int expectedBots = (Cell == "H10" || Cell == "C10") ? 10 : 8;
var director = VrBattlegrounds.Bots.BotDirector.Instance;
if (director == null || director.Bots.Count != expectedBots)
    throw new InvalidOperationException("Ожидалось ботов: " + expectedBots + ", фактически: " + (director != null ? director.Bots.Count : -1));
var initialBots = director.Bots.OrderBy(s => s.netId).ToArray();
uint[] botSessionIds = initialBots.Select(s => s.netId).ToArray();
int[] botSessionInstances = initialBots.Select(s => s.GetInstanceID()).ToArray();
string runId = launch.RunId;
double installed = EditorApplication.timeSinceStartup, phaseSince = installed, lastTick = -1, lastWrite = -1, lastProbeRead = -1;
string state = "waiting-live", finishReason = null, completionKind = null, firstKey = null, lastSignature = null;
ulong firstEpoch = 0;
uint refereeId = 0, firstModeId = 0;
int maxRound = 0, transitionsDropped = 0, runtimeErrors = 0, firstFinishFrame = -1, firstFinishWinner = 0, firstFinishRound = 0;
double firstFinishAt = 0;
string firstRuntimeError = null, firstFinishError = null;
bool terminal = false, completed = false, stopIntent = false, fourRoundsScored = false, naturalEndProof = false, firstFinishObserved = false;
bool gcRecorderValid = false;
int[] observedBeganRounds = new int[0];
int[] scoredRounds = new int[0];
object firstFinishLatch = null, naturalEndEvidence = null, markerValidSnapshot = null;
VrBattlegrounds.Managers.MapReferee firstReferee = null;
VrBattlegrounds.GameModes.EliminationMode firstMode = null;
Action<VrBattlegrounds.TeamData> onFirstFinished = null;
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
    var result = new {
        schema = "cc3-perf-watch@1", state, completed, finishReason, completionKind, owner = Owner, attempt = Attempt, cell = Cell, runId,
        elapsed = Math.Round(EditorApplication.timeSinceStartup - installed, 3), capSeconds = CapSeconds, noLiveSeconds = NoLiveSeconds,
        expectedBots, botSessionIds, botSessionInstances, firstKey, firstEpoch, refereeId, firstModeId,
        maxRound, observedBeganRounds, scoredRounds, fourRoundsScored, naturalEndProof, naturalEndEvidence,
        firstFinishObserved, firstFinishLatch, firstFinishError, stopIntent, gcRecorderValid, markerValid = markerValidSnapshot,
        transitionsDropped, runtimeErrors, firstRuntimeError,
        mapState = referee != null ? referee.CurrentState.ToString() : null,
        round = mode != null ? mode.CurrentRoundNumber : -1, phase = mode != null ? mode.CurrentRoundPhase.ToString() : null,
        updatedUtc = DateTime.UtcNow.ToString("o"), probePath, transitions
    };
    string temp = runRoot + "/watch.json.tmp";
    System.IO.File.WriteAllText(temp, Newtonsoft.Json.JsonConvert.SerializeObject(result, Newtonsoft.Json.Formatting.Indented).Replace("\r\n", "\n"), new System.Text.UTF8Encoding(false));
    if (System.IO.File.Exists(runRoot + "/watch.json")) System.IO.File.Replace(temp, runRoot + "/watch.json", null);
    else System.IO.File.Move(temp, runRoot + "/watch.json");
};
Action readProbe = () => {
    if (!System.IO.File.Exists(requestPath) || System.IO.File.ReadAllText(requestPath) != System.IO.File.ReadAllText(runRoot + "/probe-request-immutable.json"))
        throw new InvalidOperationException("Immutable probe request потерян/изменён.");
    if (!System.IO.File.Exists(probePath)) return;
    var report = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(probePath));
    if ((string)report["runTag"] != tag)
        throw new InvalidOperationException("Probe report принадлежит другой заявке: " + (string)report["runTag"]);
    if ((bool?)report["final"] == true)
        throw new InvalidOperationException("Probe завершилась раньше watcher.");
    var console = report["console"];
    if (console == null || (int?)console["errors"] != 0 || (int?)console["exceptions"] != 0 || (int?)console["asserts"] != 0 || (int?)console["relayErrors"] != 0)
        throw new InvalidOperationException("Probe console содержит ошибку/исключение/assert/Relay.");
    var frame = report["frame"];
    if (frame != null) {
        gcRecorderValid = (bool?)frame["gcRecorderValid"] ?? false;
        markerValidSnapshot = frame["markerValid"];
    }
    var match = report["match"];
    if (match == null || (int?)match["timelineDropped"] != 0)
        throw new InvalidOperationException("Журнал match.timeline неполон либо отсутствует.");
    var timeline = match["timeline"] as Newtonsoft.Json.Linq.JArray;
    if (timeline == null) throw new InvalidOperationException("Нет match.timeline.");
    scoredRounds = timeline.Where(e => (string)e["kind"] == "roundScored").Select(e => (int?)e["round"] ?? -1).Distinct().OrderBy(r => r).ToArray();
    observedBeganRounds = timeline.Where(e => (string)e["kind"] == "roundBegan").Select(e => (int?)e["round"] ?? -1).Distinct().OrderBy(r => r).ToArray();
    fourRoundsScored = Enumerable.Range(1, 4).All(r => scoredRounds.Contains(r));
    var matchEnds = timeline.Where(e => (string)e["kind"] == "matchFinished").ToArray();
    var modeEnds = timeline.Where(e => (string)e["kind"] == "modeFinished").ToArray();
    if (matchEnds.Length > 1 || modeEnds.Length > 1)
        throw new InvalidOperationException("Timeline содержит неоднозначный конец матча.");
    if (firstFinishObserved && matchEnds.Length == 1 && modeEnds.Length == 1) {
        var matchEnd = matchEnds[0];
        var modeEnd = modeEnds[0];
        if ((int?)matchEnd["winner"] != firstFinishWinner || (int?)modeEnd["winner"] != firstFinishWinner ||
            (int?)matchEnd["rounds"] != firstFinishRound || matchEnd["scores"] == null ||
            !Newtonsoft.Json.Linq.JToken.DeepEquals(matchEnd["scores"], modeEnd["scores"]) ||
            timeline.IndexOf(matchEnd) >= timeline.IndexOf(modeEnd))
            throw new InvalidOperationException("Durable naturalEnd не совпадает с exact referee latch.");
        naturalEndProof = firstFinishRound >= 4 && scoredRounds.Length >= firstFinishRound;
        if (naturalEndProof) naturalEndEvidence = new { matchFinished = matchEnd, modeFinished = modeEnd, reportUpdatedUtc = (string)report["updatedUtc"] };
    }
};
EditorApplication.CallbackFunction tick = null;
Action quitting = null;
onFirstFinished = winner => {
    if (terminal || firstFinishObserved || firstFinishError != null || state != "live-match" || completionKind != null || stopIntent) return;
    try {
        var own = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
        if (own.Owner != Owner || own.RunId != runId || !own.Ready ||
            VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.Active != probe ||
            firstReferee == null || firstMode == null || VrBattlegrounds.Managers.MapReferee.Instance != firstReferee ||
            firstReferee.ActiveGameMode != firstMode || firstReferee.CurrentState != VrBattlegrounds.Managers.MapState.Live)
            throw new InvalidOperationException("Finished source/identity не совпадает с собственным mode.");
        firstFinishFrame = Time.frameCount; firstFinishAt = EditorApplication.timeSinceStartup;
        firstFinishWinner = winner != null ? winner.teamIndex : 0; firstFinishRound = firstMode.CurrentRoundNumber;
        firstFinishLatch = new { utc = DateTime.UtcNow.ToString("o"), runId, owner = Owner, cell = Cell,
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
        durable(runRoot + "/finish-intent.json", new { utc = DateTime.UtcNow.ToString("o"), runId, owner = Owner, cell = Cell, reason, completed });
        if (VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.Active != probe)
            throw new InvalidOperationException("Active probe изменился; чужой FinishActive запрещён.");
        // Внимание: общая проба (2fa11b27) принимает ровно 1 строковый аргумент FinishActive(string reason).
        string path = VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.FinishActive(reason);
        if (string.IsNullOrEmpty(path) || System.IO.Path.GetFullPath(path) != System.IO.Path.GetFullPath(probePath) || !System.IO.File.Exists(path))
            throw new InvalidOperationException("FinishActive не сохранил ожидаемый полный отчёт.");
        var finalReport = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));
        if ((bool?)finalReport["final"] != true || (string)finalReport["runTag"] != tag)
            throw new InvalidOperationException("Нет собственного durable final probe; FinishActive не повторять.");
        if (completed && ((int?)finalReport["match"]?["timelineDropped"] != 0 || (int?)finalReport["console"]?["errors"] != 0 ||
            (int?)finalReport["console"]?["exceptions"] != 0 || (int?)finalReport["console"]?["asserts"] != 0 ||
            (int?)finalReport["console"]?["relayErrors"] != 0 || (bool?)finalReport["frame"]?["gcRecorderValid"] != true))
            throw new InvalidOperationException("Финальный отчёт пробы содержит ошибки консоли, потери timeline или невалидный gcRecorder.");
        durable(runRoot + "/finish-return.json", new { utc = DateTime.UtcNow.ToString("o"), probePath = path, completed, durableFinalVerified = true });
    } catch (Exception e) { failure = (failure ?? "") + " finish: " + e.Message; completed = false; }
    try {
        var own = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
        if (own.Owner == Owner && own.RunId == runId && own.State != "Idle") {
            durable(runRoot + "/cancel-intent.json", new { utc = DateTime.UtcNow.ToString("o"), runId, owner = Owner, cell = Cell, reason });
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
        if (now - installed >= CapSeconds) { finish("cap-600s", false); return; }
        if ((state == "waiting-live" || state == "waiting-natural-proof") && now - phaseSince >= NoLiveSeconds) {
            finish("no-live-or-natural-proof-120s", false); return;
        }
        if (runtimeErrors != 0) { finish("runtime-error", false); return; }
        if (firstFinishError != null) { finish("first-finish-latch-error: " + firstFinishError, false); return; }
        if (!EditorApplication.isPlaying || !Mirror.NetworkServer.active) { finish("play-or-server-ended", false); return; }
        if (now - lastTick < .25) return;
        lastTick = now;
        var own = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
        if (own.Owner != Owner || own.RunId != runId || !own.Ready) { finish("launch-owner-run-or-ready-lost", false); return; }
        if (VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.Active != probe) { finish("probe-lost", false); return; }
        var liveDirector = VrBattlegrounds.Bots.BotDirector.Instance;
        if (liveDirector == null || liveDirector.Bots.Count != expectedBots ||
            initialBots.Any(s => s == null || !liveDirector.Bots.Contains(s)) ||
            initialBots.Where((s, i) => s.netId != botSessionIds[i] || s.GetInstanceID() != botSessionInstances[i]).Any()) {
            finish("bot-session-loss", false); return;
        }
        var referee = VrBattlegrounds.Managers.MapReferee.Instance;
        var mode = referee != null ? referee.ActiveGameMode : null;
        var elimination = mode as VrBattlegrounds.GameModes.EliminationMode;
        bool live = referee != null && mode != null && referee.CurrentState == VrBattlegrounds.Managers.MapState.Live && elimination != null && !mode.IsWarmup;
        if (referee != null && referee.CurrentState == VrBattlegrounds.Managers.MapState.Paused) { finish("unexpected-pause", false); return; }
        if (state == "waiting-live" && live) {
            refereeId = referee.netId; firstModeId = mode.netId;
            firstReferee = referee; firstMode = elimination; firstReferee.Finished += onFirstFinished;
            state = "live-match"; phaseSince = now; write("Матч перешёл в Live.");
        }
        if (now - lastProbeRead >= 5) { lastProbeRead = now; readProbe(); }
        if (state == "live-match") {
            if (!live || referee != firstReferee || mode != firstMode) {
                if (!firstFinishObserved) { finish("mode-ended-without-exact-natural-end", false); return; }
                completionKind = "naturalEnd"; state = "waiting-natural-proof"; phaseSince = firstFinishAt;
                lastProbeRead = now; readProbe();
                write("Exact naturalEnd наблюдён; проверяем durable timeline.");
            } else {
                maxRound = Math.Max(maxRound, elimination.CurrentRoundNumber);
            }
            if (state == "live-match" && !firstFinishObserved && maxRound >= 5 && fourRoundsScored) {
                if (!VrBattlegrounds.Network.StateEventAuthority.IsWorldAuthority) { finish("authority-lost-before-stop", false); return; }
                completionKind = "adminStop"; stopIntent = true;
                int stopFrame = Time.frameCount;
                durable(runRoot + "/admin-stop-intent.json", new { utc = DateTime.UtcNow.ToString("o"), runId, cell = Cell, botSessionIds, maxRound, scoredRounds, frame = stopFrame });
                write("Сыграно 4 полных раунда (roundScored 1..4), начался Round 5: штатный MapReferee.Stop.");
                referee.Stop();
                durable(runRoot + "/admin-stop-return.json", new { utc = DateTime.UtcNow.ToString("o"), returned = true });
                finish("normal-4rounds-adminStop", true);
                return;
            }
        }
        if (state == "waiting-natural-proof" && naturalEndProof) {
            finish("normal-naturalEnd", true);
            return;
        }
        string signature = state + "/" + (elimination != null ? elimination.CurrentRoundNumber + "/" + elimination.CurrentRoundPhase : "no-elimination");
        if (signature != lastSignature) { lastSignature = signature; write("Переход " + signature); }
        if (now - lastWrite >= 5) { lastWrite = now; write(null); }
    } catch (Exception error) { finish("watch-error: " + error.Message, false); }
};
control["finish"] = finish; control["runId"] = runId; control["owner"] = Owner; control["terminal"] = false;
durable(runRoot + "/watch-install.json", new { utc = DateTime.UtcNow.ToString("o"), runId, owner = Owner, cell = Cell, expectedBots, botSessionIds, botSessionInstances, probePath, capSeconds = CapSeconds });
write("Watcher установлен для ячейки " + Cell + " (" + expectedBots + " ботов); доп. трассировщики выключены.");
AppDomain.CurrentDomain.SetData(marker, control);
Application.logMessageReceivedThreaded += onLog;
EditorApplication.quitting += quitting;
EditorApplication.update += tick;
return new { executionCompleted = true, installed = true, cell = Cell, runId, expectedBots, botSessionIds, probePath, capSeconds = CapSeconds,
    next = "B5 read-only; после stopped/Idle — B6 cleanup заявки и копирование архивов логов до finish/receive." };
