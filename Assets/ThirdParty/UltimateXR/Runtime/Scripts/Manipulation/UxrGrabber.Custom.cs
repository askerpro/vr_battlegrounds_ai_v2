using System;
using UltimateXR.Avatar;

namespace UltimateXR.Manipulation
{
    public partial class UxrGrabber
    {
        /// <summary>
        ///     Hook to validate if an object can be grabbed by this specific grabber.
        ///     Parameters: grabbable, grabPointIndex.
        ///     Returns: true if grab is allowed, false otherwise.
        /// </summary>
        public Func<UxrGrabbableObject, int, bool> CanGrabDelegate { get; set; }
    }
}
