# Этап ejection-manual: признак стреляной гильзы в учёте SDK (патч 69)

Проект 2026-10-09 (субагент Plan, только чтение). Код не менялся. Номер SDK-патча 69 зарезервирован в хабе
(`patch-reserve --key ejection-manual`; 68 занят другой задачей). Строки кода — на `cd497223`, сверять перед правкой.

## Зачем

У ручного цикла (FABARM, SRM12 = Desert Tech SRS, SniperRifle = AX-50) гильза должна вылетать не на выстреле, а
при оттяжке. Машина не хранит механическое состояние, поэтому «в патроннике стреляная гильза» — факт учёта SDK, а не
память машины (решение пользователя 2026-10-09, вариант A).

## Решение

- Учёт SDK: `bool SpentCaseInChamber` в `UxrFirearmReadinessState` (`UxrFirearmReadinessTypes.cs:59–93`), формат
  состояния v3 → v4 (v3 читается как `false`), поле — в `Serialize`, `Equals`, `GetHashCode` (от равенства зависят
  CloseOnly/EmptyRest/Admission, `IsPhysicalValidationCurrent`, публикация поправки, `UxrWeapon.Custom.cs:238`,
  `RuntimeTriggerInfo.Equals`).
- Инвариант поля: гильза ⇒ Initialized ∧ ¬ChamberRound ∧ ShotSequence > 0 (гильза и патрон взаимоисключающие).
- Ставит: Shot при `CycleType == ManualReload` (`TryShootReadinessRound` :504–508; то же правило в `expected`
  `ValidateShotCommit` :466–469). Снимают: Extract (:210), Complete (:226), Automation (:274); Initialize — новое
  состояние. Сохраняют (клон): BeginAction, Cancel, CloseOnly, EmptyRestAck, AmmoAdmission.
- Проверка: новая `ValidateSpentCaseCommit` по образцу `ValidatePostShotEmptyActionCommit`, вызов в
  `ValidateReadinessCommit` (:365). Строгая: расхождение → фиксация не применяется → форк → поправка сервера + Error.
- До машины: `WeaponLedgerReader` (`LedgerCommitInfo.SpentBefore/SpentAfter`, как `ChamberBefore`; `Build` →
  `LedgerView.SpentCase` для панелей/тестов), `WeaponEvents.OpSpentCaseBefore/After` (по умолчанию false),
  `WeaponSystem.cs:518` передаёт.
- Машина (без памяти): в `CueForCommit` после switch — `if (OpSpentCaseBefore && !OpSpentCaseAfter) Cue(CasingEjected)`.
  Shot по-прежнему даёт гильзу при FireMode ≠ Manual — ровно одна гильза на выстрел. Таблица переходов не меняется.
- Сеть: признак в `StateAfter` всех фиксаций, в поправке и в снимке; новых Cmd/Rpc нет. `SnapshotLoaded` и поправка
  сигналов не дают (лишней гильзы нет, пропуск возможен). Нового `[Preserve]` не нужно.

## Граничные случаи

Ручной цикл: Shot → гильза в патроннике → оттяжка → Extract выбрасывает гильзу → Complete досылает. Последний
патрон: EmptyAwaitRest → AckEmptyRest (гильза остаётся) → оттяжка выбрасывает. Частичная оттяжка (CloseOnly)
сохраняет гильзу. Смена магазина не трогает. Бот: Automation снимает признак → гильза у клиентов. Herrington —
`_cycleType` FullyAutomatic: гильза на Shot, признак не нужен. Срабатывает у FABARM, SRM12, SniperRifle. Револьверы с
`ManualReload` при переводе получат гильзу на Extract — учесть в F5.

## Объём и права

SDK ~35 строк (`UxrFirearmReadinessTypes.cs`, `UxrFirearmWeapon.Readiness.cs`), игра ~25 (`Core/LedgerView.cs`,
`Core/WeaponEvents.cs`, `Core/WeaponStateMachine.cs`, `Core/WeaponOutput.cs`, `WeaponSystem/Sensors/WeaponLedgerReader.cs`,
`WeaponSystem/WeaponSystem.cs`, по желанию `WeaponStatePanels.cs`). `UxrFirearmWeapon.cs` не трогается (в writes этапа
он есть — можно сузить). Метка в коде SDK — `VR Battlegrounds patch 69`.
Документы: `tasks/weapon-system/changelog/<дата>-sdk-69.md` (общий `Docs/UltimateXR/sdk-patches.md` — исторический,
не правим), `refactor-plan.md` п. 3.2/3.7 (И14)/4.2, `ledger-single-source.md` п. 3, `Docs/weapons/architecture.md`
(сигнал `CasingEjected` у ручного цикла).

## Проверки

Существующие тесты проходят без правок (новые поля по умолчанию false); устаревают описания в `WeaponEjectionTests`
(«извлечение не выбрасывает гильзу»). Шлем (до тестов): FABARM, SRS, AX-50 — гильза вылетает при оттяжке после
выстрела, не на выстреле; оттяжка без выстрела — живой патрон; частичная оттяжка гильзу не выбрасывает; второй клиент
видит то же. Новые тесты после приёмки: учёт (ставит/снимает/сохраняет, поддельные фиксации, v3/v4, Equals), машина
(автор/наблюдатель: Extract → [ActionBack, CasingEjected], одна гильза на выстрел для Semi/Auto/Manual с частичной
оттяжкой), чтение учёта, поздний вход, бот.

## Решения владельца (2026-10-09)

1. Гильза у бота на Automation — оставить (иначе дробовик бота гильз не выбрасывает никогда).
2. Расхождение признака при replay — строгая проверка, как `PostShotEmptyAction`.
3. Shot — оставить правило FireMode (результат тот же; режим огня машины строится из того же `CycleType`).
4. Устаревшее «до» в `_last` при фиксации в кадре загрузки снимка (тот же риск у `ChamberBefore`) — вне этапа,
   в очередь (защита: доверять `_last` только при `_last.Revision == commit.ExpectedRevision`; см. roadmap).
5. Реестр патчей — только журнал задачи.
6. Гильза SniperRifle — дефолт категории «снайперская винтовка» (`Sniper_Casing`), сделано в ejection.
