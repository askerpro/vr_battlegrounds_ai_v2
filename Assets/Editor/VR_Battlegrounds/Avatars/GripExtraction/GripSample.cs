using UltimateXR.Core;
using UltimateXR.Manipulation.HandPoses;
using UnityEngine;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>Точка касания подушечки на предмете — в осях корпуса предмета, м (масштаб источника). Источники пока не заполняют.</summary>
    public sealed class GripPadContact
    {
        /// <summary>thumb | index | middle | ring | little.</summary>
        public string Finger;

        /// <summary>Точка на поверхности предмета в системе корпуса, без масштаба источника.</summary>
        public Vector3 BodyPoint;
    }

    /// <summary>
    /// Нейтральный хват Grip Extraction: что источник знает о хвате, без привязки к кисти цели.
    ///
    /// <para>
    /// Система кисти источника — универсальные оси кисти SDK (<c>HandUniversalLocalAxes</c>) с началом в запястье.
    /// Из неё выводится система ладони: начало — центр ладони (<see cref="PalmCenter" />, определение SDK
    /// <c>UxrAvatarHand.GetPalmCenter</c>), оси те же. Корпус предмета хранится относительно запястья (прежнее
    /// поведение конвейера паков) и относительно центра ладони (<see cref="PalmBodyPosition" />).
    /// </para>
    /// </summary>
    public sealed class GripSample
    {
        /// <summary>Вид источника: hands | kinemation (позже avatar-record, manual).</summary>
        public string SourceKind;

        /// <summary>Рука источника, с которой снят кадр; вторая рука — зеркало.</summary>
        public UxrHandSide Side;

        /// <summary>Поза пальцев в универсальных осях (как отдаёт источник; у паков — без пястных).</summary>
        public UxrHandDescriptor Hand;

        /// <summary>Корпус предмета в системе кисти с началом в запястье, без масштаба (прежний конвейер).</summary>
        public Vector3 WristBodyPosition;
        public Quaternion WristBodyRotation;

        /// <summary>Центр ладони в той же системе (запястье — начало), без масштаба.</summary>
        public Vector3 PalmCenter;

        /// <summary>Масштаб корпуса источника — делитель k = масштаб корпуса префаба / BodyScale (как HandsPackGripAligner).</summary>
        public float BodyScale;

        /// <summary>Точки касания подушечек, если источник их знает; иначе null.</summary>
        public GripPadContact[] Pads;

        /// <summary>Исходный кадр пака — только для сверки R0 с <see cref="HandsPackGripAligner" />.</summary>
        internal HandsPackPoseExtractor.Sample Legacy;

        /// <summary>Корпус относительно центра ладони (оси кисти), без масштаба.</summary>
        public Vector3 PalmBodyPosition => WristBodyPosition - PalmCenter;

        public Quaternion PalmBodyRotation => WristBodyRotation;

        public static GripSample FromPack(HandsPackPoseExtractor.Sample sample, UxrHandSide side, string sourceKind) =>
            new GripSample
            {
                SourceKind        = sourceKind,
                Side              = side,
                Hand              = sample.Hand,
                WristBodyPosition = sample.BodyPosition,
                WristBodyRotation = sample.BodyRotation,
                PalmCenter        = sample.PalmCenter,
                BodyScale         = sample.BodyScale,
                Legacy            = sample
            };

        /// <summary>
        /// Кадр для руки <paramref name="side" />: своя рука — как есть, другая — зеркало по оси «вправо» кисти
        /// и по боковой оси корпуса (то же правило, что <see cref="HandsPackGripAligner" />).
        /// </summary>
        public void ForSide(UxrHandSide side, out Vector3 bodyPosition, out Quaternion bodyRotation, out Vector3 palmCenter)
        {
            bodyPosition = WristBodyPosition;
            bodyRotation = WristBodyRotation;
            palmCenter   = PalmCenter;
            if (side != Side)
            {
                bodyPosition.x = -bodyPosition.x;
                palmCenter.x   = -palmCenter.x;
                bodyRotation   = new Quaternion(bodyRotation.x, -bodyRotation.y, -bodyRotation.z, bodyRotation.w);
            }
        }

        /// <summary>
        /// Рамка ладони руки <paramref name="side" /> в осях корпуса предмета: начало — центр ладони источника, оси —
        /// универсальные оси кисти источника; сдвиг в единицах префаба (<paramref name="k" /> — масштаб корпуса префаба /
        /// <see cref="BodyScale" />). Та же величина, что <c>palmInBody</c> в <see cref="GripPlacement.Evaluate" />:
        /// мир = корпус.position + корпус.rotation · <paramref name="position" />, поворот = корпус.rotation · <paramref name="rotation" />.
        /// Не зависит от граббера цели — snap-точки <c>_orig</c> (<see cref="PackOrigPoses" />).
        /// </summary>
        public void PalmInBody(UxrHandSide side, float k, out Vector3 position, out Quaternion rotation)
        {
            ForSide(side, out Vector3 bodyPosition, out Quaternion bodyRotation, out Vector3 palmCenter);
            rotation = Quaternion.Inverse(bodyRotation);
            position = rotation * ((palmCenter - bodyPosition) * k);
        }

        /// <summary>Поза пальцев руки <paramref name="side" />: как в <c>HandsPackPoseImporter.CreatePoseAsset</c>.</summary>
        public UxrHandDescriptor DescriptorFor(UxrHandSide side) => side == Side ? Hand : Hand.Mirrored();
    }
}
