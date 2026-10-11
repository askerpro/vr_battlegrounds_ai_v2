# Зона bots утверждена картой экспертов; bots-fix — одна из задач

Пользователь 2026-10-11 согласовал карту экспертов: bots владеет ИИ и поведением (Blaze AI), телами
и клиповыми моделями, выбором тела до спавна, экипировкой и стрельбой ботов, сетью ботов, содержимым
бот-стендов. Границы: правила матча/спавна/команд — gameplay; механика оружия — weapon-system; тело и
IK — avatar-ik; примитивы репликации — network; запуск бот-стенда — launch-infra.

Инвентаризация хаба (read-only, 2026-10-11): writes этапов bots-fix пересекают чужие зоны —
`GameNetworkManager.cs`, `StateEventAuthority.cs`, `PlayerSession.cs` (network);
`Assets/Scripts/Player/Avatars/**`, `AvatarTeardown.cs`, SDK `UxrArmIKSolver.cs` (avatar-ik);
новые `Weapons/WeaponGripBinding.cs`, `WeaponAutomationCapability.cs` (weapon-system); `Weapons/Equipment/**`
(binding наш по контракту, каталог — weapon-system); `PlayersManager.cs`, `PlayerLoadoutManager.cs`,
`EquipmentStrip.cs`, `Corpse/DeathDropEjection.cs` (gameplay); SDK `UxrGrabbableObject.cs`,
`UxrGrabManager.Manipulation.cs`, `IUxrGrabAvailability.cs` (manipulation); `Interaction/LooseItems.cs`
(владелец по карте не назван). Явного согласия владельцев этих зон в хабе нет.

Источники: `tasks/expert-workspace-infra/expert-map.md`; hub status (bots-fix stages[].spec.writes).
