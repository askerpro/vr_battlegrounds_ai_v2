using UltimateXR.Core;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Weapons.Core;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Единственный исполнитель вибрации и подсказки оружия (этап D, план п. 4.1). Клип сигнала берёт из
    /// <see cref="WeaponHapticSet"/> ствола и отправляет через
    /// <see cref="WeaponHapticOutput"/> (одна точка, заменяется на <c>HapticService.Play</c> одной правкой). Подсказка
    /// «дошли патрон» — подсветка Action: видна, пока машина держит защёлку (<see cref="Hint"/>), или когда рука
    /// рядом (аффорданс SDK, бывший <c>WeaponChamberingReminder</c>).
    ///
    /// <para>
    /// Вибрация зада Action (<see cref="WeaponHapticCue.ActionRear"/>) идёт в руку на ручке, остальные — в руку
    /// на основной рукояти. До этапа E вибрацию упёртого ствола даёт <see cref="BarrelObstruction"/>; здесь она не дублируется.
    /// </para>
    /// </summary>
    internal sealed class WeaponFeedbackExecutor
    {
        private readonly WeaponHapticSet _haptics;
        private readonly GameObject _visual;
        private readonly GameObject _proximity;
        private bool _hint;

        public WeaponFeedbackExecutor(WeaponHapticSet haptics, GameObject visual, GameObject proximity)
        {
            _haptics = haptics; _visual = visual; _proximity = proximity;
        }

        public bool HintOn => _hint;

        /// <summary>Сколько вибраций отправлено (для проб).</summary>
        public int HapticsSent { get; private set; }

        public void Haptic(WeaponHapticCue cue, UxrGrabber mainHand, UxrGrabber handleHand)
        {
            if (cue == WeaponHapticCue.Obstructed) return; // владелец — BarrelObstruction (до E)
            UxrGrabber hand = cue == WeaponHapticCue.ActionRear ? handleHand : mainHand;
            if (WeaponHapticOutput.Play(_haptics?.For(cue), hand)) HapticsSent++;
        }

        public void Hint(bool on)
        {
            _hint = on;
            Refresh();
        }

        /// <summary>Видимость подсветки раз в кадр: защёлка машины или рука рядом.</summary>
        public void Refresh()
        {
            bool near = UxrManager.HasInstance && UxrManager.Instance.isActiveAndEnabled &&
                        UxrGrabManager.HasInstance && UxrGrabManager.Instance.isActiveAndEnabled &&
                        (UxrGrabManager.Instance.Features & UxrManipulationFeatures.Affordances) != 0 &&
                        _proximity != null && _proximity.activeInHierarchy;
            SetVisible(near || _hint);
        }

        public void Reset()
        {
            _hint = false;
            SetVisible(false);
        }

        private void SetVisible(bool visible)
        {
            if (_visual != null && _visual.activeSelf != visible) _visual.SetActive(visible);
        }
    }
}
