using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Rig;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Дескрипторы всех поз кисти (<see cref="UxrHandPoseAsset"/>) в <c>Assets</c>, включая ThirdParty, целы.
    ///
    /// <para>
    /// <b>Что ловит.</b> Если оси пальцев рига (<c>AvatarRigInfo.FingerUniversalLocalAxes</c>) схлопнулись
    /// (<c>UxrAvatarArmInfo.SolveHandAndFingerAxes</c> берёт ближайшие оси, и у согнутой кисти right совпадает с ±forward,
    /// up = 0), сохранение позы пишет вырожденный дескриптор: у узлов от запястья <c>_up</c> = 0 и <c>_forward</c> = −<c>_right</c>,
    /// у узлов от фаланги все векторы нулевые. Так испорчена правая рука нескольких поз с коммита 1b2f88612 (2026-10-16);
    /// починка — «Copy Left To Right» в редакторе поз при исправных осях рига.
    /// </para>
    ///
    /// <para>
    /// <b>(а) Оси узлов.</b> Для каждого дескриптора (Fixed — один, Blend — Open и Closed) обеих рук у каждого узла
    /// векторы <c>_right</c>, <c>_up</c>, <c>_forward</c> единичные, попарно ортогональны и образуют правую тройку
    /// (right × up = forward) с допуском <see cref="AxisTolerance"/>. Пястная без данных
    /// (<see cref="UxrFingerDescriptor.HasMetacarpalInfo"/> = false) у SDK нулевая и не проверяется; других нулевых узлов
    /// у исправных поз нет (сверено по всем позам 2026-10-22). Та же проверка стоит перед записью позы в редакторе —
    /// <c>UxrHandPoseEditorWindow.TryValidateHandDescriptor</c> (патч SDK 93); здесь она продублирована, потому что сборка
    /// тестов не ссылается на UltimateXR.Editor.
    /// </para>
    ///
    /// <para>
    /// <b>(б) Зеркальность.</b> Правая рука сравнивается с зеркалом левой по формуле SDK
    /// (<see cref="UxrHandDescriptor.Mirrored"/>): угол узла — наибольший из углов между одноимёнными осями, нулевой вектор
    /// считается разворотом на 180°. Пороги по данным 2026-10-22: у исправных поз среднее по узлам до 27,4° (GrabClaw
    /// Cyborg SDK, Open; намеренная асимметрия поз проекта — Uzi, Viper, Scar, Gun — до 18°), наибольший узел до 64,5°;
    /// у испорченных среднее и максимум 180°. Порог среднего <see cref="MirrorMeanLimitDeg"/> оставляет запас над
    /// намеренной асимметрией, порог узла <see cref="MirrorNodeLimitDeg"/> ловит перевёрнутую ось.
    /// </para>
    ///
    /// <para>Исключений нет: испорченная поза чинится, а не заносится в список.</para>
    /// </summary>
    public class HandPoseDescriptorSanityTests
    {
        private const float AxisTolerance       = 0.1f;
        private const float MirrorMeanLimitDeg  = 35f;
        private const float MirrorNodeLimitDeg  = 90f;
        private const int   MaxDefectsPerRecord = 3;

        /// <summary>База семейства Cyborg — <c>CyborgHandsBase.prefab</c>.</summary>
        private const string CyborgHandsBaseGuid = "71b74b0d08444f49bbb7aa2828d0890a";

        /// <summary>Пальцы, у которых у кисти Cyborg есть пястная кость.</summary>
        private static readonly UxrFingerType[] MetacarpalFingers =
        {
            UxrFingerType.Index, UxrFingerType.Middle, UxrFingerType.Ring, UxrFingerType.Little
        };

        private static readonly UxrFingerType[] Fingers =
        {
            UxrFingerType.Thumb, UxrFingerType.Index, UxrFingerType.Middle, UxrFingerType.Ring, UxrFingerType.Little
        };

        private sealed class PoseRecord
        {
            public string           Path;
            public UxrHandPoseAsset Pose;
        }

        private List<PoseRecord> _poses;

        [OneTimeSetUp]
        public void Загрузить_позы()
        {
            _poses = AssetDatabase.FindAssets("t:" + nameof(UxrHandPoseAsset), new[] { "Assets" })
                                  .Select(AssetDatabase.GUIDToAssetPath)
                                  .Distinct()
                                  .OrderBy(p => p)
                                  .Select(p => new PoseRecord { Path = p, Pose = AssetDatabase.LoadAssetAtPath<UxrHandPoseAsset>(p) })
                                  .Where(r => r.Pose != null)
                                  .ToList();

            Assert.That(_poses, Is.Not.Empty, "Не найдено ни одной UxrHandPoseAsset в Assets");
        }

        [Test]
        public void Оси_узлов_дескрипторов_не_вырождены()
        {
            var failures = new List<string>();

            foreach (PoseRecord record in _poses)
            {
                foreach (var (label, left, right) in Descriptors(record.Pose))
                {
                    foreach (var (side, descriptor) in new[] { ("Left", left), ("Right", right) })
                    {
                        if (descriptor == null)
                        {
                            failures.Add($"{record.Path} {label} {side}: дескриптор отсутствует");
                            continue;
                        }

                        List<string> defects = Nodes(descriptor)
                                               .Select(n => (n.Name, Defect: NodeDefect(n.Node), n.Node))
                                               .Where(n => n.Defect != null)
                                               .Select(n => $"{n.Name}: {n.Defect}; right {n.Node.Right.ToString("F3")}, " +
                                                            $"up {n.Node.Up.ToString("F3")}, forward {n.Node.Forward.ToString("F3")}")
                                               .ToList();

                        if (defects.Count > 0)
                        {
                            failures.Add($"{record.Path} {label} {side}: вырожденных узлов {defects.Count} — " +
                                         string.Join("; ", defects.Take(MaxDefectsPerRecord)) +
                                         (defects.Count > MaxDefectsPerRecord ? "; …" : ""));
                        }
                    }
                }
            }

            Assert.IsEmpty(failures, "Вырожденные оси узлов поз (починка — «Copy Left To Right»/«Copy Right To Left» в редакторе поз):\n" +
                                     string.Join("\n", failures));
        }

        [Test]
        public void Правая_рука_близка_к_зеркалу_левой()
        {
            var failures = new List<string>();

            foreach (PoseRecord record in _poses)
            {
                foreach (var (label, left, right) in Descriptors(record.Pose))
                {
                    if (left == null || right == null)
                    {
                        continue; // отсутствие дескриптора сообщает тест осей
                    }

                    UxrHandDescriptor mirrored = left.Mirrored();
                    var angles = new List<(string Name, float Angle)>();
                    var problems = new List<string>();

                    foreach (UxrFingerType fingerType in Fingers)
                    {
                        UxrFingerDescriptor m = mirrored.GetFinger(fingerType);
                        UxrFingerDescriptor r = right.GetFinger(fingerType);

                        if (m.HasMetacarpalInfo != r.HasMetacarpalInfo)
                        {
                            problems.Add($"{fingerType}: HasMetacarpalInfo левой {m.HasMetacarpalInfo}, правой {r.HasMetacarpalInfo}");
                        }

                        if (m.HasMetacarpalInfo && r.HasMetacarpalInfo)
                        {
                            angles.Add(($"{fingerType}.Metacarpal", NodeAngle(m.Metacarpal, r.Metacarpal)));
                        }

                        angles.Add(($"{fingerType}.Proximal",             NodeAngle(m.Proximal,             r.Proximal)));
                        angles.Add(($"{fingerType}.ProximalNoMetacarpal", NodeAngle(m.ProximalNoMetacarpal, r.ProximalNoMetacarpal)));
                        angles.Add(($"{fingerType}.Intermediate",         NodeAngle(m.Intermediate,         r.Intermediate)));
                        angles.Add(($"{fingerType}.Distal",               NodeAngle(m.Distal,               r.Distal)));
                    }

                    float mean = angles.Average(a => a.Angle);
                    var   max  = angles.OrderByDescending(a => a.Angle).First();

                    if (mean > MirrorMeanLimitDeg || max.Angle > MirrorNodeLimitDeg)
                    {
                        problems.Add($"среднее {mean:0.0}° (порог {MirrorMeanLimitDeg:0}°), максимум {max.Angle:0.0}° у {max.Name} " +
                                     $"(порог {MirrorNodeLimitDeg:0}°)");
                    }

                    if (problems.Count > 0)
                    {
                        failures.Add($"{record.Path} {label}: " + string.Join("; ", problems));
                    }
                }
            }

            Assert.IsEmpty(failures, "Правая рука расходится с зеркалом левой (формула UxrHandDescriptor.Mirrored):\n" +
                                     string.Join("\n", failures));
        }

        /// <summary>
        /// Каждая поза типа Fixed, зарегистрированная в самом <c>CyborgHandsBase</c> (<see cref="UxrAvatar.GetHandPoses"/>, без
        /// наследованных от базы), записана с пястными у пальцев Index, Middle, Ring, Little обеих рук
        /// (<see cref="UxrFingerDescriptor.HasMetacarpalInfo"/>).
        ///
        /// <para>
        /// Причина — дефект G6 (<c>Docs/grip-known-issues.md</c>): у кисти Cyborg пястные есть, и поза без них в рантайме SDK
        /// выгибает пальцы 2–5 назад (~179°), хотя превью в редакторе выглядит верно — статический Fit дефект почти не видит.
        /// Большой палец не проверяется: дефект G6 — у пальцев 2–5.
        /// </para>
        /// </summary>
        [Test]
        public void Позы_кисти_Cyborg_с_пястными()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(CyborgHandsBaseGuid));
            Assert.IsNotNull(prefab, $"Не найден префаб CyborgHandsBase (GUID {CyborgHandsBaseGuid}).");
            var avatar = prefab.GetComponent<UxrAvatar>();
            Assert.IsNotNull(avatar, "На CyborgHandsBase нет UxrAvatar.");

            List<UxrHandPoseAsset> poses = avatar.GetHandPoses().Where(p => p.PoseType == UxrHandPoseType.Fixed).ToList();
            Assert.That(poses, Is.Not.Empty, "У CyborgHandsBase не зарегистрировано ни одной позы Fixed.");

            var failures = new List<string>();
            foreach (UxrHandPoseAsset pose in poses)
            {
                foreach (var (side, descriptor) in new[] { ("Left", pose.HandDescriptorLeft), ("Right", pose.HandDescriptorRight) })
                {
                    if (descriptor == null)
                    {
                        failures.Add($"{AssetDatabase.GetAssetPath(pose)} {side}: дескриптор отсутствует");
                        continue;
                    }

                    List<UxrFingerType> missing = MetacarpalFingers.Where(f => !descriptor.GetFinger(f).HasMetacarpalInfo).ToList();
                    if (missing.Count > 0)
                    {
                        failures.Add($"{AssetDatabase.GetAssetPath(pose)} {side}: без пястных у {string.Join(", ", missing)}");
                    }
                }
            }

            Assert.IsEmpty(failures, "Позы CyborgHandsBase без пястных — в рантайме пальцы 2–5 вывернутся (G6); " +
                                     "перезаписать позу на кисти Cyborg с пястными:\n" + string.Join("\n", failures));
        }

        private static IEnumerable<(string Label, UxrHandDescriptor Left, UxrHandDescriptor Right)> Descriptors(UxrHandPoseAsset pose)
        {
            switch (pose.PoseType)
            {
                case UxrHandPoseType.Fixed:
                    yield return ("Fixed", pose.HandDescriptorLeft, pose.HandDescriptorRight);
                    break;

                case UxrHandPoseType.Blend:
                    yield return ("Open", pose.HandDescriptorOpenLeft, pose.HandDescriptorOpenRight);
                    yield return ("Closed", pose.HandDescriptorClosedLeft, pose.HandDescriptorClosedRight);
                    break;
            }
        }

        /// <summary>Узлы дескриптора; пястная — только при <see cref="UxrFingerDescriptor.HasMetacarpalInfo"/>.</summary>
        private static IEnumerable<(string Name, UxrFingerNodeDescriptor Node)> Nodes(UxrHandDescriptor descriptor)
        {
            foreach (UxrFingerType fingerType in Fingers)
            {
                UxrFingerDescriptor finger = descriptor.GetFinger(fingerType);

                if (finger.HasMetacarpalInfo)
                {
                    yield return ($"{fingerType}.Metacarpal", finger.Metacarpal);
                }

                yield return ($"{fingerType}.Proximal",             finger.Proximal);
                yield return ($"{fingerType}.ProximalNoMetacarpal", finger.ProximalNoMetacarpal);
                yield return ($"{fingerType}.Intermediate",         finger.Intermediate);
                yield return ($"{fingerType}.Distal",               finger.Distal);
            }
        }

        /// <summary>Копия <c>UxrHandPoseEditorWindow.GetFingerNodeDescriptorDefect</c> (патч SDK 93): причина или null.</summary>
        private static string NodeDefect(UxrFingerNodeDescriptor node)
        {
            Vector3 right   = node.Right;
            Vector3 up      = node.Up;
            Vector3 forward = node.Forward;

            if (Mathf.Abs(right.magnitude - 1f) > AxisTolerance || Mathf.Abs(up.magnitude - 1f) > AxisTolerance ||
                Mathf.Abs(forward.magnitude - 1f) > AxisTolerance)
            {
                return $"длины осей {right.magnitude:0.000}/{up.magnitude:0.000}/{forward.magnitude:0.000} вместо 1";
            }

            if (Mathf.Abs(Vector3.Dot(right, up)) > AxisTolerance || Mathf.Abs(Vector3.Dot(up, forward)) > AxisTolerance ||
                Mathf.Abs(Vector3.Dot(right, forward)) > AxisTolerance)
            {
                return $"оси не ортогональны (right·up {Vector3.Dot(right, up):0.000}, up·forward {Vector3.Dot(up, forward):0.000}, " +
                       $"right·forward {Vector3.Dot(right, forward):0.000})";
            }

            if ((Vector3.Cross(right, up) - forward).magnitude > AxisTolerance)
            {
                return $"тройка осей не правая (|right × up − forward| = {(Vector3.Cross(right, up) - forward).magnitude:0.000})";
            }

            return null;
        }

        /// <summary>Наибольший угол между одноимёнными осями двух узлов; нулевой вектор — разворот на 180°.</summary>
        private static float NodeAngle(UxrFingerNodeDescriptor a, UxrFingerNodeDescriptor b)
        {
            return Mathf.Max(AxisAngle(a.Right, b.Right), AxisAngle(a.Up, b.Up), AxisAngle(a.Forward, b.Forward));
        }

        private static float AxisAngle(Vector3 a, Vector3 b)
        {
            // Vector3.Angle возвращает 0 для нулевого вектора — здесь это худший случай, а не совпадение
            if (a.sqrMagnitude < 1e-12f || b.sqrMagnitude < 1e-12f)
            {
                return 180f;
            }

            return Vector3.Angle(a, b);
        }
    }
}
