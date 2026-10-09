using System;
using UltimateXR.Audio;
using UnityEngine;
using VrBattlegrounds.Weapons.Core;

namespace VrBattlegrounds.Weapons
{
    /// <summary>Откуда исполнитель берёт сигнал: оверрайд ствола, дефолт категории, запасной звук SDK или ниоткуда.</summary>
    public enum WeaponFeedbackSource { Override, Default, Sdk, None }

    /// <summary>
    /// Звуки механизма по сигналам машины <see cref="WeaponCue"/> (план п. 4.2). Только данные; играет
    /// <see cref="WeaponAudioExecutor"/>. Один тип для двух ролей: на стволе (хост <see cref="WeaponSystem"/>) — только
    /// оверрайды, в <see cref="WeaponFeedbackDefaults"/> — дефолты категории. Пустое поле ствола — дефолт (<see cref="Resolve"/>).
    /// Пишет только <c>WeaponSystemAuthoring</c>: в ствол переносит клипы прежних <c>AutomaticWeaponSlideFeedback</c>/
    /// <c>UxrShotgunPump</c> по смыслу события, а не по имени поля; дефолты в ствол не копирует.
    ///
    /// Вставки и выстрела здесь нет: у них другие владельцы (<c>AnchorSound</c> приёмника, SDK) — класс ошибок 2.
    /// <see cref="DryFire"/>, пустой и у ствола, и в дефолте, — звук «нет патронов» спуска SDK (<c>ShotAudioNoAmmo</c>).
    /// </summary>
    [Serializable]
    public sealed class WeaponAudioSet
    {
        [Tooltip("Action дошёл до порога извлечения (оттяжка затвора, помпа назад).")]
        [SerializeField] private UxrAudioSample _actionBack = new UxrAudioSample();

        [Tooltip("Action закрыт с досыланием патрона. Пусто — звук закрытия без подачи.")]
        [SerializeField] private UxrAudioSample _actionForwardChambered = new UxrAudioSample();

        [Tooltip("Action закрыт без подачи патрона: полный цикл на пустом магазине и частичный ход " +
                 "(сигнал ActionReturnPartial играет этот же звук — решение пользователя 2026-10-09).")]
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

        /// <summary>Клип сигнала только из этого набора (без дефолта категории); досылание без своего — звук закрытия.</summary>
        public UxrAudioSample For(WeaponCue cue) =>
            cue == WeaponCue.ActionForwardChambered && !Has(_actionForwardChambered) ? _actionForwardEmpty : Field(cue);

        /// <summary>Поле сигнала как есть, без подстановок.</summary>
        public UxrAudioSample Field(WeaponCue cue)
        {
            switch (cue)
            {
                case WeaponCue.ActionBack: return _actionBack;
                case WeaponCue.ActionForwardChambered: return _actionForwardChambered;
                case WeaponCue.ActionForwardEmpty: return _actionForwardEmpty;
                // Отдельное событие (будущий отклик «перезарядка не выполнена»), звук — как у возврата без подачи.
                case WeaponCue.ActionReturnPartial: return _actionForwardEmpty;
                case WeaponCue.ChamberEjected: return _chamberEjected;
                case WeaponCue.SlideLockCatch: return _slideLockCatch;
                case WeaponCue.DryFire: return _dryFire;
                case WeaponCue.Refusal: return _refusal;
                default: return null;
            }
        }

        /// <summary>
        /// Единственное правило подстановки (исполнитель и отчёт сборщика): сначала набор ствола целиком, затем дефолт
        /// категории. Внутри набора досылание без своего звука — звук закрытия без подачи (<see cref="For"/>): собственный
        /// звук закрытия ствола важнее звука досылания другой модели из дефолта. DryFire без звука в обоих наборах —
        /// <see cref="WeaponFeedbackSource.Sdk"/> (играет спуск SDK). null — сигнал молчит.
        /// </summary>
        public static UxrAudioSample Resolve(WeaponAudioSet overrides, WeaponAudioSet defaults, WeaponCue cue, out WeaponFeedbackSource source)
        {
            UxrAudioSample own = overrides?.For(cue);
            if (Has(own)) { source = WeaponFeedbackSource.Override; return own; }
            UxrAudioSample common = defaults?.For(cue);
            if (Has(common)) { source = WeaponFeedbackSource.Default; return common; }
            source = cue == WeaponCue.DryFire ? WeaponFeedbackSource.Sdk : WeaponFeedbackSource.None;
            return null;
        }

        public static bool Has(UxrAudioSample sample) => sample != null && sample.Clip != null;
    }
}
