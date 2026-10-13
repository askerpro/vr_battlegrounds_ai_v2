# Единая WeaponSystem: машина состояний оружия

## Цель и мотивация

Всё поведение огнестрела — затвор, патронник, спуск, поза, звук, вибрация, отклик — решает одна машина
состояний `WeaponStateMachine`. Учёт патронов один, в SDK; у каждого канала вывода один исполнитель.
Раньше за одно и то же отвечали несколько компонентов и правили состояние друг за другом: рассинхрон позы,
двойные звуки, сетевая ошибка учёта.

## Статус

2026-10-13 — `aimlock-held-lag` влит (`2bc2e9f6`): пистолет двумя руками не фиксирует отставание, если поддержку
взяли на ходу. Принято в шлеме, тесты 31/31. Следующий — `shot-cost-probe` (маркеры профайлера тракта выстрела,
SDK-патч 79, готовы в ветке `ws/shot-cost-probe`), затем замер на Quest с performance.

2026-10-13 — `ledger-spawn-ready` влит (`a1c93d40`, SDK-патч 76): наблюдатели теряли Initialize учёта у ствола,
заспавненного при подключённом клиенте (учёт включался только в `Start`), и навсегда расходились по ревизии —
не видели выстрелов ствола в руках сервера/хоста/бота. Учёт теперь включается в `OnEnable`, отказ replay
виден в логе. Находка bots-fix 5092, разбор совпал с network 5339. Ярус C bots-fix подтвердил (5500, тикет 543):
S2C, все выстрелы сервера применены у клиентов (включая опоздавших и reconnect), 0 форков, 0 отказов replay.
Случай стены арсенала стенд не покрывает — проверка человеком на двух клиентах в `acceptance-g`.

Находка manipulation 5444 (магазин кармана не доходит до опоздавшего): корень — `UxrMagazinePocket.StoreItem`
выключает заспавненный объект, Mirror не спавнит его новому клиенту. Исправляет manipulation по решению network
5460; наши дополнения — 5462 (магазин, возвращённый в карман рукой, вне SyncList; шаг приёмки учёта ствола).

2026-10-12 — **калибровка прицелов**, главный приоритет пользователя ([решения](../../Docs/weapons/decisions.md) «Прицелы»).
- `sight-tr15-optic`: коллиматор эталона TR15 Sight Review перенесён в основной TR15 через `Tr15OpticBuilder`,
  принят в шлеме, коммит `9d5276eb`. Builder теперь сохраняет Mirror `_assetId` (раньше обнулял).
- `sight-iron-kits` влит (`a3b06c63`): инструменты автосведения — комплекты TR15/MX68, SightAdjustablePart,
  решатель «Свести», таблица, writer `WeaponSightAuthoring`, окно Sight Auto-Zero (комплект на планке и
  «Родной прицел» через разделитель SightSplit). Опробовано пользователем на стенде; тесты 104/104.
  Запись в таблицу и применение к префабам выключены — финальная калибровка отдельной задачей.
- `sight-arsenal-dedupe` влит (`b624fdb3`): стена лобби 20 стволов без копий Sight Review; в каталоге одна
  строка — отпечаток арсенала лобби (разрешение gameplay 4912). Штатная пересборка каталога с отпечатком,
  не зависящим от EOL, — этап gameplay `catalog-fingerprint`.
- Хаб use-context: все этапы записаны merged (`record-merged`, 74de9191), контракты active.

Обновлено 2026-10-11. Владелец — эксперт `weapon-system`; задача перенесена в его постоянный worktree
`F:/CodexWorktrees/weapon-system/Vr_Battlegrounds_ai`, ветка `claude/weapon-system` (хаб 3358/3361, план r39).

Выполняется `use-context`: единый контекст руки и экипировки для реальной стрельбы NPC.
2026-10-11 пользователь принял механику в шлеме (аренда 425, Play Mode `Offline.unity`) на совместном
пакете checkpoint `d21bec7e`: own21 + foundation bots-fix `93f67ded` + полные метки SDK71.
Дальше тесты, accepted commit и merge. [План и бриф](use-context.md), история проверок — [Details.md](Details.md) «Сделано».

`codex-expert`: [адаптер Codex](../../.codex/agents/weapon-system-expert.toml) приведён к общему шаблону
экспертов (3275); правила роли — `experts/weapon-system/AGENT.md`. Отдельный запуск Codex не проверялся.

Влиты: B, C, C2, `drive` (5285ceb7), `waves-f` (5af1c7ef), `ejection` (722d14df), `expert-kb` (cd497223).
На машине F1–F4; F5 ещё legacy. Есть вылет, четыре категории отклика, повтор инициализации и запрет
перехвата второй рукой. Подробности — база знаний и журналы этапов.

## Архитектура

Чистая машина (`Assets/Scripts/Weapons/Core`) → датчики → порт учёта SDK → исполнители. Полное описание, инварианты,
решения пользователя — база знаний [`Docs/weapons/`](../../Docs/weapons/README.md).

## План

Очередь этапов и риски — [`Docs/weapons/roadmap.md`](../../Docs/weapons/roadmap.md) (единственный источник).
2026-10-10: план r37 подготовлен; база обновлена fetch/rebase до accepted `1001415b`, WIP сохранён.
Контекст, SDK70 и SDK71 технически verified. Full-context exact13 candidate
`d8dec7e3` включает VR/Equipment grant и SDK prerequisites. SDK72 атрибуция verified `21fcd298`,
`weapon-equipment-shooter@1` agreed обеими сторонами. Actor adapter принадлежит bots-fix.
Canonical authoring выделен отдельным этапом на writer и два prefab; `ejection-manual` следует после него.
Binding exact11 8435c229 с actor adapter сверён; разрешён DEVELOPMENT overlay в reports,
production Assets остаются у bots-fix. SDK lifetime-порт реализован по решению пользователя.
Проект `ejection-manual` — [ejection-manual.md](ejection-manual.md), SDK-патч 69.
Машинный план — [plan.json](plan.json); подробности — [Details.md](Details.md); спецификация машины —
[refactor-plan.md](refactor-plan.md); учёт C2 — [ledger-single-source.md](ledger-single-source.md); журналы этапов —
`changelog/`; скрипты (офлайн-компиляция, claim, продление допуска) — [tools/](tools/Readme.md).

## Синхронизация

- Контракты: `weapon-use-context@2` (владелец мы, agreed), `weapon-equipment-binding@1` (bots-fix; agreed, ещё не accepted),
  `haptics-api@1` (haptics-system).
- Сопровождающий: строки `Docs/weapons` в `AGENTS.md` и `Docs/README.md` — отдельный выпуск после миграции (1494).

## Проверка

C2, `drive`, `waves-f`, `ejection` — шлем пользователя, Android PASS, EditMode (последний: аренда 337, 401 тест,
1 красный вне этапа — бюджет треугольников R08). Подробности — журналы `changelog/`.

## Следующий шаг

Совместный пакет принят пользователем 2026-10-11. Что уже есть:

- NPC-прогоны bots-fix на own21: ticket413/415/419 (3427/3455/3584). Подтверждены выстрелы SDK, учёт,
  ActorSource и damage credit у MEF и Cyborg на PPK и AK105.
- Сетевое ревью network (3597): ACK совместного вливания одним пакетом. Условия 3–5 переданы
  bots-fix (3603), они обязательны до clip-cutover.
- Условия manipulation по `weapon-grab-lifetime` r2 (3447) выполнены: метки `VR Battlegrounds patch 71`,
  патч 67 и `GrabCandidates.cs` не тронуты.
- Аренда 425: импорт без ошибок, Mirror weaver обработал Binding, Missing Script 0; шлем — OK.
  Замечание пользователя про скольжение PPK в ладони при отдаче вынесено в backlog `recoil-hand-follow`:
  это штатная SDK-отдача, пакет её не меняет.

Аренда 428: AndroidCompileGate PASS, тесты оружия 236/236 (включая новые use-context),
хват/сеть/боты/аватары 777 PASS + 1 Ignored. Коммиты: foundation bots-fix, затем use-context;
вливание fast-forward в origin/dev `74de9191`. Журнал — [changelog/2026-10-11-use-context.md](changelog/2026-10-11-use-context.md).
В хабе этапы пакета use-context проведены до merged отдельным docs-кандидатом поверх origin/dev,
который содержит влитый код; это активирует контракты `weapon-use-context`, `weapon-grab-lifetime`
и `weapon-equipment-shooter` и снимает зависимость clip-cutover у bots-fix.
Следующие этапы: `ejection-manual`, затем `trigger-e` → `acceptance-g` → `cleanup-h`.

Наши authoring/readback/idempotence и Android уже PASS, asset review завершён: 0 замечаний.
Свежая офлайн-компиляция пакета: Core/UltimateXR554/Game434/DebugBootstrap6/Editor263/Tests231 exit0.
[Готовность к merge](reports/merge-readiness.md), [текущие результаты](Details.md).
