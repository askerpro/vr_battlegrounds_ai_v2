using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UltimateXR.Manipulation;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Editor.Arsenal
{
    /// <summary>
    /// Превью содержимого слотов арсенала в режиме редактора: оружие на якоре предмета
    /// и декоративный магазин на якоре магазина.
    ///
    /// <para>
    /// Превью — объекты с <c>HideFlags.DontSave</c>: в сцену и префаб они не пишутся,
    /// а значит, не переживают выход из Play Mode, открытие сцены и сохранение-перезагрузку.
    /// Раньше их создавал только инспектор слота в <c>OnEnable</c>, и превью появлялось
    /// лишь после выделения слота. Теперь этот класс сам восстанавливает превью всех
    /// слотов в загруженных сценах и в открытом префабе после каждого такого события.
    /// </para>
    ///
    /// <para>
    /// Перед входом в Play Mode превью удаляются: при выключенной перезагрузке сцены
    /// (Enter Play Mode Options) они бы остались в игре поверх настоящего оружия.
    /// </para>
    /// </summary>
    [InitializeOnLoad]
    public static class ArsenalSlotPreview
    {
        public const string ItemPreviewName = "__ItemPreview__";
        public const string MagPreviewName  = "__MagPreview__";

        static ArsenalSlotPreview()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorSceneManager.sceneOpened         += (_, _) => EnsureAll();
            PrefabStage.prefabStageOpened          += _ => EnsureAll();

            // После перезагрузки домена сцена уже загружена, но API редактора ещё не готово.
            EditorApplication.delayCall += EnsureAll;
        }

        // ── Массовые операции ──────────────────────────────────

        /// <summary>Создаёт недостающие превью у всех слотов в загруженных сценах и открытом префабе.</summary>
        public static void EnsureAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            foreach (ArsenalSlotController slot in FindEditableSlots())
                Ensure(slot);
        }

        /// <summary>Удаляет превью у всех слотов в загруженных сценах и открытом префабе.</summary>
        public static void DestroyAll()
        {
            foreach (ArsenalSlotController slot in FindEditableSlots())
                Destroy(slot);
        }

        // ── Один слот ──────────────────────────────────────────

        /// <summary>
        /// Создаёт превью слота, если его нет. Уже существующее превью не трогает —
        /// повторный вызов безопасен. Возвращает превью предмета или null.
        /// </summary>
        public static GameObject Ensure(ArsenalSlotController slot)
        {
            if (slot == null || EditorApplication.isPlayingOrWillChangePlaymode) return null;

            WeaponInfo info = slot.WeaponData;
            if (info == null || info.WeaponPrefab == null) return null;

            GameObject item = Find(slot, ItemPreviewName);
            if (item == null)
            {
                UxrGrabbableObjectAnchor itemAnchor = ResolveItemAnchor(slot);
                if (itemAnchor != null)
                {
                    item = Spawn(info.WeaponPrefab, ItemPreviewName, itemAnchor.transform,
                        info.WeaponPositionOffset, Quaternion.Euler(info.WeaponRotationOffset));
                }
            }

            if (slot is FirearmSlotController firearm && info.MagazinePrefab != null &&
                Find(slot, MagPreviewName) == null)
            {
                UxrGrabbableObjectAnchor magAnchor = ResolveMagAnchor(firearm);
                if (magAnchor != null)
                    Spawn(info.MagazinePrefab, MagPreviewName, magAnchor.transform, Vector3.zero, Quaternion.identity);
            }

            return item;
        }

        /// <summary>Удаляет все превью слота.</summary>
        public static void Destroy(ArsenalSlotController slot)
        {
            if (slot == null) return;

            var toDestroy = new List<GameObject>();
            foreach (Transform t in slot.GetComponentsInChildren<Transform>(true))
            {
                if (IsPreview(t.gameObject))
                    toDestroy.Add(t.gameObject);
            }

            foreach (GameObject go in toDestroy)
            {
                if (go == null) continue; // вложенное превью уже ушло вместе с родителем

                // Иначе инспектор остаётся с целью на удалённом объекте.
                if (Selection.activeGameObject != null &&
                    Selection.activeGameObject.transform.IsChildOf(go.transform))
                {
                    Selection.activeObject = null;
                }

                Object.DestroyImmediate(go);
            }
        }

        /// <summary>Превью с заданным именем внутри слота или null.</summary>
        public static GameObject Find(Component slot, string previewName)
        {
            foreach (Transform t in slot.GetComponentsInChildren<Transform>(true))
            {
                if (t.gameObject.name == previewName && IsPreview(t.gameObject))
                    return t.gameObject;
            }
            return null;
        }

        // ── Внутреннее ─────────────────────────────────────────

        private static bool IsPreview(GameObject go)
        {
            return (go.name == ItemPreviewName || go.name == MagPreviewName) &&
                   (go.hideFlags & HideFlags.DontSave) != 0;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.ExitingEditMode:
                    DestroyAll();
                    break;
                case PlayModeStateChange.EnteredEditMode:
                    EnsureAll();
                    break;
            }
        }

        private static IEnumerable<ArsenalSlotController> FindEditableSlots()
        {
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.prefabContentsRoot != null)
            {
                foreach (var slot in stage.prefabContentsRoot.GetComponentsInChildren<ArsenalSlotController>(true))
                    yield return slot;
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    foreach (var slot in root.GetComponentsInChildren<ArsenalSlotController>(true))
                        yield return slot;
                }
            }
        }

        // Тот же автопоиск, что у слотов в Awake: в редакторе Awake не вызывается,
        // и незаполненное поле якоря здесь пустое.
        private static UxrGrabbableObjectAnchor ResolveItemAnchor(ArsenalSlotController slot)
        {
            if (slot.ItemAnchor != null) return slot.ItemAnchor;

            foreach (var a in slot.GetComponentsInChildren<UxrGrabbableObjectAnchor>(true))
            {
                if (!a.gameObject.name.Contains("Mag"))
                    return a;
            }
            return null;
        }

        private static UxrGrabbableObjectAnchor ResolveMagAnchor(FirearmSlotController slot)
        {
            if (slot.MagAnchor != null) return slot.MagAnchor;

            foreach (var a in slot.GetComponentsInChildren<UxrGrabbableObjectAnchor>(true))
            {
                if (a.gameObject.name.Contains("Mag"))
                    return a;
            }
            return null;
        }

        private static GameObject Spawn(GameObject prefab, string previewName, Transform anchor,
            Vector3 localPosition, Quaternion localRotation)
        {
            // Обычный Instantiate, а не InstantiatePrefab: связанный экземпляр префаба
            // не дал бы удалить NetworkIdentity без предупреждений.
            var preview = Object.Instantiate(prefab);
            preview.name = previewName;
            preview.transform.SetParent(anchor, false);
            preview.transform.localPosition = localPosition;
            preview.transform.localRotation = localRotation;

            SetHideFlagsRecursive(preview.transform, HideFlags.DontSave);

            var netId = preview.GetComponent<Mirror.NetworkIdentity>();
            if (netId != null) Object.DestroyImmediate(netId, true);

            foreach (var rb in preview.GetComponentsInChildren<Rigidbody>(true))
                rb.isKinematic = true;
            foreach (var col in preview.GetComponentsInChildren<Collider>(true))
                col.enabled = false;
            foreach (var grab in preview.GetComponentsInChildren<UxrGrabbableObject>(true))
                grab.enabled = false;

            return preview;
        }

        public static void SetHideFlagsRecursive(Transform root, HideFlags flags)
        {
            root.gameObject.hideFlags = flags;
            foreach (Transform child in root)
                SetHideFlagsRecursive(child, flags);
        }
    }
}
