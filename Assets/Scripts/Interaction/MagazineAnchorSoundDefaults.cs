using UnityEngine;

namespace VrBattlegrounds.Interaction
{
    /// <summary>
    /// Общий звук вставки для <see cref="AnchorSound" />: гнездо без своего клипа звучит им. Звук вставки
    /// принадлежит приёмнику (гнезду магазина, окну приёма патрона, карману), а не предмету — у магазинов и
    /// патронов своего звука размещения нет (<c>AmmoInsertSoundTests</c>), иначе вставка звучит дважды.
    /// Лежит в <c>Resources/MagazineAnchorSoundDefaults.asset</c>.
    /// </summary>
    [CreateAssetMenu(menuName = "VR Battlegrounds/Audio/Anchor Sound Defaults")]
    public sealed class MagazineAnchorSoundDefaults : ScriptableObject
    {
        [Tooltip("Звук вставки по умолчанию — для гнёзд без своего клипа.")]
        [SerializeField] private AudioClip _insertClip;

        public AudioClip InsertClip => _insertClip;

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
