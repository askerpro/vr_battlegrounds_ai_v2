using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using UltimateXR.Avatar;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using VrBattlegrounds.Editor.HandPoseReview;

namespace VrBattlegrounds.Editor.GripAuthoring.ContactMask
{
    /// <summary>Замороженная геометрия; захват не сохраняет игровые ассеты и не удерживает сцену анализатора.</summary>
    public sealed class HandContactMaskCapture
    {
        public const string DefaultOutputDirectory = "tasks/hand-rig-quality/reports/contact-mask-authoring";
        public static readonly string[] RegionNames = { "palm", "thumb", "index", "middle", "ring", "little" };
        public FitTriangle[] Hand { get; private set; }
        public FitTriangle[] ObjectSurface { get; private set; }
        public HandPoseFitReport SourceReport { get; private set; }
        public string BindingFingerprint { get; private set; }
        public string BindingDescription { get; private set; }
        public Bounds HandBounds { get; private set; }
        public string FrameMode => "prefab_static";

        public static HandContactMaskCapture Capture(HandPoseFitRequest supplied)
        {
            if (supplied == null) throw new ArgumentNullException(nameof(supplied));
            var request = supplied.Copy();
            request.RenderImages = false;
            request.RenderReviewSheet = false;
            request.ComputeCorrectionHints = false;
            request.ApplyGripRules = false;
            request.IncludeOtherHand = false;
            request.CheckHandSelfIntersections = false;
            request.CheckSamplingConvergence = false;
            request.OutputDirectory = SafeOutputDirectory(string.IsNullOrWhiteSpace(request.OutputDirectory)
                ? Path.Combine(DefaultOutputDirectory, "capture-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"))
                : request.OutputDirectory);
            HandContactMaskCapture captured = null;
            var report = HandPoseFitAnalyzer.Analyze(request, (snapshot, metadata) =>
            {
                captured = new HandContactMaskCapture
                {
                    Hand = snapshot.Hand.ToArray(), ObjectSurface = snapshot.ObjectSurface.ToArray(),
                    SourceReport = metadata, HandBounds = snapshot.HandBounds
                };
                // Analyze разрешает AvatarPath на собственной копии request; берём уже разрешённый prefab.
                var avatar = metadata.Settings.AvatarPrefab;
                captured.BindingDescription = BuildBindingDescription(avatar, captured.Hand, request.Side.ToString());
                using (var sha = SHA256.Create())
                    captured.BindingFingerprint = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(captured.BindingDescription))).Replace("-", "").ToLowerInvariant();
            });
            if (captured == null || !string.IsNullOrEmpty(report.Error) || string.IsNullOrEmpty(captured.BindingFingerprint))
                throw new InvalidOperationException("Снимок не получен: " + report.Status + " " + report.Error);
            return captured;
        }

        internal static string BuildBindingDescription(UxrAvatar avatar, FitTriangle[] triangles, string side)
        {
            if (!avatar || !EditorUtility.IsPersistent(avatar))
                throw new ArgumentException("Для привязки нужен исходный prefab аватара.");
            var builder = new StringBuilder("hand-contact-mask-v1\nside=").Append(side).Append('\n');
            var renderers = avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            foreach (var source in triangles.Select(t => t.Source).Distinct().OrderBy(x => x, StringComparer.Ordinal))
            {
                var candidates = renderers.Where(r => HandPoseFitSnapshot.HumanPath(r.transform, avatar.transform) == source).ToArray();
                if (candidates.Length != 1 || !candidates[0].sharedMesh)
                    throw new ArgumentException("Неоднозначный или отсутствующий исходный SMR: " + source);
                var renderer = candidates[0];
                var mesh = renderer.sharedMesh;
                if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string guid, out long id))
                    throw new ArgumentException("Меш без постоянной GUID/localId: " + source);
                builder.Append(source).Append('|').Append(guid).Append('|').Append(id)
                    .Append('|').Append(AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(mesh))).Append('|');
                var lodTokens = new List<string>();
                foreach (var group in avatar.GetComponentsInChildren<LODGroup>(true))
                {
                    var lods = group.GetLODs();
                    for (int i = 0; i < lods.Length; i++)
                        if (lods[i].renderers.Contains(renderer))
                            lodTokens.Add(HandPoseFitSnapshot.HumanPath(group.transform, avatar.transform) + ":LOD" + i);
                }
                builder.Append(lodTokens.Count == 0 ? "no_lod" : string.Join(",", lodTokens.OrderBy(x => x, StringComparer.Ordinal))).Append('\n');
            }
            foreach (var t in triangles.OrderBy(t => t.Source, StringComparer.Ordinal).ThenBy(t => t.SourceSubMesh).ThenBy(t => t.SourceTriangle))
                builder.Append(t.Source).Append('|').Append(t.SourceSubMesh).Append('|').Append(t.SourceTriangle).Append('\n');
            return builder.ToString();
        }

        public HandPosePotentialContactMask NewMask()
        {
            return new HandPosePotentialContactMask
            {
                Metadata = new HandPosePotentialContactMaskMetadata
                {
                    BindingFingerprint = BindingFingerprint, Reviewed = false,
                    Provenance = "manual_whole_source_triangles; " + DateTime.UtcNow.ToString("O")
                },
                Regions = RegionNames.Select(r => new HandPosePotentialContactMaskRegion { Region = r }).ToList()
            };
        }

        public HandPosePotentialContactCoverageReport Evaluate(HandPosePotentialContactMask mask,
            HandPosePotentialContactCoverageSettings settings = null)
        {
            // Сертифицированного классификатора знака нет: gap/deep остаются unknown.
            return HandPosePotentialContactCoverage.Evaluate(Hand, new FitSurface(ObjectSurface), mask, BindingFingerprint, settings);
        }

        /// <summary>Явно выбранные человеком исходные грани. Никакого отбора по расстоянию/нормали.</summary>
        public bool SetTriangles(HandPosePotentialContactMask mask, IEnumerable<HandPosePotentialContactTriangleKey> keys,
            string region, bool erase = false)
        {
            if (!HandPosePotentialContactMask.IsRegion(region)) throw new ArgumentException("Неизвестный регион: " + region);
            if (mask?.Metadata?.BindingFingerprint != BindingFingerprint) throw new ArgumentException("Mesh binding маски не совпадает.");
            var byKey = Hand.ToDictionary(t => new HandPosePotentialContactTriangleKey(t.Source, t.SourceSubMesh, t.SourceTriangle));
            var selected = keys.Distinct().ToArray();
            foreach (var key in selected) if (!byKey.ContainsKey(key)) throw new ArgumentException("Исходная грань отсутствует: " + key);
            bool changed = false;
            foreach (var key in selected)
            {
                int index = mask.Triangles.FindIndex(t => new HandPosePotentialContactTriangleKey(t.Source, t.SourceSubMesh, t.SourceTriangle).Equals(key));
                if (erase)
                { if (index >= 0) { mask.Triangles.RemoveAt(index); changed = true; } continue; }
                if (index >= 0 && mask.Triangles[index].Region == region && mask.Triangles[index].Weight == 1) continue;
                if (index >= 0) { mask.Triangles[index].Region = region; mask.Triangles[index].Weight = 1; }
                else mask.Triangles.Add(new HandPosePotentialContactMaskTriangle
                {
                    Source = key.Source, SourceSubMesh = key.SourceSubMesh, SourceTriangle = key.SourceTriangle,
                    Region = region, Weight = 1, ReferenceAreaMm2 = byKey[key].Area * 1000000.0
                });
                changed = true;
            }
            if (changed) mask.Metadata.Reviewed = false;
            return changed;
        }

        /// <summary>Настоящий Unity render для headless editor-пилота; игровые объекты не создаются.</summary>
        public byte[] RenderMaskPreview(HandPosePotentialContactMask mask, int size = 1024, Quaternion? rotation = null)
        {
            ValidateBinding(mask);
            if (size < 64 || size > 4096) throw new ArgumentOutOfRangeException(nameof(size));
            var shader = Shader.Find("Hidden/VRBattlegrounds/HandPoseFit");
            if (!shader) throw new InvalidOperationException("Шейдер HandPoseFit не найден.");
            var preview = new PreviewRenderUtility();
            Mesh mesh = null; Material material = null; Texture2D texture = null;
            try
            {
                var entries = mask.Triangles.ToDictionary(t => new HandPosePotentialContactTriangleKey(t.Source, t.SourceSubMesh, t.SourceTriangle));
                var vertices = new List<Vector3>(); var colors = new List<Color>();
                foreach (var triangle in Hand)
                {
                    var key = new HandPosePotentialContactTriangleKey(triangle.Source, triangle.SourceSubMesh, triangle.SourceTriangle);
                    Color color = new Color(.35f, .38f, .42f);
                    if (entries.TryGetValue(key, out var entry)) color = HandContactMaskWindow.RegionColor(entry.Region);
                    vertices.Add(triangle.A); vertices.Add(triangle.B); vertices.Add(triangle.C);
                    colors.Add(color); colors.Add(color); colors.Add(color);
                }
                mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave, indexFormat = IndexFormat.UInt32 };
                mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(Enumerable.Range(0, vertices.Count).ToArray(), 0);
                mesh.RecalculateNormals(); mesh.RecalculateBounds();
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                var camera = preview.camera; var view = rotation ?? Quaternion.Euler(-25, 25, 0);
                camera.transform.SetPositionAndRotation(HandBounds.center - view * Vector3.forward * 2, view);
                camera.orthographic = true; camera.orthographicSize = Mathf.Max(.005f, HandBounds.extents.magnitude * 1.15f);
                camera.aspect = 1; camera.nearClipPlane = .001f; camera.farClipPlane = 10;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.07f, .085f, .105f);
                preview.BeginStaticPreview(new Rect(0, 0, size, size));
                preview.DrawMesh(mesh, Matrix4x4.identity, material, 0); preview.Render(true);
                texture = preview.EndStaticPreview();
                return texture.EncodeToPNG();
            }
            finally
            {
                if (texture) UnityEngine.Object.DestroyImmediate(texture);
                if (mesh) UnityEngine.Object.DestroyImmediate(mesh);
                if (material) UnityEngine.Object.DestroyImmediate(material);
                preview.Cleanup();
            }
        }

        public HandPosePotentialContactMask LoadMask(string path)
        {
            var mask = JsonConvert.DeserializeObject<HandPosePotentialContactMask>(File.ReadAllText(path));
            ValidateBinding(mask);
            return mask;
        }

        public void ValidateBinding(HandPosePotentialContactMask mask)
        {
            if (mask == null || mask.Version != HandPosePotentialContactMask.CurrentVersion ||
                mask.Metadata == null || mask.Metadata.BindingFingerprint != BindingFingerprint)
                throw new ArgumentException("Версия или mesh binding маски не совпадает со снимком; загрузка отклонена.");
            var validation = Evaluate(mask, new HandPosePotentialContactCoverageSettings { Subdivisions = 1, CheckConvergence = false });
            if (validation.Errors.Count > 0) throw new ArgumentException(string.Join("\n", validation.Errors));
        }

        /// <summary>Сохраняет доказательства статического снимка; PNG передаётся из настоящего Unity preview.</summary>
        public string Export(string directory, HandPosePotentialContactMask mask,
            HandPosePotentialContactCoverageReport coverage, byte[] previewPng)
        {
            ValidateBinding(mask);
            if (previewPng == null || previewPng.Length == 0) throw new ArgumentException("Нужен реальный PNG предпросмотра.");
            // Пересчитываем по текущей маске: устаревший отчёт вызывающего кода не публикуется.
            coverage = Evaluate(mask, coverage == null ? null : new HandPosePotentialContactCoverageSettings
            {
                ContactBandMm = coverage.ContactBandMm, Subdivisions = coverage.Subdivisions,
                CheckConvergence = coverage.CoarseSubdivisions > 0, MaxSampleQueries = coverage.MaxSampleQueries,
                MinimumNormalOpposition = coverage.MinimumNormalOpposition
            });
            directory = SafeOutputDirectory(directory);
            Directory.CreateDirectory(directory);
            Write(Path.Combine(directory, "mask.json"), JsonConvert.SerializeObject(mask, FitUnityJsonConverter.Settings));
            Write(Path.Combine(directory, "coverage.json"), JsonConvert.SerializeObject(new
            {
                frameMode = FrameMode, bindingDescription = BindingDescription,
                source = new { SourceReport.Avatar, SourceReport.Object, SourceReport.Pose,
                    SourceReport.CaptureFrame, SourceReport.CreatedUtc, SourceReport.AppliedBlend, SourceReport.Settings },
                coverage
            }, FitUnityJsonConverter.Settings));
            File.WriteAllBytes(Path.Combine(directory, "mask-preview.png"), previewPng);
            var text = new StringBuilder("# Потенциальная контактная область кисти\n\n")
                .Append("Режим: `prefab_static`. Ручная маска целых исходных треугольников; это не runtime-проверка и не приёмка хвата.\n\n")
                .Append("Источник позы: `").Append(SourceReport.Pose?.Path).Append("`; аватар: `").Append(SourceReport.Avatar?.Path)
                .Append("`; предмет: `").Append(SourceReport.Object?.Path).Append("`. GUID/localId/hash сохранены в coverage.json.\n\n")
                .Append("Binding: `").Append(BindingFingerprint).Append("`. Reviewed: ").Append(mask.Metadata.Reviewed).Append(".\n\n")
                .Append("![Размеченная кожа кисти](").Append(Path.GetFullPath(Path.Combine(directory, "mask-preview.png")).Replace('\\', '/')).Append(")\n\n")
                .Append("| Область | Потенциальная площадь, мм² | В пределах ±").Append(Number(coverage.ContactBandMm)).Append(" мм, % | Эталонно-взвешенные, % | С противоположной нормалью, % | Границы дискретизации, % |\n")
                .Append("|---|---:|---:|---:|---:|---:|\n");
            foreach (var r in coverage.Regions.Concat(coverage.Total == null ? Enumerable.Empty<HandPosePotentialContactCoverageRegion>() : new[] { coverage.Total }))
                text.Append('|').Append(r.Region).Append('|').Append(Number(r.CurrentCoveragePercent.HasValue ? (double?)r.CurrentPotentialAreaMm2 : null))
                    .Append('|').Append(Number(r.CurrentCoveragePercent)).Append('|').Append(Number(r.ReferenceWeightedContactPercent))
                    .Append('|').Append(Number(r.QualifiedCoveragePercent)).Append('|').Append(Number(r.ContactPercentLowerBound))
                    .Append(" … ").Append(Number(r.ContactPercentUpperBound)).Append("|\n");
            text.Append("\nЗнак не сертифицирован: gap/deep/полная глубина — unknown. Qualified = band и противоположные нормали; критерий нормали: dot <= -")
                .Append(coverage.MinimumNormalOpposition.ToString(CultureInfo.InvariantCulture)).Append(".\n\n")
                .Append(coverage.PrecisionMeaning).Append("\n\nСтатус: ").Append(coverage.Status)
                .Append(". Сравнение до/после в этом отчёте не заявляется.\n");
            if (coverage.Errors.Count > 0) text.Append("\nОшибки: ").Append(string.Join("; ", coverage.Errors)).Append('\n');
            Write(Path.Combine(directory, "report.md"), text.ToString());
            return Path.Combine(directory, "report.md");
        }

        public static string Number(double? value) => value.HasValue ? value.Value.ToString("0.###", CultureInfo.InvariantCulture) : "unknown";
        public static string SafeOutputDirectory(string path)
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetFullPath("tasks/hand-rig-quality/reports").TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Файлы разметки сохраняются только в tasks/hand-rig-quality/reports/.");
            return full;
        }
        static void Write(string path, string text) => File.WriteAllText(path, text.Replace("\r\n", "\n"), new UTF8Encoding(false));
    }
}
