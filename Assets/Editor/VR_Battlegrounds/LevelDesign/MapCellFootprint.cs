using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>
    /// Производный клеточный след настоящих коллайдеров. Общий для измерителя и
    /// кандидатов выращивателя: занятость не заменяет геометрию, обзор или пробитие.
    /// </summary>
    public sealed class MapCellFootprint
    {
        public readonly bool[] Touched;
        public readonly bool[] BodyBlocked;
        /// <summary>Все владельцы, задевшие клетку; не только последний найденный коллайдер.</summary>
        public readonly int[][] Owners;
        public readonly int[] BlockingOwner;

        private MapCellFootprint(int count)
        {
            Touched = new bool[count];
            BodyBlocked = new bool[count];
            Owners = new int[count][];
            BlockingOwner = new int[count];
            for (int i = 0; i < count; i++) BlockingOwner[i] = -1;
        }

        /// <summary>
        /// Снимок следа на заданной сетке. Касание границы без площади исключается
        /// минимальным численным допуском; процент заполнения не используется.
        /// Перемычка может занимать клетку, сохраняя проходимость под ней.
        /// </summary>
        public static MapCellFootprint Capture(Scene scene, MapGrid grid, float floorY,
            IReadOnlyDictionary<Collider, int> obstacles, ISet<Collider> walkThrough = null)
        {
            if (!scene.IsValid() || !scene.isLoaded || grid == null || obstacles == null ||
                grid.Cell <= 0 || float.IsNaN(grid.Cell) || float.IsInfinity(grid.Cell) ||
                float.IsNaN(floorY) || float.IsInfinity(floorY))
                throw new ArgumentException("Нужны загруженная сцена, конечная сетка и набор препятствий.");
            var result = new MapCellFootprint(grid.Count);
            PhysicsScene physics = scene.GetPhysicsScene();
            var buffer = new Collider[32];
            var owners = new HashSet<int>();
            float epsilon = Mathf.Min(.00001f, grid.Cell * .00001f);
            float halfCell = grid.Cell / 2 - epsilon;
            float ceiling = MapGridBuilder.ObstacleCeiling;
            var footprintHalf = new Vector3(halfCell, (ceiling - epsilon) / 2, halfCell);
            float bodyBottom = LevelDesignRules.StepHeight, bodyTop = LevelDesignRules.BodyTop;
            var bodyHalf = new Vector3(halfCell, (bodyTop - bodyBottom) / 2, halfCell);
            for (int i = 0; i < grid.Count; i++)
            {
                Vector2 p = grid.Center(i);
                owners.Clear();
                int count = Query(physics, new Vector3(p.x, floorY + (ceiling + epsilon) / 2, p.y), footprintHalf, ref buffer);
                for (int h = 0; h < count; h++)
                    if (obstacles.TryGetValue(buffer[h], out int owner)) owners.Add(owner);
                var ids = owners.Count == 0 ? Array.Empty<int>() : new int[owners.Count];
                owners.CopyTo(ids);
                Array.Sort(ids);
                result.Owners[i] = ids;
                result.Touched[i] = ids.Length != 0;

                count = Query(physics, new Vector3(p.x, floorY + (bodyBottom + bodyTop) / 2, p.y), bodyHalf, ref buffer);
                for (int h = 0; h < count; h++)
                {
                    Collider c = buffer[h];
                    if (!obstacles.TryGetValue(c, out int owner) || walkThrough != null && walkThrough.Contains(c)) continue;
                    result.BodyBlocked[i] = true;
                    // Стабильный представитель для прежних отчётов; полный список хранится выше.
                    if (result.BlockingOwner[i] < 0 || owner < result.BlockingOwner[i]) result.BlockingOwner[i] = owner;
                }
            }
            return result;
        }

        private static int Query(PhysicsScene physics, Vector3 center, Vector3 half, ref Collider[] buffer)
        {
            // Заполненный NonAlloc-буфер не доказывает полноту: повторяем с большим объёмом.
            int count;
            while ((count = physics.OverlapBox(center, half, buffer, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) == buffer.Length)
                Array.Resize(ref buffer, checked(buffer.Length * 2));
            return count;
        }
    }
}
