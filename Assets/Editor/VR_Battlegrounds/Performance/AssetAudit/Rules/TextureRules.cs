using System;
using UnityEditor;
using UnityEngine;

namespace VrBattlegrounds.Editor.Performance.AssetAudit.Rules
{
    /// <summary>Общее для текстурных правил: какие текстуры живут в 3D-мире (UI, спрайты, курсоры — нет).</summary>
    internal static class TextureRuleUtil
    {
        public const string Android = "Android";

        public static bool IsWorldTexture(TextureImporter importer) =>
            importer.textureType != TextureImporterType.Sprite &&
            importer.textureType != TextureImporterType.GUI &&
            importer.textureType != TextureImporterType.Cursor;

        /// <summary>Размер, который получит сборка Android: min(исходник, max size платформы).</summary>
        public static int EffectiveAndroidSize(TextureImporter importer, out int sourceSize, out int maxSize)
        {
            importer.GetSourceTextureWidthAndHeight(out int width, out int height);
            sourceSize = Mathf.Max(width, height);
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings(Android);
            maxSize = android.overridden ? android.maxTextureSize : importer.maxTextureSize;
            return Mathf.Min(sourceSize, maxSize);
        }
    }

    /// <summary>Текстура мира без mip-уровней.</summary>
    public sealed class TextureNoMipmapsRule : AuditRule, ITextureAuditCheck
    {
        [AuditThreshold("Не сообщать о текстурах меньше этого размера (px)")]
        public int MinSize = 64;

        public override string Id => "texture.no-mipmaps";
        public override string Title => "Текстура без mip-уровней";
        public override string Category => "texture";
        public override AuditSeverity DefaultSeverity => AuditSeverity.Warning;

        public override string WhyBadOnQuest =>
            "Без mip вдали каждая выборка читает полноразмерную текстуру вразброс: промахи текстурного кэша, рост трафика памяти " +
            "и мерцание (алиасинг) в шлеме. Мобильный GPU особенно чувствителен к пропускной способности памяти.";

        public override string Evidence =>
            AuditEvidence.PerformanceExpertTricks + " Столл выборки текстур 40–55 % на скалах Lobby: " + AuditEvidence.LobbyQuest3PerDraw;

        public override string FixHint => "Включить Generate Mip Maps в импорте (для 3D-мира — всегда).";

        public void CheckTexture(TextureImporter importer, Texture texture, string assetPath, AuditContext context)
        {
            if (!TextureRuleUtil.IsWorldTexture(importer) || importer.mipmapEnabled)
                return;
            int size = TextureRuleUtil.EffectiveAndroidSize(importer, out _, out _);
            if (size < MinSize)
                return;
            context.Report(this, assetPath, "нет mip-уровней (" + size + " px, " + importer.textureType + ")")
                .WithMetric("size", size)
                .WithMetric("type", importer.textureType.ToString());
        }
    }

    /// <summary>Текстура, которая на Android соберётся не в ASTC.</summary>
    public sealed class TextureAndroidFormatRule : AuditRule, ITextureAuditCheck
    {
        [AuditThreshold("Несжатая текстура больше этого размера (px) — «Критично»")]
        public int CriticalUncompressedSize = 256;

        public override string Id => "texture.android-format";
        public override string Title => "Текстура на Android не в ASTC";
        public override string Category => "texture";
        public override AuditSeverity DefaultSeverity => AuditSeverity.Warning;

        public override string WhyBadOnQuest =>
            "ASTC — родной формат Quest: малый объём и пропускная способность. Несжатые RGBA32/RGB24 в 4–8 раз тяжелее по памяти и трафику; " +
            "ETC2 хуже по качеству при том же объёме.";

        public override string Evidence => AuditEvidence.PerformanceExpertTricks + " Стандарт Meta для Quest — ASTC (Player Settings Android).";

        public override string FixHint => "Override for Android: формат ASTC (4x4 для нормалей/UI-крупных, 6x6–8x8 для декора).";

        public void CheckTexture(TextureImporter importer, Texture texture, string assetPath, AuditContext context)
        {
            TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings(TextureRuleUtil.Android);
            TextureImporterFormat format = android.overridden ? android.format : importer.GetAutomaticFormat(TextureRuleUtil.Android);
            string name = format.ToString();
            if (name.StartsWith("ASTC", StringComparison.Ordinal))
                return;

            int size = TextureRuleUtil.EffectiveAndroidSize(importer, out _, out _);
            bool uncompressed = IsUncompressed(format) || importer.textureCompression == TextureImporterCompression.Uncompressed;
            AuditSeverity severity = uncompressed
                ? size > CriticalUncompressedSize ? AuditSeverity.Critical : AuditSeverity.Warning
                : DefaultSeverity;
            if (!TextureRuleUtil.IsWorldTexture(importer) && severity == AuditSeverity.Warning)
                severity = AuditSeverity.Info;

            context.Report(this, assetPath, "Android: " + name + (android.overridden ? " (override)" : " (авто)") +
                                            (uncompressed ? ", без сжатия" : string.Empty) + ", " + size + " px", severity)
                .WithMetric("format", name)
                .WithMetric("overridden", android.overridden)
                .WithMetric("size", size);
        }

        private static bool IsUncompressed(TextureImporterFormat format)
        {
            switch (format)
            {
                case TextureImporterFormat.RGBA32:
                case TextureImporterFormat.ARGB32:
                case TextureImporterFormat.RGB24:
                case TextureImporterFormat.RGBA16:
                case TextureImporterFormat.ARGB16:
                case TextureImporterFormat.RGB16:
                case TextureImporterFormat.RGBAHalf:
                case TextureImporterFormat.RGBAFloat:
                    return true;
                default:
                    return false;
            }
        }
    }

    /// <summary>Избыточный размер текстуры в сборке Android.</summary>
    public sealed class TextureSizeRule : AuditRule, ITextureAuditCheck
    {
        [AuditThreshold("Размер (px) в сборке Android от этого — «Инфо»")]
        public int InfoSize = 2048;

        [AuditThreshold("Размер (px) в сборке Android от этого — «Предупреждение»")]
        public int WarningSize = 4096;

        public override string Id => "texture.size";
        public override string Title => "Избыточный размер текстуры";
        public override string Category => "texture";
        public override AuditSeverity DefaultSeverity => AuditSeverity.Warning;

        public override string WhyBadOnQuest =>
            "ПК-паки несут 2K–4K текстуры: на Quest это память (общая с CPU), время загрузки и размер APK; ближние mip " +
            "крупных текстур на большой площади давят на текстурный кэш. Разрешение декора выбирают по видимому размеру в шлеме.";

        public override string Evidence =>
            "Пак MilitaryCamp — ПК/HDRP (решение пользователя 2026-10-20: ПК-паки адаптировать инструментами). " + AuditEvidence.PerformanceExpertTricks;

        public override string FixHint =>
            "Max Size override для Android: декор 512–1024, крупные поверхности 1024–2048 с тайлингом; полосы mip limit по дистанции (scene-detail).";

        public void CheckTexture(TextureImporter importer, Texture texture, string assetPath, AuditContext context)
        {
            if (!TextureRuleUtil.IsWorldTexture(importer))
                return;
            int size = TextureRuleUtil.EffectiveAndroidSize(importer, out int source, out int max);
            if (size < InfoSize)
                return;
            AuditSeverity severity = size >= WarningSize ? AuditSeverity.Warning : AuditSeverity.Info;
            context.Report(this, assetPath, "Android: " + size + " px (исходник " + source + ", max size " + max + ")", severity)
                .WithGroup("size:" + assetPath)
                .WithMetric("size", size)
                .WithMetric("source", source)
                .WithMetric("maxSize", max);
        }
    }
}
