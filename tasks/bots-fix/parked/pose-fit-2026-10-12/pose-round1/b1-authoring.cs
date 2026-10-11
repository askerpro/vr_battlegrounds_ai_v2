// B1 MUTATION (Edit mode), раунд pose-fit: пересборка NPC ClipBody только для MEF и MEF Black (Cyborg исключён), на ClipBody
// генерация добавляет BotSupportHandIK (опорная рука LimbIK); socket правой руки — запись хвата по цепочке префабов
// эталона (calibration.json gripEntries: какая запись выбирается у источника и ClipBody, primary и support).
// После повторной сборки удаляются осиротевшие сгенерированные ассеты Cyborg (ClipBody, ClipAvatar, пустая папка
// ClipAvatars) — только если библиотека на них больше не ссылается; пути — staleCyborg в result.json.
// Ожидаемые счётчики: models 2 / sockets 4 / motions 18 / aims 24.
// Ниже — исходное описание B1 раунда III (сохранено):
// B1 MUTATION (Edit mode): пересборка NPC ClipBody с StampAssetIdentity + saved readback.
// Раунд III наведения: (1) знак — BotAim_Up поднимает ствол, g > 0 у всех 36 aims (иначе mismatch);
// (2) фазовые таблицы aims[] (phaseBasePitch/phaseHeading по AimPhaseBins бинам; бины ограничены медианой ±8°,
//     маска phaseClippedBins; у необрезанных таблиц среднее бинов = basePitch; число обрезанных бинов по combo/state);
// (3) детерминизм aim-клипов с ПЕРВОЙ сборки: проба BotCombatSetup.BuildAimClips в чистой временной папке
// Assets/__BotAimCreationProbe (создание с нуля, затем повтор) — байты создания = байты повтора, и после замены GUID
// своего Ref равны реальным клипам Assets/Resources/Bots/Aim; папка удаляется в finally (проверяется отсутствие).
// Остальное — как в раунде II:
// BotCombatSetup.Build генерирует aim-слой BotPistol/BotRifle (Additive, маска Body,
// iKPass=false, 1D BlendTree AimPitch по BotAim_Down/Ref/Up в Assets/Resources/Bots/Aim/); BuildCalibration пишет
// aims[] 36 с pitchGain g. Затем ПОВТОР BotCombatSetup.Build + BuildCalibration (отчёт в calibration-repeat/) обязан
// дать побайтно те же контроллеры x3, aim-клипы x3, маску и BotCombatAssets.asset. Read-only preview-проверка:
// при AimPitch=0 слой не меняет позу (дельта локальных поворотов костей < 0.01° против веса слоя 0); справочно —
// эффект AimPitch=+20 (корпус/таз/стопы/кисть, утечка вне маски).
// НЕ ПОВТОРЯТЬ после таймаута: сначала проверить durable state (result.json/composition.json под Root,
// mtime трёх ClipBody). При существующем result.json/composition.json payload сам отказывает и пишет blocked-*.json.
// Основа — tasks/bots-fix/tools/armed-authoring-package.cs (ticket402), изменены только Root и identity-a.json.
const string Root = "tasks/bots-fix/reports/pose-round1";
var resultPath = Root + "/result.json";
var stage = "preflight";
var errors = new List<string>();
var unknown = new List<string>();
var mismatches = new List<string>();
var variants = new List<object>();
var controllers = new List<object>();
var motions = new List<object>();
var definitions = new List<object>();
var identity = new List<Dictionary<string, object>>();
Dictionary<string, object> composition = null, calibration = null;
string setup = null;
bool authoringCompleted = false;
bool? calibrationPassed = null, calibrationBuilt = null;
int previewBefore = UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount;
string activeBefore = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
int previewAfter = previewBefore;
string activeAfter = activeBefore;
int? modelCount = null, socketCount = null, motionCount = null, aimCount = null;
const string LibraryPath = "Assets/Resources/Bots/BotCombatAssets.asset";
Dictionary<string, object> calibrationRepeat = null;
string libraryShaFirst = null, libraryShaRepeat = null;
bool? repeatBytesEqual = null, calibrationReportsEqual = null;
var aimRows = new List<object>();
string setupRepeat = null;
var generatedPaths = new[] {
    "Assets/Resources/Bots/BotPistol.controller", "Assets/Resources/Bots/BotRifle.controller", "Assets/Resources/Bots/BotUnarmed.controller",
    "Assets/Resources/Bots/Aim/BotAim_Ref.anim", "Assets/Resources/Bots/Aim/BotAim_Up.anim", "Assets/Resources/Bots/Aim/BotAim_Down.anim",
    "Assets/Resources/Bots/Aim/BotAim_Body.mask", LibraryPath };
Dictionary<string, string> generatedFirst = null, generatedRepeat = null;
bool? generatedBytesEqual = null;
List<string> layoutErrors = null;
var aimClips = new List<object>();
var layerIdentity = new List<object>();
float? identityMaxDeg = null, maskLeakMaxDeg = null;
int gainNegative = 0, gainPositive = 0, nonlinearOver10 = 0;
// Раунд III: проба первого создания aim-клипов и фазовые таблицы.
const string ProbeFolder = "Assets/__BotAimCreationProbe";
var aimClipNames = new[] { "BotAim_Ref.anim", "BotAim_Up.anim", "BotAim_Down.anim" };
Dictionary<string, string> probeCreated = null, probeRepeat = null, probeNormalized = null, realNormalized = null;
bool? probeCreateEqualsRepeat = null, probeEqualsReal = null, probeFolderRemoved = null;
string probeError = null;
int phaseTablesComplete = 0, phaseTablesMissing = 0;
float? phaseMeanMaxError = null;
// Обрезанные бины (отдача клипа, отклонение от медианы > 8°): модель/оружие -> состояние -> число бинов.
var clippedByCombo = new SortedDictionary<string, SortedDictionary<string, int>>(StringComparer.Ordinal);
// pose-fit: удаление осиротевших ассетов Cyborg и наличие BotSupportHandIK на ClipBody.
var staleCyborg = new List<object>();
var staleCyborgPaths = new[] {
    "Assets/Resources/Bots/ClipBodies/PlayerControllersCyborgAvatar_ClipBody.prefab",
    "Assets/Resources/Bots/ClipAvatars/PlayerControllersCyborgAvatar_ClipAvatar.asset" };
const string ClipAvatarsFolder = "Assets/Resources/Bots/ClipAvatars";
int supportComponents = 0;
var bodies = new[] {
    "Assets/Resources/Bots/ClipBodies/Optimized_MEF_Player_Black_ClipBody.prefab",
    "Assets/Resources/Bots/ClipBodies/Optimized_MEF_Player_ClipBody.prefab" };
Func<UnityEngine.Object, string> assetPath = value => value == null ? null : AssetDatabase.GetAssetPath(value);
Func<UnityEngine.Object, string> assetGuid = value => value == null ? null : AssetDatabase.AssetPathToGUID(assetPath(value));
Func<float, bool> finite = value => !float.IsNaN(value) && !float.IsInfinity(value);
Func<byte[], string> sha = bytes => {
    using (var h = System.Security.Cryptography.SHA256.Create())
        return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
};
Func<Mirror.NetworkIdentity, uint?> savedAssetId = id => {
    if (id == null) return null;
    // Public getter может лениво назначить assetId. Читаем сохранённое поле без такого побочного действия.
    var property = new SerializedObject(id).FindProperty("_assetId");
    if (property == null) { unknown.Add("NetworkIdentity serialized _assetId unavailable."); return null; }
    return (uint)property.longValue;
};
// --- Общий read-only снимок идентичности (тот же код в B2/B7) ---
Func<string, Dictionary<string, object>> identitySnapshot = path => {
    var row = new Dictionary<string, object> { { "path", path } };
    if (!System.IO.File.Exists(path)) { row["exists"] = false; return row; }
    var bytes = System.IO.File.ReadAllBytes(path);
    row["exists"] = true; row["sha256"] = sha(bytes); row["bytes"] = bytes.Length;
    var text = System.Text.Encoding.UTF8.GetString(bytes);
    row["crlf"] = text.Contains("\r\n");
    var lines = text.Replace("\r\n", "\n").Split('\n');
    var uxrIds = new List<string>();
    var assetIdValues = new List<string>();
    int prefabGuidOverrides = 0, isInPrefabOverrides = 0;
    string header = null, target = null;
    for (int i = 0; i < lines.Length; i++) {
        string line = lines[i], trimmed = lines[i].Trim();
        if (line.StartsWith("--- ")) header = trimmed;
        if (trimmed.StartsWith("- target:")) target = trimmed;
        if (trimmed == "propertyPath: __prefabGuid") prefabGuidOverrides++;
        if (trimmed == "propertyPath: __isInPrefab") isInPrefabOverrides++;
        string next = i + 1 < lines.Length ? lines[i + 1].Trim() : "";
        if (trimmed == "propertyPath: _assetId") assetIdValues.Add(target + " | " + next);
        else if (trimmed.StartsWith("_assetId:")) assetIdValues.Add(header + " | " + trimmed);
        if (!line.Contains("_uxrUniqueId")) continue;
        uxrIds.Add(trimmed.StartsWith("propertyPath:") ? target + " | " + trimmed + " | " + next : header + " | " + trimmed);
    }
    row["uxrUniqueIdLineCount"] = uxrIds.Count;
    row["uxrUniqueIdLinesSha256"] = sha(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", uxrIds.ToArray())));
    row["uxrUniqueIdLines"] = uxrIds;
    row["prefabGuidOverrides"] = prefabGuidOverrides; row["isInPrefabOverrides"] = isInPrefabOverrides;
    row["assetIdFileValues"] = assetIdValues;
    var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
    if (go == null) { row["loaded"] = false; return row; }
    row["loaded"] = true;
    var onDisk = VrBattlegrounds.EditorTools.VersionControl.UxrUniqueIdPersister.ReadIdsOnDisk(path);
    var loadedIds = new List<string>();
    int flagMismatches = 0, unpersisted = 0, emptyIds = 0, dirty = EditorUtility.IsDirty(go) ? 1 : 0;
    foreach (var component in go.GetComponentsInChildren<UltimateXR.Core.Components.UxrComponent>(true)) {
        var id = component.UniqueId;
        loadedIds.Add(AnimationUtility.CalculateTransformPath(component.transform, go.transform) + ":" + component.GetType().Name + "=" + id.ToString());
        bool inPrefab; string prefabGuid;
        if (VrBattlegrounds.EditorTools.VersionControl.UxrUniqueIdPersister.HasMismatch(component, out inPrefab, out prefabGuid)) flagMismatches++;
        if (id == Guid.Empty) emptyIds++; else if (!onDisk.Contains(id.ToString())) unpersisted++;
        if (EditorUtility.IsDirty(component)) dirty++;
    }
    loadedIds.Sort(StringComparer.Ordinal);
    var networkIdentity = go.GetComponent<Mirror.NetworkIdentity>();
    uint? serializedAssetId = null;
    if (networkIdentity != null) {
        var property = new SerializedObject(networkIdentity).FindProperty("_assetId");
        if (property != null) serializedAssetId = property.uintValue;
        if (EditorUtility.IsDirty(networkIdentity)) dirty++;
    }
    uint expectedAssetId = Mirror.NetworkIdentity.AssetGuidToUint(new Guid(AssetDatabase.AssetPathToGUID(path)));
    row["loadedUxrComponents"] = loadedIds.Count;
    row["loadedIdsSha256"] = sha(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", loadedIds.ToArray())));
    row["uxrFlagMismatches"] = flagMismatches; row["unpersistedIds"] = unpersisted; row["emptyIds"] = emptyIds;
    row["dirtyObjects"] = dirty;
    row["serializedAssetId"] = serializedAssetId; row["expectedAssetId"] = expectedAssetId;
    row["assetIdCanonical"] = serializedAssetId.HasValue && serializedAssetId.Value == expectedAssetId;
    return row;
};
try {
    if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
        throw new InvalidOperationException("Authoring требует idle EditMode.");
    if (System.IO.File.Exists(resultPath) || System.IO.File.Exists(Root + "/composition.json")) {
        // Сохраняем только новый отказ, не затирая прежние durable result/composition/calibration.
        resultPath = Root + "/blocked-authoring-" + DateTime.UtcNow.Ticks + ".json";
        throw new InvalidOperationException("A уже выполнялся в этом output root; payload не повторять.");
    }
    System.IO.Directory.CreateDirectory(Root);
    stage = "setup";
    setup = VrBattlegrounds.EditorTools.BotCombatSetup.Build();
    stage = "composition";
    composition = VrBattlegrounds.EditorTools.BotClipComposition.Build(Root);
    if (!composition.ContainsKey("passed") || !(bool)composition["passed"])
        mismatches.Add("Composition not passed; calibration/readback not executed.");
    else {
        stage = "calibration";
        calibration = VrBattlegrounds.EditorTools.BotClipComposition.BuildCalibration(Root);
        if (calibration.ContainsKey("passed")) calibrationPassed = Convert.ToBoolean(calibration["passed"]);
        if (calibration.ContainsKey("candidateBuilt")) calibrationBuilt = Convert.ToBoolean(calibration["candidateBuilt"]);
        stage = "setup-calibration-repeat";
        // Детерминизм: те же входы -> те же байты контроллеров, aim-клипов, маски и asset (без Time/случайности).
        Func<Dictionary<string, string>> hashGenerated = () => generatedPaths.ToDictionary(path => path,
            path => System.IO.File.Exists(path) ? sha(System.IO.File.ReadAllBytes(path)) : null);
        generatedFirst = hashGenerated();
        libraryShaFirst = generatedFirst[LibraryPath];
        setupRepeat = VrBattlegrounds.EditorTools.BotCombatSetup.Build();
        calibrationRepeat = VrBattlegrounds.EditorTools.BotClipComposition.BuildCalibration(Root + "/calibration-repeat");
        generatedRepeat = hashGenerated();
        libraryShaRepeat = generatedRepeat[LibraryPath];
        repeatBytesEqual = libraryShaFirst == libraryShaRepeat;
        generatedBytesEqual = generatedPaths.All(path => generatedFirst[path] != null && generatedFirst[path] == generatedRepeat[path]);
        foreach (var path in generatedPaths)
            if (generatedFirst[path] == null || generatedFirst[path] != generatedRepeat[path])
                mismatches.Add("Повторная сборка Setup+Calibration изменила/не создала " + path + ": " + generatedFirst[path] + " -> " + generatedRepeat[path]);
        stage = "aim-clip-creation-probe";
        // Первое создание в чистой папке тем же генератором, что у Build; затем повтор. GUID Ref пробы и реального Ref
        // заменяются одной меткой: остальные байты (кривые, настройки, m_Name) обязаны совпасть.
        Func<string, string, string> normalized = (path, refGuid) => {
            var text = System.IO.File.ReadAllText(path).Replace("\r\n", "\n");
            return sha(System.Text.Encoding.UTF8.GetBytes(string.IsNullOrEmpty(refGuid) ? text : text.Replace(refGuid, "<REF>")));
        };
        try {
            if (AssetDatabase.IsValidFolder(ProbeFolder) && !AssetDatabase.DeleteAsset(ProbeFolder))
                throw new InvalidOperationException("Не удалось удалить остаток пробы " + ProbeFolder);
            if (string.IsNullOrEmpty(AssetDatabase.CreateFolder("Assets", "__BotAimCreationProbe")))
                throw new InvalidOperationException("Не удалось создать " + ProbeFolder);
            VrBattlegrounds.EditorTools.BotCombatSetup.BuildAimClips(ProbeFolder);
            probeCreated = aimClipNames.ToDictionary(n => n, n => sha(System.IO.File.ReadAllBytes(ProbeFolder + "/" + n)));
            string probeRef = AssetDatabase.AssetPathToGUID(ProbeFolder + "/BotAim_Ref.anim");
            probeNormalized = aimClipNames.ToDictionary(n => n, n => normalized(ProbeFolder + "/" + n, probeRef));
            VrBattlegrounds.EditorTools.BotCombatSetup.BuildAimClips(ProbeFolder);
            probeRepeat = aimClipNames.ToDictionary(n => n, n => sha(System.IO.File.ReadAllBytes(ProbeFolder + "/" + n)));
            probeCreateEqualsRepeat = aimClipNames.All(n => probeCreated[n] == probeRepeat[n]);
            string realRef = AssetDatabase.AssetPathToGUID("Assets/Resources/Bots/Aim/BotAim_Ref.anim");
            realNormalized = aimClipNames.ToDictionary(n => n, n => normalized("Assets/Resources/Bots/Aim/" + n, realRef));
            probeEqualsReal = aimClipNames.All(n => probeNormalized[n] == realNormalized[n]);
            if (probeCreateEqualsRepeat != true)
                mismatches.Add("Первое создание aim-клипов (проба) не равно повтору: " + string.Join(", ",
                    aimClipNames.Where(n => probeCreated[n] != probeRepeat[n]).ToArray()));
            if (probeEqualsReal != true)
                mismatches.Add("Проба первого создания не равна реальным aim-клипам (после нормализации GUID Ref): " + string.Join(", ",
                    aimClipNames.Where(n => probeNormalized[n] != realNormalized[n]).ToArray()));
        } catch (Exception probe) { probeError = probe.Message; mismatches.Add("Проба создания aim-клипов: " + probe.Message); }
        finally {
            if (AssetDatabase.IsValidFolder(ProbeFolder)) AssetDatabase.DeleteAsset(ProbeFolder);
            probeFolderRemoved = !AssetDatabase.IsValidFolder(ProbeFolder) && !System.IO.Directory.Exists(ProbeFolder) &&
                                 !System.IO.File.Exists(ProbeFolder + ".meta");
            if (probeFolderRemoved != true) mismatches.Add("Остаток пробы " + ProbeFolder);
        }
        stage = "stale-cyborg";
        {
            var current = AssetDatabase.LoadAssetAtPath<VrBattlegrounds.Bots.BotCombatAssets>(LibraryPath);
            var referenced = new HashSet<UnityEngine.Object>();
            foreach (var b in current.clipBodies ?? new VrBattlegrounds.Bots.BotCombatAssets.ClipBodyBinding[0]) if (b != null) { referenced.Add(b.prefab); referenced.Add(b.source); }
            foreach (var b in current.clipAvatars ?? new VrBattlegrounds.Bots.BotCombatAssets.ClipAvatarBinding[0]) if (b != null) referenced.Add(b.clips);
            foreach (var path in staleCyborgPaths) {
                var asset = AssetDatabase.LoadMainAssetAtPath(path);
                if (asset == null) { staleCyborg.Add(new { path, existed = false, deleted = false }); continue; }
                var subAssets = AssetDatabase.LoadAllAssetsAtPath(path);
                if (subAssets.Any(a => a != null && referenced.Contains(a))) {
                    mismatches.Add("Осиротевший ассет Cyborg всё ещё в библиотеке: " + path);
                    staleCyborg.Add(new { path, existed = true, deleted = false, referencedByLibrary = true });
                    continue;
                }
                string guid = AssetDatabase.AssetPathToGUID(path);
                bool deleted = AssetDatabase.DeleteAsset(path);
                if (!deleted || System.IO.File.Exists(path) || System.IO.File.Exists(path + ".meta")) mismatches.Add("Не удалён " + path);
                staleCyborg.Add(new { path, guid, existed = true, deleted });
            }
            if (AssetDatabase.IsValidFolder(ClipAvatarsFolder)) {
                bool empty = AssetDatabase.FindAssets("", new[] { ClipAvatarsFolder }).Length == 0 &&
                             System.IO.Directory.GetFiles(ClipAvatarsFolder).Length == 0;
                bool deleted = empty && AssetDatabase.DeleteAsset(ClipAvatarsFolder);
                staleCyborg.Add(new { path = ClipAvatarsFolder, folder = true, empty, deleted });
            }
        }
        var firstReport = Root + "/calibration.json";
        var repeatReport = Root + "/calibration-repeat/calibration.json";
        if (System.IO.File.Exists(firstReport) && System.IO.File.Exists(repeatReport))
            calibrationReportsEqual = sha(System.IO.File.ReadAllBytes(firstReport)) == sha(System.IO.File.ReadAllBytes(repeatReport));
        authoringCompleted = true;
        stage = "saved-readback";
        var library = AssetDatabase.LoadAssetAtPath<VrBattlegrounds.Bots.BotCombatAssets>("Assets/Resources/Bots/BotCombatAssets.asset");
        if (library == null) throw new InvalidOperationException("Saved BotCombatAssets отсутствует.");
        modelCount = library.clipBodies == null ? 0 : library.clipBodies.Length;
        socketCount = library.sockets == null ? 0 : library.sockets.Length;
        motionCount = library.motions == null ? 0 : library.motions.Length;
        aimCount = library.aims == null ? 0 : library.aims.Length;
        if (modelCount != 2 || socketCount != 4 || motionCount != 18 || aimCount != 24)
            mismatches.Add("Expected models2/sockets4/motions18/aims24; actual " + modelCount + "/" + socketCount + "/" + motionCount + "/" + aimCount);
        var expectedSources = new Dictionary<string, string>(StringComparer.Ordinal) {
            { "310c90ae0d962f344a1eddbe48a429c9", "Assets/Prefabs/Player/Optimized_MEF_Player_Black.prefab" },
            { "b8146035f3f436a4799882b3a0d1dd45", "Assets/Prefabs/Player/Optimized_MEF_Player.prefab" }
        };
        var sourceGuids = new HashSet<string>(StringComparer.Ordinal);
        var modelSources = new HashSet<GameObject>();
        var generatedAssetIds = new HashSet<uint>();
        foreach (var model in library.clipBodies ?? new VrBattlegrounds.Bots.BotCombatAssets.ClipBodyBinding[0]) {
            if (model == null || model.source == null || model.prefab == null) { mismatches.Add("Incomplete clip body binding."); continue; }
            if (!modelSources.Add(model.source)) mismatches.Add("Duplicate source: " + assetPath(model.source));
            string actualSourceGuid = assetGuid(model.source);
            string expectedSourcePath;
            if (string.IsNullOrEmpty(actualSourceGuid) || !sourceGuids.Add(actualSourceGuid) ||
                !expectedSources.TryGetValue(actualSourceGuid, out expectedSourcePath) || expectedSourcePath != assetPath(model.source))
                mismatches.Add("Unexpected/duplicate exact source mapping: " + actualSourceGuid + "/" + assetPath(model.source));
            if (model.sourceGuid != actualSourceGuid)
                mismatches.Add("Declared sourceGuid differs from saved source identity: " + assetPath(model.source));
            var bodyPath = assetPath(model.prefab);
            if (!bodies.Contains(bodyPath)) mismatches.Add("Unexpected generated body path: " + bodyPath);
            var sourceIdentity = model.source.GetComponent<Mirror.NetworkIdentity>();
            var sourceId = savedAssetId(sourceIdentity);
            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(bodyPath);
            var savedIdentity = saved == null ? null : saved.GetComponent<Mirror.NetworkIdentity>();
            var identityId = savedAssetId(savedIdentity);
            if (identityId.HasValue && !generatedAssetIds.Add(identityId.Value)) mismatches.Add("Generated variants share assetId: " + identityId.Value);
            bool? separateAsset = identityId.HasValue && sourceId.HasValue ? (bool?)(identityId.Value != 0 && identityId.Value != sourceId.Value) : null;
            int savedIdentities = saved == null ? 0 : saved.GetComponentsInChildren<Mirror.NetworkIdentity>(true).Length;
            var savedClip = saved == null ? null : saved.GetComponent<VrBattlegrounds.Bots.BotClipBody>();
            int rootBehaviours = saved == null ? 0 : saved.GetComponents<Mirror.NetworkBehaviour>().Length;
            bool networkClip = savedClip != null;
            if (savedIdentity == null || savedIdentity.sceneId != 0 || savedIdentities != 1 || !networkClip || separateAsset == false)
                mismatches.Add("Saved identity/clip metadata mismatch: " + bodyPath);
            if (!separateAsset.HasValue) unknown.Add("Separate saved assetId comparison unknown: " + bodyPath);
            GameObject reopened = null;
            try {
                // Isolation scene выгружается без SaveAsPrefabAsset: проверяются сохранённые generated данные.
                reopened = PrefabUtility.LoadPrefabContents(bodyPath);
                var reopenedIdentity = reopened.GetComponent<Mirror.NetworkIdentity>();
                var clip = reopened.GetComponent<VrBattlegrounds.Bots.BotClipBody>();
                var human = reopened.GetComponentsInChildren<Animator>(true).Where(a => a.enabled && a.avatar != null && a.avatar.isHuman).ToArray();
                int sdkControllers = reopened.GetComponentsInChildren<UltimateXR.Avatar.Controllers.UxrStandardAvatarController>(true).Count(c => c.enabled);
                int ikSolvers = reopened.GetComponentsInChildren<UltimateXR.Animation.IK.UxrIKSolver>(true).Count(c => c.enabled);
                int tracking = reopened.GetComponentsInChildren<UltimateXR.Devices.UxrTrackingDevice>(true).Count(c => c.enabled);
                int input = reopened.GetComponentsInChildren<UltimateXR.Devices.UxrControllerInput>(true).Count(c => c.enabled);
                int grabbers = reopened.GetComponentsInChildren<UltimateXR.Manipulation.UxrGrabber>(true).Count(c => c.enabled);
                var support = reopened.GetComponentsInChildren<VrBattlegrounds.Bots.BotSupportHandIK>(true);
                bool supportOk = support.Length == 1 && support[0].gameObject == reopened && support[0].enabled;
                if (supportOk) supportComponents++;
                if (!supportOk) mismatches.Add("ClipBody без единственного включённого BotSupportHandIK на корне: " + bodyPath);
                var transport = reopened.GetComponentsInChildren<Mirror.NetworkTransformBase>(true).Select(t => new {
                    name = t.name, enabled = t.enabled, direction = t.syncDirection.ToString(),
                    rootTarget = (t.target != null ? t.target : t.transform) == reopened.transform
                }).ToArray();
                if (reopenedIdentity == null || clip == null || human.Length != 1 || sdkControllers != 0 || ikSolvers != 0 || tracking != 0 || input != 0 || grabbers != 0 ||
                    (human.Length == 1 && human[0].applyRootMotion) ||
                    transport.Any(t => t.enabled && (!t.rootTarget || t.direction != Mirror.SyncDirection.ServerToClient.ToString())))
                    mismatches.Add("Reopened pose/root writer metadata mismatch: " + bodyPath);
                variants.Add(new {
                    sourcePath = assetPath(model.source), sourceGuid = actualSourceGuid, declaredSourceGuid = model.sourceGuid, sourceAssetId = sourceId,
                    generatedPath = bodyPath, generatedGuid = assetGuid(saved), savedAssetId = identityId, separateAsset,
                    savedSceneId = savedIdentity == null ? (ulong?)null : savedIdentity.sceneId, savedIdentities,
                    savedRootNetworkBehaviours = rootBehaviours, savedNetworkClip = networkClip,
                    reopenedRootNetworkBehaviours = reopened.GetComponents<Mirror.NetworkBehaviour>().Select(b => b.GetType().FullName).ToArray(),
                    enabledHumanAnimators = human.Length, enabledSdkControllers = sdkControllers, enabledIkSolvers = ikSolvers,
                    enabledTracking = tracking, enabledInput = input, enabledGrabbers = grabbers, supportHandIK = support.Length,
                    rootMotion = human.Length == 1 ? (bool?)human[0].applyRootMotion : null, transport
                });
            } finally { if (reopened != null) PrefabUtility.UnloadPrefabContents(reopened); }
        }
        if (!sourceGuids.SetEquals(expectedSources.Keys)) mismatches.Add("Exact approved source GUID set is incomplete.");
        stage = "controller-layout";
        // Контракт раскладки (тот же валидатор, что вызывают Setup/Composition): pistol/rifle [Base, Aim], unarmed [Base].
        layoutErrors = VrBattlegrounds.EditorTools.BotClipComposition.ValidateControllerLayout(library);
        foreach (var layoutError in layoutErrors) mismatches.Add("Layout: " + layoutError);
        foreach (var clipPath in new[] { "Assets/Resources/Bots/Aim/BotAim_Ref.anim", "Assets/Resources/Bots/Aim/BotAim_Up.anim", "Assets/Resources/Bots/Aim/BotAim_Down.anim" }) {
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            if (clip == null) { mismatches.Add("Нет aim-клипа " + clipPath); continue; }
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            var curves = AnimationUtility.GetCurveBindings(clip).Select(b => new { b.propertyName, value = AnimationUtility.GetEditorCurve(clip, b).Evaluate(0f) }).ToArray();
            if (!clip.isHumanMotion || !settings.hasAdditiveReferencePose || settings.additiveReferencePoseClip == null ||
                assetPath(settings.additiveReferencePoseClip) != "Assets/Resources/Bots/Aim/BotAim_Ref.anim" || curves.Length != 9)
                mismatches.Add("Aim-клип не humanoid/без reference pose BotAim_Ref/не 9 мышц: " + clipPath);
            aimClips.Add(new { path = clipPath, guid = assetGuid(clip), clip.isHumanMotion, settings.hasAdditiveReferencePose,
                reference = assetPath(settings.additiveReferencePoseClip), settings.additiveReferencePoseTime, settings.loopTime, curves });
        }
        var expectedControllers = new[] { library.pistol, library.rifle, library.unarmed };
        for (int i = 0; i < expectedControllers.Length; i++) {
            var controller = expectedControllers[i] as UnityEditor.Animations.AnimatorController;
            var expectedStates = i == 2 ? new[] { "Idle", "Move" } : new[] { "Idle", "Move", "Back", "Left", "Right", "Shoot" };
            if (controller == null || controller.layers.Length != (i == 2 ? 1 : 2)) { mismatches.Add("Expected [Base, Aim] / [Base] controller " + i); continue; }
            var states = controller.layers[0].stateMachine.states;
            var rate = controller.parameters.FirstOrDefault(p => p.name == VrBattlegrounds.Bots.BotCombatAssets.MotionRateParameter);
            if (controller.layers.Any(l => l.iKPass) || states.Length != expectedStates.Length || expectedStates.Any(s => states.Count(c => c.state.name == s) != 1) ||
                rate == null || rate.type != AnimatorControllerParameterType.Float)
                mismatches.Add("Controller required states/rate/IK metadata mismatch: " + assetPath(controller));
            foreach (var child in states) {
                bool moving = child.state.name != "Idle" && child.state.name != "Shoot";
                if (child.state.motion == null || child.state.iKOnFeet || child.state.speedParameterActive != moving ||
                    (moving && child.state.speedParameter != VrBattlegrounds.Bots.BotCombatAssets.MotionRateParameter))
                    mismatches.Add("State motion/rate metadata mismatch: " + controller.name + "/" + child.state.name);
            }
            // states[].motion используется offline-анализом B как карта clip -> state.
            controllers.Add(new { path = assetPath(controller), guid = assetGuid(controller), ikPass = controller.layers.Any(l => l.iKPass),
                layers = controller.layers.Select(l => new { l.name, blending = l.blendingMode.ToString(), l.defaultWeight, l.iKPass,
                    mask = assetPath(l.avatarMask),
                    maskParts = l.avatarMask == null ? null : Enumerable.Range(0, (int)AvatarMaskBodyPart.LastBodyPart)
                        .Where(part => l.avatarMask.GetHumanoidBodyPartActive((AvatarMaskBodyPart)part)).Select(part => ((AvatarMaskBodyPart)part).ToString()).ToArray(),
                    states = l.stateMachine.states.Select(c => c.state.name).ToArray() }).ToArray(),
                parameters = controller.parameters.Select(p => p.name + ":" + p.type).ToArray(),
                states = states.Select(c => new { name = c.state.name, motion = assetPath(c.state.motion), motionName = c.state.motion != null ? c.state.motion.name : null,
                    c.state.speedParameterActive, c.state.speedParameter, c.state.iKOnFeet }).ToArray() });
        }
        var motionKeys = new HashSet<string>();
        foreach (var motion in library.motions ?? new VrBattlegrounds.Bots.BotCombatAssets.ClipMotionBinding[0]) {
            if (motion == null) { mismatches.Add("Null motion binding."); continue; }
            string key = assetGuid(motion.source) + "/" + assetGuid(motion.controller) + "/" + motion.state;
            if (!motionKeys.Add(key) || !finite(motion.metersPerSecond) || motion.metersPerSecond <= .01f)
                mismatches.Add("Duplicate/nonpositive/nonfinite native motion: " + key);
            motions.Add(new { source = assetPath(motion.source), controller = assetPath(motion.controller), motion.state, motion.metersPerSecond });
        }
        foreach (var model in library.clipBodies ?? new VrBattlegrounds.Bots.BotCombatAssets.ClipBodyBinding[0]) {
            if (model == null || model.source == null) continue;
            for (int i = 0; i < expectedControllers.Length; i++) foreach (var state in i == 2 ? new[] { "Move" } : new[] { "Move", "Back", "Left", "Right" }) {
                float speed = library.GetMotionSpeed(model.source, expectedControllers[i], state);
                if (!finite(speed) || speed <= .01f) mismatches.Add("Required native motion missing: " + assetPath(model.source) + "/" + i + "/" + state);
            }
        }
        stage = "aims-readback";
        // aims[]: уникальная тройка модель/контроллер/состояние, конечные и правдоподобные значения.
        var aimKeys = new HashSet<string>();
        foreach (var aim in library.aims ?? new VrBattlegrounds.Bots.BotCombatAssets.ClipAimBinding[0]) {
            if (aim == null || aim.source == null || aim.controller == null || string.IsNullOrEmpty(aim.state)) { mismatches.Add("Incomplete aim binding."); continue; }
            string key = assetGuid(aim.source) + "/" + assetGuid(aim.controller) + "/" + aim.state;
            if (!aimKeys.Add(key) || !finite(aim.lever) || !finite(aim.heading) || !finite(aim.basePitch) ||
                Mathf.Abs(aim.lever) > 1f || Mathf.Abs(aim.heading) > 90f || Mathf.Abs(aim.basePitch) > 90f)
                mismatches.Add("Duplicate/nonfinite/implausible aim: " + key);
            // g: конечен и не меньше порога BotAim.MinPitchGain (иначе runtime выключит pitch); знак измерен.
            if (!finite(aim.pitchGain) || Mathf.Abs(aim.pitchGain) < VrBattlegrounds.Bots.BotAim.MinPitchGain || !finite(aim.pitchNonlinearity))
                mismatches.Add("Aim pitchGain nonfinite/near zero: " + key + " g=" + aim.pitchGain);
            if (aim.pitchGain < 0f) gainNegative++; else if (aim.pitchGain > 0f) gainPositive++;
            // Раунд III: Up поднимает ствол — g > 0 у всех троек.
            if (!(aim.pitchGain > 0f)) mismatches.Add("Aim g <= 0 (Up не поднимает ствол): " + key + " g=" + aim.pitchGain);
            if (aim.pitchNonlinearity > .1f) nonlinearOver10++;
            // Фазовые таблицы: полны, конечны, правдоподобны; среднее бинов (равные бины) = basePitch.
            float[] phasePitch = aim.phaseBasePitch ?? new float[0], phaseHeading = aim.phaseHeading ?? new float[0];
            if (!aim.HasPhases || phasePitch.Any(v => !finite(v) || Mathf.Abs(v) > 90f) || phaseHeading.Any(v => !finite(v) || Mathf.Abs(v) > 90f))
            { phaseTablesMissing++; mismatches.Add("Фазовые таблицы неполны/невалидны: " + key); }
            else {
                phaseTablesComplete++;
                int clippedBins = VrBattlegrounds.Bots.BotCombatAssets.CountPhaseBits(aim.phaseClippedBins);
                // Ограничение вне медианы ±8° меняет среднее: сверка средней только у необрезанных таблиц.
                if (clippedBins == 0) {
                    float meanError = Mathf.Abs(phasePitch.Average() - aim.basePitch);
                    phaseMeanMaxError = Mathf.Max(phaseMeanMaxError ?? 0f, meanError);
                    if (meanError > .01f) mismatches.Add("Среднее phaseBasePitch != basePitch: " + key + " " + meanError);
                }
                string comboKey = (aim.source != null ? aim.source.name : "null") + "/" + (aim.controller != null ? aim.controller.name : "null");
                SortedDictionary<string, int> byState;
                if (!clippedByCombo.TryGetValue(comboKey, out byState)) clippedByCombo[comboKey] = byState = new SortedDictionary<string, int>(StringComparer.Ordinal);
                byState[aim.state] = clippedBins;
            }
            aimRows.Add(new { source = assetPath(aim.source), controller = assetPath(aim.controller), aim.state, aim.lever, aim.heading, aim.basePitch,
                aim.pitchGain, aim.pitchNonlinearity, phaseBasePitch = phasePitch, phaseHeading,
                aim.phaseClippedBins, phaseClippedCount = VrBattlegrounds.Bots.BotCombatAssets.CountPhaseBits(aim.phaseClippedBins),
                phasePitchSpan = phasePitch.Length > 0 ? phasePitch.Max() - phasePitch.Min() : (float?)null,
                phaseHeadingSpan = phaseHeading.Length > 0 ? phaseHeading.Max() - phaseHeading.Min() : (float?)null });
        }
        var aimStates = new[] { "Idle", "Move", "Back", "Left", "Right", "Shoot" };
        foreach (var model in library.clipBodies ?? new VrBattlegrounds.Bots.BotCombatAssets.ClipBodyBinding[0]) {
            if (model == null || model.source == null) continue;
            foreach (var controller in new[] { library.pistol, library.rifle }) foreach (var state in aimStates) {
                VrBattlegrounds.Bots.BotCombatAssets.ClipAimBinding bySource, byBody;
                if (!library.TryGetAim(model.source, controller, state, out bySource) ||
                    !library.TryGetAim(model.prefab, controller, state, out byBody) || bySource != byBody)
                    mismatches.Add("Required aim missing/unresolved: " + assetPath(model.source) + "/" + (controller != null ? controller.name : "null") + "/" + state);
            }
        }
        stage = "aim-layer-identity";
        // Read-only preview: слой Aim (вес 1) при AimPitch=0 против веса 0 — тождество позы (гейт < 0.01°).
        // Справочно AimPitch=+20 против веса 0: поворот груди/таза/головы и сдвиг стоп/кисти в системе корня,
        // утечка вне маски — локальные повороты костей, кроме Hips/Spine/Chest/UpperChest.
        var identityScene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
        try {
            var bodyBones = new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest };
            var aimStatesForIdentity = new[] { "Idle", "Move", "Back", "Left", "Right", "Shoot" };
            float worstIdentity = 0f, worstLeak = 0f;
            foreach (var model in library.clipBodies ?? new VrBattlegrounds.Bots.BotCombatAssets.ClipBodyBinding[0]) {
                if (model == null || model.prefab == null) continue;
                GameObject npc = null;
                try {
                    npc = (GameObject)PrefabUtility.InstantiatePrefab(model.prefab, identityScene);
                    var animator = npc.GetComponentsInChildren<Animator>(true).Single(a => a.enabled && a.avatar != null && a.avatar.isHuman);
                    var bones = Enumerable.Range(0, (int)HumanBodyBones.LastBone).Select(b => (HumanBodyBones)b)
                        .Where(b => animator.GetBoneTransform(b) != null).ToArray();
                    Func<HumanBodyBones, int> boneIndex = bone => Array.IndexOf(bones, bone);
                    var root = npc.transform;
                    foreach (var controller in new[] { library.pistol, library.rifle }) {
                        animator.runtimeAnimatorController = controller; animator.applyRootMotion = false; animator.Rebind();
                        foreach (var state in aimStatesForIdentity) {
                            float identityDeg = 0f, identityMm = 0f, leakDeg = 0f, chestDeg = 0f, hipsDeg = 0f, headDeg = 0f, feetMm = 0f, handMm = 0f;
                            foreach (var time in new[] { 0f, .25f, .5f, .75f }) {
                                var local = new Quaternion[3][]; var position = new Vector3[3][]; var rotation = new Quaternion[3][];
                                var settings = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 20f) };
                                for (int k = 0; k < settings.Length; k++) {
                                    animator.SetLayerWeight(1, settings[k].x);
                                    animator.SetFloat(VrBattlegrounds.Bots.BotCombatAssets.AimPitchParameter, settings[k].y);
                                    animator.Play(state, 0, time); animator.Update(0f);
                                    local[k] = new Quaternion[bones.Length]; position[k] = new Vector3[bones.Length]; rotation[k] = new Quaternion[bones.Length];
                                    for (int b = 0; b < bones.Length; b++) {
                                        var t = animator.GetBoneTransform(bones[b]);
                                        local[k][b] = t.localRotation;
                                        position[k][b] = root.InverseTransformPoint(t.position);
                                        rotation[k][b] = Quaternion.Inverse(root.rotation) * t.rotation;
                                    }
                                }
                                for (int b = 0; b < bones.Length; b++) {
                                    identityDeg = Mathf.Max(identityDeg, Quaternion.Angle(local[0][b], local[1][b]));
                                    identityMm = Mathf.Max(identityMm, Vector3.Distance(position[0][b], position[1][b]) * 1000f);
                                    if (!bodyBones.Contains(bones[b])) leakDeg = Mathf.Max(leakDeg, Quaternion.Angle(local[0][b], local[2][b]));
                                }
                                Func<HumanBodyBones, float> turned = bone => boneIndex(bone) < 0 ? 0f : Quaternion.Angle(rotation[0][boneIndex(bone)], rotation[2][boneIndex(bone)]);
                                Func<HumanBodyBones, float> moved = bone => boneIndex(bone) < 0 ? 0f : Vector3.Distance(position[0][boneIndex(bone)], position[2][boneIndex(bone)]) * 1000f;
                                chestDeg = Mathf.Max(chestDeg, Mathf.Max(turned(HumanBodyBones.Chest), turned(HumanBodyBones.UpperChest)));
                                hipsDeg = Mathf.Max(hipsDeg, turned(HumanBodyBones.Hips));
                                headDeg = Mathf.Max(headDeg, turned(HumanBodyBones.Head));
                                feetMm = Mathf.Max(feetMm, Mathf.Max(moved(HumanBodyBones.LeftFoot), moved(HumanBodyBones.RightFoot)));
                                handMm = Mathf.Max(handMm, moved(HumanBodyBones.RightHand));
                            }
                            animator.SetLayerWeight(1, 1f);
                            animator.SetFloat(VrBattlegrounds.Bots.BotCombatAssets.AimPitchParameter, 0f);
                            worstIdentity = Mathf.Max(worstIdentity, identityDeg);
                            worstLeak = Mathf.Max(worstLeak, leakDeg);
                            if (!finite(identityDeg) || identityDeg >= .01f)
                                mismatches.Add("Aim-слой при AimPitch=0 меняет позу: " + assetPath(model.source) + "/" + controller.name + "/" + state + " " + identityDeg + "°");
                            layerIdentity.Add(new { source = assetPath(model.source), controller = controller.name, state,
                                hasUpperChest = boneIndex(HumanBodyBones.UpperChest) >= 0,
                                identityDeg, identityMm, plus20 = new { chestDeg, hipsDeg, headDeg, feetMm, rightHandMm = handMm, leakDeg } });
                        }
                    }
                } finally { if (npc != null) UnityEngine.Object.DestroyImmediate(npc); }
            }
            identityMaxDeg = worstIdentity; maskLeakMaxDeg = worstLeak;
            if (layerIdentity.Count != 24) mismatches.Add("Проверка тождества aim-слоя: строк " + layerIdentity.Count + " вместо 24.");
        } finally { UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(identityScene); }
        stage = "canonical-definition-readback";
        foreach (var id in new[] { "PPK", "AK105" }) {
            var path = "Assets/Data/Weapons/" + id + "_Weapon.asset";
            var info = AssetDatabase.LoadAssetAtPath<VrBattlegrounds.Arsenal.WeaponInfo>(path);
            string reason;
            bool admitted = VrBattlegrounds.Weapons.WeaponAutomationCapability.ValidateDefinition(info, out reason);
            var prefab = info == null ? null : info.WeaponPrefab;
            var binding = prefab == null ? null : prefab.GetComponent<VrBattlegrounds.Weapons.WeaponEquipmentBinding>();
            var readiness = prefab == null ? null : prefab.GetComponent<VrBattlegrounds.Weapons.WeaponReadinessController>();
            if (!admitted || binding == null || readiness == null) mismatches.Add("Canonical automation definition incomplete: " + id + "/" + reason);
            definitions.Add(new { weapon = id, path, guid = assetGuid(info), prefabPath = assetPath(prefab), prefabGuid = assetGuid(prefab),
                capabilityValid = admitted, reason, hasEquipmentBinding = binding != null,
                readinessOwnerType = readiness == null ? null : readiness.GetType().FullName });
        }
        stage = "identity-snapshot";
        // Только чтение: базовая линия для B2/B7. Не входит в mismatches A; гейт идентичности — B2.
        foreach (var body in bodies) identity.Add(identitySnapshot(body));
        stage = "completed";
    }
} catch (Exception error) { errors.Add(stage + ": " + error); }
finally {
    previewAfter = UnityEditor.SceneManagement.EditorSceneManager.previewSceneCount;
    activeAfter = UnityEngine.SceneManagement.SceneManager.GetActiveScene().path;
    if (previewAfter != previewBefore || activeAfter != activeBefore)
        mismatches.Add("Cleanup changed preview count or active scene baseline.");
    System.IO.Directory.CreateDirectory(Root);
    if (identity.Count > 0)
        System.IO.File.WriteAllText(Root + "/identity-a.json", Newtonsoft.Json.JsonConvert.SerializeObject(new {
            phase = "after-authoring", utc = DateTime.UtcNow.ToString("o"), rows = identity }, Newtonsoft.Json.Formatting.Indented));
    System.IO.File.WriteAllText(resultPath, Newtonsoft.Json.JsonConvert.SerializeObject(new {
        executionCompleted = true, passed = authoringCompleted && errors.Count == 0 && mismatches.Count == 0 && unknown.Count == 0,
        stage, setup, composition, calibration, authoringCompleted, calibrationPassed, calibrationBuilt,
        modelCount, socketCount, motionCount, aimCount, variants, controllers, motions, aims = aimRows, definitions, mismatches, unknown, errors,
        determinism = new { libraryPath = LibraryPath, libraryShaFirst, libraryShaRepeat, repeatBytesEqual, calibrationReportsEqual, calibrationRepeat,
            setupRepeat, generatedFirst, generatedRepeat, generatedBytesEqual },
        aimLayer = new { layoutErrors, aimClips, identityMaxDeg, identityLimitDeg = 0.01f, maskLeakMaxDeg,
            gainSigns = new { negative = gainNegative, positive = gainPositive }, nonlinearOver10, rows = layerIdentity },
        creationProbe = new { folder = ProbeFolder, probeCreated, probeRepeat, probeNormalized, realNormalized,
            createEqualsRepeat = probeCreateEqualsRepeat, equalsReal = probeEqualsReal, folderRemoved = probeFolderRemoved, error = probeError },
        phaseTables = new { bins = VrBattlegrounds.Bots.BotCombatAssets.AimPhaseBins, samples = VrBattlegrounds.Bots.BotCombatAssets.AimPhaseSamples,
            complete = phaseTablesComplete, missing = phaseTablesMissing, meanMaxErrorDeg = phaseMeanMaxError,
            deviationLimitDeg = VrBattlegrounds.Bots.BotCombatAssets.AimPhaseDeviationLimit, clippedByCombo },
        identityBaseline = identity.Count > 0 ? Root + "/identity-a.json" : null, staleCyborg, supportComponents,
        cleanup = new { previewBefore, previewAfter, activeBefore, activeAfter,
            isPlaying = EditorApplication.isPlayingOrWillChangePlaymode }, gameplayAccepted = false,
        scope = "Native authoring and read-only saved prefab/definition metadata; no runtime/remote/Quest/gameplay acceptance."
    }, Newtonsoft.Json.Formatting.Indented));
}
var identitySummary = identity.Select(r => new {
    path = System.IO.Path.GetFileName((string)r["path"]),
    flagMismatches = r.ContainsKey("uxrFlagMismatches") ? r["uxrFlagMismatches"] : null,
    unpersisted = r.ContainsKey("unpersistedIds") ? r["unpersistedIds"] : null,
    assetIdCanonical = r.ContainsKey("assetIdCanonical") ? r["assetIdCanonical"] : null,
    dirty = r.ContainsKey("dirtyObjects") ? r["dirtyObjects"] : null }).ToArray();
return new { executionCompleted = true, reportPath = resultPath,
    passed = authoringCompleted && errors.Count == 0 && mismatches.Count == 0 && unknown.Count == 0,
    authoringCompleted, modelCount, socketCount, motionCount, aimCount, repeatBytesEqual, generatedBytesEqual, calibrationReportsEqual, calibrationBuilt,
    layoutErrors = layoutErrors == null ? (int?)null : layoutErrors.Count, identityMaxDeg, maskLeakMaxDeg,
    gainSigns = gainNegative + "-/" + gainPositive + "+", nonlinearOver10,
    probeCreateEqualsRepeat, probeEqualsReal, probeFolderRemoved, phaseTablesComplete, phaseMeanMaxError,
    phaseClipped = clippedByCombo.Select(c => c.Key + ": " + string.Join(", ", c.Value.Where(v => v.Value > 0)
        .Select(v => v.Key + "=" + v.Value).ToArray())).ToArray(),
    readbackVariants = variants.Count, definitions = definitions.Count, mismatches = mismatches.Count, supportComponents,
    staleCyborg = staleCyborg.Count, firstMismatch = mismatches.Count > 0 ? mismatches[0] : null,
    unknown = unknown.Count, errors = errors.Count, firstError = errors.Count > 0 ? errors[0].Substring(0, Math.Min(400, errors[0].Length)) : null,
    identity = identitySummary, previewBefore, previewAfter, gameplayAccepted = false };
