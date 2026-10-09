# Архитектура оружейной системы

Состояние на 2026-10-09 (этап `waves-f` влит, 5af1c7ef). Полная спецификация машины — `refactor-plan.md` п. 2–4.

## Главная идея

Всё поведение огнестрела — затвор, патронник, спуск, поза, звук, вибрация, подсказка — решает одна машина
состояний. Учёт патронов один, в SDK. У каждого канала вывода один исполнитель. До рефакторинга за одно и то же
отвечали 4–5 компонентов (`AutomaticWeaponSlideFeedback`, `WeaponMechanismVisuals`, `WeaponChamberingReminder`,
`ChamberPoseReturnDriver`, ветки SDK), они правили позу и звук друг за другом — отсюда классы ошибок 1–5
(п. 4.3 плана): HoldOpen закрывала пружина, двойной звук вставки, «подходит» против «можно сейчас» в кармане,
зависшая фаза показа, коллизия хэша Mirror.

## Слои (зависимости только вниз)

```
Данные        WeaponReadinessProfile (оси), WeaponMechanismRig (на хосте), WeaponAudioSet, WeaponHapticSet,
              WeaponFeedbackDefaults (ассет на категорию)
Машина        VrBattlegrounds.Weapons.Core (noEngineReferences): WeaponStateMachine, WeaponTransitions (таблица),
              LedgerView, ActionSample, WeaponEvent, LedgerCommand, WeaponCue, PoseTarget, IWeaponOutput
Хост          WeaponSystem : MonoBehaviour (один на оружии; GUID прежнего WeaponReadinessController)
                ├─ порт учёта  UxrReadinessLedgerPort → UxrFirearmWeapon.Readiness
                ├─ датчики     ход Action, магазин, контекст, (спуск — этап E)
                └─ исполнители WeaponPoseExecutor, WeaponAudioExecutor, WeaponFeedbackExecutor
                               (+ WeaponHapticOutput — одна точка отправки вибрации)
SDK           UxrFirearmWeapon(.Readiness/.AmmoAdmission), UxrFirearmMag, UxrProjectileSource, state sync
Транспорт     NetworkStateRelay (UXR-события), CartridgeIntake (единственные Mirror Cmd оружия)
```

- Машина не знает ни SDK, ни Unity — это гарантирует компилятор. Механическое состояние машина не хранит:
  на каждом шаге выводит его из снимка учёта `Derive(LedgerView, профиль)`. Машина хранит только жест руки,
  эпизод спуска, фазу показа и защёлку подсказки — то, чего нет в учёте и что не реплицируется.
- Датчики и исполнители — обычные C#-объекты внутри хоста, а не компоненты префаба: второго исполнителя канала
  на префаб поставить нельзя.
- Исполнители не читают учёт и ничего не решают: получают цель от машины (`PoseTarget`, `WeaponCue`,
  `WeaponHapticCue`, `Hint`).

## Учёт (SDK) — единственный источник

- M (патроны в магазине) живёт в `UxrFirearmMag.Rounds`; C (патронник), флаги Action, последовательности и
  ревизия — в `RuntimeTriggerInfo.Readiness`. Какой магазин вставлен — знает только гнездо
  (`GetCurrentReadinessMagazine()`), учёт копии не хранит (этап C2, SDK-патч 53).
- Каждая команда машины несёт ожидаемую ревизию; SDK отклоняет команду, если учёт изменился. Каждая операция
  объявляет точную дельту M; при replay несовпадение → берётся значение автора + `GameLog.WeaponSystem.Error`,
  стрельба не блокируется. Форк ревизии — серверная поправка.
- Команда учёта, выданная внутри чужой синхронизации SDK (`BeginSync`), не ушла бы в сеть (класс Б): хост
  ставит события SDK в очередь со снимком и разбирает её с верхнего уровня.

## Сеть

| Вопрос | Решение |
|---|---|
| Автор | `StateEventAuthority.IsAuthorOfItem(weapon)`: машина держателя; без держателя — сервер/офлайн. Команды учёта выдаёт только автор вне replay |
| Что реплицируется | Только фиксации учёта (UXR state sync), выстрел с дробью, `CommitAmmoAdmission` от сервера. Жест, эпизод, фаза показа — нет |
| Наблюдатель | Получает фиксацию → машина в роли Observer → поза и звуки механизма слышны всем. Сухой щелчок, отказ, вибрация и подсказка — только автору |
| Поздний вход | `SnapshotLoaded`: жест сброшен, эпизод Consumed, показ выводится заново (HoldOpen → сразу зад) |
| Mirror | WeaponSystem не вводит своих Cmd/Rpc. Cmd только у `CartridgeIntake`, имена не менять (хэш) |

## Каналы и владельцы

| Канал | Владелец |
|---|---|
| Ручка в руке | SDK grab (+ `PumpGrabFollow` для помп) |
| Ручка без руки, связанные и нерычажные детали | `WeaponPoseExecutor` по цели машины |
| Звуки механизма, сухой щелчок, отказ темпа | `WeaponAudioExecutor` |
| Вибрация механизма/отказа, подсветка подсказки | `WeaponFeedbackExecutor` → `WeaponHapticOutput` (сейчас `UxrControllerInput`, позже `HapticService.Play`) |
| Звук выстрела, снаряд, дробь | SDK (`CommitShotSynced`) |
| Звук вставки/выемки | `AnchorSound` приёмника (не оружия) |
| Отдача корпуса | `RecoilAccumulator` + SDK; вибрация отдачи перейдёт к WeaponSystem |
| Решение о выстреле | пока SDK (`ProcessReadinessLocalTrigger`); на этапе E — машина (патч 64) |
| Препятствие у ствола | `BarrelObstruction`/`WeaponUseBlocker` (датчик + пока свой сухой щелчок) |
| Захват: что рука может взять | `GrabRules` (цепочка правил) → `ManipulationConstraints` (что запрещено) + `TwoHandGrabPolicy` (механика) |
| Вылет патрона и гильзы | `WeaponEjectionExecutor` по сигналам `ChamberEjected` и `CasingEjected`; экземпляры — `WeaponEjectaPool`; данные — `WeaponEjectionSet` (дефолт категории + оверрайд ствола), окно — `WeaponEjectionPort` |

## Сигналы машины (`WeaponCue`)

`ActionBack` (порог извлечения), `ActionForwardChambered`, `ActionForwardEmpty`, `ActionReturnPartial`
(закрытие без извлечения — отдельное событие, звук как у `ActionForwardEmpty`), `ChamberEjected` (извлечён живой
патрон), `CasingEjected` (стреляная гильза: фиксация выстрела у самозарядного; у ручного цикла — этап ejection-manual),
`SlideLockCatch`, `DryFire` (только автор), `Refusal` (нажатие в паузе темпа, только автор).
Вставки и выстрела в перечне нет — у них другие владельцы.

## Профили и данные ствола

- Оси профиля (`WeaponReadinessProfile`): FireMode (из спуска SDK), AmmoCapability, PhysicalCapability
  (NoAction/ActionTravel), ChamberPolicy (ManualReturn / AutoOnMagazineInsert / TriggerAssistPrepareOnly),
  EmptyPose (HoldOpen/ReturnToRest), ReleasedAction (Spring/Stay). Ассеты — `Assets/Data/WeaponSystem/Profiles/`.
- Данные конкретного ствола (порог извлечения, скорости, клипы, детали) — `WeaponMechanismRig` на хосте.
- Отклик: `WeaponFeedbackDefaults` на категорию (автомат, дробовик, пистолет —
  `Assets/Data/WeaponSystem/*FeedbackDefaults.asset`); на стволе — только оверрайды. Подстановка — одно правило
  `WeaponAudioSet.Resolve` / `WeaponHapticSet.Resolve` в момент проигрывания: набор ствола целиком → дефолт →
  (для сухого щелчка) звук SDK.

## Авторинг

- `WeaponSystemAuthoring` — единственный writer префабов оружия: таблица `Weapons` (имя корня = рецепт
  сборщика; волна, профиль, категория, оверрайды клипов). Порядок: preflight на копии (те же проверки, что в игре,
  отчёт «откуда звучит каждый сигнал», «тишина» < −40 dBFS — ошибка) → запись → `SaveAsPrefabAsset` →
  нормализация `_assetId` → readback (GUID, fileID, масштаб, хваты не меняются) → повторный apply не меняет байты.
- Сборщики (`HandsPackWeaponBuilder`, `KinemationWeaponBuilder`, …) берут профиль из той же таблицы.
- Звуки: конвейер `Tools/Audio/audio_cut.py` (analyze/cut по рецепту, `--check`), импорт `SfxImportSettings`,
  уровень `SfxClipLevel`. Описание — `Docs/sound-library.md`.

## Стволы (2026-10-09)

| Группа | Стволы | Профиль |
|---|---|---|
| D (пилоты) | Herrington, FABARM SDASS | ручной Action, FixedStore + ручное заряжание |
| F1 | BrowningHiPower, Viper, TR15 | съёмный магазин, ручное досылание, HoldOpen |
| F2 | AK105, AR15, Mk14, MKR9, SRM12 | то же, ReturnToRest |
| F3 | PPK, MP5K, Scar, Uzi, SniperRifle | то же, затвор без клипов |
| F4 | SDKGun, Machinegun | без Action, досылание при вставке |
| F5 (не переведены) | Revolver, R08, SDK Shotgun | legacy; развилка (roadmap) |

Обзорные копии стены лобби (`Prefabs/Weapons/SightReview/*`) переводятся вместе со своим стволом.
Категории: дробовик — Herrington, FABARM, SRM12, SDK Shotgun; пистолет — Browning, Viper, PPK, Uzi, SDKGun,
Revolver, R08; остальное — автомат.

## Отладка

Панель состояния: меню `Tools/VR Battlegrounds/Debug/Weapon State Panel` (или `WeaponStatePanelSettings.Enabled`)
— состояние машины, учёт, патронник у каждого ствола в Play Mode. Лог — канал `GameLog.WeaponSystem`.

## Соседние владельцы (не писать без согласования)

| Область | Задача-владелец |
|---|---|
| `Assets/Scripts/Weapons/Equipment/**`, `WeaponEquipmentBinding`, `BotGunner` | bots-fix (контракт `weapon-equipment-binding@1`) |
| `VrBattlegrounds.Haptics`, `HapticService` | haptics-system (контракт `haptics-api@1`) |
| Стена арсенала, `WeaponInfo`, генератор | arsenal-generator |
| Позы рук аватаров | hand-rig-quality |
