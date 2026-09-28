using UnityEngine;

namespace VrBattlegrounds.Player.Avatars
{
    /// <summary>
    /// Локальная поза кости: захват и возврат. Нужна там, где кость должна каждый кадр
    /// начинать с одной и той же позы, а анимации, которая бы её выставляла, нет.
    ///
    /// <para>
    /// Сериализуемая намеренно: при перекомпиляции в Play Mode Unity сохраняет приватные поля
    /// компонента, только если их тип сериализуем. Несериализуемая поза обнулялась, ссылка на
    /// кость выживала — и мост ставил таз в (0, 0, 0), тело подскакивало на метр.
    /// </para>
    /// </summary>
    [System.Serializable]
    public struct BoneLocalPose
    {
        [SerializeField] private Vector3 _position;
        [SerializeField] private Quaternion _rotation;

        public Vector3 Position => _position;
        public Quaternion Rotation => _rotation;

        /// <summary>Поза захвачена (у значения по умолчанию поворот нулевой, не единичный).</summary>
        public bool IsValid => _rotation.x != 0f || _rotation.y != 0f || _rotation.z != 0f || _rotation.w != 0f;

        public BoneLocalPose(Vector3 position, Quaternion rotation)
        {
            _position = position;
            _rotation = rotation;
        }

        public static BoneLocalPose Capture(Transform bone)
        {
            return new BoneLocalPose(bone.localPosition, bone.localRotation);
        }

        public void ApplyTo(Transform bone)
        {
            bone.SetLocalPositionAndRotation(_position, _rotation);
        }
    }

    /// <summary>
    /// Геометрия связки Legs Animator ↔ UltimateXR без Unity-компонентов — считает
    /// <c>LegsAnimatorUxrBridge</c>, проверяют <c>LegsGroundingTests</c>.
    /// </summary>
    public static class LegsGrounding
    {
        /// <summary>
        /// Поза корня ног. UltimateXR носит тело на объекте <c>Dummy Forward</c>: он идёт за
        /// головой по горизонтали, но по высоте висит где угодно (под камерой на уровне шеи
        /// минус рост). Legs Animator же ждёт корень на полу. Поэтому корень ног — проекция
        /// <c>Dummy Forward</c> на плоскость пола аватара, повёрнутая только вокруг вертикали.
        /// </summary>
        /// <param name="bodyPivotPosition">Мировая позиция <c>Dummy Forward</c>.</param>
        /// <param name="bodyPivotForward">Его forward.</param>
        /// <param name="floorPoint">Точка на полу аватара — позиция корня <c>UxrAvatar</c>.</param>
        /// <param name="up">Вертикаль аватара.</param>
        /// <param name="fallbackForward">Направление, если forward смотрит вертикально.</param>
        public static Pose RootAnchor(Vector3 bodyPivotPosition, Vector3 bodyPivotForward, Vector3 floorPoint,
            Vector3 up, Vector3 fallbackForward)
        {
            Vector3 position = bodyPivotPosition - up * Vector3.Dot(bodyPivotPosition - floorPoint, up);

            Vector3 forward = Vector3.ProjectOnPlane(bodyPivotForward, up);
            if (forward.sqrMagnitude < 1e-6f) forward = Vector3.ProjectOnPlane(fallbackForward, up);
            if (forward.sqrMagnitude < 1e-6f) return new Pose(position, Quaternion.identity);

            return new Pose(position, Quaternion.LookRotation(forward, up));
        }

        /// <summary>
        /// Точка опоры: пол под ногой, поднятый на <paramref name="soleThickness"/>. Обычно 0 —
        /// высоту лодыжки над подошвой Legs Animator учитывает сам (<c>AnkleToHeel</c>), а поднятый
        /// пол он принимает за возвышение и поднимает под него всё тело.
        /// </summary>
        public static Vector3 SoleContactPoint(Vector3 floorHit, Vector3 up, float soleThickness)
        {
            return floorHit + up * soleThickness;
        }
    }
}
