# События смены масштаба калибровки

## Цель и мотивация

Калибровка роста масштабирует `Dummy Forward` аватара (`AvatarCalibrationApplier`). Предметы в якорях на скелете —
оружие в кобурах на Pelvis/Spine — масштабируются вместе с телом и остаются искажёнными (weapon-system 5827).
manipulation (задача `grab-authority`) на время смены масштаба сохраняет мировой масштаб предметов в кобурах —
локально на каждой машине. Для этого нужны события вокруг смены масштаба (запрос 5936).

## Статус

Обновлено 2026-10-14 (передача Codex). Владелец — эксперт `avatar-ik`. Ветка `avatar-ik/calibration-scale-events`,
код — `eb97eac1a`, далее документы (база origin/dev `a84b9f2ce`; origin/dev ушёл вперёд). Контракт
`calibration-scale-events` r1 — agreed (ACK manipulation 6036). Этап `scale-events` в хабе — **verified на старом
SHA `8ec241cd9`** (worker 590: компиляция 0 ошибок, тесты калибровки 26/26). Текущий SHA на worker не проверен:
очередь стояла на восстановлении базы лобби (606/607). Аренд и тикетов нет. Актуальное рабочее место эксперта —
на ветке `avatar-ik/uxr-precache-once`.

## Сделано

- `Assets/Scripts/PhysicalSpaceUtils/AvatarCalibrationApplier.cs`: `event Action<AvatarCalibrationApplier,float,float>
  ScaleChanging / ScaleChanged` — синхронно в `Apply`, до записи масштаба и после (масштаб + поля BodyIK), только при
  реальной смене (> `ScaleChangeEpsilon` 1e-4), на каждой машине; вызов изнутри синхронизации UXR — `GameLog.Error`;
  упавший обработчик логируется и не прерывает калибровку.
- Спецификация — [contracts/calibration-scale-events.spec.json](contracts/calibration-scale-events.spec.json);
  офлайн-проверка — [tools/compile-check.ps1](tools/compile-check.ps1); отчёт worker 590 — `reports/worker-590/`
  (локально, вне Git).

## Следующий шаг

1. Worker (когда сопровождающий откроет очередь): компиляция, `AndroidCompileGate`, тесты
   `VrBattlegrounds.Tests.Network.CalibrationHeightReplicationTests`, `CalibrationScaleReplicationTests`,
   `PlayerCalibrationOwnerTests` (26). Обычный checkpoint может отклониться — тогда `checkpoint --onto-baseline`.
   Для `verify` нового SHA нужен новый токен: `stop` этапа (verified устарел) → `begin`.
2. Приёмка пользователем в шлеме (ниже) → тесты событий в `Assets/Tests/EditMode/Calibration/` (новый файл в
   `Network/` попал бы в зону network) → `accept` → merge-request → merge-execute.
3. После вливания — сообщить manipulation (`grab-authority`): события в origin/dev.

## Ждёт пользователя

- Проверка в шлеме: в лобби откалибровать рост — рост и масштаб применяются как раньше, в логе нет ошибок
  `AvatarCalibrationApplier`. Без подписчиков поведение не меняется.

## Решения пользователя

- 2026-10-13: «делай до pose set в отдельной ветке от origin/main» — origin/main в репозитории только initial commit
  2026-03-01, ветка сделана от origin/dev (основная линия, origin/HEAD).
- 2026-10-14 (AskUserQuestion): приёмка — «Проверю в шлеме»; вливание — «Через брокер».

## Риски и ловушки

- Прямой `git push` в origin/dev блокировал классификатор Claude; маршрут — брокер. `request-base` — только после
  разрешения сопровождающего (6535).
- `watch-ticket` при timeout отдаёт exit 0 и ok:false — смотреть JSON; оффер живёт 60 с.
- Inbox: `inbox --owner avatar-ik --unread --as "<адрес сессии>"`, ожидание — `wait-inbox` фоновой командой; в Codex
  вопросы пользователю — сообщением «ЖДЁТ ОТВЕТА» и строкой здесь.

Машинный план — [plan.json](plan.json).
