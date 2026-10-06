using UltimateXR.Manipulation;
using UnityEngine;
using UnityEngine.Serialization;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Interaction
{
    /// <summary>
    /// Звуки якоря: вставка предмета (магазин в оружие, оружие в карман) и, если задан
    /// <see cref="_takeOutClip" />, доставание. У карманов это подтверждение «карман принял /
    /// отдал» — в игре карманов не видно. У гнезда магазина в оружии — щелчок вставки и звук
    /// выпадения магазина (рукой или кнопкой выброса).
    ///
    /// <para>
    /// Заменяет <c>AudioSource</c> с <c>Play On Awake</c> на объекте <c>Activate On Placed</c>:
    /// UltimateXR включает этот объект и тогда, когда предмет лежит в якоре с самого спавна,
    /// и звук играл без всякого действия игрока (AUD-01). Событие <c>Placed</c> от стартового
    /// состояния не приходит, а программная раскладка и выемка (магазины по карманам при спавне,
    /// карман магазинов прячет вложенное) отсекаются проверкой <see cref="_onlyByHand" />:
    /// у действия рукой в событии есть <c>Grabber</c>.
    /// </para>
    ///
    /// <para>
    /// Источник для доставания не должен лежать на объекте <c>Activate On Placed</c>: при хвате из
    /// якоря SDK выключает этот объект (<c>UxrGrabManager.GrabObject</c>), и звук обрывается на
    /// первом кадре. У карманов источник — на самом якоре (проверяет <c>AnchorActivationAudioTests</c>).
    /// </para>
    ///
    /// <para>
    /// Карман магазинов (<see cref="UxrMagazinePocket" />) хранит магазины вне якоря: события
    /// <c>Removed</c> при доставании нет, доставание приходит его событием
    /// <see cref="UxrMagazinePocket.ItemExtracted" />.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UxrGrabbableObjectAnchor))]
    public sealed class AnchorSound : MonoBehaviour
    {
        [Tooltip("Источник звука. Его clip — звук вставки; пусто — общий звук MagazineAnchorSoundDefaults. Play On Awake выключен. Для доставания — не на объекте Activate On Placed: SDK выключит его при хвате.")]
        [SerializeField] private AudioSource _source;

        [Tooltip("Звук доставания рукой. Пусто — доставание без звука (так у гнёзд магазина в оружии).")]
        [SerializeField] private AudioClip _takeOutClip;

        [Tooltip("Вставка звучит только от руки игрока. Программная установка (спавн, раскладка лоадаута) — молча.")]
        [FormerlySerializedAs("_onlyWhenPlacedByHand")]
        [SerializeField] private bool _onlyByHand = true;

        [Tooltip("Доставание звучит только от руки. Карманам — да (карман магазинов сам программно вынимает вложенное). " +
                 "Гнезду магазина в оружии — нет: выброс кнопкой A/X (MagazineEject) вынимает магазин без руки.")]
        [SerializeField] private bool _takeOutOnlyByHand = true;

        public AudioSource Source             => _source;
        public AudioClip   TakeOutClip        => _takeOutClip;
        public bool        TakeOutOnlyByHand  => _takeOutOnlyByHand;

        /// <summary>Звук вставки: свой клип источника или общий из <see cref="MagazineAnchorSoundDefaults" />.</summary>
        public AudioClip   InsertClip => _source != null && _source.clip != null ? _source.clip
                                         : MagazineAnchorSoundDefaults.Instance != null ? MagazineAnchorSoundDefaults.Instance.InsertClip : null;

        private UxrGrabbableObjectAnchor _anchor;
        private UxrMagazinePocket        _magazinePocket;

        private void Awake()
        {
            _anchor         = GetComponent<UxrGrabbableObjectAnchor>();
            _magazinePocket = GetComponent<UxrMagazinePocket>();
        }

        private void OnEnable()
        {
            _anchor.Placed  += OnPlaced;
            _anchor.Removed += OnRemoved;
            if (_magazinePocket != null) _magazinePocket.ItemExtracted += OnMagazineExtracted;
        }

        private void OnDisable()
        {
            _anchor.Placed  -= OnPlaced;
            _anchor.Removed -= OnRemoved;
            if (_magazinePocket != null) _magazinePocket.ItemExtracted -= OnMagazineExtracted;
        }

        private void OnPlaced(object sender, UxrManipulationEventArgs e)
        {
            if (_onlyByHand && e.Grabber == null) return;
            Play(InsertClip, e.GrabbableObject, "вставлен в");
        }

        private void OnRemoved(object sender, UxrManipulationEventArgs e)
        {
            if (_takeOutOnlyByHand && e.Grabber == null) return;
            Play(_takeOutClip, e.GrabbableObject, "достан из");
        }

        private void OnMagazineExtracted(UxrGrabber grabber, UxrGrabbableObject magazine)
        {
            if (_takeOutOnlyByHand && grabber == null) return;
            Play(_takeOutClip, magazine, "достан из");
        }

        private void Play(AudioClip clip, UxrGrabbableObject item, string action)
        {
            if (_source == null || clip == null) return;

            // Источник вставки может лежать на объекте Activate On Placed (гнёзда магазина в
            // оружии): к событию Placed SDK его уже включил, но на случай другого порядка —
            // включаем явно, иначе звук молчит.
            if (!_source.gameObject.activeInHierarchy) _source.gameObject.SetActive(true);

            // PlayOneShot, а не Play: вставка и доставание подряд не обрывают друг друга.
            _source.PlayOneShot(clip);
            GameLog.WeaponSystem.Verbose($"[AnchorSound] {(item != null ? item.name : "?")} {action} {name}", this);
        }
    }
}
