# Анализ состояния сетевой зоны и разбор бэклога

Обновлено: 2026-10-14, продолжение в Codex. Владелец: эксперт `network`. Worktree: `F:/CodexWorktrees/network/Vr_Battlegrounds_ai`,
ветка `claude/network`. Создана по решению пользователя (через сопровождающего agent-infra-b1).
Старые адреса Claude очищены в хабе; canonical inbox остаётся каналом связи.

## Статус (2026-10-14, передача Claude → Codex)

АКТИВНА как задача-«дом» эксперта network (inbox по owner, zone-ack, ревью сетевой части чужих этапов). Кода нет.
Текущие задачи: `precache-server-guard`, `network-sync-hardening`; код готов, текущая Unity-проверка и приёмка
не выполнены. Обещания и кандидаты — `experts/network/backlog.md`. Inbox обработан текущей сессией Codex
`01a13af5-485c-7ef1-99c6-9e1f1dd753aa`; решение 6589 прочитано, ответ 6647 записан.
Запрос готовности worker — 6643; остановка снята объявлением 6805, согласованные проверки продолжаются через FIFO.

## Цель и границы

Задача-«дом» эксперта network в хабе: адрес, inbox по owner и право выдавать zone-ack за свою зону.
Содержание задачи — анализ текущего состояния сетевой зоны и разбор
[бэклога эксперта](../../experts/network/backlog.md): какие кандидаты актуальны, что закрыто, в каком
порядке и какими задачами их брать. Код и ассеты задача не меняет. Пишет только `tasks/network-state-review/**`
и `experts/network/**`. Ревью сетевой части чужих этапов и zone-ack идут от имени этой задачи.

## Этапы

| Этап | Состояние |
|---|---|
| `state-review` | merged `ac238e942`: задача, `expert.json` |
| `journal-publish` | merged `95cf4f29d`: журнал ревью 2026-10-11 |
| `journal-2026-10-12` | журнал ревью 2026-10-12 (боты, смерть, StateSync) и backlog |

## Сделано

- `vr-test-stand/e2e-run-isolation`: согласие с шестью условиями, ACK diff, zone-ack (событие 3566). Влито
  `7ceb8a08`, сетевой diff совпал. Остаток — backlog `e2e-reconnect-port-seam`.
  [Журнал](../../experts/network/journal/2026-10-11-e2e-run-isolation-review.md).
- `use-context` (weapon-system + bots-fix): согласие на совместное вливание (3597), влито `74de91912`. Условия
  binding — до `clip-cutover`. `bots-fix/armed-development`: ACK `PlayerSession`; условие по
  `GameNetworkManager.OnStartClient` до вливания (3611, принято в 3615).
  [Журнал](../../experts/network/journal/2026-10-11-use-context-review.md).

## Дальше

- Сверить бэклог с кодом на текущем `origin/dev`, отметить устаревшее и предложить пользователю порядок.
- Ждать от bots проект ветки автостарта в `GameNetworkDiscovery` (терминальный отказ клиента) до реализации.
