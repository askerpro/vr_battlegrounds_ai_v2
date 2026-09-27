using UnityEngine;
using UltimateXR.Avatar;
using FIMSpace.FProceduralAnimation;
using VrBattlegrounds.Core;

namespace VRBattlegrounds.Integration
{
    [RequireComponent(typeof(LegsAnimator))]
    [DefaultExecutionOrder(9)] // Выполняется перед основным апдейтом LegsAnimator (у которого мы задали 10)
    public class LegsAnimatorUxrBridge : MonoBehaviour
    {
        private LegsAnimator _legsAnimator;
        private UxrAvatar _avatar;
        private Transform _dummyForward;
        private Transform _legsAnimatorRoot;
        private Vector3 _lastRootAnchorPosition;
        private bool _hasLastRootAnchorPosition;

        [Header("Floor Alignment Fix")]
        [Tooltip("Включить программное смещение луча вместо использования физического фиктивного пола.")]
        public bool useDynamicFloorOffset = true;
        
        [Tooltip("Отступ по вертикали от лодыжки до подошвы. 0.15 = 15см (полезно для Heavy Soldier обуви).")]
        public float footHeightOffset = 0.15f;

        [Header("Movement Feed")]
        [Tooltip("Минимальная горизонтальная скорость (м/с), после которой Legs Animator считается в движении.")]
        public float movementThreshold = 0.025f;

        [Tooltip("Сглаживание подаваемого вектора движения (0 = без сглаживания, 1 = сильное сглаживание).")]
        [Range(0f, 1f)]
        public float movementSmoothing = 0.1f;

        private Vector3 _smoothedVelocity;

        private void Start()
        {
            _legsAnimator = GetComponent<LegsAnimator>();

            // Wait shortly to ensure UltimateXR has completed UxrBodyIK initialization 
            // and instantiated the "Dummy Forward" object.
            Invoke(nameof(InitializeBridge), 0.1f);
        }

        private void InitializeBridge()
        {
            // Свой аватар, а не UxrAvatar.LocalAvatar: мост стоит на каждом экземпляре, в том
            // числе на чужих игроках. С LocalAvatar корень ног всех аватаров вешался под
            // локального, и после смены скина (локальный уничтожен) их LegsAnimator сыпал
            // MissingReferenceException на уничтоженных трансформах.
            _avatar = GetComponentInParent<UxrAvatar>();
            if (_avatar == null)
            {
                GameLog.Player.Warning("[LegsAnimatorUxrBridge] UxrAvatar в родителях не найден.", this);
                return;
            }

            // Find the dynamically created anchor by UltimateXR
            _dummyForward = _avatar.transform.Find("Dummy Forward");

            if (_dummyForward != null)
            {
                // Create a custom root for LegsAnimator that follows Dummy Forward in XZ, but stays on the ground Y
                GameObject rootObj = new GameObject("LegsAnimator_RootAnchor");
                rootObj.transform.SetParent(_avatar.transform);
                _legsAnimatorRoot = rootObj.transform;

                UpdateRootAnchor();
                _lastRootAnchorPosition = _legsAnimatorRoot.position;
                _hasLastRootAnchorPosition = true;

                // Dynamically reassign the Base Transform!
                _legsAnimator.Initialize_BaseTransform(_legsAnimatorRoot);

                // Enable the component so that its Start() and Initialize() run naturally with the correct Base Transform.
                _legsAnimator.enabled = true;

                GameLog.Player.Verbose($"[LegsAnimatorUxrBridge] Корень Legs Animator привязан к '{_avatar.name}'.", this);
            }
            else
            {
                GameLog.Player.Error($"[LegsAnimatorUxrBridge] У аватара '{_avatar.name}' нет Dummy Forward.", this);
            }
        }

        private void Update()
        {
            UpdateRootAnchor();
        }

        private void LateUpdate()
        {
            UpdateRootAnchor();
            FeedMovementState();
            
            if (useDynamicFloorOffset && _legsAnimator != null && _legsAnimator.enabled)
            {
                ApplyDynamicFloorOverrides();
            }
        }

        private void FeedMovementState()
        {
            if (_legsAnimatorRoot == null || _legsAnimator == null || !_legsAnimator.enabled)
            {
                return;
            }

            if (!_hasLastRootAnchorPosition)
            {
                _lastRootAnchorPosition = _legsAnimatorRoot.position;
                _hasLastRootAnchorPosition = true;
                return;
            }

            float deltaTime = Mathf.Max(Time.deltaTime, 0.0001f);
            Vector3 rawVelocity = (_legsAnimatorRoot.position - _lastRootAnchorPosition) / deltaTime;
            rawVelocity.y = 0f;

            float lerpFactor = 1f - movementSmoothing;
            _smoothedVelocity = Vector3.Lerp(_smoothedVelocity, rawVelocity, lerpFactor);

            _legsAnimator.User_SetDesiredMovementDirection(_smoothedVelocity, true);
            _legsAnimator.User_SetIsMoving(_smoothedVelocity.sqrMagnitude > movementThreshold * movementThreshold);

            _lastRootAnchorPosition = _legsAnimatorRoot.position;
        }

        private void UpdateRootAnchor()
        {
            if (_dummyForward != null && _legsAnimatorRoot != null && _avatar != null)
            {
                // Maintain Dummy Forward's X and Z, but keep Y at the Avatar's root Y (floor level)
                _legsAnimatorRoot.position = new Vector3(_dummyForward.position.x, _avatar.transform.position.y, _dummyForward.position.z);
                _legsAnimatorRoot.rotation = _dummyForward.rotation;
            }
        }

        private void ApplyDynamicFloorOverrides()
        {
            if (_legsAnimator.Legs == null || _legsAnimatorRoot == null) return;

            foreach(var leg in _legsAnimator.Legs)
            {
                if (leg.BoneEnd == null) continue;

                // Пускаем луч вертикально вниз с безопасной высоты (на 1м выше корня персонажа),
                // но именно в тех XZ-координатах, где находится нога персонажа.
                Vector3 footPosXZ = new Vector3(leg.BoneEnd.position.x, _legsAnimatorRoot.position.y + 1.0f, leg.BoneEnd.position.z);
                
                if (Physics.Raycast(footPosXZ, Vector3.down, out RaycastHit hit, 2.0f, _legsAnimator.GroundMask, _legsAnimator.RaycastHitTrigger))
                {
                    // Искусственно завышаем точку попадания, учитывая толщину подошвы
                    hit.point += Vector3.up * footHeightOffset;
                    
                    // Передаем этот хит в Legs Animator и отключаем его внутренний рейкаст для этой ноги (disableSourceRaycast = true)
                    leg.User_OverrideRaycastHit(hit, true);
                }
            }
            
            // Синхронизируем флаг заземления для всего контроллера (если хотя бы 1 луч достал до земли)
            // Но в целом LegsAnimator сам определит IsGrounded по нашему переданному hit.
        }
    }
}
