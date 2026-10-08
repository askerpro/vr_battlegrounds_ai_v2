using System;
using UltimateXR.Audio;
using UnityEngine;
using VrBattlegrounds.Weapons.Core;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Звуки механизма одного ствола по сигналам машины <see cref="WeaponCue"/> (план п. 4.2). Только данные; играет
    /// <see cref="WeaponAudioExecutor"/>. Пишет только <c>WeaponSystemAuthoring</c>: переносит клипы из прежних
    /// <c>AutomaticWeaponSlideFeedback</c>/<c>UxrShotgunPump</c> по смыслу события, а не по имени поля.
    ///
    /// Вставки и выстрела здесь нет: у них другие владельцы (<c>AnchorSound</c> приёмника, SDK) — класс ошибок 2.
    /// Пустой <see cref="DryFire"/> — звук «нет патронов» спуска SDK (<c>ShotAudioNoAmmo</c>).
    /// </summary>
    [Serializable]
    public sealed class WeaponAudioSet
    {
        [Tooltip("Action дошёл до порога извлечения (оттяжка затвора, помпа назад).")]
        [SerializeField] private UxrAudioSample _actionBack = new UxrAudioSample();

        [Tooltip("Action закрыт с досыланием патрона. Пусто — звук закрытия без подачи.")]
        [SerializeField] private UxrAudioSample _actionForwardChambered = new UxrAudioSample();

        [Tooltip("Action закрыт без подачи патрона (частичный ход, пустой магазин).")]
        [SerializeField] private UxrAudioSample _actionForwardEmpty = new UxrAudioSample();

        [Tooltip("Из патронника извлечён живой патрон.")]
        [SerializeField] private UxrAudioSample _chamberEjected = new UxrAudioSample();

        [Tooltip("Затвор встал на задержку после последнего патрона (HoldOpen).")]
        [SerializeField] private UxrAudioSample _slideLockCatch = new UxrAudioSample();

        [Tooltip("Сухой щелчок отказа спуска. Пусто — звук «нет патронов» спуска SDK.")]
        [SerializeField] private UxrAudioSample _dryFire = new UxrAudioSample();

        [Tooltip("Отказ до конца паузы между выстрелами (S2): искусственный звук, не щелчок.")]
        [SerializeField] private UxrAudioSample _refusal = new UxrAudioSample();

        public UxrAudioSample ActionBack => _actionBack;
        public UxrAudioSample ActionForwardChambered => _actionForwardChambered;
        public UxrAudioSample ActionForwardEmpty => _actionForwardEmpty;
        public UxrAudioSample ChamberEjected => _chamberEjected;
        public UxrAudioSample SlideLockCatch => _slideLockCatch;
        public UxrAudioSample DryFire => _dryFire;
        public UxrAudioSample Refusal => _refusal;

        /// <summary>Клип сигнала (null — у ствола такого звука нет; для DryFire — звук SDK).</summary>
        public UxrAudioSample For(WeaponCue cue)
        {
            switch (cue)
            {
                case WeaponCue.ActionBack: return _actionBack;
                case WeaponCue.ActionForwardChambered: return Has(_actionForwardChambered) ? _actionForwardChambered : _actionForwardEmpty;
                case WeaponCue.ActionForwardEmpty: return _actionForwardEmpty;
                case WeaponCue.ChamberEjected: return _chamberEjected;
                case WeaponCue.SlideLockCatch: return _slideLockCatch;
                case WeaponCue.DryFire: return _dryFire;
                case WeaponCue.Refusal: return _refusal;
                default: return null;
            }
        }

        public static bool Has(UxrAudioSample sample) => sample != null && sample.Clip != null;
    }
}
