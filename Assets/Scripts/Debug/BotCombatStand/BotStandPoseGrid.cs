using System;
using System.Collections.Generic;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;

namespace VrBattlegrounds.DevTools.BotCombatStand
{
    /// <summary>
    /// Набор мировых поз бота за кадр (этап pf-network): корень, камера, запястья (<c>GetHandBone</c>), оружие в правой
    /// руке (корень <see cref="UxrFirearmWeapon"/> и ShotSource первого типа выстрела); на сервере дополнительно запястья
    /// в начале PostUpdate UXR (до записи драйвера). Только чтение.
    /// </summary>
    public struct BotStandPoseSet
    {
        public bool Valid, HasWeapon, HasPreDriver;
        public Pose Root, Camera, WristL, WristR, Weapon, Shot, PreWristL, PreWristR;
        public string WeaponId;

        public static BotStandPoseSet Capture(UxrAvatar avatar)
        {
            var set = new BotStandPoseSet();
            if (avatar == null) return set;
            set.Valid = true;
            set.Root = Of(avatar.transform);
            set.Camera = Of(avatar.CameraTransform);
            set.WristL = Of(avatar.GetHandBone(UxrHandSide.Left));
            set.WristR = Of(avatar.GetHandBone(UxrHandSide.Right));
            UxrGrabber grabber = avatar.GetGrabber(UxrHandSide.Right);
            UxrGrabbableObject item = grabber != null ? grabber.GrabbedObject : null;
            UxrFirearmWeapon firearm = item != null ? item.GetComponentInParent<UxrFirearmWeapon>() : null;
            if (firearm == null && item != null) firearm = item.GetComponentInChildren<UxrFirearmWeapon>();
            UxrProjectileSource source = firearm != null ? firearm.GetComponent<UxrProjectileSource>() : null;
            if (firearm != null)
            {
                set.HasWeapon = true;
                set.Weapon = Of(firearm.transform);
                set.WeaponId = firearm.UniqueId.ToString();
                set.Shot = source != null && source.ShotTypes.Count > 0 && source.ShotTypes[0].ShotSource != null
                    ? Of(source.ShotTypes[0].ShotSource) : set.Weapon;
            }
            return set;
        }

        public static BotStandPoseSet Lerp(BotStandPoseSet a, BotStandPoseSet b, float s)
        {
            var set = b;
            set.Root = Mix(a.Root, b.Root, s);
            set.Camera = Mix(a.Camera, b.Camera, s);
            set.WristL = Mix(a.WristL, b.WristL, s);
            set.WristR = Mix(a.WristR, b.WristR, s);
            // Оружие — только если в обоих кадрах то же оружие; иначе берётся ближайший кадр.
            if (a.HasWeapon && b.HasWeapon && a.WeaponId == b.WeaponId)
            {
                set.Weapon = Mix(a.Weapon, b.Weapon, s);
                set.Shot = Mix(a.Shot, b.Shot, s);
            }
            else if (s < 0.5f) { set.HasWeapon = a.HasWeapon; set.Weapon = a.Weapon; set.Shot = a.Shot; set.WeaponId = a.WeaponId; }
            if (a.HasPreDriver && b.HasPreDriver)
            {
                set.PreWristL = Mix(a.PreWristL, b.PreWristL, s);
                set.PreWristR = Mix(a.PreWristR, b.PreWristR, s);
            }
            else set.HasPreDriver = false;
            return set;
        }

        public Dictionary<string, object> ToReport()
        {
            var report = new Dictionary<string, object>
            {
                ["root"] = BotStandNetPoses.Pose(Root.position, Root.rotation), ["camera"] = BotStandNetPoses.Pose(Camera.position, Camera.rotation),
                ["wristL"] = BotStandNetPoses.Pose(WristL.position, WristL.rotation), ["wristR"] = BotStandNetPoses.Pose(WristR.position, WristR.rotation)
            };
            if (HasWeapon)
            {
                report["weapon"] = BotStandNetPoses.Pose(Weapon.position, Weapon.rotation);
                report["shot"] = BotStandNetPoses.Pose(Shot.position, Shot.rotation);
                report["weaponId"] = WeaponId;
            }
            if (HasPreDriver)
            {
                report["preDriverWristL"] = BotStandNetPoses.Pose(PreWristL.position, PreWristL.rotation);
                report["preDriverWristR"] = BotStandNetPoses.Pose(PreWristR.position, PreWristR.rotation);
            }
            return report;
        }

        private static Pose Of(Transform transform) => transform != null ? new Pose(transform.position, transform.rotation) : Pose.identity;

        private static Pose Mix(Pose a, Pose b, float s) =>
            new Pose(Vector3.Lerp(a.position, b.position, s), Quaternion.Slerp(a.rotation, b.rotation, s));
    }

    /// <summary>
    /// Выборка поз на общей сетке серверного времени (шаг <see cref="Interval"/>): кадры подаются с их серверным
    /// временем (сервер — <c>NetworkTime.localTime</c>; клиент — <c>NetworkTime.time</c> минус сдвиг снимков NT, то есть
    /// время сервера, чью позу клиент сейчас показывает); при переходе узла сетки поза интерполируется точно на узел.
    /// Сервер и клиент дают записи с одним ключом <c>k</c> — сверка в analyze.py.
    /// </summary>
    public sealed class BotStandPoseGrid
    {
        public const double Interval = 0.25;
        private readonly int _limit;
        private BotStandPoseSet _previous;
        private double _previousTime;
        private bool _hasPrevious;

        public readonly List<object> Samples = new List<object>();

        public BotStandPoseGrid(int limit) => _limit = limit;

        public void Reset() => _hasPrevious = false;

        public void Feed(double time, BotStandPoseSet set, uint avatarNetId)
        {
            if (!set.Valid) return;
            if (_hasPrevious && time > _previousTime)
            {
                long from = (long)Math.Floor(_previousTime / Interval), to = (long)Math.Floor(time / Interval);
                // Один узел на кадр: длинный кадр (провал FPS) не размножает одинаковые записи.
                if (to > from && Samples.Count < _limit)
                {
                    double node = to * Interval;
                    float s = (float)((node - _previousTime) / (time - _previousTime));
                    Dictionary<string, object> report = BotStandPoseSet.Lerp(_previous, set, s).ToReport();
                    report["k"] = to;
                    report["t"] = node;
                    report["frameSpanMs"] = (time - _previousTime) * 1000.0;
                    report["avatarNetId"] = avatarNetId;
                    Samples.Add(report);
                }
            }
            _previous = set;
            _previousTime = time;
            _hasPrevious = true;
        }
    }
}
