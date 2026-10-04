namespace UltimateXR.Manipulation
{
    public partial class UxrGrabManager
    {
        /// <summary>
        /// VR Battlegrounds patch: обновляет снимок положения после сохранения мировой позы
        /// в обработчике Grabbing, до Compute и синхронизации того же события хвата.
        /// Метод не создаёт нового сетевого события и не меняет snap руки.
        /// </summary>
        public static void CaptureGrabbingObjectPose(UxrManipulationEventArgs e)
        {
            if (e.Grabber == null || e.GrabbableObject == null) return;
            e.CaptureObjectPose();
        }
    }
}
