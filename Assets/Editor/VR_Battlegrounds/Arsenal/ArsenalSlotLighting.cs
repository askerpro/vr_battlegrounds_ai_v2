using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    ///     Подсветка слотов арсенала в сцене карты (задача arsenal-slot-lighting, решение пользователя 2026-10-22).
    ///     <para>
    ///     Слоты станции создаёт код при загрузке карты, поэтому запечённый свет и пробы живут в сцене, а не в префабе
    ///     слота. Инструмент считает позы слотов тем же кодом, что запуск карты и превью станций
    ///     (<see cref="MapArsenalStationDescriber" />, <see cref="ArsenalStationComposer.PlanComposition" />), в открытой
    ///     позе оборудования (<see cref="ArsenalEquipmentPoses" />), и на каждый слот ставит запечённый прожектор на
    ///     невидимом кронштейне сверху-спереди и пробы света в центре слота и перед ним.
    ///     </para>
    ///     <para>
    ///     Оружие и доска слота — объекты рантайма, в лайтмапу не попадают: прожектор освещает их через пробу
    ///     (оружие светлее целиком, без пятна). Видимое пятно и состояние слота показывает светящаяся подложка
    ///     <see cref="ArsenalSlotGlow" /> в префабе слота. Runtime-цена запечённых источников — ноль.
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
        private const string CatalogPath = "Assets/Data/Maps/MapRuntimeCatalog.asset";

        // Оси слота: лицо — −Z, верх доски — y 1, оружие — около WeaponAnchor (0; 0,368; −0,072).
        // Кронштейн ~30 см сверху-спереди (пользователь 2026-10-22), луч — в центр оружия.
        internal static readonly Vector3 BracketLocal = new Vector3(0f, 0.75f, -0.32f);
        internal static readonly Vector3 AimLocal = new Vector3(0f, 0.40f, -0.07f);
        internal static readonly Vector3 ProbeCenterLocal = new Vector3(0f, 0.40f, -0.07f);
        internal static readonly Vector3 ProbeFrontLocal = new Vector3(0f, 0.40f, -0.32f);

        internal const float SpotIntensity = 1.5f, SpotRange = 1.2f, SpotAngle = 70f, SpotInnerAngle = 40f;
        internal static readonly Color SpotColor = new Color(1f, .9f, .78f);

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
                IReadOnlyList<Matrix4x4> slots = SlotFrames(station, root.Map != null ? root.Map.arsenalPreset : null, catalog);
                var group = new GameObject(station.StationKey).transform;
                group.SetParent(container.transform, false);
                for (int i = 0; i < slots.Count; i++)
                {
                    CreateSpot(group, "SlotLight_" + i, slots[i]);
                    probes.Add(slots[i].MultiplyPoint3x4(ProbeCenterLocal));
                    probes.Add(slots[i].MultiplyPoint3x4(ProbeFrontLocal));
                }
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

        /// <summary>Мировые оси каждого слота станции в открытой позе оборудования, в порядке плана.</summary>
        public static IReadOnlyList<Matrix4x4> SlotFrames(ArsenalStationCompositionBinding station, ArsenalPreset preset,
            ArsenalCompositionCatalog catalog)
        {
            ArsenalStationDescription description = MapArsenalStationDescriber.Describe(station, preset, catalog);
            if (!description.Success)
                throw new InvalidOperationException(station.StationKey + ": описание станции — " + string.Join(", ",
                    description.Failures.Select(f => f.Kind + (string.IsNullOrEmpty(f.LogicalSlotKey) ? "" : "/" + f.LogicalSlotKey))));
            ArsenalStationPlan plan = ArsenalStationComposer.PlanComposition(description, station);
            ArsenalEquipmentPoses poses = station.GetComponentInChildren<ArsenalEquipmentPoses>(true);
            return plan.Slots.Select(placement => OpenRowMatrix(placement.Row.transform, poses) *
                                                  Matrix4x4.TRS(placement.LocalPose.position, placement.LocalPose.rotation, Vector3.one))
                       .ToList();
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

        private static void CreateSpot(Transform parent, string name, Matrix4x4 slot)
        {
            Vector3 bracket = slot.MultiplyPoint3x4(BracketLocal), aim = slot.MultiplyPoint3x4(AimLocal);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(bracket, Quaternion.LookRotation(aim - bracket, slot.MultiplyVector(Vector3.up)));
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
    }
}
