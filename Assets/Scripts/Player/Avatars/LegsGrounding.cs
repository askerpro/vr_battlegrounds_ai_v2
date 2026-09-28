using UnityEngine;

namespace VrBattlegrounds.Player.Avatars
{
    /// <summary>
    /// Локальная поза кости: захват и возврат. Нужна там, где кость должна каждый кадр
    /// начинать с одной и той же позы, а анимации, которая бы её выставляла, нет.
    /// </summary>
    public readonly struct BoneLocalPose
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;

        public BoneLocalPose(Vector3 position, Quaternion rotation)
        {
            Position = position;
            Rotation = rotation;
        }

        public static BoneLocalPose Capture(Transform bone)
        {
            return new BoneLocalPose(bone.localPosition, bone.localRotation);
        }

        public void ApplyTo(Transform bone)
        {
            bone.SetLocalPositionAndRotation(Position, Rotation);
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
        /// Точка опоры ступни: пол под ногой, поднятый на толщину подошвы. Кость стопы — это
        /// лодыжка, а не подошва; без подъёма ботинок уходит в пол.
        /// </summary>
        public static Vector3 SoleContactPoint(Vector3 floorHit, Vector3 up, float soleThickness)
        {
            return floorHit + up * soleThickness;
        }
    }
}
