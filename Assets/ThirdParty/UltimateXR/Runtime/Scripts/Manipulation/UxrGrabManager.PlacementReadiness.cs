namespace UltimateXR.Manipulation
{
    public partial class UxrGrabManager
    {
        /// <summary>
        /// VR Battlegrounds patch: чтение уже выбранной SDK пары гнездо/рука.
        /// Работает и после повторного включения визуала, когда нового RangeEntered не было.
        /// Не запускает второй поиск якорей и не меняет решение установки.
        /// </summary>
        public UxrGrabber GetAnchorPlacementCandidate(UxrGrabbableObjectAnchor anchor)
        {
            return anchor != null && _grabbableObjectAnchors.TryGetValue(anchor, out GrabbableObjectAnchorInfo info) &&
                   info.HasCompatibleObjectNear ? info.FullGrabberNear : null;
        }
    }
}
