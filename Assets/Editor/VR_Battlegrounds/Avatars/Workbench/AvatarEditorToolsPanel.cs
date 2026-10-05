using System;
using System.IO;
using System.Linq;
using UltimateXR.Avatar;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor.Avatars.Workbench
{
    public sealed partial class AvatarEditorWindow
    {
        [SerializeField] bool sharedTools, historicalTools, advancedTools;
        [SerializeField] GameObject baseSource, modelSource;
        [SerializeField] Material transplantMaterial;
        [SerializeField] string baseOutput = "Assets/Prefabs/Avatars/NewPlayerBase.prefab", modelChild = "Cyborg";
        [SerializeField] string glovePaths = "", transplantExtra = "";
        [SerializeField] CyborgLegsBuilder.LegsDonor donor;
        [SerializeField] string colorSuffix = "Black", colorName = "US Marine (чёрный)";
        [SerializeField] Color mainColor = Color.black, equipmentColor = TeamColorVariantBuilder.WolfGrey, helmetColor = Color.black;

        void DrawAdditionalPreparation()
        {
            if (context.Kind == AvatarEditorContextKind.Model)
            {
                if (GUILayout.Button("Настроить Blender…")) VRBattlegrounds.Editor.CustomAvatarPipelineMenu.ConfigureBlenderExecutable();
                string blender = EditorPrefs.GetString("VRBattlegrounds.CustomAvatarPipeline.BlenderPath", "");
                Label("Blender", File.Exists(blender) ? blender : "Исполняемый файл не выбран");
                using (new EditorGUI.DisabledScope(!File.Exists(blender)))
                {
                    using (new EditorGUI.DisabledScope(preparationMode != AvatarHandsMode.Sdk))
                        BlenderButton("Удалить родные кисти (SDK)", new[] { "amputate_avatar_hands.py" }, false, false, blender);
                    BlenderButton("Добавить wrist torsion", new[] { "add_wrist_torsion_bones.py" }, false, false, blender);
                    BlenderButton("Добавить глазные кости и mapping", new[] { "add_eye_bones.py" }, true, false, blender);
                    string[] steps = preparationMode == AvatarHandsMode.Sdk
                        ? new[] { "amputate_avatar_hands.py", "add_wrist_torsion_bones.py", "add_eye_bones.py" }
                        : new[] { "add_wrist_torsion_bones.py", "add_eye_bones.py" };
                    BlenderButton("Подготовка FBX в Blender", steps, true, false, blender);
                    BlenderButton("Полный маршрут: копия FBX → Blender → риг UXR", steps, true, true, blender);
                }
                ActionButton("Map Eyes для выбранного FBX", "Только Humanoid mapping и optimizeBones в .meta выбранного FBX. Геометрия не меняется.",
                    () => AvatarScopedActions.Inputs(context), () => AvatarMaintenanceTools.Checked(() => VRBattlegrounds.Editor.ApplyEyeMapping.MapEyes(context.AssetPath)));
            }
            advancedTools = EditorGUILayout.Foldout(advancedTools, "Отдельные шаги UXR и создание вариантов", true);
            if (!advancedTools) return;
            string path = context.AssetPath;
            bool prefab = path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase);
            bool working = !context.IsAsset && workingScene.IsValid() && context.Root.scene == workingScene;
            bool sdkAllowed = CanRunSdkSteps();
            EditorGUILayout.HelpBox("Шаги UXR работают с выбранным собственным prefab или рабочим экземпляром редактора. Для полноценного рига используйте маршрут создания выше.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!prefab && !working))
            {
                WizardButton("1. Core Setup", root => VRBattlegrounds.Editor.CoreAvatarSetup.Setup(root));
                using (new EditorGUI.DisabledScope(!sdkAllowed)) WizardButton("2. Hands Integration (SDK)", root => VRBattlegrounds.Editor.HandsIntegrationSetup.Setup(root));
                WizardButton("3. Controller и камера", root => VRBattlegrounds.Editor.ControllerAndCameraSetup.Setup(root));
                using (new EditorGUI.DisabledScope(!sdkAllowed))
                {
                WizardButton("4. Finalize Rig Mapping (SDK)", root => VRBattlegrounds.Editor.FinalizeRigMappingSetup.Setup(root.GetComponent<UxrAvatar>()));
                WizardButton("Полная настройка UXR выбранной цели (SDK)", root =>
                {
                    VRBattlegrounds.Editor.CoreAvatarSetup.Setup(root);
                    VRBattlegrounds.Editor.HandsIntegrationSetup.Setup(root);
                    VRBattlegrounds.Editor.ControllerAndCameraSetup.Setup(root);
                    var avatar = root.GetComponent<UxrAvatar>();
                    VRBattlegrounds.Editor.FinalizeRigMappingSetup.Setup(avatar);
                    if (!VrBattlegrounds.EditorTools.AvatarFingertipSetup.CanSetup(avatar)) throw new InvalidOperationException("Не настроены fingertip bones.");
                    VrBattlegrounds.EditorTools.AvatarFingertipSetup.Setup(avatar);
                });
                }
                string poses = "Assets/Art/Avatars/" + SafeRigName() + "/HandPoses";
                ActionButton("6. Создать набор поз из клипов", "Новый набор в " + poses + "; существующие позы не перезаписываются.",
                    () => AvatarScopedActions.Inputs(context, poses), () => ConfigureCurrent(root => VRBattlegrounds.Editor.HandPosesSetup.Setup(root, poses, false)));
            }
            baseOutput = EditorGUILayout.TextField("Новый prefab", baseOutput);
            string saveOutput = baseOutput; var saveRoot = context.Root;
            using (new EditorGUI.DisabledScope(!working))
                ActionButton("5. Сохранить рабочий экземпляр как prefab", "Только новый выходной файл " + saveOutput, () => AvatarScopedActions.Inputs(context, saveOutput),
                    () => { AvatarMaintenanceTools.RequireNewOutput(saveOutput); AvatarMaintenanceTools.SaveNew(saveRoot, saveOutput); return "Сохранён рабочий экземпляр: " + saveOutput; });
            baseSource = (GameObject)EditorGUILayout.ObjectField("База игрового варианта", baseSource, typeof(GameObject), false);
            modelSource = (GameObject)EditorGUILayout.ObjectField("Модель Humanoid", modelSource, typeof(GameObject), false);
            using (new EditorGUI.DisabledScope(!baseSource || !modelSource))
            {
                string source = AssetDatabase.GetAssetPath(baseSource), model = AssetDatabase.GetAssetPath(modelSource), output = baseOutput;
                ActionButton("Создать игровой вариант из базы и модели", "Новый вариант " + output + ". База и модель остаются входами. Родные кисти модели; SDK-маршрут создаёт отдельный риг выше.",
                    () => AvatarScopedActions.Inputs(context, source, model, output), () => AvatarMaintenanceTools.CreateVariant(source, model, output));
            }
        }

        string SafeRigName() { try { return AvatarRigPreparation.ValidName(rigName); } catch { return "NewAvatar_Rig"; } }
        internal bool CanRunSdkSteps() => context != null && !context.PrefabChain.Contains(AvatarHandBases.NonSdkHands)
            && (context.PrefabChain.Contains(AvatarHandBases.SdkHands) || report.HandMode.StartsWith("Кисти SDK", StringComparison.Ordinal)
                || (!context.IsAsset && workingScene.IsValid() && context.Root.scene == workingScene && preparationMode == AvatarHandsMode.Sdk
                    && sdkSourcePrepared && !string.IsNullOrEmpty(sdkPreparedSourceGuid) && sdkPreparedSourceGuid == context.AssetGuid));
        void WizardButton(string title, Action<GameObject> setup) => ActionButton(title, "Только выбранный avatar prefab или рабочий экземпляр; без поиска AutoSetupAvatarTarget.",
            () => AvatarScopedActions.Inputs(context, AvatarRigPreparation.IntegrationPath, AvatarRigPreparation.LeftPath, AvatarRigPreparation.RightPath), () => ConfigureCurrent(setup));
        string ConfigureCurrent(Action<GameObject> setup)
        {
            if (context.IsAsset) return AvatarScopedActions.EditPrefab(context.AssetPath, avatar => { setup(avatar.gameObject); return "Шаг UXR выполнен."; });
            if (!workingScene.IsValid() || context.Root.scene != workingScene) throw new InvalidOperationException("Создайте рабочий экземпляр редактора.");
            Undo.RegisterFullObjectHierarchyUndo(context.Root, "Настройка рабочего аватара"); setup(context.Root); return "Шаг UXR выполнен на рабочем экземпляре; сохранение отдельным действием.";
        }
        void BlenderButton(string title, string[] steps, bool eyes, bool rig, string blender)
        {
            using (new EditorGUI.DisabledScope(plan != null || AvatarActionPlan.BlockReason != null))
                if (GUILayout.Button(title))
                    try
                    {
                        string source = context.AssetPath, output = copyPath, name = rigName;
                        var mode = preparationMode; bool poses = generatePoses;
                        var operation = new AvatarBlenderPreparation(source, output, blender, steps);
                        string[] paths = AvatarScopedActions.Inputs(context, steps.Select(s => AvatarBlenderPreparation.Scripts + "/" + s)
                            .Concat(new[] { output, blender, rig ? AvatarRigPreparation.RigPath(name) : null,
                                poses && rig ? AvatarRigPreparation.PoseFolder(name) : null, poses && rig ? "Assets/ThirdParty/UltimateXR/Editor" : null,
                                AvatarRigPreparation.IntegrationPath, AvatarRigPreparation.LeftPath, AvatarRigPreparation.RightPath }).ToArray());
                        plan = new AvatarActionPlan(context, title, "Новая копия " + output + "; шаги: " + string.Join(" → ", steps) + (rig ? "; новый риг " + name : "") + ". Исходный FBX не перезаписывается.", paths,
                            null, null, operation.Start, result => operation.Complete(result, eyes, rig, name, mode, poses), operation.Cleanup,
                            new[] { output, rig ? AvatarRigPreparation.RigPath(name) : output, poses && rig ? AvatarRigPreparation.PoseFolder(name) : output });
                        followSelection = false; error = null; commandReport = null;
                    }
                    catch (Exception e) { error = e.Message; }
        }

        void DrawAdditionalHands()
        {
            string path = context.AssetPath;
            using (new EditorGUI.DisabledScope(!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)))
            {
                ActionButton("Сбросить калибровку hand tracking", "Только calibration arrays выбранного prefab.", () => AvatarScopedActions.Inputs(context), () => AvatarMaintenanceTools.Tracking(path));
                glovePaths = EditorGUILayout.TextField("Пути мешей перчаток (;)", glovePaths);
                string[] gloves = glovePaths.Split(';').Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
                using (new EditorGUI.DisabledScope(gloves.Length == 0))
                    ActionButton("Перепривязать модульные перчатки", "Кости выбранных мешей по единственным совпадениям имени; цель " + path,
                        () => AvatarScopedActions.Inputs(context), () => AvatarMaintenanceTools.RebindGloves(path, gloves));
            }
        }
        void DrawAdditionalBody()
        {
            modelSource = (GameObject)EditorGUILayout.ObjectField("Модель для пересадки", modelSource, typeof(GameObject), false);
            transplantMaterial = (Material)EditorGUILayout.ObjectField("Материал пересадки", transplantMaterial, typeof(Material), false);
            transplantExtra = EditorGUILayout.TextField("Удалить также (,)", transplantExtra);
            using (new EditorGUI.DisabledScope(!modelSource || !transplantMaterial || !context.AssetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)))
            {
                var settings = new SkinnedMeshTransplant.Settings { TargetPrefabPath = context.AssetPath, SourceModel = modelSource, Material = transplantMaterial,
                    ExtraObjectsToRemove = transplantExtra.Split(',').Select(p => p.Trim()).Where(p => p.Length > 0).ToArray() };
                ActionButton("Пересадить skinned meshes", "Только выбранный prefab: геометрия, LOD и renderers; несовпадение костей блокирует запись.",
                    () => AvatarScopedActions.Inputs(context, AssetDatabase.GetAssetPath(modelSource), AssetDatabase.GetAssetPath(transplantMaterial)),
                    () => AvatarMaintenanceTools.Checked(() => SkinnedMeshTransplant.Run(settings)));
            }
        }
        void DrawAdditionalEquipment()
        {
            string path = context.AssetPath;
            using (new EditorGUI.DisabledScope(!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)))
            {
                ActionButton("Экспортировать карманы как общие шаблоны", "Перезапись четырёх шаблонов в Assets/Prefabs/Player/Pockets; выбранный аватар только вход.",
                    () => AvatarScopedActions.Inputs(context, "Assets/Prefabs/Player/Pockets"), () => AvatarMaintenanceTools.ExportPockets(path));
                ActionButton("Refine Back Pocket Proximity", "Выбранный аватар: Anchor_Back → Proximity_Back_R, MaxPlaceDistance=0.2. Рецепт обобщён с Cyborg.",
                    () => AvatarScopedActions.Inputs(context), () => AvatarMaintenanceTools.RefineBack(path));
            }
        }

        void DrawSharedTools()
        {
            sharedTools = EditorGUILayout.Foldout(sharedTools, "Общие сборщики и базы кистей", true);
            if (sharedTools)
            {
                Func<string[]> handPaths = () => AvatarMaintenanceTools.Scope(AvatarHandBases.PlayerBase, AvatarHandBases.SdkHands, AvatarHandBases.NonSdkHands);
                ActionButton("Создать / обновить Hand Bases", "PlayerBase_SdkHands и PlayerBase_NonSdkHands; SDK-позы переносятся из PlayerBase, оригиналы сохраняются.", handPaths,
                    () => AvatarMaintenanceTools.Checked(AvatarHandBases.CreateBases));
                ActionButton("Очистить позы на PlayerBase", "Только PlayerBase. Сначала проверьте, что игровые варианты перенесены на базы кистей.", handPaths,
                    () => AvatarMaintenanceTools.Checked(AvatarHandBases.ClearPlayerBasePoses));
                string path = context?.AssetPath ?? "";
                bool direct = path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) && AvatarLegsSetup.Chain(path).Skip(1).FirstOrDefault() == AvatarHandBases.PlayerBase;
                using (new EditorGUI.DisabledScope(!direct))
                    foreach (string handBase in new[] { AvatarHandBases.SdkHands, AvatarHandBases.NonSdkHands })
                        ActionButton("Перенести выбранный вариант на " + Path.GetFileNameWithoutExtension(handBase), "Только прямой вариант PlayerBase: " + path + ". Глубокие цепочки требуют отдельной миграции.",
                            () => AvatarMaintenanceTools.Scope(path, handBase, AvatarHandBases.PlayerBase), () => AvatarMaintenanceTools.Checked(() => AvatarHandBases.Rebase(path, handBase)));
                Func<string[]> shared = () => AvatarMaintenanceTools.Scope("Assets/Prefabs/Player", "Assets/Data", "Assets/Art/Avatars", "Assets/Prefabs/Managers", "ProjectSettings/TagManager.asset", "ProjectSettings/DynamicsManager.asset");
                ActionButton("Собрать Corpses для командных аватаров", "Командные Avatar prefab, CorpseSource, все трупы, слой и матрица физики. Область шире выбранного аватара.", shared,
                    () => AvatarMaintenanceTools.Checked(CorpseBuilder.Build));
                ActionButton("Собрать Ghost Avatar (Cyborg)", "Cyborg → Ghost; GhostData, AvatarRegistry, стратегия команд и manager prefab.", shared,
                    () => AvatarMaintenanceTools.Checked(GhostAvatarBuilder.Build));
                ActionButton("Полная сборка Hitboxes", "Все владельцы аватаров, projectile masks оружия, трупы, призрак, Layers и матрица физики.",
                    () => AvatarMaintenanceTools.Scope(shared().Concat(new[] { "Assets/Prefabs/Weapons" }).ToArray()), () => AvatarMaintenanceTools.Checked(HitboxBuilder.Build));
                watchReference = (GameObject)EditorGUILayout.ObjectField("Эталон часов", watchReference, typeof(GameObject), false);
                var registry = AssetDatabase.LoadAssetAtPath<AvatarRegistry>("Assets/Data/Player/Avatars/AvatarsRegistry.asset");
                string[] registered = registry ? (registry.avatars ?? Array.Empty<AvatarData>()).Where(d => d && d.prefab).Select(d => AssetDatabase.GetAssetPath(d.prefab)).Distinct().ToArray() : Array.Empty<string>();
                string reference = watchReference ? AssetDatabase.GetAssetPath(watchReference) : "";
                using (new EditorGUI.DisabledScope(registered.Length == 0 || !reference.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)))
                    ActionButton("Установить часы зарегистрированным аватарам", "Только захваченный список " + registered.Length + " prefab; реестр не меняется.", () => AvatarMaintenanceTools.Scope(registered.Concat(new[] { reference, AssetDatabase.GetAssetPath(registry), "Assets/Prefabs/Player/WristWatch_HUD.prefab" }).ToArray()),
                        () => { foreach (string avatar in registered) { AvatarScopedActions.RequireOwnPrefab(avatar); WristWatchInstaller.InstallFor(avatar, AssetDatabase.LoadAssetAtPath<GameObject>(reference)); AvatarScopedActions.NormalizeAndVerify(avatar); } return "Обработано аватаров: " + registered.Length; });
            }
            historicalTools = EditorGUILayout.Foldout(historicalTools, "Модельные рецепты и прежние миграции", true);
            if (!historicalTools) return;
            EditorGUILayout.HelpBox("Эти рецепты рассчитаны на указанные модели. Извлечение и копирование PlayerBase объединены в создание нового каркаса; существующий PlayerBase защищён от перезаписи.", MessageType.Info);
            baseSource = (GameObject)EditorGUILayout.ObjectField("Источник каркаса", baseSource, typeof(GameObject), false);
            baseOutput = EditorGUILayout.TextField("Новый каркас", baseOutput);
            modelChild = EditorGUILayout.TextField("Удалить модель (пусто = копия)", modelChild);
            using (new EditorGUI.DisabledScope(!baseSource))
            {
                string source = baseSource ? AssetDatabase.GetAssetPath(baseSource) : "", output = baseOutput, remove = modelChild;
                ActionButton("Создать независимый каркас", "Новый prefab " + output + "; источник " + source + "; удалить дочерний объект: " + (string.IsNullOrWhiteSpace(remove) ? "нет" : remove),
                    () => AvatarMaintenanceTools.Scope(source, output), () => AvatarMaintenanceTools.CloneBase(source, output, remove));
            }
            donor = (CyborgLegsBuilder.LegsDonor)EditorGUILayout.EnumPopup("Донор ног Cyborg", donor);
            var capturedDonor = donor;
            ActionButton("Build Cyborg Legs", "Только PlayerControllersCyborgAvatar и сгенерированные legs assets; выбран донор " + donor,
                () => AvatarMaintenanceTools.Scope(CyborgLegsBuilder.AvatarPath, CyborgLegsBuilder.OutFolder, "Assets/Prefabs/Player/MEF_Base_Avatar.prefab", "Assets/ThirdParty/UnityStarter_Robot"),
                () => AvatarMaintenanceTools.Checked(() => CyborgLegsBuilder.Build(capturedDonor)));
            EditorGUILayout.LabelField("Team Color Variant · Optimized MEF", EditorStyles.boldLabel);
            colorSuffix = EditorGUILayout.TextField("Суффикс", colorSuffix); colorName = EditorGUILayout.TextField("Название скина", colorName);
            mainColor = EditorGUILayout.ColorField("Одежда", mainColor); equipmentColor = EditorGUILayout.ColorField("Экипировка", equipmentColor); helmetColor = EditorGUILayout.ColorField("Каска", helmetColor);
            string suffix = colorSuffix, displayName = colorName; var main = mainColor; var equipment = equipmentColor; var helmet = helmetColor;
            ActionButton("Создать цветовой вариант MEF", "Источник Optimized_MEF_Player; исходный shader/material, новый материал, variant prefab и AvatarData с суффиксом " + suffix,
                () => AvatarMaintenanceTools.Scope("Assets/Models/Avatars/MEF_Optimized", "Assets/Prefabs/Player", "Assets/Data/Player/Avatars", "Assets/Shaders"),
                () => { AvatarRigPreparation.ValidName(suffix); return AvatarMaintenanceTools.Checked(() => TeamColorVariantBuilder.Build(suffix, displayName, main, equipment, helmet)); });
        }
    }
}
