using System.Collections.Generic;
using UltimateXR.Avatar;
using UltimateXR.Manipulation;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Bots
{
    /// <summary>
    /// Навигационная сетка ботов (T-48). Запекается <b>в памяти сервера</b> при первом пути на карте —
    /// ни ассетов, ни компонентов в сценах: карты (общий <c>Environment.prefab</c>) не меняются, окклюзия
    /// и геометрия не трогаются, новая карта работает без ручной подготовки.
    ///
    /// <para>
    /// Источник — твёрдые коллайдеры карты на слоях окружения (<c>Default</c>, <c>Ground</c>) по правилу
    /// <see cref="BotNavMeshSources"/>: без триггеров (зоны спавна, лазерная сетка), подвижного
    /// (<c>Rigidbody</c>), аватаров и хватаемых предметов. Сетка пересобирается при смене сцены.
    /// </para>
    ///
    /// <para>
    /// Нет сетки (на карте нет коллайдеров пола) или точки вне неё — путь из одной точки назначения: бот идёт
    /// по прямой. На текущих картах (арена — прямоугольный зал с укрытиями) сетка строится; запасной путь
    /// нужен, чтобы бот не стоял столбом на карте, где что-то пошло не так.
    /// </para>
    /// </summary>
    public static class BotNavMesh
    {
        /// <summary>Радиус человека для обхода укрытий.</summary>
        private const float AgentRadius = 0.3f;
        private const float AgentHeight = 1.8f;
        private const float AgentClimb = 0.3f;
        private const float AgentSlope = 40f;

        /// <summary>Как далеко искать сетку от точки (бот стоит у стены, цель — голова врага над полом).</summary>
        private const float SampleRadius = 2.5f;

        private static readonly string[] EnvironmentLayers = { "Default", "Ground" };

        private static int _sceneHandle = -1;
        private static bool _built;
        private static NavMeshDataInstance _instance;
        private static NavMeshPath _path;

        /// <summary>Сетка этой карты построена.</summary>
        public static bool Ready => _built && _sceneHandle == SceneManager.GetActiveScene().handle;

        /// <summary>
        /// Путь от <paramref name="from"/> до <paramref name="to"/> — углы в <paramref name="corners"/> (первый —
        /// старт). Сетки или пути нет — прямая. Возвращает true, если путь по сетке.
        /// </summary>
        public static bool TryPath(Vector3 from, Vector3 to, List<Vector3> corners)
        {
            corners.Clear();
            EnsureBuilt();

            if (_built &&
                NavMesh.SamplePosition(from, out NavMeshHit start, SampleRadius, NavMesh.AllAreas) &&
                NavMesh.SamplePosition(to, out NavMeshHit end, SampleRadius, NavMesh.AllAreas))
            {
                _path ??= new NavMeshPath();
                if (NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, _path) &&
                    _path.status != NavMeshPathStatus.PathInvalid && _path.corners.Length > 0)
                {
                    corners.Add(from);
                    corners.AddRange(_path.corners);
                    return true;
                }
            }

            corners.Add(from);
            corners.Add(to);
            return false;
        }

        /// <summary>Убрать сетку (сервер остановлен).</summary>
        public static void Clear()
        {
            if (_instance.valid) NavMesh.RemoveNavMeshData(_instance);
            _instance = default;
            _built = false;
            _sceneHandle = -1;
        }

        private static void EnsureBuilt()
        {
            int scene = SceneManager.GetActiveScene().handle;
            if (scene == _sceneHandle) return;

            Clear();
            _sceneHandle = scene;

            float started = Time.realtimeSinceStartup;
            int mask = LayerMask.GetMask(EnvironmentLayers);

            var sources = new List<NavMeshBuildSource>();
            var collected = new List<NavMeshBuildSource>();
            Bounds bounds = default;
            bool any = false;

            foreach (Collider collider in Object.FindObjectsByType<Collider>(FindObjectsSortMode.None))
            {
                if (!Included(collider, mask)) continue;

                if (any) bounds.Encapsulate(collider.bounds);
                else bounds = collider.bounds;
                any = true;
            }

            if (!any)
            {
                GameLog.Player.Warning("[BotNavMesh] На карте нет коллайдеров окружения — боты ходят по прямой.");
                return;
            }

            bounds.Expand(2f);
            NavMeshBuilder.CollectSources(bounds, mask, NavMeshCollectGeometry.PhysicsColliders, 0,
                                          new List<NavMeshBuildMarkup>(), collected);

            foreach (NavMeshBuildSource source in collected)
            {
                // Сборщик Unity берёт и триггеры, и подвижное — отбираем тем же правилом.
                if (source.component is Collider c && !Included(c, mask)) continue;
                sources.Add(source);
            }

            NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
            settings.agentRadius = AgentRadius;
            settings.agentHeight = AgentHeight;
            settings.agentClimb = AgentClimb;
            settings.agentSlope = AgentSlope;

            NavMeshData data = NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
            if (data == null)
            {
                GameLog.Player.Warning("[BotNavMesh] Сетка не построилась — боты ходят по прямой.");
                return;
            }

            _instance = NavMesh.AddNavMeshData(data);
            _built = _instance.valid;

            GameLog.Perf.Info($"[BotNavMesh] Сетка ботов для '{SceneManager.GetActiveScene().name}': {sources.Count} коллайдеров, " +
                              $"{(Time.realtimeSinceStartup - started) * 1000f:F0} мс.");
        }

        private static bool Included(Collider collider, int mask)
        {
            if (collider == null) return false;
            bool onItem = collider.GetComponentInParent<UxrAvatar>() != null ||
                          collider.GetComponentInParent<UxrGrabbableObject>() != null;

            return BotNavMeshSources.Include(collider.enabled && collider.gameObject.activeInHierarchy,
                                             collider.isTrigger,
                                             (mask & (1 << collider.gameObject.layer)) != 0,
                                             collider.attachedRigidbody != null,
                                             onItem);
        }
    }
}
