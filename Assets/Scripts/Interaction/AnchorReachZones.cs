using System.Collections.Generic;
using UltimateXR.Avatar;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UnityEngine;

namespace VrBattlegrounds.Interaction
{
    /// <summary>Форма зоны досягаемости.</summary>
    public enum ReachZoneShape
    {
        Sphere,
        Box
    }

    /// <summary>
    /// Зона досягаемости: сфера (центр, радиус) или коробка (<see cref="BoxCollider" /> в
    /// своих локальных осях).
    /// </summary>
    public readonly struct ReachZone
    {
        public readonly ReachZoneShape Shape;
        public readonly Vector3        Center;
        public readonly float          Radius;
        public readonly BoxCollider    Box;

        private ReachZone(ReachZoneShape shape, Vector3 center, float radius, BoxCollider box)
        {
            Shape  = shape;
            Center = center;
            Radius = radius;
            Box    = box;
        }

        public static ReachZone Sphere(Vector3 center, float radius) => new ReachZone(ReachZoneShape.Sphere, center, radius, null);

        public static ReachZone FromBox(BoxCollider box) => new ReachZone(ReachZoneShape.Box, box.transform.TransformPoint(box.center), 0f, box);
    }

    /// <summary>
    /// Где UltimateXR разрешает положить предмет в якорь и где разрешает его взять — теми же
    /// полями и точками, что проверяет SDK. Единственный источник для редакторских гизмо
    /// (<c>GrabbableAnchorGizmos</c>) и отладочного вида в шлеме (<c>AnchorZonesDebugView</c>):
    /// нарисованная граница обязана совпадать с игровой, иначе по ней нельзя настраивать.
    ///
    /// <para>
    /// Укладка — <c>UxrGrabbableObject.CanBePlacedOnAnchor</c>: расстояние между
    /// <c>DropProximityTransform</c> предмета и якоря не больше <c>MaxPlaceDistance</c>.
    /// Хват — <c>UxrGrabbableObject.CanBeGrabbedByGrabber</c>: точка близости граббера
    /// (ладонь) в сфере <c>MaxDistanceGrab</c> вокруг точки близости хвата, либо внутри
    /// <c>GrabProximityBox</c> в режиме <c>BoxConstrained</c>.
    /// </para>
    /// </summary>
    public static class AnchorReachZones
    {
        /// <summary>Зона, в которую должна попасть точка укладки предмета.</summary>
        public static ReachZone GetPlaceZone(UxrGrabbableObjectAnchor anchor)
        {
            return ReachZone.Sphere(anchor.DropProximityTransform.position, anchor.MaxPlaceDistance);
        }

        /// <summary>
        /// Попадает ли точка укладки предмета в зону якоря. Только геометрия: совместимость
        /// тегов и занятость якоря не проверяются — это другой вопрос, чем «достаю ли я».
        /// </summary>
        public static bool IsInPlaceZone(UxrGrabbableObjectAnchor anchor, UxrGrabbableObject item)
        {
            return item != null && Contains(GetPlaceZone(anchor), item.DropProximityTransform.position);
        }

        /// <summary>
        /// Зоны хвата предмета — по одной на точку хвата. <paramref name="avatar" /> нужен точкам,
        /// которые меряют от точки выравнивания кисти (<c>GrabProximityTransformUseSelf</c>);
        /// без аватара берётся сам предмет, как делает SDK, когда точки нет.
        /// </summary>
        public static List<ReachZone> GetGrabZones(UxrGrabbableObject grabbable, UxrAvatar avatar, UxrHandSide side)
        {
            var zones = new List<ReachZone>();

            for (int i = 0; i < grabbable.GrabPointCount; i++)
            {
                UxrGrabPointInfo point = grabbable.GetGrabPoint(i);
                if (point == null) continue;

                if (point.GrabProximityMode == UxrGrabProximityMode.BoxConstrained)
                {
                    if (point.GrabProximityBox != null) zones.Add(ReachZone.FromBox(point.GrabProximityBox));
                    continue;
                }

                zones.Add(ReachZone.Sphere(GetGrabProximityPosition(grabbable, point, avatar, side), Mathf.Max(0f, point.MaxDistanceGrab)));
            }

            return zones;
        }

        /// <summary>Лежит ли точка внутри зоны (граница включается, как в SDK).</summary>
        public static bool Contains(ReachZone zone, Vector3 point)
        {
            if (zone.Shape == ReachZoneShape.Sphere)
            {
                return Vector3.Distance(zone.Center, point) <= zone.Radius;
            }

            if (zone.Box == null) return false;

            Vector3 local = zone.Box.transform.InverseTransformPoint(point) - zone.Box.center;
            Vector3 half  = zone.Box.size * 0.5f;
            return Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z;
        }

        // Повторяет UxrGrabbableObject.GetGrabPointGrabProximityTransform (он internal).
        private static Vector3 GetGrabProximityPosition(UxrGrabbableObject grabbable, UxrGrabPointInfo point, UxrAvatar avatar, UxrHandSide side)
        {
            Transform proximity = null;

            if (point.GrabProximityTransformUseSelf)
            {
                if (avatar != null)
                {
                    UxrGripPoseInfo grip = point.GetGripPoseInfo(avatar);
                    if (grip != null) proximity = side == UxrHandSide.Left ? grip.GripAlignTransformHandLeft : grip.GripAlignTransformHandRight;
                }
            }
            else
            {
                proximity = point.GrabProximityTransform;
            }

            return (proximity != null ? proximity : grabbable.transform).position;
        }
    }
}
