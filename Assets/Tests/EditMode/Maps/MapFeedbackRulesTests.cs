using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.Editor.LevelDesign;

namespace VrBattlegrounds.Tests.Maps
{
    public class MapFeedbackRulesTests
    {
        [TestCase(true, true, 1.2f, true)]
        [TestCase(false, true, 1.2f, false)]
        [TestCase(true, false, 1.2f, false)]
        [TestCase(true, true, 0.4f, false)]
        public void LD52_Soft_предлагается_при_трёх_сильных_свойствах(bool view, bool flank, float width, bool expected)
        {
            Assert.AreEqual(expected, MapFeedbackRules.SuggestSoftCover(view, flank, width, 0.7f));
        }

        private static MapGrid Arena()
        {
            var g = new MapGrid(150, 180, 0.1f, new Vector2(-7.5f, -9f));
            g.FillZone(new Vector2(-7.5f, -9f), new Vector2(7.5f, -7f), MapGrid.ZoneA);
            g.FillZone(new Vector2(-7.5f, 7f), new Vector2(7.5f, 9f), MapGrid.ZoneB);
            return g;
        }

        [Test]
        public void LD49_Один_общий_вход_не_три_маршрута()
        {
            var g = Arena();
            g.FillRect(new Vector2(-7.5f, -5f), new Vector2(-1f, -4.8f), 2.5f, "Левая стена");
            g.FillRect(new Vector2(1f, -5f), new Vector2(7.5f, -4.8f), 2.5f, "Правая стена");
            Assert.IsNotEmpty(MapFeedbackRules.CheckApproaches(g, MapAnalyzer.Clearance(g)));
        }

        [Test]
        public void LD49_Три_пути_с_коннектором_допустимы()
        {
            var g = Arena();
            foreach (float x in new[] { -2.5f, 2.5f })
            foreach (float z in new[] { -3f, 3f })
                g.FillRect(new Vector2(x - 0.1f, z - 1.5f), new Vector2(x + 0.1f, z + 1.5f), 1.2f, "Разделитель");
            Assert.IsEmpty(MapFeedbackRules.CheckApproaches(g, MapAnalyzer.Clearance(g)));
        }

        [TestCase(0.1f)]
        [TestCase(0.68f)]
        public void LD50_Остаточная_полоса_между_блоками_запрещена(float gap)
        {
            var a = new Bounds(Vector3.zero, new Vector3(2, 2.5f, 0.15f));
            var b = new Bounds(new Vector3(0, 0, 0.15f + gap), a.size);
            Assert.IsNotEmpty(MapFeedbackRules.CheckResidualGaps(new[] { a, b }));
        }

        [TestCase(0f)]
        [TestCase(1.2f)]
        public void LD50_Соединение_или_полноценный_проход_допустимы(float gap)
        {
            var a = new Bounds(Vector3.zero, new Vector3(2, 2.5f, 0.15f));
            var b = new Bounds(new Vector3(0, 0, 0.15f + gap), a.size);
            Assert.IsEmpty(MapFeedbackRules.CheckResidualGaps(new[] { a, b }));
        }

        [Test]
        public void LD51_Открытая_карта_с_преобладанием_высокого_не_проходит()
        {
            var g = Arena();
            g.FillRect(new Vector2(-5, -2), new Vector2(5, -1), 2.5f, "Высокое");
            g.FillRect(new Vector2(-5, 1), new Vector2(-3, 2), 1.2f, "Низкое");
            Assert.IsNotEmpty(MapFeedbackRules.CheckOpenCover(g));
        }

        [Test]
        public void LD51_Низкое_и_среднее_преобладают_есть_место_присесть()
        {
            var g = Arena();
            g.FillRect(new Vector2(-5, -2), new Vector2(-4, -1), 2.5f, "Высокое");
            g.FillRect(new Vector2(-3, -2), new Vector2(0, -1), 1.2f, "Низкое");
            g.FillRect(new Vector2(1, -2), new Vector2(4, -1), 1.6f, "Среднее");
            Assert.IsEmpty(MapFeedbackRules.CheckOpenCover(g));
        }

        [Test]
        public void LD51_Низкое_под_высоким_не_маскирует_нарушение()
        {
            var g = Arena();
            g.FillRect(new Vector2(-5, -2), new Vector2(5, -1), 1.2f, "Низкое");
            g.FillRect(new Vector2(-5, -2), new Vector2(5, -1), 2.5f, "Высокое поверх");
            Assert.IsNotEmpty(MapFeedbackRules.CheckOpenCover(g));
        }
    }
}
