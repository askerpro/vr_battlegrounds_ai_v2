using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.Maps
{
    /// <summary>
    /// Какой зоне спавна принадлежит объект, стоящий в ней, — одна точка ответа для всех (стены арсенала,
    /// табло лазерной сетки).
    ///
    /// <para>
    /// <b>Почему не <c>GetComponentInParent</c>.</b> Коробка зоны отмасштабирована неравномерно
    /// (на картах 15,5 × 7,4 × 3 м), поэтому стены арсенала стоят рядом с ней в общей группе
    /// (<c>SpawnZone_Terrorists</c> / <c>SpawnZone_CT</c> в <c>Environment.prefab</c>), а не внутри —
    /// иначе их перекосило бы. Поиск по родителям такую стену зоне не отдавал, и раздача стен (T-45)
    /// считала все стены карты ничьими. Здесь — сначала родитель (тестовые и будущие зоны, где стена
    /// дочерняя), иначе зона, в плане которой стоит объект; из вложенных — меньшая (зона лобби на всю
    /// арену накрывает командные).
    /// </para>
    /// </summary>
    public static class SpawnZoneMembership
    {
        /// <summary>Зона объекта среди всех зон сцены; null — объект вне зон.</summary>
        public static TeamSpawnZone ZoneOf(Transform obj)
        {
            if (obj == null) return null;
            TeamSpawnZone parent = obj.GetComponentInParent<TeamSpawnZone>(true);
            if (parent != null) return parent;

            return ZoneAt(obj.position, Object.FindObjectsByType<TeamSpawnZone>(FindObjectsInactive.Exclude));
        }

        /// <summary>Зона объекта среди данных зон (сцена-превью, тест).</summary>
        public static TeamSpawnZone ZoneOf(Transform obj, IEnumerable<TeamSpawnZone> zones)
        {
            if (obj == null) return null;
            TeamSpawnZone parent = obj.GetComponentInParent<TeamSpawnZone>(true);
            return parent != null ? parent : ZoneAt(obj.position, zones);
        }

        /// <summary>Самая маленькая в плане зона, накрывающая точку; null — ни одной.</summary>
        public static TeamSpawnZone ZoneAt(Vector3 worldPoint, IEnumerable<TeamSpawnZone> zones)
        {
            TeamSpawnZone best = null;
            float bestArea = float.PositiveInfinity;
            if (zones == null) return null;

            foreach (TeamSpawnZone zone in zones)
            {
                if (zone == null || !zone.ContainsInPlan(worldPoint)) continue;
                float area = zone.PlanArea;
                if (area >= bestArea) continue;
                best = zone;
                bestArea = area;
            }
            return best;
        }
    }
}
