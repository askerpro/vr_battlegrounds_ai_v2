# Исследование нового окружения и регистрация в хабе

Обновлено: 2026-10-14 (статус сверён с хабом в Codex). Владелец: эксперт `performance`. Worktree: `F:/CodexWorktrees/performance/Vr_Battlegrounds_ai`,
ветка `claude/performance`. Создана по решению пользователя.

## Цель и границы

Задача-«дом» эксперта performance в хабе: адрес, inbox по owner, видимость для других агентов и право
выдавать zone-ack за свою зону. Содержание — знакомство с новым окружением экспертов (рабочее место
`experts/performance/`, хаб, карта зон) и сверка [бэклога эксперта](../../experts/performance/backlog.md)
с текущим `origin/dev`. Код и ассеты задача не меняет. Пишет только `tasks/performance-onboarding/**` и
`experts/performance/**`. Перф-ревью чужих этапов и zone-ack идут от имени этой задачи.

## Этапы

| Этап | Состояние |
|---|---|
| `onboarding` | merged `427ebf1df` |

## Дальше

- Задача-«дом»: от её имени — zone-ack и перф-ревью чужих этапов. Текущая работа — `perf-stress-scenario`
  (см. её Readme), обещания другим агентам — `experts/performance/backlog.md` «Обещания…».
- Хаб: `python Tools/agents/coordination.py inbox --owner performance --unread --as "<свой адрес>"`; ожидание —
  фоновая `wait-inbox --owner performance --as "<адрес>"`. Прежний маршрут Claude очищен при возобновлении;
  вопросы пользователю — через доступный инструмент вопросов.

## Ждёт пользователя

- Ничего открытого.
