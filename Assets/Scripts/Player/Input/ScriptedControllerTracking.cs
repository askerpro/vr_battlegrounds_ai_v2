// Скриптовый трекинг контроллеров (контракты tasks/bots-control-modes/contracts/xr-input.md,
// tasks/bots-input/contracts/bot-input-design.md).
// Компонент лежит выключенным в префабах аватаров и компилируется во всех сборках; включается штатным событием
// подключения парного ScriptedControllerInput.
using System;
using UltimateXR.Core;
using UltimateXR.Devices;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.DevTools.E2E.Input
{
    /// <summary>
    /// Трекинг двух контроллеров из кадра устройства. Позы сенсоров уходят в штатный <c>UpdateSensor</c>
    /// (сглаживание 0), запястья ставит штатный <c>UxrControllerTracking.UpdateAvatar</c> — как у шлема. Сенсоры —
    /// те же трансформы, что у трекинга Quest 3 в префабе, поэтому смещение «сенсор → запястье» совпадает.
    /// </summary>
    public sealed class ScriptedControllerTracking : UxrControllerTracking
    {
        private Vector3 _leftPosition;
        private Quaternion _leftRotation = Quaternion.identity;
        private Vector3 _rightPosition;
        private Quaternion _rightRotation = Quaternion.identity;
        private bool _hasPose;

        public override Type RelatedControllerInputType => typeof(ScriptedControllerInput);
        public override string SDKDependency => null;

        /// <summary>Компонент заявлен устройством аватара (<see cref="AvatarInputDevice"/>) — только тогда он вправе работать.</summary>
        internal bool Claimed { get; set; }

        /// <summary>
        /// Позы сенсоров в пространстве корня аватара — то, что SDK ждёт в <c>UpdateSensor</c>. Пишет только
        /// <see cref="AvatarInputDevice"/>, раз за кадр, до стадии Update.
        /// </summary>
        internal void Apply(Vector3 leftPosition, Quaternion leftRotation, Vector3 rightPosition, Quaternion rightRotation)
        {
            _leftPosition = leftPosition;
            _leftRotation = leftRotation;
            _rightPosition = rightPosition;
            _rightRotation = rightRotation;
            _hasPose = true;
        }

        /// <summary>
        /// Смещение запястья относительно сенсора контроллера: <c>запястье = сенсор × смещение</c>. Считается по
        /// публичным позам SDK (сенсор и кисть из одних данных), поэтому совпадает с тем, что применит
        /// <c>UpdateAvatar</c>. false — сенсор этой руки не настроен или поза ещё не применялась.
        /// </summary>
        public bool TryGetWristOffset(UxrHandSide side, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            bool configured = side == UxrHandSide.Left ? HasLeftHandSensorSetup : HasRightHandSensorSetup;
            if (!configured || !_hasPose || Avatar == null) return false;

            Vector3 sensorPosition = side == UxrHandSide.Left ? SensorLeftPos : SensorRightPos;
            Quaternion sensorRotation = side == UxrHandSide.Left ? SensorLeftRot : SensorRightRot;
            Vector3 wristPosition = side == UxrHandSide.Left ? SensorLeftHandPos : SensorRightHandPos;
            Quaternion wristRotation = side == UxrHandSide.Left ? SensorLeftHandRot : SensorRightHandRot;
            if (!IsUnit(sensorRotation) || !IsUnit(wristRotation)) return false;

            Quaternion inverse = Quaternion.Inverse(sensorRotation);
            position = inverse * (wristPosition - sensorPosition);
            rotation = inverse * wristRotation;
            return true;
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (Claimed) return;

            enabled = false;
            if (Application.isPlaying)
                GameLog.Error($"[ScriptedInput] {name}: скриптовый трекинг включён без устройства аватара — выключен.");
        }

        protected override void UpdateSensors()
        {
            base.UpdateSensors();
            if (!_hasPose) return;

            UpdateSensor(UxrHandSide.Left, _leftPosition, _leftRotation);
            UpdateSensor(UxrHandSide.Right, _rightPosition, _rightRotation);
        }

        // Нулевой кватернион до первого UpdateSensor «валиден» для проверок SDK на NaN, но не для обращения.
        private static bool IsUnit(Quaternion q) => Mathf.Abs(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w - 1f) < 0.01f;
    }
}
