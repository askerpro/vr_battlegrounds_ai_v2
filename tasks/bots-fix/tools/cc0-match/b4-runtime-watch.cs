// B4 MUTATION (Play после B3, launch Ready): сторож конца прогона в главном редакторе (EditorApplication.update).
// Матч стартует сам (AutoGoLive, MinPlayers = люди + N). Сторож:
//  - запасной «Начать матч»: карта в разминке FallbackGoLiveSeconds после B4 и все N ботов на месте — MapReferee.GoLive()
//    один раз (запись в watch.json; штатный путь админа «Начать матч»);
//  - стоп: конец карты (был Live, стал не Live, + EndGraceSeconds), или начался раунд StopRound (сыграно StopRound-1),
//    или Live дольше CapSeconds, или матч не начался за NoLiveSeconds. Стоп = BotStandMatchProbeGate.FinishActive (финальный
//    отчёт пробы сервера) и PlayLaunch.Cancel(runId, свой owner) — клоны пишут финальные отчёты при выходе.
// Сторож только читает состояние матча; поведения ботов/режима не меняет (кроме запасного GoLive).
// НЕ ПОВТОРЯТЬ после таймаута: сначала B5 (watch.json, AppDomain marker).
const string Root = "tasks/bots-fix/reports/cc0-match";
const string Run = "M1";          // M1 | M2 | M3 | M8 | M10 | S8 (клиенты Spectator) | S8P (клиенты Player)
const string Owner = "bots-fix.cc0-match";
const string Attempt = "attempt-1";
const int StopRound = 5;          // стоп в начале 5-го раунда: 4 раунда сыграны
const double CapSeconds = 480;    // предел Live
const double EndGraceSeconds = 8;
const double FallbackGoLiveSeconds = 45;
const double NoLiveSeconds = 120;
string RunRoot = Root + "/runtime/" + Attempt + "/" + Run;
int bots = Run == "M1" ? 1 : Run == "M2" ? 2 : Run == "M3" ? 3 : Run == "M10" ? 10 : 8;
string Marker = "bots-fix.cc0-match.watch." + Attempt + "." + Run;
if (AppDomain.CurrentDomain.GetData(Marker) != null) return new { executionCompleted = true, refused = true, reason = "Сторож уже подключён." };
if (System.IO.File.Exists(RunRoot + "/watch.json")) return new { executionCompleted = true, refused = true, reason = "watch.json уже существует." };
if (!EditorApplication.isPlaying || !Mirror.NetworkServer.active) throw new InvalidOperationException("Нужен Play с сервером в главном редакторе.");
var launchNow = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
if (!launchNow.Ready) return new { executionCompleted = true, refused = true, reason = "PlayLaunch не Ready: " + launchNow.State + " " + launchNow.Error };
if (launchNow.Owner != Owner) return new { executionCompleted = true, refused = true, reason = "Запуск принадлежит " + launchNow.Owner };
if (VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.Active == null)
    return new { executionCompleted = true, refused = true, reason = "Проба матча не включена в главном процессе (нет заявки при входе в Play?)." };
string runId = launchNow.RunId;
double installed = EditorApplication.timeSinceStartup, liveSince = -1, endSince = -1, lastWrite = 0;
bool wasLive = false, goLiveCalled = false, stopped = false;
string state = "watching", stopReason = null, goLiveResult = null;
int maxRound = -1;
var transitions = new List<string>();
Action<string> write = note => { try {
    if (note != null) transitions.Add(DateTime.UtcNow.ToString("o") + " " + note);
    var referee = VrBattlegrounds.Managers.MapReferee.Instance;
    var mode = referee != null ? referee.ActiveGameMode as VrBattlegrounds.GameModes.EliminationMode : null;
    System.IO.File.WriteAllText(RunRoot + "/watch.json", Newtonsoft.Json.JsonConvert.SerializeObject(new {
        state, stopReason, run = Run, runId, goLiveCalled, goLiveResult, maxRound,
        liveSeconds = liveSince < 0 ? 0 : Math.Round(EditorApplication.timeSinceStartup - liveSince, 1),
        mapState = referee != null ? referee.CurrentState.ToString() : null,
        modeState = mode != null ? mode.CurrentState.ToString() : null,
        round = mode != null ? mode.CurrentRoundNumber : -1, phase = mode != null ? mode.CurrentRoundPhase.ToString() : null,
        bots = VrBattlegrounds.Bots.BotDirector.Instance != null ? VrBattlegrounds.Bots.BotDirector.Instance.Bots.Count : 0,
        transitions, updatedUtc = DateTime.UtcNow.ToString("o") }, Newtonsoft.Json.Formatting.Indented)); } catch { } };
EditorApplication.CallbackFunction tick = null;
Action<string> stop = reason => {
    if (stopped) return;
    stopped = true;
    stopReason = reason;
    string probeReport = null, cancelState = null, error = null;
    try { probeReport = VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.FinishActive("auto-" + reason); } catch (Exception e) { error = "finish: " + e.Message; }
    try {
        var after = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Cancel(runId, Owner));
        cancelState = after.State;
    } catch (Exception e) { error = (error == null ? "" : error + "; ") + "cancel: " + e.Message; }
    state = error == null ? "stopped" : "stop-error";
    write("стоп: " + reason + "; probe=" + probeReport + "; cancel=" + cancelState + (error != null ? "; " + error : ""));
    EditorApplication.update -= tick;
    AppDomain.CurrentDomain.SetData(Marker, null);
};
tick = () => {
    try {
        if (stopped) return;
        if (!EditorApplication.isPlaying) {
            state = "play-ended";
            write("Play завершён до стопа сторожа");
            EditorApplication.update -= tick;
            AppDomain.CurrentDomain.SetData(Marker, null);
            return;
        }
        double now = EditorApplication.timeSinceStartup;
        var referee = VrBattlegrounds.Managers.MapReferee.Instance;
        bool live = referee != null && referee.IsLiveOrPaused;
        var mode = referee != null ? referee.ActiveGameMode as VrBattlegrounds.GameModes.EliminationMode : null;
        if (live && !wasLive) { wasLive = true; if (liveSince < 0) liveSince = now; write("Live"); }
        if (mode != null && mode.CurrentRoundNumber > maxRound) { maxRound = mode.CurrentRoundNumber; write("раунд " + maxRound); }
        if (wasLive && !live && endSince < 0) { endSince = now; write("карта вышла из Live (конец матча?)"); }
        if (endSince >= 0 && now - endSince >= EndGraceSeconds) { stop("match-end"); return; }
        if (live && mode != null && mode.CurrentRoundNumber >= StopRound) { stop("round-" + StopRound); return; }
        if (liveSince >= 0 && now - liveSince >= CapSeconds) { stop("cap"); return; }
        if (!wasLive && !goLiveCalled && now - installed >= FallbackGoLiveSeconds && referee != null &&
            VrBattlegrounds.Bots.BotDirector.Instance != null && VrBattlegrounds.Bots.BotDirector.Instance.Bots.Count >= bots) {
            goLiveCalled = true;
            goLiveResult = referee.GoLive().ToString();
            write("запасной GoLive: " + goLiveResult);
        }
        if (!wasLive && now - installed >= NoLiveSeconds) { stop("no-live"); return; }
        if (now - lastWrite >= 5) { lastWrite = now; write(null); }
    } catch (Exception e) { state = "tick-error"; write("tick: " + e.Message); }
};
System.IO.Directory.CreateDirectory(RunRoot);
AppDomain.CurrentDomain.SetData(Marker, "installed");
write("сторож подключён");
EditorApplication.update += tick;
return new { executionCompleted = true, installed = true, run = Run, runId, stopRound = StopRound, capSeconds = CapSeconds,
    next = "B5 раз в 1–2 мин (renew аренды); после watch.state=stopped и launch Idle — B6 (уборка заявки)." };
