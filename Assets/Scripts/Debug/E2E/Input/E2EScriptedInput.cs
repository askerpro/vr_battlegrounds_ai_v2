// Ярус C (несколько процессов) — симуляция ввода шлема и контроллеров у обычного сетевого клиента
// (контракты tasks/bots-control-modes/contracts/xr-input.md, tasks/bots-input/contracts/bot-input-design.md).
// Отключить из сборки: VRBG_NO_E2E.
#if !VRBG_NO_E2E
using UltimateXR.Avatar;
using UltimateXR.Core;
using UnityEngine;
using VrBattlegrounds.Core;
using VrBattlegrounds.Player.Input;

namespace VrBattlegrounds.DevTools.E2E.Input
{
    /// <summary>
    /// Скриптовый клиент: устройство ввода (<see cref="AvatarInputDevice"/>) своего Local-аватара. Открывает
    /// <see cref="ScriptedInputGate"/>, подключает устройство к локальному аватару и переносит его при смене аватара;
    /// кадры применяет само устройство.
    ///
    /// <para>
    /// <b>Один источник на процесс.</b> Включение отказывает, если позу камеры пишет <see cref="E2EHeadPose"/>;
    /// устройство отказывает, если у аватара включён другой ввод (реальная XR).
    /// </para>
    ///
    /// <para>
    /// <b>Смена аватара</b> (призрак, возрождение, смена команды) переносит ввод: прежнее устройство отключается и
    /// возвращает трекинг камеры, новое подключается. Подписчики <see cref="Rebound"/> (пилот) сбрасывают позу.
    /// </para>
    /// </summary>
    public static class E2EScriptedInput
    {
        private const string Tag = "[E2EScriptedInput]";
        private const int LocalAvatarPollFrames = 30;

        private static IAvatarInputSource _source;
        private static AvatarInputDevice _device;
        private static UxrAvatar _avatar;   // аватар устройства; ставится до подключения — первый кадр читает его
        private static int _nextPollFrame;

        /// <summary>Симуляция включена (источник задан).</summary>
        public static bool IsActive => _source != null;

        /// <summary>Аватар, которому сейчас подаётся ввод; null — ещё не появился или отказ.</summary>
        public static UxrAvatar Avatar => _avatar;

        /// <summary>Трекинг текущего аватара (смещение «сенсор → запястье» для пилота).</summary>
        public static ScriptedControllerTracking Tracking => AvatarInputDevice.For(_avatar)?.Tracking;

        /// <summary>Пространство трекинга текущего аватара — пивот камеры.</summary>
        public static Transform TrackingSpace => Avatar != null ? Avatar.CameraController : null;

        /// <summary>Последний применённый кадр.</summary>
        public static DeviceFrame LastFrame => AvatarInputDevice.For(_avatar)?.LastFrame ?? default;

        /// <summary>Причина последнего отказа привязки; null — отказа не было.</summary>
        public static string Refusal { get; private set; }

        /// <summary>Сколько кадров применено к текущему аватару.</summary>
        public static int AppliedFrames => AvatarInputDevice.For(_avatar)?.AppliedFrames ?? 0;

        /// <summary>Ввод перенесён на новый аватар (или снят: аргумент null).</summary>
        public static event System.Action<UxrAvatar> Rebound;

        /// <summary>
        /// Включает симуляцию с источником <paramref name="source"/>. Повтор с тем же источником ничего не делает,
        /// с другим — отказ. Возвращает null или причину отказа.
        /// </summary>
        public static string Activate(IAvatarInputSource source)
        {
            if (source == null) return "источник ввода не задан";
            if (_source != null) return _source == source ? null : "симуляция ввода уже включена другим источником";
            if (E2EHeadPose.Avatar != null) return "позу камеры уже пишет E2EHeadPose — второй писатель запрещён";

            _source = source;
            Refusal = null;
            ScriptedInputGate.Open();
            UxrManager.StageUpdating += OnStageUpdating;
            UxrAvatar.LocalAvatarChanged += OnLocalAvatarChanged;
            UxrAvatar.LocalAvatarStarted += OnLocalAvatarStarted;
            Application.quitting += Deactivate;

            GameLog.Debug.Info($"{Tag} Симуляция ввода включена, источник {source.GetType().Name}.");
            Bind(UxrAvatar.LocalAvatar);
            return null;
        }

        /// <summary>Выключает симуляцию: устройство отключается, трекинг камеры возвращается.</summary>
        public static void Deactivate()
        {
            if (_source == null) return;

            UxrManager.StageUpdating -= OnStageUpdating;
            UxrAvatar.LocalAvatarChanged -= OnLocalAvatarChanged;
            UxrAvatar.LocalAvatarStarted -= OnLocalAvatarStarted;
            Application.quitting -= Deactivate;

            Unbind();
            _source = null;
            ScriptedInputGate.Close();
            GameLog.Debug.Info($"{Tag} Симуляция ввода выключена.");
        }

        // ── Локальный аватар ─────────────────────────────────────────────────

        private static void OnLocalAvatarChanged(object sender, UxrAvatarEventArgs e) => Bind(e != null ? e.Avatar : null);

        private static void OnLocalAvatarStarted(object sender, UxrAvatarStartedEventArgs e) => Bind(e != null ? e.Avatar : null);

        private static void Bind(UxrAvatar avatar)
        {
            if (_source == null) return;
            if (avatar != null && avatar.AvatarMode != UxrAvatarMode.Local) avatar = null;
            if (avatar != null && avatar == Avatar) return;

            Unbind();
            if (avatar == null) return;

            // Пилот (подписчик Rebound) ставит голову над новым аватаром до первого кадра устройства.
            _avatar = avatar;
            Rebound?.Invoke(avatar);
            string refusal = AvatarInputDevice.Connect(avatar, _source, out AvatarInputDevice device);
            if (refusal != null)
            {
                Refusal = refusal;
                _avatar = null;
                Rebound?.Invoke(null);
                return;
            }

            _device = device;
            GameLog.Debug.Info($"{Tag} Ввод подан аватару {avatar.name}: камера и контроллеры — из источника.", avatar);
        }

        private static void Unbind()
        {
            AvatarInputDevice previous = _device;
            _device = null;
            _avatar = null;
            if (previous == null) return;

            bool wasConnected = previous.IsConnected;
            previous.Disconnect();
            if (wasConnected) Rebound?.Invoke(null);
        }

        /// <summary>Опрос локального аватара: прежний ушёл из Local или исчез — устройство переносится.</summary>
        private static void OnStageUpdating(UxrUpdateStage stage)
        {
            if (stage != UxrUpdateStage.Update || _source == null) return;

            // Устройство само снимается с уничтоженного/выключенного аватара — тогда переносим ввод заново.
            UxrAvatar avatar = Avatar;
            if (avatar != null && avatar.AvatarMode == UxrAvatarMode.Local && AvatarInputDevice.IsDriven(avatar)) return;
            if (avatar == null && _device == null && Time.frameCount < _nextPollFrame) return;

            _nextPollFrame = Time.frameCount + LocalAvatarPollFrames;
            if (_device != null) Unbind();
            if (Refusal == null) Bind(UxrAvatar.LocalAvatar);
        }
    }
}
#endif
