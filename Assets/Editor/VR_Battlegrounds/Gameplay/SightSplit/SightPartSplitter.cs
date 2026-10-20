using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VrBattlegrounds.Weapons.Sights;

namespace VrBattlegrounds.Editor.Gameplay.SightSplit
{
    /// <summary>Что создал <see cref="SightPartSplitter.Apply"/> во временной копии префаба.</summary>
    public sealed class SightSplitApplyResult
    {
        public SightSplitSpec Spec;
        /// <summary>Узел → разделение его меша (только узлы с вынесенной деталью).</summary>
        public readonly Dictionary<Transform, SightMeshSplit> Splits = new Dictionary<Transform, SightMeshSplit>();
        /// <summary>Половина → её компонент (pivot на точке R/F).</summary>
        public readonly Dictionary<SightSplitPartSpec, SightAdjustablePart> Parts = new Dictionary<SightSplitPartSpec, SightAdjustablePart>();
        /// <summary>Половина → объект с мешем детали (только у вынесенной).</summary>
        public readonly Dictionary<SightSplitPartSpec, MeshRenderer> PartRenderers = new Dictionary<SightSplitPartSpec, MeshRenderer>();
        public readonly List<string> Warnings = new List<string>();

        /// <summary>Меши живут только в памяти: временная копия их не сохраняет.</summary>
        public void DestroyMeshes()
        {
            foreach (SightMeshSplit split in Splits.Values) split.DestroyMeshes();
        }
    }

    /// <summary>
    /// Разделитель вшитых прицелов: по <see cref="SightSplitTable"/> выносит подвижную деталь целика/мушки из меша
    /// узла в отдельный меш и отдельный объект с <see cref="SightAdjustablePart"/>, pivot которого стоит на точке R/F.
    /// Неподвижная половина получает только опорную точку (компонент с нулевым ходом).
    ///
    /// <para>
    /// Иерархия под узлом N с мешем: <c>N/Sight{Role}Frame/Sight{Role}[/Sight{Role}Mesh]</c>.
    /// Frame — рамка с осями родителя N и единичным мировым масштабом: деталь едет вместе с N (затвор, рамка), её
    /// локальная Y — вверх оружия, а ход и сдвиг — метры мира, как у комплектов SightKit. Деталь — нулевой поворот
    /// (требование WeaponSightAuthoring.Moving). Меш детали — дочерний объект с поворотом/масштабом N и вершинами,
    /// сдвинутыми на −точка, поэтому мировые позиции вершин не меняются.
    /// </para>
    ///
    /// <para>
    /// Сам разделитель на диск не пишет: <see cref="Apply"/> работает только с временной копией (содержимое префаба в
    /// preview-сцене), <see cref="Plan"/> — сухой прогон. Постоянное разделение (раунд 2, 2026-10-20) делает
    /// единственный писатель узлов прицела <c>WeaponSightAuthoring</c>: вызывает <see cref="Apply"/> на содержимом
    /// префаба и сохраняет меши по <see cref="MeshAssetPath"/>. Префаб, уже разделённый так, <see cref="Apply"/>
    /// сначала возвращает к источнику в копии (<see cref="RevertMigrated"/>): повторный прогон даёт тот же результат.
    /// </para>
    /// </summary>
    public static class SightPartSplitter
    {
        private const float PositionTolerance = 1e-5f;
        /// <summary>Папка game-owned мешей Kinemation (KinemationWeapon.ArtRoot; сборка редактора отсюда не видна).</summary>
        private const string KinemationArtRoot = "Assets/Art/Weapons/Kinemation/";
        private const float AngleTolerance = 1e-3f;

        // ── Меш ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Делит меш узла: подвижные половины узла выносятся, у неподвижных проверяются селекторы. Каждый селектор —
        /// ровно один остров, острова половин не пересекаются; иначе отказ. Чистая функция: на входе только меш.
        /// </summary>
        public static SightMeshSplit SplitMesh(Mesh source, IReadOnlyList<SightSplitPartSpec> parts, string context)
        {
            SightMeshSnapshot snapshot = SightMeshSnapshot.Read(source, context);
            var islands = new MeshIslands(snapshot.Positions, snapshot.SubmeshIndices);
            var split = new SightMeshSplit { Source = source, Islands = islands, DroppedSkin = snapshot.DroppedSkin };

            var used = new HashSet<MeshIslands.Island>();
            var selected = new Dictionary<SightSplitPartSpec, List<MeshIslands.Island>>();
            foreach (SightSplitPartSpec part in parts)
            {
                var list = new List<MeshIslands.Island>();
                foreach (SightIslandSelector selector in part.Selectors)
                {
                    MeshIslands.Island island = islands.Select(selector, $"{context} {part.Role}");
                    if (!used.Add(island)) throw new InvalidOperationException($"{context} {part.Role}: {island} выбран дважды");
                    list.Add(island);
                }
                selected[part] = list;
                if (!part.Extract) split.ReferenceIslands[part] = list;
            }

            var extracted = new HashSet<int>();
            try
            {
                foreach (SightSplitPartSpec part in parts.Where(p => p.Extract))
                {
                    var triangleIds = selected[part].SelectMany(i => i.TriangleIds).OrderBy(t => t).ToList();
                    extracted.UnionWith(triangleIds);
                    var sourceSubmeshes = triangleIds.Select(t => islands.TriangleSubmesh[t]).Distinct().OrderBy(s => s).ToArray();
                    var vertexMap = new Dictionary<int, int>();
                    var sourceVertices = triangleIds.SelectMany(t => new[] { islands.Indices[t * 3], islands.Indices[t * 3 + 1], islands.Indices[t * 3 + 2] })
                                                    .Distinct().OrderBy(v => v).ToArray();
                    for (int i = 0; i < sourceVertices.Length; i++) vertexMap[sourceVertices[i]] = i;
                    var indices = sourceSubmeshes.Select(s => triangleIds.Where(t => islands.TriangleSubmesh[t] == s)
                                                                         .SelectMany(t => new[] { vertexMap[islands.Indices[t * 3]], vertexMap[islands.Indices[t * 3 + 1]], vertexMap[islands.Indices[t * 3 + 2]] })
                                                                         .ToArray()).ToList();
                    split.Parts.Add(new SightPartMesh
                    {
                        Spec = part, SourceVertices = sourceVertices, SourceSubmeshes = sourceSubmeshes, Islands = selected[part],
                        Triangles = triangleIds.Count,
                        Mesh = snapshot.BuildCompact(sourceVertices, indices, part.PointMesh, $"{source.name}_Sight{part.Role}")
                    });
                }

                var bodyLists = Enumerable.Range(0, snapshot.SubmeshIndices.Count).Select(_ => new List<int>()).ToArray();
                for (int t = 0; t < islands.TriangleCount; t++)
                {
                    if (extracted.Contains(t)) continue;
                    var target = bodyLists[islands.TriangleSubmesh[t]];
                    target.Add(islands.Indices[t * 3]); target.Add(islands.Indices[t * 3 + 1]); target.Add(islands.Indices[t * 3 + 2]);
                }
                split.Body = snapshot.BuildWithAllVertices(bodyLists.Select(l => l.ToArray()).ToList(), source.name + "_SightSplit");

                int bodyTriangles = bodyLists.Sum(l => l.Count / 3);
                if (bodyTriangles + split.Parts.Sum(p => p.Triangles) != snapshot.TriangleCount)
                    throw new InvalidOperationException($"{context}: треугольники корпуса {bodyTriangles} + деталей {split.Parts.Sum(p => p.Triangles)} ≠ исходным {snapshot.TriangleCount}");
            }
            catch
            {
                split.DestroyMeshes();
                throw;
            }
            return split;
        }

        // ── Префаб (временная копия) ───────────────────────────────────────

        /// <summary>
        /// Разделяет прицел во временной копии префаба: только корень содержимого префаба в preview-сцене
        /// (<see cref="PrefabUtility.LoadPrefabContents"/>), не ассет и не объект открытой сцены. Меши — в памяти,
        /// ничего не сохраняется. Узлы вне прицела (включая ShotSource и его цепочку) сверяются до/после.
        /// </summary>
        public static SightSplitApplyResult Apply(GameObject root, SightSplitSpec spec)
        {
            if (root == null || spec == null) throw new ArgumentNullException(root == null ? nameof(root) : nameof(spec));
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Разделение прицела — только в Edit Mode");
            if (EditorUtility.IsPersistent(root) || !root.scene.IsValid() || !EditorSceneManager.IsPreviewScene(root.scene) || root.transform.parent != null)
                throw new InvalidOperationException($"{spec.WeaponId}: Apply работает только с временной копией (корень содержимого префаба в preview-сцене)");
            RevertMigrated(root, spec);
            if (root.GetComponentsInChildren<SightAdjustablePart>(true).Length > 0)
                throw new InvalidOperationException($"{spec.WeaponId}: в префабе уже есть SightAdjustablePart — повторное разделение или комплект");

            var raiseNodes = spec.Raises.Select(r => Find(root.transform, r.NodePath, spec)).ToArray();
            var before = root.GetComponentsInChildren<Transform>(true).Where(t => !raiseNodes.Contains(t))
                             .ToDictionary(t => t, t => (t.parent, t.localPosition, t.localRotation, t.localScale));

            var result = new SightSplitApplyResult { Spec = spec };
            try
            {
                for (int i = 0; i < spec.Raises.Count; i++) Raise(raiseNodes[i], spec.Raises[i], spec);

                foreach (var group in spec.Parts.GroupBy(p => p.NodePath))
                {
                    Transform node = Find(root.transform, group.Key, spec);
                    foreach (SightSplitPartSpec part in group)
                        if (node.Find(part.FrameName) != null)
                            throw new InvalidOperationException($"{spec.WeaponId}: у {group.Key} уже есть {part.FrameName}");
                    var parts = group.ToList();
                    if (parts.Any(p => p.Selectors.Count > 0))
                    {
                        MeshFilter filter = CheckSourceMesh(node, parts[0], spec);
                        SightMeshSplit split = SplitMesh(filter.sharedMesh, parts, $"{spec.WeaponId} {group.Key}");
                        result.Splits[node] = split;
                        if (split.Parts.Count > 0) filter.sharedMesh = split.Body;
                    }
                    foreach (SightSplitPartSpec part in parts)
                        CreatePart(node, part, result.Splits.TryGetValue(node, out var s) ? s.Parts.FirstOrDefault(p => p.Spec == part) : null, result, spec);
                }

                foreach (var pair in before)
                {
                    Transform t = pair.Key;
                    if (t == null || t.parent != pair.Value.parent || (t.localPosition - pair.Value.localPosition).sqrMagnitude > 0f
                        || t.localRotation != pair.Value.localRotation || t.localScale != pair.Value.localScale)
                        throw new InvalidOperationException($"{spec.WeaponId}: изменился узел вне прицела {(t != null ? t.name : "(удалён)")}");
                }

                SightSplitPartSpec moving = spec.MovingPart;
                if (moving == null) result.Warnings.Add("подвижной детали нет — «Свести» только покажет ошибку");
                else if (moving.RequiredShiftMm.HasValue && (moving.RequiredShiftMm.Value * 1e-3f < -moving.TravelDown || moving.RequiredShiftMm.Value * 1e-3f > moving.TravelUp))
                    result.Warnings.Add($"оценка сдвига {moving.RequiredShiftMm.Value:+0.000;-0.000} мм вне хода −{moving.TravelDown * 1e3f:0.0}…+{moving.TravelUp * 1e3f:0.0} мм");
                return result;
            }
            catch
            {
                result.DestroyMeshes();
                throw;
            }
        }

        /// <summary>
        /// Сухой прогон по ID ствола из <see cref="SightSplitTable"/>: загружает содержимое префаба во временную
        /// preview-сцену, выполняет <see cref="Apply"/> и выгружает без сохранения. Возвращает план: узлы, меши,
        /// острова, точки, ход, будущие пути ассетов и предупреждения. На диск ничего не пишет.
        /// </summary>
        public static string Plan(string weaponId)
        {
            SightSplitSpec spec = SightSplitTable.Get(weaponId);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(spec.PrefabPath) == null)
                throw new InvalidOperationException($"{weaponId}: нет префаба {spec.PrefabPath}");
            GameObject root = PrefabUtility.LoadPrefabContents(spec.PrefabPath);
            SightSplitApplyResult result = null;
            try
            {
                result = Apply(root, spec);
                return Describe(root, result);
            }
            finally
            {
                result?.DestroyMeshes();
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        /// <summary>Путь game-owned меша детали или корпуса узла (прежнее имя <see cref="MeshAssetPath"/>).</summary>
        public static string ProposedMeshPath(SightSplitSpec spec, SightSplitPartSpec part, bool body) => MeshAssetPath(spec, part, body);

        /// <summary>
        /// Путь game-owned меша: деталь — <c>{оружие}_Sight{Role}.asset</c>, корпус узла — <c>{оружие}_{узел}_SightSplit.asset</c>
        /// рядом с мешами Kinemation или в <c>Art/Weapons/HandsPack/{папка}/Meshes</c>. Исходный меш не перезаписывается
        /// (раунд 2: сборщик Kinemation пишет его заново, а разделение всегда идёт от него — повторный прогон детерминирован).
        /// </summary>
        public static string MeshAssetPath(SightSplitSpec spec, SightSplitPartSpec part, bool body)
        {
            string weapon = Path.GetFileNameWithoutExtension(spec.PrefabPath);
            string folder = spec.Parts.First().SourceMeshPath.StartsWith(KinemationArtRoot, StringComparison.Ordinal)
                ? Path.GetDirectoryName(part.SourceMeshPath).Replace('\\', '/')
                : $"Assets/Art/Weapons/HandsPack/{Path.GetFileName(Path.GetDirectoryName(spec.PrefabPath))}/Meshes";
            return body ? $"{folder}/{weapon}_{Path.GetFileName(part.NodePath)}_SightSplit.asset" : $"{folder}/{weapon}_Sight{part.Role}.asset";
        }

        /// <summary>Исходный меш узла из спецификации (ассет + localFileID); null — не найден.</summary>
        public static Mesh SourceMesh(SightSplitPartSpec part)
        {
            foreach (UnityEngine.Object asset in AssetDatabase.LoadAllAssetsAtPath(part.SourceMeshPath))
                if (asset is Mesh mesh && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string _, out long id) && id == part.SourceMeshLocalId)
                    return mesh;
            return null;
        }

        /// <summary>
        /// Возврат уже разделённого префаба к источнику — только во временной копии: снимаются рамки
        /// <c>Sight{Role}Frame</c> спецификации, у узла с вынесенной деталью меш корпуса (<see cref="MeshAssetPath"/>)
        /// заменяется исходным. Чужие узлы и меши не трогаются: рамка без детали спецификации или чужой меш — отказ.
        /// Поднятые рамки (Scar) остаются поднятыми: подъём идемпотентен.
        /// </summary>
        public static bool RevertMigrated(GameObject root, SightSplitSpec spec)
        {
            bool reverted = false;
            foreach (SightSplitPartSpec part in spec.Parts)
            {
                Transform node = Find(root.transform, part.NodePath, spec);
                Transform frame = node.Find(part.FrameName);
                if (frame != null)
                {
                    Transform partNode = frame.Find(part.PartName);
                    if (frame.childCount != 1 || partNode == null || partNode.GetComponent<SightAdjustablePart>() == null)
                        throw new InvalidOperationException($"{spec.WeaponId}: {part.NodePath}/{part.FrameName} не похож на разделение прицела — откат отменён");
                    UnityEngine.Object.DestroyImmediate(frame.gameObject);
                    reverted = true;
                }
                if (!part.Extract) continue;
                MeshFilter filter = node.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                if (AssetDatabase.GetAssetPath(filter.sharedMesh) != MeshAssetPath(spec, part, true)) continue;
                Mesh source = SourceMesh(part) ?? throw new InvalidOperationException($"{spec.WeaponId}: нет исходного меша {part.SourceMeshPath}:{part.SourceMeshLocalId}");
                filter.sharedMesh = source;
                reverted = true;
            }
            return reverted;
        }

        // ── Детали ─────────────────────────────────────────────────────────

        private static void Raise(Transform node, SightNodeRaise raise, SightSplitSpec spec)
        {
            bool folded = Near(node.localPosition, raise.FoldedLocalPosition) && Quaternion.Angle(node.localRotation, raise.FoldedLocalRotation) < AngleTolerance;
            bool raised = Near(node.localPosition, raise.RaisedLocalPosition) && Quaternion.Angle(node.localRotation, raise.RaisedLocalRotation) < AngleTolerance;
            if (raised) return;
            if (!folded) throw new InvalidOperationException($"{spec.WeaponId}: поза {raise.NodePath} не совпала ни со сложенной, ни с поднятой — разбор устарел");
            node.SetLocalPositionAndRotation(raise.RaisedLocalPosition, raise.RaisedLocalRotation);
        }

        private static MeshFilter CheckSourceMesh(Transform node, SightSplitPartSpec part, SightSplitSpec spec)
        {
            MeshFilter filter = node.GetComponent<MeshFilter>();
            MeshRenderer renderer = node.GetComponent<MeshRenderer>();
            if (filter == null || renderer == null || filter.sharedMesh == null || node.GetComponent<SkinnedMeshRenderer>() != null)
                throw new InvalidOperationException($"{spec.WeaponId}: у {part.NodePath} нет статичного MeshFilter + MeshRenderer");
            Mesh mesh = filter.sharedMesh;
            string path = AssetDatabase.GetAssetPath(mesh);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string _, out long localId);
            int triangles = Enumerable.Range(0, mesh.subMeshCount).Sum(s => (int)mesh.GetIndexCount(s)) / 3;
            if (path != part.SourceMeshPath || localId != part.SourceMeshLocalId || mesh.vertexCount != part.SourceVertices || triangles != part.SourceTriangles)
                throw new InvalidOperationException($"{spec.WeaponId}: меш {part.NodePath} — {path}:{localId} ({mesh.name}) nv{mesh.vertexCount}/nt{triangles}, " +
                                                    $"ожидается {part.SourceMeshPath}:{part.SourceMeshLocalId} nv{part.SourceVertices}/nt{part.SourceTriangles} (источник изменён или уже разделён)");
            if (renderer.sharedMaterials.Length < mesh.subMeshCount)
                throw new InvalidOperationException($"{spec.WeaponId}: у {part.NodePath} материалов меньше, чем подмешей");
            return filter;
        }

        private static void CreatePart(Transform node, SightSplitPartSpec part, SightPartMesh mesh, SightSplitApplyResult result, SightSplitSpec spec)
        {
            if (part.Extract && mesh == null) throw new InvalidOperationException($"{spec.WeaponId} {part.Role}: деталь не вынесена");
            Transform parent = node.parent;
            float parentScale = UniformScale(parent.lossyScale, $"{spec.WeaponId}: родитель {parent.name}");
            UniformScale(node.localScale, $"{spec.WeaponId}: узел {node.name}");

            // Рамка: оси и начало родителя N, мировой масштаб 1, но дочерняя N — едет вместе с ней.
            var frame = new GameObject(part.FrameName).transform;
            frame.gameObject.layer = node.gameObject.layer;
            frame.SetParent(node, false);
            Matrix4x4 inverse = Matrix4x4.TRS(node.localPosition, node.localRotation, node.localScale).inverse * Matrix4x4.Scale(Vector3.one / parentScale);
            frame.localPosition = inverse.GetColumn(3);
            frame.localRotation = Quaternion.Inverse(node.localRotation);
            frame.localScale = new Vector3(1f / (node.localScale.x * parentScale), 1f / (node.localScale.y * parentScale), 1f / (node.localScale.z * parentScale));
            if ((frame.position - parent.position).magnitude > PositionTolerance * Mathf.Max(1f, parentScale) || Quaternion.Angle(frame.rotation, parent.rotation) > AngleTolerance
                || (frame.lossyScale - Vector3.one).magnitude > 1e-4f)
                throw new InvalidOperationException($"{spec.WeaponId}: рамка {part.FrameName} не совпала с осями {parent.name} при масштабе 1");

            var go = new GameObject(part.PartName);
            go.layer = node.gameObject.layer;
            go.transform.SetParent(frame, false);
            go.transform.localPosition = frame.InverseTransformPoint(node.TransformPoint(part.PointMesh));
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var component = go.AddComponent<SightAdjustablePart>();
            var so = new SerializedObject(component);
            so.FindProperty("_role").enumValueIndex = (int)part.Role;
            so.FindProperty("_neutralLocalPosition").vector3Value = go.transform.localPosition;
            so.FindProperty("_travelDown").floatValue = part.TravelDown;
            so.FindProperty("_travelUp").floatValue = part.TravelUp;
            so.ApplyModifiedPropertiesWithoutUndo();
            result.Parts[part] = component;

            if (mesh == null) return;
            var meshGo = new GameObject(part.MeshObjectName);
            meshGo.layer = node.gameObject.layer;
            meshGo.transform.SetParent(go.transform, false);
            meshGo.transform.localPosition = Vector3.zero;
            meshGo.transform.localRotation = node.localRotation;
            meshGo.transform.localScale = node.localScale * parentScale;
            meshGo.AddComponent<MeshFilter>().sharedMesh = mesh.Mesh;
            MeshRenderer source = node.GetComponent<MeshRenderer>();
            MeshRenderer renderer = meshGo.AddComponent<MeshRenderer>();
            EditorUtility.CopySerialized(source, renderer);
            renderer.sharedMaterials = mesh.SourceSubmeshes.Select(s => source.sharedMaterials[s]).ToArray();
            result.PartRenderers[part] = renderer;
        }

        private static string Describe(GameObject root, SightSplitApplyResult result)
        {
            SightSplitSpec spec = result.Spec;
            var text = new StringBuilder();
            text.AppendLine($"{spec.WeaponId} ({spec.PrefabPath}): {spec.Note}");
            foreach (SightNodeRaise raise in spec.Raises)
                text.AppendLine($"  подъём {raise.NodePath}: {raise.AngleDeg:+0;-0}° вокруг оси {raise.AxisDirPrefab} через {raise.AxisPointPrefabM} (префаб, м)");
            foreach (var pair in result.Splits)
            {
                SightMeshSplit split = pair.Value;
                text.AppendLine($"  узел {SightSplitPaths.Of(pair.Key, root.transform)}: меш {split.Source.name}, островов {split.Islands.Islands.Count}, треугольников {split.Islands.TriangleCount}" +
                                (split.DroppedSkin ? "; скин-атрибуты FBX в выходные меши не переносятся" : ""));
                foreach (SightPartMesh part in split.Parts)
                    text.AppendLine($"    вынос {part.Spec.Role}: {string.Join(" + ", part.Islands)} → меш nv{part.SourceVertices.Length}/nt{part.Triangles}, подмеши [{string.Join(",", part.SourceSubmeshes)}]; " +
                                    $"ассеты: деталь {ProposedMeshPath(spec, part.Spec, false)}, корпус {ProposedMeshPath(spec, part.Spec, true)}");
                foreach (var reference in split.ReferenceIslands.Where(r => r.Value.Count > 0))
                    text.AppendLine($"    контроль {reference.Key.Role}: {string.Join(" + ", reference.Value)} (остаётся в корпусе)");
            }
            foreach (SightSplitPartSpec part in spec.Parts)
            {
                SightAdjustablePart component = result.Parts[part];
                Vector3 prefabM = SightSplitPaths.PrefabMetres(root.transform, component.transform.position);
                string reference = part.PointPrefabM.HasValue ? $", разбор {Mm(part.PointPrefabM.Value)} (Δ {(prefabM - part.PointPrefabM.Value).magnitude * 1e3f:0.000} мм)" : "";
                text.AppendLine($"  {SightSplitPaths.Of(component.transform, root.transform)}: {(part.Movable ? $"подвижная, ход −{part.TravelDown * 1e3f:0.0}…+{part.TravelUp * 1e3f:0.0} мм" : "неподвижная (только точка)")}, " +
                                $"точка {Mm(prefabM)} мм префаба{reference}");
            }
            foreach (string warning in result.Warnings) text.AppendLine("  ВНИМАНИЕ: " + warning);
            return text.ToString();
        }

        private static string Mm(Vector3 metres) => $"({metres.x * 1e3f:0.000}, {metres.y * 1e3f:0.000}, {metres.z * 1e3f:0.000})";

        private static Transform Find(Transform root, string path, SightSplitSpec spec) =>
            root.Find(path) ?? throw new InvalidOperationException($"{spec.WeaponId}: нет узла {path} в {spec.PrefabPath}");

        private static bool Near(Vector3 a, Vector3 b) => (a - b).magnitude <= 1e-5f;

        private static float UniformScale(Vector3 scale, string context)
        {
            float max = Mathf.Max(scale.x, scale.y, scale.z), min = Mathf.Min(scale.x, scale.y, scale.z);
            if (min <= 0f || (max - min) > 1e-4f * max) throw new InvalidOperationException($"{context}: неравномерный или отрицательный масштаб {scale}");
            return (scale.x + scale.y + scale.z) / 3f;
        }
    }

    /// <summary>Пути узлов и координаты «метры префаба» (корень с его масштабом, без позиции и поворота корня).</summary>
    public static class SightSplitPaths
    {
        public static string Of(Transform t, Transform root)
        {
            var names = new List<string>();
            for (Transform c = t; c != null && c != root; c = c.parent) names.Add(c.name);
            names.Reverse();
            return string.Join("/", names);
        }

        public static Vector3 PrefabMetres(Transform root, Vector3 world) => Quaternion.Inverse(root.rotation) * (world - root.position);
    }
}
