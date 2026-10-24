using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Devices;
using UltimateXR.Locomotion;
using UnityEngine;
using UnityEngine.UI;
using VrBattlegrounds.Core;
using VrBattlegrounds.DevTools;

namespace VrBattlegrounds.UI.Menu.Kit
{
    /// <summary>
    /// Скролл содержимого планшета стиком (T-32): <b>пока меню открыто</b>, стик любой руки листает
    /// <see cref="ScrollRect"/> каркаса. Стик ведёт и телепорт (<see cref="UxrTeleportLocomotionBase"/>),
    /// поэтому на время открытого меню телепорт приглушается через его владельца
    /// (<see cref="DebugTeleportLocomotion.Suppress"/>) и отпускается при закрытии; включён ли телепорт
    /// вообще (только режим отладки), решает владелец.
    ///
    /// <para>
    /// Раньше стик работал только пока на планшет наведён луч — а по планшету водят пальцем, и
    /// «фокуса луча» при этом нет: стик молчал. Открытое меню — однозначный признак, что стик
    /// нужен интерфейсу. Скролл пальцем/лучом (перетаскивание) делает сам UltimateXR.
    /// Математика — <see cref="StickScrollMath"/> (под тестом).
    /// </para>
    /// </summary>
    public class MenuStickScroll : MonoBehaviour
    {
        [Tooltip("Скорость прокрутки при полном отклонении стика, px канваса в секунду.")]
        [SerializeField] private float _pixelsPerSecond = 1400f;

        [SerializeField] private ScrollRect _scroll;

        private bool _suppressing;

        public void Bind(ScrollRect scroll) => _scroll = scroll;

        private void OnDisable() => ReleaseTeleport();

        private void Update()
        {
            // Каркас активен только у открытого меню (контроллер выключает весь планшет при закрытии).
            if (!Application.isPlaying) return;

            SuppressTeleport();

            UxrControllerInput input = UxrAvatar.LocalAvatarInput;
            if (input == null || _scroll == null || _scroll.content == null) return;

            float stick = StickScrollMath.Combine(input.GetInput2D(UxrHandSide.Left, UxrInput2D.Joystick).y,
                                                  input.GetInput2D(UxrHandSide.Right, UxrInput2D.Joystick).y);

            float content = _scroll.content.rect.height;
            float viewport = _scroll.viewport != null ? _scroll.viewport.rect.height : ((RectTransform)_scroll.transform).rect.height;
            float next = StickScrollMath.Step(_scroll.verticalNormalizedPosition, stick, Time.unscaledDeltaTime,
                                              content, viewport, _pixelsPerSecond);
            if (!Mathf.Approximately(next, _scroll.verticalNormalizedPosition))
            {
                _scroll.velocity = Vector2.zero;
                _scroll.verticalNormalizedPosition = next;
            }
        }

        private void SuppressTeleport()
        {
            if (_suppressing) return;
            _suppressing = true;
            DebugTeleportLocomotion.Suppress(this);
            GameLog.UI.Verbose("[MenuStickScroll] Меню открыто: телепорт приглушён, стик листает планшет.", this);
        }

        private void ReleaseTeleport()
        {
            if (!_suppressing) return;
            _suppressing = false;
            DebugTeleportLocomotion.Release(this);
        }
    }
}
