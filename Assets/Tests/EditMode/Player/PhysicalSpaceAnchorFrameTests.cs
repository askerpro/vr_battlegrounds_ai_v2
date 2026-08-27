using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.PhysicalSpaceUtils;

namespace VrBattlegrounds.Tests.Player
{
    /// <summary>
    /// Система координат карты по паре якорей — задача <b>T-30</b>, находка <b>CAL-01</b>.
    ///
    /// <para>
    /// Что доказывают тесты. Место откалиброванного игрока обязано пережить смену карты,
    /// а мировые координаты для этого не годятся: обе карты проекта собраны из одного
    /// префаба арены, но в <c>TestMap1</c> он повёрнут на 90° вокруг Y относительно
    /// <c>TestMap2</c>. Ключевой тест — <see cref="Место_в_арене_не_зависит_от_поворота_карты"/>:
    /// одна и та же точка арены на двух картах даёт одни и те же координаты относительно
    /// якорей, хотя в мировых расходится на метры.
    /// </para>
    ///
    /// <para>
    /// Числа взяты из реальных карт: якоря лежат в префабе <c>Environment</c>
    /// (район Толстого) в точках <c>(-2.74, -0.01, -3.60)</c> и <c>(2.75, -0.01, -3.60)</c>
    /// относительно корня арены, зоны спавна — в <c>(0, 0, ±8.06)</c>.
    /// </para>
    /// </summary>
    public class PhysicalSpaceAnchorFrameTests
    {
        // ── Расстановка арены проекта, в координатах корня Environment ────────

        private static readonly Vector3 AnchorZero = new Vector3(-2.74f, -0.01f, -3.60f);
        private static readonly Vector3 AnchorOne  = new Vector3(2.75f, -0.01f, -3.60f);

        /// <summary>Поворот арены в <c>TestMap1</c> относительно <c>TestMap2</c>.</summary>
        private static readonly Quaternion Map1Rotation = Quaternion.Euler(0f, 90f, 0f);

        private static PhysicalSpaceAnchorFrame Build(Vector3 first, Vector3 second)
        {
            PhysicalSpaceAnchorFrame frame;
            string diagnosis;

            Assert.IsTrue(PhysicalSpaceAnchorFrame.TryBuild(first, second, out frame, out diagnosis),
                "Пара якорей разведена нормально, система координат обязана построиться: " + diagnosis);

            return frame;
        }

        [Test]
        public void Точка_возвращается_в_себя_после_двух_переводов()
        {
            PhysicalSpaceAnchorFrame frame = Build(AnchorZero, AnchorOne);

            Vector3 world = new Vector3(3.5f, 1.7f, -12.25f);
            Vector3 back = frame.ToWorld(frame.ToLocal(world));

            Assert.AreEqual(world.x, back.x, 1e-3f);
            Assert.AreEqual(world.y, back.y, 1e-3f);
            Assert.AreEqual(world.z, back.z, 1e-3f);
        }

        [Test]
        public void Первый_якорь_становится_началом_координат()
        {
            PhysicalSpaceAnchorFrame frame = Build(AnchorZero, AnchorOne);

            Vector3 local = frame.ToLocal(AnchorZero);

            Assert.AreEqual(0f, local.magnitude, 1e-3f,
                "Начало системы координат — якорь с меньшим id, иначе позиции считаются от чего попало.");
        }

        [Test]
        public void Второй_якорь_лежит_на_оси_вперёд()
        {
            PhysicalSpaceAnchorFrame frame = Build(AnchorZero, AnchorOne);

            Vector3 local = frame.ToLocal(AnchorOne);

            Assert.AreEqual(0f, local.x, 1e-3f, "Ось на второй якорь обязана быть +Z, а не какой попало.");
            Assert.AreEqual(frame.Separation, local.z, 1e-3f);
            Assert.AreEqual(5.49f, frame.Separation, 0.01f,
                "База якорей арены проекта — 5,49 м; расхождение значит, что расстановку поменяли.");
        }

        [Test]
        public void Место_в_арене_не_зависит_от_поворота_карты()
        {
            // Та же арена, повёрнутая на 90° вокруг Y: ровно так TestMap1 отличается
            // от TestMap2. Мировые координаты одной и той же точки арены при этом другие.
            PhysicalSpaceAnchorFrame map2 = Build(AnchorZero, AnchorOne);
            PhysicalSpaceAnchorFrame map1 = Build(Map1Rotation * AnchorZero, Map1Rotation * AnchorOne);

            Vector3 pointOnMap2 = new Vector3(0f, 0f, 0.002f);          // центр арены
            Vector3 pointOnMap1 = Map1Rotation * pointOnMap2;

            Assert.Greater(Vector3.Distance(pointOnMap2, pointOnMap1), 0f,
                "Опыт бессмысленен, если поворот не сдвинул точку.");

            Vector3 localOnMap2 = map2.ToLocal(pointOnMap2);
            Vector3 localOnMap1 = map1.ToLocal(pointOnMap1);

            Assert.AreEqual(0f, Vector3.Distance(localOnMap1, localOnMap2), 1e-3f,
                "Одно и то же место арены обязано иметь одни и те же координаты относительно якорей " +
                "на обеих картах — иначе одна калибровка на сессию работать не может.");
        }

        [Test]
        public void Позиция_перенесённая_между_картами_попадает_в_ту_же_точку_арены()
        {
            PhysicalSpaceAnchorFrame map2 = Build(AnchorZero, AnchorOne);
            PhysicalSpaceAnchorFrame map1 = Build(Map1Rotation * AnchorZero, Map1Rotation * AnchorOne);

            // Игрок стоял здесь на TestMap2 — это и есть «его место», снятое калибровкой.
            Vector3 stoodOnMap2 = new Vector3(1.5f, 0f, 4f);

            Vector3 restoredOnMap1 = map1.ToWorld(map2.ToLocal(stoodOnMap2));
            Vector3 expectedOnMap1 = Map1Rotation * stoodOnMap2;

            Assert.AreEqual(0f, Vector3.Distance(restoredOnMap1, expectedOnMap1), 1e-3f,
                "Перенос через систему координат якорей обязан поставить игрока в ту же точку арены.");

            Assert.Greater(Vector3.Distance(restoredOnMap1, stoodOnMap2), 1f,
                "Наивное сохранение мировой позиции дало бы другую точку — ради этого система координат и заведена.");
        }

        [Test]
        public void Поворот_игрока_переносится_вместе_с_позицией()
        {
            PhysicalSpaceAnchorFrame map2 = Build(AnchorZero, AnchorOne);
            PhysicalSpaceAnchorFrame map1 = Build(Map1Rotation * AnchorZero, Map1Rotation * AnchorOne);

            Quaternion facedOnMap2 = Quaternion.Euler(0f, 37f, 0f);

            Quaternion restored = map1.ToWorld(map2.ToLocal(facedOnMap2));
            Quaternion expected = Map1Rotation * facedOnMap2;

            Assert.AreEqual(0f, Quaternion.Angle(restored, expected), 0.05f,
                "Игрок обязан смотреть туда же относительно арены, иначе после смены карты он окажется " +
                "лицом в стену.");
        }

        [Test]
        public void Слипшиеся_якоря_систему_координат_не_задают()
        {
            PhysicalSpaceAnchorFrame frame;
            string diagnosis;

            bool built = PhysicalSpaceAnchorFrame.TryBuild(
                new Vector3(1f, 0f, 1f), new Vector3(1.05f, 3f, 1f), out frame, out diagnosis);

            Assert.IsFalse(built,
                "Два якоря в одной точке направления не задают: система координат развернулась бы случайно.");
            Assert.IsFalse(frame.IsValid);
            Assert.IsNotEmpty(diagnosis, "Отказ обязан объяснять себя, иначе прогон нечем расследовать.");
        }

        [Test]
        public void Высота_якорей_на_направление_не_влияет()
        {
            // Игрок регистрирует точки контроллером на случайной высоте, поэтому
            // вертикаль в направлении не участвует — так же поступает CalculateTransform.
            PhysicalSpaceAnchorFrame flat  = Build(AnchorZero, AnchorOne);
            PhysicalSpaceAnchorFrame tilted = Build(AnchorZero, AnchorOne + new Vector3(0f, 1.4f, 0f));

            Assert.AreEqual(0f, Quaternion.Angle(flat.Rotation, tilted.Rotation), 0.05f,
                "Поднятый на 1,4 м второй якорь не имеет права наклонить систему координат.");
            Assert.AreEqual(flat.Separation, tilted.Separation, 1e-3f,
                "База меряется по горизонтали.");
        }
    }
}
