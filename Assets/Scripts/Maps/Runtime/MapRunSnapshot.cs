using System;
using System.IO;
using Mirror;
using VrBattlegrounds.Managers;

namespace VrBattlegrounds.Maps.Runtime
{
    /// <summary>Closing — загрузка следующей карты принята: писатели закрыты, teardown ещё впереди (на выгрузке).</summary>
    public enum MapBootstrapStatus : byte { None, Preparing, CompositionReady, Ready, Failed, Retiring, Closing }

    /// <summary>Целый wire descriptor. Config immutable, поэтому подписчики не меняют authority через alias.</summary>
    public readonly struct MapRunSnapshot
    {
        public MapRunKey Key => Config != null ? Config.Key : default;
        public ulong Revision { get; }
        public MapBootstrapStatus Status { get; }
        public MapState MapState { get; }
        public ulong ModeEpoch { get; }
        public string ActiveModeId { get; }
        public uint ActiveModeNetId { get; }
        public uint RefereeNetId { get; }
        public uint CoordinatorNetId { get; }
        public MapRunConfig Config { get; }
        public string FailureCode { get; }
        public bool IsReady => Status == MapBootstrapStatus.Ready;

        internal MapRunSnapshot(MapRunConfig config, ulong revision, MapBootstrapStatus status,
            MapState mapState, ulong modeEpoch, string activeModeId, uint activeModeNetId,
            uint refereeNetId, uint coordinatorNetId, string failureCode)
        {
            Config = config;
            Revision = revision;
            Status = status;
            MapState = mapState;
            ModeEpoch = modeEpoch;
            ActiveModeId = activeModeId ?? string.Empty;
            ActiveModeNetId = activeModeNetId;
            RefereeNetId = refereeNetId;
            CoordinatorNetId = coordinatorNetId;
            FailureCode = failureCode ?? string.Empty;
        }
    }

    /// <summary>Явный Mirror serializer. Ограниченный полный payload, без asset refs и частичных SyncList.</summary>
    public static class MapRunSnapshotSerialization
    {
        private const byte WireVersion = 1;

        public static void WriteMapRunSnapshot(this NetworkWriter writer, MapRunSnapshot value)
        {
            writer.WriteByte(WireVersion);
            writer.WriteBool(value.Config != null);
            if (value.Config == null) return;
            MapRunConfig config = value.Config;
            writer.WriteGuid(config.Key.SessionEpoch);
            writer.WriteULong(config.Key.LoadSequence);
            writer.WriteString(config.MapScene);
            writer.WriteByte((byte)config.Kind);
            writer.WriteString(config.ContentFingerprint);
            writer.WriteUInt(config.CompositionVersion);
            writer.WriteString(config.WarmupModeId);
            writer.WriteString(config.MatchIntent.ModeId);
            writer.WriteString(config.MatchIntent.ResolutionReason);
            writer.WriteString(config.ArsenalPresetId);
            writer.WriteString(config.ArsenalFingerprint);
            writer.WriteInt(config.Stations.Count);
            foreach (MapStationConfig station in config.Stations)
            {
                writer.WriteString(station.StationKey);
                writer.WriteString(station.DecorationId);
                writer.WriteString(station.DecorationFallback);
                writer.WriteString(station.LayoutFingerprint);
                writer.WriteUInt(station.IdentitySchemaVersion);
            }
            writer.WriteULong(value.Revision);
            writer.WriteByte((byte)value.Status);
            writer.WriteInt((int)value.MapState);
            writer.WriteULong(value.ModeEpoch);
            writer.WriteString(value.ActiveModeId);
            writer.WriteUInt(value.ActiveModeNetId);
            writer.WriteUInt(value.RefereeNetId);
            writer.WriteUInt(value.CoordinatorNetId);
            writer.WriteString(value.FailureCode);
        }

        public static MapRunSnapshot ReadMapRunSnapshot(this NetworkReader reader)
        {
            if (reader.ReadByte() != WireVersion) throw new InvalidDataException("RunSnapshot.WireVersion.Unsupported");
            if (!reader.ReadBool()) return default;
            var key = new MapRunKey(reader.ReadGuid(), reader.ReadULong());
            string scene = Id(reader), fingerprint;
            var kind = (MapRunKind)reader.ReadByte();
            fingerprint = Id(reader);
            uint version = reader.ReadUInt();
            string warmup = Id(reader), match = Id(reader, true), reason = Reason(reader);
            string arsenal = Id(reader), arsenalHash = Id(reader);
            int count = reader.ReadInt();
            if (count < 0 || count > MapRunConfig.MaxStations) throw new InvalidDataException("Stations.CapacityExceeded");
            var stations = new MapStationConfig[count];
            var keys = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < count; i++)
            {
                string stationKey = Id(reader);
                if (!keys.Add(stationKey)) throw new InvalidDataException("Station.Key.Duplicate");
                stations[i] = new MapStationConfig(stationKey, Id(reader, true), Reason(reader), Id(reader), reader.ReadUInt());
                if (stations[i].IdentitySchemaVersion == 0) throw new InvalidDataException("Station.Manifest.Invalid");
            }
            var config = new MapRunConfig(key, scene, kind, fingerprint, version, warmup,
                new MapMatchIntent(match, reason), arsenal, arsenalHash, stations);
            ulong revision = reader.ReadULong();
            var status = (MapBootstrapStatus)reader.ReadByte();
            var state = (MapState)reader.ReadInt();
            ulong modeEpoch = reader.ReadULong();
            string activeMode = Id(reader, true);
            uint activeNetId = reader.ReadUInt(), refereeNetId = reader.ReadUInt(), coordinatorNetId = reader.ReadUInt();
            string failure = Reason(reader);
            if (!key.IsValid || version == 0 || revision == 0 || !Enum.IsDefined(typeof(MapRunKind), kind) ||
                !Enum.IsDefined(typeof(MapBootstrapStatus), status) || status == MapBootstrapStatus.None ||
                !Enum.IsDefined(typeof(MapState), state)) throw new InvalidDataException("RunSnapshot.Invalid");
            if (kind == MapRunKind.Combat && match.Length == 0) throw new InvalidDataException("Map.MatchMode.Missing");
            if (kind == MapRunKind.Lobby && match.Length != 0) throw new InvalidDataException("Map.Lobby.HasMatchModes");
            if (status == MapBootstrapStatus.Ready && (modeEpoch == 0 || activeNetId == 0 ||
                refereeNetId == 0 || coordinatorNetId == 0 || activeMode.Length == 0)) throw new InvalidDataException("RunSnapshot.Ready.Incomplete");
            return new MapRunSnapshot(config, revision, status, state, modeEpoch, activeMode, activeNetId,
                refereeNetId, coordinatorNetId, failure);
        }

        private static string Id(NetworkReader reader, bool optional = false)
        {
            string value = reader.ReadString();
            if (optional && value == string.Empty) return value;
            if (!MapRunResolver.Identifier(value)) throw new InvalidDataException("RunSnapshot.Identifier.Invalid");
            return value;
        }

        private static string Reason(NetworkReader reader)
        {
            string value = reader.ReadString();
            if (!MapRunResolver.Reason(value)) throw new InvalidDataException("RunSnapshot.Reason.Invalid");
            return value;
        }
    }
}
