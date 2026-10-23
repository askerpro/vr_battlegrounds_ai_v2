using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Maps.Runtime;

namespace VrBattlegrounds.Editor.Arsenal
{
    /// <summary>
    ///     Превью содержимого сгенерированных станций арсенала в Edit Mode: слоты, оружие, магазины, карточки и опоры
    ///     видны в Scene View без Play.
    ///     <para>
    ///     Состав и позы считает код игры: описание станции — <see cref="MapArsenalStationDescriber" /> (те же входы,
    ///     что у запуска карты), размещение слотов — <see cref="ArsenalStationComposer.PlanComposition" />, позы предметов —
    ///     <see cref="ArsenalPresentationApplicator.ItemWorldPose" />, карточка — <see cref="ArsenalPresentationApplicator.ApplyCard(Transform, Transform, ArsenalPresentationSnapshot)" />
    ///     и <see cref="ArsenalPriceTag.ApplyFrame" />, опоры — <see cref="ArsenalSupportProjection.MaterializeSupportRole" />.
    ///     Своё у превью только отрисовка: копии мешей и материалов (и текст карточки), без NetworkIdentity, SDK,
    ///     физики и игровых скриптов.
    ///     </para>
    ///     <para>
    ///     Копии временные: <see cref="HideFlags.DontSaveInEditor" /> и <see cref="HideFlags.DontSaveInBuild" />, корни —
    ///     в корне сцены станции, не внутри префаба станции (prefab overrides не появляются). Удаляются при входе в Play,
    ///     выгрузке сцены и перезагрузке скриптов; ряды корпуса превью сопровождает, если станцию двигают.
    ///     Превью всегда включено в интерактивном редакторе (решение пользователя 2026-10-22); в batch-режиме не строится.
    ///     </para>
    /// </summary>
    [InitializeOnLoad]
    public static class ArsenalStationScenePreview
    {
        public const string RefreshMenuPath = "Tools/VR Battlegrounds/Arsenal/Обновить превью станций";
        private const string CatalogPath = "Assets/Data/Maps/MapRuntimeCatalog.asset";
        internal const string RootPrefix = "[Превью арсенала] ";
        private const HideFlags Transient = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild | HideFlags.NotEditable;

        private sealed class RowMirror
        {
            public Transform Source;
            public Transform Mirror;
        }

        private sealed class StationPreview
        {
            public GameObject Root;
            public ArsenalStationCompositionBinding Station;
            public readonly List<RowMirror> Rows = new List<RowMirror>();
        }

        private static readonly List<StationPreview> Live = new List<StationPreview>();

        /// <summary>Итог последней пересборки: число станций и слотов или причины отказа.</summary>
        public static string LastReport { get; private set; } = string.Empty;

        static ArsenalStationScenePreview()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorSceneManager.sceneClosing += (scene, removing) => ClearScene(scene);
            AssemblyReloadEvents.beforeAssemblyReload += Clear;
            EditorApplication.update += FollowRows;
            ObjectChangeEvents.changesPublished += OnChangesPublished;
            QueueRebuild();
        }

        /// <summary>Пауза после последней правки входа: серия правок в Inspector даёт одну пересборку.</summary>
        private const double ChangeSettleSeconds = .5;
        private static double _rebuildDue = -1;

        /// <summary>Набор загруженных сцен на момент последней пересборки (<see cref="LoadedScenes" />).</summary>
        private static int _builtScenes;

        /// <summary>
        ///     Отпечаток загруженных сцен. Смена набора — пересборка: ловит любое открытие и закрытие сцен, в том числе
        ///     восстановление сцен после прогона тестов, где sceneOpened не приходит.
        /// </summary>
        private static int LoadedScenes()
        {
            int hash = 17;
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded) hash = hash * 31 + scene.handle;
            }
            return hash;
        }

        /// <summary>Входы превью: ассортимент, раскладки, оружие, стиль, паспорт карты и каталоги.</summary>
        private static bool IsInput(UnityEngine.Object asset) => asset != null && IsInputType(asset.GetType());

        /// <summary>Правка входа в Inspector или Undo — пересборка после паузы.</summary>
        private static void OnChangesPublished(ref ObjectChangeEventStream stream)
        {
            if (!Enabled) return;
            for (int i = 0; i < stream.length; i++)
            {
                if (stream.GetEventType(i) != ObjectChangeKind.ChangeAssetObjectProperties) continue;
                stream.GetChangeAssetObjectPropertiesEvent(i, out ChangeAssetObjectPropertiesEventArgs change);
                if (IsInput(EditorUtility.EntityIdToObject(change.entityId)))
                {
                    RebuildAfterChanges();
                    return;
                }
            }
        }

        /// <summary>Импорт с диска (сохранение, git pull, пересборка оружия или корпуса): пересборка после паузы.</summary>
        internal static void OnImported(string[] imported, string[] deleted)
        {
            if (!Enabled) return;
            foreach (string path in imported.Concat(deleted))
            {
                if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ||
                    (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) && IsInputType(AssetDatabase.GetMainAssetTypeAtPath(path))))
                {
                    RebuildAfterChanges();
                    return;
                }
            }
        }

        /// <summary>Тип входа; неизвестный (удалённый ассет) — считать входом.</summary>
        private static bool IsInputType(Type type) => type == null || typeof(ArsenalPreset).IsAssignableFrom(type) ||
            typeof(ArsenalSlotLayout).IsAssignableFrom(type) || typeof(WeaponInfo).IsAssignableFrom(type) ||
            typeof(ArsenalPresentationStyle).IsAssignableFrom(type) || typeof(MapData).IsAssignableFrom(type) ||
            typeof(ArsenalCompositionCatalog).IsAssignableFrom(type) || typeof(MapRuntimeCatalog).IsAssignableFrom(type);

        private static void RebuildAfterChanges() => _rebuildDue = EditorApplication.timeSinceStartup + ChangeSettleSeconds;

        /// <summary>Превью строится в любом интерактивном редакторе; в batch-режиме (сборки, CLI-тесты) — нет.</summary>
        public static bool Enabled => !Application.isBatchMode;

        [MenuItem(RefreshMenuPath, false, 21)]
        private static void RefreshMenu() => Rebuild();

        [MenuItem(RefreshMenuPath, true)]
        private static bool RefreshMenuValidate() => Enabled && !EditorApplication.isPlayingOrWillChangePlaymode;

        /// <summary>
        ///     Пересобрать превью на следующем кадре редактора (повторные запросы сливаются в один). Через
        ///     <see cref="EditorApplication.update" />, не delayCall: очередь delayCall стоит у редактора без фокуса (worker).
        /// </summary>
        public static void QueueRebuild()
        {
            double now = EditorApplication.timeSinceStartup;
            if (_rebuildDue < 0 || _rebuildDue > now) _rebuildDue = now;
        }

        /// <summary>Пересобрать превью всех станций открытых сцен; выключено или Play — только убрать.</summary>
        public static string Rebuild()
        {
            Clear();
            _builtScenes = LoadedScenes();
            if (!Enabled || EditorApplication.isPlayingOrWillChangePlaymode) return LastReport = string.Empty;

            ArsenalCompositionCatalog catalog = AssetDatabase.LoadAssetAtPath<MapRuntimeCatalog>(CatalogPath)?.ArsenalComposition;
            int stations = 0, slots = 0;
            var problems = new List<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (GameObject top in scene.GetRootGameObjects())
                foreach (MapRoot root in top.GetComponentsInChildren<MapRoot>(true))
                foreach (ArsenalStationCompositionBinding station in root.StationBindings)
                {
                    if (station == null) continue;
                    try
                    {
                        slots += Build(scene, station, root.Map != null ? root.Map.arsenalPreset : null, catalog);
                        stations++;
                    }
                    catch (Exception failure)
                    {
                        problems.Add(station.StationKey + ": " + failure.Message);
                    }
                }
            }

            LastReport = $"Превью станций: {stations} станций, {slots} слотов." +
                         (problems.Count == 0 ? string.Empty : " Не собраны: " + string.Join("; ", problems));
            if (problems.Count == 0) GameLog.Arsenal.Info("[ArsenalPreview] " + LastReport);
            else GameLog.Arsenal.Warning("[ArsenalPreview] " + LastReport);
            SceneView.RepaintAll();
            return LastReport;
        }

        /// <summary>Убрать все копии превью, включая оставшиеся от прежней сборки скриптов.</summary>
        public static void Clear()
        {
            foreach (StationPreview preview in Live)
                if (preview.Root != null) UnityEngine.Object.DestroyImmediate(preview.Root);
            Live.Clear();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                foreach (GameObject top in scene.GetRootGameObjects())
                    if (IsPreviewRoot(top)) UnityEngine.Object.DestroyImmediate(top);
            }
        }

        private static bool IsPreviewRoot(GameObject go) =>
            (go.hideFlags & HideFlags.DontSaveInEditor) != 0 && go.name.StartsWith(RootPrefix, StringComparison.Ordinal);

        private static void ClearScene(Scene scene)
        {
            for (int i = Live.Count - 1; i >= 0; i--)
            {
                GameObject root = Live[i].Root;
                if (root != null && root.scene != scene) continue;
                if (root != null) UnityEngine.Object.DestroyImmediate(root);
                Live.RemoveAt(i);
            }
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            // До Play: генератор собирает станции сам, превью в сцене запуска остаться не должно.
            if (change == PlayModeStateChange.ExitingEditMode) Clear();
            else if (change == PlayModeStateChange.EnteredEditMode) QueueRebuild();
        }

        /// <summary>
        ///     Каждый кадр редактора: отложенная пересборка, смена набора сцен; ряды корпуса могут двигать (станцию
        ///     переставили) — копии следуют за ними.
        /// </summary>
        private static void FollowRows()
        {
            if (Enabled && !EditorApplication.isPlayingOrWillChangePlaymode && LoadedScenes() != _builtScenes) QueueRebuild();
            if (_rebuildDue >= 0 && EditorApplication.timeSinceStartup >= _rebuildDue)
            {
                _rebuildDue = -1;
                Rebuild();
                return;
            }
            if (Live.Count == 0) return;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Clear();
                return;
            }
            foreach (StationPreview preview in Live)
            {
                if (preview.Root == null || preview.Station == null)
                {
                    QueueRebuild();
                    return;
                }
                foreach (RowMirror row in preview.Rows)
                {
                    if (row.Source == null)
                    {
                        QueueRebuild();
                        return;
                    }
                    Follow(row);
                }
            }
        }

        private static void Follow(RowMirror row)
        {
            Transform source = row.Source, mirror = row.Mirror;
            if (mirror.position != source.position || mirror.rotation != source.rotation) mirror.SetPositionAndRotation(source.position, source.rotation);
            if (mirror.localScale != source.lossyScale) mirror.localScale = source.lossyScale;
        }

        private static int Build(Scene scene, ArsenalStationCompositionBinding station, ArsenalPreset preset, ArsenalCompositionCatalog catalog)
        {
            if (catalog == null) throw new InvalidOperationException("нет каталога оформления станций (" + CatalogPath + ")");
            ArsenalStationDescription description = MapArsenalStationDescriber.Describe(station, preset, catalog);
            if (!description.Success)
                throw new InvalidOperationException("описание станции: " + string.Join(", ", description.Failures.Select(f =>
                    f.Kind + (string.IsNullOrEmpty(f.LogicalSlotKey) ? "" : "/" + f.LogicalSlotKey) + (string.IsNullOrEmpty(f.Detail) ? "" : " (" + f.Detail + ")"))));
            ArsenalStationPlan plan = ArsenalStationComposer.PlanComposition(description, station);

            GameObject root = Create(RootPrefix + station.StationKey, null);
            if (root.scene != scene) SceneManager.MoveGameObjectToScene(root, scene);
            var preview = new StationPreview { Root = root, Station = station };
            Live.Add(preview);

            try
            {
                var mirrors = new Dictionary<ArsenalSlotRow, Transform>();
                foreach (ArsenalSlotPlacement placement in plan.Slots)
                {
                    if (!mirrors.TryGetValue(placement.Row, out Transform mirror))
                    {
                        mirror = Create(placement.Row.name + " (" + placement.Row.RowKey + ")", root.transform).transform;
                        var row = new RowMirror { Source = placement.Row.transform, Mirror = mirror };
                        preview.Rows.Add(row);
                        Follow(row);
                        mirrors.Add(placement.Row, mirror);
                    }
                    BuildSlot(mirror, placement);
                }
            }
            finally
            {
                // Клоны модуля опор рождаются без флагов: пометить всё поддерево, даже если сборка оборвалась.
                foreach (Transform part in root.GetComponentsInChildren<Transform>(true))
                    if (part != root.transform) part.gameObject.hideFlags |= Transient | HideFlags.HideInHierarchy;
                SceneVisibilityManager.instance.DisablePicking(root, true);
            }
            return plan.Slots.Count;
        }

        private static void BuildSlot(Transform mirror, ArsenalSlotPlacement placement)
        {
            ArsenalSlotController prefab = placement.Row.SlotPrefab;
            ArsenalFrozenEntry entry = placement.Manifest.Entry;
            WeaponInfo weapon = entry.WeaponResource;
            ArsenalPresentationSnapshot presentation = placement.Presentation;

            // Рамка слота — как собранный слот: экземпляр префаба в позе плана, масштаб префаба.
            Transform frame = Create("Slot_" + entry.NetworkIndex + "_" + entry.LogicalSlotKey, mirror).transform;
            frame.SetLocalPositionAndRotation(placement.LocalPose.position, placement.LocalPose.rotation);
            frame.localScale = prefab.transform.localScale;

            // Корпус слота — видимые части префаба. Карточку и якоря ставит раскладка, свои контейнеры опор — сборщик.
            CopyVisual(prefab.transform, frame, part => part.GetComponent<ArsenalPriceTag>() != null ||
                                                        part.GetComponent<UxrGrabbableObjectAnchor>() != null ||
                                                        part.name == ArsenalPresentationApplicator.SupportsContainerName ||
                                                        part.name == ArsenalPresentationApplicator.ReturnHintsContainerName);
            // Ленту красит контроллер слота, а не материал: в превью — цвет «доступно», как у собранного слота.
            if (prefab.Glow != null)
            {
                Transform glow = frame.Find(AnimationUtility.CalculateTransformPath(prefab.Glow.transform, prefab.transform));
                Renderer renderer = glow != null ? glow.GetComponent<Renderer>() : null;
                if (renderer != null) ArsenalSlotGlow.Paint(renderer, prefab.AvailableColor, prefab.Glow.Strength, new MaterialPropertyBlock());
            }

            Item(frame, weapon.WeaponPrefab, presentation.ItemTarget, "Оружие");
            Item(frame, weapon.MagazinePrefab, presentation.MagazineTarget, "Магазин");
            Card(frame, prefab, weapon, presentation);

            if (presentation.Supports.Count != 0)
            {
                Transform supports = Create(ArsenalPresentationApplicator.SupportsContainerName, frame).transform;
                foreach (ArsenalSupportPose support in presentation.Supports)
                    ArsenalSupportProjection.MaterializeSupportRole(supports, support, presentation.Style.SupportModule);
            }
        }

        /// <summary>Предмет в той же мировой позе и масштабе, что выданный стеной (мировой масштаб — префаба).</summary>
        private static void Item(Transform frame, GameObject prefab, ArsenalPresentationPose target, string role)
        {
            ArsenalPresentationPose world = ArsenalPresentationApplicator.ItemWorldPose(frame, prefab, target);
            Transform visual = Create(role + ": " + prefab.name, frame).transform;
            visual.SetPositionAndRotation(world.Position, world.Rotation);
            visual.localScale = prefab.transform.localScale / frame.lossyScale.x;
            CopyVisual(prefab.transform, visual, null);
        }

        /// <summary>Карточка префаба слота в позе и вёрстке раскладки, текст — как у собранного слота.</summary>
        private static void Card(Transform frame, ArsenalSlotController prefab, WeaponInfo weapon, ArsenalPresentationSnapshot presentation)
        {
            ArsenalPriceTag source = prefab.GetComponentInChildren<ArsenalPriceTag>(true);
            if (source == null) return;
            Transform card = Create(source.name, frame).transform;
            card.localScale = source.transform.localScale;
            CopyVisual(source.transform, card, null);
            ArsenalPresentationApplicator.ApplyCard(frame, card, presentation);
            TextMeshPro text = card.GetComponentInChildren<TextMeshPro>(true);
            if (text == null) return;
            ArsenalPriceTag.ApplyFrame(card, text, presentation.CardSize, presentation.CardFontSize);
            text.text = ArsenalPriceTag.Text(weapon, false);
            text.color = ArsenalPriceTag.TextColor(true, false);
            text.ForceMeshUpdate(true, true);
        }

        private static GameObject Create(string name, Transform parent, bool rect = false)
        {
            GameObject go = rect ? EditorUtility.CreateGameObjectWithHideFlags(name, Transient, typeof(RectTransform))
                : EditorUtility.CreateGameObjectWithHideFlags(name, Transient);
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        /// <summary>Копия видимой части иерархии: меши, материалы и текст; выключенное и пропущенное не копируется.</summary>
        private static void CopyVisual(Transform source, Transform target, Func<Transform, bool> skip)
        {
            CopyRenderer(source, target);
            foreach (Transform child in source)
            {
                if (!child.gameObject.activeSelf || (skip != null && skip(child))) continue;
                var text = child.GetComponent<TextMeshPro>();
                Transform copy = Create(child.name, target, text != null).transform;
                copy.SetLocalPositionAndRotation(child.localPosition, child.localRotation);
                copy.localScale = child.localScale;
                if (text != null && child is RectTransform from && copy is RectTransform to)
                {
                    to.anchorMin = from.anchorMin; to.anchorMax = from.anchorMax; to.pivot = from.pivot;
                    to.sizeDelta = from.sizeDelta; to.anchoredPosition3D = from.anchoredPosition3D;
                }
                CopyVisual(child, copy, skip);
            }
        }

        private static void CopyRenderer(Transform source, Transform target)
        {
            var text = source.GetComponent<TextMeshPro>();
            if (text != null)
            {
                var copy = target.gameObject.AddComponent<TextMeshPro>();
                copy.font = text.font; copy.fontSharedMaterial = text.fontSharedMaterial; copy.text = text.text; copy.color = text.color;
                copy.alignment = text.alignment; copy.textWrappingMode = text.textWrappingMode; copy.overflowMode = text.overflowMode;
                copy.enableAutoSizing = text.enableAutoSizing; copy.fontSizeMin = text.fontSizeMin; copy.fontSizeMax = text.fontSizeMax;
                copy.fontSize = text.fontSize;
                return;
            }
            Mesh mesh = null;
            Renderer renderer = source.GetComponent<MeshRenderer>();
            var filter = source.GetComponent<MeshFilter>();
            if (renderer != null && filter != null) mesh = filter.sharedMesh;
            else if (source.GetComponent<SkinnedMeshRenderer>() is SkinnedMeshRenderer skinned && skinned != null)
            {
                // Скиннед-меш — в позе привязки: превью не анимирует кости.
                renderer = skinned;
                mesh = skinned.sharedMesh;
            }
            if (mesh == null || renderer == null || !renderer.enabled) return;
            target.gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var output = target.gameObject.AddComponent<MeshRenderer>();
            output.sharedMaterials = renderer.sharedMaterials;
            output.shadowCastingMode = renderer.shadowCastingMode;
            output.receiveShadows = renderer.receiveShadows;
        }
    }

    /// <summary>Импорт ассетов → <see cref="ArsenalStationScenePreview.OnImported" />.</summary>
    internal sealed class ArsenalStationScenePreviewImport : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom) =>
            ArsenalStationScenePreview.OnImported(imported, deleted);
    }
}
