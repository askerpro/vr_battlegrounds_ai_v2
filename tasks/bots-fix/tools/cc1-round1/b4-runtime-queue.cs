// B4 MUTATION (Play стенда после warmup): очередь cc1-round1 подэтапа cc-1 в ТЕКУЩЕМ Live. Фаза подставляется в const Phase.
// p1 = E-ak105-ref 30, PE-ppk-ref 30, H-ppk-holster 30, U-unarmed 20, P-purchase-ak105 35 (145 с; T07, seed 101);
// p2 = G-ghost-respawn-ak105 40 (T07), AB-T01/AB-T06/AB-T08 (AK105, по 25 с) (115 с). Live Respawn 300 с вмещает фазу.
// Опции снаряжения исполняет стенд (BotStandEquipmentScript), замеры попаданий/снаряжения — BotStandPlayerFlowProbe
// (summary.json cases[].Metrics["playerFlow"]). Этот payload только наблюдает (чтение) и добавляет Metrics["cc1"]:
//  - kit: состав бот-компонентов каждого тела проверяемого бота (живое/призрак) и контрольной цели;
//  - target: высота камеры цели над корнем (ожидание 1,65 ± 0,02 м) и дрейф корня (T07 — цель стоит);
//  - proxy: Blaze-прокси по кадрам (UxrManager.AvatarsUpdated) — смены animManager.currentState, латентность от
//    Play("Move"/"Back"/"Left"/"Right") до смещения корня прокси > 2 см, рост normalizedTime текущего состояния слоя 0;
//  - log: предупреждения «doesn't exist»/«Empty animation», ошибки/исключения, строки [CameraFade] (дефект прогрева UXR).
// Трасса кадров прокси — <case>/cc1-trace.json. НЕ ПОВТОРЯТЬ после таймаута: сначала B5 (queue.json фазы, AppDomain marker).
const string Phase = "p1";
const string Root = "tasks/bots-fix/reports/cc1-round1/runtime";
string PhaseRoot = Root + "/" + Phase;
string Marker = "bots-fix.cc1-round1.queue." + Phase;
var T07 = VrBattlegrounds.DevTools.BotCombatStand.BotStandScenarioId.T07;
var all = new[] {
    new { id = "E-ak105-ref", phase = "p1", scenario = T07, weapon = "AK105_Weapon", rifle = true, seconds = 30f, zero = true, noRecoil = true, holster = false, unarmed = false, purchase = (string)null, purchaseAt = -1f, killAt = -1f, respawnAt = -1f },
    new { id = "PE-ppk-ref", phase = "p1", scenario = T07, weapon = "PPK_Weapon", rifle = false, seconds = 30f, zero = true, noRecoil = true, holster = false, unarmed = false, purchase = (string)null, purchaseAt = -1f, killAt = -1f, respawnAt = -1f },
    new { id = "H-ppk-holster", phase = "p1", scenario = T07, weapon = "PPK_Weapon", rifle = false, seconds = 30f, zero = false, noRecoil = false, holster = true, unarmed = false, purchase = (string)null, purchaseAt = -1f, killAt = -1f, respawnAt = -1f },
    new { id = "U-unarmed", phase = "p1", scenario = T07, weapon = "PPK_Weapon", rifle = false, seconds = 20f, zero = false, noRecoil = false, holster = false, unarmed = true, purchase = (string)null, purchaseAt = -1f, killAt = -1f, respawnAt = -1f },
    new { id = "P-purchase-ak105", phase = "p1", scenario = T07, weapon = "PPK_Weapon", rifle = false, seconds = 35f, zero = false, noRecoil = false, holster = true, unarmed = false, purchase = "AK105_Weapon", purchaseAt = 10f, killAt = -1f, respawnAt = -1f },
    new { id = "G-ghost-respawn-ak105", phase = "p2", scenario = T07, weapon = "AK105_Weapon", rifle = true, seconds = 40f, zero = false, noRecoil = false, holster = false, unarmed = false, purchase = (string)null, purchaseAt = -1f, killAt = 8f, respawnAt = 20f },
    new { id = "AB-T01-ak105", phase = "p2", scenario = VrBattlegrounds.DevTools.BotCombatStand.BotStandScenarioId.T01, weapon = "AK105_Weapon", rifle = true, seconds = 25f, zero = false, noRecoil = false, holster = false, unarmed = false, purchase = (string)null, purchaseAt = -1f, killAt = -1f, respawnAt = -1f },
    new { id = "AB-T06-ak105", phase = "p2", scenario = VrBattlegrounds.DevTools.BotCombatStand.BotStandScenarioId.T06, weapon = "AK105_Weapon", rifle = true, seconds = 25f, zero = false, noRecoil = false, holster = false, unarmed = false, purchase = (string)null, purchaseAt = -1f, killAt = -1f, respawnAt = -1f },
    new { id = "AB-T08-ak105", phase = "p2", scenario = VrBattlegrounds.DevTools.BotCombatStand.BotStandScenarioId.T08, weapon = "AK105_Weapon", rifle = true, seconds = 25f, zero = false, noRecoil = false, holster = false, unarmed = false, purchase = (string)null, purchaseAt = -1f, killAt = -1f, respawnAt = -1f } };
var combos = all.Where(c => c.phase == Phase).ToArray();
if (AppDomain.CurrentDomain.GetData(Marker) != null) return new { executionCompleted = true, refused = true, reason = "Очередь уже подключена." };
if (System.IO.File.Exists(PhaseRoot + "/queue.json")) return new { executionCompleted = true, refused = true, reason = "queue.json уже существует." };
if (!EditorApplication.isPlaying || !Mirror.NetworkServer.active || VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.Instance == null) throw new InvalidOperationException("Нужен Play стенда.");
var standStatus = VrBattlegrounds.EditorTools.BotCombatStandEditor.Status();
if (!(bool)standStatus["owned"] || !(bool)standStatus["finished"] || Convert.ToInt32(standStatus["invalidFixture"] ?? 0) > 0) return new { executionCompleted = true, refused = true, reason = "warmup не готов", standStatus };
if (VrBattlegrounds.Bots.BotSenses.Stage() != VrBattlegrounds.Bots.BotStage.Combat) throw new InvalidOperationException("Live не Combat");

var stateNames = new[] { "Idle", "Move", "Back", "Left", "Right", "Shoot" };
var stateHashes = stateNames.Select(n => Animator.StringToHash(n)).ToArray();
var moveStates = new[] { "Move", "Back", "Left", "Right" };
Func<int, string> stateOf = hash => { for (int i = 0; i < stateHashes.Length; i++) if (stateHashes[i] == hash) return stateNames[i]; return "#" + hash; };
Func<float, float> R3 = v => (float)Math.Round(v, 3);
Func<Vector3, float[]> V3 = v => new[] { v.x, v.y, v.z };

// ── Очередь ──
var entries = new List<Dictionary<string, object>>();
var notes = new List<string>();
string queueState = "running", queueError = null, runId = null;
int index = -1; double startedAt = 0; bool cancelRequested = false; double runLimit = 0;
// ── Журнал консоли (весь Play фазы и по case) ──
int gMissing = 0, gEmpty = 0, gErrors = 0, gExceptions = 0, gWarnings = 0, gCameraFade = 0;
var gSamples = new List<string>(); var fadeSamples = new List<string>();
int cMissing = 0, cEmpty = 0, cErrors = 0, cExceptions = 0, cCameraFade = 0;
var cSamples = new List<string>();
// ── Состояние case ──
VrBattlegrounds.DevTools.BotCombatStand.BotStandSession caseSession = null;
string caseCombo = null; float caseT0 = 0f; string caseScenario = null;
long memStartMono = 0, memStartUnity = 0;
var bodies = new List<Dictionary<string, object>>();
var seenBodies = new HashSet<int>();
VrBattlegrounds.Player.PlayerController lastSubject = null, lastTarget = null;
UltimateXR.Avatar.UxrAvatar targetAvatar = null;
int tFrames = 0, tStepsOver1mm = 0; double tHSum = 0; float tHMin = float.MaxValue, tHMax = float.MinValue, tDrift = 0f, tStep = 0f;
Vector3 tFirst = Vector3.zero, tPrev = Vector3.zero; bool tSeen = false;
global::BlazeAI brain = null; float brainBorn = 0f; Vector3 brainBornPos = Vector3.zero; bool brainMovedOnce = false;
Dictionary<string, object> proxyEntry = null;
string lastAnim = null; int lastHash = 0; float lastNt = -1f;
int ntSame = 0, ntUp = 0, ntStuck = 0, sampleFrames = 0, proxyFrames = 0;
var animatorSeen = new Dictionary<string, int>();
var proxies = new List<Dictionary<string, object>>();
var transitions = new List<Dictionary<string, object>>();
var moves = new List<Dictionary<string, object>>();
Dictionary<string, object> pending = null; Vector3 pendingPos = Vector3.zero; float pendingT = 0f; int pendingFrame = 0;
var trace = new List<object>(); float traceUntil = -1f;
var targetTrace = new List<object>();
var sampleErrors = new List<string>();
// Public virtual вызовы снимаются с создания proxy, включая readiness до CaseStarted.
var blazeCalls = new List<VrBattlegrounds.Bots.BotBlazeDiagnosticEvent>();
int blazeDropped = 0;
IDisposable blazeSubscription = null;

Func<VrBattlegrounds.Player.PlayerController, string, float, Dictionary<string, object>> describe = (p, role, at) => {
    var runtime = p.GetComponent<VrBattlegrounds.Bots.BotRuntimeController>();
    return new Dictionary<string, object> {
        { "role", role }, { "t", R3(at) }, { "frame", Time.frameCount }, { "body", p.GetInstanceID() },
        { "prefab", p.SourcePrefab != null ? p.SourcePrefab.name : p.name }, { "alive", p.IsAlive },
        { "botBody", p.GetComponents<VrBattlegrounds.Bots.BotBody>().Length },
        { "botNavigator", p.GetComponents<VrBattlegrounds.Bots.BotNavigator>().Length },
        { "botGunner", p.GetComponents<VrBattlegrounds.Bots.BotGunner>().Length },
        { "botAvatarDriver", p.GetComponents<VrBattlegrounds.Bots.BotAvatarDriver>().Length },
        { "botCombatDriver", p.GetComponents<VrBattlegrounds.Bots.BotCombatDriver>().Length },
        { "botHands", p.GetComponents<VrBattlegrounds.Bots.BotHands>().Length },
        { "botWeaponAim", p.GetComponents<VrBattlegrounds.Bots.BotWeaponAim>().Length },
        { "botRuntimeController", p.GetComponents<VrBattlegrounds.Bots.BotRuntimeController>().Length },
        { "passive", runtime != null && runtime.Passive } };
};
Action<string> closePending = reason => {
    if (pending == null) return;
    pending["result"] = reason; pending["elapsed"] = R3(Time.time - pendingT); pending["elapsedFrames"] = Time.frameCount - pendingFrame;
    pending = null;
};
Action<string> closeProxy = reason => {
    if (proxyEntry == null) return;
    closePending("proxyGone");
    proxyEntry["removedT"] = R3(Time.time - caseT0); proxyEntry["removedReason"] = reason;
    proxyEntry = null; brain = null;
};
Action resetCase = () => {
    caseSession = null; caseCombo = null; caseScenario = null;
    bodies = new List<Dictionary<string, object>>(); seenBodies = new HashSet<int>(); lastSubject = null; lastTarget = null; targetAvatar = null;
    tFrames = 0; tStepsOver1mm = 0; tHSum = 0; tHMin = float.MaxValue; tHMax = float.MinValue; tDrift = 0f; tStep = 0f; tSeen = false;
    brain = null; proxyEntry = null; lastAnim = null; lastHash = 0; lastNt = -1f; ntSame = 0; ntUp = 0; ntStuck = 0; sampleFrames = 0; proxyFrames = 0;
    animatorSeen = new Dictionary<string, int>(); proxies = new List<Dictionary<string, object>>();
    transitions = new List<Dictionary<string, object>>(); moves = new List<Dictionary<string, object>>(); pending = null;
    trace = new List<object>(); targetTrace = new List<object>(); traceUntil = -1f; sampleErrors = new List<string>();
    cMissing = 0; cEmpty = 0; cErrors = 0; cExceptions = 0; cCameraFade = 0; cSamples = new List<string>();
};

Application.LogCallback onLog = (condition, stack, type) => {
    try {
        string text = condition ?? "";
        bool missing = text.Contains("doesn't exist"), empty = text.Contains("Empty animation"), fade = text.Contains("[CameraFade]");
        if (missing) { gMissing++; cMissing++; }
        if (empty) { gEmpty++; cEmpty++; }
        if (fade) { gCameraFade++; cCameraFade++; if (fadeSamples.Count < 40) fadeSamples.Add(Time.realtimeSinceStartup.ToString("0.00") + " f" + Time.frameCount + " " + (text.Length > 200 ? text.Substring(0, 200) : text)); }
        if (type == LogType.Warning) gWarnings++;
        bool bad = type == LogType.Error || type == LogType.Exception || type == LogType.Assert;
        if (type == LogType.Exception) { gExceptions++; cExceptions++; } else if (bad) { gErrors++; cErrors++; }
        if (bad || missing || empty) {
            string line = type + " " + (caseCombo ?? "-") + ": " + (text.Length > 300 ? text.Substring(0, 300) : text);
            if (gSamples.Count < 30) gSamples.Add(line);
            if (cSamples.Count < 15) cSamples.Add(line);
        }
    } catch { }
};

Action sample = () => {
    if (caseSession == null) return;
    try {
        float t = Time.time - caseT0;
        sampleFrames++;
        // Состав комплекта: проверяемый бот (каждое новое тело) и контрольная цель.
        var subject = caseSession.Subject != null ? caseSession.Subject.ActiveAvatar : null;
        if (!ReferenceEquals(subject, lastSubject)) {
            if (lastSubject != null) bodies.Add(describe(lastSubject, "subject-leave", t));
            if (subject != null && seenBodies.Add(subject.GetInstanceID())) bodies.Add(describe(subject, "subject", t));
            lastSubject = subject;
        }
        var target = caseSession.Target != null ? caseSession.Target.ActiveAvatar : null;
        if (!ReferenceEquals(target, lastTarget)) {
            if (target != null && seenBodies.Add(target.GetInstanceID())) bodies.Add(describe(target, "target", t));
            lastTarget = target; targetAvatar = target != null ? target.GetComponent<UltimateXR.Avatar.UxrAvatar>() : null;
        }
        // Сохраняем исходные samples, включая неготовую позу: агрегаты не скрывают начальный скачок.
        var targetCamera = targetAvatar != null ? targetAvatar.CameraTransform : null;
        var targetBody = target != null ? target.GetComponent<VrBattlegrounds.Bots.BotBody>() : null;
        if (targetTrace.Count < 2048) targetTrace.Add(new {
            t, time = Time.time, realtime = Time.realtimeSinceStartup, frame = Time.frameCount,
            combo = caseCombo, scenario = caseScenario, stage = VrBattlegrounds.Bots.BotSenses.Stage().ToString(),
            body = target != null ? (int?)target.GetInstanceID() : null,
            root = target != null ? V3(target.transform.position) : null,
            camera = targetCamera != null ? V3(targetCamera.position) : null,
            feet = targetBody != null ? V3(targetBody.Feet) : null,
            heightAboveRoot = target != null && targetCamera != null ? (float?)(targetCamera.position.y - target.transform.position.y) : null,
            stepFromPreviousM = tSeen && target != null ? (float?)Vector3.Distance(target.transform.position, tPrev) : null,
            referencesPresent = target != null && targetAvatar != null && targetCamera != null,
            bodyActive = target != null && target.gameObject.activeInHierarchy,
            cameraActive = targetCamera != null && targetCamera.gameObject.activeInHierarchy
        });
        // Цель: камера над корнем и дрейф корня. Критерии остаются по всем измеренным кадрам.
        if (target != null && targetAvatar != null && targetAvatar.CameraTransform != null) {
            Vector3 root = target.transform.position;
            float h = targetAvatar.CameraTransform.position.y - root.y;
            tFrames++; tHSum += h; tHMin = Mathf.Min(tHMin, h); tHMax = Mathf.Max(tHMax, h);
            if (!tSeen) { tFirst = root; tPrev = root; tSeen = true; }
            float step = Vector3.Distance(root, tPrev);
            tStep = Mathf.Max(tStep, step); if (step > 0.001f) tStepsOver1mm++;
            tDrift = Mathf.Max(tDrift, Vector3.Distance(root, tFirst)); tPrev = root;
        }
        // Blaze-прокси.
        var driver = subject != null ? subject.GetComponent<VrBattlegrounds.Bots.BotCombatDriver>() : null;
        var current = driver != null ? driver.Brain : null;
        if (!ReferenceEquals(current, brain)) {
            closeProxy(current == null ? "removed" : "replaced");
            if (current != null) {
                brain = current; brainBorn = Time.time; brainBornPos = current.transform.position; brainMovedOnce = false;
                lastAnim = null; lastHash = 0; lastNt = -1f;
                proxyEntry = new Dictionary<string, object> { { "bornT", R3(t) }, { "bornFrame", Time.frameCount }, { "name", current.name },
                    { "controller", current.anim != null && current.anim.runtimeAnimatorController != null ? current.anim.runtimeAnimatorController.name : null },
                    { "applyRootMotion", current.anim != null && current.anim.applyRootMotion },
                    { "cullingMode", current.anim != null ? current.anim.cullingMode.ToString() : null } };
                proxies.Add(proxyEntry);
                traceUntil = Time.time + 6f;
            }
        }
        if (brain == null) return;
        proxyFrames++;
        var anim = brain.anim;
        string animState = brain.animManager != null ? brain.animManager.currentState : null;
        Vector3 pos = brain.transform.position;
        float speed = brain.navmeshAgent != null ? brain.navmeshAgent.velocity.magnitude : -1f;
        string animatorState = null; float nt = -1f; bool inTransition = false;
        if (anim != null && anim.isActiveAndEnabled) {
            var info = anim.GetCurrentAnimatorStateInfo(0);
            animatorState = stateOf(info.shortNameHash); nt = info.normalizedTime; inTransition = anim.IsInTransition(0);
            int seen; animatorSeen.TryGetValue(animatorState, out seen); animatorSeen[animatorState] = seen + 1;
            if (info.shortNameHash == lastHash) { ntSame++; if (nt > lastNt + 1e-5f) ntUp++; else ntStuck++; }
            lastHash = info.shortNameHash; lastNt = nt;
        }
        if (!brainMovedOnce && new Vector2(pos.x - brainBornPos.x, pos.z - brainBornPos.z).magnitude > 0.02f) {
            brainMovedOnce = true; proxyEntry["firstDisplacementAfterBirth"] = R3(Time.time - brainBorn);
        }
        if (animState != lastAnim) {
            transitions.Add(new Dictionary<string, object> { { "t", R3(t) }, { "frame", Time.frameCount }, { "from", lastAnim }, { "to", animState },
                { "animatorState", animatorState }, { "nt", R3(nt) }, { "inTransition", inTransition }, { "blazeState", brain.state.ToString() } });
            if (animState != null && moveStates.Contains(animState)) {
                closePending("superseded");
                int ofProxy = moves.Count(m => object.Equals(m["proxy"], proxyEntry["bornFrame"]));
                int moveOfProxy = moves.Count(m => object.Equals(m["proxy"], proxyEntry["bornFrame"]) && object.Equals(m["anim"], "Move"));
                pending = new Dictionary<string, object> { { "t", R3(t) }, { "frame", Time.frameCount }, { "anim", animState }, { "from", lastAnim },
                    { "proxy", proxyEntry["bornFrame"] }, { "proxyAge", R3(Time.time - brainBorn) }, { "firstOfProxy", ofProxy == 0 },
                    { "firstMoveOfProxy", animState == "Move" && moveOfProxy == 0 },
                    { "ntAtPlay", R3(nt) }, { "animatorStateAtPlay", animatorState }, { "blazeState", brain.state.ToString() } };
                moves.Add(pending); pendingPos = pos; pendingT = Time.time; pendingFrame = Time.frameCount;
                traceUntil = Mathf.Max(traceUntil, Time.time + 1.5f);
            } else closePending("stateChanged:" + animState);
            lastAnim = animState;
        }
        if (pending != null) {
            float moved = new Vector2(pos.x - pendingPos.x, pos.z - pendingPos.z).magnitude;
            if (moved > 0.02f) {
                pending["latency"] = R3(Time.time - pendingT); pending["latencyFrames"] = Time.frameCount - pendingFrame;
                pending["animatorStateAtMove"] = animatorState; pending["ntAtMove"] = R3(nt); pending["speedAtMove"] = R3(speed);
                pending["result"] = "moved"; pending = null;
            } else if (Time.time - pendingT > 3f) closePending("noMove3s");
        }
        if (Time.time <= traceUntil && trace.Count < 4000)
            trace.Add(new object[] { R3(t), Time.frameCount, animState, animatorState, R3(nt), inTransition, R3(pos.x), R3(pos.z), R3(speed) });
    } catch (Exception error) { if (sampleErrors.Count < 16) sampleErrors.Add(error.GetType().Name + ": " + error.Message); }
};

Action write = () => { try { System.IO.Directory.CreateDirectory(PhaseRoot);
    System.IO.File.WriteAllText(PhaseRoot + "/queue.json", Newtonsoft.Json.JsonConvert.SerializeObject(new { state = queueState, error = queueError, index, currentRunId = runId,
        liveStage = EditorApplication.isPlaying ? VrBattlegrounds.Bots.BotSenses.Stage().ToString() : "NotPlaying", combos = entries, notes = notes.ToArray(),
        log = new { missingAnim = gMissing, emptyAnim = gEmpty, errors = gErrors, exceptions = gExceptions, warnings = gWarnings, cameraFade = gCameraFade,
            samples = gSamples, cameraFadeSamples = fadeSamples },
        memory = new { monoBytes = GC.GetTotalMemory(false), unityAllocated = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() },
        updatedUtc = DateTime.UtcNow.ToString("o") }, Newtonsoft.Json.Formatting.Indented)); } catch { } };

Action<VrBattlegrounds.DevTools.BotCombatStand.BotStandCaseResult, VrBattlegrounds.DevTools.BotCombatStand.BotStandSession> onStarted = (result, session) => {
    resetCase();
    caseSession = session; caseT0 = Time.time; caseScenario = result.Id;
    caseCombo = index >= 0 && index < combos.Length ? combos[index].id : null;
    memStartMono = GC.GetTotalMemory(false); memStartUnity = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
    try { if (index >= 0 && index < entries.Count) { entries[index]["caseStartedLocal"] = DateTime.Now.ToString("HH:mm:ss"); write(); } } catch { } };

Action<VrBattlegrounds.DevTools.BotCombatStand.BotStandCaseResult> onFinished = result => {
    try {
        if (caseSession == null) return;
        float t = Time.time - caseT0;
        if (lastSubject != null) bodies.Add(describe(lastSubject, "subject-end", t));
        if (lastTarget != null) bodies.Add(describe(lastTarget, "target-end", t));
        closeProxy("caseEnd");
        var metrics = new Dictionary<string, object> {
            { "combo", caseCombo }, { "scenario", caseScenario }, { "sampleFrames", sampleFrames },
            { "kit", bodies },
            { "target", new Dictionary<string, object> { { "frames", tFrames },
                { "cameraHeightAboveRoot", tFrames > 0 ? (object)new { min = R3(tHMin), max = R3(tHMax), mean = R3((float)(tHSum / tFrames)) } : null },
                { "rootDriftFromFirstM", R3(tDrift) }, { "rootMaxStepM", R3(tStep) }, { "rootStepsOver1mm", tStepsOver1mm },
                { "traceRows", targetTrace.Count }, { "traceTruncated", sampleFrames > targetTrace.Count },
                { "note", "Place задаёт ноги BotBody; корень аватара записывает штатный драйвер. В T07 проверяется стабильность корня и высоты; readiness по ссылкам не означает калибровку." } } },
            { "proxy", new Dictionary<string, object> { { "proxies", proxies }, { "frames", proxyFrames }, { "moves", moves },
                { "transitions", transitions.Take(200).ToList() }, { "transitionsTotal", transitions.Count },
                { "normalizedTime", new { sameStateFrames = ntSame, increasingFrames = ntUp, stuckFrames = ntStuck } },
                { "animatorStatesSeen", animatorSeen } } },
            { "log", new { missingAnim = cMissing, emptyAnim = cEmpty, errors = cErrors, exceptions = cExceptions, cameraFade = cCameraFade, samples = cSamples } },
            { "memory", new { monoStart = memStartMono, monoEnd = GC.GetTotalMemory(false), unityStart = memStartUnity,
                unityEnd = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() } },
            { "sampleErrors", sampleErrors } };
        result.Metrics["cc1"] = metrics;
        var folder = System.IO.Path.Combine(VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.LastOutput, result.Id + "-" + result.Seed);
        System.IO.Directory.CreateDirectory(folder);
        System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "cc1-trace.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new {
            columns = new[] { "t", "frame", "animState", "animatorState", "nt", "inTransition", "x", "z", "agentSpeed" }, rows = trace }));
        System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "cc1-target-trace.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new {
            limit = 2048, observedFrames = sampleFrames, truncated = sampleFrames > targetTrace.Count,
            validityScope = "Только ссылки и activeInHierarchy; калибровка не утверждается. Начальные samples не исключены.",
            rows = targetTrace }));
        System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "cc1-blaze-calls.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new {
            limit = 50000, dropped = blazeDropped,
            coverageComplete = blazeDropped == 0 && VrBattlegrounds.Bots.BotBlazeDiagnostics.IsSubscribed(blazeSubscription) &&
                blazeCalls.Any(e => e.Kind == VrBattlegrounds.Bots.BotBlazeDiagnosticKind.ProxyStarted) &&
                !blazeCalls.Any(e => e.Kind == VrBattlegrounds.Bots.BotBlazeDiagnosticKind.CoverageLost),
            scope = "Public virtual callbacks, включая readiness. Приватный gate и фактический Agent.Move не измерены.",
            events = blazeCalls.Select(e => new {
                kind = e.Kind.ToString(), e.OwnerBodyId, e.ProxyId, e.ProxyGeneration, e.Sequence, e.CallId, e.Result, e.Caller,
                sample = new { e.Sample.Frame, e.Sample.Time, e.Sample.Realtime, e.Sample.DeltaTime,
                    position = V3(e.Sample.Position), e.Sample.CurrentHash, e.Sample.CurrentNt, e.Sample.NextHash, e.Sample.NextNt,
                    e.Sample.TransitionHash, e.Sample.TransitionNt, e.Sample.InTransition, e.Sample.AnimatorValid,
                    e.Sample.AnimatorEnabled, e.Sample.PreviousRequestedAnimation, state = e.Sample.State.ToString(),
                    e.Sample.PathReachable, pathCorner = V3(e.Sample.PathCorner), e.Sample.AgentEnabled, e.Sample.OnNavMesh,
                    e.Sample.IsStopped, e.Sample.PathPending, e.Sample.HasPath, e.Sample.OffMeshLink,
                    agentPathStatus = e.Sample.AgentPathStatus.ToString(), destination = V3(e.Sample.AgentDestination),
                    nextPosition = V3(e.Sample.NextPosition), velocity = V3(e.Sample.Velocity),
                    desiredVelocity = V3(e.Sample.DesiredVelocity), e.Sample.IsAttacking },
                call = new { destination = V3(e.Call.Destination), e.Call.Animation, e.Call.Direction,
                    e.Call.TransitionSeconds, e.Call.MoveSpeed, e.Call.TurnSpeed, e.Call.ClosestPointDistance,
                    e.Call.Overplay, e.Call.AddAsLastPath, e.Call.ForceState, requestedState = e.Call.RequestedState.ToString() }
            }).ToArray() }));
    } catch (Exception error) { if (notes.Count < 32) notes.Add("onFinished: " + error.Message); }
    finally { caseSession = null; }
};

EditorApplication.CallbackFunction tick = null;
Action detach = () => {
    if (blazeSubscription != null) { blazeSubscription.Dispose(); blazeSubscription = null; }
    VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.CaseStarted -= onStarted;
    VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.CaseFinished -= onFinished;
    UltimateXR.Core.UxrManager.AvatarsUpdated -= sample;
    Application.logMessageReceived -= onLog;
    EditorApplication.update -= tick; };
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
            runId = null; cancelRequested = false; write();
        }
        if (index + 1 >= combos.Length) { queueState = "completed"; write(); detach(); return; }
        if (VrBattlegrounds.Bots.BotSenses.Stage() != VrBattlegrounds.Bots.BotStage.Combat) { queueState = "live-ended"; queueError = "Live завершён до " + combos[index + 1].id; write(); detach(); return; }
        index++;
        var combo = combos[index];
        blazeCalls.Clear(); blazeDropped = 0;
        var ids = new[] { combo.scenario };
        var options = new VrBattlegrounds.DevTools.BotCombatStand.BotStandRunOptions {
            OutputDirectory = Root + "/" + combo.id, Profile = VrBattlegrounds.DevTools.BotCombatStand.BotStandProfile.Production,
            Seeds = new[] { 101 }, CaseSeconds = combo.seconds, Capture = false, Rifle = combo.rifle, HandFit = false,
            WeaponName = combo.weapon, AvatarIndex = -1, ZeroAiAimSpread = combo.zero, NoRecoil = combo.noRecoil,
            ObserveShotTail = combo.zero && combo.noRecoil,
            HolsterStart = combo.holster, Unarmed = combo.unarmed, PurchaseWeaponName = combo.purchase, PurchaseAtSeconds = combo.purchaseAt,
            KillAtSeconds = combo.killAt, RespawnAtSeconds = combo.respawnAt, Cases = ids };
        var entry = new Dictionary<string, object> { { "combo", combo.id }, { "options", options }, { "requestedLocal", DateTime.Now.ToString("HH:mm:ss") } };
        entries.Add(entry);
        runLimit = combo.seconds + 60.0;
        var handle = VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.RunCases(options, ids);
        runId = handle.RunId; entry["runId"] = handle.RunId; startedAt = EditorApplication.timeSinceStartup;
        write();
    } catch (Exception error) { queueState = "failed"; queueError = error.ToString(); write(); detach(); }
};
AppDomain.CurrentDomain.SetData(Marker, DateTime.UtcNow.ToString("o"));
blazeSubscription = VrBattlegrounds.Bots.BotBlazeDiagnostics.Subscribe(
    VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.Instance, e => {
        if (blazeCalls.Count < 50000) blazeCalls.Add(e); else blazeDropped++;
    });
VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.CaseStarted += onStarted;
VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.CaseFinished += onFinished;
UltimateXR.Core.UxrManager.AvatarsUpdated += sample;
Application.logMessageReceived += onLog;
write();
EditorApplication.update += tick;
return new { executionCompleted = true, started = true, phase = Phase, combos = combos.Select(c => c.id).ToArray(), seconds = combos.Sum(c => c.seconds), nowLocal = DateTime.Now.ToString("HH:mm:ss") };
