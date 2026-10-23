using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UltimateXR.Avatar;
using UltimateXR.Manipulation;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Editor.Avatars;

namespace VrBattlegrounds.Editor.Gameplay.Tests
{
    /// <summary>
    /// Логика команды «Зарегистрировать позы для выбранного аватара» (<see cref="AvatarGripRegistration" />) без записи:
    /// выбор цели по цепочке префабов, имена поз, пропуск точки с готовым хватом, сверка YAML документа
    /// <c>UxrGrabbableObject</c> (<see cref="AvatarGripYaml" />). Чистые тесты сверки YAML идут на строках документа
    /// <c>MKR9_mag.prefab</c> из dev 47135fae0 и ожидаемой записи по образцу AK105_mag (d7d73887e); тесты на ассетах dev
    /// только читают.
    /// </summary>
    public class AvatarGripRegistrationTests
    {
        private const string BaseGuid = GripTargetProfile.CyborgBaseGuid;
        private const string Ak105Magazine = "Assets/Prefabs/Weapons/AK105/AK105_mag.prefab";
        private const string Ak105MagazineOrig = "Assets/Art/HandPoses/Orig/Kinemation/Kinemation_AK105_Magazine_orig.asset";

        // ── Чистая логика ───────────────────────────────────────────────────

        [Test]
        public void Потомок_базы_пишет_в_базу()
        {
            Assert.That(AvatarGripRegistration.RecordGuid(new[] { "child", BaseGuid, "playerbase" }, BaseGuid), Is.EqualTo(BaseGuid));
            Assert.That(AvatarGripRegistration.RecordGuid(new[] { "grandchild", "child", BaseGuid }, BaseGuid), Is.EqualTo(BaseGuid));
        }

        [Test]
        public void База_и_чужой_аватар_пишут_в_себя()
        {
            Assert.That(AvatarGripRegistration.RecordGuid(new[] { BaseGuid, "playerbase" }, BaseGuid), Is.EqualTo(BaseGuid));
            Assert.That(AvatarGripRegistration.RecordGuid(new[] { "mef", "nonsdk", "playerbase" }, BaseGuid), Is.EqualTo("mef"));
            Assert.Throws<System.ArgumentException>(() => AvatarGripRegistration.RecordGuid(new string[0], BaseGuid));
        }

        [TestCase("Kinemation_AK105_Grip_orig", "Kinemation", "AK105", "Grip", "Kinemation_AK105_Grip")]
        [TestCase("HandsPack_MP5K_Support_orig", "HandsPack", "MP5K", "Support", "HandsPack_MP5K_Support")]
        [TestCase("Kinemation_AK105_Magazine_orig", "Kinemation", "AK105", "Magazine", "Kinemation_AK105_Mag_Grip")]
        [TestCase("HandsPack_FabarmSDASS_Shell_orig", "HandsPack", "FabarmSDASS", "Shell", "HandsPack_FabarmSDASS_Shell_Grip")]
        [TestCase("Kinemation_Herrington_Shell_orig", "Kinemation", "Herrington", "Shell", "Kinemation_Herrington_Shell_Grip")]
        [TestCase("HandsPack_Two_Words_Magazine_orig", "HandsPack", "Two_Words", "Magazine", "HandsPack_Two_Words_Mag_Grip")]
        public void Имя_позы_из_имени_orig(string orig, string pack, string item, string point, string pose)
        {
            Assert.IsTrue(AvatarGripRegistration.TryParseOrigName(orig, out string p, out string i, out string t));
            Assert.That((p, i, t), Is.EqualTo((pack, item, point)));
            Assert.That(AvatarGripRegistration.GripPoseName(p, i, t), Is.EqualTo(pose));
        }

        [TestCase("Kinemation_AK105_Magazine")]
        [TestCase("AK105_orig")]
        [TestCase("Kinemation_Magazine_orig")]
        [TestCase("_AK105_Magazine_orig")]
        [TestCase("")]
        public void Неверное_имя_orig_не_разбирается(string orig)
        {
            Assert.IsFalse(AvatarGripRegistration.TryParseOrigName(orig, out _, out _, out _));
        }

        [Test]
        public void Папка_поз_самая_частая()
        {
            Assert.That(AvatarGripRegistration.MostCommonFolder(new[] { "A/x.asset", "B/y.asset", "B/z.asset", null }), Is.EqualTo("B"));
            Assert.That(AvatarGripRegistration.MostCommonFolder(new[] { "B/x.asset", "A/y.asset" }), Is.EqualTo("A"));
            Assert.IsNull(AvatarGripRegistration.MostCommonFolder(new string[0]));
        }

        [Test]
        public void Пустой_дескриптор_вырожден()
        {
            Assert.IsNotNull(AvatarGripRegistration.DescriptorDefect(new UxrHandDescriptor()));
            Assert.IsNotNull(AvatarGripRegistration.DescriptorDefect(null));
        }

        // ── Сверка YAML документа UxrGrabbableObject ────────────────────────

        [Test]
        public void Запись_дописана_в_непустой_список()
        {
            Assert.IsNull(YamlCases.Problem(YamlCases.Was(), YamlCases.Appended(), 0));
        }

        [Test]
        public void Запись_в_пустой_список()
        {
            Assert.IsNull(YamlCases.Problem(YamlCases.WasEmptyList(), YamlCases.AppendedToEmptyList(), 0));
        }

        [Test]
        public void Запись_во_вторую_точку_хвата()
        {
            Assert.IsNull(YamlCases.Problem(YamlCases.WasWithSupport(), YamlCases.AppendedToSupport(), 1));
            Assert.IsNotNull(YamlCases.Problem(YamlCases.WasWithSupport(), YamlCases.AppendedToSupport(), 0), "Блок не в той точке хвата.");
        }

        [TestCase("  __isInPrefab: 1", "  __isInPrefab: 0", "__isInPrefab")]
        [TestCase("  _uxrUniqueId: c3c052d7-850b-48f4-8f54-79a22b08e8ee", "  _uxrUniqueId: 00000000-850b-48f4-8f54-79a22b08e8ee", "_uxrUniqueId")]
        [TestCase("  _selectedAvatarForGrips: {fileID: 2537556764526307725, guid: 71b74b0d08444f49bbb7aa2828d0890a, type: 3}",
                  "  _selectedAvatarForGrips: {fileID: 0}", "_selectedAvatarForGrips")]
        [TestCase("      _poseBlendValue: 0.68", "      _poseBlendValue: 0.5", "_poseBlendValue: 0.68")]
        [TestCase("  m_EditorClassIdentifier: ", "  m_EditorClassIdentifier: UltimateXR::UltimateXR.Manipulation.UxrGrabbableObject", "m_EditorClassIdentifier")]
        public void Лишнее_изменение_вне_записи_ошибка(string from, string to, string expected)
        {
            string[] now = YamlCases.Appended();
            int index = System.Array.IndexOf(now, from);
            Assert.That(index, Is.GreaterThanOrEqualTo(0), "Нет строки " + from);
            now[index] = to;
            string problem = YamlCases.Problem(YamlCases.Was(), now, 0);
            Assert.IsNotNull(problem);
            StringAssert.Contains(expected, problem);
        }

        [Test]
        public void Неверный_блок_записи_ошибка()
        {
            string[] wrongPose = YamlCases.Appended().Select(l => l.Replace(YamlCases.PoseGuid, "ffffffffffffffffffffffffffffffff")).ToArray();
            StringAssert.Contains("_handPose", YamlCases.Problem(YamlCases.Was(), wrongPose, 0));
            string[] wrongAlign = YamlCases.Appended().Select(l => l.Replace("{fileID: " + YamlCases.RightId + "}", "{fileID: 1}")).ToArray();
            StringAssert.Contains("_gripAlignTransformHandRight", YamlCases.Problem(YamlCases.Was(), wrongAlign, 0));
            Assert.IsNotNull(YamlCases.Problem(YamlCases.Was(), YamlCases.Was(), 0), "Нет блока записи.");
            Assert.IsNotNull(YamlCases.Problem(YamlCases.Was(), YamlCases.InsertedFirst(), 0), "Блок не последним элементом.");
        }

        // ── Замена временных записей, пересоздание, шаблон ──────────────────

        [TestCase("Assets/ThirdParty/UltimateXR/Runtime/Art/Avatars/Cyborg/HandPoses/GrabClaw.asset", true)]
        [TestCase("Assets/ThirdParty/UltimateXR/Runtime/Art/Avatars/Cyborg/HandPoses/Grab.asset", true)]
        [TestCase("Assets\\ThirdParty\\UltimateXR\\Examples\\DemoGrab.asset", true)]
        [TestCase("Assets/Prefabs/Player/HandPoses/CyborgHands/Kinemation_AK105_Mag_Grip.asset", false)]
        [TestCase("Assets/Art/HandPoses/Orig/Kinemation/Kinemation_AK105_Magazine_orig.asset", false)]
        [TestCase("Assets/ThirdParty/OtherPack/Pose.asset", false)]
        [TestCase(null, false)]
        public void Поза_SDK_по_пути(string path, bool sdk)
        {
            Assert.That(AvatarGripRegistration.IsSdkPose(path), Is.EqualTo(sdk));
        }

        private const string Sdk = "Assets/ThirdParty/UltimateXR/Runtime/Art/Avatars/Cyborg/HandPoses/GrabClaw.asset";
        private const string Manual = "Assets/Prefabs/Player/HandPoses/CyborgHands/Kinemation_AK105_Mag_Grip.asset";

        private static AvatarGripRegistration.Options Options(bool replaceSdk, params string[] rebuild)
        {
            var options = new AvatarGripRegistration.Options { ReplaceSdkPoses = replaceSdk };
            options.RebuildPoints.UnionWith(rebuild);
            return options;
        }

        [Test]
        public void Решение_по_существующей_записи()
        {
            const string grip = AvatarGripRegistration.GripPoint, mag = AvatarGripRegistration.MagazinePoint;
            AvatarGripRegistration.EntryAction Decide(bool has, bool own, string pose, AvatarGripRegistration.Options o, string point) =>
                AvatarGripRegistration.Decide(has, own, pose, o, point);

            Assert.That(Decide(false, false, null, Options(true), mag), Is.EqualTo(AvatarGripRegistration.EntryAction.Create), "Записи нет — новая.");
            // Режим пересоздания выключен — ручная запись пропускается.
            Assert.That(Decide(true, true, Manual, Options(true), mag), Is.EqualTo(AvatarGripRegistration.EntryAction.Skip));
            // Включён для точки — пересоздаётся; для другой точки — пропуск.
            Assert.That(Decide(true, true, Manual, Options(true, mag), mag), Is.EqualTo(AvatarGripRegistration.EntryAction.Rebuild));
            Assert.That(Decide(true, true, Manual, Options(true, mag), grip), Is.EqualTo(AvatarGripRegistration.EntryAction.Skip));
            // Поза SDK — временная: замена по галочке или при пересоздании.
            Assert.That(Decide(true, true, Sdk, Options(true), mag), Is.EqualTo(AvatarGripRegistration.EntryAction.Replace));
            Assert.That(Decide(true, true, Sdk, Options(false), mag), Is.EqualTo(AvatarGripRegistration.EntryAction.Skip));
            Assert.That(Decide(true, true, Sdk, Options(false, mag), mag), Is.EqualTo(AvatarGripRegistration.EntryAction.Replace));
            // Пустая поза — задуманная запись: только пересоздание.
            Assert.That(Decide(true, true, null, Options(true), mag), Is.EqualTo(AvatarGripRegistration.EntryAction.Skip));
            Assert.That(Decide(true, true, null, Options(true, mag), mag), Is.EqualTo(AvatarGripRegistration.EntryAction.Replace));
            // Запись не цели (найдена по цепочке под другим GUID) — никогда не трогается.
            Assert.That(Decide(true, false, Sdk, Options(true, mag), mag), Is.EqualTo(AvatarGripRegistration.EntryAction.Skip));
        }

        [Test]
        public void Общая_поза_не_пересоздаётся()
        {
            const string guid = "0123456789abcdef0123456789abcdef";
            string entry = "    - _avatarPrefabGuid: 71b74b0d08444f49bbb7aa2828d0890a\n      _handPose: {fileID: 11400000, guid: " + guid + ", type: 2}\n";
            string overrideUse = "    - target: {fileID: 1, guid: ff, type: 3}\n      propertyPath: _grabPoint._avatarGripPoseEntries.Array.data[1]._handPose\n" +
                                 "      value: \n      objectReference: {fileID: 11400000, guid: " + guid + ", type: 2}\n";
            string registry = "  _handPoses:\n  - {fileID: 11400000, guid: " + guid + ", type: 2}\n";
            Assert.That(AvatarGripRegistration.CountPoseUses(entry + registry, guid), Is.EqualTo(1), "Регистрация в _handPoses — не назначение.");
            Assert.That(AvatarGripRegistration.CountPoseUses(overrideUse, guid), Is.EqualTo(1));
            Assert.IsFalse(AvatarGripRegistration.SharedPose(new[] { 1 }));
            Assert.IsTrue(AvatarGripRegistration.SharedPose(new[] { 2 }), "Две записи одного префаба.");
            Assert.IsTrue(AvatarGripRegistration.SharedPose(new[] { 1, 1 }), "Записи двух предметов.");
        }

        [Test]
        public void Замена_значений_записи_допустима_ровно()
        {
            string[] was = YamlCases.Appended();
            Assert.IsNull(AvatarGripYaml.ReplaceProblem(was, YamlCases.Replaced(was), YamlCases.RecordGuid, YamlCases.NewPoseGuid, 11, -22, 0));

            string[] extra = YamlCases.Replaced(was);
            extra[System.Array.IndexOf(extra, "  __isInPrefab: 1")] = "  __isInPrefab: 0";
            StringAssert.Contains("__isInPrefab", AvatarGripYaml.ReplaceProblem(was, extra, YamlCases.RecordGuid, YamlCases.NewPoseGuid, 11, -22, 0));

            string[] otherEntry = YamlCases.Replaced(was).Select(l => l == "      _poseBlendValue: 0.68" ? "      _poseBlendValue: 0.1" : l).ToArray();
            StringAssert.Contains("_poseBlendValue: 0.68", AvatarGripYaml.ReplaceProblem(was, otherEntry, YamlCases.RecordGuid, YamlCases.NewPoseGuid, 11, -22, 0));

            StringAssert.Contains("_handPose", AvatarGripYaml.ReplaceProblem(was, YamlCases.Replaced(was), YamlCases.RecordGuid, YamlCases.PoseGuid, 11, -22, 0));
            Assert.IsNotNull(AvatarGripYaml.ReplaceProblem(YamlCases.Was(), YamlCases.Appended(), YamlCases.RecordGuid, YamlCases.PoseGuid,
                                                           YamlCases.LeftId, YamlCases.RightId, 0), "Новая запись — не замена.");
        }

        [Test]
        public void Шаблон_по_габариту_меша()
        {
            var from = new Bounds(Vector3.zero, new Vector3(1f, 2f, 4f));
            var to = new Bounds(Vector3.one, new Vector3(2f, 2f, 2f));
            Assert.That(Vector3.Distance(AvatarGripRegistration.TemplatePoint(new Vector3(0.5f, 1f, 2f), from, to), new Vector3(2f, 2f, 2f)), Is.LessThan(1e-5f));
            Assert.That(Vector3.Distance(AvatarGripRegistration.TemplatePoint(Vector3.zero, from, to), Vector3.one), Is.LessThan(1e-5f), "Центр → центр.");
            var flat = new Bounds(Vector3.zero, new Vector3(0f, 2f, 2f));
            Assert.That(AvatarGripRegistration.TemplatePoint(new Vector3(0.3f, 0f, 0f), flat, to).x, Is.EqualTo(1.3f).Within(1e-5f), "Нулевая ось — отношение 1.");
        }

        // ── Ассеты dev (только чтение) ──────────────────────────────────────

        [Test]
        public void Потомок_киборга_цель_база_и_риг_профиля()
        {
            UxrAvatar avatar = Load(GripTargetProfile.MefCyborgPlayerPath).GetComponent<UxrAvatar>();
            AvatarGripRegistration.Target target = AvatarGripRegistration.ResolveTarget(avatar);
            Assert.IsTrue(target.Family);
            Assert.That(target.RecordGuid, Is.EqualTo(BaseGuid));
            Assert.That(target.RecordName, Is.EqualTo("CyborgHandsBase"));
            Assert.That(AssetDatabase.AssetPathToGUID(target.RegistryPath), Is.EqualTo(BaseGuid));
            Assert.That(target.RigPath, Is.EqualTo(GripTargetProfile.MefCyborgPlayerPath));
            // Ручной образец лежит там же и назван по тому же правилу.
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<UxrHandPoseAsset>(
                $"{target.PoseFolder}/{AvatarGripRegistration.GripPoseName("Kinemation", "AK105", AvatarGripRegistration.MagazinePoint)}.asset"));
        }

        [Test]
        public void Точка_с_хватом_базы_пропускается()
        {
            AvatarGripRegistration.Target target = AvatarGripRegistration.ResolveTarget(Load(GripTargetProfile.MefCyborgPlayerPath).GetComponent<UxrAvatar>());
            UxrGrabPointInfo point = Load(Ak105Magazine).GetComponent<UxrGrabbableObject>().GetGrabPoint(0);
            UxrGripPoseInfo existing = AvatarGripRegistration.ExistingEntry(point, target);
            Assert.IsNotNull(existing, "У AK105_mag есть запись CyborgHandsBase (d7d73887e) — точка должна пропускаться.");
            Assert.That(existing.AvatarPrefabGuid, Is.EqualTo(BaseGuid));
        }

        [Test]
        public void Поза_orig_магазина_не_вырождена()
        {
            var pose = AssetDatabase.LoadAssetAtPath<UxrHandPoseAsset>(Ak105MagazineOrig);
            Assert.IsNotNull(pose, "Нет " + Ak105MagazineOrig);
            Assert.IsNull(AvatarGripRegistration.DescriptorDefect(pose.HandDescriptorLeft));
            Assert.IsNull(AvatarGripRegistration.DescriptorDefect(pose.HandDescriptorRight));
        }

        private static GameObject Load(string path)
        {
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(asset, "Нет префаба " + path);
            return asset;
        }

        // ── YamlCases: начало ──
        /// <summary>
        /// Строки документа <c>UxrGrabbableObject</c> MKR9_mag (dev 47135fae0) и ожидаемые варианты после записи хвата
        /// CyborgHandsBase — блок в формате записи AK105_mag (d7d73887e).
        /// </summary>
        public static class YamlCases
        {
            public const string RecordGuid = "71b74b0d08444f49bbb7aa2828d0890a";
            public const string PoseGuid = "0123456789abcdef0123456789abcdef";
            public const long LeftId = 3855134755490656759;
            public const long RightId = -6781682684537006375;

            public static string Problem(string[] was, string[] now, int grabPoint) =>
                AvatarGripYaml.GrabbableProblem(was, now, RecordGuid, PoseGuid, LeftId, RightId, grabPoint);

            /// <remarks>Хвостовой пробел строк YAML в тексте записан как «$».</remarks>
            public static string[] Was() =>
                Mkr9MagGrabbable.Replace("\r\n", "\n").Split('\n')
                                .Select(l => l.EndsWith("$", System.StringComparison.Ordinal) ? l.Substring(0, l.Length - 1) : l).ToArray();

            private static string[] Block(string indent) =>
                new[]
                {
                    indent + "- _avatarPrefabGuid: " + RecordGuid,
                    indent + "  _handPose: {fileID: 11400000, guid: " + PoseGuid + ", type: 2}",
                    indent + "  _poseBlendValue: 0.33",
                    indent + "  _gripAlignTransformHandLeft: {fileID: " + LeftId + "}",
                    indent + "  _gripAlignTransformHandRight: {fileID: " + RightId + "}"
                };

            private static string[] InsertAfter(string[] lines, int index, IEnumerable<string> block) =>
                lines.Take(index + 1).Concat(block).Concat(lines.Skip(index + 1)).ToArray();

            private static int Find(string[] lines, string line, int from = 0)
            {
                int index = System.Array.IndexOf(lines, line, from);
                if (index < 0) throw new System.InvalidOperationException("Нет строки «" + line + "»");
                return index;
            }

            /// <summary>Блок CyborgHandsBase после записи MEF в _grabPoint.</summary>
            public static string[] Appended()
            {
                string[] was = Was();
                return InsertAfter(was, Find(was, "      _gripAlignTransformHandRight: {fileID: 7140115223206406474}"), Block("    "));
            }

            public const string NewPoseGuid = "fedcba9876543210fedcba9876543210";

            /// <summary>Тот же документ с заменёнными значениями блока записи CyborgHandsBase: поза, blend, align 11 / −22.</summary>
            public static string[] Replaced(string[] withEntry)
            {
                string[] lines = (string[])withEntry.Clone();
                int head = Find(lines, "    - _avatarPrefabGuid: " + RecordGuid);
                lines[head + 1] = "      _handPose: {fileID: 11400000, guid: " + NewPoseGuid + ", type: 2}";
                lines[head + 2] = "      _poseBlendValue: 0.33";
                lines[head + 3] = "      _gripAlignTransformHandLeft: {fileID: 11}";
                lines[head + 4] = "      _gripAlignTransformHandRight: {fileID: -22}";
                return lines;
            }

            /// <summary>Блок CyborgHandsBase перед записью MEF — не последним элементом.</summary>
            public static string[] InsertedFirst()
            {
                string[] was = Was();
                return InsertAfter(was, Find(was, "    _avatarGripPoseEntries:"), Block("    "));
            }

            /// <summary>Тот же документ с пустым списком записей точки 0.</summary>
            public static string[] WasEmptyList()
            {
                var lines = Was().ToList();
                int key = lines.IndexOf("    _avatarGripPoseEntries:");
                lines.RemoveRange(key + 1, 5);
                lines[key] = "    _avatarGripPoseEntries: []";
                return lines.ToArray();
            }

            public static string[] AppendedToEmptyList()
            {
                var lines = WasEmptyList().ToList();
                int key = lines.IndexOf("    _avatarGripPoseEntries: []");
                lines[key] = "    _avatarGripPoseEntries:";
                lines.InsertRange(key + 1, Block("    "));
                return lines.ToArray();
            }

            /// <summary>Документ со второй точкой хвата (Support) — копия _grabPoint элементом _additionalGrabPoints, как у AK105.</summary>
            public static string[] WasWithSupport()
            {
                string[] was = Was();
                int start = Find(was, "  _grabPoint:") + 1;
                int end = Find(was, "  _additionalGrabPoints: []");
                string[] item = was.Skip(start).Take(end - start).Select((l, i) => i == 0 ? "  - " + l.Substring(4) : l).ToArray();
                return was.Take(end).Concat(new[] { "  _additionalGrabPoints:" }).Concat(item).Concat(was.Skip(end + 1)).ToArray();
            }

            public static string[] AppendedToSupport()
            {
                string[] was = WasWithSupport();
                int support = Find(was, "  _additionalGrabPoints:");
                return InsertAfter(was, Find(was, "      _gripAlignTransformHandRight: {fileID: 7140115223206406474}", support), Block("    "));
            }

            private const string Mkr9MagGrabbable = @"MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 4230906836973670970}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: 79f5a30897a3c024c805c866226dd743, type: 3}
  m_Name: $
  m_EditorClassIdentifier: $
  _uxrUniqueId: c3c052d7-850b-48f4-8f54-79a22b08e8ee
  __prefabGuid: 4d3a79244e8df3f498b26da0ff300b22
  __isInPrefab: 1
  _isDummyGrabbableParent: 0
  _controlParentDirection: 1
  _ignoreGrabbableParentDependency: 0
  _priority: 0
  _allowMultiGrab: 1
  _translationConstraintMode: 0
  _restrictToBox: {fileID: 0}
  _restrictToSphere: {fileID: 0}
  _translationLimitsMin: {x: 0, y: 0, z: 0}
  _translationLimitsMax: {x: 0, y: 0, z: 0}
  _rotationConstraintMode: 0
  _rotationAngleLimitsMin: {x: 0, y: 0, z: 0}
  _rotationAngleLimitsMax: {x: 0, y: 0, z: 0}
  _autoRotationProvider: 1
  _rotationProvider: 0
  _rotationLongitudinalAxis:
    _axis: 2
  _needsTwoHandsToRotate: 0
  _lockedGrabReleaseDistance: 0.4
  _translationResistance: 0
  _rotationResistance: 0
  _rigidBodySource: {fileID: 3633602680810483543}
  _rigidBodyDynamicOnRelease: 1
  _verticalReleaseMultiplier: 1
  _horizontalReleaseMultiplier: 1
  _previewGrabPosesMode: 1
  _previewPosesRegenerationType: 0
  _previewPosesRegenerationIndex: -1
  _selectedAvatarForGrips: {fileID: 2537556764526307725, guid: 71b74b0d08444f49bbb7aa2828d0890a, type: 3}
  _firstGrabPointIsMain: 1
  _grabPoint:
    _editorFoldout: 1
    _editorName: $
    _grabMode: 0
    _useDefaultGrabButtons: 1
    _bothHandsCompatible: 1
    _handSide: 0
    _inputButtons: 0
    _hideHandGrabberRenderer: 0
    _defaultGripPoseInfo:
      _avatarPrefabGuid: $
      _handPose: {fileID: 0}
      _poseBlendValue: -1
      _gripAlignTransformHandLeft: {fileID: 0}
      _gripAlignTransformHandRight: {fileID: 0}
    _snapMode: 3
    _snapDirection: 0
    _snapReference: 1
    _avatarGripPoseEntries:
    - _avatarPrefabGuid: b6fe59db941fa944696ece5e1aabc032
      _handPose: {fileID: 11400000, guid: e2f8bc7c0195daa4092d8071a7494e7f, type: 2}
      _poseBlendValue: 0.68
      _gripAlignTransformHandLeft: {fileID: 288491325183357761}
      _gripAlignTransformHandRight: {fileID: 7140115223206406474}
    _alignToController: 0
    _alignToControllerAxes: {fileID: 0}
    _grabProximityMode: 0
    _grabProximityBox: {fileID: 0}
    _maxDistanceGrab: 0.12
    _grabProximityTransformUseSelf: 1
    _grabProximityTransform: {fileID: 0}
    _grabberProximityUseDefault: 1
    _grabberProximityIndex: 0
    _enableOnHandNear: {fileID: 6427537186849082334}
  _additionalGrabPoints: []
  _useParenting: 1
  _autoCreateStartAnchor: 0
  _autoAnchorMaxPlaceDistance: 0.1
  _startAnchor: {fileID: 0}
  _tag: MagMKR9
  _dropAlignTransformUseSelf: 1
  _dropAlignTransform: {fileID: 0}
  _dropSnapMode: 3
  _dropProximityTransformUseSelf: 1
  _dropProximityTransform: {fileID: 0}";
        }
        // ── YamlCases: конец ──
    }
}
