using UnityEngine;

namespace UltimateXR.Animation.IK
{
    public sealed partial class UxrBodyIK
    {
        #region Public Types & Data (VR Battlegrounds patch 34)

        /// <summary>
        ///     VR Battlegrounds patch 34: изгиб корпуса извне, в осях опоры тела (<c>Dummy Forward</c>): +X — вперёд,
        ///     −Z — вправо. Складывается с изгибом от наклона головы (сверх <c>HeadFreeRangeBend</c>) и так же делится
        ///     между Spine / Chest / UpperChest по их весам Bend; тело сдвигается так, что голова остаётся в шлеме, а руки
        ///     решаются после — кисти на контроллерах. Шлем наклона корпуса не сообщает, поэтому его задаёт игра
        ///     (наклон бегущего — <c>AvatarBodyPipeline</c>). По умолчанию identity — как в оригинальном SDK.
        ///     Не действует при <c>LockBodyPivot</c>.
        /// </summary>
        public Quaternion ExternalBodyBend { get; set; } = Quaternion.identity;

        /// <summary>
        ///     VR Battlegrounds patch 39: наибольший наклон корпуса НАЗАД, градусы (весь изгиб позвоночника: от наклона головы и
        ///     внешний). Люди почти всегда наклоняются вперёд; откидка назад от взгляда вверх или клипа хода спиной выглядит
        ///     ошибкой. Вперёд — без ограничения.
        /// </summary>
        public static float MaxBackBendDegrees = 8f;

        /// <summary>VR Battlegrounds patch 39: наибольший наклон всего корпуса вперёд (<see cref="ExternalTrunkLean" />), градусы.</summary>
        public static float MaxTrunkLeanDegrees = 60f;

        /// <summary>
        ///     VR Battlegrounds patch 39: наклон всего корпуса (Spine, Chest, UpperChest — целиком, не долями весов Bend), в осях
        ///     опоры тела. В отличие от <see cref="ExternalBodyBend" /> (делится по весам: у MEF до груди доходит ~40 %, нижняя
        ///     спина почти не гнётся) наклоняет линию таз → шея на заданный угол — таз уходит назад и вверх, голова в шлеме.
        /// </summary>
        public Quaternion ExternalTrunkLean { get; set; } = Quaternion.identity;

        /// <summary>
        ///     VR Battlegrounds patch 39: ограничить тангаж поворота корпуса (в осях опоры: +Z вперёд). Тангаж — угол вектора
        ///     «вверх» к плоскости X-Y (не зависит от крена: прежний Atan2(z, y) при наклоне вбок давал ложные десятки градусов
        ///     «назад», и поправка вокруг X вместе с креном скручивала корпус). Поправка — FromToRotation между исходным и
        ///     ограниченным вектором «вверх» (крен сохраняется, скрутки вокруг вертикали нет).
        /// </summary>
        public static Quaternion ClampPitch(Quaternion rotation, float minPitch, float maxPitch)
        {
            Vector3 bentUp = rotation * Vector3.up;
            float   radius = Mathf.Sqrt(bentUp.y * bentUp.y + bentUp.z * bentUp.z);
            float   pitch  = Mathf.Atan2(bentUp.z, bentUp.y) * Mathf.Rad2Deg;
            float   pc     = Mathf.Clamp(pitch, minPitch, maxPitch);
            if (Mathf.Approximately(pc, pitch))
            {
                return rotation;
            }

            // Sagittal pitch ограничивается в YZ, крен (X) сохраняется; угол и реконструкция используют одну метрику.
            Vector3 clamped = new Vector3(bentUp.x, radius * Mathf.Cos(pc * Mathf.Deg2Rad), radius * Mathf.Sin(pc * Mathf.Deg2Rad));
            return Quaternion.FromToRotation(bentUp, clamped) * rotation;
        }

        #endregion

        #region Public Types & Data (VR Battlegrounds patch 38)

        /// <summary>
        ///     VR Battlegrounds patch 38: идёт ли игрок — оценщик по шее из позы камеры (вход), обновляется в начале
        ///     <see cref="PreSolveAvatarIK" />. Единственный источник решения «тело идёт» для выпрямления корпуса и для ног.
        /// </summary>
        public UxrBodyMotion Motion { get; } = new UxrBodyMotion();

        /// <summary>
        ///     VR Battlegrounds patch 38, только для A/B на стенде: true — решения «тело идёт» по-старому (выпрямление по
        ///     сдвигу опоры тела, ноги по движению опоры). Убрать, когда эксперимент закреплён.
        /// </summary>
        public static bool LegacyMovementDecisions;

        /// <summary>Остаток после подбора сидячего корпуса; невозможная высота не скрывается растяжением шеи.</summary>
        public float SeatedNeckResidual { get; private set; }

        private Transform LowestSpine => _avatar.AvatarRig.Spine != null ? _avatar.AvatarRig.Spine
            : _avatar.AvatarRig.Chest != null ? _avatar.AvatarRig.Chest : _avatar.AvatarRig.UpperChest;
        private float _seatedConstraintWeight;

        /// <summary>Таз и нижняя поза текущего клипа. Размещение по XZ согласуется целиком; yaw от наклона не добавляется.</summary>
        public bool ApplySeatedPelvis(Transform hips, Pose hipsPose, float weight, out Vector3 placementDelta)
        {
            placementDelta = Vector3.zero;
            _seatedConstraintWeight = 0f;
            SeatedNeckResidual = 0f;
            Transform spine = LowestSpine;
            if (weight <= 0f || hips == null || spine == null || _avatarNeck == null || _settings.LockBodyPivot) return false;

            Pose originalHips = new Pose(hips.position, hips.rotation);
            Quaternion spineRotation = spine.rotation;
            Quaternion neckRotation = _avatarNeck.rotation;
            Vector3 trackedNeck = _avatarNeck.position;
            Quaternion headRotation = _avatarHead.rotation;
            foreach (IndependentBoneInfo bone in _independentBones)
            {
                bone.Position = bone.Transform.position;
                bone.Rotation = bone.Transform.rotation;
            }

            hips.SetPositionAndRotation(Vector3.Lerp(originalHips.position, hipsPose.position, weight),
                Quaternion.Slerp(originalHips.rotation, hipsPose.rotation, weight));
            // Поворот таза клипа не является вторым автором корпуса.
            spine.rotation = spineRotation;
            Vector3 u = Quaternion.Inverse(_avatarForward.rotation) * (_avatarNeck.position - spine.position);
            float h = Vector3.Dot(trackedNeck - spine.position, _avatarForward.up);
            float radius = Mathf.Sqrt(u.y * u.y + u.z * u.z);
            float beta = Mathf.Atan2(u.z, u.y) * Mathf.Rad2Deg;
            float alpha = radius > 1e-5f ? Mathf.Acos(Mathf.Clamp(h / radius, -1f, 1f)) * Mathf.Rad2Deg : 0f;
            float forwardPitch = alpha - beta;
            float backwardPitch = -alpha - beta;
            bool backwardAllowed = backwardPitch >= -MaxBackBendDegrees && backwardPitch <= MaxTrunkLeanDegrees;
            float pitch = backwardAllowed && Mathf.Abs(backwardPitch) < Mathf.Abs(forwardPitch) ? backwardPitch : forwardPitch;
            pitch = Mathf.Clamp(pitch, -MaxBackBendDegrees, MaxTrunkLeanDegrees);
            spine.rotation = Quaternion.AngleAxis(pitch, _avatarForward.right) * spineRotation;
            placementDelta = Vector3.ProjectOnPlane(trackedNeck - _avatarNeck.position, _avatarForward.up);
            hips.position += placementDelta;
            SeatedNeckResidual = Vector3.Distance(_avatarNeck.position, trackedNeck);

            // Отказ сохраняет прежнюю согласованную позу, а не растягивает шейный сегмент ради двух недостижимых целей.
            bool accepted = SeatedNeckResidual <= Mathf.Max(0.003f * _avatar.transform.lossyScale.y, 1e-4f);
            if (!accepted)
            {
                hips.SetPositionAndRotation(originalHips.position, originalHips.rotation);
                spine.rotation = spineRotation;
                placementDelta = Vector3.zero;
            }
            else _seatedConstraintWeight = weight;

            _avatarNeck.rotation = neckRotation;
            _avatarHead.rotation = headRotation;
            foreach (IndependentBoneInfo bone in _independentBones)
                bone.Transform.SetPositionAndRotation(bone.Position, bone.Rotation);
            return accepted;
        }

        #endregion
    }
}
