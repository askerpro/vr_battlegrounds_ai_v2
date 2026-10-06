using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UltimateXR.Core.Components;
using UltimateXR.Mechanics.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Weapons.Sights;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.Weapons.Calibration
{
    /// <summary>Создание, размещение и адресное сохранение ручного стенда; SDK-выстрелы не вызывает.</summary>
    public static class ManualSightCalibrationAuthoring
    {
        public const string ScenePath = "Assets/Scenes/Tools/WeaponSightManualCalibration.unity";
        public const string DraftFolder = "Assets/Prefabs/Weapons/SightCalibrationDrafts";
        public const string ProfileFolder = "Assets/Data/Weapons/SightCalibration/Manual";
        private const string RootName = "ManualSightCalibration";
        // Временная fault-injection нативной диагностики; не сериализуется, в обычной работе null.
        internal static Action<string> SaveCheckpointForDiagnostics;

        public static ManualSightCalibrationSession Current()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            return scene.IsValid() && scene.isLoaded
                ? scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<ManualSightCalibrationSession>(true)).SingleOrDefault() : null;
        }

        public static ManualSightCalibrationSession Open()
        {
            CheckEdit();
            var current = Current();
            if (current != null) { SceneManager.SetActiveScene(current.gameObject.scene); return current; }
            if (Enumerable.Range(0, SceneManager.sceneCount).Any(i => SceneManager.GetSceneAt(i).isDirty))
                throw new InvalidOperationException("Сначала сохраните текущие сцены; стенд не отменяет несохранённую работу.");
            if (File.Exists(ScenePath))
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                return Current() ?? throw new InvalidOperationException("В сцене нет сессии ручного стенда.");
            }
            var targetPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Environment/Lobby/ShootingTarget.prefab");
            var pivot = targetPrefab != null ? targetPrefab.GetComponent<VrBattlegrounds.Maps.ShootingTarget>()?.Pivot : null;
            if (pivot == null || pivot.Find("RingOuter") == null) throw new InvalidOperationException("Не найдена геометрия щита.");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var root = new GameObject(RootName);
            var session = root.AddComponent<ManualSightCalibrationSession>();
            session.hideFlags = HideFlags.HideInInspector;
            session.Placement = new GameObject("WeaponPlacement_EditorOnly").transform;
            session.Placement.SetParent(root.transform, false);
            session.Target = new GameObject("CalibrationTarget").transform;
            session.Target.SetParent(root.transform, false);
            session.Target.SetPositionAndRotation(new Vector3(0, 1.5f, 15), Quaternion.Euler(0, 180, 0));
            Vector3 centre = pivot.Find("RingOuter").localPosition;
            foreach (var renderer in pivot.GetComponentsInChildren<MeshRenderer>(true))
            {
                var part = new GameObject(renderer.name); part.transform.SetParent(session.Target, false);
                part.transform.localPosition = pivot.InverseTransformPoint(renderer.transform.position) - centre;
                part.transform.localRotation = Quaternion.Inverse(pivot.rotation) * renderer.transform.rotation;
                part.transform.localScale = renderer.transform.lossyScale;
                part.AddComponent<MeshFilter>().sharedMesh = renderer.GetComponent<MeshFilter>().sharedMesh;
                part.AddComponent<MeshRenderer>().sharedMaterials = renderer.sharedMaterials;
            }
            foreach (var camera in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Camera>())) camera.enabled = false;
            var settings = AssetDatabase.LoadAssetAtPath<WeaponSightCalibrationSettings>(WeaponSightAudit.SettingsPath);
            session.Distance = settings != null ? settings.DefaultZeroDistance : 15;
            EditorSceneManager.SaveScene(scene, ScenePath);
            return session;
        }

        public static bool HasEdits(ManualSightCalibrationSession session) => session != null && session.Instance != null
            && (Pose(session.Instance.transform) != session.SavedPose || Mathf.Abs(session.Distance - session.SavedDistance) > .00001f);

        public static void Load(ManualSightCalibrationSession session, WeaponInfo requested, bool discardEdits = false)
        {
            CheckEdit();
            if (session == null || requested == null || requested.WeaponPrefab == null) throw new ArgumentException("Нет сессии или оружия.");
            if (!discardEdits && HasEdits(session)) throw new InvalidOperationException("Сохраните правки или явно отмените их перед переключением.");
            var registry = AssetDatabase.LoadAssetAtPath<WeaponRegistry>(WeaponSightAudit.RegistryPath);
            var review = registry.GetById(requested.WeaponId + "_SightReview");
            var working = review != null ? review : requested;
            string input = AssetDatabase.GetAssetPath(working.WeaponPrefab);
            string output = input.StartsWith(SightGameplayReviewBuilder.Prefabs + "/", StringComparison.Ordinal)
                ? input : DraftFolder + "/" + working.WeaponId + "_ManualSight.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(output) != null) input = output;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(input);
            // Разрешаем всю конфигурацию до замены предыдущего экземпляра.
            var sources = prefab.GetComponentsInChildren<UxrProjectileSource>(true);
            var source = prefab.GetComponent<UxrProjectileSource>() ?? (sources.Length == 1 ? sources[0] : null);
            if (source == null || source.ShotTypes.Count == 0)
                throw new InvalidOperationException("Нужен однозначный UxrProjectileSource с ShotSource.");
            var settings = AssetDatabase.LoadAssetAtPath<WeaponSightCalibrationSettings>(WeaponSightAudit.SettingsPath);
            float distance = settings != null ? settings.DefaultZeroDistance : 15; int shot = 0;
            var profile = AssetDatabase.LoadAssetAtPath<WeaponSightCalibrationProfile>(ProfilePath(output));
            if (profile != null)
            {
                if (!profile.TryGetZeroDistance(settings, out distance)) throw new InvalidOperationException("Неверная дистанция профиля.");
                shot = profile.ShotTypeIndex;
            }
            if (!WeaponSightCalibrationSettings.IsValidDistance(distance) || shot < 0 || shot >= source.ShotTypes.Count || source.ShotTypes[shot].ShotSource == null)
                throw new InvalidOperationException("Неверная дистанция или выбранный ShotSource.");
            var placement = new GameObject("WeaponPlacement_EditorOnly").transform;
            placement.SetParent(session.transform, false);
            GameObject candidate;
            try
            {
                candidate = (GameObject)PrefabUtility.InstantiatePrefab(prefab, placement);
                var candidateSource = candidate.GetComponent<UxrProjectileSource>() ?? candidate.GetComponentsInChildren<UxrProjectileSource>(true).Single();
                var muzzle = candidateSource.ShotTypes[shot].ShotSource;
                placement.rotation = Quaternion.Inverse(muzzle.rotation);
                placement.position += new Vector3(0, 1.5f, 0) - muzzle.position;
            }
            catch { Object.DestroyImmediate(placement.gameObject); throw; }
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            var oldPlacement = session.Placement;
            Undo.RegisterCreatedObjectUndo(placement.gameObject, "Загрузить рабочую копию");
            Undo.RecordObjects(new Object[] { session, session.Target }, "Выбрать оружие для сведения");
            try
            {
                session.Placement = placement; session.Instance = candidate;
                session.Weapon = working; session.InputPath = input; session.OutputPath = output;
                session.Source = candidate.GetComponent<UxrProjectileSource>() ?? candidate.GetComponentsInChildren<UxrProjectileSource>(true).Single();
                session.ShotTypeIndex = shot; session.Distance = distance;
                session.Target.SetPositionAndRotation(session.Muzzle.position + session.Muzzle.forward * distance, Quaternion.LookRotation(-session.Muzzle.forward, session.Muzzle.up));
                CaptureBaseline(session); Undo.FlushUndoRecordObjects();
                if (oldPlacement != null) Undo.DestroyObjectImmediate(oldPlacement.gameObject);
                Undo.CollapseUndoOperations(group);
            }
            catch { Undo.FlushUndoRecordObjects(); Undo.RevertAllDownToGroup(group); throw; }
            EditorSceneManager.MarkSceneDirty(session.gameObject.scene);
            SceneView.RepaintAll();
        }

        public static void Align(ManualSightCalibrationSession session)
        {
            CheckEdit();
            if (session.Muzzle == null || !WeaponSightCalibrationSettings.IsValidDistance(session.Distance)) throw new InvalidOperationException("Неверный источник или дистанция.");
            Quaternion delta = Quaternion.Inverse(session.Muzzle.rotation);
            session.Placement.rotation = delta * session.Placement.rotation;
            session.Placement.position += new Vector3(0, 1.5f, 0) - session.Muzzle.position;
            SetDistance(session, session.Distance);
        }

        public static void SetDistance(ManualSightCalibrationSession session, float distance)
        {
            CheckEdit();
            if (!WeaponSightCalibrationSettings.IsValidDistance(distance)) throw new ArgumentOutOfRangeException(nameof(distance), "Дистанция должна быть в (0,20] м.");
            Undo.RecordObjects(new Object[] { session, session.Target }, "Дистанция сведения");
            session.Distance = distance;
            session.Target.position = session.Muzzle != null ? session.Muzzle.position + session.Muzzle.forward * distance : new Vector3(0, 1.5f, distance);
            session.Target.rotation = session.Muzzle != null ? Quaternion.LookRotation(-session.Muzzle.forward, session.Muzzle.up) : Quaternion.Euler(0, 180, 0);
            Undo.FlushUndoRecordObjects();
            EditorSceneManager.MarkSceneDirty(session.gameObject.scene); SceneView.RepaintAll();
        }

        public static SightAlignmentMarker Marker(ManualSightCalibrationSession session, SightAlignmentRole role) => session.Instance == null ? null
            : session.Instance.GetComponentsInChildren<SightAlignmentMarker>(true).FirstOrDefault(m => m.Role == role);

        /// <summary>Причина, по которой объект нельзя назначить деталью целика/мушки; null — можно.</summary>
        public static string PartProblem(ManualSightCalibrationSession session, Transform part)
        {
            if (session == null || session.Instance == null) return "Оружие не загружено.";
            if (part == null) return "Выделите деталь целика или мушки в Hierarchy.";
            if (part != session.Instance.transform && !part.IsChildOf(session.Instance.transform)) return "Выделенный объект не принадлежит загруженному оружию.";
            if (part.GetComponent<SightAlignmentMarker>() != null) return "Выделен сам маркер — выделите деталь, в которую его вложить.";
            return null;
        }

        /// <summary>
        /// Назначает деталь целиком (Rear) или мушкой (Front): Empty-маркер роли создаётся внутри неё
        /// или переносится из прежней детали. Стартовая точка — центр меша детали, дальше её ставят вручную.
        /// </summary>
        public static SightAlignmentMarker AssignMarker(ManualSightCalibrationSession session, Transform part, SightAlignmentRole role)
        {
            CheckEdit();
            string problem = PartProblem(session, part);
            if (problem != null) throw new InvalidOperationException(problem);
            var existing = session.Instance.GetComponentsInChildren<SightAlignmentMarker>(true).Where(m => m.Role == role).ToArray();
            if (existing.Length > 1) throw new InvalidOperationException("Найдено несколько маркеров этой роли — удалите лишние вручную.");
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            var marker = existing.FirstOrDefault();
            var go = marker != null ? marker.gameObject : new GameObject(role == SightAlignmentRole.Rear ? "SightReference_Rear" : "SightReference_Front");
            if (marker == null) Undo.RegisterCreatedObjectUndo(go, "Добавить точку прицела");
            Undo.SetTransformParent(go.transform, part, "Вложить точку прицела");
            Undo.RecordObject(go.transform, "Поставить точку прицела");
            go.transform.position = AimPointGuess(part); go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            if (marker == null) { marker = Undo.AddComponent<SightAlignmentMarker>(go); Undo.RecordObject(marker, "Роль точки прицела"); marker.Role = role; }
            Undo.CollapseUndoOperations(group);
            Selection.activeGameObject = go; EditorSceneManager.MarkSceneDirty(session.gameObject.scene); return marker;
        }

        // Pivot деталей из паков часто стоит в начале оружия, поэтому стартуем с центра собственного меша детали.
        private static Vector3 AimPointGuess(Transform part)
        {
            var own = part.GetComponent<Renderer>();
            if (own != null) return own.bounds.center;
            var nested = part.GetComponentsInChildren<Renderer>(true);
            if (nested.Length == 0) return part.position;
            var bounds = nested[0].bounds;
            foreach (var renderer in nested) bounds.Encapsulate(renderer.bounds);
            return bounds.center;
        }

        public static void ViewThroughSights(ManualSightCalibrationSession session)
        {
            CheckEdit();
            if (!session.TryMarkers(out var rear, out var front, out var reason)) throw new InvalidOperationException(reason);
            Vector3 forward = (front.transform.position - rear.transform.position).normalized;
            if (forward.sqrMagnitude < .5f || Vector3.Dot(forward, session.Muzzle.forward) <= 0) throw new InvalidOperationException("Неверный порядок точек прицела.");
            var view = SceneView.lastActiveSceneView ?? EditorWindow.GetWindow<SceneView>();
            view.orthographic = false;
            view.LookAtDirect(rear.transform.position, Quaternion.LookRotation(forward, session.Muzzle.up), .1f);
            // SceneView.size — орбитальный масштаб, cameraDistance — фактическое удаление перспективной камеры.
            view.size *= .18f / Mathf.Max(view.cameraDistance, .0001f);
            view.Focus(); view.Repaint();
        }

        public static void Save(ManualSightCalibrationSession session)
        {
            CheckEdit(); ValidateSave(session);
            string output = session.OutputPath, profilePath = ProfilePath(session);
            string before = JsonUtility.ToJson(session);
            string outputBackup = session.OutputExisted ? Backup(output) : null;
            string profileBackup = session.ProfileExisted ? Backup(profilePath) : null;
            bool outputTouched = false, profileTouched = false;
            try
            {
                Write(session, () => outputTouched = true, () => profileTouched = true);
            }
            catch (Exception original)
            {
                var failures = new List<Exception>();
                if (outputTouched) try { Restore(output, outputBackup); } catch (Exception error) { failures.Add(error); }
                if (profileTouched) try { Restore(profilePath, profileBackup); } catch (Exception error) { failures.Add(error); }
                JsonUtility.FromJsonOverwrite(before, session);
                EditorSceneManager.MarkSceneDirty(session.gameObject.scene);
                if (failures.Count > 0)
                {
                    failures.Insert(0, original);
                    throw new AggregateException("Сохранение остановлено; откат не завершён. Резервные файлы: " + outputBackup + "; " + profileBackup, failures);
                }
                throw;
            }
        }

        private static void Write(ManualSightCalibrationSession session, Action outputTouched, Action profileTouched)
        {
            CheckEdit();
            ValidateSave(session);
            EnsureFolder(DraftFolder); EnsureFolder(ProfileFolder);
            ValidateAssetStates(session);
            bool fresh = !session.OutputExisted;
            if (fresh && !AssetDatabase.CopyAsset(session.InputPath, session.OutputPath)) throw new IOException("Не удалось создать проверочную копию.");
            if (fresh) outputTouched();
            string backup = "tmp/weapon-sight-calibration/manual/backups/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff", CultureInfo.InvariantCulture) + "-" + Path.GetFileName(session.OutputPath);
            Directory.CreateDirectory(Path.GetDirectoryName(backup)); File.Copy(session.OutputPath, backup, false);
            var asset = PrefabUtility.LoadPrefabContents(session.OutputPath);
            try
            {
                var instanceNodes = GeometryNodes(session.Instance.transform); var assetNodes = GeometryNodes(asset.transform);
                ValidateHierarchy(instanceNodes, assetNodes);
                foreach (var pair in instanceNodes.Where(p => p.Key.Length > 0))
                {
                    var destination = assetNodes[pair.Key]; destination.localPosition = pair.Value.localPosition;
                    destination.localRotation = pair.Value.localRotation; destination.localScale = pair.Value.localScale;
                    if (PrefabUtility.IsPartOfPrefabInstance(destination)) PrefabUtility.RecordPrefabInstancePropertyModifications(destination);
                }
                foreach (var existing in asset.GetComponentsInChildren<SightAlignmentMarker>(true)) Object.DestroyImmediate(existing.gameObject);
                foreach (var marker in session.Instance.GetComponentsInChildren<SightAlignmentMarker>(true))
                {
                    string parentPath = instanceNodes.Single(p => p.Value == marker.transform.parent).Key;
                    var go = new GameObject(marker.name); go.transform.SetParent(assetNodes[parentPath], false);
                    go.transform.localPosition = marker.transform.localPosition; go.transform.localRotation = marker.transform.localRotation; go.transform.localScale = marker.transform.localScale;
                    go.AddComponent<SightAlignmentMarker>().Role = marker.Role;
                }
                if (fresh)
                    foreach (var component in asset.GetComponentsInChildren<UxrComponent>(true))
                    {
                        component.SetEditorUniqueId(Guid.NewGuid(), true, AssetDatabase.AssetPathToGUID(session.OutputPath));
                        if (PrefabUtility.IsPartOfPrefabInstance(component)) PrefabUtility.RecordPrefabInstancePropertyModifications(component);
                    }
                outputTouched();
                PrefabUtility.SaveAsPrefabAsset(asset, session.OutputPath, out bool success);
                if (!success) throw new IOException("Не удалось сохранить проверочный префаб.");
            }
            finally { PrefabUtility.UnloadPrefabContents(asset); }
            SaveCheckpointForDiagnostics?.Invoke("PrefabWritten");
            if (File.Exists(ProfilePath(session)) != session.ProfileExisted || Signature(ProfilePath(session)) != session.ProfileHash)
                throw new InvalidOperationException("Профиль изменён после загрузки. Настройка прицела не перезаписана.");
            var profile = AssetDatabase.LoadAssetAtPath<WeaponSightCalibrationProfile>(ProfilePath(session));
            profileTouched();
            if (profile == null) { profile = ScriptableObject.CreateInstance<WeaponSightCalibrationProfile>(); AssetDatabase.CreateAsset(profile, ProfilePath(session)); }
            var serialized = new SerializedObject(profile);
            serialized.FindProperty("_weapon").objectReferenceValue = session.Weapon;
            serialized.FindProperty("_sightId").stringValue = "ManualIron";
            serialized.FindProperty("_shotTypeIndex").intValue = session.ShotTypeIndex;
            serialized.FindProperty("_overrideZeroDistance").boolValue = true;
            serialized.FindProperty("_customZeroDistance").floatValue = session.Distance;
            serialized.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.SaveAssetIfDirty(profile);
            SaveCheckpointForDiagnostics?.Invoke("ProfileWritten");
            session.InputPath = session.OutputPath; CaptureBaseline(session);
            EditorSceneManager.MarkSceneDirty(session.gameObject.scene);
        }

        private static string Backup(string path)
        {
            string destination = "tmp/weapon-sight-calibration/manual/backups/" + Guid.NewGuid().ToString("N") + "-" + Path.GetFileName(path);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)); File.Copy(path, destination, false); return destination;
        }

        private static void Restore(string path, string backup)
        {
            if (backup == null)
            {
                if (File.Exists(path) && !AssetDatabase.DeleteAsset(path)) throw new IOException("Не удалось удалить незавершённый черновик: " + path);
                return;
            }
            File.Copy(backup, path, true);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
        }

        public static void ValidateSave(ManualSightCalibrationSession session)
        {
            if (session == null || session.Instance == null || session.Muzzle == null) throw new InvalidOperationException("Оружие не загружено.");
            if (!session.TryIntersection(out _, out _, out var reason)) throw new InvalidOperationException(reason);
            if ((session.Target.position - (session.Muzzle.position + session.Muzzle.forward * session.Distance)).sqrMagnitude > 1e-8f)
                throw new InvalidOperationException("ShotSource не смотрит в центр мишени. Нажмите «Выровнять ShotSource на центр».");
            var root = session.Instance.transform;
            if ((root.localPosition - session.RootPosition).sqrMagnitude > 1e-12f || Quaternion.Angle(root.localRotation, session.RootRotation) > .001f || (root.localScale - session.RootScale).sqrMagnitude > 1e-12f)
                throw new InvalidOperationException("Корень оружия изменён. Для служебного размещения используйте WeaponPlacement, для сведения — детали прицела.");
            if ((root.InverseTransformPoint(session.Muzzle.position) - session.MuzzlePosition).sqrMagnitude > 1e-12f || Vector3.Angle(root.InverseTransformDirection(session.Muzzle.forward), session.MuzzleForward) > .001f)
                throw new InvalidOperationException("ShotSource или его родитель изменён. Сохранение не должно корректировать направление/позицию пули.");
            if (session.ProtectedNodes == null || session.ProtectedShots == null) throw new InvalidOperationException("Перезагрузите оружие для фиксации источников пули.");
            foreach (var pose in session.ProtectedNodes)
                if (pose.Node == null || pose.Node.parent != pose.Parent || (pose.Node.localPosition - pose.Position).sqrMagnitude > 1e-12f || Quaternion.Angle(pose.Node.localRotation, pose.Rotation) > .001f || (pose.Node.localScale - pose.Scale).sqrMagnitude > 1e-12f)
                    throw new InvalidOperationException("Изменён Transform одного из ShotSource или его родителей. Сохранение остановлено.");
            foreach (var shot in session.ProtectedShots)
                if (shot.Source == null || shot.Source.ShotTypes.Count != shot.Count || shot.Source.ShotTypes[shot.Index].ShotSource != shot.Muzzle)
                    throw new InvalidOperationException("Изменена ссылка или список ShotSource. Перезагрузите оружие.");
            if (!session.ProtectedShots.Any(s => s.Source == session.Source)) throw new InvalidOperationException("Выбран другой источник пули.");
            ValidateAssetStates(session);
            foreach (var marker in session.Instance.GetComponentsInChildren<SightAlignmentMarker>(true))
                if (marker.transform.childCount != 0 || marker.GetComponents<Component>().Length != 2)
                    throw new InvalidOperationException("Каждый маркер должен быть отдельным Empty без других компонентов и детей.");
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(session.InputPath);
            ValidateHierarchy(GeometryNodes(root), GeometryNodes(source.transform));
            bool allowed = session.OutputPath.StartsWith(SightGameplayReviewBuilder.Prefabs + "/", StringComparison.Ordinal) || session.OutputPath.StartsWith(DraftFolder + "/", StringComparison.Ordinal);
            if (!allowed || session.OutputPath.Contains("..")) throw new InvalidOperationException("Сохранение разрешено только в папку проверочных копий.");
        }

        private static void CaptureBaseline(ManualSightCalibrationSession session)
        {
            var root = session.Instance.transform;
            session.RootPosition = root.localPosition; session.RootRotation = root.localRotation; session.RootScale = root.localScale;
            session.MuzzlePosition = root.InverseTransformPoint(session.Muzzle.position); session.MuzzleForward = root.InverseTransformDirection(session.Muzzle.forward);
            session.InputHash = Signature(session.InputPath);
            session.OutputExisted = File.Exists(session.OutputPath); session.OutputHash = Signature(session.OutputPath);
            session.ProfileExisted = File.Exists(ProfilePath(session)); session.ProfileHash = Signature(ProfilePath(session));
            var protectedNodes = new HashSet<Transform>(); var protectedShots = new List<ManualSightCalibrationSession.ProtectedShot>();
            foreach (var source in session.Instance.GetComponentsInChildren<UxrProjectileSource>(true))
                for (int i = 0; i < source.ShotTypes.Count; i++)
                {
                    var muzzle = source.ShotTypes[i].ShotSource;
                    protectedShots.Add(new ManualSightCalibrationSession.ProtectedShot { Source = source, Index = i, Count = source.ShotTypes.Count, Muzzle = muzzle });
                    for (var node = muzzle; node != null && (node == root || node.IsChildOf(root)); node = node.parent) protectedNodes.Add(node);
                }
            session.ProtectedNodes = protectedNodes.Select(n => new ManualSightCalibrationSession.ProtectedPose { Node = n, Parent = n.parent, Position = n.localPosition, Rotation = n.localRotation, Scale = n.localScale }).ToArray();
            session.ProtectedShots = protectedShots.ToArray();
            session.SavedPose = Pose(root); session.SavedDistance = session.Distance;
        }

        private static Dictionary<string, Transform> GeometryNodes(Transform root)
        {
            var result = new Dictionary<string, Transform>(); Visit(root, ""); return result;
            void Visit(Transform node, string path)
            {
                result.Add(path, node); int index = 0;
                foreach (Transform child in node)
                    if (child.GetComponent<SightAlignmentMarker>() == null) Visit(child, path + "/" + index++);
            }
        }

        private static void ValidateHierarchy(Dictionary<string, Transform> current, Dictionary<string, Transform> asset)
        {
            // Имя корня нового префаба Unity связывает с именем файла; корень не переносится из стенда.
            if (current.Count != asset.Count || current.Any(p => !asset.TryGetValue(p.Key, out var node) || (p.Key.Length > 0 && node.name != p.Value.name)))
                throw new InvalidOperationException("Иерархия деталей изменена. Стенд сохраняет Transform существующих деталей и два Empty-маркера; переименование/перестройку корпуса выполняйте отдельно.");
        }

        private static string Pose(Transform root)
        {
            var nodes = root.GetComponentsInChildren<Transform>(true).Select(t => new PoseNode {
                path = AnimationUtility.CalculateTransformPath(t, root) + "#" + t.GetSiblingIndex(),
                position = t.localPosition, rotation = t.localRotation, scale = t.localScale,
                role = t.GetComponent<SightAlignmentMarker>() != null ? (int)t.GetComponent<SightAlignmentMarker>().Role : -1 }).ToArray();
            return JsonUtility.ToJson(new PoseData { nodes = nodes });
        }
        [Serializable] private class PoseData { public PoseNode[] nodes; }
        [Serializable] private class PoseNode { public string path; public Vector3 position; public Quaternion rotation; public Vector3 scale; public int role; }
        private static string ProfilePath(ManualSightCalibrationSession session) => ProfilePath(session.OutputPath);
        private static string ProfilePath(string output) => ProfileFolder + "/" + Path.GetFileNameWithoutExtension(output) + ".asset";
        private static string Signature(string path)
        {
            if (!File.Exists(path)) return "";
            using (var hash = SHA256.Create()) return Convert.ToBase64String(hash.ComputeHash(File.ReadAllBytes(path))) + "|" + AssetDatabase.GetAssetDependencyHash(path);
        }
        private static void ValidateAssetStates(ManualSightCalibrationSession session)
        {
            if (Signature(session.InputPath) != session.InputHash || File.Exists(session.OutputPath) != session.OutputExisted || Signature(session.OutputPath) != session.OutputHash
                || File.Exists(ProfilePath(session)) != session.ProfileExisted || Signature(ProfilePath(session)) != session.ProfileHash)
                throw new InvalidOperationException("Ассет, назначение или профиль изменён после загрузки. Сохранение остановлено, чтобы не перезаписать чужие правки.");
        }
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/')); EnsureFolder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
        private static void CheckEdit()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Стенд работает только в готовом Edit Mode.");
        }
    }
}
