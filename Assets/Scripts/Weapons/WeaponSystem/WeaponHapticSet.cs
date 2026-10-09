using System;
using UltimateXR.Haptics;
using UnityEngine;
using VrBattlegrounds.Weapons.Core;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Вибрации по сигналам машины <see cref="WeaponHapticCue"/> (контракт haptics-api, ревизия 2): клипы SDK
    /// <see cref="UxrHapticClip"/>. Только данные; играет <see cref="WeaponFeedbackExecutor"/> через <see cref="WeaponHapticOutput"/>.
    /// Лежит рядом с <see cref="WeaponAudioSet"/>, а не внутри него: у вибрации и звука разные исполнители, у каждого канала —
    /// свои данные. На стволе — только оверрайды, в <see cref="WeaponFeedbackDefaults"/> — дефолты категории; пустой клип
    /// ствола — дефолт (<see cref="Resolve"/>). Пишет только <c>WeaponSystemAuthoring</c>, дефолты в ствол не копирует.
    ///
    /// <para>
    /// Приоритеты по контракту — отказы <c>High</c>, ход механизма <c>Normal</c>. Задаются формой и параметрами клипа
    /// после SDK-патча 66 (haptics-system); до него — стандартные <see cref="UxrHapticClipType"/>. Клип без формы
    /// (<c>None</c> и без <c>AudioClip</c>) — тишина. Упёртого ствола здесь нет: до этапа E его вибрацию даёт
    /// <c>BarrelObstruction</c>. Отдачи нет: её пока даёт SDK.
    /// </para>
    /// </summary>
    [Serializable]
    public sealed class WeaponHapticSet
    {
        [Tooltip("Ручка прошла порог извлечения (ход механизма, приоритет Normal). Рука на ручке.")]
        [SerializeField] private UxrHapticClip _actionRear = new UxrHapticClip();

        [Tooltip("Спуск при неготовом оружии (отказ, High, двойной импульс). Рука на спуске.")]
        [SerializeField] private UxrHapticClip _notReady = new UxrHapticClip();

        [Tooltip("Спуск при сбое учёта (отказ, High, тройной импульс). Рука на спуске.")]
        [SerializeField] private UxrHapticClip _faulted = new UxrHapticClip();

        [Tooltip("Спуск до конца паузы между выстрелами (отказ S2, High). Рука на спуске.")]
        [SerializeField] private UxrHapticClip _rateOfFire = new UxrHapticClip();

        public UxrHapticClip ActionRear => _actionRear;
        public UxrHapticClip NotReady => _notReady;
        public UxrHapticClip Faulted => _faulted;
        public UxrHapticClip RateOfFire => _rateOfFire;

        /// <summary>Клип сигнала; null — у сигнала нет клипа в этом наборе (упёртый ствол).</summary>
        public UxrHapticClip For(WeaponHapticCue cue)
        {
            switch (cue)
            {
                case WeaponHapticCue.ActionRear: return _actionRear;
                case WeaponHapticCue.NotReady: return _notReady;
                case WeaponHapticCue.Faulted: return _faulted;
                case WeaponHapticCue.RateOfFire: return _rateOfFire;
                default: return null;
            }
        }

        /// <summary>
        /// Единственное правило подстановки (исполнитель и отчёт сборщика): клип ствола, иначе клип дефолта категории.
        /// null — сигнал без вибрации (упёртый ствол — у <c>BarrelObstruction</c> до этапа E).
        /// </summary>
        public static UxrHapticClip Resolve(WeaponHapticSet overrides, WeaponHapticSet defaults, WeaponHapticCue cue, out WeaponFeedbackSource source)
        {
            UxrHapticClip own = overrides?.For(cue);
            if (Has(own)) { source = WeaponFeedbackSource.Override; return own; }
            UxrHapticClip common = defaults?.For(cue);
            if (Has(common)) { source = WeaponFeedbackSource.Default; return common; }
            source = WeaponFeedbackSource.None;
            return null;
        }

        /// <summary>Клип что-то играет: есть AudioClip-источник или стандартная форма.</summary>
        public static bool Has(UxrHapticClip clip) => clip != null && (clip.Clip != null || clip.FallbackClipType != UxrHapticClipType.None);
    }
}
