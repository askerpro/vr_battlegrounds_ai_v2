using System;
using UltimateXR.Animation.IK;
using UltimateXR.Avatar.Rig;
using UnityEngine;

namespace UltimateXR.Avatar.Controllers
{
    public sealed partial class UxrStandardAvatarController
    {
        #region Public Types & Data (VR Battlegrounds patch 24)

        /// <summary>
        ///     VR Battlegrounds patch 24: «режим экономии» IK чужих аватаров. Спрашивается в
        ///     <see cref="UpdateAvatarPostProcess" /> только для аватаров в режиме
        ///     <see cref="UxrAvatarMode.UpdateExternally" />: вернул false — тело и руки в этом кадре
        ///     не решаются, кости остаются в позе последнего решения. По умолчанию null — решается
        ///     каждый кадр, как в оригинальном SDK. Локальный аватар хук не видит никогда.
        ///     <para>
        ///         SDK не ссылается на код игры (зависимость обратная), поэтому политику — кто
        ///         невидим, в каком кадре решать, где живёт авторитет — ставит игра при старте
        ///         (<c>RemoteAvatarIKThrottle</c>). Исключение внутри хука не ломает кадр: аватар
        ///         решается, ошибка пишется один раз.
        ///     </para>
        /// </summary>
        public static Func<UxrAvatar, bool> ShouldSolveRemoteAvatarThisFrame { get; set; }

        #endregion

        #region Public Types & Data (VR Battlegrounds patch 34)

        /// <summary>
        ///     VR Battlegrounds patch 34: изгиб корпуса извне (см. <c>UxrBodyIK.ExternalBodyBend</c>). Передаётся в
        ///     решатель тела перед каждым решением; ставить до <c>UxrUpdateStage.PostProcess</c> этого кадра. При ногах из
        ///     клипов (патч 37) его ставит <see cref="UxrAnimatedLegs" />.
        /// </summary>
        public Quaternion ExternalBodyBend { get; set; } = Quaternion.identity;

        /// <summary>
        ///     VR Battlegrounds patch 39: наклон всего корпуса извне (см. <c>UxrBodyIK.ExternalTrunkLean</c>) — наклон игрока по
        ///     шее и наклон, не дающий тазу уйти под пол. Ставит <see cref="UltimateXR.Animation.IK.UxrAnimatedLegs" />.
        /// </summary>
        public Quaternion ExternalTrunkLean { get; set; } = Quaternion.identity;

        #endregion

        #region Public Types & Data (VR Battlegrounds patch 38)

        /// <summary>
        ///     VR Battlegrounds patch 38: оценщик движения игрока (вход — шлем) из IK тела или null (IK тела выключен). Ноги
        ///     берут из него «идёт ли», скорость и направление.
        /// </summary>
        public UxrBodyMotion BodyMotion => _bodyIK != null && _useBodyIK ? _bodyIK.Motion : null;

        #endregion

        #region Inspector Properties/Serialized Fields (VR Battlegrounds patch 35, 37)

        // Отдельный флаг, а не вендорский _useLegIK: тот сериализован как true во всех аватарах (значение по умолчанию
        // SDK, в инспекторе был заглушкой «TBD») — на нём решатель ног включился бы у всех.
        [SerializeField] [Tooltip("Ноги аватара шагают клипами ходьбы: решатель ноги (перенос ноги Final IK) ставит стопы в позы стоп копии рига, которая играет клипы с root motion. Настройки — раздел «Ноги». Выключено — ноги в позе модели.")] private bool _useNativeLegIK;

        // VR Battlegrounds patch 37: настройки ног (решатель, клипы, шаги, таз, наклон корпуса, подошва).
        [SerializeField] private UxrLegsSettings _legs = new UxrLegsSettings();

        #endregion

        #region Public Types & Data (VR Battlegrounds patch 35, 37)

        /// <summary>
        ///     VR Battlegrounds patch 35/37: решаются ли ноги решателем <see cref="UxrLegIKSolver" /> (перенос ноги Final IK).
        ///     Решатели создаются в <c>Awake</c> только при включённом флаге; цели ставит <see cref="AnimatedLegs" /> (клипы
        ///     ходьбы на копии рига) или игра в <see cref="LegIKSolving" />. Без целей (веса 0) ноги не трогаются.
        /// </summary>
        public bool UseNativeLegIK => _useNativeLegIK;

        /// <summary>VR Battlegrounds patch 37: настройки ног (раздел «Ноги» инспектора). Читаются каждый кадр.</summary>
        public UxrLegsSettings Legs => _legs;

        /// <summary>VR Battlegrounds patch 37: ноги из клипов (копия рига, шаги) или null — ноги выключены или без копии рига.</summary>
        public UxrAnimatedLegs AnimatedLegs => _animatedLegs;

        /// <summary>
        ///     VR Battlegrounds patch 37: набор клипов ходьбы (стойка) — 0, 1, 2…; смысл задаёт игра (у VR Battlegrounds: без
        ///     оружия, пистолет, винтовка). Смена сглаживается. Сетью не синхронизируется: каждая машина ставит сама.
        /// </summary>
        public float LegStance
        {
            get => _legStance;
            set
            {
                _legStance = value;

                if (_animatedLegs != null)
                {
                    _animatedLegs.Stance = value;
                }
            }
        }

        /// <summary>VR Battlegrounds patch 35: решатель левой ноги или null.</summary>
        public UxrLegIKSolver LeftLegIK => _leftLegIK;

        /// <summary>VR Battlegrounds patch 35: решатель правой ноги или null.</summary>
        public UxrLegIKSolver RightLegIK => _rightLegIK;

        /// <summary>
        ///     VR Battlegrounds patch 35: поднимается в <c>SolveBodyIK</c> после IK тела (таз и корпус уже стоят за головой
        ///     в этом кадре) и до решения ног и рук; после него цели ставят ноги из клипов (<see cref="AnimatedLegs" />).
        ///     Только при <see cref="UseNativeLegIK" />.
        /// </summary>
        public event Action<UxrStandardAvatarController> LegIKSolving;

        #endregion

        #region Private Methods (VR Battlegrounds patch 35, 37)

        /// <summary>
        ///     VR Battlegrounds patch 35: решатели ног по костям рига аватара. Колено сгибается ВПЕРЁД — нормаль плоскости
        ///     сгиба — ось «вправо» корня аватара, а не из позы префаба (у почти прямых ног в T-позе она случайна).
        ///     Патч 37: ноги из клипов, если задана копия рига.
        /// </summary>
        private void InitializeLegIK()
        {
            _leftLegIK    = null;
            _rightLegIK   = null;
            _animatedLegs = null;

            if (!_useNativeLegIK || Avatar == null || Avatar.AvatarRigType != UxrAvatarRigType.HalfOrFullBody)
            {
                return;
            }

            _leftLegIK  = CreateLegIK(Avatar.AvatarRig.LeftLeg);
            _rightLegIK = CreateLegIK(Avatar.AvatarRig.RightLeg);

            if ((_leftLegIK != null || _rightLegIK != null) && _legs != null && _legs.locomotionRig != null)
            {
                _animatedLegs = new UxrAnimatedLegs(this, _legs, _leftLegIK, _rightLegIK) { Stance = _legStance };
            }
        }

        private UxrLegIKSolver CreateLegIK(UxrAvatarLeg leg)
        {
            var solver = new UxrLegIKSolver();
            solver.Initialize(leg.UpperLeg, leg.LowerLeg, leg.Foot, leg.Toes, Avatar.transform.right);

            if (!solver.Initialized)
            {
                UxrLegsDiagnostics.Warn($"{Avatar.name}: отсутствует бедро, голень или стопа; решатель ноги отключён.", Avatar);
                return null;
            }

            solver.BendToTargetWeight = _legs != null ? _legs.kneeFollowsFoot : 0.5f;
            return solver;
        }

        /// <summary>
        ///     VR Battlegrounds patch 37: стадия PostProcess до IK (для своего и чужих аватаров) — копия рига, Hips в позу
        ///     префаба, пропуск кадра невидимого чужого аватара (патч 24).
        /// </summary>
        private void PrepareLegsForSolve(bool solveThisFrame)
        {
            _animatedLegs?.BeforeSolve(solveThisFrame);
        }

        /// <summary>VR Battlegrounds patch 35/37: цели ног (игра, затем клипы), затем обе ноги.</summary>
        private void SolveLegIK()
        {
            if (!_useNativeLegIK || (_leftLegIK == null && _rightLegIK == null))
            {
                return;
            }

            LegIKSolving?.Invoke(this);
            _animatedLegs?.Solve();
            _leftLegIK?.Solve();
            _rightLegIK?.Solve();
            _animatedLegs?.AfterSolve(); // VR Battlegrounds patch 39: ниже колена — ноги прямо из копии рига
        }

        /// <summary>VR Battlegrounds patch 37: компонент выключен — ноги в позу префаба, наклон корпуса снят.</summary>
        private void DisableLegs()
        {
            _animatedLegs?.Disable();
        }

        /// <summary>VR Battlegrounds patch 37: копия рига уничтожается вместе с аватаром.</summary>
        private void DestroyLegs()
        {
            _animatedLegs?.Destroy();
        }

        #endregion

        #region Private Types & Data (VR Battlegrounds patch 35, 37)

        private UxrLegIKSolver  _leftLegIK;
        private UxrLegIKSolver  _rightLegIK;
        private UxrAnimatedLegs _animatedLegs;
        private float           _legStance;

        #endregion

        #region Private Methods (VR Battlegrounds patch 24)

        /// <summary>
        ///     VR Battlegrounds patch 24: решать ли IK этого аватара в текущем кадре.
        ///     Локальный аватар, аватар без хука и отказ хука — всегда «решать».
        /// </summary>
        private bool ShouldSolveIKThisFrame()
        {
            Func<UxrAvatar, bool> hook   = ShouldSolveRemoteAvatarThisFrame;
            UxrAvatar             avatar = Avatar;

            if (hook == null || avatar == null || avatar.AvatarMode != UxrAvatarMode.UpdateExternally)
            {
                return true;
            }

            try
            {
                return hook(avatar);
            }
            catch (Exception e)
            {
                if (!s_loggedHookError)
                {
                    s_loggedHookError = true;
                    Debug.LogException(e);
                }

                return true;
            }
        }

        #endregion

        #region Private Types & Data (VR Battlegrounds patch 24)

        private static bool s_loggedHookError;
        private float _nextSeatedWarning;

        #endregion
    }
}
