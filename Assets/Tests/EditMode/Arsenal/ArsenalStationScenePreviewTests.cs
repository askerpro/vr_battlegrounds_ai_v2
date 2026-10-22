using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Maps.Runtime;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Tests.Arsenal
{
    /// <summary>
    ///     Расчёт размещения станции и редакторское превью станций (задача arsenal-station-scene-preview, принято
    ///     пользователем 2026-10-22).
    ///     <para>
    ///     Ряд раскладывает слоты строго по позам своего расчёта (<see cref="ArsenalSlotRow.SlotLocalPoses" />) — этот же
    ///     расчёт читает план станции, по нему видит превью. Превью Lobby собирается по плану
    ///     <see cref="ArsenalStationComposer.PlanComposition" />: те же слоты в тех же позах, план объектов не создаёт,
    ///     копии временные (не сохраняются в сцену и не делают её dirty) и содержат только визуальные компоненты.
    ///     </para>
    /// </summary>
    public sealed class ArsenalStationScenePreviewTests
    {
        private const string LobbyPath = "Assets/Scenes/Lobby.unity";
        private const string CatalogPath = "Assets/Data/Maps/MapRuntimeCatalog.asset";
        private const string PreviewType = "VrBattlegrounds.Editor.Arsenal.ArsenalStationScenePreview";
        private const string RootPrefix = "[Превью арсенала] ";
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly HashSet<Type> VisualComponents = new HashSet<Type>
        {
            typeof(Transform), typeof(RectTransform), typeof(MeshFilter), typeof(MeshRenderer), typeof(TextMeshPro), typeof(TMP_SubMesh)
        };

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object created in _created)
                if (created != null) Object.DestroyImmediate(created);
            _created.Clear();
        }

        [TestCase(ArsenalRowAlignment.Center, -0.35f, 0.05f, 0.5f)]
        [TestCase(ArsenalRowAlignment.Start, 0.3f, 0.7f, 1.15f)]
        [TestCase(ArsenalRowAlignment.End, -1f, -0.6f, -0.15f)]
        public void Ряд_вешает_слоты_в_позы_своего_расчёта(ArsenalRowAlignment alignment, float first, float second, float third)
        {
            var station = new GameObject("RowStation");
            _created.Add(station);
            var row = new GameObject("Row").AddComponent<ArsenalSlotRow>();
            row.transform.SetParent(station.transform, false);
            Quaternion rotation = Quaternion.Euler(0f, 90f, 0f);
            row.Configure("pegboard", ArsenalPresentationZone.Pegboard, null, new Vector3(0.1f, 0.2f, 0f), rotation, 0.05f, alignment);

            float[] widths = { 0.4f, 0.3f, 0.5f };
            var slots = new List<ArsenalSlotController>();
            for (int i = 0; i < widths.Length; i++)
            {
                FirearmSlotController slot = ArsenalTestStation.AddSlot(station, "Slot_" + i);
                typeof(ArsenalSlotController).GetField("_slotWidth", Private).SetValue(slot, widths[i]);
                slots.Add(slot);
            }

            Pose[] poses = row.SlotLocalPoses(widths);
            row.Arrange(slots);

            float[] expected = { first, second, third };
            for (int i = 0; i < slots.Count; i++)
            {
                Transform slot = slots[i].transform;
                Assert.AreSame(row.transform, slot.parent, "Слот не стал ребёнком ряда.");
                Assert.That(Vector3.Distance(poses[i].position, slot.localPosition), Is.LessThan(1e-5f), "Arrange разошёлся с расчётом ряда.");
                Assert.That(Quaternion.Angle(poses[i].rotation, slot.localRotation), Is.LessThan(0.01f));
                Assert.That(Vector3.Distance(new Vector3(expected[i], 0.2f, 0f), poses[i].position), Is.LessThan(1e-5f),
                    $"Центр слота {i}: зазор 0,05 м, выравнивание {alignment}, точка ряда x=0,1.");
                Assert.That(Quaternion.Angle(rotation, poses[i].rotation), Is.LessThan(0.01f));
            }
        }

        [Test]
        public void Превью_Lobby_строится_по_плану_и_не_пишется_в_сцену()
        {
            MethodInfo rebuild = PreviewMethod("Rebuild"), clear = PreviewMethod("Clear");
            WithLoadedLobby(lobby =>
            {
                var catalog = AssetDatabase.LoadAssetAtPath<MapRuntimeCatalog>(CatalogPath);
                Assert.IsNotNull(catalog?.ArsenalComposition, "Нет каталога оформления станций.");
                MapRoot root = lobby.GetRootGameObjects().SelectMany(go => go.GetComponentsInChildren<MapRoot>(true)).Single();
                ArsenalStationCompositionBinding[] stations = root.StationBindings.Where(s => s != null).ToArray();
                Assert.IsNotEmpty(stations, "В Lobby нет станций арсенала.");

                // План — чистый расчёт: объектов сцены не создаёт.
                int objectsBefore = CountObjects(lobby);
                var plans = stations.ToDictionary(s => s.StationKey, s => ArsenalStationComposer.PlanComposition(
                    MapArsenalStationDescriber.Describe(s, root.Map != null ? root.Map.arsenalPreset : null, catalog.ArsenalComposition), s));
                Assert.AreEqual(objectsBefore, CountObjects(lobby), "PlanComposition создал объекты в сцене.");

                string report = (string)rebuild.Invoke(null, null);
                StringAssert.DoesNotContain("Не собраны", report);
                int slots = plans.Values.Sum(p => p.Slots.Count);
                StringAssert.Contains($"{stations.Length} станций, {slots} слотов", report);

                GameObject[] previews = lobby.GetRootGameObjects().Where(go => go.name.StartsWith(RootPrefix, StringComparison.Ordinal)).ToArray();
                Assert.AreEqual(stations.Length, previews.Length, "Один корень превью на станцию.");
                foreach (GameObject preview in previews)
                {
                    ArsenalStationPlan plan = plans[preview.name.Substring(RootPrefix.Length)];
                    AssertSlotsFollowPlan(preview, plan);
                    foreach (Transform part in preview.GetComponentsInChildren<Transform>(true))
                    {
                        Assert.AreNotEqual(HideFlags.None, part.gameObject.hideFlags & HideFlags.DontSaveInEditor,
                            $"{part.name}: копия превью сохранится в сцену.");
                        foreach (Component component in part.GetComponents<Component>())
                            Assert.That(component != null && VisualComponents.Contains(component.GetType()),
                                $"{part.name}: в копии превью не визуальный компонент {component?.GetType().Name ?? "<missing>"}.");
                    }
                }
                Assert.IsFalse(lobby.isDirty, "Превью сделало сцену dirty.");

                clear.Invoke(null, null);
                Assert.IsFalse(lobby.GetRootGameObjects().Any(go => go.name.StartsWith(RootPrefix, StringComparison.Ordinal)),
                    "Clear оставил корни превью.");
                Assert.IsFalse(lobby.isDirty);
            });
        }

        /// <summary>Рамки слотов превью — в рядах плана, в порядке манифеста и в позах плана.</summary>
        private static void AssertSlotsFollowPlan(GameObject preview, ArsenalStationPlan plan)
        {
            var frames = new List<Transform>();
            foreach (Transform row in preview.transform)
            foreach (Transform frame in row)
                frames.Add(frame);
            Assert.AreEqual(plan.Slots.Count, frames.Count, preview.name + ": число слотов превью.");
            foreach (ArsenalSlotPlacement placement in plan.Slots)
            {
                string name = "Slot_" + placement.Manifest.Entry.NetworkIndex + "_" + placement.Manifest.Entry.LogicalSlotKey;
                Transform frame = frames.SingleOrDefault(f => f.name == name);
                Assert.IsNotNull(frame, preview.name + ": нет слота " + name);
                Assert.That(frame.parent.name, Does.EndWith("(" + placement.Row.RowKey + ")"), name + ": не тот ряд.");
                Assert.That(Vector3.Distance(placement.LocalPose.position, frame.localPosition), Is.LessThan(1e-4f), name + ": позиция.");
                Assert.That(Quaternion.Angle(placement.LocalPose.rotation, frame.localRotation), Is.LessThan(0.01f), name + ": поворот.");
                Assert.That(Vector3.Distance(placement.Row.transform.position, frame.parent.position), Is.LessThan(1e-4f), name + ": ряд не на месте.");
            }
        }

        private static int CountObjects(Scene scene) => scene.GetRootGameObjects().Sum(go => go.GetComponentsInChildren<Transform>(true).Length);

        /// <summary>Превью живёт в Assembly-CSharp-Editor, недоступной прямой ссылкой из тестового asmdef.</summary>
        private static MethodInfo PreviewMethod(string name)
        {
            Type preview = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(PreviewType, false)).FirstOrDefault(t => t != null);
            Assert.IsNotNull(preview, "Не загружено превью станций.");
            MethodInfo method = preview.GetMethod(name, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(method, PreviewType + "." + name);
            return method;
        }

        /// <summary>Открывает Lobby на время проверки и возвращает исходный набор сцен (как MapRunPreflightTests).</summary>
        private static void WithLoadedLobby(Action<Scene> check)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                PrefabStageUtility.GetCurrentPrefabStage() != null)
                Assert.Ignore("Проверка сцены требует idle Editor без prefab stage.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    Assert.Ignore("Исходные несохранённые сцены нельзя закрывать ради fixture.");

            SceneSetup[] original = EditorSceneManager.GetSceneManagerSetup();
            Scene active = SceneManager.GetActiveScene();
            bool keepEmpty = active.IsValid() && active.isLoaded && string.IsNullOrEmpty(active.path);
            if (keepEmpty && SceneManager.sceneCount != 1)
                Assert.Ignore("Fixture сохраняет безымянную исходную сцену только при единственной открытой сцене.");
            if (original.Length == 0 && !keepEmpty)
                Assert.Ignore("Нужна исходная пустая сцена: Unity не позволяет выгрузить последнюю открытую Lobby.");
            Scene lobby = default;
            try
            {
                lobby = EditorSceneManager.OpenScene(LobbyPath, keepEmpty ? OpenSceneMode.Additive : OpenSceneMode.Single);
                Assert.AreEqual(LobbyPath, lobby.path);
                check(lobby);
            }
            finally
            {
                if (keepEmpty)
                {
                    if (lobby.IsValid() && lobby.isLoaded) EditorSceneManager.CloseScene(lobby, true);
                    if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
                }
                else
                {
                    EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                    EditorSceneManager.RestoreSceneManagerSetup(original);
                }
            }
        }
    }
}
