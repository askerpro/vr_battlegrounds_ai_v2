// Ярус C (два процесса) — симуляция позы головы на шлеме без человека (контракт quest-device-run r5).
// Отключить из релизной сборки: определить VRBG_NO_E2E в Scripting Define Symbols.
#if !VRBG_NO_E2E
using System.Globalization;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UnityEngine;
using UnityEngine.XR;
using VrBattlegrounds.Core;
using VrBattlegrounds.PhysicalSpaceUtils;
#if VRB_XR_OCULUS && !UNITY_EDITOR
using Unity.XR.Oculus;
#endif

namespace VrBattlegrounds.DevTools.E2E
{
    /// <summary>
    /// Симуляция позы головы для e2e-прогона на шлеме: перф-замеры не требуют, чтобы человек держал шлем на высоте
    /// глаз. Включается флагом <c>-e2eHeadPose eye=&lt;м&gt;|calib</c> (<see cref="E2EContext.HeadPoseEnabled"/>) только
    /// вместе с <c>-e2eScenario</c> и только в собранном плеере-клиенте.
    ///
    /// <para>
    /// <b>Один писатель local pose камеры</b> (вариант A, подтверждение avatar-ik 4877). У
    /// <c>UxrAvatar.LocalAvatar</c> трекинг камеры выключается штатным <c>CameraTrackingEnabled = false</c> (гасит
    /// TrackedPoseDriver), и local pose камеры пишется только здесь — в <c>UxrManager.StageUpdating(Update)</c>, до
    /// <c>UpdateAvatar</c> локального контроллера и до стадии AvatarUsingTracking; позже в кадре камера не трогается.
    /// Поза: камера над пивотом (local XZ = 0, как голова бота в <c>BotRuntimeController.BuildFrame</c> →
    /// <c>BotAvatarDriver</c>), local Y = <c>pivot.InverseTransformPoint(root.position + root.up × цель)</c>.y — база
    /// префаба и пол калибровки у пивота учитываются сами; поворот — identity к пивоту (тангаж и крен 0, курс — от
    /// корня, его задаёт <see cref="E2EViews"/>). Пивот, Dummy Forward, BodyIK, кости и калибровка не пишутся.
    /// </para>
    ///
    /// <para>
    /// <b>Цель.</b> <c>eye=&lt;м&gt;</c> — заданная высота глаз над корнем; <c>calib</c> — <c>Applied.EyeHeight</c>
    /// применителя калибровки аватара (только чтение), без неё — <see cref="DefaultEyeHeight"/>. Замер роста калибровки
    /// читает <c>camera.y − root.y</c> и в режиме <c>calib</c> измерит ту же цель; с <c>eye=</c> замер калибровки
    /// в сценарии не запускать.
    /// </para>
    ///
    /// <para>
    /// <b>Повтор.</b> Смена локального аватара (<c>UxrAvatar.LocalAvatarChanged</c>/<c>LocalAvatarStarted</c>, в том
    /// числе через смену <c>AvatarMode</c> при Mirror-спавне) переносит симуляцию: прежнему аватару трекинг
    /// возвращается, новому выключается. Каждый слот проверяет <c>CameraTrackingEnabled == false</c> и что local pose
    /// камеры с прошлой записи никто не менял (<see cref="ForeignWrites"/>) — дёшево и без аллокаций.
    /// </para>
    ///
    /// <para>
    /// <b>Рендер.</b> OculusSettings LateLatching и SpaceWarp должны быть выключены (контракт r5): иначе рантайм
    /// подменял бы позу рендера позой шлема. Проверка в рантайме — <c>OculusSettings.s_Settings</c>; включены —
    /// отказ (<c>GameLog.Error</c>), прочитать нельзя — «не проверено» (по манифесту APK проверяет дирижёр).
    /// </para>
    ///
    /// <para>
    /// <b>Руки не симулируются</b>: лежащие контроллеры по-прежнему ведут IK рук — руки могут попасть в кадр.
    /// Статус их трекинга — <see cref="LeftControllerTracked"/>/<see cref="RightControllerTracked"/>.
    /// </para>
    ///
    /// <para>
    /// Ошибка разбора флага (<see cref="E2EContext.HeadPoseError"/>) — <c>GameLog.Error</c> и симуляция не включается.
    /// Жёсткий отказ прогона до сценария через исполнитель (как PortError/ArgError) — следующий шаг.
    /// </para>
    /// </summary>
    public static class E2EHeadPose
    {
        /// <summary>Высота глаз над корнем по умолчанию, м (режим <c>calib</c> без калибровки).</summary>
        public const float DefaultEyeHeight = 1.6f;

        private const string Tag = "[E2EHeadPose]";

        /// <summary>Допуск «local pose камеры не менялась с прошлой записи», м и градусы.</summary>
        private const float ForeignPositionEpsilon = 1e-4f;
        private const float ForeignAngleEpsilon = 0.01f;

        /// <summary>Пока своего аватара нет, страховочный опрос <c>UxrAvatar.LocalAvatar</c> раз в столько кадров.</summary>
        private const int LocalAvatarPollFrames = 30;

        private static bool _armed;
        private static bool _calib;
        private static float _requestedEye = float.NaN;
        private static UxrAvatar _avatar;
        private static Transform _camera;
        private static AvatarCalibrationApplier _applier;
        private static bool _hasWritten;
        private static Vector3 _writtenLocalPosition;
        private static Quaternion _writtenLocalRotation;
        private static int _nextPollFrame;

        /// <summary>Симуляция запрошена флагом и прошла проверки (подписки стоят).</summary>
        public static bool Armed => _armed;

        /// <summary>Симуляция пишет позу камеры текущего локального аватара.</summary>
        public static bool Active => _armed && _avatar != null && _camera != null;

        /// <summary>Режим цели: true — <c>calib</c> (Applied.EyeHeight или 1.6), false — <c>eye=&lt;м&gt;</c>.</summary>
        public static bool CalibTarget => _calib;

        /// <summary>Текущая цель высоты глаз над корнем, м; NaN — симуляция не включена.</summary>
        public static float Target { get; private set; } = float.NaN;

        /// <summary>Мировая высота <c>camera.y − root.y</c> перед последней записью (состояние после прошлого кадра), м.</summary>
        public static float LastHeight { get; private set; } = float.NaN;

        /// <summary>Тангаж камеры перед последней записью, градусы: плюс — взгляд вверх.</summary>
        public static float LastPitch { get; private set; } = float.NaN;

        /// <summary>Крен камеры перед последней записью, градусы в [-180, 180).</summary>
        public static float LastRoll { get; private set; } = float.NaN;

        /// <summary>Кадр Unity последней записи позы; -1 — записей не было.</summary>
        public static int LastFrame { get; private set; } = -1;

        /// <summary>Сколько раз поза записана.</summary>
        public static int WriteCount { get; private set; }

        /// <summary>
        /// Сколько раз local pose камеры к слоту отличалась от записанной в прошлом (чужой писатель: TrackedPoseDriver
        /// снова включён и т.п.). При каждом таком случае трекинг камеры выключается повторно.
        /// </summary>
        public static int ForeignWrites { get; private set; }

        /// <summary>Итог проверки рендера (LateLatching/SpaceWarp) в рантайме: текст для отчёта.</summary>
        public static string RenderCheck { get; private set; } = "не выполнялась";

        /// <summary>Почему симуляция не включена; null — включена или не запрашивалась.</summary>
        public static string Refusal { get; private set; }

        /// <summary>Аватар, которому выключен трекинг камеры; null — нет.</summary>
        public static UxrAvatar Avatar => _avatar;

        /// <summary>Трекинг левого контроллера (Unity XR: устройство валидно и isTracked). Только чтение.</summary>
        public static bool LeftControllerTracked => IsTracked(XRNode.LeftHand);

        /// <summary>Трекинг правого контроллера (Unity XR: устройство валидно и isTracked). Только чтение.</summary>
        public static bool RightControllerTracked => IsTracked(XRNode.RightHand);

        /// <summary>Статус контроллеров одной строкой — для detail проверок и логов.</summary>
        public static string DescribeControllers() =>
            $"контроллеры: левый {(LeftControllerTracked ? "трекается" : "не трекается")}, " +
            $"правый {(RightControllerTracked ? "трекается" : "не трекается")} (руки не симулируются, IK рук от контроллеров)";

        // ── Включение ────────────────────────────────────────────────────────

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            E2EContext context = E2EContext.FromCommandLine();
            if (context == null || (!context.HeadPoseEnabled && context.HeadPoseError == null))
                return;

            if (Application.isEditor)
            {
                Refusal = "в редакторе симуляция позы головы не включается";
                GameLog.Player.Info($"{Tag} {Refusal}.");
                return;
            }

            if (context.HeadPoseError != null)
            {
                // Жёсткий отказ прогона до сценария — шаг исполнителя (E2ERunner); пока ошибка видна вердикту по логам.
                Refusal = "ошибка флага -e2eHeadPose: " + context.HeadPoseError;
                GameLog.Error($"{Tag} {Refusal}. Симуляция не включена.");
                return;
            }

            if (context.IsServerRole || Mirror.Utils.IsHeadless())
            {
                Refusal = "роль сервера или headless: симулировать нечего";
                GameLog.Player.Warning($"{Tag} {Refusal} — флаг -e2eHeadPose проигнорирован.");
                return;
            }

            Enable(context.HeadPoseEye);
        }

        /// <summary>
        /// Включает симуляцию: <paramref name="eye"/> — цель в метрах, NaN — режим <c>calib</c>. Повторный вызов
        /// ничего не делает. Отказ (рендер с LateLatching/SpaceWarp) — <c>GameLog.Error</c> и <see cref="Refusal"/>.
        /// </summary>
        public static void Enable(float eye)
        {
            if (_armed) return;

            if (!CheckRender(out string renderRefusal))
            {
                Refusal = renderRefusal;
                GameLog.Error($"{Tag} {Refusal}. Симуляция не включена.");
                return;
            }

            _calib = float.IsNaN(eye);
            _requestedEye = eye;
            Target = _calib ? DefaultEyeHeight : eye;
            Refusal = null;
            ForeignWrites = 0;
            WriteCount = 0;
            LastFrame = -1;
            _armed = true;

            UxrManager.StageUpdating += OnStageUpdating;
            UxrAvatar.LocalAvatarChanged += OnLocalAvatarChanged;
            UxrAvatar.LocalAvatarStarted += OnLocalAvatarStarted;
            Application.quitting += Disable;

            GameLog.Player.Info($"{Tag} Симуляция позы головы включена: цель " +
                                (_calib ? "calib (Applied.EyeHeight, иначе " + Num(DefaultEyeHeight) + " м)" : Num(eye) + " м") +
                                $"; рендер: {RenderCheck}.");

            // Аватар мог появиться раньше подписки (сцена с локальным аватаром).
            Bind(UxrAvatar.LocalAvatar);
        }

        /// <summary>Выключает симуляцию: трекинг камеры возвращается, все подписки снимаются.</summary>
        public static void Disable()
        {
            if (!_armed) return;
            _armed = false;

            UxrManager.StageUpdating -= OnStageUpdating;
            UxrAvatar.LocalAvatarChanged -= OnLocalAvatarChanged;
            UxrAvatar.LocalAvatarStarted -= OnLocalAvatarStarted;
            Application.quitting -= Disable;

            Unbind();
            Target = float.NaN;
            GameLog.Player.Info($"{Tag} Симуляция позы головы выключена, трекинг камеры возвращён (записей {WriteCount}, " +
                                $"посторонних изменений позы {ForeignWrites}).");
        }

        /// <summary>LateLatching и SpaceWarp OculusSettings выключены. false — отказ с причиной.</summary>
        private static bool CheckRender(out string refusal)
        {
            refusal = null;
            // OculusSettings.s_Settings есть только в плеере (в редакторе пакет его не объявляет); симуляция — тоже только в плеере.
#if VRB_XR_OCULUS && !UNITY_EDITOR
            OculusSettings settings = OculusSettings.s_Settings;
            if (settings == null)
            {
                RenderCheck = "не проверено: OculusSettings.s_Settings в рантайме нет (проверка — по манифесту APK у дирижёра)";
                GameLog.Player.Warning($"{Tag} LateLatching/SpaceWarp {RenderCheck}.");
                return true;
            }

            RenderCheck = $"OculusSettings: LateLatching={(settings.LateLatching ? 1 : 0)}, SpaceWarp={(settings.SpaceWarp ? 1 : 0)}";
            if (settings.LateLatching || settings.SpaceWarp)
            {
                refusal = RenderCheck + " — рантайм подменял бы позу рендера позой шлема (контракт quest-device-run r5)";
                return false;
            }

            return true;
#else
            RenderCheck = "не проверено: пакета Oculus XR нет или это редактор (проверка — по манифесту APK у дирижёра)";
            GameLog.Player.Warning($"{Tag} LateLatching/SpaceWarp {RenderCheck}.");
            return true;
#endif
        }

        // ── Локальный аватар ─────────────────────────────────────────────────

        private static void OnLocalAvatarChanged(object sender, UxrAvatarEventArgs e) => Bind(e != null ? e.Avatar : null);

        private static void OnLocalAvatarStarted(object sender, UxrAvatarStartedEventArgs e) => Bind(e != null ? e.Avatar : null);

        /// <summary>Переносит симуляцию на <paramref name="avatar"/>; null или не локальный — снимает.</summary>
        private static void Bind(UxrAvatar avatar)
        {
            if (!_armed) return;
            if (avatar != null && avatar.AvatarMode != UxrAvatarMode.Local) avatar = null;
            if (avatar == _avatar && avatar != null)
            {
                if (avatar.CameraTrackingEnabled) avatar.CameraTrackingEnabled = false;
                return;
            }

            Unbind();
            if (avatar == null) return;

            _avatar = avatar;
            _avatar.CameraTrackingEnabled = false;
            _camera = _avatar.CameraTransform;
            _applier = _avatar.GetComponent<AvatarCalibrationApplier>();
            GameLog.Player.Info($"{Tag} Локальный аватар {_avatar.name}: трекинг камеры выключен, позу пишет симуляция " +
                                $"(камера {(_camera != null ? _camera.name : "не найдена")}, пивот " +
                                $"{(_avatar.CameraController != null ? _avatar.CameraController.name : "нет")}).", _avatar);
        }

        /// <summary>
        /// Возвращает трекинг камеры прежнему аватару (если он ещё жив и локальный) и забывает его. Нелокальному флаг
        /// не возвращается: сеттер SDK включил бы TrackedPoseDriver камере, которую ведёт сеть (сам
        /// <c>AvatarMode</c> гасит TPD у нелокального аватара и включает обратно только при флаге).
        /// </summary>
        private static void Unbind()
        {
            if (_avatar != null)
            {
                if (_avatar.AvatarMode == UxrAvatarMode.Local)
                {
                    _avatar.CameraTrackingEnabled = true;
                    GameLog.Player.Info($"{Tag} Аватар {_avatar.name}: трекинг камеры возвращён.", _avatar);
                }
                else
                {
                    GameLog.Player.Info($"{Tag} Аватар {_avatar.name} уже не локальный: трекинг камеры не включаем " +
                                        "(камеру ведёт сеть).", _avatar);
                }
            }

            _avatar = null;
            _camera = null;
            _applier = null;
            _hasWritten = false;
        }

        // ── Слот записи ──────────────────────────────────────────────────────

        /// <summary>
        /// <c>StageUpdating(Update)</c>: до <c>UpdateAvatar</c> локального контроллера. Без аллокаций, пока аватар есть;
        /// без аватара — редкий страховочный опрос <c>UxrAvatar.LocalAvatar</c> (LINQ SDK, до спавна).
        /// </summary>
        private static void OnStageUpdating(UxrUpdateStage stage)
        {
            if (stage != UxrUpdateStage.Update || !_armed) return;

            if (_avatar == null || _avatar.AvatarMode != UxrAvatarMode.Local)
            {
                if (_avatar != null || Time.frameCount >= _nextPollFrame)
                {
                    _nextPollFrame = Time.frameCount + LocalAvatarPollFrames;
                    Bind(UxrAvatar.LocalAvatar);
                }

                if (_avatar == null) return;
            }

            // Mirror-спавн и смена режима могут снова включить трекинг — повторяем на каждом слоте.
            if (_avatar.CameraTrackingEnabled) _avatar.CameraTrackingEnabled = false;

            if (_camera == null)
            {
                _camera = _avatar.CameraTransform;
                _hasWritten = false;
                if (_camera == null) return;
            }

            if (_applier == null) _applier = _avatar.GetComponent<AvatarCalibrationApplier>();
            Target = ResolveTarget();

            Transform root = _avatar.transform;
            Transform pivot = _avatar.CameraController;
            Transform camera = _camera;

            Measure(root, camera);
            DetectForeignWrite(camera);
            Write(root, pivot, camera, Target);
        }

        /// <summary>Цель: заданная флагом или Applied.EyeHeight калибровки (только чтение), иначе 1.6.</summary>
        private static float ResolveTarget()
        {
            if (!_calib) return _requestedEye;
            if (_applier != null)
            {
                PlayerCalibration applied = _applier.Applied;
                if (applied.HasEyeHeight) return applied.EyeHeight;
            }

            return DefaultEyeHeight;
        }

        /// <summary>Мировая высота и углы камеры до записи этого кадра.</summary>
        private static void Measure(Transform root, Transform camera)
        {
            Vector3 euler = camera.rotation.eulerAngles;
            LastHeight = camera.position.y - root.position.y;
            LastPitch = -Mathf.DeltaAngle(0f, euler.x);
            LastRoll = Mathf.DeltaAngle(0f, euler.z);
        }

        /// <summary>Local pose камеры изменилась с прошлой записи — кто-то ещё пишет; трекинг выключается повторно.</summary>
        private static void DetectForeignWrite(Transform camera)
        {
            if (!_hasWritten) return;

            camera.GetLocalPositionAndRotation(out Vector3 position, out Quaternion rotation);
            bool moved = (position - _writtenLocalPosition).sqrMagnitude > ForeignPositionEpsilon * ForeignPositionEpsilon ||
                         Quaternion.Angle(rotation, _writtenLocalRotation) > ForeignAngleEpsilon;
            if (!moved) return;

            ForeignWrites++;
            // Сеттер штатно гасит TrackedPoseDriver ещё раз (GetComponentsInChildren — только в этом редком случае).
            _avatar.CameraTrackingEnabled = false;
            if (ForeignWrites == 1 || ForeignWrites % 300 == 0)
            {
                GameLog.Player.Warning($"{Tag} Local pose камеры изменена не симуляцией (случаев: {ForeignWrites}) — " +
                                       "трекинг камеры выключен повторно.", _avatar);
            }
        }

        /// <summary>Единственная запись: local pose камеры к пивоту (прецедент BotAvatarDriver.Apply).</summary>
        private static void Write(Transform root, Transform pivot, Transform camera, float target)
        {
            Vector3 head = root.position + root.up * target;
            if (pivot != null)
            {
                Vector3 local = pivot.InverseTransformPoint(head);
                local.x = 0f;
                local.z = 0f;
                if (camera.parent == pivot)
                    camera.SetLocalPositionAndRotation(local, Quaternion.identity);
                else
                    camera.SetPositionAndRotation(pivot.TransformPoint(local), pivot.rotation);
            }
            else
            {
                camera.SetPositionAndRotation(head, root.rotation);
            }

            camera.GetLocalPositionAndRotation(out _writtenLocalPosition, out _writtenLocalRotation);
            _hasWritten = true;
            WriteCount++;
            LastFrame = Time.frameCount;
        }

        // ── Контроллеры (только чтение) ──────────────────────────────────────

        private static bool IsTracked(XRNode node)
        {
            InputDevice device = InputDevices.GetDeviceAtXRNode(node);
            return device.isValid && device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked;
        }

        private static string Num(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
#endif
