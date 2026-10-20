using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;
using Object = UnityEngine.Object;

namespace VrBattlegrounds.Editor.Performance
{
    /// <summary>
    /// Дешёвый ярус материалов статичного окружения Lobby (решение пользователя 2026-10-20:
    /// производительность важнее картинки, «замена прежде оптимизации»).
    /// Материалы пакетов (S_BasicTextured, S_Foliage) и URP Lit статичного декора переводятся на
    /// URP Baked Lit (одна текстура + лайтмап/пробы; свет в Lobby весь запечён), материалы с эмиссией —
    /// на URP Simple Lit без бликов. Исходный шейдер и перезаписанные поля хранятся в userData .meta
    /// материала — «Revert» возвращает их. Отчёт — в tasks/scene-detail/reports/ (вне Git).
    /// Пол и его материалы (WornGround, M_Concrete_01, M_Tiles_ObjectSpace, S_VP*) и светящиеся плафоны
    /// (LobbyNightLampLong, M_Emissive — их ведёт ночной пресет) не трогаются.
    /// </summary>
    public static class LobbyGpuMaterialTier
    {
        const string LobbyScenePath = "Assets/Scenes/Lobby.unity";
        const string MenuRoot = "VR Battlegrounds/Performance/Lobby GPU/";
        const string Marker = "lobby-gpu-tier-v1";
        const string BakedLitName = "Universal Render Pipeline/Baked Lit";
        const string SimpleLitName = "Universal Render Pipeline/Simple Lit";
        const string ReportFolder = "tasks/scene-detail/reports/lobby-gpu-tier";

        // Только ассеты окружения лобби: общий игровой контент не меняется.
        static readonly string[] AllowedFolders =
        {
            "Assets/Art/Lobby/",
            "Assets/Materials/Lobby/",
            "Assets/Prefabs/Environment/",
        };

        // Материалы с другим владельцем: пол и бетон пака (BoxUv/BakedLitMaterialConverter), грунт Lobby
        // (LobbyDecorationAuthoring.ApplyGroundMaterials сам ставит Baked Lit). Ни Apply, ни Revert их не трогают.
        static readonly string[] ExcludedMaterialPrefixes = { "WornGround", "M_Concrete_01", "M_Tiles_ObjectSpace", "OutsideGravel" };

        // Светящиеся плафоны ламп ведёт ночной пресет (LobbyNightLighting.ApplyLampGlow): Simple Lit/Unlit с эмиссией.
        // Baked Lit эмиссии не имеет, а Revert вернул бы им исходный шейдер без свечения.
        static readonly string[] LampGlowMaterials = { "LobbyNightLampLong", "M_Emissive" };

        // Корни сцены с игровыми/интерактивными объектами — их материалы не трогаются.
        static readonly string[] DynamicRoots = { "Gameplay" };

        enum SourceKind { None, BasicTextured, Foliage, UrpLit }

        sealed class Candidate
        {
            public Material Material;
            public string Path;
            public SourceKind Kind;
            public string Target;
            public string Skip;
            public readonly List<string> Notes = new List<string>();
            public int Users;
        }

        [Serializable]
        sealed class RevertData
        {
            public string marker;
            public string shaderName;
            public string shaderPath;
            public int renderQueue;
            public string[] keywords;
            public string renderType;
            public bool hadBaseColor;
            public Color baseColor;
            public Vector4 baseMapST;
            public bool hadCull;
            public float cull;
            public bool hadCutoff;
            public float cutoff;
            public bool hadAlphaClip;
            public float alphaClip;
            public bool hadEmissionColor;
            public Color emissionColor;
            public string emissionMapGuid;
            public bool hadAlphaToMask;
            public float alphaToMask;
        }

        [MenuItem(MenuRoot + "Report material tier")]
        static void ReportMenu() => Run(apply: false);

        [MenuItem(MenuRoot + "Apply material tier")]
        static void ApplyMenu() => Run(apply: true);

        [MenuItem(MenuRoot + "Revert material tier")]
        static void RevertMenu() => Revert();

        /// <summary>Построить список (и при apply=true применить); возвращает путь отчёта.</summary>
        public static string Run(bool apply)
        {
            Scene scene = SceneManager.GetSceneByPath(LobbyScenePath);
            if (!scene.IsValid() || !scene.isLoaded)
            {
                GameLog.Perf.Warning("[LobbyGpuMaterialTier] Откройте сцену Lobby.");
                return null;
            }

            List<Candidate> candidates = Collect(scene);
            var report = new StringBuilder();
            report.Append("# Lobby GPU material tier — ").Append(apply ? "apply" : "report").Append('\n');
            int changed = 0;
            if (apply)
            {
                // По одному: материал сохраняется до записи userData в .meta (реимпорт перечитывает ассет с диска).
                foreach (Candidate c in candidates.Where(x => x.Skip == null))
                {
                    ApplyOne(c);
                    changed++;
                }
            }

            foreach (Candidate c in candidates.OrderBy(x => x.Skip != null).ThenBy(x => x.Path))
            {
                report.Append(c.Skip == null ? (apply ? "CHANGED " : "WOULD   ") : "SKIP    ")
                      .Append(c.Path).Append(" | ").Append(c.Kind).Append(" -> ").Append(c.Target ?? "-")
                      .Append(" | users=").Append(c.Users);
                if (c.Skip != null) report.Append(" | ").Append(c.Skip);
                if (c.Notes.Count > 0) report.Append(" | ").Append(string.Join("; ", c.Notes));
                report.Append('\n');
            }
            report.Append("total=").Append(candidates.Count).Append(" eligible=")
                  .Append(candidates.Count(x => x.Skip == null)).Append(" changed=").Append(changed).Append('\n');
            string path = WriteReport(apply ? "apply" : "report", report.ToString());
            GameLog.Perf.Info($"[LobbyGpuMaterialTier] {(apply ? "Применено" : "Отчёт")}: {changed} материалов, отчёт {path}");
            return path;
        }

        /// <summary>Вернуть исходные шейдеры всем материалам с маркером.</summary>
        public static int Revert()
        {
            int reverted = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", AllowedFolders.Select(f => f.TrimEnd('/')).ToArray()))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                AssetImporter importer = AssetImporter.GetAtPath(path);
                if (importer == null || string.IsNullOrEmpty(importer.userData) || !importer.userData.Contains(Marker)) continue;
                string fileName = System.IO.Path.GetFileNameWithoutExtension(path);
                if (LampGlowMaterials.Contains(fileName) || ExcludedMaterialPrefixes.Any(p => fileName.StartsWith(p, StringComparison.Ordinal))) continue;
                RevertData data = JsonUtility.FromJson<RevertData>(importer.userData);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                Shader shader = LoadShader(data.shaderName, data.shaderPath);
                if (material == null || shader == null)
                {
                    GameLog.Perf.Warning($"[LobbyGpuMaterialTier] Не удалось вернуть {path}: шейдер {data.shaderName} не найден.");
                    continue;
                }
                material.shader = shader;
                material.shaderKeywords = data.keywords ?? Array.Empty<string>();
                material.renderQueue = data.renderQueue;
                material.SetOverrideTag("RenderType", data.renderType ?? "");
                if (data.hadBaseColor) material.SetColor("_BaseColor", data.baseColor);
                material.SetTextureScale("_BaseMap", new Vector2(data.baseMapST.x, data.baseMapST.y));
                material.SetTextureOffset("_BaseMap", new Vector2(data.baseMapST.z, data.baseMapST.w));
                if (data.hadCull) material.SetFloat("_Cull", data.cull);
                if (data.hadCutoff) material.SetFloat("_Cutoff", data.cutoff);
                if (data.hadAlphaClip) material.SetFloat("_AlphaClip", data.alphaClip);
                if (data.hadEmissionColor) material.SetColor("_EmissionColor", data.emissionColor);
                if (data.hadAlphaToMask) material.SetFloat("_AlphaToMask", data.alphaToMask);
                if (data.emissionMapGuid != null)
                    material.SetTexture("_EmissionMap", string.IsNullOrEmpty(data.emissionMapGuid)
                        ? null
                        : AssetDatabase.LoadAssetAtPath<Texture>(AssetDatabase.GUIDToAssetPath(data.emissionMapGuid)));
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
                importer.userData = "";
                importer.SaveAndReimport();
                reverted++;
            }
            GameLog.Perf.Info($"[LobbyGpuMaterialTier] Возвращено исходных шейдеров: {reverted}");
            return reverted;
        }

        static List<Candidate> Collect(Scene scene)
        {
            var users = new Dictionary<Material, List<Renderer>>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Renderer r in root.GetComponentsInChildren<Renderer>(false))
                {
                    if (!r.enabled) continue;
                    foreach (Material m in r.sharedMaterials)
                    {
                        if (m == null) continue;
                        if (!users.TryGetValue(m, out List<Renderer> list)) users[m] = list = new List<Renderer>();
                        list.Add(r);
                    }
                }
            }

            HashSet<string> foreign = ForeignDependencies();
            var result = new List<Candidate>();
            foreach (KeyValuePair<Material, List<Renderer>> pair in users)
            {
                Material m = pair.Key;
                SourceKind kind = Classify(m.shader);
                if (kind == SourceKind.None) continue;
                var c = new Candidate { Material = m, Kind = kind, Path = AssetDatabase.GetAssetPath(m), Users = pair.Value.Count };
                result.Add(c);
                if (string.IsNullOrEmpty(c.Path) || !c.Path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                    c.Skip = "не ассет .mat";
                else if (!AllowedFolders.Any(f => c.Path.StartsWith(f, StringComparison.Ordinal)))
                    c.Skip = "вне папок окружения лобби";
                else if (ExcludedMaterialPrefixes.Any(p => m.name.StartsWith(p, StringComparison.Ordinal)))
                    c.Skip = "пол (задача главного агента)";
                else if (LampGlowMaterials.Contains(m.name))
                    c.Skip = "светящийся плафон (ночной пресет LobbyNightLighting)";
                else if (foreign.Contains(c.Path))
                    c.Skip = "используется вне лобби (другие сцены/префабы)";
                else if (pair.Value.Any(IsDynamicUser))
                    c.Skip = "используется нестатичным/игровым объектом: " + Describe(pair.Value.First(IsDynamicUser));
                else
                    Plan(c);
            }
            return result;
        }

        static void Plan(Candidate c)
        {
            Material m = c.Material;
            bool cutout = m.IsKeywordEnabled("_ALPHATEST_ON") || m.renderQueue == 2450 && c.Kind == SourceKind.Foliage;
            if (c.Kind == SourceKind.UrpLit && m.HasProperty("_Surface") && m.GetFloat("_Surface") > 0.5f)
            {
                c.Skip = "прозрачный";
                return;
            }
            if (c.Kind == SourceKind.BasicTextured)
            {
                if (GetFloat(m, "_UseWorldAligned") > 0.5f) { c.Skip = "world-aligned (нужен триплан)"; return; }
                if (GetFloat(m, "_UseOpacity") > 0.5f || GetFloat(m, "_OpacityFromORM") > 0.5f) { c.Skip = "прозрачность из отдельной карты"; return; }
                if (Math.Abs(GetFloat(m, "_Saturation", 1f) - 1f) > 0.01f || Math.Abs(GetFloat(m, "_Contrast", 1f) - 1f) > 0.01f)
                    c.Notes.Add($"saturation={GetFloat(m, "_Saturation", 1f):F2} contrast={GetFloat(m, "_Contrast", 1f):F2} не переносятся");
            }
            bool emissive = IsEmissive(m, c.Kind);
            c.Target = emissive ? SimpleLitName : BakedLitName;
            if (cutout) c.Notes.Add("alpha clip");
            if (m.HasProperty(c.Kind == SourceKind.UrpLit ? "_BumpMap" : "_NormalMap") &&
                m.GetTexture(c.Kind == SourceKind.UrpLit ? "_BumpMap" : "_NormalMap") != null)
                c.Notes.Add("normal map отброшена");
        }

        static void ApplyOne(Candidate c)
        {
            Material m = c.Material;
            Shader target = Shader.Find(c.Target);
            if (target == null) throw new InvalidOperationException("Нет шейдера " + c.Target);

            // Снять значения до смены шейдера.
            Texture baseMap = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
            Vector2 scale = m.HasProperty("_BaseMap") ? m.GetTextureScale("_BaseMap") : Vector2.one;
            Vector2 offset = m.HasProperty("_BaseMap") ? m.GetTextureOffset("_BaseMap") : Vector2.zero;
            Color color = Color.white;
            float cutoff = 0.5f;
            bool cutout = m.IsKeywordEnabled("_ALPHATEST_ON") || (c.Kind == SourceKind.Foliage && m.renderQueue == 2450);
            float cull = m.HasProperty("_Cull") ? m.GetFloat("_Cull") : 2f;
            // Alpha-to-coverage при MSAA 4 сохраняет сглаженный край хвои и сетки (как в исходных материалах пакета).
            bool alphaToMask = m.IsKeywordEnabled("_ALPHATOMASK_ON") || GetFloat(m, "_AlphaToMask") > 0.5f;
            bool emissive = IsEmissive(m, c.Kind);
            Texture emissionMap = null;
            Color emissionColor = Color.black;
            switch (c.Kind)
            {
                case SourceKind.BasicTextured:
                case SourceKind.Foliage:
                    if (m.HasProperty("_Tiling"))
                    {
                        Vector4 t = m.GetVector("_Tiling");
                        if (t.x != 0f && t.y != 0f) scale = new Vector2(t.x, t.y);
                    }
                    if (m.HasProperty("_Tint")) color = m.GetColor("_Tint");
                    if (m.HasProperty("_AlphaClipping")) cutoff = m.GetFloat("_AlphaClipping");
                    if (c.Kind == SourceKind.Foliage) cutout = true;
                    if (m.HasProperty("_EmissiveMap")) emissionMap = m.GetTexture("_EmissiveMap");
                    if (m.HasProperty("_EmissiveColor")) emissionColor = m.GetColor("_EmissiveColor");
                    break;
                case SourceKind.UrpLit:
                    if (m.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT")) throw new InvalidOperationException("Прозрачный материал " + c.Path);
                    if (m.HasProperty("_BaseColor")) color = m.GetColor("_BaseColor");
                    if (m.HasProperty("_Cutoff")) cutoff = m.GetFloat("_Cutoff");
                    if (m.HasProperty("_EmissionMap")) emissionMap = m.GetTexture("_EmissionMap");
                    if (m.HasProperty("_EmissionColor")) emissionColor = m.GetColor("_EmissionColor");
                    break;
            }

            var revert = new RevertData
            {
                marker = Marker,
                shaderName = m.shader.name,
                shaderPath = AssetDatabase.GetAssetPath(m.shader),
                renderQueue = m.renderQueue,
                keywords = m.shaderKeywords,
                renderType = m.GetTag("RenderType", false, ""),
                hadBaseColor = m.HasProperty("_BaseColor"),
                baseColor = m.HasProperty("_BaseColor") ? m.GetColor("_BaseColor") : Color.white,
                baseMapST = m.HasProperty("_BaseMap")
                    ? new Vector4(m.GetTextureScale("_BaseMap").x, m.GetTextureScale("_BaseMap").y,
                        m.GetTextureOffset("_BaseMap").x, m.GetTextureOffset("_BaseMap").y)
                    : new Vector4(1, 1, 0, 0),
                hadCull = m.HasProperty("_Cull"),
                cull = cull,
                hadCutoff = m.HasProperty("_Cutoff"),
                cutoff = m.HasProperty("_Cutoff") ? m.GetFloat("_Cutoff") : 0.5f,
                hadAlphaClip = m.HasProperty("_AlphaClip"),
                alphaClip = m.HasProperty("_AlphaClip") ? m.GetFloat("_AlphaClip") : 0f,
                hadEmissionColor = m.HasProperty("_EmissionColor"),
                emissionColor = m.HasProperty("_EmissionColor") ? m.GetColor("_EmissionColor") : Color.black,
                hadAlphaToMask = m.HasProperty("_AlphaToMask"),
                alphaToMask = GetFloat(m, "_AlphaToMask"),
                emissionMapGuid = m.HasProperty("_EmissionMap") && m.GetTexture("_EmissionMap") != null
                    ? AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(m.GetTexture("_EmissionMap")))
                    : (m.HasProperty("_EmissionMap") ? "" : null),
            };

            // Альфа оттенка в материалах пакетов часто 0 (в графе не используется); в Baked Lit она умножает
            // альфу текстуры и полностью отсекла бы cutout-материалы.
            color.a = 1f;
            m.shader = target;
            m.shaderKeywords = Array.Empty<string>();
            m.SetTexture("_BaseMap", baseMap);
            m.SetTextureScale("_BaseMap", scale);
            m.SetTextureOffset("_BaseMap", offset);
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Surface", 0f);
            m.SetFloat("_Cull", cull);
            m.SetFloat("_AlphaClip", cutout ? 1f : 0f);
            m.SetFloat("_Cutoff", cutoff);
            m.SetFloat("_ReceiveShadows", 0f);
            if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", null);
            if (m.HasProperty("_AlphaToMask")) m.SetFloat("_AlphaToMask", cutout && alphaToMask ? 1f : 0f);
            if (cutout)
            {
                m.EnableKeyword("_ALPHATEST_ON");
                m.SetOverrideTag("RenderType", "TransparentCutout");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            }
            else
            {
                m.SetOverrideTag("RenderType", "Opaque");
                m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;
            }
            m.EnableKeyword("_RECEIVE_SHADOWS_OFF");
            if (c.Target == SimpleLitName && emissive)
            {
                // SimpleLitGUI.SpecularSource: 0 — блики (текстура/цвет), 1 — NoSpecular.
                m.SetFloat("_SpecularHighlights", 1f);
                m.DisableKeyword("_SPECULAR_COLOR");
                m.DisableKeyword("_SPECGLOSSMAP");
                m.SetTexture("_EmissionMap", emissionMap);
                m.SetColor("_EmissionColor", emissionColor);
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }

            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
            AssetImporter importer = AssetImporter.GetAtPath(c.Path);
            importer.userData = JsonUtility.ToJson(revert);
            importer.SaveAndReimport();
        }

        // Эмиссия с чёрным цветом (OutsideGravel, CheckpointRoad) ничего не даёт — такой материал идёт в Baked Lit.
        static bool IsEmissive(Material m, SourceKind kind)
        {
            switch (kind)
            {
                case SourceKind.BasicTextured:
                    return GetFloat(m, "_UseEmissive") > 0.5f && m.HasProperty("_EmissiveColor") && m.GetColor("_EmissiveColor").maxColorComponent > 0.001f;
                case SourceKind.UrpLit:
                    return m.IsKeywordEnabled("_EMISSION") && m.HasProperty("_EmissionColor") && m.GetColor("_EmissionColor").maxColorComponent > 0.001f;
                default:
                    return false;
            }
        }

        static SourceKind Classify(Shader shader)
        {
            if (shader == null) return SourceKind.None;
            switch (shader.name)
            {
                case "Shader Graphs/S_BasicTextured": return SourceKind.BasicTextured;
                case "Shader Graphs/S_Foliage": return SourceKind.Foliage;
                case "Universal Render Pipeline/Lit": return SourceKind.UrpLit;
                default: return SourceKind.None;
            }
        }

        static bool IsDynamicUser(Renderer r)
        {
            GameObject go = r.gameObject;
            if (!go.isStatic) return true;
            Transform root = go.transform.root;
            if (DynamicRoots.Contains(root.name)) return true;
            return r.GetComponentInParent<Rigidbody>() != null;
        }

        static string Describe(Renderer r)
        {
            string path = r.name;
            Transform t = r.transform.parent;
            for (int i = 0; t != null && i < 4; i++, t = t.parent) path = t.name + "/" + path;
            return path;
        }

        /// <summary>Материалы, на которые ссылаются другие сцены сборки и префабы вне папок окружения лобби.</summary>
        static HashSet<string> ForeignDependencies()
        {
            var roots = new List<string>();
            roots.AddRange(EditorBuildSettings.scenes.Where(s => s.enabled && s.path != LobbyScenePath).Select(s => s.path));
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (AllowedFolders.Any(f => p.StartsWith(f, StringComparison.Ordinal))) continue;
                if (p.StartsWith("Assets/ThirdParty/", StringComparison.Ordinal)) continue;
                roots.Add(p);
            }
            return new HashSet<string>(AssetDatabase.GetDependencies(roots.ToArray(), true)
                .Where(p => p.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)));
        }

        static Shader LoadShader(string name, string path)
        {
            if (!string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.Ordinal))
            {
                Shader s = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Shader>().FirstOrDefault();
                if (s != null) return s;
            }
            return Shader.Find(name);
        }

        static float GetFloat(Material m, string name, float fallback = 0f) => m.HasProperty(name) ? m.GetFloat(name) : fallback;

        static string WriteReport(string kind, string text)
        {
            string folder = Path.Combine(Directory.GetCurrentDirectory(), ReportFolder);
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, $"{DateTime.Now:yyyyMMdd-HHmmss}-{kind}.txt");
            File.WriteAllText(path, text.Replace("\r\n", "\n"), new UTF8Encoding(false));
            return path;
        }
    }
}
