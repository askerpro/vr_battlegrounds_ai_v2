using System;
using System.Collections.Generic;
using System.Linq;
using UltimateXR.Manipulation.HandPoses;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.Avatars.Workbench
{
    public sealed class AvatarInspectionReport
    {
        public readonly List<string> Findings = new List<string>();
        public Component[] Components = Array.Empty<Component>();
        public Renderer[] Renderers = Array.Empty<Renderer>();
        public UxrHandPoseAsset[] Poses = Array.Empty<UxrHandPoseAsset>();
        public readonly List<AvatarPoseEntry> PoseEntries = new List<AvatarPoseEntry>();
        public int SkinnedMeshes, Vertices, MaterialSlots, LodGroups;
        public string HandMode, BodyOwner;
    }

    public sealed class AvatarPoseEntry
    {
        public UxrHandPoseAsset Pose;
        public string OwnerPath;
        public bool Inherited;
    }

    /// <summary>Чистое чтение состава. Отчёт не вызывает установщики и не сохраняет ассеты.</summary>
    public static class AvatarInspection
    {
        public static AvatarInspectionReport Read(AvatarEditorContext context)
        {
            var report = new AvatarInspectionReport();
            if (context == null || !context.IsValid) { report.Findings.Add("Цель больше не существует; выберите её снова."); return report; }
            var root = context.Root;
            report.Components = root.GetComponentsInChildren<Component>(true).Where(c => c).ToArray();
            report.Renderers = root.GetComponentsInChildren<Renderer>(true);
            report.SkinnedMeshes = report.Renderers.OfType<SkinnedMeshRenderer>().Count();
            report.Vertices = report.Renderers.OfType<SkinnedMeshRenderer>().Sum(r => r.sharedMesh ? r.sharedMesh.vertexCount : 0);
            report.MaterialSlots = report.Renderers.Sum(r => r.sharedMaterials.Length);
            report.LodGroups = root.GetComponentsInChildren<LODGroup>(true).Length;
            report.HandMode = report.Components.OfType<Transform>().Any(t => t.name == "BigIKHandLeft" || t.name == "BigIKHandRight")
                ? "Кисти SDK (по составу)" : context.Avatar ? "Родные / другой риг — проверить" : "Ещё не настроены";
            report.BodyOwner = AvatarLegsSetup.BodyOwnerOf(context.AssetPath) ?? context.AssetPath;
            if (!context.Avatar) { report.Findings.Add("Модель ещё не имеет UxrAvatar. Игровая композиция и позы недоступны."); return report; }
            if (!root.GetComponentInChildren<Camera>(true)) report.Findings.Add("Не найдена камера аватара.");
            if (!Has(report, "UxrStandardAvatarController")) report.Findings.Add("Не найден стандартный controller UltimateXR.");
            if (!Has(report, "NetworkIdentity")) report.Findings.Add("Не найден NetworkIdentity. Регистрацию игрового аватара нужно проверить отдельно.");
            int missing = root.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            if (missing > 0) report.Findings.Add("Компоненты с потерянным script: " + missing);
            var rig = context.Avatar.AvatarRig;
            if (rig?.LeftArm?.Hand?.Wrist == null || rig?.RightArm?.Hand?.Wrist == null) report.Findings.Add("В UXR-риге отсутствует левая или правая кисть.");
            foreach (var path in context.PrefabChain.DefaultIfEmpty(context.AssetPath))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var avatar = prefab ? prefab.GetComponentInChildren<UltimateXR.Avatar.UxrAvatar>(true) : null;
                if (!avatar) continue;
                var property = new SerializedObject(avatar).FindProperty("_handPoses");
                if (property == null) continue;
                for (int i = 0; i < property.arraySize; i++)
                {
                    var pose = property.GetArrayElementAtIndex(i).objectReferenceValue as UxrHandPoseAsset;
                    if (!pose) continue;
                    var entry = report.PoseEntries.FirstOrDefault(e => e.Pose.name == pose.name);
                    if (entry == null) report.PoseEntries.Add(new AvatarPoseEntry { Pose = pose, OwnerPath = path, Inherited = path != context.AssetPath });
                    else if (entry.Pose == pose) { entry.OwnerPath = path; entry.Inherited = path != context.AssetPath; }
                }
            }
            // У несохранённого экземпляра могут быть позы, ещё не попавшие в prefab chain.
            var ownPoses = new SerializedObject(context.Avatar).FindProperty("_handPoses");
            if (ownPoses != null)
                for (int i = 0; i < ownPoses.arraySize; i++)
                {
                    var pose = ownPoses.GetArrayElementAtIndex(i).objectReferenceValue as UxrHandPoseAsset;
                    if (!pose) continue;
                    var existing = report.PoseEntries.FirstOrDefault(e => e.Pose.name == pose.name);
                    if (existing != null && existing.Pose == pose) continue;
                    if (existing != null) report.PoseEntries.Remove(existing);
                    report.PoseEntries.Insert(0, new AvatarPoseEntry { Pose = pose, OwnerPath = context.IsAsset ? context.AssetPath : "Текущий экземпляр" });
                }
            report.Poses = report.PoseEntries.Select(e => e.Pose).ToArray();
            if (report.Poses.Length == 0) report.Findings.Add("Позы кистей не найдены.");
            return report;
        }

        public static bool Has(AvatarInspectionReport report, string type) => report.Components.Any(c => c.GetType().Name == type);
    }
}
