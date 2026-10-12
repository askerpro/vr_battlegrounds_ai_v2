# Боты: Blaze AI, полные клипы и экипировка

Обновлено: 2026-10-11 (вечер). Владелец: эксперт `bots` (Claude), worktree `F:/CodexWorktrees/bots/Vr_Battlegrounds_ai`.
[Полный анализ](Details.md), [машинный план](plan.json).

## Переход на штатный поток игрока (2026-10-12)

**2026-10-12: pf-prototype принят пользователем и влит (origin/dev 43531cea3).** Ревью avatar-ik — задним числом (4844).

Решение пользователя: бот — обычный аватар, ИИ управляет оружием через адаптер намерений. Проект и решения —
[player-flow-plan.md](player-flow-plan.md): вариант P «кукловод», отдача как у людей, неточность — прицел ИИ +
отдача, перезарядка — automation. pose-fit отложен в `parked/pose-fit-2026-10-12/`. Реализация ждёт ревью зон:
avatar-ik 4184 (п.1 расходится), manipulation 4188, weapon-system 4190, network 4192, gameplay 4194,
performance 4196. Ответили (ACK с условиями): avatar-ik 4201, weapon-system 4198, gameplay 4204, network 4206,
performance 4199; ждём manipulation 4188. Новая модель строится от origin/dev (клиповый путь — архив ветки).

Решение пользователя 2026-10-12: ветка `bots-fix` перестроена от origin/dev `3240427c9`; клиповый путь (accepted
armed-development `679c4c717`, WIP) — в локальной ветке `bots-fix-clip-archive` (`d58640956`), в dev не вливается.
Этапы: pf-prototype (стенд, MEF+AK105) → pf-equipment (PPK/Unarmed/кобура/покупка/призрак; смерть со сбросом ждёт
manipulation death-release-authority) → pf-network → pf-quest → clip-cutover (перевод матча, удаление старого
гибрида, binding r3, документы, тесты после приёмки). held-motion передан manipulation; equipment-foundation,
pose-fit, acceptance сняты.

## Итог armed-development (2026-10-11)

Пользователь подтвердил: «оружие держит лучше, бот стреляет и попадает» — коммит как есть, остаток следующим
этапом. Коммиты ветки bots-fix: `c5f7e171c` (код), `e14428b12` (ассеты Resources/Bots из ticket438).
Раунды наведения: I (yaw) → II (aim-слой) → III (фазы, детерминизм) → IV (без завала в Idle), ticket419/426/434/438.
Итог IV: AK105 MEF 101/101, Cyborg 97/96 выстрелов/попаданий, все гейты PASS; PPK попадает всеми выстрелами,
но гейт наведения в Shoot не проходит из-за отдачи клипа. Остаточный завал AK105 3–6° (медиана) в движении.
**Следующий этап (предложение)**: поза рук и корпуса — опорная рука на цевье (grip-fit, с avatar-grip),
крен корпуса клипа вправо (компенсация Left-Right в aim-слое), завал в движении (socket по фазе), критерий PPK
(доля попаданий и конверсия запросов вместо медианы Shoot) — ждёт решения пользователя.
Не влито в origin/dev: условия manipulation/network/gameplay и гейт активации claim — см. ниже.

## Текущее состояние (2026-10-11)

Задача передана эксперту `bots` (task-handoff 3340/3343, план r23); прежний worktree удалён, WIP перенесён.
Runtime B (реальный выстрел MEF/Cyborg на own21 `d942dce3`) **не выполнен**: ticket404 (input `fd3f94ea`,
base `716743`) потерял аренду 10.10 20:08 без renew (лимит Codex); сопровождающий восстановил его
(3270): DONE, `accepted=false`, отчёта нет. Результат R `523969e9` содержит только 3 ClipBody-префаба,
пересохранённых Unity (~120 overrides `__prefabGuid` на префаб) — дефект authoring BotClipComposition,
причина исследуется. weapon-system ждёт наш B как свой блокер перед VR-приёмкой use-context (3383).
.meta `WeaponAutomationCapability` (GUID `2c0e5f87…`) и `WeaponGripBinding` (GUID `ca6caa5c…`) —
наши, совпадают с `fd3f94ea`; добавлены в writes вместе с `IUxrGrabAvailability.cs.meta` и Editor meta.
Пять файлов-поставщиков Binding (Equipment.meta, IUxrGrabAvailability, UxrGrabbableObject,
UxrGrabManager.Manipulation) добавлены и в armed-development: будущий input-grant — одним этапом.
В mode off SCOPE admission снят, поэтому совместный runtime input grant пока не требует.

Причина (read-only разбор субагента, проверена по коду): ClipBody сохраняется из preview-экземпляра
с `__isInPrefab=0`, GUID/`_assetId` источника; `UxrComponent.OnValidate` и Mirror `AssignAssetID` чинят
их только в памяти; постпроцессоры `UxrUniqueIdPersister`/`NetworkAssetIdNormalizer` покрывают лишь
`Assets/Prefabs`. Значения R523969e9 детерминированы и каноничны (GUID самого ClipBody).
Исправление: `BotClipComposition.StampAssetIdentity` сразу после SaveAsPrefabAsset (флаги через
`UxrUniqueIdPersister.HasMismatch`, канонический `_assetId`, SavePrefabAsset). Офлайн-компиляция дерева
`fd3f94ea` + исправление: UXR/Blaze/Weapons.Core/Product/Stand/DebugBootstrap/Editor — 7/7, 0 ошибок
(`reports/prefab-identity-fix/`). Unity не проверено; критерий — повторный Build и Play дают 0 diff
префабов, `_uxrUniqueId` не меняются (known-issue #11).
Zone ACK запрошены: gameplay 3390, manipulation 3392, weapon-system 3394 (там же ответ на 3383);
`owner:network` в хабе отсутствует (UNKNOWN) — нужна ручная доставка.

**Прогон B ticket413 (2026-10-11)**: input `212ad9f8` (origin/dev `e1612051` + exact91 `fd3f94ea` +
исправление), base `081ec677`; finish DONE, result `e61a5813` accepted=true, unexpected 0, 3 ClipBody.
Пакет `tools/armed-runtime-b/`, отчёты `reports/armed-runtime-b/` (b-verdict.json), консоль ошибок 0.
- A: 3 модели/6 sockets/27 motions, 0 mismatch; идентичность после A и после Play — PASS (SHA/`_uxrUniqueId`
  неизменны). Префабы R побайтно равны каноническим из R404 — дефект `__prefabGuid` закрыт.
- Экипировка, подготовка (cold→Fired), движение (≈21 м/20 с, скольжение ≤1 выборки), темп клипа (медиана 1.0),
  strafe и повороты — PASS для MEF/Cyborg × PPK/AK105.
- PPK: реальные SDK-выстрелы во всех 4 case (ShotSequence 0→8..14, магазин убывает, урон цели засчитан боту).
- AK105: MEF 5/0, Cyborg 0/0 выстрелов — `BotGunner.Sees` даёт Occluded, хотя диагностический muzzleRay первым
  видит хитбокс цели; причина разбирается (`diagnosis/rifle-occluded-2026-10-11.md`).
- Стенд отбросил 1057 событий-кандидатов (`WeaponCandidateEventsDropped`): source-emitted/ledger не попали в
  WeaponTimeline, поэтому автоматический вердикт FAIL и для PPK — пробел инструментария, не отсутствие выстрела.
Итог: стрельба пистолетом подтверждена, винтовкой — нет; приёмки пользователем ещё не было. Receive R не
выполнен: WIP-префабы ClipBody старее input (результаты A402 в WIP не приняты).

**Замер B2 ticket415**: input `fd193835` (+ единый луч `BotGunner.TryGetMuzzleForwardHit`, запись forward-луча/
углов, исправлен отброс событий наблюдателя стенда), result `a5ab2487` accepted=true, unexpected 0.
Таймлайн восстановлен: SDK-выстрелы mef-ak105 6, mef-ppk 12, cyborg-ak105 0. Остаточная ошибка дула
в Idle/Shoot ≈+4.5–5.5° вверх и +2° вправо, в ходьбе 7–13° — нужно наведение, не статическая поправка
([разбор](diagnosis/rifle-occluded-2026-10-11.md)). Решение по способу наведения — за пользователем.

**Наведение, раунд I (этапы 0–2), ticket419**: input `c599ac57`, result `f58d2651` accepted=true, unexpected 0
(ClipBody ×3 + BotCombatAssets с `aims[]` 36). Калибровка детерминирована (повтор — те же байты). Вердикт PASS по всем
гейтам: yaw дула в Shoot медиана ≤0.04°, скольжение 0. SDK-выстрелы/попадания: MEF AK105 39/38 (было 6),
Cyborg AK105 24/24 (было 0), MEF PPK 19/19, Cyborg PPK 18/18. Остаток pitch +4–5° (Idle/Shoot), 7–10° (ходьба) —
этапы 3–4. Отчёты `reports/aim-round1/`, план [aim-plan.md](aim-plan.md).
**Влито 2026-10-11**: совместный пакет weapon-system в origin/dev `74de9191` (ff): наш foundation — коммит `6849ba0e`
(5 blob = `93f67ded`), их use-context `c969759d9`; пользователь принял в шлеме, Weapons 236/236. Ветка bots-fix
перебазирована на `74de9191` (autostash без конфликтов; 5 локальных копий foundation побайтно равны dev — удалены);
WIP компилируется сам: 7/7, 0 ошибок. Условия network 3–5 по binding — за нами до clip-cutover.
haptics-system/pocket-removal влит (`9643a864`): after в acceptance выполнен.
**Гейт активации claim** (manipulation 3649, weapon-system 3656): на origin/dev путь ReconcileSdkGrabs недостижим (нет реализаций
IWeaponEquipmentSocketOwner; ответ 3655). Первая реализация (BotClipBody) и вызовы TryAttach/TryTransfer — только после ревью
manipulation (синтетический ReleaseObject, выключение лишь _rootGrip; правило «нельзя взять под claim» — через GrabRules) и network 3–5.
Сценарии проверки (3655 + ответ weapon-system manipulation) входят в план clip-cutover: хват игрока при claim (1/2 руки, магазин),
поздний вход, снятие claim и хват третьим клиентом, попытка хвата под claim, reconnect, хост.

**Наведение, раунд II (этапы 3–4), ticket426**: input `5321f344`, result `b01cda77` accepted=true, unexpected 0 (15 путей:
ClipBody, контроллеры, Aim-клипы/маска, BotCombatAssets). Слой: при AimPitch=0 поза неизменна (0°), утечки вне маски 0, таз/стопы 0;
+20° → грудь/голова 20°, кисть 187 мм. g все отрицательные (−0.54…−0.69, нелинейность <5%) — имена Up/Down перепутаны.
Дефект: первое создание BotAim_Up/Down недетерминировано (со 2-й сборки стабильно, `determinism-probe.json`).
SDK-выстрелы/попадания (запросы Blaze): MEF AK105 47/47 (49), Cyborg AK105 36/30 (55), MEF PPK 11/11 (23, только T07),
Cyborg PPK 15/15 (42). Гейты: Cyborg AK105 pitch/yaw PASS, но доля кадров Shoot с лучом в цель 0.63 (каждый кадр — 0.90) и
скольжение 5% (база 0.6%) — FAIL; Cyborg PPK Shoot pitch 6.7°/yaw 3.0° — FAIL (раскачка клипа Shoot); MEF — Unknown
(мало кадров Idle/Shoot для медианы). Отчёты `reports/aim-round2/`.

Фундамент для weapon-system: тех. коммит `93f67ded` (5 путей: binding + `IWeaponEquipmentSocketOwner`), 3515.
weapon-system принял (3587/3588): zone ACK на Equipment/** и WeaponAutomationCapability/GripBinding; foundation `93f67ded`
войдёт в их совместный accepted commit, владение binding — bots. С нашей стороны weapon-system разблокирован.
network (3599): до clip-cutover (claim достижим) — (3) локальная сверка IsActive && HasSdkGrabs на каждом процессе
с закрытием всех grip иерархии (сейчас ReconcileSdkGrabs только при смене epoch), без нового писателя/события;
(4) TryGetOwner: единый поиск UxrActor (GetComponent vs GetComponentInChildren в SDK) или явный инвариант.
PlayerSession на сетевое ревью: `contracts/playersession-network-review.diff`.
(5) до приёмки экипировки — ярус C: ServerOnly и хост, поздний клиент, reconnect (3597/3603). Совместный пакет
weapon-system — checkpoint `d21bec7e`, network ACK (3597) при выполненных условиях 1–2.
network 3611: PlayerSession ACK (замечание о !NetworkServer.active в SetBotControlBeforeSpawn исправлено). До merge:
GameNetworkManager.OnStartClient при отказе каталога зовёт StopClient → GameNetworkDiscovery автоповтор → бесконечный
reconnect; решить (а) проверка каталога на сервере в OnStartServer + guard/валидатор сборки, клиентский отказ терминальный,
или (б) запрет автоповтора — порядок с launch-infra, ветку в GameNetworkDiscovery согласовать с network.

**Zone ACK**: manipulation (3449) — только DEVELOPMENT; до merge 8 условий: запрет хвата через GrabRules
вместо геттера `IsGrabbable`, кеш провайдера без `GetComponentsInParent` в горячем пути, серверный backstop
в `GrabObject` с network, патч 56 убрать или зарезервировать, changelog sdk-61, LooseItems без зависимости
от Bots, ревью `ReconcileSdkGrabs`/`_rootGrip`, зелёные GrabQueryPatchTests/LooseItemTests + Quest/remote.
gameplay (3499) — только DEVELOPMENT; до merge: AvatarTeardown ранний return ломает AvatarTeardownTests:74/:101;
новый порядок release-до-Spawn в ReplaceBody затрагивает людей (ограничить botControlled или доказать эквивалентность);
AvatarManager/AvatarTeardown не должны знать каталог ботов — резолвер тела NPC на стороне bots по контракту;
PlayerSession SyncVar — ревью network или решение пользователя; CreateBotSession может вернуть null.
weapon-system (3394) — ответа нет; network — нет owner-задачи, ручная доставка.
Разблокировка weapon-system (решение пользователя): совместный merge own21 + минимальный foundation
(их PPK/AK105 ссылаются на наш WeaponEquipmentBinding guid bf92bfab…); предложение 3490, состав — анализ.

## Цель

Исправить отсутствие стрельбы, неправильный хват и отставание оружия при движении.
MEF/Cyborg анимируются целиком Rifle/Pistol-клипами: один visible Animator, без IK,
VR-захватов и SDK-поз пальцев. Оружие жёстко следует кисти. Heavy исключён.
Учёт патронов и readiness принадлежат weapon-system.
Первый результат для просмотра/принятия — вооружённые NPC с корректным движением и
реальным выстрелом; визуальные руки/хваты дорабатываются следующим шагом.
Отдельный dormant Unarmed-пакет A не соответствует этому первому результату и не публикуется.

## Архитектура

BotBody управляет корнем, Animator — bones, серверный WeaponEquipmentBinding — предметом.
Blaze использует этот же Animator. NPC variants выключают VR/IK до Awake, human assets сохраняются.
SDK-хват и equipment взаимоисключающие; один snapshot netId/epoch/socket, один вход подготовки
RequestAutomationPreparation. Никаких fake grabbers, копий M/C или второго lifecycle.

## План и статус

A3 ticket402 завершён и worker освобождён: result `955fba89`, только 5 Resources paths,
unexpected 0. Проверены 3 модели / 6 sockets / 27 motions, metadata 0 errors/mismatches/unknown,
Console errors 0. Источники и owner21 неизменны; следующая runtime-проверка ещё не запускалась.
Свежий rebase на accepted `71674333` сохранил правки (autostash `6f91c1b5d`);
incoming только docs5. Runtime input строится с сохранением этих принятых документов,
broker base указывается по актуальному published B; совпадение игрового кода проверено.

Одобренное исправление fixture реализовано: immutable BotAvatarSource до Spawn/RegisterBot,
единый resolver AvatarManager и runtime GUID validation subject/target; обычный MEF добавлен
в generic generator. Следующий authoring требует 3 модели / 6 sockets / 27 motions.
После обновления origin/dev выполнен сохранный rebase на `29a9d118` (autostash `fbeafbe19`).
Incoming changes только docs5; свежая compilation новой реализации выполняется отдельно.
Следующий Unity request ждёт опубликованную B29: сопровождающему передан inbox3237.

Runtime admission ticket400: input `68c7ebc5`, Unity import и PlayLaunch.Plan успешны.
Discovery до Play обнаружил несовместимый выбор модели: Team1 только MEF Black (0),
Team2 обычный MEF (0), Cyborg отсутствует в TeamRegistry. Play не запускался;
worker освобождён, result `7aa218b1`, изменённых путей 0, диагностика получена.
Планируется immutable выбор bot-модели до первого Spawn через штатную фабрику,
а также покрытие обычного MEF generated-каталогом. Чужие TeamRegistry assets не меняются.
Сопровождающий подтвердил причину предыдущих unexpected paths (inbox3200): Git clean
нормализация и две пропущенные собственные meta. Проверенный input68c содержит только
разрешённые Resources13/meta2 и canonical Git hashing; owner21 побайтно сохранён.

Проверка Unity A ticket399 выполнена на input `50ab1917`: composition 2/2, sockets 4,
motions 18, saved/reopened metadata 0 mismatches/unknown/errors, canonical definitions 2,
Console errors 0. Worker освобождён; результат `1287db8b` сохранён.
Автоматический receive запрещён broker (`accepted=false`): за пределами Resources/Bots
обнаружены source/meta изменения. Независимый diff-аудит проверяет нормализацию EOL;
infra_issue3183 передан сопровождающему с native уведомлением. Runtime B ещё не выполнен.

Актуально 2026-10-10: окончательный оружейный пакет получен и подтверждён
(inbox3172/3176): `d942dce3`, ровно 21 исходник/asset, включая canonical PPK/AK105.
Оружейный ticket398 завершён; владелец сообщает успешные authoring/readback/layout,
идемпотентность и Android Compile Gate. Это не проверка движения или стрельбы NPC.
Наш HEAD и свежий origin/dev — `1001415`; совместная компиляция исходников без ошибок.
Готовится собственный технический Unity input и пакет A пересоздания клиповых аватаров,
затем отдельный вооружённый runtime-прогон MEF/Cyborg. Собственной аренды пока нет.
Общий mode off установлен сопровождающим по поручению пользователя; FIFO/lease/guard
и пользовательская приёмка сохраняются. Чужие SDK/prefab используются неизменёнными.

Текущая проверка 2026-10-10: pre-request diagnostics snapshot скомпилирован — Product,
Stand, DebugBootstrap и Editor, 0 ошибок. Наблюдатель сохраняет первый фактический M/C
для каждого weapon/epoch/owner, не меняет патроны и не объявляет Unknown результатом PASS.
Добавлено отдельное read-only наблюдение смертельного попадания: SDK пропускает
DamageReceived в lethal-path. Финальная повторная компиляция Stand/Editor — 0 ошибок;
111 snapshot-хешей и 98 собственных исходников совпадают. Контекст источника при Died
явно Unverified либо Unknown: receiving не выдаётся за подтверждение применения урона.
Unity/runtime этой версии ещё не проверены.
Оружейный агент подтвердил получение запросов и своё владение canonical PPK/AK105 authoring
(inbox2826); shooter@1 ACK передан2824, готовность Binding adapter сообщена2842.
Совместный Unity input ждёт его окончательные SDK72 и prefab-кандидаты; пользовательская
пересылка сообщений не требуется.
Ответ2852 предоставил точный interface-кандидат `537466bf` для изолированной компиляции.
Binding реализует read-only TryGetOwner: неактивный claim возвращает false, невалидный
активный — true/null без возврата к прежнему VR-владельцу. Actor читается из текущего тела
без кеша; узкая source-передача и независимое review готовятся (запрос2885).
План r18 включает shooter@1 и SDK-патч73: исправление startup animation gate и Shoot-клипа
при отходе назад в external Animator режиме Blaze. Эти две правки реализованы и прошли
независимое review без critical/important; свежие Blaze/Product/Stand/DebugBootstrap/Editor
скомпилированы с 0 ошибок. Unity-проверки этих изменений ещё нет.
Ответ2903 подтвердил получение точного Binding-кандидата `8435c229` и успешную собственную
компиляцию weapon-system с полным SDK72 consumer и нашим adapter. Его итоговый кандидат
после review и canonical PPK/AK105 authoring ещё ожидаются; это не runtime-результат.
Запрос2921 закрыт ответом2933: владельцу оружия разрешены точные11 source blobs `8435c229`
в его техническом Unity input для compile/ApplyEquipmentBindings/readback/Mirror layout,
под его собственной guarded-арендой. Production/accepted merge этим не разрешены.
Первый наш armed-пакет по-прежнему ждёт результат этой canonical-миграции.
Ответ3008: broker отклонил joint input `a5247ba3` как SCOPE на read-only BotClipBody;
ticket/lease/Unity mutation не созданы. Дополнение с exact source permission2933 отправлено
сопровождающему infra в3012; штатное уведомление доставлено. Shared infra самостоятельно
не правим и ownership не расширяем. Полный19-file SDK-кандидат `d8efaee0` собран в isolated отчёте:
SDK/Core/Product/Stand/DebugBootstrap/Editor — шесть свежих PASS, 0 ошибок; Blaze переиспользован
после равенства всех source bytes. 19/19 baseline bytes совпали, 123 snapshot-хеша проверены.
Отчёт `reports/armed-full-context-compile/`; результат передан владельцу в3019.
Это DEVELOPMENT source composition, не Unity/weaving/runtime/пользовательская приёмка.
Ответ3094 подтвердил отсутствие штатного input-dependencies API. Exact11 provenance
подготовлен в `reports/immutable-input-provenance/manifest.json`: source8435, Git blobs,
SHA256 и происхождение7 product/4 SDK61. Manifest digest `6a38302a23c7fd7208c750ecfe9330bdcf937d6b7430778d0c112ce9a88c429d`.
Ответ3109 подтвердил technical input-grant route/TTL1200 с pinned RUNNING receipt;
штатное уведомление доставлено, weapon-system уведомлён3111. Это согласование дизайна,
не опубликованный machine grant: API ещё не выпущен, Unity ticket не запрашивается.
Ответ3136: по прямому поручению пользователя сопровождающий уже перевёл общий хаб в modeoff,
SCOPE/PERMIT admission временно отключён; FIFO/worker lease и ownership сохраняются.
Оружейный агент уведомлён3155 о продолжении canonical-пакета от свежей published B.
Наш HEAD обновлён до `1001415bed29124af821a9a641d0bae2a7224a5b`, autostash05421a834
восстановлен без конфликтов. Product/Stand/DebugBootstrap/Editor на новой базе — четыре
свежих PASS, 0 ошибок; SDK/Core/Blaze переиспользованы после равенства всех source bytes.
123 snapshot-хеша и99 собственных исходников совпадают; отчёт
`reports/armed-published-context-compile/`. Старый a524/B0c input не используется.

| Этап | Состояние | Следующий результат |
|---|---|---|
| armed-development | source реализован; isolated7/7 compile PASS; B ticket404 прерван без отчёта | Исправить authoring ClipBody, новый input, B runtime: настоящий выстрел |
| equipment-foundation | прежние runtime64/64 PASS; Android PASS | После технической проверки armed-development — пользовательская приёмка |
| clip-cutover | запланирован | После foundation и owner use-context атомарно переключить factory/Gunner/teardown |
| held-motion | запланирован | Обе resistance точки SDK; отдельно согласовать human root recoil |
| acceptance | запланирован | После принятия механики закрепить тестами, проверить ServerOnly +2clients |

NPC authoring проверен в ticket200:120samples,0IK/SDK controller, bone delta<.001мм; Cyborg reference pose исправлена.
Прогон303 проверил equipment и Unarmed для двух моделей; стрельба/remote/Quest ещё не подтверждены.
В принятом origin/dev пока прежний bot-путь. Невлитый DEVELOPMENT-код уже переключает
фабрику, видимый Animator/Blaze, Gunner и runtime; начальный Unarmed bind един для brain
и контрольных целей, серверная animation projection добавлена. Lifecycle читает binding
для death/drop/confiscate и освобождает старый claim до смены тела.
Передача2175 завершена: durable recipient `weapon-system`, owner `weapon-system-expert`.
Ответы2502/2528/2531: SDK70 candidate `c01d8ea` готов, полный resolver/host ещё не готов.
Точный ACK weapon-use-context@2 выдан; уведомление2558 доставлено в inbox.
Binding получил read-only recoil adapter `AllowsRootRecoil => !IsActive`. Передаём узкий
source snapshot и [DEVELOPMENT-маршрут](contracts/armed-development-composition.md),
без принятия foundation или активации production-контрактов.
Передан inbox2573: технический SHA `c05eabbfb77ebc68a197593c54d1567ee3ab68da`,
ровно11 файлов относительно `6c0473fd`; SDK70 — отдельная обязательная зависимость.
Это минимальная source-композиция для resolver/host, без переключения обычных ботов.
Совместная изолированная компиляция binding snapshot + SDK70:6/6 PASS,0 ошибок;
apply-check PASS, текущие11 WIP-хешей сохранены. Unity/runtime/новый resolver не проверены.
Полный own source + equipment-context `3ab0beaf` (ровно9 owner files) скомпонован только
в reports/armed-context-compile:5 свежих сборок PASS, SDK/Blaze подтверждены прежними
результатами и совпадением source/DLL hashes; общий scope7. Manifest109 paths, own96
hashes совпали. Static review исправил P1: перед SDK-выстрелом проверяется actual muzzle ray.
Кандидат owner пока временно выключает VR automation и не закрывает actor attribution;
разрешения на Unity-composition/production нет. Запросы2704/2717 переданы владельцу.
Canonical PPK/AK105 binding authoring принадлежит weapon-system (подтверждено2778),
его запрос2769 доставлен сопровождающим. Новый authoring актор/SDK-порт остаётся у владельца.
Принятых коммитов/push этой задачи нет.

## Блокеры и следующие действия

Пользователь принял безоружную позу MEF/Cyborg 2026-10-09; хват и стрельба ещё не приняты.
303:64/64 equipment/unarmed checks PASS (reports/equipment-303/). 306: Android PASS,
EditMode runner не запустился — выполнено0 тестов (reports/final-306/).
Просмотр318 отклонён: скольжение, оба хвата Cyborg и support/наклон Rifle MEF;
Pistol MEF визуально нормален.0 enabled IK/SDK controllers,6 bodies/4 weapons (reports/human-318/).
328:20 сочетаний, отдельный SDK grabber меняет wrist-relative offset до354мм;
скорости клипов1.5–1.94м/с против прежнего preview≤.628м/с (reports/clip-authoring/).
Кандидат anatomical socket/native gait получил независимое SOURCE APPROVED.
339 завершён/получен: calibration FAIL на source/NPC wrist rest Cyborg, socket4/motion16
не подтверждены (reports/calibration-339/status.json). Worker освобождён.
Пользователь подтвердил отсутствие оружейных SDK-поз Cyborg: importer авторит только MEF.
344 завершён/получен:30 diagnostic records,0 execution errors. В сохранённом Cyborg
Animator pivot сдвинут на−1.549м без компенсации skeleton; это видно до Instantiate.
345 завершён/получен: composition2/2 PASS, saved mesh delta≤0.000584мм;
4 sockets/16 motions созданы, native speed1.592–2.241м/с. CyborgRig compensation сохранена.
Исправление порядка окончательный Avatar → RestoreRest → validation → Save → fresh-load
validation прошло source review и Unity.
347:8 PNG получены, root visual review выявил support вне цевья и rifle muzzle вверх
(MEF23.58°, Cyborg36.25°). Finger defects допускаются пользователем, остальной хват не принят.
354:5304 samples показали, что постоянный двухточечный Idle-fit не удерживает support
во всех клипах. [Fit-план](grip-contact-plan.md) остаётся исследованием; пользователь
отложил визуальную доводку хватов до следующего этапа. Приоритет — движение и реальная стрельба.
[Диагностика](diagnosis/cyborg-wrist-rest.md). Guard source/NPC wrist сохранён.

База после свежего fetch/rebase: origin/dev `0c1a7fbcc85d36235e4f2f9a2ab4f8b2bdf4653f`;
четыре входящих коммита изменили только docs/config/task material. Autostash `fea15a4e8`
восстановлен без конфликтов. Сверка выявила только EOL-изменения шести собственных файлов;
свежие Blaze/Product/Stand/DebugBootstrap/Editor снова exit0, 0 ошибок. Все114 snapshot-хешей
и99 own-source хешей совпадают. Baseline отчёта8e3 сохранён, code-equivalence с0c отмечена отдельно.
WIP сохранён. Изменения пока не приняты, worker lease не занят.
Повторная миграция: plan.json — единственный машинный источник; общие журналы исключены,
записи перенесены в собственный [changelog](changelog/2026-10-08-sdk-journal-migration.md).
План уточнён: точные собственные Editor-файлы, без продуктовой документации и профилей WeaponInfo.

weapon-system обновил ACK binding (426/428); contract-agree выполнен, допуск foundation получен.
weapon-use-context@1 agreed; drive влит, реализация контекста у владельца теперь после waves-f.
Ответ1486: waves-f влит; use-context теперь после ejection/ejection-manual и active binding. Ответ подтверждён в хабе.
Предложение366 реализовано: capability читает Profile/IsConfigured у WeaponReadinessController;
новый WeaponSystem сохраняет эту базу. Собственная миграция18 данных и legacy-профиль удалены.
Локальная компиляция6 сборок и временных probes PASS; отчёты reports/offline-compile/.
SDK60/61/62 зарезервированы. Human root recoil остаётся отдельным несогласованным аспектом.
Обслуживание завершено, worker открыт; заявку207 отменил сопровождающий.
Арсенал влит. Ошибка fixture282 (plain Instantiate с Auto Anchor) исправлена; canonical fixture303 GREEN.
Обычная безоружная стойка приходит через общий clip-cutover, без отдельного stand pose writer.
Запросы491/498/622 обработаны: clip-cutover ждёт также startup-route и Play Launch.
Foundation не ждёт Play Launch: BotCombatStandEditor исключён из наших writes.
Генератор сохраняет идентичность состояний; повторная сборка308:4/4 ассета побайтово стабильны, SOURCE APPROVED. Отчёт reports/controller-repeatability/.
Haptics идёт первым в общем AvatarLoadoutTests: acceptance ждёт pocket-removal по запросу878.

## Приёмка

Движение/strafe/повороты ≥30с для обеих моделей и категорий; socket offset<1мм/.1°,
реальный хват, cold chamber, один authored projectile/урон.
Death/ghost/respawn/drop/VR pickup/skin/map Closing/late join и stale epoch.
Android и existing suites; новые gameplay тесты только после подтверждения пользователем.
Отчёты — [reports/](reports/). Публикация документа не означает приёмку кода.
