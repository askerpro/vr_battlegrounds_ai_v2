using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.DebugTools;
using VrBattlegrounds.Weapons.Sights;

namespace VrBattlegrounds.Editor.Weapons.Calibration
{
    /// <summary>Собирает собственную сцену из render-only копий; игровые префабы не инстанцирует и не сохраняет.</summary>
    [InitializeOnLoad]
    public static class SightCalibrationBenchBuilder
    {
        public const string ScenePath = "Assets/Scenes/Tools/WeaponSightReview.unity";
        private const string Materials = "Assets/Art/Weapons/Sights/Review";
        private const string LeaseKey = "SightBench.StartLease";
        private const string StartOwner = "WeaponSightReview";

        static SightCalibrationBenchBuilder() => EditorApplication.playModeStateChanged += HandlePlayState;

        [MenuItem("Tools/VR Battlegrounds/Weapons/Sight Calibration/Play Mode Review")]
        public static void Open() => SightCalibrationBenchWindow.Open();

        public static void StartReview()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Редактор уже в Play Mode.");
            if (SessionState.GetBool(LeaseKey, false)) throw new InvalidOperationException("Настройки старта уже заняты стендом.");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) throw new InvalidOperationException("Сначала соберите сцену стенда.");
            if (!VrBattlegrounds.Editor.PlayModeStartFromOffline.TrySetTemporaryStartScene(AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath), StartOwner))
                throw new InvalidOperationException("Стартовая сцена уже занята другим стендом.");
            SessionState.SetBool(LeaseKey, true);
            EditorApplication.isPlaying = true;
        }

        private static void HandlePlayState(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(LeaseKey, false)) return;
            VrBattlegrounds.Editor.PlayModeStartFromOffline.ClearTemporaryStartScene(StartOwner);
            SessionState.EraseBool(LeaseKey);
        }

        public static string Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit Mode required.");
            if (File.Exists(ScenePath)) throw new InvalidOperationException("Сцена уже существует; стенд не перезаписывает её автоматически.");
            var registry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>(WeaponSightAudit.RegistryPath);
            if (registry == null || registry.Count != 20) throw new InvalidOperationException("Ожидается реестр 20 моделей.");
            var previous = SceneManager.GetActiveScene();
            string[] inputs = registry.Weapons.Select(w => AssetDatabase.GetAssetPath(w.WeaponPrefab)).Distinct().ToArray();
            string[] before = inputs.Select(p => AssetDatabase.GetAssetDependencyHash(p).ToString()).ToArray();
            Material reticle = SaveMaterial("Reticle", WeaponOpticView.ShaderName, Color.red);
            Material dark = SaveMaterial("Target", "Universal Render Pipeline/Unlit", new Color(.05f,.055f,.06f));
            Material grid = SaveMaterial("Grid", "Universal Render Pipeline/Unlit", new Color(.45f,.49f,.50f));
            Material centre = SaveMaterial("Centre", "Universal Render Pipeline/Unlit", Color.white);
            Material line = SaveMaterial("SightLine", "Universal Render Pipeline/Unlit", Color.yellow);
            Material impact = SaveMaterial("SdkImpact", "Universal Render Pipeline/Unlit", Color.red);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.4f,.4f,.4f);
                var environment = new GameObject("Environment");
                var gameplay = new GameObject("Gameplay");
                var sdk = new GameObject("BenchSdkManager").AddComponent<UltimateXR.Core.UxrManager>();
                sdk.transform.SetParent(gameplay.transform);
                var manager = new GameObject("BenchWeaponManager").AddComponent<UxrWeaponManager>();
                manager.transform.SetParent(gameplay.transform);
                var camera = new GameObject("ReviewCamera").AddComponent<Camera>();
                camera.transform.SetParent(gameplay.transform); camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.12f,.14f,.16f);
                var light = new GameObject("MainLight").AddComponent<Light>();
                light.transform.SetParent(environment.transform); light.type = LightType.Directional;
                light.intensity = 1.5f; light.transform.rotation = Quaternion.Euler(40, 25, 0);
                var fill = new GameObject("FillLight").AddComponent<Light>();
                fill.transform.SetParent(environment.transform); fill.type = LightType.Directional;
                fill.intensity = .8f; fill.transform.rotation = Quaternion.Euler(320, 200, 0);
                var target = Box("Target_Grid5cm_Centre1cm", gameplay.transform, Vector3.zero, new Vector3(2, 2, .01f), dark, true);
                for (int n = -20; n <= 20; n++)
                {
                    float value = n * .05f;
                    Box("GridH", target, new Vector3(0,value,-.006f), new Vector3(2,.001f,.001f), grid);
                    Box("GridV", target, new Vector3(value,0,-.006f), new Vector3(.001f,2,.001f), grid);
                }
                Box("AxisCentreH", target, new Vector3(0,0,-.009f), new Vector3(.08f,.002f,.001f), centre);
                Box("AxisCentreV", target, new Vector3(0,0,-.009f), new Vector3(.002f,.08f,.001f), centre);
                for (int n = -4; n <= 4; n++) if (n != 0)
                {
                    Box("CentimetreH", target, new Vector3(n*.01f,0,-.01f), new Vector3(.001f,.007f,.001f), centre);
                    Box("CentimetreV", target, new Vector3(0,n*.01f,-.01f), new Vector3(.007f,.001f,.001f), centre);
                }
                var lineMark = new GameObject("PredictedSightLine_Yellow").transform; lineMark.SetParent(gameplay.transform);
                Box("H", lineMark, Vector3.zero, new Vector3(.025f,.002f,.001f), line);
                Box("V", lineMark, Vector3.zero, new Vector3(.002f,.025f,.001f), line);
                var impactMark = Box("ActualSdkImpact_Red", gameplay.transform, Vector3.zero, Vector3.one*.008f, impact);
                impactMark.gameObject.SetActive(false);
                var ordered = registry.Weapons.OrderBy(w => Array.IndexOf(new[] { "TR15", "MKR9", "SniperRifle", "Viper", "SRM12" }, w.WeaponId) is int rank && rank >= 0 ? rank : 100)
                    .ThenBy(w => w.WeaponId, StringComparer.Ordinal).ToArray();
                var entries = ordered.Select(w => BuildWeapon(w, gameplay.transform, reticle)).ToArray();
                var bench = new GameObject("SightCalibrationBench").AddComponent<SightCalibrationBench>();
                bench.transform.SetParent(gameplay.transform);
                var serialized = new SerializedObject(bench);
                serialized.FindProperty("_camera").objectReferenceValue = camera;
                serialized.FindProperty("_target").objectReferenceValue = target;
                serialized.FindProperty("_lineMark").objectReferenceValue = lineMark;
                serialized.FindProperty("_impactMark").objectReferenceValue = impactMark;
                serialized.FindProperty("_manager").objectReferenceValue = manager;
                serialized.FindProperty("_settings").objectReferenceValue = AssetDatabase.LoadAssetAtPath<WeaponSightCalibrationSettings>(WeaponSightAudit.SettingsPath);
                var array = serialized.FindProperty("_entries"); array.arraySize = entries.Length;
                for (int i = 0; i < entries.Length; i++)
                {
                    var item = array.GetArrayElementAtIndex(i); var entry = entries[i];
                    Set("Id", entry.Id); Set("Label", entry.Label); Set("Status", entry.Status);
                    Obj("Root", entry.Root); Obj("Donor", entry.Donor); Obj("Source", entry.Source); Obj("Lens", entry.Lens);
                    Obj("OriginalLens", entry.OriginalLens); Obj("PrototypeLens", entry.PrototypeLens); Obj("Optic", entry.Optic);
                    item.FindPropertyRelative("HasReferences").boolValue = entry.HasReferences;
                    item.FindPropertyRelative("Rear").vector3Value = entry.Rear; item.FindPropertyRelative("Front").vector3Value = entry.Front;
                    item.FindPropertyRelative("Centre").vector3Value = entry.Centre; item.FindPropertyRelative("Size").floatValue = entry.Size;
                    void Set(string key, string value) => item.FindPropertyRelative(key).stringValue = value;
                    void Obj(string key, UnityEngine.Object value) => item.FindPropertyRelative(key).objectReferenceValue = value;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                if (!EditorSceneManager.SaveScene(scene, ScenePath)) throw new InvalidOperationException("Scene save failed.");
                bool unchanged = inputs.Select(p => AssetDatabase.GetAssetDependencyHash(p).ToString()).SequenceEqual(before);
                if (!unchanged) throw new InvalidOperationException("Исходные префабы изменились во время сборки.");
                return "Build PASS: 20 render-only weapons, SDK source copies, MKR9/SRS donors, input hashes unchanged.";
            }
            finally { SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(scene, true); }
        }

        private static SightCalibrationBench.Entry BuildWeapon(WeaponInfo info, Transform parent, Material reticle)
        {
            GameObject prefab = info.WeaponPrefab;
            var original = prefab.GetComponentInChildren<UxrProjectileSource>(true);
            if (original == null || original.ShotTypes.Count == 0) throw new InvalidOperationException(info.WeaponId + ": no source");
            var root = new GameObject(info.WeaponId + "_RenderOnly"); root.SetActive(false); root.transform.SetParent(parent);
            root.transform.position = new Vector3(0, 1.3f, 0);
            var entry = new SightCalibrationBench.Entry { Id = info.WeaponId, Label = info.DisplayName, Root = root,
                Status = "Исходная геометрия; References и калибровка не приняты." };
            bool first = true; Bounds bounds = default;
            foreach (var renderer in prefab.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (!renderer.enabled || !Included(renderer.transform, prefab.transform)) continue;
                var filter = renderer.GetComponent<MeshFilter>(); if (filter == null || filter.sharedMesh == null) continue;
                var copy = CopyMesh(filter, root.transform, Vector3.zero, null);
                if (first) { bounds = copy.bounds; first = false; } else bounds.Encapsulate(copy.bounds);
                if (renderer.name == "SM_Attach_AR15_XPS2") entry.Lens = copy;
            }
            entry.Centre = root.transform.InverseTransformPoint(bounds.center); entry.Size = bounds.size.magnitude;
            var sourceGo = new GameObject("SdkProjectileSourceCopy"); sourceGo.SetActive(false); sourceGo.transform.SetParent(root.transform, false);
            entry.Source = sourceGo.AddComponent<UxrProjectileSource>(); EditorUtility.CopySerialized(original, entry.Source);
            var sourceSettings = new SerializedObject(entry.Source); sourceSettings.FindProperty("_weaponAnimator").objectReferenceValue = null;
            var shots = sourceSettings.FindProperty("_shotTypes");
            for (int i = 0; i < shots.arraySize; i++)
            {
                var shot = shots.GetArrayElementAtIndex(i);
                foreach (string key in new[] { "_shotSource", "_tip" })
                {
                    Transform old = (Transform)shot.FindPropertyRelative(key).objectReferenceValue;
                    var marker = new GameObject(key + i).transform; marker.SetParent(root.transform, false);
                    marker.localPosition = old.position; marker.localRotation = old.rotation;
                    shot.FindPropertyRelative(key).objectReferenceValue = marker;
                }
                foreach (string key in new[] { "_prefabInstantiateOnTipWhenShot", "_prefabInstantiateOnImpact", "_prefabScenarioImpactDecal" })
                    shot.FindPropertyRelative(key).objectReferenceValue = null;
                shot.FindPropertyRelative("_collisionLayerMask").intValue = 1; // Только Default-мишень собственного стенда.
            }
            sourceSettings.ApplyModifiedPropertiesWithoutUndo(); sourceGo.SetActive(true);
            Transform muzzle = entry.Source.ShotTypes[0].ShotSource;
            entry.Rear = root.transform.InverseTransformPoint(muzzle.position) - muzzle.forward * .35f + Vector3.up*.06f;
            entry.Front = entry.Rear + muzzle.forward;
            if (info.WeaponId == "TR15")
            {
                if (entry.Lens == null || entry.Lens.sharedMaterials.Length != 2) throw new InvalidOperationException("XPS2 binding changed.");
                entry.OriginalLens = entry.Lens.sharedMaterials[1]; entry.PrototypeLens = reticle;
                entry.Rear = new Vector3(.000286f,.107165f,.052617f);
                entry.Front = root.transform.InverseTransformPoint(muzzle.position + muzzle.forward*15);
                entry.HasReferences = true;
                entry.Optic = entry.Lens.gameObject.AddComponent<WeaponOpticView>();
                var view = new SerializedObject(entry.Optic);
                view.FindProperty("_lensRenderer").objectReferenceValue = entry.Lens;
                view.FindProperty("_lensMaterialIndex").intValue = 1;
                view.FindProperty("_source").objectReferenceValue = entry.Source;
                view.FindProperty("_weapon").objectReferenceValue = info;
                view.FindProperty("_sightId").stringValue = "XPS2";
                view.FindProperty("_settings").objectReferenceValue = AssetDatabase.LoadAssetAtPath<WeaponSightCalibrationSettings>(WeaponSightAudit.SettingsPath);
                view.FindProperty("_calibratedZeroDistance").floatValue = 15;
                view.ApplyModifiedPropertiesWithoutUndo(); entry.Optic.enabled = false;
                entry.Status = "AR-15 (TR15): прототип конечной точки 15 м, без полного fingerprint и XR-приёмки.";
            }
            if (info.WeaponId == "MKR9") AddMkr9(entry);
            if (info.WeaponId == "SniperRifle") AddSrs(entry);
            if (info.WeaponId == "Viper" || info.WeaponId == "SRM12")
            {
                JToken draft = JArray.Parse(File.ReadAllText("tmp/weapon-sight-calibration/reference-drafts.json")).Single(t => (string)t["weaponId"] == info.WeaponId);
                entry.Rear = Reference(draft["rear"], prefab); entry.Front = Reference(draft["front"], prefab);
                entry.HasReferences = true;
                entry.Status = "Черновые R/F; поправка " + (info.WeaponId == "Viper" ? "+3.0399" : "−5.3231") + " мм НЕ применена. Регулировка не принята.";
            }
            return entry;
        }

        private static Vector3 Reference(JToken record, GameObject prefab)
        {
            string guid = (string)record["meshGuid"];
            var filter = prefab.GetComponentsInChildren<MeshFilter>(true).First(f => f.sharedMesh != null && AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(f.sharedMesh)) == guid);
            if (AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(filter.sharedMesh)).ToString() != (string)record["meshHash"])
                throw new InvalidOperationException("Reference mesh hash changed: " + guid);
            int[] vertices = record["vertices"].Values<int>().ToArray();
            return vertices.Select(i => filter.transform.TransformPoint(filter.sharedMesh.vertices[i])).Aggregate(Vector3.zero, (a,b) => a+b) / vertices.Length;
        }

        private static void AddMkr9(SightCalibrationBench.Entry entry)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ThirdParty/KINEMATION/TacticalShooterPack/Prefabs/Weapons/W_MKR9.prefab");
            var filter = source.GetComponentsInChildren<MeshFilter>(true).First(f => f.sharedMesh != null && AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(f.sharedMesh)) == "ea5d7ab7423bdfc47a83b22943278601");
            entry.Donor = new GameObject("MKR9_NativeIron_Draft"); entry.Donor.transform.SetParent(entry.Root.transform, false);
            var copy = CopyMesh(filter, entry.Donor.transform, Vector3.zero, null);
            copy.transform.localPosition = new Vector3(.0008000055f,.0749983639f,.08252673f);
            copy.transform.localRotation = new Quaternion(-.7071068f,5.760116e-8f,1.62968277e-7f,.7071067f);
            copy.transform.localScale = new Vector3(99.9999847f,100,100);
            entry.Status = "Родной MKR9-комплект: визуальная посадка; References/сведение ещё не приняты.";
        }

        private static void AddSrs(SightCalibrationBench.Entry entry)
        {
            var donor = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Weapons/SRM12/SRM12.prefab");
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Art/Weapons/Sights/Materials/SniperRifle_SRS_Iron.mat");
            if (material == null) throw new InvalidOperationException("SRS material missing.");
            JToken report = JArray.Parse(File.ReadAllText("tmp/weapon-sight-calibration/srs-sniper-matched/candidates.json"))[0];
            entry.Donor = new GameObject("SRS_ModularIron_MountDraft"); entry.Donor.transform.SetParent(entry.Root.transform, false);
            foreach (JToken module in report["modules"])
            {
                string guid = (string)module["meshGuid"];
                var filter = donor.GetComponentsInChildren<MeshFilter>(true).First(f => f.sharedMesh != null && AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(f.sharedMesh)) == guid);
                if (AssetDatabase.GetAssetDependencyHash(AssetDatabase.GetAssetPath(filter.sharedMesh)).ToString() != (string)module["meshHash"])
                    throw new InvalidOperationException("SRS mesh hash changed.");
                CopyMesh(filter, entry.Donor.transform, ReadArray(module["translation"]), material);
            }
            Box("FrontAdapter_DRAFT", entry.Donor.transform, new Vector3(0,.0849500865f,.4919f), new Vector3(.034f,.003f,.035f), material);
            Box("RearAdapter_DRAFT", entry.Donor.transform, new Vector3(0,.0850463791f,.0628f), new Vector3(.034f,.003f,.035f), material);
            entry.Rear = ReadArray(report["rearReference"]); entry.Front = ReadArray(report["frontReference"]);
            entry.HasReferences = true;
            entry.Status = "SRS/SRM12: материал под SniperRifle; площадки — эскиз, линия R/F ещё не сведена на 15 м.";
        }

        private static Vector3 ReadArray(JToken value) => new Vector3((float)value[0], (float)value[1], (float)value[2]);

        private static MeshRenderer CopyMesh(MeshFilter source, Transform parent, Vector3 shift, Material material)
        {
            var go = new GameObject(source.name); go.transform.SetParent(parent, false);
            go.transform.localPosition = source.transform.position + shift;
            go.transform.localRotation = source.transform.rotation; go.transform.localScale = source.transform.lossyScale;
            go.AddComponent<MeshFilter>().sharedMesh = source.sharedMesh;
            var renderer = go.AddComponent<MeshRenderer>();
            var original = source.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = material == null ? original.sharedMaterials : original.sharedMaterials.Select(_ => material).ToArray();
            return renderer;
        }

        private static bool Included(Transform node, Transform root)
        {
            for (Transform current = node; current != null; current = current.parent)
            {
                if (!current.gameObject.activeSelf || current.name.IndexOf("Highlight", StringComparison.OrdinalIgnoreCase) >= 0 || current.name.IndexOf("Decal", StringComparison.OrdinalIgnoreCase) >= 0) return false;
                if (current == root) return true;
            }
            return false;
        }

        private static Transform Box(string name, Transform parent, Vector3 position, Vector3 size, Material material, bool collider = false)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            // Мишень является frame, поэтому её renderer/коллайдер масштабируются на ребёнке лишь для декоративных деталей.
            if (name == "Target_Grid5cm_Centre1cm")
            {
                go.transform.localScale = Vector3.one;
                var mesh = go.GetComponent<MeshFilter>();
                string path = Materials + "/ReviewTarget2m.asset";
                Mesh copy = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                if (copy == null)
                {
                    copy = UnityEngine.Object.Instantiate(mesh.sharedMesh); copy.name = "ReviewTarget2m";
                    copy.vertices = copy.vertices.Select(v => Vector3.Scale(v, size)).ToArray(); copy.RecalculateBounds();
                    AssetDatabase.CreateAsset(copy, path);
                }
                mesh.sharedMesh = copy;
                var box = go.GetComponent<BoxCollider>(); box.size = size;
            }
            else go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (!collider) UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            return go.transform;
        }

        private static Material SaveMaterial(string name, string shaderName, Color colour)
        {
            if (!AssetDatabase.IsValidFolder(Materials)) AssetDatabase.CreateFolder("Assets/Art/Weapons/Sights", "Review");
            string path = Materials + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path); if (existing != null) return existing;
            Shader shader = Shader.Find(shaderName); if (shader == null || ShaderUtil.ShaderHasError(shader)) throw new InvalidOperationException("Shader missing: " + shaderName);
            var material = new Material(shader); material.SetColor("_BaseColor", colour); AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }

    public sealed class SightCalibrationBenchWindow : EditorWindow
    {
        public static void Open() => GetWindow<SightCalibrationBenchWindow>("Проверка прицелов");
        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Отдельный экранный стенд. Запуск возвращает прежнюю сцену после Stop. В общем редакторе сначала согласуйте очередь/замок Unity.", MessageType.Info);
            EditorGUILayout.LabelField("Сцена", SightCalibrationBenchBuilder.ScenePath);
            GUI.enabled = !EditorApplication.isPlayingOrWillChangePlaymode;
            if (GUILayout.Button("Запустить стенд в Play Mode")) SightCalibrationBenchBuilder.StartReview();
            GUI.enabled = true;
            if (GUILayout.Button("План проверки и визуального осмотра"))
                UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal("Docs/tasks/weapon-sight-playmode-review.md", 1);
        }
    }
}
