using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace VrBattlegrounds.Maps.Runtime
{
    /// <summary>Запрос уже содержит captured намерение серии, никогда future Selection.</summary>
    public readonly struct MapRunRequest
    {
        public MapRunKey Key { get; }
        public string MapScene { get; }
        public string CapturedModeId { get; }
        public string ExpectedContentFingerprint { get; }

        public MapRunRequest(MapRunKey key, string mapScene, string capturedModeId, string expectedContentFingerprint)
        {
            Key = key;
            MapScene = mapScene;
            CapturedModeId = capturedModeId ?? string.Empty;
            ExpectedContentFingerprint = expectedContentFingerprint;
        }
    }

    /// <summary>Frozen catalog entry; native preflight позже проверяет фактические assets и identities.</summary>
    public sealed class MapRunMapDescription
    {
        public string Scene { get; }
        public MapRunKind Kind { get; }
        public string ContentFingerprint { get; }
        public uint CompositionVersion { get; }
        public string ArsenalPresetId { get; }
        public string ArsenalFingerprint { get; }
        public IReadOnlyList<string> SupportedMatchModes { get; }

        public MapRunMapDescription(string scene, MapRunKind kind, string fingerprint, uint compositionVersion,
            string arsenalPresetId, string arsenalFingerprint, IEnumerable<string> supportedMatchModes)
        {
            Scene = scene;
            Kind = kind;
            ContentFingerprint = fingerprint;
            CompositionVersion = compositionVersion;
            ArsenalPresetId = arsenalPresetId;
            ArsenalFingerprint = arsenalFingerprint;
            SupportedMatchModes = new List<string>(supportedMatchModes ?? Array.Empty<string>()).AsReadOnly();
        }
    }

    /// <summary>Data-only проекция catalog. Не service locator и не runtime asset resolver.</summary>
    public sealed class MapRunResolverCatalog
    {
        public string WarmupModeId { get; }
        public IReadOnlyList<string> MatchModeIds { get; }
        public IReadOnlyList<MapRunMapDescription> Maps { get; }

        public MapRunResolverCatalog(string warmupModeId, IEnumerable<string> matchModeIds,
            IEnumerable<MapRunMapDescription> maps)
        {
            WarmupModeId = warmupModeId;
            MatchModeIds = new List<string>(matchModeIds ?? Array.Empty<string>()).AsReadOnly();
            Maps = new List<MapRunMapDescription>(maps ?? Array.Empty<MapRunMapDescription>()).AsReadOnly();
        }
    }

    /// <summary>Защищённая проекция binding refs после native preflight, без собственного StationKey writer.</summary>
    public sealed class MapRunBindings
    {
        public string MapScene { get; }
        public IReadOnlyList<MapStationConfig> Stations { get; }

        public MapRunBindings(string mapScene, IEnumerable<MapStationConfig> stations)
        {
            MapScene = mapScene;
            Stations = new List<MapStationConfig>(stations ?? Array.Empty<MapStationConfig>()).AsReadOnly();
        }
    }

    public sealed class MapRunResolution
    {
        public MapRunConfig Config { get; }
        public IReadOnlyList<string> Errors { get; }
        public bool Passed => Config != null && Errors.Count == 0;

        internal MapRunResolution(MapRunConfig config, List<string> errors)
        {
            Config = config;
            Errors = new ReadOnlyCollection<string>(errors.ToArray());
        }
    }

    /// <summary>Чистый resolver. Unknown IDs/hash закрывают контракт; fallback только совместимости.</summary>
    public static class MapRunResolver
    {
        public static MapRunResolution Resolve(MapRunRequest request, MapRunResolverCatalog catalog, MapRunBindings bindings)
        {
            var errors = new List<string>();
            if (!request.Key.IsValid) errors.Add("RunKey.Invalid");
            if (!Identifier(request.MapScene)) errors.Add("Map.Scene.Invalid");
            if (catalog == null) errors.Add("Catalog.Missing");
            if (bindings == null) errors.Add("Map.Bindings.Missing");
            if (errors.Count != 0) return new MapRunResolution(null, errors);

            var modes = new HashSet<string>(StringComparer.Ordinal);
            if (!Identifier(catalog.WarmupModeId)) errors.Add("Catalog.Warmup.Invalid");
            foreach (string id in catalog.MatchModeIds)
            {
                if (!Identifier(id)) errors.Add("Catalog.Mode.Invalid");
                else if (id == catalog.WarmupModeId || !modes.Add(id)) errors.Add("Catalog.Mode.Duplicate:" + id);
            }
            var scenes = new HashSet<string>(StringComparer.Ordinal);
            MapRunMapDescription selected = null;
            foreach (MapRunMapDescription map in catalog.Maps)
            {
                if (map == null) { errors.Add("Catalog.Map.Null"); continue; }
                if (!Identifier(map.Scene)) errors.Add("Catalog.Map.Scene.Invalid");
                else if (!scenes.Add(map.Scene)) errors.Add("Catalog.Map.Duplicate:" + map.Scene);
                if (map.Scene == request.MapScene) selected = map;
                if (!Enum.IsDefined(typeof(MapRunKind), map.Kind)) errors.Add("Map.Kind.Invalid:" + map.Scene);
                if (!Identifier(map.ContentFingerprint)) errors.Add("Map.ContentFingerprint.Invalid:" + map.Scene);
                if (map.CompositionVersion == 0) errors.Add("Map.CompositionVersion.Invalid:" + map.Scene);
                if (!Identifier(map.ArsenalPresetId) || !Identifier(map.ArsenalFingerprint))
                    errors.Add("Map.Arsenal.Invalid:" + map.Scene);
                var supported = new HashSet<string>(StringComparer.Ordinal);
                foreach (string id in map.SupportedMatchModes)
                {
                    if (!modes.Contains(id)) errors.Add("Map.Mode.Unknown:" + map.Scene + "/" + id);
                    else if (!supported.Add(id)) errors.Add("Map.Mode.Duplicate:" + map.Scene + "/" + id);
                }
                if (map.Kind == MapRunKind.Combat && supported.Count == 0) errors.Add("Map.MatchMode.Missing:" + map.Scene);
                if (map.Kind == MapRunKind.Lobby && supported.Count != 0) errors.Add("Map.Lobby.HasMatchModes:" + map.Scene);
            }
            if (selected == null) errors.Add("Map.Unknown:" + request.MapScene);
            else if (selected.ContentFingerprint != request.ExpectedContentFingerprint) errors.Add("Map.ContentFingerprint.Mismatch");
            if (bindings.MapScene != request.MapScene) errors.Add("Map.Bindings.SceneMismatch");
            if (!string.IsNullOrEmpty(request.CapturedModeId) && !modes.Contains(request.CapturedModeId))
                errors.Add("Mode.Unknown:" + request.CapturedModeId);
            if (bindings.Stations.Count > MapRunConfig.MaxStations) errors.Add("Stations.CapacityExceeded");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (MapStationConfig station in bindings.Stations)
            {
                if (!Identifier(station.StationKey)) errors.Add("Station.Key.Invalid");
                else if (!keys.Add(station.StationKey)) errors.Add("Station.Key.Duplicate:" + station.StationKey);
                if (!Identifier(station.LayoutFingerprint) || station.IdentitySchemaVersion == 0)
                    errors.Add("Station.Manifest.Invalid:" + station.StationKey);
                if ((!string.IsNullOrEmpty(station.DecorationId) && !Identifier(station.DecorationId)) ||
                    !Reason(station.DecorationFallback)) errors.Add("Station.Decoration.Invalid:" + station.StationKey);
            }
            if (errors.Count != 0) return new MapRunResolution(null, errors);

            string resolved = string.Empty, reason = "NoMatch";
            if (selected.Kind != MapRunKind.Lobby && selected.SupportedMatchModes.Count > 0)
            {
                foreach (string id in selected.SupportedMatchModes)
                    if (id == request.CapturedModeId) resolved = id;
                if (resolved.Length == 0)
                {
                    resolved = selected.SupportedMatchModes[0];
                    reason = request.CapturedModeId.Length == 0 ? "DirectDefault" : "IncompatibleCapturedMode:" + request.CapturedModeId;
                }
                else reason = "CapturedMode";
            }
            return new MapRunResolution(new MapRunConfig(request.Key, selected.Scene, selected.Kind,
                selected.ContentFingerprint, selected.CompositionVersion, catalog.WarmupModeId,
                new MapMatchIntent(resolved, reason), selected.ArsenalPresetId, selected.ArsenalFingerprint,
                bindings.Stations), errors);
        }

        internal static bool Identifier(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= MapRunConfig.MaxIdentifierLength;
        internal static bool Reason(string value) => value != null && value.Length <= MapRunConfig.MaxReasonLength;
    }
}
