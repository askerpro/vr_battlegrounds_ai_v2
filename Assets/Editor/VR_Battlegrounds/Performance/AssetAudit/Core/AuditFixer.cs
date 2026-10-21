using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace VrBattlegrounds.Editor.Performance.AssetAudit
{
    /// <summary>
    /// Фиксер — исправление находок одного правила (<see cref="RuleId"/>). Аудитор только читает; фиксер меняет
    /// ассеты и всегда в два шага: <see cref="Plan"/> (dry-run, ничего не пишет) → подтверждение → <see cref="Apply"/>.
    /// Новый фиксер — класс-наследник с конструктором без параметров; ядро и окно находят его через TypeCache.
    /// </summary>
    public abstract class AuditFixer
    {
        /// <summary>Id правила, находки которого фиксер исправляет.</summary>
        public abstract string RuleId { get; }

        /// <summary>Короткое название действия для кнопки и меню.</summary>
        public abstract string Title { get; }

        /// <summary>Что именно меняется в ассете — показывается перед подтверждением.</summary>
        public abstract string WhatChanges { get; }

        /// <summary>Dry-run: действия по находкам правила; ассеты не меняются.</summary>
        public abstract List<AuditFixAction> Plan(IEnumerable<AuditFinding> findings);

        /// <summary>Применить подтверждённые действия; результат — в самих действиях (Applied/Error).</summary>
        public abstract void Apply(IReadOnlyList<AuditFixAction> actions);
    }

    /// <summary>Одно изменение одного ассета: было → станет.</summary>
    public sealed class AuditFixAction
    {
        public AuditFixAction(string assetPath, string before, string after)
        {
            AssetPath = assetPath;
            Before = before;
            After = after;
        }

        public string AssetPath { get; }
        public string Before { get; }
        public string After { get; }
        public bool Applied { get; internal set; }
        public string Error { get; internal set; }

        public void MarkApplied() => Applied = true;
        public void MarkFailed(string error) => Error = error;

        public override string ToString() => AssetPath + ": " + Before + " → " + After +
                                             (Error != null ? " [ошибка: " + Error + "]" : Applied ? " [готово]" : string.Empty);
    }

    /// <summary>Поиск фиксеров и запуск «аудит правила → план» для папок (меню, окно, MCP).</summary>
    public static class AuditFixerCatalog
    {
        private static List<AuditFixer> s_all;

        /// <summary>Все фиксеры (без состояния; список живёт до перезагрузки домена).</summary>
        public static IReadOnlyList<AuditFixer> All => s_all ??= TypeCache.GetTypesDerivedFrom<AuditFixer>()
            .Where(t => !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null)
            .Select(t => (AuditFixer)Activator.CreateInstance(t))
            .OrderBy(f => f.RuleId, StringComparer.Ordinal)
            .ToList();

        public static AuditFixer ForRule(string ruleId) => All.FirstOrDefault(f => f.RuleId == ruleId);

        /// <summary>
        /// Dry-run по папкам: аудит только правила фиксера (остальные правила выключены, JSON не пишется) → план.
        /// MCP: <c>AuditFixerCatalog.PlanForFolders("texture.android-format", "Assets/Art/...")</c>.
        /// </summary>
        public static List<AuditFixAction> PlanForFolders(string ruleId, params string[] folders)
        {
            AuditFixer fixer = ForRule(ruleId) ?? throw new ArgumentException("нет фиксера для правила " + ruleId);
            AuditRequest request = AuditRequest.Folder(folders);
            request.OutputPath = string.Empty;
            foreach (AuditRule rule in AuditRuleCatalog.CreateRules(null, new List<string>()))
            {
                if (rule.Id != ruleId)
                    request.DisabledRules.Add(rule.Id);
            }
            AuditReport report = AssetAuditRunner.Run(request);
            return fixer.Plan(report.Findings.Where(f => f.RuleId == ruleId));
        }
    }
}
