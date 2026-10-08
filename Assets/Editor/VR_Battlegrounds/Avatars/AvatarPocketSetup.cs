using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UltimateXR.Manipulation;
using VrBattlegrounds.Interaction;
using VrBattlegrounds.Core;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    /// Редактор-инструмент для управления карманами оружия и магазинов на аватарах.
    /// 
    /// Рабочий процесс:
    /// 1. "Save Pocket Prefabs" — извлекает карманы из эталонного аватара (Cyborg) и сохраняет как префабы
    /// 2. "Add Weapon Pockets" — инстанцирует префабы на кости любого аватара
    ///    Если префабы не найдены — создаёт карманы вручную с дефолтными настройками
    ///    Если карманы уже есть — обновляет только теги (upsert)
    /// </summary>
    public static class AvatarPocketSetup
    {
        private const string PocketPrefabFolder = "Assets/Prefabs/Player/Pockets";
        
        /// <summary>
        /// Авторские базовые теги и параметры карманов. Теги магазинов дополняются
        /// из WeaponRegistry: новый зарегистрированный магазин не требует правки списка.
        /// </summary>
        private static readonly PocketConfig[] Pockets = new[]
        {
            new PocketConfig("MagazinePocket", BoneTarget.Pelvis, 
                new[] { "MagMachinegun", "MagGun", "MagShotgun", "M16_Mag", "MagScar", "MagUzi", "MagMP5K", "MagPPK", "MagRevolver", "MagSniper", "MagSRM12",
                      "MagViper", "MagMKR9", "MagHerrington", "MagTR15", "MagMk14",
                      "Cartridge:Herrington", "Cartridge:FabarmSDASS" }, 0.1f,
                new Vector3(0f, -0.05f, 0.15f)),
            
            new PocketConfig("Anchor_Hip_R", BoneTarget.Pelvis,
                new[] { "SideWeapon", "Gun" }, 0.3f,
                new Vector3(0.2f, -0.1f, 0.05f)),
            
            new PocketConfig("Anchor_Back", BoneTarget.Spine,
                new[] { "BackWeapon", "Shotgun", "Machinegun", "M16_Rifle" }, 0.2f,
                new Vector3(0f, 0f, -0.25f)),
            
            new PocketConfig("BackGrabProxy", BoneTarget.Spine,
                null, 0f, // Нет тегов — это UxrGrabbableObject, не Anchor
                new Vector3(0f, 0f, -0.25f)),
        };

        #region === Save Pocket Prefabs ===

        private static void SavePocketPrefabs()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                EditorUtility.DisplayDialog("Ошибка",
                    "Выберите аватар с настроенными карманами в Hierarchy.", "OK");
                return;
            }

            // Создаём папку если нет
            if (!AssetDatabase.IsValidFolder(PocketPrefabFolder))
            {
                string parent = System.IO.Path.GetDirectoryName(PocketPrefabFolder).Replace("\\", "/");
                string folderName = System.IO.Path.GetFileName(PocketPrefabFolder);
                AssetDatabase.CreateFolder(parent, folderName);
            }

            int saved = 0;
            foreach (var config in Pockets)
            {
                Transform found = FindRecursive(selected.transform, config.Name);
                if (found == null)
                {
                    GameLog.Player.Warning($"[AvatarPocketSetup] {config.Name} не найден в {selected.name}, пропускаю.");
                    continue;
                }

                string prefabPath = $"{PocketPrefabFolder}/{config.Name}.prefab";
                
                // Временно отвязываем от родителя (чтобы сохранить без иерархии костей)
                Transform originalParent = found.parent;
                Vector3 originalLocalPos = found.localPosition;
                Quaternion originalLocalRot = found.localRotation;
                
                found.SetParent(null, false);
                
                // Сохраняем/перезаписываем префаб
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(found.gameObject, prefabPath, out bool success);
                
                // Возвращаем на место
                found.SetParent(originalParent, false);
                found.localPosition = originalLocalPos;
                found.localRotation = originalLocalRot;

                if (success)
                {
                    GameLog.Player.Info($"[AvatarPocketSetup] Сохранён: {prefabPath}");
                    saved++;
                }
                else
                {
                    GameLog.Player.Error($"[AvatarPocketSetup] Не удалось сохранить: {prefabPath}");
                }
            }

            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Готово!",
                $"Сохранено {saved} карманов как префабы в:\n{PocketPrefabFolder}/", "OK");
        }

        private static bool SavePocketPrefabsValidation()
        {
            return Selection.activeGameObject != null;
        }

        #endregion

        #region === Add Pockets to Avatar ===

        private static void AddPockets()
        {
            GameObject selected = Selection.activeGameObject;
            if (selected == null)
            {
                EditorUtility.DisplayDialog("Ошибка",
                    "Выберите корневой GameObject аватара в Hierarchy.", "OK");
                return;
            }

            string result = Setup(selected);
            EditorUtility.DisplayDialog("Карманы", result + "\nПроверьте расположение в Scene View.", "OK");
        }

        /// <summary>Явная цель без выбора первого аватара и без сохранения общих шаблонов.</summary>
        public static string Setup(GameObject selected)
        {
            if (selected == null) throw new System.ArgumentNullException(nameof(selected));
            // Проверяем каталог до первой записи: неполная конфигурация не должна
            // оставлять частично обновлённые карманы.
            string[] magazineTags = GetMagazineCompatibleTags(WeaponRegistry.Instance).ToArray();

            // --- Находим кости ---
            Transform pelvis, spine;
            if (!FindBones(selected, out pelvis, out spine, false))
                throw new System.InvalidOperationException("Не найдены pelvis/spine. Настройка карманов остановлена.");

            GameLog.Player.Info($"[AvatarPocketSetup] Найдены кости: pelvis={pelvis.name}, spine={spine.name}");

            Undo.SetCurrentGroupName("Setup Weapon Pockets");
            int undoGroup = Undo.GetCurrentGroup();
            
            int created = 0;
            int updated = 0;
            var createdNames = new HashSet<string>();

            foreach (var config in Pockets)
            {
                bool magazine = config.Name == "MagazinePocket";
                string[] tags = magazine ? magazineTags : config.Tags;
                Transform targetBone = config.Bone == BoneTarget.Pelvis ? pelvis : spine;
                Transform existing = FindRecursive(selected.transform, config.Name);

                if (existing != null)
                {
                    // Upsert — обновляем только теги
                    if (tags != null)
                    {
                        var anchor = existing.GetComponent<UxrGrabbableObjectAnchor>();
                        EnsureAnchorTags(existing.gameObject, tags, anchor != null ? anchor.MaxPlaceDistance : config.MaxPlaceDistance,
                            preserveExistingTags: magazine);
                    }
                    updated++;
                    continue;
                }

                // Пробуем инстанцировать из префаба
                string prefabPath = $"{PocketPrefabFolder}/{config.Name}.prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);

                GameObject instance;
                if (prefab != null)
                {
                    // Инстанцируем префаб (сохраняет связь с оригиналом!)
                    instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, selected.scene);
                    Undo.RegisterCreatedObjectUndo(instance, $"Instantiate {config.Name}");
                    instance.transform.SetParent(targetBone, false);
                    instance.transform.localPosition = config.DefaultPosition;
                    instance.transform.localRotation = Quaternion.identity;
                    if (tags != null)
                    {
                        var anchor = instance.GetComponent<UxrGrabbableObjectAnchor>();
                        EnsureAnchorTags(instance, tags, anchor != null ? anchor.MaxPlaceDistance : config.MaxPlaceDistance,
                            preserveExistingTags: magazine);
                    }
                    
                    GameLog.Player.Info($"[AvatarPocketSetup] {config.Name} — инстанцирован из префаба");
                }
                else
                {
                    // Фоллбэк — создаём вручную
                    instance = CreatePocketManually(config, targetBone, tags);
                    GameLog.Player.Info($"[AvatarPocketSetup] {config.Name} — создан вручную (префаб не найден)");
                }
                
                created++;
                createdNames.Add(config.Name);
            }

            Workbench.AvatarPocketTemplateBindings.Apply(selected, createdNames);
            Undo.CollapseUndoOperations(undoGroup);

            string summary = $"Создано: {created}, Обновлено: {updated}";
            
            GameLog.Player.Info($"[AvatarPocketSetup] {summary}\n" +
                      $"  pelvis: {pelvis.name} (MagazinePocket, Anchor_Hip_R)\n" +
                      $"  spine:  {spine.name} (Anchor_Back, BackGrabProxy)");

            return summary + $"\nТаз: {pelvis.name}; спина: {spine.name}.";
        }

        private static bool AddPocketsValidation()
        {
            return Selection.activeGameObject != null;
        }

        #endregion

        #region === Helpers ===

        /// <summary>
        /// Снимок совместимости кармана: авторские ammo tags плюс фактические теги
        /// магазинов всего каталога. Порядок независим от порядка WeaponRegistry.
        /// Ничего не меняет в каталоге, префабах или конфигурации карманов.
        /// </summary>
        public static IReadOnlyList<string> GetMagazineCompatibleTags(WeaponRegistry registry)
        {
            if (registry == null) throw new System.ArgumentNullException(nameof(registry));
            if (registry.Weapons == null || registry.Weapons.Count == 0)
                throw new System.InvalidOperationException("WeaponRegistry пуст: совместимость кармана магазинов не определена.");

            var tags = new SortedSet<string>(Pockets.First(p => p.Name == "MagazinePocket").Tags, System.StringComparer.Ordinal);
            for (int i = 0; i < registry.Weapons.Count; i++)
            {
                WeaponInfo weapon = registry.Weapons[i];
                if (weapon == null || weapon.WeaponPrefab == null)
                    throw new System.InvalidOperationException($"WeaponRegistry[{i}]: не задан WeaponInfo или префаб оружия.");
                // Магазин — optional в WeaponInfo; оружие без него не требует тега.
                if (weapon.MagazinePrefab == null) continue;

                UxrGrabbableObject magazine = weapon.MagazinePrefab.GetComponent<UxrGrabbableObject>();
                if (magazine == null || string.IsNullOrWhiteSpace(magazine.Tag))
                    throw new System.InvalidOperationException($"WeaponRegistry[{i}] '{weapon.WeaponId}': MagazinePrefab не имеет UxrGrabbableObject с непустым Tag на корне.");
                tags.Add(magazine.Tag);
            }
            return System.Array.AsReadOnly(tags.ToArray());
        }

        /// <summary>
        /// Создаёт карман вручную (фоллбэк, если префаб не найден).
        /// </summary>
        private static GameObject CreatePocketManually(PocketConfig config, Transform bone, string[] tags)
        {
            GameObject go = new GameObject(config.Name);
            Undo.RegisterCreatedObjectUndo(go, $"Create {config.Name}");
            go.transform.SetParent(bone, false);
            go.transform.localPosition = config.DefaultPosition;
            go.transform.localRotation = Quaternion.identity;

            if (tags != null)
            {
                // Это Anchor-тип кармана
                EnsureAnchorTags(go, tags, config.MaxPlaceDistance);
            }
            else
            {
                // BackGrabProxy — только UxrGrabbableObject
                Undo.AddComponent<UxrGrabbableObject>(go);
            }

            // Специальная логика для MagazinePocket
            if (config.Name == "MagazinePocket")
            {
                Undo.AddComponent<UxrMagazinePocket>(go);
                
                BoxCollider col = Undo.AddComponent<BoxCollider>(go);
                col.isTrigger = true;
                col.size = new Vector3(0.15f, 0.15f, 0.1f);
                
                // GrabProxy (дочерний)
                GameObject proxy = new GameObject("GrabProxy");
                Undo.RegisterCreatedObjectUndo(proxy, "Create GrabProxy");
                proxy.transform.SetParent(go.transform, false);
                
                BoxCollider proxyCol = Undo.AddComponent<BoxCollider>(proxy);
                proxyCol.size = new Vector3(0.12f, 0.12f, 0.08f);
                
                Undo.AddComponent<UxrGrabbableObject>(proxy);
                // Границы кармана видно через GrabbableAnchorGizmos (редактор) и
                // AnchorZonesDebugView (шлем) — отдельный визуализатор не нужен.
            }

            return go;
        }

        /// <summary>
        /// Находит кости pelvis и spine через Animator + fallback по именам.
        /// </summary>
        private static bool FindBones(GameObject root, out Transform pelvis, out Transform spine, bool showDialog = true)
        {
            pelvis = null;
            spine = null;

            Animator animator = root.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman)
            {
                if (showDialog) EditorUtility.DisplayDialog("Ошибка",
                    "Не найден Humanoid Animator в иерархии выбранного объекта.", "OK");
                return false;
            }

            // Animator API (работает в Scene, не в Prefab Mode)
            pelvis = animator.GetBoneTransform(HumanBodyBones.Hips);
            spine = animator.GetBoneTransform(HumanBodyBones.UpperChest)
                    ?? animator.GetBoneTransform(HumanBodyBones.Chest)
                    ?? animator.GetBoneTransform(HumanBodyBones.Spine);

            // Фоллбэк по именам (для Prefab Mode)
            if (pelvis == null)
                pelvis = FindBoneByNames(root.transform,
                    "pelvis", "Pelvis", "Hips", "hips", "mixamorig:Hips");

            if (spine == null)
            {
                spine = FindBoneByNames(root.transform,
                    "spine_03", "Spine03", "spine_02", "Spine02",
                    "UpperChest", "upper_chest", "chest", "Chest");
                
                if (spine == null)
                    spine = FindBoneByNames(root.transform,
                        "spine_01", "Spine01", "Spine", "spine");
            }

            if (pelvis == null || spine == null)
            {
                EditorUtility.DisplayDialog("Ошибка",
                    $"Не удалось найти кости.\nPelvis: {(pelvis != null ? pelvis.name : "NULL")}\n" +
                    $"Spine: {(spine != null ? spine.name : "NULL")}\n\n" +
                    "Убедитесь, что модель имеет Humanoid-скелет.", "OK");
                return false;
            }

            return true;
        }

        /// <summary>
        /// Находит или добавляет UxrGrabbableObjectAnchor, затем обновляет теги и maxPlaceDistance.
        /// </summary>
        private static void EnsureAnchorTags(GameObject go, string[] tags, float maxPlaceDistance, bool preserveExistingTags = false)
        {
            UxrGrabbableObjectAnchor anchor = go.GetComponent<UxrGrabbableObjectAnchor>();
            if (anchor == null)
                anchor = Undo.AddComponent<UxrGrabbableObjectAnchor>(go);
            
            SerializedObject so = new SerializedObject(anchor);

            SerializedProperty tagsProp = so.FindProperty("_compatibleTags");
            if (tagsProp == null)
                throw new System.InvalidOperationException("UxrGrabbableObjectAnchor._compatibleTags не найден: карман не обновлён.");
            {
                if (preserveExistingTags)
                {
                    var merged = new SortedSet<string>(tags, System.StringComparer.Ordinal);
                    for (int i = 0; i < tagsProp.arraySize; i++)
                    {
                        string authored = tagsProp.GetArrayElementAtIndex(i).stringValue;
                        if (!string.IsNullOrWhiteSpace(authored)) merged.Add(authored);
                    }
                    tags = merged.ToArray();
                }
                tagsProp.ClearArray();
                for (int i = 0; i < tags.Length; i++)
                {
                    tagsProp.InsertArrayElementAtIndex(i);
                    tagsProp.GetArrayElementAtIndex(i).stringValue = tags[i];
                }
            }

            SerializedProperty distProp = so.FindProperty("_maxPlaceDistance");
            if (distProp != null)
                distProp.floatValue = maxPlaceDistance;

            so.ApplyModifiedProperties();
        }

        private static Transform FindBoneByNames(Transform root, params string[] names)
        {
            foreach (string name in names)
            {
                Transform found = FindRecursive(root, name);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static Transform FindRecursive(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name)
                    return child;
                
                Transform result = FindRecursive(child, name);
                if (result != null)
                    return result;
            }
            return null;
        }

        #endregion

        #region === Data Structures ===

        private enum BoneTarget { Pelvis, Spine }

        private struct PocketConfig
        {
            public string Name;
            public BoneTarget Bone;
            public string[] Tags;          // null для BackGrabProxy (не Anchor)
            public float MaxPlaceDistance;
            public Vector3 DefaultPosition;

            public PocketConfig(string name, BoneTarget bone, string[] tags, float maxDist, Vector3 defaultPos)
            {
                Name = name;
                Bone = bone;
                Tags = tags;
                MaxPlaceDistance = maxDist;
                DefaultPosition = defaultPos;
            }
        }

        #endregion
    }
}
