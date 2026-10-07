using System;
using UnityEngine;

namespace VrBattlegrounds.PhysicalSpaceUtils
{
    /// <summary>Поза корня трекинга игрока в едином снимке PlayerCalibration (T-50 этап 4).</summary>
    [Serializable]
    public struct PlayerPlacement : IEquatable<PlayerPlacement>
    {
        public enum CoordinateSpace : byte { None, World, Anchors }

        public CoordinateSpace Space;
        public Vector3 Position;
        public Quaternion Rotation;
        public string CapturedOnMap;

        public bool HasValue => Space != CoordinateSpace.None;
        public bool IsAnchored => Space == CoordinateSpace.Anchors;
        public static PlayerPlacement None => default;

        public static PlayerPlacement World(Vector3 position, Quaternion rotation, string map) =>
            new PlayerPlacement { Space = CoordinateSpace.World, Position = position, Rotation = rotation, CapturedOnMap = map ?? string.Empty };

        public static PlayerPlacement Anchored(Vector3 position, Quaternion rotation, string map) =>
            new PlayerPlacement { Space = CoordinateSpace.Anchors, Position = position, Rotation = rotation, CapturedOnMap = map ?? string.Empty };

        /// <summary>Снять координаты корня; при отсутствии якорей мировой снимок пригоден только той же calibrated-карте.</summary>
        public static bool TryCapture(Vector3 position, Quaternion rotation, bool calibrated, string map,
                                      out PlayerPlacement placement, out string diagnosis)
        {
            PhysicalSpaceAnchorFrame frame = default;
            if (calibrated) PhysicalSpaceAnchorFrame.TryBuildFromScene(out frame, out _);
            return TryCapture(position, rotation, calibrated, map, frame, out placement, out diagnosis);
        }

        public static bool TryCapture(Vector3 position, Quaternion rotation, bool calibrated, string map,
                                      PhysicalSpaceAnchorFrame frame, out PlayerPlacement placement, out string diagnosis)
        {
            PlayerPlacement candidate = calibrated && frame.IsValid
                ? Anchored(frame.ToLocal(position), frame.ToLocal(rotation), map)
                : World(position, rotation, map);
            if (!TryNormalize(candidate, out placement))
            {
                diagnosis = "поза корня нечисловая или её поворот не определён";
                return false;
            }
            diagnosis = calibrated && !frame.IsValid ? "нет якорей: мировой снимок действует только на той же карте" : string.Empty;
            return true;
        }

        /// <summary>Разрешение чистое: не изменяет ни сессию, ни аватар.</summary>
        public bool TryResolve(bool calibrated, string currentMap, PhysicalSpaceAnchorFrame frame,
                               out Vector3 position, out Quaternion rotation, out string diagnosis)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (!TryNormalize(this, out PlayerPlacement valid) || !valid.HasValue)
            {
                diagnosis = "снимка места нет или он некорректен";
                return false;
            }
            if (valid.Space == CoordinateSpace.World)
            {
                if (calibrated && !string.Equals(valid.CapturedOnMap, currentMap, StringComparison.Ordinal))
                {
                    diagnosis = "мировой снимок откалиброванного игрока снят на другой карте";
                    return false;
                }
                position = valid.Position;
                rotation = valid.Rotation;
            }
            else
            {
                if (!calibrated || !frame.IsValid)
                {
                    diagnosis = !calibrated ? "игрок не калибровался по якорям" : "на карте нет действующей системы якорей";
                    return false;
                }
                position = frame.ToWorld(valid.Position);
                rotation = frame.ToWorld(valid.Rotation);
            }
            diagnosis = string.Empty;
            return true;
        }

        public static bool TryNormalize(PlayerPlacement requested, out PlayerPlacement normalized)
        {
            normalized = None;
            if (requested.Space == CoordinateSpace.None) return true;
            if (requested.Space != CoordinateSpace.World && requested.Space != CoordinateSpace.Anchors) return false;
            Vector3 p = requested.Position;
            Quaternion q = requested.Rotation;
            if (!Finite(p.x) || !Finite(p.y) || !Finite(p.z) ||
                !Finite(q.x) || !Finite(q.y) || !Finite(q.z) || !Finite(q.w)) return false;
            float norm = q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w;
            if (!Finite(norm) || norm < 0.000001f) return false;
            float inverse = 1f / Mathf.Sqrt(norm);
            requested.Rotation = new Quaternion(q.x * inverse, q.y * inverse, q.z * inverse, q.w * inverse);
            requested.CapturedOnMap = requested.CapturedOnMap ?? string.Empty;
            normalized = requested;
            return true;
        }

        public bool Equals(PlayerPlacement other) => Space == other.Space && Position.Equals(other.Position) &&
            Rotation.Equals(other.Rotation) && string.Equals(CapturedOnMap ?? string.Empty, other.CapturedOnMap ?? string.Empty, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is PlayerPlacement other && Equals(other);
        public override int GetHashCode()
        {
            unchecked { return (((int)Space * 397 ^ Position.GetHashCode()) * 397 ^ Rotation.GetHashCode()) * 397 ^ (CapturedOnMap ?? string.Empty).GetHashCode(); }
        }
        public static bool operator ==(PlayerPlacement a, PlayerPlacement b) => a.Equals(b);
        public static bool operator !=(PlayerPlacement a, PlayerPlacement b) => !a.Equals(b);
        public override string ToString() => HasValue ? $"{Space}: {Position}, карта '{CapturedOnMap}'" : "место не задано";
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
