using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor.Performance.BoxUv
{
    /// <summary>
    /// Переводит материалы с трипланарного S_VPWorldAligned (пак MilitaryCamp) на стандартный URP Baked Lit:
    /// одна выборка основной текстуры (_MainBC) + лайтмапа/проба. Слои R/G по vertex paint, нормали, краевой шум
    /// и насыщенность не переносятся — это и есть экономия (normal A/B на Quest: ~−1,87 мс кадра Lobby).
    /// Повтор текстуры сохраняется: тайлинг = метры на единицу UV / _MainTile. Для box-UV (<see cref="BoxUvProjector"/>)
    /// UV уже в метрах; для меша со своей развёрткой метры на единицу UV измерены отдельно.
    /// Повторный вызов материал на Baked Lit не трогает.
    /// </summary>
    public static class BakedLitMaterialConverter
    {
        public const string BakedLitShader = "Universal Render Pipeline/Baked Lit";

        public sealed class Entry
        {
            public string MaterialPath;
            /// <summary>true — сабмеши с этим материалом переразворачиваются box-UV (UV в метрах).</summary>
            public bool BoxUv;
            /// <summary>Метры мира на единицу UV0 меша без переразвёртки.</summary>
            public float MetresPerUv = 1f;
        }

        /// <summary>Материалы Lobby на S_VPWorldAligned (аудит UV 2026-10-20, reports/uv-audit-20261020).</summary>
        public static readonly IReadOnlyList<Entry> Materials = new[]
        {
            new Entry { MaterialPath = "Assets/Art/Lobby/Imported/MilitaryCamp/Art/Materials/M_Concrete_01.mat", BoxUv = true },
            new Entry { MaterialPath = "Assets/Art/Lobby/Imported/MilitaryCamp/Art/Materials/M_Tiles_ObjectSpace.mat", BoxUv = true },
            // WornGround (пол шатра) — материал проекта: его пишет LobbyDecorationAuthoring.ApplyGroundMaterials.
        };

        [MenuItem("Tools/VR Battlegrounds/Performance/Box UV/Convert Materials To Baked Lit")]
        private static void ConvertMenu() => GameLog.Perf.Info("[BakedLitMaterialConverter] " + JsonConvert.SerializeObject(Convert()));

        public static Dictionary<string, object> Convert()
        {
            var converted = new List<object>();
            var unchanged = new List<string>();
            var failures = new List<string>();
            Shader shader = Shader.Find(BakedLitShader);
            if (shader == null)
            {
                failures.Add("shader not found: " + BakedLitShader);
                return Report(converted, unchanged, failures);
            }
            foreach (Entry entry in Materials)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(entry.MaterialPath);
                if (material == null) { failures.Add("missing: " + entry.MaterialPath); continue; }
                if (material.shader == shader) { unchanged.Add(entry.MaterialPath); continue; }
                if (!material.HasProperty("_MainBC") || !material.HasProperty("_MainTile"))
                {
                    failures.Add("not S_VPWorldAligned: " + entry.MaterialPath + " (" + material.shader.name + ")");
                    continue;
                }
                Texture baseMap = material.GetTexture("_MainBC");
                float tile = material.GetFloat("_MainTile");
                Color tint = material.HasProperty("_MainTint") ? material.GetColor("_MainTint") : Color.white;
                if (tile <= 0f) { failures.Add("bad _MainTile " + tile + ": " + entry.MaterialPath); continue; }
                float repeat = entry.MetresPerUv / tile;
                string from = material.shader.name;

                material.shader = shader;
                material.SetTexture("_BaseMap", baseMap);
                material.SetTextureScale("_BaseMap", new Vector2(repeat, repeat));
                material.SetTextureOffset("_BaseMap", Vector2.zero);
                material.SetColor("_BaseColor", new Color(tint.r, tint.g, tint.b, 1f));
                material.renderQueue = -1;
                EditorUtility.SetDirty(material);
                converted.Add(new { path = entry.MaterialPath, from, baseMap = baseMap != null ? baseMap.name : null, tile, repeat, tint = new[] { tint.r, tint.g, tint.b } });
            }
            AssetDatabase.SaveAssets();
            return Report(converted, unchanged, failures);
        }

        private static Dictionary<string, object> Report(List<object> converted, List<string> unchanged, List<string> failures) =>
            new Dictionary<string, object>
            {
                ["converted"] = converted,
                ["unchanged"] = unchanged,
                ["failures"] = failures,
                ["passed"] = failures.Count == 0,
            };
    }
}
