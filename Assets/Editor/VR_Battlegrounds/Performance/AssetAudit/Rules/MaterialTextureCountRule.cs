using System.Collections.Generic;
using UnityEngine;

namespace VrBattlegrounds.Editor.Performance.AssetAudit.Rules
{
    /// <summary>Больше N разных текстур на материале (только свойства, которые есть у его шейдера).</summary>
    public sealed class MaterialTextureCountRule : AuditRule, IMaterialAuditCheck
    {
        [AuditThreshold("Текстур на материал, выше — «Предупреждение»")]
        public int WarningTextures = 3;

        [AuditThreshold("Текстур на материал, выше — «Критично»")]
        public int CriticalTextures = 6;

        public override string Id => "material.texture-count";
        public override string Title => "Много текстур на материал";
        public override string Category => "material";
        public override AuditSeverity DefaultSeverity => AuditSeverity.Warning;

        public override string WhyBadOnQuest =>
            "Каждая текстура материала — как минимум одна выборка на пиксель и место в текстурном кэше; на тайловом GPU " +
            "это прямой рост времени фрагмента и столлов выборки. Для декора на Quest хватает 1–3 текстур (цвет, нормаль, маска).";

        public override string Evidence =>
            AuditEvidence.LobbyQuest3PerDraw + " Скалы (S_BasicTextured): столл текстур 40–55 %; пол S_VPWorldAligned — 3 слоя × (цвет, нормаль, маска).";

        public override string FixHint =>
            "Упаковать маски в каналы одной текстуры (ORM), убрать детальные карты и нормали вдали, запечь слои в одну текстуру.";

        public void CheckMaterial(Material material, string assetPath, AuditContext context)
        {
            Shader shader = material.shader;
            if (shader == null)
                return;
            var textures = new HashSet<Texture>();
            var names = new List<string>();
            foreach (string property in material.GetTexturePropertyNames())
            {
                if (shader.FindPropertyIndex(property) < 0)
                    continue; // хвост от прежнего шейдера: не сэмплируется
                Texture texture = material.GetTexture(property);
                if (texture != null && textures.Add(texture))
                    names.Add(property);
            }
            if (textures.Count <= WarningTextures)
                return;

            AuditSeverity severity = textures.Count > CriticalTextures ? AuditSeverity.Critical : DefaultSeverity;
            context.Report(this, AuditAssetUtil.AssetLabel(material), textures.Count + " текстур на материал (" + shader.name + ")", severity)
                .WithMetric("textures", textures.Count)
                .WithMetric("properties", string.Join(", ", names));
        }
    }
}
