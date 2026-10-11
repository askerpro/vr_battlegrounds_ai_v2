# Карта экспертов

Дата: 2026-10-11, ред. 2. Согласована пользователем. Источник границ для `experts/<id>/AGENT.md`: раздел
«Не владеешь» каждого эксперта ссылается на соседей отсюда. Изменение зоны — правка этой карты и
AGENT.md обоих затронутых экспертов.

Эксперт — постоянная зона ответственности; задача хаба — единица работы внутри зоны. Каждая
продуктовая задача хаба принадлежит одному эксперту. Наличие записи здесь не даёт допуск и не
меняет owner задачи в хабе. Модель «механизм ↔ содержимое»: владелец механизма решает и пишет сам,
содержимое принадлежит эксперту фичи, механизм проводит обязательное ревью своей части чужих этапов.

## Зоны

| Эксперт | Владеет | Задачи хаба | SDK |
|---|---|---|---|
| `launch-infra` | запуск редактора и плееров во всех режимах (Play, роли, MPPM/MMP, native, собранный плеер, headless/batch), порты, DebugBootstrapGate, режим отладки (DebugAdminPolicy, DebugPerfReadout как инструмент), E2E-каркас (runner, контекст, вердикт, агрегация логов, механизм известных шумов), механизм программного XR-ввода для тестов, сборки (GameBuilder, BuildSceneResolver, E2EPlayerBuilder, Tools/release, Tools/e2e) | vr-test-stand, map-runtime-bootstrap | пакет Multiplayer Play Mode |
| `gameplay` | рантайм игры: жизненный цикл карты (MapLoader, ServerStartupRoute, каталог карт, контракт `map-startup-route`), режимы, раунды, серии, матч, команды, спавн/смерть игроков (AvatarManager, AvatarTeardown, точки спавна), переходы камеры при спавне/смене карты (UxrCameraFade), смысл команд игрока в PlayerSession, экономика и арсенал, лобби как фаза сессии; содержание E2E-сценариев игрового цикла | arsenal-generator | — |
| `level-design` | содержимое карт и лобби: иерархия сцен, геометрия, освещение и запекание, окклюзия в сценах, слои и коллайдеры окружения (основа навигации), расстановка точек спавна/станций/укрытий, корпуса и декор станций арсенала, декор и эмбиент, инструменты валидации карт и окклюзии, LobbyRangeLayoutBuilder | lobby-decoration | ParticlePack (окружение) |
| `bots` | боты: ИИ и поведение (Blaze AI), тела и клиповые модели ботов (BotBody), выбор тела до спавна, экипировка и стрельба ботов, правило источников навигации (BotNavMeshSources), сеть ботов (по правилам network), содержимое бот-стендов (BotCombatStand) | bots-fix | Blaze AI |
| `network` | механизм сетевого взаимодействия и синхронизации: сетевой движок UXR (Networking, StateSync/StateSave), UniqueId и реестр компонентов (генерация, UxrUniqueIdPersister, NetworkUxrIdentity, выравнивание по netId), интеграция с Mirror, обвязка проекта (GameNetworkManager/Discovery, relay, StateEventAuthority, фильтры событий), правила прямых Command/Rpc/SyncVar/NetworkMessage, жизненный цикл сессии (подключение/PlayerSession как сессия, reconnect), модель авторитета, Mirror и его патчи; продуктовые сетевые инварианты (поздний вход, reconnect, авторитет каждого действия, события при смене карты) и обязательное ревью сетевой части этапов | — | Mirror, UltimateXR Networking/StateSync/StateSave/UniqueId |
| `ui` | меню планшета (MenuKit, MenuScreen, экраны), дизайн-система, шрифты, HUD, UI-представление арсенала и настроек | — | SlimUI, TextMesh Pro, UltimateXR UI |
| `performance` | производительность Quest: бюджеты кадра/GC/draw calls, методы замера, стресс-тест, настройки качества/URP; бюджеты и проверка окклюзии (настройку в сценах делает level-design); ревью перф-последствий чужих этапов | — | — |
| `manipulation` | взаимодействие с grabbables: правила хвата, две руки, якоря, карманы, размещение, отпускание, сеть хвата (по правилам network), лежащие предметы (LooseItems); эффекты, вибрация и отклик при взаимодействиях; знание SDK UXR в этой части | haptics-system | UltimateXR Manipulation, Haptics |
| `weapon-system` | огнестрел: WeaponSystem, машина состояний, патроны, отклик, захват оружия, авторинг стволов, оружейные клипы KINEMATION | weapon-system | Hands_Weapons_Animations_Pack, KINEMATION, UltimateXR Mechanics (оружейная часть) |
| `avatar-grip` | кисти и хваты: позы пальцев, точки захвата оружия/планшета/магазинов, MEF, риг и скиннинг кистей, Hands Integration; сохраняет принятую калибровку MEF | hand-rig-quality | UltimateXR Avatar (кисти/позы пальцев) |
| `avatar-ik` | аватар тела: настройка аватара, порядок кадра UxrManager (tracking → manipulation → animation → IK), трекинг и ввод контроллеров (Devices), IK тела/рук до запястья/ног, реакция на попадания (HitReaction), калибровка (включая `_eyesBaseHeight`), локомоция (ходьба, телепорт) и её анимация, рендеринг аватара | legs-ik, avatar-renderer-regression | RootMotion, FImpossible Creations, BonelabAvatars, Military Soldier Mega Bundle, UltimateXR Core (UxrManager), Devices, Locomotion, Avatar (тело/IK), Animation |

Инфраструктура агентов (agent-coordination-protocol, expert-workspace-infra) — не продуктовые
эксперты; broker/proxy/hooks принадлежат agent-infra.

## Границы между соседями

- launch-infra ↔ gameplay: каркас E2E и запуск — launch-infra; сценарий и ожидаемый результат — gameplay. Когда менять карту решает gameplay, механизм запуска сети — network.
- map-runtime-bootstrap (решение пользователя 2026-10-11): оставшуюся работу (series-smoke-e2e — живой E2E, раннер, известные шумы) доделывает launch-infra; рантайм карты, серии и `map-startup-route` остаются в зоне gameplay — содержание сценария и ожидаемый результат launch-infra согласует с gameplay.
- launch-infra ↔ network: launch-infra владеет хуками запуска (DebugBootstrapGate, пара портов); GameNetworkManager/Discovery — network.
- launch-infra ↔ avatar-ik: механизм программного XR-ввода для тестов — launch-infra; правила ввода и трекинга — avatar-ik (ревью адаптера).
- network ↔ все: что реплицировать и когда решает эксперт фичи; как (авторитет, relay, сообщения, порядок, UniqueId) — network. Новый сетевой путь мимо принятых примитивов — только через network. Сетевая часть каждого этапа проходит ревью network.
- network ↔ gameplay: PlayerSession как сессия/подключение — network; смысл команд игрока (команда, готовность, админ) — gameplay.
- avatar-grip ↔ avatar-ik: общая кость кисти. avatar-ik владеет позой до запястья (IK руки) и порядком кадра, avatar-grip — пальцами и позой кисти относительно предмета. Порядок писателей кисти закрепляется контрактом avatar-ik + avatar-grip; второй писатель не добавляется. Писатели вне зон (BotBody — bots, HitReaction — avatar-ik) входят в контракт.
- avatar-grip ↔ avatar-ik: калибровку (`_eyesBaseHeight`) меняет только avatar-ik; avatar-grip сохраняет принятое значение и проверяет хваты после изменения.
- manipulation ↔ avatar-grip ↔ weapon-system: механика хвата — manipulation; поза хвата — avatar-grip; механика оружия — weapon-system.
- bots ↔ gameplay: правила матча/спавна/команд, в том числе учёт ботов в minPlayers — gameplay; участие ботов в них (решения, поведение, экипировка) — bots. bots ↔ weapon-system/avatar-ik: оружие и тело — их механики, использование ботом — bots. Запуск бот-стенда — launch-infra.
- level-design ↔ gameplay: сцена карты/лобби, её иерархия и расстановка — level-design; загрузка, каталог карт, лобби как фаза сессии, правила спавна и матча — gameplay.
- level-design ↔ bots: навигация строится в памяти из коллайдеров окружения; слои и коллайдеры — level-design, правило источников (BotNavMeshSources) — bots.
- level-design ↔ performance: бюджеты сцены задаёт performance, укладывает level-design.
- ui ↔ gameplay: правила экономики/арсенала — gameplay, их представление в меню — ui.
- performance ↔ все: performance задаёт бюджеты и проверяет; оптимизацию в чужой зоне делает владелец зоны по ТЗ/ревью performance.
- Общие документы: `Docs/gameplay.md` — владение по разделам (каждый эксперт — свои разделы, gameplay — структура документа); `Docs/troubleshooting.md` — записи по зоне владельца.
- Расположение файлов не определяет владельца: инструменты level-design в `Editor/VR_Battlegrounds/Gameplay/` и `Weapons/Calibration/LobbyRangeLayoutBuilder` принадлежат level-design; этапы перечисляют чужие файлы явно и согласуют их с владельцем.
- Инструменты `Editor/VR_Battlegrounds/Gameplay/`: редакторы режимов/судьи/игроков/сессии и GameTagsTool — gameplay; HitEffectBuilder — weapon-system; LaserGridScreensPreview — ui; валидаторы карт и OcclusionBakeTool — level-design. `ServerAuthoredAvatar` — совместно avatar-ik и bots до отдельного решения.
- SDK: патчи — общее правило AGENTS (`patch-reserve`); обновление версии SDK — межэкспертная задача под руководством владельца модуля из таблицы.
