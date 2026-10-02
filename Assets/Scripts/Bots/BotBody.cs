using UltimateXR.Avatar;
using UltimateXR.Core;
using UnityEngine;

namespace VrBattlegrounds.Bots
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
    /// <b>Бот ходит, как человек в арене (T-48): корень стоит, движется голова.</b> Игроки
    /// перемещаются только физически — корень аватара остаётся на месте калибровки, а по залу идёт
    /// камера, и тело (ноги — Legs Animator) догоняет её. Бот повторяет это: <see cref="Feet"/> —
    /// точка пола под головой, её ведёт <see cref="BotNavigator"/>, голова стоит над ней на росте
    /// человека, кисти — в позе префаба относительно «корпуса» (<see cref="Feet"/> и
    /// <see cref="Yaw"/>). Корень бота не двигается и не поворачивается. Так клиенты видят бота
    /// тем же путём, что удалённого игрока, а зона спавна (<c>TeamSpawnZone</c> проверяет центр
    /// головы) — там, где он стоит.
    /// </para>
    ///
    /// <para>
    /// Тело сменяется (смерть → призрак → возрождение, смена скина) <b>на месте прежнего корня</b>
    /// (<c>AvatarManager.ReplaceBody</c>), а бот ушёл головой далеко от корня. Поэтому новое тело
    /// получает прежние ноги через <see cref="Place"/> от директора (<see cref="BotMind"/>),
    /// иначе призрак погибшего появлялся бы у себя на базе.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BotBody : MonoBehaviour
    {
        private const float EyeHeight = 1.65f;
        private const float LookSwayDegrees = 25f;
        private const float LookSwayPeriod = 6f;
        private const float TurnDegreesPerSecond = 240f;

        private UxrAvatar _avatar;
        private Transform _head;
        private Transform _leftHand;
        private Transform _rightHand;

        private bool _placed;
        private Vector3 _feet;
        private float _yaw;

        private bool _handsCaptured;
        private Vector3 _leftHandLocalPosition;
        private Quaternion _leftHandLocalRotation;
        private Vector3 _rightHandLocalPosition;
        private Quaternion _rightHandLocalRotation;
        private float _phase;

        private bool _hasLookTarget;
        private Vector3 _lookTarget;
        private bool _walking;
        private Vector3 _walkDirection;
        private bool _hasRightHandPose;
        private Vector3 _rightHandPosition;
        private Quaternion _rightHandRotation;

        /// <summary>Кость правой кисти — нужна стрелку, чтобы пересчитать хват оружия.</summary>
        public Transform RightHand => _rightHand;

        /// <summary>Точка пола под головой — где бот стоит. До первого кадра — корень.</summary>
        public Vector3 Feet => _placed ? _feet : transform.position;

        /// <summary>Куда развёрнут корпус, градусы вокруг вертикали.</summary>
        public float Yaw => _placed ? _yaw : transform.eulerAngles.y;

        /// <summary>Поворот корпуса.</summary>
        public Quaternion BodyRotation => Quaternion.Euler(0f, Yaw, 0f);

        /// <summary>Поставить бота: ноги и разворот (новое тело продолжает путь прежнего).</summary>
        public void Place(Vector3 feet, float yaw)
        {
            _placed = true;
            _feet = feet;
            _yaw = yaw;
        }

        /// <summary>Шаг ходьбы: новые ноги и направление движения (корпус доворачивается по ходу, если не целится).</summary>
        public void WalkTo(Vector3 feet, Vector3 direction)
        {
            EnsurePlaced();
            _feet = feet;
            direction.y = 0f;
            _walking = direction.sqrMagnitude > 1e-6f;
            if (_walking) _walkDirection = direction.normalized;
        }

        /// <summary>Остановиться: корпус больше не доворачивается по ходу.</summary>
        public void StopWalking() => _walking = false;

        /// <summary>Поворачивать корпус и голову к точке. null — смотреть по ходу или по сторонам.</summary>
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

        private void EnsurePlaced()
        {
            if (_placed) return;
            Place(transform.position, transform.eulerAngles.y);
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

            EnsurePlaced();
            CaptureHands();
            Turn();

            Quaternion body = BodyRotation;

            if (_head != null)
            {
                Vector3 eye = _feet + Vector3.up * EyeHeight;
                Quaternion headRotation;

                if (_hasLookTarget && (_lookTarget - eye).sqrMagnitude > 0.01f)
                {
                    headRotation = Quaternion.LookRotation(_lookTarget - eye, Vector3.up);
                }
                else if (_walking)
                {
                    headRotation = body;
                }
                else
                {
                    float sway = Mathf.Sin((Time.time + _phase) * (2f * Mathf.PI / LookSwayPeriod)) * LookSwayDegrees;
                    headRotation = body * Quaternion.Euler(0f, sway, 0f);
                }

                _head.SetPositionAndRotation(eye, headRotation);
            }

            Place(body, _leftHand, _leftHandLocalPosition, _leftHandLocalRotation);

            if (_hasRightHandPose && _rightHand != null)
                _rightHand.SetPositionAndRotation(_rightHandPosition, _rightHandRotation);
            else
                Place(body, _rightHand, _rightHandLocalPosition, _rightHandLocalRotation);
        }

        /// <summary>Корпус доворачивается к цели, иначе — по ходу. Корень не трогается.</summary>
        private void Turn()
        {
            Vector3 flat;
            if (_hasLookTarget) flat = _lookTarget - _feet;
            else if (_walking) flat = _walkDirection;
            else return;

            flat.y = 0f;
            if (flat.sqrMagnitude < 0.01f) return;

            float wanted = Quaternion.LookRotation(flat, Vector3.up).eulerAngles.y;
            _yaw = Mathf.MoveTowardsAngle(_yaw, wanted, TurnDegreesPerSecond * Time.deltaTime);
        }

        /// <summary>
        /// Первое положение кистей относительно корня — поза префаба до первого IK. Дальше она
        /// держится относительно корпуса (ноги и разворот), иначе кисти висели бы в мире, пока бот идёт.
        /// </summary>
        private void CaptureHands()
        {
            if (_handsCaptured || _leftHand == null || _rightHand == null) return;

            Transform root = transform;
            Quaternion inverse = Quaternion.Inverse(root.rotation);
            _leftHandLocalPosition  = inverse * (_leftHand.position - root.position);
            _leftHandLocalRotation  = inverse * _leftHand.rotation;
            _rightHandLocalPosition = inverse * (_rightHand.position - root.position);
            _rightHandLocalRotation = inverse * _rightHand.rotation;
            _handsCaptured = true;
        }

        private void Place(Quaternion body, Transform target, Vector3 localPosition, Quaternion localRotation)
        {
            if (target == null || !_handsCaptured) return;
            target.SetPositionAndRotation(_feet + body * localPosition, body * localRotation);
        }
    }
}
