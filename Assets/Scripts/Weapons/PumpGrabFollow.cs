using UltimateXR.Manipulation;
using UnityEngine;

namespace VrBattlegrounds.Weapons
{
    /// <summary>
    /// Помпа идёт за смещением руки с момента хвата, а не за её абсолютным положением.
    ///
    /// <para>
    /// <b>Дефект.</b> Помпу, которая наводит ствол (<c>Control Parent Direction</c>), UltimateXR
    /// каждый кадр ставит так, чтобы её точка хвата совпала с рукой, и только потом режет по ходу.
    /// Рука почти никогда не берёт помпу ровно в точке хвата: взял на 10 см впереди — помпа
    /// упирается в передний предел, и первые 10 см оттягивания она стоит на месте.
    /// </para>
    ///
    /// <para>
    /// <b>Решение.</b> В момент хвата запоминаются положение руки и помпы в осях оружия; дальше в
    /// <c>ConstraintsApplied</c> (после решения SDK, до <c>KeepGripsInPlace</c>) помпа ставится на
    /// исходное место плюс смещение руки вдоль хода, в пределах <c>Translation Limits</c>. Рука
    /// остаётся приклеенной к одной точке помпы — это делает <c>KeepGripsInPlace</c>. Считается на
    /// каждой машине из рук аватаров, как и решение SDK.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(230)]
    public sealed class PumpGrabFollow : MonoBehaviour
    {
        [Tooltip("Граббабл помпы — ребёнок оружия с Restrict Local Offset по ходу.")]
        [SerializeField] private UxrGrabbableObject _pump;

        private Vector3 _restLocalPosition;
        private Quaternion _restLocalRotation;
        private Vector3 _pumpLocalPosition;
        private UxrGrabber _hand;
        private Vector3 _handAtGrab;
        private Vector3 _pumpAtGrab;

        public UxrGrabbableObject Pump => _pump;

        private void Awake()
        {
            if (_pump == null) return;

            _restLocalPosition = _pump.transform.localPosition;
            _restLocalRotation = _pump.transform.localRotation;
            _pumpLocalPosition = _restLocalPosition;
        }

        private void OnEnable()
        {
            if (_pump != null) _pump.ConstraintsApplied += Pump_ConstraintsApplied;
        }

        private void OnDisable()
        {
            if (_pump != null) _pump.ConstraintsApplied -= Pump_ConstraintsApplied;
            _hand = null;
        }

        private void LateUpdate()
        {
            var readiness = GetComponent<WeaponReadinessController>();
            if (_pump != null && readiness != null && readiness.IsConfigured && UxrGrabManager.HasInstance &&
                !UxrGrabManager.Instance.IsBeingGrabbed(_pump))
            {
                // Controller/Visuals могли вернуть помпу. Следующий хват начинает delta от actual pose,
                // а не resurrect сохранённый rear. Это readonly cache, не второй pose writer.
                _pumpLocalPosition = _pump.transform.localPosition;
                _hand = null;
            }
        }

        private void Pump_ConstraintsApplied(object sender, UxrApplyConstraintsEventArgs e)
        {
            if (!UxrGrabManager.Instance.GetGrabbingHand(_pump, 0, out UxrGrabber hand))
            {
                _hand = null;
                return;
            }

            Transform weapon = _pump.transform.parent;

            // Рука с контроллера, запомненная SDK в начале кадра: сама hand.transform к этому
            // моменту уже переставлена в точку хвата помпы, и смещение считалось бы от помпы.
            Vector3 handInWeapon = weapon.InverseTransformPoint(hand.UnprocessedGrabberPosition);

            // Новый хват: отсчёт от того места, где помпа стояла до него, а не куда её сдвинул SDK.
            if (hand != _hand)
            {
                _hand = hand;
                _handAtGrab = handInWeapon;
                _pumpAtGrab = _pumpLocalPosition;
            }

            _pumpLocalPosition = FollowHand(_pumpAtGrab, handInWeapon - _handAtGrab);
            _pump.transform.SetLocalPositionAndRotation(_pumpLocalPosition, _restLocalRotation);
        }

        /// <summary>
        /// Положение помпы: место на момент хвата плюс смещение руки вдоль хода, в пределах хода.
        /// Всё в осях оружия.
        /// </summary>
        public Vector3 FollowHand(Vector3 pumpAtGrab, Vector3 handDelta)
        {
            Vector3 min = _pump.TranslationLimitsMin;
            Vector3 max = _pump.TranslationLimitsMax;
            Vector3 travel = max - min;
            if (travel.sqrMagnitude < 1e-8f) return pumpAtGrab;

            Vector3 axis = travel.normalized;
            float along = Vector3.Dot(pumpAtGrab - _restLocalPosition + handDelta, axis);
            float from = Vector3.Dot(min, axis);
            float to = Vector3.Dot(max, axis);

            return _restLocalPosition + axis * Mathf.Clamp(along, Mathf.Min(from, to), Mathf.Max(from, to));
        }
    }
}
