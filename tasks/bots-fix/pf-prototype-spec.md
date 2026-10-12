# ТЗ этапа pf-prototype (этап 0): бот = обычный аватар игрока, вариант P «кукловод»

2026-10-12. Хаб: bots-fix rev44, этап `pf-prototype` RUNNING. Ветка `bots-fix` от origin/dev `3240427c9`.
Проект и условия зон — [player-flow-plan.md](player-flow-plan.md) (читать целиком, особенно «Итоги ревью зон»).

## Цель

На стенде BotCombatStand один бот MEF с AK105 — обычный префаб игрока (тот же, что у человека), аватар без
владельца в `UpdateExternally`. Серверный адаптер переводит намерения ИИ (Blaze-proxy: движение, курс, точка
прицела, стрелять) в позы корня, камеры и кистей. Тело — штатный IK UXR, хват — штатный SDK обеими руками,
выстрел — `TryToShootRound` от автора. Режим включается переключателем в конфиге стенда; матч не меняется.

## Компоненты (только сервер/хост, AddComponent как в BotDirector)

- `Assets/Scripts/Bots/Avatar/BotInputFrame.cs` — намерения кадра (голова, две кисти, спуск, хват, сброс).
- `BotAvatarDriver.cs` — единственный писатель корня (или получает его от BotBody — один писатель!), камеры
  (локально к пивоту CameraController) и `Avatar.Left/RightHandBone` (World), только до стадии Manipulation.
- `BotHands.cs` — хват/отпускание через SDK.
- `BotWeaponAim.cs` — желаемая поза оружия W* → позы кистей.

## Обязательные условия (из ревью зон; нарушение = стоп и вопрос)

avatar-ik: один адаптер, только у аватара без владельца и только на сервере/хосте; на клиентах ни одного
бот-компонента. Не писать кости, пивот, Dummy Forward, масштаб, поля BodyIK; не использовать
UxrLocomotion/UxrControllerInput; не писать после Manipulation/IK и не читать кость кисти обратно. Камера движется
вместе с корнем без отдельного сглаживания. Бот появляется стоя; телепорт корня — одним кадром. Ветку IsBot в
RemoteAvatarIKThrottle не добавлять.

manipulation: хват только на сервере под `StateEventAuthority.IsWorldAuthority`; перед `UxrGrabManager.GrabObject`
— `item.CanBeGrabbedByGrabber(grabber, point)` или `TryGrab(avatar, side)` при кисти на снапе; затем
`GrabObject(..., propagateEvents:true)`; порядок primary → support; `ReleaseObject(..., true)` только от автора;
только не удерживаемый предмет (`IsBeingGrabbed`). Геометрию снапа не кешировать — читать у SDK каждый раз.
После `!IsAlive` просто перестать хватать (сброс при смерти — не наш, не добавлять).

weapon-system: `TryToShootRound(0)` только где `StateEventAuthority.IsAuthorOfItem(weapon)`, вне
`IsInsideStateSync`, один вызов на выстрел, только после `true` от `RequestAutomationPreparation`. Проверить, что
`use.AuthoredHere` (IsAuthoredHere аватара бота на сервере) истинно, хват основной рукоятью реальный SDK с
`GrabLifetimeEpoch>0`. После перехвата — запросить automation заново. Запрещены прямые команды учёту, новые
Cmd/Rpc, запись `LocalAvatarInput`.

network: спавн только через `ServerAuthoredAvatar.Prepare` до `NetworkServer.Spawn`; новых SyncVar/Cmd/Rpc нет;
GrabObject/Release/TryToShootRound/automation — из Update/LateUpdate верхнего уровня, не из обработчика UXR-события.

gameplay: тело — штатный выбор (TeamAvatarStrategy/AvatarIndex), никакого `BotAvatarSource`/своего каталога в
спавне; признак бота — `BotDirector.IsBot` на сервере. Калибровка: в `PlayersManager.CreateBotSession` —
`session.ServerAcceptCalibration(<калибровка>, CalibrationOrigin.Connect)` до `NetworkServer.Spawn`, значение —
параметр от ботов (в границах PlayerCalibrationRules). Это единственная допустимая правка вне зоны bots.

performance: `ProfilerMarker` на BotDirector.Think, BotRuntimeController (если есть) Update/LateUpdate,
BotAvatarDriver, BotHands, BotWeaponAim, гейт Sees. Нулевые аллокации в установившемся режиме (без LINQ, лямбд,
итераторов, GetComponent*/поиска по сцене в Update/LateUpdate). Каденция raycast-ов Sees — явный параметр.

Решения пользователя: неточность — через точку прицеливания ИИ; компенсация отдачи у бота неполная
(человекоподобная), отдача добавляет разброс; перезарядка — automation.

## Порядок кадра (сервер)

Update: Blaze двигает proxy → сбор `BotInputFrame`. До стадии Manipulation `UxrManager.PostUpdate` (LateUpdate,
порядок 0) — драйвер пишет корень/камеру/кисти (MonoBehaviour Update после Blaze или подписка
`UxrManager.StageUpdated` до Manipulation — выбрать и обосновать). Потом штатно Manipulation → Animation →
PostProcess (IK). После всех стадий: гейт `Sees` по фактическому лучу дула, выстрел, замер ошибки дула для
поправки следующего кадра.

## Что есть и откуда брать

- origin/dev: текущий бот-гибрид (`Assets/Scripts/Bots/`: BotBody, BotCombatDriver, BotGunner, BotDirector …) —
  разобраться, кто сейчас пишет руки/камеру/корень бота, и при включённом режиме P не оставить второго писателя.
- Локальная ветка `bots-fix-clip-archive` (клиповый путь, только для чтения, `git show bots-fix-clip-archive:<path>`):
  можно переносить идеи/код `BotGunner.TryGetMuzzleForwardHit`, `BotAimMath` (метрика ошибки дула),
  `BotRuntimeController` (порядок кадра), улучшения стенда (BotStandRecorder, наблюдатель событий оружия).
  Не переносить: BotClipBody, BotAim, клиповые ассеты, `BotAvatarSource`, `_botControlled`, ветки в
  AvatarManager/PlayersManager/AvatarTeardown/GameNetworkManager, WeaponEquipmentBinding-реализации.
- Приём StressPuppet: `Assets/Scripts/Debug/StressTest/StressPuppet.cs`.

## Writes (строго)

`Assets/Scripts/Bots/**`, `Assets/Scripts/Debug/BotCombatStand/**`,
`Assets/Editor/VR_Battlegrounds/Bots/{BotCombatSetup,BotCombatStandBuilder,BotStandHandMotionRecorder,
BotStandHarnessControls,BotStandIdleMotionProbe,BotStandRecorder,BotStandVisualCapture}.cs`,
`Assets/Scripts/Managers/PlayersManager.cs` (только калибровка), `tasks/bots-fix/**`. Новые .cs — с .meta
(уникальный guid). LF, комментарии по-русски, только `GameLog.<Channel>.<Level>`.

## Критерии этапа (замер на стенде делает главный агент через worker)

Выстрелы/попадания не хуже раунда IV (MEF AK105 101/101); медиана ошибки дула в Shoot ≤1,5°; зазор кисть–снап
<1 см для обеих рук; оружие–граббер в одном кадре <1 мм; скольжение стоп не хуже базы; время сервера на бота
(маркеры) и байт/с на бота; 0 ошибок консоли. Стенд должен записывать эти метрики в отчёт.
