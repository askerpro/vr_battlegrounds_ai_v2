using System;
using System.Linq;
using System.IO;
using Mirror;
using UltimateXR.Avatar;
using UltimateXR.Editor.Manipulation.HandPoses;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor.Avatars.Workbench
{
    /// <summary>Единое окно аватара; просмотр и перерисовка не запускают writer.</summary>
    public sealed partial class AvatarEditorWindow : EditorWindow
    {
        static readonly string[] Tabs = { "Обзор", "Подготовка", "Руки и позы", "Тело", "Оснащение", "Обслуживание" };
        [SerializeField] UnityEngine.Object selected;
        [SerializeField] bool followSelection = true;
        [SerializeField] int tab;
        [SerializeField] Vector2 scroll;
        AvatarEditorContext context;
        AvatarInspectionReport report;
        string[] legsVariants;
        string reason, error;
        [SerializeField] NetworkManager manager;
        [SerializeField] GameObject watchReference;
        [SerializeField] AnimatorController legsController;
        [SerializeField] AvatarHandsMode preparationMode;
        [SerializeField] string rigName = "NewAvatar_Rig", copyPath = "Assets/Models/Avatars/NewAvatar_Prepared.fbx";
        [SerializeField] bool sdkSourcePrepared, generatePoses;
        [SerializeField] string sdkPreparedSourceGuid;
        AvatarActionPlan plan;
        string commandReport, registryReport;
        bool commandFailed;
        Scene workingScene;
        SceneView workingView;
        public AvatarEditorContext Context => context;
        public AvatarInspectionReport Report => report;
        public int SelectedTab { get => tab; set { tab = Mathf.Clamp(value, 0, Tabs.Length - 1); Repaint(); } }

        [MenuItem("Tools/VR Battlegrounds/Avatars/Редактор аватара", false, 0)]
        public static void Open() => OpenFor(Selection.activeObject);

        public static void OpenFor(UnityEngine.Object target)
        {
            var window = GetWindow<AvatarEditorWindow>("Аватар");
            window.minSize = new Vector2(320, 300);
            if (target) window.SetTarget(target);
            window.Show();
        }

        void OnEnable()
        {
            Selection.selectionChanged += SelectionChanged;
            EditorApplication.projectChanged += RefreshContext;
            EditorApplication.hierarchyChanged += RefreshContext;
            Undo.undoRedoPerformed += RefreshContext;
            EditorApplication.update += UpdatePlan;
            RefreshContext();
        }
        void OnDisable()
        {
            Selection.selectionChanged -= SelectionChanged;
            EditorApplication.projectChanged -= RefreshContext;
            EditorApplication.hierarchyChanged -= RefreshContext;
            Undo.undoRedoPerformed -= RefreshContext;
            EditorApplication.update -= UpdatePlan;
            plan?.Dispose(); plan = null;
            CloseWorkingInstance();
        }
        void SelectionChanged()
        {
            if (followSelection && AvatarEditorContextResolver.TryResolve(Selection.activeObject, out _, out _)) SetTarget(Selection.activeObject);
        }
        public void SetTarget(UnityEngine.Object target) { plan?.Dispose(); plan = null; selected = target; error = null; RefreshContext(); }
        void UpdatePlan()
        {
            if (plan == null) return;
            plan.Poll(context);
            if (plan.Finished)
            {
                commandFailed = !plan.Passed;
                commandReport = plan.Title + "\n" + plan.Scope + "\n" + plan.Status;
                try { commandReport += "\nОтчёт: " + plan.SaveReport(); } catch (Exception e) { commandReport += "\nОтчёт не сохранён: " + e.Message; }
                plan.Dispose(); plan = null; RefreshContext();
            }
            else if (plan.Busy) Repaint();
        }
        public void RefreshContext()
        {
            legsVariants = null;
            if (AvatarEditorContextResolver.TryResolve(selected, out context, out reason)) report = AvatarInspection.Read(context);
            else report = null;
            Repaint();
        }

        void OnGUI()
        {
            EditorGUILayout.LabelField("Редактор аватара", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            var target = EditorGUILayout.ObjectField("Аватар / данные / FBX", selected, typeof(UnityEngine.Object), true);
            if (EditorGUI.EndChangeCheck()) SetTarget(target);
            using (new EditorGUILayout.HorizontalScope())
            {
                followSelection = EditorGUILayout.ToggleLeft("Следовать выделению", followSelection);
                if (GUILayout.Button("Закрепить", GUILayout.Width(90))) followSelection = false;
                if (GUILayout.Button("Обновить", GUILayout.Width(80))) RefreshContext();
            }
            tab = GUILayout.SelectionGrid(tab, Tabs, position.width < 600 ? 3 : 6);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawCatalog();
            if (selected is AvatarRegistry registry) DrawRegistry(registry);
            else if (context == null || !context.IsValid)
            {
                EditorGUILayout.HelpBox(reason ?? "Цель недоступна.", MessageType.Info);
                if (tab == 5) DrawSharedTools();
            }
            else
            {
                EditorGUILayout.LabelField(context.Root.name + " · " + KindLabel(context.Kind), EditorStyles.boldLabel);
                if (!string.IsNullOrEmpty(context.AssetPath)) EditorGUILayout.SelectableLabel(context.AssetPath, EditorStyles.miniLabel, GUILayout.Height(32));
                using (new EditorGUI.DisabledScope(plan != null))
                {
                    if (tab == 0)
                    {
                        DrawOverview();
                        if (GUILayout.Button("Проверить скелет и иерархию")) { commandFailed = false; commandReport = AvatarMaintenanceTools.Skeleton(context.Root); }
                    }
                    else if (tab == 2) { DrawHands(); DrawAdditionalHands(); }
                    else if (tab == 3) { DrawBody(); DrawAdditionalBody(); }
                    else if (tab == 4) { DrawEquipment(); DrawAdditionalEquipment(); }
                    else if (tab == 1) { DrawPreparation(); DrawAdditionalPreparation(); }
                    else { DrawMaintenance(); DrawSharedTools(); }
                }
            }
            if (!string.IsNullOrEmpty(error)) EditorGUILayout.HelpBox(error, MessageType.Error);
            DrawPending();
            DrawPlan();
            EditorGUILayout.EndScrollView();
        }

        void DrawRegistry(AvatarRegistry registry)
        {
            EditorGUILayout.HelpBox("Выберите запись. Открытие реестра не запускает массовую настройку или регистрацию.", MessageType.Info);
            foreach (var data in (registry.avatars ?? Array.Empty<AvatarData>()).Where(d => d))
                if (GUILayout.Button(string.IsNullOrEmpty(data.displayName) ? data.name : data.displayName)) SetTarget(data);
            if (registry.ghost)
            {
                EditorGUILayout.LabelField("Системный аватар выбывшего", EditorStyles.boldLabel);
                if (GUILayout.Button(registry.ghost.name)) SetTarget(registry.ghost);
            }
            manager = (NetworkManager)EditorGUILayout.ObjectField("NetworkManager prefab", manager, typeof(NetworkManager), false);
            if (GUILayout.Button("Проверить регистрацию")) registryReport = AvatarRegistryTools.Validate(registry, manager);
            if (!string.IsNullOrEmpty(registryReport)) EditorGUILayout.HelpBox(registryReport, MessageType.Info);
            string managerPath = manager ? AssetDatabase.GetAssetPath(manager) : "";
            using (new EditorGUI.DisabledScope(!manager || !managerPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)))
                ActionButton("Синхронизировать выбранный manager", "Только spawnPrefabs выбранного manager prefab; существующие записи сохраняются.",
                    () => AvatarRegistryTools.Prefabs(registry).SelectMany(p => AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(p), true))
                        .Concat((registry.avatars ?? Array.Empty<AvatarData>()).Concat(new[] { registry.ghost }).Where(d => d).Select(AssetDatabase.GetAssetPath))
                        .Concat(new[] { AssetDatabase.GetAssetPath(registry), managerPath }).ToArray(), () => AvatarRegistryTools.Synchronize(registry, managerPath));
        }

        void DrawOverview()
        {
            if (context.Data)
            {
                EditorGUILayout.LabelField(context.Data.displayName, EditorStyles.boldLabel);
                if (context.Data.icon) GUILayout.Label(AssetPreview.GetAssetPreview(context.Data.icon) ?? context.Data.icon.texture, GUILayout.Height(100));
                ReadObject("Данные скина", context.Data);
            }
            else if (context.DataCandidates.Length > 1)
            {
                EditorGUILayout.HelpBox("Этот префаб используется несколькими AvatarData. Выберите запись для операций с данными скина.", MessageType.Info);
                foreach (var data in context.DataCandidates) if (GUILayout.Button(data.name)) SetTarget(data);
            }
            Label("Кисти", report.HandMode);
            Label("Renderers / Skinned", report.Renderers.Length + " / " + report.SkinnedMeshes);
            Label("Вершины скинов / слоты материалов", report.Vertices + " / " + report.MaterialSlots);
            Label("LOD groups", report.LodGroups.ToString());
            EditorGUILayout.HelpBox("Состав — диагностика исходников. Это не измерение производительности Quest и не проверка поведения в игре.", MessageType.Info);
            EditorGUILayout.LabelField("Цепочка префабов", EditorStyles.boldLabel);
            foreach (var path in context.PrefabChain) ReadObject(path, AssetDatabase.LoadAssetAtPath<GameObject>(path));
            if (report.Findings.Count == 0) EditorGUILayout.HelpBox("Базовые проверки состава не нашли замечаний. Полная проверка — существующие tests и Unity/шлем.", MessageType.Info);
            foreach (var finding in report.Findings) EditorGUILayout.HelpBox(finding, MessageType.Warning);
            string sourcePath = context.AssetPath;
            using (new EditorGUI.DisabledScope(!context.IsAsset))
                ActionButton("Создать рабочий экземпляр", "Изолированный preview scene и отдельный SceneView; исходный prefab не сохраняется.",
                    () => AvatarScopedActions.Inputs(context), () => CreateWorkingInstance(sourcePath));
        }

        void DrawHands()
        {
            if (!context.Avatar) { EditorGUILayout.HelpBox("Сначала подготовьте UxrAvatar.", MessageType.Info); return; }
            ReadObject("Левая кисть UXR", context.Avatar.AvatarRig?.LeftArm?.Hand?.Wrist);
            ReadObject("Правая кисть UXR", context.Avatar.AvatarRig?.RightArm?.Hand?.Wrist);
            bool canEdit = !context.IsAsset && !EditorApplication.isPlayingOrWillChangePlaymode;
            using (new EditorGUI.DisabledScope(!canEdit))
                if (GUILayout.Button("Открыть редактор поз SDK")) OpenSdkPose(null);
            if (!canEdit) EditorGUILayout.HelpBox("Редактор поз SDK требует экземпляр вне Play Mode. Создайте рабочий экземпляр отдельным действием либо выберите уже открытый аватар.", MessageType.Info);
            foreach (var entry in report.PoseEntries)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    ReadObject(entry.Pose.name, entry.Pose);
                    using (new EditorGUI.DisabledScope(!canEdit))
                        if (GUILayout.Button("Править", GUILayout.Width(65))) OpenSdkPose(entry.Pose);
                }
                EditorGUILayout.LabelField((entry.Inherited ? "Наследование: " : "Источник: ") + entry.OwnerPath, EditorStyles.wordWrappedMiniLabel);
            }
            if (GUILayout.Button("Открыть Hand Pose Review для аватара"))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(context.AssetPath);
                var avatar = prefab ? prefab.GetComponent<UxrAvatar>() : null;
                VrBattlegrounds.Editor.HandPoseReview.HandPoseFitWindow.OpenFor(avatar);
            }
        }

        void DrawBody()
        {
            Label("Владелец тела", report.BodyOwner);
            foreach (var renderer in report.Renderers) ReadObject(renderer.name, renderer);
            DrawComponents("Ноги / хитбоксы / network", "UxrStandardAvatarController", "AvatarStanceFromGrabs", "Hitbox", "NetworkIdentity", "UxrMirrorAvatar");
            string path = context.AssetPath;
            using (new EditorGUI.DisabledScope(!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)))
            {
                ActionButton("Обновить renderers", "Список renderers выбранного prefab; виртуальные кисти интеграции исключаются.", () => AvatarScopedActions.Inputs(context), () => AvatarScopedActions.Renderers(path));
                ActionButton("Пересобрать хитбоксы аватара", "Только выбранный prefab. Layers, projectile masks, corpses и ghost не меняются.",
                    () => AvatarScopedActions.Inputs(context, "ProjectSettings/TagManager.asset"), () => { AvatarScopedActions.RequireOwnPrefab(path); HitboxBuilder.BuildFor(path); AvatarScopedActions.NormalizeAndVerify(path); return "Хитбоксы: " + path; });
            }
            string owner = AvatarLegsSetup.BodyOwnerOf(path);
            Label("Владелец ног", owner ?? "Не найден игровой body owner");
            legsController = (AnimatorController)EditorGUILayout.ObjectField("Контроллер ног", legsController, typeof(AnimatorController), false);
            var controller = legsController;
            using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(owner) || !controller))
                ActionButton("Настроить ноги владельца", "Общая база: " + owner + "\nВарианты наследуют результат; старые компоненты автоматически не удаляются.",
                    () => AvatarScopedActions.Inputs(context, owner, string.IsNullOrEmpty(owner) ? null : AvatarLegsRigBaker.RigPath(owner), AssetDatabase.GetAssetPath(controller)),
                    () => { string result = AvatarLegsSetup.SetupFor(owner, controller); AvatarScopedActions.NormalizeAndVerify(owner); return result; });
        }
        void DrawEquipment()
        {
            DrawComponents("Карманы / UI / часы", "UxrGrabbableObjectAnchor", "UxrMagazinePocket", "UxrFingerTip", "WristDisplay");
            string path = context.AssetPath;
            using (new EditorGUI.DisabledScope(!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)))
            {
                ActionButton("Настроить UI fingertips", "Кончики указательных пальцев выбранного prefab.", () => AvatarScopedActions.Inputs(context), () => AvatarScopedActions.Fingertips(path));
                ActionButton("Добавить / обновить карманы", "Выбранный prefab; общие шаблоны читаются. Проверьте позиции после создания.",
                    () => AvatarScopedActions.Inputs(context, "Assets/Prefabs/Player/Pockets"), () => AvatarScopedActions.Pockets(path));
                watchReference = (GameObject)EditorGUILayout.ObjectField("Эталон часов", watchReference, typeof(GameObject), false);
                var reference = watchReference;
                using (new EditorGUI.DisabledScope(!reference))
                    ActionButton("Установить часы", "Один prefab; поза нормализуется по предплечью выбранного эталона.",
                        () => AvatarScopedActions.Inputs(context, AssetDatabase.GetAssetPath(reference), "Assets/Prefabs/Player/WristWatch_HUD.prefab"), () => { AvatarScopedActions.RequireOwnPrefab(path); string result = WristWatchInstaller.InstallFor(path, reference); AvatarScopedActions.NormalizeAndVerify(path); return result; });
            }
            var data = context.Data;
            using (new EditorGUI.DisabledScope(!data))
                ActionButton("Создать иконку скина", "PNG и поле icon выбранного AvatarData.", () => AvatarScopedActions.Inputs(context,
                    data ? "Assets/Art/Textures/AvatarsIcons/" + Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(data)) + ".png" : null), () => AvatarScopedActions.Icon(data));
        }
        void DrawPreparation()
        {
            EditorGUILayout.HelpBox("Подготовка модели имеет два пути: родные кисти и кисти SDK. Ампутация / BigIKHand допустимы только для пути SDK. Сборка рига и создание игрового варианта — разные этапы.", MessageType.Info);
            ReadObject("Текущий источник", context.Root);
            EditorGUILayout.HelpBox(AvatarRigPreparation.Inspect(context.Root), MessageType.Info);
            string sourcePath = context.AssetPath;
            if (context.Kind == AvatarEditorContextKind.Model)
            {
                copyPath = EditorGUILayout.TextField("Копия FBX", copyPath);
                string outputCopy = copyPath;
                ActionButton("Создать копию FBX для подготовки", "Новый FBX с отдельным GUID; исходный FBX не изменяется.",
                    () => AvatarScopedActions.Inputs(context, outputCopy), () => AvatarRigPreparation.CopyFbx(sourcePath, outputCopy));
                preparationMode = (AvatarHandsMode)EditorGUILayout.EnumPopup("Кисти рига", preparationMode);
                if (preparationMode == AvatarHandsMode.Sdk)
                {
                    EditorGUI.BeginChangeCheck();
                    bool preparedHere = EditorGUILayout.ToggleLeft("Этот источник подготовлен без родной геометрии кистей", sdkSourcePrepared && sdkPreparedSourceGuid == context.AssetGuid);
                    if (EditorGUI.EndChangeCheck()) { sdkSourcePrepared = preparedHere; sdkPreparedSourceGuid = preparedHere ? context.AssetGuid : null; }
                }
                rigName = EditorGUILayout.TextField("Имя нового рига", rigName);
                generatePoses = EditorGUILayout.ToggleLeft("Создать кандидаты поз из пресетов SDK", generatePoses);
                if (preparationMode == AvatarHandsMode.Native && generatePoses)
                    EditorGUILayout.HelpBox("Пресеты SDK — кандидаты для проверки. Для игрового Native варианта используется база NonSdkHands и позы пака.", MessageType.Warning);
                var mode = preparationMode; string name = rigName; bool prepared = sdkSourcePrepared && sdkPreparedSourceGuid == context.AssetGuid, poses = generatePoses;
                try
                {
                    ActionButton("Создать отдельный UXR rig", "Новый prefab рига и, по выбору, папка поз. Игра, реестр и исходная модель не меняются.",
                        () => AvatarScopedActions.Inputs(context, AvatarRigPreparation.RigPath(name), AvatarRigPreparation.PoseFolder(name),
                            AvatarRigPreparation.IntegrationPath, AvatarRigPreparation.LeftPath, AvatarRigPreparation.RightPath,
                            "Assets/ThirdParty/UltimateXR/Editor"), () => AvatarRigPreparation.Build(sourcePath, name, mode, prepared, poses));
                }
                catch (Exception e) { EditorGUILayout.HelpBox(e.Message, MessageType.Info); }
            }
            else EditorGUILayout.HelpBox("Для создания нового рига выберите исходный model asset. Для существующего аватара используйте вкладки Тело и Оснащение.", MessageType.Info);
        }
        void DrawMaintenance()
        {
            EditorGUILayout.HelpBox("Общие сборщики ниже имеют область шире выбранного аватара. Перед применением проверьте цель, входы и выходы в плане.", MessageType.Info);
            string owner = AvatarLegsSetup.BodyOwnerOf(context.AssetPath);
            if (!string.IsNullOrEmpty(owner))
            {
                ActionButton("Миграция прежних ног владельца", "Удаление obsolete legs только из общей базы " + owner + ". После проверки можно включить native IK.",
                    () => AvatarScopedActions.Inputs(context, owner), () => AvatarLegsSetup.CleanOwnerFor(owner));
                // Поиск по реестрам нужен для списка миграции, а не для каждого Layout/Repaint.
                string[] variants = legsVariants ?? (legsVariants = AvatarLegsSetup.VariantsOf(owner));
                foreach (string path in variants) EditorGUILayout.LabelField(path, EditorStyles.wordWrappedMiniLabel);
                using (new EditorGUI.DisabledScope(variants.Length == 0))
                    ActionButton("Миграция вариантов ног", "Удаление obsolete legs и снятие управляемых overrides только перечисленных вариантов.",
                        () => AvatarScopedActions.Inputs(context, variants.Concat(new[] { owner }).ToArray()), () => AvatarLegsSetup.CleanVariantsFor(owner, variants));
            }
            if (GUILayout.Button("Открыть аудит инструментов")) Application.OpenURL(Path.GetFullPath("Docs/audit/avatar-editor-tools-audit-2026-10-04.md"));
        }

        void ActionButton(string title, string scope, Func<string[]> paths, Func<string> action)
        {
            using (new EditorGUI.DisabledScope(plan != null || AvatarActionPlan.BlockReason != null))
                if (GUILayout.Button(title))
                    // Зависимости снимаются только при подготовке команды. Полный SHA/lease gate остаётся в плане.
                    try { error = null; commandReport = null; plan = new AvatarActionPlan(context, title, scope, paths(), action); followSelection = false; }
                    catch (Exception e) { error = e.Message; }
        }
        void DrawPlan()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(AvatarActionPlan.LockStatus, EditorStyles.wordWrappedMiniLabel);
            string blocked = AvatarActionPlan.BlockReason;
            if (blocked != null) EditorGUILayout.HelpBox(blocked, MessageType.Info);
            if (plan != null)
            {
                EditorGUILayout.LabelField(plan.Title, EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(plan.Scope, MessageType.Warning);
                foreach (string path in plan.Paths) EditorGUILayout.LabelField(path, EditorStyles.wordWrappedMiniLabel);
                EditorGUILayout.LabelField(plan.Status, EditorStyles.wordWrappedLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    using (new EditorGUI.DisabledScope(!plan.Ready || blocked != null))
                        if (GUILayout.Button("Применить план"))
                            try { plan.Apply(context); } catch (Exception e) { error = e.Message; }
                    if (GUILayout.Button("Отменить")) { plan.Dispose(); plan = null; }
                }
            }
            if (!string.IsNullOrEmpty(commandReport)) EditorGUILayout.HelpBox(commandReport, commandFailed ? MessageType.Error : MessageType.Info);
        }
        void DrawPending()
        {
            if (PocketZonesPrefabWriter.IsCapturing) EditorGUILayout.LabelField("Capture карманов: снимаю SHA исходников в фоне…");
            if (EditorApplication.isPlaying && context?.Avatar == UxrAvatar.LocalAvatar && context.Avatar != null)
                using (new EditorGUI.DisabledScope(PocketZonesPrefabWriter.HasPending || PocketZonesPrefabWriter.IsCapturing))
                    if (GUILayout.Button("Снять зоны локального аватара"))
                        try { PocketZonesPrefabWriter.Capture(context.Avatar); } catch (Exception e) { error = e.Message; }
            if (!PocketZonesPrefabWriter.HasPending) return;
            EditorGUILayout.HelpBox("Зоны карманов ожидают применения к исходному prefab:\n" + PocketZonesPrefabWriter.PendingPath, MessageType.Info);
            if (PocketZonesPrefabWriter.PendingPaths.Length == 0 && !PocketZonesPrefabWriter.IsCapturing)
                EditorGUILayout.HelpBox("SHA capture не завершился до reload. Значения сохранены; автоматическое применение запрещено. Экспортируйте снимок или снимите новый после отбрасывания.", MessageType.Warning);
            if (GUILayout.Button("Скопировать снимок как JSON")) EditorGUIUtility.systemCopyBuffer = SessionState.GetString("VrBattlegrounds.PendingPocketZones", "");
            using (new EditorGUI.DisabledScope(plan != null || AvatarActionPlan.BlockReason != null))
                if (GUILayout.Button("Подготовить применение зон"))
                {
                    plan = new AvatarActionPlan(null, "Применение зон карманов", "Только MaxPlaceDistance, MaxDistanceGrab и параметры захваченных box colliders исходного prefab.",
                        PocketZonesPrefabWriter.PendingPaths, PocketZonesPrefabWriter.ApplyPending, PocketZonesPrefabWriter.ValidateOrigin);
                    followSelection = false;
                }
            using (new EditorGUI.DisabledScope(plan != null || PocketZonesPrefabWriter.IsCapturing))
                if (GUILayout.Button("Отбросить снимок")) PocketZonesPrefabWriter.Discard();
        }
        void OpenSdkPose(UltimateXR.Manipulation.HandPoses.UxrHandPoseAsset pose)
        {
            try
            {
                using (var lease = VrBattlegrounds.Editor.Arsenal.ArsenalEditorActions.AcquireLease("Открытие редактора поз выбранного аватара"))
                { lease.RequireActive(); UxrHandPoseEditorWindow.Open(context.Avatar, pose); }
            }
            catch (Exception e) { error = e.Message; }
        }
        string CreateWorkingInstance(string sourcePath)
        {
            CloseWorkingInstance();
            workingScene = EditorSceneManager.NewPreviewScene();
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, workingScene);
                if (!instance) throw new InvalidOperationException("Рабочий экземпляр не создан.");
                workingView = CreateInstance<SceneView>();
                workingView.titleContent = new GUIContent("Аватар · экземпляр");
                var customScene = typeof(SceneView).GetProperty("customScene", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (customScene == null) throw new InvalidOperationException("Unity не предоставляет preview SceneView для рабочего экземпляра.");
                customScene.SetValue(workingView, workingScene);
                workingView.Show(); workingView.Frame(new Bounds(instance.transform.position + Vector3.up, Vector3.one * 2), false);
                EditorApplication.delayCall += () => { if (this && instance) SetTarget(instance); };
                return "Создан рабочий экземпляр в изолированной preview scene. Редактор поз SDK сохраняет позы своим штатным действием.";
            }
            catch { CloseWorkingInstance(); throw; }
        }
        void CloseWorkingInstance()
        {
            if (workingView) workingView.Close(); workingView = null;
            if (workingScene.IsValid()) EditorSceneManager.ClosePreviewScene(workingScene);
            workingScene = default;
        }
        void DrawComponents(string label, params string[] types)
        {
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
            foreach (var component in report.Components.Where(c => types.Contains(c.GetType().Name))) ReadObject(component.GetType().Name + " · " + component.name, component);
        }
        static string KindLabel(AvatarEditorContextKind kind) => kind == AvatarEditorContextKind.Prefab ? "префаб" : kind == AvatarEditorContextKind.Model ? "исходная модель" : kind == AvatarEditorContextKind.PrefabStage ? "Prefab Stage" : "экземпляр сцены";
        static void Label(string label, string value) => EditorGUILayout.LabelField(label, value ?? "—", EditorStyles.wordWrappedLabel);
        static void ReadObject(string label, UnityEngine.Object value)
        {
            using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField(label, value, typeof(UnityEngine.Object), true);
        }
    }
}
