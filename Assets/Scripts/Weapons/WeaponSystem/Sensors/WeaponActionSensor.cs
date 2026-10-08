using System;
using UltimateXR.Avatar;
using UltimateXR.Manipulation;
using VrBattlegrounds.Weapons.Core;

namespace VrBattlegrounds.Weapons.Sensors
{
    /// <summary>
    /// Датчик ручного хода Action (затвор, помпа). Только измеряет по <see cref="WeaponMechanismRig"/>: прогресс ручки,
    /// минимальный прогресс обязательных деталей (порог извлечения), покой, проверенную заднюю позу HoldOpen и хват.
    /// Хват/отпускание ручки — событиями <c>Grabbed/Released</c> (после SDK).
    /// </summary>
    internal sealed class WeaponActionSensor : IDisposable
    {
        private readonly WeaponMechanismRig _rig;
        private readonly UxrGrabbableObject _handle;

        public event Action HandleGrabbed;
        public event Action HandleReleased;

        public WeaponActionSensor(WeaponMechanismRig rig)
        {
            _rig = rig;
            _handle = rig != null && rig.HasAction ? rig.Handle : null;
            if (_handle == null) return;
            _handle.Grabbed += OnGrabbed;
            _handle.Released += OnReleased;
        }

        public bool HasAction => _handle != null;

        public void Dispose()
        {
            if (_handle == null) return;
            _handle.Grabbed -= OnGrabbed;
            _handle.Released -= OnReleased;
        }

        /// <param name="mainAvatar">Аватар, держащий основную рукоять в контексте автора (иначе null).</param>
        public ActionSample Sample(UxrAvatar mainAvatar)
        {
            if (_handle == null) return ActionSample.AtRest;
            bool held = _rig.IsHandleHeld;
            bool heldByMain = held && _rig.IsHandleHeldBy(mainAvatar);
            float progress = _rig.HandleProgress;
            float minimum = _rig.TryGetMinimumProgress(out float required) ? required : progress;
            return new ActionSample(held, heldByMain, progress, minimum, _rig.IsAtRest(), _rig.IsAtValidatedRear());
        }

        /// <summary>Рука на ручке (для вибрации зада Action): первая хватающая рука.</summary>
        public UxrGrabber HandleHand() =>
            _handle != null && UxrGrabManager.HasInstance && UxrGrabManager.Instance.GetGrabbingHand(_handle, 0, out UxrGrabber hand) ? hand : null;

        private void OnGrabbed(object sender, UxrManipulationEventArgs args) => HandleGrabbed?.Invoke();
        private void OnReleased(object sender, UxrManipulationEventArgs args) => HandleReleased?.Invoke();
    }
}
