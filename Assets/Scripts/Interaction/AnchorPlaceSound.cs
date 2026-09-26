using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Interaction
{
    /// <summary>
    /// Звук вставки предмета в якорь (магазин в оружие, магазин в карман, оружие за спину).
    ///
    /// <para>
    /// Заменяет <c>AudioSource</c> с <c>Play On Awake</c> на объекте <c>Activate On Placed</c>:
    /// UltimateXR включает этот объект и тогда, когда предмет лежит в якоре с самого спавна,
    /// и звук играл без всякого действия игрока (AUD-01). Событие <c>Placed</c> от стартового
    /// состояния не приходит, а программная раскладка (магазины по карманам при спавне)
    /// отсекается проверкой <see cref="_onlyWhenPlacedByHand" />.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UxrGrabbableObjectAnchor))]
    public sealed class AnchorPlaceSound : MonoBehaviour
    {
        [Tooltip("Источник звука. Play On Awake у него должен быть выключен.")]
        [SerializeField] private AudioSource _source;

        [Tooltip("Играть только если предмет вложила рука игрока. Программная установка (спавн, раскладка лоадаута) — молча.")]
        [SerializeField] private bool _onlyWhenPlacedByHand = true;

        public AudioSource Source => _source;

        private UxrGrabbableObjectAnchor _anchor;

        private void Awake()
        {
            _anchor = GetComponent<UxrGrabbableObjectAnchor>();
        }

        private void OnEnable()
        {
            _anchor.Placed += OnPlaced;
        }

        private void OnDisable()
        {
            _anchor.Placed -= OnPlaced;
        }

        private void OnPlaced(object sender, UxrManipulationEventArgs e)
        {
            if (_source == null) return;
            if (_onlyWhenPlacedByHand && e.Grabber == null) return;

            // Источник может лежать на объекте Activate On Placed: к событию Placed SDK его уже
            // включил, но на случай другого порядка — включаем явно, иначе Play() молчит.
            if (!_source.gameObject.activeInHierarchy) _source.gameObject.SetActive(true);

            _source.Play();
            GameLog.WeaponSystem.Verbose($"[AnchorPlaceSound] {e.GrabbableObject?.name} → {name}", this);
        }
    }
}
