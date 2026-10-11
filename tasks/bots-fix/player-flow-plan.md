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
