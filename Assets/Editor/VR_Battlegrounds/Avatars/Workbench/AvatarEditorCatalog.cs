using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.Avatars.Workbench
{
    public sealed partial class AvatarEditorWindow
    {
        internal sealed class CatalogEntry
        {
            internal readonly string Source, Old, Current;
            internal readonly int Tab;
            internal CatalogEntry(string source, string old, int tab, string current) { Source = source; Old = old; Tab = tab; Current = current; }
        }
        // Один маршрут для каждой из 42 прежних команд. Совпадающие возможности объединены.
        internal static readonly CatalogEntry[] LegacyCommands =
        {
            new CatalogEntry("ApplyEyeMapping.cs", "Map Eyes To FBX", 1, "Map Eyes для выбранного FBX"),
            new CatalogEntry("AvatarFingertipSetup.cs", "Setup Avatar UI Fingertips", 4, "Настроить UI fingertips"),
            new CatalogEntry("AvatarHandBases.cs", "Hand Bases/Create Hand Bases", 5, "Создать / обновить Hand Bases"),
            new CatalogEntry("AvatarHandBases.cs", "Hand Bases/Clear PlayerBase Hand Poses", 5, "Очистить позы на PlayerBase"),
            new CatalogEntry("AvatarHandBases.cs", "Hand Bases/Rebase Selected Avatar → SDK Hands", 5, "Перенести выбранный вариант на PlayerBase_SdkHands"),
            new CatalogEntry("AvatarHandBases.cs", "Hand Bases/Rebase Selected Avatar → Non-SDK Hands", 5, "Перенести выбранный вариант на PlayerBase_NonSdkHands"),
            new CatalogEntry("AvatarIconRenderer.cs", "Render Icon For Selected AvatarData", 4, "Создать иконку скина"),
            new CatalogEntry("AvatarLegsSetup.cs", "Setup Legs", 3, "Настроить ноги владельца"),
            new CatalogEntry("AvatarPocketSetup.cs", "Save Pocket Prefabs from Selected", 4, "Экспортировать карманы как общие шаблоны"),
            new CatalogEntry("AvatarPocketSetup.cs", "Add Weapon Pockets to Selected Avatar", 4, "Добавить / обновить карманы"),
            new CatalogEntry("CheckSkeletons.cs", "Check Skeletons", 0, "Проверить скелет и иерархию"),
            new CatalogEntry("ControllerAndCameraSetup.cs", "UXR Setup Wizard/3. Controller & Camera", 1, "3. Controller и камера"),
            new CatalogEntry("CoreAvatarSetup.cs", "UXR Setup Wizard/1. Core Setup", 1, "1. Core Setup"),
            new CatalogEntry("CorpseBuilder.cs", "Build Corpses", 5, "Собрать Corpses для командных аватаров"),
            new CatalogEntry("CreateBaseAvatars.cs", "Create Base Avatars", 1, "Создать игровой вариант из базы и модели"),
            new CatalogEntry("CreatePrefabSetup.cs", "UXR Setup Wizard/5. Save as Prefab", 1, "5. Сохранить рабочий экземпляр как prefab"),
            new CatalogEntry("CustomAvatarPipelineMenu.cs", "Custom Avatar Pipeline/Configure Blender Executable...", 1, "Настроить Blender…"),
            new CatalogEntry("CustomAvatarPipelineMenu.cs", "Custom Avatar Pipeline/1. Amputate Selected FBX Hands", 1, "Удалить родные кисти (SDK)"),
            new CatalogEntry("CustomAvatarPipelineMenu.cs", "Custom Avatar Pipeline/2. Add Wrist Torsion Bones", 1, "Добавить wrist torsion"),
            new CatalogEntry("CustomAvatarPipelineMenu.cs", "Custom Avatar Pipeline/3. Add Eye Bones And Map Humanoid", 1, "Добавить глазные кости и mapping"),
            new CatalogEntry("CustomAvatarPipelineMenu.cs", "Custom Avatar Pipeline/4. Create Scene Target From Selected FBX", 0, "Создать рабочий экземпляр"),
            new CatalogEntry("CustomAvatarPipelineMenu.cs", "Custom Avatar Pipeline/5. Run UXR Setup On Current Target", 1, "Полная настройка UXR выбранной цели (SDK)"),
            new CatalogEntry("CustomAvatarPipelineMenu.cs", "Custom Avatar Pipeline/Run Blender Preparation Only", 1, "Подготовка FBX в Blender"),
            new CatalogEntry("CustomAvatarPipelineMenu.cs", "Custom Avatar Pipeline/Run Full Selected FBX Pipeline", 1, "Полный маршрут: копия FBX → Blender → риг UXR"),
            new CatalogEntry("CyborgLegsBuilder.cs", "Build Cyborg Legs", 5, "Build Cyborg Legs"),
            new CatalogEntry("ExtractPlayerBase.cs", "Extract Player Base", 5, "Создать независимый каркас"),
            new CatalogEntry("FinalizeRigMappingSetup.cs", "UXR Setup Wizard/4. Finalize Rig Mapping", 1, "4. Finalize Rig Mapping (SDK)"),
            new CatalogEntry("FixAvatarRenderers.cs", "Fix Avatar Renderers", 3, "Обновить renderers"),
            new CatalogEntry("FixHandTrackingCache.cs", "Fix Hand Tracking", 2, "Сбросить калибровку hand tracking"),
            new CatalogEntry("GhostAvatarBuilder.cs", "Build Ghost Avatar", 5, "Собрать Ghost Avatar (Cyborg)"),
            new CatalogEntry("HandPoseFitWindow.cs", "Hand Pose Review", 2, "Открыть Hand Pose Review для аватара"),
            new CatalogEntry("HandPosesSetup.cs", "UXR Setup Wizard/6. Generate Default Poses", 1, "6. Создать набор поз из клипов"),
            new CatalogEntry("HandsIntegrationSetup.cs", "UXR Setup Wizard/2. Hands Integration", 1, "2. Hands Integration (SDK)"),
            new CatalogEntry("HitboxBuilder.cs", "Build Hitboxes", 5, "Полная сборка Hitboxes"),
            new CatalogEntry("InspectAvatar.cs", "Inspect Cyborg Avatar", 0, "Проверить скелет и иерархию"),
            new CatalogEntry("ModularGloveBoneMapper.cs", "UXR Setup Wizard/Modular Glove Bone Mapper", 2, "Перепривязать модульные перчатки"),
            new CatalogEntry("PlayerBasePrefabBuilder.cs", "Create PlayerBase from CyborgAvatar", 5, "Создать независимый каркас"),
            new CatalogEntry("PocketZonesPrefabWriter.cs", "Save Pocket Zones To Prefab", 4, "Снять зоны локального аватара → Подготовить применение зон"),
            new CatalogEntry("RefineBackPocket.cs", "Refine Back Pocket Proximity", 4, "Refine Back Pocket Proximity"),
            new CatalogEntry("SkinnedMeshTransplant.cs", "Transplant Skinned Meshes...", 3, "Пересадить skinned meshes"),
            new CatalogEntry("TeamColorVariantBuilder.cs", "Team Color Variant/Build Optimized MEF Black", 5, "Создать цветовой вариант MEF"),
            new CatalogEntry("WristWatchInstaller.cs", "Install Wrist Watch (registered avatars)", 5, "Установить часы зарегистрированным аватарам")
        };
        [SerializeField] bool showCatalog;
        [SerializeField] string catalogSearch = "";
        void DrawCatalog()
        {
            showCatalog = EditorGUILayout.Foldout(showCatalog, "Найти прежнюю команду меню (42)", true);
            if (!showCatalog) return;
            catalogSearch = EditorGUILayout.TextField("Поиск", catalogSearch);
            foreach (var entry in LegacyCommands.Where(e => (e.Old + e.Current).IndexOf(catalogSearch ?? "", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                EditorGUILayout.LabelField(entry.Old, EditorStyles.wordWrappedMiniLabel);
                if (GUILayout.Button(Tabs[entry.Tab] + " → " + entry.Current))
                { tab = entry.Tab; sharedTools = historicalTools = advancedTools = true; showCatalog = false; scroll = Vector2.zero; }
            }
        }
    }
}
