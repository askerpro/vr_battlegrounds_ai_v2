using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace VrBattlegrounds.Maps.Runtime
{
    /// <summary>Поколение принятой загрузки; повтор той же сцены получает новый ключ.</summary>
    public readonly struct MapRunKey : IEquatable<MapRunKey>
    {
        public Guid SessionEpoch { get; }
        public ulong LoadSequence { get; }
        public bool IsValid => SessionEpoch != Guid.Empty && LoadSequence != 0;

        public MapRunKey(Guid sessionEpoch, ulong loadSequence)
        {
            SessionEpoch = sessionEpoch;
            LoadSequence = loadSequence;
        }

        public bool Equals(MapRunKey other) => SessionEpoch == other.SessionEpoch && LoadSequence == other.LoadSequence;
        public override bool Equals(object obj) => obj is MapRunKey other && Equals(other);
        public override int GetHashCode() => unchecked(SessionEpoch.GetHashCode() * 397 ^ LoadSequence.GetHashCode());
        public static bool operator ==(MapRunKey left, MapRunKey right) => left.Equals(right);
        public static bool operator !=(MapRunKey left, MapRunKey right) => !left.Equals(right);
        public override string ToString() => $"{SessionEpoch:N}/{LoadSequence}";
    }

    public enum MapRunKind : byte { Lobby, Combat, Debug }

    /// <summary>Согласованное намерение серии; пустой ID явно означает NoMatch.</summary>
    public readonly struct MapMatchIntent
    {
        public string ModeId { get; }
        public string ResolutionReason { get; }
        public bool HasMatch => !string.IsNullOrEmpty(ModeId);

        internal MapMatchIntent(string modeId, string resolutionReason)
        {
            ModeId = modeId ?? string.Empty;
            ResolutionReason = resolutionReason ?? string.Empty;
        }
    }

    /// <summary>Frozen description станции. StationKey поставляет binding, не bootstrap.</summary>
    public readonly struct MapStationConfig
    {
        public string StationKey { get; }
        public string DecorationId { get; }
        public string DecorationFallback { get; }
        public string LayoutFingerprint { get; }
        public uint IdentitySchemaVersion { get; }

        public MapStationConfig(string stationKey, string decorationId, string decorationFallback,
            string layoutFingerprint, uint identitySchemaVersion)
        {
            StationKey = stationKey;
            DecorationId = decorationId ?? string.Empty;
            DecorationFallback = decorationFallback ?? string.Empty;
            LayoutFingerprint = layoutFingerprint;
            IdentitySchemaVersion = identitySchemaVersion;
        }
    }

    /// <summary>Неизменяемое разрешённое описание. Не содержит assets, счёта или выбора меню.</summary>
    public sealed class MapRunConfig
    {
        public const int MaxStations = 128;
        public const int MaxIdentifierLength = 128;
        public const int MaxReasonLength = 1024;

        public MapRunKey Key { get; }
        public string MapScene { get; }
        public MapRunKind Kind { get; }
        public string ContentFingerprint { get; }
        public uint CompositionVersion { get; }
        public string WarmupModeId { get; }
        public MapMatchIntent MatchIntent { get; }
        public string ArsenalPresetId { get; }
        public string ArsenalFingerprint { get; }
        public IReadOnlyList<MapStationConfig> Stations { get; }

        internal MapRunConfig(MapRunKey key, string mapScene, MapRunKind kind, string contentFingerprint,
            uint compositionVersion, string warmupModeId, MapMatchIntent matchIntent,
            string arsenalPresetId, string arsenalFingerprint, IReadOnlyList<MapStationConfig> stations)
        {
            Key = key;
            MapScene = mapScene;
            Kind = kind;
            ContentFingerprint = contentFingerprint;
            CompositionVersion = compositionVersion;
            WarmupModeId = warmupModeId;
            MatchIntent = matchIntent;
            ArsenalPresetId = arsenalPresetId;
            ArsenalFingerprint = arsenalFingerprint;
            var copy = new MapStationConfig[stations.Count];
            for (int i = 0; i < copy.Length; i++) copy[i] = stations[i];
            Stations = new ReadOnlyCollection<MapStationConfig>(copy);
        }
    }
}
