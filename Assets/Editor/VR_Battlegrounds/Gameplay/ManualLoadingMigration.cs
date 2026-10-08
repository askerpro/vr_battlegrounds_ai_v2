using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Weapons;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>Адресный preflight/apply двух явно выбранных семейств. Вызывать только в idle Editor/own lease.</summary>
    public static class ManualLoadingMigration
    {
        private sealed class Entry
        {
            public string Info, Weapon, Cartridge, Profile, AmmoType, InsertClip; public int Capacity; public WeaponEmptyPose Empty;
        }
        private static readonly Entry[] Entries =
        {
            new Entry { Info="Assets/Data/Weapons/ShotgunReal_Weapon.asset", Weapon="Assets/Prefabs/Weapons/FabarmSDASS/FabarmSDASS.prefab",
                Cartridge="Assets/Prefabs/Weapons/FabarmSDASS/FabarmSDASS_Ammo.prefab", Profile="Assets/Data/Weapons/Profiles/ManualLoadFabarmReadiness.asset",
                AmmoType="FabarmSDASS", Capacity=8, Empty=WeaponEmptyPose.ReturnToRest,
                InsertClip="Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Sounds/10Shotgun_Set/Reload_1.mp3" },
            new Entry { Info="Assets/Data/Weapons/Herrington_Weapon.asset", Weapon="Assets/Prefabs/Weapons/Herrington/Herrington.prefab",
                Cartridge="Assets/Prefabs/Weapons/Herrington/Herrington_mag.prefab", Profile="Assets/Data/Weapons/Profiles/ManualLoadHerringtonReadiness.asset",
                AmmoType="Herrington", Capacity=7, Empty=WeaponEmptyPose.HoldOpen,
                InsertClip="Assets/Audio/SFX/Weapons/Kinemation/Herrington/Herrington_MagIn.wav" }
        };
        private const string Folder = "Assets/Data/Weapons/Profiles";
        private sealed class PreflightReport
        {
            public bool passed;
            public int checkedWeapons;
            public List<string> failures;
            public List<object> rows;
            public string scope;
        }

        [MenuItem("Tools/VR Battlegrounds/Gameplay/Manual Loading/Preflight")]
        public static void PreflightMenu() { SaveReport("tmp/manual-loading-prefab-preflight.json", Preflight()); }

        public static object Preflight()
        {
            EnsureIdle();
            var failures = new List<string>(); var rows = new List<object>();
            foreach (var entry in Entries)
            {
                GameObject root = null; WeaponReadinessProfile transient = null;
                try
                {
                    var info = Required<WeaponInfo>(entry.Info);
                    if (EditorUtility.IsDirty(info) || info.MagazineSize != entry.Capacity || info.ReserveAmmo != 32 ||
                        AssetDatabase.GetAssetPath(info.WeaponPrefab) != entry.Weapon || AssetDatabase.GetAssetPath(info.MagazinePrefab) != entry.Cartridge)
                        throw new InvalidOperationException("Dirty/stale weapon binding or capacity/reserve config: " + entry.Info);
                    var profile = AssetDatabase.LoadAssetAtPath<WeaponReadinessProfile>(entry.Profile);
                    if (profile == null)
                    {
                        CheckAbsent(entry.Profile);
                        profile = transient = ScriptableObject.CreateInstance<WeaponReadinessProfile>();
                        ConfigureProfile(profile, entry.Empty);
                    }
                    else CheckProfile(profile, entry.Empty);
                    root = PrefabUtility.LoadPrefabContents(entry.Weapon);
                    string rootGuid = AssetDatabase.AssetPathToGUID(entry.Weapon);
                    uint networkAssetId = root.GetComponent<Mirror.NetworkIdentity>().assetId;
                    Vector3 scale = root.transform.localScale;
                    ManualLoadingAuthoring.ConfigureWeapon(root, entry.AmmoType, entry.Capacity, profile,
                        AssetDatabase.LoadAssetAtPath<AudioClip>(entry.InsertClip));
                    rows.Add(new { entry.Weapon, entry.Cartridge, entry.Capacity, rootGuid, networkAssetId,
                        scale = new[] { scale.x, scale.y, scale.z }, actionBindings = root.GetComponent<WeaponSystem>().Rig.ActionBindings.Length,
                        retirementEnabled=true, nativeRuntimeProof=false });
                }
                catch (Exception exception) { failures.Add(entry.Weapon + ": " + exception.Message); }
                finally { if (root != null) PrefabUtility.UnloadPrefabContents(root); if (transient != null) Object.DestroyImmediate(transient); }
            }
            return new PreflightReport { passed = failures.Count == 0, checkedWeapons = rows.Count, failures=failures, rows=rows,
                scope = "Authoring preflight only; no SDK/runtime/Quest acceptance" };
        }

        // Source-wave не вызывает Apply. Отдельный prepared ownlease пакет должен принять exact assets и preflight.
        public static object Apply()
        {
            EnsureIdle();
            var preflight = (PreflightReport)Preflight();
            if (!preflight.passed) throw new InvalidOperationException("Preflight refused: " + string.Join("; ", preflight.failures));
            foreach (var entry in Entries)
            {
                var info = Required<WeaponInfo>(entry.Info);
                if (EditorUtility.IsDirty(info) || EditorUtility.IsDirty(Required<GameObject>(entry.Weapon)) ||
                    EditorUtility.IsDirty(Required<GameObject>(entry.Cartridge))) throw new InvalidOperationException("Owned asset is dirty: " + entry.Info);
                if (info.MagazineSize != entry.Capacity || info.ReserveAmmo != 32 ||
                    AssetDatabase.GetAssetPath(info.WeaponPrefab) != entry.Weapon || AssetDatabase.GetAssetPath(info.MagazinePrefab) != entry.Cartridge)
                    throw new InvalidOperationException("Stale explicit weapon data binding.");
            }
            var backups = new Dictionary<string, byte[]>();
            foreach (var entry in Entries)
                foreach (string path in new[] { entry.Info, entry.Weapon, entry.Cartridge })
                { backups.Add(path, File.ReadAllBytes(DiskPath(path))); backups.Add(path + ".meta", File.ReadAllBytes(DiskPath(path + ".meta"))); }
            var created = new List<string>(); bool createdFolder = false; string folderGuid = null;
            try
            {
                if (!AssetDatabase.IsValidFolder(Folder))
                {
                    CheckAbsent(Folder);
                    folderGuid = AssetDatabase.CreateFolder("Assets/Data/Weapons", "Profiles");
                    createdFolder = true;
                }
                foreach (var entry in Entries)
                {
                    var profile = AssetDatabase.LoadAssetAtPath<WeaponReadinessProfile>(entry.Profile);
                    if (profile == null)
                    {
                        CheckAbsent(entry.Profile);
                        profile = ScriptableObject.CreateInstance<WeaponReadinessProfile>();
                        ConfigureProfile(profile, entry.Empty);
                        AssetDatabase.CreateAsset(profile, entry.Profile); created.Add(entry.Profile);
                        AssetDatabase.SaveAssetIfDirty(profile);
                    }
                    CheckProfile(profile, entry.Empty);
                    GameObject root = PrefabUtility.LoadPrefabContents(entry.Weapon);
                    try
                    {
                        ManualLoadingAuthoring.ConfigureWeapon(root, entry.AmmoType, entry.Capacity, profile,
                        AssetDatabase.LoadAssetAtPath<AudioClip>(entry.InsertClip));
                        if (PrefabUtility.SaveAsPrefabAsset(root, entry.Weapon) == null) throw new InvalidOperationException("Weapon save failed.");
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                    root = PrefabUtility.LoadPrefabContents(entry.Cartridge);
                    try
                    {
                        ManualLoadingAuthoring.ConfigureCartridge(root, entry.AmmoType, entry.Capacity);
                        if (PrefabUtility.SaveAsPrefabAsset(root, entry.Cartridge) == null) throw new InvalidOperationException("Cartridge save failed.");
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                    var info = Required<WeaponInfo>(entry.Info); var serialized = new SerializedObject(info);
                    serialized.FindProperty("_readinessProfile").objectReferenceValue = profile;
                    serialized.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssetIfDirty(info);
                }
                return new { passed=true, weapons=2, shellPrefabs=2, profiles=2, reserveCartridges=32,
                    retirementEnabled=true, runtimeProof=false, humanAcceptance=false };
            }
            catch
            {
                // Exact byte rollback собственных данных/meta. Чужие сцены/ассеты не сохраняются.
                foreach (var backup in backups) File.WriteAllBytes(DiskPath(backup.Key), backup.Value);
                foreach (string path in created) AssetDatabase.DeleteAsset(path);
                if (createdFolder && AssetDatabase.AssetPathToGUID(Folder, AssetPathToGUIDOptions.OnlyExistingAssets) == folderGuid &&
                    Directory.Exists(DiskPath(Folder)) && Directory.GetFileSystemEntries(DiskPath(Folder)).Length == 0) AssetDatabase.DeleteAsset(Folder);
                foreach (var entry in Entries)
                    foreach (string path in new[] { entry.Info, entry.Weapon, entry.Cartridge })
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                throw;
            }
        }
        private static void ConfigureProfile(WeaponReadinessProfile profile, WeaponEmptyPose empty)
        {
            var data = new SerializedObject(profile);
            data.FindProperty("_ammoCapability").intValue = (int)WeaponAmmoCapability.FixedStoreChamber;
            data.FindProperty("_physicalCapability").intValue = (int)WeaponPhysicalCapability.ActionTravel;
            data.FindProperty("_chamberPolicy").intValue = (int)WeaponChamberPolicy.ManualReturn;
            data.FindProperty("_emptyPose").intValue = (int)empty;
            data.ApplyModifiedPropertiesWithoutUndo();
        }
        private static void CheckProfile(WeaponReadinessProfile profile, WeaponEmptyPose empty)
        {
            if (!profile.TryValidate(out string error) || profile.AmmoCapability != WeaponAmmoCapability.FixedStoreChamber ||
                profile.PhysicalCapability != WeaponPhysicalCapability.ActionTravel || profile.ChamberPolicy != WeaponChamberPolicy.ManualReturn ||
                profile.EmptyPose != empty || EditorUtility.IsDirty(profile)) throw new InvalidOperationException(error ?? "Dirty/stale explicit tube profile.");
        }
        private static void CheckAbsent(string path)
        {
            if (File.Exists(DiskPath(path)) || Directory.Exists(DiskPath(path)) || File.Exists(DiskPath(path + ".meta")) ||
                !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path, AssetPathToGUIDOptions.OnlyExistingAssets)) ||
                AssetDatabase.LoadMainAssetAtPath(path) != null) throw new InvalidOperationException("Output exists/stale: " + path);
        }
        private static T Required<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException("Missing asset: " + path);
        private static void EnsureIdle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Idle Editor and prepared Unity lease required.");
        }
        private static void SaveReport(string path, object report)
        { File.WriteAllText(DiskPath(path), Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented)); }
        private static string DiskPath(string path)
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string target = Path.GetFullPath(Path.Combine(projectRoot, path));
            if (!target.StartsWith(projectRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Path escapes project: " + path);
            return target;
        }
    }
}
