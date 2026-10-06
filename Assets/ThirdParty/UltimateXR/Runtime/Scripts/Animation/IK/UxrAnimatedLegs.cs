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
    ///         <item><term>UltimateXR</term><description>таз, позвоночник, шея, голова, руки, хваты — без изменений (BodyIK)</description></item>
    ///         <item><term>Копия рига</term><description>Animator играет клипы стоек, хода и поворотов с root motion; корень ходит за <c>Dummy Forward</c> (<see cref="UxrLegLocomotion" />); из неё — мировые позы стоп и носков</description></item>
    ///         <item><term>Решатель ноги</term><description><see cref="UxrLegIKSolver" /> тянет стопу и носок аватара в позы стоп копии</description></item>
    ///     </list>
    ///     <para>
    ///         Только ноги ниже таза: таз, корпус и их наклоны ведёт BodyIK UltimateXR, ноги ничего в нём не меняют.
    ///         Присед и сидение — клипами (<c>Legs_Crouch</c>: 0 стоя, 1 колено, 2 сидя) по опусканию камеры относительно
    ///         роста самого игрока; при приседе ноги ниже таза берутся из клипа (<see cref="AfterSolve" />), без решателя.
    ///         Оценки таза игрока и наклонов корпуса нет.
    ///     </para>
    ///     <para>
    ///         <b>Порядок кадра.</b> Анимация Unity: Animator копии играет клип, root motion копится в
    ///         <see cref="UxrLegRootMotionReceiver" />. LateUpdate, стадия PostProcess UltimateXR: IK тела → <see cref="Solve" />
    ///         (цели стоп из копии, шаг локомоции — корень копии и параметры клипов на следующий кадр) → решатели ног → IK рук.
    ///     </para>
    ///     <para>
    ///         Копия рига — отдельный объект сцены, не дочерний аватару (иначе поиск humanoid-Animator в детях аватара
    ///         находил бы её), не рендерится. Масштаб копии — масштаб <c>Dummy Forward</c> (калибровка роста).
    ///     </para>
    /// </summary>
    public sealed class UxrAnimatedLegs
    {
        #region Public Types & Data

        /// <summary>Копия рига готова, ноги решаются.</summary>
        public bool IsReady => _ready;

        /// <summary>Идёт ли шаг.</summary>
        public bool IsMoving => _settings.locomotion.IsMoving;

        /// <summary>Animator копии рига (отладка, замеры) или null.</summary>
        public Animator RigAnimator => _rigAnimator;

        /// <summary>Оценщик «идёт ли игрок» по шлему (вход) — для шагов.</summary>
        public UxrBodyMotion Motion => _motion;

        /// <summary>Набор клипов (стойка): 0, 1, 2…; смена сглаживается (~0,25 с).</summary>
        public float Stance { get; set; }

        /// <summary>Присед: 0 — стоя, 1 — на колене, 2 — сидя (сглаженный, тот, что подан в Animator).</summary>
        public float Crouch => _crouch;

        /// <summary>
        ///     Отладка: цель <c>Legs_Crouch</c> вручную вместо высоты головы (сглаживание и всё дальнейшее — как обычно).
        ///     null — по голове. Стенды и отладочная панель ног.
        /// </summary>
        public float? CrouchOverride { get; set; }

        /// <summary>Снимок решения приседа этого кадра — для отладочной панели ног.</summary>
        public CrouchDebugState CrouchDebug => _crouchDebug;

        /// <summary>Что решил присед в этом кадре: от высоты камеры до доли ног из клипа.</summary>
        public struct CrouchDebugState
        {
            public bool  Valid;          // уровни клипов измерены, параметр Legs_Crouch есть
            public float CameraHeight;   // камера над полом аватара, м
            public float StandRef;       // рост игрока стоя (наибольшая высота камеры), м
            public float Ratio;          // CameraHeight / StandRef
            public float DeadZoneRatio;  // выше — стоя
            public float KneelRatio;     // доля роста, где клип колена (Legs_Crouch 1)
            public float SitRatio;       // доля роста, где клип сидения (Legs_Crouch 2)
            public bool  HasSit;
            public float Target;         // цель Legs_Crouch по голове (или ручная)
            public bool  Overridden;     // цель задана вручную
            public float Crouch;         // сглаженный Legs_Crouch в Animator
            public bool  StepsOff;       // глубже порога: шагов нет, root motion выброшен
            public float ClipLegsWeight; // доля локальной позы клипа в ногах ниже таза (AfterSolve)
        }

        /// <summary>
        ///     Цель <c>Legs_Crouch</c> по доле роста <paramref name="ratio" /> (камера / рост стоя): до 1 − deadZone — 0, к доле
        ///     колена — линейно до 1, к доле сидения — линейно до 2. Единственная формула: по ней решают ноги и рисует график панель.
        /// </summary>
        public static float CrouchFromRatio(float ratio, float deadZone, float kneelRatio, float sitRatio, bool hasSit)
        {
            if (ratio >= 1f - deadZone) return 0f;
            if (ratio >= kneelRatio) return Mathf.InverseLerp(1f - deadZone, kneelRatio, ratio);
            return hasSit ? 1f + Mathf.InverseLerp(kneelRatio, sitRatio, ratio) : 1f;
        }

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
            if (_rig == null && !_spawnFailed)
            {
                SpawnRig();
            }

            if (_rigAnimator != null)
            {
                _rigAnimator.enabled = solveThisFrame;
            }

            if (solveThisFrame && !_solveThisFrame)
            {
                _needsSnap = true;
            }

            _solveThisFrame = solveThisFrame;
        }

        /// <summary>После IK тела, до решения ног и рук: <c>Dummy Forward</c> уже за шеей этого кадра.</summary>
        public void Solve()
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
            float            scale      = _bodyPivot.lossyScale.y;
            float            dt         = Mathf.Max(Time.deltaTime, 1e-4f);
            _rigRoot.localScale = Vector3.one * scale;
            Vector3 up      = _avatar.transform.up;
            Vector3 pivot   = _bodyPivot.position;
            Vector3 forward = Vector3.ProjectOnPlane(_bodyPivot.forward, up);
            if (forward.sqrMagnitude < 1e-6f)
            {
                forward = Vector3.ProjectOnPlane(_avatar.transform.forward, up);
            }

            // «Идёт ли игрок» — по шлему (вход), а не по опоре тела или корню копии (выходы IK тела и самих ног).
            Camera camera = _avatar.CameraComponent;
            if (camera != null)
            {
                _motion.Update(camera.transform.position, up, Time.time);
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
                _motion.Reset();
                _left.HasGround = _right.HasGround = false;
                _needsSnap      = false;
            }

            UpdateCrouch(camera, up, scale, dt);
            bool crouched = _crouch > _settings.locomotionCrouchLimit;
            _crouchDebug.StepsOff = crouched;
            Vector3 rootMotion = Vector3.zero;
            if (crouched) _rootMotion.Consume(out _, out _);
            else rootMotion = ApplyRootMotion(up);

            // Цели — поза стоп клипа ДО шага локомоции этого кадра (как VRIK: ноги читаются до правки корня).
            foreach (LegTargets leg in _legs)
            {
                ReadFoot(leg);
                PlaceFootOnFloor(leg, up, scale, dt);
                SetTargets(leg);
            }

            // Позы ног клипа этого кадра — для AfterSolve (присед, сидение).
            for (int i = 0; i < _legCopy.Count; i++)
            {
                _legCopy[i].rig.GetLocalPositionAndRotation(out Vector3 lp, out Quaternion lr);
                _copyPoses[i] = new Pose(lp, lr);
            }

            if (crouched)
            {
                // На колене и сидя покачивание корпуса не запускает шаги и повороты.
                locomotion.Reset(_rigRoot, _rigAnimator);
            }
            else
            {
                locomotion.Solve(_rigRoot, _rigAnimator, pivot, forward, scale, Time.deltaTime, rootMotion, camera != null ? _motion : null);
            }
            if (_hasStanceParam)
            {
                _rigAnimator.SetFloat(s_stanceParam, Stance, StanceSmoothTime, Time.deltaTime);
            }

            KeepRootOnFloor(up);
        }

        /// <summary>
        ///     После решателей ног: на колене и сидя ноги ниже таза — локальные позы костей клипа (смешивание от
        ///     <see cref="UxrLegsSettings.clipLegsStart" /> до 1 по <c>Legs_Crouch</c>). Решатель в сжатой ноге сидения крутил колено; клип
        ///     держит позу целиком. Таз и корпус не трогаются.
        /// </summary>
        public void AfterSolve()
        {
            _crouchDebug.ClipLegsWeight = 0f;
            if (!_ready || !_solveThisFrame || _legCopy.Count == 0)
            {
                return;
            }

            float w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(_settings.clipLegsStart, 1f, _crouch));
            _crouchDebug.ClipLegsWeight = w;
            if (w <= 0f)
            {
                return;
            }

            for (int i = 0; i < _legCopy.Count; i++)
            {
                Transform bone = _legCopy[i].bone;
                Pose      pose = _copyPoses[i];
                bone.SetLocalPositionAndRotation(Vector3.Lerp(bone.localPosition, pose.position, w), Quaternion.Slerp(bone.localRotation, pose.rotation, w));
            }
        }

        /// <summary>Компонент выключен: ноги в позе префаба.</summary>
        public void Disable()
        {
            SetWeights(0f);
            _crouch      = 0f;
            _hasStandRef = false;
            if (_rigAnimator != null && _hasCrouchParam) _rigAnimator.SetFloat(s_crouchParam, 0f);
            if (_rigAnimator != null)
            {
                _rigAnimator.enabled = false; // выключенный аватар не тратит кадр на клипы; включит BeforeSolve
            }

            _motion.Reset();
            _needsSnap = true;
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
                UxrLegsDiagnostics.Warn($"{_avatar.name}: нет копии рига в настройках ног — ноги в позе префаба (Tools/VR Battlegrounds/Avatars/Setup Legs).", _avatar);
                return;
            }

            _rig = Object.Instantiate(prefab);
            // Копия — в сцене аватара (в том числе DontDestroyOnLoad): выгрузка карты не уносит её из-под живого аватара.
            if (_avatar.gameObject.scene.IsValid() && _rig.scene != _avatar.gameObject.scene)
            {
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(_rig, _avatar.gameObject.scene);
            }

            _rig.name    = $"{prefab.name} ({_avatar.name})";
            _rigRoot     = _rig.transform;
            _rigAnimator = _rig.GetComponent<Animator>();
            if (_rigAnimator == null || !_rigAnimator.isHuman)
            {
                _spawnFailed = true;
                UxrLegsDiagnostics.Warn($"'{prefab.name}': нет humanoid Animator на корне копии рига.", _avatar);
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

            _hasStanceParam = _hasCrouchParam = false;
            foreach (AnimatorControllerParameter p in _rigAnimator.parameters)
            {
                _hasStanceParam |= p.nameHash == s_stanceParam;
                _hasCrouchParam |= p.nameHash == s_crouchParam;
            }

            // Кости ног аватар ↔ копия по именам (копия испечена из этого же префаба) — поза клипа на колене и сидя.
            var rigBones = new Dictionary<string, Transform>();
            foreach (Transform t in _rig.GetComponentsInChildren<Transform>(true))
            {
                rigBones[t.name] = t;
            }

            _legCopy.Clear();
            foreach (LegTargets leg in _legs)
            {
                if (leg.Solver == null) continue;
                foreach (Transform bone in new[] { leg.Solver.Thigh, leg.Solver.Calf, leg.Solver.Foot, leg.Solver.Toes })
                {
                    if (bone != null && rigBones.TryGetValue(bone.name, out Transform rigBone)) _legCopy.Add((bone, rigBone));
                }
            }

            _copyPoses = new Pose[_legCopy.Count];

            _left.RigFoot  = _rigAnimator.GetBoneTransform(HumanBodyBones.LeftFoot);
            _left.RigToes  = _rigAnimator.GetBoneTransform(HumanBodyBones.LeftToes);
            _right.RigFoot = _rigAnimator.GetBoneTransform(HumanBodyBones.RightFoot);
            _right.RigToes = _rigAnimator.GetBoneTransform(HumanBodyBones.RightToes);

            // Подошва — по позе префаба копии: до первой оценки Animator стопы стоят на полу.
            foreach (LegTargets leg in _legs)
            {
                CaptureSole(leg);
            }

            if (_hasStanceParam)
            {
                _rigAnimator.SetFloat(s_stanceParam, Stance);
            }

            CaptureCrouchLevels();

            _needsSnap = true;
            _ready     = _left.RigFoot != null && _right.RigFoot != null;
            if (!_ready)
            {
                _spawnFailed = true;
                UxrLegsDiagnostics.Warn($"'{prefab.name}': нет humanoid-костей стоп у копии рига.", _avatar);
            }
        }

        /// <summary>
        ///     Высота шеи клипа (без масштаба) стоя, на колене и сидя — уровни <c>Legs_Crouch</c> 0, 1, 2 (как CaptureNeutrals
        ///     WIP). Уровень без своего клипа (шея не ниже предыдущего на <see cref="MinLevelDrop" />) не используется.
        /// </summary>
        private void CaptureCrouchLevels()
        {
            Transform neck = _rigAnimator.GetBoneTransform(HumanBodyBones.Neck) ?? _rigAnimator.GetBoneTransform(HumanBodyBones.Head);
            float     s    = Mathf.Max(_rigRoot.lossyScale.y, 1e-3f);
            for (int level = 0; level < CrouchLevels; level++)
            {
                _hasLevel[level] = false;
                if (neck == null || (level > 0 && !_hasCrouchParam)) continue;
                if (_hasCrouchParam) _rigAnimator.SetFloat(s_crouchParam, level);
                _rigAnimator.Play(UxrLegLocomotion.IdleState, 0, 0f);
                _rigAnimator.Update(0f);
                _levelNeck[level] = Vector3.Dot(neck.position - _rigRoot.position, _rigRoot.up) / s;
                _hasLevel[level]  = level == 0 || (_hasLevel[level - 1] && _levelNeck[level] < _levelNeck[level - 1] - MinLevelDrop);
            }

            if (_hasCrouchParam) _rigAnimator.SetFloat(s_crouchParam, 0f);
            _rigAnimator.Play(UxrLegLocomotion.IdleState, 0, 0f);
            _rigAnimator.Update(0f);
            _crouch      = 0f;
            _hasStandRef = false;
        }

        /// <summary>
        ///     Присед — по опусканию камеры относительно роста самого игрока стоя (наибольшая высота камеры; выше глаз аватара
        ///     × <see cref="StandRefMaxOverAvatar" /> — выброс, шлем в руке), а не относительно роста аватара: разница в росте
        ///     игрока и аватара — не присед. До <see cref="UxrLegsSettings.crouchDeadZone" /> опускания — стоя; дальше — к уровням клипов по
        ///     доле их высоты глаз от высоты глаз стоя. Формула — <see cref="CrouchFromRatio" />, сглаживание <see cref="UxrLegsSettings.crouchSmoothTime" />.
        /// </summary>
        private void UpdateCrouch(Camera camera, Vector3 up, float scale, float dt)
        {
            _crouchDebug.Valid = false;
            if (!_hasCrouchParam || camera == null || !_hasLevel[1])
            {
                return;
            }

            float eye        = EyeAboveNeck * scale;
            float camHeight  = Vector3.Dot(camera.transform.position - _avatar.transform.position, up);
            float avatarEyes = _levelNeck[0] * scale + eye;
            if (!_hasStandRef)
            {
                _standRef    = Mathf.Max(camHeight, avatarEyes * 0.5f);
                _hasStandRef = true;
            }

            if (camHeight > _standRef && camHeight <= avatarEyes * StandRefMaxOverAvatar) _standRef = camHeight;

            float f      = camHeight / Mathf.Max(_standRef, 1e-3f);
            float kneel  = (_levelNeck[1] * scale + eye) / avatarEyes;
            float sit    = _hasLevel[2] ? (_levelNeck[2] * scale + eye) / avatarEyes : kneel;
            float target = CrouchOverride ?? CrouchFromRatio(f, _settings.crouchDeadZone, kneel, sit, _hasLevel[2]);

            _crouch = Mathf.Lerp(_crouch, target, 1f - Mathf.Exp(-dt / Mathf.Max(_settings.crouchSmoothTime, 1e-3f)));
            if (Mathf.Abs(_crouch - target) < 0.001f) _crouch = target;
            _rigAnimator.SetFloat(s_crouchParam, _crouch);

            _crouchDebug = new CrouchDebugState
            {
                Valid         = true,
                CameraHeight  = camHeight,
                StandRef      = _standRef,
                Ratio         = f,
                DeadZoneRatio = 1f - _settings.crouchDeadZone,
                KneelRatio    = kneel,
                SitRatio      = sit,
                HasSit        = _hasLevel[2],
                Target        = target,
                Overridden    = CrouchOverride.HasValue,
                Crouch        = _crouch,
            };
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

            s.BendToTargetWeight = _settings.kneeFollowsFoot;
            s.PositionWeight     = _settings.legsWeight;
            s.RotationWeight     = _settings.legsWeight;
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
        }

        private const string BodyPivotName         = "Dummy Forward";
        private const float  StanceSmoothTime      = 0.08f; // смена стойки ~0,25 с
        private const int    CrouchLevels          = 3;     // Legs_Crouch: 0 стоя, 1 колено, 2 сидя
        private const float  EyeAboveNeck          = 0.11f; // глаза над шеей, м без масштаба — перевод шеи клипа в высоту глаз
        private const float  StandRefMaxOverAvatar = 1.25f; // камера выше глаз аватара × это — не рост игрока, выброс
        private const float  MinLevelDrop          = 0.1f;  // уровень приседа без своего клипа — не используется

        private static readonly int s_stanceParam = Animator.StringToHash(UxrLegLocomotion.StanceParam);
        private static readonly int s_crouchParam = Animator.StringToHash(UxrLegLocomotion.CrouchParam);

        private readonly UxrStandardAvatarController _controller;
        private readonly UxrAvatar                   _avatar;
        private readonly UxrLegsSettings             _settings;
        private readonly LegTargets                  _left   = new LegTargets();
        private readonly LegTargets                  _right  = new LegTargets();
        private readonly LegTargets[]                _legs;
        private readonly UxrBodyMotion               _motion = new UxrBodyMotion();

        private Transform                _bodyPivot;
        private GameObject               _rig;
        private Animator                 _rigAnimator;
        private Transform                _rigRoot;
        private UxrLegRootMotionReceiver _rootMotion;
        private bool                     _hasStanceParam;
        private bool                     _hasCrouchParam;
        private float                    _crouch;
        private CrouchDebugState         _crouchDebug;
        private float                    _standRef;
        private bool                     _hasStandRef;
        private readonly float[]         _levelNeck = new float[CrouchLevels];
        private readonly bool[]          _hasLevel  = new bool[CrouchLevels];
        private readonly List<(Transform bone, Transform rig)> _legCopy = new List<(Transform bone, Transform rig)>();
        private Pose[]                   _copyPoses = new Pose[0];
        private bool                     _solveThisFrame = true;
        private bool                     _needsSnap      = true;
        private bool                     _ready;
        private bool                     _spawnFailed;

        #endregion
    }
}
