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

- [x] Код, Editor-миграция в worker (билет 1054), правка станций и сцен; ревью субагентом, находки учтены.
- [x] Приёмка пользователем по heavy (2026-10-22, без ручной проверки в шлеме).
- [x] Тесты переписаны (`DogTagSetupTests`, `ArsenalVisibilityGeometryAudit`, `GameModeRulesTests`,
  `NetworkSpawnableRegistrationTests`), known-red: GrabPose `DogTagPanel` → `DogTag`.
- [x] Проверки на 1909c211: EditMode 2933 (19 известных красных, новых 0); heavy windows код 0;
  heavy editor 8/8 на том же игровом коде. Ревью зон gameplay, level-design, network, launch-infra, bots — approve.
- [x] Влито в origin/dev `1909c211` (этап work), публикация базы — билет 1066.
- [x] Записи журналов зон gameplay, level-design, network (этап journal).

Остаток вне задачи: хват жетона для кисти Cyborg — одним файлом `DogTag.prefab` (задача хватов);
известный красный ArsenalMapGeometry TestMap1 — причина не уточнена (сообщение отказа не сохранено в отчёте).

## Этапы

Этапы и области записи — в [plan.json](plan.json).
