using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UltimateXR.Rendering.FX;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Editor.Performance.AssetAudit;
using VrBattlegrounds.Editor.Performance.AssetAudit.Rules;

namespace VrBattlegrounds.Tests.AssetAudit
{
    /// <summary>
    /// Принятое поведение аудитора ассетов (приёмка пользователем 2026-10-20, задача asset-audit):
    /// каталог правил, разбор Shader Graph пака MilitaryCamp, пороги ключевых правил, отчёт и «только чтение».
    /// </summary>
    public sealed class AssetAuditTests
    {
        private const string MilitaryCampShaders = "Assets/Art/Lobby/Imported/MilitaryCamp/Art/Shaders";

        private readonly List<Object> _created = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (Object created in _created)
            {
                if (created != null)
                    Object.DestroyImmediate(created);
            }
            _created.Clear();
        }

        [Test]
        public void Catalog_FindsEveryRuleWithUniqueIdAndQuestRationale()
        {
            var errors = new List<string>();
            List<AuditRule> rules = AuditRuleCatalog.CreateRules(AuditRequest.Folder("Assets"), errors);

            Assert.That(errors, Is.Empty);
            Assert.That(rules.Select(r => r.Id), Is.Unique);
            string[] expected =
            {
                "shader.non-mobile", "shadergraph.heavy-sampling", "material.hdrp-legacy", "material.texture-count",
                "texture.no-mipmaps", "texture.android-format", "texture.size", "lod.far-not-reduced", "mesh.no-lod",
                "lighting.large-without-lightmap", "reflection.planar", "foliage.alpha-test",
                "lighting.realtime-light", "lighting.scene-not-baked", "lighting.dynamic-in-gi",
                "mesh.lightmapped-without-uv2", "lighting.zone-without-probes",
            };
            Assert.That(rules.Select(r => r.Id), Is.SupersetOf(expected));
            // Процедурное небо стоит −0,05 мс (A/B 2026-10-20): правило удалено решением пользователя 2026-10-24.
            Assert.That(rules.Select(r => r.Id), Has.No.Member("scene.procedural-skybox"));
            foreach (AuditRule rule in rules)
            {
                Assert.That(rule.WhyBadOnQuest, Is.Not.Empty, rule.Id);
                Assert.That(rule.Evidence, Is.Not.Empty, rule.Id);
            }
        }

        [Test]
        public void Catalog_AppliesThresholdOverridesAndReportsUnknownOnes()
        {
            AuditRequest request = AuditRequest.Folder("Assets");
            request.ThresholdOverrides["lod.far-not-reduced.CriticalLastLodTriangles"] = "123";
            request.ThresholdOverrides["lod.far-not-reduced.NoSuchField"] = "1";
            request.DisabledRules.Add("texture.size");
            var errors = new List<string>();

            List<AuditRule> rules = AuditRuleCatalog.CreateRules(request, errors);

            var lod = (LodFarNotReducedRule)rules.Single(r => r.Id == "lod.far-not-reduced");
            Assert.That(lod.CriticalLastLodTriangles, Is.EqualTo(123));
            Assert.That(rules.Any(r => r.Id == "texture.size"), Is.False);
            Assert.That(errors.Count(e => e.Contains("NoSuchField")), Is.EqualTo(1));
        }

        [Test]
        public void ShaderGraph_MilitaryCampWorldAlignedFloorShader_IsTriplanarWith24Samples()
        {
            ShaderGraphInfo floor = new ShaderGraphAnalyzer().Analyze(MilitaryCampShaders + "/S_VPWorldAligned.shadergraph");

            Assert.That(floor, Is.Not.Null);
            Assert.That(floor.IsWorldProjected, Is.True);
            Assert.That(floor.SubGraphUses, Is.EqualTo(8));
            Assert.That(floor.TotalSamples, Is.EqualTo(24));
        }

        [Test]
        public void ShaderGraph_MilitaryCampBasicTextured_CountsSubGraphSamplesAndBranches()
        {
            ShaderGraphInfo basic = new ShaderGraphAnalyzer().Analyze(MilitaryCampShaders + "/S_BasicTextured.shadergraph");

            Assert.That(basic.IsWorldProjected, Is.True);
            Assert.That(basic.TotalSamples, Is.EqualTo(14));
            Assert.That(basic.BranchNodes, Is.EqualTo(8));
        }

        [Test]
        public void LodRule_FarLodAboveCriticalThreshold_IsCriticalWithChain()
        {
            LODGroup group = CreateLodGroup(20000, 6000);
            AuditContext context = Context(AuditScopeKind.Folder);

            new LodFarNotReducedRule().CheckGameObject(group.gameObject, Source(group.gameObject, false), context);

            AuditFinding finding = context.RawFindings.Single();
            Assert.That(finding.Severity, Is.EqualTo(AuditSeverity.Critical));
            Assert.That(finding.Message, Does.Contain("20k / 6000"));
        }

        [Test]
        public void LodRule_FarLodOfFewHundredTriangles_IsNotReported()
        {
            LODGroup group = CreateLodGroup(20000, 200);
            AuditContext context = Context(AuditScopeKind.Folder);

            new LodFarNotReducedRule().CheckGameObject(group.gameObject, Source(group.gameObject, false), context);

            Assert.That(context.RawFindings, Is.Empty);
        }

        [Test]
        public void MeshWithoutLod_HeavyMeshOutsideLodGroup_IsReported()
        {
            MeshRenderer renderer = CreateRenderer("Heavy", 25000);
            AuditContext context = Context(AuditScopeKind.Folder);

            new MeshWithoutLodRule().CheckGameObject(renderer.gameObject, Source(renderer.gameObject, false), context);

            Assert.That(context.RawFindings.Single().Severity, Is.EqualTo(AuditSeverity.Critical));
        }

        [Test]
        public void ShaderNotMobile_UrpLitIsWarningAndUnlitPasses()
        {
            Material lit = Track(new Material(Shader.Find("Universal Render Pipeline/Lit")));
            Material unlit = Track(new Material(Shader.Find("Universal Render Pipeline/Unlit")));
            AuditContext context = Context(AuditScopeKind.Folder);
            var rule = new ShaderNotMobileRule();

            rule.CheckMaterial(lit, "lit", context);
            rule.CheckMaterial(unlit, "unlit", context);

            AuditFinding finding = context.RawFindings.Single();
            Assert.That(finding.Severity, Is.EqualTo(AuditSeverity.Warning));
            Assert.That(finding.Message, Does.Contain("Universal Render Pipeline/Lit"));
        }

        [Test]
        public void PlanarReflection_DefaultSettings_AreCritical()
        {
            var mirror = Track(new GameObject("Mirror"));
            mirror.SetActive(false); // без OnEnable: правилу нужны только сериализованные поля
            mirror.AddComponent<UxrPlanarReflectionUrp>();
            AuditContext context = Context(AuditScopeKind.Folder);

            new PlanarReflectionRule().CheckGameObject(mirror, Source(mirror, false), context);

            AuditFinding finding = context.RawFindings.Single();
            Assert.That(finding.Severity, Is.EqualTo(AuditSeverity.Critical)); // _textureSize = 1024 по умолчанию SDK
            Assert.That(finding.Message, Does.Contain("дальность без ограничения"));
            Assert.That(finding.Message, Does.Contain("все слои"));
        }

        [Test]
        public void AlphaTest_SceneAggregate_ReportsRendererCountAboveThreshold()
        {
            Material clip = Track(new Material(Shader.Find("Universal Render Pipeline/Unlit")) { renderQueue = 2450 });
            var rule = new AlphaTestFoliageRule { WarningRenderers = 2, WarningTriangles = 1000000 };
            AuditContext context = Context(AuditScopeKind.Scene);
            for (int i = 0; i < 3; i++)
            {
                MeshRenderer renderer = CreateRenderer("Leaf" + i, 10);
                renderer.sharedMaterial = clip;
                rule.CheckGameObject(renderer.gameObject, Source(renderer.gameObject, true), context);
            }

            rule.Finish(context);

            AuditFinding finding = context.RawFindings.Single();
            Assert.That(finding.Metrics.Single(m => m.Name == "renderers").Value, Is.EqualTo(3));
        }

        [Test]
        public void Runner_SortsBySeverityAndWritesLfJson()
        {
            AuditRequest request = AuditRequest.Folder(MilitaryCampShaders);
            request.OutputPath = string.Empty;

            AuditReport report = AssetAuditRunner.Run(request);

            Assert.That(report.Errors, Is.Empty);
            Assert.That(report.Findings.Select(f => (int)f.Severity), Is.Ordered.Descending);
            Assert.That(report.Findings.Any(f => f.RuleId == "shadergraph.heavy-sampling" && f.Severity == AuditSeverity.Critical &&
                                                 f.Asset.EndsWith("S_VPWorldAligned.shadergraph")), Is.True);
            string json = report.ToJson();
            Assert.That(json, Does.Not.Contain("\r"));
            Assert.That(json, Does.StartWith("{\n"));
        }

        [Test]
        public void Runner_LeavesNoNewUnsavedAssets()
        {
            HashSet<Object> before = DirtyAssets();
            AuditRequest request = AuditRequest.Folder("Assets/Art/Lobby/Imported/MilitaryCamp");
            request.OutputPath = string.Empty;

            AssetAuditRunner.Run(request);

            Assert.That(DirtyAssets().Where(a => !before.Contains(a)).Select(a => AssetDatabase.GetAssetPath(a)), Is.Empty);
        }

        private static HashSet<Object> DirtyAssets()
        {
            var dirty = new HashSet<Object>();
            foreach (Object asset in Resources.FindObjectsOfTypeAll<Object>())
            {
                if (asset != null && EditorUtility.IsDirty(asset) && AssetDatabase.Contains(asset))
                    dirty.Add(asset);
            }
            return dirty;
        }

        private static AuditContext Context(AuditScopeKind kind)
        {
            AuditRequest request = kind == AuditScopeKind.Scene ? AuditRequest.Scene("Assets/Test.unity") : AuditRequest.Folder("Assets");
            return new AuditContext(request) { ScenePath = kind == AuditScopeKind.Scene ? "Assets/Test.unity" : null };
        }

        private static AuditObjectSource Source(GameObject gameObject, bool inScene) =>
            new AuditObjectSource("Assets/Test.prefab", AuditAssetUtil.HierarchyPath(gameObject.transform), inScene, "Assets/Test.prefab");

        private LODGroup CreateLodGroup(int nearTriangles, int farTriangles)
        {
            var root = Track(new GameObject("LodRoot"));
            MeshRenderer near = CreateRenderer("LOD0", nearTriangles);
            MeshRenderer far = CreateRenderer("LOD1", farTriangles);
            near.transform.SetParent(root.transform);
            far.transform.SetParent(root.transform);
            LODGroup group = root.AddComponent<LODGroup>();
            group.SetLODs(new[] { new LOD(0.5f, new Renderer[] { near }), new LOD(0.05f, new Renderer[] { far }) });
            return group;
        }

        private MeshRenderer CreateRenderer(string name, int triangles)
        {
            var mesh = Track(new Mesh { name = name });
            mesh.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            var indices = new int[triangles * 3];
            for (int i = 0; i < indices.Length; i++)
                indices[i] = i % 3;
            mesh.triangles = indices;
            var gameObject = Track(new GameObject(name));
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            return gameObject.AddComponent<MeshRenderer>();
        }

        private T Track<T>(T created) where T : Object
        {
            _created.Add(created);
            return created;
        }
    }
}
