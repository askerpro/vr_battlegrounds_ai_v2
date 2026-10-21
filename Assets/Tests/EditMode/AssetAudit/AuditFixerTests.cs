using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Editor.Performance.AssetAudit;
using VrBattlegrounds.Editor.Performance.AssetAudit.Fixers;

namespace VrBattlegrounds.Tests.AssetAudit
{
    /// <summary>
    /// Принятое поведение фиксеров аудитора (приёмка пользователем 2026-10-21, задача astc-fixer): каталог фиксеров
    /// и AndroidAstcFixer — dry-run без записи, override Android ASTC 6x6 с прежним max size, UI-текстуры и уже ASTC
    /// не трогаются, после применения правило texture.android-format молчит.
    /// </summary>
    public sealed class AuditFixerTests
    {
        private const string RuleId = "texture.android-format";
        private const string Folder = "Assets/Tests/EditMode/AssetAudit/TmpAstcFixer";
        private const string World = Folder + "/T_World_ORM.png";
        private const string Sprite = Folder + "/T_Sprite.png";
        private const string AlreadyAstc = Folder + "/T_AlreadyAstc.png";

        [SetUp]
        public void SetUp()
        {
            if (AssetDatabase.IsValidFolder(Folder))
                AssetDatabase.DeleteAsset(Folder);
            AssetDatabase.CreateFolder(Path.GetDirectoryName(Folder).Replace('\\', '/'), Path.GetFileName(Folder));

            TextureImporter world = CreatePng(World);
            world.textureCompression = TextureImporterCompression.Uncompressed;
            world.maxTextureSize = 512;
            world.sRGBTexture = false;
            world.SaveAndReimport();

            TextureImporter sprite = CreatePng(Sprite);
            sprite.textureType = TextureImporterType.Sprite;
            sprite.textureCompression = TextureImporterCompression.Uncompressed;
            sprite.SaveAndReimport();

            TextureImporter astc = CreatePng(AlreadyAstc);
            astc.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android", overridden = true, format = TextureImporterFormat.ASTC_6x6, maxTextureSize = 256,
            });
            astc.SaveAndReimport();
        }

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(Folder);
        }

        [Test]
        public void Catalog_FindsAndroidAstcFixerForExistingRule()
        {
            List<string> ruleIds = AuditRuleCatalog.CreateRules(null, new List<string>()).Select(r => r.Id).ToList();

            Assert.That(AuditFixerCatalog.ForRule(RuleId), Is.InstanceOf<AndroidAstcFixer>());
            Assert.That(AuditFixerCatalog.All.Select(f => f.RuleId), Is.Unique);
            foreach (AuditFixer fixer in AuditFixerCatalog.All)
                Assert.That(ruleIds, Does.Contain(fixer.RuleId), fixer.GetType().Name);
        }

        [Test]
        public void Plan_ListsOnlyUncompressedWorldTextureAndChangesNothing()
        {
            string metaBefore = File.ReadAllText(World + ".meta");

            List<AuditFixAction> plan = AuditFixerCatalog.PlanForFolders(RuleId, Folder);

            Assert.That(plan.Select(a => a.AssetPath), Is.EqualTo(new[] { World }));
            Assert.That(plan[0].Before, Does.Contain("(авто)").And.Contain("max 512"));
            Assert.That(plan[0].After, Is.EqualTo("ASTC_6x6 (override), max 512"));
            Assert.That(File.ReadAllText(World + ".meta"), Is.EqualTo(metaBefore));
            Assert.That(Importer(World).GetPlatformTextureSettings("Android").overridden, Is.False);
        }

        [Test]
        public void Apply_SetsAndroidAstcKeepsMaxSizeAndSilencesRule()
        {
            List<AuditFixAction> plan = AuditFixerCatalog.PlanForFolders(RuleId, Folder);

            AuditFixerCatalog.ForRule(RuleId).Apply(plan);

            Assert.That(plan.All(a => a.Applied && a.Error == null), Is.True, string.Join("\n", plan));
            TextureImporter world = Importer(World);
            TextureImporterPlatformSettings android = world.GetPlatformTextureSettings("Android");
            Assert.That(android.overridden, Is.True);
            Assert.That(android.format, Is.EqualTo(TextureImporterFormat.ASTC_6x6));
            Assert.That(android.maxTextureSize, Is.EqualTo(512));
            Assert.That(world.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed), "Default не меняется");
            Assert.That(world.GetPlatformTextureSettings("Standalone").overridden, Is.False);
            Assert.That(Importer(Sprite).GetPlatformTextureSettings("Android").overridden, Is.False);
            Assert.That(Importer(AlreadyAstc).GetPlatformTextureSettings("Android").maxTextureSize, Is.EqualTo(256));

            AuditRequest request = AuditRequest.Folder(Folder);
            request.OutputPath = string.Empty;
            AuditReport report = AssetAuditRunner.Run(request);
            Assert.That(report.Findings.Where(f => f.RuleId == RuleId && f.Asset == World), Is.Empty);
            Assert.That(AuditFixerCatalog.PlanForFolders(RuleId, Folder), Is.Empty);
        }

        private static TextureImporter CreatePng(string path)
        {
            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            try
            {
                var pixels = new Color32[64 * 64];
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = new Color32((byte)(i % 251), (byte)(i / 64 * 4), 128, 255);
                texture.SetPixels32(pixels);
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(texture);
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            return Importer(path);
        }

        private static TextureImporter Importer(string path) => (TextureImporter)AssetImporter.GetAtPath(path);
    }
}
