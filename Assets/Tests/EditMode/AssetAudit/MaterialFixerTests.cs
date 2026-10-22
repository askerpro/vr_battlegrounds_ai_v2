using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Editor.Performance.AssetAudit;
using VrBattlegrounds.Editor.Performance.AssetAudit.Fixers;
using VrBattlegrounds.Editor.Performance.AssetAudit.Rules;

namespace VrBattlegrounds.Tests.AssetAudit
{
    /// <summary>
    /// Принятое поведение (приёмка пользователем 2026-10-22, задача scene-detail-followups): правило material.emission-off
    /// (QPM-SHADER-006) и его EmissionOffFixer — дверная лампа Lobby снова светится; BakedLitMaterialFixer — единственный
    /// писатель перевода материалов окружения на Baked Lit (QPM-SHADER-007): статичный декор переводится, динамика пропускается,
    /// box-UV путь пересчитывает тайлинг, откат возвращает исходный шейдер; AuditFinding.FixTargets не режется лимитом примеров.
    /// Временные материалы лежат в корне окружения (фиксеры вне корней ничего не меняют) и удаляются в TearDown.
    /// </summary>
    public sealed class MaterialFixerTests
    {
        private const string Folder = "Assets/Materials/Lobby/TmpMaterialFixerTests";
        private const string OutsideFolder = "Assets/Tests/EditMode/AssetAudit/TmpMaterialFixerOutside";
        private const string EmissionMap = "Assets/Art/Lobby/Imported/MilitaryCamp/Art/Textures/T_Door_lamp_EM.png";
        private const string BaseMap = "Assets/Art/Lobby/Imported/MilitaryCamp/Art/Textures/T_Door_lamp_BC.png";
        private const string LitName = "Universal Render Pipeline/Lit";
        // HDRP/Lit: в URP-проекте шейдера нет (MI_TopLight пака RuralTown ссылается на него же).
        private const string MissingShaderGuid = "6e4ae4064600d784cac1e41a9e6f2e59";
        private static readonly Color Warm = new Color(1f, .74f, .46f, 1f);

        // Рендереры живут в активной (временной) сцене test runner и удаляются в TearDown.
        private readonly List<GameObject> _objects = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            foreach (string folder in new[] { Folder, OutsideFolder })
            {
                if (AssetDatabase.IsValidFolder(folder))
                    AssetDatabase.DeleteAsset(folder);
                AssetDatabase.CreateFolder(Path.GetDirectoryName(folder).Replace('\\', '/'), Path.GetFileName(folder));
            }
        }

        [TearDown]
        public void TearDown()
        {
            _objects.ForEach(Object.DestroyImmediate);
            _objects.Clear();
            AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.DeleteAsset(OutsideFolder);
        }

        [Test]
        public void EmissionOff_CatchesBlackGiFlagAndLostEmission_ButNotRealtimeOrUnlit()
        {
            Material blackFlag = Emissive(LitName, MaterialGlobalIlluminationFlags.EmissiveIsBlack);
            Material realtime = Emissive(LitName, MaterialGlobalIlluminationFlags.RealtimeEmissive);
            Material lost = Emissive(LitName, MaterialGlobalIlluminationFlags.EmissiveIsBlack);
            lost.shader = Shader.Find(BakedLitMaterialFixer.BakedLitName);
            Material unlit = Emissive(LitName, MaterialGlobalIlluminationFlags.None);
            unlit.shader = Shader.Find("Universal Render Pipeline/Unlit");
            AuditContext context = Context();
            var rule = new EmissionOffRule();

            rule.CheckMaterial(blackFlag, "blackFlag", context);
            rule.CheckMaterial(realtime, "realtime", context);
            rule.CheckMaterial(lost, "lost", context);
            rule.CheckMaterial(unlit, "unlit", context);

            Assert.That(context.RawFindings.SelectMany(f => f.FixTargets), Is.EquivalentTo(new[] { "blackFlag", "lost" }));
            Assert.That(context.RawFindings.Single(f => f.FixTargets.Contains("lost")).Message, Does.Contain("свечение потеряно"));
        }

        // Решение пользователя 2026-10-22 (lobby-material-fixes): M_Glass_Inst не светился в паке — EmissiveIsBlack с белым
        // цветом без карты (остаток конвертации HDRP→URP) не находка; подобранный цвет (WarmLampDiffuser) и флаг None ловятся.
        [Test]
        public void EmissionOff_IgnoresConversionWhiteWithoutMap()
        {
            Material glassLeftover = Emissive(LitName, MaterialGlobalIlluminationFlags.EmissiveIsBlack);
            glassLeftover.SetTexture("_EmissionMap", null);
            glassLeftover.SetColor("_EmissionColor", Color.white);
            Material warmNoMap = Emissive(LitName, MaterialGlobalIlluminationFlags.EmissiveIsBlack);
            warmNoMap.SetTexture("_EmissionMap", null);
            Material whiteNoFlag = Emissive(LitName, MaterialGlobalIlluminationFlags.None);
            whiteNoFlag.SetTexture("_EmissionMap", null);
            whiteNoFlag.SetColor("_EmissionColor", Color.white);
            AuditContext context = Context();
            var rule = new EmissionOffRule();

            rule.CheckMaterial(glassLeftover, "glassLeftover", context);
            rule.CheckMaterial(warmNoMap, "warmNoMap", context);
            rule.CheckMaterial(whiteNoFlag, "whiteNoFlag", context);

            Assert.That(context.RawFindings.SelectMany(f => f.FixTargets), Is.EquivalentTo(new[] { "warmNoMap", "whiteNoFlag" }));
        }

        // Приёмка пользователем 2026-10-22 (lobby-material-fixes): розовые HDRP Lit паков RuralTown/PostApocalypticTown
        // переведены в Baked Lit по сохранённым свойствам, хотя в Lobby не рисуются (назначены только в FBX).
        [Test]
        public void BakedLitFixer_ConvertsPinkHdrpLit_EvenWhenNotDrawn()
        {
            string hdrp = SaveMissingShaderMaterial(Folder + "/TmpHdrpLit.mat", withHdrpBaseMap: true);
            string unknown = SaveMissingShaderMaterial(Folder + "/TmpUnknownBroken.mat", withHdrpBaseMap: false);
            List<AuditFinding> findings = Findings(new ShaderNotMobileRule(), hdrp, unknown);
            Assert.That(findings.Select(f => f.Severity), Is.All.EqualTo(AuditSeverity.Critical));
            Assert.That(findings.SelectMany(f => f.FixTargets), Is.EquivalentTo(new[] { hdrp, unknown }));
            var fixer = new BakedLitMaterialFixer();

            List<AuditFixAction> plan = fixer.Plan(findings);
            Assert.That(plan.Select(a => a.AssetPath), Is.EqualTo(new[] { hdrp }), string.Join("\n", fixer.LastSkipped));
            Assert.That(fixer.LastSkipped.Single(s => s.StartsWith(unknown)), Does.Contain("исходник неизвестен"));
            fixer.Apply(plan);

            Assert.That(plan.Single().Applied, Is.True, plan.Single().ToString());
            var material = AssetDatabase.LoadAssetAtPath<Material>(hdrp);
            Assert.That(material.shader.name, Is.EqualTo(BakedLitMaterialFixer.BakedLitName));
            Assert.That(AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap")), Is.EqualTo(BaseMap));
            Assert.That(material.GetTextureScale("_BaseMap"), Is.EqualTo(new Vector2(2f, 2f)));
            Assert.That(material.GetColor("_BaseColor"), Is.EqualTo(new Color(.8f, .8f, .8f, 1f)));
            Assert.That(AssetImporter.GetAtPath(hdrp).userData, Does.Contain(MissingShaderGuid));
            Assert.That(BakedLitMaterialFixer.Revert(Folder), Is.EqualTo(0), "исходного шейдера HDRP нет — откат пропускается");
        }

        [Test]
        public void EmissionOffFixer_RestoresGlowOnBakedLitLamp_OnlyInsideEnvironmentRoots()
        {
            string lamp = SaveEmissiveBakedLit(Folder + "/TmpDoorLamp.mat");
            string outside = SaveEmissiveBakedLit(OutsideFolder + "/TmpOutsideLamp.mat");
            var fixer = new EmissionOffFixer();

            List<AuditFixAction> plan = fixer.Plan(Findings(new EmissionOffRule(), lamp, outside));
            Assert.That(plan.Select(a => a.AssetPath), Is.EqualTo(new[] { lamp }));
            fixer.Apply(plan);

            Assert.That(plan.Single().Applied, Is.True, plan.Single().ToString());
            var material = AssetDatabase.LoadAssetAtPath<Material>(lamp);
            Assert.That(material.shader.name, Is.EqualTo(EmissionOffFixer.SimpleLitName));
            Assert.That(material.globalIlluminationFlags, Is.EqualTo(MaterialGlobalIlluminationFlags.RealtimeEmissive));
            Assert.That(material.GetFloat("_SpecularHighlights"), Is.EqualTo(1f), "QPM-SHADER-005: 1 = NoSpecular");
            Assert.That(AssetDatabase.GetAssetPath(material.GetTexture("_EmissionMap")), Is.EqualTo(EmissionMap));
            Assert.That(AssetDatabase.GetAssetPath(material.GetTexture("_BaseMap")), Is.EqualTo(BaseMap));
            string text = File.ReadAllText(lamp);
            Assert.That(text, Does.Contain("- _EMISSION"), "состояние с диска: URP не выключил _EMISSION при импорте");
            Assert.That(text, Does.Contain("m_LightmapFlags: 1"));
            Assert.That(Findings(new EmissionOffRule(), lamp), Is.Empty, "после фикса правило молчит");
            Assert.That(AssetDatabase.LoadAssetAtPath<Material>(outside).shader.name, Is.EqualTo(BakedLitMaterialFixer.BakedLitName));
        }

        [Test]
        public void BakedLitFixer_ConvertsStaticDecor_SkipsDynamicAndOtherWriter()
        {
            string staticPath = SaveLit(Folder + "/TmpStaticDecor.mat");
            string dynamicPath = SaveLit(Folder + "/TmpDynamicProp.mat");
            string ownedPath = SaveLit(Folder + "/WornGround_Tmp.mat");
            AddRenderer(staticPath, isStatic: true);
            AddRenderer(dynamicPath, isStatic: false);
            AddRenderer(ownedPath, isStatic: true);
            var fixer = new BakedLitMaterialFixer();

            List<AuditFixAction> plan = fixer.Plan(Findings(new ShaderNotMobileRule(), staticPath, dynamicPath, ownedPath));

            Assert.That(plan.Select(a => a.AssetPath), Is.EqualTo(new[] { staticPath }), string.Join("\n", fixer.LastSkipped));
            Assert.That(fixer.LastSkipped.Single(s => s.StartsWith(dynamicPath)), Does.Contain("нестатичным"));
            Assert.That(fixer.LastSkipped.Single(s => s.StartsWith(ownedPath)), Does.Contain("другой писатель"));
            fixer.Apply(plan);

            Assert.That(plan.Single().Applied, Is.True, plan.Single().ToString());
            var converted = AssetDatabase.LoadAssetAtPath<Material>(staticPath);
            Assert.That(converted.shader.name, Is.EqualTo(BakedLitMaterialFixer.BakedLitName));
            Assert.That(AssetDatabase.GetAssetPath(converted.GetTexture("_BaseMap")), Is.EqualTo(BaseMap));
            Assert.That(AssetDatabase.LoadAssetAtPath<Material>(dynamicPath).shader.name, Is.EqualTo(LitName));
            Assert.That(AssetDatabase.LoadAssetAtPath<Material>(ownedPath).shader.name, Is.EqualTo(LitName));
        }

        [Test]
        public void BakedLitFixer_WorldAlignedBoxUv_TilesByMetresAndRevertRestoresShader()
        {
            Shader worldAligned = Shader.Find("Shader Graphs/S_VPWorldAligned");
            Assume.That(worldAligned, Is.Not.Null, "шейдер пака MilitaryCamp");
            string path = Folder + "/TmpWorldAligned.mat";
            var source = new Material(worldAligned);
            source.SetTexture("_MainBC", AssetDatabase.LoadAssetAtPath<Texture>(BaseMap));
            source.SetFloat("_MainTile", 2f);
            AssetDatabase.CreateAsset(source, path);
            var fixer = new BakedLitMaterialFixer();

            List<AuditFixAction> plan = fixer.PlanWorldAligned(new[] { new KeyValuePair<string, float>(path, 1f) });
            fixer.Apply(plan);

            Assert.That(plan.Single().Applied, Is.True, plan.Single().ToString());
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Assert.That(material.shader.name, Is.EqualTo(BakedLitMaterialFixer.BakedLitName));
            Assert.That(material.GetTextureScale("_BaseMap"), Is.EqualTo(new Vector2(.5f, .5f)), "метры на единицу UV / _MainTile");
            Assert.That(AssetImporter.GetAtPath(path).userData, Does.Contain(BakedLitMaterialFixer.RevertMarker));
            Assert.That(fixer.PlanWorldAligned(new[] { new KeyValuePair<string, float>(path, 1f) }), Is.Empty, "повтор ничего не меняет");

            Assert.That(BakedLitMaterialFixer.Revert(Folder), Is.EqualTo(1));
            Assert.That(AssetDatabase.LoadAssetAtPath<Material>(path).shader, Is.EqualTo(worldAligned));
        }

        [Test]
        public void FixTargets_KeepEveryAssetOfMergedGroup_ItemsStayCapped()
        {
            AuditContext context = Context();
            var rule = new ShaderNotMobileRule();
            var materials = new List<Material>();
            for (int i = 0; i < AuditFinding.MaxItems + 5; i++)
            {
                var material = new Material(Shader.Find(LitName)) { name = "Tmp" + i };
                materials.Add(material);
                rule.CheckMaterial(material, "Assets/Tmp" + i + ".mat", context);
            }

            AuditFinding merged = context.RawFindings[0];
            foreach (AuditFinding other in context.RawFindings.Skip(1))
                merged.MergeFrom(other);

            Assert.That(merged.FixTargets.Count, Is.EqualTo(AuditFinding.MaxItems + 5));
            Assert.That(merged.Items.Count, Is.EqualTo(AuditFinding.MaxItems));
            materials.ForEach(Object.DestroyImmediate);
        }

        private static Material Emissive(string shaderName, MaterialGlobalIlluminationFlags flags)
        {
            var material = new Material(Shader.Find(shaderName));
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture>(BaseMap));
            material.SetColor("_EmissionColor", Warm);
            material.SetTexture("_EmissionMap", AssetDatabase.LoadAssetAtPath<Texture>(EmissionMap));
            material.globalIlluminationFlags = flags;
            return material;
        }

        // Как дверная лампа до фикса: Baked Lit, в сохранённых свойствах остались цвет и карта эмиссии.
        private static string SaveEmissiveBakedLit(string path)
        {
            Material material = Emissive(LitName, MaterialGlobalIlluminationFlags.EmissiveIsBlack);
            material.shader = Shader.Find(BakedLitMaterialFixer.BakedLitName);
            AssetDatabase.CreateAsset(material, path);
            return path;
        }

        // Материал HDRP Lit, как в паках «HDRP+URP»: ссылка на шейдер HDRP, которого в проекте нет (InternalErrorShader).
        private static string SaveMissingShaderMaterial(string path, bool withHdrpBaseMap)
        {
            string texEnvs = withHdrpBaseMap
                ? "    - _BaseColorMap:\n        m_Texture: {fileID: 2800000, guid: " + AssetDatabase.AssetPathToGUID(BaseMap) + ", type: 3}\n" +
                  "        m_Scale: {x: 2, y: 2}\n        m_Offset: {x: 0, y: 0}\n"
                : "    - _SomeMap:\n        m_Texture: {fileID: 0}\n        m_Scale: {x: 1, y: 1}\n        m_Offset: {x: 0, y: 0}\n";
            string yaml =
                "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!21 &2100000\nMaterial:\n  serializedVersion: 8\n" +
                "  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n" +
                "  m_PrefabAsset: {fileID: 0}\n  m_Name: " + Path.GetFileNameWithoutExtension(path) + "\n" +
                "  m_Shader: {fileID: 4800000, guid: " + MissingShaderGuid + ", type: 3}\n" +
                "  m_Parent: {fileID: 0}\n  m_ModifiedSerializedProperties: 0\n  m_ValidKeywords: []\n  m_InvalidKeywords: []\n" +
                "  m_LightmapFlags: 4\n  m_EnableInstancingVariants: 0\n  m_DoubleSidedGI: 0\n  m_CustomRenderQueue: -1\n" +
                "  stringTagMap: {}\n  disabledShaderPasses: []\n  m_LockedProperties: \n  m_SavedProperties:\n" +
                "    serializedVersion: 3\n    m_TexEnvs:\n" + texEnvs +
                "    m_Ints: []\n    m_Floats:\n    - _AlphaCutoffEnable: 0\n    - _SurfaceType: 0\n" +
                "    m_Colors:\n    - _BaseColor: {r: 0.8, g: 0.8, b: 0.8, a: 1}\n    - _EmissiveColor: {r: 0, g: 0, b: 0, a: 1}\n" +
                "  m_BuildTextureStacks: []\n  m_AllowLocking: 1\n";
            File.WriteAllText(path, yaml);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            Assume.That(AssetDatabase.LoadAssetAtPath<Material>(path).shader.name, Is.EqualTo(BakedLitMaterialFixer.ErrorShaderName));
            return path;
        }

        private static string SaveLit(string path)
        {
            var material = new Material(Shader.Find(LitName));
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture>(BaseMap));
            AssetDatabase.CreateAsset(material, path);
            return path;
        }

        private void AddRenderer(string materialPath, bool isStatic)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _objects.Add(go);
            go.isStatic = isStatic;
            go.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        }

        private static List<AuditFinding> Findings(IMaterialAuditCheck rule, params string[] paths)
        {
            AuditContext context = Context();
            foreach (string path in paths)
                rule.CheckMaterial(AssetDatabase.LoadAssetAtPath<Material>(path), path, context);
            return context.RawFindings.ToList();
        }

        private static AuditContext Context() => new AuditContext(AuditRequest.Folder("Assets"));
    }
}
