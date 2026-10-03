using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VrBattlegrounds.Editor.LevelDesign
{
    /// <summary>Адаптер сцены: одна сетка, один профиль, общие измерения для всех отчётов.</summary>
    public static class MapEvaluationScene
    {
        public static MapEvaluationResult Evaluate(Scene scene, string layoutPath = null,
            MapEvaluationProfile profile = MapEvaluationProfile.Unspecified)
        {
            if (!scene.IsValid() || !scene.isLoaded) throw new ArgumentException("Нужна загруженная сцена.");
            layoutPath = layoutPath ?? DefaultLayoutPath(scene.name);
            PositionImpactLayout layout = File.Exists(layoutPath)
                ? JsonUtility.FromJson<PositionImpactLayout>(File.ReadAllText(layoutPath, Encoding.UTF8)) : null;
            if (profile == MapEvaluationProfile.Unspecified && layout != null) profile = layout.profile;
            var built = MapGridBuilder.Build(scene);
            int ground = LayerMask.NameToLayer("Ground");
            var colliders = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Collider>(false))
                .Where(BlockoutSupportSurfaces.IsActiveSolid).ToArray();
            float floorY = colliders.Where(c => c.gameObject.layer == ground)
                .Select(c => c.bounds.max.y).DefaultIfEmpty(0).Max();
            var bounds = colliders
                .Where(c => c.gameObject.layer != ground && c.attachedRigidbody == null)
                .Select(c => c.bounds).Where(b => built.Grid != null && b.max.y > floorY + LevelDesignRules.StepHeight &&
                    b.min.y < floorY + MapGridBuilder.ObstacleCeiling).ToArray();
            return MapEvaluation.Evaluate(scene.name, built, layout, profile, footprints: bounds);
        }

        public static string DefaultLayoutPath(string map) => "Docs/level-design/maps/" + map + "-positions.json";

        public static string Write(MapEvaluationResult result, string stem)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(stem));
            string text = MapEvaluation.Format(result);
            File.WriteAllText(stem + ".json", JsonUtility.ToJson(result, true), new UTF8Encoding(false));
            File.WriteAllText(stem + ".txt", text, new UTF8Encoding(false));
            return text;
        }
    }
}
