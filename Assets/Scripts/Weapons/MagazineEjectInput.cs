using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Devices;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Кнопка выброса магазина: A на правом контроллере, X на левом (<see cref="UxrInputButtons.Button1" />
    /// у Quest). Работает кнопка той руки, что держит оружие: пистолет в правой — A, в левой — X.
    /// Оружие в обеих руках — любая из двух. Сама выемка — <see cref="MagazineEject" />.
    ///
    /// <para>
    /// На корне аватара, работает только у своего (<see cref="UxrAvatarMode.Local" />): у чужих
    /// аватаров нет контроллеров, а выпадение магазина они получают по сети. Режим проверяется
    /// каждый кадр — сетевой аватар становится локальным уже после спавна.
    /// </para>
    ///
    /// <para>
    /// Та же кнопка (<c>Button1</c>, отпускание) ставит точки калибровки в
    /// <c>PhysicalSpaceSyncManager</c> — но только в режиме калибровки, когда оружия в руках нет.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UxrAvatar))]
    public sealed class MagazineEjectInput : MonoBehaviour
    {
        [Tooltip("Кнопка выброса. Button1 — A на правом контроллере Quest, X на левом.")]
        [SerializeField] private UxrInputButtons _button = UxrInputButtons.Button1;

        private UxrAvatar _avatar;

        private void Awake()
        {
            _avatar = GetComponent<UxrAvatar>();
        }

        private void Update()
        {
            if (_avatar.AvatarMode != UxrAvatarMode.Local || _avatar.ControllerInput == null) return;

            foreach (UxrHandSide side in new[] { UxrHandSide.Left, UxrHandSide.Right })
            {
                if (_avatar.ControllerInput.GetButtonsPressDown(side, _button))
                {
                    TryEjectFromHand(side);
                }
            }
        }

        /// <summary>
        /// Выбрасывает магазин оружия в руке <paramref name="side" />. Публичный — тот же путь, что
        /// у кнопки, для проверки без контроллера.
        /// </summary>
        public bool TryEjectFromHand(UxrHandSide side)
        {
            UxrGrabber grabber = _avatar.GetGrabber(side);
            UxrGrabbableObject held = grabber != null ? grabber.GrabbedObject : null;

            if (!MagazineEject.TryEject(held, out UxrGrabbableObject magazine)) return false;

            GameLog.WeaponSystem.Info($"[MagazineEjectInput] {side}: магазин '{magazine.name}' выброшен из '{held.name}'", this);
            return true;
        }
    }
}
