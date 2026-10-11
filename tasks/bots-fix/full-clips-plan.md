# Боты: полная клиповая анимация и жёсткое удержание оружия

Технический план и история решений. Текущий статус: [Readme.md](Readme.md).

> Исполнение: основной агент inline по `superpowers:executing-plans`. Пользователь поручил
> независимое ревью: после APPROVED от другого агента приступать без повторного разрешения.
> Этот план заменяет бот-путь кистей/IK предыдущего плана; принятые правила authority,
> фаз, жизненного цикла сессии, настоящих выстрелов и единого оружейного учёта сохраняются.

**Цель:** бот целиком анимируется Rifle/Pistol-клипами; оружие жёстко следует за кистью,
без IK, VR-захватов, сопротивления и VR-поз пальцев. Удерживаемое игроком оружие также
следует кисти без translation/rotation resistance.

**Стек:** Unity 6, Animator Humanoid, Blaze AI, Mirror; существующая оружейная система
и UxrProjectileSource только для учёта, выстрела/эффектов/урона, не для позы бота.

## Требования и границы

- MEF и Cyborg; Heavy вне анализа, ассетов и матрицы.
- Скелет видимого бота имеет одного писателя — его Animator. Никаких wrist targets,
  HumanPose-copy по кадрам, обратного решения по muzzle, UXR hand poses, Body/Arm/Leg IK.
- Один владелец движения корня: BotBody, получающий маршрут/положение от Blaze либо
  домашнего Navigator. Animator.applyRootMotion=false; камера/хитбоксы следуют костям этого скелета.
- Один владелец положения экипированного оружия: крепление к кисти видимого скелета.
  SDK manipulation, recoil transforms и NetworkTransform interpolation не пишут удерживаемый предмет.
  Снаряды/звук/эффекты и урон используют существующий действительный ShotSource и оружейный учёт.
- Пальцы — из клипа. Если FBX не содержит нужной позы пальцев, добавить позу как клип/слой Animator,
  а не возвращать SDK-позу или ручную запись костей.
- Нельзя подменять владение фиктивным UxrGrabber/GrabObject. Нельзя добавлять вторую копию M/C,
  второй lifecycle подготовки или прямой обход готовности/фаз ради выстрела.
- Readiness сейчас требует физическую руку (`HasContext`); смена этой границы согласуется
  с weapon-system. Самостоятельных правок в его WRC/Core/WeaponSystem до согласования нет.
- Компиляция/пробы в worker через брокер; общий редактор и основной checkout только чтение.
  Нормальный commit/push после пользовательской приёмки. Новые NUnit механики отложены по AGENTS;
  сначала временные RED→GREEN probes, существующие тесты и Android.

## Источники правды и контракты

| Состояние | Владелец | Потребители |
|---|---|---|
| Фаза, жизнь, допуск карты | существующие MapReferee / PlayerSession / UxrActor / MapRunAdmission | runtime, движение, экипировка, выстрел |
| Выбор действия/цели | Blaze | runtime, Animator, Gunner |
| Положение корня | BotBody на сервере; существующий root NetworkTransform на клиентах | видимый Animator, socket, хитбоксы |
| Поза всех костей | один Animator видимой модели | mesh, хитбоксы, socket, camera anchor |
| Категория и фаза анимации | серверный снимок Animator | клиенты воспроизводят тот же controller/state/phase |
| Владелец предмета | общий WeaponEquipmentBinding | readiness, authority, visual attachment, teardown |
| M/C, готовность, автоматика | существующий оружейный owner | Gunner только запрашивает подготовку/выстрел |

**WeaponEquipmentBinding** — оборудование как доменное владение предметом, а не VR-хват.
Одна authoritative связь weapon→body с epoch. VR-контекст читается из реального SDK-захвата;
контекст NPC — из фактической связи экипировки. Read-only resolver не хранит параллельного holder.
Binding является источником и для readiness, и для socket/teardown; Gunner его не копирует в own Held bool.
Equipment resolver подключается также к существующему StateEventAuthority.IsAuthorOfItem/ShouldSend,
включая вложенные части оружия; авторство выводится из body authority, не из повторного вызова item resolver.
`WeaponUseContext` — immutable снимок владельца/authority/alive/equipment epoch, без M/C.
`RequestAutomationPreparation` остаётся единственным входом, контекст выводится owner'ом.
На передаче VR↔NPC активен ровно один способ владения/позы; двойное владение отвергается.

**BotAnimationTransport** — MonoBehaviour + специализированные Mirror NetworkMessage,
не добавляемый после Spawn NetworkBehaviour. Передаёт body netId, category/controller,
Для каждого слоя передаёт state hashes, normalized phase, переход/duration, weight и sequence;
также authored параметры BlendTree/aim и server time. Не передаёт отдельные головы/кисти/корень.
Host не применяет свой снимок повторно. Late join получает текущий снимок, не только события;
при ещё не созданном body сообщения удерживаются ограниченно до Spawn и удаляются при despawn.
На клиентах штатные NetworkTransform кистей/головы выключены только у ботов: их писатель Animator.
Root NetworkTransform остаётся единственным транспортом движения корня.

## Файлы и ответственности

- `Assets/Scripts/Bots/BotBody.cs`: root motion и связь с видимым Animator; убрать ApplyPose кистей/головы.
- `BotCombatDriver.cs`: Blaze на navigation proxy без второго Animator/скелета; команды видимому Animator.
  SDK Blaze получает явный внешний animation source; перенаправление одного animManager недостаточно.
- `BotRuntimeController.cs`: один порядок LateUpdate после Animator: attachment→LOS→fresh shot intent.
  Убрать подписки на UXR StageUpdating/StageUpdated и этапы PreparePose/UpdateHandPoses.
- `BotGunner.cs`: приобретение/экипировка/действительный выстрел через оружейного владельца;
  убрать GrabObject/support grabs, UXR poses, BotWeaponPoseSolver, ammo-копии и wrist writes.
- Новые `BotClipBody.cs`, `BotAnimationTransport.cs`: композиция видимого Humanoid Animator,
  runtime отключение VR/IK писателей только на теле бота, транспорт клипового состояния.
- Новый `Weapons/WeaponEquipmentBinding.cs` + use-context contract: владелец связи экипировки и release/drop.
  Readiness адаптирует weapon-system; реализация границы требует его явного согласования.
- `WeaponInfo` / выбранный owner-owned weapon authoring: постоянный `BotGripPose` относительно RightHand.
  Один authored offset на ствол/семейство, без измерения по финальной позе прошлого кадра.
- `BotCombatAssets`, `Editor/Bots/BotCombatSetup.cs`, Resources/Bots controllers: полные Rifle/Pistol
  клипы/переходы, слой пальцев при необходимости, IK Pass выключен на всех слоях.
- `Editor/Bots/BotClipComposition.cs` (новый): проверка/подготовка только MEF/Cyborg binding к видимому
  Animator и контакта authoring. Не создавать второй AvatarRegistry или менять выбор скина.
- SDK Manipulation: общий запрет resistance для реально удерживаемого primary firearm grip;
  произвольные grabbables/затворы/магазины не превращать в «оружие в руках» по ancestry.
- SDK Blaze `Scripts/BlazeAI.cs`: убрать принудительное создание локального Animator на proxy
  через RequireComponent; добавить `BindAnimatorSource(Animator)` и общий resolver источника.
  Awake и refresh/rebind (`GetComponent<Animator>` сейчас строки405/668) используют resolver;
  animManager и прямые `anim.GetCurrentAnimatorStateInfo` читают один и тот же выбранный Animator.
  Без external source существующие Blaze actors используют свой локальный Animator;
  при отсутствии обоих source — fail-closed до запуска поведения. Запись патча в Docs/BlazeAI/sdk-patches.md.
  OnEnable не вправе вернуть local source поверх external binding; это отдельная проверка re-enable.
- `AvatarTeardown`, `EquipmentStrip`, `PlayerLoadoutManager`, `DeathDropEjection`: получать NPC-предмет из общего binding,
  а не только из SDK hands; единичный drop/confiscate, очистка до уничтожения тела.
- Stand recorder / measurements: вместо holders/snap primary-support мерить root→shoulder→wrist→weapon,
  неизменность socket offset и bone lengths, отсутствие IK/VR writers, реальные снаряды/authority.
- Docs: обновить этот план, gameplay, bots-blaze-integration, troubleshooting, sdk-patches и координацию.

## Задача 1. Проверить композицию клипов и закрепить новый контракт

- [ ] Сохранить checkpoint предыдущего инженерного среза; прочитать ближайшие планы weapon-system,
  legs-ik, calibration и map-owner, согласовать пересечения и SDK номера до новых правок SDK.
- [ ] Передать weapon-system точный use-context/equipment контракт и получить решение по его внедрению.
  Начать независимую анимационную работу после APPROVED ревью, зависимый chamber путь — после ответа owner.
- [ ] В worker проверить реальные видимые Animator/bone binding MEF/Cyborg: Hand/Hips/Head/Feet
  должны совпадать с используемыми mesh/hitbox костями. Префабная композиция SDK здесь только входные данные.
- [ ] Предпочесть уже существующий Humanoid Animator модели. Если binding неполный, editor authoring
  создаёт Avatar asset для того же видимого скелета; fail-closed при недостающих bones.
  Никакого runtime fallback к копированию поз или старому IK. Созданные assets/meta только result checkpoint.
- [ ] Проверить curve/muscle binding пальцев в Pistol/Rifle clip set и scale/contact обоих моделей.
  Не утверждать «есть хват в клипе» только по имени клипа. Отключить IK Pass в контроллерах.
- [ ] RED baseline: постоянное движение/ускорение/разворот и оружие относительно плеча; измерить
  world translation обеих систем, wrist target до/после SDK, bone lengths; текущий сопротивляющийся путь
  должен демонстрировать lag, либо записать, что этот стенд эпизод пока не воспроизводит.

## Задача 2. Один клиповый писатель скелета на сервере

### Уточнение по runtime preflight 190: позой нужно владеть до Awake

Подтверждённый отказ: у MEF в Play после обычного Spawn под MEF_Rig остаётся10 transforms,
пальцы отсутствуют в Animator.GetBoneTransform при30 finger entries в самом Avatar.
UxrStandardAvatarController.Awake создаёт UxrBodyIK, Initialize переподчиняет общий корень
рендерного скелета Dummy Forward вне Animator. Поздний controller.enabled=false не отменяет
этой структурной мутации. Запрещено компенсировать её обратным reparent каждый кадр.

Рекомендуемое решение: editor authoring создаёт NPC prefab variants MEF/Cyborg от существующих
player prefabs. В variant до любого Awake выключены UseBodyIK/UseArmIK/UseLegIK/native leg IK,
controller/tracking/input и bone NetworkTransforms; visible Animator содержит NPC Avatar/controller,
все IK Pass выключены. Root NetworkTransform и исходный render skeleton сохраняются.
У variant свой Mirror assetId: клиент однозначно создаёт ту же клиповую композицию до Awake,
без разбора opaque Spawn payload и предварительного второго протокола «это бот».

Пары source prefab→NPC variant генерируются единственным BotClipComposition в BotCombatAssets.
Это производный каталог исполнения, не второй AvatarRegistry: TeamAvatarStrategy и SourcePrefab
остаются владельцами выбора внешности. Один AvatarBodyFactory выбирает вариант только по
каноническому PlayerSession.IsBotControlled, обслуживает оба Instantiate в AvatarManager,
сохраняет authored source в SourcePrefab для reconciliation. Unknown model — fail-closed,
без превращения в human/IK prefab. Human, stress fixture с marker=false и Heavy не переводятся.
Отдельный разрешённый lifecycle путь: при session.IsEliminated и точном совпадении authored source
с TeamAvatarStrategy.GhostPrefab factory пропускает штатный ghost prefab без BotClipBody/Gunner/equipment.
Это canonical ghost selection, а не fallback неизвестной живой модели. Respawn снова требует валидный
MEF/Cyborg variant; cleanup старого equipment завершается до ghost replacement.
Создание клипового variant само по себе не зависит от readiness; подключение factory к обычным
ботам выполняется атомарно с полным equipment/runtime cutover, без промежуточного гибрида.

BotAvatarSpawnRegistry первым шагом GameNetworkManager.OnStartClient регистрирует generated variants
у Mirror после base.OnStartClient (штатная RegisterClientMessages уже выполнена), затем существующий
NetworkUxrIdentity устанавливает handlers остальных UXR-предметов. На server variant Spawn использует
тот же prefab assetId; SDK avatar UniqueId продолжает вести UxrMirrorAvatar, без второй Align.
Registry не выбирает модель и не хранит runtime ownership; при StopClient снимает только свои
registrations. Никаких новых NetworkBehaviour после Spawn, экземпляр BotClipBody уже в variant.
Bind вызывается после разрешения реальной session/life и использует один visible Animator.

Проверки: один и тот же NPC variant проходит Awake/Spawn на server и client; render bones остаются
descendants Animator, нет Dummy Forward/arm/leg solvers или второго pose writer; обе категории,
re-enable и замена тела сохраняют этот инвариант. Late join получает регистрацию до первого Spawn.
SourcePrefab/выбор скина и human/stress пути не меняются. AvatarLoadoutTests/PrefabCompositionTests
ожидания перечисляются до приёмки; новые gameplay NUnit только после подтверждения пользователем.
Цена — два автоматически созданных variant ассета и startup registration, риск prefab overrides
при изменении базовой модели; Build/Validate проверяет соответствие исходной модели и runtime binding.
Authoring отдельно проверяет frame видимого Animator относительно body floor и pivot Humanoid Hips.
У Cyborg исходный VR frame и общий pelvis/spine parent не пригодны для full-body retarget без
нормализации rest space. Разрешена только статическая нормализация невзвешенных pivots с сохранением
мировых poses children, weighted matrices и BakeMesh world vertices; rootBone localBounds переносится
в тот же мировой объём. В runtime нет поправки по высоте/копирования poses, всё описывает NPC Avatar.
Root NetworkTransform variant сериализуется ServerToClient до любого Spawn.
Альтернатива — pre-spawn метаданные и собственный dormant spawn protocol для общего human assetId;
она добавляет порядок сообщений/кеш и сложнее. Variant рекомендован как проверяемая структура.

- [ ] BotClipBody включается только на подтверждённом бот-теле; до первого клипового кадра выключает
  avatar controller, device tracking, IK/animated legs, hand-pose runtime и competing avatar render writers.
  Игроки и Heavy не затрагиваются. Сохранять health/network identity/session, не удалять UxrActor.
- [ ] Animator видимого скелета получает существующий Rifle/Pistol controller. Навигационному proxy
  оставить Blaze/agent/collider, направить его animation manager на этот Animator; не клонировать скелет.
- [ ] Сначала минимальный SDK Blaze external-Animator port: `BindAnimatorSource(Animator)` на неактивном
  proxy до Awake/Start. Awake/OnEnable/refresh и повторное включение создают/обновляют AnimationManager
  от общего source; обязательный local Animator не создаётся. Проверить все прямые animation reads,
  disable/enable, refresh, controller category switch; нельзя оставить animManager с прежним Animator.
  Для штатных prefab actors без порта сохраняется локальный source.
- [ ] BotBody перемещает root на той же стадии, что navigation intent, до Animator evaluation.
  Head/camera anchor и hitboxes следуют animated bones без отдельного IK или wrist/head targets.
- [ ] GREEN: движение/страф/idle, поза всех костей совпадает с клипом; ни одного IK/VR pose вызова,
  длины костей неизменны. На смерть/ghost body writer отключён, на respawn/skin — новый binding без старого rig.

## Задача 3. Экипировка и выстрел без VR-захвата

### Согласование owner 2026-10-08: первый отдельный срез

`weapon-system-to-bots-full-clips.md` явно подтверждает получение контракта
`BOTS-FULL-CLIPS-CONTRACT-20261007-1`. Binding принадлежит bots-fix; read-only
WeaponUseContext/Resolve и readiness по `(Kind, User, Epoch)` — weapon-system.
Первый срез: binding без производственных потребителей; далее owner adapter,
затем атомарное переключение factory/Gunner. Не вливать binding до пользовательской приёмки.

Binding — NetworkBehaviour на корне prefab до Awake/Spawn. Один серверный SyncVar snapshot
содержит netId владельца, монотонную epoch, authored socket offset; detach также содержит
world pose/velocity для восстановления транспорта. Клиент только проецирует snapshot.
Неразрешённый owner не снимает claim: SDK-grab запрещён, OwnerAlive=false, socket не вычисляется.
OwnerAlive проверяет canonical session marker, актуальное тело сессии и жизнь тела.

Запрет SDK-grab выводится из generic read-only availability provider, без записи/восстановления
копии `_isGrabbable`. Provider применяется к getter и единому low-level GrabObject до BeginSync,
включая прямой и полученный сетевой вызов. В кеше допустима только ссылка на provider с проверкой
актуального ancestor, не булев результат. Производственные WeaponInfo/профили не меняются.

Гонка: локальный VR grab мог предшествовать получению серверного claim. Проекция claim сначала
откатывает такие SDK-grabs через штатный receive entry (`ExecuteStateSyncEvent`), без сетевого echo
и без второго ReleaseObject implementation; затем выключает SDK root pose/NT/физику и включает
жёсткий socket. Пока проекция не закончилась, socket не пишет transform. Attach сервера отказывает
на любой уже известный SDK-grab. Drop восстанавливает pose/transport до следующего кадра физики.
Этот race contract требует отдельного probe и одобрения независимого ревью до реализации.

SDK60 — только held firearm resistance в Manipulation; UxrFirearmWeapon/Recoil*, ammo/spуск/haptics
не трогать без отдельного согласования. Generic equipment availability требует отдельной записи
SDK-патча и сверки pipeline. Номер старого ArmIK patch58 пересёкся с новым vr-test-stand58:
это событие координации, перенумеровать собственную ещё не влитую запись до следующего checkpoint.

- [ ] Реализовать согласованный общий binding/контекст (owner оружия владеет readiness-адаптацией).
  Attach проверяет world authority, body alive/admission/epoch, отсутствие VR-holder/другой экипировки.
- [ ] Определённый authored socket offset устанавливается один раз; attachment жёстко следует
  animated RightHand в LateUpdate, без interpolation/resistance. Физика held item kinematic;
  SDK manipulation/recoil pose и оружейный NT не имеют записи, пока binding active.
- [ ] При drop атомарно снять binding/attachment, восстановить world pose, физику и сетевой transport;
  SDK-захват игроком снова допускается только после освобождения. Не хранить competing ownership flags.
- [ ] Прицел: поворот корня на цель и подходящие aim-клипы/параметры Animator. Не вращать кисть/оружие
  отдельно и не подменять направление пули: только actual ShotSource. Высотные цели без поддержанного
  aim-диапазона не приводят к «невидимому выстрелу в цель»; LOS измеряется по действительному стволу.
- [ ] Shot intent потребляется после обновления attachment; fresh frame/body/binding/target, phase,
  CanUse/readiness/author обязательны. Для chamber автоматики M/C и подготовка остаются у owner.
- [ ] RED→GREEN: Rifle/Pistol и cold chamber при готовом owner-port; drop→VR pickup,
  equipment→combat, death/confiscate/skin/map Closing; 0 unauthored/двойных выстрелов.

## Задача 4. Клиенты и жёсткое следование оружия игрока

- [ ] Клиент создаёт clip-only presentation по серверному snapshot, не включает Blaze/navigation/Gunner.
  Выключает только competing bone NetworkTransforms; root получает существующий root NT.
- [ ] Attachment на клиентах выводится из того же equipment snapshot. Нет отдельного бот-кеша
  состояния владения или клиентского расчёта стрельбы. Порядок late join body/weapon разрешается по netId+epoch.
- [ ] Оружейный drop/передача прекращают socket-follow на всех машинах; старый snapshot не возвращает binding.
- [ ] Для физически удерживаемого human firearm grip запретить сопротивление в общей точке SDK:
  не менять serialized resistance и не сохранять/восстанавливать числовую копию на grab/release.
  После release обычная физика; ограничения механических деталей сохраняются.
- [ ] Held-motion policy покрывает оба применения resistance (`UxrGrabManager.Manipulation` обычный
  solve и SetPositionAndRotationUsingConstraintsInternal), а также firearm transform recoil после
  constraints. Если recoil двигает held root отдельно от руки, переносить его видимую реакцию
  в единственного владельца позы/эффекты; не оставлять «жёсткий» hold с другим transform writer.
  Probe human held wrist→weapon во время движения, поворота и выстрела отличает это от NPC clip recoil.
  Проверить one-hand и two-hand hold после Manipulation/PostProcess, при смене primary/support,
  а не только root до SDK или сглаживание без стрельбы.
- [ ] GREEN локальных транспортных probes: переупорядоченный spawn/снимок, late join,
  host без double apply, stale epoch/dead body, socket offset постоянен при движении.
  Отдельный remote client остаётся пользовательской/двухпроцессной приёмкой, не выводится из compile.

## Задача 5. Удалить старый путь и получить проверяемый результат

- [ ] Удалить BotWeaponPoseSolver, wrist/head target управление и UXR pose selection из bots.
  SDK62 (бывший58, перенумерован после пересечения) остаётся отдельным исправлением human IK,
  не становится зависимостью клиповых ботов.
- [ ] Стенд MEF/Cyborg × pistol/rifle: ≥30с движение/ускорение/остановка/повороты/страф;
  bone lengths и weapon-to-wrist offset постоянны, no follower lag сверх численного допуска 1мм/0.1°.
  Показать shoulder/wrist/weapon trace, фотографии полной позы и пальцев, реальные projectile events.
- [ ] Жизненный цикл: смерть/ghost/respawn, drop/pickup игроком, skin/map replacement и late join.
  Неполный путь останавливает движение; old body не двигает/не стреляет новым weapon.
- [ ] AndroidCompileGate PASS, existing Bots и затронутые existing suites. До принятия не менять
  gameplay NUnit; перечислить старые SDK-holders/IK/avatar-composition ожидания в отчёте.
- [ ] Обновить документы и координацию; дать результаты и границы human/remote/Quest проверки.
  Нормальный коммит/push только после пользовательского принятия; worktree/main-editor правила сохраняются.

## Обязательный фокус ревью

1. Readiness без VR-hand не обходится прямым refill/выстрелом; equipment binding — один источник владения.
2. Видимый скелет, хитбоксы и socket используют одни bones, без hidden animated/render skeleton split.
3. На клиенте нет второго писателя bone pose или удерживаемого оружия, snapshot переживает late join/epoch.
4. Смерть/смена тела/карты/передача игроку освобождают общий binding ровно один раз.
5. «Сопротивление 0» касается held firearm grip, не всех частей/коллайдеров/механизмов; не правим Heavy.

## Цена выбранного решения

Видимый прицел ограничен существующими aim-клипами; для вертикального диапазона потребуются
дополнительные позы/BlendTree, а не скрытый IK. Один комплект клипов не гарантирует контакт левой
руки с каждым размером ствола: необходимы проверенные authored offsets/варианты клипа.
Новый equipment контекст требует согласованной оружейной границы; legacy-only обход запрещён.
