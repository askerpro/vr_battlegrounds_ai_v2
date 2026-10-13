# Боты через штатный поток игрока: проект перехода

2026-10-12. Решение пользователя: бот — обычный аватар игрока, ИИ управляет оружием, адаптер переводит
намерения ИИ в позы головы/контроллеров и ввод. «Сразу переходим на новую модель». Проект — архитектор
(только чтение, HEAD `533c7e947`); сверка с требованиями avatar-ik (событие 4182) — ниже.

## Решения пользователя (2026-10-12)

- Вход бота — **вариант P «кукловод»** (без патча SDK); нужно согласие avatar-ik по п.1 (событие 4184).
- Отдача у наблюдателей — **как у людей**; расхождение мерить на этапе 2, общая доработка — отдельно с weapon-system.
- Неточность — **неточность ИИ через точку прицеливания + отдача оружия добавляет разброс**: компенсация отдачи
  у бота человекоподобная, неполная (не гасит отдачу до нуля); собственный разброс оружия — как у игроков.
- Перезарядка — **automation** (`RequestAutomationPreparation`).
- По рекомендации (не развилки): binding спящий (контракт r3), pose-fit отложен (`parked/pose-fit-2026-10-12`),
  присед — после legs-ik/pose-set, трафик/частота поз — замер на этапе 2.

## Главный вывод

Виртуальное устройство UXR (свой `UxrTrackingDevice`/`UxrControllerInput`) без патча SDK не работает:
- трекинг и ввод обновляются только у аватара `Local` (`UxrManager.cs:1336-1341`, `:1940-1958`, `:2266`);
- спуск оружия читается из статического `UxrAvatar.LocalAvatarInput` при `AvatarMode==Local`
  (`UxrFirearmWeapon.cs:468-485,497`);
- `Local`-аватар один на машину, на хосте он занят человеком.

**Вариант P «кукловод» (рекомендация).** Проверенный приём `StressPuppet.cs:7-20,55-66`. Бот — аватар без
владельца, `UxrMirrorAvatar.cs:105` переводит его в `UpdateExternally` на всех машинах: везде он выглядит
как удалённый игрок. Адаптер на сервере пишет те же трансформы, что у удалённого игрока пишет
NetworkTransform (корень, камера, кисти). Действия — вызовы SDK: GrabObject/Release, `TryToShootRound`
от автора, подготовка через automation. Намерения собираются в `BotInputFrame`.

**Вариант V.** Патч SDK под нескольких авторов ввода (трекинг/ввод не-Local аватаров, ввод оружия не из
статического LocalAvatarInput). Затрагивает avatar-ik, manipulation, weapon-system. Исполнитель
`BotInputFrame` меняется, остальная архитектура та же.

## Компоненты (только сервер, AddComponent как в `BotDirector.cs:375-385`)

- `BotInputFrame` — намерения кадра (голова, две кисти, спуск, хват, сброс).
- `BotAvatarDriver` — единственный писатель камеры и кистей бота.
- `BotHands` — хват/сброс через SDK, primary и support из `WeaponGripBinding.TryResolve`.
- `BotWeaponAim` — желаемая поза оружия W*, из неё позы кистей.

Владельцы: корень — `BotBody` (от Blaze-proxy); тело — штатный IK UXR + штатные ноги; оружие —
`UxrGrabManager`; пальцы — позы хвата SDK; тактика — Blaze; выстрел — `BotGunner`.

## Кадр на сервере

1. Update: `BotDirector.Think` → `runtime.Tick`; Blaze двигает proxy; `BotRuntimeController.Update(11000)`:
   `BotBody.PrepareFrame`, сборка `BotInputFrame`.
2. LateUpdate `UxrManager.PostUpdate`: перед Manipulation (`UxrManager.cs:1958-1975`) драйвер пишет камеру
   и кисти; дальше штатно Manipulation → ConstraintsApplied (отдача, MainGripAimLock) → Animation →
   PostProcess (IK тела, рук, ноги).
3. `BotRuntimeController.LateUpdate(11000)`: гейт `Sees` по фактическому лучу дула, `TryToShootRound`,
   замер ошибки дула для поправки следующего кадра.

Прицел: W* — ствол на цель, верх по мировой вертикали, без завала. Правая кисть = W* × снап primary ×
(граббер→запястье), левая = W* × снап support; геометрия снимается при хвате. Медленный интеграл ошибки
дула (`BotAimMath.TryGetMuzzleError`) компенсирует отдачу. «Ствол = пуля» на сервере по определению:
снаряд из `ShotSource`, отдача поворачивает ствол до выстрела.

Клиенты: кода ботов нет. NetworkTransform (корень, камера, кисти — `ServerAuthoredAvatar.cs:24-31`),
события SDK (хват, позы, выстрел). IK и ноги считает клиент, `RemoteAvatarIKThrottle` прореживает.
Новых SyncVar нет.

## Остаётся / уходит

Остаётся: BotDirector, BotMind, BotSenses, Shopper/BuyPlan, Navigator/NavMesh/Route, BotCombatDriver
(Blaze-proxy с Animator-заглушкой), BotRuntimeController, BotGunner (`IsHolding` → UxrGrabManager),
BotBody, `PlayerSession.IsBotControlled`/`BotAvatarSource`, стенд, BotAimMath (метрика).

Уходит после приёмки: BotClipBody, ClipBody-варианты, BotClipComposition/BotCombatSetup (клиповая часть),
sockets/aims/motions/clipBodies в BotCombatAssets, BotAim, AimPitch SyncVar, ветка ClipBody в
`AvatarManager.cs:385-401`, отложенный pose-fit (`parked/pose-fit-2026-10-12`).

`WeaponEquipmentBinding` — не удалять: влит, на него ссылается `WeaponUseContext.cs:44-61`, стоит в
префабах PPK/AK105; без реализации `IWeaponEquipmentSocketOwner` спит (`IsActive=false`). Контракт r3 —
dormant; удаление — совместным этапом с weapon-system.

## Сверка с требованиями avatar-ik (4182)

| # | Требование | Проект | Статус |
|---|---|---|---|
| 1 | Адаптер — источник поз головы/контроллеров на уровне устройств, как тестовый XR-адаптер launch-infra; не писать кости/камеру/CameraTransform; один писатель на кость | P пишет камеру и кисти — те же трансформы, что NetworkTransform у удалённого игрока; кости не пишет, IK штатный | **Расхождение**: уровень устройств для N не-Local аватаров = вариант V (патч SDK). Нужен ответ avatar-ik и выбор пользователя |
| 2 | Рост/масштаб через ServerAcceptCalibration + AvatarCalibrationApplier | Добавить: бот получает калибровку через сессию (`PlayersManager.cs:93`) | Принято |
| 3 | Ноги: сейчас текущие, после legs-ik/pose-set новые; порядок вливаний согласовать | Этапы 0–2 на текущих ногах; присед после pose-set | Принято |
| 4 | Кто решает IK на сервере/хосте/клиентах, бюджет N×IK на Quest — с performance до реализации | Сервер считает IK (нужен для луча дула); клиенты — как удалённый игрок; на сервере/хосте прореживания нет | Отправить оценку performance до этапа 0 |
| 5 | Хват — штатный grab, порядок писателей кисти UxrManager без изменений | Так и есть | Принято |
| 6 | DisableVrWriters исчезает, нет второго писателя (Animator клипов + IK) | Клиповый Animator у бота-аватара не используется; Animator-заглушка только у Blaze-proxy | Принято, проверить на этапе 0 |

## Итоги ревью зон (2026-10-12)

**avatar-ik 4201 — P принят, V отклонён.** П.1 из 4182 переформулирован: «источник в слоте ввода, эквивалентный
NetworkTransform удалённого игрока». Условия:
1. Один компонент-адаптер, только у аватара без владельца и только на сервере/хосте; на клиентах единственный
   писатель — NetworkTransform.
2. Пишет только корень, камеру (локально к пивоту CameraController) и `Avatar.Left/RightHandBone` (World) — только
   до стадии Manipulation (Update или StageUpdated до Manipulation), никогда после Manipulation/IK.
3. Не пишет кости, пивот, Dummy Forward, масштаб, поля BodyIK; не использует UxrLocomotion/UxrControllerInput.
4. Калибровка — сессия бота + ServerAcceptCalibration с ростом бота; камера на высоте глаз × масштаб.
5. Камера движется вместе с корнем, без отдельного сглаживания (шаг ног считается по камере).
6. Бот появляется стоя; телепорт корня — одним кадром (скачок > teleportDistance × scale переносит ноги).
Прочее: ветку IsBot в RemoteAvatarIKThrottle не добавлять; ссылка `UxrLegLocomotion.cs:170` устарела (решение
«идёт» — UxrBodyMotion по камере, `UxrAnimatedLegs.cs:172-176`); `UxrGrabManager.cs:412` — AvatarMoved
игнорируется у UpdateExternally, телепорт бота может не перенести хват (проверить с manipulation); спуск — blend
позы кисти, ввод не нужен. Адаптер впишут в контракт писателей кисти. Diff адаптера — им на ревью до merge-request.

**weapon-system 4198.**
- Выстрел: `TryToShootRound(0)` только где `StateEventAuthority.IsAuthorOfItem(weapon)`, вне `IsInsideStateSync`,
  один вызов на выстрел, только после `true` от `RequestAutomationPreparation`. Запрещены прямые команды учёту,
  новые Cmd/Rpc, запись `LocalAvatarInput`. Этап weapon-system trigger-e позже переведёт спуск в машину состояний —
  тогда бот получит отдельный вход машины (предупредят).
- Automation как VrHand при условиях `WeaponContextSensor:41-47`: `use.AuthoredHere` (проверить `IsAuthoredHere`
  аватара бота на сервере), реальный SDK-хват основной рукоятью с `GrabLifetimeEpoch>0`, `IsWorldAuthority`.
  Грант ключуется (Kind, User, Epoch): после перехвата — запросить заново. Подсказки/сухой щелчок на хосте видит
  человек — weapon-system добавит условие Local отдельной правкой.
- Отдача: решение принято; SDK root recoil смещает корень относительно руки → при внешнем управлении кистью видно
  как скольжение ствола в ладони (их backlog recoil-hand-follow).
- Binding: сначала `record-merged` binding-foundation по `6849ba0e6` (r2 → active, их use-context-* требуют r2),
  затем r3 dormant без providing stage; r2 задним числом не отзывать. Гейт 3656 остаётся, пока Binding в коде.

**gameplay 4204.**
- Бот-ветки ClipBody в origin/dev нет — «не вливать», а не откатывать; клиповый путь в dev не вливать даже временно.
- Тело бота — `TeamAvatarStrategy` по TeamIndex/AvatarIndex, как у людей. `PlayerSession.BotAvatarSource` и ветку
  `IsBotControlled && HasBotAvatarSource` в PrefabFor — убрать; конкретная модель — AvatarIndex в каталоге команды.
- `IsBotControlled`: если нужен только серверу — держать в BotDirector/реестре ботов, не в PlayerSession;
  если клиентам — решение network.
- Бот = префаб игрока: хитбоксы, DamageLedger, смерть, труп, призрак, сброс через PlayerLoadoutManager в
  AvatarTeardown без бот-ветки (BotEquipmentLifecycle не нужен). Выстрел кредитуется сессии бота.
- Калибровка: `CreateBotSession` вызывает `ServerAcceptCalibration(<калибровка>, CalibrationOrigin.Connect)` до
  `NetworkServer.Spawn`; значение — параметр от ботов, в границах PlayerCalibrationRules.
- Покупки — только штатный серверный путь экономики.
- Этап 0: переключатель режима тела — в конфиге BotCombatStand; из зоны gameplay максимум правка CreateBotSession;
  нужен zone-request с точными writes, ACK по факту diff.

**performance 4199.**
- Отдельного бюджета ботов нет: бот входит в «игрок + 9 удалённых», 13,9 мс при 72 Гц.
- Quest-клиент: N ботов против N кукол стресс-теста — разница p50/p95 в шуме, uxr_post/skinning те же; 16 — стресс-
  точка, не критерий. Хост Quest: запаса нет (9 кукол p50 15,5 мс), порога до решения пользователя нет.
- Выделенный сервер: бюджета нет; на этапах 0 и 3 — кадр p50/p95/p99 и прирост на бота 0/1/8/16.
- Метод: условия к каждому числу (сборка, устройство, роль, сцена, SHA, Гц, FFR, CPU/GPU); quest-device-run rev3;
  фазы база/1/8/16, ≥40 с, две раскладки, ≥2 повтора; ProfilerMarker на BotDirector.Think, BotRuntimeController,
  BotAvatarDriver, BotHands, BotWeaponAim, гейт Sees; GC не растёт с N; каденция raycast-ов Sees явная; Editor/MCP
  во время замера не трогать. Перф-ревью диффа этапа 0 — до вливания.

**network 4206 — ACK проекта с условиями.**
- Трафик: NetworkTransformUnreliable 60 Гц, ожидать 4–6 КБ/с на бота на клиента (допустимо; 16 ботов ≈ 0,1 МБ/с).
  Частота ботов на этапах 0–2 как у людей; снижать — одним штатным параметром Mirror на все 4 NT бота, с ревью network.
  Этап 2: байт/с на бота у клиента и исход сервера при 8/16; порог — бот не дороже живого игрока.
- Спавн — только через `ServerAuthoredAvatar.Prepare` до `NetworkServer.Spawn`. Один писатель трансформов на процесс:
  сервер/хост — BotAvatarDriver, клиенты — только NT; ни одного бот-компонента на клиенте.
- Автор аватара и предмета — сервер; GrabObject/Release, TryToShootRound, automation — только на сервере, с явной
  проверкой IsAuthorOfItem/IsWorldAuthority, из Update/LateUpdate верхнего уровня, не из обработчика UXR-события (Issue 38).
- Поздний вход/reconnect — штатно (NT начальное состояние, UXR-снимок хвата, CombineUniqueId, AvatarStateEventGate).
  Смерть — сброс через SDK до Destroy (AvatarTeardown).
- ServerOnly: проверить, что без локального аватара и камеры UxrManager.PostUpdate доходит до драйвера и IK бота.
- Этап 2 (ярус C): ServerOnly+2 клиента, хост+клиент, поздний клиент к бою с ботом, держащим оружие, reconnect;
  оружие в руке у опоздавшего, нет дублей хвата, 0 ошибок Relay, угол ствол↔луч.
- Удалить вместе с клиповым путём BotAvatarSpawnRegistry и ветку RegisterClientPrefabs в OnStartClient (только в
  нашей ветке) — тогда условие 3611 снимается.

## Решение по базе (2026-10-12)

`PlayerSession._botControlled`/`BotAvatarSource`/`SetBotControlBeforeSpawn`, ветки AvatarManager, BotClipBody,
BotAvatarSpawnRegistry — **только в ветке bots-fix**, в origin/dev их нет (там есть серверный `BotDirector.IsBot`).
Поэтому новая модель строится **от origin/dev**, а не поверх клиповой ветки: клиповый путь (accepted
armed-development) остаётся архивом в ветке и в dev не вливается. Разногласие gameplay («убрать BotAvatarSource,
признак бота — в реестре BotDirector») и network («остаются, как в плане») снимается: в dev этих полей нет, новая
модель их не вносит; признак бота — `BotDirector.IsBot` на сервере, клиентам не нужен (кода ботов на клиентах нет).
Модель тела — AvatarIndex в каталоге команды через TeamAvatarStrategy (см. backlog team-body-catalog).

Хитбоксы: `Hitbox` — коллайдеры на костях (HitboxBuilder по скелету UXR), урон решает автор выстрела; выстрелы ботов
авторит сервер → серверу нужен позированный скелет всех аватаров. Политику IK выделенного сервера не менять.

**manipulation 4217 — модель принята; SDK 61/56 сняты, условия 3449 сняты.**
1. Хват только на сервере под `IsWorldAuthority`. `UxrGrabManager.GrabObject` правила не проверяет — вызывать
   только после `item.CanBeGrabbedByGrabber(grabber, point)` (расстояние + CanGrabDelegate = GrabRules, PlayerGrabManager
   ставит его и на руки бота) либо `TryGrab(avatar, side)`, когда кисть на снапе; затем `GrabObject(..., propagateEvents:true)`.
   Порядок primary → support. Отпускание `ReleaseObject(..., true)` только от автора. Только не удерживаемый предмет
   (`IsBeingGrabbed`), без выхвата. **Геометрию снапа не кешировать — читать у SDK каждый раз** (копия grab-state —
   нарушение инварианта; поправка к «снимаем один раз при хвате»).
2. TwoHandGrabPolicy/ManipulationConstraints работают при этом входе. Драйвер пишет абсолютную позу кисти каждый кадр
   до Manipulation и не читает кость обратно (KeepGripsInPlace пишет HandBone после ConstraintsApplied). Раскачку
   прицела мерить на этапе 0.
3. held-motion — зона manipulation: наш этап held-motion снять или перевести в after их этапа (сопротивление только у
   автора держащего аватара и относительно его корня). Этап 0 мерить с текущим сопротивлением.
4. Своего сброса рук при смерти не добавлять: в BotHands после `!IsAlive` перестать хватать. Их этап
   death-release-authority (отпускать только у автора аватара) — **блокер нашего этапа 1 «смерть со сбросом»**.
5. `UxrGrabManager.cs:412` подтверждён; закрывается их held-motion, отдельный перенос корня у нас не нужен.

Все шесть зон ответили; реализация может начинаться после перестройки plan.json и объявления этапа 0.

## Этапы (стенд; клиповый путь — по умолчанию до приёмки)

0. Прототип: переключатель режима тела на стенде, MEF + AK105: ходьба, хват, стрельба. Критерии: выстрелы/
   попадания не хуже раунда IV (101/101); медиана ошибки дула в Shoot ≤1,5°; зазор кисть–снап <1 см;
   оружие–граббер в кадре <1 мм; скольжение стоп не хуже базы; время сервера и байт/с на бота; 0 ошибок.
1. PPK, Unarmed, кобура, покупка, смерть со сбросом, призрак.
2. Сеть: ServerOnly + 2 клиента, хост, поздний вход, reconnect; угол ствол↔луч у наблюдателя ≤1° без отдачи.
3. Quest: 1/8/16 ботов, Quest клиент и хост.
4. Переход: удаление клипового пути, binding r3, документы, тесты после приёмки.

## Риски

- Сглаживание хвата SDK (held-motion) — отставание оружия, общее с людьми.
- Отдача у наблюдателей не видна (`RecoilAccumulator.cs:20-24`) — ствол ниже трассы, как у людей.
- Отставание ствола у наблюдателя ≈ угловая скорость × разница задержек — мерить.
- Досягаемость: W* ограничивать зоной досягаемости рук.
- Quest: 9 удалённых ≈1,4 мс IK (`RemoteAvatarIKPolicy.cs:9-12`); на сервере/хосте без прореживания.
- Сеть: ~3 КБ/с на бота на клиента — мерить.

## Согласования до реализации

- avatar-ik: п.1 (P или V), ноги при корне от BotBody (`UxrLegLocomotion.cs:170`), присед через высоту
  камеры, порядок с RemoteAvatarIKThrottle; ревью проекта.
- manipulation: серверный GrabObject/Release обеих точек для аватара без владельца, GrabRules/PlayerGrabManager,
  двуручные ограничения, held-motion, сброс в AvatarTeardown.
- weapon-system: `TryToShootRound` от автора без ввода, automation для бота как `VrHand`, отдача у
  наблюдателей, binding r3.
- network: 4 NetworkTransform на бота, ServerOnly/хост/поздний вход/reconnect, удаление SyncVar BotClipBody.
- gameplay: откат бот-ветки в AvatarManager, хитбоксы игрока у ботов.
- performance: бюджет N ботов × IK (сервер, хост, Quest-клиент).

## Ход этапа pf-prototype

- gameplay 4257: zone-ack на PlayersManager.cs (scope 666ea287…) по diff 4252; любая новая правка зоны — новый diff.
  Перед вливанием: в логе стенда PlayerFlow «калибровка принята (Connect)», гибрид в матче без изменений.
- manipulation 4259: начальная поза купленного ствола до SpawnServerObject по геометрии SDK — допустима (разово);
  аллокации GetPrefabGuidChain — принять на этапе 0, исправление у них (кандидат grip-pose-cache); при блокере
  бюджета прислать цифры.

## Решение пользователя 2026-10-13: миграция на новую архитектуру

«Новая архитектура нравится гораздо больше — можно удалять старый флоу на клипах и мигрировать.» Следствия:
- player-flow становится единственным режимом ботов в матче; старый гибрид dev (BotBody-руки, PoseRightHand,
  SetWeaponHands, FireRequestedShot, стадийный выстрел) и флаг режима удаляются на clip-cutover.
- Порядок этапов: pf-prototype → pf-equipment → pf-network → **clip-cutover** (перевод матча, удаление гибрида,
  BotCombatAssets/контроллеров прокси — если Blaze-proxy без них работает, binding r3, документы, тесты после
  приёмки) → pf-quest (перф на финальном коде).
- Архив клипового пути `bots-fix-clip-archive` и `parked/pose-fit-2026-10-12` удаляются после приёмки clip-cutover.

## Замеры этапа pf-prototype (стенд, Editor; отчёты tasks/bots-fix/reports/pf-round1..4, вне Git)

| Раунд (тикет) | Итог |
|---|---|
| pf-round1 (454) | 0 выстрелов: профиль готовности искали в WeaponData (у AK105/PPK — на WeaponSystem префаба); PPK недостижим |
| pf-round2 (457) | AK105 45/45; 62% Occluded из-за гейта «луч в силуэт»; PPK смещён 12 см (MainGripAimLock) |
| pf-round3 (462) | Эталон без отдачи/неточности: AK105 109/110, PPK 75/75; S/R/F/PF hitRate 0,42–0,86 при atShot ≤0,8°; PF смещение 34 мм |
| pf-round4 (469) | PE 59/59, E 62/62; PF 34 мм устранено (поддержка после устоявшегося хвата); F hitRate 0,61–0,85; выстрел: TryToShootRound ~80% стоимости |
| pf-round5 (490) | После SDK-патча 73 (manipulation): weaponLag p95 35–42 → 10–14 мм (остаток = расчётный откат root recoil 10,5 мм), поддержка на ходу; E 64/64 |
| pf-demo (482) | Демо 90/90/60 с: AK105 201/150, PPK 217/125, AK105 125/104; 0 ошибок; **пользователь принял этап 0 («вливай»)** |

Открыто: хвост зазора кисть–снап AK105 p95 35–45 мм = отставание предмета при движении (held-motion, SDK-патч 73
manipulation); MainGripAimLock + held-motion у людей (finding 4442/4445 weapon-system/manipulation); стоимость
TryToShootRound на авторе (сообщено performance/weapon-system).

## Решение пользователя 2026-10-12 (через avatar-ik, 4891): боты заменяют игроков

Ботов + игроков в сумме не больше 10. Отдельного бюджета ботов нет: бот входит в «игрок + 9 удалённых»; сверх
удалённого игрока он добавляет только ИИ/прицел/выстрелы на сервере. Точки замеров pf-quest — 0/1/9 ботов
(16 — вне продукта). Открыто у performance: Quest 3-хост уже p50 15,5 мс при бюджете 13,9 мс без ботов.

## Решение пользователя 2026-10-12 (через performance, 4895): хоста на Quest не будет

pf-quest: только Quest-клиент с 0/1/9 ботами + кадр выделенного сервера (ПК) при 9 ботах в бою. Замеры
«Quest-хост» не нужны. pf-network: хост остаётся только как ПК-хост (если поддерживается), Quest-хост не проверяем.

## Этап pf-equipment: решения эксперта (2026-10-12)

- Покупка: замену «пистолет в руке → покупка» в pf-equipment запускает только стенд; подключение покупок директора
  (OfferPurchase / ShouldArm в BotOrders) — на clip-cutover.
- Призрак бота ведёт тот же BotAvatarDriver (корень, камера, кисти в покое) без хвата/прицела/выстрелов;
  гибридный путь BotBody не используется (удаляется на clip-cutover).
- Уборка в кобуру — штатный путь SDK человека: Release поддержки → подвод DropAlign к Align якоря →
  UxrGrabManager.TryRelease (SDK кладёт в ближайший совместимый якорь).
- Ревью pf-equipment: avatar-ik 4997 — одобрено до вливания; после legs-ik/pose-set перепроверить пороги замеров кобуры
  (поза бедра при низкой голове). manipulation 4995 — ждём. Демо eq-demo (тикет 510): 0 ошибок, все функции работают.
- manipulation 5005/5016: pf-equipment одобрен с условиями: до вливания — счётчик уроненных при уборке
  (stowDroppedZero) и маркер CanBeGrabbedByGrabber; в pf-network (до включения ботов с оружием в матчах) — кобура
  у клиентов после TryRelease и поздний вход с оружием в кобуре.

## pf-network: находки net-round1 attempt-5c (тикет 543, 2026-10-13)

- Учёт оружия у клиентов: исправление weapon-system a1c93d407 (SDK 76) подтверждено — shotsSeen = выстрелам сервера,
  0 форков (5500/5506).
- «Автомат у ног, дуло в небо» у наблюдателей: храповик Mirror NetworkTransformUnreliable — после ResetState
  неизменившиеся оси достраиваются из трансформа (итог кадра после UXR); затравка — SDK root recoil на копии
  наблюдателя + KeepGripsInPlace; PosY правой кисти сервер не шлёт (порог 1 см) → дрейф до −2,7 м. Камера бота у
  части наблюдателей на полу (PosY головы ушёл один раз unreliable). Корень — network (5510), копии 5512/5514/5516.
  Блокер приёмки pf-network.
- Поздний вход: кобура пустая — manipulation (снимок якоря). H1C не стартует — KCP timeout (launch-infra 5185).
- A/B attempt-6 (тикет 548, 2026-10-13): храповик Mirror NTU подтверждён (модель клиента — 35 177 снимков, 0
  расхождений); отдача копии наблюдателя НЕ причина (без неё дрейф остаётся, в LJ хуже) — запекается любая клиентская
  правка. Камера: SpawnMessage несёт cameraLocal 0 (драйвер пишет с кадра spawn+1), PosY камеры уходит один раз, до
  спавна клиент отбрасывает NT-сообщения → bake −1650 мм; корень bake −50 мм. Патч Mirror network (последнее
  полученное значение оси + периодический полный снимок) закрывает всё; затем повтор яруса C + проверка avatar-ik
  (камера позднего наблюдателя ±1 см). Отправлено 5532–5538.
- Ярус C поверх Mirror patch 3 network (6ac69feda, attempt-7, тикет 551): храповик устранён — запекание wristR 0–2 на
  case, камера 0; дрейф запястий у наблюдателя медиана 3–7 мм; ствол↔луч 1,2–1,8° (остаток — отдача копии
  наблюдателя, «как у людей»); камера позднего наблюдателя 97/97 в ±1 см (первая ~1 с после спавна — до keyframe);
  трафик бота +~10% (~0,9 КБ/с у клиента); H1C впервые Ready; учёт чистый. Отправлено network/avatar-ik 5607/5609.
  Осталось для приёмки pf-network: вливание патча network, кобура у опоздавшего (manipulation), демо пользователю.
- Ярус C attempt-9 (тикет 562, вход 0e8a89940 = origin/dev 8306c6a86 + наложение Mirror f876148f9, S2C): патч 75
  manipulation подтверждён — PPK в кобуре у обоих клиентов после TryRelease и у опоздавшего (LH), AK105 в руке, дублей
  хвата 0, оружие после смерти лежит на полу; ошибок консоли/Relay 0, все флаги PASS. Метрика оседания исправлена
  (камера и запястья — в системе NT против серверной серии, ближайшая поза в ±0,2 с, кадры после фриза клиента
  исключены): хвост последней секунды — запястья ≤ 2 см, камера 0. Находка (у опоздавшего, LogWarning): LoadStateChanges
  не десериализует уничтоженные UxrGrabbableObject прошлых case — отправлено manipulation/network 5704/5706.
  Осталось: вливание патча network, демо пользователю.
