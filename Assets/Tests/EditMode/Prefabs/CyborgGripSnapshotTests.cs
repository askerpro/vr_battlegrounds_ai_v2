using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UltimateXR.Manipulation;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Tests.Prefabs
{
    /// <summary>
    /// Снимок принятых хватов семейства Cyborg: все записи хвата <c>CyborgHandsBase</c> (GUID <see cref="AvatarGuid" />)
    /// на префабах <c>Assets/Prefabs/Weapons</c> совпадают с <c>CyborgGripSnapshot.json</c>.
    ///
    /// <para>
    /// <b>Зачем.</b> Хваты Cyborg приняты пользователем в шлеме 2026-10-24. Пересборка ствола, перенос записей между
    /// аватарами или правка трансформа выравнивания молча сдвигают ладонь на оружии, и заметно это только в шлеме.
    /// Снимок ловит любое такое изменение: каждая запись (префаб + путь хватаемого объекта от корня префаба + индекс точки
    /// хвата) сравнивается по GUID позы, значению смешивания позы и положению/повороту трансформов выравнивания левой и
    /// правой руки в пространстве корня префаба. Допуски — <see cref="PositionToleranceMm" /> мм и
    /// <see cref="AngleToleranceDeg" />°; лишние и пропавшие записи — тоже расхождение.
    /// </para>
    ///
    /// <para>
    /// <b>Обновление снимка.</b> Только осознанно, после того как изменённый хват принят в шлеме: запустить явный тест
    /// <see cref="Записать_снимок_хватов" /> (Test Runner, EditMode, выбрать тест и Run Selected — <c>[Explicit]</c>
    /// не идёт в общем прогоне) и закоммитить изменённый JSON вместе с правкой хвата. Пустой снимок — сравнение
    /// Inconclusive до первой записи.
    /// </para>
    /// </summary>
    public class CyborgGripSnapshotTests
    {
        private const string AvatarGuid    = "71b74b0d08444f49bbb7aa2828d0890a";
        private const string WeaponsFolder = "Assets/Prefabs/Weapons";
        private const string SnapshotPath  = "Assets/Tests/EditMode/Prefabs/CyborgGripSnapshot.json";

        private const float PositionToleranceMm = 0.5f;
        private const float AngleToleranceDeg   = 0.5f;
        private const float BlendTolerance      = 0.001f;
        private const int   MaxReportedDiffs    = 60;

        [Serializable]
        private sealed class Snapshot
        {
            public string      avatarGuid;
            public List<Entry> entries = new List<Entry>();
        }

        [Serializable]
        private sealed class Entry
        {
            public string     key;
            public string     poseGuid;
            public string     poseName; // для чтения диффа; сравнивается GUID
            public float      blend;
            public bool       hasLeft;
            public Vector3    leftPositionMm;
            public Quaternion leftRotation;
            public bool       hasRight;
            public Vector3    rightPositionMm;
            public Quaternion rightRotation;
        }

        [Test]
        public void Хваты_CyborgHandsBase_совпадают_со_снимком()
        {
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(SnapshotPath);
            Assert.IsNotNull(asset, $"Нет снимка {SnapshotPath}.");
            Snapshot expected = JsonUtility.FromJson<Snapshot>(asset.text);
            if (expected == null || expected.entries == null || expected.entries.Count == 0)
            {
                Assert.Inconclusive("Снимок пуст — выполнить Записать_снимок_хватов.");
            }
            Assert.That(expected.avatarGuid, Is.EqualTo(AvatarGuid), "Снимок записан не для CyborgHandsBase.");

            Dictionary<string, Entry> actual = Collect().entries.ToDictionary(e => e.key);
            Dictionary<string, Entry> golden = expected.entries.ToDictionary(e => e.key);

            var diffs = new List<string>();
            foreach (string key in golden.Keys.Where(k => !actual.ContainsKey(k)).OrderBy(k => k))
                diffs.Add($"пропала запись: {key}");
            foreach (string key in actual.Keys.Where(k => !golden.ContainsKey(k)).OrderBy(k => k))
                diffs.Add($"новая запись: {key}");
            foreach (string key in golden.Keys.Where(actual.ContainsKey).OrderBy(k => k))
                diffs.AddRange(Compare(golden[key], actual[key]).Select(d => $"{key}: {d}"));

            if (diffs.Count > 0)
            {
                Assert.Fail($"Хваты CyborgHandsBase расходятся со снимком ({diffs.Count}):\n" +
                            string.Join("\n", diffs.Take(MaxReportedDiffs)) + (diffs.Count > MaxReportedDiffs ? "\n…" : "") +
                            "\nЕсли изменение хвата принято в шлеме — перезаписать снимок явным тестом Записать_снимок_хватов.");
            }
        }

        /// <summary>
        /// Перезаписывает <c>CyborgGripSnapshot.json</c> по текущим префабам. Явный: запускать только сознательно, после
        /// принятой в шлеме правки хвата, и коммитить JSON вместе с ней.
        /// </summary>
        [Test]
        [Explicit("Перезаписывает снимок хватов — только после принятой в шлеме правки хвата.")]
        public void Записать_снимок_хватов()
        {
            Snapshot snapshot = Collect();
            Assert.That(snapshot.entries, Is.Not.Empty, $"Не найдено ни одной записи хвата CyborgHandsBase в {WeaponsFolder}.");

            string json = JsonUtility.ToJson(snapshot, true).Replace("\r\n", "\n") + "\n";
            File.WriteAllText(SnapshotPath, json, new UTF8Encoding(false));
            AssetDatabase.ImportAsset(SnapshotPath, ImportAssetOptions.ForceUpdate);
            TestContext.Out.WriteLine($"Записано {snapshot.entries.Count} записей хвата в {SnapshotPath}.");
        }

        private static IEnumerable<string> Compare(Entry golden, Entry actual)
        {
            if (golden.poseGuid != actual.poseGuid)
                yield return $"поза {golden.poseName} ({golden.poseGuid}) → {actual.poseName} ({actual.poseGuid})";
            if (Mathf.Abs(golden.blend - actual.blend) > BlendTolerance)
                yield return $"смешивание позы {golden.blend:0.###} → {actual.blend:0.###}";

            foreach (string d in CompareHand("левая", golden.hasLeft, golden.leftPositionMm, golden.leftRotation,
                                             actual.hasLeft, actual.leftPositionMm, actual.leftRotation))
                yield return d;
            foreach (string d in CompareHand("правая", golden.hasRight, golden.rightPositionMm, golden.rightRotation,
                                             actual.hasRight, actual.rightPositionMm, actual.rightRotation))
                yield return d;
        }

        private static IEnumerable<string> CompareHand(string side, bool hadAlign, Vector3 goldenPos, Quaternion goldenRot,
                                                       bool hasAlign, Vector3 actualPos, Quaternion actualRot)
        {
            if (hadAlign != hasAlign)
            {
                yield return $"{side}: трансформ выравнивания {(hadAlign ? "был" : "не был")}, теперь {(hasAlign ? "есть" : "нет")}";
                yield break;
            }
            if (!hasAlign) yield break;

            float distance = Vector3.Distance(goldenPos, actualPos);
            if (distance > PositionToleranceMm)
                yield return $"{side}: выравнивание сдвинуто на {distance:0.00} мм ({goldenPos.ToString("F2")} → {actualPos.ToString("F2")} мм)";
            float angle = Quaternion.Angle(goldenRot, actualRot);
            if (angle > AngleToleranceDeg)
                yield return $"{side}: выравнивание повёрнуто на {angle:0.00}° ({goldenRot.eulerAngles.ToString("F1")} → {actualRot.eulerAngles.ToString("F1")})";
        }

        /// <summary>Все записи хвата <see cref="AvatarGuid" /> (собственные, без наследования) на префабах оружия.</summary>
        private static Snapshot Collect()
        {
            var snapshot = new Snapshot { avatarGuid = AvatarGuid };
            var keys = new HashSet<string>();

            IEnumerable<string> prefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] { WeaponsFolder })
                                                           .Select(AssetDatabase.GUIDToAssetPath)
                                                           .Distinct()
                                                           .OrderBy(p => p, StringComparer.Ordinal);
            foreach (string prefabPath in prefabPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null) continue;
                Transform root = prefab.transform;

                foreach (UxrGrabbableObject grabbable in prefab.GetComponentsInChildren<UxrGrabbableObject>(true))
                {
                    string objectPath = RelativePath(root, grabbable.transform);
                    for (int i = 0; i < grabbable.GrabPointCount; i++)
                    {
                        UxrGrabPointInfo point = grabbable.GetGrabPoint(i);
                        UxrGripPoseInfo grip = point?.GetGripPoseInfo(AvatarGuid);
                        if (grip == null) continue;

                        // Одноимённые соседи дают одинаковый путь — различаются порядковым номером в обходе иерархии.
                        string baseKey = $"{prefabPath}|{objectPath}|{i}";
                        string key = baseKey;
                        for (int n = 2; !keys.Add(key); n++) key = $"{baseKey}#{n}";

                        string posePath = grip.HandPose != null ? AssetDatabase.GetAssetPath(grip.HandPose) : null;
                        var entry = new Entry
                        {
                            key      = key,
                            poseGuid = string.IsNullOrEmpty(posePath) ? "" : AssetDatabase.AssetPathToGUID(posePath),
                            poseName = grip.HandPose != null ? grip.HandPose.name : "",
                            blend    = grip.PoseBlendValue,
                        };
                        entry.hasLeft  = Align(root, grip.GripAlignTransformHandLeft,  out entry.leftPositionMm,  out entry.leftRotation);
                        entry.hasRight = Align(root, grip.GripAlignTransformHandRight, out entry.rightPositionMm, out entry.rightRotation);
                        snapshot.entries.Add(entry);
                    }
                }
            }

            snapshot.entries.Sort((a, b) => string.CompareOrdinal(a.key, b.key));
            return snapshot;
        }

        /// <summary>Положение (мм) и поворот трансформа выравнивания в пространстве корня префаба; false — трансформа нет.</summary>
        private static bool Align(Transform root, Transform align, out Vector3 positionMm, out Quaternion rotation)
        {
            if (align == null)
            {
                positionMm = Vector3.zero;
                rotation   = Quaternion.identity;
                return false;
            }

            Vector3 local = root.InverseTransformPoint(align.position) * 1000f;
            positionMm = new Vector3(Round(local.x, 1000f), Round(local.y, 1000f), Round(local.z, 1000f));
            Quaternion q = Quaternion.Inverse(root.rotation) * align.rotation;
            rotation = new Quaternion(Round(q.x, 1e6f), Round(q.y, 1e6f), Round(q.z, 1e6f), Round(q.w, 1e6f));
            return true;
        }

        // Округление ради стабильного диффа JSON: 0,001 мм и 1e-6 компоненты кватерниона — много ниже допусков.
        private static float Round(float value, float scale) => Mathf.Round(value * scale) / scale;

        private static string RelativePath(Transform root, Transform target)
        {
            if (target == root) return ".";
            var names = new List<string>();
            for (Transform t = target; t != null && t != root; t = t.parent) names.Add(t.name);
            names.Reverse();
            return string.Join("/", names);
        }
    }
}
