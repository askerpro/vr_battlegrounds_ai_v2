using System;
using UnityEngine;
using VrBattlegrounds.Weapons.Core;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Что и как вылетает из окна выброса по одному сигналу: префаб (только визуал: меш, коллайдер, <see cref="Rigidbody"/>,
    /// <see cref="WeaponEjecta"/>), скорость и вращение в системе окна (<see cref="WeaponEjectionPort"/>: +X — наружу из окна,
    /// +Y — вверх, +Z — к дулу). Пустой префаб — запись пустая (у ствола: берётся дефолт категории).
    /// </summary>
    [Serializable]
    public sealed class WeaponEjectile
    {
        [Tooltip("Префаб вылета: корень с Rigidbody, коллайдером и WeaponEjecta. Ось +Z корня — к носику патрона/гильзы.")]
        [SerializeField] private GameObject _prefab;

        [Tooltip("Скорость в системе окна, м/с: +X — наружу, +Y — вверх, +Z — к дулу.")]
        [SerializeField] private Vector3 _velocity = new Vector3(1.8f, 1.2f, -0.3f);

        [Tooltip("Разброс скорости, доля (0,2 — ±20 % по каждой оси).")]
        [Range(0f, 1f)]
        [SerializeField] private float _velocityJitter = 0.2f;

        [Tooltip("Угловая скорость в системе окна, рад/с (кувырок гильзы).")]
        [SerializeField] private Vector3 _spin = new Vector3(0f, 12f, 20f);

        [Tooltip("Сколько секунд лежит до исчезновения.")]
        [SerializeField] private float _lifetime = 10f;

        public GameObject Prefab => _prefab;
        public Vector3 Velocity => _velocity;
        public float VelocityJitter => _velocityJitter;
        public Vector3 Spin => _spin;
        public float Lifetime => _lifetime;

        public static bool Has(WeaponEjectile ejectile) => ejectile != null && ejectile.Prefab != null;
    }

    /// <summary>
    /// Вылет по сигналам машины (этап ejection): живой патрон при извлечении (<see cref="WeaponCue.ChamberEjected"/>) и
    /// стреляная гильза самозарядного ствола при выстреле (<see cref="WeaponCue.CasingEjected"/>). Только данные; спавнит
    /// <see cref="WeaponEjectionExecutor"/>. Как <see cref="WeaponAudioSet"/>: один тип для двух ролей — на стволе только
    /// оверрайды, в <see cref="WeaponFeedbackDefaults"/> — дефолты категории; подстановка — <see cref="Resolve"/> в момент вылета.
    /// </summary>
    [Serializable]
    public sealed class WeaponEjectionSet
    {
        [Tooltip("Живой патрон, извлечённый из патронника ручным ходом Action.")]
        [SerializeField] private WeaponEjectile _liveRound = new WeaponEjectile();

        [Tooltip("Стреляная гильза, выброшенная выстрелом самозарядного ствола.")]
        [SerializeField] private WeaponEjectile _spentCasing = new WeaponEjectile();

        public WeaponEjectile LiveRound => _liveRound;
        public WeaponEjectile SpentCasing => _spentCasing;

        /// <summary>Запись сигнала только из этого набора; null — сигнал не про вылет.</summary>
        public WeaponEjectile For(WeaponCue cue)
        {
            switch (cue)
            {
                case WeaponCue.ChamberEjected: return _liveRound;
                case WeaponCue.CasingEjected: return _spentCasing;
                default: return null;
            }
        }

        /// <summary>Сигналы машины, на которые есть вылет.</summary>
        public static bool IsEjectionCue(WeaponCue cue) => cue == WeaponCue.ChamberEjected || cue == WeaponCue.CasingEjected;

        /// <summary>
        /// Единственное правило подстановки (исполнитель и отчёт сборщика): запись ствола целиком, если в ней есть префаб,
        /// иначе запись категории. null — вылета нет (<see cref="WeaponFeedbackSource.None"/>).
        /// </summary>
        public static WeaponEjectile Resolve(WeaponEjectionSet overrides, WeaponEjectionSet defaults, WeaponCue cue, out WeaponFeedbackSource source)
        {
            WeaponEjectile own = overrides?.For(cue);
            if (WeaponEjectile.Has(own)) { source = WeaponFeedbackSource.Override; return own; }
            WeaponEjectile common = defaults?.For(cue);
            if (WeaponEjectile.Has(common)) { source = WeaponFeedbackSource.Default; return common; }
            source = WeaponFeedbackSource.None;
            return null;
        }
    }

    /// <summary>
    /// Окно выброса ствола — геометрия, поэтому только на стволе (у категории его нет). Точка и поворот заданы в системе
    /// <see cref="Anchor"/> (деталь, с которой окно движется: корпус, затвор). Система окна: +X — наружу из окна, +Y — вверх,
    /// +Z — к дулу. Пишет <c>WeaponSystemAuthoring</c>.
    /// </summary>
    [Serializable]
    public sealed class WeaponEjectionPort
    {
        [Tooltip("Деталь, на которой окно выброса (корпус ствольной коробки, затвор).")]
        [SerializeField] private Transform _anchor;

        [Tooltip("Точка окна в системе Anchor.")]
        [SerializeField] private Vector3 _localPosition;

        [Tooltip("Поворот окна в системе Anchor: +X — наружу из окна, +Y — вверх, +Z — к дулу.")]
        [SerializeField] private Quaternion _localRotation = Quaternion.identity;

        public Transform Anchor => _anchor;
        public Vector3 LocalPosition => _localPosition;
        public Quaternion LocalRotation => _localRotation;

        public bool IsValid => _anchor != null;

        /// <summary>Мировая поза окна (false — окна нет).</summary>
        public bool TryGetWorldPose(out Vector3 position, out Quaternion rotation)
        {
            if (_anchor == null) { position = default; rotation = Quaternion.identity; return false; }
            position = _anchor.TransformPoint(_localPosition);
            rotation = _anchor.rotation * _localRotation;
            return true;
        }
    }
}
