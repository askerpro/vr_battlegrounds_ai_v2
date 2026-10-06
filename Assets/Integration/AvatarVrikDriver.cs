using System.Collections.Generic;
using RootMotion.FinalIK;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Controllers;
using UltimateXR.Core;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VRBattlegrounds.Integration
{
    /// <summary>
    /// ЭКСПЕРИМЕНТ (T-42, «Эксперимент VRIK»): тело, руки и ноги аватара решает Final IK VRIK, UltimateXR остаётся
    /// источником ввода — шлем (камера), кисти (трекинг, хваты), пальцы.
    ///
    /// <list type="table">
    /// <item><term>UltimateXR</term><description>камера, поза кистей после хватов (стадия Manipulation), позы пальцев; IK тела и рук выключен (<c>_useBodyIK = false</c>, <c>_useArmIK = false</c>)</description></item>
    /// <item><term>VRIK</term><description>корень рига (ходит за головой), таз, позвоночник, шея, голова, руки, ноги и шаги</description></item>
    /// </list>
    ///
    /// <para>
    /// <b>Порядок кадра.</b> Компонент <see cref="VRIK"/> выключен — его собственный LateUpdate шёл бы вне порядка
    /// UltimateXR. Решатель зовётся вручную в <see cref="UxrAvatar.AvatarUpdating"/> стадии
    /// <see cref="UxrUpdateStage.PostProcess"/>: к этому моменту UltimateXR поставил камеру и кисти (трекинг, затем
    /// хваты), а позы пальцев — в стадии Animation. Цели головы и рук берутся из этого состояния, затем
    /// <c>solver.Update()</c>. <c>UpdateSolverExternal()</c> не годится — он пропускает FixTransforms и живёт в цикле
    /// SolverManager.
    /// </para>
    ///
    /// <para>
    /// <b>Голова в шлеме.</b> Цель головы — не сама камера: кость головы ставится так, чтобы середина глаз модели
    /// оказалась ровно в камере, а поворот головы — поворот камеры поверх позы головы в префабе.
    /// </para>
    ///
    /// <para>
    /// <b>Кисти чужого аватара.</b> У чужого (сетевого, стенд) аватара кисти ставятся в Update, а при
    /// <see cref="IKSolverVR.Locomotion.Mode.Animated"/> Animator рига затирает их позой клипа до LateUpdate. Поэтому
    /// позы кистей чужого аватара запоминаются в конце Update (порядок 31000), до Animator. Свой аватар получает
    /// трекинг в LateUpdate UltimateXR — его кисти берутся прямо перед решением.
    /// </para>
    ///
    /// <para>
    /// <b>Корень.</b> <c>references.root</c> VRIK — объект рига (<c>MEF_Rig</c>), а не корень аватара: VRIK двигает
    /// свой корень за головой, а камера — дочерний объект корня аватара; двигать его — петля обратной связи.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(UxrAvatar))]
    [DefaultExecutionOrder(31000)]
    public class AvatarVrikDriver : MonoBehaviour
    {
        [Tooltip("VRIK на объекте рига (humanoid Animator). Компонент выключен — решатель зовёт этот драйвер.")]
        public VRIK vrik;

        [Header("Процедурные шаги (Locomotion = Procedural)")]
        [Tooltip("Подстраивать шаги процедурной локомоции под скорость головы: быстрее идёт — чаще и дальше шаги. Без этого VRIK рассчитан на топтание на месте.")]
        public bool adaptiveSteps = true;

        [Tooltip("Скорость головы, м/с, при которой параметры шагов достигают значений «бег».")]
        public float runSpeed = 3.5f;

        [Tooltip("Скорость шага (stepSpeed VRIK): в покое и на бегу.")]
        public Vector2 stepSpeedRange = new Vector2(3f, 9f);

        [Tooltip("Порог шага, м (stepThreshold VRIK): в покое и на бегу.")]
        public Vector2 stepThresholdRange = new Vector2(0.3f, 0.15f);

        [Tooltip("Предсказание по скорости головы (maxVelocity VRIK, м/с): в покое и на бегу.")]
        public Vector2 maxVelocityRange = new Vector2(0.4f, 3f);

        [Header("Клипы (Locomotion = Animated)")]
        [Tooltip("Клипы локомоции «на месте» (Mixamo In Place) и их скорости, м/с, — пишет утилита Setup VRIK Variant. VRIK подбирает скорость проигрывания по root motion клипа, а у клипов на месте его нет — скорость проигрывания ставит драйвер: скорость головы / скорость текущей смеси клипов.")]
        public AnimationClip[] speedClips;

        [Tooltip("Скорость тела в клипе, м/с (тот же порядок, что speedClips).")]
        public float[] speedClipSpeeds;

        [Tooltip("Пределы скорости проигрывания клипов локомоции.")]
        public Vector2 playbackSpeedRange = new Vector2(0.4f, 2.5f);

        private const float SpeedSmoothTime = 0.15f;
        private static readonly int VrikSpeedParam = Animator.StringToHash("VRIK_Speed");
        private readonly List<AnimatorClipInfo> _clipInfos = new List<AnimatorClipInfo>();

        private UxrAvatar _avatar;
        private UxrStandardAvatarController _controller;
        private Animator _rigAnimator;

        private Transform _headTarget, _leftHandTarget, _rightHandTarget;
        private Transform _leftHandBone, _rightHandBone;
        private Vector3 _leftHandLocalRest, _rightHandLocalRest;   // кисть от предплечья в префабе

        // Пальцы: кости под кистями и их локальные повороты на конец прошлого кадра (поза UltimateXR).
        private readonly List<Transform> _fingers = new List<Transform>();
        private Quaternion[] _fingerPose;
        private bool _fingerPoseValid;

        private Quaternion _headInCamera;   // поворот головы относительно камеры (поза префаба, взгляд вперёд)
        private Vector3 _eyesInHead;        // середина глаз от кости головы, в осях головы

        private Pose _leftHandInput, _rightHandInput;
        private bool _handsFromUpdate;

        private Vector3 _lastHead;
        private float _headSpeed;
        private bool _ready;

        /// <summary>Сглаженная горизонтальная скорость головы, м/с (отладка).</summary>
        public float HeadSpeed => _headSpeed;

        private void Awake()
        {
            _avatar = GetComponent<UxrAvatar>();
            _controller = GetComponent<UxrStandardAvatarController>();

            var targets = new GameObject("VRIK Targets").transform;
            targets.SetParent(transform, false);
            _headTarget = new GameObject("Head").transform;
            _leftHandTarget = new GameObject("LeftHand").transform;
            _rightHandTarget = new GameObject("RightHand").transform;
            _headTarget.SetParent(targets, false);
            _leftHandTarget.SetParent(targets, false);
            _rightHandTarget.SetParent(targets, false);
        }

        private void OnEnable()
        {
            _avatar.AvatarUpdating += OnAvatarUpdating;
            _avatar.AvatarUpdated += OnAvatarUpdated;
        }

        private void OnDisable()
        {
            _avatar.AvatarUpdating -= OnAvatarUpdating;
            _avatar.AvatarUpdated -= OnAvatarUpdated;
        }

        private void Start()
        {
            if (vrik == null)
            {
                GameLog.Player.Error($"[AvatarVrikDriver] У '{name}' не задан VRIK — драйвер выключен (Setup VRIK Variant).", this);
                enabled = false;
                return;
            }

            if (_controller != null && (_controller.UseBodyIK || _controller.UseArmIK))
                GameLog.Player.Warning($"[AvatarVrikDriver] У '{name}' включён IK тела/рук UltimateXR — он будет спорить с VRIK за кости.", this);

            vrik.enabled = false;
            VRIK.References r = vrik.references;
            _rigAnimator = r.root.GetComponent<Animator>();

            // Поза префаба — до первого решения: камера смотрит вперёд по корню аватара.
            _headInCamera = Quaternion.Inverse(transform.rotation) * r.head.rotation;
            _eyesInHead = Quaternion.Inverse(r.head.rotation) * (EyesMidpoint(r.head) - r.head.position);

            _leftHandBone = r.leftHand;
            _rightHandBone = r.rightHand;
            _leftHandLocalRest = _leftHandBone.localPosition;
            _rightHandLocalRest = _rightHandBone.localPosition;
            CollectFingers();

            vrik.solver.spine.headTarget = _headTarget;
            vrik.solver.leftArm.target = _leftHandTarget;
            vrik.solver.rightArm.target = _rightHandTarget;

            if (!vrik.solver.initiated)
            {
                vrik.solver.SetToReferences(r);
                vrik.solver.Initiate(r.root);
                AimKneesForward(vrik.solver.leftLeg, r.root, r.pelvis, r.leftToes != null ? r.leftToes : r.leftFoot);
                AimKneesForward(vrik.solver.rightLeg, r.root, r.pelvis, r.rightToes != null ? r.rightToes : r.rightFoot);
            }

            Transform cam = _avatar.CameraTransform;
            _lastHead = cam != null ? cam.position : transform.position;
            _ready = vrik.solver.initiated && cam != null;
            if (!_ready)
                GameLog.Player.Error($"[AvatarVrikDriver] '{name}': VRIK не инициализирован или нет камеры аватара.", this);
        }

        /// <summary>
        /// Колено гнётся вперёд. VRIK запоминает плоскость сгиба ноги один раз при инициализации — из позы в этот
        /// момент (<c>IKSolverVRLeg</c>: нормаль = бедро→голень × голень→стопа). У MEF в позе префаба колени почти
        /// прямые (сгиб 3°) и развёрнуты — нормаль (0,40; 0; ±0,92) вместо оси «вправо», и на ходу колени уходили
        /// вбок. Колено вперёд — нормаль вдоль правой оси корня рига, одинаково для обеих ног. VRIK хранит её в осях
        /// таза (<c>references.pelvis</c>) и последней кости ноги (стопа/пальцы — доля «сгиб за целью»,
        /// <c>bendToTargetWeight</c>).
        /// </summary>
        private static void AimKneesForward(IKSolverVR.Leg leg, Transform root, Transform pelvis, Transform lastBone)
        {
            Vector3 normal = root.rotation * Vector3.right;
            leg.bendNormalRelToPelvis = Quaternion.Inverse(pelvis.rotation) * normal;
            leg.bendNormalRelToTarget = Quaternion.Inverse(lastBone.rotation) * normal;
        }

        /// <summary>Середина глаз модели: humanoid-кости глаз, иначе 10 см выше и 7 см впереди кости головы.</summary>
        private Vector3 EyesMidpoint(Transform head)
        {
            if (_rigAnimator != null && _rigAnimator.isHuman)
            {
                Transform l = _rigAnimator.GetBoneTransform(HumanBodyBones.LeftEye);
                Transform rEye = _rigAnimator.GetBoneTransform(HumanBodyBones.RightEye);
                if (l != null && rEye != null) return (l.position + rEye.position) * 0.5f;
            }

            return head.position + transform.up * 0.1f + transform.forward * 0.07f;
        }

        /// <summary>Конец Update (порядок 31000): кисти чужого аватара — до того, как Animator рига перепишет кости.</summary>
        private void Update()
        {
            _handsFromUpdate = _ready && _avatar.AvatarMode != UxrAvatarMode.Local && HasController();
            if (_handsFromUpdate) CaptureHands();
        }

        private bool HasController() => _rigAnimator != null && _rigAnimator.runtimeAnimatorController != null;

        private void CaptureHands()
        {
            _leftHandBone.GetPositionAndRotation(out Vector3 lp, out Quaternion lr);
            _rightHandBone.GetPositionAndRotation(out Vector3 rp, out Quaternion rr);
            _leftHandInput = new Pose(lp, lr);
            _rightHandInput = new Pose(rp, rr);
        }

        /// <summary>
        /// Пальцы держат позу UltimateXR между кадрами. UltimateXR ставит позу кисти только при её смене (хват, сжатие),
        /// а дальше рассчитывает, что кости не трогают. Animator рига (клипы локомоции VRIK) каждый кадр пишет пальцы —
        /// даже вне маски гуманоид пишет нейтральную позу мышц («держит шар»), и поза хвата жила один кадр («сжал и
        /// сразу отпустил»). Поэтому: в конце кадра — запомнить пальцы, в стадии AvatarUsingTracking (первая в LateUpdate,
        /// Animator уже отыграл; стадия Update идёт раньше Animator) — вернуть; новую позу UltimateXR поставит поверх в стадии Animation.
        /// </summary>
        private void CollectFingers()
        {
            // Только гуманоидные кости пальцев: под кистью живут и захватчик UltimateXR, и взятые предметы.
            _fingers.Clear();
            if (_rigAnimator != null && _rigAnimator.isHuman)
            {
                for (var bone = HumanBodyBones.LeftThumbProximal; bone <= HumanBodyBones.RightLittleDistal; bone++)
                {
                    Transform t = _rigAnimator.GetBoneTransform(bone);
                    if (t != null) _fingers.Add(t);
                }
            }
            _fingerPose = new Quaternion[_fingers.Count];
            _fingerPoseValid = false;
        }

        private void RestoreFingers()
        {
            if (!_fingerPoseValid || !HasController()) return;
            for (int i = 0; i < _fingers.Count; i++)
                _fingers[i].localRotation = _fingerPose[i];
        }

        private void OnAvatarUpdated(object sender, UxrAvatarUpdateEventArgs e)
        {
            if (e.UpdateStage != UxrUpdateStage.PostProcess || !_ready) return;
            for (int i = 0; i < _fingers.Count; i++)
                _fingerPose[i] = _fingers[i].localRotation;
            _fingerPoseValid = true;
        }

        private void OnAvatarUpdating(object sender, UxrAvatarUpdateEventArgs e)
        {
            // Первая стадия LateUpdate UltimateXR — Animator рига уже отыграл (стадия Update идёт в Update(), до него).
            if (e.UpdateStage == UxrUpdateStage.AvatarUsingTracking && _ready)
            {
                RestoreFingers();
                // Кисти чужого аватара — в позу ввода до стадии Manipulation: Animator рига только что положил их в позу
                // клипа (у бёдер), и хват UltimateXR ставил взятый предмет туда, а VRIK потом уводил кисть к цели без него.
                if (_handsFromUpdate)
                {
                    _leftHandBone.SetPositionAndRotation(_leftHandInput.position, _leftHandInput.rotation);
                    _rightHandBone.SetPositionAndRotation(_rightHandInput.position, _rightHandInput.rotation);
                }
            }
            if (e.UpdateStage != UxrUpdateStage.PostProcess || !_ready) return;
            if (!ShouldSolveThisFrame()) return;

            // Кисти — ввод UltimateXR (трекинг и хваты). Запомнить до FixTransforms, который вернёт кости в позу префаба.
            if (!_handsFromUpdate) CaptureHands();

            Transform cam = _avatar.CameraTransform;
            Quaternion headRotation = cam.rotation * _headInCamera;
            _headTarget.SetPositionAndRotation(cam.position - headRotation * _eyesInHead, headRotation);
            _leftHandTarget.SetPositionAndRotation(_leftHandInput.position, _leftHandInput.rotation);
            _rightHandTarget.SetPositionAndRotation(_rightHandInput.position, _rightHandInput.rotation);

            AdaptSteps(cam.position);

            // Трекинг UltimateXR ставит кисть в мировую позу контроллера, не трогая предплечье: кисть отъезжает от
            // локтя (замер: 0,36 и 0,24 м вместо 0,27). VRIK читает длины костей из текущей позы и решил бы руку
            // с этим «резиновым» предплечьем. Поза кисти уже запомнена как цель — кость возвращается на длину префаба.
            _leftHandBone.localPosition = _leftHandLocalRest;
            _rightHandBone.localPosition = _rightHandLocalRest;

            // Без контроллера анимации кости никто не сбрасывает — решение накапливалось бы из кадра в кадр.
            if (!HasController()) vrik.solver.FixTransforms();
            vrik.solver.Update();
            MatchClipSpeed();
        }

        /// <summary>
        /// Скорость проигрывания клипов на месте: VRIK меряет её по root motion (у клипов In Place его нет — выходил
        /// потолок 3×), поэтому после решения параметр <c>VRIK_Speed</c> переписывается: скорость головы / скорость
        /// смеси клипов, которые сейчас играет Animator. Animator возьмёт его на следующей оценке.
        /// </summary>
        private void MatchClipSpeed()
        {
            if (vrik.solver.locomotion.mode != IKSolverVR.Locomotion.Mode.Animated || !HasController()) return;
            if (speedClips == null || speedClipSpeeds == null || speedClips.Length != speedClipSpeeds.Length) return;

            _rigAnimator.GetCurrentAnimatorClipInfo(0, _clipInfos);
            float expected = 0f, weight = 0f;
            foreach (AnimatorClipInfo info in _clipInfos)
            {
                int i = System.Array.IndexOf(speedClips, info.clip);
                if (i < 0) continue;
                expected += speedClipSpeeds[i] * info.weight;
                weight += info.weight;
            }

            // Idle, поворот на месте или переход — скорость клипов не трогаем.
            if (weight < 0.5f || expected < 0.05f) return;
            expected /= weight;
            _rigAnimator.SetFloat(VrikSpeedParam, Mathf.Clamp(_headSpeed / expected, playbackSpeedRange.x, playbackSpeedRange.y));
        }

        /// <summary>
        /// Процедурные шаги VRIK рассчитаны на топтание на месте: на бегу стопы не успевают за телом. Скорость, порог и
        /// предсказание шага растут со скоростью головы.
        /// </summary>
        private void AdaptSteps(Vector3 head)
        {
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 delta = head - _lastHead;
            delta.y = 0f;
            _lastHead = head;
            float speed = delta.magnitude / dt;
            _headSpeed = Mathf.Lerp(_headSpeed, speed, 1f - Mathf.Exp(-dt / SpeedSmoothTime));

            IKSolverVR.Locomotion loco = vrik.solver.locomotion;
            if (!adaptiveSteps || loco.mode != IKSolverVR.Locomotion.Mode.Procedural) return;

            float k = Mathf.Clamp01(_headSpeed / Mathf.Max(runSpeed, 0.1f));
            loco.stepSpeed = Mathf.Lerp(stepSpeedRange.x, stepSpeedRange.y, k);
            loco.stepThreshold = Mathf.Lerp(stepThresholdRange.x, stepThresholdRange.y, k);
            loco.maxVelocity = Mathf.Lerp(maxVelocityRange.x, maxVelocityRange.y, k);
        }

        /// <summary>Невидимый чужой аватар, которого UltimateXR не решает в этом кадре (патч 24), не решается и VRIK.</summary>
        private bool ShouldSolveThisFrame()
        {
            if (_avatar.AvatarMode != UxrAvatarMode.UpdateExternally) return true;

            var hook = UxrStandardAvatarController.ShouldSolveRemoteAvatarThisFrame;
            return hook == null || hook(_avatar);
        }
    }
}
