# T-25 · Счётчик раундов в `SetManager` не растёт

| | |
|---|---|
| Находка | MATCH-06 (высокий) |
| Блокирована | — |
| Блокирует | — |
| Уровень проверки | 1 (юнит-тесты — **уже написаны и красные**) |
| Оценка | 30 мин |

Разбор — в [`../audit/network-audit-2026-08.md`](../audit/network-audit-2026-08.md#match-06--высокий--сет-не-может-закончиться-по-исчерпанию-раундов).

## Что делать

`Assets/Scripts/GameModes/EliminationMode/SetManager.cs:115`, метод `OnRoundEnded`,
ветка «сет продолжается»:

```csharp
_roundManager.RoundEnded += OnRoundEnded;
_roundManager.StartNextRound(_eliminationMode);   // ← вызывается метод RoundManager
```

Должен вызываться собственный приватный `SetManager.StartNextRound()` — он инкрементирует
`_currentRound`, шлёт `RpcOnRoundStarted` и уже сам зовёт `_roundManager.StartRound(...)`.
У двух классов методы называются одинаково, поэтому ошибка незаметна глазами.

## Почему это важно

`_currentRound` увеличивается ровно один раз, в `StartSet`. Отсюда:

1. `_currentRound >= _roundsPerSet` не выполняется никогда — сет заканчивается только
   по достижению порога побед. При чётном числе раундов или серии ничьих не заканчивается
   вообще.
2. `RpcOnRoundStarted` уходит только для первого раунда — у клиентов номер раунда навсегда
   `1`. Это видимый игроку баг в HUD.

## Границы

Только эта строка. Подсчёт победителя не трогать — это [T-08](T-08-fix-tie-detection.md).
Структуру машины состояний не трогать — [T-09](T-09-explicit-round-fsm.md).

## Как проверить (автономно, без человека)

Тесты уже написаны и сейчас красные. После правки должны позеленеть три:

```
Assets/Tests/EditMode/SetManagerScoringTests.cs
  Счёт_1_1_это_ничья
  Счёт_1_0_при_исчерпании_раундов_отдаёт_победу
  Ничейные_раунды_дают_ничью_в_сете
```

Запуск через MCP:

```
run_tests(mode="EditMode", assembly_names=["VrBattlegrounds.Tests.EditMode"])
get_test_job(job_id=..., include_failed_tests=true, wait_timeout=60)
```

## Готово, когда

- Все 6 тестов в `SetManagerScoringTests` зелёные.
- Ворота `AndroidCompileGate.Run()` — PASS.
