// B4 MUTATION (Play стенда после warmup): очередь раунда II наведения — mef-ak105 и cyborg-ak105 (T07+T05),
// mef-ppk и cyborg-ppk (только T07), 6 case по 20 с, в ТЕКУЩЕМ Live (очередь та же, что в раунде I)
// + read-only sampler фактического Animator/корня/оружия, поправки курса BotAim (delta/integral/psi/c/phi) и AimPitch
// (param/target/integral/g/basePitch/elevation, значение в Animator и квант SyncVar). Payload возвращается сразу; дальше
// EditorApplication.update сам запускает BotCombatStand.RunCases по очереди (без пауз агента,
// чтобы уложиться в 300 с Respawn Live). Прогресс — durable runtime/queue.json (читать B5).
// Стенд сам вооружает бота (BotGunner.Arm -> WeaponEquipmentBinding -> RequestAutomationPreparation)
// и стреляет только штатным TryToShootRound по решению Blaze. Payload ничем из этого не управляет.
// НЕ ПОВТОРЯТЬ после таймаута: сначала B5. Повторный вызов в том же Play отказывает по AppDomain marker,
// в новом Play — по существующему queue.json.
const string Root = "tasks/bots-fix/reports/aim-round2/runtime";
const string Marker = "bots-fix.aim-round2.queue";
const float CaseSeconds = 20f;
const string Mef = "b8146035f3f436a4799882b3a0d1dd45";    // Optimized_MEF_Player (цель стенда — MEF Black)
const string Cyborg = "8e0e06323e4754f479a9f19788554652"; // PlayerControllersCyborgAvatar
var rifleCases = new[] { VrBattlegrounds.DevTools.BotCombatStand.BotStandScenarioId.T07,   // открытое поле: strafe/стрельба
                         VrBattlegrounds.DevTools.BotCombatStand.BotStandScenarioId.T05 }; // смещение угрозы: повороты
var controlCases = new[] { VrBattlegrounds.DevTools.BotCombatStand.BotStandScenarioId.T07 };
// Сначала AK105 (основная цель наведения), затем PPK обеих моделей (T07).
var combos = new[] {
    new { id = "mef-ak105", guid = Mef, weapon = "AK105_Weapon", rifle = true, cases = rifleCases },
    new { id = "cyborg-ak105", guid = Cyborg, weapon = "AK105_Weapon", rifle = true, cases = rifleCases },
    new { id = "mef-ppk", guid = Mef, weapon = "PPK_Weapon", rifle = false, cases = controlCases },
    new { id = "cyborg-ppk", guid = Cyborg, weapon = "PPK_Weapon", rifle = false, cases = controlCases } };
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
var library = Resources.Load<VrBattlegrounds.Bots.BotCombatAssets>(VrBattlegrounds.Bots.BotCombatAssets.ResourcePath);
var stateNames = new[] { "Idle", "Move", "Back", "Left", "Right", "Shoot" };
var samples = new Dictionary<VrBattlegrounds.DevTools.BotCombatStand.BotStandCaseResult, List<object>>();
var samplerErrors = new List<string>();
var entries = new List<Dictionary<string, object>>();
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
            caseSeconds = CaseSeconds, totalCases, combos = entries,
            samplerErrors = samplerErrors.ToArray(), updatedUtc = DateTime.UtcNow.ToString("o") }, Newtonsoft.Json.Formatting.Indented));
    } catch (Exception error) { note("queue.json: " + error.Message); }
};
// --- Read-only sampler: подписчик никогда не бросает (исключение превратило бы case в InvalidFixture). ---
Action<VrBattlegrounds.DevTools.BotCombatStand.BotStandCaseResult, VrBattlegrounds.DevTools.BotCombatStand.BotStandSession> onSampled = (result, session) => {
    try {
        List<object> list;
        if (!samples.TryGetValue(result, out list)) { list = new List<object>(); samples[result] = list; }
        if (list.Count >= 4000) return;
        var player = session != null && session.Subject != null ? session.Subject.ActiveAvatar : null;
        if (player == null) { list.Add(new { t = Time.time, frame = Time.frameCount, missingSubject = true }); return; }
        var body = player.GetComponent<VrBattlegrounds.Bots.BotBody>();
        var gun = player.GetComponent<VrBattlegrounds.Bots.BotGunner>();
        var driver = player.GetComponent<VrBattlegrounds.Bots.BotCombatDriver>();
        var clips = player.GetComponent<VrBattlegrounds.Bots.BotClipBody>();
        var animator = body != null ? body.Animator : null;
        string current = null, nextState = null, controller = null;
        bool transition = false;
        float? normalized = null, rate = null, native = null, stateSpeed = null, animatorPitch = null;
        if (animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null) {
            var cur = animator.GetCurrentAnimatorStateInfo(0);
            var nxt = animator.GetNextAnimatorStateInfo(0);
            foreach (var s in stateNames) { if (cur.IsName(s)) current = s; if (nxt.IsName(s)) nextState = s; }
            transition = animator.IsInTransition(0);
            normalized = cur.normalizedTime; stateSpeed = cur.speed * cur.speedMultiplier;
            rate = animator.GetFloat(VrBattlegrounds.Bots.BotCombatAssets.MotionRateParameter);
            controller = animator.runtimeAnimatorController.name;
            foreach (var parameter in animator.parameters)
                if (parameter.name == VrBattlegrounds.Bots.BotCombatAssets.AimPitchParameter) { animatorPitch = animator.GetFloat(parameter.nameHash); break; }
            if (current != null && current != "Idle" && current != "Shoot" && library != null)
                native = library.GetMotionSpeed(player.SourcePrefab, animator.runtimeAnimatorController, current);
        }
        var runtime = player.GetComponent<VrBattlegrounds.Bots.BotRuntimeController>();
        var aim = runtime != null ? runtime.Aim : null;
        Func<float, float?> finiteOrNull = v => float.IsNaN(v) || float.IsInfinity(v) ? (float?)null : v;
        object aimState = aim == null ? null : (object)new { delta = aim.Delta, deltaActive = aim.DeltaActive, integral = aim.Integral,
            psi = finiteOrNull(aim.Psi), c = finiteOrNull(aim.Lever), phi = finiteOrNull(aim.Heading),
            proxyYaw = finiteOrNull(aim.ProxyYaw), lastYawError = finiteOrNull(aim.LastYawError),
            appliedOffset = body != null ? (float?)body.AimYawOffset : null,
            pitch = new { param = aim.Pitch, target = finiteOrNull(aim.PitchTarget), integral = aim.PitchIntegral,
                g = finiteOrNull(aim.PitchGain), basePitch = finiteOrNull(aim.BasePitch), active = aim.PitchActive,
                elevation = finiteOrNull(aim.Elevation), lastError = finiteOrNull(aim.LastPitchError),
                animator = animatorPitch, synced = clips != null ? (float?)clips.SyncedAimPitch : null } };
        var feet = player.transform.position;
        var targetPlayer = session.Target != null ? session.Target.ActiveAvatar : null;
        float[] targetFeet = null; float? bearing = null;
        if (targetPlayer != null) {
            var to = targetPlayer.transform.position - feet; to.y = 0f;
            var forward = player.transform.forward; forward.y = 0f;
            targetFeet = new[] { targetPlayer.transform.position.x, targetPlayer.transform.position.y, targetPlayer.transform.position.z };
            if (to.sqrMagnitude > 1e-6f && forward.sqrMagnitude > 1e-6f) bearing = Vector3.SignedAngle(forward, to, Vector3.up);
        }
        var firearm = gun != null ? gun.Firearm : null;
        string weaponName = null, category = null;
        bool? ready = null, chamber = null, author = null;
        int? magazine = null, total = null;
        uint? shotSequence = null, revision = null;
        float[] inHand = null;
        if (firearm != null) {
            var component = firearm.GetComponent<VrBattlegrounds.Arsenal.WeaponComponent>();
            weaponName = component != null && component.WeaponData != null ? component.WeaponData.name : null;
            category = gun.WeaponCategory.HasValue ? gun.WeaponCategory.Value.ToString() : null;
            ready = firearm.IsReadyToFire(0); magazine = firearm.GetMagazineRounds(0); total = firearm.GetTotalAmmoLeft(0);
            var readiness = firearm.GetReadinessState(0);
            if (readiness != null) { shotSequence = readiness.ShotSequence; revision = readiness.Revision; chamber = readiness.ChamberRound; }
            author = VrBattlegrounds.Network.StateEventAuthority.IsAuthorOfItem(firearm);
            var hand = clips != null ? clips.RightHand : null;
            if (hand != null) {
                // Поза предмета в пространстве кисти: стабильность = отсутствие инерции (приблизительно, выборка 0.1 с).
                var local = hand.InverseTransformPoint(firearm.transform.position);
                var rotation = Quaternion.Inverse(hand.rotation) * firearm.transform.rotation;
                inHand = new[] { local.x, local.y, local.z, rotation.x, rotation.y, rotation.z, rotation.w };
            }
        }
        list.Add(new {
            t = Time.time, frame = Time.frameCount, subjectNetId = player.netId, alive = player.IsAlive,
            source = AssetDatabase.GetAssetPath(player.SourcePrefab),
            feet = new[] { feet.x, feet.y, feet.z }, yaw = body != null ? (float?)body.Yaw : null,
            state = current, next = nextState, transition, normalized, rate, nativeMetersPerSecond = native, stateSpeed, controller,
            blaze = driver != null && driver.Brain != null ? driver.Brain.state.ToString() : null,
            combat = driver != null && driver.CombatActive, hasSelectedTarget = driver != null && driver.Target != null,
            targetFeet, targetBearing = bearing,
            weapon = weaponName, category, preparation = gun != null ? gun.Preparation.ToString() : null,
            counterShots = gun != null ? (int?)gun.ShotsFired : null,
            ready, magazine, total, shotSequence, revision, chamber, author, weaponInHand = inHand, aim = aimState });
    } catch (Exception error) { note("sample: " + error.Message); }
};
Action<VrBattlegrounds.DevTools.BotCombatStand.BotStandCaseResult> onFinished = result => {
    try {
        List<object> list;
        samples.TryGetValue(result, out list);
        var folder = System.IO.Path.Combine(VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.LastOutput, result.Id + "-" + result.Seed);
        System.IO.Directory.CreateDirectory(folder);
        System.IO.File.WriteAllText(System.IO.Path.Combine(folder, "b-sampler.json"), Newtonsoft.Json.JsonConvert.SerializeObject(new {
            combo = comboId, caseId = result.Id, seed = result.Seed, caseStatus = result.Status, samples = list ?? new List<object>(),
            samplerErrors = samplerErrors.ToArray(),
            scope = "Read-only CaseSampled observer (0.1 s, before LateUpdate); weaponInHand approximate." }, Newtonsoft.Json.Formatting.Indented));
        samples.Remove(result);
    } catch (Exception error) { note("finish: " + error.Message); }
};
EditorApplication.CallbackFunction tick = null;
Action detach = () => {
    VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.CaseSampled -= onSampled;
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
            WeaponName = combo.weapon, AvatarIndex = -1, AvatarSourceGuid = combo.guid, Cases = combo.cases };
        var entry = new Dictionary<string, object> { { "combo", combo.id }, { "sourceGuid", combo.guid }, { "weapon", combo.weapon },
            { "rifle", combo.rifle }, { "cases", combo.cases.Select(c => c.ToString()).ToArray() },
            { "output", options.OutputDirectory }, { "startedUtc", DateTime.UtcNow.ToString("o") } };
        entries.Add(entry);
        runLimit = combo.cases.Length * CaseSeconds + 60.0;
        var handle = VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.RunCases(options, combo.cases);
        runId = handle.RunId; entry["runId"] = handle.RunId; startedAt = EditorApplication.timeSinceStartup;
        write();
    } catch (Exception error) {
        queueState = "failed"; queueError = error.ToString(); write(); detach();
    }
};
AppDomain.CurrentDomain.SetData(Marker, DateTime.UtcNow.ToString("o"));
VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.CaseSampled += onSampled;
VrBattlegrounds.DevTools.BotCombatStand.BotCombatStand.CaseFinished += onFinished;
write();
EditorApplication.update += tick;
return new { executionCompleted = true, started = true, combos = combos.Select(c => c.id + ":" + string.Join("+", c.cases.Select(x => x.ToString()).ToArray())).ToArray(),
    caseSeconds = CaseSeconds, queue = Root + "/queue.json",
    warmupReport = standStatus["reportPath"], expectedSeconds = totalCases * CaseSeconds };
