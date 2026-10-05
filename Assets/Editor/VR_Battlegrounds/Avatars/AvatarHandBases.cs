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
    ///   <item><see cref="SdkHands" /> — скелет кисти SDK (BigHands/Cyborg, 4 кости на палец). Позы, сделанные
    ///   на этом скелете (<c>Controller*</c>, <c>Demo*</c>), живут здесь.</item>
    ///   <item><see cref="NonSdkHands" /> — чужой скелет (3 фаланги, как у MEF). Позы — только нейтральные к
    ///   скелету, из пака (<see cref="HandsPackPoseImporter" />).</item>
    /// </list>
    /// Сам <c>PlayerBase</c> — сетевой каркас без поз кисти. Проверка — <c>AvatarHandPoseChainTests</c>.
    /// </summary>
    public static class AvatarHandBases
    {
        public const string PlayerBase  = "Assets/Prefabs/Player/PlayerBase.prefab";
        public const string SdkHands    = "Assets/Prefabs/Player/PlayerBase_SdkHands.prefab";
        public const string NonSdkHands = "Assets/Prefabs/Player/PlayerBase_NonSdkHands.prefab";

        private const string HandPoses = "_handPoses";

        /// <summary>
        /// Создаёт базы, если их нет, и копирует позы SDK с <c>PlayerBase</c> на <see cref="SdkHands" />.
        /// Повторный запуск ничего не ломает: скопированное уже лежит на базе SDK.
        /// Порядок: базы → <see cref="Rebase" /> аватаров → <see cref="ClearPlayerBasePoses" />.
        /// </summary>
        public static void CreateBases()
        {
            GameObject playerBase = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerBase);
            List<UxrHandPoseAsset> sdkPoses = OwnPoses(playerBase).Where(p => !AssetDatabase.GetAssetPath(p).StartsWith(HandsPackPoseImporter.PoseFolder)).ToList();

            CreateVariant(SdkHands, playerBase);
            CreateVariant(NonSdkHands, playerBase);

            if (sdkPoses.Count > 0)
            {
                SetPoses(SdkHands, OwnPoses(AssetDatabase.LoadAssetAtPath<GameObject>(SdkHands)).Union(sdkPoses));
            }

            // Позы пака на NonSdkHands регистрирует импортёр.
            SetPoses(NonSdkHands, OwnPoses(AssetDatabase.LoadAssetAtPath<GameObject>(NonSdkHands)).Where(p => AssetDatabase.GetAssetPath(p).StartsWith(HandsPackPoseImporter.PoseFolder)));

            AssetDatabase.SaveAssets();
            GameLog.Debug.Info($"[AvatarHandBases] Базы кисти: {sdkPoses.Count} поз SDK скопировано с PlayerBase");
        }

        /// <summary>
        /// Убирает позы кисти с <c>PlayerBase</c>. Запускать после переноса аватаров на базы: вариант, всё ещё
        /// наследующий <c>PlayerBase</c> напрямую, потерял бы унаследованные элементы списка поз.
        /// </summary>
        public static void ClearPlayerBasePoses()
        {
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
        /// Делает вариант <paramref name="variantPath" /> наследником <paramref name="basePath" /> — варианта
        /// того же родителя. GUID префаба не меняется: записи хвата на оружии и сетевой assetId остаются.
        /// Из своего списка поз убираются те, что новая база уже даёт по наследству.
        ///
        /// <para>
        /// Unity не меняет родителя варианта через API (<c>ReplacePrefabAssetOfPrefabInstance</c> бросает
        /// «Replacing the Variant parent is not supported»), поэтому переписывается YAML: см. <see cref="RebaseYaml" />.
        /// </para>
        /// </summary>
        public static void Rebase(string variantPath, string basePath)
        {
            GameObject newBase = AssetDatabase.LoadAssetAtPath<GameObject>(basePath);
            string oldGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(newBase)));
            string newGuid = AssetDatabase.AssetPathToGUID(basePath);
            List<UxrHandPoseAsset> ownPoses = OwnPoses(AssetDatabase.LoadAssetAtPath<GameObject>(variantPath));

            string yaml = System.IO.File.ReadAllText(variantPath);
            System.IO.File.WriteAllText(variantPath, RebaseYaml(yaml, oldGuid, newGuid, SourceInstanceId(System.IO.File.ReadAllText(basePath), oldGuid)));
            AssetDatabase.ImportAsset(variantPath, ImportAssetOptions.ForceUpdate);

            GameObject root = PrefabUtility.LoadPrefabContents(variantPath);
            try
            {
                HashSet<UxrHandPoseAsset> inherited = new HashSet<UxrHandPoseAsset>(newBase.GetComponent<UxrAvatar>().GetAllHandPoses());
                var avatar = new SerializedObject(root.GetComponent<UxrAvatar>());
                avatar.FindProperty("_parentPrefab").objectReferenceValue = newBase;
                WritePoses(avatar.FindProperty(HandPoses), ownPoses.Where(p => !inherited.Contains(p)));
                avatar.ApplyModifiedPropertiesWithoutUndo();
                VrBattlegrounds.Editor.Avatars.Workbench.AvatarMaintenanceTools.SavePrefab(root, variantPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

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
