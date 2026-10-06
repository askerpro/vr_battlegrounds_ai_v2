using UnityEngine;

namespace UltimateXR.Animation.IK
{
    public sealed partial class UxrBodyIK
    {
        #region Public Methods (VR Battlegrounds patch 46)

        /// <summary>
        ///     VR Battlegrounds patch 46: единственный вход для кода вне BodyIK, который двигает предков независимых костей
        ///     (запястья, поставленные трекингом). Запоминает их мировые позы; <see cref="IndependentBonesGuard.Dispose" />
        ///     возвращает их. Без этого сдвиг таза или корня рига уносит запястья — цель IK рук смещается вместе с телом.
        ///     Хранилище отдельное от push/pop <see cref="PreSolveAvatarIK" /> / <see cref="PostSolveAvatarIK" />; вложенный вызов
        ///     пустой. Использование: <c>using (bodyIK.KeepIndependentBones()) { hips.SetPositionAndRotation(...); }</c>.
        /// </summary>
        public IndependentBonesGuard KeepIndependentBones()
        {
            if (_independentBonesHeld || _independentBones == null || _independentBones.Count == 0)
            {
                return default;
            }

            if (_heldBonePoses == null || _heldBonePoses.Length != _independentBones.Count)
            {
                _heldBonePoses = new Pose[_independentBones.Count];
            }

            for (int i = 0; i < _independentBones.Count; ++i)
            {
                Transform bone = _independentBones[i].Transform;
                _heldBonePoses[i] = new Pose(bone.position, bone.rotation);
            }

            _independentBonesHeld = true;
            return new IndependentBonesGuard(this);
        }

        private void ReleaseIndependentBones()
        {
            if (!_independentBonesHeld)
            {
                return;
            }

            for (int i = 0; i < _independentBones.Count; ++i)
            {
                _independentBones[i].Transform.SetPositionAndRotation(_heldBonePoses[i].position, _heldBonePoses[i].rotation);
            }

            _independentBonesHeld = false;
        }

        /// <summary>VR Battlegrounds patch 46: см. <see cref="KeepIndependentBones" />. Структура — без выделений памяти в кадре.</summary>
        public readonly struct IndependentBonesGuard : System.IDisposable
        {
            private readonly UxrBodyIK _owner;

            internal IndependentBonesGuard(UxrBodyIK owner) => _owner = owner;

            public void Dispose() => _owner?.ReleaseIndependentBones();
        }

        private Pose[] _heldBonePoses;
        private bool   _independentBonesHeld;

        #endregion
    }
}
