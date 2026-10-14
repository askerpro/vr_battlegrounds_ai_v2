// B3 MUTATION: заявка пробы матча (BotStandMatchProbe) + штатный многопроцессный запуск PlayLaunch (свой owner) на TestMap1,
// режим Elimination, BotCount=N, AutoGoLive=true, MinPlayers = люди + N: DebugOrchestrator добавляет ботов при загрузке
// карты сервером (BotDirector.EnsureCount, без предела MaxTeamSize — он только у BotMatchStarter), «Начать матч» —
// TryGoLive при подключении последнего участника (людей и ботов). Команды людям не раздаются: Elimination
// teamAssignment=PlayerChoice, людей без команды режима нет в составах (B5/проба проверяют inModeTeam=false).
// S8 — клиенты GameRole=Spectator (ТЗ: вне команд и составов). Сервер Spectator пока не принимает
// (GameNetworkManager.OnSpectatorConnect не реализован, тикет 515: нет сессии, MapPlayable=false) — готовность "connected";
// если наблюдатель не получает тел ботов — S8P: клиенты GameRole=Player без команды режима (PlayerChoice, TeamIndex=0).
// M* — ПК-хост (main = хост с аватаром человека без команды), S8/S8P — main = выделенный сервер, "Player 2"/"Player 3" — клиенты.
// НЕ ПОВТОРЯТЬ после таймаута/обрыва MCP: сначала B5 (PlayLaunch.State, participants, launch.json).
const string Root = "tasks/bots-fix/reports/cc0-match";
const string Run = "M1";          // M1 | M2 | M3 | M8 | M10 | S8 (клиенты Spectator) | S8P (клиенты Player)
const string Owner = "bots-fix.cc0-match";
const string Attempt = "attempt-1";
const string ScenePath = "Assets/Scenes/Maps/TestMap1.unity";
string RunRoot = Root + "/runtime/" + Attempt + "/" + Run;
if (Run != "M1" && Run != "M2" && Run != "M3" && Run != "M8" && Run != "M10" && Run != "S8" && Run != "S8P") throw new InvalidOperationException("Run: M1|M2|M3|M8|M10|S8|S8P");
string role = Run.StartsWith("S") ? "server" : "host";
int clients = Run.StartsWith("S") ? 2 : 0;
int bots = Run == "M1" ? 1 : Run == "M2" ? 2 : Run == "M3" ? 3 : Run == "M10" ? 10 : 8;
bool spectators = Run == "S8";      // клиенты-наблюдатели вне команд (ТЗ); сессии у Spectator нет — готовность "connected"
int humans = (role == "host" ? 1 : 0) + (spectators ? 0 : clients);
if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
    throw new InvalidOperationException("Нужен idle EditMode.");
if (System.IO.File.Exists(RunRoot + "/launch.json"))
    return new { executionCompleted = true, refused = true, reason = "launch.json уже есть: запуск этого прогона уже был, не повторять." };
var preflights = System.IO.Directory.Exists(Root + "/" + Attempt) ? System.IO.Directory.GetFiles(Root + "/" + Attempt, "preflight-*.json") : new string[0];
if (!preflights.Any(p => (bool?)Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(p))["passed"] == true))
    return new { executionCompleted = true, refused = true, reason = "Нет PASS preflight B0 этой попытки (" + Attempt + ")." };
VrBattlegrounds.DevTools.BotCombatStand.BotStandNetObserverRequest observerRequest;
string observerReason;
if (VrBattlegrounds.DevTools.BotCombatStand.BotStandNetObserverGate.TryReadRequest(out observerRequest, out observerReason))
    return new { executionCompleted = true, refused = true, reason = "Действующая заявка BotStandNetObserver: " + observerRequest.RunTag };
var configuration = new VrBattlegrounds.DevTools.PlayLaunchConfiguration {
    Enabled = true, Role = role, ClientCount = clients, SceneSource = "scene", ScenePath = ScenePath,
    ModeId = "elimination", AutoGoLive = true, MinPlayers = humans + bots, BotCount = bots,
    PauseOnFocusLoss = false, HostIsAdmin = false, Readiness = spectators ? "connected" : "map-playable",
    GameRole = spectators ? "Spectator" : "Player", NetworkPortPolicy = "auto", TimeoutSeconds = 180 };
string json = JsonUtility.ToJson(configuration);
var plan = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.ResolvedPlayPlan>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Plan(json));
if (!plan.Passed) return new { executionCompleted = true, refused = true, reason = "Plan: " + plan.Code + " " + plan.Message };
// Заявка пробы пишется ДО Play: все процессы читают её при входе в Play (RuntimeInitializeOnLoadMethod).
string reports = System.IO.Path.GetFullPath(RunRoot + "/probe");
System.IO.Directory.CreateDirectory(reports);
string requestPath = VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.WriteRequest(new VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeRequest {
    RunTag = "cc0-match/" + Attempt + "/" + Run, OutputDirectory = reports, ExpiresUtc = DateTime.UtcNow.AddMinutes(25).ToString("o"), WriteSeconds = 10f });
System.IO.File.WriteAllText(RunRoot + "/launch.json", Newtonsoft.Json.JsonConvert.SerializeObject(new {
    requestedUtc = DateTime.UtcNow.ToString("o"), attempt = Attempt, run = Run, role, clients, spectators, bots, humans, owner = Owner, configuration,
    planHash = plan.PlanHash, probeRequest = requestPath, reportsDirectory = reports }, Newtonsoft.Json.Formatting.Indented));
var launch = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(
    VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Play(json, Owner, plan.PlanHash));
System.IO.File.WriteAllText(RunRoot + "/launch-status.json", JsonUtility.ToJson(launch, true));
return new { executionCompleted = true, started = true, run = Run, runId = launch.RunId, state = launch.State, error = launch.Error,
    next = "B5 до launch.ready=true; затем B4 того же прогона (сторож конца матча)." };
