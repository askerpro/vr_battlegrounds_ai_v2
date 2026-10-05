// --------------------------------------------------------------------------------------------------------------------
// VR Battlegrounds patch 35: решатель ноги для UltimateXR.
//
// ЛИЦЕНЗИЯ. Это адаптация кода Final IK (RootMotion, Pärtel Lang): IKSolverVR.Leg (IKSolverVRLeg.cs) и
// IKSolverVR.VirtualBone (IKSolverVRUtilities.cs). Final IK куплен в Asset Store, EULA разрешает менять и встраивать
// его код в свой проект. Файл остаётся внутри проекта: при публикации форка UltimateXR (отдельным пакетом, в открытый
// репозиторий) его выносить НЕЛЬЗЯ — это код Final IK, а не VRMADA.
// --------------------------------------------------------------------------------------------------------------------
using UnityEngine;

namespace UltimateXR.Animation.IK
{
    /// <summary>
    ///     VR Battlegrounds patch 35: аналитический решатель ноги «бедро — голень — стопа (— носок)», перенос
    ///     <c>IKSolverVR.Leg</c> из Final IK на кости аватара UltimateXR.
    ///     <para>
    ///         Цель — поза последней кости (носка, если он есть, иначе стопы) и, по желанию, поза самой стопы. Колено
    ///         сгибается в плоскости, нормаль которой — смесь «вправо от таза» и «вправо от цели»
    ///         (<see cref="BendToTargetWeight" />): колено идёт вперёд по тазу и доворачивается за стопой. Нормаль задаётся
    ///         при <see cref="Initialize" /> явно (колени ВПЕРЁД), а не берётся из позы префаба: у почти прямых ног в
    ///         T-позе плоскость сгиба случайна (у MEF нормаль развёрнута вбок — колени уходили в стороны).
    ///     </para>
    ///     <para>
    ///         Каждое решение начинается с позы префаба (локальные повороты костей ноги) — ошибка не копится из кадра в
    ///         кадр. При нулевых весах кости не трогаются вообще. Растяжения ноги (stretch Final IK) нет: недостижимая
    ///         цель — нога прямая в её сторону.
    ///     </para>
    /// </summary>
    public sealed class UxrLegIKSolver
    {
        #region Public Types & Data

        /// <summary>Решатель готов (кости бедра, голени и стопы найдены).</summary>
        public bool Initialized { get; private set; }

        /// <summary>Есть ли кость носка: тогда цель <see cref="TargetPosition" /> — носок.</summary>
        public bool HasToes => _bones != null && _bones.Length == 4;

        /// <summary>Таз ноги — родитель бедра. Плоскость сгиба колена по умолчанию едет вместе с ним.</summary>
        public Transform Pelvis { get; private set; }

        /// <summary>Кости ноги: бедро, голень, стопа, носок (или null).</summary>
        public Transform Thigh => _transforms[0];

        public Transform Calf => _transforms[1];
        public Transform Foot => _transforms[2];
        public Transform Toes => _transforms.Length > 3 ? _transforms[3] : null;

        /// <summary>Мировая позиция цели последней кости (носка или стопы).</summary>
        public Vector3 TargetPosition { get; set; }

        /// <summary>Мировой поворот цели последней кости (носка или стопы).</summary>
        public Quaternion TargetRotation { get; set; } = Quaternion.identity;

        /// <summary>Вес позиции цели, 0 — нога в позе префаба (как positionWeight Final IK).</summary>
        public float PositionWeight { get; set; }

        /// <summary>Вес поворота цели (как rotationWeight Final IK).</summary>
        public float RotationWeight { get; set; }

        /// <summary>
        ///     Задана ли отдельно поза стопы (<see cref="FootTargetPosition" />, <see cref="FootTargetRotation" />). Без неё
        ///     стопа ставится по цели носка с отношением «стопа — носок» из позы префаба. С ней — как у Final IK в режиме
        ///     анимированной локомоции, где стопа и носок читаются из клипа.
        /// </summary>
        public bool UseFootTarget { get; set; }

        public Vector3    FootTargetPosition { get; set; }
        public Quaternion FootTargetRotation { get; set; } = Quaternion.identity;

        /// <summary>
        ///     0 — плоскость сгиба колена привязана к тазу (поворот стопы колено не крутит), 1 — к цели (колено смотрит
        ///     туда же, куда носок). Final IK: bendToTargetWeight, по умолчанию 0,5.
        /// </summary>
        public float BendToTargetWeight { get; set; } = 0.5f;

        /// <summary>Поворот плоскости сгиба вокруг оси «бедро — стопа», градусы (swivelOffset Final IK).</summary>
        public float SwivelOffset { get; set; }

        /// <summary>Точка, к которой гнётся колено при <see cref="BendGoalWeight" /> &gt; 0 (bendGoal Final IK).</summary>
        public Transform BendGoal { get; set; }

        public float BendGoalWeight { get; set; }

        /// <summary>
        ///     VR Battlegrounds patch 39: точка сгиба колена без Transform — используется, если <see cref="BendGoal" /> не задан и
        ///     <see cref="UseBendGoalPosition" />. Ставит <c>UxrAnimatedLegs</c>: колено клипа, стоящее на полу.
        /// </summary>
        public Vector3 BendGoalPosition { get; set; }

        public bool UseBendGoalPosition { get; set; }

        /// <summary>VR Battlegrounds patch 39: длина бедра (бедро → колено) в позе префаба с текущим масштабом, м.</summary>
        public float ThighLength => Initialized ? _transforms[0].TransformVector(_restLocalP[1]).magnitude : 0.0f;

        /// <summary>
        ///     VR Battlegrounds patch 36: растяжение ноги (Stretching Final IK). Аргумент — расстояние бедро→цель стопы в
        ///     длинах ноги, значение — доля удлинения бедра и голени. Без растяжения недостижимая цель — нога прямой
        ///     «палкой» и стопа отрывается от следа; с ним колено распрямляется плавно и стопа дотягивается. По умолчанию
        ///     мягче кривой Pilot из демо VRIK (0,9→0; 1→0,05; 1,3→0,3): до 4 % у самой длины ноги, не больше 12 %.
        ///     null — без растяжения.
        /// </summary>
        public AnimationCurve StretchCurve { get; set; } = new AnimationCurve(new Keyframe(0.9f, 0.0f, 0.0f, 0.4f), new Keyframe(1.0f, 0.04f), new Keyframe(1.2f, 0.12f, 0.4f, 0.0f));

        /// <summary>VR Battlegrounds patch 36: множитель длины ноги (legLengthMlp Final IK).</summary>
        public float LegLengthMlp { get; set; } = 1.0f;

        /// <summary>VR Battlegrounds patch 39: длина голени (колено → стопа) в позе префаба с текущим масштабом, м.</summary>
        public float CalfLength => Initialized ? _transforms[1].TransformVector(_restLocalP[2]).magnitude : 0.0f;

        #endregion

        #region Public Methods

        /// <summary>
        ///     Готовит решатель по костям в позе префаба.
        /// </summary>
        /// <param name="thigh">Бедро</param>
        /// <param name="calf">Голень</param>
        /// <param name="foot">Стопа</param>
        /// <param name="toes">Носок или null</param>
        /// <param name="kneeBendNormal">
        ///     Мировая нормаль плоскости сгиба в текущей позе: (бедро→голень) × (голень→стопа) для колена, согнутого как надо.
        ///     Колено вперёд — ось «вправо» корпуса аватара.
        /// </param>
        public void Initialize(Transform thigh, Transform calf, Transform foot, Transform toes, Vector3 kneeBendNormal)
        {
            Initialized = false;

            if (thigh == null || calf == null || foot == null || thigh.parent == null)
            {
                return;
            }

            _transforms = toes != null ? new[] { thigh, calf, foot, toes } : new[] { thigh, calf, foot };
            _bones      = new Bone[_transforms.Length];
            _restLocal  = new Quaternion[_transforms.Length];
            _sourceLocal = new Quaternion[_transforms.Length];
            _restLocalP = new Vector3[_transforms.Length];

            for (int i = 0; i < _transforms.Length; ++i)
            {
                _restLocal[i]  = _transforms[i].localRotation;
                _sourceLocal[i] = _restLocal[i];
                _restLocalP[i] = _transforms[i].localPosition;
                _bones[i]      = new Bone();
            }

            Pelvis = thigh.parent;
            ReadBones();

            // Оси костей (от кости к следующей, в осях кости) — как VirtualBone.PreSolve.
            for (int i = 0; i < _bones.Length - 1; ++i)
            {
                _bones[i].Axis = Quaternion.Inverse(_bones[i].Rotation) * (_bones[i + 1].Position - _bones[i].Position);
            }

            Vector3 normal = kneeBendNormal.normalized;
            _bendNormalRelToPelvis = Quaternion.Inverse(Pelvis.rotation) * normal;
            _bendNormalRelToTarget = Quaternion.Inverse(LastBone.Rotation) * normal;

            TargetPosition = LastBone.Position;
            TargetRotation = LastBone.Rotation;
            Initialized    = true;
            ResetContinuity();
        }

        /// <summary>Локальные повороты оценённого клипа; позиции сбрасываются отдельно, чтобы растяжение не копилось.</summary>
        public void SetAnimationPose(Quaternion thigh, Quaternion calf, Quaternion foot, Quaternion toes, float weight)
        {
            if (!Initialized) return;
            _sourceLocal[0] = Quaternion.Slerp(_restLocal[0], thigh, weight);
            _sourceLocal[1] = Quaternion.Slerp(_restLocal[1], calf, weight);
            _sourceLocal[2] = Quaternion.Slerp(_restLocal[2], foot, weight);
            if (HasToes) _sourceLocal[3] = Quaternion.Slerp(_restLocal[3], toes, weight);
        }

        /// <summary>Телепорт или пропуск решения: прежняя плоскость больше не является опорой.</summary>
        public void ResetContinuity()
        {
            _footPlane = default;
            _toePlane = default;
            _hasNormalAngle = false;
        }

        /// <summary>Выключение владельца: убрать клип/растяжение и забыть плоскости прежней позы.</summary>
        public void RestoreRestPose()
        {
            if (!Initialized) return;
            for (int i = 0; i < _transforms.Length; i++)
            {
                _sourceLocal[i] = _restLocal[i];
                _transforms[i].SetLocalPositionAndRotation(_restLocalP[i], _restLocal[i]);
            }
            ResetContinuity();
        }

        /// <summary>
        ///     Решает ногу к цели. Кости ноги перед решением возвращаются в позу префаба; при нулевых весах ничего не делает.
        /// </summary>
        public void Solve()
        {
            if (!Initialized || (PositionWeight <= 0.0f && RotationWeight <= 0.0f))
            {
                return;
            }

            ResetToRest();
            ReadBones();
            PreSolve();
            ApplyOffsets();
            SolveChain();
            WriteBones();
        }

        #endregion

        #region Private Methods

        private Bone LastBone => _bones[_bones.Length - 1];

        private void ResetToRest()
        {
            for (int i = 0; i < _transforms.Length; ++i)
            {
                _transforms[i].SetLocalPositionAndRotation(_restLocalP[i], _sourceLocal[i]);
            }
        }

        private void ReadBones()
        {
            for (int i = 0; i < _transforms.Length; ++i)
            {
                _transforms[i].GetPositionAndRotation(out _bones[i].Position, out _bones[i].Rotation);
            }
        }

        /// <summary>
        ///     От бедра вниз: повороты, а у голени, стопы и носка — и позиции (растяжение, патч 36). Без растяжения
        ///     позиции совпадают с теми, что дали бы повороты. Поза префаба вернётся перед следующим решением.
        /// </summary>
        private void WriteBones()
        {
            for (int i = 0; i < _transforms.Length; ++i)
            {
                if (i > 0)
                {
                    _transforms[i].position = _bones[i].Position;
                }

                _transforms[i].rotation = _bones[i].Rotation;
            }
        }

        /// <summary>IKSolverVR.Leg.PreSolve: цель, сдвиг стопы вместе с носком, нормаль плоскости сгиба.</summary>
        private void PreSolve()
        {
            Bone thigh = _bones[0];
            Bone calf  = _bones[1];
            Bone foot  = _bones[2];

            _footPosition = foot.Position;
            _footRotation = foot.Rotation;
            _position     = LastBone.Position;
            _rotation     = LastBone.Rotation;

            if (RotationWeight > 0.0f)
            {
                ApplyRotationOffset(FromTo(_rotation, TargetRotation), RotationWeight);
            }

            if (PositionWeight > 0.0f)
            {
                ApplyPositionOffset(TargetPosition - _position, PositionWeight);
            }

            if (UseFootTarget && HasToes)
            {
                // Стопа — из клипа (Final IK в режиме Animated читает её из анимации), носок — цель выше.
                float w = Mathf.Max(PositionWeight, RotationWeight);
                _footPosition = Vector3.Lerp(_footPosition, FootTargetPosition, w);
                _footRotation = Quaternion.Slerp(_footRotation, FootTargetRotation, w);
            }

            _calfRelToThigh = Quaternion.Inverse(thigh.Rotation) * calf.Rotation;
            _thighRelToFoot = Quaternion.Inverse(LastBone.Rotation) * thigh.Rotation;

            Vector3 pelvisNormal = Pelvis.rotation * _bendNormalRelToPelvis;
            Vector3 targetNormal = _rotation * _bendNormalRelToTarget;

            if (BendToTargetWeight <= 0.0f)
            {
                _bendNormal = pelvisNormal;
            }
            else if (BendToTargetWeight >= 1.0f)
            {
                _bendNormal = targetNormal;
            }
            else
            {
                Vector3 axis = (_footPosition - thigh.Position).normalized;
                Vector3 from = Vector3.ProjectOnPlane(pelvisNormal, axis);
                Vector3 to = Vector3.ProjectOnPlane(targetNormal, axis);
                _bendNormal = InterpolateContinuous(from, to, axis, BendToTargetWeight, ref _normalAngle, ref _hasNormalAngle);
            }

            _bendNormal = _bendNormal.normalized;
        }

        /// <summary>IKSolverVR.Leg.ApplyOffsets: сгиб к bendGoal и swivel.</summary>
        private void ApplyOffsets()
        {
            Bone  thigh  = _bones[0];
            _goalWeight = BendGoalWeight > 0f && (BendGoal != null || UseBendGoalPosition) ? Mathf.Clamp01(BendGoalWeight) : 0f;
            _goalPosition = BendGoal != null ? BendGoal.position : BendGoalPosition;
            float swivel = SwivelOffset;

            if (swivel != 0.0f)
            {
                _bendNormal    = Quaternion.AngleAxis(swivel, thigh.Position - LastBone.Position) * _bendNormal;
                thigh.Rotation = Quaternion.AngleAxis(-swivel, thigh.Rotation * thigh.Axis) * thigh.Rotation;
            }
        }

        private void ApplyPositionOffset(Vector3 offset, float weight)
        {
            offset        *= weight;
            _footPosition += offset;
            _position     += offset;
        }

        private void ApplyRotationOffset(Quaternion offset, float weight)
        {
            if (weight < 1.0f)
            {
                offset = Quaternion.Lerp(Quaternion.identity, offset, weight);
            }

            _footRotation = offset * _footRotation;
            _rotation     = offset * _rotation;
            _footPosition = _position + offset * (_footPosition - _position);
        }

        /// <summary>IKSolverVR.Leg.Solve без растяжения: проход стопы, поворот стопы, проход носка, скрутка бедра и голени.</summary>
        private void SolveChain()
        {
            // VR Battlegrounds patch 36: растяжение — до прохода стопы, как в Final IK.
            Stretching();

            // Проход стопы: бедро — голень — стопа к позиции стопы.
            SolveTrigonometric(0, 1, 2, _footPosition, _bendNormal);

            // Стопа — в свой поворот (вместе с носком).
            RotateAroundPoint(2, _bones[2].Position, FromTo(_bones[2].Rotation, _footRotation));

            if (HasToes)
            {
                // Проход носка: «бедро — стопа — носок» как двухзвенник, носок к цели.
                Vector3 firstSegment = _bones[2].Position - _bones[0].Position;
                Vector3 secondSegment = _bones[3].Position - _bones[2].Position;
                // Сохраняем относительную достоверность до нормализации: sin угла, а не единичный шум.
                Vector3 b = Vector3.Cross(firstSegment, secondSegment) / Mathf.Max(firstSegment.magnitude * secondSegment.magnitude, 1e-10f);
                SolveTrigonometric(0, 2, 3, _position, b);
            }

            FixTwistRotations();

            if (HasToes)
            {
                _bones[3].Rotation = _rotation;
            }
        }

        /// <summary>
        ///     VR Battlegrounds patch 36: IKSolverVR.Leg.Stretching — голень и стопа отодвигаются вдоль костей на долю
        ///     <see cref="StretchCurve" /> от расстояния до цели.
        /// </summary>
        private void Stretching()
        {
            Bone thigh = _bones[0];
            Bone calf  = _bones[1];
            Bone foot  = _bones[2];

            float   legLength = Vector3.Distance(thigh.Position, calf.Position) + Vector3.Distance(calf.Position, foot.Position);
            Vector3 kneeAdd;
            Vector3 footAdd;

            if (LegLengthMlp != 1.0f)
            {
                legLength *= LegLengthMlp;
                kneeAdd   =  (calf.Position - thigh.Position) * (LegLengthMlp - 1.0f);
                footAdd   =  (foot.Position - calf.Position) * (LegLengthMlp - 1.0f);
                MoveFrom(1, kneeAdd);
                MoveFrom(2, footAdd);
            }

            if (StretchCurve == null || legLength <= 0.0f)
            {
                return;
            }

            float m = StretchCurve.Evaluate(Vector3.Distance(thigh.Position, _footPosition) / legLength);

            if (m <= 0.0f)
            {
                return;
            }

            kneeAdd = (calf.Position - thigh.Position) * m;
            footAdd = (foot.Position - calf.Position) * m;
            MoveFrom(1, kneeAdd);
            MoveFrom(2, footAdd);
        }

        /// <summary>Сдвиг кости index и всех ниже по цепочке.</summary>
        private void MoveFrom(int index, Vector3 delta)
        {
            for (int i = index; i < _bones.Length; ++i)
            {
                _bones[i].Position += delta;
            }
        }

        /// <summary>Скрутка бедра — к повороту цели (доля <see cref="BendToTargetWeight" />), голени — за бедром.</summary>
        private void FixTwistRotations()
        {
            Bone thigh = _bones[0];
            Bone calf  = _bones[1];
            Bone foot  = _bones[2];

            if (BendToTargetWeight > 0.0f)
            {
                Quaternion thighRotation = _rotation * _thighRelToFoot;
                Quaternion f             = Quaternion.FromToRotation(thighRotation * thigh.Axis, calf.Position - thigh.Position);
                thigh.Rotation = BendToTargetWeight < 1.0f ? Quaternion.Slerp(thigh.Rotation, f * thighRotation, BendToTargetWeight) : f * thighRotation;
            }

            Quaternion calfRotation = thigh.Rotation * _calfRelToThigh;
            Quaternion fromTo       = Quaternion.FromToRotation(calfRotation * calf.Axis, foot.Position - calf.Position);
            calf.Rotation = fromTo * calfRotation;
        }

        /// <summary>VirtualBone.SolveTrigonometric: двухзвенник first — second — third к цели в плоскости bendNormal.</summary>
        private void SolveTrigonometric(int first, int second, int third, Vector3 targetPosition, Vector3 bendNormal)
        {
            Vector3 origin = _bones[first].Position;
            Vector3 dir = targetPosition - origin;
            float l1 = Vector3.Distance(origin, _bones[second].Position);
            float l2 = Vector3.Distance(_bones[second].Position, _bones[third].Position);
            float chainLength = l1 + l2;
            if (l1 <= 1e-7f || l2 <= 1e-7f) return;

            ref BendPlane plane = ref (third == 2 ? ref _footPlane : ref _toePlane);
            float epsilon = chainLength * 1e-5f;
            float distance = dir.magnitude;
            Vector3 axis = distance > epsilon ? dir / distance
                : plane.Valid ? plane.Axis : (_bones[third].Position - origin).normalized;
            if (axis.sqrMagnitude < 0.5f) axis = Vector3.down;
            Vector3 fallback = Vector3.ProjectOnPlane(plane.Valid ? plane.Direction : _bones[second].Position - origin, axis);
            if (fallback.sqrMagnitude < epsilon * epsilon)
                fallback = Vector3.ProjectOnPlane(Pelvis.forward + Pelvis.up, axis);
            if (fallback.sqrMagnitude < 1e-8f)
                fallback = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right);
            fallback.Normalize();

            Vector3 normalDirection = Vector3.Cross(axis, bendNormal);
            float normalConfidence = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.001f, 0.01f, normalDirection.magnitude));
            Vector3 bendDir = InterpolateInPlane(fallback, normalDirection, axis, normalConfidence);
            if (third == 2 && _goalWeight > 0f)
            {
                Vector3 hint = Vector3.ProjectOnPlane(_goalPosition - origin, axis);
                float confidence = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.001f, 0.01f, hint.magnitude / chainLength));
                // Не нормализуем миллиметровый шум в полный поворот колена.
                bendDir = InterpolateInPlane(fallback, bendDir, axis, confidence);
                bendDir = InterpolateContinuous(bendDir, hint, axis, confidence * _goalWeight, ref plane.GoalAngle, ref plane.HasGoalAngle);
            }
            plane.Axis = axis;
            plane.Direction = bendDir;
            plane.Valid = true;

            float d = Mathf.Clamp(distance, Mathf.Max(Mathf.Abs(l1 - l2), epsilon), chainLength);
            float x = Mathf.Clamp((d * d + l1 * l1 - l2 * l2) / (2f * d), -l1, l1);
            float y = Mathf.Sqrt(Mathf.Max((l1 - x) * (l1 + x), 0f));
            Vector3 toBendPoint = axis * x + bendDir * y;

            Quaternion q1 = Quaternion.FromToRotation(_bones[second].Position - _bones[first].Position, toBendPoint);
            RotateAroundPoint(first, _bones[first].Position, q1);

            Quaternion q2 = Quaternion.FromToRotation(_bones[third].Position - _bones[second].Position, targetPosition - _bones[second].Position);
            RotateAroundPoint(second, _bones[second].Position, q2);
        }

        /// <summary>Направление на точку сгиба по теореме косинусов (длина не равна длине первой кости).</summary>
        private static Vector3 InterpolateInPlane(Vector3 from, Vector3 to, Vector3 axis, float weight)
        {
            if (from.sqrMagnitude < 1e-10f) return to.sqrMagnitude > 1e-10f ? to.normalized : Vector3.zero;
            if (to.sqrMagnitude < 1e-10f || weight <= 0f) return from.normalized;
            from.Normalize();
            to.Normalize();
            float angle = Mathf.Atan2(Vector3.Dot(axis, Vector3.Cross(from, to)), Vector3.Dot(from, to)) * Mathf.Rad2Deg;
            return (Quaternion.AngleAxis(angle * Mathf.Clamp01(weight), axis) * from).normalized;
        }

        /// <summary>Непрерывная подписанная ветка угла; история не запрещает полный направленный поворот.</summary>
        private static Vector3 InterpolateContinuous(Vector3 from, Vector3 to, Vector3 axis, float weight, ref float previousAngle, ref bool hasAngle)
        {
            if (from.sqrMagnitude < 1e-10f || to.sqrMagnitude < 1e-10f)
                return InterpolateInPlane(from, to, axis, weight);
            from.Normalize();
            to.Normalize();
            float angle = Mathf.Atan2(Vector3.Dot(axis, Vector3.Cross(from, to)), Vector3.Dot(from, to)) * Mathf.Rad2Deg;
            if (hasAngle) angle = previousAngle + Mathf.DeltaAngle(previousAngle, angle);
            previousAngle = angle;
            hasAngle = true;
            return (Quaternion.AngleAxis(angle * Mathf.Clamp01(weight), axis) * from).normalized;
        }

        /// <summary>VirtualBone.RotateAroundPoint: поворот кости index и всех ниже по цепочке вокруг точки.</summary>
        private void RotateAroundPoint(int index, Vector3 point, Quaternion rotation)
        {
            for (int i = index; i < _bones.Length; ++i)
            {
                _bones[i].Position = point + rotation * (_bones[i].Position - point);
                _bones[i].Rotation = rotation * _bones[i].Rotation;
            }
        }

        private static Quaternion FromTo(Quaternion from, Quaternion to)
        {
            return to == from ? Quaternion.identity : to * Quaternion.Inverse(from);
        }

        #endregion

        #region Private Types & Data

        /// <summary>Кость решателя: мировая поза и ось к следующей кости (VirtualBone Final IK).</summary>
        private sealed class Bone
        {
            public Vector3    Position;
            public Quaternion Rotation;
            public Vector3    Axis;
        }

        private Transform[]  _transforms = new Transform[0];
        private Bone[]       _bones;
        private Quaternion[] _restLocal;
        private Quaternion[] _sourceLocal;
        private Vector3[]    _restLocalP;

        private Vector3    _bendNormalRelToPelvis;
        private Vector3    _bendNormalRelToTarget;
        private Vector3    _bendNormal;
        private Vector3    _footPosition;
        private Quaternion _footRotation = Quaternion.identity;
        private Vector3    _position;
        private Quaternion _rotation = Quaternion.identity;
        private Quaternion _calfRelToThigh = Quaternion.identity;
        private Quaternion _thighRelToFoot = Quaternion.identity;
        private struct BendPlane { public Vector3 Axis, Direction; public bool Valid, HasGoalAngle; public float GoalAngle; }
        private BendPlane _footPlane, _toePlane;
        private Vector3 _goalPosition;
        private float _goalWeight;
        private float _normalAngle;
        private bool _hasNormalAngle;

        #endregion
    }
}
