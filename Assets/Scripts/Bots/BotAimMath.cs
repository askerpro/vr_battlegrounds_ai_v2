using UnityEngine;

namespace VrBattlegrounds.Bots
{
    /// <summary>
    /// Геометрия наведения ствола бота: одна метрика ошибки дула для поправки отдачи (<see cref="BotWeaponAim"/>)
    /// и для стенда. Чистые функции без состояния, только чтение Transform. Перенесено из архива клипового пути
    /// (ветка bots-fix-clip-archive) без клиповых частей.
    /// Система — корень субъекта: +pitch — ствол выше цели, +yaw — правее цели; градусы.
    /// </summary>
    public static class BotAimMath
    {
        /// <summary>Отклонение ShotSource.forward от направления дуло→точка цели в системе корня.</summary>
        public readonly struct MuzzleError
        {
            /// <summary>pitch ствола минус pitch направления на цель; &gt; 0 — выше цели.</summary>
            public readonly float Pitch;
            /// <summary>DeltaAngle(yaw на цель, yaw ствола); &gt; 0 — правее цели.</summary>
            public readonly float Yaw;
            /// <summary>Полный угол между стволом и направлением на цель.</summary>
            public readonly float Angle;
            /// <summary>Дистанция дуло→цель, м.</summary>
            public readonly float Distance;

            public MuzzleError(float pitch, float yaw, float angle, float distance)
            {
                Pitch = pitch; Yaw = yaw; Angle = angle; Distance = distance;
            }
        }

        /// <summary>
        /// Ошибка дула к точке цели в системе корня <paramref name="root"/> (поворот без масштаба).
        /// false — дуло совпадает с точкой цели (направление не определено).
        /// </summary>
        public static bool TryGetMuzzleError(Transform root, Vector3 muzzlePosition, Vector3 muzzleForward,
                                             Vector3 target, out MuzzleError error)
        {
            Vector3 toTarget = target - muzzlePosition;
            if (toTarget.sqrMagnitude < 1e-8f) { error = default; return false; }
            Vector3 forward = root.InverseTransformDirection(muzzleForward);
            Vector3 aim = root.InverseTransformDirection(toTarget.normalized);
            error = new MuzzleError(PitchOf(forward) - PitchOf(aim), Mathf.DeltaAngle(YawOf(aim), YawOf(forward)),
                                    Vector3.Angle(muzzleForward, toTarget), toTarget.magnitude);
            return true;
        }

        /// <summary>Угол над горизонтальной плоскостью системы вектора; + вверх, градусы.</summary>
        public static float PitchOf(Vector3 v) => Mathf.Atan2(v.y, new Vector2(v.x, v.z).magnitude) * Mathf.Rad2Deg;

        /// <summary>Курс вектора вокруг +Y; 0 — вперёд (+Z), + вправо (+X), градусы.</summary>
        public static float YawOf(Vector3 v) => Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
    }
}
