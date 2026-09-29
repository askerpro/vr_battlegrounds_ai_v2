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

        /// <summary>
        ///     VR Battlegrounds patch 28: насколько далеко от <c>transform</c> захватчика может стоять любой его
        ///     proximity-трансформ (<see cref="GetProximityTransform" />). Нужен грубой отсечке
        ///     <see cref="UxrGrabbableObject.IsOutsideCoarseGrabRange" />. Трансформы — дети кисти, смещение
        ///     постоянно; считается один раз.
        /// </summary>
        internal float CoarseProximityReach
        {
            get
            {
                if (_coarseProximityReach < 0.0f)
                {
                    float reach = 0.0f;

                    if (_optionalProximityTransforms != null)
                    {
                        foreach (UnityEngine.Transform proximity in _optionalProximityTransforms)
                        {
                            if (proximity != null)
                            {
                                reach = UnityEngine.Mathf.Max(reach, UnityEngine.Vector3.Distance(proximity.position, transform.position));
                            }
                        }
                    }

                    _coarseProximityReach = reach;
                }

                return _coarseProximityReach;
            }
        }

        private float _coarseProximityReach = -1.0f;
    }
}
