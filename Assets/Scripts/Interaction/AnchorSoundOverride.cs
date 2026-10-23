using UltimateXR.Manipulation;
using UnityEngine;

namespace VrBattlegrounds.Interaction
{
    /// <summary>
    /// Свои звуки предмета для якорей-приёмников: магазин, оружие или патрон звучит при вставке и доставании
    /// этими клипами вместо клипа якоря и дефолта роли (<see cref="MagazineAnchorSoundDefaults" />). Пустое поле —
    /// звук выбирает якорь.
    ///
    /// <para>
    /// Сам ничего не проигрывает: играет только <see cref="AnchorSound" /> приёмника, который спрашивает этот
    /// компонент у размещаемого или извлекаемого предмета. Так у события один исполнитель и двойного звука нет
    /// (правило <c>AmmoInsertSoundTests</c>). Ставится на корень предмета, рядом с <see cref="UxrGrabbableObject" />.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UxrGrabbableObject))]
    public sealed class AnchorSoundOverride : MonoBehaviour
    {
        [Tooltip("Звук вставки этого предмета в любой якорь. Пусто — звук якоря.")]
        [SerializeField] private AudioClip _placeClip;

        [Tooltip("Звук доставания этого предмета из любого якоря. Пусто — звук якоря.")]
        [SerializeField] private AudioClip _takeOutClip;

        public AudioClip PlaceClip   => _placeClip;
        public AudioClip TakeOutClip => _takeOutClip;
    }
}
