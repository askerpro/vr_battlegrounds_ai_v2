# Единая WeaponSystem — подробности

## Факты и границы

Технические документы задачи (полная спецификация; в тематическую документацию `Docs/` переносятся
отдельным этапом после приёмки):

| Документ | О чём |
|---|---|
| [refactor-plan.md](refactor-plan.md) | Архитектура, таблица переходов, этапы, решения S1–S4 (п. 5.2) |
| [ledger-single-source.md](ledger-single-source.md) | Учёт без копий физического состояния (C2), проба форка ревизии |
| [haptics-research.md](../../Docs/tasks/haptics-research.md) | Исследование вибрации; сервис ведёт задача `haptics-system` |
| [weapon-readiness-feedback-design.md](../../Docs/tasks/weapon-readiness-feedback-design.md) | Предыдущая архитектура готовности (этапы 1–3), которая заменяется |

Задача пишет: `Assets/Scripts/Weapons/**` (кроме `Equipment/**` — bots-fix), SDK `Mechanics/Weapons/UxrFirearm*`
и `UxrWeapon.Custom.cs`, на волнах F — префабы и данные оружия, сборщики `Editor/VR_Battlegrounds/Gameplay/`,
тесты `Assets/Tests/EditMode/Weapons/**`. Не пишет: `WeaponEquipmentBinding` (bots-fix), `VrBattlegrounds.Haptics`
(haptics-system), стену арсенала (arsenal-generator), общие журналы и индексы.

## Сделано

| Что | Коммит |
|---|---|
| Ручное заряжание дробовиков (FABARM, Herrington); бесконечная выдача патронов карманом и арсеналом; один звук вставки — у приёмника | e15c53ce |
| HoldOpen у Herrington | 66dfba8e |
| План рефакторинга принят | 6259ba33 |
| Этап B: машина состояний + структурные тесты | 573ddab1 |
| Этап C: теневой режим + дебаг-панель (только редактор) | 06624cb9 |
| Этап C2: учёт без копии магазина, SDK-патч 53 — сетевая ошибка Herrington (форк ревизии) исправлена; попутно предупреждение о кинематическом теле при выдаче магазинов арсеналом | 38381e54 |

## Решения пользователя

| Тема | Решение |
|---|---|
| S1 | ствол без анимации пустого: на последнем выстреле обычная анимация выстрела |
| S2 | спуск до конца паузы между выстрелами — отрицательная вибрация + звук `UI_Error_Subtle_Deep`, без подсветки; у автоматического оружия нажатие в паузе — выстрел по её окончании, как SDK (решение владельца, пользователю сообщено) |
| S3 | закрыто архитектурно (C2) |
| S4 | отпущенная помпа FABARM остаётся на месте; ось профиля «пружина / остаётся» |
| Звук оттягивания | чинится на этапе D |
| Число патронов не сошлось | `GameLog.WeaponSystem.Error`, игру не ломать |
| Расхождение версий учёта | серверная поправка (В-Л6) |
| Сбой уведомлений учёта | `Error`, стрельба не блокируется (В-Л5) |
| Смена магазина при открытом затворе | нужен новый цикл |
| Звук «патроны заканчиваются» | слой к выстрелу на последних N, слышат все, порог в профиле ствола (D/E) |
| Вибрация | отдельная задача `haptics-system`, пишет под будущую WeaponSystem; отдача — сигнал WeaponSystem |

## Архитектурный анализ

- Контекст использования (`use-context`): `WeaponUseContext.Resolve(weapon, trigger)` → `Kind` (None / VrHand /
  Equipment), `User`, `Hand`, `AuthoredHere`, `Epoch`. Хват SDK и экипировка NPC взаимоисключающие; если оба
  активны — `None` и `Error`. Право автоматики ключуется по `(Kind, User, Epoch)`; временный G1
  (`_automationGrantPending`, не влит) этим заменяется.
- Физическая отдача корня: в режиме Equipment SDK-отдача не применяется (отдачу NPC даёт клип Animator), порт
  в `UxrFirearmWeapon`, SDK-патч 63. В режиме VR-руки — как сейчас.
- Спуск в машину (этап E) — SDK-патч 64.

## Координация

- bots-fix: контракт BOTS-FULL-CLIPS-CONTRACT-20261007-1, ответы —
  `.agent-state/coordination/weapon-system-to-bots-full-clips.md`. Binding реализован, проверка в worker ждёт
  окончания обслуживания протокола.
- haptics-system: контракт для этапа D (`HapticService.Play`, `IWeaponRecoilHapticsOwner`) согласован сообщениями
  2026-10-07; оформить ревизией контракта при строгом режиме.
- Волны F — после map-runtime-bootstrap 2, arsenal-generator 3 и bots-fix (`pipeline.md`, подтверждён пользователем).

## Проверки и расследования

- C2: проба RED (аренда 130) → GREEN (153, 156) по 9 сценариям; Android PASS; шлем 2026-10-07 — Herrington
  и FABARM работают, `[WeaponLedger]` в логе нет. Локальные отчёты — `tmp/ledger-probe/` (перенести в
  `tasks/weapon-system/reports/`).
- `machine-s124`: аренда 170 — `AndroidCompileGate` PASS, `VrBattlegrounds.Tests.Weapons` 16/16. Таблица — 83
  перехода. Шлем: цель — ноль расхождений тени на Herrington и FABARM, кроме Н6.
- `drive` (2026-10-08, не влит): все сборки компилируются офлайн (`tmp/weapon-shadow/compile.ps1`), включая
  тесты. В Unity не проверено: на тикеты 219 и 220 брокер сразу дал OFFERED, но отказал в claim — «нужен
  чистый редактор на принятой базе». Изменения не применялись, оба тикета отменены. План аренды:
  1. `WeaponSystemAuthoring.MigratePilots()` — preflight, правка ассетов, readback: GUID, fileID, `_assetId`,
     масштаб и хваты не меняются; удаляются ровно AWSF, WMV, Reminder, `UxrShotgunPump` и два отсутствующих скрипта.
  2. `AndroidCompileGate`.
  3. EditMode-тесты.

## Этап drive: решения реализации и открытые вопросы

| Тема | Решение |
|---|---|
| События SDK | Фиксации, фронты спуска и хват приходят внутри синхронизации SDK; команда там была бы отклонена (класс Б). Хост ставит событие в очередь со снимком учёта, контекста и хода и разбирает её с верхнего уровня. Фронт спуска снимается до выстрела, поэтому ложного сухого щелчка после последнего патрона нет |
| До этапа E | `Shoot` и `RequestAdmission` порт не исполняет: решают спуск SDK и `CartridgeIntake`. Запасной щелчок SDK подавлен подпиской на `LocalTriggerAttemptDecided`. `DryFire(Obstructed)` и его вибрация остаются у `BarrelObstruction` |
| Вибрация | Контракт haptics-api, ревизия 2. Клипы `UxrHapticClip` лежат в `WeaponHapticSet` хоста (`ActionRear`, `NotReady`, `Faulted`, `RateOfFire`). Набор отдельный, рядом с `WeaponAudioSet`: у звука и вибрации разные исполнители, данные канала — у его исполнителя. Отправляет одна точка `WeaponHapticOutput`, пока через `UxrControllerInput.SendHapticFeedback(side, clip)` и только локальной руке. Замена на `HapticService.Play(clip, hand, role, gain)` — одна строка. Значения по умолчанию пишет `WeaponSystemAuthoring`, и только в пустой клип: отказы — RumbleFreq 0,4, 0,06–0,15 с; ход — Click 0,25. Формы двойного и тройного импульса и приоритет High/Normal появятся с патчем 66. Отдачу даёт SDK, `IWeaponRecoilHapticsOwner` не реализован |
| Звуки | Переносятся по смыслу события. У помпы `_audioSlide` — оттяжка. У затвора сборщиков `_audioSlideBack` — оттяжка (рецепт `SlideBackAudio`). Прежний AWSF играл поля затвора наоборот. Herrington: оттяжка — `Herrington_BoltBack`, досылание — `Herrington_BoltForward` |
| Тень | Удалена: машине больше не с чем сравниваться. `WeaponShadowSettings` остался только ради меню `Debug/WeaponSystemShadowMenu.cs` (вне области этапа); удалить вместе с меню |
| `FormerlySerializedAs` | `_triggerIndex` и `_profile` сохранили имена. Остальные поля переехали внутрь `_rig` (вложенный класс), атрибут туда не дотягивается. Их перезаписывает writer |
| Изменение поведения | Вибрация NotReady — на любую причину, раньше только ChamberingRequired. Частичная оттяжка теперь звучит при закрытии (Н1) |

**Открыто (файлы вне области `drive`):**

- Абстрактный `WeaponReadinessController` — переходное имя хоста. Его используют `BotGunner` (bots-fix),
  `PumpGrabFollow`, `WeaponLedgerIntegrity` и `ManualLoadingPrefabTests`. Убрать, когда эти файлы перейдут
  на `WeaponSystem`.
- `ArsenalWeaponDiagnostics` (Editor/Arsenal) пометит у пилотов «Нет WeaponMechanismVisuals».
- Устаревшие ожидания тестов (п. 6 плана) — список уточняется прогоном.

## Этап waves-f

Реализован 2026-10-09, проверен в worker (аренды 240 — отказ readback по `_assetId`, без записи; 241 — применено).
Шлем впереди. Локальные отчёты: `tasks/weapon-system/reports/waves-f/` (preflight, migrate2-*.json).

**Дефолты отклика по категориям** (решение пользователя 2026-10-09).

| Что | Решение |
|---|---|
| Данные | `WeaponFeedbackDefaults` (`Assets/Scripts/Weapons/WeaponSystem/`): категория, `WeaponAudioSet`, `WeaponHapticSet` — те же типы, что у ствола. Ассеты `Assets/Data/WeaponSystem/{Rifle,Shotgun,Pistol}FeedbackDefaults.asset` |
| Ствол | Хост хранит `_feedbackDefaults` (ссылка на ассет категории); его `_audio`/`_haptics` — только оверрайды |
| Подстановка | Одно правило `WeaponAudioSet.Resolve` / `WeaponHapticSet.Resolve`: сначала набор ствола целиком (досылание без своего звука — звук закрытия ствола), затем дефолт. Сухой щелчок без источника — звук SDK. Исполнители вызывают правило при каждом проигрывании: правка ассета действует сразу |
| Writer | Дефолты не копирует. Ставит ссылку категории; оверрайд, равный дефолту (прежние копии отказа S2 и вибраций пилотов), снимает. Звуки механизма из прежних компонентов — данные ствола, остаются оверрайдами. Отчёт: событие → оверрайд / дефолт / звук SDK / «нет вовсе» (ошибка preflight) |
| Начальные звуки | Автомат — AK105 (`AK105_BoltBack/Forward`, затвор Kinemation, эталон автомата); дробовик — помпа `UxrShotgunPump` (`ShotgunPump01/02`, FABARM и SDK Shotgun); пистолет — Viper (`Viper_BoltBack/Forward`, стартовый пистолет). Сухой щелчок — `TriggerNoAmmo` (он же у спусков SDK всех стволов), отказ — `UI_Error_Subtle_Deep_stereo` |
| Необязательные слои | Извлечение живого патрона и задержка затвора: клипов в проекте не назначено ни одному стволу, в дефолтах пусто. Это слои поверх оттяжки и позы HoldRear — молчание допустимо, preflight его не считает ошибкой |
| Вибрация | Одинакова для трёх категорий (значения пилотов drive): отказы RumbleFreqNormal 0,4 (0,08 / 0,15 с), темп RumbleFreqLow 0,4 (0,06 с), ход Click 0,25 |
| Категория ствола | Явная таблица `WeaponSystemAuthoring.Weapons` (имя корня = рецепт сборщика): в `WeaponInfo` есть только Rifle/Pistol, дробовика нет (SRM12, Herrington — Rifle), `ShotgunPellets` есть не у всех дробовиков. Preflight сверяет «пистолет» таблицы с `WeaponInfo.Category` (только чтение). Ствол без строки writer не настроит |

Категории на waves-f: дробовик — Herrington, FABARM, SRM12, SDK Shotgun; пистолет — Browning, Viper, PPK, Uzi (в арсенале
Pistol), SDKGun, Revolver, R08; автомат — остальные. **Устарело (этап ejection):** SRM12 — Desert Tech SRS, не дробовик;
добавлена четвёртая категория «снайперская винтовка» (SRM12, SniperRifle = AX-50). Актуально — `Docs/weapons/architecture.md`.

**Волны.** Таблица волн, профилей и категорий — одна (`WeaponSystemAuthoring.Weapons`), её же читают сборщики
(рецепт без профиля → профиль волны). Профили — `Assets/Data/WeaponSystem/Profiles/`: `DetachableHoldOpenReadiness`
(F1), `DetachableReturnToRestReadiness` (F2, F3), `DetachableNoActionReadiness` (F4, AutoOnMagazineInsert).
Обзорные копии стены лобби (`SightReview/*`) переведены вместе с исходным стволом (вопрос В10); черновик
`SightCalibrationDrafts/MKR9_NativeProbe` не тронут (удаляется на cleanup-h). Затвор без клипов (F3): один жёсткий
Action — ручка, корпус — её родитель, зад — покой + полный ход. `WeaponInfo` не меняется.

Порядок миграции на ствол: preflight на загруженной копии (writer, проверка хоста `TryValidateConfiguration` —
те же правила, что в игре, отчёт отклика) → `LoadPrefabContents` → writer → `SaveAsPrefabAsset` → канонический
`_assetId` (`NetworkAssetIdNormalizer`: сохранение пишет 0) → readback → повторный apply, байты не меняются. Сбой —
побайтный откат волны. Удалённые `_uxrUniqueId` в диффе — только у снятых AWSF.

**F5 — развилка, не переведена.** Хост жёстко связан с учётом SDK с патронником; профиль `LegacyAmmo` он отвергает.

| Вариант | Суть | Цена и риски |
|---|---|---|
| **А (рекомендую). Револьвер = магазин + патронник без ручного хода** | Барабан — съёмный магазин, профиль как F4 (NoAction, AutoOnMagazineInsert): досылание при вставке и после каждого выстрела делает учёт SDK. Барабан R08 — деталь клипа `InMagazine` rig (уже поддержана) | Новой архитектуры нет, только строка таблицы. Учёт — «5 в барабане + 1 в патроннике», сумма та же; показ числа патронов у барабана нужно проверить в шлеме |
| Б. Порт учёта MagazineOnly | Интерфейс `IWeaponLedgerPort`, второй порт поверх legacy-счётчика SDK, синтетический `LedgerView` | Правка хоста, датчиков и порта (~300 строк), риск для пилотов; второй путь учёта |

SDK Shotgun: нужен `PumpGrabFollow` и корпус для помпы (как FABARM) — авторинг префаба; делать в том же проходе F5
после решения по револьверу.

**Боты.** В `dev` `BotGunner` требует `WeaponInfo.ReadinessProfile` = профиль хоста; у переведённых стволов поле
пустое (как и до этапа), бот из них не стреляет — так было и для legacy-стволов. В bots-fix
`WeaponAutomationCapability` читает `controller.Profile`: переведённые стволы проходят проверку определения,
подготовка — `RequestAutomationPreparation` (T44). Стрельба бота в Play не проверялась. `VrBattlegrounds.Tests.Bots`
зелёные.

**Проверка (аренда 241).** Миграция D–F4 `passed`; `AndroidCompileGate` PASS; EditMode (Weapons, Bots, Haptics,
префабы оружия, `WeaponDropPhysicsTests`, `OutOfWorldGuardTests`, `WeaponPartGrabTests`, `NetworkAssetIdOnDiskTests`,
`UxrUniqueId*`, Pump*) — 449, красных 7:

- устаревшие ожидания (переписать после приёмки): `WeaponSystemPilotPrefabTests.Звуки_механизма_из_WeaponAudioSet`
  и `.Вибрации_отказов_и_хода_не_пустые` (Herrington, FABARM) — ждут отказ и вибрации на стволе, теперь они в
  дефолте категории; проверять надо `Resolve`. Тем же правилом переписать проверки `host.Audio.For` в
  `WeaponFeedbackTests`/`KinemationWeaponTests` (пока зелёные: оверрайды механизма у переведённых есть);
- вне этапа: бюджет треугольников R08 (известно с drive); `WeaponShotSoundTests` — общий звук выстрела у стволов
  HandsPack; `WeaponDropPhysicsTests` — лог «Invalid serialized file header» `TestMap2/LightingData.asset` в worker.

Шире прогона: `GrabPoseCoverageTests`, `HandsPackHandPoseTests`, `KinemationHandPoseTests` красные по хватам аватаров —
этап их данных не меняет.

**Тесты после приёмки (2026-10-09, аренда 261).** Переписаны под `Resolve`: `WeaponSystemPilotPrefabTests` (звук, вибрации — из дефолта «дробовик»), проверки звуков хода в `WeaponFeedbackTests`/`KinemationWeaponTests`. Новые: `WeaponActionReturnPartialTests` (CloseOnly → ровно ActionReturnPartial, полный цикл → ActionForward*), `WeaponFeedbackResolveTests` (оверрайд/дефолт/SDK/None, частичный ход = ActionForwardEmpty), `WeaponSystemWavePrefabTests` (F1–F4 по таблице: один хост, нет прежних компонентов, категория/профиль/дефолты, `FeedbackReport` без ошибок; SRM12 `_Cut` не тишина; сторож уровня), `Tools/Audio/tests/test_audio_cut.py` (`cut --recipe --check`). EditMode 475: 473 зелёные, 2 вне этапа (треугольники R08, `LightingData` TestMap2); `AndroidCompileGate` PASS.

**Шлем, чек-лист по категориям** (все — из арсенала, обычная игра):

- Пистолет (Viper, PPK, SDKGun): оттяжка и возврат — звук Viper/«затвор» ствола; последний патрон Viper — затвор
  сзади, толчок вперёд досылает; PPK — затвор возвращается в покой; SDKGun — стреляет сразу после вставки магазина;
  спуск в паузе — тихий «UI_Error» и слабая вибрация; пустой — щелчок и двойная вибрация.
- Автомат (AK105, TR15, Scar, Machinegun): AK105 — звук своего затвора, частичная оттяжка без извлечения, отпущенный
  затвор пружиной досылает; TR15 — HoldOpen; Scar — затвор без клипа ходит по оси, щелчок вибрации на заднем упоре;
  Machinegun — очередь сразу после вставки магазина, без затвора.
- Дробовик (Herrington, FABARM, SRM12): отказ темпа и вибрации те же, что вчера, но теперь из дефолта; SRM12 —
  цикл затвора между выстрелами (Manual), звук SRM12.
- Наблюдатель (второй клиент): слышит затвор переведённых стволов.
