# Разделение куклы аватара и игровой логики

> Исследование сохранено 2026-10-19 по решению пользователя; задача PARKED.
> Выбор архитектуры, прототип и реализация ждут отдельного возобновления.
> Ниже сохранён статический аудит исходной ревизии, без нового runtime-доказательства.

Исследование: разделение куклы аватара и игровой логики
Дата: 2026-10-19. Статус: статический аудит, реализация и runtime-прототип не выполнялись.

Это передача другому агенту для дальнейшего проектирования. Не включать большой рефакторинг в текущую задачу player-prefab-layout: там отдельно выполняется перевод Ghost в самостоятельный prefab, исправление связанных тестов и подготовка принятой реструктуризации к публикации.

Исследован код F:/CodexWorktrees/player-prefab-layout. При передаче HEAD/origin/dev этого worktree: 48d38b9b2885b771f9cc961b4b640eaa7f74ca9e. Аудированные runtime-файлы Player, Network, Mirror и UltimateXR не менялись между прежней базой 2e1b6a2f и этой ревизией. Рабочее дерево содержит незавершённые изменения текущей задачи. Номера строк ниже — ориентиры, проверить в своём checkout.

1. Запрос пользователя и последние уточнения

Пользователь хочет отделить модель/риг, трекинг, IK, контроллер куклы и позы рук от игровых компонентов, которые сейчас находятся на аватаре. Эти части должны переиспользоваться независимо и собираться в игровое тело.

Название PlayerBody подходит для заменяемого игрового тела. Это название сущности, а не требование создать новый C# класс или обязательный PlayerBody.prefab.

Рассматриваются оба варианта: небольшой готовый PlayerBody.prefab с динамической куклой и полностью программное создание GameObject со всеми нужными компонентами.

Отдельный AvatarSlot исключён: он избыточен.

Предложенный PlayerAvatarBinding, если он вообще понадобится, хранит только ссылку на runtime UxrAvatar/экземпляр куклы и необходимые связи. Ссылка на prefab в готовом binding не нужна. Prefab выбирает сервер и использует сборщик при создании. При сохранении всех компонентов на одном корне отдельный binding-компонент тоже может оказаться лишним — не вводить его ради самого названия.

PlayerSession остаётся источником постоянного состояния и выбора аватара. AvatarManager остаётся единственным владельцем создания и замены игрового тела. Конкретный способ композиции пока не выбран пользователем.

2. Текущие владельцы состояния и жизненный цикл

PlayerSession хранит команду, выбранный скин, счёт, калибровку, выбывание и связь ActiveAvatarNetId. Тело ссылается на сессию через SessionNetId.

PlayerController — NetworkBehaviour с RequireComponent(typeof(UxrActor)); это два отдельных компонента. Здоровье тела принадлежит UxrActor, выбывание игрока — сессии. Не создавать ещё одного владельца этих данных в новой сущности.

Смерть и возрождение обслуживает существующий AvatarManager. Он выбирает нужное тело по состоянию сессии, создаёт новый сетевой экземпляр, связывает его с прежней сессией, отпускает предметы старого тела и уничтожает старое тело. Замена из UXR-события смерти откладывается до безопасной точки.

Ghost выдаётся отдельным телом после смерти. В будущей композиции он должен оставаться ограниченным профилем с планшетом, без боевого loadout.

Источники:
- Assets/Scripts/Player/PlayerSession.cs:226,239,555,581,619
- Assets/Scripts/Player/PlayerController.cs:22-23,43-49,129,141,343,358
- Assets/Scripts/Player/Avatars/TeamAvatarStrategy.cs:26
- Assets/Scripts/Player/Avatars/AvatarManager.cs:469-520
- Docs/session-architecture.md:11,32

3. Какие варианты действительно стоит сравнить

А. Небольшой PlayerBody.prefab + кукла дочерним объектом.
Префаб хранит игровые компоненты и конфигурацию. Сборщик создаёт неактивное тело, добавляет выбранную куклу и устанавливает ссылки. Удобно для Inspector и проверки сериализованных настроек. Однако перенос UxrAvatar в дочерний объект требует адаптации нынешних связей UXR/authority и регистрации — это не просто перенос компонентов.

Б. Полная сборка из пустого GameObject.
Технически Mirror допускает создание объекта своим кодом. Все компоненты добавляются, настраиваются и связываются до первого Awake. Нужно воспроизводимо задать весь состав NetworkBehaviour, сериализованные настройки, SDK UniqueId и зависимости. Готового универсального Initialize для всего нынешнего набора нет; AddComponent недостаточно.

В. Экземпляр AvatarPrefab становится конечным корнем, игровая логика добавляется на него.
Этот вариант лучше сохраняет нынешние предположения о соседстве UxrAvatar, UxrMirrorAvatar, NetworkIdentity и игровых компонентов. Авторинг куклы и игровой набор могут быть независимыми без дополнительного родителя. Он заслуживает отдельного прототипа. Сериализованные SDK-компоненты и их ID желательно сохранить; добавление UxrActor в runtime всё равно требует решения для исходного UniqueId.

Первоначальная рекомендация исследователя — начинать с сериализованной композиции, затем переходить к runtime assembler. После уточнений пользователя стоит отдельно сравнить вариант В с А: вариант В может потребовать меньше изменений текущего glue. Статика ещё не доказывает, какой вариант лучше по итоговой сложности.

4. Mirror: готовый prefab не является техническим требованием

NetworkServer.Spawn(GameObject, uint assetId, owner) позволяет указать ID создаваемой композиции. NetworkClient.RegisterSpawnHandler предоставляет поддерживаемую точку собственной сборки на клиенте. Отдельный transport не нужен.

Один NetworkIdentity находится на корне. Дочерние NetworkBehaviour разрешены, но NetworkIdentity.Awake фиксирует их список и ComponentIndex. Полная структура и порядок компонентов должны совпадать у сервера и каждого клиента до этого вызова.

Неактивное создание необходимо с самого начала. Отключение уже активно созданного объекта не отменяет выполненный Awake. Собирать состав из Awake самого PlayerBody небезопасно: нельзя полагаться на его порядок относительно NetworkIdentity.Awake.

На сервере Spawn активирует готовый root. На клиенте custom SpawnHandler получает SpawnMessage до ApplySpawnPayload; он может вернуть уже собранный неактивный объект. Далее Mirror активирует объект, применяет сетевые данные и вызывает callbacks. Host использует серверный экземпляр и не должен собирать вторую куклу.

Практический вариант адресации — одинаковый каталог assetId -> конфигурация тела/куклы. Это предложение, а не утверждённая новая сущность. Клиент не должен ждать AvatarIndex в OnStartClient: состав сетевых компонентов требуется раньше, а сессия может ещё не появиться. Ручной разбор внутреннего SyncVar payload ради выбора модели не рекомендуется.

Источники:
- Assets/ThirdParty/Mirror/Core/NetworkIdentity.cs:294-312,372-383
- Assets/ThirdParty/Mirror/Core/NetworkServer.cs:1568-1574,1623-1641
- Assets/ThirdParty/Mirror/Core/NetworkClient.cs:960,1108-1163,1211-1226,1358-1372
- Assets/ThirdParty/Mirror/Core/Messages.cs:62-79
- Assets/ThirdParty/Mirror/Core/Tools/Utils.cs:16

5. Жёсткие текущие связи, которые нельзя потерять

PlayerController ищет соседние UxrMirrorAvatar/UxrAvatar. PlayerLoadoutManager требует UxrAvatar на том же объекте; при переносе на отдельный gameplay-root RequireComponent может создать лишний avatar. PlayerGrabManager, калибровка и эффекты тоже используют нынешнее соседство.

UxrMirrorAvatar ожидает UxrAvatar на себе и объединяет SDK ID только в его поддереве. UxrActor на родителе вне этого поддерева выпадет из текущей процедуры регистрации.

StateEventAuthority ищет NetworkIdentity непосредственно на UxrAvatar. AvatarRootEventAdmission требует согласованную связь avatar, identity, netId, UniqueId и registry, включая общий root. Перевод на явные ссылки должен сохранить эти проверки, а не обходить их.

Кукла включает согласованную систему UxrAvatar/controller/tracking/camera/rig/IK/hand poses. Сохраняется один владелец записи костей и прежнее различие local/remote/server режимов.

Источники:
- Assets/Scripts/Player/PlayerController.cs:22,68,204,496
- Assets/Scripts/Player/PlayerLoadoutManager.cs:46,89
- Assets/Scripts/Player/Avatars/ServerAuthoredAvatar.cs:18,28
- Assets/Scripts/Network/StateEventAuthority.cs:183-190
- Assets/Scripts/Network/AvatarRootEventAdmission.cs:79-92,128-140,157-164
- Assets/ThirdParty/UltimateXR/Runtime/Scripts/Networking/Integrations/Net/Mirror/UxrMirrorAvatar.cs:104-118,147,176
- Assets/ThirdParty/UltimateXR/Runtime/Scripts/Avatar/Controllers/UxrStandardAvatarController.cs:28
- experts/avatar-ik/AGENT.md:18

6. Дополнительный риск полной AddComponent-сборки: SDK UniqueId

UxrActor наследует UxrComponent. После AddComponent у него нет сохранённого _uxrUniqueId. Awake вызывает RegisterIfNecessary; SDK выводит исходный ID из пути объекта, включающего сцену, sibling index, имя и индекс компонента. Эти значения на сервере и клиенте не обязаны совпадать.

UxrMirrorAvatar затем комбинирует исходные SDK ID с netId. Одинаковый netId не исправляет разные исходные ID.

Публичный TryPrepareRuntimeUniqueId умеет подготовить точный ID свежего неактивного компонента, но помечает его как runtime-prepared. CombineUniqueId содержит защиту от повторной комбинации такого компонента. Нельзя считать эту API готовой заменой prefab ID для неизменённого UxrMirrorAvatar: конкретный путь рекурсии и назначение исходного/конечного ID нужно проверить прототипом.

ChangeUniqueId также доступен, но регистрирует состояние сразу. Безопасный порядок ранней настройки ещё не доказан.

Ещё пример отсутствующей конфигурации: у runtime-added UxrMirrorAvatar приватный _localDisabledGameObjects не имеет инициализатора, а local initialization вызывает ForEach. Нельзя рассчитывать, что все SDK компоненты корректно работают с C# defaults после AddComponent.

Источники:
- Assets/ThirdParty/UltimateXR/Runtime/Scripts/Mechanics/Weapons/UxrActor.cs:16
- Assets/ThirdParty/UltimateXR/Runtime/Scripts/Core/Components/UxrComponent.cs:72,270-295,389-445,724-737
- Assets/ThirdParty/UltimateXR/Runtime/Scripts/Core/Unique/UxrUniqueIdImplementer_1.cs:119-140,261-279
- Assets/ThirdParty/UltimateXR/Runtime/Scripts/Extensions/Unity/TransformExt.cs:860-885
- Assets/ThirdParty/UltimateXR/Runtime/Scripts/Extensions/Unity/ComponentExt.cs:230-233
- Assets/ThirdParty/UltimateXR/Runtime/Scripts/Networking/Integrations/Net/Mirror/UxrMirrorAvatar.cs:38,104-118

7. Рекомендуемая дальнейшая работа другому агенту

Сначала согласовать границу данных/компонентов: что остаётся сериализованным SDK-ядром, что является игровой политикой и какие настройки задаёт фабрика. Не вводить обязательный Binding, отдельный Slot, второй health owner или второй transport без конкретной необходимости.

Сравнить А/Б/В на одном небольшом прототипе с общим серверным и клиентским сборщиком. Проверить исходные и комбинированные UniqueId UxrAvatar/UxrActor, одинаковый состав NetworkBehaviour, правильную регистрацию и cleanup. Сохранить текущую семантику полной замены тела; смена только куклы у уже работающего сетевого root — отдельная задача.

Обязательная последующая проверка: dedicated server/client, host без двойной сборки, late join, повторная смерть/возрождение/смена тела, authority, синхронизация рук, калибровка, локальная голова, освобождение предметов и Ghost tablet-only.

Качество VR принимает пользователь. До этого статический аудит и технический прототип не называть доказательством готовой архитектуры. Реализацию оформлять отдельно по текущим AGENTS.md; исследование само по себе не выдаёт аренду Unity или владение другой задачей.
