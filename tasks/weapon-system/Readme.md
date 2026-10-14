# Единая WeaponSystem: машина состояний оружия

## Цель и мотивация

Всё поведение огнестрела — затвор, патронник, спуск, поза, звук, вибрация, отклик — решает одна машина
состояний `WeaponStateMachine`. Учёт патронов один, в SDK; у каждого канала вывода один исполнитель.
Раньше за одно и то же отвечали несколько компонентов и правили состояние друг за другом: рассинхрон позы,
двойные звуки, сетевая ошибка учёта.

## Передача (2026-10-14, Claude → Codex)

Владелец — эксперт `weapon-system` (роль: `experts/weapon-system/AGENT.md`, backlog и журнал там же).
Worktree `F:/CodexWorktrees/weapon-system/Vr_Battlegrounds_ai`, ветка `claude/weapon-system`
(сохранена и на origin). Хаб: задача `weapon-system`, план r58+. Координация — режим off.
- Inbox: `python Tools/agents/coordination.py inbox --owner weapon-system --unread --as "<свой адрес>"`;
  ожидание — `wait-inbox --owner weapon-system --as "<свой адрес>"` фоновой командой (не Monitor/watch-inbox).
- Вопросы пользователю: в Codex — явным сообщением с пометкой «ЖДЁТ ОТВЕТА» + строка в «Ждёт пользователя» ниже
  (AskUserQuestion — только Claude). Route не трогать вручную: новая сессия публикует адрес сама.
- Аренд и заявок worker у задачи нет (последние 578/587/596 закрыты). Worker/брокер выключены до команды.

## Статус

**Ветка `claude/weapon-system` = origin/dev + 1 коммит `f59ae8341`** — этап `shot-cost-probe` (running в хабе):
маркеры профайлера тракта выстрела, SDK-патч 79 ([changelog](changelog/2026-10-13-sdk-79.md)). Поведение не
меняют; офлайн-компиляция OK; ревью diff сделано; Unity/тесты не запускались; в origin/dev НЕ влиты — нужны для
замера `shot-pipeline-budget`, вливать вместе с ним/после проверки в Unity.

Влито 2026-10-12…13: `aimlock-held-lag` (`2bc2e9f6`, принято в шлеме, тесты 31/31), `ledger-spawn-ready`
(`a1c93d40`, SDK-патч 76, подтверждён ярусом C bots-fix 5500), прицелы `sight-tr15-optic`/`sight-iron-kits`/
`sight-arsenal-dedupe`, use-context (`74de9191`). История — `changelog/`, журналы `experts/weapon-system/journal/`.

## Следующий шаг

1. **`shot-pipeline-budget`** — замер стоимости выстрела на Quest ([проект](shot-pipeline-budget.md), метод
   performance 6156, план принят 6203). Решено: сцена Lobby; бот стреляет на ПК-сервере через отладочный драйвер
   bots `BotShotCostDriver` (их этап после clip-cutover cc-1), шлем-наблюдатель; сценарий E2E `shot-cost`
   (`ShotCostScenario.cs`, `ShotCostFrameLog.cs` в `Assets/Scripts/Debug/E2E/Scenarios/` — в owns weapon-system по
   6198), строка в `E2ERunner.cs` — `zone-request` launch-infra. Блокеры: bots-fix/clip-cutover merged; этап
   драйвера bots; ответ gameplay 6222 (бот-сессия в Lobby). `quest-perf-pipeline/e2e-log-verdict` уже влит
   (`0cd489fd`). Можно начинать независимое: `tasks/weapon-system/tools/shot_cost_report.py` (`--selftest`),
   рекордер `ShotCostFrameLog` (без аллокаций в кадре, по образцу `PerfFrameRecorder`). Этап добавить в plan.json,
   когда закроется `shot-cost-probe` (хаб: один running-этап на worktree).
2. Затем пулы по цифрам (вспышка/трассер/звук/попадания — наши; логи UXR `_logLevelCore 5` и рефлексия
   StateSync на клиентах — network 5871/5896).
3. Очередь этапов: `ejection-manual` (после shot-cost-probe, aimlock, hand-rig-quality/grip-extraction-run) →
   `trigger-e` → `acceptance-g` (после bots-fix/clip-cutover) → `cleanup-h`. Roadmap — `Docs/weapons/roadmap.md`.

## Ждёт пользователя

- Финальная калибровка прицелов — отдельной задачей по его команде (инструменты влиты, запись/Apply выключены).
- `recoil-hand-follow` — воспроизвести «оружие при отдаче заходит в меш кисти» (backlog п. 4), когда он даст время.
- Приоритет network-правок логов UXR/рефлексии (5896) — решение пользователя о запуске.

## Решения пользователя (свежие; полный список — `Docs/weapons/decisions.md`)

- 2026-10-13 «да я бы брал это из бэклога … остальное чиним»: shot-cost, aimlock-held-lag; recoil-hand-follow —
  «надо будет воспроизвести — оставляем пока в бэклоге».
- 2026-10-13: отставание оружия от контроллера допустимо, пока виртуальная кисть идёт с оружием
  («если отстает от контроллера а виртуальная рука и оружие двигаются вместе то это ок»).
- 2026-10-13 «проверено - проблем нет» — приёмка aimlock-held-lag в шлеме.
- 2026-10-14: замер shot-cost — «Сначала только бот» (стрелок-человек после `xr-input` launch-infra).
- 2026-10-12: калибровка прицелов — «пока делаем только инструменты - финальная калибровка будем отдельной задачей».

## Риски и ловушки

- `SaveAsPrefabAsset` обнуляет `NetworkIdentity._assetId` — после записи префаба `NetworkAssetIdNormalizer` +
  сверка с диском; тест `Assets/Tests/EditMode/Prefabs/NetworkAssetIdOnDiskTests.cs`. Касается и чужих писателей
  (avatar-grip поймано 6378).
- Ответ `editor-broker.py claim` писать в файл целиком (потерянный токен блокирует очередь всех).
- MCP `run_tests` часто рвёт сессию на старте — проверить `guard` (`is_test_running`) перед повтором; результаты
  в `%USERPROFILE%/AppData/LocalLow/DefaultCompany/VR Battlegrounds/TestResults.xml` (сверять время).
- В редакторе worker `Time.deltaTime` бывает 0,333 с → тесты сопротивления Inconclusive (окружение).
- Харнесс `TwoHandGrabHarness` не вешает граббер на кость кисти (`UxrHandIntegration.Awake`) — см.
  `MainGripAimLockHeldLagTests`.
- Изменение running-этапа (writes) хаб запрещает: stop → register → begin.
- После каждого `register` повторить `contract-ack` пяти контрактов (ACK привязан к ревизии плана).

## Архитектура и синхронизация

Машина (`Assets/Scripts/Weapons/Core`) → датчики → порт учёта SDK → исполнители. База знаний —
[`Docs/weapons/`](../../Docs/weapons/README.md). Контракты: `weapon-use-context@2`, `weapon-grab-lifetime@2`,
`weapon-equipment-shooter@1` (наши, active), `weapon-equipment-binding@2` (bots-fix), `haptics-api@1`.
Машинный план — [plan.json](plan.json); подробности — [Details.md](Details.md); спецификация машины —
[refactor-plan.md](refactor-plan.md); скрипты — [tools/](tools/Readme.md).
