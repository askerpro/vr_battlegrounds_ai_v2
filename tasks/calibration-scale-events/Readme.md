# События смены масштаба калибровки

## Цель и мотивация

Калибровка роста масштабирует `Dummy Forward` аватара (`AvatarCalibrationApplier`). Предметы в якорях на скелете —
оружие в кобурах на Pelvis/Spine — масштабируются вместе с телом и остаются искажёнными (weapon-system 5827).
manipulation (задача `grab-authority`) на время смены масштаба сохраняет мировой масштаб предметов в кобурах —
локально на каждой машине. Для этого нужны события вокруг смены масштаба (запрос 5936).

## Статус

Обновлено 2026-10-14, продолжение Codex. Владелец — эксперт `avatar-ik`. Ветка `avatar-ik/calibration-scale-events`
перебазирована на origin/dev `d3c62ca5e`, срез кода `f4ac8c20f`; конфликтов нет, исходник applier не изменился.
Непринятый патч прогрева сохранён отдельно, stash `7c77e9f76c296936b274588472281993e5e6c44c`;
после проверки калибровки основная сессия вернёт ветку прогрева. Контракт
`calibration-scale-events` r1 — agreed (ACK manipulation 6036). Исторический verified относился к
SHA `8ec241cd9` (worker590: компиляция 0 ошибок, тесты калибровки 26/26). Исторический worker611 отменён до запуска.
Свежая проверка: checkpoint `31edcc4de594d8b19a588d67fe23b29fb7d4ba9b`, worker638 DONE;
Unity-компиляция точного исходника/наличие событий в загруженной сборке — PASS, AndroidCompileGate — PASS,
существующие тесты — **26/26, failed=0, skipped=0**. Finish: paths=[], unexpected=[].
Отчёт `reports/worker-638/summary.json` и полные ответы MCP внутри задачи, вне Git.
План revision3; свежий **verified** в хабе относится к checkpoint `31edcc4de` (epoch5),
перепроверка заменяет историческую. Пользовательская приёмка остаётся впереди.
Свежая офлайн-компиляция UltimateXR555/VrBattlegrounds459 исходников — exit0, errors0;
логи `reports/resume-compile/out/` внутри задачи, вне Git. Актуальное рабочее место эксперта —
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

1. Перепроверка на принятой базе и свежий verify выполнены (worker638, checkpoint31edcc4de).
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

- Прямой `git push` в origin/dev блокировал классификатор Claude; маршрут — брокер. Запрет6535 снят6695;
  публикация базы — только после принятого вливания по штатному маршруту.
- `watch-ticket` при timeout отдаёт exit 0 и ok:false — смотреть JSON; оффер живёт 60 с.
- Inbox: `inbox --owner avatar-ik --unread --as "<адрес сессии>"`, ожидание — `wait-inbox` фоновой командой;
  вопросы пользователю — штатным инструментом, новых вопросов по уже записанной приёмке нет.

Машинный план — [plan.json](plan.json).
