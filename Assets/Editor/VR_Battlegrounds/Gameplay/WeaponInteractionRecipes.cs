using System;
using System.Collections.Generic;
using System.Linq;
using UltimateXR.Core;
using UltimateXR.Manipulation;
using UnityEngine;

namespace VrBattlegrounds.Editor.Gameplay
{
    /// <summary>
    /// Явные семантические объёмы хвата; нейтральные точки хвата (центры ладоней рук пака) служат предложением места на
    /// корпусе. Затравка — ближайшая точка поверхности меша (<see cref="SurfaceSeed" />), не ближайшая вершина: точка
    /// ладони лежит в глубине кисти, в 1–2 см от поверхности, и на редких вершинах плоской грани ближайшая вершина
    /// скачет на сантиметры. Рукоять (<c>Primary</c>), смоделированная отдельной оболочкой внутри меша корпуса, берётся
    /// островом меша целиком (<see cref="MeshIslands" />, пороги <see cref="IslandRejection" />); иначе — обрезка
    /// коробкой фиксированного размера вокруг затравки.
    /// </summary>
    public static class WeaponInteractionRecipes
    {
        /// <summary>Остров берётся целиком, если по каждой оси корня он не больше коробки области × этот множитель.</summary>
        internal const float IslandSizeFactor = 1.35f;

        /// <summary>…и не мельче детали: наибольший размер острова — не меньше этой доли наибольшего размера коробки.</summary>
        internal const float IslandMinFactor = 0.6f;

        /// <summary>…и центр его bounds не дальше этого расстояния от затравки, м.</summary>
        internal const float IslandCenterReach = 0.06f;

        internal static readonly Vector3 PrimarySize = new(0.075f, 0.115f, 0.065f);
        internal static readonly Vector3 SupportSize = new(0.085f, 0.075f, 0.13f);
        internal static readonly Vector3 PistolSupportSize = new(0.075f, 0.085f, 0.065f);
        internal static readonly Vector3 PistolSupportOffset = new(0f, -0.015f, 0.02f);

        /// <param name="palms">
        /// Нейтральные точки хвата в осях корня по номеру точки — центры ладоней рук пака
        /// (<see cref="HandsPackWeaponBuilder.PalmPoints" />). Без них — snap-точки <c>Orig</c> (<see cref="PackOrigPoses" />).
        /// Записи хвата аватаров области не задают: без обеих точек — исключение.
        /// </param>
        public static WeaponInteractionRecipe For(GameObject root, HandsPackWeaponRecipe source = null, Vector3[] palms = null)
        {
            var grab = root.GetComponent<UxrGrabbableObject>();
            Transform body = root.GetComponentsInChildren<MeshFilter>(true)
                .Where(f => f.sharedMesh != null && !f.name.StartsWith("GrabHighlight") &&
                            f.GetComponentInParent<UxrGrabbableObject>(true) == grab)
                .OrderByDescending(f => f.sharedMesh.vertexCount).First().transform;
            bool pistol = grab.Tag == "Gun";
            var geometry = new Dictionary<Transform, PartGeometry>();
            WeaponVisualRegion Region(string name, Transform part, Vector3 point, Vector3 size, bool wholeIsland = false)
            {
                if (!geometry.TryGetValue(part, out PartGeometry g)) geometry[part] = g = PartGeometry.Of(root.transform, part);
                return SurfaceRegion(g, name, part, point, size, wholeIsland);
            }
            Vector3 Grip(int index)
            {
                // Только нейтральные точки: области не зависят от того, какие аватары зарегистрированы.
                if (palms != null && index < palms.Length) return palms[index];
                Transform orig = PackOrigPoses.FindSnap(root.transform, index == 0 ? "Grip" : "Support", index == 0 ? UxrHandSide.Right : UxrHandSide.Left);
                if (orig != null) return root.transform.InverseTransformPoint(orig.position);
                throw new InvalidOperationException(
                    $"{root.name}: нет нейтральной точки хвата {index} — ни центров ладоней рук пака (HandsPackWeaponBuilder.PalmPoints), " +
                    "ни snap-точки Orig (PackOrigPoses). Записи хвата аватаров для областей подсветки не используются.");
            }
            var recipe = new WeaponInteractionRecipe
            {
                Name = root.name.Replace("(Clone)", ""),
                AssetFolder = $"Assets/Art/Weapons/Interaction/{root.name.Replace("(Clone)", "")}",
                Primary = Region("Primary", body, Grip(0), PrimarySize, wholeIsland: true)
            };
            if (grab.GrabPointCount > 1)
                recipe.Support = pistol
                    // Вторая рука пистолета обнимает первую: прежнее смещение от коробки рукояти, не от острова.
                    ? new WeaponVisualRegion
                    {
                        Name = "Support", Source = body, Seed = recipe.Primary.Seed, SeedIsland = recipe.Primary.SeedIsland,
                        Bounds = new Bounds(recipe.Primary.Bounds.center + PistolSupportOffset, PistolSupportSize)
                    }
                    : Region("Support", body, Grip(1), SupportSize);
            UxrGrabbableObject action = root.GetComponentsInChildren<UxrGrabbableObject>(true)
                .FirstOrDefault(g => g != grab && g.GetComponentsInParent<UxrGrabbableObjectAnchor>(true).Length == 0);
            if (action != null)
            {
                string partName = source?.ActionGripPart ?? source?.ActionPart;
                Transform part = action.GetComponentsInChildren<MeshFilter>(true)
                    .Where(f => f.sharedMesh != null && !f.name.StartsWith("GrabHighlight"))
                    .First(f => partName == null || f.name == partName.Replace('.', '_') ||
                                NormalizePartName(f.name) == NormalizePartName(partName)).transform;
                recipe.ActionGripPart = part;
                recipe.ActionContact = source?.ActionGripContact;
                Vector3 point = root.transform.InverseTransformPoint(part.TransformPoint(
                    recipe.ActionContact ?? part.GetComponent<MeshFilter>().sharedMesh.bounds.center));
                recipe.Action = Region("Action", part, point,
                    action.name == "Pump" ? new Vector3(0.085f, 0.08f, 0.15f) : new Vector3(0.06f, 0.055f, 0.06f));
            }
            var anchor = root.GetComponentInChildren<UxrGrabbableObjectAnchor>(true);
            if (anchor != null)
                recipe.Insertion = Region("Insertion", body, root.transform.InverseTransformPoint(
                    (anchor.DropProximityTransform ?? anchor.transform).position), new Vector3(0.085f, 0.055f, 0.085f));
            return recipe;
        }

        /// <summary>
        /// Область вокруг нейтральной точки <paramref name="point" /> (оси корня): затравка — ближайшая точка поверхности
        /// <paramref name="g" />; коробка <paramref name="size" /> с центром в затравке на оси симметрии меша. При
        /// <paramref name="wholeIsland" /> остров затравочного треугольника, прошедший <see cref="IslandRejection" />,
        /// заменяет обрезку коробкой.
        /// </summary>
        internal static WeaponVisualRegion SurfaceRegion(PartGeometry g, string name, Transform part, Vector3 point, Vector3 size, bool wholeIsland)
        {
            Vector3 seed = SurfaceSeed(g, point, out int triangle);
            // Ось симметрии оружия: объём охватывает обе стороны рукояти, а не только правую щёчку.
            var region = new WeaponVisualRegion
            {
                Name = name, Source = part, Seed = seed,
                Bounds = new Bounds(new Vector3(g.SymmetryX, seed.y, seed.z), size),
                SeedIsland = MeshIslands.Of(g.Vertices, g.Triangles, g.Labels, triangle)
            };
            if (!wholeIsland) return region;
            region.IslandRejection = IslandRejection(region.SeedIsland, seed, size);
            if (region.IslandRejection == null) region.Triangles = region.SeedIsland.Triangles;
            return region;
        }

        /// <summary>
        /// Почему остров нельзя взять целиком как деталь размера коробки <paramref name="size" />; null — можно.
        /// Остров должен по каждой оси корня помещаться в <see cref="IslandSizeFactor" />×коробки (рукоять, а не рамка,
        /// ложа или корпус), быть не мельче <see cref="IslandMinFactor" /> её наибольшего размера (не винт или накладка
        /// рукояти) и лежать у затравки (<see cref="IslandCenterReach" />).
        /// </summary>
        internal static string IslandRejection(MeshIslands.Island island, Vector3 seed, Vector3 size)
        {
            for (int axis = 0; axis < 3; axis++)
                if (island.Bounds.size[axis] > size[axis] * IslandSizeFactor)
                    return $"остров больше коробки ×{IslandSizeFactor:0.##} по {"xyz"[axis]}: " +
                           $"{island.Bounds.size[axis] * 1000f:F0} > {size[axis] * IslandSizeFactor * 1000f:F0} мм";
            float longest = Mathf.Max(island.Bounds.size.x, Mathf.Max(island.Bounds.size.y, island.Bounds.size.z));
            float boxLongest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            if (longest < boxLongest * IslandMinFactor)
                return $"остров мельче детали: {longest * 1000f:F0} < {boxLongest * IslandMinFactor * 1000f:F0} мм";
            float reach = Vector3.Distance(island.Bounds.center, seed);
            return reach > IslandCenterReach
                ? $"центр острова в {reach * 1000f:F0} мм от затравки (> {IslandCenterReach * 1000f:F0} мм)"
                : null;
        }

        /// <summary>Ближайшая к <paramref name="point" /> точка поверхности (оси корня) и её треугольник.</summary>
        internal static Vector3 SurfaceSeed(PartGeometry g, Vector3 point, out int triangle)
        {
            triangle = -1;
            Vector3 best = point;
            float bestDistance = float.PositiveInfinity;
            for (int t = 0; t * 3 + 2 < g.Triangles.Length; t++)
            {
                Vector3 q = ClosestPointOnTriangle(point, g.Vertices[g.Triangles[t * 3]], g.Vertices[g.Triangles[t * 3 + 1]],
                    g.Vertices[g.Triangles[t * 3 + 2]]);
                float distance = (q - point).sqrMagnitude;
                if (distance >= bestDistance) continue;
                bestDistance = distance; best = q; triangle = t;
            }
            if (triangle < 0) throw new InvalidOperationException("Нет треугольников для затравки области подсветки.");
            return best;
        }

        /// <summary>Ближайшая точка треугольника abc (Ericson, Real-Time Collision Detection, 5.1.5).</summary>
        internal static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) return b + (c - b) * ((d4 - d3) / (d4 - d3 + (d5 - d6)));
            float denominator = va + vb + vc;
            // Вырожденный треугольник: все барицентрики нулевые — ближайшей считается вершина a.
            if (Mathf.Abs(denominator) < 1e-20f) return a;
            float v = vb / denominator, w = vc / denominator;
            return a + ab * v + ac * w;
        }

        private static string NormalizePartName(string name) => name.Replace("Shogun_", "").Replace("_mesh", "")
            .Replace("_Mesh", "").Replace("-", "").Replace(" ", "").Replace('.', '_');
    }

    /// <summary>Меш детали в осях корня оружия: вершины, треугольники <c>sharedMesh.triangles</c> и их острова.</summary>
    internal sealed class PartGeometry
    {
        public Vector3[] Vertices;
        public int[] Triangles;

        /// <summary>x центра bounds меша в осях корня — ось симметрии оружия.</summary>
        public float SymmetryX;

        private int[] _labels;

        /// <summary>Остров каждого треугольника (<see cref="MeshIslands.Labels" />), считается один раз.</summary>
        public int[] Labels => _labels ??= MeshIslands.Labels(Vertices, Triangles);

        public static PartGeometry Of(Transform root, Transform part)
        {
            Mesh mesh = part.GetComponent<MeshFilter>().sharedMesh;
            Matrix4x4 toRoot = root.worldToLocalMatrix * part.localToWorldMatrix;
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i] = toRoot.MultiplyPoint3x4(vertices[i]);
            return new PartGeometry { Vertices = vertices, Triangles = mesh.triangles, SymmetryX = toRoot.MultiplyPoint3x4(mesh.bounds.center).x };
        }
    }
}
