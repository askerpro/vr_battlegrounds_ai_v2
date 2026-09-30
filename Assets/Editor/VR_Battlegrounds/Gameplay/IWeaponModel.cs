using System.Collections.Generic;
using UltimateXR.Core;
using UnityEngine;
using VrBattlegrounds.Editor.Avatars;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Источник геометрии и механики оружия для <see cref="HandsPackWeaponBuilder" />: детали, их места
    /// относительно корпуса, ход деталей в клипах. Реализации — пак Hands (<see cref="HandsPackWeapon" />)
    /// и пак KINEMATION (<see cref="KinemationWeapon" />).
    ///
    /// <para>
    /// Деталь называется строкой источника (рендерер пака Hands, кость KINEMATION). Размеры — в единицах
    /// меша: детали ставятся с масштабом 1, реальный размер задаёт масштаб корня префаба.
    /// </para>
    /// </summary>
    public interface IWeaponModel
    {
        Mesh MeshOf(string part);
        Material[] MaterialsOf(string part);

        /// <summary>Место детали в осях корпуса.</summary>
        Matrix4x4 PartInBody(string part);

        /// <summary>Поза клипа в момент <paramref name="time" />; <c>null</c> — поза покоя модели.</summary>
        void Sample(string clipName, float time = 0f);

        /// <summary>Ход и поворот детали за клип от кадра покоя <paramref name="restClip" />.</summary>
        PartMotion Measure(string part, string clipName, string restClip = "Idle");

        /// <summary>Оси прицеливания (ствол, верх) в осях корпуса, округлённые до осей меша.</summary>
        (Vector3 forward, Vector3 up) AimAxes(string poseClip);

        /// <summary>Статичные меши деталей под <paramref name="container" />; корпус — в <paramref name="bodyLocal" />.</summary>
        Dictionary<string, Transform> BuildParts(Transform container, Matrix4x4 bodyLocal, IDictionary<string, string> partNames);
    }

    /// <summary>
    /// Источник, у которого есть кадр рук на оружии в универсальных осях ладони UltimateXR: хват ставится
    /// прямо по нему (<see cref="HandsPackGripAligner" />), без калибровки по донору того же пака.
    /// </summary>
    public interface IHandGripSource
    {
        /// <summary>Кадр руки <paramref name="side" /> на оружии; корпус — деталь-корпус источника, масштаб 1.</summary>
        HandsPackPoseExtractor.Sample GripSample(UxrHandSide side);
    }
}
