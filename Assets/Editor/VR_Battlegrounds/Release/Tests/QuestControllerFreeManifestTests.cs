using System.Collections.Generic;
using System.Xml;
using NUnit.Framework;

namespace VrBattlegrounds.EditorTools.Release.Tests
{
    /// <summary>
    /// Записи рук в манифесте Quest для запуска без пробуждения контроллеров (<see cref="QuestControllerFreeManifest" />)
    /// и их проверка (<see cref="QuestManifestValidator" />): в Test — необязательные руки и разрешение, в Prod — ничего.
    /// Тесты лежат рядом с кодом: <c>Release/</c> — в <c>Assembly-CSharp-Editor</c>, на который asmdef тестов не ссылается.
    /// </summary>
    public class QuestControllerFreeManifestTests
    {
        private const string Feature = QuestControllerFreeManifest.HandTrackingFeature;
        private const string Permission = QuestControllerFreeManifest.HandTrackingPermission;

        private static XmlDocument Manifest(string extra = "")
        {
            var document = new XmlDocument();
            document.LoadXml(
                "<manifest xmlns:android=\"http://schemas.android.com/apk/res/android\" package=\"com.test\">" +
                extra +
                "<application><activity android:name=\"A\"><intent-filter>" +
                "<category android:name=\"" + QuestManifestValidator.VrCategory + "\"/>" +
                "</intent-filter></activity>" +
                "<meta-data android:name=\"" + QuestManifestValidator.SupportedDevicesKey + "\" android:value=\"quest2\"/>" +
                "</application></manifest>");
            return document;
        }

        private static string FeatureXml(string required) =>
            "<uses-feature android:name=\"" + Feature + "\"" + (required == null ? "" : " android:required=\"" + required + "\"") + "/>";

        private static string PermissionXml() => "<uses-permission android:name=\"" + Permission + "\"/>";

        private static int Count(XmlDocument document, string element, string name)
        {
            int count = 0;
            foreach (XmlElement e in document.SelectNodes("/manifest/" + element))
                if (e.GetAttribute("name", "http://schemas.android.com/apk/res/android") == name) count++;
            return count;
        }

        // ── Patch ───────────────────────────────────────────────────────────

        [Test]
        public void Patch_AddsOptionalHandsAndPermission_BeforeApplication()
        {
            XmlDocument document = Manifest();

            Assert.IsNull(QuestControllerFreeManifest.Patch(document, out bool changed));
            Assert.IsTrue(changed);
            Assert.AreEqual(false, QuestControllerFreeManifest.ReadHandTrackingRequired(document));
            Assert.IsTrue(QuestControllerFreeManifest.HasHandTrackingPermission(document));

            XmlNodeList children = document.DocumentElement.ChildNodes;
            Assert.AreEqual(3, children.Count);
            Assert.AreEqual("uses-feature", children[0].Name, "записи рук — до <application>");
            Assert.AreEqual("uses-permission", children[1].Name);
            Assert.AreEqual("application", children[2].Name);
        }

        [Test]
        public void Patch_Twice_NoDuplicatesAndNoChange()
        {
            XmlDocument document = Manifest();
            QuestControllerFreeManifest.Patch(document, out _);

            Assert.IsNull(QuestControllerFreeManifest.Patch(document, out bool changedAgain));
            Assert.IsFalse(changedAgain, "повторный прогон ничего не меняет");
            Assert.AreEqual(1, Count(document, "uses-feature", Feature));
            Assert.AreEqual(1, Count(document, "uses-permission", Permission));
        }

        [Test]
        public void Patch_PartialSet_AddsOnlyMissing()
        {
            XmlDocument document = Manifest(FeatureXml("false"));

            Assert.IsNull(QuestControllerFreeManifest.Patch(document, out bool changed));
            Assert.IsTrue(changed);
            Assert.AreEqual(1, Count(document, "uses-feature", Feature));
            Assert.AreEqual(1, Count(document, "uses-permission", Permission));
        }

        [TestCase("true")]
        [TestCase(null)]
        public void Patch_RequiredHands_RefusesWithoutChanges(string required)
        {
            XmlDocument document = Manifest(FeatureXml(required));
            string before = document.OuterXml;

            Assert.IsNotNull(QuestControllerFreeManifest.Patch(document, out bool changed),
                "обязательные руки (в том числе без атрибута) — продуктовое решение, не исправляются");
            Assert.IsFalse(changed);
            Assert.AreEqual(before, document.OuterXml);
        }

        [Test]
        public void Patch_NoManifestRoot_Refuses()
        {
            var document = new XmlDocument();
            document.LoadXml("<other/>");
            Assert.IsNotNull(QuestControllerFreeManifest.Patch(document, out bool changed));
            Assert.IsFalse(changed);
        }

        // ── Правила валидатора ──────────────────────────────────────────────

        private static string Evaluate(BuildConfig config, XmlDocument document) =>
            QuestManifestValidator.Evaluate(BuildProfile.Quest, config, QuestManifestValidator.ReadXml(document), new List<string>());

        [Test]
        public void Test_PatchedManifest_Passes()
        {
            XmlDocument document = Manifest();
            QuestControllerFreeManifest.Patch(document, out _);
            Assert.IsNull(Evaluate(BuildConfig.Test, document));
        }

        [Test]
        public void Test_WithoutHands_Fails()
        {
            Assert.IsNotNull(Evaluate(BuildConfig.Test, Manifest()));
            Assert.IsNotNull(Evaluate(BuildConfig.Test, Manifest(FeatureXml("false"))), "без разрешения — не сработал");
            Assert.IsNotNull(Evaluate(BuildConfig.Test, Manifest(PermissionXml())), "без uses-feature — не сработал");
        }

        [Test]
        public void Test_RequiredHands_Fails()
        {
            Assert.IsNotNull(Evaluate(BuildConfig.Test, Manifest(FeatureXml("true") + PermissionXml())));
        }

        [Test]
        public void Prod_WithoutHands_Passes()
        {
            Assert.IsNull(Evaluate(BuildConfig.Prod, Manifest()));
        }

        [Test]
        public void Prod_AnyHandEntry_Fails()
        {
            Assert.IsNotNull(Evaluate(BuildConfig.Prod, Manifest(FeatureXml("false"))));
            Assert.IsNotNull(Evaluate(BuildConfig.Prod, Manifest(PermissionXml())));
        }

        // ── aapt dump xmltree ───────────────────────────────────────────────

        private static string Aapt(string features) =>
            "N: android=http://schemas.android.com/apk/res/android\n" +
            "  E: manifest (line=2)\n" +
            features +
            "    E: application (line=20)\n";

        private const string AaptFeatureFalse =
            "    E: uses-feature (line=10)\n" +
            "      A: android:name(0x01010003)=\"" + Feature + "\" (Raw: \"" + Feature + "\")\n" +
            "      A: android:required(0x0101028e)=(type 0x12)0x0\n";

        private const string AaptFeatureTrue =
            "    E: uses-feature (line=10)\n" +
            "      A: android:name(0x01010003)=\"" + Feature + "\" (Raw: \"" + Feature + "\")\n" +
            "      A: android:required(0x0101028e)=(type 0x12)0xffffffff\n";

        private const string AaptFeatureNoRequired =
            "    E: uses-feature (line=10)\n" +
            "      A: android:name(0x01010003)=\"" + Feature + "\" (Raw: \"" + Feature + "\")\n";

        private const string AaptPermission =
            "    E: uses-permission (line=11)\n" +
            "      A: android:name(0x01010003)=\"" + Permission + "\" (Raw: \"" + Permission + "\")\n";

        [Test]
        public void Aapt_OptionalHandsAndPermission()
        {
            QuestManifestValidator.ManifestFacts facts = QuestManifestValidator.ReadAaptXmlTree(Aapt(AaptFeatureFalse + AaptPermission));
            Assert.AreEqual(false, facts.HandTrackingRequired);
            Assert.IsTrue(facts.HasHandTrackingPermission);
            Assert.IsNull(QuestManifestValidator.EvaluateHandTracking(BuildConfig.Test, facts));
            Assert.IsNotNull(QuestManifestValidator.EvaluateHandTracking(BuildConfig.Prod, facts));
        }

        [Test]
        public void Aapt_RequiredTrueOrMissing_IsRequired()
        {
            Assert.AreEqual(true, QuestManifestValidator.ReadAaptXmlTree(Aapt(AaptFeatureTrue)).HandTrackingRequired);
            Assert.AreEqual(true, QuestManifestValidator.ReadAaptXmlTree(Aapt(AaptFeatureNoRequired)).HandTrackingRequired,
                "без android:required Android считает true");
        }

        [Test]
        public void Aapt_NoHands()
        {
            QuestManifestValidator.ManifestFacts facts = QuestManifestValidator.ReadAaptXmlTree(Aapt(""));
            Assert.IsNull(facts.HandTrackingRequired);
            Assert.IsFalse(facts.HasHandTrackingPermission);
            Assert.IsNull(QuestManifestValidator.EvaluateHandTracking(BuildConfig.Prod, facts));
            Assert.IsNotNull(QuestManifestValidator.EvaluateHandTracking(BuildConfig.Test, facts));
        }
    }
}
