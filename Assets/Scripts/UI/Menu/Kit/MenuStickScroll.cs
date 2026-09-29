using System.Collections.Generic;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Devices;
using UltimateXR.Locomotion;
using UnityEngine;
using UnityEngine.UI;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.UI.Menu.Kit
{
    /// <summary>
    /// Скролл содержимого планшета стиком (T-32): <b>пока меню открыто</b>, стик любой руки листает
    /// <see cref="ScrollRect"/> каркаса. Стик ведёт и телепорт (<see cref="UxrTeleportLocomotionBase"/>),
    /// поэтому на время открытого меню телепорт выключается и возвращается при закрытии. Включаются
    /// только те компоненты, которые выключил этот скрипт.
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

        private readonly List<UxrTeleportLocomotionBase> _suppressed = new List<UxrTeleportLocomotionBase>();
        private UxrAvatar _suppressedAvatar;

        public void Bind(ScrollRect scroll) => _scroll = scroll;

        private void OnDisable() => RestoreTeleport();

        private void Update()
        {
            // Каркас активен только у открытого меню (контроллер выключает весь планшет при закрытии).
            if (!Application.isPlaying) return;

            UxrAvatar avatar = UxrAvatar.LocalAvatar;
            if (avatar != _suppressedAvatar)
            {
                RestoreTeleport();
                SuppressTeleport(avatar);
            }

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

        private void SuppressTeleport(UxrAvatar avatar)
        {
            _suppressedAvatar = avatar;
            if (avatar == null) return;

            foreach (UxrTeleportLocomotionBase teleport in avatar.GetComponentsInChildren<UxrTeleportLocomotionBase>())
            {
                if (!teleport.enabled) continue;
                teleport.enabled = false;
                _suppressed.Add(teleport);
            }
            if (_suppressed.Count > 0)
                GameLog.UI.Verbose($"[MenuStickScroll] Меню открыто: телепорт выключен ({_suppressed.Count}), стик листает планшет.", this);
        }

        private void RestoreTeleport()
        {
            foreach (UxrTeleportLocomotionBase teleport in _suppressed)
                if (teleport != null) teleport.enabled = true;
            _suppressed.Clear();
            _suppressedAvatar = null;
        }
    }
}
