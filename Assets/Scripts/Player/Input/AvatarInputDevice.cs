// Устройство ввода аватара: кадр источника → камера, контроллеры и кнопки аватара
// (контракт tasks/bots-input/contracts/bot-input-design.md). Компилируется во всех сборках: им пользуются боты на
// сервере; скриптовый клиент — через E2EScriptedInput (вырезается VRBG_NO_E2E).
using System.Collections.Generic;
using Mirror;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Devices;
using UltimateXR.Devices.Integrations;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.DevTools.E2E.Input;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.Player.Input
{
    /// <summary>
    /// Единственный владелец ввода одного аватара: включает в нём скриптовые контроллеры и трекинг (лежат выключенными в
    /// префабе) и раз за кадр применяет кадр источника (<see cref="IAvatarInputSource"/>) штатными путями UltimateXR.
    /// Дальше ввод читает код человека: <c>UxrStandardAvatarController</c> (хват по Grip), <c>UxrControllerTracking</c>
    /// (запястья), <c>UxrFirearmWeapon</c> (курок), IK и ноги.
    ///
    /// <para>
    /// <b>Кому.</b> Local-аватару этого процесса — только при открытом <see cref="ScriptedInputGate"/> (скриптовый клиент,
    /// E2E). Внешнему аватару — только на сервере под <see cref="StateEventAuthority.IsWorldAuthority"/> и только без
    /// владельца-соединения (бот); такому аватару ставится <c>UxrAvatar.InputDriven</c> (UltimateXR patch 89).
    /// </para>
    ///
    /// <para>
    /// <b>Один источник на аватар.</b> Подключение отказывает, если у аватара уже есть устройство или включён другой
    /// ввод (кроме gamepad). Компоненты без заявки устройства выключают себя сами.
    /// </para>
    ///
    /// <para>
    /// <b>Один писатель.</b> Local pose камеры к пивоту (<c>UxrAvatar.CameraController</c>) пишется только здесь — в
    /// <c>UxrManager.StageUpdating(Update)</c> до <c>UpdateAvatar</c> контроллера и сразу при подключении (чтобы новое
    /// тело не жило кадр с головой в позе префаба). Позы контроллеров уходят в <see cref="ScriptedControllerTracking"/>
    /// (запястья ставит SDK), кнопки — в <see cref="ScriptedControllerInput"/>. Корень, кости, пивот, BodyIK и калибровка
    /// не пишутся: корень стоит, движется голова (физическая ходьба).
    /// </para>
    ///
    /// <para>
    /// <b>Пространство трекинга</b> — пивот камеры, как у шлема. Позы контроллеров переводятся в пространство корня
    /// аватара минус <c>HeightOffset</c> — то, что SDK ждёт в <c>UpdateSensor</c>.
    /// </para>
    /// </summary>
    public sealed class AvatarInputDevice
    {
        private const string Tag = "[AvatarInputDevice]";

        // Ключ — InstanceID аватара: он доступен и у уничтоженного объекта, снятие не зависит от Unity-null сравнения.
        private static readonly Dictionary<int, AvatarInputDevice> s_devices = new Dictionary<int, AvatarInputDevice>();
        private static readonly List<AvatarInputDevice> s_active = new List<AvatarInputDevice>();
        private static readonly List<AvatarInputDevice> s_stale = new List<AvatarInputDevice>();
        private static bool s_subscribed;

        private readonly IAvatarInputSource _source;
        private readonly ScriptedControllerInput _input;
        private readonly ScriptedControllerTracking _tracking;
        private readonly int _avatarId;
        private DeviceFrame _frame;
        private bool _hasPose;
        private bool _connected;

        private AvatarInputDevice(UxrAvatar avatar, IAvatarInputSource source, ScriptedControllerInput input,
                                  ScriptedControllerTracking tracking)
        {
            Avatar = avatar;
            _avatarId = avatar.GetInstanceID();
            _source = source;
            _input = input;
            _tracking = tracking;
        }

        /// <summary>Аватар, которому подаётся ввод.</summary>
        public UxrAvatar Avatar { get; }

        /// <summary>Источник ввода этого устройства.</summary>
        public IAvatarInputSource Source => _source;

        /// <summary>Устройство подключено и применяет кадры.</summary>
        public bool IsConnected => _connected;

        /// <summary>Трекинг аватара (смещение «сенсор → запястье» для пилота).</summary>
        public ScriptedControllerTracking Tracking => _tracking;

        /// <summary>Пространство трекинга — пивот камеры аватара (корень, если пивота нет).</summary>
        public Transform TrackingSpace => Avatar == null ? null : Avatar.CameraController != null ? Avatar.CameraController : Avatar.transform;

        /// <summary>Последний применённый кадр.</summary>
        public DeviceFrame LastFrame => _frame;

        /// <summary>Сколько кадров применено.</summary>
        public int AppliedFrames { get; private set; }

        /// <summary>Устройство аватара; null — ввод аватару не подаётся.</summary>
        public static AvatarInputDevice For(UxrAvatar avatar) =>
            avatar != null && s_devices.TryGetValue(avatar.GetInstanceID(), out AvatarInputDevice device) ? device : null;

        /// <summary>У аватара подключено устройство.</summary>
        public static bool IsDriven(UxrAvatar avatar) => For(avatar) != null;

        /// <summary>
        /// Подключает устройство к аватару с источником <paramref name="source"/>. Возвращает null или причину отказа
        /// (тогда <paramref name="device"/> = null, отказ записан в лог).
        /// </summary>
        public static string Connect(UxrAvatar avatar, IAvatarInputSource source, out AvatarInputDevice device)
        {
            device = null;
            string refusal = CheckConnect(avatar, source, out ScriptedControllerInput input, out ScriptedControllerTracking tracking);
            if (refusal != null)
            {
                GameLog.Error($"{Tag} Аватар {(avatar != null ? avatar.name : "null")}: {refusal}. Ввод не подан.", avatar);
                return refusal;
            }

            device = new AvatarInputDevice(avatar, source, input, tracking);
            refusal = device.Start();
            if (refusal != null)
            {
                GameLog.Error($"{Tag} Аватар {avatar.name}: {refusal}. Ввод не подан.", avatar);
                device = null;
                return refusal;
            }

            return null;
        }

        /// <summary>Отключает устройство: кнопки отпускаются, компоненты выключаются, флаг SDK снимается.</summary>
        public void Disconnect()
        {
            if (!_connected) return;
            _connected = false;

            if (_input != null)
            {
                _input.Disconnect();
                _input.Claimed = false;
            }
            if (_tracking != null) _tracking.Claimed = false;

            if (Avatar != null)
            {
                if (Avatar.AvatarMode == UxrAvatarMode.Local) Avatar.CameraTrackingEnabled = true;
                Avatar.InputDriven = false;
            }

            if (s_devices.TryGetValue(_avatarId, out AvatarInputDevice registered) && registered == this) s_devices.Remove(_avatarId);
            s_active.Remove(this);
            _frame = default;
            GameLog.Debug.Info($"{Tag} Ввод снят с аватара {(Avatar != null ? Avatar.name : "(уничтожен)")}.");
        }

        // ── Подключение ──────────────────────────────────────────────────────

        private static string CheckConnect(UxrAvatar avatar, IAvatarInputSource source, out ScriptedControllerInput input,
                                           out ScriptedControllerTracking tracking)
        {
            input = null;
            tracking = null;
            if (avatar == null) return "аватар не задан";
            if (source == null) return "источник ввода не задан";
            if (For(avatar) != null) return "у аватара уже есть устройство ввода — один источник на аватар";

            if (avatar.AvatarMode == UxrAvatarMode.Local)
            {
                if (!ScriptedInputGate.Allows) return "Local-аватару ввод подаёт только скриптовый клиент (E2EScriptedInput)";
            }
            else if (avatar.AvatarMode == UxrAvatarMode.UpdateExternally)
            {
                if (!NetworkServer.active || !StateEventAuthority.IsWorldAuthority)
                    return "внешнему аватару ввод подаётся только на сервере (автор мира)";
                NetworkIdentity identity = avatar.GetComponent<NetworkIdentity>();
                if (identity != null && identity.connectionToClient != null)
                    return "у аватара есть владелец-соединение — его ввод на клиенте игрока";
            }
            else
            {
                return $"режим аватара {avatar.AvatarMode} не поддерживается";
            }

            input = avatar.GetComponentInChildren<ScriptedControllerInput>(true);
            tracking = avatar.GetComponentInChildren<ScriptedControllerTracking>(true);
            if (input == null || tracking == null)
                return "в префабе аватара нет ScriptedControllerInput/ScriptedControllerTracking " +
                       "(меню Tools/VR Battlegrounds/Testing/Скриптовый ввод в префабах аватаров)";

            foreach (UxrControllerInput other in avatar.EnabledControllerInputs)
            {
                if (other == input || other is UxrGamepadInput) continue;
                return $"у аватара включён другой ввод {other.GetType().Name} — один источник на аватар";
            }

            return null;
        }

        private string Start()
        {
            _frame = default;
            _hasPose = false;
            AppliedFrames = 0;
            _input.Claimed = true;
            _tracking.Claimed = true;
            if (Avatar.AvatarMode == UxrAvatarMode.Local) Avatar.CameraTrackingEnabled = false;
            else Avatar.InputDriven = true;

            _input.Connect();
            if (!_input.enabled || !_tracking.enabled)
            {
                string reason = $"устройство не подключилось: input.enabled={_input.enabled}, tracking.enabled={_tracking.enabled}";
                _connected = true;
                Disconnect();
                return reason;
            }

            _connected = true;
            s_devices[_avatarId] = this;
            s_active.Add(this);
            Subscribe();

            // Первый кадр сразу: новое тело не должно жить с головой в позе префаба до ближайшей стадии Update.
            ApplyFrame();
            GameLog.Debug.Info($"{Tag} Ввод подан аватару {Avatar.name} ({Avatar.AvatarMode}), источник {_source.GetType().Name}.", Avatar);
            return null;
        }

        // ── Слот кадра ───────────────────────────────────────────────────────

        private static void Subscribe()
        {
            if (s_subscribed) return;
            s_subscribed = true;
            UxrManager.StageUpdating += OnStageUpdating;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            if (s_subscribed) UxrManager.StageUpdating -= OnStageUpdating;
            s_subscribed = false;
            s_devices.Clear();
            s_active.Clear();
            s_stale.Clear();
        }

        /// <summary>
        /// <c>StageUpdating(Update)</c>: кадр источника каждому устройству. Ввод и трекинг SDK читает в этой же стадии
        /// сразу после слота — голова, руки и кнопки из одного кадра.
        /// </summary>
        private static void OnStageUpdating(UxrUpdateStage stage)
        {
            if (stage != UxrUpdateStage.Update || s_active.Count == 0) return;

            s_stale.Clear();
            for (int i = 0; i < s_active.Count; i++)
            {
                AvatarInputDevice device = s_active[i];
                if (device.Avatar == null || !device.Avatar.isActiveAndEnabled) s_stale.Add(device);
                else device.ApplyFrame();
            }

            // Аватар уничтожен или выключен — устройство снимается (новое тело подключает владелец источника).
            for (int i = 0; i < s_stale.Count; i++) s_stale[i].Disconnect();
        }

        private void ApplyFrame()
        {
            if (Avatar.AvatarMode == UxrAvatarMode.Local && Avatar.CameraTrackingEnabled) Avatar.CameraTrackingEnabled = false;

            DeviceFrame frame = _frame;
            bool read = _source.TryRead(ref frame);
            if (!read)
            {
                // Ввода нет — кнопки отпущены, позы прежние.
                frame.Left.Pressed = UxrInputButtons.None;
                frame.Right.Pressed = UxrInputButtons.None;
            }

            _frame = frame;
            _hasPose |= read;
            if (_hasPose)
            {
                // До первого кадра источника позы не пишутся: нулевой кадр поставил бы голову на пол.
                WriteCamera(frame);
                WriteControllers(frame);
            }
            _input.Apply(frame);
            AppliedFrames++;
        }

        private void WriteCamera(in DeviceFrame frame)
        {
            Transform camera = Avatar.CameraTransform;
            Transform space = Avatar.CameraController;
            if (camera == null) return;

            if (space == null)
                camera.SetPositionAndRotation(Avatar.transform.TransformPoint(frame.HeadPosition), Avatar.transform.rotation * frame.HeadRotation);
            else if (camera.parent == space)
                camera.SetLocalPositionAndRotation(frame.HeadPosition, frame.HeadRotation);
            else
                camera.SetPositionAndRotation(space.TransformPoint(frame.HeadPosition), space.rotation * frame.HeadRotation);
        }

        private void WriteControllers(in DeviceFrame frame)
        {
            Transform root = Avatar.transform;
            Transform space = TrackingSpace;
            Quaternion toRoot = Quaternion.Inverse(root.rotation) * space.rotation;

            Vector3 left = root.InverseTransformPoint(space.TransformPoint(frame.Left.Position));
            Vector3 right = root.InverseTransformPoint(space.TransformPoint(frame.Right.Position));
            left.y -= _tracking.HeightOffset;
            right.y -= _tracking.HeightOffset;
            _tracking.Apply(left, toRoot * frame.Left.Rotation, right, toRoot * frame.Right.Rotation);
        }
    }
}
