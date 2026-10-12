using Unity.Profiling;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Rig;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UnityEngine;
using VrBattlegrounds.PhysicalSpaceUtils;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Bots
{
    /// <summary>
    /// Режим <see cref="BotBodyMode.PlayerFlow"/>: желаемая поза оружия W* и позы кистей из неё.
    ///
    /// <para>
    /// W*: ствол (<c>ShotSource</c>) смотрит в точку прицеливания ИИ, верх — по мировой вертикали, без завала;
    /// основная рукоять стоит в точке хвата перед плечом (<see cref="RifleHold"/>/<see cref="PistolHold"/>), направление
    /// ограничено зоной досягаемости рук (курс и тангаж относительно корпуса). Правая кисть = W* × снап основной
    /// рукояти × (граббер→запястье), левая = W* × снап поддержки × (граббер→запястье). Геометрия снапа не кешируется:
    /// каждый вызов читает её у SDK (<c>ComputeRequiredGrabberTransform</c>) и переводит в систему оружия.
    /// </para>
    ///
    /// <para>
    /// Неточность — через точку прицеливания ИИ (её выбирает <see cref="BotGunner"/>). Отдача: после всех стадий
    /// измеряется ошибка дула (<see cref="BotAimMath.TryGetMuzzleError"/>), её разомкнутая оценка сглаживается
    /// (<see cref="CompensationSeconds"/>), а поправка гасит только долю <see cref="RecoilCompensation"/> —
    /// компенсация человекоподобная, неполная: отдача добавляет разброс. Управление не читает кости кистей.
    /// </para>
    ///
    /// <para>
    /// <b>Досягаемость (pf-round1).</b> Рука UXR (<c>UxrArmIKSolver</c>, <c>LimitHandReach</c>) не дотягивается дальше
    /// «плечо + плечевая кость + предплечье» и укорачивает кисть — оружие уходит за кистью, а ствол — с прицела
    /// (PPK: кисти стабильно в 128–133 мм от команды, ошибка дула 7,6°). Поэтому хват строится от плеча, а W*
    /// оттягивается вдоль ствола к корпусу, пока обе кисти не окажутся в доле <see cref="ReachFraction"/> длины руки.
    /// </para>
    ///
    /// <para>
    /// <b>Геометрия плеч — статическая, выход IK не читается</b> (решение 2026-10-12: адаптер — источник ввода, как
    /// контроллеры человека). Один раз из префаба тела (<c>PlayerController.SourcePrefab</c>, исходная поза рига без IK)
    /// снимаются смещения плеч (<c>UpperArm</c>) от глаз (середина костей глаз рига; без них — голова на
    /// <c>EyesBaseHeight</c>) в системе корня и длины рук (плечевая кость + предплечье до запястья). В кадре плечо =
    /// глаза, которые драйвер пишет в камеру, + курс головы × смещение × масштаб калибровки
    /// (<c>AvatarCalibrationApplier.AppliedScale</c>). Задержку доворота торса UXR и наклон корпуса покрывает запас
    /// <see cref="ReachFraction"/> и отвод, а не чтение костей.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BotWeaponAim : MonoBehaviour
    {
        private static readonly ProfilerMarker SolveMarker = new ProfilerMarker("BotWeaponAim.Solve");

        /// <summary>Основная рукоять винтовки относительно правого плеча в системе прицела (вправо, вверх, вперёд), метры при росте глаз 1,65.</summary>
        public Vector3 RifleHold = new Vector3(-0.06f, -0.08f, 0.24f);

        /// <summary>Основная рукоять пистолета относительно правого плеча в системе прицела, метры при росте глаз 1,65.</summary>
        public Vector3 PistolHold = new Vector3(-0.17f, -0.02f, 0.36f);

        /// <summary>Правое плечо относительно глаз в системе корпуса — только запасной вариант, когда у рига нет костей руки.</summary>
        public Vector3 ShoulderFromEye = new Vector3(0.16f, -0.17f, -0.02f);

        /// <summary>Доля длины руки, дальше которой запястье не ставится (запас на ключицу/сглаживание, локоть чуть согнут).</summary>
        public float ReachFraction = 0.94f;

        /// <summary>Предел отвода оружия к корпусу ради досягаемости, метры.</summary>
        public float MaxReachPullback = 0.35f;

        /// <summary>Предел курса ствола относительно корпуса, градусы (досягаемость рук).</summary>
        public float MaxYawFromBody = 60f;

        /// <summary>Предел тангажа ствола, градусы.</summary>
        public float MaxPitch = 70f;

        /// <summary>Доля сглаженной ошибки дула, которую гасит поправка (0 — нет компенсации отдачи, 1 — полная).</summary>
        public float RecoilCompensation = 0.7f;

        /// <summary>Постоянная времени оценки ошибки дула, секунды.</summary>
        public float CompensationSeconds = 0.25f;

        /// <summary>Предел поправки по каждой оси, градусы.</summary>
        public float MaxCompensationDegrees = 8f;

        private Vector2 _openLoop;     // x — тангаж, y — курс, градусы
        private Vector2 _compensation; // применяемая поправка, градусы

        private bool _restCaptured, _restKnown;
        private Vector3 _rightShoulderFromEye, _leftShoulderFromEye; // исходная поза, система корня, масштаб 1
        private float _rightArmRest, _leftArmRest;
        private PlayerController _player;
        private AvatarCalibrationApplier _applier;

        /// <summary>Последнее решение: насколько W* отведено к корпусу ради досягаемости, метры (0 — не понадобилось).</summary>
        public float LastReachPullback { get; private set; }

        /// <summary>Последнее решение упёрлось в <see cref="MaxReachPullback"/> — кисти могут не дотянуться.</summary>
        public bool LastReachLimited { get; private set; }

        /// <summary>W* последнего решения: желаемая мировая поза трансформа хвата (<c>grip.transform</c>). Для замера стенда.</summary>
        public Pose LastWeaponPose { get; private set; }

        /// <summary>Кадр Unity последнего успешного решения (-1 — не было).</summary>
        public int LastSolveFrame { get; private set; } = -1;

        /// <summary>Ошибка дула последнего наблюдения (после всех стадий кадра).</summary>
        public BotAimMath.MuzzleError LastMuzzleError { get; private set; }
        public bool HasMuzzleError { get; private set; }

        /// <summary>Применяемая поправка (тангаж, курс), градусы.</summary>
        public Vector2 Compensation => _compensation;

        /// <summary>Сбросить оценку отдачи (нет цели, нет оружия, смена тела).</summary>
        public void ResetCompensation()
        {
            _openLoop = Vector2.zero;
            _compensation = Vector2.zero;
            HasMuzzleError = false;
        }

        /// <summary>
        /// Поза кисти, при которой граббер стоит на снапе точки <paramref name="point"/> предмета в его текущей позе
        /// (подвести руку к лежащему/висящему в кобуре оружию). false — SDK не дал снап.
        /// </summary>
        public bool TryReach(UxrAvatar avatar, UxrGrabbableObject grip, int point, UxrHandSide side, out Pose hand)
        {
            hand = default;
            UxrGrabber grabber = avatar != null ? avatar.GetGrabber(side) : null;
            Transform bone = avatar != null ? avatar.GetHandBone(side) : null;
            if (grabber == null || bone == null || grip == null) return false;
            if (!grip.ComputeRequiredGrabberTransform(grabber, point, out Vector3 position, out Quaternion rotation, false)) return false;
            hand = HandFromGrabber(bone, grabber.transform, position, rotation);
            return true;
        }

        private void Awake()
        {
            _player = GetComponent<PlayerController>();
        }

        /// <summary>
        /// Один раз: смещения плеч от глаз и длины рук из исходной позы префаба тела (не из живого скелета — его пишет IK).
        /// false — префаба/костей нет; тогда плечо оценивается от глаз (<see cref="ShoulderFromEye"/>) без ограничения
        /// досягаемости.
        /// </summary>
        private bool EnsureRestGeometry()
        {
            if (_restCaptured) return _restKnown;
            _restCaptured = true;
            _applier = GetComponent<AvatarCalibrationApplier>();

            GameObject prefab = _player != null ? _player.SourcePrefab : null;
            UxrAvatar rest = prefab != null ? prefab.GetComponent<UxrAvatar>() : null;
            if (rest == null || rest.AvatarRig == null) return _restKnown = false;

            Transform root = rest.transform;
            Quaternion inverse = Quaternion.Inverse(root.rotation);
            UxrAvatarHead head = rest.AvatarRig.Head;
            Vector3 eyes;
            if (head != null && head.LeftEye != null && head.RightEye != null)
                eyes = (head.LeftEye.position + head.RightEye.position) * 0.5f;
            else if (head != null && head.Head != null && _applier != null && _applier.EyesBaseHeight > 0f)
                eyes = new Vector3(head.Head.position.x, root.position.y + _applier.EyesBaseHeight, head.Head.position.z);
            else return _restKnown = false;

            if (!RestArm(rest, UxrHandSide.Right, out Vector3 rightShoulder, out _rightArmRest) ||
                !RestArm(rest, UxrHandSide.Left, out Vector3 leftShoulder, out _leftArmRest)) return _restKnown = false;

            _rightShoulderFromEye = inverse * (rightShoulder - eyes);
            _leftShoulderFromEye = inverse * (leftShoulder - eyes);
            return _restKnown = true;
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
        /// Решить W* и позы кистей. <paramref name="body"/> — курс корпуса (корень этого кадра), <paramref name="eye"/> и
        /// <paramref name="head"/> — мировые глаза и поворот головы, которые драйвер запишет в камеру,
        /// <paramref name="heightScale"/> — рост глаз / 1,65. <paramref name="compensate"/> — применять поправку отдачи.
        /// </summary>
        public bool TrySolve(UxrAvatar avatar, Transform muzzle, UxrGrabbableObject grip, int primary, int support,
                             bool rifle, Quaternion body, Vector3 eye, Quaternion head, float heightScale, Vector3 aimPoint, bool compensate,
                             out Pose rightHand, out Pose leftHand, out bool hasLeft)
        {
            using (SolveMarker.Auto())
            {
                rightHand = leftHand = default;
                hasLeft = false;
                LastReachPullback = 0f;
                LastReachLimited = false;
                if (avatar == null || muzzle == null || grip == null) return false;

                UxrGrabber right = avatar.GetGrabber(UxrHandSide.Right);
                Transform rightBone = avatar.GetHandBone(UxrHandSide.Right);
                if (right == null || rightBone == null) return false;

                // Геометрия читается у SDK в этом вызове и переводится в систему оружия (не кешируется).
                Transform weapon = grip.transform;
                Quaternion weaponInverse = Quaternion.Inverse(weapon.rotation);
                Vector3 muzzleLocal = weaponInverse * (muzzle.position - weapon.position);
                Quaternion muzzleLocalRotation = weaponInverse * muzzle.rotation;

                if (!grip.ComputeRequiredGrabberTransform(right, primary, out Vector3 primaryPosition, out Quaternion primaryRotation, false))
                    return false;
                Vector3 primaryLocal = weaponInverse * (primaryPosition - weapon.position);
                Quaternion primaryLocalRotation = weaponInverse * primaryRotation;
                GrabberOffset(rightBone, right.transform, out Vector3 rightOffset, out Quaternion rightOffsetRotation);

                UxrGrabber left = support >= 0 ? avatar.GetGrabber(UxrHandSide.Left) : null;
                Transform leftBone = left != null ? avatar.GetHandBone(UxrHandSide.Left) : null;
                Vector3 supportLocal = default;
                Quaternion supportLocalRotation = Quaternion.identity;
                Vector3 leftOffset = default;
                Quaternion leftOffsetRotation = Quaternion.identity;
                if (left != null && leftBone != null &&
                    grip.ComputeRequiredGrabberTransform(left, support, out Vector3 supportPosition, out Quaternion supportRotation, false))
                {
                    supportLocal = weaponInverse * (supportPosition - weapon.position);
                    supportLocalRotation = weaponInverse * supportRotation;
                    GrabberOffset(leftBone, left.transform, out leftOffset, out leftOffsetRotation);
                    hasLeft = true;
                }

                // Плечи этого кадра — из ввода (глаза и курс головы, которые пишет драйвер) и исходной позы рига, без IK.
                bool restKnown = EnsureRestGeometry();
                float scale = _applier != null && _applier.AppliedScale > 0f ? _applier.AppliedScale : 1f;
                Quaternion headYaw = Quaternion.Euler(0f, head.eulerAngles.y, 0f);
                Vector3 rightShoulder = restKnown ? eye + headYaw * (_rightShoulderFromEye * scale) : eye + body * (ShoulderFromEye * heightScale);
                Vector3 leftShoulder = restKnown ? eye + headYaw * (_leftShoulderFromEye * scale) : Vector3.zero;
                float rightArm = _rightArmRest * scale, leftArm = _leftArmRest * scale;
                Vector3 hold = (rifle ? RifleHold : PistolHold) * heightScale;
                float bodyYaw = body.eulerAngles.y;

                // Дуло зависит от позы оружия, поза — от направления и отвода ради досягаемости: несколько итераций.
                Vector3 direction = aimPoint - rightShoulder;
                Quaternion weaponRotation = weapon.rotation;
                Vector3 grabPoint = rightShoulder;
                Quaternion rightRotation = Quaternion.identity, leftRotation = Quaternion.identity;
                Vector3 rightWrist = default, leftWrist = default;
                float pullback = 0f;
                for (int i = 0; i < 5; i++)
                {
                    Quaternion aim = Aim(direction, bodyYaw, compensate);
                    Vector3 forward = aim * Vector3.forward;
                    grabPoint = rightShoulder + aim * hold - forward * pullback;
                    weaponRotation = aim * Quaternion.Inverse(muzzleLocalRotation);
                    Vector3 weaponPosition = grabPoint - weaponRotation * primaryLocal;

                    rightRotation = weaponRotation * primaryLocalRotation * Quaternion.Inverse(rightOffsetRotation);
                    rightWrist = grabPoint - rightRotation * rightOffset;
                    if (hasLeft)
                    {
                        leftRotation = weaponRotation * supportLocalRotation * Quaternion.Inverse(leftOffsetRotation);
                        leftWrist = weaponPosition + weaponRotation * supportLocal - leftRotation * leftOffset;
                    }

                    // Досягаемость: насколько дальше доли длины руки стоят запястья; отвод — вдоль ствола к корпусу.
                    // В последней итерации отвод не меняется: возвращаемые кисти соответствуют применённому отводу.
                    if (restKnown && i < 4)
                    {
                        float need = Excess(rightWrist, rightShoulder, rightArm, forward);
                        if (hasLeft) need = Mathf.Max(need, Excess(leftWrist, leftShoulder, leftArm, forward));
                        if (need > 1e-4f)
                        {
                            pullback = Mathf.Min(MaxReachPullback, pullback + need);
                            LastReachLimited = pullback >= MaxReachPullback;
                        }
                    }

                    Vector3 muzzleAt = weaponPosition + weaponRotation * muzzleLocal;
                    Vector3 next = aimPoint - muzzleAt;
                    if (next.sqrMagnitude > 1e-4f) direction = next;
                }

                LastReachPullback = pullback;
                LastWeaponPose = new Pose(grabPoint - weaponRotation * primaryLocal, weaponRotation);
                LastSolveFrame = Time.frameCount;
                rightHand = new Pose(rightWrist, rightRotation);
                if (hasLeft) leftHand = new Pose(leftWrist, leftRotation);
                return true;
            }
        }

        /// <summary>
        /// Сколько отвести оружие вдоль <paramref name="forward"/>, чтобы запястье вошло в долю длины руки (≥ 0). Отвод на
        /// d укорачивает плечо→запястье примерно на d·cos(угол между ними), поэтому делится на проекцию (не меньше 0,3).
        /// </summary>
        private float Excess(Vector3 wrist, Vector3 shoulder, float armLength, Vector3 forward)
        {
            Vector3 reach = wrist - shoulder;
            float distance = reach.magnitude;
            float excess = distance - ReachFraction * armLength;
            if (excess <= 0f || distance < 1e-4f) return 0f;
            return excess / Mathf.Max(0.3f, Vector3.Dot(reach / distance, forward));
        }

        /// <summary>
        /// Замер после всех стадий кадра (LateUpdate): ошибка фактического дула к точке прицеливания и оценка отдачи.
        /// <paramref name="root"/> — корень бота (система метрики <see cref="BotAimMath"/>).
        /// </summary>
        public void ObserveMuzzle(Transform root, Transform muzzle, Vector3 aimPoint, float deltaTime, bool learn)
        {
            if (root == null || muzzle == null ||
                !BotAimMath.TryGetMuzzleError(root, muzzle.position, muzzle.forward, aimPoint, out BotAimMath.MuzzleError error))
            {
                HasMuzzleError = false;
                return;
            }

            LastMuzzleError = error;
            HasMuzzleError = true;
            if (!learn) return;

            // Разомкнутая ошибка: что показал бы ствол без поправки (+ — выше/правее цели).
            Vector2 openLoop = new Vector2(error.Pitch + _compensation.x, error.Yaw + _compensation.y);
            float blend = 1f - Mathf.Exp(-Mathf.Max(0f, deltaTime) / Mathf.Max(0.01f, CompensationSeconds));
            _openLoop = Vector2.Lerp(_openLoop, openLoop, blend);
            _compensation = new Vector2(
                Mathf.Clamp(_openLoop.x * RecoilCompensation, -MaxCompensationDegrees, MaxCompensationDegrees),
                Mathf.Clamp(_openLoop.y * RecoilCompensation, -MaxCompensationDegrees, MaxCompensationDegrees));
        }

        /// <summary>Поворот ствола: на точку, в пределах досягаемости, с поправкой отдачи; верх — мировая вертикаль.</summary>
        private Quaternion Aim(Vector3 direction, float bodyYaw, bool compensate)
        {
            if (direction.sqrMagnitude < 1e-6f) direction = Quaternion.Euler(0f, bodyYaw, 0f) * Vector3.forward;
            float yaw = BotAimMath.YawOf(direction);
            float pitch = BotAimMath.PitchOf(direction);
            if (compensate)
            {
                pitch -= _compensation.x;
                yaw -= _compensation.y;
            }
            yaw = bodyYaw + Mathf.Clamp(Mathf.DeltaAngle(bodyYaw, yaw), -MaxYawFromBody, MaxYawFromBody);
            pitch = Mathf.Clamp(pitch, -MaxPitch, MaxPitch);
            // Euler: x положительный опускает ствол, поэтому тангаж со знаком минус.
            return Quaternion.Euler(-pitch, yaw, 0f);
        }

        /// <summary>
        /// Поза кости кисти, при которой её граббер встанет в (<paramref name="position"/>, <paramref name="rotation"/>).
        /// Связь граббер→запястье — константа рига, читается относительной (не обратная связь по позе кисти).
        /// </summary>
        private static Pose HandFromGrabber(Transform bone, Transform grabber, Vector3 position, Quaternion rotation)
        {
            GrabberOffset(bone, grabber, out Vector3 offset, out Quaternion offsetRotation);
            Quaternion handRotation = rotation * Quaternion.Inverse(offsetRotation);
            return new Pose(position - handRotation * offset, handRotation);
        }

        /// <summary>Граббер в системе кости кисти (мировые единицы): константа рига, не поза кисти.</summary>
        private static void GrabberOffset(Transform bone, Transform grabber, out Vector3 offset, out Quaternion offsetRotation)
        {
            Quaternion boneInverse = Quaternion.Inverse(bone.rotation);
            offset = boneInverse * (grabber.position - bone.position);
            offsetRotation = boneInverse * grabber.rotation;
        }
    }
}
