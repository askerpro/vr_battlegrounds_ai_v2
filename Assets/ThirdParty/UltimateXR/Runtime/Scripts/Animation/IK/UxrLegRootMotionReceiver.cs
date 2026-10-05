// --------------------------------------------------------------------------------------------------------------------
// VR Battlegrounds patch 37: перехват root motion копии рига ног.
// --------------------------------------------------------------------------------------------------------------------
using UnityEngine;

namespace UltimateXR.Animation.IK
{
    /// <summary>
    ///     VR Battlegrounds patch 37: забирает root motion Animator копии рига себе. С <c>OnAnimatorMove</c> Unity не двигает
    ///     корень сам, а поза клипа остаётся «на месте» относительно корня; сдвиг за кадр копится здесь, и
    ///     <see cref="UxrAnimatedLegs" /> применяет его к корню сам (только горизонталь и рысканье). Без
    ///     <c>applyRootMotion</c> <c>Animator.deltaPosition</c> нулевой — поэтому не выключение, а перехват.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UxrLegRootMotionReceiver : MonoBehaviour
    {
        #region Public Methods

        /// <summary>Накопленный с прошлого вызова сдвиг и поворот корня клипом; обнуляет накопленное.</summary>
        public void Consume(out Vector3 delta, out Quaternion deltaRotation)
        {
            delta          = _delta;
            deltaRotation  = _deltaRotation;
            _delta         = Vector3.zero;
            _deltaRotation = Quaternion.identity;
        }

        #endregion

        #region Unity

        private void Awake()
        {
            _animator = GetComponent<Animator>();
        }

        private void OnAnimatorMove()
        {
            if (_animator == null)
            {
                return;
            }

            _delta         += _animator.deltaPosition;
            _deltaRotation =  _animator.deltaRotation * _deltaRotation;
        }

        #endregion

        #region Private Types & Data

        private Animator   _animator;
        private Vector3    _delta;
        private Quaternion _deltaRotation = Quaternion.identity;

        #endregion
    }
}
