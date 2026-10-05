using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.Player.WallPass
{
    /// <summary>
    /// Только косметическое продвижение: реальные вход/выход замкнутого выпуклого объёма.
    /// Глубина головы и bounds не определяют толщину, штраф или законность опоры.
    /// </summary>
    internal sealed class WallPassBarrierProgress
    {
        private const float Epsilon = 0.0001f;
        private Collider _barrier;
        private Vector3 _support;
        private Vector3 _axis;

        public void Reset()
        {
            _barrier = null;
            _support = _axis = default;
        }

        public float Measure(Vector3 head, Vector3? originalSupport, List<Collider> obstacles)
        {
            // Reset tracker виден здесь через отсутствие опоры; законное обновление
            // опоры тоже начинает новую историю, не меняя саму игровую опору.
            if (!originalSupport.HasValue || !Finite(originalSupport.Value))
            {
                Reset();
                return 0;
            }
            Vector3 support = originalSupport.Value;
            if ((support - _support).sqrMagnitude > Epsilon * Epsilon) Reset();
            Vector3 source = new Vector3(support.x, head.y, support.z);
            Vector3 delta = head - source;
            float distance = delta.magnitude;
            if (distance <= Epsilon)
            {
                Reset();
                return 0;
            }
            Vector3 direction = delta / distance;

            if (_barrier != null)
            {
                if (!Active(_barrier))
                {
                    Reset();
                    return 0;
                }
                if (!TryInterval(_barrier, head, _axis, out float entry, out float exit)) return 0;
                // Возврат за поверхность входа завершает косметическую историю.
                // Уход вбок/через проём без интервала её не переориентирует.
                if (entry > Epsilon)
                {
                    Reset();
                    return 0;
                }
                return Progress(_barrier, source, head, direction, distance, entry, exit);
            }

            // Ближайший реальный вход от исходной стороны. Неподдержанная форма
            // первой стены не позволяет подменить её следующей выпуклой стеной.
            Collider first = null;
            float nearest = float.PositiveInfinity;
            foreach (Collider obstacle in obstacles)
            {
                if (!Active(obstacle)) continue;
                float crossing = float.PositiveInfinity;
                if (obstacle.Raycast(new Ray(source, direction), out RaycastHit hit, distance + Epsilon))
                    crossing = hit.distance;
                // Не перескакиваем через односторонний non-convex mesh, который
                // виден только обратному лучу. Его толщина остаётся неизвестной.
                if (!Supported(obstacle) && obstacle.Raycast(new Ray(head, -direction),
                        out RaycastHit reverseHit, distance + Epsilon))
                    crossing = Mathf.Min(crossing, Mathf.Max(0, distance - reverseHit.distance));
                if (crossing >= nearest) continue;
                first = obstacle;
                nearest = crossing;
            }
            if (first == null || !TryInterval(first, head, direction, out float initialEntry,
                    out float initialExit) || initialEntry > Epsilon) return 0;
            float initialProgress = Progress(first, source, head, direction, distance,
                                            initialEntry, initialExit);
            if (initialProgress <= 0) return 0;
            _barrier = first;
            _support = support;
            _axis = direction;
            return initialProgress;
        }

        private float Progress(Collider barrier, Vector3 source, Vector3 head, Vector3 direction,
                               float distance, float entry, float exit)
        {
            // Опора на высоте головы должна уверенно лежать с исходной стороны.
            // Например, пол законен рядом с выступом, но его проекция внутрь выступа
            // не даёт достоверной стороны входа и визуального интервала.
            if ((barrier.ClosestPoint(source) - source).sqrMagnitude <= Epsilon * Epsilon ||
                Vector3.Dot(source - head, _barrier == null ? direction : _axis) >= entry - Epsilon)
                return 0;
            // Проверяем фактический путь к голове отдельно от фиксированной оси:
            // окно и низкое укрытие без пересечения не получают выдуманную толщину.
            if (!barrier.Raycast(new Ray(source, direction), out RaycastHit hit, distance + Epsilon) ||
                hit.distance <= Epsilon || Vector3.Dot(hit.normal, direction) >= -Epsilon) return 0;
            return Mathf.Clamp01(-entry / (exit - entry));
        }

        private static bool TryInterval(Collider barrier, Vector3 head, Vector3 axis,
                                        out float entry, out float exit)
        {
            entry = exit = 0;
            if (!Supported(barrier)) return false;
            Bounds bounds = barrier.bounds;
            // Bounds используются только для гарантированно внешних стартов лучей.
            float span = Vector3.Distance(head, bounds.center) + bounds.extents.magnitude + 0.01f;
            if (float.IsNaN(span) || float.IsInfinity(span)) return false;
            if (!barrier.Raycast(new Ray(head - axis * span, axis), out RaycastHit enterHit, 2 * span) ||
                !barrier.Raycast(new Ray(head + axis * span, -axis), out RaycastHit exitHit, 2 * span) ||
                Vector3.Dot(enterHit.normal, axis) >= -Epsilon ||
                Vector3.Dot(exitHit.normal, axis) <= Epsilon) return false;
            entry = Vector3.Dot(enterHit.point - head, axis);
            exit = Vector3.Dot(exitHit.point - head, axis);
            return Finite(enterHit.point) && Finite(exitHit.point) && exit - entry > Epsilon;
        }

        private static bool Active(Collider collider) => collider != null && collider.enabled &&
            collider.gameObject.activeInHierarchy && !collider.isTrigger;

        private static bool Supported(Collider collider) => collider is BoxCollider ||
            collider is SphereCollider || collider is CapsuleCollider || collider is MeshCollider mesh && mesh.convex;

        private static bool Finite(Vector3 point) =>
            !float.IsNaN(point.x) && !float.IsInfinity(point.x) &&
            !float.IsNaN(point.y) && !float.IsInfinity(point.y) &&
            !float.IsNaN(point.z) && !float.IsInfinity(point.z);
    }
}
