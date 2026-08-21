using UnityEngine;
using UnityEngine.UI;
using VrBattlegrounds.Core;
using VrBattlegrounds.UI.Menu;

namespace VrBattlegrounds.PhysicalSpaceUtils
{
    /// <summary>
    /// Displays floating instructions during the VR Calibration process.
    /// Integrated with the specific PhysicalSpaceSyncManager state events.
    /// </summary>
    public class HUDWidget_PhysicalSpaceSync : MonoBehaviour
    {
        [Header("UI Components")]
        [SerializeField] private GameObject hudContent;
        [SerializeField] private Text instructionText; // Note: Use TextMeshProUGUI if preferred in the new project

        [Header("Instruction Messages")]
        public string startMessage = "Синхронизируйте контроллер с точкой 1";
        public string firstAnchorMessage = "Синхронизируйте контроллер с точкой 2";
        public string completedMessage = "Синхронизация завершена!";
        public string cancelledMessage = "Синхронизация отменена";

        [Header("Timing")]
        public float messageDisplayDuration = 2f;

        private void Start()
        {
            SetHUDActive(false);

            if (PhysicalSpaceSyncManager.Instance != null)
            {
                SubscribeToEvents();
            }
            else
            {
                GameLog.PhysicalSpace.Warning("[MenuPhysicalSpaceSyncHUD] PhysicalSpaceSyncManager is not found in the scene.");
            }
        }

        private void SubscribeToEvents()
        {
            var manager = PhysicalSpaceSyncManager.Instance;
            manager.OnCalibrationStarted += HandleSyncStarted;
            manager.OnCalibrationCancelled += HandleSyncCancelled;
            manager.OnFirstAnchorRegistered += HandleFirstAnchorRegistered;
            // manager.OnSecondAnchorRegistered += HandleSecondAnchorRegistered; // Removed as per instruction
            manager.OnCalibrationCompleted += HandleSyncCompleted;

            manager.OnHeightCalibrationStarted += HandleHeightSyncStarted;
            manager.OnHeightCalibrationCompleted += HandleHeightSyncCompleted;
        }

        private void UnsubscribeFromEvents()
        {
            var manager = PhysicalSpaceSyncManager.Instance;
            if (manager == null) return;

            manager.OnCalibrationStarted -= HandleSyncStarted;
            manager.OnCalibrationCancelled -= HandleSyncCancelled;
            manager.OnFirstAnchorRegistered -= HandleFirstAnchorRegistered;
            manager.OnSecondAnchorRegistered -= HandleSecondAnchorRegistered;
            manager.OnCalibrationCompleted -= HandleSyncCompleted;

            manager.OnHeightCalibrationStarted -= HandleHeightSyncStarted;
            manager.OnHeightCalibrationCompleted -= HandleHeightSyncCompleted;
        }

        private void HandleSyncStarted()
        {
            SetHUDActive(true);
            ShowMessage(startMessage);
        }

        private void HandleSyncCancelled()
        {
            ShowMessage(cancelledMessage);
            Invoke(nameof(HideHUD), messageDisplayDuration);
        }

        private void HandleFirstAnchorRegistered()
        {
            ShowMessage(firstAnchorMessage);
        }

        private void HandleSecondAnchorRegistered()
        {
            ShowMessage("Обработка данных...");
        }

        private void HandleSyncCompleted()
        {
            ShowMessage(completedMessage);
            Invoke(nameof(HideHUD), messageDisplayDuration);
        }

        private void ShowMessage(string message)
        {
            if (instructionText != null)
            {
                instructionText.text = message;
            }
        }

        private void SetHUDActive(bool active)
        {
            if (hudContent != null)
            {
                hudContent.SetActive(active);
            }
        }

        private void HideHUD()
        {
            SetHUDActive(false);
        }

        private void HandleHeightSyncStarted()
        {
            SetHUDActive(true);
            ShowMessage("Калибровка высоты:\nКоснитесь контроллером пола и нажмите кнопку A (или X).");
        }

        private void HandleHeightSyncCompleted()
        {
            ShowMessage("Высота откалибрована!");
            Invoke(nameof(HideHUD), messageDisplayDuration);
        }

        private void OnDestroy()
        {
            UnsubscribeFromEvents();
        }
    }
}
