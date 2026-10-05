// --------------------------------------------------------------------------------------------------------------------
// VR Battlegrounds patch 37: математика ног из клипов (чистые функции, проверяются EditMode-тестами игры).
// --------------------------------------------------------------------------------------------------------------------
using UnityEngine;

namespace UltimateXR.Animation.IK
{
    /// <summary>
    ///     VR Battlegrounds patch 37: как перенести таз и наклон корпуса из клипа на копии рига на аватар. Чистые функции.
    /// </summary>
    public static class UxrLegPoseMath
    {
        #region Public Methods

        /// <summary>
        ///     Таз аватара: поза префаба плюс отклонение таза клипа от покоя того же набора клипов. Отклонение, а не сама поза
        ///     клипа: гуманоидная поза таза в клипе отличается от позы префаба постоянным сдвигом и поворотом, и прямой перенос
        ///     складывает тело пополам. Поворот — раздельно: рысканье (вокруг <paramref name="upInParent" /> — вертикали в
        ///     осях родителя таза) с долей <paramref name="yawWeight" />, наклон и покачивание — с долей
        ///     <paramref name="tiltWeight" /> (клипы шага вбок разворачивают таз по ходу, а корпус игрока смотрит вперёд).
        /// </summary>
        public static Pose HipsFromClip(Pose avatarRest, Pose clipIdle, Pose clipCurrent, Vector3 upInParent, float tiltWeight, float yawWeight)
        {
            // Поворот в осях родителя: current = delta × idle.
            Quaternion delta = clipCurrent.rotation * Quaternion.Inverse(clipIdle.rotation);
            SwingTwist(delta, upInParent, out Quaternion swing, out Quaternion twist);

            Quaternion weighted = Quaternion.Slerp(Quaternion.identity, swing, Mathf.Clamp01(tiltWeight)) *
                                  Quaternion.Slerp(Quaternion.identity, twist, Mathf.Clamp01(yawWeight));
            // Обратно в оси таза: доля отклонения поверх позы префаба.
            Quaternion local  = Quaternion.Inverse(clipIdle.rotation) * weighted * clipIdle.rotation;
            Vector3    offset = clipCurrent.position - clipIdle.position;
            return new Pose(avatarRest.position + offset * Mathf.Clamp01(tiltWeight), avatarRest.rotation * local);
        }

        /// <summary>
        ///     Локальная поза корня ног (кость таза под Hips, родитель бёдер), при которой ноги стоят так, будто Hips в
        ///     позе <paramref name="hipsMoved" />, а сам Hips остаётся в <paramref name="hipsStill" />. Hips — родитель
        ///     позвоночника: его наклон из клипа бега наклонял весь корпус, и грудь отставала от головы («резиновая шея»).
        /// </summary>
        public static Pose LegRootUnderStillHips(Pose hipsStill, Pose hipsMoved, Pose legRootRest)
        {
            // Hips_still × root == Hips_moved × root_rest.
            return Multiply(Multiply(Inverse(hipsStill), hipsMoved), legRootRest);
        }

        /// <summary>Скорость, м/с, с которой корпус начинает наклоняться по ходу (шаг — прямо).</summary>
        public const float LeanStartSpeed = 1f;

        /// <summary>Скорость, м/с, на которой наклон полный (бег).</summary>
        public const float LeanFullSpeed = 3.5f;

        /// <summary>
        ///     Наклон корпуса бегущего в осях корпуса (x — вправо, z — вперёд): вперёд до <paramref name="maxDegrees" />,
        ///     вбок — вполсилы, назад — без наклона. Шлем наклона корпуса не сообщает, поэтому он выводится из скорости тела.
        /// </summary>
        public static Quaternion RunLean(Vector3 velocity, Vector3 bodyForward, float maxDegrees)
        {
            Vector3 forward = Vector3.ProjectOnPlane(bodyForward, Vector3.up).normalized;
            if (forward.sqrMagnitude < 1e-6f)
            {
                return Quaternion.identity;
            }

            Vector3 right = Vector3.Cross(Vector3.up, forward);
            float   ahead = Vector3.Dot(velocity, forward);
            float   aside = Vector3.Dot(velocity, right);

            float pitch = maxDegrees * LeanShare(ahead);
            float roll  = 0.5f * maxDegrees * LeanShare(Mathf.Abs(aside)) * Mathf.Sign(aside);

            // Unity: +X — наклон вперёд, −Z — вправо.
            return Quaternion.Euler(pitch, 0f, -roll);
        }

        /// <summary>
        ///     Разложение поворота на скрутку вокруг <paramref name="axis" /> и наклон: <c>q = swing × twist</c>.
        /// </summary>
        public static void SwingTwist(Quaternion q, Vector3 axis, out Quaternion swing, out Quaternion twist)
        {
            Vector3 a = axis.normalized;
            Vector3 p = Vector3.Project(new Vector3(q.x, q.y, q.z), a);
            twist = new Quaternion(p.x, p.y, p.z, q.w);
            float n = Mathf.Sqrt(twist.x * twist.x + twist.y * twist.y + twist.z * twist.z + twist.w * twist.w);
            twist = n > 1e-6f ? new Quaternion(twist.x / n, twist.y / n, twist.z / n, twist.w / n) : Quaternion.identity;
            swing = q * Quaternion.Inverse(twist);
        }

        /// <summary>Значение между покоями соседних наборов клипов (стойка дробная во время смены).</summary>
        public static int StanceSegment(float stance, int count, out int next, out float t)
        {
            float s = Mathf.Clamp(stance, 0f, Mathf.Max(count - 1, 0));
            int   a = Mathf.FloorToInt(s);
            next = Mathf.Min(a + 1, count - 1);
            t    = s - a;
            return a;
        }

        /// <summary>Композиция поз как матриц: <c>parent × child</c>.</summary>
        public static Pose Multiply(Pose parent, Pose child)
        {
            return new Pose(parent.position + parent.rotation * child.position, parent.rotation * child.rotation);
        }

        /// <summary>Обратная поза: <c>Multiply(Inverse(a), a)</c> — единичная.</summary>
        public static Pose Inverse(Pose pose)
        {
            Quaternion inverse = Quaternion.Inverse(pose.rotation);
            return new Pose(inverse * -pose.position, inverse);
        }

        #endregion

        #region Private Methods

        private static float LeanShare(float speed)
        {
            return Mathf.Clamp01((speed - LeanStartSpeed) / (LeanFullSpeed - LeanStartSpeed));
        }

        #endregion
    }
}
