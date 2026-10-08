# map-runtime-bootstrap — запуск карты через MapBootstrap

## Цель и мотивация

Любая карта запускается одним серверным путём из `MapRoot`: неизменяемый конфиг запуска, служебные объекты из
каталога, производный допуск (`MapRunAdmission`), барьер начального снимка Relay. Раньше менеджеры раскладывались по
сценам вручную, предметы выдавались мимо допуска, клиент применял снимок до готовности карты. Запуск нужен
генератору станций арсенала и сетевой игре.

## Статус

2026-10-08, владелец `map-runtime-bootstrap` (Claude Code). Влито и принято пользователем: контракты, миграция карт,
барьер Relay, транзакция загрузки, допуск, постоянные тесты (последнее — c0a53921). Этап `generated-stations` —
адаптер генерируемых станций — проверен на worker на базе ec92098a и вливается по поручению пользователя
(«проверь сам через MCP и вливаем», 2026-10-08).

## Архитектурное решение

- Сервер: `MapBootstrap` → Resolve → BeginRun → станции (authored — Prepare пресета; generated —
  `MapArsenalCompositionAdapter`: Capture → PrepareComposition → Activate) → служебные объекты → CompositionReady →
  разминка → server Ready.
- Клиент собирает generated-станции тем же адаптером с тем же `MapRunKey` и сверяет с конфигом сервера;
  `IsLocallyReady` открывает барьер снимка Relay.
- Отказы именованные (`Arsenal.Generated.*`), без отката на authored.

Подробно — [Details.md](Details.md). Прежние `Docs/tasks/map-runtime-bootstrap-*.md` — история до
2026-10-08, не обновляются (протокол запрещает этапам запись в `Docs/tasks`). Отдельное предложение единого запуска Play —
[play-launch-proposal.md](play-launch-proposal.md) (ждёт решений пользователя; станет своей задачей в своём worktree).

## План

1. `generated-stations`: worker — компиляция, AndroidCompileGate, пробы [tools/](tools/), EditMode-группы, Play на хосте → вливание.
2. SHA генератору: он переводит лобби на сгенерированные станции.

Машинный план — [plan.json](plan.json).

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

Владелец: после вливания — SHA генератору (этап `lobby-switch`).
