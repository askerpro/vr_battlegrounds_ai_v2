using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Распределённое внешнее окружение; вызывается единственным авторингом Lobby.</summary>
    public static class LobbyExteriorDecorationBuilder
    {
        private static int nearTreeOrdinal;

        public static void Build(Transform parent, Bounds arena)
        {
            if (!parent) throw new ArgumentNullException(nameof(parent));
            nearTreeOrdinal = 0;
            // Группа использует систему координат общего авторинга, без второго основания/света.
            var exterior = Group("ExteriorDecoration", parent, Vector3.zero, 0f);
            var palette = new Palette();
            float y = arena.max.y;
            // Технический реквизит сосредоточен у машины; оружейные припасы находятся внутри шатра.
            Service(exterior, "VehicleMaintenance", new Vector3(14f, y, -27f), 0f, palette, true);
            // Единственная вышка теперь принадлежит угловому КПП BaseBackdrop.
            FuelDepot(exterior, new Vector3(12f, y, -30.5f), 0f, palette);
            SupplyTruck(exterior, new Vector3(14f, y, -22f), 90f);
            // Светильники сервисных зон создаёт единый авторинг освещения из реальных источников.

            // NearEdgeDetails снят: случайные мешки и пороговые камни не относятся к рабочим зонам.

            ForestIsland(exterior, "EastNearForest", new Vector3(27f, y, 2f), 7, 31, palette);
            ForestIsland(exterior, "WestNearForest", new Vector3(-27f, y, -2f), 7, 61, palette);
            ForestIsland(exterior, "NorthEastForest", new Vector3(33f, y, 33f), 6, 91, palette);
            ForestIsland(exterior, "NorthWestForest", new Vector3(-30f, y, 36f), 6, 121, palette);
            ForestIsland(exterior, "SouthEastForest", new Vector3(31f, y, -37f), 6, 151, palette);
            ForestIsland(exterior, "SouthWestForest", new Vector3(-34f, y, -33f), 6, 181, palette);
            var distant = Group("ConnectedIrregularForestBands", exterior, Vector3.zero, 0f);
            EndForestBand(distant, "NorthBehindTargets", y, 1, 24, 311);
            EndForestBand(distant, "SouthBehindCamp", y, -1, 22, 419);
            SideForestBand(distant, "EastForestLayer", y, 1, 10, 521);
            SideForestBand(distant, "WestForestLayer", y, -1, 10, 631);
            var horizon = Group("LowLayeredHorizon", exterior, Vector3.zero, 0f);
            HorizonBelt(horizon, y);
            ClearFunctionalApproaches(exterior, arena);
        }

        // Сохраняем число деревьев, освобождая утверждённые зоны и подходы; камни не занимают рабочие места.
        public static void ClearFunctionalApproaches(Transform exterior, Bounds arena)
        {
            var plan = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText("tasks/lobby-decoration/implementation/SitePlan-2026-10-10.json"));
            var zones = plan["zones"].Select(z => new Bounds(
                new Vector3(arena.center.x + ((float)z["x"][0] + (float)z["x"][1]) * .5f, 0,
                    arena.center.z + ((float)z["z"][0] + (float)z["z"][1]) * .5f),
                new Vector3((float)z["x"][1] - (float)z["x"][0] + 4, 100,
                    (float)z["z"][1] - (float)z["z"][0] + 4))).ToArray();
            foreach (var t in exterior.GetComponentsInChildren<Transform>(true))
            {
                bool tree = t.name == "SilverFir" || t.name == "SilverFir_Factory06";
                bool stone = t.name == "ForestBoulder";
                if (!tree && !stone) continue;
                // Только ранее измеренные собственные ForestBoulder; ServiceStone сохраняется.
                if (stone) { UnityEngine.Object.DestroyImmediate(t.gameObject); continue; }
                var renderers = t.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) continue;
                var b = renderers[0].bounds;
                foreach (var r in renderers) b.Encapsulate(r.bounds);
                if (!zones.Any(z => z.Intersects(b))) continue;
                if (stone) UnityEngine.Object.DestroyImmediate(t.gameObject);
                else t.position += Vector3.right * (b.center.x >= arena.center.x ? arena.center.x + 40 - b.min.x : arena.center.x - 40 - b.max.x);
            }
        }

        private const string Imported = "Assets/Art/Lobby/Imported/";
        private const string CratePath = Imported + "Catalog/HauntedFarmHouse/URP/Art/Prefabs/SM_Crate_A_01.prefab";
        private const string GeneratorPath = Imported + "Catalog/RPG_FPS_game_assets_industrial/Other_props/Generators/Generator_v1/Generator_v1.prefab";
        private const string FirPath = Imported + "Catalog/RuralTown/URP/Art/Scanned_Foliage/Foliage/SilverFir/Meshes/SM_SilverFir_M_01.fbx";
        private const string FirMaterials = Imported + "Catalog/RuralTown/URP/Art/Scanned_Foliage/Foliage/SilverFir/Materials/";

        private sealed class Palette
        {
            public readonly Material Steel = MaterialAsset("CampSteel", new Color(.24f, .29f, .26f));
            public readonly Material Timber = MaterialAsset("WeatheredTimber", new Color(.35f, .29f, .20f));
            public readonly Material Sand = MaterialAsset("SandbagCanvas", new Color(.47f, .43f, .29f));
            public readonly Material Rock = MaterialAsset("ForestStone", new Color(.33f, .35f, .32f));
            public readonly Material Dark = MaterialAsset("DarkRubber", new Color(.10f, .12f, .11f));
            public readonly Material Brass = MaterialAsset("UtilityBrass", new Color(.42f, .34f, .18f));
            public readonly Mesh StoneMesh = StoneMeshAsset();
            public readonly Mesh PatchMesh = PatchMeshAsset();
            public readonly Material Soil = SoilMaterialAsset();
        }

        private static Transform Group(string name, Transform parent, Vector3 position, float yaw)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            go.transform.localScale = Vector3.one;
            return go.transform;
        }

        private static void Service(Transform parent, string name, Vector3 foot, float yaw, Palette p, bool generator)
        {
            var group = Group(name, parent, foot, yaw);
            GroundPatch(group, new Vector3(-.5f, .012f, 0), new Vector3(6.8f, 1, 6), p);
            if (generator) Model(GeneratorPath, "Generator", group, new Vector3(1.7f, 0, .5f), 1.15f, 12f);
            Table(group, new Vector3(-1.25f, 0, .2f), p);
            Barrel(group, new Vector3(1.7f, 0, -1.6f), p);
            Barrel(group, new Vector3(2.4f, 0, -1.25f), p);
            Sandbags(group, new Vector3(1.15f, 0, 2.55f), -8f, p, 5, 2);
            Rock(group, "ServiceStone", new Vector3(-2.6f, 0, 1.7f), new Vector3(1.4f, .45f, 1f), 24f, p);
        }

        private static void Store(Transform parent, string name, Vector3 foot, float yaw, Palette p)
        {
            var group = Group(name, parent, foot, yaw);
            int variant = name.StartsWith("North", StringComparison.Ordinal) ? 0 : name.StartsWith("South", StringComparison.Ordinal) ? 1 : 2;
            GroundPatch(group, new Vector3(0, .009f, .4f), new Vector3(7.5f, 1, 6.5f), p);
            Model(CratePath, "LowerCrateA", group, new Vector3(-.75f, 0, 0), 1f, variant * 9f);
            Model(CratePath, "LowerCrateB", group, new Vector3(.75f, 0, .2f + variant * .45f), .8f, 16f - variant * 17f);
            Model(CratePath, "UpperCrate", group, variant == 1 ? new Vector3(-1.6f, 0, -1.3f) : new Vector3(-.75f, 1.02f, 0), .63f, -8f - variant * 11f);
            Model(CratePath, "SeparatedCrate", group, new Vector3(2.2f, 0, -1.4f), .65f, 32f);
            Barrel(group, new Vector3(-2.25f, 0, 1.1f), p);
            Sandbags(group, new Vector3(.4f - variant * .2f, 0, 2.5f), 8f + variant * 13, p, 6 - variant, 2);
            Rock(group, "StoreStone", new Vector3(2.65f, 0, 1.75f), new Vector3(1.5f, .55f, 1.1f), 51f, p);
            Shape("TimberPileA", group, PrimitiveType.Cube, new Vector3(-2.4f, .11f, -1.25f), new Vector3(.20f, .18f, 2.1f), p.Timber);
            Shape("TimberPileB", group, PrimitiveType.Cube, new Vector3(-2.13f, .11f, -1.3f), new Vector3(.20f, .18f, 1.8f), p.Timber);
            WeaponCase(group, new Vector3(1.45f, .04f, .65f), -14f, p);
            WeaponCase(group, new Vector3(1.40f, .30f, .65f), -9f, p);
        }

        private static void Table(Transform parent, Vector3 foot, Palette p)
        {
            var group = Group("FieldWorkTable", parent, foot, 0);
            Shape("WoodTop", group, PrimitiveType.Cube, new Vector3(0, .91f, 0), new Vector3(1.65f, .09f, .8f), p.Timber);
            foreach (float x in new[] { -.68f, .68f })
                foreach (float z in new[] { -.28f, .28f })
                    Shape("SteelLeg", group, PrimitiveType.Cube, new Vector3(x, .43f, z), new Vector3(.055f, .86f, .055f), p.Steel);
            Shape("ToolCase", group, PrimitiveType.Cube, new Vector3(-.4f, 1.04f, .04f), new Vector3(.47f, .18f, .30f), p.Steel);
            Shape("CaseHandle", group, PrimitiveType.Cube, new Vector3(-.4f, 1.15f, .04f), new Vector3(.16f, .035f, .035f), p.Dark);
            Shape("FoldedCanvas", group, PrimitiveType.Cube, new Vector3(.40f, .98f, -.04f), new Vector3(.48f, .055f, .37f), p.Sand);
        }

        private static void Watchtower(Transform parent, Vector3 foot, float yaw)
        {
            PaidModel("SM_WatchTower_01", "PaidWatchtower", parent, foot, yaw, 8f);
        }

        private static void FuelDepot(Transform parent, Vector3 foot, float yaw, Palette p)
        {
            var group = Group("FuelDepot", parent, foot, yaw);
            // GUID позволяет использовать уже существующий asset без дублирования/перезаписи.
            Model(AssetDatabase.GUIDToAssetPath("b2be9b4ffc291194c96942bd60bdc25c"), "ImportedFuelTank", group, Vector3.zero, 2.2f, 0);
            for (int i = 0; i < 4; i++) Barrel(group, new Vector3(1.2f + i * .75f, 0, -2.1f + (i % 2) * .16f), p);
            Sandbags(group, new Vector3(-.2f, 0, 2.25f), 0, p, 6, 2);
        }

        private static void SupplyTruck(Transform parent, Vector3 foot, float yaw)
        {
            // Купленная машина сохраняет реальный единичный масштаб и все пять source LOD.
            PaidModel("SM_ArmoredVehicle", "PaidArmoredVehicle", parent, foot, yaw);
        }

        private static void Fence(Transform parent, Vector3 foot, float yaw, int bays, bool longBay = false)
        {
            var group = Group("SeparatedMetalFence", parent, foot, yaw);
            float width = longBay ? 4f : 2f;
            for (int i = 0; i < bays; i++)
                PaidModel(longBay ? "SM_Fence_long_4m" : "SM_Fence_long_2m", "PaidFenceSegment", group,
                    new Vector3(i * width, 0, 0), 0);
        }

        private static void LampPost(Transform parent, Vector3 foot, float yaw, Palette p)
        {
            var group = Group("UnlitCampLampPost", parent, foot, yaw);
            Shape("Pole", group, PrimitiveType.Cylinder, new Vector3(0, 2.25f, 0), new Vector3(.12f, 2.25f, .12f), p.Steel);
            Shape("LampArm", group, PrimitiveType.Cube, new Vector3(.4f, 4.4f, 0), new Vector3(.9f, .07f, .07f), p.Steel);
            Shape("ClosedLampHousing", group, PrimitiveType.Cube, new Vector3(.78f, 4.35f, 0), new Vector3(.40f, .18f, .23f), p.Dark);
            Shape("LampGlass", group, PrimitiveType.Cube, new Vector3(.78f, 4.251f, 0), new Vector3(.33f, .016f, .18f), p.Sand);
            Group("ExteriorLightSocket", group, new Vector3(.78f, 4.20f, 0), 0);
        }

        private static void WeaponCase(Transform parent, Vector3 foot, float yaw, Palette p)
        {
            var group = Group("ClosedMilitaryWeaponCase", parent, foot, yaw);
            Shape("HardCaseBody", group, PrimitiveType.Cube, new Vector3(0, .12f, 0), new Vector3(1.3f, .24f, .45f), p.Steel);
            Shape("LidSeal", group, PrimitiveType.Cube, new Vector3(0, .195f, 0), new Vector3(1.32f, .025f, .46f), p.Dark);
            foreach (float x in new[] { -.48f, -.24f, .24f, .48f })
                Shape("LidRib", group, PrimitiveType.Cube, new Vector3(x, .249f, 0), new Vector3(.028f, .024f, .38f), p.Dark);
            foreach (float x in new[] { -.4f, .4f })
                Shape("CaseLatch", group, PrimitiveType.Cube, new Vector3(x, .20f, -.235f), new Vector3(.065f, .08f, .03f), p.Brass);
            Shape("CarryHandle", group, PrimitiveType.Cube, new Vector3(0, .14f, -.255f), new Vector3(.21f, .03f, .05f), p.Dark);
        }

        private static void GroundPatch(Transform parent, Vector3 center, Vector3 size, Palette p)
        {
            var patch = Group("WornSoilPatch", parent, center, 0).gameObject;
            patch.transform.localScale = size;
            patch.AddComponent<MeshFilter>().sharedMesh = p.PatchMesh;
            patch.AddComponent<MeshRenderer>().sharedMaterial = p.Soil;
            Sanitize(patch);
        }

        private static Material SoilMaterialAsset()
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath("abc00000000002094398110741054851"));
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GUIDToAssetPath("abc00000000010966334330632806938"));
            if (!texture || !normal) throw new InvalidOperationException("Не найдены выбранные Sand1 A/N textures внешнего лагеря.");
            var material = MaterialAsset("WornSoil", Color.white);
            // Меняется только собственный материал Exterior; shared Imported не затрагивается.
            material.SetTexture("_BaseMap", texture);
            material.SetTextureScale("_BaseMap", new Vector2(3, 3));
            material.SetTexture("_BumpMap", normal);
            material.SetTextureScale("_BumpMap", new Vector2(3, 3));
            material.SetFloat("_BumpScale", .35f);
            material.EnableKeyword("_NORMALMAP");
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Mesh PatchMeshAsset()
        {
            const string path = "Assets/Art/Lobby/Exterior/WornSoilPatch.asset";
            EnsureFolder("Assets/Art/Lobby/Exterior");
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing) return existing;
            var vertices = new Vector3[13];
            var uv = new Vector2[13];
            var triangles = new int[36];
            uv[0] = new Vector2(.5f, .5f);
            for (int i = 0; i < 12; i++)
            {
                float angle = i * Mathf.PI * 2 / 12;
                float radius = .48f + .035f * Mathf.Sin(i * 2.1f);
                vertices[i + 1] = new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius);
                uv[i + 1] = new Vector2(vertices[i + 1].x + .5f, vertices[i + 1].z + .5f);
                triangles[i * 3] = 0;
                triangles[i * 3 + 1] = (i + 1) % 12 + 1;
                triangles[i * 3 + 2] = i + 1;
            }
            var mesh = new Mesh { name = "WornSoilPatch", vertices = vertices, uv = uv, triangles = triangles };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static void Barrel(Transform parent, Vector3 foot, Palette p)
        {
            var group = Group("UtilityBarrel", parent, foot, 0);
            Shape("PaintedBody", group, PrimitiveType.Cylinder, new Vector3(0, .47f, 0), new Vector3(.58f, .47f, .58f), p.Steel);
            foreach (float y in new[] { .10f, .78f })
                Shape("ReinforcingBand", group, PrimitiveType.Cylinder, new Vector3(0, y, 0), new Vector3(.605f, .025f, .605f), p.Dark);
            Shape("ClosedCap", group, PrimitiveType.Cylinder, new Vector3(.14f, .946f, 0), new Vector3(.055f, .007f, .055f), p.Brass);
        }

        private static void Sandbags(Transform parent, Vector3 foot, float yaw, Palette p, int count, int rows)
        {
            var group = Group("ShortSandbagStack", parent, foot, yaw);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Art/Lobby/Generated/SingleSandbag.obj");
            if (!source) throw new InvalidOperationException("Не найден SingleSandbag.obj для внешнего лагеря.");
            for (int row = 0; row < rows; row++)
                for (int i = 0; i < count - row; i++)
                {
                    var bag = UnityEngine.Object.Instantiate(source, group);
                    bag.name = "CanvasSandbag";
                    Sanitize(bag);
                    bag.transform.localPosition = new Vector3((i - (count - row - 1) * .5f) * .44f, row * .18f, 0);
                    bag.transform.localRotation = Quaternion.Euler(0, (i % 2 == 0 ? 3 : -3), 0);
                    bag.transform.localScale = Vector3.one;
                    foreach (var r in bag.GetComponentsInChildren<Renderer>(true)) r.sharedMaterial = p.Sand;
                    GroundAt(bag, bag.transform.localPosition, group);
                }
        }

        private static void ForestIsland(Transform parent, string name, Vector3 center, int count, int seed, Palette p)
        {
            var group = Group(name, parent, center, 0);
            var random = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                float angle = i * Mathf.PI * 2f / count;
                float radius = Range(random, 2f, 6.4f);
                Tree(group, new Vector3(Mathf.Cos(angle) * radius, 0, Mathf.Sin(angle) * radius), Range(random, 8f, 12.5f), Range(random, 0, 360));
            }
            for (int i = 0; i < 3; i++)
                Rock(group, "ForestBoulder", new Vector3(Range(random, -5f, 5f), 0, Range(random, -4f, 4f)),
                    new Vector3(Range(random, 1.2f, 2.6f), Range(random, .5f, 1.2f), Range(random, 1.3f, 2.3f)), Range(random, 0, 360), p);
        }

        private static void EndForestBand(Transform parent, string name, float ground, int direction, int count, int seed)
        {
            var group = Group(name, parent, Vector3.zero, 0);
            var random = new System.Random(seed);
            int rowCount = count / 2;
            float halfWidth = direction > 0 ? 42f : 44f;
            for (int row = 0; row < 2; row++)
                for (int i = 0; i < rowCount; i++)
                {
                    float t = i / (float)(rowCount - 1);
                    float spacing = halfWidth * 2 / (rowCount - 1);
                    float x = Mathf.Lerp(-halfWidth, halfWidth, t) + row * spacing * .46f +
                        1.1f * Mathf.Sin(i * 2.3f + row * .7f) + Range(random, -.65f, .65f);
                    float z = direction * ((row == 0 ? 52f : 64.5f) +
                        4.1f * Mathf.Sin(t * Mathf.PI * 2.3f + row * 1.4f + .6f) + Range(random, -1.2f, 1.2f));
                    // Самый ближний +Z ствол остаётся за z46: все пять мишеней заканчиваются до z38.394.
                    if (direction > 0) z = Mathf.Clamp(z, 46.2f, 73f);
                    Tree(group, new Vector3(x, ground, z), Range(random, 10f, 14f), Range(random, 0, 360));
                }
        }

        private static void SideForestBand(Transform parent, string name, float ground, int side, int count, int seed)
        {
            var group = Group(name, parent, Vector3.zero, 0);
            var random = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                float t = i / (float)(count - 1);
                float z = Mathf.Lerp(-51f, 53f, t) + 1.7f * Mathf.Sin(i * 1.9f + side) + Range(random, -1.2f, 1.2f);
                float x = side * (44f + (i % 2) * 5.3f + 2.2f * Mathf.Sin(t * Mathf.PI * 2 + .3f * side) + Range(random, -1f, 1f));
                Tree(group, new Vector3(x, ground, z), Range(random, 10f, 14f), Range(random, 0, 360));
            }
        }

        private static void HorizonBelt(Transform parent, float ground)
        {
            var go = Group("ClosedIrregularTerrainBelt", parent, new Vector3(0, ground, 0), 0).gameObject;
            go.AddComponent<MeshFilter>().sharedMesh = HorizonBeltMeshAsset();
            go.AddComponent<MeshRenderer>().sharedMaterial = MaterialAsset("HorizonBlueGrey", new Color(.39f, .46f, .49f));
            Sanitize(go);
            go.GetComponent<MeshRenderer>().receiveShadows = false;
        }

        private static Mesh HorizonBeltMeshAsset()
        {
            const string path = "Assets/Art/Lobby/Exterior/ClosedIrregularHorizonBelt.asset";
            EnsureFolder("Assets/Art/Lobby/Exterior");
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing) return existing;
            const int segments = 96;
            float[] radii = { 200, 220, 240, 260, 280 };
            var vertices = new Vector3[segments * radii.Length];
            var triangles = new List<int>();
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2 / segments;
                float crest = Mathf.Clamp(9.2f + 1.4f * Mathf.Sin(angle * 2 + .3f) + .55f * Mathf.Sin(angle * 5 + .9f) +
                    1.8f * BroadHill(angle, 35, .5f) + 1.2f * BroadHill(angle, 225, .55f) - .8f * BroadHill(angle, 125, .65f), 6, 13);
                float[] heights = { -10, crest * .45f, crest, crest * .30f, -10 };
                for (int ring = 0; ring < radii.Length; ring++)
                    vertices[ring * segments + i] = new Vector3(Mathf.Cos(angle) * radii[ring], heights[ring], Mathf.Sin(angle) * radii[ring]);
            }
            for (int strip = 0; strip < radii.Length - 1; strip++)
                for (int i = 0; i < segments; i++)
                {
                    int next = (i + 1) % segments;
                    int a = strip * segments + i, b = strip * segments + next;
                    int c = a + segments, d = b + segments;
                    // Общие вершины замыкают шов; промежуточные кольца формируют пологие склоны без торцов.
                    triangles.AddRange(new[] { a, b, c, b, d, c });
                }
            var mesh = new Mesh { name = "ClosedIrregularHorizonBelt", vertices = vertices, triangles = triangles.ToArray() };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static float BroadHill(float angle, float centerDegrees, float width)
        {
            float delta = Mathf.DeltaAngle(angle * Mathf.Rad2Deg, centerDegrees) * Mathf.Deg2Rad;
            return Mathf.Exp(-delta * delta / (2 * width * width));
        }

        private static GameObject PaidModel(string stem, string name, Transform parent, Vector3 foot, float yaw, float? height = null)
        {
            var source = LobbyMilitaryCampSources.Prefab(stem);
            if (!source) throw new InvalidOperationException("Не найден paid URP prefab: " + stem);
            var go = UnityEngine.Object.Instantiate(source, parent);
            go.name = name;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            go.transform.localScale = Vector3.one;
            Sanitize(go);
            if (height.HasValue)
            {
                float actualHeight = RenderBounds(go).size.y;
                if (actualHeight < .001f) throw new InvalidOperationException("Пустой paid prefab: " + stem);
                go.transform.localScale *= height.Value / actualHeight;
            }
            GroundAt(go, foot, parent);
            // Source LODGroup и его полный массив renderer/threshold сохраняются, включая LOD4.
            foreach (var lod in go.GetComponentsInChildren<LODGroup>(true)) lod.RecalculateBounds();
            return go;
        }

        private static void Tree(Transform parent, Vector3 foot, float height, float yaw)
        {
            int ordinal = nearTreeOrdinal;
            if (nearTreeOrdinal < 38) nearTreeOrdinal++;
            if (ordinal == 3 || ordinal == 13 || ordinal == 22 || ordinal == 31)
            {
                // Четыре согласованные ближние замены: source без LOD и с собственными зелёными материалами.
                Model("Assets/Art/Lobby/Imported/Catalog/AbandonedFactory/Art/Prefabs/SM_SilverFir_06.prefab",
                    "SilverFir_Factory06", parent, foot, Mathf.Clamp(height * .55f, 4.8f, 6.8f), yaw);
                return;
            }
            var tree = Model(FirPath, "SilverFir", parent, foot, height, yaw);
            var bark = AssetDatabase.LoadAssetAtPath<Material>(FirMaterials + "M_SilverFir_Bark_01.mat");
            var atlas = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Lobby/Exterior/RuralFirNeedlesGreen.mat");
            if (!bark || !atlas) throw new InvalidOperationException("Не найдены материалы SilverFir.");
            foreach (var renderer in tree.GetComponentsInChildren<Renderer>(true))
            {
                var filter = renderer.GetComponent<MeshFilter>();
                if (!filter || !filter.sharedMesh || filter.sharedMesh.subMeshCount != 2)
                    throw new InvalidOperationException("SilverFir ожидает два submesh (Bark, Atlas): " + renderer.name);
                // Порядок подтверждён source FBX для всех четырёх LOD; null source slots не подменяют кору хвоей.
                renderer.sharedMaterials = new[] { bark, atlas };
            }
            ConfigureLods(tree, true);
        }

        private static GameObject Model(string path, string name, Transform parent, Vector3 foot, float height, float yaw)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!source) throw new InvalidOperationException("Не найден внешний реквизит: " + path);
            var go = UnityEngine.Object.Instantiate(source, parent);
            go.name = name;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.Euler(0, yaw, 0);
            go.transform.localScale = Vector3.one;
            Sanitize(go);
            var bounds = RenderBounds(go);
            if (bounds.size.y < .001f) throw new InvalidOperationException("Пустая геометрия: " + path);
            go.transform.localScale *= height / bounds.size.y;
            GroundAt(go, foot, parent);
            ConfigureLods(go, false);
            return go;
        }

        private static void GroundAt(GameObject go, Vector3 foot, Transform parent)
        {
            Bounds b = RenderBounds(go);
            Vector3 offset = parent.InverseTransformVector(new Vector3(-b.center.x, -b.min.y, -b.center.z));
            Vector3 origin = parent.InverseTransformPoint(Vector3.zero);
            go.transform.localPosition += foot + offset - origin;
        }

        private static Bounds RenderBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("Реквизит не содержит Renderer: " + go.name);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        private static void ConfigureLods(GameObject go, bool tree)
        {
            if (!tree && go.GetComponentsInChildren<LODGroup>(true).Length > 0) return;
            var renderers = go.GetComponentsInChildren<Renderer>(true);
            var levels = new List<LOD>();
            float[] thresholds = tree ? new[] { .5f, .3f, .15f, .012f } : new[] { .45f, .23f, .10f, .015f };
            for (int i = 0; i < 4; i++)
            {
                int level = i;
                var matching = renderers.Where(r => r.name.IndexOf("_LOD" + level, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
                if (matching.Length > 0) levels.Add(new LOD(thresholds[i], matching));
            }
            if (levels.Count < 2) return;
            foreach (var old in go.GetComponentsInChildren<LODGroup>(true)) UnityEngine.Object.DestroyImmediate(old);
            var group = go.AddComponent<LODGroup>();
            group.fadeMode = LODFadeMode.None;
            group.SetLODs(levels.ToArray());
            group.RecalculateBounds();
        }

        private static void Sanitize(GameObject go)
        {
            // Оставляем только визуальные компоненты, включая LOD; никакого поведения/физики/сети.
            // Companion behaviours удаляются раньше зависимого Light, чтобы не нарушить RequireComponent.
            foreach (var behaviour in go.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(behaviour);
            foreach (var component in go.GetComponentsInChildren<Component>(true))
                if (component && !(component is Transform) && !(component is MeshFilter) &&
                    !(component is Renderer) && !(component is LODGroup)) UnityEngine.Object.DestroyImmediate(component);
            foreach (var transform in go.GetComponentsInChildren<Transform>(true))
            {
                transform.gameObject.layer = 0;
                GameObjectUtility.SetStaticEditorFlags(transform.gameObject, StaticEditorFlags.OccludeeStatic);
            }
            foreach (var renderer in go.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = true;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            }
        }

        private static void Shape(string name, Transform parent, PrimitiveType type, Vector3 center, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            Sanitize(go);
            go.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static void Rock(Transform parent, string name, Vector3 foot, Vector3 size, float yaw, Palette p)
        {
            var go = Group(name, parent, foot, yaw).gameObject;
            go.transform.localScale = size;
            go.AddComponent<MeshFilter>().sharedMesh = p.StoneMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = p.Rock;
            Sanitize(go);
        }

        private static Material MaterialAsset(string name, Color color)
        {
            const string folder = "Assets/Materials/Lobby/Exterior";
            EnsureFolder(folder);
            string path = folder + "/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing) return existing;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (!shader) throw new InvalidOperationException("URP/Lit недоступен для лагерного реквизита.");
            var material = new Material(shader) { name = name, enableInstancing = true };
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", .12f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static Mesh StoneMeshAsset()
        {
            const string folder = "Assets/Art/Lobby/Exterior";
            EnsureFolder(folder);
            const string path = folder + "/ForestStone.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing) return existing;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();
            const int sides = 7;
            for (int ring = 0; ring < 3; ring++)
                for (int i = 0; i < sides; i++)
                {
                    float angle = i * Mathf.PI * 2 / sides + ring * .19f;
                    float radius = ring == 1 ? .52f : (ring == 0 ? .44f : .27f);
                    radius *= 1 + .13f * Mathf.Sin(i * 2.7f + ring);
                    vertices.Add(new Vector3(Mathf.Cos(angle) * radius, ring == 0 ? 0 : (ring == 1 ? .48f : .91f + .06f * Mathf.Sin(i)), Mathf.Sin(angle) * radius));
                }
            vertices.Add(new Vector3(.04f, 1f, -.02f));
            for (int ring = 0; ring < 2; ring++)
                for (int i = 0; i < sides; i++)
                {
                    int a = ring * sides + i, b = ring * sides + (i + 1) % sides, c = a + sides, d = b + sides;
                    triangles.AddRange(new[] { a, c, b, b, c, d });
                }
            for (int i = 0; i < sides; i++) triangles.AddRange(new[] { 14 + i, 21, 14 + (i + 1) % sides });
            // Раздельные вершины сохраняют читаемые грани малополигонального валуна (35 треугольников).
            var flat = triangles.Select(index => vertices[index]).ToArray();
            var mesh = new Mesh { name = "ForestStone", vertices = flat, triangles = Enumerable.Range(0, flat.Length).ToArray() };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            AssetDatabase.CreateAsset(mesh, path);
            return mesh;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            string parent = path.Substring(0, slash);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(slash + 1));
        }

        private static float Range(System.Random random, float min, float max) => min + (float)random.NextDouble() * (max - min);
    }
}
