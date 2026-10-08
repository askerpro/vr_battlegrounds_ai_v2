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
