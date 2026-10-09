using System;
using System.Collections.Generic;
using System.Linq;
using Mirror;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.GameModes;
using VrBattlegrounds.Managers;

namespace VrBattlegrounds.Maps.Runtime
{
    /// <summary>Центральные typed prefab refs и derived fingerprints. Не per-map service roster.</summary>
    [CreateAssetMenu(fileName = "MapRuntimeCatalog", menuName = "VR Battlegrounds/Maps/Runtime Catalog")]
    public sealed class MapRuntimeCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class ContentEntry
        {
            public MapData Map;
            public string ScenePath;
            public string ContentFingerprint;
            public string ArsenalFingerprint;
        }
        [SerializeField] private MapRegistry _maps;
        [Tooltip("Отладочные стенды (MapData.kind = Debug): запускаются тем же MapBootstrap, но в меню реестра карт их нет.")]
        [SerializeField] private MapData[] _debugMaps = Array.Empty<MapData>();
        [SerializeField] private GameModeRegistry _modes;
        [SerializeField] private MapReferee _refereePrefab;
        [SerializeField] private ArsenalBoundaryWall _coordinatorPrefab;
        [SerializeField] private ContentEntry[] _content = Array.Empty<ContentEntry>();
        [SerializeField] private uint _compositionVersion = 1;
        [Tooltip("Скомпилированные ресурсы генератора арсенала. Нужен только картам со станциями в режиме Generated: " +
                 "без него такая карта не запускается (Station.Generated.CatalogMissing).")]
        [SerializeField] private ArsenalCompositionCatalog _arsenalComposition;
        public MapRegistry Maps => _maps;

        /// <summary>Ресурсы генератора для станций в режиме Generated; null — сгенерированных станций в проекте нет.</summary>
        public ArsenalCompositionCatalog ArsenalComposition => _arsenalComposition;

        /// <summary>Отладочные стенды каталога: вне меню реестра, тот же запуск карты.</summary>
        public IReadOnlyList<MapData> DebugMaps => Array.AsReadOnly(_debugMaps ?? Array.Empty<MapData>());

        /// <summary>Все карты каталога: реестр меню и отладочные стенды.</summary>
        public IEnumerable<MapData> AllMaps =>
            (_maps != null && _maps.maps != null ? _maps.maps : Array.Empty<MapData>()).Concat(_debugMaps ?? Array.Empty<MapData>());

        /// <summary>Каноническая MapData сцены: из реестра, иначе из отладочных стендов.</summary>
        public MapData FindMap(string sceneName)
        {
            MapData map = _maps != null && _maps.maps != null ? _maps.GetBySceneName(sceneName) : null;
            if (map != null) return map;
            foreach (MapData debug in _debugMaps ?? Array.Empty<MapData>())
                if (debug != null && debug.sceneName == sceneName) return debug;
            return null;
        }
        public GameModeRegistry Modes => _modes;
        public MapReferee RefereePrefab => _refereePrefab;
        public ArsenalBoundaryWall CoordinatorPrefab => _coordinatorPrefab;

        /// <summary>Запечённый отпечаток содержимого карты; runtime не пересчитывает его по assets.</summary>
        public string ContentFingerprintFor(MapData map)
        {
            foreach (var entry in _content ?? Array.Empty<ContentEntry>())
                if (entry != null && entry.Map == map) return entry.ContentFingerprint;
            return null;
        }

        public MapCatalogValidation Validate(IReadOnlyList<GameObject> registeredPrefabs)
        {
            var errors = new List<string>();
            if (_maps == null || _maps.maps == null || _maps.maps.Length == 0) errors.Add("Catalog.MapRegistry.Missing");
            else if (AllMaps.Where(m => m != null).Select(m => m.sceneName).Distinct(StringComparer.Ordinal).Count() != AllMaps.Count())
                errors.Add("Catalog.MapRegistry.InvalidOrDuplicate");
            foreach (MapData debug in _debugMaps ?? Array.Empty<MapData>())
                if (debug == null || debug.kind != MapRunKind.Debug) errors.Add("Catalog.DebugMap.NotDebug:" + (debug != null ? debug.sceneName : "null"));
            if (_modes == null || _modes.Warmup == null) errors.Add("Catalog.ModeRegistry.Warmup.Missing");
            if (_compositionVersion == 0) errors.Add("Catalog.CompositionVersion.Invalid");
            if (registeredPrefabs == null) errors.Add("Catalog.SpawnRegistry.Missing");
            var required = new HashSet<GameObject>();
            AddPrefab(_refereePrefab != null ? _refereePrefab.gameObject : null, "Referee", required, errors);
            AddPrefab(_coordinatorPrefab != null ? _coordinatorPrefab.gameObject : null, "Coordinator", required, errors);
            var descriptions = new List<MapRunMapDescription>();
            var seen = new HashSet<MapData>();
            foreach (var entry in _content ?? Array.Empty<ContentEntry>())
            {
                if (entry == null || entry.Map == null || !seen.Add(entry.Map)) { errors.Add("Catalog.Content.InvalidOrDuplicate"); continue; }
                var map = entry.Map;
                if (FindMap(map.sceneName) != map) errors.Add("Catalog.Content.ForeignMap:" + map.sceneName);
                if (string.IsNullOrEmpty(entry.ScenePath) || !MapRunResolver.Identifier(entry.ContentFingerprint) ||
                    !MapRunResolver.Identifier(entry.ArsenalFingerprint)) errors.Add("Catalog.Content.Provenance:" + map.sceneName);
                if (map.arsenalPreset == null || !MapRunResolver.Identifier(map.arsenalPreset.PresetId)) errors.Add("Map.Arsenal.Missing:" + map.sceneName);
                else
                {
                    var weapons = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var item in map.arsenalPreset.Entries)
                    {
                        if (item.Weapon == null || !MapRunResolver.Identifier(item.Weapon.WeaponId) || !weapons.Add(item.Weapon.WeaponId) ||
                            string.IsNullOrWhiteSpace(item.Row)) { errors.Add("Map.Arsenal.Entry.Invalid:" + map.sceneName); continue; }
                        AddPrefab(item.Weapon.WeaponPrefab, "Weapon:" + item.Weapon.WeaponId, required, errors);
                        AddPrefab(item.Weapon.MagazinePrefab, "Magazine:" + item.Weapon.WeaponId, required, errors);
                    }
                }
                if (map.supportedModes != null && map.supportedModes.Any(m => m == null)) errors.Add("Map.Mode.Null:" + map.sceneName);
                foreach (var mode in map.supportedModes ?? Array.Empty<GameModeData>())
                    if (mode != null && (_modes == null || (mode != _modes.Warmup &&
                        !(_modes.modes ?? Array.Empty<GameModeData>()).Any(m => m == mode))))
                        errors.Add("Map.Mode.NonCanonical:" + map.sceneName + "/" + mode.modeId);
                descriptions.Add(new MapRunMapDescription(map.sceneName, map.kind, entry.ContentFingerprint, _compositionVersion,
                    map.arsenalPreset != null ? map.arsenalPreset.PresetId : null, entry.ArsenalFingerprint,
                    (map.supportedModes ?? Array.Empty<GameModeData>()).Where(m => m != null && (_modes == null || m != _modes.Warmup)).Select(m => m.modeId)));
            }
            if (AllMaps.Any(m => m == null || !seen.Contains(m))) errors.Add("Catalog.Content.Incomplete");
            if (_modes != null)
            {
                var all = new List<GameModeData>(_modes.modes ?? Array.Empty<GameModeData>());
                if (_modes.Warmup != null) all.Add(_modes.Warmup);
                foreach (var mode in all)
                {
                    if (mode == null || !MapRunResolver.Identifier(mode.modeId)) { errors.Add("Catalog.Mode.Invalid"); continue; }
                    AddPrefab(mode.modePrefab, "Mode:" + mode.modeId, required, errors);
                    if (mode.modePrefab != null && mode.modePrefab.GetComponent<GameMode>() == null) errors.Add("Mode.Component.Missing:" + mode.modeId);
                    if (mode != _modes.Warmup && (mode.teams == null || mode.teams.Length < 1 || mode.teams.Any(t => t == null || t.teamIndex <= 0) ||
                        mode.teams.Select(t => t != null ? t.teamIndex : 0).Distinct().Count() != mode.teams.Length)) errors.Add("Mode.Teams.Invalid:" + mode.modeId);
                }
            }
            var assetIds = new Dictionary<uint, GameObject>();
            foreach (var prefab in registeredPrefabs ?? Array.Empty<GameObject>())
            {
                var ni = prefab != null ? prefab.GetComponent<NetworkIdentity>() : null;
                if (ni == null || ni.assetId == 0 || ni.sceneId != 0 || prefab.GetComponentsInChildren<NetworkIdentity>(true).Length != 1)
                { errors.Add("SpawnRegistry.Identity.Invalid:" + (prefab != null ? prefab.name : "null")); continue; }
                if (assetIds.TryGetValue(ni.assetId, out var duplicate) && duplicate != prefab)
                    errors.Add("SpawnRegistry.AssetId.Collision:" + prefab.name);
                else assetIds[ni.assetId] = prefab;
            }
            foreach (var prefab in required)
            {
                var identity = prefab.GetComponent<NetworkIdentity>();
                if (identity == null || identity.assetId == 0 || identity.sceneId != 0 || prefab.GetComponentsInChildren<NetworkIdentity>(true).Length != 1)
                { errors.Add("Prefab.Identity.Invalid:" + prefab.name); continue; }
                if (assetIds.TryGetValue(identity.assetId, out var previous) && previous != prefab) errors.Add("Prefab.AssetId.Duplicate:" + prefab.name);
                else assetIds[identity.assetId] = prefab;
                if (registeredPrefabs == null || !registeredPrefabs.Contains(prefab)) errors.Add("Prefab.Unregistered:" + prefab.name);
            }
            var catalog = errors.Count == 0 ? new MapRunResolverCatalog(_modes.Warmup.modeId,
                (_modes.modes ?? Array.Empty<GameModeData>()).Where(m => m != null).Select(m => m.modeId), descriptions) : null;
            // Чистый resolver также проверяет весь catalog: unknown IDs/duplicates/map kinds.
            if (catalog != null && descriptions.Count > 0)
            {
                var first = descriptions[0];
                var check = MapRunResolver.Resolve(new MapRunRequest(new MapRunKey(Guid.NewGuid(), 1), first.Scene, "", first.ContentFingerprint),
                    catalog, new MapRunBindings(first.Scene, Array.Empty<MapStationConfig>()));
                errors.AddRange(check.Errors);
            }
            return new MapCatalogValidation(errors, errors.Count == 0 ? catalog : null);
        }

        public MapRunResolution Resolve(MapRunRequest request, MapRootBindings bindings, IReadOnlyList<GameObject> registeredPrefabs)
        {
            var validation = Validate(registeredPrefabs);
            var errors = new List<string>(validation.Errors);
            if (bindings == null) errors.Add("Map.Bindings.Missing");
            if (errors.Count != 0) return new MapRunResolution(null, errors);
            var canonical = FindMap(request.MapScene);
            if (canonical == null) errors.Add("Map.Unknown:" + request.MapScene);
            else if (bindings.Map != canonical) errors.Add("Map.Bindings.MapDataMismatch");
            if (errors.Count != 0) return new MapRunResolution(null, errors);
            if (bindings.Map.kind == MapRunKind.Combat)
                foreach (var mode in bindings.Map.supportedModes ?? Array.Empty<GameModeData>())
                    if (mode != null && mode != _modes.Warmup)
                        foreach (var team in mode.teams ?? Array.Empty<VrBattlegrounds.TeamData>())
                            if (team != null && !bindings.Zones.Any(z => z != null && z.HomeTeam == team)) errors.Add("Zone.Team.Missing:" + team.teamIndex);
            return errors.Count == 0 ? MapRunResolver.Resolve(request, validation.Description, bindings.Description) : new MapRunResolution(null, errors);
        }
        private static void AddPrefab(GameObject prefab, string role, HashSet<GameObject> required, List<string> errors)
        { if (prefab == null) errors.Add("Prefab.Missing:" + role); else required.Add(prefab); }

#if UNITY_EDITOR
        // Только native preflight/bake. Runtime content не перечитывает Editor AssetDatabase.
        public IReadOnlyList<ContentEntry> EditorContent => Array.AsReadOnly(_content ?? Array.Empty<ContentEntry>());
#endif
    }

    public sealed class MapCatalogValidation
    {
        public bool Passed => Errors.Count == 0 && Description != null;
        public IReadOnlyList<string> Errors { get; }
        public MapRunResolverCatalog Description { get; }
        internal MapCatalogValidation(IEnumerable<string> errors, MapRunResolverCatalog description)
        { Errors = new List<string>(errors).AsReadOnly(); Description = description; }
    }
}
