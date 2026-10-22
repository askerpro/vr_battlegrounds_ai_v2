using System.Collections.Generic;
using System.Linq;
using UltimateXR.Avatar;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    /// Две базы игровых аватаров по типу кисти — варианты <c>PlayerBase</c>:
    /// <list type="bullet">
    ///   <item><see cref="SdkHands" /> — общая база поз кистей Cyborg без собственного рига и геометрии.
    ///   Конкретный риг принадлежит дочернему аватару.</item>
    ///   <item><see cref="NonSdkHands" /> — чужой скелет (3 фаланги, как у MEF). Позы — только нейтральные к
    ///   скелету, из пака (<see cref="HandsPackPoseImporter" />).</item>
    /// </list>
    /// Сам <c>PlayerBase</c> — сетевой каркас без поз кисти. Проверка — <c>AvatarHandPoseChainTests</c>.
    /// </summary>
    public static class AvatarHandBases
    {
        public const string PlayerBase  = "Assets/Prefabs/Player/Avatars/Bases/PlayerBase.prefab";
        public const string SdkHands    = "Assets/Prefabs/Player/Avatars/CyborgHands/Base/CyborgHandsBase.prefab";
        public const string NonSdkHands = "Assets/Prefabs/Player/Avatars/Bases/PlayerBase_NonSdkHands.prefab";

        private const string HandPoses = "_handPoses";

        /// <summary>
        /// Проверяет подготовленную миграцией базу Cyborg и создаёт базу Non-SDK при необходимости.
        /// Позы Cyborg остаются в канонической базе; пустая замена этой базы здесь не создаётся.
        /// </summary>
        public static void CreateBases()
        {
            GameObject playerBase = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerBase);
            RequireCyborgBase();

            CreateVariant(NonSdkHands, playerBase);

            // Позы пака на NonSdkHands регистрирует импортёр.
            SetPoses(NonSdkHands, OwnPoses(AssetDatabase.LoadAssetAtPath<GameObject>(NonSdkHands)).Where(p => AssetDatabase.GetAssetPath(p).StartsWith(HandsPackPoseImporter.PoseFolder)));

            AssetDatabase.SaveAssets();
            GameLog.Debug.Info("[AvatarHandBases] База Cyborg проверена, база Non-SDK обновлена.");
        }

        /// <summary>
        /// Проверяет контракт общей базы Cyborg без исключений: GUID, прямой вариант <c>PlayerBase</c> (Unity и UXR),
        /// нет рига и тела (<c>Cyborg</c>, <c>MEF_Rig</c>, <c>CyborgGeo</c>), есть собственные позы.
        /// <paramref name="reason" /> — первое нарушение или null. Нужна находкам Редактора аватара и операциям с базами.
        /// </summary>
        public static bool IsCleanCyborgBase(out string reason)
        {
            GameObject playerBase = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerBase);
            GameObject cyborgBase = AssetDatabase.LoadAssetAtPath<GameObject>(SdkHands);
            UxrAvatar avatar = cyborgBase != null ? cyborgBase.GetComponent<UxrAvatar>() : null;
            reason = playerBase == null ? "нет PlayerBase " + PlayerBase
                : cyborgBase == null ? "нет CyborgHandsBase " + SdkHands
                : avatar == null ? "у CyborgHandsBase нет корневого UxrAvatar"
                : AssetDatabase.AssetPathToGUID(SdkHands) != GripTargetProfile.CyborgBaseGuid ? "GUID CyborgHandsBase не совпадает с " + GripTargetProfile.CyborgBaseGuid
                : PrefabUtility.GetCorrespondingObjectFromSource(cyborgBase) != playerBase ? "Unity-родитель CyborgHandsBase — не PlayerBase"
                : avatar.ParentPrefab != playerBase ? "UXR-родитель (_parentPrefab) CyborgHandsBase — не PlayerBase"
                : avatar.AvatarRig.Transforms.Any() ? "у CyborgHandsBase заполнен риг аватара"
                : cyborgBase.transform.Find("Cyborg") != null || cyborgBase.transform.Find("MEF_Rig") != null ||
                  cyborgBase.transform.Find("CyborgGeo") != null ? "в CyborgHandsBase есть тело (Cyborg / MEF_Rig / CyborgGeo)"
                : !avatar.GetHandPoses().Any() ? "у CyborgHandsBase нет собственных поз кисти"
                : null;
            return reason == null;
        }

        private static void RequireCyborgBase()
        {
            if (!IsCleanCyborgBase(out string reason))
            {
                throw new System.InvalidOperationException(
                    "[AvatarHandBases] Нужна существующая CyborgHandsBase с сохранёнными позами, без рига и геометрии тела, " +
                    "прямой вариант PlayerBase: " + reason + ". Исправление — вручную или из истории Git (миграция 9c79ccb58).");
            }
        }

        /// <summary>
        /// Убирает позы кисти с <c>PlayerBase</c>. Запускать после переноса аватаров на базы: вариант, всё ещё
        /// наследующий <c>PlayerBase</c> напрямую, потерял бы унаследованные элементы списка поз.
        /// </summary>
        public static void ClearPlayerBasePoses()
        {
            RequireCyborgBase();
            // Элемент списка варианта, равный элементу родителя, Unity не хранит как переопределение — он пропал бы
            // вместе с родительским. Поэтому списки баз снимаются до очистки и записываются заново после.
            var bases = new[] { SdkHands, NonSdkHands }.ToDictionary(p => p, p => OwnPoses(AssetDatabase.LoadAssetAtPath<GameObject>(p)));

            SetPoses(PlayerBase, Enumerable.Empty<UxrHandPoseAsset>());
            foreach (var (path, poses) in bases.Select(b => (b.Key, b.Value)))
            {
                SetPoses(path, poses);
            }

            AssetDatabase.SaveAssets();
        }

        private static void RebaseSelectedToSdk() => RebaseSelected(SdkHands);

        private static void RebaseSelectedToNonSdk() => RebaseSelected(NonSdkHands);

        private static void RebaseSelected(string basePath)
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeGameObject);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".prefab"))
            {
                GameLog.Error("[AvatarHandBases] Выдели префаб аватара в окне Project.");
                return;
            }

            Rebase(path, basePath);
        }

        /// <summary>
        /// Переподчиняет существующий аватар через Unity API. Общий конвейер сохраняет
        /// разрешённые позы, проверяет внешние ссылки и возвращает исходник при отказе.
        /// </summary>
        public static void Rebase(string variantPath, string basePath)
        {
            CyborgHandsPrefabMigration.ReparentVariant(variantPath, basePath);
            GameLog.Debug.Info($"[AvatarHandBases] {variantPath} → наследник {basePath}");
        }

        private const long IdMask = 0x7FFFFFFFFFFFFFFF;

        /// <summary>
        /// Переносит вариант со старого родителя (<paramref name="oldGuid" />) на новый — вариант старого, чей
        /// экземпляр старого родителя имеет id <paramref name="newBaseInstanceId" />.
        ///
        /// <para>
        /// Объект, унаследованный через экземпляр префаба, получает id <c>(id в источнике ^ id экземпляра) &amp; 0x7FFF…</c>.
        /// Объект старого родителя <c>X</c> в новой базе — <c>(X ^ P) &amp; mask</c>, поэтому ссылки
        /// <c>{fileID: X, guid: old}</c> переписываются в <c>{fileID: X ^ P, guid: new}</c>. А сам экземпляр получает
        /// id <c>I ^ P</c>: тогда унаследованный объект варианта — <c>(X ^ P) ^ (I ^ P) = X ^ I</c>, тот же, что был.
        /// Id всех объектов варианта не меняются, и внешние ссылки на них (сцены, данные аватаров, оружие) остаются
        /// целыми. Формула сначала проверяется на всех stripped-объектах файла — при расхождении ничего не пишется.
        /// </para>
        /// </summary>
        [System.Obsolete("Исторический helper для совместимости. Для миграции используйте CyborgHandsPrefabMigration.ReparentVariant().")]
        public static string RebaseYaml(string yaml, string oldGuid, string newGuid, long newBaseInstanceId)
        {
            long instanceId    = SourceInstanceId(yaml, oldGuid);
            long newInstanceId = (instanceId ^ newBaseInstanceId) & IdMask;
            CheckStrippedIds(yaml, oldGuid, instanceId);

            // Ссылки на объекты старого родителя → объекты новой базы. Корень-ассет (100100000) — просто смена guid.
            string result = System.Text.RegularExpressions.Regex.Replace(yaml, @"\{fileID: (-?\d+), guid: " + oldGuid + @", type: 3\}", m =>
            {
                long source = long.Parse(m.Groups[1].Value);
                long mapped = source == 100100000 ? source : (source ^ newBaseInstanceId) & IdMask;
                return $"{{fileID: {mapped}, guid: {newGuid}, type: 3}}";
            });

            result = RenameInstance(result, instanceId, newInstanceId);
            CheckStrippedIds(result, newGuid, newInstanceId);
            return result;
        }

        private static string RenameInstance(string yaml, long from, long to)
        {
            string result = System.Text.RegularExpressions.Regex.Replace(yaml, @"(^--- !u!1001 &)" + from + @"\b", m => m.Groups[1].Value + to,
                                                                        System.Text.RegularExpressions.RegexOptions.Multiline);
            return result.Replace($"{{fileID: {from}}}", $"{{fileID: {to}}}");
        }

        /// <summary>Каждый stripped-объект экземпляра <paramref name="instanceId" /> имеет id <c>(источник ^ экземпляр) &amp; mask</c>.</summary>
        private static void CheckStrippedIds(string yaml, string sourceGuid, long instanceId)
        {
            var block = new System.Text.RegularExpressions.Regex(
                @"^--- !u!\d+ &(-?\d+) stripped\r?\n(?:(?!^---).*\r?\n)*?\s*m_CorrespondingSourceObject: \{fileID: (-?\d+), guid: " + sourceGuid + @", type: 3\}\r?\n(?:(?!^---).*\r?\n)*?\s*m_PrefabInstance: \{fileID: (-?\d+)\}",
                System.Text.RegularExpressions.RegexOptions.Multiline);
            foreach (System.Text.RegularExpressions.Match m in block.Matches(yaml))
            {
                long id = long.Parse(m.Groups[1].Value), source = long.Parse(m.Groups[2].Value), owner = long.Parse(m.Groups[3].Value);
                if (owner == instanceId && ((source ^ instanceId) & IdMask) != id)
                {
                    throw new System.InvalidOperationException($"id stripped-объекта {id} не выводится из источника {source} и экземпляра {instanceId}: формула Unity не подтвердилась, файл не тронут.");
                }
            }
        }

        /// <summary>id экземпляра префаба <paramref name="sourceGuid" /> в YAML (единственный).</summary>
        private static long SourceInstanceId(string yaml, string sourceGuid)
        {
            var instances = System.Text.RegularExpressions.Regex.Matches(yaml,
                @"^--- !u!1001 &(-?\d+)\r?\n(?:(?!^---).*\r?\n)*?\s*m_SourcePrefab: \{fileID: 100100000, guid: " + sourceGuid + @", type: 3\}",
                System.Text.RegularExpressions.RegexOptions.Multiline);
            if (instances.Count != 1)
            {
                throw new System.InvalidOperationException($"Ожидался один экземпляр префаба {sourceGuid}, найдено {instances.Count}.");
            }

            return long.Parse(instances[0].Groups[1].Value);
        }

        private static void CreateVariant(string path, GameObject parent)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
            {
                return;
            }

            var previewScene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(parent, previewScene);
            try
            {
                instance.name = System.IO.Path.GetFileNameWithoutExtension(path);
                var avatar = new SerializedObject(instance.GetComponent<UxrAvatar>());
                avatar.FindProperty("_parentPrefab").objectReferenceValue = parent;
                avatar.ApplyModifiedPropertiesWithoutUndo();
                VrBattlegrounds.Editor.Avatars.Workbench.AvatarMaintenanceTools.SavePrefab(instance, path);
            }
            finally
            {
                Object.DestroyImmediate(instance);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(previewScene);
            }
        }

        private static List<UxrHandPoseAsset> OwnPoses(GameObject prefab)
        {
            SerializedProperty list = new SerializedObject(prefab.GetComponent<UxrAvatar>()).FindProperty(HandPoses);
            return Enumerable.Range(0, list.arraySize).Select(i => list.GetArrayElementAtIndex(i).objectReferenceValue as UxrHandPoseAsset)
                             .Where(p => p != null).Distinct().ToList();
        }

        private static void SetPoses(string path, IEnumerable<UxrHandPoseAsset> poses)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var avatar = new SerializedObject(root.GetComponent<UxrAvatar>());
                WritePoses(avatar.FindProperty(HandPoses), poses);
                avatar.ApplyModifiedPropertiesWithoutUndo();
                VrBattlegrounds.Editor.Avatars.Workbench.AvatarMaintenanceTools.SavePrefab(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void WritePoses(SerializedProperty list, IEnumerable<UxrHandPoseAsset> poses)
        {
            UxrHandPoseAsset[] items = poses.Distinct().ToArray();
            list.arraySize = items.Length;
            for (int i = 0; i < items.Length; i++)
            {
                list.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
            }
        }
    }
}
