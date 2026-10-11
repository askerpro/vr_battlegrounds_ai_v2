using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.LevelDesign;
using VrBattlegrounds.LobbyZones;
using VrBattlegrounds.Maps.Runtime;
using VrBattlegrounds.PhysicalSpaceUtils;

namespace VrBattlegrounds.Editor.Maps.LobbyDecoration
{
    /// <summary>Единственный редакторский владелец производного размещения групп зоны.</summary>
    [InitializeOnLoad]
    public static class LobbyZoneLayoutEditor
    {
        private static int suppression;
        private static bool queued;
        private static readonly Dictionary<LobbyZoneLayoutBinding, string> reported = new Dictionary<LobbyZoneLayoutBinding, string>();

        static LobbyZoneLayoutEditor()
        {
            ObjectChangeEvents.changesPublished += ObjectsChanged;
            Undo.undoRedoPerformed += UndoChanged;
            EditorSceneManager.sceneOpened += (_, __) => Schedule();
            EditorApplication.playModeStateChanged += PlayChanged;
            Schedule();
        }

        private sealed class SuppressionScope : IDisposable
        {
            private bool disposed;
            public SuppressionScope() { suppression++; }
            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                suppression--;
            }
        }

        /// <summary>При пересборке старая связь не применяется к удаляемой или временной иерархии.</summary>
        public static IDisposable SuppressAutomaticSynchronization() => new SuppressionScope();

        public static LobbyZoneLayoutBinding BindAndCapture(Transform owner, Transform source,
            Transform environmentRoot, Transform gameplayRoot, params Transform[] groups)
        {
            RequireEditing();
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            var placements = Capture(source, groups);
            CheckReferences(owner, source, environmentRoot, gameplayRoot, placements, null);
            using (SuppressAutomaticSynchronization())
            {
                var binding = owner.GetComponent<LobbyZoneLayoutBinding>();
                if (binding == null) binding = Undo.AddComponent<LobbyZoneLayoutBinding>(owner.gameObject);
                Undo.RecordObject(binding, "Связать общую зону");
                binding.Configure(source, environmentRoot, gameplayRoot, placements);
                EditorUtility.SetDirty(binding);
                return binding;
            }
        }

        /// <summary>Явно принимает новое относительное размещение после правки ведомой группы.</summary>
        public static void CaptureRelativeArrangement(LobbyZoneLayoutBinding binding)
        {
            RequireEditing();
            Check(binding);
            var placements = Capture(binding.SourceZone, binding.Groups.Select(p => p.group).ToArray());
            using (SuppressAutomaticSynchronization())
            {
                Undo.RecordObject(binding, "Принять размещение групп зоны");
                binding.Configure(binding.SourceZone, binding.EnvironmentRoot, binding.GameplayRoot, placements);
                EditorUtility.SetDirty(binding);
                reported.Remove(binding);
            }
        }

        /// <summary>Явно связывает аренную композицию с неподвижным паспортом; сам паспорт не изменяется.</summary>
        public static void ConstrainToPhysicalArena(LobbyZoneLayoutBinding binding, PhysicalArenaDefinition definition)
        {
            RequireEditing();
            Check(binding);
            ReadPhysicalArenaFloor(binding, definition);
            using (SuppressAutomaticSynchronization())
            {
                Undo.RecordObject(binding, "Привязать композицию к физической арене");
                binding.SetPhysicalArenaConstraint(definition);
                EditorUtility.SetDirty(binding);
                EditorSceneManager.MarkSceneDirty(binding.gameObject.scene);
            }
        }

        private static Bounds ReadPhysicalArenaFloor(LobbyZoneLayoutBinding binding, PhysicalArenaDefinition definition)
        {
            Scene scene = binding.gameObject.scene;
            if (definition == null || definition.gameObject.scene != scene)
                throw new InvalidOperationException("Потеряна ссылка на паспорт физической арены этой сцены.");
            if (definition.GetComponent<PhysicalArenaLayout>() == null || !Identity(definition.transform)
                || !definition.gameObject.activeInHierarchy
                || scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<PhysicalArenaDefinition>(true)).Count() != 1)
                throw new InvalidOperationException("Нужен единственный активный паспорт PhysicalArenaLayout с единичным трансформом.");
            if (!definition.Valid(out string reason) || definition.floorShape == null || !definition.TryFloorBounds(out Bounds floor))
                throw new InvalidOperationException("Некорректны данные пола физической арены. " + reason);
            Transform arena = definition.transform;
            if (Under(arena, binding.SourceZone) || Under(binding.SourceZone, arena)
                || binding.Groups.Any(p => Under(arena, p.group) || Under(p.group, arena)))
                throw new InvalidOperationException("Паспорт физической арены не может входить в перемещаемую композицию или управлять её группами.");
            return floor;
        }

        private static void ValidatePhysicalArenaPose(LobbyZoneLayoutBinding binding)
        {
            if (!binding.ConstrainedToPhysicalArena) return;
            Bounds floor = ReadPhysicalArenaFloor(binding, binding.PhysicalArena);
            Vector3 canonical = new Vector3(floor.center.x, floor.max.y, floor.center.z);
            Transform source = binding.SourceZone;
            if (!Close(source.position, canonical) || !Close(source.rotation, Quaternion.identity) || !ScaleOne(source))
                throw new InvalidOperationException("Композиция шатра и стрельбища смещена относительно неподвижной физической арены. Верните её в центр пола, на его верхнюю поверхность, с поворотом 0 и масштабом 1. Сохранение, Play Mode и сборка недоступны до восстановления совмещения.");
        }

        private static LobbyZoneLayoutBinding.GroupPlacement[] Capture(Transform source, Transform[] groups)
        {
            if (source == null) throw new InvalidOperationException("Не назначена общая зона окружения.");
            if (groups == null || groups.Length == 0) throw new InvalidOperationException("Не назначены связанные группы.");
            return groups.Select(group =>
            {
                if (group == null) throw new InvalidOperationException("Потеряна связанная группа зоны.");
                return new LobbyZoneLayoutBinding.GroupPlacement
                {
                    group = group, position = source.InverseTransformPoint(group.position),
                    rotation = Quaternion.Inverse(source.rotation) * group.rotation, scale = group.lossyScale
                };
            }).ToArray();
        }

        private static void Check(LobbyZoneLayoutBinding binding)
        {
            if (binding == null) throw new InvalidOperationException("Потеряна привязка зоны.");
            CheckReferences(binding.transform, binding.SourceZone, binding.EnvironmentRoot,
                binding.GameplayRoot, binding.Groups, binding);
        }

        private static bool Under(Transform item, Transform parent) => item == parent || item.IsChildOf(parent);
        private static bool ScaleOne(Transform item) => Close(item.lossyScale, Vector3.one);
        private static bool Identity(Transform item) => ScaleOne(item) && Close(item.position, Vector3.zero)
            && Close(item.rotation, Quaternion.identity);
        private static bool Close(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < .00000001f;
        private static bool Close(Quaternion a, Quaternion b) => Quaternion.Angle(a, b) < .001f;
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Finite(Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        private static bool Finite(Quaternion value) => Finite(value.x) && Finite(value.y) && Finite(value.z)
            && Finite(value.w) && Mathf.Abs(value.x * value.x + value.y * value.y + value.z * value.z + value.w * value.w - 1) < .001f;

        private static void CheckReferences(Transform owner, Transform source, Transform environment,
            Transform gameplay, LobbyZoneLayoutBinding.GroupPlacement[] groups, LobbyZoneLayoutBinding self)
        {
            Scene scene = owner.gameObject.scene;
            if (!scene.IsValid() || !scene.isLoaded || EditorSceneManager.IsPreviewScene(scene)
                || EditorUtility.IsPersistent(owner) || PrefabUtility.IsPartOfPrefabAsset(owner))
                throw new InvalidOperationException("Связь зоны создаётся только в рабочей сцене после установки префаба.");
            if (environment == null || gameplay == null || environment == gameplay
                || environment.gameObject.scene != scene || gameplay.gameObject.scene != scene
                || environment.parent != null || gameplay.parent != null || !Identity(environment) || !Identity(gameplay))
                throw new InvalidOperationException("Корни Environment и Gameplay должны принадлежать сцене и иметь единичный трансформ.");
            if (!Under(owner, gameplay)) throw new InvalidOperationException("Владелец привязки должен находиться в Gameplay.");
            if (source == null || source == environment || source.gameObject.scene != scene || !Under(source, environment)
                || !ScaleOne(source) || !Close(source.localScale, Vector3.one))
                throw new InvalidOperationException("Назначьте зону внутри Environment с масштабом 1; масштабируйте отдельные декорации.");
            if (Forbidden(source) || Interactive(source)) throw new InvalidOperationException("Общая зона содержит игровую сущность или физическую разметку.");
            if (groups == null || groups.Length == 0) throw new InvalidOperationException("У общей зоны нет связанных групп.");
            var seen = new HashSet<Transform>();
            foreach (var placement in groups)
            {
                Transform group = placement.group;
                if (group == null || group.gameObject.scene != scene || !seen.Add(group) || group == environment || group == gameplay
                    || Under(group, source) || Under(source, group) || Forbidden(group)
                    || !(Under(group, gameplay) || (Under(group, environment) && !Interactive(group))))
                    throw new InvalidOperationException("Связанная группа потеряна, повторена или нарушает границы общей зоны.");
                if (group.parent == null || !ScaleOne(group.parent))
                    throw new InvalidOperationException("Родитель связанной группы должен иметь масштаб 1.");
                if (!Finite(placement.position) || !Finite(placement.rotation) || !Finite(placement.scale)
                    || placement.scale.x <= 0 || placement.scale.y <= 0 || placement.scale.z <= 0)
                    throw new InvalidOperationException("Повреждено сохранённое размещение группы зоны.");
            }
            var items = seen.ToArray();
            for (int i = 0; i < items.Length; i++)
                for (int j = i + 1; j < items.Length; j++)
                    if (Under(items[i], items[j]) || Under(items[j], items[i]))
                        throw new InvalidOperationException("Связанные группы не должны быть вложены друг в друга.");
            foreach (var other in Bindings(scene).Where(b => b != self && b.gameObject != owner.gameObject))
            {
                if (other.SourceZone != null && seen.Any(g => Under(other.SourceZone, g)))
                    throw new InvalidOperationException("Связанная группа не может содержать источник другой зоны.");
                foreach (var p in other.Groups)
                    if (p.group != null && (Under(source, p.group) || seen.Any(g => Under(g, p.group) || Under(p.group, g))))
                        throw new InvalidOperationException("Одной группой уже управляет другая привязка зоны.");
            }
        }

        private static bool Forbidden(Transform group) => group.GetComponentsInChildren<MapRoot>(true).Length != 0
            || group.GetComponentsInChildren<PhysicalArenaLayout>(true).Length != 0
            || group.GetComponentsInChildren<PhysicalSpaceAnchor>(true).Length != 0;

        private static bool Interactive(Transform group) => group.GetComponentsInChildren<Mirror.NetworkIdentity>(true).Length != 0
            || group.GetComponentsInChildren<Rigidbody>(true).Length != 0
            || group.GetComponentsInChildren<VrBattlegrounds.Maps.ShootingTarget>(true).Length != 0
            || group.GetComponentsInChildren<VrBattlegrounds.Maps.TeamSpawnZone>(true).Length != 0
            || group.GetComponentsInChildren<VrBattlegrounds.Arsenal.ArsenalWallController>(true).Length != 0
            || group.GetComponentsInChildren<VrBattlegrounds.Arsenal.ArsenalStationAnchor>(true).Length != 0
            || group.GetComponentsInChildren<UltimateXR.Manipulation.UxrGrabbableObject>(true).Length != 0;

        private static LobbyZoneLayoutBinding[] Bindings(Scene scene) => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<LobbyZoneLayoutBinding>(true)).ToArray();

        private static bool Matches(LobbyZoneLayoutBinding.GroupPlacement placement, Vector3 origin, Quaternion rotation)
            => Close(placement.group.position, origin + rotation * placement.position)
                && Close(placement.group.rotation, rotation * placement.rotation) && Close(placement.group.lossyScale, placement.scale);

        /// <summary>Принимает только изменение источника; самостоятельную правку ведомого не затирает.</summary>
        private static void Reconcile(LobbyZoneLayoutBinding binding, bool recordAuthoringUndo)
        {
            Check(binding);
            Transform source = binding.SourceZone;
            var groups = binding.Groups;
            bool moved = !Close(source.position, binding.LastSourcePosition) || !Close(source.rotation, binding.LastSourceRotation);
            foreach (var p in groups)
                if (!Matches(p, source.position, source.rotation)
                    && (!moved || !Matches(p, binding.LastSourcePosition, binding.LastSourceRotation)))
                    throw new InvalidOperationException("Группа «" + p.group.name + "» изменена отдельно. Примите её новое относительное размещение явно.");
            if (!moved) return;
            using (SuppressAutomaticSynchronization())
            {
                // Автоматическое следование и его frame — производные данные без отдельного Undo.
                // Undo записывается здесь только для явной авторской нормализации перед генерацией.
                if (recordAuthoringUndo)
                {
                    var changed = groups.Where(p => !Matches(p, source.position, source.rotation)).Select(p => (UnityEngine.Object)p.group).ToArray();
                    Undo.RecordObject(binding, "Переместить общую зону");
                    if (changed.Length != 0) Undo.RecordObjects(changed, "Переместить группы общей зоны");
                }
                foreach (var p in groups)
                {
                    p.group.SetPositionAndRotation(source.position + source.rotation * p.position, source.rotation * p.rotation);
                    p.group.localScale = p.scale;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(p.group);
                    EditorUtility.SetDirty(p.group);
                }
                binding.RecordSourceFrame();
                EditorUtility.SetDirty(binding);
                EditorSceneManager.MarkSceneDirty(binding.gameObject.scene);
            }
        }

        public static void ValidateScene(Scene scene)
        {
            RequireEditing();
            var bindings = Bindings(scene);
            foreach (var binding in bindings) Reconcile(binding, false);
            // В редакторском черновике группы следуют за источником; физическое совмещение проверяется после этого.
            foreach (var binding in bindings) ValidatePhysicalArenaPose(binding);
        }

        /// <summary>Явная подготовка world-coordinate генератора: сначала нормализуются источник и все его группы.</summary>
        public static void NormalizeForRegeneration(IEnumerable<LobbyZoneLayoutBinding> bindings, Vector3 canonicalWorldPivot)
        {
            RequireEditing();
            var items = bindings.ToArray();
            // Сначала сверяем все связи, чтобы повреждённые данные не начали частичную нормализацию.
            foreach (var binding in items) Reconcile(binding, true);
            using (SuppressAutomaticSynchronization())
                foreach (var binding in items)
                {
                    Transform source = binding.SourceZone;
                    Undo.RecordObject(source, "Подготовить зону к пересборке");
                    source.SetPositionAndRotation(canonicalWorldPivot, Quaternion.identity);
                    source.localScale = Vector3.one;
                    PrefabUtility.RecordPrefabInstancePropertyModifications(source);
                    Reconcile(binding, true);
                }
        }

        /// <summary>В сборке только проверяет сохранённые данные, не авторит производные трансформы.</summary>
        public static void ValidateBakedScene(Scene scene)
        {
            foreach (var binding in Bindings(scene))
            {
                Check(binding);
                if (binding.Groups.Any(p => !Matches(p, binding.SourceZone.position, binding.SourceZone.rotation)))
                    throw new InvalidOperationException("Размещение групп общей зоны не сохранено перед сборкой.");
                ValidatePhysicalArenaPose(binding);
            }
        }

        private static IEnumerable<Scene> LoadedScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.isLoaded && !EditorSceneManager.IsPreviewScene(scene)) yield return scene;
            }
        }

        public static void ValidateLoadedScenes()
        {
            if (suppression != 0) return;
            FlushPendingSynchronization();
            foreach (Scene scene in LoadedScenes()) ValidateScene(scene);
        }

        public static void FlushPendingSynchronization()
        {
            if (!queued || suppression != 0 || EditorApplication.isPlaying) return;
            EditorApplication.update -= Flush;
            queued = false;
            string failure = null;
            foreach (Scene scene in LoadedScenes())
                foreach (var binding in Bindings(scene))
                    try { Reconcile(binding, false); reported.Remove(binding); }
                    catch (Exception error) { Report(binding, error.Message); failure = error.Message; }
            if (failure != null) throw new InvalidOperationException(failure);
        }

        private static void RequireEditing()
        {
            if (EditorApplication.isPlaying) throw new InvalidOperationException("Компоновка общей зоны редактируется вне Play Mode.");
        }

        private static void ObjectsChanged(ref ObjectChangeEventStream stream)
        {
            if (suppression != 0 || EditorApplication.isPlayingOrWillChangePlaymode) return;
            for (int i = 0; i < stream.length; i++)
                if (stream.GetEventType(i) != ObjectChangeKind.ChangeAssetObjectProperties)
                { Schedule(); return; }
        }

        private static void UndoChanged() => Schedule();
        private static void Schedule()
        {
            if (EditorApplication.isPlaying) return;
            if (queued) return;
            queued = true;
            // Подписка существует только пока есть работа; не зависит от дренирования delayCall редактором.
            EditorApplication.update -= Flush;
            EditorApplication.update += Flush;
        }

        private static void Flush()
        {
            if (!queued) { EditorApplication.update -= Flush; return; }
            // Временный запрет не теряет запрос. Следующий разрешённый tick завершит его.
            if (suppression != 0 || EditorApplication.isPlayingOrWillChangePlaymode) return;
            try { FlushPendingSynchronization(); }
            catch (InvalidOperationException) { /* Отдельные ошибки уже записаны один раз для каждой привязки. */ }
        }

        private static void Report(LobbyZoneLayoutBinding binding, string message)
        {
            foreach (var missing in reported.Keys.Where(key => key == null).ToArray()) reported.Remove(missing);
            if (reported.TryGetValue(binding, out string previous) && previous == message) return;
            reported[binding] = message;
            GameLog.Error("Общая зона: " + message, binding);
        }

        private static void PlayChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode || suppression != 0) return;
            try { ValidateLoadedScenes(); }
            catch (Exception error)
            {
                EditorApplication.isPlaying = false;
                GameLog.Error("Запуск отменён: " + error.Message);
            }
        }
    }

    // Этот callback может исключить несогласованную сцену из сохранения, в отличие от sceneSaving.
    public sealed class LobbyZoneSaveGuard : AssetModificationProcessor
    {
        private static string[] OnWillSaveAssets(string[] paths)
        {
            var blocked = new HashSet<string>();
            foreach (string path in paths.Where(p => p.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)))
            {
                Scene scene = SceneManager.GetSceneByPath(path);
                if (!scene.IsValid() || !scene.isLoaded || EditorApplication.isPlaying) continue;
                try { LobbyZoneLayoutEditor.ValidateScene(scene); }
                catch (Exception error) { blocked.Add(path); GameLog.Error("Сохранение сцены отменено: " + error.Message); }
            }
            return paths.Where(p => !blocked.Contains(p)).ToArray();
        }
    }

    public sealed class LobbyZoneBuildGuard : IProcessSceneWithReport
    {
        public int callbackOrder => -100;
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null) return;
            try { LobbyZoneLayoutEditor.ValidateBakedScene(scene); }
            catch (Exception error) { throw new BuildFailedException("Общая зона: " + error.Message); }
        }
    }

    [CustomEditor(typeof(LobbyZoneLayoutBinding))]
    public sealed class LobbyZoneLayoutBindingInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var binding = (LobbyZoneLayoutBinding)target;
            EditorGUILayout.HelpBox("Перемещайте общую зону окружения: связанные группы перемещаются вместе с ней. Отдельные правки мишеней внутри группы сохраняются.", MessageType.Info);
            using (new EditorGUI.DisabledScope(true)) DrawDefaultInspector();
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Принять новое размещение связанных групп"))
                    try { LobbyZoneLayoutEditor.CaptureRelativeArrangement(binding); }
                    catch (Exception error) { GameLog.Error(error.Message, binding); }
        }
    }
}
