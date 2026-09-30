using System;
using System.Collections.Generic;
using UnityEngine;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>
    /// Арена сверху: в каждой клетке — можно ли там стоять, верх препятствия и чьё оно, зона
    /// стороны; плюс функция видимости между двумя точками.
    ///
    /// <para>
    /// <b>Проходимость</b> (<see cref="Blocked"/>) — есть ли твёрдое в полосе тела
    /// <see cref="LevelDesignRules.StepHeight"/>…<see cref="LevelDesignRules.BodyTop"/>: дверь с
    /// перемычкой проходима, окно с подоконником — нет. <b>Верх</b> (<see cref="Height"/>) — для
    /// классов высот и картинки. <b>Видимость</b> (<see cref="LineOfSight"/>) — отдельно от сетки:
    /// окно, бойница и щель в несколько сантиметров сеткой не разрешаются. Из сцены
    /// (<see cref="MapGridBuilder"/>) она — лучи по настоящим коллайдерам; у искусственной сетки
    /// тестов — по пролётам клеток (<see cref="FillSpan"/>).
    /// </para>
    /// <para>
    /// <b>Прострел</b> (<see cref="ShotLine"/>) — отдельно от видимости: Soft закрывает вид, но пуля его пробивает,
    /// Visual (листва, сетка) закрывает вид, а пуля пролетает; Hard останавливает и то и другое (T-41).
    /// </para>
    /// </summary>
    public sealed class MapGrid
    {
        /// <summary>Клетка вне спавн-зон.</summary>
        public const byte NoZone = 0;
        /// <summary>Зона стороны A (первая по имени команды-хозяина).</summary>
        public const byte ZoneA = 1;
        /// <summary>Зона стороны B.</summary>
        public const byte ZoneB = 2;

        public readonly int Width;
        public readonly int Depth;
        public readonly float Cell;
        /// <summary>Мировые XZ угла клетки (0, 0).</summary>
        public readonly Vector2 Origin;

        /// <summary>В полосе тела есть твёрдое — стоять и идти нельзя.</summary>
        public readonly bool[] Blocked;
        /// <summary>Верх самого высокого препятствия над полом, м; 0 — пусто.</summary>
        public readonly float[] Height;
        /// <summary>Индекс препятствия в <see cref="Obstacles"/> или −1.</summary>
        public readonly int[] Owner;
        /// <summary><see cref="NoZone"/>, <see cref="ZoneA"/> или <see cref="ZoneB"/>.</summary>
        public readonly byte[] Zone;
        /// <summary>Имена препятствий для сообщений.</summary>
        public readonly List<string> Obstacles = new List<string>();
        /// <summary>Класс укрытия каждого препятствия (по индексу <see cref="Obstacles"/>).</summary>
        public readonly List<CoverClass> ObstacleCover = new List<CoverClass>();

        /// <summary>
        /// Видно ли из точки в точку: XZ — мир, Y — над полом. По умолчанию — по пролётам клеток.
        /// </summary>
        public Func<Vector3, Vector3, bool> LineOfSight;

        /// <summary>
        /// Долетит ли пуля из точки в точку (координаты как у <see cref="LineOfSight"/>): сквозь Soft и Visual — да,
        /// сквозь Hard — нет. По умолчанию — по пролётам клеток.
        /// </summary>
        public Func<Vector3, Vector3, bool> ShotLine;

        /// <summary>Твёрдые пролёты клетки по высоте (x — низ, y — верх) и их класс — для видимости и прострела по сетке.</summary>
        private readonly List<(Vector2 span, CoverClass cover)>[] _spans;

        public MapGrid(int width, int depth, float cell, Vector2 origin)
        {
            Width = width;
            Depth = depth;
            Cell = cell;
            Origin = origin;
            Blocked = new bool[width * depth];
            Height = new float[width * depth];
            Owner = new int[width * depth];
            Zone = new byte[width * depth];
            _spans = new List<(Vector2, CoverClass)>[width * depth];
            for (int i = 0; i < Owner.Length; i++) Owner[i] = -1;
            LineOfSight = (a, b) => SpanClear(a, b, _ => true);
            ShotLine = (a, b) => SpanClear(a, b, cover => cover == CoverClass.Hard);
        }

        public int Count => Width * Depth;

        public int Index(int x, int z) => z * Width + x;

        public int X(int index) => index % Width;

        public int Z(int index) => index / Width;

        public bool InBounds(int x, int z) => x >= 0 && z >= 0 && x < Width && z < Depth;

        /// <summary>Мировые XZ центра клетки.</summary>
        public Vector2 Center(int index) =>
            Origin + new Vector2((X(index) + 0.5f) * Cell, (Z(index) + 0.5f) * Cell);

        /// <summary>Клетка, в которую попадает мировая точка XZ; −1 — вне сетки.</summary>
        public int IndexAt(Vector2 world)
        {
            int x = Mathf.FloorToInt((world.x - Origin.x) / Cell);
            int z = Mathf.FloorToInt((world.y - Origin.y) / Cell);
            return InBounds(x, z) ? Index(x, z) : -1;
        }

        /// <summary>Имя препятствия в клетке или пусто.</summary>
        public string OwnerName(int index) => Owner[index] >= 0 ? Obstacles[Owner[index]] : "";

        /// <summary>Сплошное препятствие от пола до <paramref name="height"/> (мир XZ, по центрам клеток).</summary>
        public void FillRect(Vector2 min, Vector2 max, float height, string name, CoverClass cover = CoverClass.Hard) =>
            FillSpan(min, max, 0f, height, name, cover);

        /// <summary>
        /// Твёрдый пролёт от <paramref name="bottom"/> до <paramref name="top"/> над полом:
        /// подоконник, перемычка над дверью или окном, балка.
        /// </summary>
        public void FillSpan(Vector2 min, Vector2 max, float bottom, float top, string name, CoverClass cover = CoverClass.Hard)
        {
            int owner = Obstacles.Count;
            Obstacles.Add(name);
            ObstacleCover.Add(cover);
            bool blocks = bottom < LevelDesignRules.BodyTop && top > LevelDesignRules.StepHeight;
            for (int i = 0; i < Count; i++)
            {
                Vector2 c = Center(i);
                if (c.x < min.x || c.x > max.x || c.y < min.y || c.y > max.y) continue;
                (_spans[i] ??= new List<(Vector2, CoverClass)>()).Add((new Vector2(bottom, top), cover));
                if (blocks) Blocked[i] = true;
                if (top > Height[i]) Height[i] = top;
                if (blocks || Owner[i] < 0) Owner[i] = owner;
            }
        }

        /// <summary>Разметить прямоугольник как спавн-зону стороны.</summary>
        public void FillZone(Vector2 min, Vector2 max, byte zone)
        {
            for (int i = 0; i < Count; i++)
            {
                Vector2 c = Center(i);
                if (c.x >= min.x && c.x <= max.x && c.y >= min.y && c.y <= max.y) Zone[i] = zone;
            }
        }

        /// <summary>Класс укрытия препятствия в клетке (Hard, если препятствия нет).</summary>
        public CoverClass CoverAt(int index) => Owner[index] >= 0 ? ObstacleCover[Owner[index]] : CoverClass.Hard;

        /// <summary>Луч по пролётам: перекрыт, если на его высоте в клетке пролёт, который <paramref name="stops"/>.</summary>
        private bool SpanClear(Vector3 a, Vector3 b, Func<CoverClass, bool> stops)
        {
            var from = new Vector2(a.x, a.z);
            var to = new Vector2(b.x, b.z);
            int steps = Mathf.Max(1, Mathf.CeilToInt((to - from).magnitude / (Cell * 0.5f)));
            for (int s = 1; s < steps; s++)
            {
                float t = (float)s / steps;
                int i = IndexAt(Vector2.Lerp(from, to, t));
                if (i < 0) return false;
                if (_spans[i] == null) continue;
                float h = Mathf.Lerp(a.y, b.y, t);
                foreach ((Vector2 span, CoverClass cover) in _spans[i])
                    if (h >= span.x && h <= span.y && stops(cover)) return false;
            }
            return true;
        }
    }
}
