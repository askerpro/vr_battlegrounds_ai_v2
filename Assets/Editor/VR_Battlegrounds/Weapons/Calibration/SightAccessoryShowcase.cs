using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.Weapons.Calibration
{
    /// <summary>
    /// Витрина прицелов и аксессуаров из паков оружия рядом с оружием ручного стенда.
    /// Только для выбора деталей: экземпляры связаны с исходными префабами/FBX пака, игровых компонентов не получают.
    /// </summary>
    public static class SightAccessoryShowcase
    {
        public const string RootName = "AccessoryShowcase_EditorOnly";
        private const string Hands = "Assets/ThirdParty/Hands_Weapons_Animations_Pack_Update/Prefabs/Weapons_Details";
        private const string Kinemation = "Assets/ThirdParty/KINEMATION/TacticalShooterPack";

        // Ряды витрины: оружие на стенде стоит дулом в (0, 1.5, 0) и смотрит в +Z, ряды уходят назад справа от него.
        private static readonly Vector3 Origin = new Vector3(.45f, 1.3f, 0f);
        private const float RowPitch = .65f, Gap = .07f;

        private sealed class Category
        {
            public string Title; public Func<string, bool> Match;
            public Category(string title, Func<string, bool> match) { Title = title; Match = match; }
        }

        private static readonly Category[] Categories =
        {
            new Category("Коллиматоры и голографы", n => (n.StartsWith("Sight_0") && n != "Sight_09") || n.StartsWith("Holograph") || n.Contains("XPS2")),
            new Category("Оптика и магнифер", n => n.StartsWith("Sight_") || n.Contains("Scope") || n.Contains("G33")),
            new Category("Механические прицелы", n => n.Contains("IronSights") || n.Contains("FrontSight") || n.Contains("RearSight")),
            new Category("Глушители и пламегасители", n => n.StartsWith("Silencer") || n.Contains("_Silencer") || n.StartsWith("Flame_Arrester")),
            new Category("Фонари", n => n.Contains("Flaslight") || n.Contains("Flashlight")),
            new Category("Рукоятки и прочее", n => true),
        };

        public static IEnumerable<string> Sources()
        {
            foreach (var folder in new[] { Hands, Kinemation + "/Prefabs/Attachments" })
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { folder }).OrderBy(g => AssetDatabase.GUIDToAssetPath(g), StringComparer.Ordinal))
                    yield return AssetDatabase.GUIDToAssetPath(guid);
            string weapons = Kinemation + "/Meshes/Weapons/";
            foreach (var path in new[]
            {
                // Holograph_Sight.prefab пака уже показывает XPS2; здесь — FBX без префабов.
                "TR15/Attachments/SM_Attach_AR15_G33.FBX", "TR15/Attachments/SM_Attach_AR15_Grip.FBX", "TR15/Attachments/SM_Attach_AR15_Silencer.FBX",
                "TR15/Attachments/Ironsights/SKM_TR15_FrontSight.FBX", "TR15/Attachments/Ironsights/SKM_TR15_RearSight.FBX",
                "SRM-12/Attachments/Phantom_TAC/SM_Phantom_TAC_Scope.FBX", "MX68/Attachments/SM_IronSights_MX68.FBX", "MKR9/SM_MKR9_IronSights.fbx",
            }) yield return weapons + path;
        }

        /// <summary>Пересобирает витрину в открытой сцене ручного стенда; прежняя витрина удаляется.</summary>
        public static GameObject Build(Scene scene)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Витрина собирается только в готовом Edit Mode.");
            Undo.IncrementCurrentGroup(); int group = Undo.GetCurrentGroup();
            foreach (var old in scene.GetRootGameObjects().Where(g => g.name == RootName)) Undo.DestroyObjectImmediate(old);
            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Витрина аксессуаров");
            SceneManager.MoveGameObjectToScene(root, scene);
            root.transform.position = Origin;

            var assets = Sources().Select(p => (path: p, prefab: AssetDatabase.LoadAssetAtPath<GameObject>(p))).Where(a => a.prefab != null).ToList();
            _materialsByMesh = MaterialsByMesh();
            var used = new HashSet<string>();
            int row = 0;
            foreach (var category in Categories)
            {
                var items = assets.Where(a => !used.Contains(a.path) && category.Match(Path.GetFileNameWithoutExtension(a.path))).ToList();
                if (items.Count == 0) continue;
                var rowRoot = new GameObject(category.Title).transform;
                rowRoot.SetParent(root.transform, false);
                rowRoot.localPosition = new Vector3(0, 0, -row * RowPitch);
                Label(rowRoot, category.Title, new Vector3(-.02f, 0, 0), .006f, TextAnchor.MiddleRight);
                float x = 0;
                foreach (var item in items)
                {
                    used.Add(item.path);
                    float width = Place(rowRoot, item.prefab, item.path, ref x);
                    x += width + Gap;
                }
                row++;
            }
            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
            return root;
        }

        // Ставит экземпляр длинной осью вдоль +Z, низом на линию ряда; возвращает ширину по X.
        private static float Place(Transform row, GameObject prefab, string path, ref float x)
        {
            var holder = new GameObject(Path.GetFileNameWithoutExtension(path)).transform;
            holder.SetParent(row, false);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, holder);
            // Голые FBX пака импортированы без материалов — берём их из префабов оружия с тем же мешем.
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                var current = renderer.sharedMaterials;
                if (!current.Any(m => m == null || AssetDatabase.GetAssetPath(m) == path || AssetDatabase.GetAssetPath(m).StartsWith("Packages/", StringComparison.Ordinal))) continue;
                if (MeshOf(renderer) is Mesh mesh && _materialsByMesh.TryGetValue(mesh, out var materials)) { renderer.sharedMaterials = materials; continue; }
                // Меш не используется ни одним префабом: встроенный MI_SRM12 → M_SRM-12_Body по имени.
                renderer.sharedMaterials = current.Select(m => m != null && AssetDatabase.GetAssetPath(m) == path ? MaterialByName(m.name) ?? m : m).ToArray();
            }
            // Поворот импорта (KINEMATION: Z-up → −90° по X) уже ставит деталь верхом вверх и стволом в +Z;
            // докручиваем только то, что после него всё ещё лежит длинной осью поперёк.
            var size = LocalBounds(instance.transform, holder).size;
            if (size.y > size.z && size.y >= size.x) instance.transform.localRotation = Quaternion.Euler(-90, 0, 0) * instance.transform.localRotation;
            else if (size.x > size.z && size.x > size.y) instance.transform.localRotation = Quaternion.Euler(0, 90, 0) * instance.transform.localRotation;
            var bounds = LocalBounds(instance.transform, holder);
            // Ширина места — по детали или подписи, чтобы имена мелких деталей не сливались; деталь по центру места.
            float slot = Mathf.Max(bounds.size.x, holder.name.Length * .012f);
            instance.transform.localPosition -= new Vector3(bounds.min.x - (slot - bounds.size.x) * .5f, bounds.min.y, bounds.center.z);
            holder.localPosition = new Vector3(x, 0, 0);
            Label(holder, holder.name, new Vector3(slot * .5f, 0, -bounds.size.z * .5f - .015f), .0035f, TextAnchor.UpperCenter);
            return slot;
        }

        private static Dictionary<Mesh, Material[]> _materialsByMesh = new Dictionary<Mesh, Material[]>();

        private static Dictionary<Mesh, Material[]> MaterialsByMesh()
        {
            var result = new Dictionary<Mesh, Material[]>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { Kinemation + "/Prefabs", "Assets/Prefabs/Weapons" }))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                if (prefab == null) continue;
                foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                    if (MeshOf(renderer) is Mesh mesh && !result.ContainsKey(mesh) && renderer.sharedMaterials.Length > 0 && renderer.sharedMaterials.All(m => m != null))
                        result.Add(mesh, renderer.sharedMaterials);
            }
            return result;
        }

        private static Material MaterialByName(string embedded)
        {
            string key = Normalize(embedded.StartsWith("MI_", StringComparison.Ordinal) ? embedded.Substring(3) : embedded);
            return AssetDatabase.FindAssets("t:Material", new[] { Kinemation + "/Meshes/Weapons" })
                .Select(AssetDatabase.GUIDToAssetPath).Where(p => p.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)).OrderBy(p => p, StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<Material>)
                .FirstOrDefault(m => m != null && key.Length > 0 && Normalize(m.name).Contains(key));
            string Normalize(string name) => new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
        }

        private static Mesh MeshOf(Renderer renderer) =>
            renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh : renderer.TryGetComponent<MeshFilter>(out var filter) ? filter.sharedMesh : null;

        private static Bounds LocalBounds(Transform root, Transform space)
        {
            var result = new Bounds(); bool first = true;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = MeshOf(renderer);
                if (mesh == null) continue;
                var b = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var point = space.InverseTransformPoint(renderer.transform.TransformPoint(corner));
                    if (first) { result = new Bounds(point, Vector3.zero); first = false; } else result.Encapsulate(point);
                }
            }
            return result;
        }

        // Подпись лежит на плоскости ряда, читается сверху.
        private static void Label(Transform parent, string text, Vector3 position, float size, TextAnchor anchor)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(90, 0, 0);
            var mesh = go.AddComponent<TextMesh>();
            mesh.text = text; mesh.characterSize = size; mesh.fontSize = 64; mesh.anchor = anchor; mesh.color = Color.white;
        }
    }
}
