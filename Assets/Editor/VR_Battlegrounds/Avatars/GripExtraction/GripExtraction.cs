using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using UltimateXR.Core;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>
    /// Grip Extraction Tools — вход для execute_code: один конвейер «источник хвата → профиль кисти цели».
    /// Разбор и план: tasks/hand-rig-quality/reports/cyborg-fit/pipeline-design.md, Details.md §«Grip Extraction Tools».
    ///
    /// <para>
    /// <c>VrBattlegrounds.Editor.Avatars.GripExtraction.RunJson("{\"recipes\":[\"all\"],\"target\":\"CyborgHands\",\"mode\":\"measure\",\"outDir\":\"…\"}")</c>
    /// — запрос <see cref="GripExtractionRequest" />, полный отчёт <see cref="GripExtractionReport" /> пишется в outDir
    /// (<c>grip-extraction-&lt;mode&gt;.json</c>), возвращается сводка (или полный отчёт при <c>returnFull</c>).
    /// </para>
    ///
    /// <para>
    /// Профиля MEF и сверки R0 с прежним импортёром (align <c>Grabs/HandsPack/&lt;поза&gt;/MEF_Base_Avatar</c>) больше нет:
    /// аватар хвата MEF выведен (решение пользователя 2026-10-23), его записи сняты с оружия.
    /// </para>
    /// </summary>
    public static class GripExtraction
    {
        private static readonly JsonSerializerSettings Json = new JsonSerializerSettings
        {
            ContractResolver  = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false, OverrideSpecifiedNames = true } },
            Formatting        = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        public static string RunJson(string requestJson)
        {
            var                  request = JsonConvert.DeserializeObject<GripExtractionRequest>(requestJson, Json);
            GripExtractionReport report  = Run(request);
            return request != null && request.ReturnFull ? JsonConvert.SerializeObject(report, Json) : JsonConvert.SerializeObject(Summary(report), Json);
        }

        public static GripExtractionReport Run(GripExtractionRequest request)
        {
            var report = new GripExtractionReport { CreatedUtc = DateTime.UtcNow.ToString("O"), Request = request, Status = "error" };
            try
            {
                if (request == null) throw new ArgumentException("Пустой запрос.");
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                {
                    throw new InvalidOperationException("Редактор занят Play/compile/import.");
                }

                GripExtractionMode mode = request.ParseMode();
                GripTargetProfile profile = GripTargetProfile.Get(request.Target)
                                                             .With(request.ParseAnchor(), request.ParseEncoding(), request.RegisterPoses, request.RemoveReplacedAligns);
                report.Mode            = mode.ToString();
                report.Source          = request.Source;
                report.Target          = profile.Name;
                report.Anchor          = profile.Anchor.ToString();
                report.Encoding        = profile.Encoding.ToString();
                report.RigPrefab       = profile.RigPrefabPath;
                report.RecordAvatar    = profile.RecordAvatarPath;
                report.PoseRegistry    = profile.PoseRegistryPath;
                report.OutputDirectory = ResolveOutDir(request.OutDir, mode);
                report.RecordGuid      = profile.ResolveRecordGuid();

                var options = new GripExtractionOptions
                {
                    Profile              = profile,
                    RecordGuid           = report.RecordGuid,
                    NormalOffsetMm       = request.NormalOffsetMm,
                    MaxNormalOffsetMm    = request.MaxNormalOffsetMm,
                    NormalOffsetMmByHand = request.NormalOffsetMmByHand,
                    CurlDegByHand        = request.CurlDegByHand
                };

                using (IGripSource source = CreateSource(request.Source))
                using (var rig = new GripTargetRig(profile.RigPrefabPath))
                {
                    CheckRig(profile, rig, report.RecordGuid, report.Warnings);
                    foreach (GripSourcePoint point in source.Select(request.Recipes))
                    {
                        try
                        {
                            report.Points.Add(GripExtractor.Extract(source, point, rig, options));
                        }
                        catch (Exception e)
                        {
                            GameLog.Debug.Warning($"[GripExtraction] {point.Pose}: {e.Message}");
                            report.Points.Add(new GripExtractionResult { Pose = point.Pose, SourceKind = point.SourceKind, Weapon = point.Weapon, GrabPoint = point.GrabPoint, Error = e.Message, Point = point });
                        }
                    }
                }

                if (mode != GripExtractionMode.Measure)
                {
                    Write(mode, profile, report, request);
                }

                report.Status = report.Points.Any(p => p.Error != null) ? "partial" : "ok";
            }
            catch (Exception e)
            {
                report.Status = "error";
                report.Error  = e.ToString();
                GameLog.Error($"[GripExtraction] {e.Message}");
            }

            if (!string.IsNullOrEmpty(report.OutputDirectory))
            {
                report.ReportPath = Path.Combine(report.OutputDirectory, $"grip-extraction-{(report.Mode ?? "run").ToLowerInvariant()}.json");
                File.WriteAllText(report.ReportPath, JsonConvert.SerializeObject(report, Json), new UTF8Encoding(false));
            }

            GameLog.Debug.Info($"[GripExtraction] {report.Status}: {report.Mode} {report.Target}/{report.Anchor}, точек {report.Points.Count}, " +
                               $"записано {report.Written.Count}, предупреждений {report.Warnings.Count} → {report.ReportPath}");
            return report;
        }

        private static IGripSource CreateSource(string source)
        {
            if (string.IsNullOrEmpty(source) || string.Equals(source, PackClipGripSource.SourceId, StringComparison.OrdinalIgnoreCase))
            {
                return new PackClipGripSource();
            }

            // Точки расширения IGripSource: avatar-record (запись хвата другого аватара), manual (grip-authoring).
            throw new ArgumentException($"source: «{source}» пока не реализован (есть {PackClipGripSource.SourceId}).");
        }

        /// <summary>Сторожа профиля: GUID записи в цепочке рига, пястные, сочетание кодирования и рига.</summary>
        private static void CheckRig(GripTargetProfile profile, GripTargetRig rig, string recordGuid, List<string> warnings)
        {
            List<string> chain = rig.PrefabGuidChain();
            if (!chain.Contains(recordGuid))
            {
                throw new ArgumentException($"{profile.Name}: GUID записи {recordGuid} не в цепочке префабов рига {profile.RigPrefabPath} ({string.Join(", ", chain)}).");
            }

            foreach (UxrHandSide side in GripTargetRig.Sides)
            {
                bool metacarpals = rig.HasMetacarpals(side);
                if (profile.RequireMetacarpals && !metacarpals)
                {
                    throw new ArgumentException($"{profile.Name}: у рига {profile.RigPrefabPath} нет пястных {side}.");
                }

                if (metacarpals && profile.Encoding == GripPoseEncoding.AsSource)
                {
                    warnings.Add($"{side}: риг с пястными, поза пишется как есть (без пястных) — рантайм SDK исказит пальцы (pose-transfer-diagnosis); нужен encoding=rig.");
                }
            }
        }

        /// <summary>Preview — копия в памяти, без сохранения; Apply — позы, регистрация (по профилю), align и записи, Hand Pose Fit по флагу.</summary>
        private static void Write(GripExtractionMode mode, GripTargetProfile profile, GripExtractionReport report, GripExtractionRequest request)
        {
            bool                       save   = mode == GripExtractionMode.Apply;
            List<GripExtractionResult> failed = report.Points.Where(p => p.Error != null).ToList();
            if (save && failed.Count > 0)
            {
                throw new InvalidOperationException($"Apply отменён: точки с ошибкой расчёта ({string.Join(", ", failed.Select(p => p.Pose))}).");
            }

            List<GripExtractionResult> points     = report.Points.Where(p => p.Error == null).ToList();
            var                        poses      = new Dictionary<string, UxrHandPoseAsset>();
            var                        transients = new List<UxrHandPoseAsset>();
            try
            {
                foreach (GripExtractionResult point in points)
                {
                    UxrHandDescriptor left  = point.Hand(nameof(UxrHandSide.Left)).Descriptor;
                    UxrHandDescriptor right = point.Hand(nameof(UxrHandSide.Right)).Descriptor;
                    if (save)
                    {
                        poses[point.Pose] = GripPoseAssets.Write(profile.PoseFolderFor(point.Point), point.Pose, left, right);
                        point.Written.Add(point.PosePath);
                        report.Written.Add(point.PosePath);
                    }
                    else
                    {
                        UxrHandPoseAsset transient = GripPoseAssets.CreateTransient(point.Pose, left, right);
                        transients.Add(transient);
                        poses[point.Pose] = transient;
                    }
                }

                if (save)
                {
                    // Сохраняем только позы запроса: общая запись затронула бы чужие dirty assets.
                    foreach (UxrHandPoseAsset pose in poses.Values) AssetDatabase.SaveAssetIfDirty(pose);
                }

                if (save && profile.RegisterPoses)
                {
                    Dictionary<string, string> before = GripPrefabIdentityGuard.Snapshot(profile.PoseRegistryPath);
                    HandsPackPoseImporter.RegisterOnPoseBase(profile.PoseRegistryPath, poses.Values);
                    GripPrefabIdentityGuard.Verify(profile.PoseRegistryPath, before);
                    report.Written.Add($"{profile.PoseRegistryPath}: _handPoses (недостающие)");
                }
                else
                {
                    // Preview сверяет ассет, который перезапишет Apply (если он уже есть).
                    IEnumerable<UxrHandPoseAsset> check = points.Select(p => save ? poses[p.Pose] : AssetDatabase.LoadAssetAtPath<UxrHandPoseAsset>(p.PosePath) ?? poses[p.Pose]);
                    report.Warnings.AddRange(GripPoseAssets.CheckRegistered(profile.PoseRegistryPath, check));
                }

                foreach (IGrouping<string, GripExtractionResult> weapon in points.GroupBy(p => p.Weapon))
                {
                    List<string> lines = GripRecordWriter.WriteWeapon(weapon.Key, weapon.ToList(), profile, report.RecordGuid, poses, save, report.Warnings);
                    report.Written.AddRange(save ? lines : lines.Select(l => "preview (не сохранено): " + l));
                    if (save)
                    {
                        foreach (GripExtractionResult point in weapon) point.Written.Add(weapon.Key);
                    }
                }
            }
            finally
            {
                foreach (UxrHandPoseAsset transient in transients) UnityEngine.Object.DestroyImmediate(transient);
            }

            if (save && request.Analyze)
            {
                foreach (GripExtractionResult point in points)
                {
                    foreach (GripHandResult hand in point.Hands)
                    {
                        var side = (UxrHandSide)Enum.Parse(typeof(UxrHandSide), hand.Side);
                        hand.Fit = GripFitCheck.Run(profile, point, side, hand.NormalOffsetMm, report.OutputDirectory);
                    }
                }
            }
        }

        /// <summary>Отчёт — вне Assets (как Hand Pose Fit), чтобы не запускать импорт.</summary>
        private static string ResolveOutDir(string outDir, GripExtractionMode mode)
        {
            string directory = string.IsNullOrEmpty(outDir)
                                   ? Path.Combine("Temp/GripExtraction", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + mode.ToString().ToLowerInvariant())
                                   : outDir;
            directory = Path.GetFullPath(directory);
            string assets = Path.GetFullPath("Assets").TrimEnd(Path.DirectorySeparatorChar);
            if (directory.Equals(assets, StringComparison.OrdinalIgnoreCase) || directory.StartsWith(assets + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("outDir: отчёт хранится вне Assets.");
            }

            Directory.CreateDirectory(directory);
            return directory;
        }

        /// <summary>Сводка для execute_code: статус, до 10 примеров, строка на руку.</summary>
        private static object Summary(GripExtractionReport r) =>
            new
            {
                r.Status,
                r.Error,
                r.Mode,
                r.Target,
                r.Anchor,
                r.Encoding,
                r.ReportPath,
                Points          = r.Points.Count,
                Errors          = r.Points.Where(p => p.Error != null).Select(p => $"{p.Pose}: {p.Error}").Take(10).ToList(),
                Warnings        = r.Warnings.Count,
                WarningExamples = r.Warnings.Take(10).ToList(),
                Written         = r.Written.Count,
                WrittenExamples = r.Written.Take(10).ToList(),
                Hands = r.Points.SelectMany(p => p.Hands.Select(h => new
                {
                    p.Pose,
                    h.Side,
                    PalmMm        = h.Palm?.ErrorMm,
                    LegacyMm      = h.VsLegacyWrist?.Mm,
                    LegacyDeg     = h.VsLegacyWrist?.Deg,
                    ExistingMm    = (h.ExistingNamed ?? h.ExistingRecord)?.DeltaMm,
                    ExistingDeg   = (h.ExistingNamed ?? h.ExistingRecord)?.DeltaDeg,
                    ExistingPalmMm = (h.ExistingNamed ?? h.ExistingRecord)?.Palm?.ErrorMm,
                    h.RuntimeVsWorldDeg,
                    h.PoseVsExistingDeg,
                    Fit             = h.Fit?.Overall,
                    NextOffsetMm    = h.Fit?.SuggestedTotalNormalOffsetMm
                })).ToList()
            };
    }
}
