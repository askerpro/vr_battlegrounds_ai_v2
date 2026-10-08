using System;
using UltimateXR.Haptics;
using UnityEngine;
using VrBattlegrounds.Weapons.Core;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Вибрации одного ствола по сигналам машины <see cref="WeaponHapticCue"/> (контракт haptics-api, ревизия 2):
    /// клипы SDK <see cref="UxrHapticClip"/> в данных ствола. Только данные; играет <see cref="WeaponFeedbackExecutor"/>
    /// через <see cref="WeaponHapticOutput"/>. Лежит рядом с <see cref="WeaponAudioSet"/>, а не внутри него: у вибрации
    /// и звука разные исполнители, у каждого канала — свои данные. Пишет только <c>WeaponSystemAuthoring</c>.
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

        /// <summary>Клип что-то играет: есть AudioClip-источник или стандартная форма.</summary>
        public static bool Has(UxrHapticClip clip) => clip != null && (clip.Clip != null || clip.FallbackClipType != UxrHapticClipType.None);
    }
}
