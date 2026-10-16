// B7: только пассивные публичные getter-ы, 1 с / две сессии. C# 6. Не повторять установку.
// Никаких Prepare/Fire/Arm/Offer/Grab, private reflection, Cancel или FinishActive.
const string Root = "tasks/bots-fix/reports/cc2-lifecycle-round2";
const string Attempt = "attempt-1";
const string Run = "M2";
const string Owner = "bots-fix.cc2-lifecycle";
const int RowCapacity = 2048;
const double CapSeconds = 720;
string runRoot = Root + "/runtime/" + Attempt + "/" + Run;
string path = runRoot + "/combat-observation.json";
string marker = Owner + ".combat." + Attempt + "." + Run;
string watchMarker = Owner + ".watch." + Attempt + "." + Run;
if (AppDomain.CurrentDomain.GetData(marker) != null || System.IO.File.Exists(path) || System.IO.File.Exists(path + ".tmp"))
    return new { executionCompleted = true, refused = true, reason = "B7 уже устанавливался; читать существующий отчёт, не повторять." };
if (!EditorApplication.isPlaying || !Mirror.NetworkServer.active || Mirror.NetworkClient.active)
    throw new InvalidOperationException("Нужен собственный ServerOnly Play после B4.");
var launch = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
var evidence = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(runRoot + "/launch-status.json"));
string runId = (string)evidence["RunId"];
if (!launch.Ready || launch.Owner != Owner || string.IsNullOrEmpty(runId) || launch.RunId != runId)
    throw new InvalidOperationException("Нужен точный собственный Ready RunId.");
var watchControl = AppDomain.CurrentDomain.GetData(watchMarker) as Dictionary<string, object>;
if (watchControl == null || (string)watchControl["owner"] != Owner || (string)watchControl["runId"] != runId || (bool)watchControl["terminal"])
    throw new InvalidOperationException("Нужен собственный активный B4 control.");
var watchInstall = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(runRoot + "/watch.json"));
if ((string)watchInstall["owner"] != Owner || (string)watchInstall["runId"] != runId)
    throw new InvalidOperationException("watch.json другой сессии запуска.");
var probe = VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.Active;
if (probe == null) throw new InvalidOperationException("Нет собственной active probe.");
string probePath = System.IO.Path.GetFullPath(probe.ReportPath);
string expectedProbeRoot = System.IO.Path.GetFullPath(runRoot + "/probe") + System.IO.Path.DirectorySeparatorChar;
if (!probePath.StartsWith(expectedProbeRoot, StringComparison.OrdinalIgnoreCase) ||
    probePath != System.IO.Path.GetFullPath((string)watchInstall["probePath"]))
    throw new InvalidOperationException("Probe path не совпадает с собственным B4.");
string requestPath = VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.RequestPath;
string immutable = System.IO.File.ReadAllText(runRoot + "/probe-request-immutable.json");
if (System.IO.File.ReadAllText(requestPath) != immutable) throw new InvalidOperationException("Immutable probe request изменён.");
var director = VrBattlegrounds.Bots.BotDirector.Instance;
var players = VrBattlegrounds.Managers.PlayersManager.Instance;
if (director == null || director.Bots.Count != 2 || players == null || players.Sessions.Count != 2 ||
    players.Sessions.Any(s => s == null || !director.Bots.Contains(s))) throw new InvalidOperationException("Нужны две собственные bot-сессии.");
var subjects = director.Bots.OrderBy(s => s.netId).ToArray();
uint[] sessionIds = subjects.Select(s => s.netId).ToArray();
int[] sessionInstances = subjects.Select(s => s.GetInstanceID()).ToArray();
var watchSessions = (watchInstall["sessionIds"] as Newtonsoft.Json.Linq.JArray);
if (watchSessions == null || !watchSessions.Select(v => (uint)v).OrderBy(v => v).SequenceEqual(sessionIds))
    throw new InvalidOperationException("Сессии не совпадают с B4.");
double installed = EditorApplication.timeSinceStartup, lastTick = -1, lastWrite = -1;
string installedUtc = DateTime.UtcNow.ToString("o"), endedUtc = null, stopReason = null, lastCloseRequest = null;
bool finalized = false, hardIncomplete = false;
int errors = 0, dropped = 0, lastCapturedFrame = -1;
float lastCapturedTime = 0, lastCapturedRealtime = 0;
var rows = new List<object>();
var errorSamples = new List<string>();
var control = new Dictionary<string, object>();
Action<string> error = message => {
    errors++;
    if (errorSamples.Count < 8) errorSamples.Add(message.Length <= 500 ? message : message.Substring(0, 500));
    control["errors"] = errors;
};
Func<string, Func<object>, object> read = (name, getter) => {
    try { return getter(); }
    catch (Exception e) { error(name + ": " + e.GetType().Name + ": " + e.Message); return "UNKNOWN/getter-error"; }
};
Func<UltimateXR.Mechanics.Weapons.UxrFirearmWeapon, object> inspectFirearm = firearm => {
    if (firearm == null) return null;
    var result = new Dictionary<string, object>();
    var identity = firearm.GetComponentInParent<Mirror.NetworkIdentity>();
    var weapon = firearm.GetComponent<VrBattlegrounds.Arsenal.WeaponComponent>();
    var host = firearm.GetComponent<VrBattlegrounds.Weapons.WeaponSystem>();
    result["instanceId"] = firearm.GetInstanceID(); result["netId"] = identity != null ? identity.netId : 0;
    result["name"] = firearm.name; result["weaponInfo"] = weapon != null && weapon.WeaponData != null ? weapon.WeaponData.name : null;
    result["active"] = firearm.gameObject.activeInHierarchy; result["enabled"] = firearm.enabled;
    result["owner"] = read("firearm.Owner", () => {
        var actor = firearm.Owner;
        var ownerBody = actor != null ? actor.GetComponentInParent<VrBattlegrounds.Player.PlayerController>() : null;
        return new { actorId = actor != null ? actor.GetInstanceID() : 0, bodyId = ownerBody != null ? ownerBody.GetInstanceID() : 0,
            sessionNetId = ownerBody != null && ownerBody.Session != null ? ownerBody.Session.netId : 0 };
    });
    result["canUse"] = read("firearm.CanUse", () => firearm.CanUse);
    result["isUseBlocked"] = read("firearm.IsUseBlocked", () => firearm.IsUseBlocked);
    result["host"] = host == null ? (object)"ABSENT/WeaponSystem" : read("WeaponSystem getters", () => new {
        configured = host.IsConfigured, triggerIndex = host.TriggerIndex,
        ammoCapability = host.Profile != null ? host.Profile.AmmoCapability.ToString() : null,
        chamberPolicy = host.Profile != null ? host.Profile.ChamberPolicy.ToString() : null,
        lastRowId = host.LastRowId, commandsRejected = host.CommandsRejected, machineState = host.MachineState.ToString()
    });
    // BotGunner стреляет через trigger0. Только query-методы SDK, без QueryReadinessDecision/policy/preparation.
    result["trigger0"] = read("SDK trigger0 getters", () => {
        if (firearm.TriggerCount <= 0) return (object)"UNKNOWN/no-trigger0";
        bool ledger = firearm.UsesReadinessLedger(0);
        var state = ledger ? firearm.GetReadinessState(0) : null; // SDK возвращает копию, живой учёт не меняется.
        object snapshot = state == null ? (object)null : new {
            initialized = state.ReadinessInitialized, chamberRound = state.ChamberRound, actionOpen = state.ActionOpen,
            cyclePendingRaw = state.ChamberCyclePending, cycleSequence = state.CycleSequence,
            revision = state.Revision, shotSequence = state.ShotSequence, postShotEmptyAction = state.PostShotEmptyAction
        };
        int ammo = firearm.GetAmmoLeft(0);
        return new {
            ledger, mechanicalReadyToFire = firearm.IsReadyToFire(0), loaded = firearm.IsLoaded(0),
            hasMagazine = firearm.HasMagAttached(0), ammoLeftSdk = ammo, ammoCapacitySdk = firearm.GetAmmoCapacity(0),
            sdkUnlimitedSentinel = ammo == int.MaxValue, totalAmmoSdk = firearm.GetTotalAmmoLeft(0),
            effectiveCyclePending = ledger ? (bool?)firearm.IsReadinessCyclePending(0) : null,
            snapshot, scope = "SDK query, trigger0; mechanicalReady не включает общий CanUse; int.MaxValue — SDK sentinel, не физический запас"
        };
    });
    return result;
};
Action<bool> write = final => {
    bool ownWatchTerminal = false, ownWatchCompleted = false;
    string watchState = null, watchReason = null;
    try {
        var watch = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(runRoot + "/watch.json"));
        if ((string)watch["owner"] == Owner && (string)watch["runId"] == runId) {
            watchState = (string)watch["state"]; watchReason = (string)watch["finishReason"];
            ownWatchTerminal = watchState == "finishing" || watchState == "stopped" || watchState == "stop-error";
            ownWatchCompleted = watchState == "stopped" && (bool?)watch["completed"] == true;
        }
    } catch (Exception e) { error("watch close evidence: " + e.Message); }
    string json = Newtonsoft.Json.JsonConvert.SerializeObject(new {
        schema = "cc2-combat-observation@1", owner = Owner, runId, attempt = Attempt, run = Run, probePath,
        installedUtc, endedUtc, updatedUtc = DateTime.UtcNow.ToString("o"), final, stopReason, lastCloseRequest,
        completedObservation = final && ownWatchCompleted && !hardIncomplete && errors == 0 && dropped == 0,
        incomplete = !final || !ownWatchCompleted || hardIncomplete || errors != 0 || dropped != 0,
        watchState, watchReason, ownWatchTerminal, ownWatchCompleted,
        rows, rowCapacity = RowCapacity, sampleIntervalSeconds = 1, capSeconds = CapSeconds,
        dropped, errors, errorSamples, sessionIds, sessionInstances, lastCapturedFrame, lastCapturedTime, lastCapturedRealtime,
        elapsed = Math.Round(EditorApplication.timeSinceStartup - installed, 3),
        unknownScope = new {
            mind = "UNKNOWN: публичного lookup текущего BotMind нет; сопоставлять по session/epoch с уже существующим probe journal",
            aiRequestCount = "UNKNOWN: публичного cumulative счётчика нет; ShotRequested — лишь мгновенный bool, false не означает ноль запросов",
            shotOutcome = "OutcomeCount — публичные cumulative исходы текущего тела; exact последнее событие и порядок между выборками UNKNOWN. В751 эти счётчики ещё не снимались",
            sampling = "1 с, переходные запросы/отказы могут пройти между выборками; отсутствие сигнала не доказывает отсутствие действия",
            readiness = "Только существующие публичные getter-ы; никакой подготовки. SDK ledger shotSequence — consumption, не доказательство эмиссии",
            verdict = "Пассивный diagnostic; completedObservation относится к закрытию окна, не gameplay PASS или Quest/perf приёмке"
        }
    }, Newtonsoft.Json.Formatting.Indented).Replace("\r\n", "\n");
    byte[] bytes = new System.Text.UTF8Encoding(false).GetBytes(json);
    string temp = path + ".tmp";
    using (var stream = new System.IO.FileStream(temp, System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.Read)) {
        stream.Write(bytes, 0, bytes.Length); stream.Flush(true);
    }
    if (System.IO.File.Exists(path)) System.IO.File.Replace(temp, path, null); else System.IO.File.Move(temp, path);
};
EditorApplication.CallbackFunction tick = null;
Action<PlayModeStateChange> onPlay = null;
Action onQuit = null;
Action<string> finish = reason => {
    lastCloseRequest = reason;
    if (!finalized) {
        finalized = true; stopReason = reason; endedUtc = DateTime.UtcNow.ToString("o");
        EditorApplication.update -= tick; EditorApplication.playModeStateChanged -= onPlay; EditorApplication.quitting -= onQuit;
        control["terminal"] = true;
    }
    // B6 может ещё раз записать финал с последним durable watch outcome; новых gameplay/SDK чтений здесь нет.
    try { write(true); control["lastWriteError"] = null; }
    catch (Exception e) { error("final write: " + e.Message); control["lastWriteError"] = e.Message; }
};
onPlay = change => { if (change == PlayModeStateChange.ExitingPlayMode || change == PlayModeStateChange.EnteredEditMode) finish("play-exit"); };
onQuit = () => finish("editor-quit");
tick = () => {
    if (finalized) return;
    try {
        // B4 удаляет marker в том же callback, где начинает Cancel. Сначала проверяем захваченный own control.
        if ((bool)watchControl["terminal"]) { finish("own-b4-terminal"); return; }
        double now = EditorApplication.timeSinceStartup;
        if (now - lastTick < 1) return;
        lastTick = now;
        var watch = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(runRoot + "/watch.json"));
        bool ownWatch = (string)watch["owner"] == Owner && (string)watch["runId"] == runId;
        string ws = (string)watch["state"];
        if (ownWatch && (ws == "finishing" || ws == "stopped" || ws == "stop-error")) { finish("own-durable-b4-terminal"); return; }
        if (now - installed >= CapSeconds) { hardIncomplete = true; finish("observer-cap-720s"); return; }
        if (!EditorApplication.isPlaying || !Mirror.NetworkServer.active || Mirror.NetworkClient.active) { finish("play-or-serveronly-ended"); return; }
        var own = JsonUtility.FromJson<VrBattlegrounds.EditorTools.TestStand.PlayLaunchStatus>(VrBattlegrounds.EditorTools.TestStand.PlayLaunch.Status());
        if (!ownWatch || own.Owner != Owner || own.RunId != runId || !own.Ready ||
            !ReferenceEquals(AppDomain.CurrentDomain.GetData(watchMarker), watchControl)) {
            hardIncomplete = true; finish("own-launch-or-watch-lost"); return;
        }
        if (VrBattlegrounds.DevTools.BotCombatStand.BotStandMatchProbeGate.Active != probe ||
            System.IO.Path.GetFullPath(probe.ReportPath) != probePath || !System.IO.File.Exists(requestPath) ||
            System.IO.File.ReadAllText(requestPath) != immutable) { hardIncomplete = true; finish("own-probe-or-immutable-request-lost"); return; }
        var liveDirector = VrBattlegrounds.Bots.BotDirector.Instance;
        var livePlayers = VrBattlegrounds.Managers.PlayersManager.Instance;
        if (liveDirector == null || livePlayers == null || liveDirector.Bots.Count != 2 || livePlayers.Sessions.Count != 2 ||
            subjects.Where((s, i) => s == null || !liveDirector.Bots.Contains(s) || !livePlayers.Sessions.Contains(s) ||
                s.netId != sessionIds[i] || s.GetInstanceID() != sessionInstances[i]).Any()) {
            hardIncomplete = true; finish("session-or-foreign-loss"); return;
        }
        var referee = VrBattlegrounds.Managers.MapReferee.Instance;
        var mode = referee != null ? referee.ActiveGameMode : null;
        var elimination = mode as VrBattlegrounds.GameModes.EliminationMode;
        var authority = VrBattlegrounds.Maps.Runtime.MapRunAuthority.Instance;
        var descriptor = authority != null ? authority.Current : default(VrBattlegrounds.Maps.Runtime.MapRunSnapshot);
        foreach (var subject in subjects) {
            if (rows.Count >= RowCapacity) { dropped++; hardIncomplete = true; finish("row-cap-2048"); return; }
            var body = subject.ActiveAvatar;
            var gunner = body != null ? body.GetComponent<VrBattlegrounds.Bots.BotGunner>() : null;
            var driver = body != null ? body.GetComponent<VrBattlegrounds.Bots.BotCombatDriver>() : null;
            var runtime = body != null ? body.GetComponent<VrBattlegrounds.Bots.BotRuntimeController>() : null;
            var selected = gunner != null ? gunner.SelectedFirearm : null;
            var held = gunner != null ? gunner.Firearm : null;
            var brain = driver != null ? driver.Brain : null;
            var target = gunner != null ? gunner.Target : null;
            var row = new Dictionary<string, object> {
                ["utc"] = DateTime.UtcNow.ToString("o"), ["editorTime"] = now, ["frame"] = Time.frameCount,
                ["time"] = Time.time, ["realtime"] = Time.realtimeSinceStartup,
                ["runKey"] = descriptor.Key.ToString(), ["modeEpoch"] = descriptor.ModeEpoch, ["descriptorReady"] = descriptor.IsReady,
                ["mode"] = descriptor.ActiveModeId, ["mapState"] = referee != null ? referee.CurrentState.ToString() : null,
                ["round"] = elimination != null ? elimination.CurrentRoundNumber : -1, ["phase"] = elimination != null ? elimination.CurrentRoundPhase.ToString() : null,
                ["sessionNetId"] = subject.netId, ["sessionInstanceId"] = subject.GetInstanceID(), ["session"] = subject.PlayerName,
                ["mind"] = "UNKNOWN/no-public-lookup", ["bodyNetId"] = body != null ? body.netId : 0,
                ["bodyId"] = body != null ? body.GetInstanceID() : 0, ["actorId"] = body != null && body._actor != null ? body._actor.GetInstanceID() : 0,
                ["alive"] = body != null && body.IsAlive, ["team"] = subject.TeamIndex,
                ["gunnerPresent"] = gunner != null, ["driverPresent"] = driver != null,
                ["armRequested"] = gunner != null ? (object)gunner.ArmRequested : null,
                ["hasWeapon"] = gunner != null ? (object)gunner.HasWeapon : null,
                ["holding"] = gunner != null ? (object)(held != null) : null,
                ["heldInstanceId"] = held != null ? held.GetInstanceID() : 0,
                ["seesTarget"] = gunner != null ? (object)gunner.SeesTarget : null,
                ["shotRequestedThisInstant"] = gunner != null ? (object)gunner.ShotRequested : null,
                ["aiRequestCount"] = "UNKNOWN/no-public-counter", ["shotsFired"] = gunner != null ? (object)gunner.ShotsFired : null,
                ["outcomeCountsPublic"] = runtime != null ? (object)Enum.GetValues(typeof(VrBattlegrounds.Bots.BotShotOutcome))
                    .Cast<VrBattlegrounds.Bots.BotShotOutcome>().ToDictionary(outcome => outcome.ToString(), outcome => runtime.OutcomeCount(outcome)) : null,
                ["targetBodyId"] = target != null ? target.GetInstanceID() : 0, ["targetBodyNetId"] = target != null ? target.netId : 0,
                ["targetSessionNetId"] = target != null && target.Session != null ? target.Session.netId : 0,
                ["targetAlive"] = target != null ? (object)target.IsAlive : null,
                ["targetRootDistanceM"] = body != null && target != null ? (object)Vector3.Distance(body.transform.position, target.transform.position) : null,
                ["combatActive"] = driver != null ? (object)driver.CombatActive : null,
                ["brainState"] = brain != null ? brain.state.ToString() : null,
                ["brainEnemyInstanceId"] = brain != null && brain.enemyToAttack != null ? brain.enemyToAttack.GetInstanceID() : 0,
                ["weaponSystemEnabled"] = UltimateXR.Mechanics.Weapons.UxrWeaponManager.HasInstance ? (object)UltimateXR.Mechanics.Weapons.UxrWeaponManager.Instance.WeaponSystemEnabled : null
            };
            row["selectedFirearm"] = read("selected firearm snapshot", () => inspectFirearm(selected));
            row["brainTarget"] = driver == null ? null : read("driver.Target", () => {
                var bt = driver.Target;
                return new { bodyId = bt != null ? bt.GetInstanceID() : 0, sessionNetId = bt != null && bt.Session != null ? bt.Session.netId : 0 };
            });
            row["lastAimGateErrorDeg"] = gunner != null && !float.IsNaN(gunner.LastGateAimErrorDeg) && !float.IsInfinity(gunner.LastGateAimErrorDeg) ? (object)gunner.LastGateAimErrorDeg : null;
            row["lastBlockerId"] = gunner != null && gunner.LastBlocker != null ? gunner.LastBlocker.GetInstanceID() : 0;
            rows.Add(row); lastCapturedFrame = Time.frameCount; lastCapturedTime = Time.time; lastCapturedRealtime = Time.realtimeSinceStartup;
        }
        if (now - lastWrite >= 5) { lastWrite = now; write(false); }
    } catch (Exception e) { error("observer tick: " + e.Message); hardIncomplete = true; finish("observer-error"); }
};
control["owner"] = Owner; control["runId"] = runId; control["probePath"] = probePath;
control["finish"] = finish; control["terminal"] = false; control["errors"] = 0;
write(false); // Отчёт-маркер до подписки; при ошибке записи observer не устанавливается.
AppDomain.CurrentDomain.SetData(marker, control);
EditorApplication.update += tick; EditorApplication.playModeStateChanged += onPlay; EditorApplication.quitting += onQuit;
return new { executionCompleted = true, installed = true, passive = true, owner = Owner, runId, probePath, path,
    rowCapacity = RowCapacity, sampleIntervalSeconds = 1, capSeconds = CapSeconds,
    next = "B6 может вызвать только exact own control.finish('b6-passive-close'); B7 не управляет матчем/запуском/оружием." };
