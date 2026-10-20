using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;

namespace VrBattlegrounds.Editor.Performance.AssetAudit
{
    /// <summary>
    /// Статический разбор .shadergraph/.shadersubgraph (текст JSON) без компиляции: выборки текстур
    /// с учётом сабграфов, Branch, признаки трипланарной/world-aligned проекции. Это эвристика по узлам графа:
    /// число выборок — верхняя оценка (Branch по свойству считает обе ветви, и GPU платит за обе).
    /// </summary>
    public sealed class ShaderGraphInfo
    {
        public string Path;
        public string Name;
        /// <summary>Выборки текстур узлами самого графа (Triplanar = 3, POM = 8).</summary>
        public int DirectSamples;
        /// <summary>Выборки с учётом сабграфов: каждое использование сабграфа добавляет его выборки.</summary>
        public int TotalSamples;
        public int SubGraphUses;
        public int BranchNodes;
        public int CustomFunctionNodes;
        public bool HasTriplanarNode;
        /// <summary>Почему граф считается трипланарным/world-aligned; пусто — не считается.</summary>
        public readonly List<string> ProjectionReasons = new List<string>();

        public bool IsWorldProjected => ProjectionReasons.Count > 0;
    }

    public sealed class ShaderGraphAnalyzer
    {
        private static readonly Regex ObjectStart = new Regex(@"^\{", RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex NodeType = new Regex(@"""m_Type""\s*:\s*""UnityEditor\.ShaderGraph\.([A-Za-z0-9_]+)""", RegexOptions.Compiled);
        private static readonly Regex SubGraphGuid = new Regex(@"\\""guid\\""\s*:\s*\\""([0-9a-f]{32})\\""", RegexOptions.Compiled);
        private static readonly Regex Space = new Regex(@"""m_Space""\s*:\s*(\d+)", RegexOptions.Compiled);

        /// <summary>Цена узла в выборках текстур. POM — условно 8 (шагов больше, но это порядок величины).</summary>
        private static readonly Dictionary<string, int> SampleNodeCost = new Dictionary<string, int>
        {
            { "SampleTexture2DNode", 1 },
            { "SampleTexture2DLODNode", 1 },
            { "SampleTexture2DArrayNode", 1 },
            { "SampleTexture3DNode", 1 },
            { "SampleCubemapNode", 1 },
            { "SampleReflectedCubemapNode", 1 },
            { "SampleRawCubemapNode", 1 },
            { "SampleVirtualTextureNode", 1 },
            { "ParallaxMappingNode", 1 },
            { "ParallaxOcclusionMappingNode", 8 },
            { "TriplanarNode", 3 },
        };

        // CoordinateSpace Shader Graph: Object 0, View 1, World 2, Tangent 3, AbsoluteWorld 4.
        private const int SpaceWorld = 2;
        private const int SpaceAbsoluteWorld = 4;

        private readonly Dictionary<string, ShaderGraphInfo> _cache = new Dictionary<string, ShaderGraphInfo>();
        private readonly HashSet<string> _inProgress = new HashSet<string>();

        public static bool IsShaderGraphPath(string path) =>
            !string.IsNullOrEmpty(path) &&
            (path.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase) ||
             path.EndsWith(".shadersubgraph", StringComparison.OrdinalIgnoreCase));

        public ShaderGraphInfo Analyze(string path)
        {
            if (!IsShaderGraphPath(path))
                return null;
            if (_cache.TryGetValue(path, out ShaderGraphInfo cached))
                return cached;
            if (!_inProgress.Add(path))
                return null; // цикл сабграфов: второй заход не считаем
            try
            {
                ShaderGraphInfo info = Parse(path);
                _cache[path] = info;
                return info;
            }
            finally
            {
                _inProgress.Remove(path);
            }
        }

        private ShaderGraphInfo Parse(string path)
        {
            var info = new ShaderGraphInfo { Path = path, Name = System.IO.Path.GetFileNameWithoutExtension(path) };
            string fullPath = System.IO.Path.GetFullPath(path);
            if (!File.Exists(fullPath))
                return info;

            string text = File.ReadAllText(fullPath);
            bool worldPosition = false;
            bool normalVector = false;
            int subGraphSamples = 0;

            MatchCollection starts = ObjectStart.Matches(text);
            for (int i = 0; i < starts.Count; i++)
            {
                int from = starts[i].Index;
                int to = i + 1 < starts.Count ? starts[i + 1].Index : text.Length;
                string chunk = text.Substring(from, to - from);
                Match type = NodeType.Match(chunk);
                if (!type.Success)
                    continue;

                string nodeType = type.Groups[1].Value;
                if (SampleNodeCost.TryGetValue(nodeType, out int cost))
                {
                    info.DirectSamples += cost;
                    if (nodeType == "TriplanarNode")
                        info.HasTriplanarNode = true;
                    continue;
                }

                switch (nodeType)
                {
                    case "SubGraphNode":
                        info.SubGraphUses++;
                        subGraphSamples += AnalyzeSubGraph(chunk, info);
                        break;
                    case "PositionNode":
                        Match space = Space.Match(chunk);
                        if (space.Success && int.TryParse(space.Groups[1].Value, out int value) &&
                            (value == SpaceWorld || value == SpaceAbsoluteWorld))
                            worldPosition = true;
                        break;
                    case "NormalVectorNode":
                        normalVector = true;
                        break;
                    case "BranchNode":
                        info.BranchNodes++;
                        break;
                    case "CustomFunctionNode":
                        info.CustomFunctionNodes++;
                        break;
                }
            }

            info.TotalSamples = info.DirectSamples + subGraphSamples;

            if (info.HasTriplanarNode)
                info.ProjectionReasons.Add("узел Triplanar");
            if (worldPosition && info.TotalSamples > 0)
                info.ProjectionReasons.Add("Position (World) рядом с выборками текстур");
            if (normalVector && info.DirectSamples >= 3)
                info.ProjectionReasons.Add("Normal Vector + " + info.DirectSamples + " выборки (шаблон трипланара)");
            if (NameLooksProjected(info.Name))
                info.ProjectionReasons.Add("имя «" + info.Name + "»");
            return info;
        }

        private int AnalyzeSubGraph(string chunk, ShaderGraphInfo parent)
        {
            Match guid = SubGraphGuid.Match(chunk);
            if (!guid.Success)
                return 0;
            string subPath = AssetDatabase.GUIDToAssetPath(guid.Groups[1].Value);
            ShaderGraphInfo sub = Analyze(subPath);
            if (sub == null)
                return 0;
            parent.BranchNodes += sub.BranchNodes;
            parent.CustomFunctionNodes += sub.CustomFunctionNodes;
            if (sub.IsWorldProjected)
            {
                string reason = "сабграф " + sub.Name + " (" + string.Join("; ", sub.ProjectionReasons) + ")";
                if (!parent.ProjectionReasons.Contains(reason))
                    parent.ProjectionReasons.Add(reason);
            }
            return sub.TotalSamples;
        }

        private static bool NameLooksProjected(string name) =>
            name.IndexOf("Triplanar", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("WorldAligned", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("WorldSpace", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
