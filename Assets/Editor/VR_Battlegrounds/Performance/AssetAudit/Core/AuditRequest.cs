using System.Collections.Generic;

namespace VrBattlegrounds.Editor.Performance.AssetAudit
{
    public enum AuditScopeKind
    {
        /// <summary>Папки целиком (пак): материалы, текстуры, шейдеры, префабы и модели.</summary>
        Folder,
        /// <summary>Выбранные ассеты (папки раскрываются) и, по флагу, их зависимости.</summary>
        Assets,
        /// <summary>Сцена: объекты сцены + ассеты из её зависимостей + настройки окружения.</summary>
        Scene,
    }

    /// <summary>Что и как проверять. Создаётся фабриками <see cref="Folder"/>, <see cref="Assets"/>, <see cref="Scene"/>.</summary>
    public sealed class AuditRequest
    {
        public AuditScopeKind Kind { get; private set; }

        /// <summary>Папки, ассеты или один путь сцены — по <see cref="Kind"/>.</summary>
        public List<string> Paths { get; } = new List<string>();

        /// <summary>Добавлять зависимости выбранных ассетов (для сцены — всегда).</summary>
        public bool IncludeDependencies = true;

        /// <summary>Префиксы путей, чьи ассеты не проверяются сами (шейдеры URP, текстуры пакетов).</summary>
        public List<string> ExcludePrefixes { get; } = new List<string> { "Packages/" };

        /// <summary>Id выключенных правил.</summary>
        public HashSet<string> DisabledRules { get; } = new HashSet<string>();

        /// <summary>Переопределение порогов: ключ <c>ruleId.Поле</c>, значение строкой (массив — через запятую).</summary>
        public Dictionary<string, string> ThresholdOverrides { get; } = new Dictionary<string, string>();

        /// <summary>Файл JSON-отчёта; null — <c>Logs/AssetAudit/&lt;охват&gt;-&lt;время&gt;.json</c>; пустая строка — не писать.</summary>
        public string OutputPath;

        /// <summary>Выгружать из памяти ассеты, которые стали несохранёнными во время прогона (см. отчёт errors).</summary>
        public bool RevertNewlyDirtied = true;

        /// <summary>Полоса прогресса (окно и меню); из MCP/batchmode выключена.</summary>
        public bool ShowProgress;

        public static AuditRequest Folder(params string[] folders) => Create(AuditScopeKind.Folder, folders);

        public static AuditRequest Assets(params string[] assetPaths) => Create(AuditScopeKind.Assets, assetPaths);

        public static AuditRequest Scene(string scenePath) => Create(AuditScopeKind.Scene, scenePath);

        /// <summary>Читаемое описание охвата для отчёта и имени файла.</summary>
        public string Describe() => Kind + ": " + string.Join(", ", Paths);

        private static AuditRequest Create(AuditScopeKind kind, params string[] paths)
        {
            var request = new AuditRequest { Kind = kind };
            foreach (string path in paths)
            {
                if (!string.IsNullOrWhiteSpace(path))
                    request.Paths.Add(path.Replace('\\', '/').TrimEnd('/'));
            }
            return request;
        }
    }
}
