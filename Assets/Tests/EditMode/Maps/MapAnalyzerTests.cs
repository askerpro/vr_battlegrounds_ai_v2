using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.Editor.LevelDesign;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Tests.Maps
{
    /// <summary>
    /// Проверки <see cref="MapAnalyzer"/> на искусственных картах 10×12 м: в каждой паре один
    /// сценарий нарушает правило, другой — нет. Так доказано, что проверка ловит нарушение и
    /// не шумит на нормальной геометрии; на настоящих картах её гоняет <c>MapPrinciplesTests</c>.
    /// </summary>
    public class MapAnalyzerTests
    {
        private const float Tall = 2.5f;
        private const float Mid = 1.6f;

        /// <summary>Пустая арена 10×12 м: зона A — южные 2 м, зона B — северные.</summary>
        private static MapGrid Arena()
        {
            var g = new MapGrid(100, 120, 0.1f, Vector2.zero);
            g.FillZone(new Vector2(0f, 0f), new Vector2(10f, 2f), MapGrid.ZoneA);
            g.FillZone(new Vector2(0f, 10f), new Vector2(10f, 12f), MapGrid.ZoneB);
            return g;
        }

        /// <summary>Стена поперёк арены на z 5.5–5.7 со щелью шириной <paramref name="gap"/> посередине.</summary>
        private static MapGrid WallWithGap(float gap)
        {
            MapGrid g = Arena();
            g.FillRect(new Vector2(0f, 5.5f), new Vector2(5f - gap / 2f, 5.7f), Tall, "Запад");
            g.FillRect(new Vector2(5f + gap / 2f, 5.5f), new Vector2(10f, 5.7f), Tall, "Восток");
            return g;
        }

        // ── LD-23 ────────────────────────────────────────────────────────────

        [Test]
        public void Пустая_арена_без_нарушений()
        {
            MapGrid g = Arena();
            float[] clear = MapAnalyzer.Clearance(g);

            Assert.IsEmpty(MapAnalyzer.FindNarrowPassages(g, clear));
            Assert.IsEmpty(MapAnalyzer.FindUnreachable(g, clear));
        }

        [Test]
        public void Щель_0_8_м_узкий_проход()
        {
            MapGrid g = WallWithGap(0.8f);
            List<MapAnalyzer.NarrowPassage> found = MapAnalyzer.FindNarrowPassages(g, MapAnalyzer.Clearance(g));

            Assert.AreEqual(1, found.Count, string.Join("\n", found));
            Assert.AreEqual(0.8f, found[0].Width, 0.15f, found[0].ToString());
            StringAssert.Contains("Запад", found[0].Between);
            StringAssert.Contains("Восток", found[0].Between);
        }

        [Test]
        public void Проход_1_2_м_не_нарушение()
        {
            MapGrid g = WallWithGap(1.2f);
            Assert.IsEmpty(MapAnalyzer.FindNarrowPassages(g, MapAnalyzer.Clearance(g)));
        }

        [Test]
        public void Тупиковая_ниша_0_8_м_не_проход()
        {
            MapGrid g = Arena();
            // Ниша шириной 0.8 м и глубиной 1.5 м, открытая на юг.
            g.FillRect(new Vector2(4f, 6f), new Vector2(4.6f, 7.5f), Tall, "Левая");
            g.FillRect(new Vector2(5.4f, 6f), new Vector2(6f, 7.5f), Tall, "Правая");
            g.FillRect(new Vector2(4f, 7.5f), new Vector2(6f, 8f), Tall, "Задняя");

            Assert.IsEmpty(MapAnalyzer.FindNarrowPassages(g, MapAnalyzer.Clearance(g)));
        }

        // ── LD-25 ────────────────────────────────────────────────────────────

        [Test]
        public void Щель_0_4_м_отрезает_половину()
        {
            MapGrid g = WallWithGap(0.4f);
            float[] clear = MapAnalyzer.Clearance(g);

            List<MapAnalyzer.Pocket> pockets = MapAnalyzer.FindUnreachable(g, clear);
            Assert.AreEqual(2, pockets.Count, "Каждая сторона должна потерять чужую половину:\n" + string.Join("\n", pockets));
            Assert.IsEmpty(MapAnalyzer.FindNarrowPassages(g, clear), "Щель уже плеч — стена, а не проход.");
        }

        [Test]
        public void Замкнутая_комната_недостижима()
        {
            MapGrid g = Arena();
            g.FillRect(new Vector2(3f, 5f), new Vector2(6f, 5.2f), Tall, "Ю");
            g.FillRect(new Vector2(3f, 7.8f), new Vector2(6f, 8f), Tall, "С");
            g.FillRect(new Vector2(3f, 5f), new Vector2(3.2f, 8f), Tall, "З");
            g.FillRect(new Vector2(5.8f, 5f), new Vector2(6f, 8f), Tall, "В");

            List<MapAnalyzer.Pocket> pockets = MapAnalyzer.FindUnreachable(g, MapAnalyzer.Clearance(g));
            Assert.AreEqual(2, pockets.Count, string.Join("\n", pockets));
            Assert.IsTrue(pockets.All(p => (p.Center - new Vector2(4.5f, 6.5f)).magnitude < 0.3f), string.Join("\n", pockets));
        }

        // ── LD-15 ────────────────────────────────────────────────────────────

        [Test]
        public void Открытая_арена_простреливается_от_базы_до_базы()
        {
            MapGrid g = Arena();
            Assert.IsNotEmpty(MapAnalyzer.FindBaseToBaseSightlines(g, MapAnalyzer.Clearance(g)));
        }

        [Test]
        public void Высокая_стена_закрывает_прострел_средняя_нет()
        {
            MapGrid tall = Arena();
            tall.FillRect(new Vector2(0f, 5.5f), new Vector2(10f, 5.7f), Tall, "Высокая");
            Assert.IsEmpty(MapAnalyzer.FindBaseToBaseSightlines(tall, MapAnalyzer.Clearance(tall)));

            MapGrid mid = Arena();
            mid.FillRect(new Vector2(0f, 5.5f), new Vector2(10f, 5.7f), Mid, "Средняя");
            Assert.IsNotEmpty(MapAnalyzer.FindBaseToBaseSightlines(mid, MapAnalyzer.Clearance(mid)),
                "Средняя стена (1.6 м) оставляет голову открытой — прострел глаза в глаза есть.");
        }

        // ── Окна и двери ─────────────────────────────────────────────────────

        /// <summary>
        /// Высокая стена поперёк арены на z 5.9–6.1 с проёмом x 4.4–5.6: снизу до <paramref name="sillTop"/>
        /// и сверху от <paramref name="lintelBottom"/> — твёрдое.
        /// </summary>
        private static MapGrid WallWithOpening(float sillTop, float lintelBottom)
        {
            MapGrid g = Arena();
            g.FillRect(new Vector2(0f, 5.9f), new Vector2(4.4f, 6.1f), Tall, "Стена_З");
            g.FillRect(new Vector2(5.6f, 5.9f), new Vector2(10f, 6.1f), Tall, "Стена_В");
            if (sillTop > 0f) g.FillSpan(new Vector2(4.4f, 5.9f), new Vector2(5.6f, 6.1f), 0f, sillTop, "Подоконник");
            g.FillSpan(new Vector2(4.4f, 5.9f), new Vector2(5.6f, 6.1f), lintelBottom, Tall, "Перемычка");
            return g;
        }

        [Test]
        public void Окно_на_уровне_груди_видно_присевшим_и_не_проходимо()
        {
            MapGrid g = WallWithOpening(sillTop: 1.0f, lintelBottom: 1.6f);
            float[] clear = MapAnalyzer.Clearance(g);
            Vector2 a = new Vector2(5f, 3f), b = new Vector2(5f, 9f);

            Assert.IsFalse(MapAnalyzer.Visible(g, a, LevelDesignRules.EyeHeight, b, LevelDesignRules.EyeHeight),
                           "Стоя глаза (1.7 м) выше окна (1.0–1.6 м) — перемычка закрывает.");
            Assert.IsTrue(MapAnalyzer.SeeEachOther(g, a, b, out float ha, out float hb), "Присевшие видят друг друга в окно.");
            Assert.IsTrue(MapAnalyzer.ThroughOpening(g, a, ha, b, hb), "Контакт через окно, а не поверх укрытия.");
            Assert.IsNotEmpty(MapAnalyzer.FindBaseToBaseSightlines(g, clear), "Прострел база—база через окно — тоже прострел.");
            Assert.AreEqual(2, MapAnalyzer.FindUnreachable(g, clear).Count, "Через подоконник не пройти — половины разделены.");
        }

        [Test]
        public void Дверь_с_перемычкой_проходима_и_видна_насквозь()
        {
            MapGrid g = WallWithOpening(sillTop: 0f, lintelBottom: 2.0f);
            float[] clear = MapAnalyzer.Clearance(g);
            Vector2 a = new Vector2(5f, 3f), b = new Vector2(5f, 9f);

            Assert.IsEmpty(MapAnalyzer.FindUnreachable(g, clear), "Под перемычкой на 2.0 м проходят.");
            Assert.IsTrue(MapAnalyzer.Visible(g, a, LevelDesignRules.EyeHeight, b, LevelDesignRules.EyeHeight));
            Assert.IsTrue(MapAnalyzer.ThroughOpening(g, a, LevelDesignRules.EyeHeight, b, LevelDesignRules.EyeHeight));
        }

        [Test]
        public void Поверх_низкого_укрытия_не_проём()
        {
            MapGrid g = Arena();
            g.FillRect(new Vector2(0f, 5.9f), new Vector2(10f, 6.1f), 1.2f, "Низкая");
            Vector2 a = new Vector2(5f, 3f), b = new Vector2(5f, 9f);

            Assert.IsTrue(MapAnalyzer.Visible(g, a, LevelDesignRules.EyeHeight, b, LevelDesignRules.EyeHeight));
            Assert.IsFalse(MapAnalyzer.ThroughOpening(g, a, LevelDesignRules.EyeHeight, b, LevelDesignRules.EyeHeight));
        }

        // ── Прострел (T-41) ──────────────────────────────────────────────────

        private static MapGrid WallAcross(CoverClass cover)
        {
            MapGrid g = Arena();
            g.FillRect(new Vector2(0f, 5.9f), new Vector2(10f, 6.1f), Tall, "Стена_" + cover, cover);
            return g;
        }

        [TestCase(CoverClass.Hard, false)]
        [TestCase(CoverClass.Soft, true)]
        [TestCase(CoverClass.Visual, true)]
        public void Стена_закрывает_вид_а_простреливается_по_классу(CoverClass cover, bool shootable)
        {
            MapGrid g = WallAcross(cover);
            float[] clear = MapAnalyzer.Clearance(g);

            Assert.IsEmpty(MapAnalyzer.FindBaseToBaseSightlines(g, clear), $"{cover}: высокая стена закрывает вид.");
            Assert.AreEqual(shootable, MapAnalyzer.CanShoot(g, new Vector2(5f, 3f), new Vector2(5f, 9f)), $"{cover}: пуля долетает?");
            Assert.AreEqual(shootable, MapAnalyzer.FindBaseToBaseShotlines(g, clear).Count > 0,
                            $"{cover}: прострел спавна вслепую.");
        }

        // ── LD-48 ────────────────────────────────────────────────────────────

        [Test]
        public void Перешагиваемое_не_выше_метра_и_не_толще_30_см()
        {
            var items = new[]
            {
                new MapAnalyzer.Vaultable { Name = "Заборчик", Height = 0.9f, Thickness = 0.1f },
                new MapAnalyzer.Vaultable { Name = "Высокий", Height = 1.2f, Thickness = 0.1f },
                new MapAnalyzer.Vaultable { Name = "Толстый", Height = 0.9f, Thickness = 0.6f },
            };

            List<string> problems = MapAnalyzer.CheckVaultables(items);
            Assert.AreEqual(2, problems.Count, string.Join("\n", problems));
            Assert.IsTrue(problems.Any(p => p.StartsWith("Высокий") && p.Contains("высотой")), string.Join("\n", problems));
            Assert.IsTrue(problems.Any(p => p.StartsWith("Толстый") && p.Contains("толщиной")), string.Join("\n", problems));
        }

        // ── LD-20 ────────────────────────────────────────────────────────────

        [Test]
        public void Высота_вне_классов_и_несовпадение_с_именем()
        {
            var tops = new Dictionary<string, float>
            {
                ["LD_Wall_Tall"] = 2.5f,
                ["LD_Wall_Mid"] = 1.6f,
                ["LD_Block_Low"] = 1.2f,
                ["LD_Crate"] = 1.2f,
                ["LD_Beam_Low"] = 1.9f,
                ["LD_Can_Low"] = 1.6f,
            };

            List<string> problems = MapAnalyzer.CheckCoverHeights(tops);
            Assert.AreEqual(2, problems.Count, string.Join("\n", problems));
            Assert.IsTrue(problems.Any(p => p.StartsWith("LD_Beam_Low") && p.Contains("ни Low")), string.Join("\n", problems));
            Assert.IsTrue(problems.Any(p => p.StartsWith("LD_Can_Low") && p.Contains("это Mid")), string.Join("\n", problems));
        }
    }
}
