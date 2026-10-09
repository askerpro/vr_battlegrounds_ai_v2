using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
using UltimateXR.Core.Unique;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.LevelDesign;
using VrBattlegrounds.PhysicalSpaceUtils;

namespace VrBattlegrounds.Maps.Runtime
{
    [Flags]
    public enum MapDebugExemptions { None = 0, Calibration = 1, Stations = 2, TeamZones = 4 }

    /// <summary>Один авторский вход карты. Не хранит run state и не назначает station keys.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1690)]
    public sealed class MapRoot : MonoBehaviour
    {
        [SerializeField] private MapData _map;
        [SerializeField] private GameObject _environment;
        [SerializeField] private GameObject _gameplay;
        [SerializeField] private PhysicalArenaLayout _layout;
        [SerializeField] private TeamSpawnZone[] _zones = Array.Empty<TeamSpawnZone>();
        [SerializeField] private ArsenalStationCompositionBinding[] _stations = Array.Empty<ArsenalStationCompositionBinding>();
        public MapData Map => _map;

        /// <summary>
        /// Авторские станции без полной проверки — для клиентской привязки координатора развёртывания.
        /// Сервер берёт станции только из прошедшего <see cref="ValidateBindings"/>.
        /// </summary>
        public IReadOnlyList<ArsenalStationCompositionBinding> StationBindings =>
            Array.AsReadOnly(_stations ?? Array.Empty<ArsenalStationCompositionBinding>());

        /// <param name="includeSceneScans">
        /// Сканы всей сцены — сетевые <c>sceneId</c> и UltimateXR id каждого компонента. Это авторская
        /// проверка (preflight, миграция, сборка): в рантайме системы законно добавляют в сцену свои
        /// объекты — например, <c>UxrManager</c> навешивает <c>UxrCanvas</c> с пустым id на world-space
        /// канвасы (<c>AutoEnableOnWorldCanvases</c>), — и скан дал бы ложный отказ. Рантайм опирается на
        /// отпечаток содержимого, проверенный preflight-ом, и на проверки станций, которые остаются всегда.
        /// </param>
        public MapRootValidation ValidateBindings(bool includeSceneScans = true)
        {
            var errors = new List<string>();
            Scene scene = gameObject.scene;
            var roots = InScene<MapRoot>(scene);
            if (roots.Length != 1 || roots[0] != this) errors.Add("MapRoot.Count");
            if (!gameObject.activeInHierarchy) errors.Add("MapRoot.Inactive");
            if (!Unit(transform)) errors.Add("MapRoot.Transform");
            if (_map == null) errors.Add("MapData.Missing");
            else
            {
                if (!MapRunResolver.Identifier(_map.sceneName) || _map.sceneName != scene.name) errors.Add("MapData.SceneMismatch");
                if (!Enum.IsDefined(typeof(MapRunKind), _map.kind)) errors.Add("MapData.Kind.Invalid");
                if (_map.debugExemptions != MapDebugExemptions.None && _map.kind != MapRunKind.Debug)
                    errors.Add("MapData.Exemptions.RequireDebug");
                if ((_map.debugExemptions & ~(MapDebugExemptions.Calibration | MapDebugExemptions.Stations | MapDebugExemptions.TeamZones)) != 0)
                    errors.Add("MapData.Exemptions.Unknown");
            }
            CheckGroup(_environment, "Environment", scene, errors);
            CheckGroup(_gameplay, "Gameplay", scene, errors);
            if (_environment != null && _gameplay != null && (_environment == _gameplay ||
                _environment.transform.IsChildOf(_gameplay.transform) || _gameplay.transform.IsChildOf(_environment.transform)))
                errors.Add("Map.Groups.Overlap");
            bool exemptCalibration = Exempt(MapDebugExemptions.Calibration);
            if (_layout == null)
            {
                if (!exemptCalibration) errors.Add("PhysicalArenaLayout.Missing");
            }
            else
            {
                if (_layout.gameObject.scene != scene || !Unit(_layout.transform)) errors.Add("PhysicalArenaLayout.Frame");
                if ((_environment != null && _layout.transform.IsChildOf(_environment.transform)) ||
                    (_gameplay != null && _layout.transform.IsChildOf(_gameplay.transform))) errors.Add("PhysicalArenaLayout.GroupOverlap");
                if (_layout.GetComponentsInChildren<Collider>(true).Length != 0) errors.Add("PhysicalArenaLayout.Collider");
                if (_layout.diagnosticGeometry == null || _layout.diagnosticGeometry.activeSelf ||
                    !_layout.diagnosticGeometry.transform.IsChildOf(_layout.transform)) errors.Add("PhysicalArenaLayout.DiagnosticGeometry");
                var definition = _layout.GetComponent<PhysicalArenaDefinition>();
                if (definition == null || !definition.Valid(out _)) errors.Add("PhysicalArenaLayout.Definition");
                var anchors = _layout.GetComponentsInChildren<PhysicalSpaceAnchor>(true);
                if (anchors.Length != 2 || anchors.Any(a => !a.gameObject.activeInHierarchy) ||
                    (anchors.Length == 2 && (anchors[0].id == anchors[1].id ||
                        !PhysicalSpaceAnchorFrame.TryBuild(anchors[0].transform.position, anchors[1].transform.position, out _, out _))))
                    errors.Add("PhysicalArenaLayout.CalibrationAnchors");
                if (InScene<PhysicalSpaceAnchor>(scene).Any(a => !a.transform.IsChildOf(_layout.transform)))
                    errors.Add("PhysicalArenaLayout.ForeignAnchor");
            }
            var zones = _zones ?? Array.Empty<TeamSpawnZone>();
            var stationRefs = _stations ?? Array.Empty<ArsenalStationCompositionBinding>();
            var distinctZones = new HashSet<TeamSpawnZone>();
            foreach (var zone in zones)
                if (zone == null || !distinctZones.Add(zone) || zone.gameObject.scene != scene || !InGameplay(zone.transform))
                    errors.Add("Zone.Reference.Invalid");
            if (zones.Length == 0 && !Exempt(MapDebugExemptions.TeamZones)) errors.Add("Zone.Missing");
            if (InScene<TeamSpawnZone>(scene).Any(z => !distinctZones.Contains(z))) errors.Add("Zone.Unlisted");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            var identities = new HashSet<ulong>();
            var allUids = new HashSet<Guid>();
            var stationConfigs = new List<MapStationConfig>();
            var seen = new HashSet<ArsenalStationCompositionBinding>();
            foreach (var binding in stationRefs)
            {
                if (binding == null || !seen.Add(binding)) { errors.Add("Station.Reference.Invalid"); continue; }
                string key = binding.StationKey;
                if (!MapRunResolver.Identifier(key) || !keys.Add(key)) errors.Add("Station.Key.InvalidOrDuplicate:" + key);
                if (binding.gameObject.scene != scene || !InGameplay(binding.transform) ||
                    !((binding.transform.lossyScale - Vector3.one).sqrMagnitude < .000001f))
                    errors.Add("Station.Frame:" + key);
                if (binding.Controller == null || binding.Controller.gameObject != binding.gameObject ||
                    binding.GetComponent<ArsenalStationPresetBinding>() == null ||
                    binding.StationAnchor == null || binding.StationAnchor.gameObject != binding.gameObject ||
                    binding.StationAnchor.Wall != binding.Controller || binding.EquipmentPoses == null || binding.EquipmentPoses.gameObject != binding.gameObject)
                    errors.Add("Station.OwnerRefs:" + key);
                // Слоты станции вешает сборщик в ряды корпуса: корпус без рядов собрать нельзя.
                if (binding.GetComponentsInChildren<ArsenalSlotRow>(true).Length == 0) errors.Add("Station.Rows.Missing:" + key);
                // Авторских слотов в корпусе нет: их ID пересекались бы с ID сборщика.
                if (binding.GetComponentsInChildren<ArsenalSlotController>(true).Length != 0) errors.Add("Station.AuthoredSlots:" + key);
                var ni = binding.StationIdentity;
                if (ni == null || ni.gameObject != binding.gameObject || ni.sceneId == 0 || !identities.Add(ni.sceneId) ||
                    binding.GetComponentsInChildren<NetworkIdentity>(true).Length != 1) errors.Add("Station.SceneIdentity:" + key);
                if (binding.StationAnchor != null)
                {
                    if (binding.StationAnchor.Zone == null || !distinctZones.Contains(binding.StationAnchor.Zone) ||
                        binding.transform.IsChildOf(binding.StationAnchor.Zone.transform)) errors.Add("Station.Zone:" + key);
                    if (!binding.StationAnchor.HasBoardDirection) errors.Add("Station.Placement:" + key);
                    foreach (var marker in new[] { binding.StationAnchor.StandingPoint, binding.StationAnchor.ArenaFacing })
                        if (marker == null || marker.gameObject.scene != scene || !InGameplay(marker) ||
                            stationRefs.Any(s => s != null && s != binding && marker.IsChildOf(s.transform)))
                            errors.Add("Station.Placement.Reference:" + key);
                }
                var uids = binding.GetComponentsInChildren<MonoBehaviour>(true).OfType<IUxrUniqueId>().Select(u => u.UniqueId).ToArray();
                if (uids.Any(id => id == Guid.Empty || !allUids.Add(id))) errors.Add("Station.UxrIdentity:" + key);
                // Здесь станция описана только оболочкой: оформление, layout hash и версию схемы ID её записи до
                // публикации config подставляет MapBootstrap из описания генератора (MapArsenalCompositionAdapter.Apply).
                stationConfigs.Add(new MapStationConfig(key, string.Empty, "Generated", ShellFingerprint(binding, uids), 1));
            }
            if (stationRefs.Length == 0 && !Exempt(MapDebugExemptions.Stations)) errors.Add("Station.Missing");
            if (InScene<ArsenalWallController>(scene).Any(w => !seen.Any(s => s != null && s.Controller == w))) errors.Add("Station.Unlisted");
            if (_environment != null && (_environment.GetComponentsInChildren<NetworkIdentity>(true).Length != 0 ||
                _environment.GetComponentsInChildren<TeamSpawnZone>(true).Length != 0)) errors.Add("Environment.InteractiveContent");
            if (InScene<Managers.MapReferee>(scene).Length != 0 || InScene<ArsenalBoundaryWall>(scene).Length != 0)
                errors.Add("Map.LegacyServices.Present");
            if (includeSceneScans) ScanSceneIdentities(scene, errors);
            return new MapRootValidation(errors, errors.Count == 0 ? new MapRootBindings(_map, _environment, _gameplay,
                _layout, zones, stationRefs, new MapRunBindings(_map.sceneName, stationConfigs)) : null);
        }

        private static void ScanSceneIdentities(Scene scene, List<string> errors)
        {
            var sceneIds = new HashSet<ulong>();
            foreach (var identity in InScene<NetworkIdentity>(scene))
                if (identity.sceneId == 0 || !sceneIds.Add(identity.sceneId)) errors.Add("SceneIdentity.Invalid:" + identity.name);
            // Причина и владелец — в тексте ошибки: без них отказ запуска карты не расследовать.
            var sceneUids = new Dictionary<Guid, MonoBehaviour>();
            foreach (var behaviour in InScene<MonoBehaviour>(scene))
            {
                if (!(behaviour is IUxrUniqueId component)) continue;
                if (component.UniqueId == Guid.Empty) errors.Add("SceneUxrIdentity.Empty:" + Describe(behaviour));
                else if (sceneUids.TryGetValue(component.UniqueId, out MonoBehaviour first))
                    errors.Add("SceneUxrIdentity.Duplicate:" + Describe(behaviour) + "=" + Describe(first));
                else sceneUids.Add(component.UniqueId, behaviour);
            }
        }

        private bool Exempt(MapDebugExemptions flag) => _map != null && _map.kind == MapRunKind.Debug && (_map.debugExemptions & flag) != 0;
        private bool InGameplay(Transform item) => _gameplay != null && item.IsChildOf(_gameplay.transform);
        private static void CheckGroup(GameObject group, string name, Scene scene, List<string> errors)
        {
            if (group == null) { errors.Add(name + ".Missing"); return; }
            if (group.scene != scene || !group.activeInHierarchy || !Unit(group.transform)) errors.Add(name + ".Frame");
        }
        internal static bool Unit(Transform frame) => (frame.lossyScale - Vector3.one).sqrMagnitude < .000001f &&
            frame.position.sqrMagnitude < .000001f && Quaternion.Angle(frame.rotation, Quaternion.identity) < .001f;
        // Станция вправе иметь world placement; её scale проверяется отдельно от grouping roots.
        /// <summary>Отпечаток оболочки станции: ключ, sceneId, UniqueId корпуса и кадры размещения.</summary>
        private static string ShellFingerprint(ArsenalStationCompositionBinding binding, Guid[] uids)
        {
            var values = new List<string> { binding.StationKey ?? "", binding.StationIdentity != null ? binding.StationIdentity.sceneId.ToString() : "" };
            values.AddRange(uids.OrderBy(id => id).Select(id => id.ToString("D")));
            // Стабильные кадры размещения; анимируемые цели и предметы источником не являются.
            using (var stream = new System.IO.MemoryStream())
            using (var writer = new System.IO.BinaryWriter(stream))
            {
                foreach (var value in values) writer.Write(value);
                WriteFrame(writer, binding.transform);
                if (binding.StationAnchor != null)
                {
                    WriteFrame(writer, binding.StationAnchor.StandingPoint);
                    WriteFrame(writer, binding.StationAnchor.ArenaFacing);
                }
                writer.Flush();
                using (var hash = System.Security.Cryptography.SHA256.Create())
                    return BitConverter.ToString(hash.ComputeHash(stream.ToArray())).Replace("-", "").ToLowerInvariant();
            }
        }
        private static void WriteFrame(System.IO.BinaryWriter writer, Transform frame)
        {
            writer.Write(frame != null);
            if (frame == null) return;
            var p = frame.position; var q = frame.rotation; var s = frame.lossyScale;
            writer.Write(p.x); writer.Write(p.y); writer.Write(p.z);
            writer.Write(q.x); writer.Write(q.y); writer.Write(q.z); writer.Write(q.w);
            writer.Write(s.x); writer.Write(s.y); writer.Write(s.z);
        }
        private static string Describe(Component component)
        {
            var path = component.name;
            for (Transform t = component.transform.parent; t != null; t = t.parent) path = t.name + "/" + path;
            return component.GetType().Name + "@" + path;
        }

        internal static T[] InScene<T>(Scene scene) where T : Component => scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<T>(true)).ToArray();
    }

    public sealed class MapRootBindings
    {
        public MapData Map { get; }
        public GameObject Environment { get; }
        public GameObject Gameplay { get; }
        public PhysicalArenaLayout Layout { get; }
        public IReadOnlyList<TeamSpawnZone> Zones { get; }
        public IReadOnlyList<ArsenalStationCompositionBinding> Stations { get; }
        public MapRunBindings Description { get; }
        internal MapRootBindings(MapData map, GameObject environment, GameObject gameplay, PhysicalArenaLayout layout,
            TeamSpawnZone[] zones, ArsenalStationCompositionBinding[] stations, MapRunBindings description)
        {
            Map = map; Environment = environment; Gameplay = gameplay; Layout = layout;
            Zones = new ReadOnlyCollection<TeamSpawnZone>(zones.ToArray());
            Stations = new ReadOnlyCollection<ArsenalStationCompositionBinding>(stations.ToArray()); Description = description;
        }

        /// <summary>Те же ссылки с другим описанием станций (записи сгенерированных станций из описания генератора).</summary>
        internal MapRootBindings WithDescription(MapRunBindings description) =>
            new MapRootBindings(Map, Environment, Gameplay, Layout, Zones.ToArray(), Stations.ToArray(), description);
    }
    public sealed class MapRootValidation
    {
        public bool Passed => Errors.Count == 0 && Bindings != null;
        public IReadOnlyList<string> Errors { get; }
        public MapRootBindings Bindings { get; }
        internal MapRootValidation(IEnumerable<string> errors, MapRootBindings bindings)
        { Errors = new List<string>(errors).AsReadOnly(); Bindings = bindings; }
    }
}
