using System;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Interaction;

namespace VrBattlegrounds.Haptics
{
    /// <summary>
    /// Отклик взаимодействий по умолчанию — один ассет проекта (<c>Resources/InteractionFeedbackConfig.asset</c>): какой префаб
    /// отклика включать для вида взаимодействия. Префаб отклика — обычный GO из компонентов Unity: <see cref="HapticPlayer" />,
    /// звук, подсветка, частицы. Для конкретного якоря или предмета значение перекрывает
    /// <see cref="InteractionFeedbackOverride" /> (самое частное побеждает). Пустое поле — без отклика. Исполнитель —
    /// <see cref="InteractionFeedback" />. Без ассета — пустой конфиг и <c>GameLog.Error</c>.
    /// </summary>
    [CreateAssetMenu(fileName = nameof(InteractionFeedbackConfig), menuName = "VR Battlegrounds/Haptics/Interaction Feedback Config")]
    public sealed class InteractionFeedbackConfig : ScriptableObject
    {
        private static InteractionFeedbackConfig s_instance;

        [Header("Якорь готов: отпусти — предмет встанет, нажми grip — достанешь")]
        [Tooltip("Карман магазинов на аватаре.")]
        [SerializeField] private GameObject _readyMagazinePocket;
        [Tooltip("Якорь основного оружия на спине (Anchor_Back).")]
        [SerializeField] private GameObject _readyPrimaryAnchor;
        [Tooltip("Якорь второго оружия на бедре (Anchor_Hip).")]
        [SerializeField] private GameObject _readySecondaryAnchor;
        [Tooltip("Прочий якорь на аватаре.")]
        [SerializeField] private GameObject _readyAvatarOther;
        [Tooltip("Якорь вне аватара: гнездо магазина оружия, слот арсенала.")]
        [SerializeField] private GameObject _readyWorldAnchor;

        [Header("Предмет")]
        [Tooltip("Свободный предмет в досягаемости пустой руки (нажми grip — возьмёшь).")]
        [SerializeField] private GameObject _itemInReach;
        [Tooltip("Разово: предмет взят.")]
        [SerializeField] private GameObject _itemGrab;
        [Tooltip("Разово: предмет уложен в якорь.")]
        [SerializeField] private GameObject _itemPlace;
        [Tooltip("Разово: предмет отпущен не в якорь.")]
        [SerializeField] private GameObject _itemRelease;

        [Tooltip("Сколько живёт разовый отклик события, с (звук, частицы), затем возвращается в пул.")]
        [SerializeField, Min(0.1f)] private float _oneShotLifetime = 2f;

        public static InteractionFeedbackConfig Instance
        {
            get
            {
                if (s_instance == null) s_instance = Resources.Load<InteractionFeedbackConfig>(nameof(InteractionFeedbackConfig));
                if (s_instance == null)
                {
                    GameLog.Error("[InteractionFeedbackConfig] Нет Resources/InteractionFeedbackConfig.asset — отклика взаимодействий не будет.");
                    s_instance = CreateInstance<InteractionFeedbackConfig>();
                }
                return s_instance;
            }
        }

        /// <summary>Префаб отклика готовности якоря данной роли или null.</summary>
        public GameObject AnchorReady(AnchorRoleKind role) => role switch
        {
            AnchorRoleKind.Magazine => _readyMagazinePocket,
            AnchorRoleKind.Primary => _readyPrimaryAnchor,
            AnchorRoleKind.Secondary => _readySecondaryAnchor,
            AnchorRoleKind.AvatarOther => _readyAvatarOther,
            _ => _readyWorldAnchor,
        };

        public GameObject ItemInReach => _itemInReach;
        public GameObject ItemGrab => _itemGrab;
        public GameObject ItemPlace => _itemPlace;
        public GameObject ItemRelease => _itemRelease;
        public float OneShotLifetime => _oneShotLifetime;

        /// <summary>Правка конфига в инспекторе: исполнитель пересоздаёт активные отклики.</summary>
        public static event Action<InteractionFeedbackConfig> Changed;

        private void OnValidate() => Changed?.Invoke(this);
    }
}
