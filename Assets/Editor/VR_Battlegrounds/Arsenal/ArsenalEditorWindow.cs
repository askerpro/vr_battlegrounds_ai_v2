using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Editor.Gameplay;
using VrBattlegrounds.Editor.Avatars;
using VrBattlegrounds.EditorTools;

namespace VrBattlegrounds.Editor.Arsenal
{
    /// <summary>Единый рабочий стол оружия и станций. Перерисовка читает только сохранённые данные.</summary>
    public sealed class ArsenalEditorWindow : EditorWindow
    {
        public enum Tab { Catalog, Stations, Build, Interaction, Checks }
        private static readonly string[] Tabs = { "Каталог", "Станции", "Сборка", "Взаимодействие", "Проверки" };
        [SerializeField] private Tab tab;
        [SerializeField] private WeaponInfo weapon;
        [SerializeField] private ArsenalPreset preset;
        [SerializeField] private MapData map;
        [SerializeField] private string search = "";
        [SerializeField] private int categoryFilter;
        [SerializeField] private int sourceFilter;
        private WeaponInfo[] catalogue = Array.Empty<WeaponInfo>();
        private ArsenalPreset[] presets = Array.Empty<ArsenalPreset>();
        private MapData[] maps = Array.Empty<MapData>();
        private List<ArsenalPreset.Entry> draft = new List<ArsenalPreset.Entry>();
        private Vector2 scroll;
        private bool maintenance;
        private bool buildSetExpanded;
        private readonly HashSet<WeaponInfo> buildSet = new HashSet<WeaponInfo>();
        private string result = "Выберите предмет или операцию. Чтение и выбор строк не сохраняют ассеты.";
        private string planTitle;
        private string planNote;
        private string[] planPaths;
        private string[] planReadPaths;
        private Func<string> apply;
        private Dictionary<string, string> planSnapshot;
        private Scene planScene;
        private string planScenePath;
        private bool planHasScene;
        private bool planSavesAllAssets;
        private bool showPlanDependencies;
        private Dictionary<string, string> checkSnapshot;
        private ArsenalWeaponDiagnostics.Report diagnostics;
        private long generation;
        private PendingOperation pending;
        private enum OperationStage { Dependencies, Prepare, Validate, After, CheckCapture, CheckCompare }
        private sealed class SnapshotBatch
        {
            public Dictionary<string, string> Reads, Outputs;
        }
        private sealed class PendingOperation
        {
            public long Generation;
            public string Title, Note, Workspace, ScenePath, StageLabel, WriterReport;
            public string[] Paths, Inputs, ReadPaths;
            public Func<string> Action;
            public Scene Scene;
            public bool HasScene, SaveAll, WriterStarted;
            public int InputIndex;
            public readonly HashSet<string> Dependencies = new HashSet<string>(StringComparer.Ordinal);
            public readonly CancellationTokenSource Cancellation = new CancellationTokenSource();
            public ArsenalEditorActions.Lease Lease;
            public Task<SnapshotBatch> Task;
            public OperationStage Stage;
            public Dictionary<string, string> Expected, Before;
            public Exception WriterError;
            public volatile ArsenalFileSnapshot.Progress Progress;
        }
        public Tab SelectedTab => tab;

        [MenuItem("Tools/VR Battlegrounds/Arsenal/Редактор арсенала", false, 0)]
        public static void Open() => OpenTab(Tab.Catalog);
        public static void OpenTab(int index) => OpenTab((Tab)Mathf.Clamp(index, 0, 4));
        public static void OpenWeapon(Tab value, string path)
        {
            OpenTab(value);
            var window = GetWindow<ArsenalEditorWindow>();
            var selected = window.catalogue.FirstOrDefault(w => AssetDatabase.GetAssetPath(w.WeaponPrefab) == path);
            if (selected != null) { window.weapon = selected; window.search = ""; window.categoryFilter = 0; window.sourceFilter = 0; }
        }
        public static void OpenMaintenance(Tab value)
        {
            OpenTab(value);
            GetWindow<ArsenalEditorWindow>().maintenance = true;
        }
        public static void OpenBuildSet(IEnumerable<string> paths)
        {
            OpenTab(Tab.Build);
            var window = GetWindow<ArsenalEditorWindow>();
            var selectedPaths = new HashSet<string>(paths);
            window.buildSet.Clear();
            foreach (var item in window.catalogue.Where(w => selectedPaths.Contains(AssetDatabase.GetAssetPath(w.WeaponPrefab)))) window.buildSet.Add(item);
            window.buildSetExpanded = true;
        }
        public static void OpenTab(Tab value)
        {
            var window = GetWindow<ArsenalEditorWindow>("Арсенал");
            window.tab = value;
            window.minSize = new Vector2(620, 440);
            window.CancelPlan();
            window.Show();
        }
        private void OnEnable()
        {
            EditorApplication.update += PollPendingOperation;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
            RefreshState();
            result = SessionState.GetString("VrBattlegrounds.ArsenalEditor.OperationReport", result);
        }
        private void OnDisable()
        {
            EditorApplication.update -= PollPendingOperation;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeReload;
            OnBeforeReload();
        }
        private void OnBeforeReload()
        {
            generation++;
            var operation = pending;
            pending = null;
            if (operation != null)
            {
                operation.Cancellation.Cancel();
                try { operation.Lease?.Dispose(); }
                catch (Exception exception) { VrBattlegrounds.Core.GameLog.Arsenal.Error("Не удалось освободить собственную аренду окна: " + exception.Message); }
                if (operation.WriterStarted)
                    SessionState.SetString("VrBattlegrounds.ArsenalEditor.OperationReport", "Окно закрыто или скрипты перезагружены после запуска writer. " +
                        (operation.WriterError == null ? operation.WriterReport : operation.WriterError.Message) + "\nПолный отчёт изменений не завершён; автоматического отката нет.");
                // После закрытия/reload никакой continuation не может перейти к writer.
                if (operation.Task == null || operation.Task.IsCompleted) operation.Cancellation.Dispose();
                else operation.Task.ContinueWith(task => { if (task.IsFaulted) _ = task.Exception; operation.Cancellation.Dispose(); }, TaskScheduler.Default);
            }
            ClearPlan();
        }
        private void RefreshState()
        {
            catalogue = ArsenalEditorStatus.Assets<WeaponInfo>("Assets/Data/Weapons");
            presets = ArsenalEditorStatus.Assets<ArsenalPreset>("Assets/Data");
            maps = ArsenalEditorStatus.Assets<MapData>("Assets/Data");
            if (weapon == null) weapon = catalogue.FirstOrDefault();
            if (preset == null) preset = presets.FirstOrDefault();
            draft = preset != null ? preset.Entries.ToList() : new List<ArsenalPreset.Entry>();
            CancelPlan();
        }
        private void ClearPlan() { apply = null; planTitle = null; planPaths = null; planReadPaths = null; planSnapshot = null; planHasScene = false; }
        private void CancelPlan()
        {
            generation++;
            ClearPlan();
            if (pending != null && !pending.WriterStarted) pending.Cancellation.Cancel();
        }
        private void OnGUI()
        {
            EditorGUILayout.LabelField("Арсенал", EditorStyles.largeLabel);
            EditorGUILayout.LabelField("Сцена: " + SceneManager.GetActiveScene().name + " · " + ArsenalEditorActions.LockStatus, EditorStyles.miniLabel);
            if (ArsenalEditorActions.BlockReason != null) EditorGUILayout.HelpBox(ArsenalEditorActions.BlockReason, MessageType.Warning);
            int next = GUILayout.SelectionGrid((int)tab, Tabs, position.width < 820 ? 3 : 5, EditorStyles.toolbarButton);
            if (next != (int)tab) { tab = (Tab)next; CancelPlan(); }
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (tab != Tab.Stations) DrawWeaponChoice();
            switch (tab)
            {
                case Tab.Catalog: DrawCatalogue(); break;
                case Tab.Stations: DrawStations(); break;
                case Tab.Build: DrawBuild(); break;
                case Tab.Interaction: DrawInteraction(); break;
                case Tab.Checks: DrawChecks(); break;
            }
            DrawPlan();
            DrawPendingOperation();
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Результат", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(result, EditorStyles.wordWrappedLabel, GUILayout.MinHeight(100));
            EditorGUILayout.EndScrollView();
        }
        private void DrawWeaponChoice()
        {
            string nextSearch = EditorGUILayout.TextField("Поиск названия / ID", search);
            if (nextSearch != search) search = nextSearch;
            using (new EditorGUILayout.HorizontalScope())
            {
                categoryFilter = EditorGUILayout.Popup("Категория", categoryFilter, new[] { "Все", "Винтовки", "Пистолеты", "Снаряжение", "Ближний бой" });
                sourceFilter = EditorGUILayout.Popup("Источник", sourceFilter, new[] { "Все", "KINEMATION", "Hands", "SDK / нет рецепта" });
            }
            var visible = catalogue.Where(w => (w.DisplayName + " " + w.WeaponId).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 &&
                (categoryFilter == 0 || (int)w.Category == categoryFilter - 1) &&
                (sourceFilter == 0 || sourceFilter == 1 && ArsenalEditorActions.Kinemation(w) != null || sourceFilter == 2 && ArsenalEditorActions.Source(w).StartsWith("Hands") || sourceFilter == 3 && ArsenalEditorActions.Source(w).StartsWith("SDK"))).ToArray();
            if (visible.Length == 0) EditorGUILayout.HelpBox("Совпадений нет", MessageType.Info);
            else
            {
                int index = Array.IndexOf(visible, weapon);
                int chosen = EditorGUILayout.Popup("Оружие (" + catalogue.Length + ")", Mathf.Max(0, index), visible.Select(w => w.DisplayName + " [" + w.WeaponId + "]").ToArray());
                if (index < 0 || chosen != index) { weapon = visible[chosen]; CancelPlan(); }
            }
            if (weapon == null) return;
            EditorGUILayout.LabelField(weapon.DisplayName + " · " + weapon.Category + " · " + ArsenalEditorActions.Source(weapon), EditorStyles.boldLabel);
            Link("Данные оружия", weapon);
            Link("Оружие", weapon.WeaponPrefab);
            Link("Магазин", weapon.MagazinePrefab);
        }
        private void Link(string label, UnityEngine.Object obj)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.ObjectField(label, obj, typeof(UnityEngine.Object), false);
                using (new EditorGUI.DisabledScope(obj == null))
                    if (GUILayout.Button("Открыть", GUILayout.Width(70)))
                    {
                        if (obj is GameObject || obj is SceneAsset)
                            Try(() => ArsenalEditorActions.Run("Открыть " + label, Array.Empty<string>(), () => { AssetDatabase.OpenAsset(obj); return "Открыт ассет: " + AssetDatabase.GetAssetPath(obj); }, false));
                        else AssetDatabase.OpenAsset(obj);
                    }
            }
        }
        private void DrawCatalogue()
        {
            if (weapon != null)
            {
                EditorGUILayout.LabelField("ID: " + weapon.WeaponId + " · GUID: " + AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(weapon)), EditorStyles.miniLabel);
                EditorGUILayout.LabelField($"Баланс: урон {weapon.Damage}; темп {weapon.FireRate}; магазин {weapon.MagazineSize}; цена {weapon.Price}");
                EditorGUILayout.LabelField("В ассортиментах: " + string.Join(", ", presets.Where(p => p.Entries.Any(e => e.Weapon == weapon)).Select(p => p.PresetId)));
                EditorGUILayout.HelpBox("Данные и смещения WeaponInfo общие для всех станций и карт этого предмета.", MessageType.Info);
                var selected = weapon;
                Command("Применить баланс выбранного оружия", ArsenalEditorStatus.Prefabs(new[] { selected }), "Оружие и его магазин; формулы существующего WeaponBalanceApplier.", () => WeaponBalanceApplier.Apply(new[] { selected }), new[] { AssetDatabase.GetAssetPath(selected) }, savesAllAssets: false);
            }
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Ассортимент · порядок задаёт адреса слотов", EditorStyles.boldLabel);
            var nextPreset = (ArsenalPreset)EditorGUILayout.ObjectField("Ассортимент", preset, typeof(ArsenalPreset), false);
            if (nextPreset != preset) { preset = nextPreset; draft = preset != null ? preset.Entries.ToList() : new List<ArsenalPreset.Entry>(); CancelPlan(); }
            if (preset != null)
            {
                for (int i = 0; i < draft.Count; i++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var entry = draft[i];
                        EditorGUI.BeginChangeCheck();
                        entry.Weapon = (WeaponInfo)EditorGUILayout.ObjectField((i + 1).ToString(), entry.Weapon, typeof(WeaponInfo), false);
                        entry.Row = EditorGUILayout.TextField(entry.Row, GUILayout.Width(100));
                        if (EditorGUI.EndChangeCheck()) { draft[i] = entry; CancelPlan(); }
                        if (GUILayout.Button("↑", GUILayout.Width(25)) && i > 0) { var previous = draft[i - 1]; draft[i - 1] = entry; draft[i] = previous; CancelPlan(); }
                        if (GUILayout.Button("−", GUILayout.Width(25))) { draft.RemoveAt(i); CancelPlan(); break; }
                    }
                }
                if (GUILayout.Button("Добавить выбранное оружие")) { draft.Add(new ArsenalPreset.Entry { Weapon = weapon, Row = "pegboard" }); CancelPlan(); }
                string problem = ArsenalEditorStatus.ValidatePreset(preset, draft);
                foreach (var consumer in maps.Where(m => m.arsenalPreset == preset))
                    problem = problem ?? ArsenalEditorStatus.CapacityProblem(draft, consumer);
                if (problem != null) EditorGUILayout.HelpBox(problem, MessageType.Warning);
                using (new EditorGUI.DisabledScope(problem != null))
                {
                    var target = preset;
                    var entries = draft.ToArray();
                    Command("Сохранить выбранный ассортимент", new[] { AssetDatabase.GetAssetPath(target) }, "Карты-потребители: " + string.Join(", ", maps.Where(m => m.arsenalPreset == target).Select(m => m.displayName)) + ". Порядок и ряды сохраняются как показано.", () =>
                    {
                        string invalid = ArsenalEditorStatus.ValidatePreset(target, entries);
                        foreach (var consumer in maps.Where(m => m.arsenalPreset == target)) invalid = invalid ?? ArsenalEditorStatus.CapacityProblem(entries, consumer);
                        if (invalid != null) throw new InvalidOperationException(invalid);
                        Undo.RecordObject(target, "Ассортимент арсенала");
                        var so = new SerializedObject(target);
                        var property = so.FindProperty("_entries"); property.arraySize = entries.Length;
                        for (int i = 0; i < entries.Length; i++) { var item = property.GetArrayElementAtIndex(i); item.FindPropertyRelative("Weapon").objectReferenceValue = entries[i].Weapon; item.FindPropertyRelative("Row").stringValue = entries[i].Row; }
                        so.ApplyModifiedProperties(); AssetDatabase.SaveAssetIfDirty(target);
                        return "Ассортимент сохранён. Станцию соберёт генератор при запуске карты.";
                    }, new[] { ArsenalPresetAssetBuilder.CommonPath, ArsenalPresetAssetBuilder.DemoPath }.Concat(maps.Select(AssetDatabase.GetAssetPath)).ToArray(), savesAllAssets: false);
                }
            }
            var nextMap = (MapData)EditorGUILayout.ObjectField("Карта", map, typeof(MapData), false);
            if (nextMap != map) { map = nextMap; CancelPlan(); }
            string capacity = ArsenalEditorStatus.ValidatePreset(preset) ?? ArsenalEditorStatus.CapacityProblem(preset.Entries, map);
            if (capacity != null) EditorGUILayout.HelpBox(capacity, MessageType.Info);
            using (new EditorGUI.DisabledScope(capacity != null))
            {
                var targetMap = map; var targetPreset = preset;
                Command("Назначить сохранённый ассортимент карте", map == null ? Array.Empty<string>() : new[] { AssetDatabase.GetAssetPath(map) }, "Изменяется только ссылка MapData; применение при следующей загрузке карты.", () =>
                { string invalid = ArsenalEditorStatus.ValidatePreset(targetPreset) ?? ArsenalEditorStatus.CapacityProblem(targetPreset.Entries, targetMap); if (invalid != null) throw new InvalidOperationException(invalid); Undo.RecordObject(targetMap, "Ассортимент карты"); targetMap.arsenalPreset = targetPreset; EditorUtility.SetDirty(targetMap); AssetDatabase.SaveAssetIfDirty(targetMap); return "Назначение карты сохранено."; }, new[] { ArsenalPresetAssetBuilder.CommonPath, ArsenalPresetAssetBuilder.DemoPath, AssetDatabase.GetAssetPath(targetPreset) }, savesAllAssets: false);
            }
            maintenance = EditorGUILayout.Foldout(maintenance, "Обслуживание и восстановление каталога", true);
            if (!maintenance) return;
            var allBalanced = catalogue.Where(w => w.HasBalance).ToArray();
            Command("Применить баланс всего каталога", ArsenalEditorStatus.Prefabs(allBalanced), "Все предметы с заданным балансом и их магазины; SDK без баланса пропускаются.", () => WeaponBalanceApplier.Apply(allBalanced), allBalanced.Select(AssetDatabase.GetAssetPath).ToArray(), savesAllAssets: false);
            Command("Восстановить старый проект: перенести имена Hands", WeaponNamingMigration.AffectedPaths, "Историческая миграция фиксированных шести GUID, а не редактор новых имён. Шесть prefab и Art motion/interaction folders; имена шести WeaponInfo. Частичный отказ не откатывается автоматически.", WeaponNamingMigration.Apply);
            Command("Дополнить регистрацию каталога в каноническом менеджере", new[] { "Assets/Prefabs/Managers/--- MANAGERS ---.prefab" }, "Все WeaponInfo каталога: оружие и магазины; сцена не используется для поиска менеджера.", () => ArsenalPresetAssetBuilder.RegisterNetworkPrefabs(catalogue), ArsenalEditorStatus.Prefabs(catalogue).Concat(catalogue.Select(AssetDatabase.GetAssetPath)).ToArray(), savesAllAssets: false);
        }
        private void DrawStations()
        {
            EditorGUILayout.LabelField("Игровая станция", EditorStyles.boldLabel);
            DrawStation(ArsenalPresetAssetBuilder.CommonPath);
            EditorGUILayout.HelpBox("Слоты станций собирает генератор при запуске карты: корпус задаёт ряды (ArsenalSlotRow), пресет — какое оружие в какой ряд, раскладку в слоте — оружие.", MessageType.Info);
            EditorGUILayout.LabelField("Префаб станции лобби", EditorStyles.boldLabel);
            DrawStation(ArsenalPresetAssetBuilder.DemoPath);
            var scene = SceneManager.GetActiveScene();
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(scene.path)))
                Command("Обновить ниши активной карты: " + scene.name, new[] { scene.path }, "Геометрия активной сцены; сцена станет dirty и не сохранится автоматически. Затем Bake Occlusion (all maps).", () => { string report = ArsenalMapMigration.UpdateLayout(scene); EditorSceneManager.MarkSceneDirty(scene); return report; }, new[] { "Assets/Art/ArsenalBoundary" }, scene, false);
            var review = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/Debug/CommonArsenalReview.unity");
            if (review != null && GUILayout.Button("Открыть стенд CommonArsenalReview")) Try(() => ArsenalEditorActions.Run("Открыть стенд арсенала", Array.Empty<string>(), () => { EditorSceneManager.OpenScene(AssetDatabase.GetAssetPath(review)); return "Открыт стенд."; }, false));
            maintenance = EditorGUILayout.Foldout(maintenance, "Обслуживание всех карт и общих карточек", true);
            if (!maintenance) return;
            Command("Мигрировать игровые карты и Lobby", new[] { "Assets/Scenes/Maps", "Assets/Scenes/Lobby.unity", "Assets/Prefabs/Maps/ZoneBoundaryDisplayHousing.prefab" }, "Все карты и Lobby + общий prefab корпуса display; сохраняет сцены и backups. После геометрии нужен общий Bake Occlusion.", ArsenalMapMigration.Run, new[] { ArsenalPresetAssetBuilder.CommonPath, ArsenalPresetAssetBuilder.DemoPath, "Assets/Art/ArsenalBoundary" }, savesAllAssets: false);
            Command("Обновить общие карточки слотов", new[] { "Assets/Prefabs/Arsenal/Slots", "Assets/Art/ArsenalBoundary/WeaponCardBacking.mat" }, "Общий builder карточек затрагивает slot prefabs и backing material, используемые всеми станциями.", () => { ArsenalCardPrefabBuilder.Build(); return "Общие карточки обновлены."; }, savesAllAssets: false);
        }
        private void DrawStation(string path)
        {
            EditorGUILayout.LabelField(ArsenalEditorStatus.Capacity(path));
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Link("Префаб станции", root);
            if (root == null) return;
            var wall = root.GetComponent<ArsenalWallController>();
            var anchor = root.GetComponent<ArsenalStationAnchor>();
            var equipment = root.GetComponent<ArsenalEquipmentPoses>();
            if (anchor != null) EditorGUILayout.LabelField("Габарит открытой станции: " + anchor.RaisedBoundsWorld.size.ToString("F2"));
            if (equipment != null) EditorGUILayout.LabelField("Габарит закрытой станции: " + equipment.FoldedBoundsLocal.size.ToString("F2"));
        }
        private void DrawBuild()
        {
            var kin = ArsenalEditorActions.Kinemation(weapon); var hands = ArsenalEditorActions.Hands(weapon);
            EditorGUILayout.HelpBox("Пересборка из источника изменяет оружие, магазин, Art/motion/interaction, звуки и позы. Общая база поз влияет на наследующие аватары. После rebuild баланс применяется отдельно в «Каталоге»; воспроизводимость новой readiness policy ведётся отдельным этапом. Этот маршрут не подтверждает игровую готовность.", MessageType.Warning);
            if (kin != null)
            {
                EditorGUILayout.LabelField("Рецепт: " + kin.Weapon.Name + " · исходник: " + kin.PackPrefab);
                if (GUILayout.Button("Проверить источник KINEMATION")) Try(() => ArsenalEditorActions.Run("Проверка источника KINEMATION", Array.Empty<string>(), () => ArsenalEditorActions.ReportSource(kin), false));
                Command("Подготовить Read/Write исходных моделей KINEMATION", ArsenalEditorActions.SourceSettingsPaths(kin), "Только ModelImporter .meta мешей выбранного исходного префаба и выбранных attachments; явный reimport исходников.", () => { KinemationWeapon.EnsureReadable(ArsenalEditorActions.SourcePrefab(kin), kin.Attachments); return "Read/Write исходных моделей подготовлен."; }, ArsenalBuildPreflight.Inputs(weapon), savesAllAssets: false);
                var selected = weapon;
                Command("Пересобрать выбранное оружие KINEMATION из источника", BuildPaths(selected), "Существующий source pipeline: sounds → geometry/magazine → poses/base → recoil → motion/regions; общие materials и insertion material входят в область. Баланс/readiness не подтверждены этой операцией.", () => { ArsenalBuildPreflight.Validate(new[] { selected }); KinemationWeaponBuilder.Build(kin); return "Source rebuild " + kin.Weapon.Name + " завершён; баланс и игровая проверка отдельно."; }, ArsenalBuildPreflight.Inputs(selected));
            }
            else if (hands != null)
            {
                EditorGUILayout.LabelField("Рецепт: " + hands.Name + " · исходная папка: " + hands.Folder);
                if (GUILayout.Button("Проверить источник Hands")) Try(() => ArsenalEditorActions.Run("Проверка источника Hands", Array.Empty<string>(), () => { using var source = new HandsPackWeapon(hands.Folder, hands.BodyPart); return source.Report(hands.RestClip); }, false));
                var selected = weapon;
                Command("Пересобрать выбранное оружие Hands T-38 из источника", BuildPaths(selected), "BuildFull: geometry/magazine → ImportFor (общая база поз) → recoil alignment. Баланс/readiness не подтверждены этой операцией.", () => { ArsenalBuildPreflight.Validate(new[] { selected }); HandsPackWeaponBuilder.BuildFull(hands); return "Source rebuild " + hands.Name + " завершён; баланс и игровая проверка отдельно."; }, ArsenalBuildPreflight.Inputs(selected));
            }
            else EditorGUILayout.HelpBox("Для этого предмета нет одиночного полного рецепта. SDK-варианты не пересобираются этим окном; три прежних Hands-импорта доступны отдельным набором ниже.", MessageType.Info);
            if (weapon != null && AssetDatabase.GetAssetPath(weapon.WeaponPrefab) == Ar15ScaleCalibration.WeaponPath)
                Command("Обновить размер AR-15 и его внешнего магазина", Ar15ScaleCalibration.OutputPaths(), "Только масштаб двух корней .70; локальные дочерние frames и вложенный магазин сохраняются.",
                    () => { string report = Ar15ScaleCalibration.Preflight(); Ar15ScaleCalibration.Apply(); return report; }, Ar15ScaleCalibration.InputPaths(), savesAllAssets: false);
            if (kin != null && kin.Weapon.Name == "TR15")
                Command("Обновить линзу прицела TR15", Tr15OpticBuilder.OutputPaths(), "Выбранный TR15 и три generated game-owned optic assets; source TGA читаются без изменения импортера.",
                    () => { string report = Tr15OpticBuilder.Preflight(); Tr15OpticBuilder.Apply(); return report; }, Tr15OpticBuilder.InputPaths(), savesAllAssets: false);
            buildSetExpanded = EditorGUILayout.Foldout(buildSetExpanded, "Пересборка выбранного набора", true);
            if (buildSetExpanded)
            {
                foreach (var item in catalogue)
                {
                    bool wasSelected = buildSet.Contains(item);
                    bool nowSelected = EditorGUILayout.ToggleLeft(item.DisplayName + " · " + ArsenalEditorActions.Source(item), wasSelected);
                    if (wasSelected != nowSelected) { if (nowSelected) buildSet.Add(item); else buildSet.Remove(item); CancelPlan(); }
                }
                var selectedSet = catalogue.Where(buildSet.Contains).ToArray();
                var unsupported = selectedSet.Where(w => ArsenalEditorActions.Kinemation(w) == null && ArsenalEditorActions.Hands(w) == null).ToArray();
                if (unsupported.Length > 0) EditorGUILayout.HelpBox("Нет одиночного полного рецепта: " + string.Join(", ", unsupported.Select(w => w.DisplayName)), MessageType.Warning);
                using (new EditorGUI.DisabledScope(selectedSet.Length == 0 || unsupported.Length > 0))
                    Command("Пересобрать выбранный набор из источников (" + selectedSet.Length + ")", selectedSet.SelectMany(BuildPaths).Distinct().ToArray(), "Сначала preflight каждого источника, затем существующие source pipelines. Общие позы и source import dependencies входят в область; баланс/readiness отдельно.", () =>
                    {
                        ArsenalBuildPreflight.Validate(selectedSet);
                        foreach (var item in selectedSet)
                        {
                            var k = ArsenalEditorActions.Kinemation(item);
                            if (k != null) KinemationWeaponBuilder.Build(k); else HandsPackWeaponBuilder.BuildFull(ArsenalEditorActions.Hands(item));
                        }
                        return "Пересобрано из источников оружий: " + selectedSet.Length + "; баланс и игровая проверка отдельно.";
                    }, selectedSet.SelectMany(ArsenalBuildPreflight.Inputs).Distinct().ToArray());
            }
            EditorGUILayout.LabelField("Поддержанный набор Hands: Browning Hi-Power · AR-15 · FABARM SDASS", EditorStyles.boldLabel);
            Command("Пересобрать набор Hands: 3 оружия + 3 магазина", new[] { "Assets/Prefabs/Weapons/BrowningHiPower", "Assets/Prefabs/Weapons/AR15", "Assets/Prefabs/Weapons/FabarmSDASS", "Assets/Art/Weapons/HandsPack" }, "Фиксированный source bundle из immutable baseline: preflight, geometry/magazine, motion и проверка идентичности. Не одиночный рецепт; баланс/readiness отдельно.", () => { HandsPackLegacyRebuild.RebuildAll(); return "Source набор 3+3 пересобран; баланс и игровая проверка отдельно."; });
            maintenance = EditorGUILayout.Foldout(maintenance, "Обслуживание источников и восстановление", true);
            if (!maintenance) return;
            Command("Обновить звук револьвера из R08", new[] { "Assets/Prefabs/Weapons/Revolver/Revolver.prefab", KinemationWeaponBuilder.RevolverShotPath }, "Нарезка WAV выстрела из пакета KINEMATION и ссылка _shotAudio выбранного исторического Revolver. Не полная сборка.", () => { KinemationWeaponBuilder.MakeRevolverShot(); return "Звук револьвера обновлён."; }, new[] { KinemationWeaponBuilder.R08.ShotSound });
            Command("Подготовить шаблоны трёх Hands-импортов", new[] { "Assets/Art/Weapons/HandsPack" }, "Создаёт Art snapshots и motion assets; не является чистой проверкой.", HandsPackLegacyRebuild.PrepareBaseline);
        }
        private static string[] BuildPaths(WeaponInfo selected)
        {
            var kin = ArsenalEditorActions.Kinemation(selected);
            string name = selected != null && selected.WeaponPrefab != null ? selected.WeaponPrefab.name : "";
            var recipe = kin?.Weapon ?? ArsenalEditorActions.Hands(selected);
            var paths = ArsenalEditorStatus.Prefabs(new[] { selected }).Concat(new[] { "Assets/Art/Weapons/Interaction/" + name, ArsenalBuildPreflight.InsertionMaterial, AvatarHandBases.NonSdkHands, "Assets/Prefabs/Player/MEF_Base_Avatar.prefab" });
            if (recipe != null) paths = paths.Concat(new[] { $"Assets/Prefabs/Weapons/{recipe.PrefabFolder}/{recipe.Name}.prefab", $"Assets/Prefabs/Weapons/{recipe.PrefabFolder}/{recipe.MagazineName ?? recipe.Name + "_mag"}.prefab" });
            var poses = kin != null ? KinemationWeaponBuilder.PoseRecipes(kin) : HandsPackPoseImporter.Recipes.Where(r => selected != null && r.Weapon == AssetDatabase.GetAssetPath(selected.WeaponPrefab));
            paths = paths.Concat(poses.Select(r => (r.PoseFolder ?? HandsPackPoseImporter.PoseFolder) + "/" + r.Pose + ".asset"));
            if (kin != null) paths = paths.Concat(new[] { KinemationWeapon.ArtRoot + "/" + name, KinemationWeapon.ArtRoot + "/Materials", KinemationAudio.Root + "/" + name }).Concat(ArsenalEditorActions.SourceSettingsPaths(kin)).Concat(ArsenalBuildPreflight.MaterialSettingsPaths(kin));
            if (kin != null && kin.Weapon.Name == "TR15") paths = paths.Concat(Tr15OpticBuilder.OutputPaths());
            return paths.Distinct().ToArray();
        }
        private void DrawInteraction()
        {
            if (weapon == null || weapon.WeaponPrefab == null) return;
            var selected = weapon; string path = AssetDatabase.GetAssetPath(selected.WeaponPrefab);
            EditorGUILayout.HelpBox("Хват, ручной цикл и смещения редактируются в существующем Inspector префаба. Подгонка кисти по геометрии пока исследование; успешного отчёта о прилегании здесь нет.", MessageType.Info);
            EditorGUILayout.HelpBox("Импорт оружейных поз изменяет grabs выбранного оружия, общую базу поз и MEF; наследующие аватары получают обновление. Это не локальная настройка одной станции.", MessageType.Info);
            if (GUILayout.Button("Проверить области хвата выбранного оружия")) Try(() => ArsenalEditorActions.Run("Проверка областей выбранного оружия", Array.Empty<string>(), () => WeaponInteractionReview.Preflight(new[] { path }), false));
            Command("Обновить подсветку и напоминание выбранного оружия", ArsenalEditorStatus.Prefabs(new[] { selected }).Concat(new[] { "Assets/Art/Weapons" }).ToArray(), "Выбранное оружие и его магазин; existing installer. Корпус, баланс и позы кистей не пересобираются.", () => WeaponInteractionReview.Apply(new[] { path }, selected.MagazinePrefab == null ? Array.Empty<string>() : new[] { AssetDatabase.GetAssetPath(selected.MagazinePrefab) }));
            var kin = ArsenalEditorActions.Kinemation(selected);
            if (kin != null) Command("Обновить механические анимации KINEMATION", ArsenalEditorStatus.Prefabs(new[] { selected }).Concat(new[] { KinemationWeapon.ArtRoot + "/" + kin.Weapon.Name, ArsenalBuildPreflight.InsertionMaterial }).Concat(ArsenalEditorActions.SourceSettingsPaths(kin)).ToArray(), "Motion + bindings/контакты и Empty range; KinemationFixReview.Apply. Исходные ModelImporter могут получить Read/Write.", () => { KinemationFixReview.Apply(kin.Weapon.Name); return "Механика обновлена."; }, ArsenalBuildPreflight.Inputs(selected));
            if (kin != null) Command("Импортировать позы кистей KINEMATION", BuildPaths(selected), "Только позы и grabs оружия, общий pose base и MEF. Source model import dependencies могут получить Read/Write.", () => { KinemationWeaponBuilder.ImportPoses(kin); return "Позы KINEMATION импортированы."; }, ArsenalBuildPreflight.Inputs(selected));
            if (HandsPackPoseImporter.Recipes.Any(r => r.Weapon == path))
                Command("Импортировать позы кистей Hands", new[] { path, "Assets/Art/HandPoses", AvatarHandBases.NonSdkHands, "Assets/Prefabs/Player/MEF_Base_Avatar.prefab" }, "Поза оружия, MEF grip avatar и общий pose base; наследующие аватары получают обновление.", () => { HandsPackPoseImporter.ImportFor(path); return "Позы импортированы."; }, HandsPackPoseImporter.Recipes.Where(p => p.Weapon == path).Select(p => HandsPackWeapon.PackModels + p.Folder).ToArray());
            Command("Обновить внутренний патрон двух дробовиков", new[] { "Assets/Prefabs/Weapons/FabarmSDASS", "Assets/Prefabs/Weapons/Herrington" }, "Фиксированный набор двух дробовиков: ammo alignment/ejection, не баланс/ёмкость.", WeaponInternalAmmoAlignment.ApplyCurrentShotguns);
        }
        private void DrawChecks()
        {
            if (GUILayout.Button("Обновить каталог без записи")) { RefreshState(); result = "Каталог перечитан: " + catalogue.Length + " предметов."; }
            if (GUILayout.Button("Проверить ссылки, ID и физическую структуру каталога")) Try(() => ArsenalEditorStatus.Report(catalogue));
            EditorGUILayout.LabelField("Диагностика готовых KINEMATION-префабов", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Motion/bindings, точки хвата и meshes готового prefab; отсутствующие данные отображаются строками. Источник и его Read/Write не изменяются. Проверка исходных клипов — во вкладке «Сборка».", MessageType.Info);
            var kin = ArsenalEditorActions.Kinemation(weapon);
            using (new EditorGUI.DisabledScope(pending != null || ArsenalEditorActions.BlockReason != null))
            {
                using (new EditorGUI.DisabledScope(kin == null))
                    if (GUILayout.Button("Диагностика выбранного KINEMATION")) Try(() => ArsenalEditorActions.Run("Диагностика выбранного KINEMATION", Array.Empty<string>(), () => { diagnostics = ArsenalWeaponDiagnostics.Capture(new[] { kin }); return diagnostics.Summary; }, false));
                if (GUILayout.Button("Диагностика всех поддержанных KINEMATION (8)")) Try(() => ArsenalEditorActions.Run("Диагностика поддержанных KINEMATION", Array.Empty<string>(), () => { diagnostics = ArsenalWeaponDiagnostics.Capture(KinemationWeaponBuilder.All); return diagnostics.Summary; }, false));
            }
            if (diagnostics != null)
            {
                var captured = diagnostics;
                Command("Экспортировать полученный снимок диагностики в tmp", new[] { ArsenalWeaponDiagnostics.ExportPath }, "Только " + ArsenalWeaponDiagnostics.ExportPath + "; полученный снимок " + captured.CapturedUtc + ", не новая проверка. Исторические Docs artifacts и Assets не записываются.", () => ArsenalWeaponDiagnostics.Export(captured), savesAllAssets: false);
            }
            var checkPaths = catalogue.Select(AssetDatabase.GetAssetPath).Concat(ArsenalEditorStatus.Prefabs(catalogue))
                .Concat(presets.Select(AssetDatabase.GetAssetPath)).Concat(maps.Select(AssetDatabase.GetAssetPath))
                .Concat(new[] { ArsenalPresetAssetBuilder.CommonPath, ArsenalPresetAssetBuilder.DemoPath, "Assets/Prefabs/Managers/--- MANAGERS ---.prefab" }).Distinct().ToArray();
            using (new EditorGUI.DisabledScope(pending != null))
                if (GUILayout.Button("Снять состояние каталога, ассортиментов и станций")) Try(() => BeginCheckSnapshot(checkPaths, false));
            using (new EditorGUI.DisabledScope(checkSnapshot == null || pending != null))
                if (GUILayout.Button("Сравнить с сохранённым снимком")) Try(() => BeginCheckSnapshot(checkPaths, true));
            if (GUILayout.Button("Проверить области хвата всех оружий")) Try(() => ArsenalEditorActions.Run("Проверка областей каталога", Array.Empty<string>(), WeaponInteractionReview.Preflight, false));
            Command("Проверить компиляцию под Android", new[] { "Temp/AndroidCompileGate" }, "Компилирует player scripts; игровые ассеты не меняет. Editor-only окно проверяется компиляцией Unity Editor.", () => { var report = AndroidCompileGate.Run(); return report.Summary + "\n" + string.Join("\n", report.Errors); });
            EditorGUILayout.HelpBox("Существующие EditMode проверки: WeaponDropPhysicsTests, OutOfWorldGuardTests, WeaponPartGrabTests, WeaponScaleTests, WeaponBalanceTests, ArsenalPrefabMutationTests. Запуск — Window/General/Test Runner, сборка VrBattlegrounds.Tests.EditMode. Проверка в шлеме отдельно: хват, перезарядка, отдача, магазины и открытие станции.", MessageType.Info);
        }
        private void Command(string title, string[] paths, string note, Func<string> action, string[] readPaths = null, Scene? sceneContext = null, bool savesAllAssets = true)
        {
            using (new EditorGUI.DisabledScope(pending != null || ArsenalEditorActions.BlockReason != null))
                if (GUILayout.Button(title)) Try(() => BeginPrepare(title, paths, note, action, readPaths, sceneContext, savesAllAssets));
        }
        private string BeginPrepare(string title, string[] paths, string note, Func<string> action, string[] reads, Scene? scene, bool saveAll)
        {
            if (pending != null) throw new InvalidOperationException("Дождитесь завершения текущей операции.");
            CancelPlan();
            var operation = new PendingOperation
            {
                Generation = generation, Title = title, Note = note, Action = action,
                Paths = paths.Where(p => !string.IsNullOrEmpty(p)).Distinct().ToArray(),
                Inputs = paths.Concat(reads ?? Array.Empty<string>()).Where(p => !string.IsNullOrEmpty(p)).Distinct().ToArray(),
                Scene = scene.GetValueOrDefault(), HasScene = scene.HasValue, SaveAll = saveAll,
                Workspace = Directory.GetParent(Application.dataPath).FullName,
                Stage = OperationStage.Dependencies, StageLabel = "Очередь зависимостей"
            };
            operation.ScenePath = operation.HasScene ? operation.Scene.path : null;
            try
            {
                operation.Lease = ArsenalEditorActions.AcquireLease(title);
                RequireOperationGuards(operation, operation.Inputs);
                pending = operation;
                return "Подготовка поставлена в очередь. До готового снимка применение недоступно.";
            }
            catch { operation.Lease?.Dispose(); operation.Cancellation.Dispose(); throw; }
        }
        private string BeginApply()
        {
            if (pending != null || apply == null) throw new InvalidOperationException("Нет готового свободного плана.");
            var operation = new PendingOperation
            {
                Generation = generation, Title = planTitle, Action = apply, Paths = (string[])planPaths.Clone(),
                ReadPaths = (string[])planReadPaths.Clone(), Expected = new Dictionary<string, string>(planSnapshot, StringComparer.OrdinalIgnoreCase),
                Scene = planScene, ScenePath = planScenePath, HasScene = planHasScene, SaveAll = planSavesAllAssets,
                Workspace = Directory.GetParent(Application.dataPath).FullName
            };
            try
            {
                operation.Lease = ArsenalEditorActions.AcquireLease(operation.Title);
                RequireOperationGuards(operation, operation.ReadPaths);
                ClearPlan();
                pending = operation;
                StartSnapshot(operation, OperationStage.Validate);
                return "Повторная проверка SHA и выходов. Запись ещё не началась.";
            }
            catch { operation.Lease?.Dispose(); operation.Cancellation.Dispose(); throw; }
        }
        private string BeginCheckSnapshot(string[] paths, bool compare)
        {
            if (pending != null) throw new InvalidOperationException("Дождитесь завершения текущей операции.");
            CancelPlan();
            var operation = new PendingOperation
            {
                Generation = generation, Title = "Снимок каталога", Paths = (string[])paths.Clone(),
                Expected = compare ? checkSnapshot : null, Workspace = Directory.GetParent(Application.dataPath).FullName
            };
            pending = operation;
            StartSnapshot(operation, compare ? OperationStage.CheckCompare : OperationStage.CheckCapture);
            return "Файловый снимок выполняется в фоне; ассеты не записываются.";
        }
        private static void RequireOperationGuards(PendingOperation operation, string[] reads)
        {
            operation.Lease.RequireActive();
            if (operation.HasScene) ArsenalEditorStatus.RequireScene(operation.Scene, operation.ScenePath);
            ArsenalEditorActions.RequireSavedAssets(operation.SaveAll);
            ArsenalEditorActions.RequireCleanAssets(reads.Concat(operation.Paths));
        }
        private static void StartSnapshot(PendingOperation operation, OperationStage stage)
        {
            operation.Stage = stage;
            operation.StageLabel = stage == OperationStage.Validate ? "Проверка источников перед записью" : stage == OperationStage.After ? "Отчёт после записи" : "SHA256 файлов и .meta";
            operation.Progress = null;
            // Все входы уже захвачены на main thread; worker не обращается к Unity или окну.
            string workspace = operation.Workspace;
            string[] paths = (string[])operation.Paths.Clone();
            string[] reads = operation.ReadPaths == null ? Array.Empty<string>() : (string[])operation.ReadPaths.Clone();
            CancellationToken cancel = stage == OperationStage.After ? CancellationToken.None : operation.Cancellation.Token;
            operation.Task = Task.Run(() =>
            {
                var batch = new SnapshotBatch();
                Action<ArsenalFileSnapshot.Progress> progress = value => operation.Progress = value;
                if (stage == OperationStage.Prepare || stage == OperationStage.Validate)
                    batch.Reads = ArsenalFileSnapshot.Read(workspace, reads, cancel, progress);
                if (stage != OperationStage.Prepare)
                    batch.Outputs = ArsenalFileSnapshot.Read(workspace, paths, cancel, progress);
                return batch;
            });
        }
        private void PollPendingOperation()
        {
            var operation = pending;
            if (operation == null) return;
            try
            {
                if (!operation.WriterStarted && (operation.Generation != generation || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || operation.Lease != null && ArsenalEditorActions.EditorBlockReason != null))
                    operation.Cancellation.Cancel();
                if (operation.Cancellation.IsCancellationRequested && !operation.WriterStarted)
                {
                    if (operation.Task != null && !operation.Task.IsCompleted) { Repaint(); return; }
                    if (operation.Task != null && operation.Task.IsFaulted) _ = operation.Task.Exception;
                    FinishPending(operation, "Операция отменена до записи; ассеты не изменялись.");
                    return;
                }
                if (operation.Stage == OperationStage.Dependencies)
                {
                    RequireOperationGuards(operation, operation.Inputs);
                    if (operation.InputIndex < operation.Inputs.Length)
                    {
                        var watch = System.Diagnostics.Stopwatch.StartNew();
                        string input = operation.Inputs[operation.InputIndex++];
                        foreach (string path in ArsenalEditorStatus.DependenciesForInput(input)) operation.Dependencies.Add(path);
                        operation.StageLabel = "Зависимости " + operation.InputIndex + "/" + operation.Inputs.Length + ": " + input + " · " + watch.Elapsed.TotalSeconds.ToString("F2") + " с";
                    }
                    else
                    {
                        operation.ReadPaths = operation.Dependencies.OrderBy(p => p, StringComparer.Ordinal).ToArray();
                        RequireOperationGuards(operation, operation.ReadPaths);
                        StartSnapshot(operation, OperationStage.Prepare);
                    }
                    Repaint(); return;
                }
                if (!operation.Task.IsCompleted) { Repaint(); return; }
                // GetResult вызывается только после IsCompleted: главному потоку нечего ждать.
                var batch = operation.Task.GetAwaiter().GetResult();
                if (operation.Stage == OperationStage.Prepare)
                {
                    RequireOperationGuards(operation, operation.ReadPaths);
                    operation.Cancellation.Token.ThrowIfCancellationRequested();
                    if (operation.Generation != generation) throw new OperationCanceledException();
                    planTitle = operation.Title; planNote = operation.Note; planPaths = operation.Paths;
                    planReadPaths = operation.ReadPaths; planSnapshot = batch.Reads; apply = operation.Action;
                    planHasScene = operation.HasScene; planScene = operation.Scene; planScenePath = operation.ScenePath;
                    planSavesAllAssets = operation.SaveAll;
                    FinishPending(operation, "План подготовлен. Просмотрите область изменения ниже.");
                }
                else if (operation.Stage == OperationStage.Validate)
                {
                    if (!string.IsNullOrEmpty(ArsenalEditorStatus.Diff(operation.Expected, batch.Reads)))
                        throw new InvalidOperationException("Ассеты или их зависимости изменились после подготовки плана. Подготовьте план снова.");
                    RequireOperationGuards(operation, operation.ReadPaths);
                    operation.Cancellation.Token.ThrowIfCancellationRequested();
                    if (operation.Generation != generation) throw new OperationCanceledException();
                    operation.Before = batch.Outputs;
                    operation.WriterStarted = true;
                    operation.StageLabel = "Запись: текущий сборщик не поддерживает отмену после старта";
                    try { operation.WriterReport = operation.Action(); }
                    catch (Exception exception) { operation.WriterError = exception; }
                    StartSnapshot(operation, OperationStage.After);
                }
                else if (operation.Stage == OperationStage.After)
                {
                    string diff = ArsenalEditorStatus.Diff(operation.Before, batch.Outputs);
                    string report = operation.WriterError == null ? operation.WriterReport : "Операция остановлена: " + operation.WriterError.Message;
                    report += "\n" + (operation.WriterError == null ? "Изменённые файлы в заявленной области:\n" : "Изменения до остановки:\n") + (string.IsNullOrEmpty(diff) ? "Нет изменений в заявленной области" : diff);
                    FinishPending(operation, report, operation.WriterError != null);
                }
                else
                {
                    string report;
                    if (operation.Stage == OperationStage.CheckCapture) { checkSnapshot = batch.Outputs; report = "Снимок SHA256 в памяти окна: " + checkSnapshot.Count + " файлов. Assets не записаны."; }
                    else { string diff = ArsenalEditorStatus.Diff(operation.Expected, batch.Outputs); report = string.IsNullOrEmpty(diff) ? "Изменений в снимке нет." : "Изменились файлы:\n" + diff; }
                    FinishPending(operation, report);
                }
            }
            catch (Exception exception)
            {
                // Если writer уже выполнился, ошибка снимка не скрывает его исключение и частичные изменения.
                string report = "Операция остановлена: " + (operation.WriterError == null ? "" : operation.WriterError.Message + "\n") + exception.Message;
                if (operation.WriterStarted) report += "\nWriter уже запускался; полный отчёт изменений недоступен. Автоматического отката нет.";
                FinishPending(operation, report, !(exception is OperationCanceledException));
            }
        }
        private void FinishPending(PendingOperation operation, string report, bool error = false)
        {
            if (pending != operation) return;
            pending = null;
            try { operation.Lease?.Dispose(); }
            catch (Exception exception) { report += "\nНе удалось освободить собственную аренду: " + exception.Message; error = true; }
            finally { operation.Cancellation.Dispose(); }
            result = report;
            SessionState.SetString("VrBattlegrounds.ArsenalEditor.OperationReport", report);
            if (error) VrBattlegrounds.Core.GameLog.Arsenal.Error(report);
            Repaint();
        }
        private void DrawPendingOperation()
        {
            var operation = pending;
            if (operation == null) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(operation.Title, EditorStyles.boldLabel);
            EditorGUILayout.LabelField(operation.StageLabel, EditorStyles.wordWrappedLabel);
            var progress = operation.Progress;
            if (progress != null)
                EditorGUILayout.HelpBox($"Файлы {progress.Files}/{progress.TotalFiles} · {progress.Bytes / 1048576d:F1}/{progress.TotalBytes / 1048576d:F1} МиБ · {progress.Seconds:F1} с\n{progress.Path}", MessageType.Info);
            if (!operation.WriterStarted && GUILayout.Button("Отменить подготовку / проверку")) CancelPlan();
        }
        private void DrawPlan()
        {
            if (apply == null) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Будет изменено: " + planTitle, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(planNote + "\n" + string.Join("\n", planPaths), MessageType.Warning);
            showPlanDependencies = EditorGUILayout.Foldout(showPlanDependencies, "Читаемые зависимости и .meta: " + planReadPaths.Length + " путей", true);
            if (showPlanDependencies) EditorGUILayout.SelectableLabel(string.Join("\n", planReadPaths), EditorStyles.wordWrappedLabel, GUILayout.Height(120));
            using (new EditorGUI.DisabledScope(pending != null || ArsenalEditorActions.BlockReason != null))
                if (GUILayout.Button("Применить показанный план", GUILayout.Height(30))) Try(BeginApply);
            if (GUILayout.Button("Отменить план")) CancelPlan();
        }
        private void Try(Func<string> action)
        {
            try { result = action(); }
            catch (Exception exception) { result = "Операция остановлена: " + exception.Message; VrBattlegrounds.Core.GameLog.Arsenal.Error(result); }
            Repaint();
        }
    }
}
