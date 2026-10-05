using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using VrBattlegrounds.Weapons.Sights;
using Report = VrBattlegrounds.Editor.Weapons.Calibration.WeaponSightAuditReport;

namespace VrBattlegrounds.Editor.Weapons.Calibration
{
    /// <summary>Читает resolved prefab assets без Instantiate/Awake и без сохранения исходников.</summary>
    public static class WeaponSightAudit
    {
        public const string SettingsPath = "Assets/Data/Weapons/SightCalibration/WeaponSightCalibrationSettings.asset";
        public const string RegistryPath = "Assets/Data/Weapons/Resources/WeaponRegistry.asset";
        public const string ReportPath = "tmp/weapon-sight-calibration/audit.json";

        [MenuItem("Tools/VR Battlegrounds/Weapons/Sight Calibration/Audit")]
        public static void RunMenu()
        {
            var report = Run();
            GameLog.WeaponSystem.Info($"[WeaponSightAudit] {(report.passed ? "PASS inventory" : "FAIL inventory")}: {report.weapons.Count}/{report.registryCount}; ошибок {report.failures.Count}; {ReportPath}. Прицелы требуют разметки.");
        }

        public static Report Run()
        {
            var report = Capture(AssetDatabase.LoadAssetAtPath<WeaponRegistry>(RegistryPath),
                AssetDatabase.LoadAssetAtPath<WeaponSightCalibrationSettings>(SettingsPath));
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, JsonUtility.ToJson(report, true));
            return report;
        }

        public static Report Capture(WeaponRegistry registry, WeaponSightCalibrationSettings settings)
        {
            var report = new Report { observedAtUtc = DateTime.UtcNow.ToString("O"), registry = Identity(registry), settings = Identity(settings) };
            if (registry == null) report.failures.Add("MissingRegistry");
            if (settings == null || !WeaponSightCalibrationSettings.IsValidDistance(settings.DefaultZeroDistance))
                report.failures.Add("InvalidDefaultDistanceOrMissingSettings");
            if (settings != null) report.defaultZeroDistance = settings.DefaultZeroDistance;
            if (registry == null) return report;

            var profiles = AssetDatabase.FindAssets("t:WeaponSightCalibrationProfile")
                .Select(guid => AssetDatabase.LoadAssetAtPath<WeaponSightCalibrationProfile>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(profile => profile != null).OrderBy(profile => AssetDatabase.GetAssetPath(profile), StringComparer.Ordinal).ToArray();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var registered = new HashSet<WeaponInfo>();
            report.registryCount = registry.Count;
            for (int i = 0; i < registry.Count; i++)
            {
                WeaponInfo info = registry.Weapons[i];
                var entry = new Report.WeaponEntry { registryIndex = i };
                report.weapons.Add(entry);
                try
                {
                    if (info == null) throw new InvalidOperationException("MissingWeaponInfo");
                    entry.weaponId = info.WeaponId;
                    entry.info = Identity(info);
                    registered.Add(info);
                    if (string.IsNullOrWhiteSpace(info.WeaponId) || !ids.Add(info.WeaponId))
                        throw new InvalidOperationException("MissingOrDuplicateWeaponId");
                    if (info.WeaponPrefab == null || !PrefabUtility.IsPartOfPrefabAsset(info.WeaponPrefab))
                        throw new InvalidOperationException("MissingOrNonAssetPrefab");
                    CapturePrefab(entry, info.WeaponPrefab, report.failures);
                    CaptureProfiles(entry, info, profiles, settings, report.failures);
                }
                catch (Exception exception)
                {
                    entry.status = "AuditFailed";
                    report.failures.Add($"registry[{i}] {entry.weaponId}: {exception.GetType().Name}: {exception.Message}");
                }
            }
            foreach (var profile in profiles)
                if (!registered.Contains(profile.Weapon)) report.failures.Add("UnregisteredProfileWeapon: " + AssetDatabase.GetAssetPath(profile));
            if (registry.Count == 0) report.failures.Add("EmptyRegistry");
            report.passed = report.failures.Count == 0;
            return report;
        }

        private static void CapturePrefab(Report.WeaponEntry entry, GameObject prefab, List<string> failures)
        {
            entry.prefab = Identity(prefab);
            entry.rootScale = prefab.transform.localScale;
            foreach (var node in prefab.GetComponentsInChildren<Transform>(true)) entry.nodes.Add(Node(node, prefab.transform));
            entry.componentTypes = prefab.GetComponentsInChildren<Component>(true)
                .Select(component => component == null ? "MissingScript" : component.GetType().FullName)
                .Distinct().OrderBy(name => name, StringComparer.Ordinal).ToList();
            if (entry.componentTypes.Contains("MissingScript")) failures.Add(entry.weaponId + ": MissingScript");

            foreach (var source in prefab.GetComponentsInChildren<UxrProjectileSource>(true))
            {
                if (source.ShotTypes == null) throw new InvalidOperationException("MissingShotTypes");
                for (int i = 0; i < source.ShotTypes.Count; i++)
                {
                    var shot = source.ShotTypes[i];
                    if (shot == null || shot.ShotSource == null || shot.ProjectilePrefab == null)
                        throw new InvalidOperationException("IncompleteShotType: " + i);
                    entry.shots.Add(new Report.Shot
                    {
                        sourceComponentPath = PathOf(source.transform, prefab.transform), shotTypeIndex = i,
                        origin = Node(shot.ShotSource, prefab.transform), tip = Node(shot.Tip, prefab.transform),
                        prefabPosition = shot.ShotSource.position, prefabDirection = shot.ShotSource.forward,
                        automaticTrajectory = shot.UseAutomaticProjectileTrajectory, speed = shot.ProjectileSpeed,
                        projectile = Identity(shot.ProjectilePrefab)
                    });
                }
            }
            foreach (var firearm in prefab.GetComponentsInChildren<UxrFirearmWeapon>(true))
            {
                var triggers = new SerializedObject(firearm).FindProperty("_triggers");
                var source = firearm.GetComponent<UxrProjectileSource>();
                if (triggers == null || !triggers.isArray || source == null) throw new InvalidOperationException("UnresolvedTriggerContract");
                for (int i = 0; i < triggers.arraySize; i++)
                {
                    var index = triggers.GetArrayElementAtIndex(i).FindPropertyRelative("_projectileShotIndex");
                    if (index == null || index.intValue < 0 || index.intValue >= source.ShotTypes.Count)
                        throw new InvalidOperationException("InvalidTriggerShotType: " + i);
                    entry.triggers.Add(new Report.Trigger { firearmPath = PathOf(firearm.transform, prefab.transform), triggerIndex = i, shotTypeIndex = index.intValue });
                }
            }
            if (entry.shots.Count == 0 || entry.triggers.Count == 0) throw new InvalidOperationException("NoShotsOrTriggers");

            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                Mesh mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : filter != null ? filter.sharedMesh : null;
                if (mesh == null) continue;
                using (var data = MeshUtility.AcquireReadOnlyMeshData(mesh))
                {
                    if (data.Length != 1 || data[0].vertexCount != mesh.vertexCount)
                        throw new InvalidOperationException("MeshDataMismatch: " + mesh.name);
                }
                entry.geometry.Add(new Report.Geometry
                {
                    nodePath = PathOf(renderer.transform, prefab.transform), rendererType = renderer.GetType().FullName,
                    mesh = Identity(mesh), vertexCount = mesh.vertexCount, subMeshCount = mesh.subMeshCount,
                    localBounds = mesh.bounds, isReadable = mesh.isReadable,
                    materials = renderer.sharedMaterials.Select(Identity).ToList()
                });
            }
            if (entry.geometry.Count == 0) throw new InvalidOperationException("NoResolvedGeometry");
        }

        private static void CaptureProfiles(Report.WeaponEntry entry, WeaponInfo info, WeaponSightCalibrationProfile[] profiles,
            WeaponSightCalibrationSettings settings, List<string> failures)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var profile in profiles.Where(profile => profile.Weapon == info))
            {
                bool validDistance = profile.TryGetZeroDistance(settings, out float distance);
                string key = profile.SightId + ":" + profile.TriggerIndex + ":" + profile.ShotTypeIndex;
                bool valid = validDistance && !string.IsNullOrWhiteSpace(profile.SightId) && keys.Add(key)
                    && entry.triggers.Count(t => t.triggerIndex == profile.TriggerIndex && t.shotTypeIndex == profile.ShotTypeIndex) == 1;
                entry.profiles.Add(new Report.Profile
                {
                    asset = Identity(profile), sightId = profile.SightId, triggerIndex = profile.TriggerIndex,
                    shotTypeIndex = profile.ShotTypeIndex, usesCustomDistance = profile.OverridesZeroDistance,
                    zeroDistance = distance, status = valid ? "NeedsReferenceReview" : "InvalidConfiguration"
                });
                if (!valid) failures.Add(entry.weaponId + ": InvalidProfile " + AssetDatabase.GetAssetPath(profile));
            }
        }

        private static Report.AssetIdentity Identity(UnityEngine.Object asset)
        {
            if (asset == null) return null;
            string path = AssetDatabase.GetAssetPath(asset);
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long id) || string.IsNullOrEmpty(path))
                throw new InvalidOperationException("NonPersistentIdentity: " + asset.name);
            return new Report.AssetIdentity { path = path, guid = guid, localFileId = id, dependencyHash = AssetDatabase.GetAssetDependencyHash(path).ToString() };
        }

        private static Report.Node Node(Transform node, Transform root)
        {
            return node == null ? null : new Report.Node { identity = Identity(node), semanticPath = PathOf(node, root),
                localPosition = node.localPosition, localRotation = node.localRotation, localScale = node.localScale };
        }

        private static string PathOf(Transform node, Transform root)
        {
            var parts = new List<string>();
            while (node != null)
            {
                parts.Add(node.name + "[" + node.GetSiblingIndex() + "]");
                if (node == root) { parts.Reverse(); return string.Join("/", parts); }
                node = node.parent;
            }
            throw new InvalidOperationException("ReferenceOutsideWeaponRoot");
        }
    }
}
