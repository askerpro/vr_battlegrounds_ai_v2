using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace VrBattlegrounds.Tests.Maps
{
    /// <summary>
    /// Принятое состояние рендера Lobby под Quest (scene-detail, 2026-10-20: визуальная приёмка в шлеме и normal A/B
    /// 10,81 → 4,26 мс): нет трипланарного S_VPWorldAligned, box-UV у целей в метрах, конфиг сканера актуален,
    /// грунт на Baked Lit с параметрами авторинга, пол шатра в лайтмапе, свет шатра покрывает помещение,
    /// плафоны светятся. Ассеты и сцена не пересохраняются; проверка правила отражений временно выключает пробу шатра
    /// в открытой сцене и возвращает её в finally.
    /// Правила — Docs/quest-performance-manifest.md (QPM-SHADER-001/002/005/006, QPM-LIGHT-002/004/008). Свет шатра и пробу
    /// отражений принял пользователь 2026-10-22 (lobby-reflections).
    /// </summary>
    public class LobbyQuestRenderStateTests
    {
        private const string LobbyPath = "Assets/Scenes/Lobby.unity";
        private const string BakedLit = "Universal Render Pipeline/Baked Lit";
        private const string FloorPath = "Environment/ArmoryRangeZone/MapShell/floor";
        private const string FloorMeshPath = "Assets/Art/Lobby/Generated/LobbyTentFloor.asset";

        private Scene _scene;
        private bool _opened;

        [OneTimeSetUp]
        public void OpenLobby()
        {
            _scene = SceneManager.GetSceneByPath(LobbyPath);
            if (_scene.IsValid() && _scene.isLoaded) return;
            _scene = EditorSceneManager.OpenScene(LobbyPath, OpenSceneMode.Additive);
            _opened = true;
        }

        [OneTimeTearDown]
        public void CloseLobby()
        {
            if (_opened) EditorSceneManager.CloseScene(_scene, true);
        }

        // Тестовая asmdef не может ссылаться на predefined Assembly-CSharp-Editor.
        private static Type EditorType(string fullName)
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(fullName)).FirstOrDefault(t => t != null);
            Assert.IsNotNull(type, "Editor assembly с " + fullName + " не загружена.");
            return type;
        }

        private IEnumerable<Renderer> Renderers() =>
            _scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>(true));

        [Test]
        public void NoLobbyRendererUsesTriplanarWorldAlignedShader()
        {
            var offenders = Renderers()
                .SelectMany(r => r.sharedMaterials.Where(m => m != null && m.shader != null && m.shader.name.Contains("S_VPWorldAligned"))
                    .Select(m => r.name + ":" + m.name))
                .Distinct().ToList();
            Assert.IsEmpty(offenders, "QPM-SHADER-002: трипланарный S_VPWorldAligned заменён на box-UV + Baked Lit");
        }

        [Test]
        public void GroundMaterialsMatchAuthoringBakedLitState()
        {
            var floor = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Lobby/WornGround.mat");
            var gravel = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Lobby/OutsideGravel.mat");
            foreach (var m in new[] { floor, gravel })
            {
                Assert.IsNotNull(m);
                Assert.AreEqual(BakedLit, m.shader.name, m.name + ": QPM-SHADER-001");
            }
            Assert.That(floor.GetTextureScale("_BaseMap").x, Is.EqualTo(39.59f / 2f).Within(1e-3f), "повтор пола — каждые 2 м");
            Assert.That(floor.GetColor("_BaseColor").r, Is.EqualTo(0.85f).Within(1e-3f));
            Assert.That(gravel.GetTextureScale("_BaseMap").x, Is.EqualTo(45f).Within(1e-3f));
        }

        [Test]
        public void BoxUvTargetsAreImportedInMetres()
        {
            Type targetsType = EditorType("VrBattlegrounds.Editor.Performance.BoxUv.BoxUvTargets");
            object config = targetsType.GetMethod("Load", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
            var targets = (IList)targetsType.GetField("Targets").GetValue(config);
            Assert.Greater(targets.Count, 0, "конфиг box-UV не пуст");
            var bad = new List<string>();
            foreach (object target in targets)
            {
                Type t = target.GetType();
                string model = (string)t.GetField("Model").GetValue(target);
                string meshName = (string)t.GetField("Mesh").GetValue(target);
                var submeshes = (List<int>)t.GetField("Submeshes").GetValue(target);
                Mesh mesh = AssetDatabase.LoadAllAssetsAtPath(model).OfType<Mesh>().FirstOrDefault(m => m.name == meshName);
                if (mesh == null) { bad.Add(model + "/" + meshName + ": нет меша"); continue; }
                Vector3[] v = mesh.vertices;
                Vector2[] uv = mesh.uv;
                double area3 = 0, area2 = 0;
                foreach (int sub in submeshes)
                {
                    int[] tri = mesh.GetTriangles(sub);
                    for (int i = 0; i + 2 < tri.Length; i += 3)
                    {
                        area3 += Vector3.Cross(v[tri[i + 1]] - v[tri[i]], v[tri[i + 2]] - v[tri[i]]).magnitude * 0.5;
                        Vector2 p = uv[tri[i + 1]] - uv[tri[i]], q = uv[tri[i + 2]] - uv[tri[i]];
                        area2 += Math.Abs(p.x * q.y - p.y * q.x) * 0.5;
                    }
                }
                double metresPerUv = area2 > 0 ? Math.Sqrt(area3 / area2) : -1;
                if (Math.Abs(metresPerUv - 1.0) > 0.05) bad.Add(model + "/" + meshName + ": " + metresPerUv.ToString("0.###") + " м/UV");
                if (mesh.uv2.Length != mesh.vertexCount) bad.Add(model + "/" + meshName + ": нет UV2 для лайтмапы");
            }
            Assert.IsEmpty(bad, "постпроцессор box-UV применён при импорте");
        }

        [Test]
        public void BoxUvConfigMatchesFreshScan()
        {
            Type targetsType = EditorType("VrBattlegrounds.Editor.Performance.BoxUv.BoxUvTargets");
            Type scanner = EditorType("VrBattlegrounds.Editor.Performance.BoxUv.BoxUvTargetScanner");
            object committed = targetsType.GetMethod("Load", BindingFlags.Public | BindingFlags.Static).Invoke(null, null);
            var scenes = (string[])scanner.GetField("DefaultScenes").GetValue(null);
            object fresh = scanner.GetMethod("Scan", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { scenes });
            Assert.AreEqual(Describe(committed), Describe(fresh),
                "BoxUvTargets.json устарел: перезапустить Tools/VR Battlegrounds/Performance/Box UV/Scan And Apply");
        }

        private static string Describe(object config)
        {
            var targets = (IList)config.GetType().GetField("Targets").GetValue(config);
            return string.Join("\n", targets.Cast<object>().Select(t =>
            {
                Type type = t.GetType();
                return type.GetField("Model").GetValue(t) + "|" + type.GetField("Mesh").GetValue(t) + "|" +
                       string.Join(",", (List<int>)type.GetField("Submeshes").GetValue(t));
            }).OrderBy(s => s, StringComparer.Ordinal));
        }

        [Test]
        public void TentFloorIsLightmappedWithItsOwnMesh()
        {
            string rootName = FloorPath.Substring(0, FloorPath.IndexOf('/'));
            GameObject floor = _scene.GetRootGameObjects().Where(r => r.name == rootName)
                .Select(r => r.transform.Find(FloorPath.Substring(rootName.Length + 1)))
                .FirstOrDefault(t => t != null)?.gameObject;
            Assert.IsNotNull(floor, FloorPath);
            Mesh mesh = floor.GetComponent<MeshFilter>().sharedMesh;
            Assert.AreEqual(FloorMeshPath, AssetDatabase.GetAssetPath(mesh), "QPM-LIGHT-003: свой меш, общий FBX не трогаем");
            Assert.AreEqual(mesh.vertexCount, mesh.uv2.Length, "у меша пола есть UV2");
            var renderer = floor.GetComponent<MeshRenderer>();
            Assert.AreEqual(ReceiveGI.Lightmaps, renderer.receiveGI, "QPM-LIGHT-002: пол в лайтмапе, не на пробе");
            Assert.GreaterOrEqual(renderer.lightmapIndex, 0, "пол запечён в лайтмапу");
        }

        // Свет шатра принят пользователем 2026-10-22 (lobby-reflections): фон — светящиеся круги под кровлей, акценты —
        // прожекторы под плафонами только над точками стрельбы; ламп-плафонов CampLighting/BayLamp/Pendant и плафона над
        // арсеналом нет (акцент арсенала — задача arsenal-slot-lighting).
        private const string FixturesPath = "Environment/ArmoryRangeZone/OpenTent/TentLightFixtures";

        private Transform Find(string path)
        {
            string rootName = path.Substring(0, path.IndexOf('/'));
            return _scene.GetRootGameObjects().Where(r => r.name == rootName)
                .Select(r => r.transform.Find(path.Substring(rootName.Length + 1))).FirstOrDefault(t => t != null);
        }

        [Test]
        public void TentBackgroundIsDiscsAndAccentsAreRangeSpots()
        {
            Transform fixtures = Find(FixturesPath);
            Assert.IsNotNull(fixtures, FixturesPath);
            var fills = fixtures.Cast<Transform>().Where(t => t.name.StartsWith("TentFill_")).ToList();
            Assert.GreaterOrEqual(fills.Count, 20, "фон шатра — сетка кругов");
            foreach (Transform fill in fills)
            {
                Light light = fill.GetComponentInChildren<Light>(true);
                Assert.IsNotNull(light, fill.name + ": свет у сокета (ночной пресет)");
                Assert.AreEqual("InteriorFillLight", light.name);
                Assert.AreEqual(LightmapBakeType.Baked, light.lightmapBakeType, fill.name + ": QPM-LIGHT-001");
            }
            var accents = fixtures.Cast<Transform>().Where(t => t.name.StartsWith("TentAccent_")).Select(t => t.name).OrderBy(n => n).ToArray();
            CollectionAssert.AreEqual(new[] { "TentAccent_RangeNorth", "TentAccent_RangeSouth" }, accents, "плафоны — только над точками стрельбы");
            foreach (string name in accents)
            {
                Light light = fixtures.Find(name).GetComponentInChildren<Light>(true);
                Assert.AreEqual(LightType.Spot, light.type, name + ": прожектор вниз — пятно на полу точки стрельбы");
                Assert.Greater(Vector3.Dot(light.transform.forward, Vector3.down), 0.99f, name + ": направлен вниз");
                Assert.AreEqual(LightmapBakeType.Baked, light.lightmapBakeType, name);
            }
            Transform zone = Find("Environment/ArmoryRangeZone");
            var legacy = zone.Cast<Transform>().Where(t => t.name == "SM_Lamp_long" || t.name == "InteriorWarmLight").Select(t => t.name).ToList();
            Assert.IsEmpty(legacy, "плафоны CampLighting и их лампы убраны");
            Assert.IsNull(zone.Find("OpenTent/EastBays/Lamps"), "плафоны BayLamp убраны");
            Assert.IsNull(zone.Find("OpenTent/WestWallDetail/Pendants"), "плафоны SM_TopLight убраны");
        }

        [Test]
        public void TentLightFixturesStayOutOfGi()
        {
            Transform fixtures = Find(FixturesPath);
            var lampGlow = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Lobby/Night/LobbyNightLampLong.mat");
            var renderers = fixtures.GetComponentsInChildren<MeshRenderer>(true)
                .Concat(Renderers().OfType<MeshRenderer>().Where(r => r.sharedMaterials.Contains(lampGlow))).Distinct().ToList();
            Assert.IsNotEmpty(renderers);
            foreach (MeshRenderer renderer in renderers)
            {
                var flags = GameObjectUtility.GetStaticEditorFlags(renderer.gameObject);
                Assert.AreEqual((StaticEditorFlags)0, flags & StaticEditorFlags.ContributeGI,
                    renderer.name + ": светлые круги в GI давали «светлячков» в пробах, плафон освещался своей лампой");
                Assert.AreEqual(LightProbeUsage.Off, renderer.lightProbeUsage, renderer.name + ": вид светильника задаёт эмиссия");
            }
        }

        [Test]
        public void TentReflectionProbeCoversWeaponZones()
        {
            var probe = _scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ReflectionProbe>(false))
                .SingleOrDefault(p => p.name == "TentReflectionProbe");
            Assert.IsNotNull(probe, "QPM-LIGHT-008: проба отражений шатра");
            Assert.AreEqual(ReflectionProbeMode.Baked, probe.mode);
            Assert.IsTrue(probe.boxProjection);
            Assert.IsNotNull(probe.bakedTexture, "проба запечена с Generate Lighting");
            var zones = _scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MonoBehaviour>(false))
                .Where(c => c != null && (c.GetType().Name == "ArsenalStationAnchor" || c.GetType().Name == "TeamSpawnZone"))
                .Select(c => c.transform.position + Vector3.up * 1.2f).ToList();
            Assert.IsNotEmpty(zones);
            foreach (Vector3 point in zones)
                Assert.IsTrue(probe.bounds.Contains(point), "зона оружия " + point + " внутри пробы");
            Transform floor = Find(FloorPath);
            Assert.AreNotEqual((StaticEditorFlags)0, GameObjectUtility.GetStaticEditorFlags(floor.gameObject) & StaticEditorFlags.ReflectionProbeStatic,
                "без ReflectionProbeStatic запечённая проба снимала одно небо — металл чернел");
        }

        [Test]
        public void DarkReflectionRuleFindsNothingAndCatchesMissingProbe()
        {
            Type runner = EditorType("VrBattlegrounds.Editor.Performance.AssetAudit.AssetAuditRunner");
            Func<int> findings = () =>
            {
                object report = runner.GetMethod("RunScene", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { LobbyPath });
                var list = (IEnumerable)report.GetType().GetProperty("Findings").GetValue(report);
                return list.Cast<object>().Count(f => (string)f.GetType().GetProperty("RuleId").GetValue(f) == "lighting.dark-reflection-zone");
            };
            Assert.AreEqual(0, findings(), "QPM-LIGHT-008: зоны оружия под пробой");
            ReflectionProbe probe = _scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ReflectionProbe>(false))
                .Single(p => p.name == "TentReflectionProbe");
            probe.enabled = false;
            try
            {
                Assert.Greater(findings(), 0, "без пробы правило ловит тёмные зоны оружия (так было до задачи)");
            }
            finally
            {
                probe.enabled = true;
            }
        }

        [Test]
        public void CivilianLampsAreRemovedFromAssetBase()
        {
            const string catalog = "Assets/Art/Lobby/Imported/Catalog/";
            string[] removed =
            {
                "RuralTown/URP/Art/Prefabs/SM_TopLight.prefab", "RuralTown/URP/Art/Prefabs/SM_Lamp.prefab",
                "RuralTown/URP/Art/Prefabs/SM_LampPost.prefab", "RuralTown/URP/Art/Prefabs/SM_Lamp_02.prefab",
                "PostApocalypticTown/URP/Art/Prefabs/SM_Lamp.prefab", "PostApocalypticTown/URP/Art/Prefabs/SM_Light1.prefab",
                "PostApocalypticTown/URP/Art/Prefabs/SM_Light3.prefab", "AbandonedFactory/Art/Prefabs/SM_Lamp_01a.prefab",
                "AbandonedFactory/Art/Prefabs/SM_CoverLight_01.prefab",
            };
            var present = removed.Where(p => AssetDatabase.LoadAssetAtPath<GameObject>(catalog + p) != null).ToList();
            Assert.IsEmpty(present, "гражданские лампы не место в базе ассетов военного лобби (пользователь 2026-10-22)");
        }

        [Test]
        // Дверная лампа: приёмка пользователем 2026-10-22 (scene-detail-followups) — после EmissionOffFixer светится.
        [TestCase("Assets/Materials/Lobby/Night/LobbyNightLampLong.mat")]
        [TestCase("Assets/Materials/Lobby/Night/LobbyNightDoorLamp.mat")]
        public void LampMaterialsGlowWithoutGiAndHighlights(string path)
        {
            var lamp = AssetDatabase.LoadAssetAtPath<Material>(path);
            Assert.IsNotNull(lamp);
            // Состояние с диска: URP при импорте пересчитывает _EMISSION по GI-флагам, поэтому проверяется сохранённый файл.
            string text = System.IO.File.ReadAllText(AssetDatabase.GetAssetPath(lamp));
            Assert.That(text, Does.Contain("- _EMISSION"), "QPM-SHADER-006: _EMISSION сохранён в ассете");
            Assert.AreEqual(MaterialGlobalIlluminationFlags.RealtimeEmissive, lamp.globalIlluminationFlags,
                "None/EmissiveIsBlack выключают _EMISSION при импорте; BakedEmissive дал бы вклад в лайтмапы");
            Assert.AreEqual("Universal Render Pipeline/Simple Lit", lamp.shader.name, "Baked Lit эмиссии не имеет");
            Assert.AreEqual(1f, lamp.GetFloat("_SpecularHighlights"), "QPM-SHADER-005: 1 = NoSpecular");
            Assert.IsNotNull(lamp.GetTexture("_EmissionMap"), "светится рассеиватель по карте эмиссии, а не весь корпус");
        }
    }
}
