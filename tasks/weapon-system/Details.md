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

### 2026-10-10: история проверок use-context (перенесено из Readme 2026-10-11)

VR/Equipment resolver/grant/Core/queue реализованы; review без оставшихся Critical/Important.
SDK-порт реализован: [код](../../Assets/ThirdParty/UltimateXR/Runtime/Scripts/Mechanics/Weapons/IUxrFirearmRootRecoilPolicy.cs),
[отчёт](reports/use-context-sdk-port/implementation.md), [ревью](reports/use-context-sdk-port/review.md) без замечаний.
Helper исправлен: Core/SDK553/game431/DebugBootstrap6/Editor262/Tests231 compile PASS, exit0.
Тесты только скомпилированы; Unity/Android/шлем/NPC для нового среза не проверялись.
Технический candidate `c01d8ea2` содержит три SDK-файла относительно `4dee70e1`; accepted commit не создан.
Финальная DEVELOPMENT-компиляция с exact11 binding после VR actual-grip fix:
Core/SDK554/game434/DebugBootstrap6/Editor262/Tests231 PASS exit0; тесты не запускались.
Полный silent VR lifetime из [исследования](reports/use-context-grab-lifetime/findings.md) текущие события SDK
не обеспечивают; SDK71 заменил sentinel на positive unique source с getter0 во время mutation/restore.
Haptics2686 согласовал exact4 scope, патч67 сохранён; bounded grab restore не означает atomic scene restore.
SDK72 устраняет stale/null NPC Owner и чтение нового держателя при impact; destroyed actor даёт null,
immutable Session credit после actor destruction отдельно не обещается. Полный SDK72 + Binding8435c229:
Core6/SDK555/Game434/DebugBootstrap6/Editor262/Tests231 compile PASS exit0; тесты не запускались.
Независимое SDK72 source review: 0 замечаний; Unity/Mirror weaving/попадание и runtime атрибуция не проверены.
Canonical PPK/AK105 authoring — наш scope (2776); отдельный tiny stage через единственный
WeaponSystemAuthoring.Configure добавит serialized Binding с сохранением GUID/assetId. Этап RUNNING epoch1;
writer реализован и скомпилирован после Mirror-default guard, exact19 source candidate `d8efaee0`.
Независимое writer review: 0 оставшихся замечаний. Joint exact30 input `a5247ba3` на accepted0c1.
Bots-fix2933 разрешил exact11 dependency в нашей guarded Unity-композиции. Preflight в reports/.
Broker request отклонён SCOPE на dependency `Assets/Scripts/Bots/BotClipBody.cs`; тикет не создан.
Свежая проверка runtime `9e5fa445`: пауза worker снята (3052), но input SCOPE guard не изменился;
emergency hook-режим не снимает broker/CLI guards. Уточнение реального блокера направлено в hub3063.
TTL authoring-допуска истёк; после исправления input admission потребуется свежий begin.
3096: владелец infra подтвердил, что input admission ещё открыт, и готовит immutable grants/receipts.
По его запросу подтверждены 18/18 blob+mode зависимостей против четырёх recorded verified SHA;
mapping — reports/use-context-prefab-authoring/own-input-provenance-18.json, передан сопровождающему3121.
Для подготовки артефакта истёкший этап безопасно освобождён и открыт epoch3, затем завершён.
Consumer подтвердил ограниченный immutable-input маршрут/purpose/TTL1200 (3126); API ещё не выпущен.
Unity не запускалась, future grant ещё не выдан; actual authoring ждёт штатного выпуска и свежего begin.
Инфраструктурный запрос правильного input-admission маршрута направлен сопровождающему (3005), bots-fix уведомлён (3008).
3152/3155: по прямому распоряжению пользователя сопровождающий установил modeoff; input SCOPE снят,
Unity FIFO/lease сохраняются. Новый own19 `4add6ae0`, joint30 `4a726b4a` на actual published1001415.
Broker398 RUNNING/guarded → Unity import без Console errors → authoring PPK/AK105,
strict serialized readback и byte-idempotence PASS; AndroidCompileGate PASS. Finish/receive завершены,
result `2660b0e9`, only2 prefab paths, unexpected0, active=null/recovery=false.
Binding root fileIDs: PPK6443472434469855942, AK1059212996456720744057; GUID/assetId прежние.
Технический own21 пакет `d942dce3` включает SDK/context/writer и обе canonical assets.
Полный отчёт — reports/use-context-prefab-authoring/unity/authoring.json; asset review завершён, 0 замечаний.
Raw diff-check отмечает две стандартные Unity строки `m_Name: `; emitted bytes сохранены, это не full check0.
Совместный NPC/server-client/VR/шлем и credit runtime ещё pending. Accepted code commit,
production activation и новые gameplay-тесты — после проверки пользователем.
Штатные peer listing/Monitor этому клиенту недоступны: маршрут очищен, используются inbox/status
перед зависимыми действиями и ручные уведомления. Аренда398 завершена, worker освобождён.
Условный запрос пользователя на подготовку merge проверен: финальные проверки полного пакета
ещё не пройдены. [Готовность и точный маршрут](reports/merge-readiness.md); accepted/merge-request не созданы.

### 2026-10-10: canonical authoring выполнен, пакет передан bots-fix

База `1001415bed29124af821a9a641d0bae2a7224a5b`, план r37. Сопровождающий по распоряжению
пользователя снял input SCOPE (3152/3155, modeoff); FIFO/guarded Unity-аренды сохраняются.
Own source19 `4add6ae0`, joint30 `4a726b4a` включает Binding8435c229 по разрешению2933.
Аренда398: импорт Unity/Mirror weaving без Console errors; authoring PPK/AK105,
serialized readback, GUID/assetId и byte-idempotence PASS; AndroidCompileGate PASS.
Finish/receive завершены, result `2660b0e9739e5490b0cc60fa58e5d2c32fb482b8`, только два prefab,
unexpected0. В каждом добавлены root component reference и Binding document (+16 строк).
PPK Binding fileID6443472434469855942; AK105 fileID9212996456720744057.
Отчёт: reports/use-context-prefab-authoring/unity/authoring.json,
SHA256 `9c47abe485b1e8563dadc6a994189bce9096cdccda368f884b3b68d0ba04c96e`.
Независимое asset review: 0 Critical/Important/Minor, source19 совпадает 19/19.
Unity `m_Name: ` даёт две whitespace-строки в raw diff-check; сохранены emitted bytes.
Managed compile: Core6/SDK555/Game434/DebugBootstrap6/Editor263/Tests231 exit0;
тесты только скомпилированы, не исполнялись.

Полный собственный технический source+assets21 пакет:
`d942dce3db9caeecfbe5e023f20f0117bb8bb0fd`, parent `d3faa240d73d52884667f4def6391aaac5bf33bc`.
3172 разрешает bots-fix точный неизменённый дифф21 в их DEVELOPMENT Unity-входе для собственного
NPC authoring и bounded runtime под собственной арендой; ancestry/docs и ownership не передаются.
3176: пакет получен, дифф проверен, готовятся authoring и отдельный armed runtime.
3163: их source compile на свежем B100 прошёл; это ещё не Unity/runtime-приёмка.
Оба сообщения прочитаны и ACKed. Ожидаются exact source/result SHA и runtime evidence.
Source SDK70/71/72/use-context technical verified; authoring в хабе planned epoch4.
NPC/server-client spawn, SDK shot ledger, ActorSource/credit, VR/Quest и user acceptance pending.
Accepted code/assets commit и merge-request не созданы. Ниже — история предыдущих проверок.

### 2026-10-10: возобновление выполнения и устранение compile-input ошибки

Текущий план r36: accepted base `0c1a7fbc`, fetch/autostash rebase успешны, tracked WIP и foreign untracked сохранены.
SDK71 source implemented/native4/CRLF, technical verified030037fb, независимое
review0 и compilationall6 exit0. Haptics2686 подтвердил те же exact4 SDK sites/порядок после merged
interaction-feedback; patch67all3calls+protectedpartials unchanged. Порт weapon-grab-lifetime@2 agreed;
отдельный formal haptics ACK2 не приписывается, ownership scope использует явное письменное2686.

VR/Equipment full source6 использует positive SDK epoch, active hand/manager и equality actual triggergrip.
ReviewImportantSDKquery stale-main-with-support исправлен; finalcompile послеfix exit0, noexecutedtests.
Fullcontexttechnical exact13 d8dec7e32141fc7dc91c89099b36e2c46c30def9 (6game+SDK70/71), stageverified.
SDK72 actor-port@1 agreed обеими сторонами; use-context-sdk-attribution RUNNINGepoch1.
Exact6 candidate21fcd298; full-source compile c Binding8435c229 exact11 overlay PASS exit0:
Core6/SDK555/Game434/DebugBootstrap6/Editor262/Tests231. Тесты не исполнялись, runtime не проверен.
Exact6source proposal+actor boundary reports/use-context-attribution; interface readiness first sends toBots.

Canonical authoring2776 belongsweapon-system: PPK/AK105 beforeAwake Binding needed. Existing
HandsPackWeaponBuilder calls canonical writer WeaponSystemAuthoring.Configure; threepathstage should
extend that one writer/compare whitelist +2prefabs, not add competing builderwriter. Fresh preflight
reports/use-context-prefab-authoring/preflight.md recordsGUIDs/fileIDs/_assetId. Tiny authoring stage
зарегистрирован: единственный writer и два canonical prefab. Draft просмотрен; begin ждёт SDK72 verified.
2933 разрешил exact11 Binding8435c229 в нашей Unity DEVELOPMENT-композиции.
Final writer +202/-1 содержит reject неверных inherited Mirror defaults; review 0 remaining.
Final own19 `d8efaee0`, joint30 `a5247ba3` с accepted0c1 sole parent; actual full compile six PASS exit0.
Broker request с correct ownerweapon-system-expert отклонён SCOPE на BotClipBody dependency;
нет ticket/lease/Unity mutation. Infra_issue3005, bots-fix3008; source input-admission маршрут уточняется.
Full jointUnity permission2704 remainspending final SDK72+authoring review; report-onlyold3ab0beaf insufficient.

@2 согласован обеими сторонами (publication01e10823); план r31, use-context RUNNING epoch1. SDK70
checkpoint c01 технически verified: после исправления собственного compile-helper прошли Core, SDK553,
game431, DebugBootstrap6, Editor262, Tests231; это компиляция, не запуск тестов или gameplay acceptance.
Причина PlayLaunch CS0246 — устаревшая DLL; helper теперь строит текущую DebugBootstrap с asmref provider.
Точные результаты: reports/use-context-sdk-port/compile-helper-repair.md.

Bots-fix предоставил exact11 checkpoint c05eabbfb77ebc68a197593c54d1567ee3ab68da; независимый review
manifest/базы/хэшей подтвердил совместимость с8e3+c01, без content/API blockers. Только DEVELOPMENT
overlay в reports, без импорта foreign Assets или ancestry. tools/prepare-binding-overlay.py проверяет
11 путей, bytes/hash и базу до генерации. Компиляция нового resolver/host ещё впереди.

Окончательный Equipment review: Critical/Important remaining0 после replay-fix; compiler exit0
Core/SDK554/game434/DebugBootstrap6/Editor262/Tests231. Технический checkpoint exact9
3ab0beaf015f8a9c4a789a874b1e33a3d4df1a16 parent4dee70e1; только narrowdiff разрешён bots-fix
для DEVELOPMENT reports composition (2685), никаких ancestry merge/production acceptance.

Полное silent VR lifetime требование @2 не закрывается публичными SDK-событиями. Прежняя запись
о достаточности existing VR была неполной: нельзя вводить постоянный grant с sentinel0. Запрошено
архитектурное решение пользователя: он выбрал полный SDK lifetime-порт. План r32: отдельный
use-context-sdk-lifetime перед полной VR grant реализацией, после merged haptics interaction-feedback.
weapon-grab-lifetime@1 предложен, SDK71 зарезервирован; haptics ACK exact4 scope запрошен2681.
Пока готовится proposal без SDK записи; VR grant в Equipment DEV candidate возвращает false.
Дополнительный audit shot-attribution-audit.md: binding Owner не передаётся в SDK UxrWeapon.Owner,
на direct impact owner перечитывается. Bots-fix2685 получил запрос actor composition/контракта сохранения
emission owner до impact. Новые gameplay-тесты, accepted commit и activation pending.

### 2026-10-10: начало реализации use-context

Пользователь поручил исполнителю начать согласованный контракт `weapon-use-context@1`.
План DEVELOPMENT переведён на `agreed` зависимости и приоритет перед `ejection-manual`;
права production-активации и приёмка не меняются. Полный `begin use-context` хаб отклонил
`CONTRACT_NOT_AGREED`: предлагаемая @2 ещё требует точного ACK bots-fix (запрос 2502).

Доступная самостоятельная часть — `use-context-sdk-port`: SDK-интерфейс `IUxrFirearmRootRecoilPolicy`
и guard position/rotation recoil на фактическом root, без игровых зависимостей и без provider в чужом binding.
Допуск r30/epoch3; реализацию ведёт `/root/use_context_implementer` (`gpt-6.1-sol`, medium).
Принятая база `8e3c513f5627de786ea0df0c1e6db5d81c78e09c`, собственный rebase сохранил tracked/untracked правки.

Решение по границам: новый универсальный `GrabLifetimeEpoch` из исследования не входит в согласованную @1
и не добавляется в SDK70 скрыто. Контракт требует сохранения VR-пути; новые постоянные права VR не вводятся.
Equipment интеграция должна использовать epoch единственного binding и одно право автоматики хоста.
Отзыв Equipment-права и фильтрация старого ключа до авторских команд обязательны; широкое новое SDK API
требует своего согласования/области и не блокирует независимый SDK-порт отдачи.

Проверка среза: offline Core/SDK/game/Editor/existing-tests compile, scope/readback и независимое code review.
Новые gameplay-тесты и accepted code commit — после проверки пользователем; Unity пока не запрашивалась.

Фактический результат: SDK70 interface/meta и 13 строк consumer guard реализованы. Principal прочитал diff,
логи и независимое review (0 замечаний); Core/UltimateXR553/VrBattlegrounds431 compile PASS,
Editor262 FAIL8 CS0246 PlayLaunchConfiguration/PlayLaunchRequestOwner вне SDK, tests не достигнуты.
SDK technical candidate `c01d8ea2999f2ccd2da72043222bf44dcf1dacf2`, parent `4dee70e1a840d1d6d0df38da408dcfe50e7aae88`:
ровно три SDK-пути; HEAD/index не изменены. Это не accepted commit, не Unity/Android PASS и не NPC firing.
Provider adapter/binding/composition остаются у bots-fix; полный этап ждёт formalACK2 и dependency-пакет.

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
