using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Измеряет композицию в изолированной физической сцене по её настоящим маркерам.</summary>
    public static class ContactCatalogMeasurement
    {
        public static Transform[] Markers(GameObject prefab)
        {
            var root = prefab.transform.Find("Positions");
            if (root == null) throw new InvalidOperationException("Префабу нужен дочерний Positions с маркерами A/B/C.");
            var markers = root.Cast<Transform>().ToArray();
            if (markers.Length < 2 || markers.Length > 3 || markers.Select(t => t.name).Distinct().Count() != markers.Length)
                throw new InvalidOperationException("Нужны 2–3 уникальных маркера позиций.");
            return markers;
        }

        public static PositionImpactResult Measure(GameObject prefab, out string output)
        {
            if (prefab == null || !PrefabUtility.IsPartOfPrefabAsset(prefab))
                throw new ArgumentException("Выберите сохранённый префаб.");
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                instance.transform.position = Vector3.zero;
                Physics.SyncTransforms();
                var built = MapGridBuilder.Build(scene);
                if (built.Grid == null || built.Problems.Count != 0)
                    throw new InvalidOperationException(string.Join("; ", built.Problems));
                var layout = new PositionImpactLayout { map = prefab.name,
                    positions = Markers(instance).Select(Position).ToArray() };
                var result = PositionImpactAnalysis.Analyze(built.Grid, layout);
                string guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab));
                string stem = "Temp/LevelDesign/ContactCatalog/" + guid;
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(stem));
                File.WriteAllText(stem + ".json", JsonUtility.ToJson(result, true), new UTF8Encoding(false));
                File.WriteAllText(stem + "_input.json", JsonUtility.ToJson(layout, true), new UTF8Encoding(false));
                output = stem + ".json"; return result;
            }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        private static ImpactPosition Position(Transform marker)
        {
            Vector2 center = new Vector2(marker.position.x, marker.position.z);
            return new ImpactPosition { id = marker.name, min = center - Vector2.one * .55f,
                max = center + Vector2.one * .55f, protectedState = "crouching",
                states = new[] { State(marker, "standing", 1.7f, 1.65f, 1.2f),
                    State(marker, "crouching", 1.1f, .9f, .75f) } };
        }

        private static ImpactState State(Transform marker, string id, float eye, float muzzle, float chest)
            => new ImpactState { id = id, center = new Vector2(marker.position.x, marker.position.z),
                yaw = marker.eulerAngles.y,
                eyeOffset = new Vector3(0, eye, 0), muzzleOffset = new Vector3(0, muzzle, .15f),
                body = new[] { Sample(0, eye, 1), Sample(0, chest, 2),
                    Sample(-.3f, chest, 1), Sample(.3f, chest, 1) } };
        private static ImpactBodySample Sample(float x, float y, float weight)
            => new ImpactBodySample { offset = new Vector3(x, y, 0), weight = weight };
    }
}
