using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Maps.Runtime;

namespace VrBattlegrounds.Editor.Arsenal
{
    /// <summary>
    ///     Подсветка слотов арсенала в сцене карты (задача arsenal-slot-lighting, решения пользователя 2026-10-22/23).
    ///     <para>
    ///     Слоты станции создаёт код при загрузке карты, поэтому запечённый свет и пробы живут в сцене, а не в префабе
    ///     слота. Инструмент считает позы слотов тем же кодом, что запуск карты и превью станций
    ///     (<see cref="MapArsenalStationDescriber" />, <see cref="ArsenalStationComposer.PlanComposition" />), в открытой
    ///     позе оборудования (<see cref="ArsenalEquipmentPoses" />). На каждый слот — запечённый прожектор на невидимом
    ///     кронштейне сверху-спереди, луч в лицевую сторону доски слота, и пробы света там же и перед слотом.
    ///     </para>
    ///     <para>
    ///     Оружие почти целиком металл: его вид — отражение, а не рассеянный свет. Поэтому у станции своя запечённая
    ///     проба отражений (Box Projection) и над верхним рядом светящаяся LED-планка — в пробе она даёт металлу блик.
    ///     Прожектор через пробу света освещает неметаллические части. Видимое пятно и состояние слота — подложка
    ///     <see cref="ArsenalSlotGlow" /> в префабе слота. Runtime-цена: одна кубмапа на станцию, источники запечены.
    ///     </para>
    ///     <para>
    ///     Владелец <c>Environment/ArsenalSlotLighting</c> — только этот инструмент; контейнер пересобирается целиком.
    ///     После смены пресета, раскладки или станции — запустить снова и перезапечь свет сцены.
    ///     </para>
    /// </summary>
    public static class ArsenalSlotLighting
    {
        public const string MenuPath = "Tools/VR Battlegrounds/Arsenal/Подсветка слотов (свет и пробы в сцене)";
        public const string ContainerName = "ArsenalSlotLighting";
        public const string ProbeGroupName = "SlotProbes";
        public const string BarName = "SlotLedBar", ReflectionName = "SlotReflections";
        public const string BoardName = "PegboardSection";
        public const string BarMaterialPath = "Assets/Materials/Arsenal/ArsenalSlotLedBar.mat";
        private const string CatalogPath = "Assets/Data/Maps/MapRuntimeCatalog.asset";

        // Точки в осях слота (лицо — −Z, верх — +Y): прицел — 7 см перед лицевой стороной доски слота (вертикальная
        // доска стрелкового ряда и горизонтальная полка пистолетов), кронштейн — 35 см выше и 30 см вперёд от прицела.
        internal const float AimOffset = 0.07f, BracketUp = 0.35f, BracketForward = 0.30f, ProbeForward = 0.25f;

        // 1,5 давало в центре слота 1,69 при фоне шатра 0,14, но металл оставался тёмным (запекание 1079); 0,3 — 0,44,
        // «слишком тускло» (пользователь 2026-10-23); металлу блик даёт планка в пробе отражений, прожектор — 0,6.
        internal const float SpotIntensity = 0.6f, SpotRange = 1.2f, SpotAngle = 70f, SpotInnerAngle = 40f;
        internal static readonly Color SpotColor = new Color(1f, .9f, .78f);

        // Планка: 6 см над верхом доски верхнего ряда, 22 см перед ней; яркость >1 — в HDR-пробе это блик.
        internal const float BarAbove = 0.06f, BarForward = 0.22f, BarHeight = 0.025f, BarDepth = 0.04f, BarOverhang = 0.25f;
        internal const float BarBrightness = 3f;
        internal static readonly Color BarColor = new Color(1f, .92f, .82f);
        internal const float ReflectionMargin = 0.4f;
        internal const int ReflectionResolution = 128, ReflectionImportance = 2;

        /// <summary>Расчётные точки слота в мире.</summary>
        public readonly struct SlotPoints
        {
            public readonly ArsenalSlotRow Row;
            public readonly Vector3 Aim, Bracket, Front, BoardTop, Up, Forward, Along;

            public SlotPoints(ArsenalSlotRow row, Vector3 aim, Vector3 bracket, Vector3 front, Vector3 boardTop, Vector3 up,
                Vector3 forward, Vector3 along)
            {
                Row = row; Aim = aim; Bracket = bracket; Front = front; BoardTop = boardTop; Up = up; Forward = forward; Along = along;
            }
        }

        [MenuItem(MenuPath)]
        private static void Menu()
        {
            string report = Apply(SceneManager.GetActiveScene());
            GameLog.Arsenal.Info("[ArsenalSlotLighting] " + report);
            EditorUtility.DisplayDialog("Подсветка слотов арсенала", report, "OK");
        }

        [MenuItem(MenuPath, true)]
        private static bool MenuValidate() => !EditorApplication.isPlayingOrWillChangePlaymode;

        /// <summary>Пересобрать подсветку слотов всех станций сцены. Возвращает отчёт.</summary>
        public static string Apply(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("Сцена не загружена.");
            GameObject environment = scene.GetRootGameObjects().SingleOrDefault(r => r.name == "Environment")
                                     ?? throw new InvalidOperationException("В сцене " + scene.name + " нет корня Environment.");
            ArsenalCompositionCatalog catalog = AssetDatabase.LoadAssetAtPath<MapRuntimeCatalog>(CatalogPath)?.ArsenalComposition
                                                ?? throw new InvalidOperationException("Нет каталога оформления станций (" + CatalogPath + ").");
            Material barMaterial = BarMaterial();

            Transform old = environment.transform.Find(ContainerName);
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
            var container = new GameObject(ContainerName);
            Undo.RegisterCreatedObjectUndo(container, "Подсветка слотов арсенала");
            container.transform.SetParent(environment.transform, false);

            var report = new StringBuilder();
            var probes = new List<Vector3>();
            int stations = 0, lights = 0;
            foreach (GameObject top in scene.GetRootGameObjects())
            foreach (MapRoot root in top.GetComponentsInChildren<MapRoot>(true))
            foreach (ArsenalStationCompositionBinding station in root.StationBindings)
            {
                if (station == null) continue;
                IReadOnlyList<SlotPoints> slots = Slots(station, root.Map != null ? root.Map.arsenalPreset : null, catalog);
                if (slots.Count == 0) continue;
                var group = new GameObject(station.StationKey).transform;
                group.SetParent(container.transform, false);
                for (int i = 0; i < slots.Count; i++)
                {
                    CreateSpot(group, "SlotLight_" + i, slots[i]);
                    probes.Add(slots[i].Aim);
                    probes.Add(slots[i].Front);
                }
                CreateBar(group, slots, barMaterial);
                CreateReflectionProbe(group, slots);
                stations++;
                lights += slots.Count;
                report.Append(station.StationKey).Append(": ").Append(slots.Count).Append(" слотов. ");
            }

            var probeGroup = new GameObject(ProbeGroupName).AddComponent<LightProbeGroup>();
            probeGroup.transform.SetParent(container.transform, false);
            probeGroup.probePositions = probes.Select(p => container.transform.InverseTransformPoint(p)).ToArray();

            EditorSceneManager.MarkSceneDirty(scene);
            return $"Станций {stations}, прожекторов {lights}, проб {probes.Count}. " + report +
                   "Перезапеките свет сцены (Generate Lighting).";
        }

        /// <summary>Точки подсветки каждого слота станции в открытой позе оборудования, в порядке плана.</summary>
        public static IReadOnlyList<SlotPoints> Slots(ArsenalStationCompositionBinding station, ArsenalPreset preset,
            ArsenalCompositionCatalog catalog)
        {
            ArsenalStationDescription description = MapArsenalStationDescriber.Describe(station, preset, catalog);
            if (!description.Success)
                throw new InvalidOperationException(station.StationKey + ": описание станции — " + string.Join(", ",
                    description.Failures.Select(f => f.Kind + (string.IsNullOrEmpty(f.LogicalSlotKey) ? "" : "/" + f.LogicalSlotKey))));
            ArsenalStationPlan plan = ArsenalStationComposer.PlanComposition(description, station);
            ArsenalEquipmentPoses poses = station.GetComponentInChildren<ArsenalEquipmentPoses>(true);
            return plan.Slots.Select(placement => Points(placement.Row, OpenRowMatrix(placement.Row.transform, poses) *
                                                  Matrix4x4.TRS(placement.LocalPose.position, placement.LocalPose.rotation, Vector3.one)))
                       .ToList();
        }

        /// <summary>Точки слота по его доске (<see cref="BoardName" /> в префабе слота ряда).</summary>
        internal static SlotPoints Points(ArsenalSlotRow row, Matrix4x4 frame)
        {
            Transform board = row.SlotPrefab != null ? row.SlotPrefab.transform.Find(BoardName) : null;
            Vector3 center = board != null ? board.localPosition : new Vector3(0f, .5f, 0f);
            Quaternion rotation = board != null ? board.localRotation : Quaternion.identity;
            float halfHeight = board != null ? board.localScale.y * .5f : .5f;
            // Лицевая сторона доски — та, что смотрит к игроку: вперёд у стены, вверх у полки.
            Vector3 face = rotation * Vector3.back;
            if (Vector3.Dot(face, Vector3.up + Vector3.back) < 0f) face = -face;
            Vector3 boardUp = rotation * Vector3.up;
            if (Vector3.Dot(boardUp, Vector3.up) < 0f) boardUp = -boardUp;

            Vector3 aim = center + face * AimOffset;
            Vector3 bracket = aim + Vector3.up * BracketUp + Vector3.back * BracketForward;
            Vector3 front = aim + Vector3.back * ProbeForward;
            Vector3 top = center + boardUp * halfHeight;
            return new SlotPoints(row, frame.MultiplyPoint3x4(aim), frame.MultiplyPoint3x4(bracket), frame.MultiplyPoint3x4(front),
                frame.MultiplyPoint3x4(top), frame.MultiplyVector(Vector3.up).normalized, frame.MultiplyVector(Vector3.back).normalized,
                frame.MultiplyVector(Vector3.right).normalized);
        }

        /// <summary>Мировая матрица ряда, когда подвижное оборудование станции полностью выдвинуто.</summary>
        internal static Matrix4x4 OpenRowMatrix(Transform row, ArsenalEquipmentPoses poses)
        {
            if (poses != null)
                foreach (ArsenalEquipmentPoses.PoseTarget pose in poses.Targets)
                {
                    if (pose.Target == null || pose.OpenPose == null || !row.IsChildOf(pose.Target)) continue;
                    Matrix4x4 open = Matrix4x4.TRS(pose.OpenPose.position, pose.OpenPose.rotation, pose.Target.lossyScale);
                    return open * pose.Target.worldToLocalMatrix * row.localToWorldMatrix;
                }
            return row.localToWorldMatrix;
        }

        private static void CreateSpot(Transform parent, string name, SlotPoints slot)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(slot.Bracket, Quaternion.LookRotation(slot.Aim - slot.Bracket, slot.Up));
            var light = go.AddComponent<Light>();
            light.type = LightType.Spot;
            light.lightmapBakeType = LightmapBakeType.Baked;
            light.color = SpotColor;
            light.intensity = SpotIntensity;
            light.range = SpotRange;
            light.spotAngle = SpotAngle;
            light.innerSpotAngle = SpotInnerAngle;
            light.shadows = LightShadows.None;
        }

        /// <summary>LED-планка над верхним рядом станции: видна в пробе отражений, в GI не участвует.</summary>
        private static void CreateBar(Transform parent, IReadOnlyList<SlotPoints> slots, Material material)
        {
            float topY = slots.Max(s => s.BoardTop.y);
            List<SlotPoints> row = slots.Where(s => s.BoardTop.y > topY - 0.05f).ToList();
            Vector3 along = row[0].Along, origin = row[0].BoardTop;
            float min = row.Min(s => Vector3.Dot(s.BoardTop - origin, along)) - BarOverhang;
            float max = row.Max(s => Vector3.Dot(s.BoardTop - origin, along)) + BarOverhang;
            Vector3 mid = row.Aggregate(Vector3.zero, (sum, s) => sum + s.BoardTop) / row.Count;
            mid += along * ((min + max) * .5f - Vector3.Dot(mid - origin, along));

            GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.DestroyImmediate(bar.GetComponent<Collider>());
            bar.name = BarName;
            bar.transform.SetParent(parent, false);
            bar.transform.SetPositionAndRotation(mid + row[0].Up * BarAbove + row[0].Forward * BarForward,
                Quaternion.LookRotation(-row[0].Forward, row[0].Up));
            bar.transform.localScale = new Vector3(max - min, BarHeight, BarDepth);
            var renderer = bar.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            GameObjectUtility.SetStaticEditorFlags(bar, StaticEditorFlags.ReflectionProbeStatic | StaticEditorFlags.BatchingStatic);
        }

        /// <summary>Запечённая проба отражений станции: коробка вокруг всех слотов с запасом.</summary>
        private static void CreateReflectionProbe(Transform parent, IReadOnlyList<SlotPoints> slots)
        {
            var bounds = new Bounds(slots[0].Aim, Vector3.zero);
            foreach (SlotPoints slot in slots)
            {
                bounds.Encapsulate(slot.Aim);
                bounds.Encapsulate(slot.Front);
                bounds.Encapsulate(slot.BoardTop);
            }
            bounds.Expand(ReflectionMargin * 2f);

            var go = new GameObject(ReflectionName);
            go.transform.SetParent(parent, false);
            go.transform.position = bounds.center;
            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Baked;
            probe.boxProjection = true;
            probe.size = bounds.size;
            probe.center = Vector3.zero;
            probe.resolution = ReflectionResolution;
            probe.hdr = true;
            probe.importance = ReflectionImportance;
            probe.blendDistance = 0f;
        }

        /// <summary>Материал планки: Unlit тёплый белый с яркостью выше 1 (в HDR-пробе — блик). Писатель — этот инструмент.</summary>
        private static Material BarMaterial()
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(BarMaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(material, BarMaterialPath);
            }
            Color color = BarColor * BarBrightness;
            color.a = 1f;
            if (material.GetColor("_BaseColor") != color)
            {
                material.SetColor("_BaseColor", color);
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
            }
            return material;
        }
    }
}
