// --------------------------------------------------------------------------------------------------------------------
// VR Battlegrounds patch 37: ноги аватара из клипов ходьбы — решатель ноги (патч 35) + копия рига с root motion.
// --------------------------------------------------------------------------------------------------------------------
using System.Collections.Generic;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Controllers;
using UnityEngine;

namespace UltimateXR.Animation.IK
{
    /// <summary>
    ///     VR Battlegrounds patch 37: ноги аватара UltimateXR, которые шагают клипами. Создаётся и вызывается
    ///     <see cref="UxrStandardAvatarController" /> (при <c>Use Leg IK (native)</c> и заданной копии рига), настройки —
    ///     <see cref="UxrLegsSettings" /> в разделе «Ноги» контроллера.
    ///     <list type="table">
    ///         <item><term>UltimateXR</term><description><c>Dummy Forward</c> за шеей, позвоночник, шея, голова, руки, хваты</description></item>
    ///         <item><term>Копия рига</term><description>клипы ходьбы с root motion; корень ходит за <c>Dummy Forward</c> (<see cref="UxrLegLocomotion" />); из неё — мировые позы стоп и носков, таз и наклон груди</description></item>
    ///         <item><term>Решатель ноги</term><description><see cref="UxrLegIKSolver" /> тянет стопу и носок аватара в позы стоп копии</description></item>
    ///     </list>
    ///     <para>
    ///         <b>Порядок кадра.</b> Анимация Unity: Animator копии играет клип, root motion копится в
    ///         <see cref="UxrLegRootMotionReceiver" />. LateUpdate, стадия PostProcess UltimateXR: Hips — в позу префаба
    ///         (<see cref="BeforeSolve" />), затем <c>SolveBodyIK</c>: BodyIK.PreSolve → <see cref="PrepareCurrentPose" />
    ///         (размещение копии и единая snapshot) → согласование таза/корпуса BodyIK → первый проход рук → BodyIK.PostSolve
    ///         → <see cref="Solve" /> и решатели ног → <see cref="AfterSolve" /> → окончательный проход рук.
    ///     </para>
    ///     <para>
    ///         <b>Стопы стоят</b>, потому что цель — мировая поза стопы клипа, а клип переставляет её, только когда шагает.
    ///         Таз ставит UltimateXR; ноги дотягиваются от него. Корень копии не уходит от опоры дальше
    ///         <see cref="UxrLegLocomotion.maxRootOffset" />.
    ///     </para>
    ///     <para>
    ///         Копия рига — отдельный объект сцены, не дочерний аватару (иначе поиск humanoid-Animator в детях аватара
    ///         находил бы её), не рендерится; кости аватара ищутся по именам — UltimateXR к первому кадру уже перенёс скелет
    ///         под <c>Dummy Forward</c>. Масштаб копии — масштаб <c>Dummy Forward</c> (калибровка роста).
    ///     </para>
    /// </summary>
    public sealed class UxrAnimatedLegs
    {
        #region Public Types & Data

        /// <summary>Сколько наборов клипов (стоек) имеет свою позу покоя для таза и груди: 0, 1, 2.</summary>
        public const int StanceNeutrals = 3;

        /// <summary>Копия рига готова, ноги решаются.</summary>
        public bool IsReady => _ready;

        /// <summary>Идёт ли шаг.</summary>
        public bool IsMoving => _settings.locomotion.IsMoving;

        /// <summary>Animator копии рига (отладка, замеры) или null.</summary>
        public Animator RigAnimator => _rigAnimator;

        public Transform Hips => _hips;
        public Pose CurrentHipsPose => _hipsPose;
        public float CurrentPoseWeight => _poseValid && _canCopyPose && PelvisFromClip ? _poseWeight : 0f;
        public int PoseRevision { get; private set; }

        /// <summary>Набор клипов (стойка): 0, 1, 2…; смена сглаживается (~0,25 с).</summary>
        public float Stance { get; set; }

        #endregion

        #region Constructors & Finalizer

        public UxrAnimatedLegs(UxrStandardAvatarController controller, UxrLegsSettings settings, UxrLegIKSolver left, UxrLegIKSolver right)
        {
            _controller   = controller;
            _avatar       = controller.Avatar;
            _settings     = settings;
            _left.Solver  = left;
            _right.Solver = right;
            _legs         = new[] { _left, _right };
        }

        #endregion

        #region Public Methods

        /// <summary>
        ///     Стадия PostProcess до решения IK: копия появляется при первом кадре (после переноса скелета UltimateXR).
        ///     Невидимый чужой аватар, которого UltimateXR не решает в этом кадре (патч 24), не шагает: копия стоит, после
        ///     пропуска ноги переставляются под тело.
        /// </summary>
        public void BeforeSolve(bool solveThisFrame)
        {
            _poseValid = false;
            if (_rig == null && !_spawnFailed)
            {
                SpawnRig();
            }

            if (_rigAnimator != null)
            {
                _rigAnimator.enabled = solveThisFrame;
            }

            // Hips — в позу префаба до IK тела. Повёрнутый в прошлом кадре (таз клипа на Hips у ригов, где бёдра висят прямо
            // на нём), он унёс бы кости позвоночника, которые IK тела ставит не абсолютно (у MEF — CC_Base_Waist), и грудь
            // отставала бы от головы.
            if (solveThisFrame && _ready && _hips != null)
            {
                _hips.SetLocalPositionAndRotation(_hipsRest.position, _hipsRest.rotation);
                // Отдельный pelvis тоже начинается с нейтральной позы этого кадра: иначе частичный
                // Slerp накапливает результат прошлого кадра и сохраняет поворот после вставания.
                if (_legRoot != null && _legRoot != _hips)
                    _legRoot.SetLocalPositionAndRotation(_legRootRest.position, _legRootRest.rotation);
            }

            if (solveThisFrame && !_solveThisFrame)
            {
                _needsSnap = true;
            }

            _solveThisFrame = solveThisFrame;
        }

        /// <summary>После IK тела, до решения ног и рук: <c>Dummy Forward</c> уже за шеей этого кадра.</summary>
        public void PrepareCurrentPose()
        {
            if (!_ready || !_solveThisFrame)
            {
                SetWeights(0f);
                return;
            }

            if (_bodyPivot == null)
            {
                _bodyPivot = _avatar.transform.Find(BodyPivotName);
            }

            if (_bodyPivot == null)
            {
                SetWeights(0f);
                return;
            }

            UxrLegLocomotion locomotion = _settings.locomotion;
            // VR Battlegrounds patch 38: «идёт ли игрок», скорость и направление — из оценщика по шлему (вход), а не из
            // движения опоры тела или корня копии (выходы IK тела и самих ног). null — по-старому (A/B стенда, IK тела выкл.).
            UxrBodyMotion    motion     = UxrBodyIK.LegacyMovementDecisions ? null : _controller.BodyMotion;
            float            scale      = _bodyPivot.lossyScale.y;
            _rigRoot.localScale = Vector3.one * scale;
            Vector3 up      = _avatar.transform.up;
            Vector3 pivot   = _bodyPivot.position;
            Vector3 forward = Vector3.ProjectOnPlane(_bodyPivot.forward, up);
            if (forward.sqrMagnitude < 1e-6f)
            {
                forward = Vector3.ProjectOnPlane(_avatar.transform.forward, up);
            }

            // Первый кадр, телепорт, возврат после пропуска: корень копии — сразу под опорой, стопы — покой.
            Vector3 gap = Vector3.ProjectOnPlane(pivot - _rigRoot.position, up);
            if (_needsSnap || gap.magnitude > _settings.teleportDistance * scale)
            {
                PlaceRootOnFloor(pivot, forward, up);
                locomotion.Reset(_rigRoot, _rigAnimator);
                _rigAnimator.Play(UxrLegLocomotion.IdleState, 0, 0f);
                _rigAnimator.Update(0f);
                _rootMotion.Consume(out _, out _);
                _left.HasGround = _right.HasGround = false;
                _left.Solver?.ResetContinuity();
                _right.Solver?.ResetContinuity();
                _needsSnap      = false;
            }

            // Веса относятся к уже оценённым костям. Feed ниже пишет только параметры следующего кадра.
            _evaluatedSit = _hasSitParam ? Mathf.Clamp01(_rigAnimator.GetFloat(s_sitParam)) : 0f;
            _poseWeight = Mathf.SmoothStep(0f, 1f, _evaluatedSit);
            _evaluatedStance = CurrentStance;
            Vector3 rootMotion = Vector3.zero;
            if (_evaluatedSit > 0.001f) _rootMotion.Consume(out _, out _);
            else rootMotion = ApplyRootMotion(up);
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);

            // Патч 39: таз и наклон корпуса игрока — по шее из шлема (вход). Наклон двигает шею вперёд, но не ноги: опора шагов
            // — под тазом (шея минус смещение наклона), иначе наклон в полуприседе читался бы как шаг.
            Vector3 neckIn = motion != null && motion.HasData ? motion.Position : _neck != null ? _neck.position : pivot;
            // Направление наклона — по опоре корпуса, а не по взгляду: в приседе игрок смотрит вниз, проекция взгляда на пол
            // мала и прыгает — ось наклона корпуса крутилась вокруг вертикали (скрутка спины, 2026-10-01).
            _pelvis.Update(neckIn, _avatar.transform.position, up, forward, scale, _torsoLength, motion != null ? motion.Speed : 0f, _crouch, MinPelvisHeight(out float minZone), minZone, dt);
            // Патч 39: «идёт ли игрок» для ног — по движению оценки таза, а не шеи: качание корпуса на коленях — не шаг.
            UxrBodyMotion legsMotion = motion != null ? _pelvis.Motion : null;
            Vector3 stepPivot = StepPivot(pivot, scale) - _pelvis.StepOffset;
            if (_evaluatedSit > 0.001f)
            {
                // Качание сидящего корпуса не запускает шаги/повороты и не тянет опору под голову.
                locomotion.Reset(_rigRoot, _rigAnimator);
                _rigAnimator.SetBool(UxrLegLocomotion.IsMovingParam, false);
                _rigAnimator.SetFloat(UxrLegLocomotion.TurnParam, 0f);
            }
            else locomotion.Solve(_rigRoot, _rigAnimator, stepPivot, forward, scale, Time.deltaTime, rootMotion, legsMotion);
            // Следующее сидение — по высоте входной шеи против измеренных stand/sit поз, независимо от kneeling.
            FeedStanceAndCrouch(scale);
            KeepRootOnFloor(up);

            // Все мировые данные читаются после окончательного размещения корня, в одной ревизии.
            _hipsPose = new Pose(_rigHips.position, _rigHips.rotation);
            if (_rigLegRoot != null) _legRootPose = new Pose(_rigLegRoot.position, _rigLegRoot.rotation);
            for (int i = 0; i < _legCopy.Count; i++) _copyPoses[i] = LocalPose(_legCopy[i].rig);
            ReadFoot(_left);
            ReadFoot(_right);
            foreach (LegTargets leg in _legs)
            {
                leg.ThighPos = leg.RigThigh != null ? leg.RigThigh.position : leg.FootPos;
                leg.KneePos = leg.RigKnee != null ? leg.RigKnee.position : leg.FootPos;
                leg.ThighRotation = leg.RigThigh != null ? leg.RigThigh.localRotation : Quaternion.identity;
                leg.KneeRotation = leg.RigKnee != null ? leg.RigKnee.localRotation : Quaternion.identity;
                leg.FootRotation = leg.RigFoot.localRotation;
                leg.ToeRotation = leg.RigToes != null ? leg.RigToes.localRotation : Quaternion.identity;
                PlaceFootOnFloor(leg, up, scale, dt);
            }
            _poseValid = true;
            PoseRevision++;

            // Наклон корпуса — в IK тела следующего кадра (патч 34). Патч 38: только пока игрок идёт (по оценщику) — клипы
            // покоя и поворота на месте корпус не наклоняют: наклон из клипа поворота сдвигал опору тела, и это было одним из
            // звеньев петли «повернул голову → корпус догнал взгляд».
            Quaternion lean = _settings.torsoFromClip > 0f ? TorsoFromClip()
                              : _settings.runLean > 0f     ? UxrLegPoseMath.RunLean(locomotion.BodyVelocity, forward, _settings.runLean) : Quaternion.identity;
            if (motion != null)
            {
                _leanWeight = Mathf.MoveTowards(_leanWeight, legsMotion.IsWalking ? 1f : 0f, Time.deltaTime / LeanFadeTime);
                lean        = Quaternion.Slerp(Quaternion.identity, lean, _leanWeight);
            }

            _lean                        = Quaternion.Slerp(_lean, lean, 1f - Mathf.Exp(-Time.deltaTime / LeanSmoothTime));
            // Патч 39: наклон корпуса игрока (из шеи — вход) — изгиб корпуса аватара: голова в шлеме, таз уходит назад, как у
            // игрока в полуприседе с наклоном. Без него UltimateXR гнёт спину только от наклона самой головы (> 60°), и аватар
            // сидел с прямой спиной под низкой головой. Изгиб — выход, ни одно решение его не читает.
            // Только ВПЕРЁД по опоре корпуса (как moveBodyBackWhenCrouching у VRIK — таз назад вдоль корня): берётся составляющая
            // оценки наклона вдоль «вперёд» опоры. Боковая и задняя составляющие оценки идут от того, где замерло место ног
            // (`_anchor` ловит шею в момент выхода из стойки), — случайный сдвиг шеи вбок в этот момент наклонял корпус вбок, и таз
            // аватара уезжал то вбок, то назад (шлем, 2026-10-01). Оценке они нужны (порог колена), корпусу — нет.
            Quaternion neckLean = Quaternion.identity;
            Vector3    leanDir  = Quaternion.Inverse(_bodyPivot.rotation) * _pelvis.LeanDirection;
            leanDir.y = 0f;
            if (_settings.trunkLeanGain > 0f && leanDir.sqrMagnitude > 1e-8f)
            {
                float forwardLean = _pelvis.LeanAngle * leanDir.normalized.z;
                if (forwardLean > 0f)
                {
                    // Не больше MaxTrunkLean: угол больше 180° у кватерниона — это оборот корпуса вокруг оси (было при усилении 2).
                    neckLean = Quaternion.AngleAxis(Mathf.Min(forwardLean * _settings.trunkLeanGain, MaxTrunkLean), Vector3.right);
                }
            }

            _neckLean                    = Quaternion.Slerp(_neckLean, neckLean, 1f - Mathf.Exp(-Time.deltaTime / LeanSmoothTime));
            _controller.ExternalBodyBend  = _lean;
            // Сидячий корпус согласует BodyIK по текущей snapshot; запаздывающий наклон шеи туда не добавляется.
            _controller.ExternalTrunkLean = _evaluatedSit > 0f ? Quaternion.identity : _neckLean;
        }

        /// <summary>Коррекция размещения тела переносится на всю нижнюю позу, а не на отдельные колени.</summary>
        public void CommitPlacementCorrection(Vector3 delta, bool accepted)
        {
            if (!accepted) _poseWeight = 0f;
            if (!_poseValid || delta.sqrMagnitude == 0f) return;
            _rigRoot.position += delta;
            _hipsPose.position += delta;
            _legRootPose.position += delta;
            foreach (LegTargets leg in _legs)
            {
                leg.FootPos += delta;
                leg.ToePos += delta;
                leg.ThighPos += delta;
                leg.KneePos += delta;
            }
        }

        /// <summary>После согласования тела: цели и twist baseline из сохранённой текущей позы.</summary>
        public void Solve()
        {
            if (!_poseValid) { SetWeights(0f); return; }
            Vector3 up = _avatar.transform.up;
            float scale = _bodyPivot.lossyScale.y;
            if (CurrentPoseWeight > 0f && _legRoot != null && _legRoot != _hips)
                _legRoot.SetPositionAndRotation(Vector3.Lerp(_legRoot.position, _legRootPose.position, CurrentPoseWeight),
                    Quaternion.Slerp(_legRoot.rotation, _legRootPose.rotation, CurrentPoseWeight));
            foreach (LegTargets leg in _legs)
            {
                if (leg.Solver == null) continue;
                leg.Solver.UseBendGoalPosition = false;
                leg.Solver.BendGoalWeight = 0f;
                // Опора на колено не входит в эту итерацию. Обычное сидение задаёт колено из клипа.
                GuardKneeAboveFloor(leg, up, scale);
                KneeFromClipWhenSitting(leg, up, scale);
                leg.Solver.SetAnimationPose(leg.ThighRotation, leg.KneeRotation, leg.FootRotation, leg.ToeRotation, SitWeight);
                SetTargets(leg);
                if (CurrentPoseWeight >= 0.9999f)
                    leg.Solver.PositionWeight = leg.Solver.RotationWeight = 0f;
            }
        }

        /// <summary>Минимум обычного приседа для оценки движения; сидячая поза измеряется независимо.</summary>
        private float MinPelvisHeight(out float zone)
        {
            zone = SquatZone;
            return _settings.minPelvisHeight;
        }

        /// <summary>VR Battlegrounds patch 39: оценка таза и наклона корпуса игрока (отладка, стенд).</summary>
        public UxrPelvisEstimate PelvisEstimate => _pelvis;

        /// <summary>
        ///     Опора шагов — точка под шеей, а не сам <c>Dummy Forward</c>. Наклон корпуса из клипа (патч 34) сдвигает
        ///     <c>Dummy Forward</c> назад, чтобы голова осталась в шлеме; шаги, считающие скорость по нему, видели этот сдвиг как
        ///     торможение — петля «шаг → наклон → опора назад → стоп → наклон снят → опора вперёд → шаг»: на 0,3 м/с ноги
        ///     семенили 2–4 шаг/с по 13 см (стенд, 2026-10-01; без наклона — 0,8 шаг/с по 58 см). Шея идёт за шлемом и
        ///     наклона корпуса не видит; смещение опоры от шеи — как в покое (снято в первом кадре, в осях опоры).
        /// </summary>
        private Vector3 StepPivot(Vector3 pivot, float scale)
        {
            if (_neck == null)
            {
                return pivot;
            }

            if (!_hasPivotFromNeck)
            {
                _pivotFromNeck    = Quaternion.Inverse(_bodyPivot.rotation) * (pivot - _neck.position) / Mathf.Max(scale, 1e-3f);
                _hasPivotFromNeck = true;
            }

            return _neck.position + _bodyPivot.rotation * (_pivotFromNeck * scale);
        }

        /// <summary>Компонент выключен: ноги в позе префаба, наклон корпуса снят.</summary>
        public void Disable()
        {
            _poseValid = false;
            _poseWeight = _sit = _evaluatedSit = _crouch = 0f;
            _solveThisFrame = false;
            SetWeights(0f);
            _controller.ExternalBodyBend = Quaternion.identity;
            _controller.ExternalTrunkLean = Quaternion.identity;
            if (_rigAnimator != null)
            {
                _rigAnimator.enabled = false; // выключенный аватар не тратит кадр на клипы; включит BeforeSolve
                if (_hasSitParam) _rigAnimator.SetFloat(s_sitParam, 0f);
            }

            if (_hips != null) _hips.SetLocalPositionAndRotation(_hipsRest.position, _hipsRest.rotation);
            if (_legRoot != null && _legRoot != _hips)
                _legRoot.SetLocalPositionAndRotation(_legRootRest.position, _legRootRest.rotation);
            foreach (LegTargets leg in _legs)
            {
                leg.HasGround = false;
                leg.Solver?.RestoreRestPose();
            }
            _rootMotion?.Consume(out _, out _);

            _lean                        = Quaternion.identity;
            _neckLean                    = Quaternion.identity;
            _pelvis.Reset();
            _needsSnap                   = true;
        }

        /// <summary>Аватар уничтожен: копия рига — тоже.</summary>
        public void Destroy()
        {
            if (_rig != null)
            {
                Object.Destroy(_rig);
            }

            _rig   = null;
            _ready = false;
        }

        #endregion

        #region Private Methods

        private void SpawnRig()
        {
            _ready = false;
            GameObject prefab = _settings.locomotionRig;
            if (prefab == null)
            {
                _spawnFailed = true;
                UxrLegsDiagnostics.Warn($"{_avatar.name}: не задан locomotion rig ног (Setup Legs).", _avatar);
                return;
            }

            _rig          = Object.Instantiate(prefab);
            // Копия — в сцене аватара (в том числе DontDestroyOnLoad): выгрузка карты не уносит её из-под живого аватара.
            if (_avatar.gameObject.scene.IsValid() && _rig.scene != _avatar.gameObject.scene)
            {
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_rig, _avatar.gameObject.scene);
            }

            _rig.name     = $"{prefab.name} ({_avatar.name})";
            _rigRoot      = _rig.transform;
            _rigAnimator  = _rig.GetComponent<Animator>();
            if (_rigAnimator == null || !_rigAnimator.isHuman)
            {
                _spawnFailed = true;
                UxrLegsDiagnostics.Warn($"'{prefab.name}': нет humanoid Animator на корне копии ног.", _avatar);
                return;
            }

            if (_settings.locomotionController != null)
            {
                _rigAnimator.runtimeAnimatorController = _settings.locomotionController;
            }

            // Root motion перехватывается (UxrLegRootMotionReceiver) и применяется вручную: только горизонталь и рысканье.
            _rigAnimator.applyRootMotion = true;
            _rigAnimator.cullingMode     = AnimatorCullingMode.AlwaysAnimate;
            _rootMotion                  = _rig.GetComponent<UxrLegRootMotionReceiver>();
            if (_rootMotion == null)
            {
                _rootMotion = _rig.AddComponent<UxrLegRootMotionReceiver>();
            }

            _hasStanceParam              = _hasCrouchParam = false;
            foreach (AnimatorControllerParameter p in _rigAnimator.parameters)
            {
                _hasStanceParam |= p.nameHash == s_stanceParam;
                _hasSitParam |= p.nameHash == s_sitParam;
                _hasCrouchParam |= p.nameHash == s_crouchParam;
            }

            // Кости аватара — по именам: копия испечена из этого же префаба, а скелет аватара уже перенесён UltimateXR.
            var avatarBones = new Dictionary<string, Transform>();
            foreach (Transform t in _avatar.GetComponentsInChildren<Transform>(true))
            {
                avatarBones[t.name] = t;
            }

            _neck             = _avatar.AvatarRig.Head.Neck != null ? _avatar.AvatarRig.Head.Neck : _avatar.AvatarRig.Head.Head;
            _hasPivotFromNeck = false;

            _left.RigFoot  = _rigAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
            _left.RigToes  = _rigAnimator.GetBoneTransform(HumanBodyBones.LeftToes);
            _right.RigFoot = _rigAnimator.GetBoneTransform(HumanBodyBones.RightFoot);
            _right.RigToes = _rigAnimator.GetBoneTransform(HumanBodyBones.RightToes);
            // Патч 39: колено клипа (сустав — начало голени) и бедро — для колена на полу.
            _left.RigThigh  = _rigAnimator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
            _left.RigKnee   = _rigAnimator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
            _right.RigThigh = _rigAnimator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
            _right.RigKnee  = _rigAnimator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
            _rigNeck        = _rigAnimator.GetBoneTransform(HumanBodyBones.Neck) ?? _rigAnimator.GetBoneTransform(HumanBodyBones.Head);
            _hasPelvisHeight = false;
            _pelvis.Reset();

            // Подошва — по позе префаба копии: до первой оценки Animator стопы стоят на полу.
            foreach (LegTargets leg in _legs)
            {
                CaptureSole(leg);
            }

            // Таз и грудь клипа — отклонение от покоя того же набора поверх позы префаба.
            _rigHips  = _rigAnimator.GetBoneTransform(HumanBodyBones.Hips);
            _rigChest = _rigAnimator.GetBoneTransform(HumanBodyBones.UpperChest) ?? _rigAnimator.GetBoneTransform(HumanBodyBones.Chest);
            CaptureNeutrals();

            _hips    = null;
            _legRoot = null;
            UxrLegIKSolver any = _left.Solver ?? _right.Solver;
            if (_rigHips != null && any != null && avatarBones.TryGetValue(_rigHips.name, out _hips))
            {
                _hipsRest = LocalPose(_hips);
                Transform pelvis = any.Pelvis;
                if (pelvis == _hips || pelvis.parent == _hips)
                {
                    // Бёдра висят на корне ног (MEF: CC_Base_Pelvis под CC_Base_Hip, киборг: Pelvis под CyborgRig) или прямо
                    // на Hips (Heavy: pelvis) — в обоих случаях таз клипа идёт только на ноги, позвоночник остаётся в позе
                    // IK тела (ApplySeatedPelvis).
                    _legRoot     = pelvis;
                    _legRootRest = LocalPose(pelvis);
                }
                else
                {
                    UxrLegsDiagnostics.Warn($"{_avatar.name}: неподдерживаемый корень ног '{pelvis.name}', Hips '{_hips.name}'.", _avatar);
                }
            }

            // Патч 39: пары костей ног аватар ↔ риг по именам — прямая копия ног ниже колена (AfterSolve).
            _legCopy.Clear();
            _rigLegRoot = null;
            var rigBones = new Dictionary<string, Transform>();
            foreach (Transform t in _rig.GetComponentsInChildren<Transform>(true))
            {
                rigBones[t.name] = t;
            }

            if (_legRoot != null)
            {
                rigBones.TryGetValue(_legRoot.name, out _rigLegRoot);
            }

            foreach (LegTargets leg in _legs)
            {
                if (leg.Solver == null)
                {
                    continue;
                }

                foreach (Transform bone in new[] { leg.Solver.Thigh, leg.Solver.Calf, leg.Solver.Foot, leg.Solver.Toes })
                {
                    if (bone != null && rigBones.TryGetValue(bone.name, out Transform rigBone))
                    {
                        _legCopy.Add((bone, rigBone));
                    }
                }
            }

            _needsSnap = true;
            _copyPoses = new Pose[_legCopy.Count];
            int expected = 0;
            foreach (LegTargets leg in _legs)
                if (leg.Solver != null) expected += leg.Solver.HasToes ? 4 : 3;
            _canCopyPose = _hips != null && _rigHips != null && _legRoot != null && _rigLegRoot != null
                && _left.Solver != null && _right.Solver != null && _legCopy.Count == expected;
            foreach (var pair in _legCopy)
                _canCopyPose &= pair.bone.parent != null && pair.rig.parent != null && pair.bone.parent.name == pair.rig.parent.name;
            _ready     = _left.RigFoot != null && _right.RigFoot != null && _rigHips != null;
            if (_ready && !_canCopyPose) UxrLegsDiagnostics.Warn($"{_avatar.name}: неполная/несовместимая карта позы сидения; копирование отключено.", _avatar);
            if (!_ready)
            {
                _spawnFailed = true;
                UxrLegsDiagnostics.Warn($"'{prefab.name}': неполная карта ног humanoid.", _avatar);
            }
        }

        /// <summary>Измерение стояния и обычного сидения каждой стойки, независимо от kneeling.</summary>
        private void CaptureNeutrals()
        {
            for (int set = 0; set < StanceNeutrals; set++)
            {
                if (_hasStanceParam) _rigAnimator.SetFloat(s_stanceParam, set);
                if (_hasCrouchParam) _rigAnimator.SetFloat(s_crouchParam, 0f);
                if (_hasSitParam) _rigAnimator.SetFloat(s_sitParam, 0f);
                _rigAnimator.Play(UxrLegLocomotion.IdleState, 0, 0f);
                _rigAnimator.Update(0f);
                _hipsNeutral[set] = _rigHips != null ? LocalPose(_rigHips) : default;
                _chestNeutral[set] = _rigChest != null ? Quaternion.Inverse(_rigRoot.rotation) * _rigChest.rotation : Quaternion.identity;
                _hasSitClip[set] = false;
                if (_rigNeck == null || _rigHips == null) continue;
                float scale = Mathf.Max(_rigRoot.lossyScale.y, 1e-3f);
                _standNeck[set] = Vector3.Dot(_rigNeck.position - _rigRoot.position, _rigRoot.up) / scale;
                if (set == 0) _torsoLength = _standNeck[set] - Vector3.Dot(_rigHips.position - _rigRoot.position, _rigRoot.up) / scale;
                if (!_hasSitParam) continue;
                _rigAnimator.SetFloat(s_sitParam, 1f);
                _rigAnimator.Play(UxrLegLocomotion.IdleState, 0, 0f);
                _rigAnimator.Update(0f);
                _sitHips[set] = Vector3.Dot(_rigHips.position - _rigRoot.position, _rigRoot.up) / scale;
                _sitNeck[set] = Vector3.Dot(_rigNeck.position - _rigRoot.position, _rigRoot.up) / scale;
                _hasSitClip[set] = _sitNeck[set] < _standNeck[set] - MinSitDrop;
            }
            _sit = _crouch = 0f;
            if (_hasStanceParam) _rigAnimator.SetFloat(s_stanceParam, Stance);
            if (_hasSitParam) _rigAnimator.SetFloat(s_sitParam, 0f);
            _rigAnimator.Play(UxrLegLocomotion.IdleState, 0, 0f);
            _rigAnimator.Update(0f);
        }

        private float CurrentStance => _hasStanceParam ? _rigAnimator.GetFloat(s_stanceParam) : 0f;

        private Pose HipsNeutral()
        {
            int a = UxrLegPoseMath.StanceSegment(CurrentStance, StanceNeutrals, out int b, out float k);
            return new Pose(Vector3.Lerp(_hipsNeutral[a].position, _hipsNeutral[b].position, k),
                            Quaternion.Slerp(_hipsNeutral[a].rotation, _hipsNeutral[b].rotation, k));
        }

        private Quaternion ChestNeutral()
        {
            int a = UxrLegPoseMath.StanceSegment(CurrentStance, StanceNeutrals, out int b, out float k);
            return Quaternion.Slerp(_chestNeutral[a], _chestNeutral[b], k);
        }

        /// <summary>
        ///     Наклон корпуса клипа: поворот груди копии от её покоя, в осях корня копии (x — вправо, z — вперёд — те же оси,
        ///     что у изгиба патча 34). Наклон вперёд-назад — как в клипе; крен вбок — только переменная часть (клипы с оружием
        ///     на ходу постоянно кренят корпус к оружию, и постоянный крен валил корпус аватара в одну сторону).
        /// </summary>
        private Quaternion TorsoFromClip()
        {
            if (_rigChest == null)
            {
                return Quaternion.identity;
            }

            Quaternion delta = Quaternion.Inverse(_rigRoot.rotation) * _rigChest.rotation * Quaternion.Inverse(ChestNeutral());
            UxrLegPoseMath.SwingTwist(delta, Vector3.up, out Quaternion swing, out Quaternion twist);

            Vector3 bentUp = swing * Vector3.up;
            float   pitch  = Mathf.Atan2(bentUp.z, bentUp.y) * Mathf.Rad2Deg;
            float   roll   = Mathf.Atan2(bentUp.x, bentUp.y) * Mathf.Rad2Deg;
            _rollMean =  Mathf.Lerp(_rollMean, roll, 1f - Mathf.Exp(-Time.deltaTime / RollMeanTime));
            roll      -= _rollMean;

            // Патч 39: наклон назад из клипа (ход спиной) — вчетверо слабее и не больше 2° до усиления: откидка корпуса назад
            // выглядит ошибкой, вперёд — естественно.
            if (pitch < 0f)
            {
                pitch = Mathf.Max(pitch * ClipBackLeanScale, -ClipBackLeanMax);
            }

            float      k    = _settings.torsoFromClip * _settings.torsoBendGain;
            Quaternion bend = Quaternion.Euler(pitch * k, 0f, -roll * k);
            return bend * Quaternion.Slerp(Quaternion.identity, twist, _settings.torsoTwistFromClip * _settings.torsoBendGain);
        }

        /// <summary>Root motion клипа этого кадра — корню копии: сдвиг по горизонтали, поворот — только рысканье.</summary>
        private Vector3 ApplyRootMotion(Vector3 up)
        {
            _rootMotion.Consume(out Vector3 clipDelta, out Quaternion clipRotation);
            Vector3 delta = Vector3.ProjectOnPlane(clipDelta, up);
            _rigRoot.position += delta;

            Vector3 f = Vector3.ProjectOnPlane(clipRotation * _rigRoot.forward, up);
            if (f.sqrMagnitude > 1e-6f)
            {
                _rigRoot.rotation = Quaternion.LookRotation(f, up);
            }

            return delta;
        }

        private static void ReadFoot(LegTargets leg)
        {
            if (leg.RigFoot == null)
            {
                return;
            }

            leg.RigFoot.GetPositionAndRotation(out leg.FootPos, out leg.FootRot);
            if (leg.RigToes != null)
            {
                leg.RigToes.GetPositionAndRotation(out leg.ToePos, out leg.ToeRot);
            }
            else
            {
                leg.ToePos = leg.FootPos;
                leg.ToeRot = leg.FootRot;
            }
        }

        /// <summary>Параметры следующего кадра. Высота tracked neck выбирает обычное сидение без промежуточного колена.</summary>
        private void FeedStanceAndCrouch(float scale)
        {
            if (_hasStanceParam) _rigAnimator.SetFloat(s_stanceParam, Stance, StanceSmoothTime, Time.deltaTime);
            if (_hasCrouchParam) _rigAnimator.SetFloat(s_crouchParam, 0f);
            _crouch = 0f;
            if (!_hasSitParam || _neck == null) return;
            Vector3 neck = _controller.BodyMotion != null && _controller.BodyMotion.HasData
                ? _controller.BodyMotion.Position : _neck.position;
            float height = Vector3.Dot(neck - _avatar.transform.position, _avatar.transform.up) / Mathf.Max(scale, 1e-3f);
            float hyst = Mathf.Max(_settings.crouchHysteresis, 0f);
            if (!_hasPelvisHeight) { _pelvisHeight = height; _hasPelvisHeight = true; }
            else if (height > _pelvisHeight + hyst) _pelvisHeight = height - hyst;
            else if (height < _pelvisHeight - hyst) _pelvisHeight = height + hyst;
            int a = UxrLegPoseMath.StanceSegment(_evaluatedStance, StanceNeutrals, out int b, out float k);
            float aSit = _hasSitClip[a] ? Mathf.InverseLerp(_standNeck[a] - Mathf.Max(_settings.crouchBand, 0.15f), _sitNeck[a] + hyst, _pelvisHeight) : 0f;
            float bSit = _hasSitClip[b] ? Mathf.InverseLerp(_standNeck[b] - Mathf.Max(_settings.crouchBand, 0.15f), _sitNeck[b] + hyst, _pelvisHeight) : 0f;
            float target = Mathf.Lerp(aSit, bSit, k);
            _sit = Mathf.Lerp(_sit, target, 1f - Mathf.Exp(-Time.deltaTime / CrouchSmoothTime));
            if (Mathf.Abs(_sit - target) < 0.0001f) _sit = target;
            _rigAnimator.SetFloat(s_sitParam, _sit);
        }



        /// <summary>
        ///     VR Battlegrounds patch 39: после решателей ног (<c>UxrStandardAvatarController.SolveLegIK</c>). По доле
        ///     <see cref="CurrentPoseWeight" /> ноги переходят с результата решателя на сохранённые локальные позы
        ///     бёдер, голеней, стоп и носков. Корень уже размещён до IK. В полном сидении (доля 1)
        ///     ноги точно как в клипе; решатель там не участвует. Копия рига испечена из этого же
        ///     префаба — кости совпадают по именам.
        /// </summary>
        public void AfterSolve()
        {
            if (!_ready || !_solveThisFrame || _legCopy.Count == 0)
            {
                return;
            }

            float w = CurrentPoseWeight;
            if (w <= 0f)
            {
                return;
            }

            for (int i = 0; i < _legCopy.Count; i++)
            {
                Transform bone = _legCopy[i].bone;
                Pose pose = _copyPoses[i];
                bone.SetLocalPositionAndRotation(Vector3.Lerp(bone.localPosition, pose.position, w), Quaternion.Slerp(bone.localRotation, pose.rotation, w));
            }
        }

        /// <summary>VR Battlegrounds patch 39: A/B стенда — false возвращает таз от головы ниже колена (статический флаг).</summary>
        public static bool PelvisFromClip = true;


        /// <summary>
        ///     Доля таза из клипа: 0 до <c>Legs_Crouch</c> 0,5 (стоя, полуприсед — таз от головы, как прежде), 1 с колена
        ///     (<c>Legs_Crouch</c> ≥ 1) и в сидении; между — плавно (smoothstep), без скачка на границах.
        /// </summary>
        private float PelvisClipWeight => CurrentPoseWeight;

        /// <summary>Доля сидения на полу: 0 при колене и выше, 1 — сидение (<c>Legs_Crouch</c> 1..2).</summary>
        private float SitWeight => _poseValid ? _evaluatedSit : 0f;

        /// <summary>
        ///     VR Battlegrounds patch 39: колено → сидение — плоскость сгиба ноги из колена клипа. Нормаль решателя по умолчанию —
        ///     смесь «вперёд по тазу» и «за поворотом цели стопы» (<see cref="UxrLegsSettings.kneeFollowsFoot" />); у сидения
        ///     стопа подогнутой ноги лежит подошвой вверх, вытянутой — носком вперёд-вбок, нормаль от стопы почти противоположна
        ///     нормали от таза, и их смесь (Slerp почти противоположных векторов) переворачивала плоскость сгиба — колено и бедро
        ///     делали полные обороты (стенд Crouch, 2026-10-01: до 170°/кадр при Legs_Crouch 1,4–2). Здесь колено ведётся к колену
        ///     копии рига (направление бедро → колено клипа от бедра аватара): прежняя точка сгиба (<see cref="KeepKneeOnFloor" />,
        ///     <see cref="GuardKneeAboveFloor" />) смещается к ней по доле сидения.
        /// </summary>
        private void KneeFromClipWhenSitting(LegTargets leg, Vector3 up, float scale)
        {
            UxrLegIKSolver s = leg.Solver;
            float          w = SitWeight;
            if (s == null || w <= 0f || leg.RigKnee == null || leg.RigThigh == null)
            {
                return;
            }

            Vector3 knee = s.Thigh.position + (leg.KneePos - leg.ThighPos);
            // Не ниже пола (как у KeepKneeOnFloor): таз аватара ниже таза сидения (голова ниже) опускает и колено клипа.
            Vector3 floor = _avatar.transform.position;
            float   below = _settings.kneeFloorClearance * scale - Vector3.Dot(knee - floor, up);
            if (below > 0f)
            {
                knee += up * below;
            }

            float prev = s.UseBendGoalPosition ? s.BendGoalWeight : 0f;
            // Прежняя точка (колено на полу, ведение колена решателя) — к колену клипа по доле сидения: в сидении колено клипа.
            s.BendGoalPosition    = prev > 0f ? Vector3.Lerp(s.BendGoalPosition, knee, w) : knee;
            s.UseBendGoalPosition = true;
            s.BendGoalWeight      = Mathf.Max(prev, w);
        }

        /// <summary>
        ///     VR Battlegrounds patch 39: присед без позы клипа на колене (набор без неё или колено клипа не на полу). Колено,
        ///     которое решатель поставил бы ниже <see cref="UxrLegsSettings.kneeFloorClearance" /> + 0,1 м, ведётся вперёд по
        ///     корпусу (точка сгиба — колени не перекрещиваются), а ниже пола — ставится на пол, и стопа уходит назад под таз,
        ///     как у человека, севшего на пятки (колено к полу без провала, но без позы клипа). Без памяти, плавно по высоте.
        /// </summary>
        private void GuardKneeAboveFloor(LegTargets leg, Vector3 up, float scale)
        {
            UxrLegIKSolver s = leg.Solver;
            Vector3 floor = _avatar.transform.position;
            Vector3 thigh = s.Thigh.position;
            float   l1    = s.ThighLength;
            float   l2    = s.CalfLength;
            Vector3 tf    = leg.FootPos - thigh;
            float   d     = tf.magnitude;
            if (d < 1e-4f || l1 <= 0f || l2 <= 0f)
            {
                return;
            }

            // Колено — вперёд и вверх по корпусу (перпендикуляр к бедро→стопа): у глубокого приседа ось ноги почти горизонтальна,
            // и чистое «вперёд» вырождалось — плоскость сгиба прыгала, колени перекрещивались.
            Vector3 axis    = tf / d;
            Vector3 flatFwd = Vector3.ProjectOnPlane(_bodyPivot.forward, up).normalized;
            Vector3 hint    = Vector3.ProjectOnPlane(flatFwd + up, axis);
            if (hint.sqrMagnitude < 1e-6f)
            {
                return;
            }

            hint.Normalize();
            float   dc   = Mathf.Min(d, l1 + l2);
            float   a    = (dc * dc + l1 * l1 - l2 * l2) / (2f * dc);
            Vector3 knee = thigh + axis * a + hint * Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - a * a));

            // Плоскость сгиба ведётся, только когда нога заметно согнута (присед), — шаги ходьбы её не трогают.
            float len = l1 + l2;
            float w   = Mathf.Clamp01((KneeGuardBend * len - d) / (KneeGuardBendFade * len)) * (1f - SitWeight);
            if (w <= 0f)
            {
                return;
            }

            float clear  = _settings.kneeFloorClearance * scale;
            float ky     = Vector3.Dot(knee - floor, up);
            float wFloor = (1f - Mathf.Clamp01((ky - clear) / (KneeGuardFade * scale))) * (1f - SitWeight);
            if (wFloor > 0f)
            {
                // Колено у пола: колено — на пол на длине бедра от бедра (вперёд по корпусу), стопа — назад от колена на длине
                // голени (сел на пятки); плавно по высоте колена.
                float   dyK    = Vector3.Dot(thigh - floor, up) - clear;
                Vector3 kneeOn = dyK >= l1 ? thigh - up * l1 : thigh - up * dyK + flatFwd * Mathf.Sqrt(Mathf.Max(0f, l1 * l1 - dyK * dyK));
                float   footY  = Vector3.Dot(leg.FootPos - floor, up);
                float   dyF    = footY - Vector3.Dot(kneeOn - floor, up);
                Vector3 footOn = kneeOn + up * dyF - flatFwd * Mathf.Sqrt(Mathf.Max(0f, l2 * l2 - dyF * dyF));
                Vector3 flatShift = Vector3.ProjectOnPlane(footOn - leg.FootPos, up) * wFloor;
                leg.FootPos += flatShift;
                leg.ToePos  += flatShift;
                knee         = Vector3.Lerp(knee, kneeOn, wFloor);
            }

            s.BendGoalPosition    = knee;
            s.UseBendGoalPosition = true;
            s.BendGoalWeight      = w;
        }

        private void SetTargets(LegTargets leg)
        {
            if (leg.Solver == null || leg.RigFoot == null)
            {
                return;
            }

            UxrLegIKSolver s = leg.Solver;
            if (s.HasToes && leg.RigToes != null)
            {
                s.TargetPosition     = leg.ToePos;
                s.TargetRotation     = leg.ToeRot;
                s.UseFootTarget      = true;
                s.FootTargetPosition = leg.FootPos;
                s.FootTargetRotation = leg.FootRot;
            }
            else
            {
                s.TargetPosition = leg.FootPos;
                s.TargetRotation = leg.FootRot;
                s.UseFootTarget  = false;
            }

            // Сидение: скрутка бедра за поворотом цели стопы выключается — стопа сидения повёрнута (подошва вверх у подогнутой
            // ноги), и бедро крутилось за ней на пол-оборота за кадр (см. KneeFromClipWhenSitting).
            s.BendToTargetWeight = _settings.kneeFollowsFoot * (1f - SitWeight);
            s.PositionWeight     = _settings.legsWeight;
            s.RotationWeight     = _settings.legsWeight;
        }


        private bool IsThigh(Transform t)
        {
            return (_left.Solver != null && t == _left.Solver.Thigh) || (_right.Solver != null && t == _right.Solver.Thigh);
        }

        /// <summary>
        ///     Точки подошвы в позе префаба копии (стопа на полу): пятка и точка под лодыжкой — в осях кости стопы, под
        ///     пальцами и носок — в осях кости пальцев. Подошва — плоскость на <see cref="UxrLegsSettings.ankleHeight" /> ниже
        ///     кости стопы; вылет пятки назад и носка вперёд — доли высоты лодыжки (ботинок MEF: 0,55 и 0,3).
        /// </summary>
        private void CaptureSole(LegTargets leg)
        {
            leg.SoleInFoot = leg.SoleInToes = null;
            if (leg.RigFoot == null)
            {
                return;
            }

            Vector3 up         = _rigRoot.up;
            float   s          = Mathf.Max(_rigRoot.lossyScale.y, 1e-3f);
            float   h          = _settings.ankleHeight * s;
            Vector3 ankle      = leg.RigFoot.position;
            Vector3 toes       = leg.RigToes != null ? leg.RigToes.position : ankle + _rigRoot.forward * h;
            Vector3 dir        = Vector3.ProjectOnPlane(toes - ankle, up).normalized;
            Vector3 underAnkle = ankle - up * h;
            Vector3 heel       = underAnkle - dir * (0.55f * h);
            Vector3 ball       = toes - up * Vector3.Dot(toes - underAnkle, up);
            Vector3 tip        = ball + dir * (0.3f * h);

            Transform toeBone = leg.RigToes != null ? leg.RigToes : leg.RigFoot;
            leg.SoleInFoot = new[] { ToLocal(leg.RigFoot, heel, s), ToLocal(leg.RigFoot, underAnkle, s) };
            leg.SoleInToes = new[] { ToLocal(toeBone, ball, s), ToLocal(toeBone, tip, s) };
        }

        private static Vector3 ToLocal(Transform bone, Vector3 world, float scale)
        {
            return Quaternion.Inverse(bone.rotation) * (world - bone.position) / scale;
        }

        /// <summary>
        ///     Высота цели стопы: подошва в опоре — на пол. Стопа и пальцы сдвигаются одним телом, только по вертикали;
        ///     поворот стопы — как в клипе. В воздухе поправка не меняется и не даёт подошве уйти в пол.
        /// </summary>
        private void PlaceFootOnFloor(LegTargets leg, Vector3 up, float scale, float dt)
        {
            if (leg.RigFoot == null || leg.SoleInFoot == null)
            {
                return;
            }

            Vector3 floor   = _avatar.transform.position;
            bool    planted = leg.HasGround && Vector3.ProjectOnPlane(leg.FootPos - leg.PrevFootPos, up).magnitude / dt < _settings.plantedSpeed * scale;
            leg.PrevFootPos = leg.FootPos;

            if (!_settings.groundFeet)
            {
                return;
            }

            float g = SoleHeight(leg, floor, up, scale);
            if (!leg.HasGround)
            {
                leg.Ground    = g;
                leg.HasGround = true;
            }
            else if (planted)
            {
                leg.Ground = Mathf.Lerp(leg.Ground, g, 1f - Mathf.Exp(-dt / Mathf.Max(_settings.groundSmoothTime, 1e-3f)));
            }

            // Ниже пола подошва не уходит никогда: поправка не больше текущей высоты подошвы клипа.
            leg.Ground = Mathf.Min(leg.Ground, g);
            Vector3 shift = -up * leg.Ground;
            leg.FootPos += shift;
            leg.ToePos  += shift;
        }

        private static float SoleHeight(LegTargets leg, Vector3 floor, Vector3 up, float scale)
        {
            float min = float.MaxValue;
            foreach (Vector3 p in leg.SoleInFoot)
            {
                min = Mathf.Min(min, Vector3.Dot(leg.FootPos + leg.FootRot * (p * scale) - floor, up));
            }

            foreach (Vector3 p in leg.SoleInToes)
            {
                min = Mathf.Min(min, Vector3.Dot(leg.ToePos + leg.ToeRot * (p * scale) - floor, up));
            }

            return min;
        }

        private void PlaceRootOnFloor(Vector3 pivot, Vector3 forward, Vector3 up)
        {
            Vector3 floor = pivot + Vector3.Project(_avatar.transform.position - pivot, up);
            _rigRoot.SetPositionAndRotation(floor, Quaternion.LookRotation(forward, up));
        }

        /// <summary>Корень копии — на полу аватара и без наклона: root motion двигает только по горизонтали и по рысканью.</summary>
        private void KeepRootOnFloor(Vector3 up)
        {
            Vector3 p = _rigRoot.position;
            p += Vector3.Project(_avatar.transform.position - p, up);
            Vector3 f = Vector3.ProjectOnPlane(_rigRoot.forward, up);
            _rigRoot.SetPositionAndRotation(p, f.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(f, up) : _rigRoot.rotation);
        }

        private void SetWeights(float weight)
        {
            foreach (LegTargets leg in _legs)
            {
                if (leg.Solver == null)
                {
                    continue;
                }

                leg.Solver.PositionWeight = weight;
                leg.Solver.RotationWeight = weight;
            }
        }

        private static Pose LocalPose(Transform t)
        {
            t.GetLocalPositionAndRotation(out Vector3 position, out Quaternion rotation);
            return new Pose(position, rotation);
        }

        #endregion

        #region Private Types & Data

        /// <summary>Кости копии рига, откуда берётся цель ноги.</summary>
        private sealed class LegTargets
        {
            public UxrLegIKSolver Solver;
            public Transform      RigFoot;
            public Transform      RigToes;
            public Vector3        FootPos;
            public Vector3        ToePos;
            public Quaternion     FootRot;
            public Quaternion     ToeRot;

            // Подошва: точки в осях кости стопы и пальцев (без масштаба).
            public Vector3[] SoleInFoot;
            public Vector3[] SoleInToes;
            public float     Ground; // поправка высоты: подошва клипа в опоре над полом, м
            public bool      HasGround;
            public Vector3   PrevFootPos;

            // Патч 39: бедро и колено копии — колено приседа на полу.
            public Transform RigThigh;
            public Transform RigKnee;
            public Vector3 ThighPos, KneePos;
            public Quaternion ThighRotation, KneeRotation, FootRotation, ToeRotation;
        }

        private const string BodyPivotName    = "Dummy Forward";
        private const float  LeanSmoothTime   = 0.12f;
        private const float  LeanFadeTime     = 0.3f; // патч 38: наклон из клипа включается/снимается с ходьбой
        private const float  CrouchSmoothTime = 0.15f;
        private const float  StanceSmoothTime = 0.08f; // смена стойки ~0,25 с
        private const float  RollMeanTime     = 1f;
        private const float  MinCrouchDrop    = 0.15f; // патч 39: шея клипа приседа ниже стоячей меньше этого — приседа в наборе нет
        private const float  ClipBackLeanScale = 0.25f; // патч 39: доля наклона назад из клипа
        private const float  ClipBackLeanMax   = 2f;    // патч 39: наклон назад из клипа до усиления не больше, °
        private const float  KneeGuardFade    = 0.1f;  // патч 39: колено решателя выше пола + зазор на столько — без поправки
        private const float  MaxTrunkLean     = 55f;   // патч 39: наибольший наклон всего корпуса вперёд, ° (с наклоном покоя груди ~17° — до ~70° от вертикали)
        private const float  MinSitDrop       = 0.1f;  // патч 39: таз при Legs_Crouch = 2 ниже таза колена меньше этого — сидения в наборе нет
        private const float  SquatZone        = 0.35f; // патч 39: присед решателем — наклон «таз не ниже минимума» начинается на столько выше минимума
        private const float  KneeGuardBend    = 0.9f;  // патч 39: нога короче доли длины — плоскость сгиба ведётся вперёд-вверх
        private const float  KneeGuardBendFade = 0.2f; // патч 39: за сколько доли длины ноги ведение включается полностью

        private static readonly int s_stanceParam = Animator.StringToHash(UxrLegLocomotion.StanceParam);
        private static readonly int s_crouchParam = Animator.StringToHash(UxrLegLocomotion.CrouchParam);
        private static readonly int s_sitParam = Animator.StringToHash(UxrLegLocomotion.SitParam);

        private readonly UxrStandardAvatarController       _controller;
        private readonly UxrAvatar                         _avatar;
        private readonly UxrLegsSettings                   _settings;
        private readonly LegTargets                        _left  = new LegTargets();
        private readonly LegTargets                        _right = new LegTargets();
        private readonly LegTargets[]                      _legs;
        private readonly Pose[]                            _hipsNeutral  = new Pose[StanceNeutrals];
        private readonly Quaternion[]                      _chestNeutral = new Quaternion[StanceNeutrals];
        private readonly float[]                           _sitHips       = new float[StanceNeutrals];
        private readonly List<(Transform bone, Transform rig)> _legCopy = new List<(Transform, Transform)>(); // патч 39: кости ног аватара ↔ рига
        private Transform                                  _rigLegRoot;
        private readonly bool[]                            _hasSitClip    = new bool[StanceNeutrals];

        private Transform                _bodyPivot;
        private Transform                _neck;
        private Vector3                  _pivotFromNeck;
        private bool                     _hasPivotFromNeck;
        private GameObject               _rig;
        private Animator                 _rigAnimator;
        private Transform                _rigRoot;
        private UxrLegRootMotionReceiver _rootMotion;
        private Transform                _rigHips;
        private Transform                _rigChest;
        private Transform                _hips;
        private Transform                _legRoot;
        private Pose                     _hipsRest;
        private Pose                     _legRootRest;
        private bool                     _hasStanceParam;
        private bool                     _hasCrouchParam;
        private bool _hasSitParam, _poseValid, _canCopyPose;
        private float _sit, _evaluatedSit, _evaluatedStance, _poseWeight;
        private Pose _hipsPose, _legRootPose;
        private Pose[] _copyPoses = new Pose[0];
        private readonly float[] _standNeck = new float[StanceNeutrals];
        private readonly float[] _sitNeck = new float[StanceNeutrals];
        private float                    _crouch;
        private Transform                _rigNeck;
        private float                    _pelvisHeight;
        private bool                     _hasPelvisHeight;
        private float                    _torsoLength = 0.53f;
        private Quaternion               _neckLean    = Quaternion.identity;
        private readonly UxrPelvisEstimate _pelvis    = new UxrPelvisEstimate();
        private float                    _rollMean;
        private Quaternion               _lean           = Quaternion.identity;
        private float                    _leanWeight;
        private bool                     _solveThisFrame = true;
        private bool                     _needsSnap      = true;
        private bool                     _ready;
        private bool                     _spawnFailed;

        #endregion
    }
}
