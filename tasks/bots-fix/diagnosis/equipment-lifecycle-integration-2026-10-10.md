# Equipment lifecycle integration

2026-10-10. Назначенный bounded пакет, armed-development epoch5/revision15 после rebase.
Исходники: AvatarTeardown, AvatarManager, EquipmentStrip, PlayerLoadoutManager, LooseItems,
новый Weapons/Equipment/BotEquipmentLifecycle.cs и его .meta.

- BotEquipmentLifecycle каждый раз читает spawned identities и сравнивает canonical
  binding.IsActive/Owner с UxrAvatar. Mutation читает server dictionary, read-only loadout
  допускает client dictionary. Нет кеша владельца, второго Held bool или lifecycle MonoBehaviour.
- ReleaseOwned допускает только активный сервер/world authority и вызывает TryDrop.
  На смерти velocity — существующий DeathDropEjection.VelocityFor от мёртвого тела;
  на disconnect/обычном освобождении — zero; на confiscate drop→NetworkServer.Destroy.
  Сам helper не пишет transform/Rigidbody или snapshot. Отказ TryDrop логируется.
- ServerDropEquipment/ServerStrip освобождают binding до SDK-manager guard. Без SDK
  manager магазины всё равно очищаются. AvatarTeardown имеет fallback при отсутствии loadout.
- CollectEquippedWeapons добавляет claim weapons тем же свежим lookup, сохраняя прежний
  per-WeaponData dedup для оружия из SDK hands/anchors.
- LooseItems.IsLoose и Remove оба отвергают любой активный ancestor claim, включая
  disabled binding/неразрешённого owner и дочерние предметы оружия.
- ReplaceBody проверяет execution prefab и instantiated PlayerController/NetworkIdentity/
  UxrAvatar до старого cleanup. Старый аватар ещё canonical при drop/confiscate. Cleanup
  теперь до new Spawn/ActiveAvatar/CarryLifeState/Admit; поздний дубликат удалён.
  Если claim после cleanup остался, candidate удаляется, старое тело/session не заменяются.
  CarryLifeState/Admit сохранены, здоровье/helper это не пишет.

## Проверка/ограничения

Source diff --check PASS (только предупреждения о существующей EOL политике), meta GUID
`0bef9be59b054d3197a81f41aeacbb76` уникальна в Scripts. Scoped scan: нет нового velocity
writer, все bound drop вызывают TryDrop. Совместный csc запускает principal; Unity,
runtime probes, gameplay NUnit и коммиты этим исполнителем не выполнялись.

DeathDropEjection.ScheduleFor сейчас перечисляет только SDK hands/anchors. У valid claim
таких захватов/якоря нет, поэтому helper не добавляет туда второй импульс. Для явного
prediction-race guard principal может добавить HasActiveClaim skip в DeathDropEjection;
этот файл вне моей exclusive reservation и здесь не изменён.

Replacement отказ после частичного legacy cleanup не восстанавливает уже отпущенное
SDK/изъятое снаряжение; зато сохраняет тело и не публикует новое при оставшемся claim.
Не считать это транзакционным rollback. Drop/clear/skin/death/disconnect на клиентах,
latejoin и actual remote physics требуют нового joint runtime probe.
