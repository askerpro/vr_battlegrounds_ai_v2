// B3 MUTATION: заявка клиентского наблюдателя + штатный многопроцессный запуск PlayLaunch (свой owner) на сцене стенда ботов.
// S2C: главный редактор = выделенный сервер, клоны "Player 2"/"Player 3" = клиенты-игроки (GameRole=Player). attempt-2:
// зрители не получают сессию (GameNetworkManager.OnSpectatorConnect не реализован, тикет 515). Целью бота клиенты не
// становятся штатно: в Respawn команду выбирает игрок (никто не назначает), разминка без команд — TeamIndex=0, а
// BotCombatDriver.IsEnemy/BotSenses.NearestEnemy игрока без команды врагом не считают (B4 проверяет это перед каждым case). H1C: главный = ПК-хост, "Player 2" = клиент. Стенд ботов исполняется в главном редакторе
// (RunCases требует NetworkServer.active), поэтому H1C запускается как Role=host + 1 клиент, а не Role=client + хост-клон.
// BotCombatStandEditor (зона launch-infra) не используется: он жёстко задаёт ClientCount=0; GoLive и очередь — B4.
// НЕ ПОВТОРЯТЬ после таймаута/обрыва MCP: сначала B5 (PlayLaunch.State, participants, launch.json).
const string Root = "tasks/bots-fix/reports/net-round1";
const string Topology = "S2C";   // S2C | H1C
const string Phase = "p1";       // p1 | p2 (p2 только S2C: поздний вход и reconnect)
const string Owner = "bots-fix.net-round1";
const string Attempt = "attempt-6"; // новая папка попытки: прежние runtime/<topology>-<phase> не мешают B3
const string Variant = "A1";     // attempt-6 A/B: A1 — ClientNoRecoil=false, A2 — ClientNoRecoil=true; "" — без варианта
const string ScenePath = "Assets/Scenes/Debug/BotCombatStand.unity";
string RunRoot = Root + "/runtime/" + Attempt + "/" + Topology + "-" + Phase + (Variant.Length > 0 ? "-" + Variant : "");
bool ClientNoRecoil = Variant == "A2";
if (Variant != "" && Variant != "A1" && Variant != "A2") throw new InvalidOperationException("Variant — A1 | A2 | \"\".");
if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
    throw new InvalidOperationException("Нужен idle EditMode.");
if (Topology == "H1C" && Phase != "p1") throw new InvalidOperationException("H1C — только фаза p1 (один клиент: поздний вход/reconnect некому наблюдать параллельно).");
if (System.IO.File.Exists(RunRoot + "/launch.json"))
    return new { executionCompleted = true, refused = true, reason = "launch.json уже есть: запуск этой фазы уже был, не повторять." };
var preflights = System.IO.Directory.Exists(Root + "/" + Attempt) ? System.IO.Directory.GetFiles(Root + "/" + Attempt, "preflight-*.json") : new string[0];
if (!preflights.Any(p => (bool?)Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(p))["passed"] == true))
    return new { executionCompleted = true, refused = true, reason = "Нет PASS preflight B0 этой попытки (" + Attempt + ")." };
var configuration = new VrBattlegrounds.DevTools.PlayLaunchConfiguration {
    Enabled = true, Role = Topology == "S2C" ? "server" : "host", ClientCount = Topology == "S2C" ? 2 : 1,
    SceneSource = "scene", ScenePath = ScenePath, ModeId = "respawn", AutoGoLive = false, BotCount = 0,
    PauseOnFocusLoss = false, HostIsAdmin = false, Readiness = "map-playable", GameRole = "Player",
    NetworkPortPolicy = "auto", TimeoutSeconds = 180 };
string json = JsonUtility.ToJson(configuration);
var plan = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.ResolvedPlayPlan>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Plan(json));
if (!plan.Passed) return new { executionCompleted = true, refused = true, reason = "Plan: " + plan.Code + " " + plan.Message };
// Заявка наблюдателя пишется ДО Play: клоны читают её при входе в Play (RuntimeInitializeOnLoadMethod).
string clients = System.IO.Path.GetFullPath(RunRoot + "/clients");
System.IO.Directory.CreateDirectory(clients);
string requestPath = VrBattlegrounds.DevTools.BotCombatStand.BotStandNetObserverGate.WriteRequest(new VrBattlegrounds.DevTools.BotCombatStand.BotStandNetObserverRequest {
    RunTag = "net-round1/" + Attempt + "/" + Topology + "-" + Phase + (Variant.Length > 0 ? "-" + Variant : ""), OutputDirectory = clients,
    ExpiresUtc = DateTime.UtcNow.AddMinutes(30).ToString("o"), ClientNoRecoil = ClientNoRecoil });
// launch.json до Play: durable отметка попытки, даже если ответ MCP потеряется.
System.IO.File.WriteAllText(RunRoot + "/launch.json", Newtonsoft.Json.JsonConvert.SerializeObject(new {
    requestedUtc = DateTime.UtcNow.ToString("o"), attempt = Attempt, topology = Topology, phase = Phase, variant = Variant, clientNoRecoil = ClientNoRecoil, owner = Owner, configuration, planHash = plan.PlanHash,
    observerRequest = requestPath, clientsDirectory = clients }, Newtonsoft.Json.Formatting.Indented));
var launch = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(
    VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Play(json, Owner, plan.PlanHash));
System.IO.File.WriteAllText(RunRoot + "/launch-status.json", JsonUtility.ToJson(launch, true));
return new { executionCompleted = true, started = true, runId = launch.RunId, state = launch.State, error = launch.Error, topology = Topology, phase = Phase, variant = Variant, clientNoRecoil = ClientNoRecoil,
    next = "Ждать B5: launch.ready=true (все участники Connected, карта допущена); затем B4 той же фазы." };
