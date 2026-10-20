using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.Performance.AssetAudit.Rules
{
    /// <summary>
    /// Планарное отражение (UxrPlanarReflectionUrp) с большим разрешением, без ограничения дальности или по всем слоям.
    /// Поля читаются через SerializedObject по имени: сборка правила не зависит от UltimateXR.
    /// </summary>
    public sealed class PlanarReflectionRule : AuditRule, IGameObjectAuditCheck
    {
        [AuditThreshold("Имена типов компонентов планарного отражения")]
        public string[] ComponentTypes = { "UxrPlanarReflectionUrp" };

        [AuditThreshold("Сторона текстуры отражения (px), выше — «Предупреждение»")]
        public int WarningTextureSize = 512;

        [AuditThreshold("Сторона текстуры отражения (px), от этого — «Критично»")]
        public int CriticalTextureSize = 1024;

        [AuditThreshold("Дальность отражения (м), выше или 0 (без ограничения) — «Предупреждение»")]
        public float WarningMaxDistance = 10f;

        public override string Id => "reflection.planar";
        public override string Title => "Дорогое планарное отражение";
        public override string Category => "scene";
        public override AuditSeverity DefaultSeverity => AuditSeverity.Warning;

        public override string WhyBadOnQuest =>
            "Зеркало перерисовывает сцену ещё раз для каждого глаза в свою текстуру (2 × размер²): это дополнительные проходы " +
            "геометрии и пикселей. Чем больше разрешение, дальность и число слоёв, тем дороже — при том что зеркало занимает малую часть экрана.";

        public override string Evidence => AuditEvidence.LobbyQuest3PerDraw + " Зеркало Lobby 2 × 768² ≈ 0,64 мс GPU.";

        public override string FixHint =>
            "Текстура 256–512, _maxReflectionDistance 3–6 м (ближний план и игроки), слои — только игроки и ближний декор; " +
            "рисовать только когда зеркало видно.";

        public void CheckGameObject(GameObject gameObject, AuditObjectSource source, AuditContext context)
        {
            foreach (MonoBehaviour component in gameObject.GetComponents<MonoBehaviour>())
            {
                if (component == null || System.Array.IndexOf(ComponentTypes, component.GetType().Name) < 0)
                    continue;
                if (source.InScene && !component.enabled)
                    continue;
                Check(component, source, context);
            }
        }

        private void Check(MonoBehaviour component, AuditObjectSource source, AuditContext context)
        {
            var serialized = new SerializedObject(component);
            int size = serialized.FindProperty("_textureSize")?.intValue ?? 0;
            float distance = serialized.FindProperty("_maxReflectionDistance")?.floatValue ?? 0f;
            int layers = serialized.FindProperty("_reflectLayers")?.intValue ?? -1;

            var reasons = new List<string>();
            AuditSeverity severity = AuditSeverity.Info;
            if (size >= CriticalTextureSize)
                severity = AuditSeverity.Critical;
            else if (size > WarningTextureSize)
                severity = AuditSeverity.Warning;
            if (size > WarningTextureSize)
                reasons.Add("текстура 2 × " + size + "²");
            if (distance <= 0f || distance > WarningMaxDistance)
            {
                reasons.Add(distance <= 0f ? "дальность без ограничения" : "дальность " + distance.ToString("0.#") + " м");
                if (severity < AuditSeverity.Warning)
                    severity = AuditSeverity.Warning;
            }
            if (layers == -1)
            {
                reasons.Add("отражаются все слои");
                if (severity < AuditSeverity.Warning)
                    severity = AuditSeverity.Warning;
            }
            if (reasons.Count == 0)
                return;

            context.Report(this, source.AssetPath, string.Join(", ", reasons), severity)
                .WithGroup(source.GroupAsset + "|" + source.HierarchyPath)
                .WithItem(source.InScene ? source.HierarchyPath : source.AssetPath + ":" + source.HierarchyPath)
                .WithMetric("textureSize", size)
                .WithMetric("pixelsPerFrame", 2 * size * size)
                .WithMetric("maxReflectionDistance", distance)
                .WithMetric("reflectLayers", layers);
        }
    }
}
