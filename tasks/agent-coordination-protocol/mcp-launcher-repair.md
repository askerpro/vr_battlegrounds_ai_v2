# Восстановление MCP launcher, 2026-10-09

Причина: Windows CREATE_NO_WINDOW в продуктовом launcher теряет неявные standard
handles. Runtime отвечает напрямую, но штатный launcher в bots-fix, vr-test-stand
и 2cf1 ждёт initialize до timeout. Явные stdin/stdout/stderr устраняют отказ.

Исходный checkout: F:/CodexWorktrees/c14d/agent-infra, base c94bf7bc.
Артефакты: reports/recovery-292-20261009/. Секреты хранятся только локально.

- [x] Сохранить исходный launcher в agent-infra/client_launchers/unity_mcp_proxy.py.
- [x] Добавить tests/test_mcp_launcher_stdio.py с реальным Windows pipe и увидеть
  отказ передачи initialize. Затем добавить только явные standard streams.
- [x] Запустить pipe regression и существующие proxy/process checks. Закрепить
  технический source checkpoint; документация описывает discovery перед чтением.
- [x] До deployment исключить чужую активную операцию. Обновлять только launcher
  с точным ожидаемым SHA256; неизвестную/dirty версию сохранять и пропускать.
  Общий runtime и Unity не требуют изменения. Повторить штатный initialize в
  проверенных checkout; уведомить владельцев через inbox.

Worker ticket292 уже DONE; аварийный R сохранён. Handoff task revision19→20
завершён, все прежние specs/состояния сохранены. Игровая приёмка не выполняется.

Проверено: source141c5ab01234a284f29308017972971b2f9dc9ca; 24/24 tests, независимое ревью без блокеров. Rollout9 внешних checkout; live exact-config initialize/tools/instances/project-info10/10, включая нетронутый haptics (1,20–1,42 с, 48 tools). Main и worker tracked launchers не менялись; runtime VERSION=c94bf7bc. Адресные уведомления MCP: events1394–1403, bots-fix permit guidance1404. Уже работающим клиентам требуется restart/resume.

## Штатный выпуск после аварийного восстановления

Локальный rollout был аварийным обходом. Пользователь 2026-10-09 потребовал
завершить его штатно: отдельный проверенный candidate, merge через coordination
в origin/dev и локальный rebase владельцев. После публикации launcher агенты
сохраняют свой локальный diff перед rebase; одинаковую аварийную правку можно
снять только после проверки её наличия в новой базе. Чужие изменения не
сбрасываются, ветки агентов сопровождающий не переписывает.
