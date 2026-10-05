using System;
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
    /// Редакторские копии только геометрии оружия и магазина. Игровые компоненты не создаются.
    /// Собственные transient roots обновляются при изменении источника; в ассеты не сохраняются.
    /// </summary>
    [InitializeOnLoad]
    public static class ArsenalSlotPreview
    {
        public const string ItemPreviewName = "__ItemPreview__";
        public const string MagPreviewName = "__MagPreview__";
        private const HideFlags PreviewFlags = HideFlags.DontSave | HideFlags.NotEditable;

        private sealed class PreviewRecord
        {
            public ArsenalSlotController Owner;
            public GameObject Source;
            public Hash128 DependencyHash;
            public Vector3 Offset;
            public Quaternion Rotation;
            public int AnchorId;
            public string PresentationRevision;
        }

        private static readonly Dictionary<GameObject, PreviewRecord> Records = new Dictionary<GameObject, PreviewRecord>();
        private static readonly Dictionary<ArsenalSlotController, string> PresentationErrors = new Dictionary<ArsenalSlotController, string>();
        public static string PresentationError(ArsenalSlotController slot) => slot != null && PresentationErrors.TryGetValue(slot, out var message) ? message : null;
        private static bool _refreshQueued;

        static ArsenalSlotPreview()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorSceneManager.sceneOpened += (_, _) => QueueRefresh();
            EditorSceneManager.sceneClosed += _ => PruneRecords();
            PrefabStage.prefabStageOpened += _ => QueueRefresh();
            EditorApplication.projectChanged += QueueRefresh;
            AssemblyReloadEvents.beforeAssemblyReload += DestroyAll;
            QueueRefresh();
        }

        private static void QueueRefresh()
        {
            if (_refreshQueued) return;
            _refreshQueued = true;
            EditorApplication.delayCall += FlushRefresh;
        }

        private static void FlushRefresh()
        {
            _refreshQueued = false;
            if (EditorApplication.isPlayingOrWillChangePlaymode || AnimationMode.InAnimationMode()) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating) { QueueRefresh(); return; }
            EnsureAll();
        }

        /// <summary>Обновляет только открытые обычные сцены и текущий Prefab Stage; helper preview scenes пропускает.</summary>
        public static void EnsureAll()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || AnimationMode.InAnimationMode()) return;
            foreach (Scene scene in EditableScenes()) EnsureAll(scene);
        }

        /// <summary>Тот же маршрут для одной сцены: позволяет изолировать временную проверялку.</summary>
        public static void EnsureAll(Scene scene)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || AnimationMode.InAnimationMode() || !IsEditableScene(scene)) return;
            RemoveOrphans(scene);
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (ArsenalSlotController slot in root.GetComponentsInChildren<ArsenalSlotController>(true))
                    Ensure(slot);
        }

        /// <summary>Очищает собственные transient roots, включая оторванные от слота.</summary>
        public static void DestroyAll()
        {
            foreach (Scene scene in EditableScenes()) DestroyAll(scene);
        }

        public static void DestroyAll(Scene scene)
        {
            if (!IsEditableScene(scene)) return;
            PruneRecords();
            var roots = new List<GameObject>();
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    if (IsPreview(child.gameObject)) roots.Add(child.gameObject);
            foreach (GameObject root in roots) DestroyPreview(root);
        }

        /// <summary>Возвращает актуальное превью. Неизменное editable превью сохраняет редактируемые local TRS.</summary>
        public static GameObject Ensure(ArsenalSlotController slot)
        {
            // Guard предшествует даже обновлению карточки: persistent asset не должен изменяться инспектором.
            if (!IsEditableSlot(slot) || EditorApplication.isPlayingOrWillChangePlaymode || AnimationMode.InAnimationMode()) return null;
            PruneRecords();
            WeaponInfo info = slot.WeaponData;
            if (info == null || info.WeaponPrefab == null) { Destroy(slot); return null; }

            if (!TryResolveAnchor(slot, slot.ItemAnchor, false, out UxrGrabbableObjectAnchor itemAnchor))
            { Destroy(slot); return null; }
            UxrGrabbableObjectAnchor magAnchor = null;
            bool needsMagazine = slot is FirearmSlotController && info.MagazinePrefab != null;
            if (needsMagazine && !TryResolveAnchor(slot, ((FirearmSlotController)slot).MagAnchor, true, out magAnchor))
            { Destroy(slot); return null; }
            ArsenalPresentationSnapshot presentation;
            try { presentation = ArsenalPresentationApplicator.Resolve(slot); }
            catch (InvalidOperationException exception) { PresentationErrors[slot] = exception.Message; Destroy(slot); return null; }
            PresentationErrors.Remove(slot);
            string revision = PresentationRevision(slot, presentation);
            var itemPose = ArsenalPresentationApplicator.ItemLocalPose(slot, false, presentation);
            var magPose = needsMagazine ? ArsenalPresentationApplicator.ItemLocalPose(slot, true, presentation) : default;

            // Оба якоря разрешены ДО создания геометрии; вложенный магазин оружия не участвует в поиске.
            // Persistent карточка — explicit generator projection, не writer инспекторского Ensure.
            var card = slot.GetComponentInChildren<ArsenalPriceTag>(true);
            if (presentation.IsStyled && (card == null ||
                (slot.transform.InverseTransformPoint(card.transform.position) - presentation.CardTarget.Position).sqrMagnitude > 1e-10f ||
                Quaternion.Angle(Quaternion.Inverse(slot.transform.rotation) * card.transform.rotation, presentation.CardTarget.Rotation) > .001f))
                PresentationErrors[slot] = "Карточка не соответствует Style; явно пересоберите производную станцию. Preview не меняет persistent card.";
            GameObject item = EnsureKind(slot, info.WeaponPrefab, ItemPreviewName, itemAnchor.transform,
                itemPose.Position, itemPose.Rotation, null, revision);
            if (needsMagazine)
                EnsureKind(slot, info.MagazinePrefab, MagPreviewName, magAnchor.transform,
                    magPose.Position, magPose.Rotation, slot.GetComponent<ArsenalMagazineOffer>(), revision);
            else DestroyKind(slot, MagPreviewName);
            return item;
        }

        private static GameObject EnsureKind(ArsenalSlotController slot, GameObject source, string name,
            Transform anchor, Vector3 offset, Quaternion rotation, ArsenalMagazineOffer offer, string revision)
        {
            var existing = FindPreviews(slot, name);
            string path = AssetDatabase.GetAssetPath(source);
            Hash128 hash = string.IsNullOrEmpty(path) ? default : AssetDatabase.GetAssetDependencyHash(path);
            GameObject item = existing.Count == 1 ? existing[0] : null;
            // Handle draft сохраняется до явного Save/Reset/Rebuild, даже при новом source revision.
            if (item != null && item.transform.parent == anchor && (item.hideFlags & HideFlags.NotEditable) == 0 &&
                Records.TryGetValue(item, out PreviewRecord draft) && draft.Owner == slot && draft.Source == source)
                return item;
            bool current = item != null && item.transform.parent == anchor &&
                Records.TryGetValue(item, out PreviewRecord record) && record.Owner == slot &&
                record.Source == source && record.DependencyHash == hash && record.Offset == offset &&
                record.Rotation == rotation && record.AnchorId == anchor.GetInstanceID() && record.PresentationRevision == revision;
            if (!current)
            {
                foreach (GameObject old in existing) DestroyPreview(old);
                item = SpawnGeometry(source, name, anchor, offset, rotation, offer,
                    ArsenalPresentationApplicator.ProjectionLocalScale(source, anchor, ArsenalPresentationApplicator.Resolve(slot)));
                Records[item] = new PreviewRecord { Owner = slot, Source = source, DependencyHash = hash,
                    Offset = offset, Rotation = rotation, AnchorId = anchor.GetInstanceID(), PresentationRevision = revision };
            }
            else if ((item.hideFlags & HideFlags.NotEditable) != 0)
            {
                // Только блокированное превью следует данным; ручной offset editing не сбрасывается.
                ApplyPlacement(item.transform, ArsenalPresentationApplicator.ProjectionLocalScale(source, anchor, ArsenalPresentationApplicator.Resolve(slot)), offset, rotation);
                if (offer != null && !ArsenalPresentationApplicator.Resolve(slot).IsStyled) offer.FitToSurface(item.transform);
            }
            return item;
        }

        private static string PresentationRevision(ArsenalSlotController slot, ArsenalPresentationSnapshot presentation)
        {
            string Dependency(UnityEngine.Object value)
            {
                string path = value != null ? AssetDatabase.GetAssetPath(value) : null;
                return string.IsNullOrEmpty(path) ? "none" : AssetDatabase.GetAssetDependencyHash(path).ToString();
            }
            var offer = slot.GetComponent<ArsenalMagazineOffer>();
            return Dependency(presentation.Style) + "|" + Dependency(slot.WeaponData) + "|" +
                slot.ItemAnchor.transform.position.ToString("R") + "|" + slot.ItemAnchor.transform.rotation.ToString("R") + "|" + slot.ItemAnchor.transform.lossyScale.ToString("R") + "|" +
                ((slot as FirearmSlotController)?.MagAnchor != null ? ((FirearmSlotController)slot).MagAnchor.transform.localScale.ToString("R") : "no mag scale") + "|" +
                slot.transform.position.ToString("R") + "|" + slot.transform.rotation.ToString("R") + "|" + slot.transform.lossyScale.ToString("R") + "|" +
                (offer != null && offer.Surface != null ? Dependency(offer.Surface.sharedMesh) + "|" + offer.Surface.transform.position.ToString("R") + "|" +
                    offer.Surface.transform.rotation.ToString("R") + "|" + offer.Surface.transform.lossyScale.ToString("R") + "|" + offer.SurfaceNormal.ToString("R") : "no surface");
        }

        public static void Destroy(ArsenalSlotController slot)
        {
            if (!IsEditableSlot(slot)) return;
            var roots = FindPreviews(slot, null);
            // Запись владельца позволяет убрать оторванное превью без обхода чужой сцены.
            foreach (var pair in Records)
                if (pair.Key != null && pair.Value.Owner == slot && IsEditableScene(pair.Key.scene) && !roots.Contains(pair.Key))
                    roots.Add(pair.Key);
            foreach (GameObject root in roots) DestroyPreview(root);
        }

        private static void DestroyKind(ArsenalSlotController slot, string name)
        {
            foreach (GameObject root in FindPreviews(slot, name)) DestroyPreview(root);
        }

        public static GameObject Find(Component slot, string name)
        {
            if (!(slot is ArsenalSlotController owner) || !IsEditableSlot(owner)) return null;
            var found = FindPreviews(owner, name);
            return found.Count == 0 ? null : found[0];
        }

        private static List<GameObject> FindPreviews(ArsenalSlotController slot, string name)
        {
            var result = new List<GameObject>();
            foreach (Transform child in slot.GetComponentsInChildren<Transform>(true))
                if (IsPreview(child.gameObject) && (name == null || child.name == name) &&
                    child.GetComponentInParent<ArsenalSlotController>(true) == slot)
                    result.Add(child.gameObject);
            // Оторванный root остаётся owned. Ensure должен убрать его сразу, без общего refresh.
            foreach (var pair in Records)
                if (pair.Key != null && pair.Value.Owner == slot && IsPreview(pair.Key) &&
                    (name == null || pair.Key.name == name) && IsEditableScene(pair.Key.scene) && !result.Contains(pair.Key))
                    result.Add(pair.Key);
            return result;
        }

        private static void PruneRecords()
        {
            var dead = new List<GameObject>();
            foreach (var pair in Records)
                if (pair.Key == null || pair.Value.Owner == null || !pair.Key.scene.IsValid() || !pair.Key.scene.isLoaded)
                    dead.Add(pair.Key);
            foreach (GameObject key in dead) Records.Remove(key);
        }

        private static bool IsPreview(GameObject go) => go != null &&
            (go.name == ItemPreviewName || go.name == MagPreviewName) &&
            (go.hideFlags & HideFlags.DontSave) != 0;

        private static void DestroyPreview(GameObject root)
        {
            if (ReferenceEquals(root, null)) return;
            Records.Remove(root);
            if (root == null) return;
            if (!IsPreview(root) || EditorUtility.IsPersistent(root) || !IsEditableScene(root.scene)) return;
            if (Selection.activeGameObject != null && Selection.activeGameObject.transform.IsChildOf(root.transform))
                Selection.activeObject = null;
            UnityEngine.Object.DestroyImmediate(root);
        }

        private static void RemoveOrphans(Scene scene)
        {
            var roots = new List<GameObject>();
            foreach (GameObject root in scene.GetRootGameObjects())
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                    if (IsPreview(child.gameObject) && child.GetComponentInParent<ArsenalSlotController>(true) == null)
                        roots.Add(child.gameObject);
            foreach (GameObject root in roots) DestroyPreview(root);
        }

        private static bool TryResolveAnchor(ArsenalSlotController slot, UxrGrabbableObjectAnchor configured,
            bool magazine, out UxrGrabbableObjectAnchor result)
        {
            result = null;
            if (configured != null)
            {
                if (!OwnsAnchor(slot, configured)) return false;
                result = configured;
                return true;
            }
            foreach (var candidate in slot.GetComponentsInChildren<UxrGrabbableObjectAnchor>(true))
            {
                if (!OwnsAnchor(slot, candidate) || candidate.name.Contains("Mag") != magazine) continue;
                if (result != null) { result = null; return false; }
                result = candidate;
            }
            return result != null;
        }

        private static bool OwnsAnchor(ArsenalSlotController slot, UxrGrabbableObjectAnchor anchor)
        {
            if (anchor == null || EditorUtility.IsPersistent(anchor) || anchor.gameObject.scene != slot.gameObject.scene ||
                anchor.GetComponentInParent<ArsenalSlotController>(true) != slot) return false;
            for (Transform parent = anchor.transform; parent != null && parent != slot.transform; parent = parent.parent)
                if (IsPreview(parent.gameObject)) return false;
            return true;
        }

        private static GameObject SpawnGeometry(GameObject source, string name, Transform anchor,
            Vector3 offset, Quaternion rotation, ArsenalMagazineOffer offer, Vector3 scale)
        {
            // Текущий каталог — static meshes. Будущий skinned source требует отдельного BakeMesh route.
            if (source.GetComponentInChildren<SkinnedMeshRenderer>(true) != null)
                throw new InvalidOperationException("Превью арсенала поддерживает только MeshFilter/MeshRenderer: " + source.name);
            GameObject root = null;
            try
            {
                root = new GameObject(name) { hideFlags = PreviewFlags, layer = source.layer };
                root.SetActive(false);
                SceneManager.MoveGameObjectToScene(root, anchor.gameObject.scene);
                root.transform.SetParent(anchor, false);
                if (root.transform.parent != anchor) throw new InvalidOperationException("Не установлен родитель превью арсенала.");
                ApplyPlacement(root.transform, scale, offset, rotation);
                CopyGeometry(source.transform, root.transform);
                // Render-only копия не содержит Behaviour/physics/SDK callbacks даже при активации.
                root.SetActive(source.activeSelf);
                if (offer != null && !ArsenalPresentationApplicator.Resolve(offer.Slot).IsStyled) offer.FitToSurface(root.transform);
                return root;
            }
            catch
            {
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                throw;
            }
        }

        private static void ApplyPlacement(Transform root, Vector3 scale, Vector3 offset, Quaternion rotation)
        {
            root.localScale = scale;
            root.SetLocalPositionAndRotation(offset, rotation);
        }

        private static void CopyGeometry(Transform source, Transform target)
        {
            var filter = source.GetComponent<MeshFilter>();
            if (filter != null) target.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var renderer = source.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                var copy = target.gameObject.AddComponent<MeshRenderer>();
                copy.sharedMaterials = renderer.sharedMaterials;
                copy.enabled = renderer.enabled;
                copy.shadowCastingMode = renderer.shadowCastingMode;
                copy.receiveShadows = renderer.receiveShadows;
                copy.lightProbeUsage = renderer.lightProbeUsage;
                copy.reflectionProbeUsage = renderer.reflectionProbeUsage;
                copy.additionalVertexStreams = renderer.additionalVertexStreams;
            }
            foreach (Transform child in source)
            {
                if (IsPreview(child.gameObject)) continue;
                var clone = new GameObject(child.name) { hideFlags = PreviewFlags, layer = child.gameObject.layer };
                try
                {
                    clone.SetActive(false);
                    clone.transform.SetParent(target, false);
                    if (clone.transform.parent != target) throw new InvalidOperationException("Не установлен родитель части превью.");
                    clone.transform.localPosition = child.localPosition;
                    clone.transform.localRotation = child.localRotation;
                    clone.transform.localScale = child.localScale;
                    CopyGeometry(child, clone.transform);
                    clone.SetActive(child.gameObject.activeSelf);
                }
                catch
                {
                    if (clone != null) UnityEngine.Object.DestroyImmediate(clone);
                    throw;
                }
            }
        }

        private static bool IsEditableSlot(ArsenalSlotController slot) => slot != null &&
            !EditorUtility.IsPersistent(slot) && IsEditableScene(slot.gameObject.scene);

        private static bool IsEditableScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return false;
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && stage.prefabContentsRoot != null && stage.scene == scene) return true;
            return !EditorSceneManager.IsPreviewScene(scene);
        }

        private static IEnumerable<Scene> EditableScenes()
        {
            var seen = new HashSet<int>();
            PrefabStage stage = PrefabStageUtility.GetCurrentPrefabStage();
            if (stage != null && IsEditableScene(stage.scene) && seen.Add(stage.scene.handle)) yield return stage.scene;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (IsEditableScene(scene) && seen.Add(scene.handle)) yield return scene;
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingEditMode) DestroyAll();
            else if (change == PlayModeStateChange.EnteredEditMode) QueueRefresh();
        }

        public static void SetHideFlagsRecursive(Transform root, HideFlags flags)
        {
            root.gameObject.hideFlags = flags;
            foreach (Transform child in root) SetHideFlagsRecursive(child, flags);
        }
    }
}
