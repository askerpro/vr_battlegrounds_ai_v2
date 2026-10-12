using UltimateXR.Avatar;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UnityEngine;
using VrBattlegrounds.PhysicalSpaceUtils;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Bots
{
    /// <summary>
    /// Статическая геометрия рук тела бота (режим <see cref="BotBodyMode.PlayerFlow"/>): смещения плеч от глаз и длины рук
    /// из исходной позы префаба тела (<c>PlayerController.SourcePrefab</c>, риг без IK), снимается один раз на тело.
    /// Живой скелет не читается — адаптер источник ввода, как контроллеры человека. Масштаб — калибровка
    /// (<c>AvatarCalibrationApplier.AppliedScale</c>). Используют <see cref="BotWeaponAim"/> (плечи для досягаемости) и
    /// <see cref="BotRuntimeController"/> (поза покоя опущенных рук — <see cref="TryRestHands"/>).
    /// </summary>
    public sealed class BotArmRestGeometry
    {
        /// <summary>Запястье в покое: доля длины руки вниз от плеча.</summary>
        public const float RestDownFraction = 0.9f;
        /// <summary>Запястье в покое: вперёд от плеча и внутрь (к бедру), метры при росте глаз 1,65.</summary>
        public const float RestForward = 0.07f;
        public const float RestInward = 0.05f;
        /// <summary>Наклон пальцев вперёд от вертикали в покое (доля «вперёд» в направлении пальцев).</summary>
        public const float RestFingersForward = 0.15f;
        /// <summary>Правое плечо от глаз в системе корпуса, длина руки — запасные значения, если у рига нет костей руки.</summary>
        public static readonly Vector3 FallbackShoulderFromEye = new Vector3(0.16f, -0.17f, -0.02f);
        public const float FallbackArmLength = 0.55f;

        private bool _captured, _known;
        private Vector3 _rightShoulderFromEye, _leftShoulderFromEye; // исходная поза, система корня, масштаб 1
        private float _rightArm, _leftArm;
        private AvatarCalibrationApplier _applier;

        public Vector3 RightShoulderFromEye => _rightShoulderFromEye;
        public Vector3 LeftShoulderFromEye => _leftShoulderFromEye;
        public float RightArm => _rightArm;
        public float LeftArm => _leftArm;

        /// <summary>Масштаб калибровки тела (1 — нет применителя).</summary>
        public float Scale => _applier != null && _applier.AppliedScale > 0f ? _applier.AppliedScale : 1f;

        /// <summary>
        /// Один раз: смещения плеч от глаз и длины рук из исходной позы префаба тела. false — префаба/костей нет (тогда
        /// плечо оценивается от глаз по <see cref="FallbackShoulderFromEye"/>).
        /// </summary>
        public bool Ensure(Component owner)
        {
            if (_captured) return _known;
            _captured = true;
            PlayerController player = owner != null ? owner.GetComponent<PlayerController>() : null;
            _applier = owner != null ? owner.GetComponent<AvatarCalibrationApplier>() : null;

            GameObject prefab = player != null ? player.SourcePrefab : null;
            UxrAvatar rest = prefab != null ? prefab.GetComponent<UxrAvatar>() : null;
            if (rest == null || rest.AvatarRig == null) return _known = false;

            Transform root = rest.transform;
            Quaternion inverse = Quaternion.Inverse(root.rotation);
            UxrAvatarHead head = rest.AvatarRig.Head;
            Vector3 eyes;
            if (head != null && head.LeftEye != null && head.RightEye != null)
                eyes = (head.LeftEye.position + head.RightEye.position) * 0.5f;
            else if (head != null && head.Head != null && _applier != null && _applier.EyesBaseHeight > 0f)
                eyes = new Vector3(head.Head.position.x, root.position.y + _applier.EyesBaseHeight, head.Head.position.z);
            else return _known = false;

            if (!RestArm(rest, UxrHandSide.Right, out Vector3 rightShoulder, out _rightArm) ||
                !RestArm(rest, UxrHandSide.Left, out Vector3 leftShoulder, out _leftArm)) return _known = false;

            _rightShoulderFromEye = inverse * (rightShoulder - eyes);
            _leftShoulderFromEye = inverse * (leftShoulder - eyes);
            return _known = true;
        }

        /// <summary>Плечо и длина руки в исходной позе префаба (плечевая кость + предплечье до запястья).</summary>
        private static bool RestArm(UxrAvatar rest, UxrHandSide side, out Vector3 shoulder, out float length)
        {
            shoulder = default;
            length = 0f;
            UxrAvatarArm arm = rest.GetArm(side);
            Transform wrist = arm != null && arm.Hand != null ? arm.Hand.Wrist : null;
            if (arm == null || arm.UpperArm == null || arm.Forearm == null || wrist == null) return false;
            shoulder = arm.UpperArm.position;
            length = Vector3.Distance(arm.UpperArm.position, arm.Forearm.position) + Vector3.Distance(arm.Forearm.position, wrist.position);
            return length > 0.05f;
        }

        /// <summary>
        /// Естественная поза опущенных рук: запястье = плечо (глаза + курс корпуса × смещение рига × масштаб) + вниз
        /// <see cref="RestDownFraction"/> длины руки, чуть вперёд и к бедру; пальцы вниз, ладонь к бедру (тыльная сторона
        /// наружу). Ориентация кости — через универсальные оси кисти рига (<c>HandUniversalLocalAxes</c>: up — тыльная
        /// сторона, forward — пальцы); это константа рига, не поза скелета. <paramref name="eye"/> — глаза, которые драйвер
        /// пишет в камеру, <paramref name="body"/> — курс корпуса, <paramref name="heightScale"/> — рост глаз / 1,65.
        /// </summary>
        public bool TryRestHands(UxrAvatar avatar, Component owner, Vector3 eye, Quaternion body, float heightScale,
                                 out Pose left, out Pose right)
        {
            left = right = default;
            if (avatar == null) return false;
            bool known = Ensure(owner);
            float scale = Scale;
            bool l = RestHand(avatar, UxrHandSide.Left, known, scale, eye, body, heightScale, out left);
            bool r = RestHand(avatar, UxrHandSide.Right, known, scale, eye, body, heightScale, out right);
            return l && r;
        }

        private bool RestHand(UxrAvatar avatar, UxrHandSide side, bool known, float scale, Vector3 eye, Quaternion body,
                              float heightScale, out Pose hand)
        {
            hand = default;
            bool isRight = side == UxrHandSide.Right;
            Vector3 fallback = isRight ? FallbackShoulderFromEye : Vector3.Scale(FallbackShoulderFromEye, new Vector3(-1f, 1f, 1f));
            Vector3 shoulderLocal = known ? (isRight ? _rightShoulderFromEye : _leftShoulderFromEye) * scale : fallback * heightScale;
            float arm = known ? (isRight ? _rightArm : _leftArm) * scale : FallbackArmLength * heightScale;

            Vector3 forward = body * Vector3.forward;
            Vector3 outward = body * (isRight ? Vector3.right : Vector3.left);
            Vector3 shoulder = eye + body * shoulderLocal;
            Vector3 wrist = shoulder + Vector3.down * (RestDownFraction * arm) + forward * (RestForward * heightScale) -
                            outward * (RestInward * heightScale);

            // Пальцы вниз (чуть вперёд), тыльная сторона наружу — ладонь к бедру.
            Quaternion universal = Quaternion.LookRotation((Vector3.down + forward * RestFingersForward).normalized, outward);
            UxrAvatarArmInfo info = avatar.AvatarRigInfo != null ? avatar.AvatarRigInfo.GetArmInfo(side) : null;
            if (info == null || info.HandUniversalLocalAxes == null) return false;
            hand = new Pose(wrist, universal * info.HandUniversalLocalAxes.UniversalToActualAxesRotation);
            return true;
        }
    }
}
