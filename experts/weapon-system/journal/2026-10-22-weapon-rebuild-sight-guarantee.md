# weapon-rebuild-sight-guarantee: транзакционная пересборка арсенала, магазины без баз SDK, шарнир отдачи в запястье, 2026-10-22

Полная пересборка ствола из пака либо даёт эквивалентный префаб, либо отказывает целиком с причиной. Полупересобранного
префаба не остаётся. Пересобраны все 17 стволов. Решения пользователя — в `tasks/weapon-rebuild-sight-guarantee/Details.md`.

Для зоны:
- `WeaponRebuildFinish.Run` + `WeaponRebuildTransaction`: побайтная копия, откат с выгрузкой dirty-объектов из памяти,
  `ReleaseForeign` для чужих dirty. Сохранение только своей области (`OwnAssetSave`), общий `SaveAssets` убран.
- Режима «принудительно, пересвести позже» нет: сбой шага прицела — отказ и откат. После смены ShotSource ствол
  сначала пересводят на стенде.
- Магазины — самостоятельные префабы (не варианты), без геометрии и записей хвата сэмплов SDK. Базы
  MagGun/MagMachinegun/MagShotgun и `Assets/Unused` удалены.
- `ConfigureHost` хоста WeaponSystem — после клипов и motion. Иначе HoldOpen и ручное заряжание отказывали.
  Помпа FabarmSDASS выключена, окно приёма сохраняет `Reload_1`.
- `RecoilAccumulator`: шарнир отдачи в руках — запястье держащей руки (`UxrGrabber.HandBone`, `ConstraintsApplying`).
  Без руки — авторская точка.
- Тесты: `WeaponRebuildTransactionTests`, `PackMagazineStructureTests`, `RecoilPivotTests`.
- Известные красные: покрытие хвата Cyborg у магазинов. Регистрация поз для оружия и магазинов — отдельно, через
  экспортированные позы `_orig`.

Материалы: `tasks/weapon-rebuild-sight-guarantee/changelog/`.
