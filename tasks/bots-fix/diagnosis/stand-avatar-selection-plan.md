# Выбор canonical NPC-модели в существующем стенде

2026-10-10. Read-only архитектурный план, реализации нет. B400: PlayLaunch.Plan PASS; Team1 имеет только MEF Black/index0, Team2 — MEF normal/index0, canonical Cyborg отсутствует. Play не запускался; release400/R7aa218b1ac6fc3ce3f25200a52e194d9bb08860d без changed paths. Эти runtime сведения получены от principal; самостоятельно Unity не выполнялся. Текущий совместный input68c уже compiled/metadata checked по principal.

## Причина и предел исправления

`BotStandSession.Begin:50-60` сначала вызывает AddBot с обычным team skin, затем пытается ChangeAvatar по index. `BotDirector.AddBot:117-126` создаёт сессию, выбирает команду и сразу создаёт тело. Поэтому даже последующее изменение index не предотвращает unsupported первый spawn. Отдельная контрольная цель также выбирает обычный skin противоположной команды (`BotStandSession:79-82`); исправить только subject недостаточно.

`PlayersManager.CreateBotSession:223-224` сначала NetworkServer.Spawn, затем RegisterBot/SessionConnected. Возможный mode callback может создать тело уже на регистрации: передать выбор после CreateBotSession недостаточно. Выбор должен быть установлен **до NetworkServer.Spawn и RegisterBot**, а не только до явного SpawnBody. Команда и canonical source — разные входы: команда определяет роль, enemy policy, спавн-зону и uniform; source определяет поддерживаемую модель. Не расширять TeamRegistry и не менять shared TeamData/avatar lists ради fixture. Heavy исключён; неизвестный source должен дать InvalidFixture до создания сессии.

## Рекомендуемый минимальный дизайн

1. `BotStandRunOptions` получает сериализуемый `AvatarSourceGuid` вместо зависимости нового пакета от AvatarIndex. GUID разрешается в Editor Prepare через AssetDatabase и точно сверяется с canonical `BotCombatAssets.clipBodies[].source`. Runtime получает стабильный GUID как строку, разрешает по заранее подготовленному catalog mapping/entry; не использовать AssetDatabase в runtime сборке. Практическая ещё более узкая форма: сериализуемое **имя source prefab** только после проверки уникальности в catalog; предпочтителен GUID, если в BotCombatAssets добавляется authored `sourceGuid` в existing ClipBodyBinding.
2. `BotCombatAssets` — catalog, а не выбор игрока: добавить `sourceGuid` к существующему ClipBodyBinding; builder заполняет его из source AssetPathToGUID. Read-only `TryGetCanonicalSource(guid,out source)` требует ровно одну запись, ненулевые source/variant, source!=variant, BotClipBody/NetworkIdentity на variant. Ошибка/повтор/unknown/Heavy запрещают fixture до CreateBotSession. Catalog source identity сохранена. Дубликат lookup не создаёт отдельного registry или cache.
3. Выбор хранится в **server-only immutable поле PlayerSession**, `BotAvatarSource`, с read-only getter и internal before-spawn setter рядом с `SetBotControlBeforeSpawn`. Нет static dictionary, Scene object writer, SyncVar копии или поддерживаемого вручную AvatarIndex. Выбор фиксируется на всю жизнь данной сессии. Setter допустим только при netId0, botControlled true, world/server authority; after-spawn изменение запрещено. Source intent не пишет Transform/Animator/SDK Owner/claim/ammo и не имеет новой automation lifecycle.
4. `PlayersManager.CreateBotSession` получает optional canonical source. До Instantiate/session creation проверяет canonical source через read-only AvatarManager/catalog validation, затем после SetBotControlBeforeSpawn и **до NetworkServer.Spawn/RegisterBot** задаёт immutable source. Unknown/unsupported/non-AI source запрещается до регистрации, без fallback. AvatarManager остаётся единственным owner body factory; PlayersManager только фиксирует input session до её публикации. Отдельный post-CreateBotSession configure API не добавлять.
5. В AvatarManager добавить одну общую read-only функцию **выбора source**, используемую SpawnAvatar, ChangeAvatar/PrefabFor и ServerReconcileBody: штатный eliminated ghost имеет приоритет; для живой AI-сессии валидный context даёт canonical source; иначе существующая team strategy. Source выбор происходит **до** ExecutionPrefabFor, Instantiate, SourcePrefab assignment и Spawn. `ExecutionPrefabFor` затем выбирает generated variant как сейчас; `PlayerController.SourcePrefab` всегда исходный canonical prefab, никогда variant. Ни один caller не подменяет prefab после Awake.
6. `BotDirector.AddBot` получает optional `GameObject canonicalSource=null` и передаёт его в CreateBotSession. Существующий AddBot()/EnsureCount сохраняют поведение. Non-null validated до создания session; никаких post-registration selection changes. ChooseTeam остаётся владельцем team policy, intent не меняет team/AvatarIndex. Если SessionConnected уже создал body, не делать второй spawn: дальнейший existing normal spawn должен быть идемпотентен по ActiveAvatar либо callback contract исключает ранний body и это подтверждается inspection. Не выставлять team вручную subject ради skin.
7. `BotStandSession.Begin` валидирует subject source и контрольный source до создания любой сессии. Subject создаётся `director.AddBot(subjectSource)`; прежний постспавновый ChangeAvatar по AvatarIndex для explicit source пути не выполняется. T02 второй бот получает тот же explicit source через AddBot. Контрольная цель сохраняет нынешнюю отдельную сессию/отсутствие director combat управления: CreateBotSession(...botControlled:true, canonicalSource:controlSource) → штатный SessionTeamAssigner opponent → SpawnAvatar. Source уже установлен до регистрации. Control source фиксировать поддерживаемым MEF Black независимо от team; это объявленная fixture модель. Не превращать target в director combat AI и не добавлять fake grab/equip.
8. После spawn stand проверяет `ActiveAvatar.SourcePrefab == requestedCanonicalSource`, actual generated variant identity, текущую team и Session link. Existing Gunner.Arm(weapon), normal Blaze/RequestAutomationPreparation/TryToShootRound не меняются. Все четыре пары проходят через один normal factory путь: MEF/PPK, MEF/AK105, Cyborg/PPK, Cyborg/AK105.

## Lifecycle и ownership

Intent принадлежит сессии, не телу: death → штатный ghost, respawn/reconcile → тот же выбранный canonical source; actor/body caches отсутствуют. Stand.Dispose уже снимает observer до удаления own sessions, поэтому удаление сессии уничтожает intent без static leftovers. Immutable выбор сохраняется через map boundary для той же session и каждый новый spawn вновь проверяет доступный catalog; исчезнувший source — явный отказ, не unsupported fallback. Никаких отдельного clear writer, timers/OnSceneChanged daemon или competing registry. Stand сессии обязаны удаляться штатно до stop; новая сессия получает новый выбор. Human sessions/API clients не получают override.

## Владение файлами и ожидаемые изменения

| Файл | Ответственность |
|---|---|
| Assets/Scripts/Player/PlayerSession.cs | Immutable server-only BotAvatarSource before-spawn field; единственная истина выбора |
| Assets/Scripts/Managers/PlayersManager.cs | Optional validated source в CreateBotSession до Spawn/RegisterBot |
| Assets/Scripts/Bots/BotCombatAssets.cs | GUID lookup по единственному existing composition catalog |
| Assets/Editor/VR_Battlegrounds/Bots/BotClipComposition.cs | authored sourceGuid в own generated bindings; A rebuild |
| Assets/Scripts/Player/Avatars/AvatarManager.cs | Read-only validate intent; единый source resolver на всех body paths; ghost priority |
| Assets/Scripts/Bots/BotDirector.cs | optional source до первого body spawn; normal team assignment и own cleanup |
| Assets/Scripts/Debug/BotCombatStand/BotStandRunContracts.cs | explicit source option; backward compatibility index отдельно |
| Assets/Scripts/Debug/BotCombatStand/BotStandSession.cs | normal subject/target factory input и actual SourcePrefab check |
| Assets/Editor/VR_Battlegrounds/Bots/BotCombatStandEditor.cs | validation explicit source before PlayLaunch; no shared asset writes |
| Assets/Resources/Bots/BotCombatAssets.asset | только own generator output sourceGuid после A |

TeamRegistry/TeamData, canonical human/source prefab, weapon prefabs, SDK, tests и shared indexes не изменять. PlayerSession/PlayersManager узкий scope добавлен по прямому steering principal для устранения раннего SessionConnected callback. Principal сохраняет task plan/Readme; здесь только bounded proposal.

## Сравнение альтернатив

- Только AvatarIndex или постспавновый ChangeAvatar: Cyborg отсутствует в обеих team lists; unsupported первая target/team2 модель создаётся раньше override. Не решает blocker.
- Временно менять TeamData/TeamRegistry: второй writer shared selection policy, source assets и cleanup risk; не допускается.
- Передавать prefab только в один SpawnAvatar overload: решает первый spawn, но death/reconcile/respawn снова выбирают team strategy. При появлении нескольких callers получаются разные source owners.
- Static stand resolver/delegate в AvatarManager: глобальная mutable подписка, leakage и scope ambiguity; session-local intent проще проверить и уничтожить.
- Session-local immutable before-spawn intent + единый AvatarManager source resolver: smallest практический вариант, который также исправляет control target до первого spawn/registration и сохраняет normal factory/reconcile без TeamRegistry mutations. Отдельный компонент или post-registration setter добавляют поверхность, не устраняя ранний callback.

## Review и будущая проверка

До реализации independent review подтверждает общий source resolver/ghost precedence, доступ setter только server AI до Spawn/RegisterBot, immutable session cleanup и catalog GUID уникальность. После реализации — scoped compile/existing checks; gameplay тесты до user acceptance не переписывать. A rebuild/readback подтверждает sourceGuid +2 identities +BotClipBody metadata. B проверяет оба канонических source независимо от team defaults, четыре пары, cold ledger/emission/ActorSource/damage, own cleanup. Production default Team2 normal MEF остаётся отдельной открытой catalog-coverage проблемой: не менять production skin policy в этом bounded fixture fix. Непредусмотренный первый spawn в fixture должен отсутствовать, не подавляться в логах.

Фактическое evidence source inspection: BotDirector107-129/302-345; PlayersManager205-225 (создаёт только сессию, SetBotControlBeforeSpawn до NetworkServer.Spawn); PlayerSession41-46 (AI flag); AvatarManager67-115/188-215/243-247/358-374; TeamAvatarStrategy24-39; BotStandSession50-84. Unity/B400 результаты в этом документе не переобъявлены собственным исполнением.
