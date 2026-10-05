using UltimateXR.Avatar;
using UltimateXR.Avatar.Controllers;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Core;
using UltimateXR.Animation.IK;

namespace VrBattlegrounds.Player.Avatars
{
    /// <summary>
    /// Стойка ног аватара по оружию в руках — набор клипов ходьбы <see cref="UxrStandardAvatarController.LegStance"/>
    /// (ноги из клипов, патч UltimateXR 37): 0 без оружия, 1 пистолет, 2 винтовка; смену сглаживает UltimateXR (~0,25 с).
    /// Единственная игровая часть ног: остальное (решатель, копия рига, шаги) — в UltimateXR, настройки — раздел «Ноги»
    /// контроллера.
    ///
    /// <para>
    /// Источник — захваты UltimateXR (<see cref="UxrGrabManager.GetObjectBeingGrabbed"/>) обеих рук и категория оружия из
    /// данных арсенала (<see cref="WeaponComponent.WeaponData"/> → <see cref="WeaponCategory"/>). Захваты реплицируются
    /// UltimateXR, поэтому у чужого аватара стойка выходит та же без своей синхронизации. Опрос каждый кадр: два
    /// словарных запроса менеджера и <c>GetComponentInParent</c> только при смене захваченного объекта.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UxrStandardAvatarController))]
    public class AvatarStanceFromGrabs : MonoBehaviour
    {
        public const float Unarmed = 0f, Pistol = 1f, Rifle = 2f;

        [Tooltip("Ставить стойку по оружию в руках. Выкл — стойку задаёт кто-то другой (стенд, отладка).")]
        public bool followGrabs = true;

        private UxrAvatar _avatar;
        private UxrStandardAvatarController _controller;
        private UxrGrabbableObject _left, _right;
        private WeaponCategory? _leftCategory, _rightCategory;

        private void Awake()
        {
            _avatar = GetComponent<UxrAvatar>();
            _controller = GetComponent<UxrStandardAvatarController>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void BindLegsDiagnostics()
        {
            UxrLegsDiagnostics.WarningSink = (message, context) => GameLog.Player.Warning("[UxrLegs] " + message, context);
        }

        private void Update()
        {
            if (!followGrabs || _avatar == null || _controller == null || !UxrGrabManager.HasInstance) return;

            Refresh(UxrHandSide.Left, ref _left, ref _leftCategory);
            Refresh(UxrHandSide.Right, ref _right, ref _rightCategory);
            _controller.LegStance = StanceFor(_leftCategory, _rightCategory);
        }

        private void Refresh(UxrHandSide hand, ref UxrGrabbableObject cached, ref WeaponCategory? category)
        {
            UxrGrabManager.Instance.GetObjectBeingGrabbed(_avatar, hand, out UxrGrabbableObject grabbed);
            if (grabbed == cached) return;

            cached = grabbed;
            WeaponComponent weapon = grabbed != null ? grabbed.GetComponentInParent<WeaponComponent>() : null;
            category = weapon != null && weapon.WeaponData != null ? weapon.WeaponData.Category : (WeaponCategory?)null;
        }

        /// <summary>Стойка по категориям оружия в руках: винтовка важнее пистолета; снаряжение и нож — без оружия.</summary>
        public static float StanceFor(WeaponCategory? left, WeaponCategory? right)
        {
            if (left == WeaponCategory.Rifle || right == WeaponCategory.Rifle) return Rifle;
            if (left == WeaponCategory.Pistol || right == WeaponCategory.Pistol) return Pistol;
            return Unarmed;
        }
    }
}
