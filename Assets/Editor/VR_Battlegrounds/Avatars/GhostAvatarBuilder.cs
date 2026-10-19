using System.Collections.Generic;
using System.Linq;
using Mirror;
using UltimateXR.Avatar;
using UltimateXR.Manipulation;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player;
using VrBattlegrounds.Player.Avatars;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    /// Собирает аватар выбывшего — призрака (T-35) — самостоятельным префабом и регистрирует его.
    /// Призрак — обычный аватар, отличающийся набором частей: своего типа у него нет, выбывший
    /// игрок определяется по сессии (<c>PlayerSession.IsEliminated</c>). Префаб не правится
    /// руками: всё, чем призрак отличается от киборга, задано здесь, и повторная сборка
    /// приводит его к этому виду.
    ///
    /// <list type="bullet">
    /// <item><b>Снимается снаряжение и игра живого:</b> <c>PlayerLoadoutManager</c>,
    ///       <c>MagazineEjectInput</c>, карманы и кобуры (якоря и их
    ///       ручки-прокси), любые хватаемые предметы на теле.</item>
    /// <item><b>Снимаются хитбоксы</b> — все сплошные коллайдеры. Триггер камеры остаётся: по нему
    ///       зона команды видит, что игрок пришёл (возрождение в Elimination).</item>
    /// <item><b>Тело — на материале призрака</b> (<c>Prefabs/Player/Avatars/Ghost/GhostMaterial.mat</c>);
    ///       цвет команды ставит <see cref="TeamColorTint"/> в рантайме. Старая копия тела
    ///       <c>Cyborg/Ghost</c> удаляется.</item>
    /// <item><b>Вид выбывшего</b> у своего игрока — <see cref="GhostViewEffect"/>.</item>
    /// <item><b>Регистрация:</b> <c>Data/Player/Avatars/Ghost.asset</c> → <c>AvatarRegistry.ghost</c>
    ///       (не список скинов), <c>TeamAvatarStrategy.ghostPrefab</c>, <c>spawnPrefabs</c>.</item>
    /// </list>
    /// Проверка — <c>GhostAvatarTests</c>.
    /// </summary>
    public static class GhostAvatarBuilder
    {
        public const string GhostPath = "Assets/Prefabs/Player/Avatars/Ghost/GhostAvatar.prefab";

        private const string SourcePath = "Assets/Prefabs/Player/Avatars/CyborgHands/Cyborg/PlayerControllersCyborgAvatar.prefab";
        private const string MaterialPath = "Assets/Prefabs/Player/Avatars/Ghost/GhostMaterial.mat";
        private const string DataPath = "Assets/Data/Player/Avatars/Ghost.asset";
        private const string SourceDataPath = "Assets/Data/Player/Avatars/Cyborg.asset";
        private const string RegistryPath = "Assets/Data/Player/Avatars/AvatarsRegistry.asset";
        private const string StrategyPath = "Assets/Data/Player/Avatars/TeamAvatarStrategy.asset";
        private const string ManagersPath = "Assets/Prefabs/Managers/--- MANAGERS ---.prefab";
        private const string TabletPath = "Assets/Prefabs/UI/Menu/Tablet/Tablet_Base.prefab";
        private const string CyborgHandsBaseGuid = "71b74b0d08444f49bbb7aa2828d0890a";

        // Только жесты ввода/UI и хват планшета; оружейные позы живого аватара не наследуются.
        private static readonly string[] GhostPoseGuids =
        {
            "3f8364620cf31c447a54358fb66bfd35", // Default
            "56f373d38c0d6d1469a8e33faa184670", // Pointing
            "44ea1f76294e38d47885b28c3a3438bd", // Grab
            "50e73c61f1d966d409da705792e92e86", // GrabStandard
        };

        /// <summary>Компоненты живого игрока, которых у призрака нет. Порядок — сначала зависимые.</summary>
        private static readonly string[] LivingOnlyComponents =
        {
            "MagazineEjectInput", "PlayerLoadoutManager",
        };

        public static void Build()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (source == null || material == null)
            {
                GameLog.Player.Error($"[GhostAvatarBuilder] Нет {SourcePath} или {MaterialPath}.");
                return;
            }

            var poses = GhostPoseGuids.Select(guid =>
                AssetDatabase.LoadAssetAtPath<UxrHandPoseAsset>(AssetDatabase.GUIDToAssetPath(guid))).ToArray();
            if (poses.Any(pose => pose == null))
                throw new System.InvalidOperationException("Не найдены обязательные позы ввода/UI призрака.");

            string ghostGuid = AssetDatabase.AssetPathToGUID(GhostPath);
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(GhostPath);
            long rootFileId = 0;
            if (existing != null && !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(existing, out ghostGuid, out rootFileId))
                throw new System.InvalidOperationException("Не удалось прочитать identity существующего призрака.");

            var previewScene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source, previewScene);
            GameObject prefab;
            try
            {
                instance.name = "GhostAvatar";
                Strip(instance);
                // OutermostRoot сохраняет вложенные model/device-prefab. У варианта после
                // распаковки остаётся instance базы, поэтому снимаем только цепочку корня.
                for (int depth = 0; PrefabUtility.IsPartOfPrefabInstance(instance); ++depth)
                {
                    if (depth >= 4)
                        throw new System.InvalidOperationException("Неожиданная глубина prefab-цепочки призрака.");
                    PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
                }
                ConfigureStandaloneAvatar(instance, ghostGuid, poses);
                Paint(instance, material);
                if (instance.GetComponent<TeamColorTint>() == null) instance.AddComponent<TeamColorTint>();
                if (instance.GetComponent<GhostViewEffect>() == null) instance.AddComponent<GhostViewEffect>();

                prefab = VrBattlegrounds.Editor.Avatars.Workbench.AvatarMaintenanceTools.SavePrefab(instance, GhostPath);
                // При первом создании GUID появляется только после SavePrefab. Существующий
                // файл не удаляется: его GUID и внешние ссылки на корень должны сохраниться.
                if (string.IsNullOrEmpty(ghostGuid))
                {
                    ConfigureStandaloneAvatar(prefab, AssetDatabase.AssetPathToGUID(GhostPath), poses);
                    PrefabUtility.SavePrefabAsset(prefab, out bool saved);
                    if (!saved) throw new System.InvalidOperationException("Не сохранена SDK identity призрака.");
                }
                if (PrefabUtility.GetPrefabAssetType(prefab) != PrefabAssetType.Regular ||
                    !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(prefab, out string savedGuid, out long savedRootFileId) ||
                    (existing != null && (savedGuid != ghostGuid || savedRootFileId != rootFileId)))
                    throw new System.InvalidOperationException("Не сохранены самостоятельный prefab и identity корня призрака.");
                var savedAvatar = prefab.GetComponent<UxrAvatar>();
                if (savedAvatar == null || savedAvatar.ParentAvatarPrefab != null ||
                    !savedAvatar.GetPrefabGuidChain().SequenceEqual(new[] { savedGuid }) ||
                    !savedAvatar.GetHandPoses().Select(pose => AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(pose)))
                        .OrderBy(guid => guid).SequenceEqual(GhostPoseGuids.OrderBy(guid => guid)))
                    throw new System.InvalidOperationException("SDK-цепочка или локальные позы призрака не соответствуют самостоятельному аватару.");
            }
            finally
            {
                Object.DestroyImmediate(instance);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(previewScene);
            }

            EnsureTabletGripBindings(prefab);
            Register(prefab);
            AssetDatabase.SaveAssets();
            GameLog.Player.Info($"[GhostAvatarBuilder] Призрак собран: {GhostPath}.");
        }

        private static void ConfigureStandaloneAvatar(GameObject root, string guid, UxrHandPoseAsset[] poses)
        {
            var avatar = root.GetComponent<UxrAvatar>();
            if (avatar == null) throw new System.InvalidOperationException("На призраке отсутствует UxrAvatar.");
            var serialized = new SerializedObject(avatar);
            serialized.FindProperty("_prefabGuid").stringValue = guid;
            serialized.FindProperty("_parentPrefab").objectReferenceValue = null;
            var handPoses = serialized.FindProperty("_handPoses");
            handPoses.arraySize = poses.Length;
            for (int i = 0; i < poses.Length; ++i)
                handPoses.GetArrayElementAtIndex(i).objectReferenceValue = poses[i];
            serialized.FindProperty("_defaultHandPose").objectReferenceValue = poses[0];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureTabletGripBindings(GameObject ghost)
        {
            string ghostGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(ghost));
            var tablet = AssetDatabase.LoadAssetAtPath<GameObject>(TabletPath);
            if (tablet == null || string.IsNullOrEmpty(ghostGuid) ||
                !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(tablet, out string tabletGuid, out long tabletRootFileId))
                throw new System.InvalidOperationException("Не найдены префабы/identity для хвата планшета призраком.");

            var grabbables = tablet.GetComponentsInChildren<UxrGrabbableObject>(true);
            if (grabbables.Length != 1 || grabbables[0].GrabPointCount != 2 ||
                AssetDatabase.GetAssetPath(grabbables[0]) != TabletPath)
                throw new System.InvalidOperationException("Ожидались один собственный UxrGrabbableObject и две точки Tablet_Base.");
            var grab = grabbables[0];
            var points = Enumerable.Range(0, grab.GrabPointCount).Select(grab.GetGrabPoint).ToArray();
            // Все источники проверяются до записи; Single также выявляет дубликаты GUID.
            var sources = points.Select(point => point.AvatarGripPoseEntries.Single(entry => entry.AvatarPrefabGuid == CyborgHandsBaseGuid)).ToArray();
            var targets = points.Select(point => point.AvatarGripPoseEntries.SingleOrDefault(entry => entry.AvatarPrefabGuid == ghostGuid)).ToArray();
            if (sources.Any(entry => entry.HandPose == null || entry.GripAlignTransformHandLeft == null || entry.GripAlignTransformHandRight == null))
                throw new System.InvalidOperationException("У исходного хвата планшета отсутствует поза или выравнивание кисти.");

            bool changed = false;
            for (int i = 0; i < points.Length; ++i)
            {
                var source = sources[i];
                var target = targets[i];
                if (target != null && target.HandPose == source.HandPose && target.PoseBlendValue == source.PoseBlendValue &&
                    target.GripAlignTransformHandLeft == source.GripAlignTransformHandLeft && target.GripAlignTransformHandRight == source.GripAlignTransformHandRight)
                    continue;
                points[i].CheckAddGripPoseInfo(ghostGuid);
                target = points[i].AvatarGripPoseEntries.Single(entry => entry.AvatarPrefabGuid == ghostGuid);
                // Прочие GUID-записи и fallback не меняются. Blend и обе align-ссылки
                // копируются отдельно для каждой точки, без округления и пересчёта.
                target.HandPose = source.HandPose;
                target.PoseBlendValue = source.PoseBlendValue;
                target.GripAlignTransformHandLeft = source.GripAlignTransformHandLeft;
                target.GripAlignTransformHandRight = source.GripAlignTransformHandRight;
                changed = true;
            }
            if (!changed) return;

            if (PrefabUtility.IsPartOfPrefabInstance(grab)) PrefabUtility.RecordPrefabInstancePropertyModifications(grab);
            EditorUtility.SetDirty(grab);
            PrefabUtility.SavePrefabAsset(tablet, out bool saved);
            if (!saved || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(tablet, out string savedGuid, out long savedRootFileId) ||
                savedGuid != tabletGuid || savedRootFileId != tabletRootFileId)
                throw new System.InvalidOperationException("Не сохранены привязки/identity планшета для призрака.");
        }

        private static void Strip(GameObject root)
        {
            foreach (string typeName in LivingOnlyComponents)
            {
                Component c = root.GetComponent(typeName);
                if (c != null) Object.DestroyImmediate(c);
            }

            // Карманы и кобуры — целиком, с ручками-прокси; прочие хватаемые предметы на теле.
            foreach (UxrGrabbableObjectAnchor anchor in root.GetComponentsInChildren<UxrGrabbableObjectAnchor>(true))
            {
                if (anchor != null) Object.DestroyImmediate(anchor.gameObject);
            }
            foreach (UxrGrabbableObject grabbable in root.GetComponentsInChildren<UxrGrabbableObject>(true))
            {
                if (grabbable != null) Object.DestroyImmediate(grabbable.gameObject);
            }

            // Хитбоксы (T-36) — объектами целиком, затем прочие сплошные коллайдеры.
            foreach (Hitbox hitbox in root.GetComponentsInChildren<Hitbox>(true))
            {
                if (hitbox != null) Object.DestroyImmediate(hitbox.gameObject);
            }
            foreach (Collider collider in root.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger).ToList())
            {
                Object.DestroyImmediate(collider);
            }

            // Старая копия тела-призрака внутри киборга.
            Transform legacy = root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(t => t.name == "Ghost" && t.parent != null && t.parent.name == "Cyborg");
            if (legacy != null) Object.DestroyImmediate(legacy.gameObject);
        }

        private static void Paint(GameObject root, Material material)
        {
            foreach (SkinnedMeshRenderer r in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (r.GetComponentInParent<UxrHandIntegration>(true) != null) continue;
                r.sharedMaterials = Enumerable.Repeat(material, r.sharedMaterials.Length).ToArray();
            }
        }

        private static void Register(GameObject prefab)
        {
            var data = AssetDatabase.LoadAssetAtPath<AvatarData>(DataPath);
            if (data == null)
            {
                data = ScriptableObject.CreateInstance<AvatarData>();
                AssetDatabase.CreateAsset(data, DataPath);
            }
            var sourceData = AssetDatabase.LoadAssetAtPath<AvatarData>(SourceDataPath);
            data.displayName = "Призрак";
            data.icon = sourceData != null ? sourceData.icon : null;
            data.prefab = prefab;
            EditorUtility.SetDirty(data);

            var registry = AssetDatabase.LoadAssetAtPath<AvatarRegistry>(RegistryPath);
            registry.ghost = data;
            EditorUtility.SetDirty(registry);

            var strategy = new SerializedObject(AssetDatabase.LoadAssetAtPath<TeamAvatarStrategy>(StrategyPath));
            strategy.FindProperty("ghostPrefab").objectReferenceValue = prefab;
            strategy.ApplyModifiedPropertiesWithoutUndo();

            GameObject managers = PrefabUtility.LoadPrefabContents(ManagersPath);
            try
            {
                NetworkManager manager = managers.GetComponentInChildren<NetworkManager>(true);
                if (manager != null && !manager.spawnPrefabs.Contains(prefab))
                {
                    manager.spawnPrefabs.Add(prefab);
                    VrBattlegrounds.Editor.Avatars.Workbench.AvatarMaintenanceTools.SavePrefab(managers, ManagersPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(managers);
            }
        }
    }
}
