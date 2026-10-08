using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Tests.Maps
{
    /// <summary>
    /// Блоки <c>LD_Alphabet</c> несут сетку из пака <c>UnityStarter_Robot/Environment</c>, и сетка на них
    /// не растянута: у каждого треугольника плотность UV одинакова вдоль всех рёбер и одна на все блоки.
    ///
    /// <para>
    /// Блоки — стандартные куб и цилиндр с неравномерным масштабом корня (стена 2×2.5×0.15 м). Штатные UV
    /// примитива кладут текстуру 0..1 на каждую грань, и сетка растягивается по-разному на каждой грани.
    /// Тайлинг материала один на все грани и этого не исправит. Поэтому меши блоков строит
    /// <c>Tools/VR Battlegrounds/Gameplay/Texture LD Blocks</c> (<c>LevelDesignBlockTexturer</c>)
    /// с UV в метрах под масштаб префаба.
    /// </para>
    /// </summary>
    public class LevelDesignBlockTextureTests
    {
        private const string BlocksFolder    = "Assets/Prefabs/LevelDesign/LD_Alphabet";
        private const string MaterialsFolder = "Assets/ThirdParty/UnityStarter_Robot/Environment/Materials/";

        /// <summary>Метров на один повтор текстуры; должно совпадать с <c>LevelDesignBlockTexturer.TileMeters</c>.</summary>
        private static float TileMeters
        {
            get
            {
                // Инструмент находится в editor-сборке; плотностью владеет его
                // паспорт сетки, а не историческая копия числа в тесте.
                System.Type owner = System.AppDomain.CurrentDomain.GetAssemblies()
                    .Select(a => a.GetType("VrBattlegrounds.Editor.LevelDesignBlockTexturer"))
                    .FirstOrDefault(t => t != null);
                Assert.IsNotNull(owner, "Нет владельца плотности UV LevelDesignBlockTexturer");
                var property = owner.GetProperty("TileMeters");
                Assert.IsNotNull(property, "Нет контракта TileMeters");
                float meters = (float)property.GetValue(null);
                Assert.Greater(meters, 0f, "Паспорт задаёт недопустимую плотность UV");
                return meters;
            }
        }

        /// <summary>Допуск плотности: хорда фасетки цилиндра короче дуги на ~0.5 %.</summary>
        private const float Tolerance = 0.03f;

        [Test]
        public void Блоки_носят_материалы_окружения_из_пака()
        {
            var failures = new List<string>();
            int checks = 0;

            foreach (GameObject block in LoadBlocks())
            foreach (Renderer r in block.GetComponentsInChildren<Renderer>(true))
            foreach (Material m in r.sharedMaterials)
            {
                checks++;
                string path = m != null ? AssetDatabase.GetAssetPath(m) : "null";
                if (!path.StartsWith(MaterialsFolder))
                    failures.Add($"{block.name}/{r.name}: материал {path}");
            }

            Assert.Greater(checks, 0, "Контроль: блоков не найдено.");
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        [Test]
        public void Сетка_на_блоках_не_растянута()
        {
            var failures = new List<string>();
            float expected = 1f / TileMeters;
            int checks = 0;

            foreach (GameObject block in LoadBlocks())
            foreach (MeshFilter mf in block.GetComponentsInChildren<MeshFilter>(true))
            {
                Mesh mesh = mf.sharedMesh;
                Assert.IsNotNull(mesh, $"{block.name}/{mf.name}: нет меша");
                Vector3 scale = mf.transform.lossyScale;
                Vector3[] v = mesh.vertices;
                Vector2[] uv = mesh.uv;
                if (uv.Length != v.Length)
                {
                    failures.Add($"{block.name}: у меша {mesh.name} нет UV");
                    continue;
                }

                int[] tris = mesh.triangles;
                float worst = 0f;
                for (int t = 0; t < tris.Length; t += 3)
                for (int e = 0; e < 3; e++)
                {
                    int a = tris[t + e], b = tris[t + (e + 1) % 3];
                    float world = Vector3.Scale(v[b] - v[a], scale).magnitude;
                    if (world < 1e-4f) continue;
                    checks++;
                    float density = (uv[b] - uv[a]).magnitude / world;
                    worst = Mathf.Max(worst, Mathf.Abs(density / expected - 1f));
                }

                if (worst > Tolerance)
                    failures.Add($"{block.name}: меш {mesh.name} растянут, отклонение плотности UV до {worst:P0}");
            }

            Assert.Greater(checks, 0, "Контроль: рёбер не найдено.");
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        private static IEnumerable<GameObject> LoadBlocks()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { BlocksFolder }))
                yield return AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
        }
    }
}
