using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UltimateXR.Manipulation;
using VrBattlegrounds.Arsenal;

namespace VrBattlegrounds.Editor.Arsenal
{
    /// <summary>Устанавливает сетевой склад на обе станции и прижимает магазин/карточку к геометрии слота.</summary>
    public static class ArsenalMagazineOfferInstaller
    {
        private static readonly string[] StationPaths =
        {
            "Assets/Prefabs/Arsenal/CommonOpenArsenalStation.prefab",
            "Assets/Prefabs/Arsenal/LobbyDemoArsenalStation.prefab"
        };

        /// <summary>Вызов из генератора после настройки пресета/геометрии, до измерения габаритов станции.</summary>
        public static void Configure(GameObject root)
        {
            CheckEditor();
            var wall = root.GetComponent<ArsenalWallController>();
            if (wall == null || root.GetComponent<Mirror.NetworkIdentity>() == null)
                throw new InvalidOperationException("Станция должна иметь корневые ArsenalWallController и NetworkIdentity.");
            if (root.GetComponent<ArsenalMagazineSupply>() == null) root.AddComponent<ArsenalMagazineSupply>();
            var equipment = root.GetComponent<ArsenalEquipmentPoses>();
            var originals = equipment != null ? equipment.Targets.Where(p => p.Target != null)
                .Select(p => (target: p.Target, position: p.Target.position, rotation: p.Target.rotation)).ToArray() : null;
            try
            {
                if (equipment != null) equipment.Apply(1f);
                foreach (var slot in wall.Slots)
                {
                    if (!(slot is FirearmSlotController firearm)) continue;
                    var anchor = firearm.MagAnchor;
                    var presentation = ArsenalPresentationApplicator.Resolve(slot);
                    VrBattlegrounds.Editor.Arsenal.ArsenalSupportModuleBuilder.MaterializePresentation(slot, presentation);
                    if (anchor == null) anchor = slot.GetComponentsInChildren<UxrGrabbableObjectAnchor>(true).FirstOrDefault(a => a.name.Contains("Mag"));
                    if (anchor == null) throw new InvalidOperationException("Нет MagAnchor: " + slot.name);
                    Vector3 normal = slot.PresentationZone == ArsenalPresentationZone.Shelf ? root.transform.up : -root.transform.forward;
                    var surface = FindSurface(root, slot, anchor.transform.position, normal);
                    if (surface == null) throw new InvalidOperationException("Нет MeshFilter поверхности: " + slot.name);
                    var offer = slot.GetComponent<ArsenalMagazineOffer>() ?? slot.gameObject.AddComponent<ArsenalMagazineOffer>();
                    offer.Configure(firearm, anchor, surface, surface.transform.InverseTransformDirection(normal));
                    // В сохранённом ассете только держатель и якорь; настоящий предмет появляется исключительно через серверный спавн.
                    if (slot.WeaponData != null && slot.WeaponData.MagazinePrefab != null)
                    {
                        GameObject sample = ArsenalPresentationApplicator.CreateDisplaySample(slot, true);
                        try
                        {
                            ArsenalPresentationApplicator.ApplyMagazine(offer, sample.transform);
                        }
                        finally { UnityEngine.Object.DestroyImmediate(sample); }
                    }
                    EditorUtility.SetDirty(offer);
                }
            }
            finally
            {
                if (originals != null)
                    foreach (var pose in originals) pose.target.SetPositionAndRotation(pose.position, pose.rotation);
            }
        }

        private static MeshFilter FindSurface(GameObject root, ArsenalSlotController slot, Vector3 point, Vector3 normal)
        {
            // У каждого современного слота своя перфорированная пластина, она приоритетнее общей нижней полки.
            var local = slot.GetComponentsInChildren<MeshFilter>(true).Where(m => m.sharedMesh != null && m.name == "PegboardSection").ToArray();
            if (local.Length > 0) return Nearest(local, point, normal);
            var candidates = root.GetComponentsInChildren<MeshFilter>(true).Where(m => m.sharedMesh != null &&
                (slot.PresentationZone == ArsenalPresentationZone.Shelf
                    ? m.name == "ShelfPegboardStrip" || m.name == "SehlfMesh" || m.name == "EquipmentTray"
                    : m.name == "PegboardSection")).ToArray();
            return Nearest(candidates, point, normal);
        }
        private static MeshFilter Nearest(IEnumerable<MeshFilter> meshes, Vector3 point, Vector3 normal)
        {
            MeshFilter best = null; float distance = float.PositiveInfinity;
            foreach (var mesh in meshes)
            {
                Vector3 center = mesh.transform.TransformPoint(mesh.sharedMesh.bounds.center);
                Vector3 offset = center - point;
                // Учитываем боковое расстояние и глубину, не выбирая соседний слот по одному лишь X.
                float score = Vector3.ProjectOnPlane(offset, normal).sqrMagnitude + .1f * Mathf.Pow(Vector3.Dot(offset, normal), 2f);
                if (score < distance) { best = mesh; distance = score; }
            }
            return best;
        }
        /// <summary>Временные настоящие магазины для общего измерения станции; вызывающий уничтожает весь список в finally до SaveAsPrefabAsset.</summary>
        public static void AddRestingMagazineSamples(GameObject root, IList<GameObject> samples)
        {
            foreach (var slot in root.GetComponent<ArsenalWallController>().Slots)
            {
                var offer = slot != null ? slot.GetComponent<ArsenalMagazineOffer>() : null;
                if (offer == null || offer.Anchor == null || slot.WeaponData == null || slot.WeaponData.MagazinePrefab == null || !slot.gameObject.activeInHierarchy) continue;
                var sample = ArsenalPresentationApplicator.CreateDisplaySample(slot, true);
                samples.Add(sample);
                foreach (var body in sample.GetComponentsInChildren<Rigidbody>(true)) body.isKinematic = true;
                ArsenalPresentationApplicator.ApplyMagazine(offer, sample.transform);
            }
        }

        public static string ApplyCurrentPrefabs()
        {
            CheckEditor();
            InstallMagazineHistories();
            foreach (string path in StationPaths)
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) throw new InvalidOperationException("Нет станции: " + path);
                var root = PrefabUtility.LoadPrefabContents(path);
                try { Configure(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            return "Common и LobbyDemo: реальные сетевые magazine offers; контакт магазинов и плоских Shelf карточек настроен.";
        }

        /// <summary>Маркер присутствует до спавна и манипуляции на обеих машинах, даже если привязка склада задержалась.</summary>
        public static string InstallMagazineHistories()
        {
            CheckEditor();
            var paths = AssetDatabase.FindAssets("t:WeaponInfo", new[] { "Assets/Data/Weapons" })
                .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<WeaponInfo>)
                .Where(w => w != null && w.MagazinePrefab != null).Select(w => AssetDatabase.GetAssetPath(w.MagazinePrefab))
                .Distinct().ToArray();
            if (paths.Length != 20) throw new InvalidOperationException("Ожидалось 20 уникальных magazine prefabs, есть " + paths.Length);
            foreach (string path in paths)
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (root.GetComponent<UxrGrabbableObject>() == null || root.GetComponent<Mirror.NetworkIdentity>() == null)
                        throw new InvalidOperationException("Магазин не имеет корневого grab/NI: " + path);
                    if (root.GetComponent<MagazineManipulationHistory>() == null) root.AddComponent<MagazineManipulationHistory>();
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            return "Durable magazine manipulation history: " + paths.Length + " prefabs.";
        }

        /// <summary>Явная миграция только закрытой сцены: текущая человеческая сцена никогда не правится/не сохраняется.</summary>
        public static string ApplyClosedScene(string scenePath)
        {
            CheckEditor();
            var loaded = SceneManager.GetSceneByPath(scenePath);
            if (loaded.IsValid() && loaded.isLoaded) throw new InvalidOperationException("Сцена уже открыта; закрыть её перед миграцией: " + scenePath);
            var scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            try
            {
                foreach (var wall in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ArsenalWallController>(true)))
                {
                    Configure(wall.gameObject);
                    foreach (var component in wall.GetComponentsInChildren<Component>(true))
                        if (component != null) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                }
                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Сцена не сохранена: " + scenePath);
                return "Magazine offers/contact/card migrated: " + scenePath;
            }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        /// <summary>Временный read-only отчёт по закрытым экземплярам префабов; ничего не сохраняет.</summary>
        public static string DiagnoseCurrentPrefabs()
        {
            CheckEditor();
            var lines = new List<string>();
            foreach (string path in StationPaths)
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var wall = root.GetComponent<ArsenalWallController>();
                    lines.Add(path + ": slots=" + wall.Slots.Count + ", supply=" + (root.GetComponent<ArsenalMagazineSupply>() != null));
                    foreach (var slot in wall.Slots)
                    {
                        var offer = slot.GetComponent<ArsenalMagazineOffer>();
                        lines.Add(slot.name + ": offer=" + (offer != null) + ", anchor=" + (offer != null && offer.Anchor != null) +
                            ", magazineNI=" + (slot.WeaponData != null && slot.WeaponData.MagazinePrefab != null &&
                            slot.WeaponData.MagazinePrefab.GetComponent<Mirror.NetworkIdentity>() != null) +
                            ", cardFlat=" + (slot.PresentationZone != ArsenalPresentationZone.Shelf ||
                            Vector3.Dot(slot.transform.TransformDirection(slot.CardLocalRotation * Vector3.back), root.transform.up) > .99f));
                        if (offer == null || offer.Anchor == null || offer.Surface == null || slot.WeaponData == null || slot.WeaponData.MagazinePrefab == null) continue;
                        var sample = ArsenalPresentationApplicator.CreateDisplaySample(slot, true);
                        try
                        {
                            ArsenalPresentationApplicator.ApplyMagazine(offer, sample.transform);
                            Vector3 normal = offer.SurfaceNormal;
                            float plane = ArsenalMagazineOffer.Support(offer.Surface.sharedMesh.bounds, offer.Surface.transform, normal, true);
                            if (!ArsenalMagazineOffer.TrySupport(sample.transform, normal, false, out float nearest))
                                throw new InvalidOperationException("Нет видимой геометрии магазина: " + slot.name);
                            float error = nearest - plane - .001f;
                            lines.Add(slot.WeaponData.WeaponId + ": contactError=" + error.ToString("F6") +
                                ", mass=" + (sample.GetComponent<Rigidbody>() != null ? sample.GetComponent<Rigidbody>().mass.ToString("F3") : "missing"));
                            if (Mathf.Abs(error) > .0005f) throw new InvalidOperationException("Магазин не касается поверхности: " + slot.name + ", error=" + error);
                        }
                        finally { UnityEngine.Object.DestroyImmediate(sample); }
                    }
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            return string.Join("\n", lines);
        }
        private static void CheckEditor()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Только Edit Mode под замком Unity.");
        }
    }
}
