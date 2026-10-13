using UltimateXR.Core;
using UnityEngine;

namespace VrBattlegrounds.Editor.Avatars
{
    /// <summary>Поза align в системе корня предмета: позиция в локальных единицах корня, поворот относительно корня.</summary>
    public struct GripLocalPose
    {
        public Vector3    Position;
        public Quaternion Rotation;

        public static GripLocalPose Of(Transform root, Transform t) =>
            new GripLocalPose { Position = root.InverseTransformPoint(t.position), Rotation = Quaternion.Inverse(root.rotation) * t.rotation };

        public Vector3 WorldPosition(Transform root) => root.TransformPoint(Position);

        public Quaternion WorldRotation(Transform root) => root.rotation * Rotation;
    }

    /// <summary>Проверка align: куда при snap попадает центр ладони источника относительно центра ладони цели.</summary>
    public sealed class GripPalmCheck
    {
        /// <summary>Расстояние, мм.</summary>
        public double ErrorMm;

        /// <summary>То же в универсальных осях кисти цели, мм: [вправо, вверх (тыл кисти), вперёд (к пальцам)].</summary>
        public double[] ErrorHandMm;

        /// <summary>Поворот корпуса против кадра источника в осях кисти цели, градусы.</summary>
        public double AxesErrorDeg;
    }

    /// <summary>
    /// Посадка одной руки: поза align, при которой корпус предмета стоит относительно кисти цели так же, как в кадре
    /// источника. SDK при snap совмещает align с граббером руки — align и есть граббер в системе корня предмета.
    ///
    /// <para>
    /// Опора <see cref="GripAnchor.Wrist" /> — формула <see cref="HandsPackGripAligner.Place" /> операция в операцию
    /// (сверка R0). Опора <see cref="GripAnchor.PalmCenter" />: <c>корпус = C_цели + Q_цели·(k·(t − c))</c>, где
    /// <c>t</c> — корпус, <c>c</c> — центр ладони источника в осях кисти. Сдвиг <c>s</c> по нормали ладони: предмет
    /// смещается на <c>−s·n</c>, <c>n</c> — SDK <c>GetPalmOutDirection</c>; <c>s &gt; 0</c> — ладонь ближе к предмету.
    /// </para>
    /// </summary>
    public sealed class GripPlacement
    {
        private readonly Transform     _root;
        private readonly GripTargetRig _rig;
        private readonly UxrHandSide   _side;
        private readonly float         _rootScale;
        private readonly Vector3       _bodyPos;
        private readonly Quaternion    _bodyRot;
        private readonly Vector3       _t;
        private readonly Quaternion    _r;
        private readonly Vector3       _c;

        /// <param name="weaponRoot">Корень префаба предмета (содержимое, открытое на чтение или запись)</param>
        /// <param name="body">Корпус предмета — тот же, что корпус источника</param>
        /// <param name="sample">Кадр источника</param>
        /// <param name="side">Рука цели</param>
        /// <param name="rig">Риг цели в нужной позе пальцев (центр ладони зависит от пястных)</param>
        public GripPlacement(Transform weaponRoot, Transform body, GripSample sample, UxrHandSide side, GripTargetRig rig)
        {
            _root      = weaponRoot;
            _rig       = rig;
            _side      = side;
            _rootScale = weaponRoot.lossyScale.x;
            _bodyPos   = weaponRoot.InverseTransformPoint(body.position);
            _bodyRot   = Quaternion.Inverse(weaponRoot.rotation) * body.rotation;
            K          = body.lossyScale.x / sample.BodyScale;
            sample.ForSide(side, out _t, out _r, out _c);
        }

        /// <summary>Масштаб источника → префаб: масштаб корпуса префаба / масштаб корпуса источника.</summary>
        public float K { get; }

        public GripLocalPose Compute(GripAnchor anchor, float normalOffsetMeters)
        {
            Quaternion hand = _rig.HandRotation(_side);

            // Порядок операций для Wrist — как HandsPackGripAligner.Place (зеркало до умножения на k даёт те же биты).
            Vector3    handBodyPos  = anchor == GripAnchor.Wrist ? _t * K : (_t - _c) * K;
            Vector3    origin       = anchor == GripAnchor.Wrist ? _rig.WristPosition(_side) : _rig.PalmCenter(_side);
            Quaternion worldBodyRot = hand * _r;
            Vector3    worldBodyPos = origin + hand * handBodyPos;
            if (normalOffsetMeters != 0f)
            {
                worldBodyPos -= _rig.PalmOut(_side) * normalOffsetMeters;
            }

            Quaternion rootRot = worldBodyRot * Quaternion.Inverse(_bodyRot);
            Vector3    rootPos = worldBodyPos - rootRot * (_bodyPos * _rootScale);

            // Граббер в системе этого корня → та же поза относительно настоящего корня.
            Transform grabber = _rig.Grabber(_side);
            return new GripLocalPose
            {
                Position = Quaternion.Inverse(rootRot) * (grabber.position - rootPos) / _rootScale,
                Rotation = Quaternion.Inverse(rootRot) * grabber.rotation
            };
        }

        /// <summary>
        /// Обратный ход snap: корень предмета по align и грабберу цели, затем центр ладони источника (жёстко с корпусом)
        /// против центра ладони цели, сдвинутого на <paramref name="normalOffsetMeters" />.
        /// </summary>
        public GripPalmCheck Evaluate(GripLocalPose align, float normalOffsetMeters)
        {
            Transform  grabber      = _rig.Grabber(_side);
            Quaternion rootRot      = grabber.rotation * Quaternion.Inverse(align.Rotation);
            Vector3    rootPos      = grabber.position - rootRot * (align.Position * _rootScale);
            Vector3    bodyWorldPos = rootPos + rootRot * (_bodyPos * _rootScale);
            Quaternion bodyWorldRot = rootRot * _bodyRot;

            Vector3    palmInBody = Quaternion.Inverse(_r) * ((_c - _t) * K);
            Vector3    source     = bodyWorldPos + bodyWorldRot * palmInBody;
            Quaternion hand       = _rig.HandRotation(_side);
            Vector3    target     = _rig.PalmCenter(_side) - _rig.PalmOut(_side) * normalOffsetMeters;
            Vector3    error      = source - target;
            Vector3    inHand     = Quaternion.Inverse(hand) * error * 1000f;
            return new GripPalmCheck
            {
                ErrorMm      = GripMath.Round(error.magnitude * 1000f),
                ErrorHandMm  = GripMath.Round(inHand),
                AxesErrorDeg = GripMath.Round(Quaternion.Angle(bodyWorldRot, hand * _r))
            };
        }

        /// <summary>Расхождение двух поз align в мире предмета: мм и градусы.</summary>
        public GripDelta Delta(GripLocalPose a, GripLocalPose b, string path = null) =>
            new GripDelta
            {
                Path = path,
                Mm   = GripMath.Round(Vector3.Distance(a.WorldPosition(_root), b.WorldPosition(_root)) * 1000f),
                Deg  = GripMath.Round(Quaternion.Angle(a.Rotation, b.Rotation))
            };
    }
}
