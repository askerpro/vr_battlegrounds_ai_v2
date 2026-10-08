using System;
using UltimateXR.Haptics;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Interaction;

namespace VrBattlegrounds.Haptics
{
    /// <summary>
    /// Клипы вибрации для событий, которые поднимает сама игра, а не компонент с собственным полем клипа: готовность
    /// якоря по его роли, хват/укладка/отпускание предмета своей рукой. Каждый слот — <see cref="UxrHapticClip" /> с общей
    /// формой (<see cref="UxrHapticWaveform" />) и своими силой, приоритетом, паузой повтора и кулдауном. Слот без формы —
    /// без вибрации. Для отдельного якоря или предмета слот перекрывает <see cref="HapticOverride" />. Новый сценарий
    /// взаимодействия — новый слот/форма в данных, а не новый скрипт (<c>tasks/haptics-system/Details.md</c>, п. 0).
    /// Грузится из <c>Resources/HapticRoles.asset</c>; без ассета — пустые роли и <c>GameLog.Error</c>.
    /// </summary>
    [CreateAssetMenu(fileName = nameof(HapticRoles), menuName = "VR Battlegrounds/Haptics/Haptic Roles")]
    public sealed class HapticRoles : ScriptableObject
    {
        private static HapticRoles s_instance;

        [Header("Якорь готов: отпусти — предмет встанет, нажми grip — достанешь")]
        [Tooltip("Карман магазинов на аватаре.")]
        [SerializeField] private UxrHapticClip _readyMagazinePocket = new UxrHapticClip();
        [Tooltip("Якорь основного оружия на спине (Anchor_Back).")]
        [SerializeField] private UxrHapticClip _readyPrimaryAnchor = new UxrHapticClip();
        [Tooltip("Якорь второго оружия на бедре (Anchor_Hip).")]
        [SerializeField] private UxrHapticClip _readySecondaryAnchor = new UxrHapticClip();
        [Tooltip("Прочий якорь на аватаре.")]
        [SerializeField] private UxrHapticClip _readyAvatarOther = new UxrHapticClip();
        [Tooltip("Якорь вне аватара: гнездо магазина оружия, слот арсенала. Поиск по всем якорям сцены — включать осознанно.")]
        [SerializeField] private UxrHapticClip _readyWorldAnchor = new UxrHapticClip();

        [Header("Предмет в руке своего игрока")]
        [SerializeField] private UxrHapticClip _itemGrab = new UxrHapticClip();
        [SerializeField] private UxrHapticClip _itemPlace = new UxrHapticClip();
        [SerializeField] private UxrHapticClip _itemRelease = new UxrHapticClip();

        public static HapticRoles Instance
        {
            get
            {
                if (s_instance == null) s_instance = Resources.Load<HapticRoles>(nameof(HapticRoles));
                if (s_instance == null)
                {
                    GameLog.Error("[HapticRoles] Нет Resources/HapticRoles.asset — вибрации взаимодействий не будет.");
                    s_instance = CreateInstance<HapticRoles>();
                }
                return s_instance;
            }
        }

        /// <summary>Клип готовности якоря данной роли.</summary>
        public UxrHapticClip AnchorReady(AnchorRoleKind role) => role switch
        {
            AnchorRoleKind.Magazine => _readyMagazinePocket,
            AnchorRoleKind.Primary => _readyPrimaryAnchor,
            AnchorRoleKind.Secondary => _readySecondaryAnchor,
            AnchorRoleKind.AvatarOther => _readyAvatarOther,
            _ => _readyWorldAnchor,
        };

        public UxrHapticClip ItemGrab => _itemGrab;
        public UxrHapticClip ItemPlace => _itemPlace;
        public UxrHapticClip ItemRelease => _itemRelease;

        /// <summary>Правка ролей в инспекторе: непрерывные голоса нужно перезапустить с новыми параметрами клипа.</summary>
        public static event Action<HapticRoles> Changed;

        private void OnValidate() => Changed?.Invoke(this);
    }
}
