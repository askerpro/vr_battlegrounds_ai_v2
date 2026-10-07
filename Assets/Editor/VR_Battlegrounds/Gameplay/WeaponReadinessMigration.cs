using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Weapons;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Этап 4 готовности оружия (Docs/tasks/weapon-readiness-feedback-design.md): адресный preflight → apply → readback
    /// профиля «съёмный магазин + патронник + HoldOpen» для явного списка стволов. Список расширяется по таблице дизайна
    /// только после проверки предыдущей волны в шлеме. Компоненты пишет <see cref="WeaponReadinessAuthoring"/> — тот же
    /// writer, что у сборщиков, поэтому пересборка префаба даёт те же bindings. Вызывать в idle Editor (своя аренда).
    /// </summary>
    public static class WeaponReadinessMigration
    {
        private sealed class Entry { public string Info, Weapon; }

        // Волна HoldOpen: у этих стволов есть Empty-клип (Browning/«Gun», Viper, TR15); Herrington — ManualLoadingMigration.
        private static readonly Entry[] HoldOpenWave =
        {
            new Entry { Info = "Assets/Data/Weapons/Gun_Weapon.asset", Weapon = "Assets/Prefabs/Weapons/BrowningHiPower/BrowningHiPower.prefab" },
            new Entry { Info = "Assets/Data/Weapons/Viper_Weapon.asset", Weapon = "Assets/Prefabs/Weapons/Viper/Viper.prefab" },
            new Entry { Info = "Assets/Data/Weapons/TR15_Weapon.asset", Weapon = "Assets/Prefabs/Weapons/TR15/TR15.prefab" },
        };
        private const string ProfilePath = WeaponReadinessAuthoring.DetachableHoldOpenProfile;

        [MenuItem("Tools/VR Battlegrounds/Gameplay/Weapon Readiness/Preflight HoldOpen Wave")]
        public static void PreflightMenu() => Log(Preflight());

        [MenuItem("Tools/VR Battlegrounds/Gameplay/Weapon Readiness/Apply HoldOpen Wave")]
        public static void ApplyMenu() => Log(Apply());

        /// <summary>Без записи на диск: mapping каждого ствола на загруженной копии префаба, идентичность сети и данных.</summary>
        public static Dictionary<string, object> Preflight()
        {
            EnsureIdle();
            var failures = new List<string>(); var rows = new List<object>();
            WeaponReadinessProfile transient = null;
            try
            {
                var profile = AssetDatabase.LoadAssetAtPath<WeaponReadinessProfile>(ProfilePath);
                if (profile == null) { CheckAbsent(ProfilePath); profile = transient = CreateProfile(); }
                else CheckProfile(profile);
                foreach (var entry in HoldOpenWave)
                {
                    GameObject root = null;
                    try
                    {
                        var info = Required<WeaponInfo>(entry.Info);
                        if (EditorUtility.IsDirty(info) || AssetDatabase.GetAssetPath(info.WeaponPrefab) != entry.Weapon)
                            throw new InvalidOperationException("Dirty/stale weapon binding: " + entry.Info);
                        if (info.ReadinessProfile != null && info.ReadinessProfile != profile)
                            throw new InvalidOperationException("Чужой профиль готовности уже назначен: " + entry.Info);
                        root = PrefabUtility.LoadPrefabContents(entry.Weapon);
                        WeaponReadinessAuthoring.ConfigureFromSource(root, profile);
                        rows.Add(Describe(entry, root));
                    }
                    catch (Exception exception) { failures.Add(entry.Weapon + ": " + exception.Message); }
                    finally { if (root != null) PrefabUtility.UnloadPrefabContents(root); }
                }
            }
            catch (Exception exception) { failures.Add(ProfilePath + ": " + exception.Message); }
            finally { if (transient != null) Object.DestroyImmediate(transient); }
            return new Dictionary<string, object> { ["passed"] = failures.Count == 0, ["failures"] = failures, ["rows"] = rows,
                ["scope"] = "Authoring preflight; runtime и шлем не проверяются" };
        }

        /// <summary>Preflight, затем профиль (если нет), компоненты префабов и ссылка WeaponInfo. Сбой — побайтный откат.</summary>
        public static Dictionary<string, object> Apply()
        {
            EnsureIdle();
            var preflight = Preflight();
            if (!(bool)preflight["passed"]) throw new InvalidOperationException("Preflight refused: " + string.Join("; ", (List<string>)preflight["failures"]));
            var backups = new Dictionary<string, byte[]>();
            foreach (var entry in HoldOpenWave)
                foreach (string path in new[] { entry.Info, entry.Weapon, entry.Info + ".meta", entry.Weapon + ".meta" })
                    backups.Add(path, File.ReadAllBytes(DiskPath(path)));
            bool createdProfile = false;
            try
            {
                var profile = AssetDatabase.LoadAssetAtPath<WeaponReadinessProfile>(ProfilePath);
                if (profile == null)
                {
                    profile = CreateProfile();
                    AssetDatabase.CreateAsset(profile, ProfilePath); createdProfile = true;
                    AssetDatabase.SaveAssetIfDirty(profile);
                }
                CheckProfile(profile);
                foreach (var entry in HoldOpenWave)
                {
                    GameObject root = PrefabUtility.LoadPrefabContents(entry.Weapon);
                    try
                    {
                        WeaponReadinessAuthoring.ConfigureFromSource(root, profile);
                        if (PrefabUtility.SaveAsPrefabAsset(root, entry.Weapon) == null) throw new InvalidOperationException("Weapon save failed.");
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                    var info = Required<WeaponInfo>(entry.Info); var serialized = new SerializedObject(info);
                    serialized.FindProperty("_readinessProfile").objectReferenceValue = profile;
                    serialized.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssetIfDirty(info);
                }
                return Readback();
            }
            catch
            {
                foreach (var backup in backups) File.WriteAllBytes(DiskPath(backup.Key), backup.Value);
                if (createdProfile) AssetDatabase.DeleteAsset(ProfilePath);
                foreach (var entry in HoldOpenWave)
                    foreach (string path in new[] { entry.Info, entry.Weapon })
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                throw;
            }
        }

        /// <summary>Чтение сохранённых ассетов: профиль, компоненты, mapping, сетевой id на диске, ссылка данных.</summary>
        public static Dictionary<string, object> Readback()
        {
            var failures = new List<string>(); var rows = new List<object>();
            var profile = AssetDatabase.LoadAssetAtPath<WeaponReadinessProfile>(ProfilePath);
            if (profile == null) failures.Add("Нет профиля " + ProfilePath);
            foreach (var entry in HoldOpenWave)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.Weapon);
                var controller = prefab != null ? prefab.GetComponent<WeaponReadinessController>() : null;
                var info = AssetDatabase.LoadAssetAtPath<WeaponInfo>(entry.Info);
                if (controller == null || controller.Profile != profile) failures.Add(entry.Weapon + ": controller/profile");
                if (info == null || info.ReadinessProfile != profile) failures.Add(entry.Info + ": WeaponInfo.ReadinessProfile");
                if (prefab != null && (prefab.GetComponent<WeaponTriggerAttemptRouter>() == null || prefab.GetComponent<WeaponAttemptFeedback>() == null))
                    failures.Add(entry.Weapon + ": router/attempt feedback");
                var identity = prefab != null ? prefab.GetComponent<Mirror.NetworkIdentity>() : null;
                uint canonical = Mirror.NetworkIdentity.AssetGuidToUint(new Guid(AssetDatabase.AssetPathToGUID(entry.Weapon)));
                var match = System.Text.RegularExpressions.Regex.Match(File.ReadAllText(DiskPath(entry.Weapon)), @"^  _assetId: (\d+)",
                    System.Text.RegularExpressions.RegexOptions.Multiline);
                if (identity == null || !match.Success || uint.Parse(match.Groups[1].Value) != canonical)
                    failures.Add(entry.Weapon + ": _assetId на диске не канонический");
                if (prefab != null) rows.Add(Describe(entry, prefab));
            }
            return new Dictionary<string, object> { ["passed"] = failures.Count == 0, ["failures"] = failures, ["rows"] = rows };
        }

        private static object Describe(Entry entry, GameObject root)
        {
            var controller = new SerializedObject(root.GetComponent<WeaponReadinessController>());
            var bindings = controller.FindProperty("_requiredBindings");
            var targets = new List<string>();
            for (int i = 0; i < bindings.arraySize; i++)
            {
                var item = bindings.GetArrayElementAtIndex(i);
                var target = item.FindPropertyRelative("Target").objectReferenceValue as Transform;
                targets.Add((target != null ? target.name : "null") + " rest=" + item.FindPropertyRelative("RestPosition").vector3Value.ToString("F4") +
                    " rear=" + item.FindPropertyRelative("RearPosition").vector3Value.ToString("F4") +
                    " rot=" + item.FindPropertyRelative("AnimateRotation").boolValue);
            }
            return new { entry.Weapon, emptyRearTime = controller.FindProperty("_emptyRearTime").floatValue, bindings = targets,
                scale = root.transform.localScale.ToString("F4") };
        }

        private static WeaponReadinessProfile CreateProfile()
        {
            var profile = ScriptableObject.CreateInstance<WeaponReadinessProfile>();
            var data = new SerializedObject(profile);
            data.FindProperty("_ammoCapability").intValue = (int)WeaponAmmoCapability.DetachableMagazineChamber;
            data.FindProperty("_physicalCapability").intValue = (int)WeaponPhysicalCapability.ActionTravel;
            data.FindProperty("_chamberPolicy").intValue = (int)WeaponChamberPolicy.ManualReturn;
            data.FindProperty("_emptyPose").intValue = (int)WeaponEmptyPose.HoldOpen;
            data.ApplyModifiedPropertiesWithoutUndo();
            return profile;
        }

        private static void CheckProfile(WeaponReadinessProfile profile)
        {
            if (!profile.TryValidate(out string error) || profile.AmmoCapability != WeaponAmmoCapability.DetachableMagazineChamber ||
                profile.PhysicalCapability != WeaponPhysicalCapability.ActionTravel || profile.ChamberPolicy != WeaponChamberPolicy.ManualReturn ||
                profile.EmptyPose != WeaponEmptyPose.HoldOpen || EditorUtility.IsDirty(profile))
                throw new InvalidOperationException(error ?? "Dirty/stale HoldOpen profile.");
        }

        private static void CheckAbsent(string path)
        {
            if (File.Exists(DiskPath(path)) || File.Exists(DiskPath(path + ".meta")) ||
                !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path, AssetPathToGUIDOptions.OnlyExistingAssets)))
                throw new InvalidOperationException("Output exists/stale: " + path);
        }

        private static T Required<T>(string path) where T : Object =>
            AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new InvalidOperationException("Missing asset: " + path);

        private static void EnsureIdle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Idle Editor and prepared Unity lease required.");
        }

        private static void Log(Dictionary<string, object> report) =>
            VrBattlegrounds.Core.GameLog.WeaponSystem.Info(Newtonsoft.Json.JsonConvert.SerializeObject(report, Newtonsoft.Json.Formatting.Indented));

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
