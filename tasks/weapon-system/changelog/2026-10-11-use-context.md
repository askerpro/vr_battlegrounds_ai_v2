# Этап use-context принят: единый контекст руки и экипировки

Дата: 2026-10-11. Задача: `weapon-system`. Совместное вливание с bots-fix: foundation `93f67ded`.

## Что вошло

- `WeaponUseContext.Resolve` — единственный вывод пользователя оружия. VR-рука по SDK-хвату с
  `GrabLifetimeEpoch > 0`, NPC по активному `WeaponEquipmentBinding`; конфликт хвата и claim даёт None
  и одно сообщение об ошибке.
- Машина: `WeaponContext.HasAuthorContext` (рука или equipment) и `AutomationAllowed`. Жест контакта и
  предложение патрона по-прежнему требуют руки автора.
- SDK70 `IUxrFirearmRootRecoilPolicy`: запрет отдачи корня, пока ствол экипирован у NPC.
- SDK71 lifetime захвата (`UxrGrabber.GrabLifetimeEpoch`). Метки приведены к `VR Battlegrounds patch 71`
  по условию manipulation (3447); патч 67 и `UxrGrabManager.GrabCandidates.cs` не тронуты.
- SDK72 `IUxrWeaponOwnerProvider`: стрелок NPC фиксируется на сервере при выстреле.
- Canonical authoring: `WeaponEquipmentBinding` на корне PPK и AK105 через `WeaponSystemAuthoring`.
- Foundation bots-fix: `WeaponEquipmentBinding` и `IWeaponEquipmentSocketOwner` (отдельный коммит).

## Проверки

- Шлем пользователя 2026-10-11, аренда 425, `Offline.unity`: PPK, AK105, контрольные стволы, две руки,
  карман — OK. Импорт без ошибок, Mirror weaver обработал Binding, Missing Script 0.
- NPC-прогоны bots-fix: ticket413/415/419 — выстрелы SDK, учёт, ActorSource и damage credit у MEF и Cyborg.
- Сетевое ревью network 3597: ACK одним пакетом.
- Аренда 428 на пакете с тестами: AndroidCompileGate PASS; EditMode `VrBattlegrounds.Tests.Weapons`
  236/236; хват, сеть, боты, вибрация, аватары, игрок — 778 (777 PASS, 1 Ignored, 0 FAIL).
  Полный EditMode 2644: падения только вне пакета (карты/LD, стенд ботов в билде, позы хвата Cyborg).
- Новые тесты: `WeaponUseContextMachineTests`, `WeaponUseContextPrefabTests`.

## Осталось

- Условия network 3–5 для Binding — bots-fix, до clip-cutover.
- Backlog `recoil-hand-follow`: кисть должна сопровождать ствол при отдаче (замечание пользователя на PPK).
