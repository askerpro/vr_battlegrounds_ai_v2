# asset-audit — аудитор ассетов под Quest

Обновлено: 2026-10-20. Owner: asset-audit (Claude). Ветка `task/asset-audit`, worktree `F:/CodexWorktrees/asset-audit`.

## Цель и мотивация
Editor-инструмент, который сразу показывает проблемные для Quest 2/3 места в ассетах (пак, выборка, сцена)
и пополняется правилами по мере новых находок. Повод: Lobby на Quest 3 — GPU 10,8 мс при бюджете 7 мс
(scene-detail, per-draw 2026-10-20). Решение пользователя: ПК-паки не отбрасывать, а адаптировать
инструментами; аудитор только читает, исправления — отдельные «фиксеры» позже.

## Архитектура
`Assets/Editor/VR_Battlegrounds/Performance/AssetAudit/` (свой asmdef `VrBattlegrounds.AssetAudit.Editor`):
- `Core/` — ядро: `AuditRule` (id, серьёзность, «почему плохо на Quest», доказательство, подсказка, пороги
  `[AuditThreshold]`), интерфейсы проверок `I*AuditCheck` (материал, текстура, шейдер, объект, сцена, итог),
  охват `AuditInventory`, раннер, слияние одинаковых находок, JSON-отчёт (LF), разбор Shader Graph.
- `Rules/` — по классу на правило; находятся через `TypeCache`. Новое правило = новый класс, ядро не правится.
- Окно `Tools/VR Battlegrounds/Performance/Asset Audit/Open Window`, меню Audit Selection / Active Scene,
  MCP: `AssetAuditRunner.RunFolder(...)/RunScene(...).Summary()`, batchmode `AssetAuditMenu.RunFromCommandLine`.
- Отчёт по умолчанию — `Logs/AssetAudit/*.json` (вне Git).

Этапы и области записи — [plan.json](plan.json).

## План
- [x] Ядро, окно, batchmode/MCP вход
- [x] Первые 11 правил по находкам Lobby
- [x] Прогон по MilitaryCamp и Lobby.unity в worker (ticket 902, 911), сверка с находками, калибровка порогов
- [x] Приёмка пользователем (окно в worker, ticket 915; отчёт «Аудитор ассетов под Quest: первый прогон и вердикты») — 2026-10-20
- [ ] EditMode-тесты `Assets/Tests/EditMode/AssetAudit/` (12 шт., компилируются офлайн; прогон в worker — ticket 917)
- [ ] Ревью зоны performance, heavy-регрессия, merge

## Проверки
- Офлайн-компиляция Roslyn из Unity 6000.4.1f1 против DLL движка: 0 ошибок, 0 предупреждений (2026-10-20).
- Unity worker, прогон 2: пак 708 ассетов за 3,8 с, Lobby 2366 ассетов за 10,5 с, исключений правил 0;
  отчёты `reports/*-r2.json`. Окно в Unity не открывалось (проверяет пользователь).
- Последняя правка (фильтр `AssetDatabase.Contains` в защите от несохранённых ассетов) — только компиляция.

## Следующий шаг
Пользователь смотрит окно и вердикты; затем тесты на принятое поведение. Сверка и побочный эффект — [Details.md](Details.md).
