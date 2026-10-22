# Жетон арсенала спавнится в рантайме из префаба

2026-10-22. Задача создана командой `task-new`: один агент ведёт её в собственном worktree в ветке `task/dogtag-runtime-spawn` (схема «задача = агент»).

## Цель

Жетон DogTag не вшит в станции арсенала на сценах, а спавнится сервером в DogTagAnchor станции из своего префаба тем же путём, что оружие на стене (ArsenalWallController -> NetworkServer.Spawn).

Зачем: правка компонентов жетона (точки хвата UXR с `_uxrUniqueId`) требовала правок 8 сцен и 2 станций
(per-instance id). Хват жетона под CyborgHandsBase добавит после переделки задача хватов одним файлом.

## Решение

- `Assets/Prefabs/Arsenal/DogTag/DogTag.prefab` — сетевой предмет: `NetworkIdentity`, `UxrGrabbableObject`
  (без стартового якоря), кинематический `Rigidbody`, `OutOfWorldGuard`, метка `DogTagItem`; вид — из Common-станции.
  Зарегистрирован в `spawnPrefabs` (`--- MANAGERS ---`).
- `DogTagPanel` — крючок `DogTagAnchor` + табло + `_tagPrefab`; вложенного жетона нет.
- Сервер (`ArsenalWallController.ServerUpdateDogTag`): `MapRunAdmission.CreateMapItem` → `BindTag` (крючок,
  `SetNetworkAnchor`) → `NetworkUxrIdentity.SpawnServerObject` → SyncVar `_dogTagNetId`, `_dogTagOnRack`.
  Выдаётся, только когда режим использует жетон; пропавший заменяется через `_lostWeaponReplaceDelay`.
- Клиент: ждёт объект в `NetworkClient.spawned` и активным; на крючок вешает только при `_dogTagOnRack`.
- Скрытие (таймер, разминка) — выключением крючка, не жетона: Mirror спавнит позднему клиенту только `activeSelf`.
- Замена жетона не сбрасывает «один раз за закупку» (`EnableGrab`, не `ResetTag`).
- Сцены: удалены только строки переопределений жетона (по XOR fileID станций); id якорей не тронуты.
- Офлайн (без сервера) жетона нет — как и оружия стены.

## Статус

- [x] Код, Editor-миграция, правка сцен; независимое ревью субагентом, находки учтены.
- [x] Миграция в worker (билет 1054, R 25511530), receive; EditMode-подмножество 282/296 (красные — известные и
  устаревшие проверки жетона); host TestMap1/Elimination: 8 жетонов на крючках, id выровнены.
- [x] Heavy editor/default на 231ebb3c (+ изменения, билет 1058): код 0, 8/8, UxrComponentNotFound/NRE/Exception 0;
  жетон снимают удалённые клиенты (scripted-combat-round, -next-round). Отчёт — reports/heavy-editor-1/verdict.json.
- [ ] Ручная проверка пользователем (Play/шлем), затем переписать устаревшие тесты
  (`DogTagSetupTests`, `ArsenalVisibilityGeometryAudit`, `GameModeRulesTests` про жетон, `NetworkSpawnableRegistrationTests`).
- [ ] Коммит, known-red (`DogTagPanel` → `DogTag` в GrabPose), heavy, ревью зон, вливание.
  Одноразовая миграция `DogTagRuntimeSpawnMigration.cs` выполнена и удалена (в ветку не входит).

## Этапы

Этапы и области записи — в [plan.json](plan.json).
