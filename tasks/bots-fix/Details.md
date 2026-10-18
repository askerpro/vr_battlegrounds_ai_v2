# Боты: полный анализ и архитектурные решения

> Сверка 2026-10-18: текущие состояние, WIP-зависимости и порядок завершения —
> в [completion-plan.md](completion-plan.md) и [Readme.md](Readme.md).
> Этот разбор сохраняет историю проекта: ожидания вливания seed/арсенала/PlayLaunch
> и названия прежних BotAvatarSource/IsBotControlled не описывают текущие blockers/API.

Владелец: `bots`, задача `bots-fix`, Codex. Текущий worktree: `F:/CodexWorktrees/bots`.
Исторический анализ зафиксирован 2026-10-08. Текущий статус — [Readme.md](Readme.md), машинный план — [plan.json](plan.json).
Возобновление и свежие проверки 2026-10-14 — [changelog](changelog/2026-10-14-codex-resume.md);
текущая архитектура — [player-flow-plan.md](player-flow-plan.md), реализация — [cc1-plan.md](cc1-plan.md).
Координация актуализирована 2026-10-09: requests491/498/622 обработаны.
Прогон303 после восстановления MCP: input8bee2f3645ae7bd31ef110ecddd1572b49cde17e,
resultd2d905280ca8cca75be76d172cfe5602a4fd7696; finish/receive выполнены.
64/64 checks PASS, включая negative raw AutoAnchor и canonical attach/transfer/drop,
SDK exclusion/replay/rollback, transport direction, owner destruction и Unarmed до/после Bind.
На4 парах model/weapon socket position/rotation residual0мм/0°. До Bind enabled SDK controllers0;
кисти MEF ниже плеч на~.545м, Cyborg~.578м. PNG просмотрены: нормальная стойка с опущенными руками.
Полные локальные доказательства reports/equipment-303/; проверка не включает стрельбу/remote/Quest.
Ordinary factory/runtime ещё hybrid; новое Unarmed поведение приходит через атомарный clip-cutover,
без добавления параллельного stand pose writer. Android нового среза пока pending.
Инфраструктурная передача: пользователь поручил полностью передать MCP/worker сопровождающему.
Сводка и доказательства — hub event1356 для agent-coordination-protocol, уведомление восстановителя1358.
Отдельная инфраструктурная диагностика в bots-fix прекращена. Ticket292: interrupted authoring
execute_code timeout300s, следующий equipment-build выполнен; две временные EquipmentProbe
prefab требуют безопасной очистки владельцем recovery. Play и runtime292 не запускались.
Здесь остаются игровые исходники, unarmed pose и исправленный fixture; продолжение Unity после
готовности worker/MCP и восстановления допуска. Детальные инфраструктурные трассы сюда не копируются.
Runtime282: input4aec90a75ebd38e1416a753943af120c2b4d6f85,
resultff70b5ed1dba4e5ce4381d5c99182519d699999a. Finish/receive выполнены, metadata получены.
58 checks,42fail, первый отказ attach; исходный probe не сохранял reason и продолжал зависимые
проверки. Полный binding.json сохранён в broker artifacts282/tasks/bots-fix/reports/equipment/.
Диагностическая проба теперь сохраняет reason/authority/netId/canonical/live/admission и останавливает
ветку при отказе attach. Инварианты production binding не ослаблены.
Запрос пользователя о T-позе: имеющийся Unarmed_Idle_Static.anim используется новым NPC-контроллером.
Старый BotCombatDriver при !armed удаляет animation source и оставляет позу префаба;
этот hybrid-путь удаляется cutover, новый BotClipBody получает явную Unarmed/Pistol/Rifle позу.
При анализе282 найден дефект fixture: plain Instantiate пробуждает weapon Auto Anchor до attach.
У PPK root _autoCreateStartAnchor=1, SDK UxrGrabbableObject.Awake создаёт anchor;
канонический NetworkUxrIdentity.CreateInstance подавляет его до Awake. Проба переведена на
MapRunAdmission.CreateActiveMapItem/SetActive/SpawnServerObject, observer — на CreateInstance.
Добавлена отрицательная проверка raw spawn, ожидающая отказ на занятом anchor. Binding не ослаблен.
Unarmed source и diagnostic fail-stop получили независимое SOURCE APPROVED от clip_plan_review;
runtime/PNG ожидают worker288, компиляция точного payload проверяется отдельно.
В after.state хранится требуемое будущее состояние зависимости, а не её фактический статус.
Арсенал, startup-route и Play Launch ещё не влиты. Наш clip-cutover обязан дождаться их вливания.
Foundation не ждёт Play Launch: BotCombatStandEditor.cs исключён из наших writes.
Запрос878: acceptance ждёт haptics-system/pocket-removal merged для общего AvatarLoadoutTests.cs.
Собственного невлитого diff этого теста нет; haptics может убрать ожидание PocketHaptics первым.
Это требование будущего вливания, не отметка о выполнении pocket-removal. Обратной зависимости нет.
Для общего GameNetworkManager.cs порядок Play Launch → clip-cutover предотвращает пересечение.
Предыдущие планы и audit-файлы — техническая история, не параллельная доска текущего состояния.

## Цель и мотивация

Исправить отсутствие стрельбы, неправильный хват и отставание оружия при движении.
У живых MEF/Cyborg один видимый Animator управляет всем скелетом и пальцами через Rifle/Pistol
клипы. Боты не используют IK, VR-захват или VR hand poses. Heavy исключён из анализа и правок.
Экипированное оружие жёстко следует animated RightHand; учёт патронов остаётся только у weapon-system.
У физически удерживаемого игроком оружия также не должно быть translation/rotation resistance.

Пользователь поручил независимое ревью плана и реализацию после APPROVED. План и NPC authoring
одобрены; equipment source после исправлений также SOURCE APPROVED. Это разрешение реализации,
не пользовательская приёмка в Unity и не основание отмечать код accepted/merged.

## Границы и владение

- Здесь: Bots/Blaze adapter, NPC variants и их производный каталог, `WeaponEquipmentBinding`,
  SDK semantic admission/availability/resistance, потребители context, сетевое представление и teardown.
- Weapon-system: `WeaponUseContext.Resolve`, readiness/автоматика по `(Kind, User, Epoch)`,
  Weapons/Core/WeaponSystem, SDK firearm/ammo/спуск/отдача. Его код самостоятельно не изменяю.
- Контролируемая сессия определяется каноническим `PlayerSession.IsBotControlled`, заданным до Spawn.
  Human/stress marker=false, человеческие source prefabs/Avatar и Heavy не переводятся на NPC clips.
- Один автор корня тела — BotBody; навигация даёт намерение. Один автор bones — visible Animator.
  Blaze/AnimationManager читают этот же Animator; отдельного скелета на navigation proxy нет.
- Один автор предмета — серверный binding. SDK-хват и equipment claim взаимоисключающие.
  Никаких копий M/C, fake UxrGrabber или второго lifecycle подготовки; вход только RequestAutomationPreparation.
- Source→NPC variant генерируется из выбранного исходного prefab, не заменяет AvatarRegistry.
  Unknown live model — отказ; canonical eliminated ghost проходит штатным отдельным путём.

## Архитектура и последовательность

1. **equipment-foundation.** Завершить и проверить невлитый инженерный срез: authority/lifecycle,
   Blaze external Animator, pre-Awake NPC variants, canonical session marker, read-only capability
   и equipment binding без производственных потребителей. Срез содержит накопленные совместимые
   изменения прежнего bot-пути; это временный путь до атомарного cutover, не окончательная схема.
   Точный кандидат на вливание формируется только после runtime проверки и пользовательской приёмки.
2. **clip-cutover.** После принятой foundation и owner use-context переключить factory/registry,
   Body/Driver/Gunner/Shopper и teardown атомарно. Удалить BotWeaponPoseSolver и SDK-grab/IK из NPC.
   Добавить authoritative animation/equipment snapshots, late join, body/weapon ordering и stale epoch guards.
3. **held-motion.** SDK60: обе точки применения resistance в Manipulation без числового save/restore.
   Согласовать физическую отдачу корня удерживаемого VR-оружия с weapon-system: текущая договорённость
   про NPC recoil не является согласием на изменение human firearm recoil. До этого этап заблокирован.
4. **acceptance.** После пользовательского подтверждения новой механики закрепить её тестами;
   проверить реальные два клиента через адресный Play Mode стенд и отдельно перечислить XR/Quest границы.

Binding: один серверный snapshot netId/epoch/socket; detach содержит world pose/velocity.
OwnerAlive требует живое актуальное тело canonical bot session. Неразрешённый observer owner сохраняет
claim и запрет SDK-хвата; socket не вычисляется. Все ancestor availability providers складываются AND,
без кеша результата и изменения `_isGrabbable`. Low-level GrabObject проверяет claim до BeginSync.
Полученный claim откатывает VR prediction штатным SDK receive path без echo; nested receive откладывает
projection. До успешного rollback socket writer не включается. NT использует ResetState без смены authority.
Сервер отказывает на занятую правую руку; занятость выводится из spawned items, отдельного body registry нет.

## Контракты и зависимости

| Контракт | Владелец | Предоставление | Нужное согласование |
|---|---|---|---|
| weapon-equipment-binding@1 | bots-fix | equipment-foundation | Формальный ACK weapon-system точной ревизии API/взаимного исключения |
| weapon-use-context@1 | weapon-system | use-context | Владелец предлагает; bots-fix читает и ACK context/automation/NPC root recoil |
| held-root-motion@1 | weapon-system | новый явно объявленный этап владельца | Физическая отдача human held root; пока не согласовано |

Legacy BOTS-FULL-CLIPS-CONTRACT-20261007-1 явно получен и прочитан weapon-system
(`weapon-system-to-bots-full-clips.md`). API принят, источник: UUID25b51afb-9e6a-4f64-b798-f8d815cab294.
Это архитектурное evidence; формальные ACK/agree нового протокола от чужого имени не создаются.
Старый capability ACK — другой контракт. C2 уже в принятой базе, временный G1 не влит и новым use-context заменяется.

Пересечения: weapon-system use-context/waves-f (Weapons consumers, SDK docs, 18 legacy profiles),
arsenal-generator (WeaponInfo/legacy metadata и будущий Loadout consumer), map-runtime-bootstrap
(stand/editor lifecycle), vr-test-stand (адресный запуск и SDK numbering), hand-rig-quality (SDK numbering).
Полные опубликованные документы читать по agents/status либо точному SHA; чужие области не откатывать.
SDK56 semantic admission, SDK60 resistance, SDK61 availability/low-level guard, SDK62 ArmIK
(бывший58 после пересечения с vr-test-stand58). При миграции зарезервировать те же ключи через patch-reserve;
конфликт номера не скрывать. Root firearm/ammo/haptics не входят в SDK60/61.

## Фактический статус и доказательства

- NPC authoring: ticket200 input6cedde901d392b53784bb6f0fb8adf41e7be677f,
  resultf31d9dff42a1e8470105625937f8a8c24a0b7f7c, finish/receive выполнены.
  120samples MEF/Cyborg, Pistol/Rifle:0IK/SDK controller, bone delta<.001мм;
  Cyborg reference T-pose исправлена, руки перед корпусом. Android PASS этого среза.
  Это безоружейный прогон: contact/socket/стрельба/remote/Quest им не подтверждены.
- Existing105 tests:97pass/8fail; чистая принятая база ticket193 повторила те же8names/messages.
  Четыре old LegsAnimator/bridge, три pocket tags AK105/R08, один LightingData.asset invalid header.
  Ожидания новой механики до user acceptance не переписывались.
- Equipment source: SOURCE APPROVED после ResetState/AND/VR fixture/admission исправлений.
  Свежая локальная компиляция SDK/Blaze/Core/Game/Stand/Editor/probes PASS.
  Прогон303 подтвердил equipment runtime:64/64 PASS, MEF/Cyborg × PPK/AK105,
  maxPositionMm=0/maxAngle=0; SDK-grab exclusion/rollback/nested receive и NT authority проверены.
  Unarmed до Bind и после SetPose:0 enabled SDK controllers, кисти ниже плеч; PNG просмотрены.
  Прогон306: Android PASS/0 ошибок; existing EditMode suites не стартовали за20с (0 тестов).
  Это ошибка инициализации runner, не провал assertions; передана сопровождающему issue1464.
  Lobby восстановлена,306 finish/receive выполнены; отчёты reports/final-306/.
  Прогон308: повторный Build сохраняет байты4 ассетов и GlobalObjectId состояний;
  SOURCE APPROVED отдельным ревьюером.308 finish/receive выполнены, отчёт reports/controller-repeatability/.
  Стрельба, фактический контакт хвата, реальные remote clients и Quest остаются непроверенными.
- Заявка207 была QUEUED до сообщения об обслуживании; сопровождающий отменил её при миграции.
  Обслуживание завершено, worker открыт; своей аренды нет. Новая заявка только после допуска.
  Binding ACK обновлён (426/428), contract-agree и допуск foundation выполнены.
  Контракт weapon-use-context@1 прочитан в checkout владельца и ACK оформлен.
  Предложение366 реализовано: definition и held capability читают профиль из контроллера оружия.
  WeaponSystem сохраняет базу WeaponReadinessController (Profile/IsConfigured), по ответу428.
  Собственная миграция18 WeaponInfo, legacy-профиль и writer BotWeaponCapabilityMigration удалены;
  diff Data/Weapons пустой. Локальная компиляция6 сборок и временных probes PASS после этой правки,
  отчёты reports/offline-compile/. Unity weaving/runtime этим не проверены.
  Порядок arsenal-generator/composer-presentation merged → equipment-foundation согласован406:
  владелец арсенала атомарно мигрирует станции и BotCombatStandBuilder; после этого наш rebase.
- Ordinary factory/Gunner ещё hybrid; новые equipment consumers не подключены. SDK60 не реализован.
  Обычных коммитов/push и принятия этой задачи пользователем нет.

Исторические локальные evidence: `tmp/bots-refactor-200/reports/`, `tmp/bots-refactor-193/`,
`tmp/bots-refactor/offline-compile/` (только в этом worktree). Новые reports/probes —
`tasks/bots-fix/reports/`; старые ссылки сохраняют provenance и не становятся второй доской статуса.
Технические решения: [full-clips plan](full-clips-plan.md),
[review](full-clips-review.md), [verification](full-clips-verification.md).

## Критерии проверки и приёмки

### Уточнение после пользовательского просмотра318 и authoring-прогона328

Просмотр не принят: скольжение, оружие вне рук Cyborg, неверный support/наклон Rifle MEF.
Pistol MEF визуально нормален. Безоружная поза принята отдельно.328 finish/receive выполнены,
20 сочетаний модель/оружие/состояние; полный отчёт reports/clip-authoring/probe.json.
SDK grabber не является ребёнком visible wrist у обеих моделей; относительный offset меняется
до353.64мм. Масштабы root/wrist равны1. Исходный preview ошибочно замерял controller-hand после
проигрывания клипа. Такой offset не является authored геометрией руки.
Импортированные locomotion clips имеют averageSpeed примерно1.5–1.94м/с; старый preview двигал
корень по синусоиде с максимумом0.628м/с и возвращал его назад внутри того же Move. Направление
preview исправлено на отдельные монотонные Forward/Back/Left/Right; tempo ещё требует коррекции.
Скорость по высоте ankle в328 — только оценка, не доказательство контакта/отсутствия скольжения.
supportToWristMm сравнивает разные frames и не является критерием попадания в опору.

Независимое ревью отклонило автоматический reverse-P для Cyborg: primary PPK/AK105 авторился
только по MEF_Base_Avatar, точного Cyborg grip entry нет. SDK fallback к root не допустим для NPC.
На ревью передана NPC-only recipe: явный MEF reference → анатомический hand frame → модель NPC,
единый generated socket в BotCombatAssets; runtime читает его и не обращается к controller-hand.
Контакт пальцев/support и направление ShotSource должны пройти отдельные замеры и просмотр.
Второй кандидат: native gait speed целевой модели измеряется при editor root-motion sampling;
runtime сохраняет единственного владельца root, applyRootMotion=false, rate берёт из его скорости.
Это уточнение согласованного full-clips плана; gameplay всё ещё не принят.

- SDK claim: direct/replayed grab, predicted VR rollback, nested receive, nested permissive provider,
  unresolved owner, re-enable, server-only write, transfer/drop/body destruction, сохранённая NT authority.
- MEF/Cyborg × Pistol/Rifle: движение/ускорение/повороты/strafe ≥30с; один Animator/noIK,
  неизменные bone lengths и socket offset (<1мм/.1°), фотографии реального хвата с оружием.
- Cold M=0/C=0, Equipment→Combat, реальный projectile/урон и один authored shot; actual ShotSource/LOS,
  без отдельного поворота кисти или подмены направления пули.
- Death/ghost/respawn/skin/map Closing/VR pickup; старые body/epoch не двигают и не стреляют новым предметом.
- Адресный ServerOnly+2client стенд: late join/order/host duplicate, точные RunId/ParticipantId/ProcessSessionId;
  это не XR-ввод и не Quest acceptance. Stop/cleanup/finish перед анализом.
- AndroidCompileGate и затронутые existing suites. Новые gameplay NUnit только после фактического user acceptance.
