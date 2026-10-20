# Пакет C': бой, смерть и реестр SDK с ботами на выделенном сервере

2026-10-20. Этап 1 эпика bots-epic, сокращённый блок C. Только существующие пробы и диагностический снимок;
новых тестовых ожиданий нет.

Топология: PlayLaunch server + 2 PC Spectator + 8 ботов, TestMap1, elimination, AutoGoLive, MinPlayers 8,
диагностика старта включена. Вход — origin/dev на момент заявки.

## Прогон 1 (аренда 872)

Шаги 1–4 и 6 выполнены, reconnect не дошёл: клиенты-клоны тормозили. Результаты и разбор — в Readme.md
(раздел C'). Выводы: проба не видела смертельный удар (`DamageReceived` SDK не поднимает для смертельного урона),
торможение клонов — рост отчёта NetObserver, 12 мёртвых записей после смены карты — UxrGrabbableObject.

## Прогон 2 (после починки проб)

Правки проб (код стенда, Assets/Scripts/Debug/BotCombatStand):
- MatchProbe слушает `UxrWeaponManager.DamageReceiving` (как DamageLedger игры), `dies` из события; урон, который
  режим не пропускает (разминка), не пишется.
- NetObserver выгружает ушедшие тела с завершёнными замерами строкой в `<участник>-pid<N>.bodies.jsonl` и
  освобождает их; периодическая запись — компактная, раз в 5 с; общие ряды клиента прорежены до 8192; схема `@3`.

Шаги:
1. До Play — заявки `BotStandMatchProbeGate.WriteRequest` и `BotStandNetObserverGate.WriteRequest`, отчёты в
   `Logs/bv-cprime2/{probe,observer}` worker (вне Assets).
2. Play, Ready, 2–3 раунда боя ботов на TestMap1. Снимок реестра S1 (`registry-snapshot.cs.txt`, label S1).
3. `MapLoader.Instance.LoadMap("TestMap2")`, готовность, боты воюют; снимок S2.
4. Адресный disconnect Player 2 (StandRequest), пауза, reconnect; Player 2 снова видит карту и ботов; снимок S3.
5. `MapLoader.Instance.LoadMap("TestMap1")`, готовность, бой; снимок S4.
6. `BotStandMatchProbeGate.FinishActive`, Cancel, очистка заявок, архив `Logs/bv-cprime2` и логов участников в
   `tasks/bots-validation/reports/cprime2-<ticket>/`, finish/receive.

Снимок реестра пишет сводку в `registry.txt` и все записи в `registry-<label>.tsv` (ключ, живая/мёртвая, тип,
сцена, путь, activeInHierarchy, enabled, OriginalUniqueId, CombineIdSource, счётчик s_idCollisions).

Разбор:
- (a) каждое убийство: смертельный удар (`dies=true`) от той же сессии-убийцы и транзакция `Kill` у неё —
  `analyze_cprime2.py` (scratch, перенести при повторном использовании);
- (b) на одну жизнь одно тело (сервер); у клиентов нет `respawnedSameNetId`, `duplicateWeaponIdsMax = 0`,
  `botComponentsOnClient = 0`; отчёт клиента не растёт с числом жизней (размер `.json` стабилен, тела — в jsonl);
  кадр клонов (`clientFrameMs`) без роста к концу прогона;
- (c) реестр: `analyze_registry.py <папка>` — мёртвые записи каждого снимка по сцене/типу/пути из предыдущего
  снимка; настоящие коллизии живых записей; дубли тел — 0; записи старой карты после смены не остаются.

Ограничение: execute_code работает только в главном редакторе (сервер); реестр клиентов — по логам и NetObserver.
