using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Interaction;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Оружие из пака KINEMATION Tactical Shooter (T-39) собрано по паку и подготовлено под Quest.
    ///
    /// <para>
    /// Корпус в паке — один <see cref="SkinnedMeshRenderer" />, детали — кости внутри него; сборщик
    /// (<c>KinemationWeaponBuilder</c>) режет меш по костям на статичные детали. Тест сам пересчитывает место
    /// детали (кость в осях кости корпуса), её вершины (кость × bindpose), ход затвора и угол спуска из клипа
    /// оружия — независимо от сборщика. Плюс то, ради чего пак переделывается: лишние патроны выброшены,
    /// материалы — URP Lit с текстурами ≤ 1024 для Android, звуки — моно и короткие.
    /// </para>
    ///
    /// <para>Новый ствол KINEMATION — кейс в <see cref="Cases" />.</para>
    /// </summary>
    public class KinemationWeaponTests
    {
        private const string Pack = "Assets/ThirdParty/KINEMATION/TacticalShooterPack/";
        private const string LitShader = "Universal Render Pipeline/Lit";
        private const int MaxTexture = 1024;
        private const float MaxSoundSeconds = 2.5f;

        public sealed class Case
        {
            public string Prefab;
            public string PackPrefab;               // W_*.prefab
            public string AnimFolder;               // Animations/<папка>/Weapon
            public string RestClip;                 // клип покоя оружия: ставит магазин в гнездо (A_W_*_Idle)
            public string Body = "Body";            // кость корпуса
            public string BodyPath = "MeshContainer/Base";
            public Dictionary<string, string> Parts; // кость пака → путь детали в префабе
            public string ActionPart;               // кость затвора
            public string ActionPath = "Slide";
            public string TriggerPart = "Trigger";
            public string Clip;                     // клип оружия с ходом затвора и спуска
            public string EmptyClip;                // отдельная открытая поза; ручной ход включает ещё 4 мм
            public string[] Dropped;                // кости, которых в префабе быть не должно (лишние патроны)
            public int MaxTriangles;                // бюджет ствола с магазином
            public Dictionary<string, string> Attachments; // обвес пака (MeshRenderer: глушитель, коллиматор) → путь в префабе
            public string Muzzle;                   // обвес, на срезе которого дуло (глушитель); null — корпус

            public override string ToString() => System.IO.Path.GetFileNameWithoutExtension(Prefab);
        }

        public static IEnumerable<Case> Cases()
        {
            const string W = "Assets/Prefabs/Weapons/";
            yield return new Case
            {
                Prefab = W + "SRM12/SRM12.prefab", PackPrefab = "W_Oryx_SRM-12", AnimFolder = "SRM-12", RestClip = "A_W_SRM-12_Idle",
                Parts = new Dictionary<string, string>
                {
                    { "Trigger", "MeshContainer/Trigger" }, { "Bolt", "Slide/Bolt" }, { "Bipod", "MeshContainer/Bipod" },
                    { "Safety", "MeshContainer/Safety" }, { "SRS_Magazine", "MeshContainer/MagAnchor/SRM12_mag/Mesh" },
                    { "Bullet", "MeshContainer/MagAnchor/SRM12_mag/Bullet" }
                },
                ActionPart = "Bolt", Clip = "A_W_SRM-12_Fire",
                Dropped = new[] { "Bullet_001", "Bullet_002", "Bullet_003", "Bullet_004", "Spring" }, MaxTriangles = 20000
            };
            yield return new Case
            {
                Prefab = W + "Mk14/Mk14.prefab", PackPrefab = "W_Mk14EBR", AnimFolder = "Mk14EBR",
                Parts = new Dictionary<string, string>
                {
                    { "Trigger", "MeshContainer/Trigger" }, { "BoltCharger", "Slide/BoltCharger" }, { "Bolt", "Slide/Bolt" },
                    { "MagRelease", "MeshContainer/MagRelease" }, { "Mk14_Magazine", "MeshContainer/MagAnchor/Mk14_mag/Mesh" },
                    { "Mk14_Ammo_001", "MeshContainer/MagAnchor/Mk14_mag/Mk14_Ammo_001" }
                },
                ActionPart = "BoltCharger", Clip = "A_W_Mk14EBR_Fire",
                Dropped = new[] { "Mk14_Ammo_003", "Mk14_Ammo_020", "Mk14_Follower", "Mk14_MagString" }, MaxTriangles = 34000
            };
            yield return new Case
            {
                Prefab = W + "AK105/AK105.prefab", PackPrefab = "W_AK105", AnimFolder = "AK105", RestClip = "A_W_AK105_Idle",
                Parts = new Dictionary<string, string>
                {
                    { "Trigger", "MeshContainer/Trigger" }, { "Charger", "Slide/Charger" }, { "Stock", "MeshContainer/Stock" },
                    { "Magazine", "MeshContainer/MagAnchor/AK105_mag/Mesh" }, { "Ammo_001", "MeshContainer/MagAnchor/AK105_mag/Ammo_001" }
                },
                ActionPart = "Charger", Clip = "A_W_AK105_Fire",
                Dropped = new[] { "Ammo_003", "Ammo_030", "Follower", "Spring" }, MaxTriangles = 36000
            };
            yield return new Case
            {
                Prefab = W + "MKR9/MKR9.prefab", PackPrefab = "W_MKR9", AnimFolder = "MKR9", RestClip = "A_W_MKR9_Idle",
                Parts = new Dictionary<string, string>
                {
                    { "Trigger", "MeshContainer/Trigger" }, { "Bolt", "Slide/Bolt" }, { "ChargingHandle", "Slide/ChargingHandle" },
                    { "Stock", "MeshContainer/Stock" }, { "Mag", "MeshContainer/MagAnchor/MKR9_mag/Mesh" },
                    { "Ammo_01", "MeshContainer/MagAnchor/MKR9_mag/Ammo_01" }
                },
                ActionPart = "Bolt", Clip = "A_W_MKR9_Fire",
                Dropped = new[] { "Ammo_03", "Ammo_30", "Follower", "Spring" }, MaxTriangles = 30000
            };
            yield return new Case
            {
                Prefab = W + "Viper/Viper.prefab", PackPrefab = "W_WK-11_Viper", AnimFolder = "WK-11_Viper", RestClip = "A_W_WK-11_Viper_Idle",
                Parts = new Dictionary<string, string>
                {
                    { "Trigger", "MeshContainer/Trigger" }, { "Bolt", "Slide/Bolt" }, { "Hammer", "MeshContainer/Hammer" },
                    { "Magazine", "MeshContainer/MagAnchor/Viper_mag/Mesh" }, { "Cartridge_026", "MeshContainer/MagAnchor/Viper_mag/Cartridge_026" }
                },
                ActionPart = "Bolt", Clip = "A_W_WK-11_Viper_Fire", EmptyClip = "A_W_WK-11_Viper_Fire_Empty",
                Dropped = new[] { "Cartridge", "Cartridge_001", "String", "Follower" }, MaxTriangles = 13000
            };
            yield return new Case
            {
                Prefab = W + "Herrington/Herrington.prefab", PackPrefab = "W_Herrington_11-87_Police", AnimFolder = "Herrington_11-87",
                RestClip = "A_W_Herrington_11-87_Idle",
                Parts = new Dictionary<string, string>
                {
                    { "Trigger", "MeshContainer/Trigger" }, { "Bolt", "Slide/Bolt" }, { "Feed", "MeshContainer/Feed" }
                },
                ActionPart = "Bolt", Clip = "A_W_Herrington_11-87_Fire", EmptyClip = "A_W_Herrington_11-87_Fire_Out",
                Dropped = new string[0], MaxTriangles = 10000
            };
            yield return new Case
            {
                Prefab = W + "TR15/TR15.prefab", PackPrefab = "W_TR15", AnimFolder = "TR15", RestClip = "A_W_TR15_Idle",
                Parts = new Dictionary<string, string>
                {
                    { "Trigger", "MeshContainer/Trigger" }, { "Bolt", "Slide/Bolt" }, { "Charger", "Slide/Charger" },
                    { "Dustcover", "MeshContainer/Dustcover" }, { "Magazine", "MeshContainer/MagAnchor/TR15_mag/Mesh" },
                    { "Cartridge_1", "MeshContainer/MagAnchor/TR15_mag/Cartridge_1" }
                },
                Attachments = new Dictionary<string, string>
                {
                    { "SM_Attach_AR15_Silencer", "MeshContainer/SM_Attach_AR15_Silencer" }, { "SM_Attach_AR15_XPS2", "MeshContainer/SM_Attach_AR15_XPS2" }
                },
                Muzzle = "SM_Attach_AR15_Silencer",
                ActionPart = "Bolt", Clip = "A_W_TR15_Fire", EmptyClip = "A_W_TR15_Fire_Out",
                Dropped = new[] { "Cartridge_3", "Cartridge_30", "Follower", "Spring", "Reticle", "SM_Attach_AR15_Grip" }, MaxTriangles = 30000
            };
        }

        /// <summary>Револьвер: затвора нет — отдельный набор проверок (детали и спуск), ход затвора не проверяется.</summary>
        public static IEnumerable<Case> Revolvers()
        {
            yield return new Case
            {
                Prefab = "Assets/Prefabs/Weapons/R08/R08.prefab", PackPrefab = "W_R08", AnimFolder = "R08", RestClip = "A_W_R08_Idle",
                Parts = new Dictionary<string, string>
                {
                    { "Trigger", "MeshContainer/Trigger" }, { "Hammer", "MeshContainer/Hammer" },
                    { "Cylinder", "MeshContainer/MagAnchor/R08_mag/Mesh" }, { "Ammo_01", "MeshContainer/MagAnchor/R08_mag/Ammo_01" }
                },
                Clip = "A_W_R08_Fire", Dropped = new[] { "Speedloader" }, MaxTriangles = 22000
            };
        }

        public static IEnumerable<Case> AllCases() => Cases().Concat(Revolvers());

        [TestCaseSource(nameof(AllCases))]
        public void Детали_стоят_как_в_паке(Case c)
        {
            GameObject prefab = Load(c);
            using var pack = new PackModel(c);
            Transform body = Find(prefab, c.BodyPath);
            var failures = new List<string>();

            foreach (var pair in c.Parts)
            {
                Transform part = Find(prefab, pair.Value);
                Matrix4x4 actual = Rigid(body.localToWorldMatrix).inverse * Rigid(part.localToWorldMatrix);
                Matrix4x4 expected = pack.PartInBody(pair.Key);
                float dPos = Vector3.Distance(actual.GetColumn(3), expected.GetColumn(3));
                float dRot = Quaternion.Angle(actual.rotation, expected.rotation);
                if (dPos > 0.0005f || dRot > 0.5f)
                    failures.Add($"{pair.Value}: смещена на {dPos * 1000f:F1} мм, повёрнута на {dRot:F1}° от кости {pair.Key}");
            }

            Assert.IsEmpty(failures, $"{prefab.name}: детали не на своих местах:\n" + string.Join("\n", failures));
        }

        /// <summary>Вершины детали — те же, что у кости в паке (кость × bindpose), в осях корпуса: меш вырезан верно.</summary>
        [TestCaseSource(nameof(AllCases))]
        public void Геометрия_детали_как_в_паке(Case c)
        {
            GameObject prefab = Load(c);
            using var pack = new PackModel(c);
            Transform body = Find(prefab, c.BodyPath);
            var failures = new List<string>();

            foreach (var pair in c.Parts)
            {
                Transform part = Find(prefab, pair.Value);
                Mesh mesh = part.GetComponent<MeshFilter>().sharedMesh;
                Matrix4x4 toBody = Rigid(body.localToWorldMatrix).inverse * part.localToWorldMatrix;
                Bounds actual = BoundsOf(mesh.vertices.Select(v => toBody.MultiplyPoint3x4(v)));
                Bounds expected = pack.BoneVerticesInBody(pair.Key);

                float d = Vector3.Distance(actual.min, expected.min) + Vector3.Distance(actual.max, expected.max);
                if (d > 0.001f) failures.Add($"{pair.Value}: габарит {actual.size * 100f} см, в паке {expected.size * 100f} см (расхождение {d * 1000f:F1} мм)");
            }

            Assert.IsEmpty(failures, $"{prefab.name}: геометрия деталей не совпадает с паком:\n" + string.Join("\n", failures));
        }

        /// <summary>
        /// Обвесы пака (глушитель, корпус коллиматора) стоят там же, где в префабе пака, а дуло — на срезе глушителя:
        /// иначе снаряд, вспышка и проверка ствола в стене (<c>BarrelObstruction</c>) начинались бы внутри глушителя.
        /// </summary>
        [TestCaseSource(nameof(WithAttachments))]
        public void Обвесы_на_месте_и_дуло_на_срезе_глушителя(Case c)
        {
            GameObject prefab = Load(c);
            using var pack = new PackModel(c);
            Transform body = Find(prefab, c.BodyPath);
            var failures = new List<string>();

            foreach (var pair in c.Attachments)
            {
                Transform part = Find(prefab, pair.Value);
                Matrix4x4 actual = Rigid(body.localToWorldMatrix).inverse * Rigid(part.localToWorldMatrix);
                Matrix4x4 expected = pack.AttachmentInBody(pair.Key);
                float dPos = Vector3.Distance(actual.GetColumn(3), expected.GetColumn(3));
                float dRot = Quaternion.Angle(actual.rotation, expected.rotation);
                if (dPos > 0.0005f || dRot > 0.5f) failures.Add($"{pair.Value}: смещён на {dPos * 1000f:F1} мм, повёрнут на {dRot:F1}° от обвеса пака");

                // Меш обвеса с масштабом его трансформа в паке: габарит в осях корпуса совпадает.
                Bounds got = BoundsOf(part.GetComponent<MeshFilter>().sharedMesh.vertices.Select(v => (Rigid(body.localToWorldMatrix).inverse * part.localToWorldMatrix).MultiplyPoint3x4(v)));
                Bounds want = pack.AttachmentVerticesInBody(pair.Key);
                float d = Vector3.Distance(got.min, want.min) + Vector3.Distance(got.max, want.max);
                if (d > 0.001f) failures.Add($"{pair.Value}: габарит {got.size * 100f} см, в паке {want.size * 100f} см");
            }

            if (c.Muzzle != null)
            {
                Transform muzzle = Find(prefab, c.Attachments[c.Muzzle]);
                Transform tip = Find(prefab, "MeshContainer/Tip");
                Vector3 forward = prefab.transform.forward;
                float front = muzzle.GetComponent<MeshFilter>().sharedMesh.vertices.Max(v => Vector3.Dot(muzzle.TransformPoint(v), forward));
                float gap = front - Vector3.Dot(tip.position, forward);
                if (Mathf.Abs(gap) > 0.002f) failures.Add($"Tip в {gap * 100f:F1} см от среза {c.Muzzle} (вдоль ствола)");
            }

            Assert.IsEmpty(failures, $"{prefab.name}:\n" + string.Join("\n", failures));
        }

        public static IEnumerable<Case> WithAttachments() => Cases().Where(c => c.Attachments != null);

        [TestCaseSource(nameof(Cases))]
        public void Ход_затвора_как_в_клипе(Case c)
        {
            GameObject prefab = Load(c);
            using var pack = new PackModel(c);
            Vector3 far = pack.Far(c.ActionPart, c.Clip);

            var action = Find(prefab, c.ActionPath).GetComponent<UxrGrabbableObject>();
            Vector3 limit = action.TranslationLimitsMin + action.TranslationLimitsMax;
            Transform body = Find(prefab, c.BodyPath);
            Vector3 expected = prefab.transform.InverseTransformDirection(body.TransformDirection(far));

            // Fire-ход и полный ручной ход — разные величины. Открытая Empty-поза
            // должна помещаться в одностороннюю тягу, плюс принятые 4 мм довзведения.
            if (c.EmptyClip != null)
            {
                // Обязательные детали берём из контракта кейса, а не из проверяемых bindings:
                // потерянный Charger не должен уменьшать ожидаемый ход TR15.
                var parts = c.Parts.Where(p => p.Value.StartsWith(c.ActionPath + "/", System.StringComparison.Ordinal))
                    .Select(p => p.Key).ToArray();
                Assert.That(parts, Does.Contain(c.ActionPart), "Кейс не задаёт деталь затвора под ручной тягой.");

                var visuals = prefab.GetComponent<WeaponMechanismVisuals>();
                var host = prefab.GetComponent<WeaponSystem>();
                (string part, Transform target)[] bound;
                if (visuals == null && host != null && host.Rig.HasAction)
                {
                    // Этап drive (пилот Herrington): механические позы ведёт хост WeaponSystem, данные — в Rig
                    // (детали клипа Rig.Parts — перенос bindings WeaponMechanismVisuals один к одному).
                    // Проверка _originalSlideLength снята: поле было у WeaponMechanismVisuals, которого на пилоте нет;
                    // Fire-ход живёт в дорожке Motion.Fire, а сам ход затвора по-прежнему сверяется с клипом ниже.
                    Assert.IsNotNull(host.Rig.Motion?.Empty, "Нет отдельной Empty-позы.");
                    bound = host.Rig.Parts.Where(p => p != null).Select(p => (p.Name, p.Target)).ToArray();
                }
                else
                {
                    Assert.IsNotNull(visuals, "Нет владельца механических поз.");
                    Assert.That(new SerializedObject(visuals).FindProperty("_originalSlideLength").floatValue,
                        Is.EqualTo(expected.magnitude).Within(0.001f), "Исходный Fire-ход потерян.");
                    Assert.IsNotNull(visuals.Motion?.Empty, "Нет отдельной Empty-позы.");
                    Assert.IsNotNull(visuals.Bindings, "Нет bindings механических деталей.");
                    bound = visuals.Bindings.Where(b => b != null).Select(b => (b.Part, b.Target)).ToArray();
                }
                foreach (string part in parts)
                {
                    var bindings = bound.Where(b => b.part == part).ToArray();
                    Assert.That(bindings.Length, Is.EqualTo(1), $"{part}: нужен один binding ручной детали.");
                    Assert.That(bindings[0].target, Is.SameAs(Find(prefab, c.Parts[part])),
                        $"{part}: binding не ссылается на предусмотренную ручную деталь.");
                }
                using var emptyPack = new PackModel(c);
                float empty = emptyPack.MaxProjectedTravel(parts, c.EmptyClip,
                    prefab.transform.worldToLocalMatrix * body.localToWorldMatrix, Vector3.back);
                expected = Vector3.back * (Mathf.Max(expected.magnitude, empty) + 0.004f);
            }

            Assert.AreEqual(UxrTranslationConstraintMode.RestrictLocalOffset, action.TranslationConstraint, $"{action.name}: ход не ограничен");
            Assert.That(expected.magnitude, Is.GreaterThan(0.001f), "Клип не задаёт ручного хода.");
            Assert.That(Vector3.Distance(action.TranslationLimitsMin, Vector3.Min(expected, Vector3.zero)), Is.LessThan(0.001f));
            Assert.That(Vector3.Distance(action.TranslationLimitsMax, Vector3.Max(expected, Vector3.zero)), Is.LessThan(0.001f));
            Assert.That(Vector3.Distance(limit, expected), Is.LessThan(0.001f), $"{prefab.name}: ход затвора {limit * 100f} см, в клипе '{c.Clip}' {expected * 100f} см");
        }

        [TestCaseSource(nameof(AllCases))]
        public void Угол_спуска_как_в_клипе(Case c)
        {
            GameObject prefab = Load(c);
            using var pack = new PackModel(c);
            float angle = pack.MaxAngle(c.TriggerPart, c.Clip);

            float degrees = new SerializedObject(prefab.GetComponent<UxrFirearmWeapon>()).FindProperty("_triggers").GetArrayElementAtIndex(0)
                                                                                         .FindPropertyRelative("_triggerRotationDegrees").floatValue;
            Assert.That(Mathf.Abs(degrees), Is.EqualTo(angle).Within(1f), $"{prefab.name}: спуск {degrees}°, в клипе '{c.Clip}' {angle:F0}°");
        }

        /// <summary>Модель в реальном масштабе: корень ×1, как в паке (1 ед. = 1 м).</summary>
        [TestCaseSource(nameof(AllCases))]
        public void Масштаб_корня_единица(Case c)
        {
            Assert.That(Load(c).transform.localScale, Is.EqualTo(Vector3.one), "Пак KINEMATION в метрах — масштаб корня не нужен.");
        }

        [TestCaseSource(nameof(AllCases))]
        public void Лишних_патронов_нет_и_треугольники_в_бюджете(Case c)
        {
            GameObject prefab = Load(c);
            var meshes = prefab.GetComponentsInChildren<MeshFilter>(true).Where(f => f.name != "GrabHighlight" && f.sharedMesh != null).ToList();
            var names = new HashSet<string>(meshes.Select(f => f.name));
            var present = c.Dropped.Where(names.Contains).ToList();
            int triangles = meshes.Sum(f => Enumerable.Range(0, f.sharedMesh.subMeshCount).Sum(s => (int)f.sharedMesh.GetIndexCount(s) / 3));

            Assert.IsEmpty(present, $"{prefab.name}: в префабе детали, которые должны быть выброшены: {string.Join(", ", present)}");
            Assert.That(triangles, Is.LessThanOrEqualTo(c.MaxTriangles), $"{prefab.name}: {triangles} треугольников, бюджет {c.MaxTriangles}");
        }

        [TestCaseSource(nameof(AllCases))]
        public void Материалы_URP_Lit_и_текстуры_для_Android_не_больше_1024(Case c)
        {
            var failures = new List<string>();
            foreach (GameObject prefab in PrefabAndMagazine(c))
            foreach (Renderer r in prefab.GetComponentsInChildren<Renderer>(true))
            foreach (Material m in r.sharedMaterials)
            {
                if (m == null) { failures.Add($"{prefab.name}/{r.name}: пустой материал"); continue; }
                if (r.name == "GrabHighlight") continue;
                bool opticLens = c.PackPrefab == "W_TR15" && r.name == "SM_Attach_AR15_XPS2" &&
                    AssetDatabase.GetAssetPath(m) == "Assets/Art/Weapons/Kinemation/TR15/Materials/TR15_XPS2_Lens.mat";
                if (opticLens)
                {
                    // Только game-owned прозрачная линза XPS2: корпус по-прежнему URP Lit.
                    if (m.shader.name != "Universal Render Pipeline/Unlit" ||
                        m.GetFloat("_Surface") != 1f || m.GetFloat("_ZWrite") != 0f ||
                        m.GetFloat("_SrcBlend") != (float)UnityEngine.Rendering.BlendMode.SrcAlpha ||
                        m.GetFloat("_DstBlend") != (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha ||
                        m.renderQueue != (int)UnityEngine.Rendering.RenderQueue.Transparent || m.GetTexture("_BaseMap") == null)
                        failures.Add($"{prefab.name}/{r.name}: линза должна быть прозрачной Unlit с alpha blending без записи глубины и с текстурой.");
                }
                else if (m.shader.name != LitShader) failures.Add($"{prefab.name}/{r.name}: «{m.name}» на шейдере {m.shader.name}, ожидается {LitShader}");
                if (AssetDatabase.GetAssetPath(m).StartsWith(Pack)) failures.Add($"{prefab.name}/{r.name}: «{m.name}» — материал пака, а не проекта");

                foreach (string property in m.GetTexturePropertyNames())
                {
                    Texture t = m.GetTexture(property);
                    if (t == null) continue;
                    var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(t)) as TextureImporter;
                    if (importer == null) continue;
                    TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings("Android");
                    int max = android.overridden ? android.maxTextureSize : importer.maxTextureSize;
                    if (max > MaxTexture) failures.Add($"{m.name}.{property}: {t.name} — для Android {max}, нужно ≤ {MaxTexture}");
                }
            }

            Assert.IsEmpty(failures.Distinct().ToList(), string.Join("\n", failures.Distinct()));
        }

        /// <summary>Звуки ствола — свои (не файлы пака), моно и короткие: выстрел, затвор, магазин.</summary>
        [TestCaseSource(nameof(AllCases))]
        public void Звуки_моно_короткие_не_из_пака(Case c)
        {
            GameObject prefab = Load(c);
            var clips = new List<(string what, AudioClip clip)>();

            SerializedProperty trigger = new SerializedObject(prefab.GetComponent<UxrFirearmWeapon>()).FindProperty("_triggers").GetArrayElementAtIndex(0);
            clips.Add(("выстрел", trigger.FindPropertyRelative("_shotAudio._clip").objectReferenceValue as AudioClip));
            var feedback = prefab.GetComponent<AutomaticWeaponSlideFeedback>();
            var host = prefab.GetComponent<WeaponSystem>();
            if (feedback != null) // у револьвера затвора нет
            {
                var slide = new SerializedObject(feedback);
                clips.Add(("затвор назад", slide.FindProperty("_audioSlideBack._clip").objectReferenceValue as AudioClip));
                clips.Add(("затвор вперёд", slide.FindProperty("_audioSlideForward._clip").objectReferenceValue as AudioClip));
            }
            else if (host != null && host.Rig.HasAction)
            {
                // Этап drive (пилот Herrington): AutomaticWeaponSlideFeedback снят, звуки затвора — в WeaponAudioSet хоста.
                // Звук отказа Refusal здесь не проверяется: он не звук ствола, а общий UI-сигнал (S2).
                // Этап waves-f: звучит то, что выберет исполнитель, — оверрайд ствола или дефолт категории (Resolve).
                WeaponAudioSet defaults = host.FeedbackDefaults != null ? host.FeedbackDefaults.Audio : null;
                clips.Add(("затвор назад", WeaponAudioSet.Resolve(host.Audio, defaults, VrBattlegrounds.Weapons.Core.WeaponCue.ActionBack, out _)?.Clip));
                clips.Add(("затвор вперёд", WeaponAudioSet.Resolve(host.Audio, defaults, VrBattlegrounds.Weapons.Core.WeaponCue.ActionForwardChambered, out _)?.Clip));
            }
            var anchor = trigger.FindPropertyRelative("_ammunitionMagAnchor").objectReferenceValue as UxrGrabbableObjectAnchor;
            AnchorSound sound = anchor.GetComponent<AnchorSound>();
            clips.Add(("магазин вставлен", sound.InsertClip));
            clips.Add(("магазин снят", sound.TakeOutClip));

            var failures = new List<string>();
            foreach (var (what, clip) in clips)
            {
                if (clip == null) { failures.Add($"{what}: нет звука"); continue; }
                string path = AssetDatabase.GetAssetPath(clip);
                if (path.StartsWith(Pack)) failures.Add($"{what}: {clip.name} — файл пака (стерео, 5–10 с), а не нарезка");
                if (clip.channels != 1) failures.Add($"{what}: {clip.name} — {clip.channels} канала, для 3D-звука нужно моно");
                if (clip.length > MaxSoundSeconds) failures.Add($"{what}: {clip.name} — {clip.length:F1} с, не длиннее {MaxSoundSeconds} с");
            }

            Assert.IsEmpty(failures, $"{prefab.name}:\n" + string.Join("\n", failures));
        }

        // ── Вспомогательное ─────────────────────────────────────────────────────

        private static GameObject Load(Case c)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(c.Prefab);
            Assert.IsNotNull(prefab, $"Нет префаба {c.Prefab}");
            return prefab;
        }

        private static IEnumerable<GameObject> PrefabAndMagazine(Case c)
        {
            yield return Load(c);
            var mag = AssetDatabase.LoadAssetAtPath<GameObject>(c.Prefab.Replace(".prefab", "_mag.prefab"));
            Assert.IsNotNull(mag, $"Нет магазина {c.Prefab.Replace(".prefab", "_mag.prefab")}");
            yield return mag;
        }

        private static Transform Find(GameObject prefab, string path)
        {
            Transform t = prefab.transform.Find(path);
            Assert.IsNotNull(t, $"Контроль: в {prefab.name} есть {path}");
            return t;
        }

        private static Matrix4x4 Rigid(Matrix4x4 m) => Matrix4x4.TRS(m.GetColumn(3), m.rotation, Vector3.one);

        private static Bounds BoundsOf(IEnumerable<Vector3> points)
        {
            bool first = true;
            var b = new Bounds();
            foreach (Vector3 p in points)
            {
                if (first) { b = new Bounds(p, Vector3.zero); first = false; }
                else b.Encapsulate(p);
            }
            return b;
        }

        /// <summary>Префаб пака в позе префаба; клипы оружия — на его аниматоре.</summary>
        private sealed class PackModel : System.IDisposable
        {
            private readonly Case _case;
            private readonly GameObject _instance;
            private readonly Animator _animator;
            private readonly Dictionary<string, (SkinnedMeshRenderer renderer, int bone)> _bones = new Dictionary<string, (SkinnedMeshRenderer, int)>();

            public PackModel(Case c)
            {
                _case = c;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>($"{Pack}Prefabs/Weapons/{c.PackPrefab}.prefab");
                Assert.IsNotNull(prefab, $"Нет префаба пака {c.PackPrefab}");
                _instance = Object.Instantiate(prefab);
                _instance.hideFlags = HideFlags.HideAndDontSave;
                _animator = _instance.GetComponentInChildren<Animator>(true);
                if (c.RestClip != null) Clip(c.RestClip).SampleAnimation(_animator.gameObject, 0f);
                // Магазин полный: первый клип его аниматора в кадре 0 (так ставит его MagAnimator пака).
                foreach (Animator a in _instance.GetComponentsInChildren<Animator>(true))
                    if (a != _animator && a.runtimeAnimatorController != null && a.runtimeAnimatorController.animationClips.Length > 0)
                        a.runtimeAnimatorController.animationClips[0].SampleAnimation(a.gameObject, 0f);

                // Кость с вершинами — по имени; у кости без вершин (Magazine в корпусе) деталь не ищется.
                foreach (SkinnedMeshRenderer r in _instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    var count = new int[r.bones.Length];
                    foreach (BoneWeight w in r.sharedMesh.boneWeights) count[w.boneIndex0]++;
                    for (int b = 0; b < count.Length; b++)
                        if (count[b] > 0 && r.bones[b] != null && !_bones.ContainsKey(r.bones[b].name)) _bones[r.bones[b].name] = (r, b);
                }
            }

            public void Dispose() => Object.DestroyImmediate(_instance);

            private Transform Bone(string name) => _bones[name].renderer.bones[_bones[name].bone];

            public Matrix4x4 PartInBody(string part) => Rigid(Bone(_case.Body).localToWorldMatrix).inverse * Rigid(Bone(part).localToWorldMatrix);

            private MeshFilter Attachment(string name) =>
                _instance.GetComponentsInChildren<MeshFilter>(true).First(f => f.name == name && f.sharedMesh != null && f.GetComponent<MeshRenderer>() != null);

            public Matrix4x4 AttachmentInBody(string name) => Rigid(Bone(_case.Body).localToWorldMatrix).inverse * Rigid(Attachment(name).transform.localToWorldMatrix);

            public Bounds AttachmentVerticesInBody(string name)
            {
                MeshFilter f = Attachment(name);
                Matrix4x4 m = Rigid(Bone(_case.Body).localToWorldMatrix).inverse * f.transform.localToWorldMatrix;
                return BoundsOf(WithCpuData(f.sharedMesh).vertices.Select(v => m.MultiplyPoint3x4(v)));
            }

            public Bounds BoneVerticesInBody(string part)
            {
                (SkinnedMeshRenderer r, int bone) = _bones[part];
                Mesh mesh = WithCpuData(r.sharedMesh);
                Matrix4x4 m = Rigid(Bone(_case.Body).localToWorldMatrix).inverse * r.bones[bone].localToWorldMatrix * mesh.bindposes[bone];
                BoneWeight[] weights = mesh.boneWeights;
                Vector3[] vertices = mesh.vertices;
                // Вершины треугольников, у которых эта кость главная у большинства вершин (как режет сборщик).
                var used = new HashSet<int>();
                for (int s = 0; s < mesh.subMeshCount; s++)
                {
                    int[] tris = mesh.GetTriangles(s);
                    for (int i = 0; i < tris.Length; i += 3)
                    {
                        int a = weights[tris[i]].boneIndex0, b = weights[tris[i + 1]].boneIndex0, c = weights[tris[i + 2]].boneIndex0;
                        int owner = a == b || a == c ? a : b == c ? b : a;
                        if (owner != bone) continue;
                        used.Add(tris[i]); used.Add(tris[i + 1]); used.Add(tris[i + 2]);
                    }
                }
                return BoundsOf(used.Select(i => m.MultiplyPoint3x4(vertices[i])));
            }

            /// <summary>Наибольшее смещение детали от позы префаба за клип, в осях корпуса.</summary>
            public Vector3 Far(string part, string clip)
            {
                Matrix4x4 rest = PartInBody(part);
                AnimationClip a = Clip(clip);
                Vector3 far = Vector3.zero;
                for (float t = 0f; t <= a.length + 1e-4f; t += 1f / 120f)
                {
                    a.SampleAnimation(_animator.gameObject, t);
                    Vector3 d = (Vector3)PartInBody(part).GetColumn(3) - (Vector3)rest.GetColumn(3);
                    if (d.magnitude > far.magnitude) far = d;
                }
                return far;
            }

            /// <summary>Проекция всех движущихся деталей Empty-клипа, независимо от экспортированного motion asset.</summary>
            public float MaxProjectedTravel(string[] parts, string clip, Matrix4x4 bodyToRoot, Vector3 axis)
            {
                var rest = parts.ToDictionary(p => p, p => (Vector3)PartInBody(p).GetColumn(3));
                AnimationClip a = Clip(clip);
                float far = 0f;
                int steps = Mathf.CeilToInt(a.length * 120f);
                for (int i = 0; i <= steps; i++)
                {
                    a.SampleAnimation(_animator.gameObject, Mathf.Min(i / 120f, a.length));
                    foreach (string part in parts)
                    {
                        Vector3 delta = (Vector3)PartInBody(part).GetColumn(3) - rest[part];
                        far = Mathf.Max(far, Vector3.Dot(bodyToRoot.MultiplyVector(delta), axis));
                    }
                }
                return far;
            }

            public float MaxAngle(string part, string clip)
            {
                Matrix4x4 rest = PartInBody(part);
                AnimationClip a = Clip(clip);
                float angle = 0f;
                for (float t = 0f; t <= a.length + 1e-4f; t += 1f / 120f)
                {
                    a.SampleAnimation(_animator.gameObject, t);
                    angle = Mathf.Max(angle, Quaternion.Angle(PartInBody(part).rotation, rest.rotation));
                }
                return angle;
            }

            /// <summary>Меш пака без Read/Write после импорта других ассетов бывает без вершин на CPU — перезагрузка из модели.</summary>
            private static Mesh WithCpuData(Mesh mesh)
            {
                if (mesh.vertices.Length == mesh.vertexCount) return mesh;
                string path = AssetDatabase.GetAssetPath(mesh), name = mesh.name;
                Resources.UnloadAsset(mesh);
                return AssetDatabase.LoadAllAssetsAtPath(path).OfType<Mesh>().First(m => m.name == name);
            }

            private AnimationClip Clip(string name) =>
                AssetDatabase.FindAssets("t:AnimationClip", new[] { $"{Pack}Animations/{_case.AnimFolder}/Weapon" })
                             .SelectMany(g => AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(g)))
                             .OfType<AnimationClip>().First(c => c.name == name);
        }
    }
}
