// Ярус C (несколько процессов) — симуляция ввода шлема и контроллеров у обычного сетевого клиента
// (контракт tasks/bots-control-modes/contracts/xr-input.md). Отключить из сборки: VRBG_NO_E2E.
#if !VRBG_NO_E2E
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Devices;
using UltimateXR.Devices.Integrations;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.DevTools.E2E.Input
{
    /// <summary>
    /// Единственный владелец симуляции ввода в процессе: привязывается к своему локальному аватару, включает в нём
    /// скриптовые контроллеры и трекинг (лежат выключенными в префабе) и раз за кадр применяет кадр источника.
    ///
    /// <para>
    /// <b>Один источник на процесс.</b> Включение отказывает, если у аватара уже включён другой ввод (реальная XR),
    /// или если позу камеры пишет <see cref="E2EHeadPose"/>.
    /// </para>
    ///
    /// <para>
    /// <b>Один писатель.</b> Local pose камеры к пивоту (<c>UxrAvatar.CameraController</c>) пишется только здесь, в
    /// <c>UxrManager.StageUpdating(Update)</c> до <c>UpdateAvatar</c> локального контроллера — тот же слот, что у
    /// <see cref="E2EHeadPose"/>. Позы контроллеров уходят в <see cref="ScriptedControllerTracking"/> (запястья ставит
    /// SDK), кнопки — в <see cref="ScriptedControllerInput"/>. Кости, корень, пивот, BodyIK и калибровка не пишутся.
    /// </para>
    ///
    /// <para>
    /// <b>Пространство трекинга</b> — пивот камеры, как у шлема: туда же калибровка кладёт пол. Позы контроллеров
    /// переводятся в пространство корня аватара минус <c>HeightOffset</c> — то, что SDK ждёт в <c>UpdateSensor</c>,
    /// поэтому голова и руки остаются согласованными при любой калибровке.
    /// </para>
    ///
    /// <para>
    /// <b>Смена аватара</b> (призрак, возрождение, смена команды) переносит ввод: прежний аватар отключается и
    /// получает трекинг камеры обратно, новый подключается. Подписчики <see cref="Rebound"/> (пилот) сбрасывают позу.
    /// </para>
    /// </summary>
    public static class E2EScriptedInput
    {
        private const string Tag = "[E2EScriptedInput]";
        private const int LocalAvatarPollFrames = 30;

        private static IAvatarInputSource _source;
        private static UxrAvatar _avatar;
        private static ScriptedControllerInput _input;
        private static ScriptedControllerTracking _tracking;
        private static DeviceFrame _frame;
        private static int _nextPollFrame;

        /// <summary>Симуляция включена (источник задан).</summary>
        public static bool IsActive => _source != null;

        /// <summary>Аватар, которому сейчас подаётся ввод; null — ещё не появился или отказ.</summary>
        public static UxrAvatar Avatar => _avatar;

        /// <summary>Трекинг текущего аватара (смещение «сенсор → запястье» для пилота).</summary>
        public static ScriptedControllerTracking Tracking => _tracking;

        /// <summary>Пространство трекинга текущего аватара — пивот камеры.</summary>
        public static Transform TrackingSpace => _avatar != null ? _avatar.CameraController : null;

        /// <summary>Последний применённый кадр.</summary>
        public static DeviceFrame LastFrame => _frame;

        /// <summary>Причина последнего отказа привязки; null — отказа не было.</summary>
        public static string Refusal { get; private set; }

        /// <summary>Сколько кадров применено к текущему аватару.</summary>
        public static int AppliedFrames { get; private set; }

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
            if (avatar == _avatar && avatar != null) return;

            Unbind();
            if (avatar == null) return;

            ScriptedControllerInput input = avatar.GetComponentInChildren<ScriptedControllerInput>(true);
            ScriptedControllerTracking tracking = avatar.GetComponentInChildren<ScriptedControllerTracking>(true);
            if (input == null || tracking == null)
            {
                Refuse(avatar, "в префабе аватара нет ScriptedControllerInput/ScriptedControllerTracking " +
                               "(меню Tools/VR Battlegrounds/Testing/Скриптовый ввод в префабах аватаров)");
                return;
            }

            foreach (UxrControllerInput other in avatar.EnabledControllerInputs)
            {
                if (other == input || other is UxrGamepadInput) continue;
                Refuse(avatar, $"у аватара включён другой ввод {other.GetType().Name} — один источник на процесс");
                return;
            }

            _avatar = avatar;
            _input = input;
            _tracking = tracking;
            _frame = default;
            AppliedFrames = 0;
            _avatar.CameraTrackingEnabled = false;
            _input.Connect();
            if (!_input.enabled || !_tracking.enabled)
            {
                Refuse(avatar, $"устройство не подключилось: input.enabled={_input.enabled}, tracking.enabled={_tracking.enabled}");
                Unbind();
                return;
            }

            GameLog.Debug.Info($"{Tag} Ввод подан аватару {_avatar.name}: камера и контроллеры — из источника.", _avatar);
            Rebound?.Invoke(_avatar);
        }

        private static void Unbind()
        {
            UxrAvatar previous = _avatar;
            if (_input != null) _input.Disconnect();
            if (previous != null && previous.AvatarMode == UxrAvatarMode.Local) previous.CameraTrackingEnabled = true;

            _avatar = null;
            _input = null;
            _tracking = null;
            _frame = default;
            if (previous != null) Rebound?.Invoke(null);
        }

        private static void Refuse(UxrAvatar avatar, string reason)
        {
            Refusal = reason;
            GameLog.Error($"{Tag} Аватар {(avatar != null ? avatar.name : "null")}: {reason}. Ввод не подан.");
        }

        // ── Слот кадра ───────────────────────────────────────────────────────

        /// <summary>
        /// <c>StageUpdating(Update)</c>: снимок кадра источника, запись камеры, кадр контроллерам. Ввод и трекинг SDK
        /// читает в этой же стадии сразу после слота — голова, руки и кнопки из одного кадра.
        /// </summary>
        private static void OnStageUpdating(UxrUpdateStage stage)
        {
            if (stage != UxrUpdateStage.Update || _source == null) return;

            if (_avatar == null || _avatar.AvatarMode != UxrAvatarMode.Local)
            {
                if (_avatar != null || Time.frameCount >= _nextPollFrame)
                {
                    _nextPollFrame = Time.frameCount + LocalAvatarPollFrames;
                    if (_avatar != null) Unbind();
                    if (Refusal == null) Bind(UxrAvatar.LocalAvatar);
                }

                if (_avatar == null) return;
            }

            if (_avatar.CameraTrackingEnabled) _avatar.CameraTrackingEnabled = false;

            DeviceFrame frame = _frame;
            bool hasFrame = _source.TryRead(ref frame);
            if (!hasFrame)
            {
                // Ввода нет — кнопки отпущены, позы прежние.
                frame.Left.Pressed = UxrInputButtons.None;
                frame.Right.Pressed = UxrInputButtons.None;
            }

            _frame = frame;
            WriteCamera(frame);
            WriteControllers(frame);
            _input.Apply(frame);
            AppliedFrames++;
        }

        private static void WriteCamera(in DeviceFrame frame)
        {
            Transform camera = _avatar.CameraTransform;
            Transform space = _avatar.CameraController;
            if (camera == null) return;

            if (space == null)
                camera.SetPositionAndRotation(_avatar.transform.TransformPoint(frame.HeadPosition), _avatar.transform.rotation * frame.HeadRotation);
            else if (camera.parent == space)
                camera.SetLocalPositionAndRotation(frame.HeadPosition, frame.HeadRotation);
            else
                camera.SetPositionAndRotation(space.TransformPoint(frame.HeadPosition), space.rotation * frame.HeadRotation);
        }

        private static void WriteControllers(in DeviceFrame frame)
        {
            Transform root = _avatar.transform;
            Transform space = _avatar.CameraController != null ? _avatar.CameraController : root;
            Quaternion toRoot = Quaternion.Inverse(root.rotation) * space.rotation;

            Vector3 left = root.InverseTransformPoint(space.TransformPoint(frame.Left.Position));
            Vector3 right = root.InverseTransformPoint(space.TransformPoint(frame.Right.Position));
            left.y -= _tracking.HeightOffset;
            right.y -= _tracking.HeightOffset;
            _tracking.Apply(left, toRoot * frame.Left.Rotation, right, toRoot * frame.Right.Rotation);
        }
    }
}
#endif
