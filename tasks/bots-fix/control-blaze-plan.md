# Управление ботами и адаптер Blaze — план реализации

Исторический инженерный план. Текущий статус: [Readme.md](Readme.md).

> Исполнитель: применять superpowers:executing-plans по шагам. Делегирование отдельно не запрошено.

**Цель:** вернуть настоящую стрельбу и правильный хват, убрать встречные зависимости и неявное владение состоянием.
**Архитектура:** Director владеет сессией/закупкой, один runtime-контроллер — переходами и порядком кадра тела,
Blaze — тактикой. SDK остаётся единственным владельцем захвата, боезапаса и выстрела;
решатель позы использует фактические snap-привязки оружия.
**Стек:** Unity 6, Blaze AI, UltimateXR, Mirror, Android/Quest.
**Основание:** [аудит и runtime-доказательства](../audit/bots-architecture-2026-10-07.md).
**Статус:** принят пользователем 2026-10-07, реализация идёт в bots-fix.

Проверенный input `08c4e212`, ticket 112: AK105 31/31, Browning 26/25 запросов/снарядов,
Android PASS, existing Bots 30/30. Основной runtime/pose/capability срез реализован.
Открыты отрицательные/lifecycle/матрица аватаров и пользовательская приёмка.
Cold-grab воспроизведён красным; исправление владельца оружия проверяется отдельным overlay.
Согласованная граница: readiness меняет weapon-system, бот использует только RequestAutomationPreparation.

## Ограничения

- Не создавать собственный AI вместо штатных Alert/CoverShooter/GoingToCover.
- Синхронизируемое действие исполняет только StateEventAuthority; M/C хранится только в SDK.
- Нельзя разрешать отсутствие capability профиля молчаливым legacy fallback.
- Редактор только через broker; до request конкретный пакет, finish сразу после завершения.
- Новые тесты механики и переписывание старых ожиданий — после проверки человеком в Unity/шлеме.
- Принятый коммит — после пользовательской проверки; сейчас только документация и допустимые checkpoints.
- Геометрия production карт и общие player/avatar-префабы в этом срезе не требуют изменения.
  Если изменение аватара станет необходимым, вместе обновить AvatarLoadoutTests/PrefabCompositionTests
  после приёмки по правилу проекта; не расширять scope скрыто.
- Любой необходимый SDK-патч документировать в соответствующем sdk-patches.md.

## Владельцы и интерфейсы

| Состояние | Единственный владелец | Потребители |
|---|---|---|
| Сессия, команда, фаза карты | существующие PlayerSession/MapReferee | Director, snapshot кадра |
| План/оплата закупки | Director + BotMind, round/body token | runtime получает заявку и возвращает результат выдачи |
| Режим тела, binding epoch, переходы | BotRuntimeController | Driver, Navigator, Gunner, Body |
| Боевой враг, путь/укрытие, таймеры атаки | Blaze на сервере | Driver экспортирует intent/позу, не хранит второго врага |
| Захват и владелец оружия | UxrGrabManager/UxrWeapon | Gunner хранит binding, сверяет действительное удержание |
| Capability | WeaponInfo профиль, проверенный против SDK | общий валидатор для закупки, binding и diagnostics |
| Право подготовки, chamber evidence | WeaponReadinessController | Gunner запрашивает подготовку и получает результат |
| Боезапас/готовность/снаряд | UxrFirearmWeapon ledger/legacy SDK | readiness и Gunner; собственных ammo-копий нет |
| Желаемые кисти | BotWeaponPoseSolver, результат кадра | BotBody единолично применяет до Manipulation |
| Финальная constrained-поза | SDK manipulation/IK | диагностика читает, не исправляет после решателя |

Планируемые типы в `Assets/Scripts/Bots/`:

- `BotControlContext`: readonly снимок body epoch, stage, alive, weapons admission и loadout token.
- `BotShotIntent`: request id, frame, body epoch, weapon binding, target; не сериализуется как второй AI state.
- `BotShotResult`: Fired либо причина (NoWeapon, InvalidCapability, Preparing, NoLegalTarget,
  Occluded, NotAuthor, PhaseBlocked, StaleIntent, SdkRejected). Счётчики без покадрового спама GameLog.
- `BotRuntimeController`: устанавливает контекст и исполняет стадии; очистка intent/pose при выходе из Combat.
- `BotWeaponPoseSolver`: вход — базовая поза, binding primary/support и aim point;
  выход — согласованные кисти. Не пишет Transform и не стреляет.

Gunner не ищет Driver через GetComponent; Driver не вызывает методы Gunner из shootEvent.
Runtime получает intent от адаптера и передаёт его стрелку. Контекст кадра не становится
второй изменяемой копией PlayerSession/WeaponsEnabled; перед действием проверяется актуальный admission.

## Шаг 1. Наблюдаемость и действительная цель

Файлы: `Assets/Scripts/Debug/BotCombatStand/BotCombatStand.cs`, `BotStandMeasurements.cs`,
`Assets/Editor/VR_Battlegrounds/Bots/BotStandRecorder.cs`, `BotStandHandMotionRecorder.cs`.

- [x] В T02 измерять фактически выбранного врага каждого бота; fixture target хранить отдельно.
- [x] Отсутствующую цель/наблюдение представлять явно; не выдавать нулевую дистанцию за измерение.
- [x] Добавить замеры requested/rejected по причинам/emitted, primary/support holders,
  snap residual в мм/градусах до/после Manipulation и после PostProcess.
- [x] Повторить красные T02 Pistol/Rifle: сохранить отсутствие профиля, 0 снарядов и корректную цель.
- [ ] До изменения chamber automation воспроизвести Initialize-before-grab и Equipment→Combat
  на реальном профилированном оружии; сохранить state/revision/holder/correlation.

Результат: отказ локализуется одной причиной; новые постоянные NUnit-тесты не добавлены.

## Шаг 2. Замкнуть оружейный контракт и подготовку

Файлы: `Assets/Scripts/Weapons/WeaponAutomationCapability.cs` (новый узкий валидатор),
`WeaponReadinessController.cs`, `Assets/Scripts/Bots/BotShopper.cs`, `BotGunner.cs`,
`Assets/Editor/VR_Battlegrounds/Bots/BotWeaponCapabilityMigration.cs` (новый),
`Assets/Data/Weapons/*_Weapon.asset`, `Assets/Data/Weapons/Profiles/LegacyAmmoReadiness.asset` (новый).

- [x] Определить единый Resolve/Validate для WeaponInfo и текущего firearm:
  profile, ammo mode, controller configuration, trigger 0 и grip readiness согласованы.
- [x] Через scoped Editor-миграцию назначить явный legacy-профиль только проверенным legacy-префабам;
  никаких массовых SaveAssets. Chamber/fixed-store не переводить в legacy.
- [x] Закупка использует тот же валидатор до оплаты; неконфигурированное оружие даёт явный отказ.
  Поддержать все capability, уже разрешённые актуальным игровым каталогом, включая fixed-store;
  validation не должна молча сокращать каталог ради зелёного результата.
- [ ] После подтверждения гипотезы шага 1 проверить исправление выдачи права в readiness owner.
  Отдельный automation begin отменён после согласования: создавал бы второй lifecycle у бота.
  Единственный вход — RequestAutomationPreparation; право выводит владелец из контекста.
  Не расширять manual authority и не писать боезапас в обход SDK.
- [ ] Обслуживать preparation каждый разрешённый тик до shot intent, включая первый выстрел
  и опустошение магазина; сохранять физические Empty/HoldOpen и ограничения CanUse.
- [ ] Получить реальные ProjectileShot и ShotsFired на тех же T02 Pistol/Rifle;
  проверить первый выстрел, пустой магазин, потерю/повторный захват, неподходящую capability.

Результат: выбранное разрешённое оружие исполняемо; отказ заранее объясним без второго ammo writer.

## Шаг 3. Один порядок исполнения и lifecycle тела

Файлы: новые `BotRuntimeController.cs`, `BotControlContext.cs`, `BotShotIntent.cs`,
`BotShotResult.cs` в `Assets/Scripts/Bots/`; существующие Director, CombatDriver, Gunner,
Body, Navigator, Mind, Orders в той же папке.

- [ ] Director оставляет сессии/команды/закупку/смену тела, передаёт единый control context.
  Сделать заявку закупки одноразовой по token; результат выдачи отделить от оплаты.
  Ready объявлять после выполненного согласованного loadout либо явного разрешённого fallback.
  В закупке проверяется выдача/захват/конфигурация, не IsReadyToFire: CanUse до Combat запрещён,
  поэтому ожидание боевой readiness здесь создало бы взаимную блокировку запуска раунда.
- [ ] Runtime единолично переключает Idle/Return/Armed/Combat/Disabled и body/binding epoch.
  Сохранить стабильный визуальный источник, включать/выключать боевые модули без второго live rig.
- [ ] Adapter оставляет setup/filter и чтение Blaze; callback экспортирует intent с конкретным врагом.
  Убрать встречную зависимость Gunner↔Driver и неиспользуемую Hunt/Engage-политику Combat.
- [ ] В Update выполнять управление loadout/navigation; перед Manipulation, после оценки Animator,
  вычислять базовую стойку/прицел и применять кисти один раз. Конкретная UXR stage —
  StageUpdating(Manipulation); подтвердить порядок короткой трассой до включения.
- [ ] После StageUpdated(Manipulation) проверить актуальную фазу, legality, authority, muzzle LOS,
  weapon binding и исполнить один актуальный intent. Intent живёт один кадр, не переносится
  на другую цель/оружие/тело; отклонение отражается в BotShotResult.
- [ ] На переходе фазы, disable, смерти, смене тела/карты очищать intent/pose/bindings,
  отписывать callbacks, освобождать cover occupancy, затем удалять свой rig.
- [ ] Проверить несколько раундов, Combat→Equipment, смерть→призрак→респавн,
  смену команды/скина/карты и остановку сервера; rig count 0/1, без старых subscribers/intent.

Результат: подготовка, поза и выстрел исполняются в проверяемом порядке без взаимного восстановления состояния.

## Шаг 4. Хват и прицел по геометрии оружия

Файлы: новый `Assets/Scripts/Bots/BotWeaponPoseSolver.cs`; Gunner/Body/CombatDriver;
при необходимости `BotCombatAssets.cs`, `Assets/Editor/VR_Battlegrounds/Bots/BotCombatSetup.cs`.

- [ ] На binding вычислить primary/support snap для actual avatar, side и текущего оружия;
  использовать SDK ComputeRequiredGrabberTransform и преобразование grabber→wrist.
  Support выбирать по объявленному support grip, не брать action/затвор/магазин.
- [ ] Если семантика support в текущих данных недостаточна, добавить её в единый weapon grip
  descriptor `Assets/Scripts/Weapons/WeaponGripBinding.cs` и scoped validator для всех затронутых
  оружий; хранить только роли/индексы существующих UXR points, не создавать bot-only копию snap poses.
- [ ] Основная рука задаёт положение оружия, реальный ShotSource — прицел;
  roll брать из базовой стойки, не принудительно обнулять LookRotation(Vector3.up).
  Оба запястья рассчитываются из одного weapon pose; относительный хват не перемеряется каждый кадр.
- [ ] Выполнить штатный SDK primary и support grab с проверкой автора и допустимости;
  finger pose принадлежит SDK. Учитывать MainGripAimLock и SDK look-at averaging.
  У оружия без валидной поддержки — только объявленный одноручный режим.
- [ ] Убрать немедленные writes из SetWeaponHands и отдельный aim Update стрелка;
  BotBody — единственный применитель целевых головы/кистей, SDK сохраняет constraints/IK.
- [ ] Проверить MEF/Cyborg × Pistol/Rifle, idle/move/strafe/shoot,
  конкретные стволы разных размеров; получить side/front снимки после SDK и residuals.
  Предварительный инженерный ориентир rigid snap: ≤5 мм/≤5° после стабилизации;
  допустимый residual для shape/constraints задаётся их контрактом, визуальное качество отдельно.
  Heavy исключён из текущего анализа и проверок по прямому поручению пользователя.
  Инженерная матрица ticket163 завершена; визуальное качество ещё не принято.
  [Результаты RED/GREEN и границы проверки](../audit/bots-verification-2026-10-07.md).
- [ ] Проверить отсутствие нарастающего drift минимум 30 с idle, движение и смену оружия;
  сохранение LOS, реальные выстрелы и корректный release/drop обеих рук.

Результат: руки/пальцы соответствуют конкретным хватам, клип задаёт стойку, а не контакт.

## Шаг 5. Проверка и закрепление принятой логики

- [ ] После каждого игрового среза получить `AndroidCompileGate.Run()` PASS через worker;
  existing EditMode запускать без изменения ожиданий до приёмки механики.
- [ ] Итоговый runtime: исходные T02 Pistol/Rifle, союзник/смена роли, LOS-стена,
  полный/неполный путь, холодный chamber, последний патрон, lifecycle.
  Любая заявка имеет emitted либо зарегистрированную причину отказа.
- [ ] Приложить Unity/headset-чеклист: две руки/пальцы, прицел/стрельба, смерть/drop,
  смена карты/скина; отдельно host+remote client — один снаряд/один урон, правильная репликация хвата.
  Quest 2/3 — CPU/GC на выбранном числе ботов. Локальный сервер не считать этой приёмкой.
- [ ] Перечислить устаревшие тесты до изменения: BotAuthorityTests привязан к тексту Gunner
  и устаревшему Update; BotOrdersTests требует Hunt/Engage. После подтверждения пользователем
  заменить их проверками ownership/intent lifetime/admission, закрепить принятые weapon/pose контракты.
- [ ] Обновить `Docs/gameplay.md`, `Docs/tasks/bots-blaze-integration.md`,
  `Docs/tasks/bot-combat-stand.md`, troubleshooting и индекс Docs/README адресно по итоговой реализации.
- [ ] Интеграция/коммит только после принятия; перед вливанием rebase origin/dev,
  адресное повторение проверок при затронутом коде/ассетах, соблюдение pipeline проекта.

## Фокус ревью

1. Намерение выстрела не переживает смену цели, тела, оружия или команды.
2. Профиль отсутствует/несогласован: нет оплаты неисполняемого loadout и нет скрытого fallback.
3. Chamber initialized до захвата/CanUse: automation получает новое валидное право, чужое evidence не наследует.
4. Две руки: support не превращается в writer направления вопреки MainGripAimLock/SDK контракту.
5. Return/Combat и teardown: ровно один rig, один pose applier, нет выстрела после закрытия admission.

Текущие проверки и решения исполнителя: `tmp/bots-refactor-progress.md`.
