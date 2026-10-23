using System;
using UnityEngine;

namespace VrBattlegrounds.Interaction
{
    /// <summary>
    /// Звуки якорей по умолчанию для <see cref="AnchorSound" />: пара «вставка/доставание» на каждую роль якоря
    /// (<see cref="AnchorRoleKind" />) и общий звук вставки. Якорь без своего клипа звучит дефолтом своей роли, роль
    /// без дефолта — общим звуком вставки (доставания по умолчанию нет). Подбор звука на слух — правка этого ассета,
    /// без прохода по префабам. Приёмник боеприпасов ствола (гнездо магазина, окно приёма патрона) до дефолта роли
    /// спрашивает запасной звук категории ствола (<c>WeaponFeedbackDefaults</c>) — порядок держит <see cref="AnchorSound" />.
    ///
    /// <para>
    /// Звук вставки принадлежит приёмнику (гнездо магазина, окно приёма патрона, карман, слот арсенала), а не
    /// предмету — у магазинов и патронов своего звука размещения нет (<c>AmmoInsertSoundTests</c>), иначе вставка
    /// звучит дважды. Предмет может только подменить клип приёмника (<see cref="AnchorSoundOverride" />).
    /// </para>
    ///
    /// <para>
    /// Имя класса историческое (раньше здесь был только звук вставки магазина): ассет ищется по нему в
    /// <c>Resources/MagazineAnchorSoundDefaults.asset</c>, и на имя ссылаются тесты.
    /// </para>
    /// </summary>
    [CreateAssetMenu(menuName = "VR Battlegrounds/Audio/Anchor Sound Defaults")]
    public sealed class MagazineAnchorSoundDefaults : ScriptableObject
    {
        /// <summary>Пара звуков роли якоря. Пустое поле — у роли нет своего дефолта.</summary>
        [Serializable]
        public struct RoleClips
        {
            [Tooltip("Вставка: предмет положили в якорь рукой.")]
            public AudioClip place;

            [Tooltip("Доставание: предмет взяли из якоря.")]
            public AudioClip takeOut;
        }

        [Tooltip("Общий звук вставки — якорям без своего клипа и без дефолта роли (в том числе гнезду магазина и окну приёма патрона без запасного звука категории ствола).")]
        [SerializeField] private AudioClip _insertClip;

        [Tooltip("Стойки и слоты арсенала (роль World): положить/взять оружие и магазин.")]
        [SerializeField] private RoleClips _weaponAnchor;

        [Tooltip("Карман основного оружия на спине (Anchor_Back, роль Primary).")]
        [SerializeField] private RoleClips _riflePocket;

        [Tooltip("Карман пистолета на бедре (Anchor_Hip_R, роль Secondary).")]
        [SerializeField] private RoleClips _pistolPocket;

        [Tooltip("Карман магазинов (MagazinePocket, роль Magazine).")]
        [SerializeField] private RoleClips _magazinePocket;

        [Tooltip("Гнездо магазина оружия (роль WeaponMagazine) и окно приёма патрона: присоединить/отщёлкнуть магазин. Звучит, если у гнезда нет своего клипа (нарезки пака) и у категории ствола нет запасного (WeaponFeedbackDefaults).")]
        [SerializeField] private RoleClips _magazineSocket;

        /// <summary>Общий звук вставки.</summary>
        public AudioClip InsertClip => _insertClip;

        /// <summary>Дефолт вставки роли или null, если у роли его нет (общий звук выбирает <see cref="AnchorSound" />).</summary>
        public AudioClip RolePlaceClip(AnchorRoleKind role) => Clips(role).place;

        /// <summary>Дефолт доставания роли или null.</summary>
        public AudioClip RoleTakeOutClip(AnchorRoleKind role) => Clips(role).takeOut;

        private RoleClips Clips(AnchorRoleKind role) => role switch
        {
            AnchorRoleKind.World => _weaponAnchor,
            AnchorRoleKind.Primary => _riflePocket,
            AnchorRoleKind.Secondary => _pistolPocket,
            AnchorRoleKind.Magazine => _magazinePocket,
            AnchorRoleKind.WeaponMagazine => _magazineSocket,
            // AvatarOther, ItemOther — своих дефолтов нет. Окно приёма патрона AnchorSound спрашивает как гнездо магазина.
            _ => default,
        };

        private static MagazineAnchorSoundDefaults s_instance;

        /// <summary>Настройки из Resources или null, если ассета нет.</summary>
        public static MagazineAnchorSoundDefaults Instance
        {
            get
            {
                if (s_instance == null) s_instance = Resources.Load<MagazineAnchorSoundDefaults>(nameof(MagazineAnchorSoundDefaults));
                return s_instance;
            }
        }
    }
}
