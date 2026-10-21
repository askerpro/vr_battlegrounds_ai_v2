using System.Collections.Generic;
using UnityEditor;
using VrBattlegrounds.Core;
using VrBattlegrounds.Editor.Performance.AssetAudit.Rules;

namespace VrBattlegrounds.Editor.Performance.AssetAudit.Fixers
{
    /// <summary>
    /// Фиксер правила <c>texture.android-format</c>: Android override ASTC 6x6 (решение пользователя 2026-10-20 —
    /// единый формат проекта, как у 318 текстур с override). Max size Android сохраняется; Default и прочие платформы
    /// не трогаются. UI-текстуры (Sprite/GUI/Cursor) пропускаются: несжатость там бывает намеренной.
    /// </summary>
    public sealed class AndroidAstcFixer : AuditFixer
    {
        public const TextureImporterFormat TargetFormat = TextureImporterFormat.ASTC_6x6;

        public override string RuleId => "texture.android-format";
        public override string Title => "Android → ASTC 6x6";
        public override string WhatChanges =>
            "Импорт текстуры: override для Android, формат ASTC 6x6, max size прежний. Default/Standalone не меняются.";

        public override List<AuditFixAction> Plan(IEnumerable<AuditFinding> findings)
        {
            var actions = new List<AuditFixAction>();
            var seen = new HashSet<string>();
            foreach (AuditFinding finding in findings)
            {
                string path = finding.Asset;
                if (!seen.Add(path) || !(AssetImporter.GetAtPath(path) is TextureImporter importer))
                    continue;
                if (!TextureRuleUtil.IsWorldTexture(importer))
                    continue;
                TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings(TextureRuleUtil.Android);
                TextureImporterFormat current = android.overridden ? android.format : importer.GetAutomaticFormat(TextureRuleUtil.Android);
                if (current == TargetFormat)
                    continue;
                int maxSize = MaxSize(importer, android);
                actions.Add(new AuditFixAction(path,
                    current + (android.overridden ? " (override)" : " (авто)") + ", max " + maxSize,
                    TargetFormat + " (override), max " + maxSize));
            }
            return actions;
        }

        public override void Apply(IReadOnlyList<AuditFixAction> actions)
        {
            foreach (AuditFixAction action in actions)
            {
                if (!(AssetImporter.GetAtPath(action.AssetPath) is TextureImporter importer))
                {
                    action.MarkFailed("нет TextureImporter");
                    continue;
                }
                TextureImporterPlatformSettings android = importer.GetPlatformTextureSettings(TextureRuleUtil.Android);
                int maxSize = MaxSize(importer, android);
                android.overridden = true;
                android.format = TargetFormat;
                android.maxTextureSize = maxSize;
                android.textureCompression = TextureImporterCompression.Compressed;
                importer.SetPlatformTextureSettings(android);
                importer.SaveAndReimport();

                TextureImporterPlatformSettings check = importer.GetPlatformTextureSettings(TextureRuleUtil.Android);
                if (check.overridden && check.format == TargetFormat)
                    action.MarkApplied();
                else
                    action.MarkFailed("после импорта формат " + check.format);
            }
            int applied = 0;
            foreach (AuditFixAction action in actions)
                applied += action.Applied ? 1 : 0;
            GameLog.Perf.Info("[AndroidAstcFixer] применено " + applied + " из " + actions.Count);
        }

        private static int MaxSize(TextureImporter importer, TextureImporterPlatformSettings android) =>
            android.overridden ? android.maxTextureSize : importer.maxTextureSize;
    }
}
