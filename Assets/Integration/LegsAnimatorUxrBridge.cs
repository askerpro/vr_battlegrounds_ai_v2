using FIMSpace.FProceduralAnimation;
using UltimateXR.Avatar;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player.Avatars;

namespace VRBattlegrounds.Integration
{
    /// <summary>
    /// Связка Legs Animator (FImpossible) с аватаром UltimateXR, у рига которого нет анимации.
    ///
    /// <para>
    /// <b>Корень ног.</b> Тело UltimateXR носит <c>Dummy Forward</c>, который висит под камерой
    /// на произвольной высоте. Legs Animator ждёт корень на полу — мост заводит
    /// <c>LegsAnimator_RootAnchor</c>: проекцию <c>Dummy Forward</c> на пол аватара
    /// (<see cref="LegsGrounding.RootAnchor"/>) — и отдаёт его плагину базовым трансформом.
    /// </para>
    ///
    /// <para>
    /// <b>Таз.</b> Плагин в LateUpdate берёт текущую позу таза за «кадр анимации» и ставит таз в
    /// неё плюс свою поправку. Анимации нет — без сброса поправка ложится поверх прошлой: в режиме
    /// Calibrate копится каждый кадр, в FixedCalibrate запекается при каждом выключении/включении
    /// плагина. Таз уезжает вверх, а UltimateXR, держа голову у камеры, вдавливает шею в плечи.
    /// Мост каждый кадр возвращает таз в позу префаба — то, что делала бы анимация.
    /// </para>
    ///
    /// <para>
    /// <b>Пол.</b> Кость стопы — лодыжка, поэтому мост сам бросает луч под каждой ногой и отдаёт
    /// плагину точку пола, поднятую на толщину подошвы.
    /// </para>
    ///
    /// <para>
    /// <b>Движение плагину не подаётся.</b> Анимации ходьбы у аватаров нет: с флагом «идёт»
    /// Legs Animator отпускает ступни, и они скользят вместе с телом. Шаги получаются в режиме
    /// «стоит» — ступня приклеена, пока тело не уйдёт далеко, потом переставляется.
    /// </para>
    ///
    /// <para>
    /// <b>Порядок.</b> У плагина DefaultExecutionOrder -7, у моста 9: Update моста идёт после
    /// Update плагина, но до его LateUpdate, где плагин запоминает позу таза. Плагин и мост в
    /// префабе включены: плагин должен проинициализироваться в позе модели, до первого решения
    /// IK, — иначе запомнит опорную высоту таза из позы под камерой и будет держать таз выше.
    /// Проверка — <c>AvatarLoadoutTests.Legs_Animator_настроен_на_своих_костях</c>.
    /// </para>
    /// </summary>
    [RequireComponent(typeof(LegsAnimator))]
    [DefaultExecutionOrder(9)]
    public class LegsAnimatorUxrBridge : MonoBehaviour
    {
        private const string BodyPivotName = "Dummy Forward";
        private const string RootAnchorName = "LegsAnimator_RootAnchor";

        // Сколько ждать, пока UltimateXR создаст Dummy Forward, прежде чем признать ошибку.
        private const float BodyPivotTimeout = 2f;

        // Луч под ногой: с этой высоты над полом аватара и на эту длину вниз.
        private const float RayStartHeight = 1f;
        private const float RayLength = 2f;

        [Tooltip("Бросать луч под ногами самому и поднимать точку пола на толщину подошвы.")]
        public bool useDynamicFloorOffset = true;

        [Tooltip("Толщина подошвы: от кости стопы (лодыжки) до низа ботинка, метры.")]
        public float footHeightOffset = 0.15f;

        private LegsAnimator _legsAnimator;
        private UxrAvatar _avatar;
        private Transform _hips;
        private BoneLocalPose _hipsPose;
        private Transform _bodyPivot;
        private Transform _rootAnchor;
        private float _bindingStartTime;
        private bool _reportedMissingPivot;

        private void Awake()
        {
            _legsAnimator = GetComponent<LegsAnimator>();
            CaptureHipsPose();
        }

        private void Start()
        {
            _bindingStartTime = Time.time;
        }

        private void Update()
        {
            RestoreHipsPose();

            if (_rootAnchor == null) TryBindRootAnchor();
            UpdateRootAnchor();
        }

        private void LateUpdate()
        {
            // UltimateXR двигает Dummy Forward в своём LateUpdate — догоняем.
            UpdateRootAnchor();

            if (useDynamicFloorOffset && _rootAnchor != null && _legsAnimator.enabled)
                OverrideFloorHits();
        }

        /// <summary>
        /// Запоминает позу таза из префаба. Зовётся из Awake — до того, как плагин или IK
        /// сдвинули кость.
        /// </summary>
        public void CaptureHipsPose()
        {
            _hips = _legsAnimator != null ? _legsAnimator.Hips : null;
            if (_hips != null) _hipsPose = BoneLocalPose.Capture(_hips);
        }

        /// <summary>Возвращает таз в позу префаба. Нужен и при выключенном плагине.</summary>
        public void RestoreHipsPose()
        {
            if (_hips != null) _hipsPose.ApplyTo(_hips);
        }

        private void TryBindRootAnchor()
        {
            // Свой аватар, а не UxrAvatar.LocalAvatar: мост стоит на каждом экземпляре, в том
            // числе на чужих игроках; с LocalAvatar ноги всех аватаров висели на локальном.
            if (_avatar == null) _avatar = GetComponentInParent<UxrAvatar>();
            if (_avatar == null)
            {
                ReportOnce("UxrAvatar в родителях не найден — ноги не привязаны.");
                return;
            }

            // Dummy Forward создаёт UxrBodyIK при инициализации контроллера — ждём его, а не
            // угадываем задержку.
            _bodyPivot = _avatar.transform.Find(BodyPivotName);
            if (_bodyPivot == null)
            {
                if (Time.time - _bindingStartTime > BodyPivotTimeout)
                    ReportOnce($"У аватара '{_avatar.name}' нет {BodyPivotName} — ноги не привязаны.");
                return;
            }

            _rootAnchor = new GameObject(RootAnchorName).transform;
            _rootAnchor.SetParent(_avatar.transform, false);
            UpdateRootAnchor();

            _legsAnimator.Initialize_BaseTransform(_rootAnchor);
            _legsAnimator.enabled = true;

            GameLog.Player.Verbose($"[LegsAnimatorUxrBridge] Корень Legs Animator привязан к '{_avatar.name}'.", this);
        }

        private void UpdateRootAnchor()
        {
            if (_rootAnchor == null || _bodyPivot == null) return;

            Transform avatar = _avatar.transform;
            Pose pose = LegsGrounding.RootAnchor(_bodyPivot.position, _bodyPivot.forward, avatar.position, avatar.up, avatar.forward);
            _rootAnchor.SetPositionAndRotation(pose.position, pose.rotation);
        }

        private void OverrideFloorHits()
        {
            Vector3 up = _avatar.transform.up;

            foreach (LegsAnimator.Leg leg in _legsAnimator.Legs)
            {
                if (leg.BoneEnd == null) continue;

                // Луч строго вниз под ногой, с высоты над полом аватара — не от самой ступни,
                // которая может оказаться под полом.
                Vector3 foot = leg.BoneEnd.position;
                Vector3 origin = foot - up * Vector3.Dot(foot - _rootAnchor.position, up) + up * RayStartHeight;

                if (Physics.Raycast(origin, -up, out RaycastHit hit, RayLength, _legsAnimator.GroundMask, _legsAnimator.RaycastHitTrigger))
                {
                    hit.point = LegsGrounding.SoleContactPoint(hit.point, up, footHeightOffset);
                    leg.User_OverrideRaycastHit(hit, true);
                }
            }
        }

        private void ReportOnce(string message)
        {
            if (_reportedMissingPivot) return;
            _reportedMissingPivot = true;
            GameLog.Player.Error($"[LegsAnimatorUxrBridge] {message}", this);
        }
    }
}
