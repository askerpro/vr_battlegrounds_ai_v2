using UltimateXR.Avatar;
using UltimateXR.Core;
using UnityEngine;

namespace VrBattlegrounds.DevTools.Bots
{
    /// <summary>
    /// Поза бота. Аватар бота — обычный аватар игрока без владельца, то есть в режиме
    /// <see cref="UxrAvatarMode.UpdateExternally"/>, как чужой игрок на шлеме: тело строит IK
    /// по голове (<c>CameraTransform</c>) и кистям. У живого игрока их двигает
    /// <c>NetworkTransform</c> с его шлема, у бота — этот компонент на сервере, а клиентам
    /// поза уходит тем же <c>NetworkTransform</c> (<c>ServerToClient</c>, см.
    /// <c>ServerAuthoredAvatar</c>). Приём тот же, что у куклы стресс-теста (<c>StressPuppet</c>).
    ///
    /// <para>
    /// Голова ставится на рост человека: <b>без этого камера остаётся там, где лежит в префабе</b>,
    /// и зона спавна (<c>TeamSpawnZone</c> проверяет центр головы) бота не видит — а без зоны
    /// бот не возрождается в новом раунде. Кисти держатся там, где были в префабе
    /// относительно корня, голова медленно поворачивается — чтобы было видно, что бот «жив».
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BotBody : MonoBehaviour
    {
        private const float EyeHeight = 1.65f;
        private const float LookSwayDegrees = 25f;
        private const float LookSwayPeriod = 6f;

        private UxrAvatar _avatar;
        private Transform _head;
        private Transform _leftHand;
        private Transform _rightHand;

        private const float TurnDegreesPerSecond = 240f;

        private bool _handsCaptured;
        private Vector3 _leftHandLocalPosition;
        private Quaternion _leftHandLocalRotation;
        private Vector3 _rightHandLocalPosition;
        private Quaternion _rightHandLocalRotation;
        private float _phase;

        private bool _hasLookTarget;
        private Vector3 _lookTarget;
        private bool _hasRightHandPose;
        private Vector3 _rightHandPosition;
        private Quaternion _rightHandRotation;

        /// <summary>Кость правой кисти — нужна стрелку, чтобы пересчитать хват оружия.</summary>
        public Transform RightHand => _rightHand;

        /// <summary>Поворачивать корпус и голову к точке. null — стоять как стоит.</summary>
        public void LookAt(Vector3? target)
        {
            _hasLookTarget = target.HasValue;
            if (target.HasValue) _lookTarget = target.Value;
        }

        /// <summary>Поставить правую кисть в мировую позу (стрелок целится). Сбрасывается <see cref="ClearRightHandPose"/>.</summary>
        public void SetRightHandPose(Vector3 position, Quaternion rotation)
        {
            _hasRightHandPose = true;
            _rightHandPosition = position;
            _rightHandRotation = rotation;
        }

        public void ClearRightHandPose()
        {
            _hasRightHandPose = false;
        }

        private void Awake()
        {
            _avatar = GetComponent<UxrAvatar>();
            _phase = Random.value * LookSwayPeriod;
        }

        private void OnEnable()
        {
            UxrManager.StageUpdated += OnStageUpdated;
        }

        private void OnDisable()
        {
            UxrManager.StageUpdated -= OnStageUpdated;
        }

        /// <summary>
        /// Поза ставится в конце стадии <see cref="UxrUpdateStage.Update"/>: IK аватара
        /// (<see cref="UxrUpdateStage.PostProcess"/>) ещё впереди и возьмёт её в этом же кадре.
        /// </summary>
        private void OnStageUpdated(UxrUpdateStage stage)
        {
            if (stage != UxrUpdateStage.Update || _avatar == null) return;

            // Камера и кости доступны после инициализации UltimateXR — берутся лениво.
            if (_head == null)      _head      = _avatar.CameraTransform;
            if (_leftHand == null)  _leftHand  = _avatar.GetHandBone(UxrHandSide.Left);
            if (_rightHand == null) _rightHand = _avatar.GetHandBone(UxrHandSide.Right);

            Transform root = transform;
            CaptureHands(root);
            TurnRoot(root);

            if (_head != null)
            {
                Vector3 eye = root.position + root.up * EyeHeight;
                Quaternion headRotation;

                if (_hasLookTarget && (_lookTarget - eye).sqrMagnitude > 0.01f)
                {
                    headRotation = Quaternion.LookRotation(_lookTarget - eye, Vector3.up);
                }
                else
                {
                    float yaw = Mathf.Sin((Time.time + _phase) * (2f * Mathf.PI / LookSwayPeriod)) * LookSwayDegrees;
                    headRotation = root.rotation * Quaternion.Euler(0f, yaw, 0f);
                }

                _head.SetPositionAndRotation(eye, headRotation);
            }

            Place(root, _leftHand, _leftHandLocalPosition, _leftHandLocalRotation);

            if (_hasRightHandPose && _rightHand != null)
                _rightHand.SetPositionAndRotation(_rightHandPosition, _rightHandRotation);
            else
                Place(root, _rightHand, _rightHandLocalPosition, _rightHandLocalRotation);
        }

        /// <summary>Корпус доворачивается к цели по горизонтали. Корень бота рассылает сервер.</summary>
        private void TurnRoot(Transform root)
        {
            if (!_hasLookTarget) return;

            Vector3 flat = _lookTarget - root.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.01f) return;

            Quaternion wanted = Quaternion.LookRotation(flat, Vector3.up);
            root.rotation = Quaternion.RotateTowards(root.rotation, wanted, TurnDegreesPerSecond * Time.deltaTime);
        }

        /// <summary>
        /// Первое положение кистей относительно корня — поза префаба до первого IK.
        /// Держим её дальше, иначе кисти остались бы висеть в мире, пока корень двигается.
        /// </summary>
        private void CaptureHands(Transform root)
        {
            if (_handsCaptured || _leftHand == null || _rightHand == null) return;

            Quaternion inverse = Quaternion.Inverse(root.rotation);
            _leftHandLocalPosition  = root.InverseTransformPoint(_leftHand.position);
            _leftHandLocalRotation  = inverse * _leftHand.rotation;
            _rightHandLocalPosition = root.InverseTransformPoint(_rightHand.position);
            _rightHandLocalRotation = inverse * _rightHand.rotation;
            _handsCaptured = true;
        }

        private void Place(Transform root, Transform target, Vector3 localPosition, Quaternion localRotation)
        {
            if (target == null || !_handsCaptured) return;
            target.SetPositionAndRotation(root.TransformPoint(localPosition), root.rotation * localRotation);
        }
    }
}
