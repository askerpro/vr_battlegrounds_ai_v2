using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Maps;

namespace VrBattlegrounds.EditorTools
{
    /// <summary>Меняет только существующий визуальный щит; Pivot, RingOuter и попадания сохраняются.</summary>
    public static class LobbyTargetVisualAdapter
    {
        public static void Apply(Scene scene)
        {
            string sourcePath = AssetDatabase.GUIDToAssetPath("abc00000000000597718462212395182");
            var source = AssetDatabase.LoadAllAssetsAtPath(sourcePath).OfType<Mesh>().First(m => m.name.Contains("LOD0"));
            if (source.subMeshCount < 2) throw new InvalidOperationException("Нет отдельного submesh щита.");
            string path = "Assets/Art/Lobby/TargetPanel.asset";
            var panel = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (!panel) { panel = new Mesh { name = "LobbyTargetPanel" }; AssetDatabase.CreateAsset(panel, path); }
            var indices = source.GetTriangles(1); var used = indices.Distinct().ToArray();
            var original = source.vertices; var bounds = new Bounds(original[used[0]], Vector3.zero);
            foreach (int i in used) bounds.Encapsulate(original[i]);
            var map = used.Select((index, next) => new { index, next }).ToDictionary(p => p.index, p => p.next);
            var printVertices = used.Select(i => new Vector3((original[i].x - bounds.center.x) / bounds.size.y * (.7f/.6f),
                (original[i].y - bounds.center.y) / bounds.size.y, (original[i].z - bounds.center.z) / Mathf.Max(.001f, bounds.size.z))).ToArray();
            var backing = new[] { new Vector3(-.5f,-.5f,-.28f),new Vector3(.5f,-.5f,-.28f),new Vector3(.5f,.5f,-.28f),new Vector3(-.5f,.5f,-.28f),
                new Vector3(-.5f,-.5f,.28f),new Vector3(.5f,-.5f,.28f),new Vector3(.5f,.5f,.28f),new Vector3(-.5f,.5f,.28f) };
            panel.Clear(); panel.vertices = printVertices.Concat(backing).ToArray();
            panel.uv = used.Select(i => source.uv[i]).Concat(new Vector2[8]).ToArray();
            panel.subMeshCount = 2;
            panel.SetTriangles(new[] {0,2,1,0,3,2,4,5,6,4,6,7,0,4,7,0,7,3,1,2,6,1,6,5,0,1,5,0,5,4,3,7,6,3,6,2}.Select(i=>i+used.Length).ToArray(),0);
            panel.SetTriangles(indices.Select(i => map[i]).ToArray(),1); panel.RecalculateNormals(); panel.RecalculateBounds();
            EditorUtility.SetDirty(panel); AssetDatabase.SaveAssetIfDirty(panel);
            string matPath = "Assets/Materials/Lobby/CardboardTarget.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (!material) { material = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(material, matPath); }
            string sourceMaterial = AssetDatabase.GUIDToAssetPath("abc00000000011422199519173746255");
            var match = Regex.Match(File.ReadAllText(sourceMaterial), @"- _BaseMap:\s*m_Texture: \{fileID: \d+, guid: ([a-f0-9]{32})");
            if (!match.Success) throw new InvalidOperationException("Нет исходной текстуры картонной мишени.");
            material.shader = Shader.Find("Universal Render Pipeline/Lit");
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture>(AssetDatabase.GUIDToAssetPath(match.Groups[1].Value)));
            material.SetColor("_BaseColor", Color.white); material.SetFloat("_Cull", 0); material.SetFloat("_Smoothness", .05f);
            EditorUtility.SetDirty(material); AssetDatabase.SaveAssetIfDirty(material);
            string backingPath = "Assets/Materials/Lobby/CardboardTargetBacking.mat";
            var backingMaterial = AssetDatabase.LoadAssetAtPath<Material>(backingPath);
            if (!backingMaterial) { backingMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(backingMaterial, backingPath); }
            backingMaterial.SetColor("_BaseColor",new Color(.87f,.87f,.84f)); backingMaterial.SetFloat("_Smoothness",.05f);
            EditorUtility.SetDirty(backingMaterial); AssetDatabase.SaveAssetIfDirty(backingMaterial);
            foreach (var target in scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<ShootingTarget>(true)))
            {
                var pivot = target.transform.Find("Pivot");
                if (!pivot) throw new InvalidOperationException("Изменился контракт Pivot мишени.");
                var plate = pivot.GetComponentsInChildren<MeshRenderer>(true).Single(r => r.name == "Plate");
                Undo.RecordObjects(new UnityEngine.Object[] { plate, plate.GetComponent<MeshFilter>() }, "Картонный щит Lobby");
                plate.sharedMaterials = new[] {backingMaterial,material}; plate.GetComponent<MeshFilter>().sharedMesh = panel;
                PrefabUtility.RecordPrefabInstancePropertyModifications(plate);
                PrefabUtility.RecordPrefabInstancePropertyModifications(plate.GetComponent<MeshFilter>());
                foreach (var renderer in pivot.GetComponentsInChildren<MeshRenderer>(true))
                    if (renderer != plate && (renderer.name.StartsWith("Ring", StringComparison.Ordinal) || renderer.name == "Bullseye"))
                    { Undo.RecordObject(renderer, "Скрыть прежние кольца"); renderer.enabled = false; PrefabUtility.RecordPrefabInstancePropertyModifications(renderer); }
            }
        }
    }
}
