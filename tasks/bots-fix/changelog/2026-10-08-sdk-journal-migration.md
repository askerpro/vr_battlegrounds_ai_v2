# SDK и Blaze: записи перенесены в задачу

2026-10-08. Общие журналы и индекс исключены из writes по agent_coordination.md.
Проверки ниже исторические; новая база требует повторной проверки. Текущий статус — ../Readme.md.

## Docs/BlazeAI/sdk-patches.md


## Внешний Animator видимого бота (полные клипы, 2026-10-07)

BlazeAI.BindAnimatorSource(Animator) задаёт общий источник для AnimationManager и прямых
чтений anim. Убрано RequireComponent(Animator): navigation proxy не создаёт лишний Animator.
Awake/OnEnable/refresh используют один resolver; повторное включение не возвращает локальный
источник поверх внешнего. Штатный actor без внешнего binding использует свой local Animator.
При отсутствии источника OnEnable выключает поведение; уничтоженный внешний источник
не подменяется случайным local Animator. Binding настраивается до активации proxy.

Это подготовленная SDK-граница одобренного плана plans/2026-10-07-bots-full-clips.md.
Runtime-проверка preflight/re-enable ещё выполняется; исходный рефактор ботов пока не переключён.

## Docs/README.md

| `BotRuntimeController`, `BotCombatDriver`, `BotWeaponPoseSolver`, `WeaponAutomationCapability` | Единый порядок кадра бота, свежие shot intents, общий read-only capability и фактические SDK хваты. Ticket 112: AK105 31/31, Browning 26/25 запросов/снарядов; Android и existing Bots PASS. Cold-grab Herrington исправляет владелец weapon-system; lifecycle, визуальная/сетевая/Quest-приёмка остаются — [интеграция](tasks/bots-blaze-integration.md), [аудит](audit/bots-architecture-2026-10-07.md), [план](plans/2026-10-07-bots-control-blaze.md), [патчи Blaze](BlazeAI/sdk-patches.md) |

## Docs/UltimateXR/sdk-patches.md

## Патч 56: общий semantic admission программного захвата (bots-fix, 2026-10-07)

Файл: `Runtime/Scripts/Manipulation/UxrGrabbableObject.cs`.

`GrabObject` — исполнитель, который не проверяет eligibility. Бот раньше вызывал его
непосредственно после authority, обходя IsGrabbable, disabled point, сторону руки и CanGrabDelegate.
Добавлен read-only `CanBeGrabbedByGrabberWithoutProximity(grabber, point)` для программной
выдачи: тот же общий predicate, что physical `CanBeGrabbedByGrabber`, без coarse/proximity range.
Остальные guards и игровой CanGrabDelegate сохранены; disabled grabber также отклоняется.
Старый двухаргументный physical API сохраняет проверку расстояния и прежнее поведение.
Боевой автор перед фактическим GrabObject всё равно проверяет StateEventAuthority.

Программный primary бота использует новый query; support использует полный physical query
после первой Manipulation основного хвата и окончания smooth transition.
Ticket 112: primary/support фактически удерживают AK105/Browning, после Manipulation residual
практически нулевой; production запросы/снаряды 31/31 и 26/25. Android PASS, существующие Bots 30/30.
Повторный захват и разные аватары ещё проверяются; новые тесты механики — после приёмки.

## Патч 62: IK руки сохраняет входную ориентацию кисти (bots-fix, 2026-10-07)

Файл: `Runtime/Scripts/Animation/IK/UxrArmIKSolver.cs`.

`SolveIKPass` сохранял позицию кисти до `ResetClavicle`/`SolveClavicle`, а ориентацию —
после поворота родителя. Поэтому вращение clavicle переносилось на цель кисти;
проход завершался уже с другой world rotation, даже при сохранённой позиции.
Теперь входная ориентация сохраняется до изменения clavicle и восстанавливается
вместе с позицией; финальная запись использует ту же ориентацию. Ограничение досягаемости
позиции остаётся в прежнем решателе. Ботам не добавлен второй postprocess-писатель.

RED: ticket 157, временный probe на собственных MEF и Cyborg: после поворота clavicle
и вызова `ResetClavicle` обе руки потеряли 26–40° входной ориентации, положение
сохранилось с погрешностью меньше 0.002 мм. Transform/cache восстановлены после probe.
Это подтверждение дефекта прохода IK; источник каждого остаточного смещения в обычном
PostProcess этим probe отдельно не установлен. Heavy исключён из текущей матрицы.
GREEN: ticket 161, обе руки MEF/Cyborg × pistol/rifle: ошибка ориентации 0°,
позиции <0.002 мм. AndroidCompileGate PASS, существующие Bots 30/30 PASS.
Это сохраняет целевую world rotation; анатомия пальцев/локтей требует отдельной оценки.

## Патч 61: read-only запрет SDK-хвата при внешней экипировке (bots-fix, 2026-10-08)

Файлы: `Manipulation/IUxrGrabAvailability.cs`, `UxrGrabbableObject.cs`,
`UxrGrabManager.Manipulation.cs`. Номер зарезервирован в pipeline до реализации.

Generic provider выводит запрет из серверного equipment claim. Getter `IsGrabbable`
сохраняет исходную `_isGrabbable` и добавляет этот запрет; setter/state save не меняются.
Provider ищется в актуальной иерархии без кеша, включая inactive components: detach вложенной
детали или disable binding не оставляют stale availability. SDK не зависит от сборки игры.

Та же проверка стоит в private low-level `GrabObject` до BeginSync/anchor/holder mutations:
прямой и полученный сетевой вызовы не обходят claim. Обычные replay не перепроверяют proximity
или `_isGrabbable`. Binding откатывает предсказанный VR-хват при получении серверного snapshot
через существующий receive path SDK, без второго ReleaseObject и исходящего event echo.

Статус: source/локальная компиляция; runtime RED/GREEN ожидают снятия объявленного обслуживания
agent coordination. Изолированный probe подготовлен, производственные consumers не подключены.
Это не подтверждение стрельбы, remote/Quest или контакта хвата.
