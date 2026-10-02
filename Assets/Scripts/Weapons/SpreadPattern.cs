using System;
using UnityEngine;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Параметры точности ствола по Counter-Strike 2 — как в <c>items_game.txt</c>: единицы скрипта CS (тангенс
    /// отклонения × 1000, то есть миллирадианы). Логика — <see cref="WeaponAccuracy"/>, на префабе —
    /// <see cref="WeaponSpread"/>. С 2026-10-02 в игре применяется только spread дробовика;
    /// inaccuracy и recovery сохранены справочно для старых данных и неизменённых тестов.
    /// Источник чисел и дата сверки — <c>Docs/tasks/T-38-weapon-roster-and-balance.md</c>.
    /// </summary>
    [Serializable]
    public sealed class SpreadPattern
    {
        [Tooltip("CS2 spread: постоянный разброс ствола, мрад. У дробовика — конус дроби.")]
        [SerializeField, Min(0f)] private float _spread;

        [Tooltip("CS2 inaccuracy_stand: неточность стоя, мрад. Для снайперских — значение в прицеле (…_alt).")]
        [SerializeField, Min(0f)] private float _inaccuracyStand;

        [Tooltip("CS2 inaccuracy_move: неточность на бегу, мрад. В VR берётся малой долей по скорости головы.")]
        [SerializeField, Min(0f)] private float _inaccuracyMove;

        [Tooltip("CS2 inaccuracy_fire: прибавка неточности за выстрел, мрад.")]
        [SerializeField, Min(0f)] private float _inaccuracyFire;

        [Tooltip("CS2 recovery_time_stand: за столько секунд накопленная неточность спадает до 10 %.")]
        [SerializeField, Min(0.01f)] private float _recoveryTime = 0.35f;

        public SpreadPattern() { }

        public SpreadPattern(float spread, float inaccuracyStand, float inaccuracyMove, float inaccuracyFire, float recoveryTime)
        {
            _spread = spread;
            _inaccuracyStand = inaccuracyStand;
            _inaccuracyMove = inaccuracyMove;
            _inaccuracyFire = inaccuracyFire;
            _recoveryTime = recoveryTime;
        }

        public float Spread => _spread;
        public float InaccuracyStand => _inaccuracyStand;
        public float InaccuracyMove => _inaccuracyMove;
        public float InaccuracyFire => _inaccuracyFire;
        public float RecoveryTime => _recoveryTime;

        public bool SameAs(SpreadPattern other) =>
            other != null && Mathf.Approximately(_spread, other._spread) && Mathf.Approximately(_inaccuracyStand, other._inaccuracyStand) &&
            Mathf.Approximately(_inaccuracyMove, other._inaccuracyMove) && Mathf.Approximately(_inaccuracyFire, other._inaccuracyFire) &&
            Mathf.Approximately(_recoveryTime, other._recoveryTime);

        public override string ToString() =>
            $"spread {_spread}, stand {_inaccuracyStand}, move {_inaccuracyMove}, fire {_inaccuracyFire}, recovery {_recoveryTime}s";
    }
}
