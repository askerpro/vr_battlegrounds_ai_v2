# map-runtime-bootstrap — запуск карты через MapBootstrap

## Цель и мотивация

Любая карта запускается одним серверным путём из `MapRoot`: неизменяемый конфиг запуска, служебные объекты из
каталога, производный допуск (`MapRunAdmission`), барьер начального снимка Relay. Раньше менеджеры раскладывались по
сценам вручную, предметы выдавались мимо допуска, клиент применял снимок до готовности карты. Запуск нужен
генератору станций арсенала и сетевой игре.

## Статус

2026-10-08, владелец `map-runtime-bootstrap` (Claude Code). Всё влито: контракты, миграция карт, барьер Relay,
транзакция загрузки, допуск, тесты (c0a53921, приняты пользователем) и этап `generated-stations` — адаптер
генерируемых станций (c427527d, проверен на worker, влит по поручению пользователя).

## Архитектурное решение

- Сервер: `MapBootstrap` → Resolve → BeginRun → станции (authored — Prepare пресета; generated —
  `MapArsenalCompositionAdapter`: Capture → PrepareComposition → Activate) → служебные объекты → CompositionReady →
  разминка → server Ready.
- Клиент собирает generated-станции тем же адаптером с тем же `MapRunKey` и сверяет с конфигом сервера;
  `IsLocallyReady` открывает барьер снимка Relay.
- Отказы именованные (`Arsenal.Generated.*`), без отката на authored.

Подробно — [Details.md](Details.md). Прежние `Docs/tasks/map-runtime-bootstrap-*.md` — история до
2026-10-08, не обновляются (протокол запрещает этапам запись в `Docs/tasks`).

## План

`generated-stations` влит. Следующий этап — `startup-route`: старт сервера сразу в целевую сцену без обязательного
лобби (контракт `map-startup-route` rev 2 agreed). Реализован и проверен на worker и e2e двух процессов с поздним клиентом (GREEN),
ждёт приёмки пользователем и вливания; подробности — [Details](Details.md). Машинный план — [plan.json](plan.json).

## Синхронизация

Зависит от API сборщика arsenal-generator (сигнатуры сверены с dev, контракт в реестре не зарегистрирован — `needs`
пуст, причины в Details.md). Этап arsenal-generator `lobby-switch` ждёт наш `generated-stations` (merged).

## Проверка

База ec92098a, аренда worker 212 (finish принят, неожиданных изменений нет): компиляция 0 ошибок, AndroidCompileGate
PASS, пробы [tools/](tools/) 32/32, EditMode Maps/Network/Managers/Arsenal/ArsenalWall/Modes/SpawnZones — 658 тестов,
7 падений только в тестах содержимого сцен (геометрия, текстуры блокаута, жетоны, раскладка лобби), код этапа их не
касается. Хост: Lobby → TestMap1 → та же карта ещё раз → Стоп → Lobby, server Ready на каждом шаге, ошибок в консоли 0.
Не проверено: серверная половина с generated-станцией и e2e — в проекте нет карты с такой станцией.

## Следующий шаг

SHA c427527d генератору отправлен через `coordination.py message`; базу worker публикует сопровождающий.

Остаток (владелец map-runtime-bootstrap):
1. После `lobby-switch` генератора — проверить серверную половину адаптера и e2e на лобби со сгенерированными станциями.
2. Уборка: пути отчётов `Docs/tasks/report/map-runtime-bootstrap/` в
   `MapBootstrapMigration.cs:38` и `MapRunPreflight.cs:72` → `tasks/map-runtime-bootstrap/reports/`; комментарий про
   удалённую пробу в `MapRunContractTests.cs:15`; временные ветки `codex/tmp/map-bootstrap-*`.
3. Мелкие правки по итогам проверки пользователя 2026-10-07 — список ждёт пользователя.
4. Не проверено, ждёт общей проверки игры после вливаний агентов: два клиента, поздний вход, шлем.
