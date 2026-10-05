using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using VrBattlegrounds.LevelDesign;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Player.WallPass
{
    /// <summary>
    /// Законная опора по камере: свободные ноги/таз, линия тела через настоящий проём,
    /// непрерывный путь от прошлой опоры. Высота пола задаётся системой арены, без IK.
    /// </summary>
    public sealed class WallPassGeometry
    {
        private readonly PhysicsScene _scene;
        private readonly float _floorY;
        private readonly List<Collider> _obstacles = new List<Collider>();
        private readonly List<Collider> _nearby = new List<Collider>();
        private readonly HashSet<Collider> _obstacleSet = new HashSet<Collider>();
        private Collider[] _overlaps = new Collider[32];
        private RaycastHit[] _hits = new RaycastHit[32];
        private readonly HashSet<Collider> _warnedShapes = new HashSet<Collider>();
        private readonly WallPassBarrierProgress _barrierProgress = new WallPassBarrierProgress();
        private static readonly Vector3[] DepthDirections =
        {
            Vector3.right, Vector3.up, Vector3.forward,
            new Vector3(1, 1, 1).normalized, new Vector3(1, 1, -1).normalized,
            new Vector3(1, -1, 1).normalized, new Vector3(-1, 1, 1).normalized
        };

        public WallPassGeometry(PhysicsScene scene, float floorY)
        {
            _scene = scene;
            _floorY = floorY;
            // Статичная карта фиксируется на создание детектора, а не ищется на каждом тике.
            // Физическая сцена может содержать несколько аддитивно загруженных Unity-сцен.
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene loaded = SceneManager.GetSceneAt(i);
                if (!loaded.isLoaded || !loaded.GetPhysicsScene().Equals(scene)) continue;
                var physicalSources = PhysicalArenaSources.Collect(loaded);
                foreach (GameObject root in loaded.GetRootGameObjects())
                    foreach (Collider collider in root.GetComponentsInChildren<Collider>(true))
                        if (!physicalSources.Contains(collider) && IsStaticObstacle(collider) && _obstacleSet.Add(collider))
                            _obstacles.Add(collider);
            }
        }

        public WallPassObservation Observe(Vector3 head, Vector3? previousSupport)
        {
            var result = new WallPassObservation { HeadClearance = float.PositiveInfinity };
            if (!_scene.IsValid() || !Finite(head))
            {
                _barrierProgress.Reset();
                return result;
            }
            CollectNearby(head, previousSupport);
            MeasureHead(head, ref result);
            float pelvisHeight = Mathf.Clamp(head.y - _floorY - 0.5f, 0.08f, 0.9f);
            Vector3 projection = new Vector3(head.x, _floorY, head.z);
            Vector3 original = default;
            Vector3 independent = default;
            float bestOriginal = float.PositiveInfinity;
            float bestAny = float.PositiveInfinity;
            bool blockedIndependent = false;

            Consider(projection);
            if (previousSupport.HasValue) Consider(previousSupport.Value);
            for (int ring = 0; ring < 2; ring++)
            {
                float radius = ring == 0 ? 0.2f : WallPassRules.MaxLean;
                for (int angle = 0; angle < WallPassRules.RingSamples; angle++)
                {
                    float radians = angle * (2 * Mathf.PI / WallPassRules.RingSamples);
                    Consider(projection + new Vector3(Mathf.Cos(radians), 0, Mathf.Sin(radians)) * radius);
                }
            }

            result.HasOriginalSupport = !float.IsPositiveInfinity(bestOriginal);
            result.HasAnySupport = !float.IsPositiveInfinity(bestAny);
            result.Support = result.HasOriginalSupport ? original : independent;
            result.LeanDistance = result.HasOriginalSupport ? bestOriginal :
                previousSupport.HasValue ? HorizontalDistance(head, previousSupport.Value) : float.PositiveInfinity;
            result.CrossedBarrier = previousSupport.HasValue && !result.HasOriginalSupport &&
                                    result.HasAnySupport && blockedIndependent && result.HeadDepth <= 0.001f;
            result.BarrierProgress = _barrierProgress.Measure(head, previousSupport, _nearby);
            return result;

            void Consider(Vector3 candidate)
            {
                candidate.y = _floorY;
                float distance = HorizontalDistance(head, candidate);
                if (distance > WallPassRules.MaxLean + 0.0001f) return;
                if (distance >= bestOriginal) return;
                if (!SupportFree(candidate, pelvisHeight) ||
                    SegmentBlocked(candidate + Vector3.up * pelvisHeight, head, result.HeadDepth)) return;
                bool reachable = !previousSupport.HasValue ||
                                 !SupportPathBlocked(previousSupport.Value, candidate, pelvisHeight);
                if (distance < bestAny)
                {
                    bestAny = distance;
                    independent = candidate;
                    blockedIndependent = !reachable;
                }
                if (reachable)
                {
                    bestOriginal = distance;
                    original = candidate;
                }
            }
        }

        private static bool IsStaticObstacle(Collider collider)
        {
            if (collider == null || collider.isTrigger || !collider.CompareTag(GameTags.Environment)) return false;
            for (Transform t = collider.transform; t != null; t = t.parent)
                if (t.GetComponent<Rigidbody>() != null || t.GetComponent<Animator>() != null ||
                    t.GetComponent<NetworkIdentity>() != null || t.GetComponent<VaultableObstacle>() != null)
                    return false;
            return true;
        }

        private bool Included(Collider collider) => collider != null && collider.enabled &&
            collider.gameObject.activeInHierarchy && !collider.isTrigger && _obstacleSet.Contains(collider);

        private void CollectNearby(Vector3 head, Vector3? previousSupport)
        {
            // Один обход карты на тик; 34 кандидата затем смотрят только локальные объёмы.
            // Включаем весь путь от прежней опоры, даже при скачке сетевой позы.
            float radius = WallPassRules.MaxLean + WallPassRules.HintReleaseClearance;
            Vector3 min = new Vector3(head.x - radius, _floorY + 0.025f, head.z - radius);
            Vector3 max = new Vector3(head.x + radius, Mathf.Max(head.y, _floorY + 0.9f) + 0.2f, head.z + radius);
            if (previousSupport.HasValue)
            {
                Vector3 previous = previousSupport.Value;
                min.x = Mathf.Min(min.x, previous.x - WallPassRules.SupportRadius);
                min.z = Mathf.Min(min.z, previous.z - WallPassRules.SupportRadius);
                max.x = Mathf.Max(max.x, previous.x + WallPassRules.SupportRadius);
                max.z = Mathf.Max(max.z, previous.z + WallPassRules.SupportRadius);
            }
            var volume = new Bounds((min + max) * 0.5f, max - min);
            _nearby.Clear();
            foreach (Collider collider in _obstacles)
                if (Included(collider) && collider.bounds.Intersects(volume)) _nearby.Add(collider);
        }

        private bool SupportFree(Vector3 support, float pelvisHeight)
        {
            Vector3 lower = support + Vector3.up * Mathf.Min(0.3f, pelvisHeight);
            Vector3 upper = support + Vector3.up * pelvisHeight;
            int count;
            while ((count = _scene.OverlapCapsule(lower, upper, WallPassRules.SupportRadius,
                       _overlaps, ~0, QueryTriggerInteraction.Ignore)) == _overlaps.Length)
                Array.Resize(ref _overlaps, checked(_overlaps.Length * 2));
            for (int i = 0; i < count; i++)
                if (Included(_overlaps[i])) return false;
            // Overlap не обязан считать полость non-convex MeshCollider занятым объёмом.
            // Проверяем реальные поверхности; bounds никогда не становится самой стеной.
            foreach (Collider collider in _nearby)
            {
                if (!Included(collider)) continue;
                if (collider.bounds.Contains(lower) && PointDepth(collider, lower) > 0.001f) return false;
                if (collider.bounds.Contains(upper) && PointDepth(collider, upper) > 0.001f) return false;
            }
            return true;
        }

        private bool SupportPathBlocked(Vector3 from, Vector3 to, float pelvisHeight)
        {
            from.y = to.y = _floorY;
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.0001f) return false;
            Vector3 lower = from + Vector3.up * Mathf.Min(0.3f, pelvisHeight);
            Vector3 upper = from + Vector3.up * pelvisHeight;
            if (CapsuleBlocked(lower, upper, delta / distance, distance)) return true;
            // Односторонний mesh обязан остановить проход с обеих сторон.
            return CapsuleBlocked(to + Vector3.up * Mathf.Min(0.3f, pelvisHeight),
                                  to + Vector3.up * pelvisHeight, -delta / distance, distance);
        }

        private bool CapsuleBlocked(Vector3 lower, Vector3 upper, Vector3 direction, float distance)
        {
            int count;
            while ((count = _scene.CapsuleCast(lower, upper, WallPassRules.SupportRadius, direction,
                       _hits, distance, ~0, QueryTriggerInteraction.Ignore)) == _hits.Length)
                Array.Resize(ref _hits, checked(_hits.Length * 2));
            for (int i = 0; i < count; i++)
                if (Included(_hits[i].collider)) return true;
            return false;
        }

        private bool SegmentBlocked(Vector3 from, Vector3 to, float headDepth)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 0.0001f) return false;
            bool shallowHead = headDepth > 0 && headDepth < WallPassRules.HeadDepth;
            return RayBlocked(from, delta / distance, distance, shallowHead ? (Vector3?)to : null) ||
                   RayBlocked(to, -delta / distance, distance, null);
        }

        private bool RayBlocked(Vector3 origin, Vector3 direction, float distance, Vector3? shallowHead)
        {
            int count;
            while ((count = _scene.Raycast(origin, direction, _hits, distance, ~0,
                       QueryTriggerInteraction.Ignore)) == _hits.Length)
                Array.Resize(ref _hits, checked(_hits.Length * 2));
            for (int i = 0; i < count; i++)
            {
                Collider collider = _hits[i].collider;
                if (!Included(collider) || _hits[i].distance >= distance - 0.001f) continue;
                // Последний вход в объём головы <5 см остаётся подсказкой, а не NoSupport.
                // Обратный луч отдельно ловит прежние входы в другие части того же mesh.
                if (shallowHead.HasValue && PointDepth(collider, shallowHead.Value) > 0 &&
                    PointDepth(collider, shallowHead.Value) < WallPassRules.HeadDepth) continue;
                return true;
            }
            return false;
        }

        private void MeasureHead(Vector3 head, ref WallPassObservation observation)
        {
            foreach (Collider collider in _nearby)
            {
                if (!Included(collider) || collider.bounds.SqrDistance(head) >
                    WallPassRules.HintReleaseClearance * WallPassRules.HintReleaseClearance) continue;
                float depth = PointDepth(collider, head);
                observation.HeadDepth = Mathf.Max(observation.HeadDepth, depth);
                if (depth > 0)
                    observation.HeadClearance = 0;
                else if (!(collider is MeshCollider mesh) || mesh.convex)
                    observation.HeadClearance = Mathf.Min(observation.HeadClearance,
                        Vector3.Distance(head, collider.ClosestPoint(head)));
                else
                    observation.HeadClearance = Mathf.Min(observation.HeadClearance, MeshClearance(collider, head));
            }
        }

        private float PointDepth(Collider collider, Vector3 point)
        {
            if (!collider.bounds.Contains(point)) return 0;
            if (collider is BoxCollider box)
            {
                Vector3 local = box.transform.InverseTransformPoint(point) - box.center;
                Vector3 half = box.size * 0.5f;
                if (Mathf.Abs(local.x) >= half.x || Mathf.Abs(local.y) >= half.y || Mathf.Abs(local.z) >= half.z) return 0;
                // Обратная транспонированная матрица даёт расстояние до плоскости и при shear.
                Matrix4x4 normalMatrix = box.transform.worldToLocalMatrix.transpose;
                return Mathf.Min((half.x - Mathf.Abs(local.x)) / normalMatrix.MultiplyVector(Vector3.right).magnitude,
                       Mathf.Min((half.y - Mathf.Abs(local.y)) / normalMatrix.MultiplyVector(Vector3.up).magnitude,
                                 (half.z - Mathf.Abs(local.z)) / normalMatrix.MultiplyVector(Vector3.forward).magnitude));
            }
            if (collider is SphereCollider sphere)
            {
                Vector3 scale = sphere.transform.lossyScale;
                float radius = sphere.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                return Mathf.Max(0, radius - Vector3.Distance(point, sphere.transform.TransformPoint(sphere.center)));
            }
            if (collider is CapsuleCollider capsule)
            {
                Vector3 scale = capsule.transform.lossyScale;
                int axis = capsule.direction;
                float radius = capsule.radius * Mathf.Max(Mathf.Abs(scale[(axis + 1) % 3]), Mathf.Abs(scale[(axis + 2) % 3]));
                float halfSegment = Mathf.Max(0, capsule.height * Mathf.Abs(scale[axis]) * 0.5f - radius);
                Vector3 localAxis = axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward;
                Vector3 direction = capsule.transform.TransformDirection(localAxis).normalized;
                Vector3 center = capsule.transform.TransformPoint(capsule.center);
                Vector3 nearest = center + direction * Mathf.Clamp(Vector3.Dot(point - center, direction), -halfSegment, halfSegment);
                return Mathf.Max(0, radius - Vector3.Distance(point, nearest));
            }
            if (collider is MeshCollider) return MeshDepth(collider, point);
            WarnShape(collider, "тип коллайдера не имеет проверенного объёма головы; пути опоры продолжают проверяться");
            return 0;
        }

        private float MeshDepth(Collider collider, Vector3 point)
        {
            float depth = float.PositiveInfinity;
            int inside = 0;
            foreach (Vector3 direction in DepthDirections)
            {
                SurfacePair pair = Surfaces(collider, point, direction);
                if (!pair.HasNegative || !pair.HasPositive) continue;
                // Ближайшие реальные грани: выход спереди и выход сзади ограничивают объём.
                // В оконном проёме первая грань либо отсутствует, либо направлена на вход.
                if (pair.NegativeNormalDot < -0.0001f && pair.PositiveNormalDot > 0.0001f)
                {
                    inside++;
                    depth = Mathf.Min(depth, Mathf.Min(-pair.Negative * -pair.NegativeNormalDot,
                                                       pair.Positive * pair.PositiveNormalDot));
                }
                else return 0;
            }
            return inside >= 2 ? depth : 0;
        }

        private float MeshClearance(Collider collider, Vector3 point)
        {
            float clearance = float.PositiveInfinity;
            foreach (Vector3 direction in DepthDirections)
            {
                SurfacePair pair = Surfaces(collider, point, direction);
                if (pair.HasNegative) clearance = Mathf.Min(clearance, -pair.Negative);
                if (pair.HasPositive) clearance = Mathf.Min(clearance, pair.Positive);
            }
            return clearance;
        }

        private SurfacePair Surfaces(Collider collider, Vector3 point, Vector3 direction)
        {
            var pair = new SurfacePair { Negative = float.NegativeInfinity, Positive = float.PositiveInfinity };
            float span = Vector3.Distance(collider.bounds.center, point) + collider.bounds.extents.magnitude + 0.01f;
            Scan(point - direction * span, direction);
            Scan(point + direction * span, -direction);
            return pair;

            void Scan(Vector3 origin, Vector3 rayDirection)
            {
                float remaining = 2 * span;
                for (int i = 0; i < 256; i++)
                {
                    if (!collider.Raycast(new Ray(origin, rayDirection), out RaycastHit hit, remaining)) return;
                    float signed = Vector3.Dot(hit.point - point, direction);
                    if (signed <= 0 && signed > pair.Negative)
                    {
                        pair.HasNegative = true;
                        pair.Negative = signed;
                        pair.NegativeNormalDot = Vector3.Dot(hit.normal, direction);
                    }
                    if (signed >= 0 && signed < pair.Positive)
                    {
                        pair.HasPositive = true;
                        pair.Positive = signed;
                        pair.PositiveNormalDot = Vector3.Dot(hit.normal, direction);
                    }
                    // Обходим все входные грани с обеих сторон, без глобального queriesHitBackfaces.
                    float step = hit.distance + 0.00001f;
                    origin += rayDirection * step;
                    remaining -= step;
                    if (remaining <= 0) return;
                }
                WarnShape(collider, "больше 256 поверхностей вдоль луча; требуется отдельная проверка формы карты");
            }
        }

        private void WarnShape(Collider collider, string reason)
        {
            if (_warnedShapes.Add(collider))
                GameLog.Player.Warning($"[WallPassGeometry] '{collider.name}': {reason}.", collider);
        }

        private struct SurfacePair
        {
            public bool HasNegative, HasPositive;
            public float Negative, Positive, NegativeNormalDot, PositiveNormalDot;
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x, z = a.z - b.z;
            return Mathf.Sqrt(x * x + z * z);
        }

        private static bool Finite(Vector3 value) =>
            !float.IsNaN(value.x) && !float.IsNaN(value.y) && !float.IsNaN(value.z) &&
            !float.IsInfinity(value.x) && !float.IsInfinity(value.y) && !float.IsInfinity(value.z);
    }
}
