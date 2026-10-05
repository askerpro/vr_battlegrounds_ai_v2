using System;
using UnityEngine;
using VrBattlegrounds.Weapons.Sights;

namespace VrBattlegrounds.Editor.Weapons.Calibration
{
    /// <summary>Два луча ручного стенда; double-модель переиспользует общий SightGeometrySolver.</summary>
    public static class ManualSightCalibrationGeometry
    {
        public static bool TryIntersection(this ManualSightCalibrationSession session, out Vector3 hit, out Vector2 offsetMm, out string reason)
        {
            hit = default; offsetMm = default;
            if (!session.TryMarkers(out var rear, out var front, out reason)) return false;
            if (session.Muzzle == null || session.Target == null) { reason = "Не назначен ShotSource или мишень."; return false; }
            if (session.Placement == null || (session.Placement.lossyScale - Vector3.one).sqrMagnitude > 1e-10f)
            { reason = "Масштаб служебного размещения должен быть 1. Масштаб самого оружия сохраняется исходным."; return false; }
            Vector3 direction = front.transform.position - rear.transform.position;
            float denominator = Vector3.Dot(direction, session.Muzzle.forward);
            if (direction.sqrMagnitude < 1e-12f || denominator <= 1e-7f)
            { reason = "Мушка должна находиться впереди целика; линия должна идти к мишени."; return false; }
            if ((session.Target.position - (session.Muzzle.position + session.Muzzle.forward * session.Distance)).sqrMagnitude > 1e-8f)
            { reason = "ShotSource не смотрит в центр. Нажмите «Выровнять ShotSource на центр»."; return false; }
            try
            {
                var evaluation = SightGeometrySolver.Evaluate(Point(session.Muzzle.position), Point(session.Muzzle.forward),
                    Point(rear.transform.position), Point(front.transform.position), session.Distance);
                var delta = evaluation.transverseIntercept + evaluation.transverseSlope * session.Distance;
                var point = Point(session.Muzzle.position) + Point(session.Muzzle.forward) / Point(session.Muzzle.forward).Length * session.Distance + delta;
                hit = new Vector3((float)point.x, (float)point.y, (float)point.z);
                offsetMm = new Vector2((float)SightGeometrySolver.Point.Dot(delta, Point(session.Muzzle.right)), (float)SightGeometrySolver.Point.Dot(delta, Point(session.Muzzle.up))) * 1000;
                if (!Finite(hit.x) || !Finite(hit.y) || !Finite(hit.z) || !Finite(offsetMm.x) || !Finite(offsetMm.y))
                { reason = "Неконечные координаты маркеров."; return false; }
                return true;
            }
            catch (ArgumentException exception) { reason = exception.Message; return false; }
        }
        private static SightGeometrySolver.Point Point(Vector3 p) => new SightGeometrySolver.Point(p.x, p.y, p.z);
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
