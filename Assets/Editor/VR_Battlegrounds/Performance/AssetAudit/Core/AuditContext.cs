using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.Performance.AssetAudit
{
    /// <summary>
    /// Общий контекст прогона для правил: приём находок, охват, общие кэши анализа.
    /// Правила только читают ассеты; всё, что они пишут, — находки.
    /// </summary>
    public sealed class AuditContext
    {
        private readonly List<AuditFinding> _findings = new List<AuditFinding>();
        private readonly Dictionary<Shader, List<string>> _materialsByShader = new Dictionary<Shader, List<string>>();
        private readonly ShaderGraphAnalyzer _shaderGraphs = new ShaderGraphAnalyzer();

        internal AuditContext(AuditRequest request) => Request = request;

        public AuditRequest Request { get; }
        public AuditScopeKind ScopeKind => Request.Kind;
        public bool IsSceneScope => Request.Kind == AuditScopeKind.Scene;

        /// <summary>Путь проверяемой сцены (только в охвате «сцена»).</summary>
        public string ScenePath { get; internal set; }

        internal IReadOnlyList<AuditFinding> RawFindings => _findings;

        /// <summary>Зарегистрировать находку. Серьёзность по умолчанию — <see cref="AuditRule.DefaultSeverity"/>.</summary>
        public AuditFinding Report(AuditRule rule, string asset, string message, AuditSeverity? severity = null)
        {
            var finding = new AuditFinding(rule, severity ?? rule.DefaultSeverity, asset, message);
            _findings.Add(finding);
            return finding;
        }

        /// <summary>Материалы охвата на этом шейдере (пути; заполнено до проверок шейдеров).</summary>
        public IReadOnlyList<string> MaterialsUsing(Shader shader) =>
            shader != null && _materialsByShader.TryGetValue(shader, out List<string> list) ? list : (IReadOnlyList<string>)System.Array.Empty<string>();

        /// <summary>Разбор Shader Graph (кэш на прогон). null — шейдер не из Shader Graph.</summary>
        public ShaderGraphInfo GetShaderGraphInfo(Shader shader)
        {
            string path = shader != null ? AssetDatabase.GetAssetPath(shader) : null;
            return _shaderGraphs.Analyze(path);
        }

        public ShaderGraphInfo GetShaderGraphInfo(string shaderGraphPath) => _shaderGraphs.Analyze(shaderGraphPath);

        internal void RegisterMaterialUse(Material material, string assetPath)
        {
            if (material == null || material.shader == null)
                return;
            if (!_materialsByShader.TryGetValue(material.shader, out List<string> list))
                _materialsByShader[material.shader] = list = new List<string>();
            string label = AssetDatabase.IsSubAsset(material) ? assetPath + "#" + material.name : assetPath;
            if (!list.Contains(label))
                list.Add(label);
        }
    }
}
