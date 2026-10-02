using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Материалы стенда из текстур установленного пака; исходные материалы паков не меняются.</summary>
    public static class ArsenalSurfaceMaterials
    {
        private const string Folder = "Assets/Art/ArsenalBoundary/";
        private const string Textures = "Assets/HIVEMIND/PostApocalypticTown/URP/Art/Textures/Metal/";

        public static Material Metal(string name, Color tint, float metallic, float smoothness, bool normal = true)
        {
            string path = Folder + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Textures + "T_Metal_A.png"));
            material.SetColor("_BaseColor", tint);
            material.SetFloat("_Metallic", metallic);
            material.SetFloat("_Smoothness", smoothness);
            material.SetTextureScale("_BaseMap", new Vector2(2f, 2f));
            material.SetTexture("_BumpMap", normal ? AssetDatabase.LoadAssetAtPath<Texture2D>(Textures + "T_Metal_N.png") : null);
            material.SetFloat("_BumpScale", .25f);
            if (normal) material.EnableKeyword("_NORMALMAP"); else material.DisableKeyword("_NORMALMAP");
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        public static void Apply(GameObject root)
        {
            var paint = Metal("ArsenalOlivePaint", new Color(.52f, .56f, .40f), .3f, .26f);
            var steel = Metal("ArsenalWornSteel", new Color(.80f, .82f, .80f), .75f, .32f);
            var trim = Metal("ArsenalDarkMetal", new Color(.23f, .25f, .23f), .5f, .22f);
            foreach (var renderer in root.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (renderer.name == "PegboardSection" || renderer.name == "ShelfPegboardStrip")
                {
                    string name = "PerforatedSteel_" + Mathf.RoundToInt(renderer.transform.localScale.x * 1000f) +
                        "_" + Mathf.RoundToInt(renderer.transform.localScale.y * 1000f);
                    var panel = AssetDatabase.LoadAssetAtPath<Material>(Folder + name + ".mat");
                    if (panel == null)
                    {
                        panel = new Material(AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Arsenal/WeaponRackMaterial.mat")) { name = name };
                        AssetDatabase.CreateAsset(panel, Folder + name + ".mat");
                    }
                    panel.SetColor("_BaseColor", new Color(.52f, .55f, .48f));
                    panel.SetFloat("_Metallic", .65f); panel.SetFloat("_Smoothness", .25f);
                    // Квадратная текстура: одна плотность по X/Y сохраняет круги при любом размере панели.
                    panel.SetTextureScale("_BaseMap", new Vector2(renderer.transform.localScale.x * 4f,
                        renderer.transform.localScale.y * 4f));
                    EditorUtility.SetDirty(panel); renderer.sharedMaterial = panel;
                    continue;
                }
                string original = renderer.sharedMaterial == null ? "" : AssetDatabase.GetAssetPath(renderer.sharedMaterial);
                if (original.EndsWith("Steel.mat") || original.Contains("ConcreteArsenalWallMat")) renderer.sharedMaterial = paint;
                else if (original.EndsWith("BrushedMetal.mat") || renderer.name == "SehlfMesh" || renderer.name == "EquipmentTray") renderer.sharedMaterial = steel;
                else if (original.EndsWith("DarkTrim.mat")) renderer.sharedMaterial = trim;
                else continue;
                FixSurfaceUV(renderer);
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
            }
        }

        private static void FixSurfaceUV(MeshRenderer renderer)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null) return;
            var source = filter.sharedMesh;
            Vector3 scale = renderer.transform.lossyScale;
            string name = "SurfaceUV_" + source.name.Replace('/', '_') + "_" + Mathf.RoundToInt(scale.x * 1000f) +
                "_" + Mathf.RoundToInt(scale.y * 1000f) + "_" + Mathf.RoundToInt(scale.z * 1000f);
            string path = Folder + name + ".asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = Object.Instantiate(source); mesh.name = name;
                var vertices = mesh.vertices; var normals = mesh.normals;
                var uv = new Vector2[vertices.Length];
                for (int i = 0; i < uv.Length; i++)
                {
                    Vector3 p = Vector3.Scale(vertices[i], scale), n = normals[i];
                    uv[i] = Mathf.Abs(n.y) >= Mathf.Abs(n.x) && Mathf.Abs(n.y) >= Mathf.Abs(n.z)
                        ? new Vector2(p.x, p.z) : Mathf.Abs(n.x) > Mathf.Abs(n.z) ? new Vector2(p.z, p.y) : new Vector2(p.x, p.y);
                }
                mesh.uv = uv; mesh.RecalculateTangents();
                AssetDatabase.CreateAsset(mesh, path);
            }
            filter.sharedMesh = mesh;
            PrefabUtility.RecordPrefabInstancePropertyModifications(filter);
        }

        /// <summary>Для сварных прутков нужны UV: без них текстура была бы одним цветом.</summary>
        public static void EnsureWireUV(Mesh mesh)
        {
            if (mesh.uv.Length == mesh.vertexCount) return;
            var vertices = mesh.vertices; var normals = mesh.normals;
            var uv = new Vector2[vertices.Length];
            for (int i = 0; i < uv.Length; i++)
            {
                Vector3 n = normals[i], p = vertices[i];
                uv[i] = Mathf.Abs(n.z) >= Mathf.Abs(n.x) && Mathf.Abs(n.z) >= Mathf.Abs(n.y)
                    ? new Vector2(p.x, p.y) : Mathf.Abs(n.x) > Mathf.Abs(n.y) ? new Vector2(p.z, p.y) : new Vector2(p.x, p.z);
            }
            mesh.uv = uv;
            mesh.RecalculateTangents();
            EditorUtility.SetDirty(mesh);
        }
    }
}
