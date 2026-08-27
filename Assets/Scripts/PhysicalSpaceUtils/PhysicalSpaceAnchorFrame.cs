using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.PhysicalSpaceUtils
{
    /// <summary>
    /// Система координат карты, заданная парой <see cref="PhysicalSpaceAnchor" />.
    ///
    /// <para>
    /// <b>Зачем.</b> Якоря отмечают в виртуальной арене две точки, которым в реальной
    /// комнате соответствуют физические метки на полу. «Место игрока» после калибровки —
    /// это место <b>относительно этих меток</b>, а не мировые координаты сцены. Разница
    /// не теоретическая: обе карты проекта собраны из одного и того же префаба арены
    /// (<c>Environment</c>, район Толстого), но в <c>TestMap1</c> он повёрнут на 90° вокруг Y,
    /// а в <c>TestMap2</c> и <c>Lobby</c> стоит без поворота. Мировые координаты якорей
    /// поэтому <b>не совпадают</b>:
    /// </para>
    /// <code>
    /// якорь id=0:  TestMap2 (-2.74, -0.01, -3.60)   TestMap1 (-3.60, -0.01,  2.74)
    /// якорь id=1:  TestMap2 ( 2.75, -0.01, -3.60)   TestMap1 (-3.60, -0.01, -2.75)
    /// ось якорей:  TestMap2 вдоль +X                TestMap1 вдоль -Z
    /// </code>
    /// <para>
    /// Сохранить мировую позицию при смене карты значило бы развернуть игрока на 90°
    /// относительно арены — в стену или в чужую базу. Сохранить позицию <b>в этой системе
    /// координат</b> — значит оставить его там же, где он физически стоит.
    /// </para>
    ///
    /// <para>
    /// <b>Устройство.</b> Начало — якорь с меньшим <c>id</c>, ось <c>+Z</c> смотрит
    /// на второй якорь, крен и тангаж отброшены: пара якорей задаёт направление
    /// на плоскости пола, вертикаль у всех карт общая. Той же трактовки держится
    /// <see cref="PhysicalSpaceSyncManager.CalculateTransform" /> — он тоже сплющивает
    /// оба направления через <c>Scale(1, 0, 1)</c>.
    /// </para>
    /// </summary>
    public readonly struct PhysicalSpaceAnchorFrame
    {
        /// <summary>
        /// Минимальное расстояние между якорями, при котором направление вообще
        /// определено, метры. Полметра — заведомо меньше любой осмысленной расстановки
        /// (в арене проекта якоря разведены на 5,5 м) и заведомо больше шума.
        /// </summary>
        public const float MinAnchorSeparation = 0.5f;

        /// <summary>Мировая позиция якоря с меньшим <c>id</c>.</summary>
        public readonly Vector3 Origin;

        /// <summary>Поворот системы координат: <c>+Z</c> смотрит на второй якорь, крен и тангаж нулевые.</summary>
        public readonly Quaternion Rotation;

        /// <summary>Горизонтальное расстояние между якорями, метры.</summary>
        public readonly float Separation;

        /// <summary>Система координат построена и ей можно пользоваться.</summary>
        public readonly bool IsValid;

        private PhysicalSpaceAnchorFrame(Vector3 origin, Quaternion rotation, float separation)
        {
            Origin     = origin;
            Rotation   = rotation;
            Separation = separation;
            IsValid    = true;
        }

        /// <summary>Мировая точка → координаты относительно якорей.</summary>
        public Vector3 ToLocal(Vector3 worldPosition)
        {
            return Quaternion.Inverse(Rotation) * (worldPosition - Origin);
        }

        /// <summary>Координаты относительно якорей → мировая точка.</summary>
        public Vector3 ToWorld(Vector3 localPosition)
        {
            return Rotation * localPosition + Origin;
        }

        /// <summary>Мировой поворот → поворот относительно якорей.</summary>
        public Quaternion ToLocal(Quaternion worldRotation)
        {
            return Quaternion.Inverse(Rotation) * worldRotation;
        }

        /// <summary>Поворот относительно якорей → мировой поворот.</summary>
        public Quaternion ToWorld(Quaternion localRotation)
        {
            return Rotation * localRotation;
        }

        /// <summary>
        /// Строит систему координат по двум мировым точкам.
        /// </summary>
        /// <param name="first">Якорь с меньшим <c>id</c> — он же начало координат.</param>
        /// <param name="second">Второй якорь — он задаёт направление <c>+Z</c>.</param>
        /// <param name="frame">Результат. Осмыслен только при <c>true</c>.</param>
        /// <param name="diagnosis">Почему не получилось. Пусто при успехе.</param>
        public static bool TryBuild(Vector3 first, Vector3 second,
                                    out PhysicalSpaceAnchorFrame frame, out string diagnosis)
        {
            Vector3 flat = Vector3.Scale(second - first, new Vector3(1f, 0f, 1f));
            float separation = flat.magnitude;

            if (separation < MinAnchorSeparation)
            {
                frame = default(PhysicalSpaceAnchorFrame);
                diagnosis = $"якоря разведены по горизонтали всего на {separation:F2} м " +
                            $"(нужно не меньше {MinAnchorSeparation:F2} м) — направление не определено";
                return false;
            }

            frame = new PhysicalSpaceAnchorFrame(first, Quaternion.LookRotation(flat / separation, Vector3.up), separation);
            diagnosis = string.Empty;
            return true;
        }

        /// <summary>
        /// Строит систему координат по якорям, стоящим на текущей сцене.
        ///
        /// <para>
        /// Якоря сортируются по <c>id</c> — тем же порядком, что и в
        /// <c>PhysicalSpaceSyncManager.CollectAnchors</c>, — и берутся первые два.
        /// Порядок обязан быть одинаковым от прогона к прогону, иначе система координат
        /// разворачивается на 180° случайным образом.
        /// </para>
        /// </summary>
        public static bool TryBuildFromScene(out PhysicalSpaceAnchorFrame frame, out string diagnosis)
        {
            PhysicalSpaceAnchor[] found = Object.FindObjectsByType<PhysicalSpaceAnchor>(FindObjectsSortMode.None);

            if (found.Length < 2)
            {
                frame = default(PhysicalSpaceAnchorFrame);
                diagnosis = $"на сцене '{UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}' " +
                            $"найдено якорей: {found.Length}, а нужно два";
                return false;
            }

            List<PhysicalSpaceAnchor> ordered = new List<PhysicalSpaceAnchor>(found);
            ordered.Sort((a, b) => a.id.CompareTo(b.id));

            return TryBuild(ordered[0].transform.position, ordered[1].transform.position, out frame, out diagnosis);
        }

        public override string ToString()
        {
            return IsValid
                ? $"начало {Origin}, ось на второй якорь {Rotation.eulerAngles.y:F1}°, база {Separation:F2} м"
                : "(система координат не построена)";
        }
    }
}
