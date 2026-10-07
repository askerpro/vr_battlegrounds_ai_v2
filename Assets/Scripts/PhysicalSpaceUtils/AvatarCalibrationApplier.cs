using System;
using System.Reflection;
using UltimateXR.Animation.IK;
using UltimateXR.Avatar;
using UltimateXR.Avatar.Controllers;
using UltimateXR.Devices;
using UnityEngine;
using Mirror;
using UltimateXR.Core;
using VrBattlegrounds.Core;
using VrBattlegrounds.Network;

namespace VrBattlegrounds.PhysicalSpaceUtils
{
    /// <summary>
    /// <b>Единственный писатель</b> производного состояния аватара от калибровки игрока (T-50).
    ///
    /// <para>
    /// Висит на каждом аватаре — своём, чужом, на сервере; добавляется в рантайме при первой связи
    /// с сессией (<see cref="For" />), префабы не меняются. Применение <b>абсолютное</b> от базы,
    /// снятой у этого аватара до первого применения, поэтому идемпотентно: сколько бы раз и в каком
    /// порядке его ни звали (связь, хук, предсказание, смена модели), результат один.
    /// </para>
    ///
    /// <list type="bullet">
    ///   <item>пивот камеры (<c>UxrAvatar.CameraController</c>) = база префаба + пол;</item>
    ///   <item><c>Dummy Forward</c> = рост глаз / <c>EyesBaseHeight</c> <b>этой</b> модели;</item>
    ///   <item>поля BodyIK <c>_neckPosRelativeToEyes</c>, <c>_avatarForwardPosRelativeToNeck</c> = база × масштаб;</item>
    ///   <item>смещение рук (<c>UxrControllerTracking.HeightOffset</c>) = пол — только у своего аватара.</item>
    /// </list>
    ///
    /// <para>
    /// Класс ошибки, ради которого он заведён: пивот двигали два писателя дельтами, своего аватара
    /// выбирали по <c>UxrAvatar.LocalAvatar</c> (при смене аватара он ещё указывает на старый), поля
    /// IK правили умножением на new/old. Итог — B1: пол на пивоте дважды, руки один раз.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AvatarCalibrationApplier : MonoBehaviour
    {
        /// <summary>Имя опоры тела, которую создаёт SDK в <c>UxrBodyIK.Initialize</c>.</summary>
        public const string DummyForwardName = "Dummy Forward";

        // ── Рефлексия во внутренности UltimateXR ─────────────────────────────
        //
        // Публичного доступа к этим полям нет. Переименованное при обновлении поле не даёт ошибки
        // компиляции — рефлексия вернёт null, и калибровка молча перестанет работать. Поэтому каждое
        // обращение идёт через ResolveSdkField, который на ненайденное поле пишет Error. Список —
        // Docs/UltimateXR/sdk-patches.md, раздел «Зависимости от приватных членов SDK».

        private const BindingFlags SdkPrivateField = BindingFlags.NonPublic | BindingFlags.Instance;

        private static readonly FieldInfo BodyIKSettingsField = ResolveSdkField(typeof(UxrStandardAvatarController), "_bodyIKSettings");
        private static readonly FieldInfo BodyIKField = ResolveSdkField(typeof(UxrStandardAvatarController), "_bodyIK");
        private static readonly FieldInfo NeckField = ResolveSdkField(typeof(UxrBodyIK), "_neckPosRelativeToEyes");
        private static readonly FieldInfo ForwardField = ResolveSdkField(typeof(UxrBodyIK), "_avatarForwardPosRelativeToNeck");

        private static FieldInfo ResolveSdkField(Type sdkType, string fieldName)
        {
            FieldInfo field = sdkType.GetField(fieldName, SdkPrivateField);
            if (field == null)
            {
                GameLog.Error(
                    $"[AvatarCalibrationApplier] В типе {sdkType.FullName} больше нет приватного поля \"{fieldName}\". " +
                    "UltimateXR обновился и переименовал его — калибровка роста работать не будет. См. " +
                    "Docs/UltimateXR/sdk-patches.md, раздел «Зависимости от приватных членов SDK».");
            }

            return field;
        }

        // ── База, снятая до первого применения ───────────────────────────────

        private bool _baseCaptured;
        private Transform _pivot;
        private Vector3 _pivotBase;
        private Transform _dummyForward;
        private UxrBodyIK _bodyIK;
        private Vector3 _neckBase;
        private Vector3 _forwardBase;
        private float _eyesBaseHeight;

        /// <summary>Наблюдатель SDK не публикует обратно позу, которую сейчас ставит применитель.</summary>
        public bool IsApplyingPlacement { get; private set; }

        /// <summary>Последнее применённое значение.</summary>
        public PlayerCalibration Applied { get; private set; }

        /// <summary>Применённый масштаб скелета.</summary>
        public float AppliedScale { get; private set; } = 1f;

        /// <summary>Базовый рост глаз этой модели (<c>UxrBodyIKSettings.EyesBaseHeight</c>), 0 — неизвестен.</summary>
        public float EyesBaseHeight
        {
            get
            {
                CaptureBase();
                return _eyesBaseHeight;
            }
        }

        /// <summary>Применитель аватара: существующий или новый.</summary>
        public static AvatarCalibrationApplier For(UxrAvatar avatar)
        {
            if (avatar == null) return null;

            AvatarCalibrationApplier applier = avatar.GetComponent<AvatarCalibrationApplier>();
            return applier != null ? applier : avatar.gameObject.AddComponent<AvatarCalibrationApplier>();
        }

        /// <summary>
        /// Ставит аватару производное состояние от <paramref name="calibration" />.
        /// </summary>
        /// <param name="calibration">Калибровка игрока, которому принадлежит аватар.</param>
        /// <param name="ownAvatar">Аватар игрока этой машины: только ему принадлежит трекинг рук.</param>
        public void Apply(PlayerCalibration calibration, bool ownAvatar, bool applyPlacement = false,
                          bool synchronizePlacement = false)
        {
            CaptureBase();

            if (_pivot != null)
                _pivot.localPosition = _pivotBase + new Vector3(0f, calibration.FloorOffset, 0f);

            float scale = calibration.ScaleFor(_eyesBaseHeight);

            if (_dummyForward != null)
                _dummyForward.localScale = new Vector3(scale, scale, scale);

            if (_bodyIK != null)
            {
                NeckField?.SetValue(_bodyIK, _neckBase * scale);
                ForwardField?.SetValue(_bodyIK, _forwardBase * scale);
            }

            // Патч SDK 54: каждое устройство принадлежит конкретному аватару, общей статики нет.
            if (ownAvatar)
            {
                UxrAvatar avatar = GetComponent<UxrAvatar>();
                foreach (UxrControllerTracking tracking in GetComponentsInChildren<UxrControllerTracking>(true))
                {
                    if (tracking.Avatar == avatar) tracking.HeightOffset = calibration.FloorOffset;
                }
            }

            if (applyPlacement && calibration.Placement.HasValue)
                ApplyPlacement(calibration, ownAvatar, synchronizePlacement);

            if (Applied != calibration || !Mathf.Approximately(AppliedScale, scale))
            {
                GameLog.PhysicalSpace.Verbose(
                    $"[AvatarCalibrationApplier] {name}: {calibration}, масштаб {scale:F3} (EyesBaseHeight {_eyesBaseHeight:F2}), " +
                    $"{(ownAvatar ? "свой" : "чужой")}.", this);
            }

            Applied = calibration;
            AppliedScale = scale;
        }

        private void ApplyPlacement(PlayerCalibration calibration, bool ownAvatar, bool synchronizePlacement)
        {
            PhysicalSpaceAnchorFrame frame = default;
            if (calibration.Placement.IsAnchored) PhysicalSpaceAnchorFrame.TryBuildFromScene(out frame, out _);
            string map = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
            if (!calibration.Placement.TryResolve(calibration.IsCalibrated, map, frame, out Vector3 position,
                                                  out Quaternion rotation, out string diagnosis))
            {
                GameLog.PhysicalSpace.Warning($"[AvatarCalibrationApplier] Поза {name} не применена: {diagnosis}.", this);
                return;
            }
            if (!synchronizePlacement && Vector3.SqrMagnitude(transform.position - position) < 0.0000000001f &&
                Quaternion.Angle(transform.rotation, rotation) < 0.001f) return;

            UxrAvatar avatar = GetComponent<UxrAvatar>();
            bool author = avatar != null && StateEventAuthority.IsAuthoredHere(avatar);
            IsApplyingPlacement = true;
            try
            {
                if (synchronizePlacement && ownAvatar && author && UxrManager.Instance != null && avatar.CameraComponent != null)
                {
                    // SDK-патч 55 синхронизирует root target. Camera-floor target на получателе
                    // пересчитывал бы корень от запаздывающего трекинга чужой головы.
                    UxrManager.Instance.MoveAvatarRootTo(avatar, position, rotation);
                }
                else if (NetworkServer.active || (ownAvatar && author))
                {
                    // Проекция принятого сервером значения для коллайдеров. SDK-событие чужого
                    // владельца не публикуем; его автор применит предсказание/ответ сам.
                    transform.SetPositionAndRotation(position, rotation);
                }
            }
            finally
            {
                IsApplyingPlacement = false;
            }
        }

        /// <summary>
        /// Снимает базу префаба один раз. Писатель этих величин — только этот компонент, поэтому
        /// первое обращение видит значения префаба и инициализации SDK.
        /// </summary>
        private void CaptureBase()
        {
            if (_baseCaptured) return;
            _baseCaptured = true;

            UxrAvatar avatar = GetComponent<UxrAvatar>();

            _pivot = avatar != null ? avatar.CameraController : null;
            if (_pivot != null) _pivotBase = _pivot.localPosition;
            else GameLog.PhysicalSpace.Warning($"[AvatarCalibrationApplier] У аватара '{name}' нет пивота камеры — пол применить некуда.", this);

            _dummyForward = transform.Find(DummyForwardName);
            if (_dummyForward == null)
                GameLog.PhysicalSpace.Warning($"[AvatarCalibrationApplier] У аватара '{name}' нет '{DummyForwardName}' — масштаб применить некуда.", this);

            UxrStandardAvatarController controller = GetComponent<UxrStandardAvatarController>();
            if (controller == null)
            {
                GameLog.PhysicalSpace.Warning($"[AvatarCalibrationApplier] У аватара '{name}' нет UxrStandardAvatarController — рост не применяется.", this);
                return;
            }

            var settings = BodyIKSettingsField?.GetValue(controller) as UxrBodyIKSettings;
            _eyesBaseHeight = settings != null ? settings.EyesBaseHeight : 0f;

            _bodyIK = BodyIKField?.GetValue(controller) as UxrBodyIK;
            if (_bodyIK != null)
            {
                if (NeckField != null) _neckBase = (Vector3)NeckField.GetValue(_bodyIK);
                if (ForwardField != null) _forwardBase = (Vector3)ForwardField.GetValue(_bodyIK);
            }
        }
    }
}
