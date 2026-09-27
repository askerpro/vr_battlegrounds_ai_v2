using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Оружие из пака Hands Weapons Animations собрано по паку, а не на глаз.
    ///
    /// <para>
    /// Деталь пака — <see cref="SkinnedMeshRenderer" />, жёстко привязанный к одной кости; её
    /// место — <c>кость × bindpose</c>, а не трансформ рендерера (они расходятся). Клип выстрела
    /// двигает те же кости: из него берутся ход помпы/затвора и угол спуска. Тест пересчитывает
    /// всё это сам, независимо от сборщика <c>HandsPackWeaponBuilder</c>.
    /// </para>
    ///
    /// <para>
    /// Новое оружие из пака — добавь кейс в <see cref="Cases" />.
    /// </para>
    /// </summary>
    public class HandsPackWeaponTests
    {
        private const string Pack = "Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Modelas/";

        public sealed class Case
        {
            public string Prefab;
            public string Folder;
            public string Body;                      // рендерер корпуса в паке
            public string BodyPath;                  // корпус в префабе
            public Dictionary<string, string> Parts; // деталь пака → путь в префабе
            public string ActionPart;                // помпа/затвор в паке
            public string ActionPath;                // его граббабл в префабе
            public string TriggerPart;
            public string Clip = "Shot";
            public string RestClip = "Idle";

            public override string ToString() => System.IO.Path.GetFileNameWithoutExtension(Prefab);
        }

        public static IEnumerable<Case> Cases()
        {
            yield return new Case
            {
                Prefab      = "Assets/Prefabs/Weapons/ShotgunReal/Shotgun_real.prefab",
                Folder      = "Hands_Shotgun",
                Body        = "Shogun_Base_mesh",
                BodyPath    = "MeshContainer/Base",
                Parts       = new Dictionary<string, string>
                {
                    { "Shogun_Fore-End_mesh", "Pump/ForeEnd" },
                    { "Shogun_Triger_mesh", "MeshContainer/Trigger" },
                    { "Shogun_Gate_mesh", "MeshContainer/Gate" },
                    { "Shogun_Staple_mesh", "MeshContainer/Staple" }
                },
                ActionPart  = "Shogun_Fore-End_mesh",
                ActionPath  = "Pump",
                TriggerPart = "Shogun_Triger_mesh"
            };
        }

        [TestCaseSource(nameof(Cases))]
        public void Детали_стоят_как_в_паке(Case c)
        {
            GameObject prefab = Load(c);
            using var pack = new PackModel(c);
            pack.BindPose();

            Transform body = prefab.transform.Find(c.BodyPath);
            var failures = new List<string>();

            foreach (var pair in c.Parts)
            {
                Transform part = prefab.transform.Find(pair.Value);
                Assert.IsNotNull(part, $"Контроль: в {prefab.name} есть {pair.Value}");

                // Место детали в осях корпуса, в единицах меша — масштаб корня сокращается.
                Matrix4x4 actual   = body.localToWorldMatrix.inverse * part.localToWorldMatrix;
                Matrix4x4 expected = pack.PartInBody(pair.Key);

                float dPos = Vector3.Distance(actual.GetColumn(3), expected.GetColumn(3));
                float dRot = Quaternion.Angle(actual.rotation, expected.rotation);
                if (dPos > 0.0005f || dRot > 0.5f)
                    failures.Add($"{pair.Value}: смещена на {dPos * 1000f:F1} мм, повёрнута на {dRot:F1}° от места в паке");
            }

            Assert.IsEmpty(failures, $"{prefab.name}: детали не на своих местах:\n" + string.Join("\n", failures));
        }

        [TestCaseSource(nameof(Cases))]
        public void Ход_помпы_или_затвора_как_в_клипе(Case c)
        {
            GameObject prefab = Load(c);
            using var pack = new PackModel(c);
            (Vector3 axis, float travel, _) = pack.Motion(c.ActionPart);

            var action = prefab.transform.Find(c.ActionPath).GetComponent<UxrGrabbableObject>();
            Vector3 limit = action.TranslationLimitsMin + action.TranslationLimitsMax; // одна из границ нулевая

            // Ход в единицах меша: помпа — ребёнок корня, как и контейнер деталей с масштабом 1.
            Transform body = prefab.transform.Find(c.BodyPath);
            Vector3 expected = prefab.transform.InverseTransformDirection(body.TransformDirection(axis)).normalized * travel;

            Assert.AreEqual(UxrTranslationConstraintMode.RestrictLocalOffset, action.TranslationConstraint, $"{action.name}: ход не ограничен");
            Assert.That(Vector3.Distance(limit, expected), Is.LessThan(0.001f),
                        $"{prefab.name}/{action.name}: ход {limit * 100f} см, в клипе '{c.Clip}' {expected * 100f} см");
        }

        [TestCaseSource(nameof(Cases))]
        public void Угол_спуска_как_в_клипе(Case c)
        {
            GameObject prefab = Load(c);
            using var pack = new PackModel(c);
            (_, _, float angle) = pack.Motion(c.TriggerPart);

            var firearm = prefab.GetComponent<UxrFirearmWeapon>();
            float degrees = new SerializedObject(firearm).FindProperty("_triggers").GetArrayElementAtIndex(0)
                                                         .FindPropertyRelative("_triggerRotationDegrees").floatValue;

            Assert.That(Mathf.Abs(degrees), Is.EqualTo(angle).Within(1f), $"{prefab.name}: спуск {degrees}°, в клипе '{c.Clip}' {angle:F0}°");
        }

        private static GameObject Load(Case c)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(c.Prefab);
            Assert.IsNotNull(prefab, $"Нет префаба {c.Prefab}");
            return prefab;
        }

        /// <summary>Модель пака, повёрнутая кадрами клипов.</summary>
        private sealed class PackModel : System.IDisposable
        {
            private readonly Case _case;
            private readonly GameObject _instance;
            private readonly Dictionary<string, SkinnedMeshRenderer> _parts;

            public PackModel(Case c)
            {
                _case = c;
                string model = AssetDatabase.FindAssets("t:Model", new[] { Pack + c.Folder }).Select(AssetDatabase.GUIDToAssetPath).First(p => !p.Contains("@"));
                _instance = (GameObject)Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(model));
                _instance.hideFlags = HideFlags.HideAndDontSave;
                _parts = _instance.GetComponentsInChildren<SkinnedMeshRenderer>(true).ToDictionary(r => r.name);
                _model = model;
            }

            private readonly string _model;

            public void Dispose() => Object.DestroyImmediate(_instance);

            public void BindPose()
            {
                foreach (var src in AssetDatabase.LoadAssetAtPath<GameObject>(_model).GetComponentsInChildren<Transform>(true))
                {
                    Transform dst = _instance.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == src.name);
                    if (dst != null) dst.SetLocalPositionAndRotation(src.localPosition, src.localRotation);
                }
            }

            public Matrix4x4 PartInBody(string part) => Placement(_case.Body).inverse * Placement(part);

            /// <summary>Ось хода, наибольший ход и наибольший угол детали за клип от кадра покоя.</summary>
            public (Vector3 axis, float travel, float angle) Motion(string part)
            {
                Clip(_case.RestClip).SampleAnimation(_instance, 0f);
                Matrix4x4 rest = PartInBody(part);
                AnimationClip clip = Clip(_case.Clip);

                Vector3 far = Vector3.zero;
                float angle = 0f;
                for (float t = 0f; t <= clip.length + 1e-4f; t += 1f / 120f)
                {
                    clip.SampleAnimation(_instance, t);
                    Matrix4x4 m = PartInBody(part);
                    Vector3 d = (Vector3)m.GetColumn(3) - (Vector3)rest.GetColumn(3);
                    if (d.magnitude > far.magnitude) far = d;
                    angle = Mathf.Max(angle, Quaternion.Angle(m.rotation, rest.rotation));
                }

                return (far.normalized, far.magnitude, angle);
            }

            private Matrix4x4 Placement(string part)
            {
                SkinnedMeshRenderer r = _parts[part];
                var count = new int[r.bones.Length];
                foreach (var w in r.sharedMesh.boneWeights) count[w.boneIndex0]++;
                int bone = System.Array.IndexOf(count, count.Max());
                return r.bones[bone].localToWorldMatrix * r.sharedMesh.bindposes[bone];
            }

            private AnimationClip Clip(string name) =>
                AssetDatabase.FindAssets("t:AnimationClip", new[] { Pack + _case.Folder })
                             .SelectMany(g => AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(g)))
                             .OfType<AnimationClip>().First(c => c.name == name);
        }
    }
}
