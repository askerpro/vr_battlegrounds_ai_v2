using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VrBattlegrounds.Editor.GripAuthoring.ContactMask;
using VrBattlegrounds.Editor.HandGeometry;
using VrBattlegrounds.Editor.HandPoseReview;

namespace VrBattlegrounds.Editor.GripAuthoring.ContactPoints
{
    /// <summary>Эталон одной кисти без оружия. Все изменения позы происходят на временной копии.</summary>
    public sealed class HandContactProbeCapture
    {
        public const string DefaultOutputDirectory = "tasks/hand-rig-quality/reports/phalanx-contact-points";
        public FitTriangle[] Hand { get; private set; }
        public HandPhalanxReferenceSegment[] Segments { get; private set; }
        public string BindingFingerprint { get; private set; }
        public UxrHandSide Side { get; private set; }
        public Bounds HandBounds { get; private set; }
        public string ReferencePosePath { get; private set; }
        public Vector3 ReferencePalmCenter { get; private set; }
        public Vector3 ReferencePalmOut { get; private set; }

        public static HandContactProbeCapture Capture(UxrAvatar prefab, UxrHandSide side, UxrHandPoseAsset referencePose = null)
        {
            if (!prefab || !EditorUtility.IsPersistent(prefab)) throw new ArgumentException("Выберите prefab аватара.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Редактор занят Play/compile/import.");
            var preview = new PreviewRenderUtility();
            try
            {
                var clone = preview.InstantiatePrefabInScene(prefab.gameObject);
                foreach (var behaviour in clone.GetComponentsInChildren<Behaviour>(true)) behaviour.enabled = false;
                var avatar = clone.GetComponent<UxrAvatar>();
                var pose = referencePose ? referencePose : avatar.DefaultHandPose;
                if (!pose) throw new ArgumentException("Нет эталонной позы кисти.");
                // Default SDK — Fixed; OpenGrip применяется только к явно выбранной Blend-позе.
                if (pose.PoseType == UxrHandPoseType.Fixed) avatar.SetCurrentHandPoseImmediately(side, pose);
                else if (pose.PoseType == UxrHandPoseType.Blend)
                    UxrAvatarRig.UpdateHandUsingDescriptor(avatar, side, pose.GetHandDescriptor(side, UxrBlendPoseType.OpenGrip));
                else throw new NotSupportedException("Неизвестный тип эталонной позы.");
                var worldToFrame = Matrix4x4.TRS(clone.transform.position, clone.transform.rotation, Vector3.one).inverse;
                var hand = avatar.GetHand(side);
                var fingers = new[] { hand.Thumb, hand.Index, hand.Middle, hand.Ring, hand.Little };
                var names = new[] { "thumb", "index", "middle", "ring", "little" };
                var armInfo = avatar.AvatarRigInfo.GetArmInfo(side);
                var infos = new[] { armInfo.ThumbInfo, armInfo.IndexInfo, armInfo.MiddleInfo, armInfo.RingInfo, armInfo.LittleInfo };
                var zones = new Dictionary<Transform, string>();
                var segmentsByBone = new Dictionary<Transform, string>();
                var segments = new List<HandPhalanxReferenceSegment>();
                for (int f = 0; f < fingers.Length; f++)
                {
                    var finger = fingers[f];
                    var nodes = new[] { finger.Metacarpal, finger.Proximal, finger.Intermediate, finger.Distal };
                    var labels = new[] { "metacarpal", "proximal", "intermediate", "distal" };
                    for (int n = 0; n < nodes.Length; n++) if (nodes[n])
                        foreach (var child in nodes[n].GetComponentsInChildren<Transform>(true))
                        { zones[child] = names[f]; segmentsByBone[child] = names[f] + "/" + labels[n]; }
                    var phalanges = new[] { finger.Proximal, finger.Intermediate, finger.Distal };
                    for (int n = 0; n < phalanges.Length; n++)
                    {
                        var bone = phalanges[n];
                        Vector3 start = bone ? worldToFrame.MultiplyPoint3x4(bone.position) : Vector3.zero;
                        Vector3 end = start;
                        if (n < 2 && phalanges[n + 1]) end = worldToFrame.MultiplyPoint3x4(phalanges[n + 1].position);
                        else if (n == 2 && bone && infos[f] != null)
                            end = worldToFrame.MultiplyPoint3x4(bone.TransformPoint(infos[f].DistalLocalTip));
                        segments.Add(new HandPhalanxReferenceSegment
                        {
                            Id = names[f] + "/" + labels[n + 1], Finger = names[f], Segment = labels[n + 1],
                            Start = start, End = end,
                            PalmarDirection = bone ? -worldToFrame.MultiplyVector(bone.TransformVector(armInfo.FingerUniversalLocalAxes.LocalUp)).normalized : Vector3.zero
                        });
                    }
                }
                var allowed = new HashSet<Transform>(hand.Transforms);
                foreach (var bone in hand.Transforms)
                    foreach (var child in bone.GetComponentsInChildren<Transform>(true)) allowed.Add(child);
                var grabber = avatar.GetComponentsInChildren<UxrGrabber>(true).SingleOrDefault(g => g.Side == side);
                var main = grabber ? grabber.HandRenderer : null;
                var lower = HandPoseFitSnapshot.LowerLodRenderers(clone);
                var triangles = new List<FitTriangle>();
                foreach (var skin in clone.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (lower.Contains(skin) || (skin != main && (!skin.enabled || !skin.gameObject.activeInHierarchy)) ||
                        !skin.sharedMesh || !skin.bones.Any(allowed.Contains)) continue;
                    var sample = HandMeshCapture.Capture(skin, worldToFrame, avatar.transform);
                    var weights = new float[sample.Vertices.Length];
                    var dominant = Enumerable.Repeat(-1, weights.Length).ToArray();
                    for (int v = 0; v < weights.Length; v++)
                    {
                        float best = 0;
                        for (int w = sample.WeightOffsets[v]; w < sample.WeightOffsets[v + 1]; w++)
                        {
                            var influence = sample.Weights[w];
                            if (!allowed.Contains(sample.Bones[influence.BoneIndex])) continue;
                            weights[v] += influence.Weight;
                            if (influence.Weight > best) { best = influence.Weight; dominant[v] = influence.BoneIndex; }
                        }
                    }
                    for (int sub = 0; sub < sample.SubmeshTriangles.Length; sub++)
                    {
                        var indices = sample.SubmeshTriangles[sub];
                        for (int i = 0; i < indices.Length; i += 3)
                        {
                            int a = indices[i], b = indices[i + 1], c = indices[i + 2];
                            if ((weights[a] + weights[b] + weights[c]) / 3 < .5f) continue;
                            int dominantBone = new[] { a, b, c }.Where(v => dominant[v] >= 0).GroupBy(v => dominant[v])
                                .OrderByDescending(g => g.Sum(v => weights[v])).Select(g => g.Key).DefaultIfEmpty(-1).First();
                            if (dominantBone < 0) continue;
                            var bone = sample.Bones[dominantBone];
                            var triangle = new FitTriangle(sample.Vertices[a], sample.Vertices[b], sample.Vertices[c],
                                zones.TryGetValue(bone, out var zone) ? zone : "palm_wrist")
                            {
                                Source = sample.RendererPath, SourceSubMesh = sub, SourceTriangle = i / 3,
                                FingerSegment = segmentsByBone.TryGetValue(bone, out var segment) ? segment : "palm_wrist"
                            };
                            if (triangle.Area >= 1e-12f) triangles.Add(triangle);
                        }
                    }
                }
                if (triangles.Count == 0) throw new InvalidOperationException("Не получена кожа выбранной кисти.");
                // Cached SDK DistalLocalTip бывает несогласован со стороной/масштабом. Длину конечной фаланги
                // берём из действительной эталонной кожи вдоль её костной оси, а не из кешированного радиуса.
                foreach (var segment in segments.Where(s => s.Segment == "distal"))
                {
                    Vector3 axis = (segment.End - segment.Start).normalized;
                    float extent = triangles.Where(t => t.Zone == segment.Finger && t.FingerSegment == segment.Id)
                        .SelectMany(t => new[] { t.A, t.B, t.C }).Select(p => Vector3.Dot(p - segment.Start, axis))
                        .Where(d => !float.IsNaN(d) && !float.IsInfinity(d) && d > 0).DefaultIfEmpty(0).Max();
                    segment.End = segment.Start + axis * extent;
                }
                var bounds = new Bounds(triangles[0].A, Vector3.zero);
                foreach (var triangle in triangles) { bounds.Encapsulate(triangle.A); bounds.Encapsulate(triangle.B); bounds.Encapsulate(triangle.C); }
                if (!hand.GetPalmOutDirection(side, out Vector3 palmOut))
                    throw new InvalidOperationException("Не определено направление ладонной поверхности эталона.");
                return new HandContactProbeCapture
                {
                    Hand = triangles.ToArray(), Segments = segments.ToArray(), Side = side, HandBounds = bounds,
                    ReferencePalmCenter = worldToFrame.MultiplyPoint3x4(VrBattlegrounds.Editor.Avatars.HandsPackPoseExtractor.PalmCenterWorld(hand)),
                    ReferencePalmOut = worldToFrame.MultiplyVector(palmOut).normalized,
                    BindingFingerprint = Fingerprint(prefab, triangles.ToArray(), side), ReferencePosePath = AssetDatabase.GetAssetPath(pose)
                };
            }
            finally { preview.Cleanup(); }
        }

        internal static string Fingerprint(UxrAvatar avatar, FitTriangle[] triangles, UxrHandSide side)
        {
            string description = HandContactMaskCapture.BuildBindingDescription(avatar, triangles, side.ToString());
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(description))).Replace("-", "").ToLowerInvariant();
        }

        public HandPhalanxProbeSet CreateProbes()
        {
            var probes = HandPhalanxContactProbes.Select(Hand, BindingFingerprint, Side.ToString(), Segments);
            probes.Method += "; distal_length=actual_reference_skin_extent_along_bone_axis";
            return probes;
        }

        /// <summary>Передаёт IK весь проверенный набор; ни одна фаланга не теряется при создании anchors.</summary>
        public VrBattlegrounds.Editor.Avatars.ContactIk.ContactIkPatchBinding[] CreateIkBindings(HandPhalanxProbeSet probes, bool reviewed = false)
        {
            Validate(probes);
            if (!probes.Valid || probes.Errors == null || probes.Errors.Count != 0 || probes.Probes.Any(p => !string.IsNullOrEmpty(p.Error)))
                throw new ArgumentException("IK требует полный валидный набор всех ожидаемых опорных точек.");
            return probes.Probes.Select(p => new VrBattlegrounds.Editor.Avatars.ContactIk.ContactIkPatchBinding
            {
                Anchor = new VrBattlegrounds.Editor.Avatars.ContactIk.ContactIkSurfaceAnchor
                { Source = p.Source, SourceSubMesh = p.SourceSubMesh, SourceTriangle = p.SourceTriangle, Barycentric = p.Barycentric },
                Target = new VrBattlegrounds.Editor.Avatars.ContactIk.ContactIkTarget
                { PatchId = p.SegmentId, Mode = VrBattlegrounds.Editor.Avatars.ContactIk.ContactIkTargetMode.FixedPointBand, MatchNormal = false },
                CorrespondenceReviewed = reviewed,
                Provenance = "fixed phalanx probes; " + probes.Method + "; inferred=" + probes.Inferred
            }).ToArray();
        }

        /// <summary>Отражение материальных точек в анатомических рамках одноимённых фаланг; оружие не участвует.</summary>
        public static HandPhalanxProbeSet Mirror(HandContactProbeCapture source, HandContactProbeCapture target, HandPhalanxProbeSet sourceSet)
        {
            if (source == null || target == null) throw new ArgumentNullException("Нужны оба снимка кистей.");
            source.Validate(sourceSet);
            if (source.Side == target.Side) throw new ArgumentException("Нужна противоположная кисть.");
            if (!source.Segments.Select(s => s.Id).OrderBy(s => s).SequenceEqual(target.Segments.Select(s => s.Id).OrderBy(s => s)))
                throw new ArgumentException("Наборы фаланг разных кистей не совпадают.");
            if (target.Hand.GroupBy(t => new HandPosePotentialContactTriangleKey(t.Source, t.SourceSubMesh, t.SourceTriangle)).Any(g => g.Count() != 1))
                throw new ArgumentException("Повторный материальный ключ целевой кожи.");
            var result = new HandPhalanxProbeSet
            {
                BindingFingerprint = target.BindingFingerprint, Side = target.Side.ToString(), ExpectedCount = sourceSet.ExpectedCount,
                Inferred = true, Method = "mirrored_anatomical_bone_start_frames; maximum_projection_error_1mm; connected_edge_ties_deterministic; no_weapon_input"
            };
            if (!sourceSet.Valid) result.Errors.Add("Исходный набор невалиден; зеркало не исправляет ошибки источника.");
            if (sourceSet.Errors != null) result.Errors.AddRange(sourceSet.Errors.Select(e => "Источник: " + e));
            var used = new HashSet<HandPosePotentialContactTriangleKey>();
            foreach (var probe in sourceSet.Probes)
            {
                var output = new HandPhalanxProbe { SegmentId = probe.SegmentId, Finger = probe.Finger, Segment = probe.Segment, SelectionMode = "mirrored_segment_material_point" };
                result.Probes.Add(output);
                if (!string.IsNullOrEmpty(probe.Error)) { output.Error = "Источник: " + probe.Error; continue; }
                var from = source.Segments.Single(s => s.Id == probe.SegmentId);
                var to = target.Segments.Single(s => s.Id == probe.SegmentId);
                var sourceTriangle = source.Hand.Single(t => Matches(t, probe));
                Vector3 point = Position(sourceTriangle, probe.Barycentric);
                Vector3 Reflect(Vector3 vector)
                {
                    Vector3 a = (from.End - from.Start).normalized, p = Vector3.ProjectOnPlane(from.PalmarDirection, a).normalized, x = Vector3.Cross(a, p).normalized;
                    Vector3 b = (to.End - to.Start).normalized, q = Vector3.ProjectOnPlane(to.PalmarDirection, b).normalized, y = Vector3.Cross(b, q).normalized;
                    return -y * Vector3.Dot(vector, x) + q * Vector3.Dot(vector, p) + b * Vector3.Dot(vector, a);
                }
                if ((from.End - from.Start).sqrMagnitude < 1e-10f || (to.End - to.Start).sqrMagnitude < 1e-10f ||
                    Vector3.Cross(from.End - from.Start, from.PalmarDirection).sqrMagnitude < 1e-12f ||
                    Vector3.Cross(to.End - to.Start, to.PalmarDirection).sqrMagnitude < 1e-12f)
                { output.Error = "Вырожденная рамка зеркальной фаланги."; continue; }
                // Материал отражается от одноимённого сустава: асимметрия длины SDK tip не сдвигает кожу.
                Vector3 expected = to.Start + Reflect(point - from.Start);
                Vector3 normal = Reflect(sourceTriangle.Normal).normalized;
                var candidates = target.Hand.Where(t => t.Zone == probe.Finger && t.FingerSegment == probe.Finger + "/" + probe.Segment && Vector3.Dot(t.Normal, normal) > .8f)
                    .Select(t => new { Triangle = t, Point = HandPoseFitGeometry.ClosestPoint(expected, t) })
                    .Select(c => new { c.Triangle, c.Point, Error = (c.Point - expected).magnitude }).OrderBy(c => c.Error)
                    .ThenBy(c => c.Triangle.Source, StringComparer.Ordinal).ThenBy(c => c.Triangle.SourceSubMesh).ThenBy(c => c.Triangle.SourceTriangle).ToArray();
                if (candidates.Length == 0 || candidates[0].Error > .001f)
                { output.Error = "Зеркальная кожа не найдена в пределах1мм; ближайший полигон не назначен."; continue; }
                var best = candidates[0];
                if (candidates.Skip(1).Any(c => c.Error <= best.Error + .00001f && (c.Point - best.Point).sqrMagnitude > 1e-10f && !ShareEdge(best.Triangle, c.Triangle)))
                { output.Error = "Неоднозначная проекция зеркальной точки; нужна проверка."; continue; }
                var key = new HandPosePotentialContactTriangleKey(best.Triangle.Source, best.Triangle.SourceSubMesh, best.Triangle.SourceTriangle);
                if (!used.Add(key)) { output.Error = "Повторный полигон при зеркальном переносе."; continue; }
                output.Source = key.Source; output.SourceSubMesh = key.SourceSubMesh; output.SourceTriangle = key.SourceTriangle;
                output.Barycentric = Barycentric(best.Triangle, best.Point);
                if (!ValidBarycentric(output.Barycentric)) { output.Error = "Нечисловая/недопустимая зеркальная barycentric-точка."; continue; }
                output.ReferencePosition = Position(best.Triangle, output.Barycentric);
                output.SelectionDistanceMm = best.Error * 1000; output.MaximumSelectionDistanceMm = 1;
            }
            result.Errors.AddRange(result.Probes.Where(p => !string.IsNullOrEmpty(p.Error)).Select(p => p.SegmentId + ": " + p.Error));
            result.Valid = result.ExpectedCount > 0 && result.Probes.Count == result.ExpectedCount && result.Errors.Count == 0;
            target.Validate(result);
            return result;
        }

        public static HandPhalanxProbeReport Measure(HandPoseFitRequest supplied, HandPhalanxProbeSet probes)
        {
            if (supplied == null) throw new ArgumentNullException(nameof(supplied));
            var request = supplied.Copy();
            if (!request.AvatarPrefab && !string.IsNullOrEmpty(request.AvatarPath))
            { var go = AssetDatabase.LoadAssetAtPath<GameObject>(request.AvatarPath); if (go) request.AvatarPrefab = go.GetComponent<UxrAvatar>(); }
            if (!request.ObjectPrefab && !string.IsNullOrEmpty(request.ObjectPath)) request.ObjectPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(request.ObjectPath);
            if (!request.PoseOverride && !string.IsNullOrEmpty(request.PoseOverridePath)) request.PoseOverride = AssetDatabase.LoadAssetAtPath<UxrHandPoseAsset>(request.PoseOverridePath);
            if (!request.AvatarPrefab || !request.ObjectPrefab) throw new ArgumentException("Нужны prefab аватара и предмета.");
            var metadata = new HandPoseFitReport { Settings = request };
            HandPhalanxProbeReport report;
            using (var snapshot = new HandPoseFitSnapshot(request, metadata))
                report = HandPhalanxContactProbes.Evaluate(snapshot.Hand.ToArray(), new FitSurface(snapshot.ObjectSurface.ToArray()),
                    Fingerprint(request.AvatarPrefab, snapshot.Hand.ToArray(), request.Side), probes, 2);
            if (!string.IsNullOrWhiteSpace(request.OutputDirectory))
            {
                string directory = HandContactMaskCapture.SafeOutputDirectory(request.OutputDirectory);
                Directory.CreateDirectory(directory); WriteJson(Path.Combine(directory, "point-report.json"), report);
            }
            return report;
        }

        public HandPhalanxProbeSet LoadProbes(string path)
        {
            var probes = JsonConvert.DeserializeObject<HandPhalanxProbeSet>(File.ReadAllText(path), FitUnityJsonConverter.Settings);
            Validate(probes);
            foreach (var p in probes.Probes.Where(p => string.IsNullOrEmpty(p.Error))) p.ReferencePosition = Position(Hand.Single(t => Matches(t, p)), p.Barycentric);
            return probes;
        }

        public string Export(string directory, HandPhalanxProbeSet probes)
        {
            Validate(probes); directory = HandContactMaskCapture.SafeOutputDirectory(directory);
            if (File.Exists(Path.Combine(directory, "probes.json"))) throw new IOException("В каталоге уже есть probes.json; выберите новый каталог.");
            Directory.CreateDirectory(directory); WriteJson(Path.Combine(directory, "probes.json"), probes);
            File.WriteAllBytes(Path.Combine(directory, "probe-preview.png"), RenderPreview(probes));
            var text = new StringBuilder("# Опорные точки фаланг\n\n").Append("Сторона: ").Append(Side)
                .Append(". Критерий: расстояние каждой фиксированной точки до предмета ±2мм. Площадь не оценивается.\n\n")
                .Append("Эталонная поза: ").Append(ReferencePosePath).Append(". Оружие не участвовало в выборе.\n")
                .Append("Точек: ").Append(probes.Probes.Count(p => string.IsNullOrEmpty(p.Error))).Append('/').Append(probes.ExpectedCount)
                .Append(". Inferred mirror: ").Append(probes.Inferred).Append(". Человеческая проверка выбора ещё требуется.\n\n")
                .Append("![Опорные точки](").Append(Path.Combine(directory, "probe-preview.png").Replace('\\', '/')).Append(")\n\n");
            foreach (var probe in probes.Probes) text.Append("- ").Append(probe.SegmentId).Append(": ").Append(probe.Error ?? "выбран материальный полигон + barycentric").Append('\n');
            var path = Path.Combine(directory, "report.md"); File.WriteAllText(path, text.ToString(), new UTF8Encoding(false)); return path;
        }

        public byte[] RenderPreview(HandPhalanxProbeSet probes, int size = 768)
        {
            Validate(probes);
            if (size < 64 || size > 4096) throw new ArgumentOutOfRangeException(nameof(size));
            var preview = new PreviewRenderUtility(); Mesh mesh = null; Material material = null; Texture2D texture = null;
            try
            {
                var vertices = new List<Vector3>(); var colors = new List<Color>();
                foreach (var t in Hand)
                {
                    bool selected = probes.Probes.Any(p => string.IsNullOrEmpty(p.Error) && Matches(t, p));
                    var color = selected ? new Color(1, .75f, .1f) : new Color(.35f, .38f, .42f);
                    vertices.Add(t.A); vertices.Add(t.B); vertices.Add(t.C); colors.Add(color); colors.Add(color); colors.Add(color);
                }
                // Малые октаэдры показывают саму barycentric-точку, а не только её исходную грань.
                foreach (var p in probes.Probes.Where(p => string.IsNullOrEmpty(p.Error)))
                {
                    var triangle = Hand.Single(t => Matches(t, p));
                    Vector3 center = Position(triangle, p.Barycentric) + triangle.Normal * .0003f;
                    var offsets = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
                    var indices = new[] { 0, 2, 4, 2, 1, 4, 1, 3, 4, 3, 0, 4, 2, 0, 5, 1, 2, 5, 3, 1, 5, 0, 3, 5 };
                    foreach (int i in indices) { vertices.Add(center + offsets[i] * .0007f); colors.Add(new Color(.15f, 1, 1)); }
                }
                mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(Enumerable.Range(0, vertices.Count).ToArray(), 0); mesh.RecalculateNormals();
                var shader = Shader.Find("Hidden/VRBattlegrounds/HandPoseFit"); if (!shader) throw new InvalidOperationException("Нет шейдера preview.");
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                var middle = Segments.FirstOrDefault(s => s.Id == "middle/proximal");
                Vector3 forward = middle != null ? (middle.End - middle.Start).normalized : Vector3.up;
                Vector3 palmar = middle != null ? middle.PalmarDirection.normalized : Vector3.forward;
                var camera = preview.camera;
                camera.transform.SetPositionAndRotation(HandBounds.center + palmar * .5f, Quaternion.LookRotation(-palmar, forward));
                camera.orthographic = true; camera.orthographicSize = Mathf.Max(.005f, HandBounds.extents.magnitude * 1.12f);
                camera.aspect = 1; camera.nearClipPlane = .001f; camera.farClipPlane = 10;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f, .085f, .105f);
                preview.BeginStaticPreview(new Rect(0, 0, size, size)); preview.DrawMesh(mesh, Matrix4x4.identity, material, 0); preview.Render(true);
                texture = preview.EndStaticPreview(); return texture.EncodeToPNG();
            }
            finally
            {
                if (texture) UnityEngine.Object.DestroyImmediate(texture); if (mesh) UnityEngine.Object.DestroyImmediate(mesh);
                if (material) UnityEngine.Object.DestroyImmediate(material); preview.Cleanup();
            }
        }

        void Validate(HandPhalanxProbeSet probes)
        {
            if (probes == null || probes.SchemaVersion != 1 || probes.BindingFingerprint != BindingFingerprint || probes.Side != Side.ToString())
                throw new ArgumentException("Версия, сторона или mesh binding точек не совпадают.");
            if (probes.ExpectedCount != Segments.Length || probes.Probes == null || probes.Probes.Count != probes.ExpectedCount ||
                probes.Probes.Any(p => p == null || !Segments.Any(s => s.Id == p.SegmentId && s.Finger == p.Finger && s.Segment == p.Segment)) ||
                probes.Probes.Select(p => p.SegmentId).Distinct().Count() != probes.ExpectedCount)
                throw new ArgumentException("Неполный/повторный набор ожидаемых фаланг.");
            foreach (var p in probes.Probes.Where(p => string.IsNullOrEmpty(p.Error)))
            {
                var matches = Hand.Where(t => Matches(t, p)).ToArray();
                if (!Segments.Any(s => s.Id == p.SegmentId && s.Finger == p.Finger && s.Segment == p.Segment) ||
                    matches.Length != 1 || matches[0].FingerSegment != p.Finger + "/" + p.Segment ||
                    !ValidBarycentric(p.Barycentric))
                    throw new ArgumentException("Недействительная материальная точка: " + p.SegmentId);
            }
        }
        static bool Matches(FitTriangle t, HandPhalanxProbe p) => t.Source == p.Source && t.SourceSubMesh == p.SourceSubMesh && t.SourceTriangle == p.SourceTriangle;
        static Vector3 Position(FitTriangle t, Vector3 bary) => t.A * bary.x + t.B * bary.y + t.C * bary.z;
        static Vector3 Barycentric(FitTriangle t, Vector3 point)
        {
            Vector3 a = t.B - t.A, b = t.C - t.A, p = point - t.A;
            // Cross-формула в double не теряет площадь тонких ненулевых граней при вычитании aa*bb-ab*ab.
            double nx = (double)a.y * b.z - (double)a.z * b.y, ny = (double)a.z * b.x - (double)a.x * b.z, nz = (double)a.x * b.y - (double)a.y * b.x;
            double d = nx * nx + ny * ny + nz * nz;
            if (!(d > 0) || double.IsInfinity(d)) return new Vector3(float.NaN, float.NaN, float.NaN);
            double y = (((double)p.y * b.z - (double)p.z * b.y) * nx + ((double)p.z * b.x - (double)p.x * b.z) * ny + ((double)p.x * b.y - (double)p.y * b.x) * nz) / d;
            double z = (((double)a.y * p.z - (double)a.z * p.y) * nx + ((double)a.z * p.x - (double)a.x * p.z) * ny + ((double)a.x * p.y - (double)a.y * p.x) * nz) / d;
            var bary = new Vector3((float)(1 - y - z), (float)y, (float)z);
            if (bary.x >= -.00001f && bary.y >= -.00001f && bary.z >= -.00001f && bary.x <= 1.00001f && bary.y <= 1.00001f && bary.z <= 1.00001f)
            { bary = new Vector3(Mathf.Clamp01(bary.x), Mathf.Clamp01(bary.y), Mathf.Clamp01(bary.z)); bary /= bary.x + bary.y + bary.z; }
            return bary;
        }
        static bool ValidBarycentric(Vector3 bary) => !float.IsNaN(bary.sqrMagnitude) && !float.IsInfinity(bary.sqrMagnitude) &&
            bary.x >= 0 && bary.y >= 0 && bary.z >= 0 && bary.x <= 1 && bary.y <= 1 && bary.z <= 1 && Mathf.Abs(bary.x + bary.y + bary.z - 1) <= .00001f;
        static bool ShareEdge(FitTriangle a, FitTriangle b)
        {
            if (a.Source != b.Source || a.SourceSubMesh != b.SourceSubMesh) return false;
            var first = new[] { a.A, a.B, a.C }; var second = new[] { b.A, b.B, b.C };
            return first.Count(p => second.Any(q => (p - q).sqrMagnitude <= 1e-12f)) >= 2;
        }
        static void WriteJson(string path, object value) => File.WriteAllText(path, JsonConvert.SerializeObject(value, FitUnityJsonConverter.Settings).Replace("\r\n", "\n"), new UTF8Encoding(false));
    }
}
