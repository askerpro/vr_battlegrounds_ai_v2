using UltimateXR.Manipulation;
using UnityEngine;
using UnityEngine.Serialization;
using VrBattlegrounds.Core;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Interaction
{
    /// <summary>
    /// Звуки якоря: вставка предмета (магазин в оружие, оружие в карман) и, если нашёлся клип
    /// (<see cref="TakeOutClip" />), доставание. У карманов это подтверждение «карман принял /
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
    /// Выбор клипа (вставка и доставание одинаково): переопределение на предмете (<see cref="AnchorSoundOverride" />)
    /// → свой клип якоря (clip источника / <see cref="_takeOutClip" />) → у приёмника боеприпасов ствола — запасной звук
    /// гнезда категории ствола (<see cref="WeaponFeedbackDefaults.MagazineInsert" />/<see cref="WeaponFeedbackDefaults.MagazineTakeOut" />,
    /// ассет — по ссылке хоста <see cref="WeaponSystem" />) → дефолт роли якоря (<see cref="MagazineAnchorSoundDefaults" />,
    /// роль — <see cref="AnchorRole.Get" />) → общий звук вставки (у доставания общего нет — тишина). Играет только этот
    /// компонент приёмника; предмет сам не звучит.
    /// </para>
    ///
    /// <para>
    /// Приёмник боеприпасов ствола — гнездо магазина (роль <see cref="AnchorRoleKind.WeaponMagazine" />) и окно приёма патрона
    /// ручного заряжания (<see cref="CartridgeIntake.Intake" />): окно звучит по той же цепочке, что гнездо (дефолт роли —
    /// гнезда магазина), так что пустой клип гнезда не оставляет окно беззвучным. Ствол без хоста или без ссылки на
    /// дефолты категории — сразу дефолт роли.
    /// </para>
    ///
    /// <para>
    /// Звук локальный: Placed/Removed SDK приходят на каждой машине, клипы и роль одинаковы у всех (префаб,
    /// иерархия), поэтому синхронизировать нечего.
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
        [Tooltip("Источник звука. Его clip — свой звук вставки; пусто — у гнезда магазина и окна приёма патрона запасной звук категории ствола (WeaponFeedbackDefaults), затем дефолт роли или общий звук MagazineAnchorSoundDefaults. Play On Awake выключен. Для доставания — не на объекте Activate On Placed: SDK выключит его при хвате.")]
        [SerializeField] private AudioSource _source;

        [Tooltip("Свой звук доставания. Пусто — у гнезда магазина и окна приёма патрона запасной звук категории ствола (WeaponFeedbackDefaults), затем дефолт роли из MagazineAnchorSoundDefaults; нет и его — доставание без звука.")]
        [SerializeField] private AudioClip _takeOutClip;

        [Tooltip("Вставка звучит только от руки игрока. Программная установка (спавн, раскладка лоадаута) — молча.")]
        [FormerlySerializedAs("_onlyWhenPlacedByHand")]
        [SerializeField] private bool _onlyByHand = true;

        [Tooltip("Доставание звучит только от руки. Карманам — да (карман магазинов сам программно вынимает вложенное). " +
                 "Гнезду магазина в оружии — нет: выброс кнопкой A/X (MagazineEject) вынимает магазин без руки.")]
        [SerializeField] private bool _takeOutOnlyByHand = true;

        public AudioSource Source             => _source;
        public bool        TakeOutOnlyByHand  => _takeOutOnlyByHand;

        /// <summary>
        /// Звук вставки без учёта предмета: свой клип источника → запасной звук гнезда категории ствола (приёмник
        /// боеприпасов) → дефолт роли → общий из <see cref="MagazineAnchorSoundDefaults" />.
        /// </summary>
        public AudioClip InsertClip => OwnInsertClip != null ? OwnInsertClip : DefaultInsertClip;

        /// <summary>
        /// Звук доставания без учёта предмета: свой клип → запасной звук гнезда категории ствола (приёмник боеприпасов) →
        /// дефолт роли → нет звука.
        /// </summary>
        public AudioClip TakeOutClip => _takeOutClip != null ? _takeOutClip : DefaultTakeOutClip;

        /// <summary>Свой звук вставки якоря (clip источника) без дефолтов; null — нет своего.</summary>
        public AudioClip OwnInsertClip => _source != null ? _source.clip : null;

        /// <summary>Свой звук доставания якоря без дефолтов; null — нет своего.</summary>
        public AudioClip OwnTakeOutClip => _takeOutClip;

        /// <summary>
        /// Звук вставки по умолчанию, без своего клипа: запасной звук категории ствола (приёмник боеприпасов) → дефолт роли →
        /// общий. Редакторская очистка сравнивает с ним свой клип: совпадение — копия дефолта, её можно снять.
        /// </summary>
        public AudioClip DefaultInsertClip
        {
            get
            {
                AnchorRoleKind role = DefaultsRole(out WeaponFeedbackDefaults category);
                if (category != null && category.MagazineInsert != null) return category.MagazineInsert;
                MagazineAnchorSoundDefaults defaults = MagazineAnchorSoundDefaults.Instance;
                if (defaults == null) return null;
                AudioClip roleClip = defaults.RolePlaceClip(role);
                return roleClip != null ? roleClip : defaults.InsertClip;
            }
        }

        /// <summary>Звук доставания по умолчанию, без своего клипа: категория ствола (приёмник боеприпасов) → дефолт роли → нет.</summary>
        public AudioClip DefaultTakeOutClip
        {
            get
            {
                AnchorRoleKind role = DefaultsRole(out WeaponFeedbackDefaults category);
                if (category != null && category.MagazineTakeOut != null) return category.MagazineTakeOut;
                MagazineAnchorSoundDefaults defaults = MagazineAnchorSoundDefaults.Instance;
                return defaults != null ? defaults.RoleTakeOutClip(role) : null;
            }
        }

        /// <summary>Якорь — приёмник боеприпасов ствола (гнездо магазина или окно приёма патрона) с дефолтами категории.</summary>
        public bool IsWeaponAmmoReceiver
        {
            get
            {
                DefaultsRole(out _, out bool receiver);
                return receiver;
            }
        }

        /// <summary>Звук вставки предмета: его переопределение или <see cref="InsertClip" />.</summary>
        public AudioClip GetInsertClip(UxrGrabbableObject item)
        {
            AnchorSoundOverride own = item != null ? item.GetComponent<AnchorSoundOverride>() : null;
            return own != null && own.PlaceClip != null ? own.PlaceClip : InsertClip;
        }

        /// <summary>Звук доставания предмета: его переопределение или <see cref="TakeOutClip" />.</summary>
        public AudioClip GetTakeOutClip(UxrGrabbableObject item)
        {
            AnchorSoundOverride own = item != null ? item.GetComponent<AnchorSoundOverride>() : null;
            return own != null && own.TakeOutClip != null ? own.TakeOutClip : TakeOutClip;
        }

        // Роль и категория считаются по иерархии при каждом событии (редко): у префаба в редакторе Awake не было, якорь
        // может сменить ствол-родителя (оружие в кармане — тоже родитель), а кэш — вторая копия того, чем владеют AnchorRole
        // и хост WeaponSystem.
        private UxrGrabbableObjectAnchor Anchor => _anchor != null ? _anchor : GetComponent<UxrGrabbableObjectAnchor>();

        /// <summary>
        /// Роль, по которой берётся дефолт <see cref="MagazineAnchorSoundDefaults" />, и дефолты категории ствола, если якорь —
        /// приёмник боеприпасов ствола (гнездо магазина или окно приёма патрона; окно звучит дефолтом роли гнезда).
        /// </summary>
        private AnchorRoleKind DefaultsRole(out WeaponFeedbackDefaults category) => DefaultsRole(out category, out _);

        private AnchorRoleKind DefaultsRole(out WeaponFeedbackDefaults category, out bool receiver)
        {
            category = null;
            receiver = false;
            UxrGrabbableObjectAnchor anchor = Anchor;
            AnchorRoleKind role = AnchorRole.Get(anchor);
            if (role != AnchorRoleKind.WeaponMagazine)
            {
                CartridgeIntake intake = anchor.GetComponentInParent<CartridgeIntake>(true);
                if (intake == null || intake.Intake != anchor) return role;
                role = AnchorRoleKind.WeaponMagazine;
            }
            receiver = true;
            WeaponSystem host = anchor.GetComponentInParent<WeaponSystem>(true);
            if (host != null) category = host.FeedbackDefaults;
            return role;
        }

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
            Play(GetInsertClip(e.GrabbableObject), e.GrabbableObject, "вставлен в");
        }

        private void OnRemoved(object sender, UxrManipulationEventArgs e)
        {
            if (_takeOutOnlyByHand && e.Grabber == null) return;
            Play(GetTakeOutClip(e.GrabbableObject), e.GrabbableObject, "достан из");
        }

        private void OnMagazineExtracted(UxrGrabber grabber, UxrGrabbableObject magazine)
        {
            if (_takeOutOnlyByHand && grabber == null) return;
            Play(GetTakeOutClip(magazine), magazine, "достан из");
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
