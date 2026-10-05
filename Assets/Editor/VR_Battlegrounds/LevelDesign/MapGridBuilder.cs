using System.Collections.Generic;
using System.Threading;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.LevelDesign;
using VrBattlegrounds.Maps;
using VrBattlegrounds.Weapons;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>
    /// Строит <see cref="MapGrid"/> по сцене карты.
    ///
    /// <para>
    /// <b>Пол</b> — твёрдые коллайдеры слоя <c>Ground</c>, их XZ — границы сетки (край арены).
    /// <b>Препятствие</b> — твёрдый коллайдер без <c>Rigidbody</c> ниже <see cref="ObstacleCeiling"/>
    /// над полом: стены, укрытия, подоконники, перемычки, стены арсенала. Подвижное и потолок в
    /// него не входят. <b>Проходимость</b> — запрос объёма клетки в полосе тела (дверь с
    /// перемычкой проходима, окно с подоконником — нет); <b>верх</b> — лучом сверху по настоящей
    /// форме коллайдера (повёрнутые стены и «дорито» ложатся верно). <b>Стороны</b> — две
    /// <see cref="TeamSpawnZone"/> с командой-хозяином; A — первая по имени команды.
    /// </para>
    /// <para>
    /// <b>Перешагиваемое</b> (<see cref="VaultableObstacle"/>, LD-48) проходимо: закрывает обзор, но
    /// ходить сквозь него законно. Его размеры собираются для проверки <see cref="MapAnalyzer.CheckVaultables"/>.
    /// </para>
    /// <para>
    /// <b>Видимость</b> — лучом по коллайдерам препятствий этой сцены, а не по сетке: окно,
    /// бойница и щель в несколько сантиметров видны точно. Поэтому сцена должна быть открыта,
    /// пока идёт анализ <see cref="MapAnalyzer"/>.
    /// </para>
    /// <para>
    /// <b>Прострел</b> — теми же правилами, что у пули (<see cref="WallPenetration"/>, T-41): класс — ближайший
    /// <see cref="CoverSurface"/> (без него — Hard), Visual пуля пролетает, Soft пробивает, если выход найден не дальше
    /// <see cref="WallPenetration.MaxThickness"/> и пробитий не больше <see cref="WallPenetration.MaxPenetrations"/>.
    /// Урон не считается — он зависит от оружия; «простреливается» значит «хоть какое-то оружие пробьёт».
    /// </para>
    /// </summary>
    public static class MapGridBuilder
    {
        /// <summary>Шаг сетки, м.</summary>
        public const float DefaultCell = 0.1f;

        /// <summary>Выше этого над полом твёрдое — потолок и навесы, а не препятствие, м.</summary>
        public const float ObstacleCeiling = 3f;

        /// <summary>Префикс префабов блокаута (<c>Assets/Prefabs/LevelDesign/LD_Alphabet</c>).</summary>
        public const string BlockoutPrefix = "LD_";

        /// <summary>Сетка карты и верх каждого укрытия блокаута (для LD-20).</summary>
        public sealed class Result
        {
            public MapGrid Grid;
            public readonly List<KeyValuePair<string, float>> BlockoutTops = new List<KeyValuePair<string, float>>();
            /// <summary>Перешагиваемые преграды: имя, высота и толщина (для LD-48).</summary>
            public readonly List<MapAnalyzer.Vaultable> Vaultables = new List<MapAnalyzer.Vaultable>();
            public readonly List<string> Problems = new List<string>();
        }

        public static Result Build(Scene scene, float cell = DefaultCell, byte[] capturedZones = null)
        {
            using (var operation = Begin(scene, cell, capturedZones))
            { while (!operation.Step()) { } return operation.Result; }
        }

        public static MapGridBuildOperation Begin(Scene scene, float cell = DefaultCell, byte[] capturedZones = null,
            CancellationToken token = default)
        { MapGrowthSnapshotBuilder.RequireMainThread(); return new MapGridBuildOperation(scene, cell, capturedZones, token); }

        internal static IEnumerable<int> BuildSteps(Scene scene, float cell, byte[] capturedZones, Result result)
        {
            if (!scene.IsValid() || !scene.isLoaded || cell <= 0 || float.IsNaN(cell) || float.IsInfinity(cell))
                throw new System.ArgumentException("Нужны загруженная сцена и положительный конечный шаг сетки.");
            Physics.SyncTransforms();
            Collider[] colliders = scene.GetRootGameObjects()
                                        .SelectMany(r => r.GetComponentsInChildren<Collider>(false))
                                        .Where(BlockoutSupportSurfaces.IsActiveSolid)
                                        .ToArray();

            int ground = LayerMask.NameToLayer("Ground");
            Collider[] floors = colliders.Where(c => c.gameObject.layer == ground).ToArray();
            if (floors.Length == 0)
            {
                result.Problems.Add("нет пола на слое Ground");
                yield break;
            }

            Bounds floor = floors[0].bounds;
            foreach (Collider c in floors) floor.Encapsulate(c.bounds);
            float floorY = floor.max.y;

            var physicalSources = PhysicalArenaSources.Collect(scene);
            var obstacles = new Dictionary<Collider, int>();
            var owners = new Dictionary<Transform, int>();
            var vaultable = new HashSet<Collider>();
            var grid = new MapGrid(
                Mathf.RoundToInt(floor.size.x / cell),
                Mathf.RoundToInt(floor.size.z / cell),
                cell,
                new Vector2(floor.min.x, floor.min.z));

            var registeredTops = new Dictionary<Transform,float>();
            var registeredVaultables = new Dictionary<Transform,MapAnalyzer.Vaultable>();
            foreach (Collider c in colliders)
            {
                if (physicalSources.Contains(c) || c.gameObject.layer == ground || c.attachedRigidbody != null) continue;
                Bounds b = c.bounds;
                if (b.min.y >= floorY + ObstacleCeiling || b.max.y <= floorY + .00001f) continue;
                if (b.max.x < floor.min.x || b.min.x > floor.max.x || b.max.z < floor.min.z || b.min.z > floor.max.z) continue;

                CoverSurface surface = CoverSurface.Of(c);
                var block = c.GetComponentInParent<BlockoutBlockInstance>();
                Transform root = block != null ? block.transform : surface != null ? surface.transform : c.transform;
                if (!owners.TryGetValue(root, out int owner))
                {
                    owner = grid.Obstacles.Count;
                    owners.Add(root, owner);
                    grid.Obstacles.Add(ShortPath(root));
                    var summary=block!=null?block.MaterialSummary:BlockoutCoverSummary.Hard;
                    grid.ObstacleCover.Add(block!=null&&block.HasSections
                        ?summary==BlockoutCoverSummary.Soft?CoverClass.Soft:summary==BlockoutCoverSummary.Visual?CoverClass.Visual:CoverClass.Hard
                        :surface!=null?surface.Class:CoverClass.Hard);
                    grid.ObstacleMixedCover.Add(block!=null&&block.HasSections&&summary==BlockoutCoverSummary.Mixed);
                }
                obstacles[c] = owner;

                var vaultMarker=c.GetComponentInParent<VaultableObstacle>();
                if (vaultMarker != null)
                {
                    vaultable.Add(c);
                    if(!registeredVaultables.TryGetValue(vaultMarker.transform,out var vaultEntry))
                    {
                        vaultEntry=new MapAnalyzer.Vaultable {Name=ShortPath(vaultMarker.transform)};
                        registeredVaultables.Add(vaultMarker.transform,vaultEntry);
                    }
                    vaultEntry.Height=Mathf.Max(vaultEntry.Height,b.max.y-floorY);
                    vaultEntry.Thickness=Mathf.Max(vaultEntry.Thickness,Thickness(c));
                    continue;
                }

                if(block!=null)
                {
                    registeredTops.TryGetValue(block.transform,out float previousTop);
                    registeredTops[block.transform]=Mathf.Max(previousTop,b.max.y-floorY);
                }
                else if (c.gameObject.name.StartsWith(BlockoutPrefix))
                    result.BlockoutTops.Add(new KeyValuePair<string, float>(
                        $"{ShortPath(c.transform)} у ({b.center.x:F1}; {b.center.z:F1})", b.max.y - floorY));
            }
            foreach(var pair in registeredTops)result.BlockoutTops.Add(new KeyValuePair<string,float>(ShortPath(pair.Key),pair.Value));
            result.Vaultables.AddRange(registeredVaultables.Values);

            PhysicsScene physics = scene.GetPhysicsScene();
            var rays = new CompleteRayQuery(physics);
            float top = floorY + 10f;
            foreach (int count in MapCellFootprint.CaptureSteps(scene, grid, floorY, obstacles, vaultable, value => grid.Footprint = value))
                yield return count;

            for (int i = 0; i < grid.Count; i++)
            {
                Vector2 p = grid.Center(i);

                int count = rays.Cast(new Vector3(p.x, top, p.y), Vector3.down, top - floorY - 0.01f);
                var hits = rays.Hits;
                yield return 1;
                for (int h = 0; h < count; h++)
                {
                    if (!obstacles.TryGetValue(hits[h].collider, out int owner)) continue;
                    float height = hits[h].point.y - floorY;
                    if (height <= grid.Height[i]) continue;
                    grid.Height[i] = height;
                    grid.Owner[i] = owner;
                }

                grid.Blocked[i] = grid.Footprint.BodyBlocked[i];
                if (grid.Blocked[i]) grid.Owner[i] = grid.Footprint.BlockingOwner[i];
            }

            grid.LineOfSight = (a, b) =>
            {
                Vector3 from = new Vector3(a.x, floorY + a.y, a.z), to = new Vector3(b.x, floorY + b.y, b.z);
                Vector3 dir = to - from;
                int n = rays.Cast(from, dir.normalized, dir.magnitude);
                var hits = rays.Hits;
                for (int h = 0; h < n; h++)
                    if (obstacles.ContainsKey(hits[h].collider)) return false;
                return true;
            };

            grid.BeginLineBatch = requests => MapGrowthRayBatch.Schedule(physics,
                requests.Select(r => new MapGrowthRayRequest(r.Id, r.From + Vector3.up * floorY, r.To + Vector3.up * floorY)).ToArray(), obstacles.Keys);

            grid.ShotLine = (a, b) =>
            {
                Vector3 from = new Vector3(a.x, floorY + a.y, a.z), to = new Vector3(b.x, floorY + b.y, b.z);
                Vector3 dir = to - from;
                int n = rays.Cast(from, dir.normalized, dir.magnitude);
                var hits = rays.Hits;
                System.Array.Sort(hits, 0, n, HitDistanceComparer.Instance);
                int penetrations = 0;
                float resumeDistance = 0f;
                for (int h = 0; h < n; h++)
                {
                    // Пуля продолжает луч за выходом: вложенные объёмы внутри пройденной преграды не дают новых входов.
                    if (hits[h].distance < resumeDistance) continue;
                    if (!obstacles.ContainsKey(hits[h].collider)) continue;
                    CoverSurface surface = CoverSurface.Of(hits[h].collider);
                    CoverClass cover = surface != null ? surface.Class : CoverClass.Hard;
                    if (cover == CoverClass.Visual)
                    {
                        resumeDistance = hits[h].distance + WallPenetration.ExitOffset;
                        continue;
                    }
                    if (cover == CoverClass.Hard || surface.PenetrationModifier < WallPenetration.MinPenetrationModifier) return false;
                    if (++penetrations > WallPenetration.MaxPenetrations) return false;
                    if(CoverIntervalResolver.TryResolve(hits[h].collider,hits[h].point,dir.normalized,WallPenetration.ExitOffset,out var traversal))
                    {
                        if(traversal.blocked||traversal.thickness>WallPenetration.MaxThickness||traversal.penetrationModifier<WallPenetration.MinPenetrationModifier)return false;
                        resumeDistance=Vector3.Dot(traversal.resumePoint-from,dir.normalized);continue;
                    }
                    if (!WallPenetration.TryFindExit(hits[h].collider, hits[h].point, dir.normalized, out RaycastHit exit)) return false;
                    resumeDistance = Vector3.Dot(exit.point - from, dir.normalized) + WallPenetration.ExitOffset;
                }
                return true;
            };

            if (capturedZones == null) MarkZones(scene, grid, floorY, result.Problems);
            else
            {
                if (capturedZones.Length != grid.Count || capturedZones.Any(z => z != 0 && z != MapGrid.ZoneA && z != MapGrid.ZoneB))
                    throw new System.ArgumentException("Маска спавнов не соответствует сетке замороженной карты.");
                System.Array.Copy(capturedZones, grid.Zone, grid.Count);
            }
            result.Grid = grid;
        }

        private const int AllLayers = ~0;

        // Raycast не гарантирует ближайшие попадания при заполнении массива. Используется всеми видами лучей.
        private sealed class CompleteRayQuery
        {
            private const int MaximumHits = 4096;
            private readonly PhysicsScene physics;
            public RaycastHit[] Hits { get; private set; } = new RaycastHit[64];
            public CompleteRayQuery(PhysicsScene physics) => this.physics = physics;
            public int Cast(Vector3 from, Vector3 direction, float distance)
            {
                for (;;)
                {
                    int count = physics.Raycast(from, direction, Hits, distance, AllLayers, QueryTriggerInteraction.Ignore);
                    if (count < Hits.Length) return count;
                    if (Hits.Length >= MaximumHits)
                        throw new System.InvalidOperationException("Измерение луча неполно: не менее " + MaximumHits + " попаданий. Упростите пересекаемую геометрию карты.");
                    Hits = new RaycastHit[System.Math.Min(MaximumHits, Hits.Length * 2)];
                }
            }
        }

        private sealed class HitDistanceComparer : IComparer<RaycastHit>
        {
            public static readonly HitDistanceComparer Instance = new HitDistanceComparer();
            public int Compare(RaycastHit a, RaycastHit b) => a.distance.CompareTo(b.distance);
        }

        /// <summary>Толщина — меньший горизонтальный размер: у коробки — по её осям (поворот не раздувает), иначе — по AABB.</summary>
        private static float Thickness(Collider c)
        {
            if (c is BoxCollider box)
            {
                Vector3 size = Vector3.Scale(box.size, c.transform.lossyScale);
                Vector3 right = c.transform.right, forward = c.transform.forward;
                // Горизонтальные оси коробки — те, что лежат в плоскости пола.
                float x = Mathf.Abs(right.y) < 0.5f ? Mathf.Abs(size.x) : float.PositiveInfinity;
                float z = Mathf.Abs(forward.y) < 0.5f ? Mathf.Abs(size.z) : float.PositiveInfinity;
                float y = Mathf.Abs(c.transform.up.y) < 0.5f ? Mathf.Abs(size.y) : float.PositiveInfinity;
                return Mathf.Min(x, Mathf.Min(y, z));
            }
            return Mathf.Min(c.bounds.size.x, c.bounds.size.z);
        }

        private static void MarkZones(Scene scene, MapGrid grid, float floorY, List<string> problems)
        {
            TeamSpawnZone[] zones = scene.GetRootGameObjects()
                                         .SelectMany(r => r.GetComponentsInChildren<TeamSpawnZone>(true))
                                         .Where(z => z.HomeTeam != null)
                                         .OrderBy(z => z.HomeTeam.name, System.StringComparer.Ordinal)
                                         .ToArray();
            if (zones.Length != 2)
            {
                problems.Add($"спавн-зон с командой {zones.Length}, нужно две");
                return;
            }

            for (int z = 0; z < 2; z++)
            {
                var box = zones[z].GetComponent<BoxCollider>();
                byte side = z == 0 ? MapGrid.ZoneA : MapGrid.ZoneB;
                for (int i = 0; i < grid.Count; i++)
                {
                    Vector2 p = grid.Center(i);
                    Vector3 local = box.transform.InverseTransformPoint(new Vector3(p.x, floorY + 1f, p.y)) - box.center;
                    Vector3 half = box.size * 0.5f;
                    if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.y) <= half.y && Mathf.Abs(local.z) <= half.z)
                        grid.Zone[i] = side;
                }
            }
        }

        /// <summary>Имя с родителем — «CQB_Blockout/LD_Wall_Tall», чтобы найти объект в иерархии.</summary>
        private static string ShortPath(Transform t) =>
            t.parent != null ? $"{t.parent.name}/{t.name}" : t.name;
    }
}
