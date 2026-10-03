using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>
    /// Картинки отчёта о карте, вид сверху, север (+Z) вверху, сетка через 1 м (толще — через 5 м).
    ///
    /// <para>
    /// <b>Раскладка:</b> укрытия цветом класса (Low — зелёный, Mid — жёлтый, Tall — красный, вне
    /// классов — фиолетовый; Soft — тот же цвет в косую полоску: пробивается пулей; Visual — бирюзовый: закрывает
    /// только вид), зоны сторон (A — голубая, B — песочная), непроходимое у стен —
    /// темнее пола. Нарушения: узкий проход — пурпурный круг, прострел база—база — красные линии
    /// (до 30 кратчайших), карман — чёрный.
    /// <b>Видимость:</b> из каждой точки — какая доля чужой половины видна глазами (зелёный 0 % →
    /// красный 100 %), белый круг — самая «всевидящая» точка (LD-09).
    /// </para>
    /// </summary>
    public static class MapReportImage
    {
        private const int Px = 4;

        private static readonly Color32 Floor = new Color32(236, 236, 236, 255);
        private static readonly Color32 NearWall = new Color32(214, 214, 214, 255);
        private static readonly Color32 ZoneA = new Color32(196, 214, 246, 255);
        private static readonly Color32 ZoneB = new Color32(246, 222, 192, 255);
        private static readonly Color32 Low = new Color32(110, 185, 110, 255);
        private static readonly Color32 Mid = new Color32(235, 190, 70, 255);
        private static readonly Color32 Tall = new Color32(200, 75, 65, 255);
        private static readonly Color32 Unclassed = new Color32(130, 70, 170, 255);
        private static readonly Color32 VisualCover = new Color32(90, 200, 190, 255);
        private static readonly Color32 MixedCover = new Color32(80, 130, 205, 255);
        private static readonly Color32 PocketColor = new Color32(40, 40, 40, 255);
        private static readonly Color32 Passage = new Color32(230, 40, 200, 255);
        private static readonly Color32 Sight = new Color32(220, 30, 30, 255);
        private static readonly Color32 GridLine = new Color32(0, 0, 0, 28);
        private static readonly Color32 GridLineBold = new Color32(0, 0, 0, 70);

        public static byte[] Layout(MapGrid g, float[] clear,
                                    IEnumerable<MapAnalyzer.NarrowPassage> passages,
                                    IEnumerable<MapAnalyzer.Sightline> sightlines,
                                    IEnumerable<MapAnalyzer.Pocket> pockets)
        {
            var tex = new Canvas(g);
            float[] ownerTop = OwnerTops(g);
            for (int i = 0; i < g.Count; i++) tex.FillCell(i, BaseColor(g, clear, ownerTop, i), g.CoverAt(i) == VrBattlegrounds.Maps.CoverClass.Soft);

            foreach (MapAnalyzer.Pocket p in pockets.Where(p => p.Area > 0f))
                tex.Disc(p.Center, Mathf.Sqrt(p.Area / Mathf.PI), PocketColor);

            tex.Grid();

            foreach (MapAnalyzer.Sightline s in sightlines.OrderBy(s => s.Length).Take(30))
                tex.Line(s.From, s.To, Sight, 1);

            foreach (MapAnalyzer.NarrowPassage p in passages)
                tex.Ring(p.Center, 0.6f, Passage, 3);

            return tex.Encode();
        }

        public static byte[] Visibility(MapGrid g, float[] clear, MapAnalyzer.VisibilityStats stats)
        {
            var tex = new Canvas(g);
            for (int i = 0; i < g.Count; i++)
                tex.FillCell(i, MapAnalyzer.IsFree(g, i) ? (Color32)new Color(0.85f, 0.85f, 0.85f) : (Color32)new Color(0.3f, 0.3f, 0.3f));

            float half = LevelDesignRules.SampleStep * 0.5f;
            foreach (KeyValuePair<int, float> s in stats.Share)
            {
                Color c = Color.Lerp(new Color(0.2f, 0.75f, 0.3f), new Color(0.9f, 0.15f, 0.1f), Mathf.Clamp01(s.Value));
                tex.Rect(g.Center(s.Key) - new Vector2(half, half), g.Center(s.Key) + new Vector2(half, half), c, g);
            }

            tex.Grid();
            tex.Ring(stats.MaxEnemyShareAt, 0.4f, Color.white, 3);
            return tex.Encode();
        }

        /// <summary>
        /// Верх каждого препятствия целиком. Красить клетку по её собственной высоте нельзя:
        /// у бочки и «дорито» скругления и скаты ниже верха, и объект выглядел бы «вне классов».
        /// </summary>
        private static float[] OwnerTops(MapGrid g)
        {
            var top = new float[g.Obstacles.Count];
            for (int i = 0; i < g.Count; i++)
                if (g.Owner[i] >= 0) top[g.Owner[i]] = Mathf.Max(top[g.Owner[i]], g.Height[i]);
            return top;
        }

        private static Color32 BaseColor(MapGrid g, float[] clear, float[] ownerTop, int i)
        {
            if (!MapAnalyzer.IsFree(g, i))
            {
                if (g.MixedCoverAt(i)) return MixedCover;
                if (g.CoverAt(i) == VrBattlegrounds.Maps.CoverClass.Visual) return VisualCover;
                switch (MapAnalyzer.ClassOf(g.Owner[i] >= 0 ? ownerTop[g.Owner[i]] : g.Height[i]))
                {
                    case "Low": return Low;
                    case "Mid": return Mid;
                    case "Tall": return Tall;
                    default: return Unclassed;
                }
            }

            if (!MapAnalyzer.IsWalkable(g, clear, i)) return NearWall;
            if (g.Zone[i] == MapGrid.ZoneA) return ZoneA;
            if (g.Zone[i] == MapGrid.ZoneB) return ZoneB;
            return Floor;
        }

        /// <summary>Пиксельный холст поверх сетки: <see cref="Px"/> пикселей на клетку.</summary>
        private sealed class Canvas
        {
            private readonly MapGrid _g;
            private readonly int _w, _h;
            private readonly Color32[] _pixels;

            public Canvas(MapGrid g)
            {
                _g = g;
                _w = g.Width * Px;
                _h = g.Depth * Px;
                _pixels = new Color32[_w * _h];
            }

            /// <summary>Залить клетку; <paramref name="striped"/> — косая светлая полоска (Soft).</summary>
            public void FillCell(int i, Color32 c, bool striped = false)
            {
                int x0 = _g.X(i) * Px, y0 = _g.Z(i) * Px;
                Color32 light = Color32.Lerp(c, new Color32(255, 255, 255, 255), 0.6f);
                for (int y = 0; y < Px; y++)
                for (int x = 0; x < Px; x++)
                    _pixels[(y0 + y) * _w + x0 + x] = striped && ((x0 + x + y0 + y) % 6 < 2) ? light : c;
            }

            public void Rect(Vector2 min, Vector2 max, Color c, MapGrid g)
            {
                Vector2Int a = ToPx(min), b = ToPx(max);
                for (int y = Mathf.Max(0, a.y); y < Mathf.Min(_h, b.y); y++)
                for (int x = Mathf.Max(0, a.x); x < Mathf.Min(_w, b.x); x++)
                {
                    int cell = g.IndexAt(FromPx(x, y));
                    if (cell >= 0 && MapAnalyzer.IsFree(g, cell)) _pixels[y * _w + x] = c;
                }
            }

            public void Grid()
            {
                for (int m = Mathf.CeilToInt(_g.Origin.x); m <= _g.Origin.x + _g.Width * _g.Cell; m++)
                {
                    int x = ToPx(new Vector2(m, 0f)).x;
                    for (int y = 0; y < _h; y++) Blend(x, y, m % 5 == 0 ? GridLineBold : GridLine);
                }
                for (int m = Mathf.CeilToInt(_g.Origin.y); m <= _g.Origin.y + _g.Depth * _g.Cell; m++)
                {
                    int y = ToPx(new Vector2(0f, m)).y;
                    for (int x = 0; x < _w; x++) Blend(x, y, m % 5 == 0 ? GridLineBold : GridLine);
                }
            }

            public void Line(Vector2 a, Vector2 b, Color32 c, int thickness)
            {
                Vector2Int p = ToPx(a), q = ToPx(b);
                int steps = Mathf.Max(Mathf.Abs(q.x - p.x), Mathf.Abs(q.y - p.y), 1);
                for (int s = 0; s <= steps; s++)
                {
                    float t = (float)s / steps;
                    int x = Mathf.RoundToInt(Mathf.Lerp(p.x, q.x, t)), y = Mathf.RoundToInt(Mathf.Lerp(p.y, q.y, t));
                    for (int dy = -thickness / 2; dy <= thickness / 2; dy++)
                    for (int dx = -thickness / 2; dx <= thickness / 2; dx++)
                        Set(x + dx, y + dy, c);
                }
            }

            public void Disc(Vector2 center, float radius, Color32 c)
            {
                Vector2Int o = ToPx(center);
                int r = Mathf.Max(1, Mathf.RoundToInt(radius / _g.Cell * Px));
                for (int dy = -r; dy <= r; dy++)
                for (int dx = -r; dx <= r; dx++)
                    if (dx * dx + dy * dy <= r * r) Set(o.x + dx, o.y + dy, c);
            }

            public void Ring(Vector2 center, float radius, Color32 c, int thickness)
            {
                Vector2Int o = ToPx(center);
                float r = radius / _g.Cell * Px;
                for (int dy = -(int)r - thickness; dy <= r + thickness; dy++)
                for (int dx = -(int)r - thickness; dx <= r + thickness; dx++)
                {
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d >= r - thickness * 0.5f && d <= r + thickness * 0.5f) Set(o.x + dx, o.y + dy, c);
                }
            }

            public byte[] Encode()
            {
                var texture = new Texture2D(_w, _h, TextureFormat.RGBA32, false);
                try
                {
                    texture.SetPixels32(_pixels);
                    texture.Apply();
                    return texture.EncodeToPNG();
                }
                finally
                {
                    Object.DestroyImmediate(texture);
                }
            }

            private Vector2Int ToPx(Vector2 world) => new Vector2Int(
                Mathf.RoundToInt((world.x - _g.Origin.x) / _g.Cell * Px),
                Mathf.RoundToInt((world.y - _g.Origin.y) / _g.Cell * Px));

            private Vector2 FromPx(int x, int y) =>
                _g.Origin + new Vector2((x + 0.5f) / Px * _g.Cell, (y + 0.5f) / Px * _g.Cell);

            private void Set(int x, int y, Color32 c)
            {
                if (x >= 0 && y >= 0 && x < _w && y < _h) _pixels[y * _w + x] = c;
            }

            private void Blend(int x, int y, Color32 c)
            {
                if (x < 0 || y < 0 || x >= _w || y >= _h) return;
                _pixels[y * _w + x] = Color32.Lerp(_pixels[y * _w + x], new Color32(c.r, c.g, c.b, 255), c.a / 255f);
            }
        }
    }
}
