# Map bootstrap: пакет runtime-интеграции после authoring foundation

| Цель | Мы здесь | Осталось выполнить | Технический документ |
|---|---|---|---|
| Подключить проверенный паспорт/refs карты к runtime composition | Пакет разрешён пользователем 2026-10-06 и реализован в ветке `claude/map-runtime-bootstrap`: MapBootstrap, MapRunAdmission, хуки владельцев, инструмент миграции; Android PASS, dry run 5/6 (шестая исправлена) | Применить миграцию сцен, Play Mode, EditMode-регресс, приёмка в шлеме; Relay-барьер и генерируемые станции — задачи 5/7 | Этот документ; [план](map-runtime-bootstrap-plan.md), [handoff](map-runtime-bootstrap-handoff.md) |

## Почему пакет выделен

Автоматическая проверка разрешений отклонила общий патч с MapBootstrap/admission, MapReferee, арсеналом и AvatarManager. Причина: «широкое production-impacting изменение с риском блокировки gameplay и не является узким локальным срезом». Патч не был применён: новых MapBootstrap/MapRunAdmission/MapRunServiceBinding в Assets нет, actor hooks им не изменены. Этот документ предъявил конкретные границы; 2026-10-06 пользователь явно разрешил пакет («продолжай до полного завершения интеграции и миграции»). Фактическое состояние реализации — в [handoff](map-runtime-bootstrap-handoff.md); адресная миграция сцен выполнена отдельным инструментом `MapBootstrapMigration`, а не этим пакетом.

## Предлагаемое поведение

Новая opt-in карта с MapRoot проходит проверку собственных bindings и canonical catalog, получает run scope и два inactive registered service prefab. До Spawn они получают один RunKey и размещаются в нужной scene; typed refs coordinator задаются до активации. Затем authority публикует только CompositionReady. На всех шагах mode Begin, stock и avatars остаются запрещены. Ready и remote LocalPlayable требуют последующих mode/Relay proofs.

Карты, адресная миграция которых ещё не проведена, явно сохраняют legacy путь. Runtime не добавляет MapRoot и не удаляет сценовые services ради автолечения. Ни одна сцена, MapData asset или общая PhysicalArenaLayout этим пакетом не мигрируется. Native fixture доказывает отдельную ветку с MapRoot, а regression — сохранение unmigrated пути.

## Точный write-set

| Путь | Разрешаемая ответственность |
|---|---|
| `Assets/Scripts/Maps/Runtime/MapBootstrap.cs` | Новый keyed orchestrator preparation/spawn/teardown, закрытый admission; авторский Environment не создаёт |
| `Assets/Scripts/Maps/Runtime/MapRunServiceBinding.cs`, `MapRunSnapshot.cs` | Только wire принадлежность dynamic services и serializer RunKey; не второй config/state writer |
| `Assets/Scripts/Maps/Runtime/MapRunAdmission.cs` | Производный допуск current key/revision/scope; одна queued avatar request на session с отменой старого scope; никаких собственных Ready-флагов |
| `Assets/Scripts/Network/GameNetworkManager.cs` | Центральная catalog ref и событие готового SessionContext, включая initial no-onlineScene path; не менять connection/PlayerSession handshake |
| `Assets/Scripts/Managers/MapReferee.cs` | Managed экземпляр не включает warmup и weapon access до допуска; режимы/счёт и новый mode transition относятся task4 |
| `Assets/Scripts/Arsenal/ArsenalWallController.cs`, `ArsenalStationPresetBinding.cs` | Managed station не refill-ит и не разрешает trade до допуска; explicit canonical Prepare вместо параллельного scene lookup |
| `Assets/Scripts/Arsenal/ArsenalBoundaryWall.cs`, `ArsenalDeploymentAnimator.cs` | Закрыть gameplay motion writer до допуска; не менять geometry/poses и удерживаемые предметы |
| `Assets/Scripts/Arsenal/ArsenalGrabRule.cs` и существующий magazine predicate при необходимости | Чистый local grab predicate за LocalPlayable, без дополнительного SDK IsGrabbable writer |
| `Assets/Scripts/Player/Avatars/AvatarManager.cs` | Общая точка создания откладывает request до server Ready; состояния PlayerSession и existing avatar teardown сохраняются |
| Новые service prefabs, новый центральный catalog и адресная spawnPrefabs registration | Только собственные новые assets и необходимые registered refs; перед сохранением readback/shared-file hash, без global SaveAssets |
| Собственный temporary Editor Maps runner, `Tools/Probes/MapRuntimeBootstrap/`, artifacts | Изолированные probes, cleanup и ограниченный вывод |

Чужие generator/SDK/source weapon/magazine файлы не входят. Shared Arsenal write-set должен быть согласован с producer; его manifest/UID algorithm bootstrap не присваивает. Avatar prefab/loadout и постоянные expectations не меняются.

## Приёмка пакета до движения дальше

- Missing/corrupt binding или service заканчивается named failure до любого stock/avatar/mode Begin.
- OnStartServer authored station до callback не выдаёт stock; request queues не дают старому scope спавнить аватар после unload.
- CompositionReady не открывает gameplay policies, host не дублирует services; dedicated composition не требует local avatar/client hook.
- Session/root/scene/service callbacks во всех порядках и initial start без SceneChanged приводят к одной composition либо точному отказу. Чужой/stale key не связывает service с новой картой.
- Клиентские взаимодействия остаются закрыты; недоказанный SDK Relay не заменяется таймером, fake ACK или стеновым Ready.
- Existing unmigrated карты сохраняют прежний запуск. Нет дополнительного writers IsUseBlocked/IsGrabbable/current snapshot; запрет не деактивирует удерживаемый предмет.
- Own lease подтверждена до каждого Unity-вызова; complete import/test/cleanup до release. Android, временные native probes и read-only review обязательны. Permanent tests/commit — после human acceptance.

## Доказанная основа и пределы

[Lifecycle baseline (локально)](report/map-runtime-bootstrap/lifecycle-baseline.json) воспроизвёл четыре legacy отказа. [Инвентарь (локально)](report/map-runtime-bootstrap/native-inventory-summary.json) охватил шесть карт: world transforms и serialized IDs без коллизий, runtime registrations не проверены. [Root14/0 (локально)](report/map-runtime-bootstrap/root-preflight-latest.json), [adversarial6/0 (локально)](report/map-runtime-bootstrap/catalog-adversarial-latest.json) и [Android (локально)](report/map-runtime-bootstrap/android-authoring-final.json) подтверждают только authoring integrity. Запуск карты, сетевой transport, живые policy effects и Quest этим foundation не доказаны.
