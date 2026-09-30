using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
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

        public static Result Build(Scene scene, float cell = DefaultCell)
        {
            var result = new Result();
            Collider[] colliders = scene.GetRootGameObjects()
                                        .SelectMany(r => r.GetComponentsInChildren<Collider>(false))
                                        .Where(c => c.enabled && !c.isTrigger)
                                        .ToArray();

            int ground = LayerMask.NameToLayer("Ground");
            Collider[] floors = colliders.Where(c => c.gameObject.layer == ground).ToArray();
            if (floors.Length == 0)
            {
                result.Problems.Add("нет пола на слое Ground");
                return result;
            }

            Bounds floor = floors[0].bounds;
            foreach (Collider c in floors) floor.Encapsulate(c.bounds);
            float floorY = floor.max.y;

            var obstacles = new Dictionary<Collider, int>();
            var vaultable = new HashSet<Collider>();
            var grid = new MapGrid(
                Mathf.RoundToInt(floor.size.x / cell),
                Mathf.RoundToInt(floor.size.z / cell),
                cell,
                new Vector2(floor.min.x, floor.min.z));

            foreach (Collider c in colliders)
            {
                if (c.gameObject.layer == ground || c.attachedRigidbody != null) continue;
                Bounds b = c.bounds;
                if (b.min.y >= floorY + ObstacleCeiling || b.max.y <= floorY + LevelDesignRules.StepHeight) continue;
                if (b.max.x < floor.min.x || b.min.x > floor.max.x || b.max.z < floor.min.z || b.min.z > floor.max.z) continue;

                obstacles[c] = grid.Obstacles.Count;
                grid.Obstacles.Add(ShortPath(c.transform));
                CoverSurface surface = CoverSurface.Of(c);
                grid.ObstacleCover.Add(surface != null ? surface.Class : CoverClass.Hard);

                if (c.GetComponentInParent<VaultableObstacle>() != null)
                {
                    vaultable.Add(c);
                    result.Vaultables.Add(new MapAnalyzer.Vaultable
                    {
                        Name = $"{ShortPath(c.transform)} у ({b.center.x:F1}; {b.center.z:F1})",
                        Height = b.max.y - floorY,
                        Thickness = Thickness(c),
                    });
                    continue;
                }

                if (c.gameObject.name.StartsWith(BlockoutPrefix))
                    result.BlockoutTops.Add(new KeyValuePair<string, float>(
                        $"{ShortPath(c.transform)} у ({b.center.x:F1}; {b.center.z:F1})", b.max.y - floorY));
            }

            PhysicsScene physics = scene.GetPhysicsScene();
            var hits = new RaycastHit[64];
            var overlaps = new Collider[32];
            float top = floorY + 10f;
            float bandBottom = floorY + LevelDesignRules.StepHeight, bandTop = floorY + LevelDesignRules.BodyTop;
            var bandHalf = new Vector3(cell * 0.49f, (bandTop - bandBottom) * 0.5f, cell * 0.49f);

            for (int i = 0; i < grid.Count; i++)
            {
                Vector2 p = grid.Center(i);

                int count = physics.Raycast(new Vector3(p.x, top, p.y), Vector3.down, hits, top - floorY - 0.01f,
                                            AllLayers, QueryTriggerInteraction.Ignore);
                for (int h = 0; h < count; h++)
                {
                    if (!obstacles.TryGetValue(hits[h].collider, out int owner)) continue;
                    float height = hits[h].point.y - floorY;
                    if (height <= grid.Height[i]) continue;
                    grid.Height[i] = height;
                    grid.Owner[i] = owner;
                }

                int inBand = physics.OverlapBox(new Vector3(p.x, (bandBottom + bandTop) * 0.5f, p.y), bandHalf, overlaps,
                                                Quaternion.identity, AllLayers, QueryTriggerInteraction.Ignore);
                for (int o = 0; o < inBand; o++)
                {
                    if (vaultable.Contains(overlaps[o]) || !obstacles.TryGetValue(overlaps[o], out int owner)) continue;
                    grid.Blocked[i] = true;
                    grid.Owner[i] = owner;
                    break;
                }
            }

            grid.LineOfSight = (a, b) =>
            {
                Vector3 from = new Vector3(a.x, floorY + a.y, a.z), to = new Vector3(b.x, floorY + b.y, b.z);
                Vector3 dir = to - from;
                int n = physics.Raycast(from, dir.normalized, hits, dir.magnitude, AllLayers, QueryTriggerInteraction.Ignore);
                for (int h = 0; h < n; h++)
                    if (obstacles.ContainsKey(hits[h].collider)) return false;
                return true;
            };

            grid.ShotLine = (a, b) =>
            {
                Vector3 from = new Vector3(a.x, floorY + a.y, a.z), to = new Vector3(b.x, floorY + b.y, b.z);
                Vector3 dir = to - from;
                int n = physics.Raycast(from, dir.normalized, hits, dir.magnitude, AllLayers, QueryTriggerInteraction.Ignore);
                int penetrations = 0;
                for (int h = 0; h < n; h++)
                {
                    if (!obstacles.ContainsKey(hits[h].collider)) continue;
                    CoverSurface surface = CoverSurface.Of(hits[h].collider);
                    CoverClass cover = surface != null ? surface.Class : CoverClass.Hard;
                    if (cover == CoverClass.Visual) continue;
                    if (cover == CoverClass.Hard || surface.PenetrationModifier < WallPenetration.MinPenetrationModifier) return false;
                    if (++penetrations > WallPenetration.MaxPenetrations) return false;
                    if (!WallPenetration.TryFindExit(hits[h].collider, hits[h].point, dir.normalized, out _)) return false;
                }
                return true;
            };

            MarkZones(scene, grid, floorY, result.Problems);
            result.Grid = grid;
            return result;
        }

        private const int AllLayers = ~0;

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
