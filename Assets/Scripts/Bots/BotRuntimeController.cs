using System;
using Mirror;
using Unity.Profiling;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UltimateXR.Mechanics.Weapons;
using UnityEngine;
using VrBattlegrounds.Arsenal;
using VrBattlegrounds.Network;
using VrBattlegrounds.Player;

namespace VrBattlegrounds.Bots
{
    /// <summary>
    /// Порядок кадра бота в режиме <see cref="BotBodyMode.PlayerFlow"/> (вариант P «кукловод»). Только сервер/хост,
    /// только аватар без владельца. Владельцы: корень/камера/кисти — <see cref="BotAvatarDriver"/>; ноги, курс и взгляд —
    /// <see cref="BotBody"/> (от Blaze-рига/навигатора); оружие, цель и выстрел — <see cref="BotGunner"/>; хват —
    /// <see cref="BotHands"/>; поза оружия — <see cref="BotWeaponAim"/>. Этот компонент только вызывает их по порядку.
    ///
    /// <para>Кадр сервера:</para>
    /// <list type="number">
    /// <item>Update (порядок 11000, после Blaze): ноги/курс (<see cref="BotBody.PrepareFrame"/>), сборка
    /// <see cref="BotInputFrame"/> → <see cref="BotAvatarDriver.Submit"/>.</item>
    /// <item><c>UxrManager.PostUpdate</c> (LateUpdate, порядок 0): драйвер пишет позы перед Manipulation; дальше штатно
    /// Manipulation → Animation → PostProcess (IK).</item>
    /// <item>LateUpdate (порядок 11000, после всех стадий): видимость с каденцией <see cref="SeesIntervalFrames"/>,
    /// ошибка фактического дула (поправка следующего кадра), выстрел по запросу Blaze (гейт — луч дула), затем хват/сброс.
    /// Действия SDK — только отсюда, не из обработчиков событий UXR.</item>
    /// </list>
    /// </summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(11000)]
    [RequireComponent(typeof(BotBody), typeof(BotGunner), typeof(BotAvatarDriver))]
    [RequireComponent(typeof(BotHands), typeof(BotWeaponAim))]
    public sealed class BotRuntimeController : MonoBehaviour
    {
        private static readonly ProfilerMarker UpdateMarker = new ProfilerMarker("BotRuntimeController.Update");
        private static readonly ProfilerMarker LateUpdateMarker = new ProfilerMarker("BotRuntimeController.LateUpdate");
        private static readonly ProfilerMarker ShotMarker = new ProfilerMarker("BotRuntimeController.Shot");
        private static readonly ProfilerMarker ShotSampledMarker = new ProfilerMarker("BotRuntimeController.ShotSampled");

        /// <summary>Опущенный ствол без цели: точка прицеливания относительно глаз в системе корпуса, метры при росте 1,65.</summary>
        private static readonly Vector3 LowReadyFromEye = new Vector3(0.1f, -1.2f, 3f);

        /// <summary>Каденция raycast-а видимости (<see cref="BotGunner.RefreshSees"/>), кадры. Гейт выстрела — каждый запрос.</summary>
        public int SeesIntervalFrames = BotPlayerFlowSettings.SeesIntervalFrames;

        /// <summary>Замер каждого запроса выстрела (поза кадра после всех стадий). Только чтение — стенд.</summary>
        public event Action<BotShotSample> ShotSampled;

        private BotBody _body;
        private BotGunner _gunner;
        private BotAvatarDriver _driver;
        private BotHands _hands;
        private BotWeaponAim _aim;
        private PlayerController _player;
        private UxrAvatar _avatar;
        private BotInputFrame _frame;
        private int _seesPhase;
        private readonly int[] _outcomes = new int[(int)BotShotOutcome.Fired + 1];

        /// <summary>Последний собранный кадр намерений.</summary>
        public BotInputFrame LastFrame => _frame;

        /// <summary>Сколько запросов Blaze закончились исходом <paramref name="outcome"/>.</summary>
        public int OutcomeCount(BotShotOutcome outcome) => _outcomes[(int)outcome];

        private bool CanControl => NetworkServer.active && StateEventAuthority.IsWorldAuthority && !NetworkServer.isLoadingScene &&
                                   _player != null && _avatar != null && _driver != null && _driver.CanDrive;

        private void Awake()
        {
            _body = GetComponent<BotBody>();
            _gunner = GetComponent<BotGunner>();
            _driver = GetComponent<BotAvatarDriver>();
            _hands = GetComponent<BotHands>();
            _aim = GetComponent<BotWeaponAim>();
            _player = GetComponent<PlayerController>();
            _avatar = GetComponent<UxrAvatar>();
        }

        private void OnEnable()
        {
            if (!NetworkServer.active || !StateEventAuthority.IsWorldAuthority)
            {
                enabled = false;
                return;
            }
            _gunner.SetPlayerFlow(true);
            // Разнести raycast-ы видимости разных ботов по кадрам.
            _seesPhase = (GetHashCode() & 0x7fffffff) % Mathf.Max(1, SeesIntervalFrames);
        }

        private void OnDisable()
        {
            if (_gunner != null) _gunner.SetPlayerFlow(false);
            if (_aim != null) _aim.ResetCompensation();
        }

        private void Update()
        {
            using (UpdateMarker.Auto())
            {
                if (!CanControl) return;
                _body.PrepareFrame();
                BuildFrame();
                _driver.Submit(_frame);
            }
        }

        private void LateUpdate()
        {
            using (LateUpdateMarker.Auto())
            {
                if (!CanControl) return;

                int interval = Mathf.Max(1, SeesIntervalFrames);
                if (interval == 1 || (Time.frameCount + _seesPhase) % interval == 0) _gunner.RefreshSees();

                // Ошибка фактического дула этого кадра — поправка отдачи следующего.
                bool engaged = _gunner.HasAimPoint && _gunner.Firearm != null;
                if (engaged) _aim.ObserveMuzzle(transform, _gunner.Muzzle, _gunner.AimPoint, Time.deltaTime, true);
                else _aim.ResetCompensation();

                if (_gunner.ShotRequested)
                using (ShotMarker.Auto())
                {
                    var sample = new BotShotSample
                    {
                        Frame = Time.frameCount, Time = Time.time,
                        HasError = engaged && _aim.HasMuzzleError, Error = _aim.LastMuzzleError, Compensation = _aim.Compensation
                    };
                    sample.Outcome = _gunner.ExecuteRequestedShot();
                    sample.GateAimErrorDeg = _gunner.LastGateAimErrorDeg;
                    sample.Blocker = _gunner.LastBlocker;
                    _outcomes[(int)sample.Outcome]++;
                    // Подписчики — только стенд (проба): их стоимость отдельно от стоимости бота.
                    using (ShotSampledMarker.Auto())
                    {
                        ShotSampled?.Invoke(sample);
                    }
                }

                // Хват/сброс — после выстрела: поза кадра для гейта не меняется посреди LateUpdate.
                _hands.Tick(_frame, _gunner.SelectedFirearm);
            }
        }

        private void BuildFrame()
        {
            var frame = new BotInputFrame { Frame = Time.frameCount };
            Vector3 feet = _body.Feet;
            Quaternion body = _body.BodyRotation;
            frame.RootPosition = feet;
            frame.RootRotation = body;

            // Голова — как HMD в пространстве трекинга: локально к пивоту камеры, на высоте глаз калибровки.
            // Пивот пишет только AvatarCalibrationApplier; здесь его поза относительно корня только читается.
            Transform root = transform;
            Transform pivot = _avatar.CameraController;
            Quaternion rootInverse = Quaternion.Inverse(root.rotation);
            Vector3 pivotInRoot = pivot != null ? rootInverse * (pivot.position - root.position) : Vector3.zero;
            Quaternion pivotRotationInRoot = pivot != null ? rootInverse * pivot.rotation : Quaternion.identity;
            float scale = pivot != null ? pivot.lossyScale.y : root.lossyScale.y;
            Quaternion pivotRotation = body * pivotRotationInRoot;

            float eyeHeight = EyeHeight();
            float heightScale = eyeHeight / BotPlayerFlowSettings.EyeHeight;
            Vector3 headLocal = new Vector3(0f, eyeHeight, 0f);
            Vector3 eye = feet + body * pivotInRoot + pivotRotation * (headLocal * scale);
            frame.HeadPosition = headLocal;
            Quaternion head = _body.HeadRotation(eye);
            frame.HeadRotation = Quaternion.Inverse(pivotRotation) * head;

            if (_body.TryGetRestHands(out Pose restLeft, out Pose restRight))
            {
                frame.HasLeftHand = frame.HasRightHand = true;
                frame.LeftHand = restLeft;
                frame.RightHand = restRight;
            }

            UxrFirearmWeapon firearm = _gunner.SelectedFirearm;
            bool resolved = BotHands.TryResolveGrips(firearm, out UxrGrabbableObject grip, out int primary, out int support);
            if (resolved && _hands.Holds(grip, primary, UxrHandSide.Right))
            {
                bool aiming = _gunner.HasAimPoint;
                Vector3 aimPoint = aiming ? _gunner.AimPoint : eye + body * (LowReadyFromEye * heightScale);
                bool rifle = _gunner.WeaponCategory == WeaponCategory.Rifle;
                if (_aim.TrySolve(_avatar, _gunner.Muzzle, grip, primary, support, rifle, body, eye, head, heightScale, aimPoint, aiming,
                                  out Pose right, out Pose left, out bool hasLeft))
                {
                    frame.HasRightHand = true;
                    frame.RightHand = right;
                    if (hasLeft)
                    {
                        frame.HasLeftHand = true;
                        frame.LeftHand = left;
                    }
                    frame.GrabSupport = hasLeft && !_hands.Holds(grip, support, UxrHandSide.Left);
                }
            }
            else if (resolved && _player.IsAlive && UxrGrabManager.HasInstance && !UxrGrabManager.Instance.IsBeingGrabbed(grip) &&
                     _aim.TryReach(_avatar, grip, primary, UxrHandSide.Right, out Pose reach))
            {
                // Подвести правую кисть к снапу основной рукояти; хват — BotHands после всех стадий.
                frame.HasRightHand = true;
                frame.RightHand = reach;
                frame.GrabPrimary = true;
            }

            // Сброс — только чтобы освободить руки под выбранное оружие; без выбора ничего не отпускаем.
            frame.Release = resolved && (HoldsForeign(UxrHandSide.Right, grip) || HoldsForeign(UxrHandSide.Left, grip));
            frame.Trigger = _gunner.ShotRequested;
            _frame = frame;
        }

        private bool HoldsForeign(UxrHandSide side, UxrGrabbableObject keep)
        {
            UxrGrabber grabber = _avatar.GetGrabber(side);
            UxrGrabbableObject held = grabber != null ? grabber.GrabbedObject : null;
            return held != null && held != keep;
        }

        private float EyeHeight()
        {
            var session = _player.Session;
            return session != null && session.Calibration.HasEyeHeight ? session.Calibration.EyeHeight : BotPlayerFlowSettings.EyeHeight;
        }
    }
}
