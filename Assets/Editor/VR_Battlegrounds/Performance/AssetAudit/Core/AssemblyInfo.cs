using System.Runtime.CompilerServices;

// Тесты правил создают AuditContext напрямую (конструктор внутренний: правила получают контекст только от раннера).
[assembly: InternalsVisibleTo("VrBattlegrounds.Tests.EditMode.AssetAudit")]
