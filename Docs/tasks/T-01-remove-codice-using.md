# T-01 · Удалить editor-only `using` из PlayerController

| | |
|---|---|
| Находка | BUILD-01 (критично для сборки) |
| Блокирована | — |
| Блокирует | T-06 |
| Уровень проверки | 0 (компиляция под Android) |
| Оценка | 5 мин |

Статус — в [`README.md`](README.md). Разбор — в
[`../audit/network-audit-2026-08.md`](../audit/network-audit-2026-08.md#build-01--критично-для-сборки--editor-only-namespace-в-рантайм-скрипте).

## Что делать

В `Assets/Scripts/Player/PlayerController.cs` удалить строку 9:

```csharp
using static Codice.Client.Commands.WkTree.WorkspaceTreeNode;
```

`Codice.*` приходит из `com.unity.collab-proxy` (Plastic SCM) и существует только
в редакторе. В файле не используется — попал автодополнением. В плеере под Android
editor-сборки исключаются, и это `CS0246` на сборке под Quest.

## Границы

Только эта строка. Остальные `using` в файле не трогать. `PlayerController` содержит
другие находки (см. T-11) — они не входят в эту задачу.

## Как проверить

1. Компиляция в редакторе без ошибок (`mcp__unityMCP__read_console`, тип `error`).
2. `File → Build Settings → Android → Build` доходит до компиляции скриптов без `CS0246`.
   После T-06 это делает скрипт ворот.

## Готово, когда

- Строка удалена, консоль редактора чистая.
- Сборка под Android компилирует скрипты.
