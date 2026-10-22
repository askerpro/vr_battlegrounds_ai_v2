using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using VrBattlegrounds.Core;

namespace VrBattlegrounds.Editor.Performance.AssetAudit.Fixers
{
    /// <summary>
    /// Фиксер правила <c>shader.non-mobile</c> (QPM-SHADER-001) — единственный писатель перевода материалов окружения
    /// на мобильный шейдер (QPM-SHADER-007; заменил LobbyGpuMaterialTier и BoxUv/BakedLitMaterialConverter).
    /// S_BasicTextured, S_Foliage и URP Lit статичного декора → URP Baked Lit (одна текстура + лайтмапа/пробы);
    /// с видимой эмиссией → URP Simple Lit без бликов. S_VPWorldAligned (трипланар пака MilitaryCamp) — только через
    /// <see cref="PlanWorldAligned"/>: перевод возможен, когда UV меша уже в метрах (box-UV, его зовёт BoxUvTargetScanner).
    /// Розовые материалы HDRP Lit (шейдер HDRP не установлен — Hidden/InternalErrorShader) переводятся по сохранённым
    /// HDRP-свойствам (_BaseColorMap, _BaseColor, _AlphaCutoff, _EmissiveColor): хуже розового не станет, поэтому такой
    /// материал переводится и тогда, когда в открытых сценах не рисуется (назначен только в FBX через externalObjects).
    /// Исходный шейдер и перезаписанные поля хранятся в userData .meta материала — <see cref="Revert"/> возвращает их
    /// (кроме HDRP: исходного шейдера в проекте нет, откат пропускается с предупреждением; его HDRP-свойства остаются в материале).
    ///
    /// Аудит-путь (<see cref="Plan"/>) берёт материалы из находок и переводит только безопасные:
    /// ассет .mat в корнях окружения (<see cref="EnvironmentRoots"/>), не у другого писателя (<see cref="OwnedElsewhere"/>),
    /// рисуется в открытых сценах только статичными объектами и не используется другими сценами сборки и префабами
    /// вне корней окружения. Причины пропуска — в <see cref="LastSkipped"/>. Запуск: окно аудита после «Audit Active Scene»
    /// или меню «Fix Mobile Shaders in Active Scene».
    /// </summary>
    public sealed class BakedLitMaterialFixer : AuditFixer
    {
        public const string BakedLitName = "Universal Render Pipeline/Baked Lit";
        public const string SimpleLitName = "Universal Render Pipeline/Simple Lit";

        /// <summary>Маркер данных отката в userData .meta (исторический, формат прежнего LobbyGpuMaterialTier — 91 материал Lobby).</summary>
        public const string RevertMarker = "lobby-gpu-tier-v1";

        /// <summary>Корни ассетов окружения: материалы вне них (общий игровой контент) не меняются. Новая карта — добавить свой корень.</summary>
        public static readonly string[] EnvironmentRoots =
        {
            "Assets/Art/Lobby/",
            "Assets/Materials/Lobby/",
            "Assets/Prefabs/Environment/",
        };

        /// <summary>
        /// Материалы с другим писателем (QPM-SHADER-007), по префиксу имени: ни план, ни откат их не трогают.
        /// Грунт Lobby — LobbyDecorationAuthoring.ApplyGroundMaterials (сам ставит Baked Lit); светящиеся плафоны —
        /// ночной пресет LobbyNightLighting (Simple Lit/Unlit с эмиссией); дверную лампу создаёт тот же пресет, а её
        /// свечение ведёт <see cref="EmissionOffFixer"/> (в userData .meta осталась старая запись отката яруса — не применять). Бетон и плитка пака идут через box-UV
        /// (<see cref="PlanWorldAligned"/>), их аудит-путь пропускает как world-aligned.
        /// </summary>
        public static readonly string[] OwnedElsewhere =
        {
            "WornGround", "OutsideGravel",
            "LobbyNightLampLong", "LobbyNightDoorLamp", "M_Emissive",
        };

        // Корни сцены с игровыми/интерактивными объектами — их материалы не трогаются.
        private static readonly string[] DynamicRoots = { "Gameplay" };

        public const string ErrorShaderName = "Hidden/InternalErrorShader";

        private enum SourceKind { None, BasicTextured, Foliage, UrpLit, WorldAligned, HdrpLitMissing }

        private sealed class Planned
        {
            public SourceKind Kind;
            public string Target;
            public float MetresPerUv = 1f;
        }

        [Serializable]
        private sealed class RevertData
        {
            public string marker;
            public string shaderName;
            public string shaderPath;
            // GUID отсутствующего шейдера (HDRP Lit): Shader.Find его не вернёт, откат такого материала пропускается.
            public string missingShaderGuid;
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

        private readonly Dictionary<string, Planned> _planned = new Dictionary<string, Planned>(StringComparer.Ordinal);
        private readonly List<string> _skipped = new List<string>();

        public override string RuleId => "shader.non-mobile";
        public override string Title => "Шейдер → Baked Lit / Simple Lit";
        public override string WhatChanges =>
            "Материал окружения: шейдер пака, URP Lit или розовый HDRP Lit → URP Baked Lit (с эмиссией — Simple Lit без бликов). " +
            "Переносятся основная текстура, тайлинг, цвет, alpha clip; нормали, ORM, насыщенность/контраст отбрасываются. " +
            "Исходное — в userData .meta (откат; у HDRP Lit шейдера нет — откат пропускается).";

        /// <summary>Пропуски последнего плана: «путь — причина».</summary>
        public IReadOnlyList<string> LastSkipped => _skipped;

        public override List<AuditFixAction> Plan(IEnumerable<AuditFinding> findings)
        {
            BeginPlan();
            var paths = new List<string>();
            foreach (AuditFinding finding in findings)
                paths.AddRange(finding.FixTargets.Count > 0 ? (IEnumerable<string>)finding.FixTargets : new[] { finding.Asset });

            Dictionary<Material, List<Renderer>> users = SceneUsers();
            HashSet<string> foreign = null;
            var actions = new List<AuditFixAction>();
            foreach (string path in paths.Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal))
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                string skip = CommonSkip(path, material, out SourceKind kind);
                if (skip == null && kind == SourceKind.WorldAligned)
                    skip = "world-aligned: сначала box-UV (Tools/VR Battlegrounds/Performance/Box UV/Scan And Apply)";
                bool drawn = users.TryGetValue(material, out List<Renderer> drawnBy);
                // Розовый материал хуже любого перевода: не рисуется в открытых сценах — всё равно переводится.
                if (skip == null && !drawn && kind != SourceKind.HdrpLitMissing)
                    skip = "не рисуется в открытых сценах — статичность не проверить";
                if (skip == null && drawn && drawnBy.FirstOrDefault(IsDynamicUser) is Renderer dynamic)
                    skip = "используется нестатичным/игровым объектом: " + Describe(dynamic);
                if (skip == null && (foreign ??= ForeignDependencies()).Contains(path))
                    skip = "используется вне открытых сцен (другие сцены сборки или префабы вне корней окружения)";
                AddPlanned(actions, path, material, kind, 1f, skip);
            }
            return actions;
        }

        /// <summary>
        /// План для S_VPWorldAligned с box-UV: ключ — путь материала, значение — метры мира на единицу UV0 (1 для box-UV).
        /// Проверку сабмешей и сцен уже сделал вызывающий (BoxUvTargetScanner); здесь — только тип шейдера и владелец.
        /// </summary>
        public List<AuditFixAction> PlanWorldAligned(IEnumerable<KeyValuePair<string, float>> materials)
        {
            BeginPlan();
            var actions = new List<AuditFixAction>();
            foreach (KeyValuePair<string, float> pair in materials)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(pair.Key);
                string skip = CommonSkip(pair.Key, material, out SourceKind kind);
                if (skip == null && kind != SourceKind.WorldAligned)
                    skip = "не S_VPWorldAligned (" + material.shader.name + ")";
                if (skip == null && pair.Value <= 0f)
                    skip = "метры на единицу UV <= 0";
                AddPlanned(actions, pair.Key, material, kind, pair.Value, skip);
            }
            return actions;
        }

        public override void Apply(IReadOnlyList<AuditFixAction> actions)
        {
            Shader bakedLit = Shader.Find(BakedLitName);
            Shader simpleLit = Shader.Find(SimpleLitName);
            foreach (AuditFixAction action in actions)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(action.AssetPath);
                if (!_planned.TryGetValue(action.AssetPath, out Planned planned))
                    action.MarkFailed("действие не из последнего плана этого фиксера — перепланировать");
                else if (material == null)
                    action.MarkFailed("нет материала");
                else if (Classify(material) != planned.Kind)
                    action.MarkFailed("шейдер изменился после плана: " + material.shader.name);
                else
                {
                    Shader target = planned.Target == SimpleLitName ? simpleLit : bakedLit;
                    if (target == null)
                    {
                        action.MarkFailed("нет шейдера " + planned.Target);
                        continue;
                    }
                    // По одному: материал сохраняется до записи userData в .meta (реимпорт перечитывает ассет с диска).
                    Convert(material, action.AssetPath, planned, target);
                    action.MarkApplied();
                }
            }
            GameLog.Perf.Info("[BakedLitMaterialFixer] применено " + actions.Count(a => a.Applied) + " из " + actions.Count);
        }

        /// <summary>Вернуть исходные шейдеры материалам с данными отката в папках (по умолчанию — корни окружения).</summary>
        public static int Revert(params string[] folders)
        {
            string[] roots = (folders != null && folders.Length > 0 ? folders : EnvironmentRoots).Select(f => f.TrimEnd('/')).ToArray();
            int reverted = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Material", roots))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                AssetImporter importer = AssetImporter.GetAtPath(path);
                if (importer == null || string.IsNullOrEmpty(importer.userData) || !importer.userData.Contains(RevertMarker)) continue;
                if (IsOwnedElsewhere(System.IO.Path.GetFileNameWithoutExtension(path))) continue;
                RevertData data = JsonUtility.FromJson<RevertData>(importer.userData);
                if (!string.IsNullOrEmpty(data.missingShaderGuid))
                {
                    GameLog.Perf.Warning($"[BakedLitMaterialFixer] {path} не возвращён: исходный шейдер {data.missingShaderGuid} (HDRP) не установлен.");
                    continue;
                }
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                Shader shader = LoadShader(data.shaderName, data.shaderPath);
                if (material == null || shader == null)
                {
                    GameLog.Perf.Warning($"[BakedLitMaterialFixer] Не удалось вернуть {path}: шейдер {data.shaderName} не найден.");
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
            GameLog.Perf.Info($"[BakedLitMaterialFixer] Возвращено исходных шейдеров: {reverted}");
            return reverted;
        }

        private void BeginPlan()
        {
            _planned.Clear();
            _skipped.Clear();
        }

        private void AddPlanned(List<AuditFixAction> actions, string path, Material material, SourceKind kind, float metresPerUv, string skip)
        {
            var notes = new List<string>();
            if (skip == null)
                skip = PlanTarget(material, kind, notes, out string target) ?? Register(path, kind, target, metresPerUv);
            if (skip != null)
            {
                _skipped.Add(path + " — " + skip);
                return;
            }
            Planned planned = _planned[path];
            string after = (planned.Target == SimpleLitName ? "Simple Lit (эмиссия, без бликов)" : "Baked Lit") +
                           (notes.Count > 0 ? " [" + string.Join("; ", notes) + "]" : string.Empty);
            actions.Add(new AuditFixAction(path, material.shader.name, after));
        }

        private string Register(string path, SourceKind kind, string target, float metresPerUv)
        {
            _planned[path] = new Planned { Kind = kind, Target = target, MetresPerUv = metresPerUv };
            return null;
        }

        private static string CommonSkip(string path, Material material, out SourceKind kind)
        {
            kind = SourceKind.None;
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
                return "не ассет .mat (встроенный в модель — сначала извлечь материал)";
            if (material == null)
                return "материал не загружен";
            if (!EnvironmentRoots.Any(r => path.StartsWith(r, StringComparison.Ordinal)))
                return "вне корней окружения";
            if (IsOwnedElsewhere(material.name))
                return "другой писатель материала (QPM-SHADER-007)";
            kind = Classify(material);
            if (kind != SourceKind.None) return null;
            string shaderName = material.shader != null ? material.shader.name : "(нет)";
            if (shaderName == ErrorShaderName)
                return "шейдер с ошибкой, но в материале нет свойств HDRP Lit (_BaseColorMap) — исходник неизвестен";
            return shaderName == BakedLitName || shaderName == SimpleLitName ? "уже " + shaderName : "нет конвертера для шейдера " + shaderName;
        }

        /// <summary>Целевой шейдер; не null — причина пропуска.</summary>
        private static string PlanTarget(Material m, SourceKind kind, List<string> notes, out string target)
        {
            target = null;
            if (kind == SourceKind.UrpLit && (GetFloat(m, "_Surface") > 0.5f || m.IsKeywordEnabled("_SURFACE_TYPE_TRANSPARENT")))
                return "прозрачный";
            if (kind == SourceKind.HdrpLitMissing)
            {
                if (SavedFloat(m, "_SurfaceType") > 0.5f) return "прозрачный (HDRP _SurfaceType)";
                notes.Add("из HDRP Lit: шейдер HDRP не установлен");
                if (SavedTexture(m, "_NormalMap", out _, out _) != null) notes.Add("normal map отброшена");
            }
            if (kind == SourceKind.BasicTextured)
            {
                if (GetFloat(m, "_UseWorldAligned") > 0.5f) return "world-aligned (нужен триплан или box-UV)";
                if (GetFloat(m, "_UseOpacity") > 0.5f || GetFloat(m, "_OpacityFromORM") > 0.5f) return "прозрачность из отдельной карты";
                if (Math.Abs(GetFloat(m, "_Saturation", 1f) - 1f) > 0.01f || Math.Abs(GetFloat(m, "_Contrast", 1f) - 1f) > 0.01f)
                    notes.Add($"saturation={GetFloat(m, "_Saturation", 1f):F2} contrast={GetFloat(m, "_Contrast", 1f):F2} не переносятся");
            }
            if (kind == SourceKind.WorldAligned)
            {
                if (!m.HasProperty("_MainBC") || !m.HasProperty("_MainTile")) return "нет _MainBC/_MainTile";
                if (m.GetFloat("_MainTile") <= 0f) return "_MainTile <= 0";
                notes.Add("слои vertex paint, нормали и краевой шум не переносятся");
            }
            target = IsEmissive(m, kind) ? SimpleLitName : BakedLitName;
            if (IsCutout(m, kind)) notes.Add("alpha clip");
            string normal = kind == SourceKind.UrpLit ? "_BumpMap" : "_NormalMap";
            if (m.HasProperty(normal) && m.GetTexture(normal) != null) notes.Add("normal map отброшена");
            return null;
        }

        private static void Convert(Material m, string path, Planned planned, Shader target)
        {
            // Снять значения до смены шейдера.
            Texture baseMap = m.HasProperty("_BaseMap") ? m.GetTexture("_BaseMap") : null;
            Vector2 scale = m.HasProperty("_BaseMap") ? m.GetTextureScale("_BaseMap") : Vector2.one;
            Vector2 offset = m.HasProperty("_BaseMap") ? m.GetTextureOffset("_BaseMap") : Vector2.zero;
            Color color = Color.white;
            float cutoff = 0.5f;
            bool cutout = IsCutout(m, planned.Kind);
            float cull = m.HasProperty("_Cull") ? m.GetFloat("_Cull") : 2f;
            // Alpha-to-coverage при MSAA 4 сохраняет сглаженный край хвои и сетки (как в исходных материалах пакета).
            bool alphaToMask = m.IsKeywordEnabled("_ALPHATOMASK_ON") || GetFloat(m, "_AlphaToMask") > 0.5f;
            bool emissive = IsEmissive(m, planned.Kind);
            Texture emissionMap = null;
            Color emissionColor = Color.black;
            switch (planned.Kind)
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
                    if (m.HasProperty("_EmissiveMap")) emissionMap = m.GetTexture("_EmissiveMap");
                    if (m.HasProperty("_EmissiveColor")) emissionColor = m.GetColor("_EmissiveColor");
                    break;
                case SourceKind.UrpLit:
                    if (m.HasProperty("_BaseColor")) color = m.GetColor("_BaseColor");
                    if (m.HasProperty("_Cutoff")) cutoff = m.GetFloat("_Cutoff");
                    if (m.HasProperty("_EmissionMap")) emissionMap = m.GetTexture("_EmissionMap");
                    if (m.HasProperty("_EmissionColor")) emissionColor = m.GetColor("_EmissionColor");
                    break;
                case SourceKind.WorldAligned:
                    // Повтор текстуры сохраняется: тайлинг = метры на единицу UV / _MainTile (для box-UV UV уже в метрах).
                    baseMap = m.GetTexture("_MainBC");
                    float repeat = planned.MetresPerUv / m.GetFloat("_MainTile");
                    scale = new Vector2(repeat, repeat);
                    offset = Vector2.zero;
                    if (m.HasProperty("_MainTint")) color = m.GetColor("_MainTint");
                    break;
                case SourceKind.HdrpLitMissing:
                    // У шейдера с ошибкой нет свойств: значения — из сохранённых свойств HDRP Lit.
                    baseMap = SavedTexture(m, "_BaseColorMap", out scale, out offset);
                    color = SavedColor(m, "_BaseColor", Color.white);
                    cutoff = SavedFloat(m, "_AlphaCutoff", 0.5f);
                    cull = SavedFloat(m, "_DoubleSidedEnable") > 0.5f ? 0f : 2f;
                    emissionMap = SavedTexture(m, "_EmissiveColorMap", out _, out _);
                    emissionColor = SavedColor(m, "_EmissiveColor", Color.black);
                    break;
            }

            RevertData revert = CaptureRevert(m, cull);
            if (planned.Kind == SourceKind.HdrpLitMissing)
                revert.missingShaderGuid = SerializedShaderGuid(path) ?? "?";

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
                m.renderQueue = (int)RenderQueue.AlphaTest;
            }
            else
            {
                m.SetOverrideTag("RenderType", "Opaque");
                m.renderQueue = (int)RenderQueue.Geometry;
            }
            m.EnableKeyword("_RECEIVE_SHADOWS_OFF");
            if (planned.Target == SimpleLitName && emissive)
            {
                // SimpleLitGUI.SpecularSource: 0 — блики (текстура/цвет), 1 — NoSpecular (QPM-SHADER-005).
                m.SetFloat("_SpecularHighlights", 1f);
                m.DisableKeyword("_SPECULAR_COLOR");
                m.DisableKeyword("_SPECGLOSSMAP");
                m.SetTexture("_EmissionMap", emissionMap);
                m.SetColor("_EmissionColor", emissionColor);
                m.EnableKeyword("_EMISSION");
                // AnyEmissive обязателен: с None/EmissiveIsBlack URP выключит _EMISSION при импорте. RealtimeEmissive, а не
                // BakedEmissive: свет запекают настоящие источники, эмиссия не должна второй раз идти в лайтмапы (QPM-SHADER-006).
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }

            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
            AssetImporter importer = AssetImporter.GetAtPath(path);
            // Повторный перевод не затирает данные отката исходного шейдера.
            if (string.IsNullOrEmpty(importer.userData) || !importer.userData.Contains(RevertMarker))
                importer.userData = JsonUtility.ToJson(revert);
            importer.SaveAndReimport();
        }

        private static RevertData CaptureRevert(Material m, float cull) => new RevertData
        {
            marker = RevertMarker,
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

        private static bool IsCutout(Material m, SourceKind kind) =>
            kind == SourceKind.Foliage || m.IsKeywordEnabled("_ALPHATEST_ON") ||
            (kind == SourceKind.HdrpLitMissing && SavedFloat(m, "_AlphaCutoffEnable") > 0.5f);

        // Эмиссия с чёрным цветом (OutsideGravel, CheckpointRoad) ничего не даёт — такой материал идёт в Baked Lit.
        private static bool IsEmissive(Material m, SourceKind kind)
        {
            switch (kind)
            {
                case SourceKind.BasicTextured:
                    return GetFloat(m, "_UseEmissive") > 0.5f && m.HasProperty("_EmissiveColor") && m.GetColor("_EmissiveColor").maxColorComponent > 0.001f;
                case SourceKind.UrpLit:
                    return m.IsKeywordEnabled("_EMISSION") && m.HasProperty("_EmissionColor") && m.GetColor("_EmissionColor").maxColorComponent > 0.001f;
                case SourceKind.HdrpLitMissing:
                    return SavedColor(m, "_EmissiveColor", Color.black).maxColorComponent > 0.001f;
                default:
                    return false;
            }
        }

        private static SourceKind Classify(Material material)
        {
            Shader shader = material.shader;
            // Отсутствующий шейдер Unity подменяет на InternalErrorShader; HDRP Lit узнаётся по сохранённой _BaseColorMap.
            if ((shader == null || shader.name == ErrorShaderName) && HasSaved(material, "m_TexEnvs", "_BaseColorMap"))
                return SourceKind.HdrpLitMissing;
            if (shader == null) return SourceKind.None;
            switch (shader.name)
            {
                case "Shader Graphs/S_BasicTextured": return SourceKind.BasicTextured;
                case "Shader Graphs/S_Foliage": return SourceKind.Foliage;
                case "Universal Render Pipeline/Lit": return SourceKind.UrpLit;
                case "Shader Graphs/S_VPWorldAligned": return SourceKind.WorldAligned;
                default: return SourceKind.None;
            }
        }

        private static bool IsOwnedElsewhere(string materialName) =>
            OwnedElsewhere.Any(p => materialName.StartsWith(p, StringComparison.Ordinal));

        /// <summary>Материал → включённые рендереры открытых сцен, которые его рисуют.</summary>
        private static Dictionary<Material, List<Renderer>> SceneUsers()
        {
            var users = new Dictionary<Material, List<Renderer>>();
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
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
            }
            return users;
        }

        private static bool IsDynamicUser(Renderer r)
        {
            GameObject go = r.gameObject;
            if (!go.isStatic) return true;
            if (DynamicRoots.Contains(go.transform.root.name)) return true;
            return r.GetComponentInParent<Rigidbody>() != null;
        }

        private static string Describe(Renderer r)
        {
            string path = r.name;
            Transform t = r.transform.parent;
            for (int i = 0; t != null && i < 4; i++, t = t.parent) path = t.name + "/" + path;
            return path;
        }

        /// <summary>Материалы, на которые ссылаются неоткрытые сцены сборки и префабы вне корней окружения.</summary>
        private static HashSet<string> ForeignDependencies()
        {
            var open = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < SceneManager.sceneCount; i++) open.Add(SceneManager.GetSceneAt(i).path);
            var roots = new List<string>();
            roots.AddRange(EditorBuildSettings.scenes.Where(s => s.enabled && !open.Contains(s.path)).Select(s => s.path));
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets" }))
            {
                string p = AssetDatabase.GUIDToAssetPath(guid);
                if (EnvironmentRoots.Any(f => p.StartsWith(f, StringComparison.Ordinal))) continue;
                if (p.StartsWith("Assets/ThirdParty/", StringComparison.Ordinal)) continue;
                roots.Add(p);
            }
            return new HashSet<string>(AssetDatabase.GetDependencies(roots.ToArray(), true)
                .Where(p => p.EndsWith(".mat", StringComparison.OrdinalIgnoreCase)), StringComparer.Ordinal);
        }

        private static Shader LoadShader(string name, string path)
        {
            if (!string.IsNullOrEmpty(path) && path.StartsWith("Assets/", StringComparison.Ordinal))
            {
                Shader s = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Shader>().FirstOrDefault();
                if (s != null) return s;
            }
            return Shader.Find(name);
        }

        private static float GetFloat(Material m, string name, float fallback = 0f) => m.HasProperty(name) ? m.GetFloat(name) : fallback;

        // Сохранённые свойства (m_SavedProperties) переживают смену и потерю шейдера — по ним читается HDRP Lit.
        private static SerializedProperty FindSaved(SerializedObject serialized, string array, string name)
        {
            SerializedProperty entries = serialized.FindProperty("m_SavedProperties." + array);
            for (int i = 0; entries != null && i < entries.arraySize; i++)
            {
                SerializedProperty entry = entries.GetArrayElementAtIndex(i);
                if (entry.FindPropertyRelative("first")?.stringValue == name)
                    return entry.FindPropertyRelative("second");
            }
            return null;
        }

        private static bool HasSaved(Material m, string array, string name) => FindSaved(new SerializedObject(m), array, name) != null;

        private static float SavedFloat(Material m, string name, float fallback = 0f) =>
            FindSaved(new SerializedObject(m), "m_Floats", name)?.floatValue ?? fallback;

        private static Color SavedColor(Material m, string name, Color fallback) =>
            FindSaved(new SerializedObject(m), "m_Colors", name)?.colorValue ?? fallback;

        private static Texture SavedTexture(Material m, string name, out Vector2 scale, out Vector2 offset)
        {
            SerializedProperty env = FindSaved(new SerializedObject(m), "m_TexEnvs", name);
            scale = env?.FindPropertyRelative("m_Scale").vector2Value ?? Vector2.one;
            offset = env?.FindPropertyRelative("m_Offset").vector2Value ?? Vector2.zero;
            return env?.FindPropertyRelative("m_Texture").objectReferenceValue as Texture;
        }

        /// <summary>GUID шейдера из текста .mat: ссылку на отсутствующий шейдер API не отдаёт.</summary>
        private static string SerializedShaderGuid(string path)
        {
            string text = System.IO.File.ReadAllText(path);
            System.Text.RegularExpressions.Match match =
                System.Text.RegularExpressions.Regex.Match(text, @"m_Shader: \{fileID: -?\d+, guid: ([0-9a-f]{32})");
            return match.Success ? match.Groups[1].Value : null;
        }
    }
}
