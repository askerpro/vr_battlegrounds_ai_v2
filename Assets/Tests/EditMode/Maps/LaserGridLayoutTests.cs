using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.Tests.Maps
{
    /// <summary>
    /// Раскладка табло лазерной сетки (T-47): 4 общих табло — по одному на грань, персональный кусок —
    /// напротив каждой стены арсенала (стена заслоняет свою грань); всё внутри граней, мимо стен, без
    /// наложений, лицом в зону. Геометрия — как у зон спавна на картах: коробка 15,5 × 3,05 × 3,72 м,
    /// 4 стены по 2,55 м шириной и 4 м высотой у задней грани (выше самой сетки).
    /// </summary>
    public class LaserGridLayoutTests
    {
        private const float Eps = 1e-3f;

        private static LaserGridBox MapBox(float yaw = 0f) =>
            new LaserGridBox(new Vector3(0f, 0f, 8.06f), yaw, 7.75f, 1.5227f, 0f, 3.72f);

        private static List<LaserGridWall> MapWalls(LaserGridBox box)
        {
            float[] xs = { -5.73f, -2.6f, 0.52f, 3.71f };
            var walls = new List<LaserGridWall>();
            for (int i = 0; i < xs.Length; i++)
                walls.Add(new LaserGridWall(i, box.Center + box.Right * xs[i] + box.Forward * 1.3f, 1.27f, 4.0f));
            return walls;
        }

        [TestCase(0f, TestName = "Зона_карты_4_общих_и_по_куску_на_стену")]
        [TestCase(180f, TestName = "Повёрнутая_зона_4_общих_и_по_куску_на_стену")]
        public void Четыре_общих_и_персональный_на_каждую_стену(float yaw)
        {
            LaserGridBox box = MapBox(yaw);
            List<LaserGridWall> walls = MapWalls(box);
            List<LaserGridScreenPose> poses = LaserGridLayout.Build(box, walls);

            List<LaserGridScreenPose> common = poses.Where(p => p.Kind == LaserGridScreenKind.Common).ToList();
            Assert.AreEqual(4, common.Count, "Общих табло не 4.");
            CollectionAssert.AreEquivalent(LaserGridLayout.Faces, common.Select(p => p.Face), "Общие табло — не по одному на грань.");

            foreach (LaserGridWall wall in walls)
            {
                List<LaserGridScreenPose> mine = poses.Where(p => p.Kind == LaserGridScreenKind.Personal && p.WallId == wall.Id).ToList();
                Assert.AreEqual(1, mine.Count, $"У стены {wall.Id} не один персональный кусок.");
                LaserGridScreenPose pose = mine[0];

                Assert.AreEqual(LaserGridFace.Front, pose.Face, $"Кусок стены {wall.Id} не напротив неё (стена заслоняет заднюю грань).");
                Assert.AreEqual(LaserGridLayout.AlongFace(box, pose.Face, wall.Center), pose.Along, 0.01f,
                    $"Кусок стены {wall.Id} не в её дорожке.");
                Assert.AreEqual(LaserGridLayout.PersonalCenterAboveFloor, pose.CenterY - box.FloorY, Eps,
                    $"Кусок стены {wall.Id} не на уровне глаз.");
            }

            AssertInsideFacesAndApart(box, poses);
            AssertClearOfWalls(box, walls, poses);
        }

        [Test]
        public void Без_стен_общие_по_центру_граней_над_головой()
        {
            LaserGridBox box = MapBox();
            List<LaserGridScreenPose> poses = LaserGridLayout.Build(box, new List<LaserGridWall>());

            Assert.AreEqual(4, poses.Count);
            foreach (LaserGridScreenPose pose in poses)
            {
                Assert.AreEqual(LaserGridScreenKind.Common, pose.Kind);
                Assert.AreEqual(0f, pose.Along, Eps, $"Табло грани {pose.Face} не по центру.");
                Assert.AreEqual(LaserGridLayout.FreeCenterAboveFloor, pose.CenterY, Eps, $"Табло грани {pose.Face} не над головой.");
            }
            AssertInsideFacesAndApart(box, poses);
        }

        [Test]
        public void Общее_табло_грани_за_стенами_встаёт_в_просвет()
        {
            LaserGridBox box = MapBox();
            List<LaserGridWall> walls = MapWalls(box);
            LaserGridScreenPose back = LaserGridLayout.Build(box, walls)
                .Single(p => p.Kind == LaserGridScreenKind.Common && p.Face == LaserGridFace.Back);

            Assert.GreaterOrEqual(back.Width, LaserGridLayout.CommonWidth - Eps, "В просвете у края места хватает — табло сузилось зря.");
            AssertClearOfWalls(box, walls, new[] { back });
        }

        [Test]
        public void Общее_табло_грани_с_кусками_в_ряду_у_середины()
        {
            LaserGridBox box = MapBox();
            List<LaserGridScreenPose> poses = LaserGridLayout.Build(box, MapWalls(box));
            LaserGridScreenPose front = poses.Single(p => p.Kind == LaserGridScreenKind.Common && p.Face == LaserGridFace.Front);

            Assert.Less(Mathf.Abs(front.Along), 1.5f, "Общее табло передней грани ушло от середины, хотя промежуток у середины есть.");
            Assert.AreEqual(LaserGridLayout.PersonalCenterAboveFloor, front.CenterY - box.FloorY, Eps, "Общее табло не в ряду кусков.");
        }

        [Test]
        public void Низкие_тесные_стены_общее_табло_над_ними()
        {
            LaserGridBox box = new LaserGridBox(Vector3.zero, 0f, 3f, 1.5f, 0f, 4.5f);
            var walls = new List<LaserGridWall>();
            float[] xs = { -2.1f, -0.7f, 0.7f, 2.1f };
            for (int i = 0; i < xs.Length; i++) walls.Add(new LaserGridWall(i, new Vector3(xs[i], 0f, 1.2f), 0.7f, 2.2f));

            List<LaserGridScreenPose> poses = LaserGridLayout.Build(box, walls);
            LaserGridScreenPose back = poses.Single(p => p.Kind == LaserGridScreenKind.Common && p.Face == LaserGridFace.Back);

            Assert.GreaterOrEqual(back.Bottom, 2.2f + LaserGridLayout.GapAboveWall - Eps, "Просвета нет — табло обязано встать над стенами.");
            AssertInsideFacesAndApart(box, poses);
            AssertClearOfWalls(box, walls, poses);
        }

        [Test]
        public void Узкая_грань_табло_не_шире_грани()
        {
            LaserGridBox box = new LaserGridBox(Vector3.zero, 0f, 0.6f, 3f, 0f, 3.5f);
            AssertInsideFacesAndApart(box, LaserGridLayout.Build(box, null));
        }

        [Test]
        public void Табло_смотрят_наружу_и_стоят_внутри_зоны()
        {
            LaserGridBox box = MapBox(90f);
            foreach (LaserGridScreenPose pose in LaserGridLayout.Build(box, MapWalls(box)))
            {
                Vector3 outward = LaserGridLayout.Outward(box, pose.Face);
                Assert.Greater(Vector3.Dot(pose.Rotation * Vector3.forward, outward), 0.999f,
                    $"Табло грани {pose.Face}: текст TMP читается изнутри, только если +Z табло смотрит наружу.");

                Vector2 plan = box.ToPlan(pose.Position);
                Assert.LessOrEqual(Mathf.Abs(plan.x), box.HalfWidth - LaserGridLayout.Inset + Eps, $"Табло грани {pose.Face} вне зоны.");
                Assert.LessOrEqual(Mathf.Abs(plan.y), box.HalfDepth - LaserGridLayout.Inset + Eps, $"Табло грани {pose.Face} вне зоны.");
            }
        }

        [Test]
        public void Ближайшая_грань_по_плану()
        {
            LaserGridBox box = MapBox(180f);
            Assert.AreEqual(LaserGridFace.Back, LaserGridLayout.NearestFace(box, box.Center + box.Forward * 1.4f));
            Assert.AreEqual(LaserGridFace.Front, LaserGridLayout.NearestFace(box, box.Center - box.Forward * 1.4f));
            Assert.AreEqual(LaserGridFace.Right, LaserGridLayout.NearestFace(box, box.Center + box.Right * 7.6f));
            Assert.AreEqual(LaserGridFace.Left, LaserGridLayout.NearestFace(box, box.Center - box.Right * 7.6f));
        }

        /// <summary>Каждое табло в пределах своей грани по длине и высоте; табло одной грани не накладываются.</summary>
        public static void AssertInsideFacesAndApart(LaserGridBox box, IReadOnlyList<LaserGridScreenPose> poses)
        {
            foreach (LaserGridScreenPose p in poses)
            {
                float half = LaserGridLayout.HalfLength(box, p.Face);
                Assert.GreaterOrEqual(p.MinAlong, -half - Eps, $"{p.Kind} на грани {p.Face} вылезает за край.");
                Assert.LessOrEqual(p.MaxAlong, half + Eps, $"{p.Kind} на грани {p.Face} вылезает за край.");
                Assert.GreaterOrEqual(p.Bottom, box.FloorY - Eps, $"{p.Kind} на грани {p.Face} ниже пола.");
                Assert.LessOrEqual(p.Top, box.TopY + Eps, $"{p.Kind} на грани {p.Face} выше сетки.");
                Assert.Greater(p.Width, 0.2f, $"{p.Kind} на грани {p.Face} схлопнулось.");
            }

            for (int i = 0; i < poses.Count; i++)
            for (int j = i + 1; j < poses.Count; j++)
            {
                LaserGridScreenPose a = poses[i], b = poses[j];
                if (a.Face != b.Face) continue;
                bool apartAlong = a.MaxAlong <= b.MinAlong + Eps || b.MaxAlong <= a.MinAlong + Eps;
                bool apartUp = a.Top <= b.Bottom + Eps || b.Top <= a.Bottom + Eps;
                Assert.IsTrue(apartAlong || apartUp,
                    $"Табло накладываются на грани {a.Face}: {a.Kind} ({a.Along:F2}, {a.CenterY:F2}) и {b.Kind} ({b.Along:F2}, {b.CenterY:F2}).");
            }
        }

        /// <summary>Ни одно табло не стоит за стеной арсенала (стена заслоняет ближайшую к ней грань), кроме как над ней.</summary>
        public static void AssertClearOfWalls(LaserGridBox box, IReadOnlyList<LaserGridWall> walls, IEnumerable<LaserGridScreenPose> poses)
        {
            foreach (LaserGridScreenPose p in poses)
            foreach (LaserGridWall wall in walls)
            {
                LaserGridFace face = LaserGridLayout.NearestFace(box, wall.Center);
                if (face != p.Face) continue;
                float u = LaserGridLayout.AlongFace(box, face, wall.Center);
                bool apartAlong = p.MaxAlong <= u - wall.HalfWidth + Eps || p.MinAlong >= u + wall.HalfWidth - Eps;
                bool above = p.Bottom >= wall.TopY - Eps;
                Assert.IsTrue(apartAlong || above,
                    $"{p.Kind} на грани {p.Face} ({p.Along:F2}, {p.CenterY:F2}) за стеной {wall.Id} ({u:F2} ± {wall.HalfWidth:F2}, верх {wall.TopY:F2}) — его не видно.");
            }
        }
    }
}
