using System.Collections.Generic;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.DevTools
{
    /// <summary>Точка телепорта режима отладки: где встать на полу и куда смотреть.</summary>
    public readonly struct DebugTeleportTarget
    {
        public readonly string Id;
        public readonly string Label;
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;

        public DebugTeleportTarget(string id, string label, Vector3 position, Quaternion rotation)
        {
            Id = id;
            Label = label;
            Position = position;
            Rotation = rotation;
        }
    }

    /// <summary>
    /// Точки телепорта режима отладки на загруженной карте: зоны спавна команд
    /// (<see cref="TeamSpawnZone"/>), места перед стенами арсенала (<see cref="ArsenalWallController"/>) и
    /// точки, поставленные на карту (<see cref="DebugTeleportPoint"/>, стенды <c>TestMap3</c>).
    ///
    /// <para>
    /// Список строят обе стороны по одной сцене и одному правилу: клиент — для кнопок, сервер —
    /// чтобы по <see cref="DebugTeleportTarget.Id"/> найти точку сам, не доверяя координатам
    /// клиента. Поэтому порядок задаётся местом объекта (<see cref="ComparePlace"/>), а не
    /// <c>InstanceID</c> — он у сервера и клиента разный.
    /// </para>
    /// </summary>
    public static class DebugTeleportTargets
    {
        /// <summary>На сколько метров перед стеной арсенала встать: полки выступают на ~0,7 м.</summary>
        public const float ArsenalStandDistance = 1.2f;

        public const string ZonePrefix = "zone:";
        public const string ArsenalPrefix = "arsenal:";
        public const string PointPrefix = "point:";

        public static List<DebugTeleportTarget> Collect()
        {
            var result = new List<DebugTeleportTarget>();

            var zones = new List<TeamSpawnZone>(Object.FindObjectsByType<TeamSpawnZone>());
            zones.Sort((a, b) => ComparePlace(a.transform.position, b.transform.position));
            for (int i = 0; i < zones.Count; i++)
            {
                Transform t = zones[i].transform;
                string team = zones[i].Team != null ? zones[i].Team.Name : "без команды";
                result.Add(new DebugTeleportTarget(ZonePrefix + i, "Зона: " + team, t.position, t.rotation));
            }

            var walls = new List<ArsenalWallController>(Object.FindObjectsByType<ArsenalWallController>());
            walls.Sort((a, b) => ComparePlace(a.transform.position, b.transform.position));
            for (int i = 0; i < walls.Count; i++)
            {
                Transform t = walls[i].transform;
                ArsenalStandPoint(t.position, t.forward, out Vector3 position, out Quaternion rotation);
                result.Add(new DebugTeleportTarget(ArsenalPrefix + i, "Арсенал " + (i + 1), position, rotation));
            }

            var points = new List<DebugTeleportPoint>(Object.FindObjectsByType<DebugTeleportPoint>());
            points.Sort((a, b) => ComparePlace(a.transform.position, b.transform.position));
            for (int i = 0; i < points.Count; i++)
            {
                Transform t = points[i].transform;
                result.Add(new DebugTeleportTarget(PointPrefix + i, points[i].Label, t.position, t.rotation));
            }

            return result;
        }

        public static bool TryResolve(string id, out DebugTeleportTarget target)
        {
            foreach (DebugTeleportTarget candidate in Collect())
            {
                if (candidate.Id == id)
                {
                    target = candidate;
                    return true;
                }
            }

            target = default;
            return false;
        }

        /// <summary>
        /// Место перед стеной арсенала: полки смотрят по <c>forward</c> стены (проверено по
        /// слотам в Lobby), игрок встаёт на <see cref="ArsenalStandDistance"/> перед ней и
        /// смотрит на неё. Высота — пол стены.
        /// </summary>
        public static void ArsenalStandPoint(Vector3 wallPosition, Vector3 wallForward, out Vector3 position, out Quaternion rotation)
        {
            Vector3 flat = new Vector3(wallForward.x, 0f, wallForward.z);
            if (flat.sqrMagnitude < 1e-6f) flat = Vector3.forward;
            flat.Normalize();

            position = wallPosition + flat * ArsenalStandDistance;
            rotation = Quaternion.LookRotation(-flat, Vector3.up);
        }

        /// <summary>
        /// Порядок точек, одинаковый на всех машинах: по X, затем по Z, затем по Y, с точностью
        /// до сантиметра (сериализованные позиции совпадают, мелкая разница float — нет).
        /// </summary>
        public static int ComparePlace(Vector3 a, Vector3 b)
        {
            int c = Cm(a.x).CompareTo(Cm(b.x));
            if (c != 0) return c;
            c = Cm(a.z).CompareTo(Cm(b.z));
            return c != 0 ? c : Cm(a.y).CompareTo(Cm(b.y));
        }

        private static long Cm(float metres) => (long)Mathf.Round(metres * 100f);
    }
}
