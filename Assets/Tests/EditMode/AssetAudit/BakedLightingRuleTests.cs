using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Editor.Performance.AssetAudit;
using VrBattlegrounds.Editor.Performance.AssetAudit.Fixers;
using VrBattlegrounds.Editor.Performance.AssetAudit.Rules;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Tests.AssetAudit
{
    /// <summary>
    /// Общие правила запечённого света и их фиксеры (приёмка пользователем 2026-10-24, задача lobby-decor-builders):
    /// реалтайм-свет и незапечённая сцена на любой карте, ContributeGI у подвижных, меш в лайтмапе без UV2
    /// (своя копия меша для сцены — QPM-LIGHT-003), игровые зоны без проб. Сцены синтетические, во временной папке.
    /// </summary>
    public sealed class BakedLightingRuleTests
    {
        private const string Folder = "Assets/Tests/EditMode/AssetAudit/TmpBakedLighting";
        private const string ScenePath = Folder + "/BakedLightingTest.unity";
        private const string SharedModel = "Assets/Models/Arenas/Nalchick/Tolstogo_street/nalchik_tolst.fbx";

        private Scene _scene;

        // Сцена одна (Single): раннер EditMode держит несохранённую untitled-сцену, рядом с ней новую создать нельзя;
        // исходный набор сцен Test Framework восстанавливает после прогона.
        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.CreateFolder("Assets/Tests/EditMode/AssetAudit", "TmpBakedLighting");
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            Assert.IsTrue(EditorSceneManager.SaveScene(_scene, ScenePath), "временная сцена не сохранилась");
        }

        [TearDown]
        public void TearDown()
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(Folder);
        }

        [Test]
        public void RealtimeLight_ReportsEnabledRealtimeAndMixed_NotBakedOrDisabled()
        {
            Light realtime = AddLight("Realtime", LightmapBakeType.Realtime);
            Light mixed = AddLight("Mixed", LightmapBakeType.Mixed);
            AddLight("Baked", LightmapBakeType.Baked);
            AddLight("Off", LightmapBakeType.Realtime).enabled = false;

            List<AuditFinding> findings = Check(new RealtimeLightInBakedSceneRule());

            Assert.That(findings.SelectMany(f => f.Items), Is.EquivalentTo(new[] { "Realtime (Point)", "Mixed (Point)" }));
            Assert.That(findings.SelectMany(f => f.FixTargets),
                Is.EquivalentTo(new[] { GlobalObjectId.GetGlobalObjectIdSlow(realtime).ToString(), GlobalObjectId.GetGlobalObjectIdSlow(mixed).ToString() }));
            Assert.That(findings.All(f => f.Severity == AuditSeverity.Warning));
        }

        [Test]
        public void SceneNotBaked_SceneWithLightWithoutLightingSettings_IsReported_EmptySceneIsNot()
        {
            Assert.That(Check(new SceneNotBakedRule()), Is.Empty, "пустая сцена");

            AddLight("Sun", LightmapBakeType.Realtime);
            List<AuditFinding> findings = Check(new SceneNotBakedRule());

            Assert.That(findings, Has.Count.EqualTo(1));
            StringAssert.Contains("не назначен ассет LightingSettings", findings[0].Message);
        }

        [Test]
        public void DynamicInGi_ContributeGiUnderRigidbody_IsReported_StaticDecorIsNot()
        {
            GameObject crate = AddCube("Crate", StaticEditorFlags.ContributeGI | StaticEditorFlags.BatchingStatic);
            crate.AddComponent<Rigidbody>();
            AddCube("Wall", StaticEditorFlags.ContributeGI);

            List<AuditFinding> findings = Check(new DynamicObjectInGiRule());

            Assert.That(findings.SelectMany(f => f.Items), Is.EqualTo(new[] { "Crate" }));
            StringAssert.Contains("Rigidbody", findings[0].Message);
        }

        [Test]
        public void LightmappedWithoutUv2_OnlyContributeGiLightmapReceiversWithoutUv2()
        {
            Mesh noUv2 = MeshAsset("NoUv2", uv2: false);
            AddCube("NoUv2", StaticEditorFlags.ContributeGI).GetComponent<MeshFilter>().sharedMesh = noUv2;
            AddCube("WithUv2", StaticEditorFlags.ContributeGI).GetComponent<MeshFilter>().sharedMesh = MeshAsset("WithUv2", uv2: true);
            GameObject probes = AddCube("OnProbes", StaticEditorFlags.ContributeGI);
            probes.GetComponent<MeshFilter>().sharedMesh = noUv2;
            probes.GetComponent<MeshRenderer>().receiveGI = ReceiveGI.LightProbes;

            List<AuditFinding> findings = Check(new LightmappedMeshWithoutUv2Rule());

            Assert.That(findings.SelectMany(f => f.Items), Is.EqualTo(new[] { ScenePath + ":NoUv2" }));
        }

        [Test]
        public void ZoneWithoutProbes_BakedSceneWithoutProbes_ReportsGameplayPoints()
        {
            SceneManager.SetActiveScene(_scene);
            var settings = new LightingSettings { bakedGI = true, realtimeGI = false };
            AssetDatabase.CreateAsset(settings, Folder + "/Baked.lighting");
            Lightmapping.lightingSettings = settings;
            var spawn = new GameObject("Spawn");
            SceneManager.MoveGameObjectToScene(spawn, _scene);
            spawn.AddComponent<TeamSpawnZone>();

            List<AuditFinding> findings = Check(new GameplayZoneWithoutProbesRule());

            Assert.That(findings, Has.Count.EqualTo(1));
            StringAssert.Contains("нет light probes", findings[0].Message);
        }

        [Test]
        public void RealtimeLightBakeFixer_ChangesOnlyModeAndSavesScene()
        {
            Light light = AddLight("Lamp", LightmapBakeType.Mixed);
            light.intensity = 3.5f;
            AuditFixer fixer = AuditFixerCatalog.ForRule("lighting.realtime-light");

            List<AuditFixAction> plan = fixer.Plan(Check(new RealtimeLightInBakedSceneRule()));
            Assert.That(plan, Has.Count.EqualTo(1));
            fixer.Apply(plan);

            Assert.IsTrue(plan[0].Applied, plan[0].Error);
            Assert.AreEqual(LightmapBakeType.Baked, light.lightmapBakeType);
            Assert.AreEqual(3.5f, light.intensity);
            Assert.IsFalse(_scene.isDirty, "сцена сохранена");
        }

        [Test]
        public void DynamicObjectGiFixer_RemovesOnlyContributeGi()
        {
            GameObject crate = AddCube("Crate", StaticEditorFlags.ContributeGI | StaticEditorFlags.OccludeeStatic);
            crate.AddComponent<Animator>();
            AuditFixer fixer = AuditFixerCatalog.ForRule("lighting.dynamic-in-gi");

            List<AuditFixAction> plan = fixer.Plan(Check(new DynamicObjectInGiRule()));
            fixer.Apply(plan);

            Assert.IsTrue(plan.Single().Applied, plan.Single().Error);
            Assert.AreEqual(StaticEditorFlags.OccludeeStatic, GameObjectUtility.GetStaticEditorFlags(crate));
        }

        [Test]
        public void LightmapUv2Fixer_SharedModelMesh_GetsSceneCopyAndImportIsUnchanged()
        {
            Mesh shared = AssetDatabase.LoadAllAssetsAtPath(SharedModel).OfType<Mesh>().FirstOrDefault(m => !m.HasVertexAttribute(VertexAttribute.TexCoord1));
            Assume.That(shared, Is.Not.Null, "в общем FBX нет меша без UV2");
            var importer = (ModelImporter)AssetImporter.GetAtPath(SharedModel);
            bool importBefore = importer.generateSecondaryUV;
            GameObject floor = AddCube("Floor", StaticEditorFlags.ContributeGI);
            floor.GetComponent<MeshFilter>().sharedMesh = shared;
            var collider = floor.AddComponent<MeshCollider>();
            collider.sharedMesh = shared;
            AuditFixer fixer = AuditFixerCatalog.ForRule("mesh.lightmapped-without-uv2");

            List<AuditFixAction> plan = fixer.Plan(Check(new LightmappedMeshWithoutUv2Rule()));
            fixer.Apply(plan);

            Mesh copy = floor.GetComponent<MeshFilter>().sharedMesh;
            Assert.IsTrue(plan.Single().Applied, plan.Single().Error);
            Assert.AreEqual(Folder + "/BakedLightingTest/" + LightmapUv2Fixer.MeshFolderName + "/nalchik_tolst_" + shared.name + "_UV2.asset",
                AssetDatabase.GetAssetPath(copy));
            Assert.IsTrue(copy.HasVertexAttribute(VertexAttribute.TexCoord1));
            Assert.AreSame(shared, collider.sharedMesh, "коллайдер остаётся на исходном меше");
            Assert.AreEqual(importBefore, ((ModelImporter)AssetImporter.GetAtPath(SharedModel)).generateSecondaryUV, "импорт общего FBX не меняется");
        }

        [Test]
        public void LightmapUv2Fixer_OwnMeshAsset_IsUnwrappedInPlace()
        {
            Mesh own = MeshAsset("Generated", uv2: false);
            string path = AssetDatabase.GetAssetPath(own);
            AddCube("Generated", StaticEditorFlags.ContributeGI).GetComponent<MeshFilter>().sharedMesh = own;
            AuditFixer fixer = AuditFixerCatalog.ForRule("mesh.lightmapped-without-uv2");

            List<AuditFixAction> plan = fixer.Plan(Check(new LightmappedMeshWithoutUv2Rule()));
            fixer.Apply(plan);

            Assert.IsTrue(plan.Single().Applied, plan.Single().Error);
            Assert.IsTrue(AssetDatabase.LoadAssetAtPath<Mesh>(path).HasVertexAttribute(VertexAttribute.TexCoord1));
        }

        /// <summary>Объекты сцены сохраняются до проверки: <see cref="GlobalObjectId"/> несохранённого объекта пуст.</summary>
        private List<AuditFinding> Check(ISceneAuditCheck rule)
        {
            Assert.IsTrue(EditorSceneManager.SaveScene(_scene), "сцена не сохранилась");
            var context = new AuditContext(AuditRequest.Scene(ScenePath)) { ScenePath = ScenePath };
            rule.CheckScene(_scene, ScenePath, context);
            return context.RawFindings.ToList();
        }

        private Light AddLight(string name, LightmapBakeType mode)
        {
            var go = new GameObject(name);
            SceneManager.MoveGameObjectToScene(go, _scene);
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.lightmapBakeType = mode;
            return light;
        }

        private GameObject AddCube(string name, StaticEditorFlags flags)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            SceneManager.MoveGameObjectToScene(go, _scene);
            GameObjectUtility.SetStaticEditorFlags(go, flags);
            go.GetComponent<MeshRenderer>().receiveGI = ReceiveGI.Lightmaps;
            return go;
        }

        private static Mesh MeshAsset(string name, bool uv2)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Mesh mesh = Object.Instantiate(cube.GetComponent<MeshFilter>().sharedMesh);
            Object.DestroyImmediate(cube);
            mesh.name = name;
            mesh.uv2 = uv2 ? mesh.uv : new Vector2[0];
            Assert.AreEqual(uv2, mesh.HasVertexAttribute(VertexAttribute.TexCoord1), name);
            AssetDatabase.CreateAsset(mesh, Folder + "/" + name + ".asset");
            return mesh;
        }
    }
}
