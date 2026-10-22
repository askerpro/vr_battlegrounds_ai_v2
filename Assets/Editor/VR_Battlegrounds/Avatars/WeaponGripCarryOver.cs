using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateXR.Manipulation;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>Откуда пересборка ствола берёт позы и записи хвата.</summary>
    public enum WeaponGripPoseMode
    {
        /// <summary>
        /// По умолчанию. Импорт поз из клипов не вызывается: записи хвата всех аватаров (точки оружия, затвор,
        /// магазин) и их трансформы выравнивания переносятся из текущего префаба как есть.
        /// </summary>
        KeepCurrent,

        /// <summary>
        /// Позы MEF заново снимаются с клипа пака (<see cref="HandsPackPoseImporter" />). Записи остальных аватаров
        /// (Cyborg доводится руками) переносятся из текущего префаба. Сборщики оружия этот режим больше не принимают
        /// (<see cref="WeaponGripCarryOver.RequireBuilderMode" />): регистрация поз и хватов аватаров — отдельная команда.
        /// </summary>
        ReimportFromPack
    }

    /// <summary>
    /// Перенос хватов через пересборку ствола. Сборщик из пака пишет префаб заново, поэтому записи хвата
    /// (<c>_avatarGripPoseEntries</c>) и трансформы выравнивания снимаются с текущего префаба до сборки
    /// (<see cref="Capture" />) и возвращаются после неё (<see cref="Restore" />).
    ///
    /// <para>
    /// Переносятся записи и их трансформы с локальной позой относительно родителя. Недостающие узлы цепочки
    /// создаются с прежней позой. Прочие поля точек хвата остаются от сборщика, существующие узлы геометрии
    /// не двигаются. Записи переносимых аватаров заменяют записи сборщика, порядок записей — как в прежнем
    /// префабе. Ставшие ненужными трансформы сборщика удаляются (<see cref="HandsPackPoseImporter.RemoveUnused" />).
    /// </para>
    /// </summary>
    public static class WeaponGripCarryOver
    {
        /// <summary>Хваты префабов до пересборки.</summary>
        public sealed class Snapshot
        {
            internal readonly List<PrefabGrips> Prefabs = new List<PrefabGrips>();
        }

        internal sealed class PrefabGrips
        {
            public string Path;
            public readonly List<GrabbableGrips> Grabbables = new List<GrabbableGrips>();
            public readonly Dictionary<string, Node> Nodes = new Dictionary<string, Node>();
        }

        internal sealed class GrabbableGrips
        {
            public string Path;
            public GameObject SelectedAvatar;
            public readonly List<List<Entry>> Points = new List<List<Entry>>();
        }

        internal sealed class Entry
        {
            public string AvatarGuid;
            public UxrHandPoseAsset Pose;
            public float Blend;
            public string LeftPath, RightPath; // прежний префаб: путь от корня
            public Transform Left, Right;      // текущий префаб: ссылка
        }

        internal sealed class Node
        {
            public Vector3 Position, Scale;
            public Quaternion Rotation;
            public int Sibling, Layer;
            public bool Active, Snap;
        }

        /// <summary>
        /// Сборщик оружия из паков не добавляет и не регистрирует позы и хваты аватаров (решение пользователя 2026-10-22):
        /// записи хвата переносятся как есть, сборка готовит только исходные позы <c>_orig</c> (<see cref="PackOrigPoses" />).
        /// Перезапись поз MEF из пака — явная команда (<see cref="HandsPackPoseImporter" />, Grip Extraction).
        /// </summary>
        public static void RequireBuilderMode(WeaponGripPoseMode mode, string builder)
        {
            if (mode != WeaponGripPoseMode.KeepCurrent)
                throw new InvalidOperationException($"{builder}: сборка не регистрирует позы аватаров (режим {mode}). " +
                                                    "Хваты MEF из поз пака — отдельная команда «Зарегистрировать хваты для аватара MEF».");
        }

        /// <summary>
        /// Снимает хваты с существующих префабов (оружие и его магазин). Отсутствующий путь пропускается: первая
        /// сборка ствола переносить нечего.
        /// </summary>
        public static Snapshot Capture(params string[] prefabPaths)
        {
            var snapshot = new Snapshot();
            foreach (string path in prefabPaths.Where(p => !string.IsNullOrEmpty(p) && AssetDatabase.LoadAssetAtPath<GameObject>(p) != null))
            {
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var prefab = new PrefabGrips { Path = path };
                    foreach (UxrGrabbableObject grabbable in OwnGrabbables(root))
                    {
                        var grips = new GrabbableGrips { Path = PathOf(root.transform, grabbable.transform), SelectedAvatar = SelectedAvatar(grabbable) };
                        for (int i = 0; i < grabbable.GrabPointCount; i++)
                        {
                            grips.Points.Add(grabbable.GetGrabPoint(i).AvatarGripPoseEntries.Select(e => new Entry
                            {
                                AvatarGuid = e.AvatarPrefabGuid, Pose = e.HandPose, Blend = e.PoseBlendValue,
                                LeftPath = Record(prefab, root.transform, e.GripAlignTransformHandLeft),
                                RightPath = Record(prefab, root.transform, e.GripAlignTransformHandRight)
                            }).ToList());
                        }

                        prefab.Grabbables.Add(grips);
                    }

                    snapshot.Prefabs.Add(prefab);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            return snapshot;
        }

        /// <summary>
        /// Возвращает хваты в пересобранные префабы. <see cref="WeaponGripPoseMode.KeepCurrent" /> переносит записи
        /// всех аватаров. <see cref="WeaponGripPoseMode.ReimportFromPack" /> переносит все, кроме аватаров импортёра поз:
        /// их записи остаются от сборки и импорта.
        /// </summary>
        public static void Restore(Snapshot snapshot, WeaponGripPoseMode mode)
        {
            HashSet<string> rewritten = mode == WeaponGripPoseMode.ReimportFromPack ? HandsPackPoseImporter.GripAvatarGuidSet() : new HashSet<string>();
            foreach (PrefabGrips prefab in snapshot.Prefabs)
            {
                GameObject root = PrefabUtility.LoadPrefabContents(prefab.Path);
                var replaced = new List<Transform>();
                int carried = 0;
                try
                {
                    foreach (GrabbableGrips grips in prefab.Grabbables)
                    {
                        Transform host = grips.Path.Length == 0 ? root.transform : root.transform.Find(grips.Path);
                        var grabbable = host != null ? host.GetComponent<UxrGrabbableObject>() : null;
                        if (grabbable == null)
                            throw new InvalidOperationException($"{prefab.Path}: после сборки нет захватываемого «{grips.Path}» — хваты не перенести");
                        if (grabbable.GrabPointCount != grips.Points.Count)
                            throw new InvalidOperationException($"{prefab.Path}/{grips.Path}: точек хвата {grabbable.GrabPointCount}, было {grips.Points.Count}");

                        var so = new SerializedObject(grabbable);
                        for (int i = 0; i < grips.Points.Count; i++)
                        {
                            List<Entry> current = grabbable.GetGrabPoint(i).AvatarGripPoseEntries.Select(e => new Entry
                            {
                                AvatarGuid = e.AvatarPrefabGuid, Pose = e.HandPose, Blend = e.PoseBlendValue,
                                Left = e.GripAlignTransformHandLeft, Right = e.GripAlignTransformHandRight
                            }).ToList();
                            var result = new List<Entry>();
                            foreach (Entry old in grips.Points[i])
                            {
                                Entry fresh = current.FirstOrDefault(e => e.AvatarGuid == old.AvatarGuid);
                                if (rewritten.Contains(old.AvatarGuid) && fresh != null)
                                {
                                    result.Add(fresh);
                                    continue;
                                }

                                result.Add(new Entry
                                {
                                    AvatarGuid = old.AvatarGuid, Pose = old.Pose, Blend = old.Blend,
                                    Left = Ensure(prefab, root.transform, old.LeftPath), Right = Ensure(prefab, root.transform, old.RightPath)
                                });
                                carried++;
                            }

                            // Аватары, которых в прежнем префабе не было, — от сборки, в конце списка.
                            result.AddRange(current.Where(e => grips.Points[i].All(o => o.AvatarGuid != e.AvatarGuid)));
                            replaced.AddRange(current.Where(e => !result.Contains(e)).SelectMany(e => new[] { e.Left, e.Right }));
                            Write(so.FindProperty(i == 0 ? "_grabPoint._avatarGripPoseEntries" : $"_additionalGrabPoints.Array.data[{i - 1}]._avatarGripPoseEntries"), result);
                        }

                        so.FindProperty("_selectedAvatarForGrips").objectReferenceValue = grips.SelectedAvatar;
                        so.ApplyModifiedPropertiesWithoutUndo();
                    }

                    // Трансформы сборщика, на которые больше не ссылается ни одна запись, — не два места одного хвата.
                    foreach (Transform old in replaced.Where(t => t != null).Distinct().ToList())
                    {
                        if (old != null) HandsPackPoseImporter.RemoveUnused(root, old, null);
                    }

                    RestoreSiblingOrder(prefab, root.transform);
                    PrefabUtility.SaveAsPrefabAsset(root, prefab.Path);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }

                // SaveAsPrefabAsset пишет _assetId = 0 — вернуть канон (NetworkAssetIdOnDiskTests).
                VrBattlegrounds.EditorTools.VersionControl.NetworkAssetIdNormalizer.Normalize(new[] { prefab.Path }, false);
                GameLog.Debug.Info($"[WeaponGripCarryOver] {prefab.Path}: перенесено записей хвата {carried} ({mode})");
            }
        }

        /// <summary>Захватываемые самого префаба: магазин в гнезде — вложенный префаб со своим снимком.</summary>
        private static IEnumerable<UxrGrabbableObject> OwnGrabbables(GameObject root) =>
            root.GetComponentsInChildren<UxrGrabbableObject>(true)
                .Where(g => g.GetComponentsInParent<UxrGrabbableObjectAnchor>(true).Length == 0);

        private static GameObject SelectedAvatar(UxrGrabbableObject grabbable) =>
            new SerializedObject(grabbable).FindProperty("_selectedAvatarForGrips").objectReferenceValue as GameObject;

        private static string PathOf(Transform root, Transform t)
        {
            var names = new List<string>();
            for (Transform p = t; p != null && p != root; p = p.parent) names.Insert(0, p.name);
            return string.Join("/", names);
        }

        /// <summary>
        /// Путь узла с номером среди одноимённых братьев (<c>имя␁номер</c>): у варианта магазина бывают два
        /// «Grab Left (MEF_cyborg_hands)» — от базы и свой.
        /// </summary>
        private static string NodePath(Transform root, Transform t)
        {
            var segments = new List<string>();
            for (Transform p = t; p != null && p != root; p = p.parent)
            {
                int ordinal = p.parent.Cast<Transform>().TakeWhile(c => c != p).Count(c => c.name == p.name);
                segments.Insert(0, p.name + Separator + ordinal);
            }

            return string.Join("/", segments);
        }

        private const char Separator = '\u0001';

        /// <summary>Запоминает трансформ выравнивания и цепочку его родителей; возвращает путь от корня.</summary>
        private static string Record(PrefabGrips prefab, Transform root, Transform align)
        {
            if (align == null) return null;
            if (!align.IsChildOf(root))
                throw new InvalidOperationException($"{prefab.Path}: трансформ выравнивания {align.name} вне префаба");
            for (Transform t = align; t != root; t = t.parent)
            {
                string path = NodePath(root, t);
                if (prefab.Nodes.ContainsKey(path)) continue;
                prefab.Nodes[path] = new Node
                {
                    Position = t.localPosition, Rotation = t.localRotation, Scale = t.localScale, Sibling = t.GetSiblingIndex(),
                    Layer = t.gameObject.layer, Active = t.gameObject.activeSelf, Snap = t.GetComponent<UxrGrabbableObjectSnapTransform>() != null
                };
            }

            return NodePath(root, align);
        }

        /// <summary>
        /// Трансформ по прежнему пути: недостающие узлы создаются с прежней позой. Поза листа (самого трансформа
        /// выравнивания) ставится прежняя, существующие узлы геометрии над ним не трогаются.
        /// </summary>
        private static Transform Ensure(PrefabGrips prefab, Transform root, string path)
        {
            if (path == null) return null;
            Transform parent = root;
            string[] names = path.Split('/');
            for (int i = 0; i < names.Length; i++)
            {
                string nodePath = string.Join("/", names, 0, i + 1);
                Node node = prefab.Nodes[nodePath];
                int cut = names[i].LastIndexOf(Separator);
                string name = names[i].Substring(0, cut);
                int ordinal = int.Parse(names[i].Substring(cut + 1));
                List<Transform> namesakes = parent.Cast<Transform>().Where(c => c.name == name).ToList();
                Transform child = ordinal < namesakes.Count ? namesakes[ordinal] : null;
                bool leaf = i == names.Length - 1;
                if (child == null)
                {
                    child = new GameObject(name) { layer = node.Layer }.transform;
                    child.SetParent(parent, false);
                    child.SetSiblingIndex(Mathf.Min(node.Sibling, parent.childCount - 1));
                    child.gameObject.SetActive(node.Active);
                    leaf = true; // созданный узел целиком прежний
                }

                if (leaf)
                {
                    child.SetLocalPositionAndRotation(node.Position, node.Rotation);
                    child.localScale = node.Scale;
                }

                if (node.Snap && child.GetComponent<UxrGrabbableObjectSnapTransform>() == null) child.gameObject.AddComponent<UxrGrabbableObjectSnapTransform>();
                parent = child;
            }

            return parent;
        }

        /// <summary>
        /// Прежний порядок среди братьев — только у узлов хвата (в поддереве лишь Transform и snap-трансформы):
        /// узлы геометрии сборщика не трогаются. Сборка добавляет и убирает детей корня, созданная папка съезжает.
        /// </summary>
        private static void RestoreSiblingOrder(PrefabGrips prefab, Transform root)
        {
            foreach (KeyValuePair<string, Node> pair in prefab.Nodes.OrderBy(p => p.Value.Sibling))
            {
                Transform node = Resolve(root, pair.Key);
                if (node == null || node.parent.childCount <= pair.Value.Sibling) continue;
                // Узлы базы варианта (магазин) переставлять нельзя — только свои.
                if (PrefabUtility.IsPartOfPrefabInstance(node.gameObject) && !PrefabUtility.IsAddedGameObjectOverride(node.gameObject)) continue;
                bool gripOnly = node.GetComponentsInChildren<Component>(true)
                                    .All(c => c is Transform || c is UxrGrabbableObjectSnapTransform);
                if (gripOnly) node.SetSiblingIndex(pair.Value.Sibling);
            }
        }

        private static Transform Resolve(Transform root, string path)
        {
            Transform current = root;
            foreach (string segment in path.Split('/'))
            {
                int cut = segment.LastIndexOf(Separator);
                string name = segment.Substring(0, cut);
                int ordinal = int.Parse(segment.Substring(cut + 1));
                current = current.Cast<Transform>().Where(c => c.name == name).ElementAtOrDefault(ordinal);
                if (current == null) return null;
            }

            return current;
        }

        private static void Write(SerializedProperty list, List<Entry> entries)
        {
            list.arraySize = entries.Count;
            for (int i = 0; i < entries.Count; i++)
            {
                SerializedProperty element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("_avatarPrefabGuid").stringValue          = entries[i].AvatarGuid;
                element.FindPropertyRelative("_handPose").objectReferenceValue         = entries[i].Pose;
                element.FindPropertyRelative("_poseBlendValue").floatValue             = entries[i].Blend;
                element.FindPropertyRelative("_gripAlignTransformHandLeft").objectReferenceValue  = entries[i].Left;
                element.FindPropertyRelative("_gripAlignTransformHandRight").objectReferenceValue = entries[i].Right;
            }
        }
    }
}
