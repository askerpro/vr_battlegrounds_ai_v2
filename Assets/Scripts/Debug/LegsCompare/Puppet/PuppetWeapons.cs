using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.DevTools.LegsCompare
{
    /// <summary>
    /// Оружие в руках аватара стенда — настоящим захватом UltimateXR (<see cref="UxrGrabManager.GrabObject(UxrGrabber, UxrGrabbableObject, int, bool)"/>):
    /// правая кисть — точка 0 (рукоять), левая — точка 1 (цевьё), если она есть. Позы пальцев, привязка оружия к кисти и
    /// стойка UxrLegs по оружию (<c>AvatarStanceFromGrabs</c>) работают сами. Без сети: экземпляр префаба обычный, события
    /// захвата не рассылаются.
    /// </summary>
    public sealed class PuppetWeapons
    {
        private readonly UxrAvatar _avatar;
        private GameObject _weapon;
        private UxrGrabbableObject _grabbable;
        private UxrGrabber _rightGrabber;
        private Pose _inGrabber;
        private bool _follow;
        private Pose _grabberInHand, _prev;
        private Transform _hand;
        private bool _hasPrev;
        private float _jitterMm, _jitterDeg;

        public PuppetWeapons(UxrAvatar avatar) => _avatar = avatar;

        /// <summary>Сейчас в руках (захват подтверждён менеджером).</summary>
        public bool IsHolding => _grabbable != null && UxrGrabManager.HasInstance && UxrGrabManager.Instance.IsBeingGrabbedBy(_grabbable, _avatar);

        /// <summary>Взять <paramref name="prefab"/> (null — убрать оружие). Возвращает, состоялся ли захват.</summary>
        public bool Hold(GameObject prefab, bool twoHands)
        {
            Drop();
            if (prefab == null || _avatar == null || !UxrGrabManager.HasInstance) return false;

            UxrGrabber right = _avatar.GetGrabber(UxrHandSide.Right);
            UxrGrabber left = _avatar.GetGrabber(UxrHandSide.Left);
            if (right == null) return false;

            _weapon = Object.Instantiate(prefab, right.transform.position, right.transform.rotation);
            _weapon.name = $"PuppetWeapon_{prefab.name} ({_avatar.name})";
            _grabbable = _weapon.GetComponentInChildren<UxrGrabbableObject>();
            if (_grabbable == null)
            {
                GameLog.Debug.Warning($"[PuppetWeapons] У '{prefab.name}' нет UxrGrabbableObject.");
                Drop();
                return false;
            }

            UxrGrabManager.Instance.GrabObject(right, _grabbable, 0, false);
            if (twoHands && left != null && _grabbable.GrabPointCount > 1)
                UxrGrabManager.Instance.GrabObject(left, _grabbable, 1, false);

            _rightGrabber = right;
            _follow = false;

            if (!IsHolding)
            {
                GameLog.Debug.Warning($"[PuppetWeapons] {_avatar.name}: захват '{prefab.name}' не состоялся.");
                Drop();
                return false;
            }

            // Аватар куклы — «чужой» (UpdateExternally): UltimateXR не двигает предмет в его руке (позу присылает сеть) и не
            // выравнивает его по точке хвата — оружие оставалось как появилось (вверх ногами, у бедра). Выравнивание — как у
            // менеджера хвата: где должен быть захватчик для текущей позы оружия (ComputeRequiredGrabberTransform) → поза
            // оружия в осях захватчика; захватчик в осях кости кисти — постоянный (он её ребёнок).
            Transform w = _weapon.transform;
            if (!_grabbable.ComputeRequiredGrabberTransform(right, 0, out Vector3 gp, out Quaternion gr, false))
            {
                gp = right.transform.position;
                gr = right.transform.rotation;
            }
            _inGrabber = new Pose(Quaternion.Inverse(gr) * (w.position - gp), Quaternion.Inverse(gr) * w.rotation);
            Transform hand = right.transform.parent;
            _grabberInHand = new Pose(Quaternion.Inverse(hand.rotation) * (right.transform.position - hand.position), Quaternion.Inverse(hand.rotation) * right.transform.rotation);
            _hand = hand;
            _follow = true;
            return true;
        }

        /// <summary>
        /// Оружие — от ЦЕЛЕВОЙ позы правой кисти (стенд ставит её до IK этого кадра): один писатель за кадр. Раньше оружие
        /// шло за кистью после IK, а IK второй руки тянул кисти к оружию — петля, оружие дёргалось.
        /// </summary>
        /// <summary>
        /// Ставить оружие от решённой кисти в конце кадра (<see cref="PlaceAfterIK"/>), а не от цели: у VRIK кисть ставит
        /// VRIK после стадий UltimateXR и рука второй руки к оружию не тянется — петли нет, а цель и решённая кисть
        /// расходятся (оружие висело вниз).
        /// </summary>
        public bool AfterIK;

        /// <summary>Конец кадра, для <see cref="AfterIK"/>: оружие — от решённой кисти.</summary>
        public void PlaceAfterIK()
        {
            if (!AfterIK || _hand == null) return;
            Pose hand = new Pose(_hand.position, _hand.rotation);
            AfterIK = false;
            Place(hand);
            AfterIK = true;
        }

        public void Place(Pose handTarget)
        {
            if (AfterIK) return;
            if (!_follow || _weapon == null || !IsHolding) return;
            Vector3 gp = handTarget.position + handTarget.rotation * _grabberInHand.position;
            Quaternion gr = handTarget.rotation * _grabberInHand.rotation;
            _weapon.transform.SetPositionAndRotation(gp + gr * _inGrabber.position, gr * _inGrabber.rotation);
        }

        /// <summary>Дрожание оружия относительно кости кисти после IK, кадр к кадру: мм и градусы (сброс при чтении).</summary>
        public (float mm, float deg) TakeJitter()
        {
            var r = (_jitterMm, _jitterDeg);
            _jitterMm = _jitterDeg = 0f;
            return r;
        }

        /// <summary>Конец кадра: замер дрожания (поза оружия в осях кисти против прошлого кадра).</summary>
        public void MeasureJitter()
        {
            if (!_follow || _weapon == null || _hand == null) return;
            Pose inHand = new Pose(Quaternion.Inverse(_hand.rotation) * (_weapon.transform.position - _hand.position), Quaternion.Inverse(_hand.rotation) * _weapon.transform.rotation);
            if (_hasPrev)
            {
                _jitterMm = Mathf.Max(_jitterMm, Vector3.Distance(inHand.position, _prev.position) * 1000f);
                _jitterDeg = Mathf.Max(_jitterDeg, Quaternion.Angle(inHand.rotation, _prev.rotation));
            }
            _prev = inHand;
            _hasPrev = true;
        }

        public void Drop()
        {
            _follow = false;
            if (_grabbable != null && UxrGrabManager.HasInstance) UxrGrabManager.Instance.ReleaseGrabs(_grabbable, false);
            if (_weapon != null) Object.Destroy(_weapon);
            _weapon = null;
            _grabbable = null;
        }
    }
}
