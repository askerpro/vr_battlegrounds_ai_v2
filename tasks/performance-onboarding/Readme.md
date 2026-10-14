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

## Ревью 2026-10-14

`scene-detail`, запрос 6813 → ответ 6875: статических блокеров не найдено; Quality содержит только девять
переименований, 140 texture-meta меняют только имя группы. Snapshot HEAD cc485d9bd, diff SHA256 895a39b7a72e…,
155 файлов без drift. Для ACK protected-инварианта запрошен export 112 GUID; runtime/UI/Quest приёмка открыта.
`Apply` событийный, но выделяет коллекции при применении правил. Отчёт: reports/scene-detail-6813/source-review.md.

`network-sync-hardening`, запрос7005 → owner disposition7025: три derived shader-prefilter поля
`PC.asset` в Rb11d19aa57 не принимать в source, canonical asset сохранить. URP17.4.0 updater/save
подтверждён, main/worker source hashes совпадают; runtime/variants counts не измерялись.
Review note7021: native build report18:02–18:04MSK предшествует заявке65120:29, свежий BuildPASS
этим файлом не подтверждён. Owner/agent-infra уведомлены, source/receive/ACK не менялись.
Отчёт: reports/network-7005/urp-prefilter-review.md; raw snapshot и primary-check рядом.

Уточнение7023: alternative correlation поддерживает свежую build identity651 — execution history
внутри lease, export386files/EXE+3DLL hashes, шесть role JSON с одной revision/buildGuid.
Старые native DateTime не используются для freshness; причина конверсии остаётся предположением owner.
Первая серия functional24/24, но RED/unexpected4; вторая24/24 GREEN/logs0. Это не perf/Quest acceptance,
launch executable path в role JSON не аттестован. Отчёт: reports/network-7005/correlation-review.md.
