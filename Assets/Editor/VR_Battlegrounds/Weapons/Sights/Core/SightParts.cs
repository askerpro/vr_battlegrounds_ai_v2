using System;
using System.Linq;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Weapons.Sights;

namespace VrBattlegrounds.Editor.Weapons.Sights
{
    /// <summary>
    /// Поиск половин прицела и ShotSource и сборка входа решателя — общие для стенда комплекта и стенда родного
    /// прицела. Только чтение Transform, без записи.
    /// </summary>
    public static class SightParts
    {
        /// <summary>
        /// Ровно две SightAdjustablePart (Rear и Front) в <paramref name="scope"/>; подвижная — не больше одной
        /// (null — подвижной детали нет, например PPK). Подвижная деталь — с нулевым локальным поворотом.
        /// </summary>
        public static void Find(GameObject scope, out SightAdjustablePart rear, out SightAdjustablePart front, out SightAdjustablePart moving)
        {
            var parts = scope.GetComponentsInChildren<SightAdjustablePart>(true);
            if (parts.Length != 2 || parts.Count(p => p.Role == SightPartRole.Rear) != 1 || parts.Count(p => p.Role == SightPartRole.Front) != 1)
                throw new InvalidOperationException($"{scope.name}: нужно ровно две SightAdjustablePart (Rear и Front), найдено {parts.Length}");
            rear = parts.Single(p => p.Role == SightPartRole.Rear);
            front = parts.Single(p => p.Role == SightPartRole.Front);
            var adjustable = parts.Where(p => p.IsAdjustable).ToArray();
            if (adjustable.Length > 1) throw new InvalidOperationException($"{scope.name}: подвижных деталей {adjustable.Length}, допускается одна");
            moving = adjustable.Length == 1 ? adjustable[0] : null;
            if (moving != null && Quaternion.Angle(moving.transform.localRotation, Quaternion.identity) > .01f)
                throw new InvalidOperationException($"{moving.name}: ход только по локальной Y — поворот детали должен быть нулевым");
        }

        public static Transform FindShotSource(GameObject root)
        {
            var sources = root.GetComponentsInChildren<UxrProjectileSource>(true);
            if (sources.Length != 1) throw new InvalidOperationException($"{root.name}: UxrProjectileSource {sources.Length}, нужен 1");
            var shotTypes = sources[0].ShotTypes;
            if (shotTypes == null || shotTypes.Count == 0 || shotTypes[0] == null || shotTypes[0].ShotSource == null)
                throw new InvalidOperationException($"{root.name}: нет ShotSource у типа выстрела 0");
            return shotTypes[0].ShotSource;
        }

        /// <summary>Вход решателя по текущей позе. Без подвижной детали — режим только измерения.</summary>
        public static SightAutoZeroInput Input(SightAdjustablePart rear, SightAdjustablePart front, SightAdjustablePart moving,
                                               Transform muzzle, double zeroDistance)
        {
            return new SightAutoZeroInput
            {
                Rear = rear.transform.position, Front = front.transform.position,
                MovingRole = moving != null ? moving.Role : SightPartRole.Front,
                MeasureOnly = moving == null,
                MoveAxisPerUnit = moving != null ? moving.transform.parent.TransformVector(moving.transform.localRotation * Vector3.up) : Vector3.zero,
                RearUp = rear.transform.up, FrontUp = front.transform.up,
                MuzzlePosition = muzzle.position, MuzzleForward = muzzle.forward, MuzzleUp = muzzle.up,
                ZeroDistance = zeroDistance,
                CurrentOffset = moving != null ? moving.Offset : 0, TravelDown = moving != null ? moving.TravelDown : 0,
                TravelUp = moving != null ? moving.TravelUp : 0
            };
        }
    }
}
