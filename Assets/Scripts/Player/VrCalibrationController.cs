using UltimateXR.Avatar;
using UnityEngine;

namespace VrBattlegrounds.UI
{
    /// <summary>
    /// Калибровка VR-шлема относительно физической арены.
    ///
    /// Проблема: inside-out tracking шлема накапливает дрейф — виртуальная позиция
    /// постепенно расходится с реальной. Перед матчем и по необходимости
    /// игрок встаёт на физическую метку на полу и нажимает "Откалибровать".
    ///
    /// Принцип работы:
    ///   1. Игрок стоит на точке калибровки (известные мировые координаты в Unity).
    ///   2. Вызывается Calibrate() — вычисляется смещение между реальной точкой
    ///      и текущей позицией шлема в мировом пространстве.
    ///   3. Смещение применяется к Tracking Origin аватара (родительский Transform
    ///      над UxrAvatar), чтобы выровнять виртуальное пространство с реальным.
    /// </summary>
    public class VrCalibrationController : MonoBehaviour
    {
        [Header("Калибровочная точка")]
        [Tooltip("Transform с известной позицией в мировом пространстве Unity. " +
                 "Соответствует физической метке на полу арены.")]
        [SerializeField] private Transform _calibrationPoint;

        [Tooltip("Tracking Origin — родительский объект над UxrAvatar, " +
                 "смещение которого выравнивает виртуальное пространство с реальным.")]
        [SerializeField] private Transform _trackingOrigin;

        /// <summary>
        /// Выполняет калибровку: выравнивает tracking origin так, чтобы
        /// текущая позиция шлема совпала с физической меткой на полу.
        ///
        /// Вызывать когда игрок стоит на калибровочной метке.
        /// </summary>
        public void Calibrate()
        {
            if (_calibrationPoint == null || _trackingOrigin == null)
            {
                Debug.LogWarning("[VrCalibrationController] Не заданы CalibrationPoint или TrackingOrigin");
                return;
            }

            UxrAvatar localAvatar = UxrAvatar.LocalAvatar;
            if (localAvatar == null)
            {
                Debug.LogWarning("[VrCalibrationController] Локальный аватар не найден");
                return;
            }

            // Текущая позиция шлема в мировом пространстве (только XZ, Y не трогаем)
            Vector3 headsetWorldPos = localAvatar.CameraComponent.transform.position;

            // Смещение: куда должен переместиться tracking origin
            // чтобы шлем оказался ровно на калибровочной точке
            Vector3 offset = _calibrationPoint.position - headsetWorldPos;
            offset.y = 0f; // вертикаль не корректируем — шлем сам знает высоту пола

            _trackingOrigin.position += offset;

            Debug.Log($"[VrCalibrationController] Калибровка выполнена. Offset: {offset}");
        }
    }
}
