using UltimateXR.Avatar;
using UltimateXR.Core;
using UnityEngine;

namespace VrBattlegrounds.DevTools.StressTest
{
    /// <summary>
    /// Кукла стресс-теста — обычный аватар игрока, заспавненный сервером без владельца.
    /// <c>UxrMirrorAvatar</c> переводит такой аватар в <see cref="UxrAvatarMode.UpdateExternally"/>,
    /// то есть ровно в тот режим, в котором шлем рисует чужого игрока: IK тела, позы кистей,
    /// хитбоксы, актор — всё настоящее.
    ///
    /// <para>
    /// Вместо сети позу куклы задаёт этот компонент: те же трансформы, что у настоящего
    /// remote-аватара двигает <c>NetworkTransform</c> (корень, камера, кисти), плюс позы
    /// пальцев, которые в игре едут каналом состояния UltimateXR. На хосте
    /// <c>NetworkTransform</c> без владельца трансформы не перетирает
    /// (<c>connectionToClient == null</c>), так что спорить с ним не приходится.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StressPuppet : MonoBehaviour
    {
        private UxrAvatar _avatar;
        private Transform _head;
        private Transform _leftHand;
        private Transform _rightHand;
        private Vector3    _anchorPosition;
        private Quaternion _anchorRotation;
        private string _leftPose;
        private string _rightPose;
        private float  _leftBlend  = -1f;
        private float  _rightBlend = -1f;

        /// <summary>Задержка повтора относительно локального аватара, с.</summary>
        public float Delay { get; private set; }

        public UxrAvatar Avatar => _avatar;

        public void Initialize(float delay, Vector3 anchorPosition, Quaternion anchorRotation)
        {
            Delay           = delay;
            _anchorPosition = anchorPosition;
            _anchorRotation = anchorRotation;
            _avatar = GetComponent<UxrAvatar>();
        }

        /// <summary>
        /// Применяет снимок. Корень — якорь куклы, сдвинутый так же, как корень игрока
        /// сдвинулся от старта прогона; голова и кисти — в координатах своего корня.
        /// </summary>
        public void Apply(in AvatarPoseSample sample)
        {
            if (_avatar == null) return;

            // Трансформы берутся лениво: камера и кости доступны после инициализации UltimateXR.
            if (_head == null)      _head      = _avatar.CameraTransform;
            if (_leftHand == null)  _leftHand  = _avatar.GetHandBone(UxrHandSide.Left);
            if (_rightHand == null) _rightHand = _avatar.GetHandBone(UxrHandSide.Right);

            Transform root = _avatar.transform;
            root.SetPositionAndRotation(_anchorPosition + _anchorRotation * sample.rootPosition,
                                        _anchorRotation * sample.rootRotation);

            Place(root, _head,      sample.headPosition,      sample.headRotation);
            Place(root, _leftHand,  sample.leftHandPosition,  sample.leftHandRotation);
            Place(root, _rightHand, sample.rightHandPosition, sample.rightHandRotation);

            ApplyHandPose(UxrHandSide.Left,  sample.leftHandPose,  sample.leftHandBlend,  ref _leftPose,  ref _leftBlend);
            ApplyHandPose(UxrHandSide.Right, sample.rightHandPose, sample.rightHandBlend, ref _rightPose, ref _rightBlend);
        }

        private static void Place(Transform root, Transform target, Vector3 localPosition, Quaternion localRotation)
        {
            if (target == null) return;
            target.SetPositionAndRotation(root.TransformPoint(localPosition), root.rotation * localRotation);
        }

        /// <summary>
        /// Поза кисти ставится только при смене: в игре она тоже приходит событием,
        /// а не каждый кадр. Позы, которой нет у скина куклы, UltimateXR просто не найдёт.
        /// </summary>
        private void ApplyHandPose(UxrHandSide side, string pose, float blend, ref string current, ref float currentBlend)
        {
            if (string.IsNullOrEmpty(pose)) return;
            if (pose == current && Mathf.Abs(blend - currentBlend) < 0.02f) return;

            current      = pose;
            currentBlend = blend;
            _avatar.SetCurrentHandPose(side, pose, blend, false);
        }

        /// <summary>
        /// Снимает позу локального аватара в координатах, пригодных для переноса на куклу.
        /// </summary>
        public static bool TryCapture(UxrAvatar local, Vector3 startRootPosition, Quaternion startRootRotation,
                                      out AvatarPoseSample sample)
        {
            sample = default;
            if (local == null || local.CameraTransform == null) return false;

            Transform root = local.transform;
            Quaternion startInverse = Quaternion.Inverse(startRootRotation);

            sample.rootPosition = startInverse * (root.position - startRootPosition);
            sample.rootRotation = startInverse * root.rotation;

            ToLocal(root, local.CameraTransform, out sample.headPosition, out sample.headRotation);
            ToLocal(root, local.GetHandBone(UxrHandSide.Left),  out sample.leftHandPosition,  out sample.leftHandRotation);
            ToLocal(root, local.GetHandBone(UxrHandSide.Right), out sample.rightHandPosition, out sample.rightHandRotation);

            sample.leftHandPose   = local.GetCurrentRuntimeHandPose(UxrHandSide.Left)?.PoseName;
            sample.leftHandBlend  = local.GetCurrentHandPoseBlendValue(UxrHandSide.Left);
            sample.rightHandPose  = local.GetCurrentRuntimeHandPose(UxrHandSide.Right)?.PoseName;
            sample.rightHandBlend = local.GetCurrentHandPoseBlendValue(UxrHandSide.Right);
            return true;
        }

        private static void ToLocal(Transform root, Transform target, out Vector3 position, out Quaternion rotation)
        {
            if (target == null)
            {
                position = Vector3.zero;
                rotation = Quaternion.identity;
                return;
            }

            position = root.InverseTransformPoint(target.position);
            rotation = Quaternion.Inverse(root.rotation) * target.rotation;
        }
    }
}
