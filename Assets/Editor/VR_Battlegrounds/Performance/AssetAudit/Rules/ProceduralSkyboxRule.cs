using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VrBattlegrounds.Editor.Performance.AssetAudit.Rules
{
    /// <summary>
    /// Скайбокс сцены на дорогом шейдере (Skybox/Procedural). Материал читается из блока RenderSettings файла сцены,
    /// чтобы не делать сцену активной; встроенный Default-Skybox — тоже Procedural.
    /// </summary>
    public sealed class ProceduralSkyboxRule : AuditRule, ISceneAuditCheck
    {
        private const string BuiltinExtraGuid = "0000000000000000f000000000000000";
        private const long DefaultSkyboxFileId = 10304;

        private static readonly Regex SkyboxReference = new Regex(
            @"RenderSettings:[\s\S]*?m_SkyboxMaterial:\s*\{fileID:\s*(-?\d+)(?:,\s*guid:\s*([0-9a-f]{32}))?", RegexOptions.Compiled);

        [AuditThreshold("Шейдеры скайбокса, считающиеся дорогими")]
        public string[] HeavySkyboxShaders = { "Skybox/Procedural" };

        public override string Id => "scene.procedural-skybox";
        public override string Title => "Процедурный скайбокс";
        public override string Category => "scene";
        public override AuditSeverity DefaultSeverity => AuditSeverity.Warning;

        public override string WhyBadOnQuest =>
            "Skybox/Procedural считает атмосферное рассеяние на каждый пиксель неба — а небо в VR занимает большую часть обоих глаз. " +
            "Статичному небу хватает цвета, градиента или кубмапы на Unlit.";

        public override string Evidence => AuditEvidence.LobbyQuest3PerDraw + " Skybox/Procedural — 8,3 % кадра одним draw.";

        public override string FixHint =>
            "Сплошной цвет фона камеры, градиент или запечённая кубмапа (Skybox/Cubemap) — снять процедурное небо в кубмапу один раз.";

        public void CheckScene(Scene scene, string scenePath, AuditContext context)
        {
            string shaderName = ReadSkyboxShader(scenePath, out string materialLabel);
            if (shaderName == null && scene.IsValid() && SceneManager.GetActiveScene() == scene && RenderSettings.skybox != null)
            {
                shaderName = RenderSettings.skybox.shader != null ? RenderSettings.skybox.shader.name : null;
                materialLabel = AuditAssetUtil.AssetLabel(RenderSettings.skybox);
            }
            if (shaderName == null || Array.IndexOf(HeavySkyboxShaders, shaderName) < 0)
                return;
            context.Report(this, materialLabel, "скайбокс сцены на " + shaderName)
                .WithItem(scenePath)
                .WithMetric("shader", shaderName);
        }

        private static string ReadSkyboxShader(string scenePath, out string materialLabel)
        {
            materialLabel = null;
            string fullPath = Path.GetFullPath(scenePath);
            if (!File.Exists(fullPath))
                return null;
            Match match = SkyboxReference.Match(File.ReadAllText(fullPath));
            if (!match.Success || !long.TryParse(match.Groups[1].Value, out long fileId) || fileId == 0)
                return null;
            string guid = match.Groups[2].Success ? match.Groups[2].Value : null;
            if (guid == BuiltinExtraGuid && fileId == DefaultSkyboxFileId)
            {
                materialLabel = "Default-Skybox (встроенный)";
                return "Skybox/Procedural";
            }
            string path = string.IsNullOrEmpty(guid) ? null : AssetDatabase.GUIDToAssetPath(guid);
            var material = string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null || material.shader == null)
                return null;
            materialLabel = path;
            return material.shader.name;
        }
    }
}
