using System;
using UltimateXR.Avatar;
using UltimateXR.Manipulation;
using VrBattlegrounds.Weapons.Core;

namespace VrBattlegrounds.Weapons.Sensors
{
    /// <summary>
    /// Датчик ручного хода Action (затвор, помпа). Только измеряет: прогресс ручки, минимальный прогресс
    /// обязательных деталей, покой, проверенную заднюю позу Empty и хват. Хват/отпускание ручки — событиями
    /// <c>Grabbed/Released</c> (после SDK). На этапе C покой, порог и задняя поза читаются тем же доказательством,
    /// что у <see cref="WeaponReadinessController"/>, — расхождение тени не может возникнуть из-за другой геометрии.
    /// </summary>
    internal sealed class WeaponActionSensor : IDisposable
    {
        private readonly WeaponReadinessController _controller;
        private readonly AutomaticWeaponSlideFeedback _feedback;
        private readonly UxrGrabbableObject _handle;

        public event Action HandleGrabbed;
        public event Action HandleReleased;

        public WeaponActionSensor(WeaponReadinessController controller, AutomaticWeaponSlideFeedback feedback)
        {
            _controller = controller;
            _feedback = feedback;
            _handle = feedback != null ? feedback.Slide : null;
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
            bool held = UxrGrabManager.HasInstance && UxrGrabManager.Instance.IsBeingGrabbed(_handle);
            bool heldByMain = held && mainAvatar != null && UxrGrabManager.Instance.IsBeingGrabbedBy(_handle, mainAvatar);
            float progress = _feedback.SignedSlideProgress;
            float minimum = _controller.TryGetRequiredActionProgress(out float required) ? required : progress;
            return new ActionSample(held, heldByMain, progress, minimum, _controller.IsActionAtRest, _controller.IsActionAtValidatedEmptyRear);
        }

        private void OnGrabbed(object sender, UxrManipulationEventArgs args) => HandleGrabbed?.Invoke();
        private void OnReleased(object sender, UxrManipulationEventArgs args) => HandleReleased?.Invoke();
    }
}
