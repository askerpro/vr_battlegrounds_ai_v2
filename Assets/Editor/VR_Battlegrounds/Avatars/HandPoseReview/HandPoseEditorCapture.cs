using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using UltimateXR.Avatar;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.HandPoseReview
{
    /// <summary>Читает текущее состояние сцены, включая несохранённые кости; не применяет pose/snap.</summary>
    public static class HandPoseEditorCapture
    {
        public static HandPoseFitSnapshot Capture(UxrAvatar avatar, UxrHandPoseAsset pose, float blend,
            GameObject root, HandPoseFitRequest settings, out HandPoseFitReport report, HandPoseFitSnapshot objectCache=null, HandPoseFitReport objectMetadata=null)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Для editor-снимка завершите Play/compile/import.");
            if (!avatar || !pose || !root)
                throw new ArgumentException("Выберите текущий сценовый аватар, позу SDK и сценовый предмет.");
            var request = settings.Copy();
            HandPoseFitAnalyzer.ValidateSettings(request);
            report = new HandPoseFitReport { CreatedUtc = DateTime.UtcNow.ToString("O"), UnityVersion = Application.unityVersion, Settings = request };
            var snapshot = new HandPoseFitSnapshot(avatar, pose, blend, root, request, report, objectCache, objectMetadata);
            report.EditorPoseDirty = EditorUtility.IsDirty(pose);
            report.EditorPoseStateJson = EditorJsonUtility.ToJson(pose, false);
            report.EditorObjectSnapshotHash = objectMetadata?.EditorObjectSnapshotHash ?? Hash(snapshot.ObjectSurface);
            report.EditorContactObjectHash = objectMetadata?.EditorContactObjectHash ?? (snapshot.ContactObject.Count==snapshot.ObjectSurface.Count ? report.EditorObjectSnapshotHash : Hash(snapshot.ContactObject));
            var geometryHash = Hash(snapshot.Hand) + "|" + report.EditorObjectSnapshotHash + "|" + Hash(snapshot.OtherHand);
            using (var hash = SHA256.Create())
                report.EditorSnapshotHash = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(geometryHash + "|" + report.EditorPoseStateJson + "|" + report.EditorAppliedPoseStateJson + "|" + settings.Side + "|" + blend.ToString("R",System.Globalization.CultureInfo.InvariantCulture)))).Replace("-", "").ToLowerInvariant();
            return snapshot;
        }

        public static string Hash(params IEnumerable<FitTriangle>[] surfaces)
        {
            using (var stream = new MemoryStream()) {
                using (var writer = new BinaryWriter(stream, Encoding.UTF8, true)) {
                    foreach (var surface in surfaces) {
                        writer.Write("surface");
                        foreach (var triangle in surface) {
                            foreach (var p in new[] { triangle.A, triangle.B, triangle.C }) { writer.Write(p.x); writer.Write(p.y); writer.Write(p.z); }
                        }
                    }
                }
                stream.Position = 0;
                using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
            }
        }

        public static HandPoseFitReport Export(UxrAvatar avatar, UxrHandPoseAsset pose, float blend,
            GameObject root, HandPoseFitRequest settings, Camera sceneCamera, HandPoseGpuField liveField)
        {
            using(var snapshot=Capture(avatar,pose,blend,root,settings,out var report))
                return ExportCaptured(snapshot,report,root,settings,sceneCamera,liveField);
        }

        /// <summary>Полный анализ и все PNG из того же frozen snapshot, который рисует overlay.</summary>
        public static HandPoseFitReport ExportCaptured(HandPoseFitSnapshot snapshot,HandPoseFitReport report,
            GameObject root,HandPoseFitRequest settings,Camera sceneCamera,HandPoseGpuField liveField)
        {
            var request=settings.Copy();request.RenderImages=true;
            request.OutputDirectory=Path.GetFullPath(Path.Combine("UserReports/HandPoseFit",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")+"-"+Guid.NewGuid().ToString("N").Substring(0,8)));
            report.Settings=request;report.CreatedUtc=DateTime.UtcNow.ToString("O");
            report.LivePreviewMethod="sdk_preview_runtime_skinning; gpu_voxel_distance_field_approximate; full_metrics_cpu_geometry";
            if(liveField!=null){report.LiveFieldVoxelMm=liveField.VoxelMetres*1000;report.LiveFieldErrorMm=liveField.ErrorMetres*1000;}
            foreach(var surface in report.Surfaces) {
                var triangles=surface.Kind=="hand"?snapshot.Hand:snapshot.ObjectSurface;
                surface.Topology=HandPoseFitGeometry.Topology(triangles.Where(t=>t.Source==surface.Path).ToArray());
            }
            HandPoseFitAnalyzer.AnalyzeCaptured(snapshot,report);
            if(sceneCamera && report.Error==null) {
                var frame=Matrix4x4.TRS(root.transform.position,root.transform.rotation,Vector3.one);
                HandPoseFitRenderer.RenderEditorView(snapshot,report,
                    frame.inverse.MultiplyPoint3x4(sceneCamera.transform.position),Quaternion.Inverse(root.transform.rotation)*sceneCamera.transform.rotation,
                    sceneCamera.orthographic,sceneCamera.orthographicSize,sceneCamera.fieldOfView);
                File.WriteAllText(report.JsonPath,JsonConvert.SerializeObject(report,FitUnityJsonConverter.Settings),new UTF8Encoding(false));
            }
            return report;
        }
    }
}
